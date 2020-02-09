using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Spectator
{
    public static class SpectatorExtensions
    {
        public static bool IsAny(this SpectateEvent @event, params SpectateEvent[] events) => events.Contains(@event);
        public static bool IsEnd(this StormState stormState) => stormState.State == GameState.EndOfGame;
        public static bool IsRunning(this StormState stormState) => stormState.State == GameState.Running;
        public static bool IsPaused(this StormState stormState) => stormState.State == GameState.Paused;
        public static bool IsStart(this StormState stormState) => stormState.State == GameState.StartOfGame;
        public static bool IsNearEnd(this StormState stormState, TimeSpan end) => stormState.Timer.Add(TimeSpan.FromMinutes(2)) >= end;
        public static bool IsNearStart(this StormState stormState) => stormState.Timer.IsWithin(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));

        public static async Task SpectateAsync(this StormPlayer player, CancellationToken token = default)
        {
            if (player == null) await Task.CompletedTask;
            await Task.Delay(player.Duration <= TimeSpan.Zero ? TimeSpan.Zero : player.Duration, token);
        }

        public static bool SupportsCarriedObjectives(this StormReplay replay) => replay.Replay.MapAlternativeName switch
        {
            Constants.CarriedObjectiveMaps.BlackheartsBay => true,
            Constants.CarriedObjectiveMaps.TombOfTheSpiderQueen => true,
            Constants.CarriedObjectiveMaps.WarheadJunction => true,
            _ => false
        };

        public static int ToKills(this SpectateEvent @event) => @event switch
        {
            SpectateEvent.Kill => 1,
            SpectateEvent.MultiKill => 2,
            SpectateEvent.TripleKill => 3,
            SpectateEvent.QuadKill => 4,
            SpectateEvent.PentaKill => 5,
            _ => throw new Exception($"Unhandled {nameof(SpectateEvent)}")
        };
    }
}