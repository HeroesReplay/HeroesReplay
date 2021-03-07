using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Context
{
    public class ReplayContext : IReplayContext
    {
        public ContextData Previous { get; set; }
        public ContextData Current { get; set; }

    }
}