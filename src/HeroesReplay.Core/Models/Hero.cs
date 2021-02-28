namespace HeroesReplay.Core.Models
{
    public class Hero
    {
        public string Name { get; }
        public string UnitId { get; }
        public string HyperlinkId { get; }

        /// <summary>
        /// Used for the TeamBans
        /// </summary>
        public string AttributeId { get; }

        public Hero(string name, string unitId, string hyperLinkId, string attributeId)
        {
            Name = name;
            UnitId = unitId;
            HyperlinkId = hyperLinkId;
            AttributeId = attributeId;
        }
    }
}