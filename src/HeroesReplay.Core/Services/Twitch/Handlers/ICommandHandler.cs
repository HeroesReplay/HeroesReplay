using TwitchLib.Client.Events;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public interface ICommandHandler
    {
        bool CanHandle(ChatMessage message);
        void Execute(ChatMessage message);
    }
}
