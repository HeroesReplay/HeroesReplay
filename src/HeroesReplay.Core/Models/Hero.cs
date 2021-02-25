namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class Hero
    {
        public string Name { get; }
        public string UnitId { get; }
        public string HyperlinkId { get; }

        public Hero(string name, string unitId, string hyperLinkId)
        {
            Name = name;
            UnitId = unitId;
            HyperlinkId = hyperLinkId;
        }
    }
}