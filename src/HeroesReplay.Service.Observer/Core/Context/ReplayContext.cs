using HeroesReplay.Core.Models;

namespace HeroesReplay.Service.Spectator.Core.Context
{
    public class ReplayContext : IReplayContext
    {
        public ContextData Previous { get; set; }
        public ContextData Current { get; set; }
    }
}