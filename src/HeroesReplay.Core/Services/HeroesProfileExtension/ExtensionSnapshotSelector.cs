using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public static class ExtensionSnapshotSelector
{
    public const string Lobby = "lobby";
    public const string InGame = "in_game";
    public const string Ended = "ended";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static ExtensionSnapshot Select(ExtensionGame game, string phase, TimeSpan? timer)
    {
        if (game == null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        bool showHeroes = phase != Lobby;
        IReadOnlyList<string>[] talents = TalentsFor(game, phase, timer);
        var players = new List<ExtensionSnapshotPlayer>(game.Players.Count);
        for (int i = 0; i < game.Players.Count; i++)
        {
            ExtensionPlayer player = game.Players[i];
            players.Add(
                new ExtensionSnapshotPlayer(
                    player.Name,
                    player.BattleTag,
                    player.Region,
                    player.Team,
                    showHeroes ? player.Hero : null,
                    showHeroes ? player.HeroAttribute : null,
                    talents[i],
                    player.Ai
                )
            );
        }

        return new ExtensionSnapshot
        {
            Phase = phase,
            GameMode = game.GameMode,
            Map = game.Map,
            GameVersion = game.GameVersion,
            Players = players,
        };
    }

    public static string Hash(ExtensionSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Json);

    private static IReadOnlyList<string>[] TalentsFor(
        ExtensionGame game,
        string phase,
        TimeSpan? timer
    )
    {
        var talents = new IReadOnlyList<string>[game.Players.Count];
        var picked = new List<string>[game.Players.Count];
        for (int i = 0; i < game.Players.Count; i++)
        {
            picked[i] = [];
        }

        if (phase != Lobby)
        {
            foreach (ExtensionTalentPick pick in game.Talents)
            {
                if (pick.PlayerIndex < 0 || pick.PlayerIndex >= game.Players.Count)
                {
                    continue;
                }

                if (phase == InGame && (!timer.HasValue || pick.Time > timer.Value))
                {
                    continue;
                }

                List<string> names = picked[pick.PlayerIndex];
                if (names.Count >= 7 || names.Contains(pick.TalentName))
                {
                    continue;
                }

                names.Add(pick.TalentName);
            }
        }

        for (int i = 0; i < talents.Length; i++)
        {
            talents[i] = picked[i];
        }

        return talents;
    }
}
