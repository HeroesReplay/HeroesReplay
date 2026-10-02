#:package Google.Apis.YouTube.v3

// One-off migration for issues #114 and #116. It rewrites metadata on videos that are already uploaded.
//
//   dotnet run tools/youtube-fix-descriptions.cs                    # dry run: print what would change
//   dotnet run tools/youtube-fix-descriptions.cs -- --apply         # update descriptions
//   dotnet run tools/youtube-fix-descriptions.cs -- --apply --titles --limit 40
//
// Descriptions: "Hero (Name#1234)" becomes "Hero (Name)" on the Blue/Red roster lines. Twitch link becomes https.
// Titles (only with --titles): "Hero requested by Viewer - ..." becomes "Hero focus - ...",
// and a leading "Requested by Viewer - " is removed.
//
// Auth is the playlist consent (full youtube scope, user "{ChannelId}:library") that the uploader's
// playlist filing already stores. Run it on the machine that files playlists. videos.list costs 1 unit
// per 50 videos. Each videos.update costs 50 units, out of the same 10,000-unit day the uploader uses,
// so --limit (default 40) keeps one run at 2,000 units. Run it again on a later day for the rest.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

bool apply = args.Contains("--apply");
bool titles = args.Contains("--titles");
int limit = IntArg("--limit", 40);
string dataDirectory = StringArg("--data", @"C:\heroesreplay\Data");
string channelId = StringArg("--channel", "UCpf5rn5UlJTUZF9n98HXS5A");

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
        channelId + ":library",
        CancellationToken.None
    );
}

using var youtube = new YouTubeService(
    new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "heroesreplay-description-migration",
    }
);

ChannelsResource.ListRequest channels = youtube.Channels.List("contentDetails");
channels.Mine = true;
ChannelListResponse mine = await channels.ExecuteAsync();
string uploads = mine.Items?.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;
if (uploads == null)
{
    Console.Error.WriteLine("The signed-in account has no uploads playlist.");
    return 1;
}

var ids = new List<string>();
string page = null;
do
{
    PlaylistItemsResource.ListRequest items = youtube.PlaylistItems.List("contentDetails");
    items.PlaylistId = uploads;
    items.MaxResults = 50;
    items.PageToken = page;
    PlaylistItemListResponse response = await items.ExecuteAsync();
    ids.AddRange(response.Items.Select(item => item.ContentDetails.VideoId));
    page = response.NextPageToken;
} while (page != null);

Console.WriteLine($"{ids.Count} uploaded videos. Mode: {(apply ? "apply" : "dry run")}.");

int changed = 0;
int updated = 0;
foreach (string[] batch in ids.Chunk(50))
{
    VideosResource.ListRequest videos = youtube.Videos.List("snippet");
    videos.Id = string.Join(",", batch);
    VideoListResponse found = await videos.ExecuteAsync();
    foreach (Video video in found.Items)
    {
        VideoSnippet snippet = video.Snippet;
        if (!IsReplayVideo(snippet.Description))
        {
            continue;
        }

        string description = FixDescription(snippet.Description);
        string title = titles ? FixTitle(snippet.Title) : snippet.Title;
        if (description == snippet.Description && title == snippet.Title)
        {
            continue;
        }

        changed++;
        Console.WriteLine();
        Console.WriteLine($"{video.Id}  {snippet.Title}");
        if (title != snippet.Title)
        {
            Console.WriteLine($"  title -> {title}");
        }

        foreach (string line in ChangedLines(snippet.Description, description))
        {
            Console.WriteLine($"  {line}");
        }

        if (!apply || updated >= limit)
        {
            continue;
        }

        // A snippet update replaces the whole snippet. Title, category and tags are sent back as read.
        var update = new Video
        {
            Id = video.Id,
            Snippet = new VideoSnippet
            {
                Title = title,
                Description = description,
                CategoryId = snippet.CategoryId,
                Tags = snippet.Tags,
                DefaultLanguage = snippet.DefaultLanguage,
                DefaultAudioLanguage = snippet.DefaultAudioLanguage,
            },
        };
        await youtube.Videos.Update(update, "snippet").ExecuteAsync();
        updated++;
        Console.WriteLine("  updated");
    }
}

Console.WriteLine();
Console.WriteLine($"{changed} videos need a change. {updated} updated this run.");
if (apply && changed > updated)
{
    Console.WriteLine($"Limit {limit} reached. Run again on another quota day for the rest.");
}

return 0;

static bool IsReplayVideo(string description) =>
    description != null
    && (
        description.Contains("Replay ID:", StringComparison.Ordinal)
        || description.Contains("heroesprofile.com/Match", StringComparison.Ordinal)
    );

static string FixDescription(string description)
{
    string[] lines = description.Replace("\r\n", "\n").Split('\n');
    for (int i = 0; i < lines.Length; i++)
    {
        if (lines[i].StartsWith("Blue: ", StringComparison.Ordinal) || lines[i].StartsWith("Red: ", StringComparison.Ordinal))
        {
            // Name#1234 inside "Hero (Name#1234)" or a bare "Name#1234" before a comma or the end.
            lines[i] = Regex.Replace(lines[i], @"#\d+(?=\)|,|$)", string.Empty);
        }
        else if (lines[i] == "Twitch: http://twitch.tv/saltysadism")
        {
            lines[i] = "Twitch: https://twitch.tv/saltysadism";
        }
    }

    return string.Join("\n", lines);
}

static string FixTitle(string title)
{
    string fixedTitle = Regex.Replace(title, @"^(?<hero>.+?) requested by [^-]+? - ", "${hero} focus - ");
    return Regex.Replace(fixedTitle, @"^Requested by [^-]+? - ", string.Empty);
}

static IEnumerable<string> ChangedLines(string before, string after)
{
    string[] old = before.Replace("\r\n", "\n").Split('\n');
    string[] now = after.Split('\n');
    for (int i = 0; i < Math.Min(old.Length, now.Length); i++)
    {
        if (old[i] != now[i])
        {
            yield return "- " + old[i];
            yield return "+ " + now[i];
        }
    }
}

int IntArg(string name, int fallback)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int value) ? value : fallback;
}

string StringArg(string name, string fallback)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
}
