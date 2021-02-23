﻿using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface ISessionCreator
    {
        Task CreateAsync(StormReplay stormReplay);
    }
}