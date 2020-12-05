using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HeroesReplay.Core.Shared
{
    public class GameDataService
    {
        private readonly Settings settings;

        public List<Hero> Heroes { get; } = new List<Hero>();
        public List<Map> Maps { get; } = new List<Map>();

        public GameDataService(Settings settings)
        {
            this.settings = settings;
            this.Heroes = LoadHeroes();
            this.Maps = LoadMaps();         
        }

        private List<Map> LoadMaps()
        {
            using (FileStream mapStream = File.OpenRead(Path.Combine(settings.CurrentDirectory, "Assets", "Maps.json")))
            {
                using (JsonDocument mapJson = JsonDocument.Parse(mapStream))
                {
                    var maps = from map in mapJson.RootElement.EnumerateArray()
                               let name = map.GetProperty("name").GetString()
                               let altName = map.GetProperty("short_name").GetString()
                               select new Map(name, altName);

                    return maps.ToList();
                }
            }
        }

        private List<Hero> LoadHeroes()
        {
            using (FileStream heroStream = File.OpenRead(Path.Combine(settings.CurrentDirectory, "Assets", "Heroes.json")))
            {
                using (JsonDocument heroJson = JsonDocument.Parse(heroStream))
                {
                    var heroes = from hero in heroJson.RootElement.EnumerateObject()
                                 let name = hero.Value.GetProperty("name").GetString()
                                 let altName = hero.Value.GetProperty("alt_name").GetString()
                                 let type = (HeroType)Enum.Parse(typeof(HeroType), hero.Value.GetProperty("type").GetString())
                                 select new Hero(name, altName, type);

                    return heroes.ToList();
                }
            }
        }
    }
}
