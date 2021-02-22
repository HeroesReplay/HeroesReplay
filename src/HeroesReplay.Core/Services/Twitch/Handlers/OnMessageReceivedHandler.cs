using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;

using TwitchLib.Client.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public class OnMessageReceivedHandler : IOnMessageReceivedHandler
    {
        private readonly ILogger<OnMessageReceivedHandler> logger;
        private readonly IEnumerable<ICommandHandler> handlers;

        public OnMessageReceivedHandler(ILogger<OnMessageReceivedHandler> logger, IEnumerable<ICommandHandler> handlers)
        {
            this.logger = logger;
            this.handlers = handlers;
        }

        public void Handle(OnMessageReceivedArgs args)
        {
            foreach (var handler in handlers)
            {
                try
                {
                    if (handler.CanHandle(args.ChatMessage))
                    {
                        handler.Execute(args.ChatMessage);
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
