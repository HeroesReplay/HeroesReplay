using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class ReplayData
    {
        public int ReplayId { get; init; }
        public int AverageRating { get; init; }
        public string Tier { get; init; }
        public string Map { get; init; }
    }
}