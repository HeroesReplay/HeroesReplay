using System;
using System.Collections.Generic;
using System.Globalization;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Metadata;

public sealed class FullMatchMetadataOptions
{
    public bool IncludeSpoilers { get; init; }
    public bool IncludeRequestAttribution { get; init; } = true;
    public string CategoryId { get; init; } = "20";
    public YouTubeTitleSettings Titles { get; init; }
}

public sealed record FullMatchMetadataInput
{
    public int? ReplayId { get; init; }
    public DateTime? GameDateUtc { get; init; }
    public string GameVersion { get; init; }
    public string Map { get; init; }
    public string MapAlternativeName { get; init; }
    public string HeroesProfileMap { get; init; }
    public string GameMode { get; init; }
    public string Rank { get; init; }
    public double? AverageMmr { get; init; }
    public string FocusHero { get; init; }
    public IReadOnlyList<ReplayMediaPlayer> Roster { get; init; }
    public IReadOnlyList<Hero> HeroCatalog { get; init; }
    public bool RecordAndUpload { get; init; }
    public string RequestedBy { get; init; }

    /// <summary>True only when the reward named a player slot. A focus hero alone does not change the title.</summary>
    public bool NamedPlayer { get; init; }
    public IReadOnlyList<TeamKillClip> NotableEvents { get; init; }

    /// <summary>Caller sets this only after completion and media validation. It is not inferred.</summary>
    public bool IsCompleteRecording { get; init; }
    public string Winner { get; init; }

    /// <summary>
    /// Heroes Profile hero statistics for the replay's patch and game type, or null. Only used
    /// when <c>YouTube:Titles:StatHooks:Enabled</c> is true.
    /// </summary>
    public HeroStatsSnapshot HeroStats { get; init; }
}

public sealed class FullMatchMetadata
{
    public string TemplateVersion { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
    public IReadOnlyList<string> DescriptionLines { get; init; }
    public IReadOnlyList<string> Tags { get; init; }
    public int? ReplayId { get; init; }
    public string HeroesProfileUrl { get; init; }
    public string CategoryId { get; init; }
    public string Map { get; init; }
    public string GameMode { get; init; }
    public string Rank { get; init; }
    public int? AverageMmr { get; init; }
    public string FocusHero { get; init; }
    public bool ClaimsFullMatch { get; init; }
    public bool ClaimsPentakill { get; init; }
    public bool ClaimsTeamWipe { get; init; }
    public bool IncludesSpoiler { get; init; }

    /// <summary>
    /// The Heroes Profile statistics hook picked for the title's draft slot, or null. Its
    /// <c>Stats:</c> line and the attribution stay in the description even when a long title drops it.
    /// </summary>
    public string StatHook { get; init; }
}

public static class FullMatchMetadataBuilder
{
    public const string TemplateVersion = "7";
    public const int TitleMaxCharacters = 100;
    public const int DescriptionMaxCharacters = 5000;
    public const int TagMaxCharacters = 30;
    public const int TagsMaxCharacters = 500;

    private const string HeroesProfileUrlPrefix =
        "https://www.heroesprofile.com/Match/Single/?replayID=";

    public static FullMatchMetadata Build(
        FullMatchMetadataInput input,
        FullMatchMetadataOptions options
    )
    {
        if (options == null)
        {
            options = new FullMatchMetadataOptions();
        }

        string category = Clean(options.CategoryId, 16) ?? "20";
        YouTubeTitleSettings titles = options.Titles ?? new YouTubeTitleSettings();
        if (input == null)
        {
            return Empty(category);
        }

        int? replayId = input.ReplayId is int id && id > 0 ? id : null;
        string replayText = replayId?.ToString(CultureInfo.InvariantCulture);
        string profileUrl = replayId == null ? null : HeroesProfileUrlPrefix + replayText;
        string map = EnglishMapNames.Prefer(
            input.HeroesProfileMap,
            input.Map,
            input.MapAlternativeName
        );
        map = Clean(map, 120);
        string mode = Clean(input.GameMode, 80);
        string rank = Clean(input.Rank, 40);
        string focus = Clean(input.FocusHero, 40);
        string build = Clean(input.GameVersion, 40);
        string requestor = options.IncludeRequestAttribution ? Clean(input.RequestedBy, 40) : null;
        if (!input.RecordAndUpload)
        {
            requestor = null;
        }

        TeamKillClip[] events = ReplayMediaEvidence.Accepted(input.NotableEvents);
        bool pentakill = ReplayMediaEvidence.HasKind(events, TeamKillClips.PentakillKind);
        bool teamWipe = ReplayMediaEvidence.HasKind(events, TeamKillClips.TeamWipeKind);
        int? mmr = RoundedMmr(input.AverageMmr);
        string describedWhen = DateLabel(input.GameDateUtc);
        string winner = options.IncludeSpoilers ? Clean(input.Winner, 80) : null;
        MatchDraft draft = MatchDraft.Read(input.HeroCatalog, input.Roster, titles);
        string featuredHero = HeroFeature.Select(
            titles,
            input.HeroCatalog,
            input.Roster,
            input.GameDateUtc
        );
        bool namedLead = input.NamedPlayer && titles.NamedPlayerTitles && focus != null;
        if (namedLead && featuredHero != null && HeroDraft.SameName(featuredHero, focus))
        {
            featuredHero = null;
        }

        string feature = FeatureSegment(titles, featuredHero);
        StatHook hook =
            draft.Title == null
                ? Hook(input, titles, map, mode, namedLead ? focus : null, featuredHero)
                : null;
        string hookTitle = Clean(hook?.Title, 80);
        string title = TitleFor(
            titles,
            input.NamedPlayer,
            map,
            mode,
            rank,
            focus,
            feature,
            draft.Title ?? hookTitle,
            replayText
        );

        var lines = new List<string>();
        if (hookTitle != null)
        {
            // Heroes Profile's terms: the attribution sits near the top, where YouTube shows it unfolded.
            lines.Add(StatHookPicker.Attribution);
        }

        if (input.IsCompleteRecording)
        {
            lines.Add("Full match.");
        }

        AddLine(lines, replayText == null ? null : "Replay ID: " + replayText);
        AddLine(lines, profileUrl == null ? null : "Heroes Profile: " + profileUrl);
        AddLine(lines, describedWhen == null ? null : "Date: " + describedWhen + " UTC");
        AddLine(lines, build == null ? null : "Build: " + build);
        AddLine(lines, map == null ? null : "Map: " + map);
        AddLine(lines, mode == null ? null : "Mode: " + mode);
        AddLine(lines, rank == null ? null : "Rank: " + rank);
        AddLine(lines, focus == null ? null : "Featured: " + focus);
        AddLine(lines, draft.Line == null ? null : "Draft: " + draft.Line);
        AddLine(lines, featuredHero == null ? null : "Featuring: " + featuredHero);
        AddLine(lines, hookTitle == null ? null : Clean(hook.StatsLine, 400));
        AddLine(lines, Highlights(events));
        AddLine(lines, requestor == null ? null : "Requested by: " + requestor);
        string resultLine = winner == null ? null : "Result: " + winner;
        AddLine(lines, resultLine);
        AddRoster(lines, input.Roster);

        IReadOnlyList<string> kept = FitLines(lines);
        string description = string.Join("\n", kept);
        string[] tags = Tags(
            map,
            mode,
            rank,
            focus,
            pentakill,
            teamWipe,
            replayId != null,
            draft.Labels
        );
        string combined = title + "\n" + description;
        return new FullMatchMetadata
        {
            TemplateVersion = TemplateVersion,
            Title = title,
            Description = description,
            DescriptionLines = kept,
            Tags = tags,
            ReplayId = replayId,
            HeroesProfileUrl = profileUrl,
            CategoryId = category,
            Map = map,
            GameMode = mode,
            Rank = rank,
            AverageMmr = mmr,
            FocusHero = focus,
            ClaimsFullMatch = input.IsCompleteRecording && Has(description, "Full match."),
            ClaimsPentakill = pentakill && Has(combined, "pentakill"),
            ClaimsTeamWipe = teamWipe && Has(combined, "team wipe"),
            IncludesSpoiler = resultLine != null && Has(description, resultLine),
            StatHook = hookTitle,
        };
    }

    /// <summary>
    /// The statistics hook for the draft slot when the match has no draft note, or null. The title
    /// never names a hero twice, so the named player and the featured hero are skipped.
    /// </summary>
    private static StatHook Hook(
        FullMatchMetadataInput input,
        YouTubeTitleSettings titles,
        string map,
        string mode,
        string namedFocus,
        string featuredHero
    )
    {
        if (titles.StatHooks?.Enabled != true || input.HeroStats == null)
        {
            return null;
        }

        var named = new List<string>();
        if (namedFocus != null)
        {
            named.Add(namedFocus);
        }

        if (featuredHero != null)
        {
            named.Add(featuredHero);
        }

        return StatHookPicker.Pick(
            input.HeroStats,
            input.HeroCatalog,
            input.Roster,
            map,
            named,
            titles.StatHooks,
            mode
        );
    }

    private static FullMatchMetadata Empty(string category)
    {
        return new FullMatchMetadata
        {
            TemplateVersion = TemplateVersion,
            Title = string.Empty,
            Description = string.Empty,
            DescriptionLines = Array.Empty<string>(),
            Tags = Array.Empty<string>(),
            CategoryId = category,
            ClaimsFullMatch = false,
            ClaimsPentakill = false,
            ClaimsTeamWipe = false,
            IncludesSpoiler = false,
        };
    }

    private static string FeatureSegment(YouTubeTitleSettings titles, string hero)
    {
        if (string.IsNullOrWhiteSpace(hero))
        {
            return null;
        }

        string prefix =
            titles == null || string.IsNullOrWhiteSpace(titles.FeaturePrefix)
                ? "Ft."
                : titles.FeaturePrefix.Trim();
        return prefix + " " + hero.Trim();
    }

    private static string TitleFor(
        YouTubeTitleSettings titles,
        bool namedPlayer,
        string map,
        string mode,
        string rank,
        string hero,
        string feature,
        string draft,
        string replayId
    )
    {
        // A title never names the Twitch viewer. A reward that picked a player leads with that hero.
        // The draft note sits just before the replay id, so it is the first part dropped at 100 characters.
        string lead =
            namedPlayer && titles.NamedPlayerTitles && hero != null ? hero + " focus" : null;
        return ComposeTitle(lead, feature, map, mode, rank, draft, replayId);
    }

    private static string ComposeTitle(params string[] parts)
    {
        var current = new List<string>();
        if (parts != null)
        {
            foreach (string part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    current.Add(part.Trim());
                }
            }
        }

        while (current.Count > 1)
        {
            string title = string.Join(" - ", current);
            if (title.Length <= TitleMaxCharacters)
            {
                return title;
            }

            if (current.Count > 2)
            {
                current.RemoveAt(current.Count - 2);
            }
            else
            {
                current.RemoveAt(0);
            }
        }

        if (current.Count == 0)
        {
            return string.Empty;
        }

        string only = current[0];
        return only.Length <= TitleMaxCharacters ? only : only.Substring(0, TitleMaxCharacters);
    }

    private static string Highlights(IReadOnlyList<TeamKillClip> events)
    {
        if (events == null || events.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        int limit = Math.Min(events.Count, 8);
        for (int i = 0; i < limit; i++)
        {
            TeamKillClip clip = events[i];
            string kind = clip.Kind == TeamKillClips.PentakillKind ? "pentakill" : "team wipe";
            string hero = Clean(clip.Hero, 40);
            parts.Add(hero == null ? kind : hero + " " + kind);
        }

        return "Highlights: " + string.Join("; ", parts);
    }

    private static void AddRoster(List<string> lines, IReadOnlyList<ReplayMediaPlayer> roster)
    {
        AddLine(lines, RosterLine("Blue", roster, 0));
        AddLine(lines, RosterLine("Red", roster, 1));
    }

    private static string RosterLine(
        string teamName,
        IReadOnlyList<ReplayMediaPlayer> roster,
        int team
    )
    {
        if (roster == null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (ReplayMediaPlayer player in roster)
        {
            if (player == null || player.Team != team)
            {
                continue;
            }

            string described = Describe(player);
            if (!string.IsNullOrWhiteSpace(described))
            {
                parts.Add(described);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return teamName + ": " + string.Join(", ", parts);
    }

    private static string Describe(ReplayMediaPlayer player)
    {
        string hero = Clean(player.Hero, 40);
        if (player.IsAi)
        {
            return hero == null ? "AI" : hero + " (AI)";
        }

        string account = Account(player);
        if (hero != null && account != null)
        {
            return hero + " (" + account + ")";
        }

        return hero ?? account;
    }

    /// <summary>
    /// The display name without the BattleTag number. YouTube turns <c>#1234</c> in a description into a hashtag.
    /// </summary>
    private static string Account(ReplayMediaPlayer player)
    {
        string name = Clean(player.Name, 40);
        int hash = name?.IndexOf('#') ?? -1;
        return hash < 0 ? name : Clean(name.Substring(0, hash), 40);
    }

    private static string[] Tags(
        string map,
        string mode,
        string rank,
        string focus,
        bool pentakill,
        bool teamWipe,
        bool replayKnown,
        IReadOnlyList<string> compositions
    )
    {
        var tags = new List<string>();
        AddTag(tags, map);
        AddTag(tags, mode);
        AddTag(tags, ReplayMediaRanks.Display(rank));
        AddTag(tags, focus);
        if (pentakill)
        {
            AddTag(tags, "Pentakill");
        }

        if (teamWipe)
        {
            AddTag(tags, "Team wipe");
        }

        if (tags.Count > 0 || replayKnown)
        {
            AddTag(tags, "Heroes of the Storm");
        }

        // One tag per composition label. They come last, so the 500-character budget drops them first.
        if (compositions != null)
        {
            foreach (string label in compositions)
            {
                AddTag(tags, label);
            }
        }

        while (tags.Count > 0 && string.Join(",", tags).Length > TagsMaxCharacters)
        {
            tags.RemoveAt(tags.Count - 1);
        }

        return tags.ToArray();
    }

    private static void AddTag(List<string> tags, string value)
    {
        string clean = Clean(value, TagMaxCharacters);
        if (clean == null)
        {
            return;
        }

        clean = clean.Replace(",", string.Empty).Trim();
        if (clean.Length == 0)
        {
            return;
        }

        if (clean.Length > TagMaxCharacters)
        {
            clean = clean.Substring(0, TagMaxCharacters).TrimEnd();
        }

        foreach (string tag in tags)
        {
            if (string.Equals(tag, clean, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        tags.Add(clean);
    }

    private static IReadOnlyList<string> FitLines(List<string> lines)
    {
        while (lines.Count > 1 && JoinLength(lines) > DescriptionMaxCharacters)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 1 && lines[0].Length > DescriptionMaxCharacters)
        {
            lines[0] = lines[0].Substring(0, DescriptionMaxCharacters);
        }

        return lines.ToArray();
    }

    private static int JoinLength(List<string> lines)
    {
        int length = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                length++;
            }

            length += lines[i].Length;
        }

        return length;
    }

    private static void AddLine(List<string> lines, string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            lines.Add(line);
        }
    }

    private static int? RoundedMmr(double? mmr)
    {
        if (mmr is not double value || double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return null;
        }

        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)rounded;
    }

    private static string DateLabel(DateTime? gameDate)
    {
        if (gameDate is not DateTime value || value.Kind == DateTimeKind.Local)
        {
            return null;
        }

        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static bool Has(string text, string phrase)
    {
        return text != null
            && phrase != null
            && text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Clean(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || max <= 0)
        {
            return null;
        }

        var chars = new char[value.Length];
        int count = 0;
        bool pendingSpace = false;
        foreach (char character in value.Trim())
        {
            if (character == '<' || character == '>')
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (count == 0 || pendingSpace)
                {
                    continue;
                }

                chars[count++] = ' ';
                pendingSpace = true;
                continue;
            }

            chars[count++] = character;
            pendingSpace = false;
            if (count >= max)
            {
                break;
            }
        }

        if (count == 0)
        {
            return null;
        }

        if (chars[count - 1] == ' ')
        {
            count--;
        }

        return count == 0 ? null : new string(chars, 0, count);
    }
}
