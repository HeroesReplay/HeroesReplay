using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Stream
{
    public interface IReplayDetailsWriter
    {
        Task ClearFileForObs();
        Task WriteFileForObs();
        Task WriteYouTubeDetails();
    }
}