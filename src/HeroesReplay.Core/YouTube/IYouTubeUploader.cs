using System.Threading.Tasks;

namespace HeroesReplay.Core.YouTube;

public interface IYouTubeUploader
{
    Task ListenAsync();
}
