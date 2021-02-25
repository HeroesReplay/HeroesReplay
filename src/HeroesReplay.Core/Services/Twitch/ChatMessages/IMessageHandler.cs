
using TwitchLib.Client.Events;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public interface IMessageHandler
    {
        bool CanHandle(ChatMessage chatMessage);
        void Execute(ChatMessage chatMessage);
    }
}
