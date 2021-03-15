using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Service.Twitch.Core.ChatMessages;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Events;

namespace HeroesReplay.Service.Twitch.Core.RedeemedRewards
{
    public class OnMessageReceivedHandler : IOnMessageHandler
    {
        private readonly ILogger<OnMessageReceivedHandler> logger;
        private readonly IEnumerable<IMessageHandler> handlers;

        public OnMessageReceivedHandler(ILogger<OnMessageReceivedHandler> logger, IEnumerable<IMessageHandler> handlers)
        {
            this.logger = logger;
            this.handlers = handlers;
        }

        public void Handle(OnMessageReceivedArgs args)
        {
            foreach (IMessageHandler handler in handlers)
            {
                try
                {
                    if (handler.CanHandle(args.ChatMessage))
                    {
                        Task.Run(() => handler.Execute(args.ChatMessage));
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not handle {args.ChatMessage.Message} with {handler.GetType().Name}");
                }
            }
        }
    }
}
