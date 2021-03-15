using System.Threading.Tasks;

namespace HeroesReplay.Service.Twitch.Core.Bot
{
    public interface ITwitchBot
    {
        Task StartAsync();
        Task StopAsync();
    }
}