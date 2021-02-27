using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Models
{
    public class LogEvents
    {
        public EventId QueueItem = new EventId(1002, nameof(TwitchClient));
        public EventId GameController = new EventId(1000);
        public EventId CaptureStrategy = new EventId(1000, nameof(CaptureStrategy));
        public EventId HPApi = new EventId(2000, nameof(HPApi));
        public EventId HPTwitchApi = new EventId(3000, nameof(HPTwitchApi));
        public EventId TwitchBot = new EventId(3000, nameof(TwitchBot));
        public EventId TwitchClient = new EventId(3100, nameof(TwitchClient));
        public EventId TwitchPubSub = new EventId(3200, nameof(TwitchPubSub));
        public EventId Rewards = new EventId(3200, nameof(Rewards));
        public EventId Commands = new EventId(3200, nameof(Commands));
        public EventId Analyzers = new EventId(3200, nameof(Analyzers));
        public EventId Obs = new EventId(3200, nameof(Obs));
    }
}