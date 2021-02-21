using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class ReplayData
    {
        public int ReplayId { get; init; }
        public int AverageMmr { get; init; }
        public string Tier { get; init; }
        public string Map { get; init; }
        public Dictionary<string, float> PlayerMmrs { get; init; }
    }
}