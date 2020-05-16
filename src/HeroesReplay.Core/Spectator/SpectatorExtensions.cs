using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Spectator
{
    public static class SpectatorExtensions
    {
        public static bool IsAny(this SpectateEvent spectateEvent, params SpectateEvent[] events) => events.Contains(spectateEvent);
        public static bool IsEnd(this StormState stormState) => stormState.State == GameState.EndOfGame;
        public static bool IsRunning(this StormState stormState) => stormState.State == GameState.Running;
        public static bool IsPaused(this StormState stormState) => stormState.State == GameState.Paused;
        public static bool IsStart(this StormState stormState) => stormState.State == GameState.StartOfGame;

        public static int ToKills(this SpectateEvent spectateEvent) => spectateEvent switch
        {
            SpectateEvent.Kill => 1,
            SpectateEvent.MultiKill => 2,
            SpectateEvent.TripleKill => 3,
            SpectateEvent.QuadKill => 4,
            SpectateEvent.QuintupleKill => 5,
            _ => throw new Exception($"Unhandled {nameof(SpectateEvent)}")
        };
    }
}