using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.YouTube;

public interface IYouTubePlaylistClient
{
    Task<string> FindOrCreateAsync(string title, CancellationToken cancellationToken);

    Task InsertAsync(string playlistId, string videoId, CancellationToken cancellationToken);
}
