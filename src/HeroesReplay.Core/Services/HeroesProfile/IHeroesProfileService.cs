using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IHeroesProfileService
    {
        Task<string> GetMMRTier(StormReplay stormReplay);
        Task<Uri> GetMatchLink(StormReplay stormReplay);
        Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId);
    }
}