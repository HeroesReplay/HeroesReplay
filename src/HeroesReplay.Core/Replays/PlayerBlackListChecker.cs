using System.Linq;
using HeroesReplay.Core.Replays.HotsApi;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.Core.Replays
{
    /// <summary>
    /// This should use a service that uses the blizzard auth service to confirm that a player is who they say they are,
    /// so that we can remove them from being visible on the replay.
    /// </summary>
    /// <remarks>
    /// There a few methods to removing the player from the service.
    ///
    /// 1. They can be removed safely by simply not using this replay file.
    /// 2. Modify the replay file by replacing their battletag bits with a randomly generated battletag of the same length
    /// </remarks>
    public class PlayerBlackListChecker
    {
        private readonly IConfiguration configuration;

        public PlayerBlackListChecker(IConfiguration configuration)
        {
            this.configuration = configuration;
        }


        public bool IsUsable(Replay replay)
        {
            return true;

            #pragma warning disable CS0162 // Unreachable code detected

            var blackList = configuration.GetValue<long[]>("blacklist");
            var blizzardIds = replay.Players.Select(p => p.Blizz_id).ToArray();
            return !blizzardIds.Any(id => blizzardIds.Contains(id));

            #pragma warning restore CS0162 // Unreachable code detected
        }
    }
}