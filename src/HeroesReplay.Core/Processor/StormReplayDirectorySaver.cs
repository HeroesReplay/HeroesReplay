using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;

namespace HeroesReplay.Core.Replays
{

    public class StormReplayDirectorySaver : IStormReplaySaver
    {
        private readonly IConfiguration configuration;

        public StormReplayDirectorySaver(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        public async Task<StormReplay> SaveReplayAsync(StormReplay stormReplay)
        {
            string replay = Path.GetFileName(stormReplay.Path);
            string directory = configuration.GetValue<string>(Constants.ConfigKeys.ReplayDestination);

            string copy = Path.Combine(directory, replay);

            File.Copy(stormReplay.Path, copy);

            return await Task.FromResult(new StormReplay(copy, stormReplay.Replay));
        }
    }
}