#:package Google.Apis.YouTube.v3

// One-off migration for the playlist cleanup. It changes playlists that are already on the channel
// to the groups the library pass files now.
//
//   dotnet run tools/youtube-retire-playlists.cs                              # dry run: print the plan
//   dotnet run tools/youtube-retire-playlists.cs -- --apply                   # rename and delete
//   dotnet run tools/youtube-retire-playlists.cs -- --season "Season 2026"    # also retire that title
//
// Renamed: "Hero focus" becomes "Hero gameplay", so its videos stay on it. When the channel already has
// a "Hero gameplay" playlist, "Hero focus" is deleted instead.
// Deleted: the patch playlists ("Patch 2.57", "Patch 2.55 archive", "Unknown patch", and the --season
// title, the old YouTube:SeasonName), every "Unusual drafts - <note>" and every "Hero focus - <role>".
// Deleting a playlist removes only the playlist. Its videos stay on the channel.
//
// The library pass refills "Unusual drafts" and "Hero gameplay" itself, newest video first, on its own
// quota. This script also rewrites Data\youtube-playlists.json so the pass forgets the deleted playlists
// and knows "Hero gameplay" already holds what "Hero focus" held (no duplicate inserts). The file is
// saved next to itself as youtube-playlists.json.bak first.
//
// Stop the uploader first (heroesreplay services stop), so no library pass writes the cache meanwhile.
// Auth is the library consent (full youtube scope, user "{ChannelId}-library") that the library pass
// already stores. Run it on the machine that files playlists. playlists.list costs 1 unit per 50
// playlists, and each playlists.update or playlists.delete costs 50 units of the same 10,000-unit day
// the uploader uses. These units are not written to Data\youtube-quota-units.json.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

const string HeroFocus = "Hero focus";
const string HeroGameplay = "Hero gameplay";

bool apply = args.Contains("--apply");
string dataDirectory = StringArg("--data", @"C:\heroesreplay\Data");
string channelId = StringArg("--channel", "UCpf5rn5UlJTUZF9n98HXS5A");
string season = StringArg("--season", null);
var spacing = TimeSpan.FromSeconds(5);

string secretsPath = Path.Combine(dataDirectory, "client_secrets.json");
if (!File.Exists(secretsPath))
{
    Console.Error.WriteLine($"Missing {secretsPath}. Run tools/fill-secrets-from-op.ps1.");
    return 1;
}

UserCredential credential;
await using (FileStream stream = File.OpenRead(secretsPath))
{
    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
        GoogleClientSecrets.FromStream(stream).Secrets,
        new[] { YouTubeService.Scope.Youtube },
        channelId + "-library",
        CancellationToken.None
    );
}

using var youtube = new YouTubeService(
    new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "heroesreplay-playlist-migration",
    }
);

var playlists = new List<Playlist>();
string page = null;
do
{
    PlaylistsResource.ListRequest list = youtube.Playlists.List("snippet,contentDetails");
    list.Mine = true;
    list.MaxResults = 50;
    list.PageToken = page;
    PlaylistListResponse response = await list.ExecuteAsync();
    playlists.AddRange(response.Items ?? []);
    page = response.NextPageToken;
} while (page != null);

Playlist rename = playlists.FirstOrDefault(playlist => Same(playlist.Snippet.Title, HeroFocus));
bool gameplayExists = playlists.Any(playlist => Same(playlist.Snippet.Title, HeroGameplay));
if (gameplayExists)
{
    rename = null;
}

// With "Hero gameplay" already on the channel, "Hero focus" is not renamed but deleted.
List<Playlist> retire = playlists
    .Where(playlist =>
        Retired(playlist.Snippet.Title, season)
        || (gameplayExists && Same(playlist.Snippet.Title, HeroFocus))
    )
    .ToList();

Console.WriteLine($"{playlists.Count} playlists on the channel. Mode: {(apply ? "apply" : "dry run")}.");
Console.WriteLine();
if (rename != null)
{
    Console.WriteLine($"Rename  {rename.Id}  {rename.Snippet.Title} -> {HeroGameplay} ({Items(rename)} videos)");
}

foreach (Playlist playlist in retire)
{
    Console.WriteLine($"Delete  {playlist.Id}  {playlist.Snippet.Title} ({Items(playlist)} videos)");
}

int writes = retire.Count + (rename == null ? 0 : 1);
Console.WriteLine();
Console.WriteLine($"{writes} playlist write(s), {writes * 50} units.");
if (writes == 0)
{
    Console.WriteLine("Nothing to change on the channel.");
}

if (!apply)
{
    Console.WriteLine("Dry run: nothing was changed. Run again with --apply.");
    return 0;
}

var deleted = new HashSet<string>(StringComparer.Ordinal);
string renamedId = null;
bool first = true;
try
{
    if (rename != null)
    {
        // A snippet update replaces the whole snippet, so the description and language go back as read.
        await Space();
        await youtube
            .Playlists.Update(
                new Playlist
                {
                    Id = rename.Id,
                    Snippet = new PlaylistSnippet
                    {
                        Title = HeroGameplay,
                        Description = rename.Snippet.Description,
                        DefaultLanguage = rename.Snippet.DefaultLanguage,
                    },
                },
                "snippet"
            )
            .ExecuteAsync();
        renamedId = rename.Id;
        Console.WriteLine($"Renamed {rename.Snippet.Title} to {HeroGameplay}.");
    }

    foreach (Playlist playlist in retire)
    {
        await Space();
        await youtube.Playlists.Delete(playlist.Id).ExecuteAsync();
        deleted.Add(playlist.Id);
        Console.WriteLine($"Deleted {playlist.Snippet.Title}.");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Stopped: {exception.Message}");
    Console.Error.WriteLine("Run the script again (on the next quota day for a quota error) for the rest.");
}
finally
{
    UpdateCache(Path.Combine(dataDirectory, "youtube-playlists.json"), deleted, renamedId);
}

return deleted.Count + (renamedId == null ? 0 : 1) == writes ? 0 : 1;

async Task Space()
{
    // The library pass waits YouTube:LibraryWriteSpacing between writes, because YouTube throttles
    // playlist writes sent back to back.
    if (!first)
    {
        await Task.Delay(spacing);
    }

    first = false;
}

// The library pass keys the cache by title: PlaylistIds is title -> id, and each FiledVideoIds entry is
// "<title>\n<videoId>". A deleted or retired title loses both. The renamed playlist moves to the new
// title with every video it holds, so the pass does not insert those videos a second time.
void UpdateCache(string path, HashSet<string> deletedIds, string renamed)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"No {path}. The library pass looks the playlists up itself.");
        return;
    }

    JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject();
    JsonObject ids = root["PlaylistIds"] as JsonObject ?? new JsonObject();
    JsonArray filed = root["FiledVideoIds"] as JsonArray ?? new JsonArray();

    var dropped = new HashSet<string>(StringComparer.Ordinal);
    var moved = new HashSet<string>(StringComparer.Ordinal);
    foreach ((string title, JsonNode value) in ids.ToList())
    {
        string id = value?.GetValue<string>();
        if (renamed != null && id == renamed)
        {
            moved.Add(title);
        }
        else if ((id != null && deletedIds.Contains(id)) || Retired(title, season))
        {
            dropped.Add(title);
        }
        else if (Same(title, HeroFocus) && gameplayExists)
        {
            // "Hero focus" was deleted, not renamed; the pass files "Hero gameplay" itself.
            dropped.Add(title);
        }
    }

    foreach (string title in dropped.Concat(moved))
    {
        ids.Remove(title);
    }

    if (renamed != null)
    {
        ids[HeroGameplay] = renamed;
    }

    var kept = new JsonArray();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    int forgotten = 0;
    foreach (string key in filed.Select(node => node?.GetValue<string>()).Where(key => key != null))
    {
        int split = key.IndexOf('\n');
        string title = split < 0 ? key : key.Substring(0, split);
        string videoId = split < 0 ? string.Empty : key.Substring(split + 1);
        string next = key;
        if (moved.Contains(title))
        {
            next = HeroGameplay + "\n" + videoId;
        }
        else if (dropped.Contains(title) || Retired(title, season))
        {
            forgotten++;
            continue;
        }

        if (seen.Add(next))
        {
            kept.Add((JsonNode)next);
        }
    }

    root["PlaylistIds"] = ids;
    root["FiledVideoIds"] = kept;
    File.Copy(path, path + ".bak", overwrite: true);
    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(
        $"Updated {path}: {dropped.Count} playlist(s) and {forgotten} filed video(s) forgotten, {moved.Count} playlist(s) renamed. Backup: {path}.bak"
    );
}

// The groups the library pass no longer files.
static bool Retired(string title, string seasonTitle)
{
    string normal = Normal(title);
    return Regex.IsMatch(normal, @"^patch \d+\.\d+( archive)?$")
        || normal == "unknown patch"
        || (!string.IsNullOrWhiteSpace(seasonTitle) && normal == Normal(seasonTitle))
        || normal.StartsWith("unusual drafts - ", StringComparison.Ordinal)
        || normal.StartsWith("hero focus - ", StringComparison.Ordinal);
}

// YouTube Studio may keep a title with other case or spacing, as the library pass allows.
static bool Same(string left, string right) => Normal(left) == Normal(right);

static string Normal(string title) =>
    string.Join(' ', (title ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        .ToLowerInvariant();

static long Items(Playlist playlist) => playlist.ContentDetails?.ItemCount ?? 0;

string StringArg(string name, string fallback)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}
