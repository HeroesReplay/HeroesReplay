namespace HeroesReplay.Spectator
{
    public class Hero
    {
        public string Name { get; }
        public HeroType HeroType { get; }

        public Hero(string name, HeroType heroType)
        {
            Name = name;
            HeroType = heroType;
        }

        public bool IsMelee => HeroType == HeroType.Melee;
        public bool IsRanged => HeroType == HeroType.Ranged;
    }
}