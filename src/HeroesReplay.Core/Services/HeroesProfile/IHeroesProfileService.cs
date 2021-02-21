using HeroesReplay.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IHeroesProfileService
    {
        Task<ReplayData> GetReplayDataAsync(int replayId);
        Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId);

        /// <exception cref="ReplayDeletedException">When the replay was found but the raw asset is deleted.</exception>
        /// <exception cref="ReplayVersionNotSupportedException">When the replay was found but the raw asset is deleted.</exception>
        /// <exception cref="ReplayNotFoundException">When the replay was found but the raw asset is deleted.</exception>
        Task<HeroesProfileReplay> GetReplayAsync(int replayId);

        /// <exception cref="ReplayDeletedException">When the replay was found but the raw asset is deleted.</exception>
        /// <exception cref="ReplayVersionNotSupportedException">When the replay was found but the raw asset is deleted.</exception>
        /// <exception cref="ReplayNotFoundException">When the replay was found but the raw asset is deleted.</exception>
        Task<RewardReplay> GetReplayAsync(GameMode? mode, Tier? tier = null, string map = null);

        Task<string> CreateReplaySessionAsync(ExtensionPayload payload, CancellationToken token = default);
        Task<bool> CreatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdateReplayDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerTalentsAsync(List<ExtensionPayload> lists, string sessionId, CancellationToken token = default);
        Task<bool> NotifyTwitchAsync(CancellationToken token = default);
    }
}