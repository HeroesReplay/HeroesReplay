using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Models
{
    public class ReplayRequest
    {
        public string Login { get; set; }
        public int? ReplayId { get; set; }
        public Tier? Tier { get; set; }
        public Map Map { get; set; }
        public GameMode? GameMode { get; set; }
    }
}
