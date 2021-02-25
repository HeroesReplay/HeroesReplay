namespace HeroesReplay.Core.Models
{
    public class Map
    {
        public string Name { get; }
        public string AltName { get; }
        public bool RankedRotation { get; }
        public string Type { get; }
        public bool Playable { get; }

        public Map(string name, string altName, bool rankedRotation, string type, bool playable)
        {
            Playable = playable;
            Type = type;
            RankedRotation = rankedRotation;
            Name = name;
            AltName = altName;
        }
    }
}
