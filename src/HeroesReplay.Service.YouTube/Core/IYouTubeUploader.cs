using System.Threading.Tasks;

namespace HeroesReplay.Service.YouTube.Core
{
    public interface IYouTubeUploader
    {
        Task ListenAsync();
    }
}
