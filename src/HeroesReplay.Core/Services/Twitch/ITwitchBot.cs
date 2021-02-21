using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Twitch
{
    public interface ITwitchBot
    {
        Task InitializeAsync();
    }
}