using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Shared;

namespace HeroesReplay.Spectator
{
    public static class SpectatorExtensions
    {
        public static bool IsEventIn(this StormPlayer stormPlayer, params GameEvent[] events) => events.Contains(stormPlayer.Event);
        public static bool IsEnd(this GameState state) => state == GameState.EndOfGame;
        public static bool IsRunning(this GameState state) => state == GameState.Running;
        public static bool IsPaused(this GameState state) => state == GameState.Paused;
        public static bool IsStart(this GameState state) => state == GameState.StartOfGame;
        public static bool IsNearEnd(this TimeSpan timeSpan, TimeSpan end) => timeSpan.Add(TimeSpan.FromMinutes(1)) >= end;
        public static bool IsNearStart(this TimeSpan timeSpan) => timeSpan.IsWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));

        public static int ToKills(this GameEvent @event) => @event switch
        {
            GameEvent.Kill => 1,
            GameEvent.MultiKill => 2,
            GameEvent.TripleKill => 3,
            GameEvent.QuadKill => 4,
            GameEvent.PentaKill => 5,
            _ => throw new Exception($"Unhandled {nameof(GameEvent)}")
        };

        public static IEnumerable<StormPlayer> Or(this IEnumerable<StormPlayer> selection, IEnumerable<StormPlayer> next) => selection.Any() ? selection : next;
    }
}