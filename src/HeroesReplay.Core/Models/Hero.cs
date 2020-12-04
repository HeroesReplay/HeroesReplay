namespace HeroesReplay.Core.Shared
{
    public class Hero
    {
        public string Name { get; }
        public string AltName { get; }
        public HeroType HeroType { get; }

        public Hero(string name, string altName, HeroType heroType)
        {
            Name = name;
            AltName = altName;
            HeroType = heroType;
        }

        public bool IsMelee => HeroType == HeroType.Melee;
        public bool IsRanged => HeroType == HeroType.Ranged;
    }
}