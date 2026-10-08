using System;
using System.Collections.Generic;
using System.Globalization;
using Heroes.ReplayParser;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;

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
            FocusHero = PlayerPriorityRequest.HeroName(
                replay,
                PlayerPriorityRequest.PlayerIndex(replay, request)
            ),
            ViewerRequested = request != null,
            RecordAndUpload = ReplayRequestKind.RecordsAndUploads(request),
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

    /// <summary>
    /// Each player with the catalog's English name of the hero played (<see cref="PlayedHero"/>).
    /// The replay's character name is in the uploader's game language, and the lobby hero is not the
    /// hero played in ARAM (#348).
    /// </summary>
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
                    Hero = PlayedHero.Name(heroes, replay, player),
                    Name = FirstText(player.Name),
                    BattleTag = player.BattleTag,
                    IsAi = player.PlayerType == PlayerType.Computer,
                    Talents = Talents(player),
                }
            );
        }

        return roster;
    }

    /// <summary>
    /// The talent ids the player picked, in pick order. The parser names a talent only when game
    /// events and statistics were both parsed; an unnamed pick is skipped.
    /// </summary>
    public static IReadOnlyList<string> Talents(Player player)
    {
        if (player?.Talents == null || player.Talents.Length == 0)
        {
            return Array.Empty<string>();
        }

        var talents = new List<string>(player.Talents.Length);
        foreach (Talent talent in player.Talents)
        {
            if (!string.IsNullOrWhiteSpace(talent?.TalentName))
            {
                talents.Add(talent.TalentName.Trim());
            }
        }

        return talents;
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
