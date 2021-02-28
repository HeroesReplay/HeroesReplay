using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.YouTube
{
    public interface IYouTubeUploader
    {
        Task ListenAsync();
    }
}
