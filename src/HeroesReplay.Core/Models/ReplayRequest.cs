namespace HeroesReplay.Core.Models
{
    public class ReplayRequest
    {
        public string Login { get; set; }
        public int? ReplayId { get; set; }
        public bool? Bronze { get; set; }
        public bool? Silver { get; set; }
        public bool? Gold { get; set; }
        public bool? Platinum { get; set; }
        public bool? Diamond { get; set; }
        public bool? Master { get; set; }
    }
}
