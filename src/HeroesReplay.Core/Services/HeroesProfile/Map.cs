namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class Map
    {
        public string Name { get; }
        public string AltName { get; }

        public Map(string name, string altName)
        {
            this.Name = name;
            this.AltName = altName;
        }
    }
}
