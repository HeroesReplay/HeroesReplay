using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public class ExtensionPayloadBuilder : IExtensionPayloadsBuilder
{
    private const int MaxPlayers = 10;
    private const int MaxTalents = 7;

    private readonly ILogger<ExtensionPayloadBuilder> logger;
    private readonly AppSettings settings;

    public ExtensionPayloadBuilder(ILogger<ExtensionPayloadBuilder> logger, AppSettings settings)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public ExtensionGame CreatePayloads(Replay replay)
    {
        if (settings.TwitchExtension?.Enabled != true || replay == null)
        {
            return null;
        }

        List<Player> selected = new();
        if (replay.Players != null)
        {
            foreach (Player player in replay.Players)
            {
                if (selected.Count >= MaxPlayers)
                {
                    break;
                }

                if (
                    player == null
                    || string.IsNullOrWhiteSpace(player.Name)
                    || (player.Team != 0 && player.Team != 1)
                )
                {
                    continue;
                }

                selected.Add(player);
            }
        }

        int fallbackRegion = 0;
        foreach (Player player in selected)
        {
            if (player.BattleNetRegionId is >= 1 and <= 5)
            {
                fallbackRegion = player.BattleNetRegionId;
                break;
            }
        }

        var roster = new List<ExtensionPlayer>(selected.Count);
        var rosterPlayers = new List<Player>(selected.Count);
        foreach (Player player in selected)
        {
            int region = player.BattleNetRegionId;
            if (region is < 1 or > 5)
            {
                region = fallbackRegion;
            }

            if (region is < 1 or > 5)
            {
                logger.LogWarning(
                    "Twitch extension skipped {Name} because region {Region} is not 1-5.",
                    player.Name,
                    player.BattleNetRegionId
                );
                continue;
            }

            bool ai = player.PlayerType == PlayerType.Computer;
            rosterPlayers.Add(player);
            roster.Add(
                new ExtensionPlayer(
                    Limit(player.Name, 32, "name"),
                    ai ? 0 : player.BattleTag,
                    region,
                    player.Team,
                    Limit(HeroName(player), 64, "hero"),
                    Limit(player.HeroAttributeId, 64, "hero_attribute"),
                    ai
                )
            );
        }

        var picks = new List<ExtensionTalentPick>();
        var seen = new Dictionary<int, List<string>>();
        string talentChosen = settings.TrackerEvents?.TalentChosen;
        if (
            !string.IsNullOrEmpty(talentChosen)
            && replay.TrackerEvents != null
            && replay.Players != null
        )
        {
            foreach (TrackerEvent trackerEvent in replay.TrackerEvents)
            {
                if (
                    !TryReadTalent(
                        trackerEvent,
                        talentChosen,
                        out string talentName,
                        out int playerSlot
                    )
                )
                {
                    continue;
                }

                if ((uint)playerSlot >= (uint)replay.Players.Length)
                {
                    continue;
                }

                Player player = replay.Players[playerSlot];
                int rosterIndex = rosterPlayers.IndexOf(player);
                if (rosterIndex < 0)
                {
                    continue;
                }

                if (!seen.TryGetValue(rosterIndex, out List<string> names))
                {
                    names = new List<string>();
                    seen[rosterIndex] = names;
                }

                if (names.Count >= MaxTalents || names.Contains(talentName))
                {
                    continue;
                }

                talentName = Limit(talentName, 128, "talent");
                names.Add(talentName);
                picks.Add(new ExtensionTalentPick(trackerEvent.TimeSpan, rosterIndex, talentName));
            }
        }

        logger.LogInformation("Twitch extension talent picks: {Count}.", picks.Count);
        return new ExtensionGame
        {
            GameMode = Limit(replay.GameMode.ToString(), 32, "game_mode"),
            Map = Limit(replay.Map, 64, "map"),
            GameVersion = Limit(replay.ReplayVersion, 32, "game_version"),
            Players = roster,
            Talents = picks,
        };
    }

    private bool TryReadTalent(
        TrackerEvent trackerEvent,
        string talentChosen,
        out string talentName,
        out int playerIndex
    )
    {
        talentName = null;
        playerIndex = -1;
        try
        {
            if (trackerEvent?.Data?.dictionary == null)
            {
                return false;
            }

            if (
                !trackerEvent.Data.dictionary.TryGetValue(0, out TrackerEventStructure eventName)
                || eventName?.blobText != talentChosen
            )
            {
                return false;
            }

            talentName = trackerEvent
                .Data
                .dictionary[1]
                .optionalData
                .array[0]
                .dictionary[1]
                .blobText;
            long slot = trackerEvent
                .Data
                .dictionary[2]
                .optionalData
                .array[0]
                .dictionary[1]
                .vInt
                .Value;
            playerIndex = (int)slot - 1;
            return !string.IsNullOrEmpty(talentName);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Skipped a tracker event the Twitch extension could not read.");
            return false;
        }
    }

    private static string HeroName(Player player)
    {
        if (!string.IsNullOrEmpty(player.Character))
        {
            return player.Character;
        }

        return player.HeroAttributeId;
    }

    private string Limit(string value, int max, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        logger.LogWarning(
            "Twitch extension {Field} truncated from {Length} to {Max}.",
            field,
            value.Length,
            max
        );
        return value.Substring(0, max);
    }
}
