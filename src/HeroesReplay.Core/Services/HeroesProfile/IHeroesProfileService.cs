using HeroesReplay.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IHeroesProfileService
    {
        Task<(int RankPoints, string Tier)> GetMMRAsync(SessionData sessionData);
        Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId);
        Task<string> CreateReplaySessionAsync(HeroesProfileTwitchPayload payload, CancellationToken token = default);
        Task<bool> CreatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdateReplayDataAsync(HeroesProfileTwitchPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerTalentsAsync(List<HeroesProfileTwitchPayload> lists, string sessionId, CancellationToken token = default);
        Task<bool> NotifyTwitchAsync(CancellationToken token = default);
    }
}