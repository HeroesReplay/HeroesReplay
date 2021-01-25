using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface IReplayDetailsWriter
    {
        Task ClearDetailsAsync();
        Task WriteDetailsAsync(StormReplay replay);
    }
}