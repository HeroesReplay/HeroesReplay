﻿using HeroesReplay.Core.Models;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IHeroesProfileService
    {
        Task<(int RankPoints, string Tier)> GetMMRAsync(StormReplay stormReplay);
        Uri GetMatchLink(StormReplay stormReplay);
        Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId);
        Task<string> CreateReplaySessionAsync(HeroesProfileTwitchPayload payload);
        Task<bool> CreatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId);
        Task<bool> UpdatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId);
        Task<bool> UpdateReplayDataAsync(HeroesProfileTwitchPayload payload, string sessionId);
        Task<bool> UpdatePlayerTalentsAsync(List<HeroesProfileTwitchPayload> lists, string sessionId);
        Task<bool> NotifyTwitchAsync();
    }
}