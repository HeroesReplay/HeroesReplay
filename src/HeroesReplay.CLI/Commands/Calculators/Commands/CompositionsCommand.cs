using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Calculators.Commands;

/// <summary>
/// The corpus report behind <c>YouTube:Titles:Compositions</c> (issue #140): how often each
/// composition label and each unusual-draft note appears, per team and per game.
/// </summary>
public class CompositionsCommand : Command
{
    public CompositionsCommand()
        : base(
            "compositions",
            "Count team-composition labels (split push, siege, dive, poke, and the rest) and unusual-draft notes across a folder of .StormReplay files, one file at a time. Prints each replay's notes and title slot, then how often each label appears per team and per game, so the YouTube:Titles:Compositions thresholds and Frequencies can be tuned. Reads the heroes-data2 catalog under Location:DataDirectory. Read-only: it does not launch the game or write next to the replays."
        )
    {
        var directoryOption = new Option<string>("--directory")
        {
            Description =
                "Directory of .StormReplay files. Only the top level is scanned, one file at a time.",
            Required = true,
        };
        directoryOption.Aliases.Add("-d");

        var outputOption = new Option<string>("--output")
        {
            Description =
                "Optional report file: .md writes the frequency tables and one row per replay, .csv writes the frequency table.",
        };
        outputOption.Aliases.Add("-o");

        Options.Add(directoryOption);
        Options.Add(outputOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(directoryOption),
                    parseResult.GetValue(outputOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken
                )
        );
    }

    internal static async Task<int> RunAsync(
        string directory,
        string output,
        TextWriter console,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        console ??= Console.Out;
        error ??= Console.Error;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            error.WriteLine($"Replay directory was not found: {directory}");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(output) && Format(output) == null)
        {
            error.WriteLine("--output must end in .md or .csv.");
            return 1;
        }

        List<string> files = Replays(directory);
        if (files.Count == 0)
        {
            error.WriteLine($"No .StormReplay files were found in {directory}");
            return 1;
        }

        AppSettings settings = ServiceCollectionExtensions.LoadOfflineSettings();
        var data = new GameData(NullLogger<GameData>.Instance, settings);
        await data.LoadDataAsync().ConfigureAwait(false);
        YouTubeTitleSettings titles = settings.YouTube?.Titles ?? new YouTubeTitleSettings();
        return Run(files, data.Heroes, titles, output, console, error, cancellationToken);
    }

    /// <summary>
    /// Reads every replay in <paramref name="files"/> against <paramref name="catalog"/>, prints
    /// the per-replay lines and the frequency tables, and writes <paramref name="output"/>.
    /// Exit 0 when at least one game had both teams matched.
    /// </summary>
    public static int Run(
        IReadOnlyList<string> files,
        IReadOnlyList<Hero> catalog,
        YouTubeTitleSettings titles,
        string output,
        TextWriter console,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        titles ??= new YouTubeTitleSettings();
        var tally = new CompositionTally(titles);
        console.WriteLine(
            $"Reading {files.Count} replays against {catalog?.Count ?? 0} catalog heroes, one file at a time."
        );
        for (int index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReplayRow row = Read(files[index], catalog, titles);
            tally.Add(row);
            console.WriteLine($"[{index + 1}/{files.Count}] {row.Describe()}");
        }

        console.WriteLine();
        foreach (string line in tally.Summary())
        {
            console.WriteLine(line);
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(
                output,
                Format(output) == ".csv" ? tally.Csv() : tally.Markdown(),
                new UTF8Encoding(false)
            );
            console.WriteLine($"Wrote {output}");
        }

        if (tally.Games == 0)
        {
            error.WriteLine("No replay had two full teams the catalog could match.");
            return 2;
        }

        return 0;
    }

    private static string Format(string output)
    {
        string extension = Path.GetExtension(output)?.ToLowerInvariant();
        return extension == ".md" || extension == ".csv" ? extension : null;
    }

    private static List<string> Replays(string directory) =>
        new DirectoryInfo(directory)
            .EnumerateFiles("*.StormReplay", SearchOption.TopDirectoryOnly)
            .Select(file => file.FullName)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ReplayRow Read(
        string path,
        IReadOnlyList<Hero> catalog,
        YouTubeTitleSettings titles
    )
    {
        var row = new ReplayRow { Id = ReplayId(path) };
        try
        {
            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                path,
                false,
                SummaryOptions()
            );
            if (replay?.Players == null || replay.Players.Length == 0)
            {
                row.Error = result.ToString();
                return row;
            }

            row.Map = EnglishMapNames.Prefer(null, replay.Map, replay.MapAlternativeName);
            row.Roster = Roster(replay, catalog);
            row.Matched = Matched(row.Roster, catalog, 0) && Matched(row.Roster, catalog, 1);
            for (int team = 0; team < 2; team++)
            {
                DraftNote role = HeroDraft.TeamNote(catalog, row.Roster, titles, team);
                row.Roles[team] = role;
                row.Labels[team] = TeamComposition.Labels(catalog, row.Roster, team, titles);
            }

            MatchDraft draft = MatchDraft.Read(catalog, row.Roster, titles);
            row.Draft = draft.Line;
            row.Title = draft.Title;
            return row;
        }
        catch (Exception exception)
        {
            row.Error = exception.GetType().Name + ": " + exception.Message;
            return row;
        }
    }

    /// <summary>
    /// Each player as the catalog's English name of the hero played (<see cref="PlayedHero"/>).
    /// The replay's character name is localized, and the lobby hero is not the hero played in ARAM.
    /// </summary>
    private static List<ReplayMediaPlayer> Roster(Replay replay, IReadOnlyList<Hero> catalog)
    {
        var roster = new List<ReplayMediaPlayer>();
        foreach (Player player in replay.Players)
        {
            if (player == null)
            {
                continue;
            }

            roster.Add(
                new ReplayMediaPlayer
                {
                    Team = player.Team,
                    Hero = PlayedHero.Name(catalog, replay, player),
                    IsAi = player.PlayerType == PlayerType.Computer,
                    Talents = ReplayMediaFacts.Talents(player),
                }
            );
        }

        return roster;
    }

    private static bool Matched(
        List<ReplayMediaPlayer> roster,
        IReadOnlyList<Hero> catalog,
        int team
    )
    {
        int count = 0;
        foreach (ReplayMediaPlayer player in roster)
        {
            if (player.Team != team)
            {
                continue;
            }

            if (HeroDraft.Find(catalog, player.Hero) == null)
            {
                return false;
            }

            count++;
        }

        return count >= TeamComposition.TeamSize;
    }

    private static string ReplayId(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        int digits = 0;
        while (digits < name.Length && char.IsDigit(name[digits]))
        {
            digits++;
        }

        return digits > 0 ? name.Substring(0, digits) : name;
    }

    /// <summary>
    /// Game events and statistics are parsed only for the talent names, so a tank talent such as
    /// Varian's Taunt counts the same way the title does. That is about 0.5 s a replay.
    /// </summary>
    private static ParseOptions SummaryOptions() =>
        new()
        {
            IgnoreErrors = true,
            AllowPTR = true,
            ShouldParseEvents = true,
            ShouldParseUnits = false,
            ShouldParseMouseEvents = false,
            ShouldParseDetailedBattleLobby = false,
            ShouldParseMessageEvents = false,
            ShouldParseStatistics = true,
        };

    private sealed class ReplayRow
    {
        public string Id { get; init; }
        public string Map { get; set; }
        public List<ReplayMediaPlayer> Roster { get; set; } = new();
        public bool Matched { get; set; }
        public DraftNote[] Roles { get; } = new DraftNote[2];
        public IReadOnlyList<DraftNote>[] Labels { get; } =
            new IReadOnlyList<DraftNote>[] { Array.Empty<DraftNote>(), Array.Empty<DraftNote>() };
        public string Draft { get; set; }
        public string Title { get; set; }
        public string Error { get; set; }

        public string Heroes(int team) =>
            string.Join(
                ", ",
                Roster.Where(player => player.Team == team).Select(player => player.Hero)
            );

        public string Notes(int team)
        {
            var notes = new List<string>();
            if (Roles[team] != null)
            {
                notes.Add(Roles[team].Text);
            }

            notes.AddRange(Labels[team].Select(label => label.Text));
            return notes.Count == 0 ? "-" : string.Join("; ", notes);
        }

        public string Describe()
        {
            if (Error != null)
            {
                return $"{Id} failed: {Error}";
            }

            if (!Matched)
            {
                return $"{Id} {Map}: a team has a hero the catalog does not know ({Heroes(0)} | {Heroes(1)})";
            }

            return $"{Id} {Map} | Blue [{Notes(0)}] | Red [{Notes(1)}] | title: {Title ?? "-"}";
        }
    }

    private sealed class CompositionTally
    {
        private readonly YouTubeTitleSettings titles;
        private readonly Dictionary<string, NoteCount> labels = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NoteCount> drafts = new(StringComparer.Ordinal);
        private readonly List<ReplayRow> rows = new();
        private int failed;
        private int unmatched;
        private int compositionTitles;
        private int roleTitles;

        public CompositionTally(YouTubeTitleSettings titles)
        {
            this.titles = titles;
            TeamCompositionSettings settings = Settings;
            foreach (string key in TeamComposition.Keys)
            {
                labels[key] = new NoteCount(key, Fallback(settings, key));
            }

            foreach ((string key, string text) in DraftKeys)
            {
                drafts[key] = new NoteCount(key, text);
            }
        }

        public int Games { get; private set; }

        private TeamCompositionSettings Settings =>
            titles.Compositions ?? new TeamCompositionSettings();

        public void Add(ReplayRow row)
        {
            rows.Add(row);
            if (row.Error != null)
            {
                failed++;
                return;
            }

            if (!row.Matched)
            {
                unmatched++;
                return;
            }

            Games++;
            var labelGame = new HashSet<string>(StringComparer.Ordinal);
            var draftGame = new HashSet<string>(StringComparer.Ordinal);
            for (int team = 0; team < 2; team++)
            {
                foreach (DraftNote label in row.Labels[team])
                {
                    labels[label.Key].AddTeam(label.Text);
                    labelGame.Add(label.Key);
                }

                DraftNote role = row.Roles[team];
                if (role != null)
                {
                    if (!drafts.ContainsKey(role.Key))
                    {
                        drafts[role.Key] = new NoteCount(role.Key, role.Key);
                    }

                    drafts[role.Key].AddTeam(role.Text);
                    draftGame.Add(role.Key);
                }
            }

            foreach (string key in labelGame)
            {
                labels[key].Games++;
            }

            foreach (string key in draftGame)
            {
                drafts[key].Games++;
            }

            if (row.Title == null)
            {
                return;
            }

            bool composition = row.Labels.Any(team =>
                team.Any(label =>
                    row.Title.Contains(label.Text, StringComparison.OrdinalIgnoreCase)
                )
            );
            if (composition && !RoleTitle(row))
            {
                compositionTitles++;
            }
            else
            {
                roleTitles++;
            }
        }

        /// <summary>The words for a label no replay had: the configured label, counted for melee assassins and specialists.</summary>
        private static string Fallback(TeamCompositionSettings settings, string key)
        {
            CompositionRule rule = Rule(settings, key);
            string label = rule?.Label?.Trim();
            if (string.IsNullOrEmpty(label))
            {
                return key;
            }

            if (key != TeamComposition.MeleeAssassins && key != TeamComposition.Specialists)
            {
                return label;
            }

            string count = rule.MinHeroes switch
            {
                2 => "Double",
                3 => "Triple",
                _ => rule.MinHeroes.ToString(CultureInfo.InvariantCulture),
            };
            return count + " " + label.ToLowerInvariant();
        }

        private static CompositionRule Rule(TeamCompositionSettings settings, string key) =>
            key switch
            {
                TeamComposition.SplitPush => settings.SplitPush,
                TeamComposition.Siege => settings.Siege,
                TeamComposition.Dive => settings.Dive,
                TeamComposition.Poke => settings.Poke,
                TeamComposition.MeleeAssassins => settings.MeleeAssassins,
                TeamComposition.AllMelee => settings.AllMelee,
                TeamComposition.OneRanged => settings.OneRanged,
                TeamComposition.Sustain => settings.Sustain,
                TeamComposition.Specialists => settings.Specialists,
                TeamComposition.MercControl => settings.MercControl,
                TeamComposition.GlassCannon => settings.GlassCannon,
                _ => null,
            };

        private static bool RoleTitle(ReplayRow row) =>
            row.Roles.Any(role =>
                role != null && row.Title.Contains(role.Text, StringComparison.OrdinalIgnoreCase)
            );

        public IEnumerable<string> Summary()
        {
            yield return $"Replays: {rows.Count}. Games with both teams matched: {Games} ({Games * 2} teams). Failed to parse: {failed}. A team the catalog could not match: {unmatched}.";
            yield return $"Max frequency to name a label: {Percent(Settings.MaxFrequency)} of games. Titles read YouTube:Titles:Compositions:Frequencies, not this run; copy the game shares there after a retune.";
            yield return string.Empty;
            yield return "Composition labels (raw, before the max-frequency filter):";
            yield return Row("Key", "Label", "Teams", "Team %", "Games", "Game %", "< max");
            foreach (NoteCount count in labels.Values)
            {
                yield return Row(
                    count.Key,
                    count.Text,
                    count.Teams.ToString(CultureInfo.InvariantCulture),
                    Percent(Share(count.Teams, Games * 2)),
                    count.Games.ToString(CultureInfo.InvariantCulture),
                    Percent(Share(count.Games, Games)),
                    Share(count.Games, Games) < Settings.MaxFrequency ? "yes" : "no"
                );
            }

            yield return string.Empty;
            yield return "Unusual-draft notes (role counts, YouTube:Titles):";
            yield return Row("Key", "Note", "Teams", "Team %", "Games", "Game %", string.Empty);
            foreach (NoteCount count in drafts.Values)
            {
                yield return Row(
                    count.Key,
                    count.Text,
                    count.Teams.ToString(CultureInfo.InvariantCulture),
                    Percent(Share(count.Teams, Games * 2)),
                    count.Games.ToString(CultureInfo.InvariantCulture),
                    Percent(Share(count.Games, Games)),
                    string.Empty
                );
            }

            yield return string.Empty;
            yield return $"Title slot: composition label {compositionTitles} ({Percent(Share(compositionTitles, Games))}), draft note {roleTitles} ({Percent(Share(roleTitles, Games))}), none {Games - compositionTitles - roleTitles}.";
        }

        public string Csv()
        {
            var text = new StringBuilder();
            text.AppendLine("kind,key,label,teams,team_share,games,game_share,under_max");
            foreach (NoteCount count in labels.Values)
            {
                text.AppendLine(
                    CsvRow("composition", count, Share(count.Games, Games) < Settings.MaxFrequency)
                );
            }

            foreach (NoteCount count in drafts.Values)
            {
                text.AppendLine(
                    CsvRow("draft", count, Share(count.Games, Games) < Settings.MaxFrequency)
                );
            }

            return text.ToString();
        }

        public string Markdown()
        {
            var text = new StringBuilder();
            text.AppendLine("# Team compositions");
            text.AppendLine();
            text.AppendLine(
                $"{Games} games with both teams matched ({rows.Count} replays, {failed} failed, {unmatched} with an unknown hero). A label is named when its game share is below {Percent(Settings.MaxFrequency)}."
            );
            text.AppendLine();
            text.AppendLine("| Label | Key | Teams | Team % | Games | Game % | Under max |");
            text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | --- |");
            foreach (NoteCount count in labels.Values)
            {
                text.AppendLine(
                    $"| {count.Text} | {count.Key} | {count.Teams} | {Percent(Share(count.Teams, Games * 2))} | {count.Games} | {Percent(Share(count.Games, Games))} | {(Share(count.Games, Games) < Settings.MaxFrequency ? "yes" : "no")} |"
                );
            }

            text.AppendLine();
            text.AppendLine("| Draft note | Key | Teams | Team % | Games | Game % |");
            text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
            foreach (NoteCount count in drafts.Values)
            {
                text.AppendLine(
                    $"| {count.Text} | {count.Key} | {count.Teams} | {Percent(Share(count.Teams, Games * 2))} | {count.Games} | {Percent(Share(count.Games, Games))} |"
                );
            }

            text.AppendLine();
            text.AppendLine(
                $"Title slot: composition label {compositionTitles}, draft note {roleTitles}, none {Games - compositionTitles - roleTitles}."
            );
            text.AppendLine();
            text.AppendLine(
                "| Replay | Map | Blue | Red | Blue notes | Red notes | Draft | Title |"
            );
            text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (ReplayRow row in rows)
            {
                if (row.Error != null)
                {
                    text.AppendLine($"| {row.Id} | | | | | | failed: {Cell(row.Error)} | |");
                    continue;
                }

                text.AppendLine(
                    $"| {row.Id} | {Cell(row.Map)} | {Cell(row.Heroes(0))} | {Cell(row.Heroes(1))} | {Cell(row.Notes(0))} | {Cell(row.Notes(1))} | {Cell(row.Draft)} | {Cell(row.Title)} |"
                );
            }

            return text.ToString();
        }

        private string CsvRow(string kind, NoteCount count, bool named) =>
            string.Join(
                ",",
                kind,
                count.Key,
                CsvCell(count.Text),
                count.Teams.ToString(CultureInfo.InvariantCulture),
                Share(count.Teams, Games * 2).ToString("0.0000", CultureInfo.InvariantCulture),
                count.Games.ToString(CultureInfo.InvariantCulture),
                Share(count.Games, Games).ToString("0.0000", CultureInfo.InvariantCulture),
                named ? "yes" : "no"
            );

        private static string Row(params string[] cells) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-26} {1,-24} {2,6} {3,8} {4,6} {5,8} {6,6}",
                cells
            );

        private static double Share(int count, int total) => total <= 0 ? 0 : (double)count / total;

        private static string Percent(double share) =>
            (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        private static string Cell(string value) =>
            string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "/");

        private static string CsvCell(string value) =>
            value != null && value.Contains(',') ? "\"" + value + "\"" : value ?? string.Empty;

        private static readonly (string Key, string Text)[] DraftKeys =
        {
            (nameof(YouTubeTitleSettings.NoTankOrHealer), "No tank or healer"),
            (nameof(YouTubeTitleSettings.NoHealer), "No healer"),
            (nameof(YouTubeTitleSettings.TripleHealer), "Triple healer"),
            (nameof(YouTubeTitleSettings.DoubleHealer), "Double healer"),
            (nameof(YouTubeTitleSettings.DoubleBruiserWithoutTank), "Double bruiser"),
            (nameof(YouTubeTitleSettings.NoTank), "No tank"),
            (nameof(YouTubeTitleSettings.DoubleTank), "Double tank"),
            (nameof(YouTubeTitleSettings.TripleBruiser), "Triple bruiser"),
            (nameof(YouTubeTitleSettings.DoubleSupport), "Double support"),
            (nameof(YouTubeTitleSettings.NoRangedAssassin), "No ranged assassin"),
        };
    }

    private sealed class NoteCount
    {
        public NoteCount(string key, string fallback)
        {
            Key = key;
            this.fallback = fallback;
        }

        private readonly string fallback;

        public string Key { get; }

        /// <summary>The words seen, in the order first seen: <c>Double melee assassin / Triple melee assassin</c>.</summary>
        public string Text => texts.Count == 0 ? fallback ?? "-" : string.Join(" / ", texts);

        public int Teams { get; private set; }
        public int Games { get; set; }

        private readonly List<string> texts = new();

        public void AddTeam(string text)
        {
            Teams++;
            if (!string.IsNullOrWhiteSpace(text) && !texts.Contains(text))
            {
                texts.Add(text);
            }
        }
    }
}
