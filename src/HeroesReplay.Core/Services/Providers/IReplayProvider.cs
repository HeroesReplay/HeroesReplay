using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Providers
{
    public interface IReplayProvider
    {
        /// <summary>
        /// Attemps to the load the next available replay.
        /// </summary>
        /// <returns>
        /// LoadedReplay or Null
        /// </returns>
        Task<LoadedReplay> TryLoadNextReplayAsync();
    }
}