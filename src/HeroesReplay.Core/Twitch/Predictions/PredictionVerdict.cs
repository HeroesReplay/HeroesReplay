using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.Twitch.Predictions;

/// <summary>How a resolved prediction went for the viewers who took part.</summary>
public enum PredictionVerdictKind
{
    /// <summary>Nobody predicted.</summary>
    Silence,

    /// <summary>Every predictor backed the winner.</summary>
    Unanimous,

    /// <summary>Nobody backed the winner.</summary>
    Wipeout,

    /// <summary>The losing side held more points (more viewers when Helix sent no points).</summary>
    Upset,

    /// <summary>The winning side held more points (more viewers when Helix sent no points).</summary>
    Favourite,

    /// <summary>Both sides were level.</summary>
    EvenMatch,
}

/// <summary>The composed line and the templates it was made from, for the repeat memory.</summary>
public sealed record PredictionVerdictLine(string Text, IReadOnlyList<string> Templates);

/// <summary>
/// The line chat and the prediction-report page get when a prediction resolves, in the voice of
/// Alarak, Highlord of the Tal'darim. The lines are written for this stream and only borrow his
/// manner. An opening suits how the viewers did (<see cref="PredictionVerdictKind"/>), and a
/// callout names the top winner, a long streak, or the biggest loser. A template is only used
/// when every placeholder in it has a value, and recently used templates are skipped while
/// another one fits.
/// </summary>
public static class PredictionVerdict
{
    /// <summary>Twitch drops chat messages longer than this.</summary>
    public const int ChatLimit = 500;

    /// <summary>A top winner on at least this many correct calls in a row gets a streak callout.</summary>
    public const int StreakWorthMentioning = 3;

    /// <summary>How many used templates <see cref="PredictionStreakBook"/> keeps to avoid.</summary>
    public const int RecentToAvoid = 6;

    private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>Fits any contested result: upset, favourite, or an even split.</summary>
    internal static readonly string[] Contest =
    {
        "{winner} wins. {loser} has been weighed, measured, and discarded.",
        "{winner} ascends. {loser} may resume their place at the bottom of the Chain.",
        "{winner} prevails. I would applaud, but I save that for things that surprise me.",
        "The match is settled in favour of {winner}. {loser} fought like servants of a dead god.",
        "{winner} is victorious. I watched {loser} crumble and was mildly entertained.",
        "{loser} has fallen, as I knew they would. The rest of you merely caught up.",
        "{winner} wins the Rak'Shir. {loser} forfeits everything, including my interest.",
        "Victory to {winner}. Those who doubted them may kneel now. Those who did not may also kneel.",
        "{winner} takes it. {loser} should study this defeat; it is the only lesson they can afford.",
        "The Highlord has seen enough. {winner} wins, and {loser} is dismissed.",
        "{winner} conquers {map}. {loser} will not be remembered there.",
        "{map} belongs to {winner} now. I have seen sturdier defences from a lone zealot.",
        "On {map}, {winner} proved worthy. {loser} proved only that they showed up.",
        "{winner} claims {map}. A modest conquest. I have taken greater ones on a whim.",
    };

    internal static readonly IReadOnlyDictionary<PredictionVerdictKind, string[]> Openings =
        new Dictionary<PredictionVerdictKind, string[]>
        {
            [PredictionVerdictKind.Upset] = new[]
            {
                "{winner} wins against the odds. The crowd backed {loser}. The crowd is a herd.",
                "An upset. The confident flock that chose {loser} has been shorn, and {winner} stands over it.",
                "{winner} wins. Most of you trusted {loser}. I would call that brave, if it were not so foolish.",
                "The favourite falls. {winner} rises, and {pool} points of misplaced faith change hands.",
                "{loser} was the popular choice. Popularity has never won a single battle. {winner} wins.",
                "You placed your trust in {loser}. Trust is a luxury for the weak. {winner} wins.",
                "{winner} defies the chat and wins. Delicious.",
                "Only {winners} of you dared to back {winner}. The other {losers} may now explain themselves.",
                "{winner} wins, and {map} has humbled the majority. Good. Humility suits you.",
            },
            [PredictionVerdictKind.Favourite] = new[]
            {
                "{winner} wins, as the herd expected. Even a herd stumbles the right way sometimes.",
                "The obvious choice was {winner}, and it was correct. Do not expect praise for predicting a sunrise.",
                "{winner} wins. The crowd was right. I find that deeply irritating.",
                "Most of you picked {winner}, and most of you will now be insufferable. Enjoy it while it lasts.",
                "{winner} wins. To those who backed {loser}: defiance is only admirable when it works.",
                "Predictable. {winner} wins and the safe bet pays out. How thrilling.",
            },
            [PredictionVerdictKind.EvenMatch] = new[]
            {
                "{winner} wins. The chat split down the middle, which means half of you were wrong. As usual.",
            },
            [PredictionVerdictKind.Unanimous] = new[]
            {
                "Every one of you chose {winner}, and {winner} won. A rare moment of collective competence.",
                "Not a single soul doubted {winner}. Suspicious, but correct.",
                "{winner} wins and no one loses a point. Where is the fun in that?",
                "All of you backed {winner}. I expected at least one fool. You disappoint me in the strangest ways.",
            },
            [PredictionVerdictKind.Wipeout] = new[]
            {
                "{winner} wins. Not one of you saw it coming. Not one.",
                "Every point went on {loser}, and every point is gone. Magnificent.",
                "{winner} wins and the chat is wiped out to the last viewer. I have rarely been so entertained.",
                "A total collapse. {winner} wins, and your faith in {loser} has cost you everything you staked.",
                "{pool} points, all on {loser}, all lost. The Tal'darim would sing of this folly, if they sang.",
            },
            [PredictionVerdictKind.Silence] = new[]
            {
                "{winner} wins. No one predicted. Cowardice is also a choice.",
                "{winner} wins, and not one of you dared to wager. The Tal'darim would have cast you out.",
                "{winner} wins. An empty prediction. Silence is not neutrality; it is fear.",
                "{winner} wins. You hoarded your points and risked nothing. Ascension is not for spectators.",
            },
        };

    internal static readonly string[] TopCallouts =
    {
        "{top} takes {won} points. Do not let it go to your head; that is my role.",
        "{top} walks away with {won} points. Adequate.",
        "{top} read the battle correctly and earns {won} points. I am almost impressed.",
        "{won} points to {top}. Guard them; someone stronger will come for them.",
        "{top} claims {won} points. Rise, {top}. Slowly.",
    };

    internal static readonly string[] StreakCallouts =
    {
        "{top} has been right {streak} times in a row. Either a prophet or a cheat. I respect both.",
        "{streak} correct calls in a row for {top}. Ascension suits you.",
        "{top} has called it right {streak} times straight. Keep climbing; the fall will be spectacular.",
    };

    internal static readonly string[] LoserCallouts =
    {
        "{victim} threw away {spent} points. A generous offering to the worthy.",
        "{victim} lost {spent} points. I trust the lesson was worth the price.",
        "{victim} wagered {spent} points on {loser}. I will remember that, {victim}. Fondly.",
        "Condolences to {victim}, who lost {spent} points. No, not really.",
    };

    public static PredictionVerdictKind Kind(PredictionReport report)
    {
        int winners = WinnerVoters(report);
        int losers = LoserVoters(report);
        if (winners == 0 && losers == 0)
        {
            return PredictionVerdictKind.Silence;
        }

        if (losers == 0)
        {
            return PredictionVerdictKind.Unanimous;
        }

        if (winners == 0)
        {
            return PredictionVerdictKind.Wipeout;
        }

        int compare =
            report.WinnerPoints > 0 || report.LoserPoints > 0
                ? report.WinnerPoints.CompareTo(report.LoserPoints)
                : winners.CompareTo(losers);
        return compare switch
        {
            < 0 => PredictionVerdictKind.Upset,
            > 0 => PredictionVerdictKind.Favourite,
            _ => PredictionVerdictKind.EvenMatch,
        };
    }

    /// <summary>
    /// Null when the report has no winner (a canceled or still-open prediction).
    /// <paramref name="recent"/> are templates used lately; they are skipped while another fits.
    /// </summary>
    public static PredictionVerdictLine Compose(
        PredictionReport report,
        IEnumerable<string> recent,
        Random random
    )
    {
        if (report == null || string.IsNullOrWhiteSpace(report.WinningOutcome))
        {
            return null;
        }

        random ??= Random.Shared;
        var avoid = new HashSet<string>(recent ?? Array.Empty<string>(), StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> values = Values(report);
        string opening = Pick(OpeningsFor(Kind(report)), values, avoid, random);
        string callout = Pick(CalloutsFor(values), values, avoid, random);

        string text = Fill(opening, values);
        var used = new List<string> { opening };
        if (callout != null)
        {
            string longer = text + " " + Fill(callout, values);
            if (longer.Length <= ChatLimit)
            {
                text = longer;
                used.Add(callout);
            }
        }

        if (text.Length > ChatLimit)
        {
            text = text.Substring(0, ChatLimit - 3) + "...";
        }

        return new PredictionVerdictLine(text, used);
    }

    internal static IEnumerable<string> OpeningsFor(PredictionVerdictKind kind)
    {
        IEnumerable<string> own = Openings.TryGetValue(kind, out string[] lines)
            ? lines
            : Array.Empty<string>();
        return
            kind
                is PredictionVerdictKind.Upset
                    or PredictionVerdictKind.Favourite
                    or PredictionVerdictKind.EvenMatch
            ? own.Concat(Contest)
            : own;
    }

    internal static bool Fits(string template, IReadOnlyDictionary<string, string> values)
    {
        foreach (Match match in Placeholder.Matches(template))
        {
            if (!values.ContainsKey(match.Groups[1].Value))
            {
                return false;
            }
        }

        return true;
    }

    internal static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder.Replace(
            template,
            match =>
                values.TryGetValue(match.Groups[1].Value, out string value) ? value : match.Value
        );

    internal static IReadOnlyDictionary<string, string> Values(PredictionReport report)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["winner"] = report.WinningOutcome.Trim(),
        };
        AddText(values, "loser", report.LosingOutcome);
        AddText(values, "map", report.Map);
        AddCount(values, "winners", WinnerVoters(report));
        AddCount(values, "losers", LoserVoters(report));
        AddCount(values, "pool", report.LoserPoints);

        PredictionParticipant top = report.Winners?.FirstOrDefault(row =>
            row.PointsWon > 0 && !string.IsNullOrWhiteSpace(Name(row))
        );
        if (top != null)
        {
            values["top"] = Name(top);
            values["won"] = Number(top.PointsWon);
            if (top.Streak >= StreakWorthMentioning)
            {
                values["streak"] = Number(top.Streak);
            }
        }

        PredictionParticipant victim = report.Losers?.FirstOrDefault(row =>
            row.PointsUsed > 0 && !string.IsNullOrWhiteSpace(Name(row))
        );
        if (victim != null)
        {
            values["victim"] = Name(victim);
            values["spent"] = Number(victim.PointsUsed);
        }

        return values;
    }

    private static IEnumerable<string> CalloutsFor(IReadOnlyDictionary<string, string> values) =>
        values.ContainsKey("streak") ? StreakCallouts : TopCallouts.Concat(LoserCallouts);

    private static string Pick(
        IEnumerable<string> templates,
        IReadOnlyDictionary<string, string> values,
        ISet<string> avoid,
        Random random
    )
    {
        List<string> fits = templates.Where(template => Fits(template, values)).ToList();
        if (fits.Count == 0)
        {
            return null;
        }

        List<string> fresh = fits.Where(template => !avoid.Contains(template)).ToList();
        List<string> pool = fresh.Count > 0 ? fresh : fits;
        return pool[random.Next(pool.Count)];
    }

    private static int WinnerVoters(PredictionReport report) =>
        Math.Max(report.WinnerVoters, report.Winners?.Count ?? 0);

    private static int LoserVoters(PredictionReport report) =>
        Math.Max(report.LoserVoters, report.Losers?.Count ?? 0);

    private static string Name(PredictionParticipant row) =>
        string.IsNullOrWhiteSpace(row.DisplayName) ? row.Login : row.DisplayName;

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static void AddText(Dictionary<string, string> values, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value.Trim();
        }
    }

    private static void AddCount(Dictionary<string, string> values, string key, int value)
    {
        if (value > 0)
        {
            values[key] = Number(value);
        }
    }
}
