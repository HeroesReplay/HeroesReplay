using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IReplayDetailsWriter
    {
        Task ClearFileForObs();
        Task WriteFileForObs();
        Task WriteYouTubeDetails();
    }
}