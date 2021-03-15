using TwitchLib.Client.Models;

namespace HeroesReplay.Service.Twitch.Core.ChatMessages
{
    public interface IMessageHandler
    {
        bool CanHandle(ChatMessage chatMessage);
        void Execute(ChatMessage chatMessage);
    }
}
