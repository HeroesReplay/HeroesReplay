using System;
using System.Collections.Generic;
using System.Globalization;
using Heroes.ReplayParser;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;

namespace HeroesReplay.Core.Services.Media;

/// <summary>
/// One pre-launch view of a parsed replay. Completion and media stay empty until the session ends.
/// </summary>
public static class ReplayMediaFacts
{
    public static ReplayMediaPolicyInput From(
        LoadedReplay loaded,
        bool alreadyPublished,
        bool alreadyScheduled,
        bool inOutbox
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
            Roster = Roster(replay),
            FocusHero = PlayerPriorityRequest.HeroName(replay, request?.PlayerIndex),
            ViewerRequested = request != null,
            RecordAndUpload = request?.RecordAndUpload == true,
            RequestedBy = FirstText(request?.Login),
            NotableEvents = TeamKillClips.Select(TeamKillDeaths.FromReplay(replay)),
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

    private static IReadOnlyList<ReplayMediaPlayer> Roster(Replay replay)
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
                    Hero = Hero(player),
                    Name = FirstText(player.Name),
                    BattleTag = player.BattleTag,
                    IsAi = player.PlayerType == PlayerType.Computer,
                }
            );
        }

        return roster;
    }

    private static string Hero(Player player)
    {
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
