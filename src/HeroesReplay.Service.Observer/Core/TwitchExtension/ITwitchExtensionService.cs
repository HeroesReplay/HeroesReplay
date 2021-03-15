using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analyzer;

namespace HeroesReplay.Service.Spectator.Core.HeroesProfileExtension
{
    public interface ITwitchExtensionService
    {
        Task<string> CreateReplaySessionAsync(TalentsPayload payload, CancellationToken token = default);
        Task<bool> CreatePlayerDataAsync(TalentsPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerDataAsync(TalentsPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdateReplayDataAsync(TalentsPayload payload, string sessionId, CancellationToken token = default);
        Task<bool> UpdatePlayerTalentsAsync(List<TalentsPayload> lists, string sessionId, CancellationToken token = default);
        Task<bool> NotifyTwitchAsync(CancellationToken token = default);
    }
}