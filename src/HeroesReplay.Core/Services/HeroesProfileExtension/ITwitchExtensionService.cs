
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface ITwitchExtensionService
    {
        Task<string> CreateReplaySessionAsync(ExtensionPayload payload, CancellationToken token = default);
        Task<bool> CreatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdateReplayDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerTalentsAsync(List<ExtensionPayload> lists, string sessionId, CancellationToken token = default);
        Task<bool> NotifyTwitchAsync(CancellationToken token = default);
    }
}