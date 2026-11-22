using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeroesReplay.Core.HeroFocus.Cards;

namespace HeroesReplay.Core.YouTube.Metadata;

public enum PlayerStoryKind
{
    WinStreak,
    LossStreak,
    OneTrick,
    WorstHero,
    BestHero,
    Climb,
    Slide,
}

/// <summary>
/// One story about a player before the match: the title lead (it names the hero, never the
/// player) and the <c>Story:</c> description line with its numbers.
/// </summary>
public sealed record PlayerStory(PlayerStoryKind Kind, string Hero, string Title, string Line);

/// <summary>
/// Picks at most one story from the players' Heroes Profile history
/// (<c>YouTube:Titles:PlayerStories</c>). Every story counts only games before the match, so it
/// never tells the result. The strongest story wins: a long streak, a one-trick or a player on
/// their worst hero lately beat a climb or a best-hero pick. Each kind rotates five to seven
/// wordings by replay id, so the channel does not repeat one line. A hero the title already
/// names is skipped.
/// </summary>
public static class PlayerStoryPicker
{
    public const string StoryLabel = "Story:";

    public static PlayerStory Pick(
        IReadOnlyList<PlayerStoryFacts> players,
        int? replayId,
        IReadOnlyCollection<string> namedHeroes,
        PlayerStorySettings settings
    )
    {
        if (settings?.Enabled != true || players == null || players.Count == 0)
        {
            return null;
        }

        int seed = Math.Abs(replayId ?? 0);
        (PlayerStory Story, double Score)? best = null;
        foreach (PlayerStoryFacts player in players)
        {
            if (
                player?.History?.IsKnown != true
                || string.IsNullOrWhiteSpace(player.Hero)
                || (
                    namedHeroes != null
                    && namedHeroes.Any(name => HeroDraft.SameName(name, player.Hero))
                )
            )
            {
                continue;
            }

            foreach ((PlayerStory story, double score) in Candidates(player, settings, seed))
            {
                if (best == null || score > best.Value.Score)
                {
                    best = (story, score);
                }
            }
        }

        PlayerStory picked = best?.Story;
        // TryReadTitleId reads the first all-digit title part as the replay id.
        return picked == null || int.TryParse(picked.Title.Trim(), out _) ? null : picked;
    }

    private static IEnumerable<(PlayerStory, double)> Candidates(
        PlayerStoryFacts player,
        PlayerStorySettings settings,
        int seed
    )
    {
        PlayerHistory history = player.History;
        string hero = player.Hero.Trim();
        string whose = Team(player.Team) + hero;

        int streak = Math.Abs(history.Streak);
        if (settings.Streaks && streak >= Math.Max(2, settings.StreakMinGames))
        {
            bool won = history.Streak > 0;
            string n = Number(streak);
            string title = won
                ? Rotate(
                    seed,
                    $"{hero} rides a {n}-game win streak",
                    $"{n} straight wins: can anyone stop this {hero}?",
                    $"{hero} is on a heater: {n} wins in a row",
                    $"Hot streak: {hero} with {n} wins in a row",
                    $"Unbeaten in {n} games: {hero}",
                    $"{hero} has won {n} straight. Make it {Number(streak + 1)}?"
                )
                : Rotate(
                    seed,
                    $"{hero}, {n} losses deep. Redemption arc?",
                    $"Tilt check: {hero} after {n} straight losses",
                    $"{hero} needs this one: {n} losses in a row",
                    $"{hero} on a {n}-game losing streak",
                    $"Snap the skid? {hero} after {n} straight losses",
                    $"Breaking point? {hero}, {n} losses in a row"
                );
            yield return (
                new PlayerStory(
                    won ? PlayerStoryKind.WinStreak : PlayerStoryKind.LossStreak,
                    hero,
                    title,
                    Line(
                        $"{whose} came into this game on {n} Storm League {(won ? "wins" : "losses")} in a row"
                    )
                ),
                50 + 5 * streak
            );
        }

        int career = player.CareerGames ?? 0;
        bool shareTrick =
            history.Games >= Math.Max(1, settings.OneTrickMinGames)
            && history.HeroShare >= settings.OneTrickMinShare;
        bool careerTrick = career >= Math.Max(1, settings.MainMinCareerGames);
        if (settings.OneTricks && (shareTrick || careerTrick))
        {
            string title = careerTrick
                ? Rotate(
                    seed,
                    $"{Number(career)}-game {hero} main",
                    $"The {hero} one-trick",
                    $"{Number(career)} games of {hero} and counting",
                    $"{hero} specialist: {Number(career)} games",
                    $"{Number(career)} games deep on {hero}",
                    $"The {hero} main ({Number(career)} games)"
                )
                : Rotate(
                    seed,
                    $"The {hero} one-trick",
                    $"{hero} one-trick, nothing else",
                    $"{hero} or nothing",
                    $"All-in on {hero}: {Percent(history.HeroShare)} of their games",
                    $"Only {hero}: {history.HeroGames} of their last {history.Games} games",
                    $"{hero} every game"
                );
            string line = careerTrick
                ? $"{whose} has played {Number(career)} Storm League games on {hero}"
                : $"{whose} played {hero} in {history.HeroGames} of their last {history.Games} Storm League games";
            yield return (
                new PlayerStory(PlayerStoryKind.OneTrick, hero, title, Line(line)),
                70 + 10 * Math.Min(1, history.HeroShare)
            );
        }

        if (history.HeroGames > 0)
        {
            string record = $"{history.HeroWins}-{history.HeroGames - history.HeroWins}";
            double rate = (double)history.HeroWins / history.HeroGames;
            if (settings.WorstHero && history.WorstHero && rate <= settings.WorstMaxWinRate)
            {
                yield return (
                    new PlayerStory(
                        PlayerStoryKind.WorstHero,
                        hero,
                        Rotate(
                            seed,
                            $"{hero}: their worst hero lately. Bold.",
                            $"Comfort pick? {hero} has been their worst hero lately",
                            $"Against the odds: {hero} ({record} lately)",
                            $"Risky pick: {hero}, {record} lately",
                            $"{hero} despite a {record} record lately",
                            $"Their lowest win rate lately: {hero}"
                        ),
                        Line(
                            $"{whose} went {record} on {hero} over the last {Days(settings)} days, the lowest win rate of their heroes"
                        )
                    ),
                    75
                );
            }

            if (settings.BestHero && history.BestHero && rate >= settings.BestMinWinRate)
            {
                yield return (
                    new PlayerStory(
                        PlayerStoryKind.BestHero,
                        hero,
                        Rotate(
                            seed,
                            $"{hero} on their best hero ({record} lately)",
                            $"Comfort pick: {hero}, {record} lately",
                            $"{hero}, their go-to ({record})",
                            $"Signature hero: {hero} ({record} lately)",
                            $"{hero}, their strongest pick lately ({record})",
                            $"Best in their pool: {hero}, {record}"
                        ),
                        Line(
                            $"{whose} went {record} on {hero} over the last {Days(settings)} days, the best win rate of their heroes"
                        )
                    ),
                    55
                );
            }
        }

        if (
            settings.Climbs
            && history.MmrChange is double change
            && Math.Abs(change) >= Math.Max(1, settings.ClimbMinMmr)
        )
        {
            bool up = change > 0;
            string points = Number((int)Math.Round(Math.Abs(change)));
            yield return (
                new PlayerStory(
                    up ? PlayerStoryKind.Climb : PlayerStoryKind.Slide,
                    hero,
                    up
                        ? Rotate(
                            seed,
                            $"{hero} on a +{points} MMR climb",
                            $"Climbing fast: +{points} MMR {hero}",
                            $"{hero} up {points} MMR in {history.MmrGames} games",
                            $"On the rise: {hero}, +{points} MMR",
                            $"+{points} MMR and climbing: {hero}",
                            $"Ladder climber {hero} (+{points} MMR)"
                        )
                        : Rotate(
                            seed,
                            $"{hero}'s -{points} MMR slide. Turnaround?",
                            $"Bounce-back game? {hero} is down {points} MMR",
                            $"{hero} down {points} MMR in {history.MmrGames} games",
                            $"Rough patch: {hero}, -{points} MMR",
                            $"Stop the slide? {hero}, -{points} MMR",
                            $"{hero} looking to win back {points} MMR"
                        ),
                    Line(
                        $"{whose} {(up ? "gained" : "lost")} {points} Storm League MMR over their last {history.MmrGames} games"
                    )
                ),
                40 + Math.Abs(change) / 2
            );
        }
    }

    private static string Rotate(int seed, params string[] lines) => lines[seed % lines.Length];

    private static string Team(int team) =>
        team switch
        {
            0 => "Blue's ",
            1 => "Red's ",
            _ => string.Empty,
        };

    private static int Days(PlayerStorySettings settings) => Math.Max(1, settings.LookbackDays);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Percent(double share) =>
        Number((int)Math.Round(100 * Math.Min(1, Math.Max(0, share)))) + "%";

    private static string Line(string sentence) => StoryLabel + " " + sentence + ".";
}
