using System;
using System.Collections.Generic;
using System.Globalization;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;

namespace HeroesReplay.Core.MediaPolicy;

/// <summary>
/// One pre-launch view of a parsed replay. Completion and media stay empty until the session ends.
/// </summary>
public static class ReplayMediaFacts
{
    public static ReplayMediaPolicyInput From(
        LoadedReplay loaded,
        bool alreadyPublished,
        bool alreadyScheduled,
        bool inOutbox,
        IReadOnlyList<Hero> heroes = null
    )
    {
        if (loaded == null)
        {
            return new ReplayMediaPolicyInput
            {
                AlreadyPublished = alreadyPublished,
                AlreadyScheduled = alreadyScheduled,
                InOutbox = inOutbox,
                NotableEvents = Array.Empty<TeamKillClip>(),
                Roster = Array.Empty<ReplayMediaPlayer>(),
            };
        }

        Replay replay = loaded.Replay;
        HeroesProfileReplay profile = loaded.HeroesProfileReplay;
        RewardRequest request = loaded.RewardQueueItem?.Request;
        return new ReplayMediaPolicyInput
        {
            ReplayId = ReplayId(loaded),
            GameDateUtc = GameDate(replay, profile),
            GameVersion = FirstText(replay?.ReplayVersion, profile?.GameVersion),
            Map = EnglishMapNames.Prefer(profile?.Map, replay?.Map, replay?.MapAlternativeName),
            GameMode = GameMode(replay, profile),
            Rank = FirstText(profile?.Rank),
            AverageMmr = profile?.AverageMmr,
            Roster = Roster(replay, heroes),
            FocusHero = PlayerPriorityRequest.HeroName(replay, request?.PlayerIndex),
            ViewerRequested = request != null,
            RecordAndUpload = request?.RecordAndUpload == true,
            RequestedBy = FirstText(request?.Login),
            NotableEvents = TeamKillClips.Select(TeamKillDeaths.FromReplay(replay, heroes)),
            AlreadyPublished = alreadyPublished,
            AlreadyScheduled = alreadyScheduled,
            InOutbox = inOutbox,
        };
    }

    private static int? ReplayId(LoadedReplay loaded)
    {
        if (loaded.ReplayId is int id && id > 0)
        {
            return id;
        }

        if (loaded.HeroesProfileReplay?.Id is int profileId && profileId > 0)
        {
            return profileId;
        }

        return null;
    }

    private static DateTime? GameDate(Replay replay, HeroesProfileReplay profile)
    {
        if (replay != null && replay.Timestamp != default)
        {
            return replay.Timestamp;
        }

        string text = profile?.GameDate;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (
            DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed
            )
        )
        {
            return parsed;
        }

        return null;
    }

    private static string GameMode(Replay replay, HeroesProfileReplay profile)
    {
        string fromProfile = FirstText(profile?.GameType);
        if (fromProfile != null)
        {
            return fromProfile;
        }

        return replay == null ? null : replay.GameMode.ToString();
    }

    private static IReadOnlyList<ReplayMediaPlayer> Roster(
        Replay replay,
        IReadOnlyList<Hero> heroes
    )
    {
        if (replay?.Players == null || replay.Players.Length == 0)
        {
            return Array.Empty<ReplayMediaPlayer>();
        }

        var roster = new List<ReplayMediaPlayer>(replay.Players.Length);
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
                    Hero = Hero(player, heroes),
                    Name = FirstText(player.Name),
                    BattleTag = player.BattleTag,
                    IsAi = player.PlayerType == PlayerType.Computer,
                }
            );
        }

        return roster;
    }

    /// <summary>
    /// The catalog's English name, matched by attribute id first. The replay's character name is in
    /// the uploader's game language, so a French or Korean replay would otherwise miss draft notes,
    /// composition labels, and English titles.
    /// </summary>
    private static string Hero(Player player, IReadOnlyList<Hero> heroes)
    {
        if (heroes != null && heroes.Count > 0)
        {
            Hero match =
                HeroDraft.Find(heroes, player.HeroAttributeId)
                ?? HeroDraft.Find(heroes, player.HeroId)
                ?? HeroDraft.Find(heroes, player.Character);
            if (!string.IsNullOrWhiteSpace(match?.Name))
            {
                return match.Name.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(player.Character))
        {
            return player.Character.Trim();
        }

        if (!string.IsNullOrWhiteSpace(player.HeroAttributeId))
        {
            return player.HeroAttributeId.Trim();
        }

        return null;
    }

    private static string FirstText(params string[] values)
    {
        if (values == null)
        {
            return null;
        }

        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
