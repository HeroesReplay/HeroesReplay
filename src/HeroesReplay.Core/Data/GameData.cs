using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Core.Runner
{
    public class GameData : IGameData
    {
        private readonly ILogger<GameData> logger;
        private readonly Settings settings;
        private readonly string heroesDataPath;

        public IReadOnlyDictionary<string, UnitGroup> UnitGroups { get; private set; }
        public IReadOnlyList<Map> Maps { get; private set; }
        public IReadOnlyList<Hero> Heroes { get; private set; }
        public IReadOnlyCollection<string> CoreNames { get; private set; }

        public GameData(ILogger<GameData> logger, Settings settings)
        {
            this.logger = logger;
            this.settings = settings;
            this.heroesDataPath = string.IsNullOrWhiteSpace(settings.HeroesToolChest.HeroesDataPath) ? Path.Combine(settings.AssetsPath, "HeroesData") : settings.HeroesToolChest.HeroesDataPath;
        }

        private async Task LoadHeroesAsync()
        {
            var json = await File.ReadAllTextAsync(Path.Combine(settings.AssetsPath, "Heroes.json"));

            using (JsonDocument heroJson = JsonDocument.Parse(json))
            {
                var heroes = from hero in heroJson.RootElement.EnumerateObject()
                             let name = hero.Value.GetProperty("name").GetString()
                             let altName = hero.Value.GetProperty("alt_name").GetString()
                             let type = (HeroType)Enum.Parse(typeof(HeroType), hero.Value.GetProperty("type").GetString())
                             select new Hero(name, altName, type);

                Heroes = new ReadOnlyCollection<Hero>(heroes.ToList());
            }
        }

        private async Task LoadMapsAsync()
        {
            var json = await File.ReadAllTextAsync(Path.Combine(settings.AssetsPath, "Maps.json"));

            using (var mapJson = JsonDocument.Parse(json))
            {
                Maps = new ReadOnlyCollection<Map>(
                        (from map in mapJson.RootElement.EnumerateArray()
                         let name = map.GetProperty("name").GetString()
                         let altName = map.GetProperty("short_name").GetString()
                         select new Map(name, altName)).ToList());
            }
        }

        private async Task DownloadReleaseIfEmpty()
        {
            var exists = Directory.Exists(heroesDataPath);
            var release = settings.HeroesToolChest.HeroesDataReleaseUri;

            if (exists && Directory.EnumerateFiles(heroesDataPath, "*.json", SearchOption.AllDirectories).Any())
            {
                logger.LogInformation("Heroes Data exists. No need to download HeroesToolChest hero-data.");
            }
            else
            {
                logger.LogInformation($"heroes-data does not exists. Downloading files to: {heroesDataPath}");

                if (!exists)
                    Directory.CreateDirectory(heroesDataPath);

                using (var client = new HttpClient())
                {
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Github.User}:{settings.Github.AccessToken}"));

                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HeroesReplay", "1.0"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);

                    var response = await client.GetAsync(release);
                    var json = await response.Content.ReadAsStringAsync();

                    using (var document = JsonDocument.Parse(json))
                    {
                        if (document.RootElement.TryGetProperty("name", out JsonElement commit) && document.RootElement.TryGetProperty("zipball_url", out JsonElement link))
                        {
                            var name = commit.GetString();
                            var uri = link.GetString();

                            using (var data = await client.GetStreamAsync(new Uri(uri)))
                            {
                                using (var write = File.OpenWrite(Path.Combine(heroesDataPath, name)))
                                {
                                    await data.CopyToAsync(write);
                                    await write.FlushAsync();
                                }
                            }

                            using (var reader = File.OpenRead(Path.Combine(heroesDataPath, name)))
                            {
                                ZipArchive zip = new ZipArchive(reader);
                                zip.ExtractToDirectory(heroesDataPath);
                            }
                        }
                    }
                }
            }
        }

        private async Task LoadCoreNamesAsync()
        {
            var names = new HashSet<string>();
            var files = Directory
                .GetFiles(heroesDataPath, "*.json", SearchOption.AllDirectories)
                .Where(x => x.Contains("unitdata_"));

            foreach (var file in files)
            {
                var json = await File.ReadAllTextAsync(file);

                using (var document = JsonDocument.Parse(json, new JsonDocumentOptions() { AllowTrailingCommas = true }))
                {
                    foreach (var o in document.RootElement.EnumerateObject())
                    {
                        if (o.Value.TryGetProperty("scalingLinkId", out JsonElement value) && settings.HeroesToolChest.ScalingLinkId.Equals(value.GetString()))
                        {
                            names.Add(o.Name.Contains("-") ? o.Name.Split('-')[1] : o.Name);
                        }
                    }
                }
            }

            this.CoreNames = new ReadOnlyCollection<string>(names.ToList());
        }

        private async Task LoadUnitGroupsAsync()
        {
            var unitGroups = new Dictionary<string, UnitGroup>();
            var ignore = settings.HeroesToolChest.IgnoreUnits.ToList();

            var files = Directory
                .GetFiles(heroesDataPath, "*.json", SearchOption.AllDirectories)
                .Where(x => x.Contains("herodata_") || x.Contains("unitdata_"))
                .OrderByDescending(x => x.Contains("herodata_"))
                .ThenBy(x => x.Contains("unitdata_"));

            foreach (var file in files)
            {
                using (var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions() { AllowTrailingCommas = true }))
                {
                    foreach (var o in document.RootElement.EnumerateObject())
                    {
                        if (o.Value.TryGetProperty("unitId", out var unitId))
                        {
                            var descriptors = new List<string>();

                            if (o.Value.TryGetProperty("descriptors", out var descriptorsElement))
                            {
                                descriptors.AddRange(descriptorsElement.EnumerateArray().Select(x => x.GetString()));
                            }

                            unitGroups[unitId.GetString()] = UnitGroup.Hero;

                            if (o.Value.TryGetProperty("heroUnits", out var heroUnits))
                            {
                                foreach (var hu in heroUnits.EnumerateArray())
                                {
                                    foreach (var huo in hu.EnumerateObject())
                                    {
                                        unitGroups[huo.Name] = UnitGroup.Hero;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var name = o.Name.Contains("-") ? o.Name.Split('-')[1] : o.Name;
                            var map = o.Name.Contains("-") ? o.Name.Split('-')[0] : string.Empty;

                            List<string> attributes = new List<string>();
                            List<string> descriptors = new List<string>();

                            if (o.Value.TryGetProperty("attributes", out var attributesElement))
                                attributes.AddRange(attributesElement.EnumerateArray().Select(x => x.GetString()));

                            if (o.Value.TryGetProperty("descriptors", out var descriptorsElement))
                                descriptors.AddRange(descriptorsElement.EnumerateArray().Select(x => x.GetString()));

                            if (attributes.Contains("MapBoss") && name.EndsWith("Defender") && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MercenaryCamp;
                                continue;
                            }

                            if (attributes.Contains("MapBoss") && name.EndsWith("Laner") && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MercenaryCamp;
                                continue;
                            }

                            if (attributes.Count == 1 && attributes.Contains("Merc") && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MercenaryCamp;
                                continue;
                            }

                            // Beacons
                            // Gems on Tomb
                            if (name.EndsWith("CaptureBeacon") || 
                                name.EndsWith("ControlBeacon") || 
                                name.StartsWith("ItemSoulPickup") || 
                                name.Equals("ItemCannonball") || // Doubloons
                                name.StartsWith("SoulCage") || 
                                name.Equals("DocksTreasureChest") || 
                                name.Equals("DocksPirateCaptain"))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if ((attributes.Contains("MapCreature") || attributes.Contains("MapBoss")) && !(name.EndsWith("Laner") || name.EndsWith("Defender")) && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if (name.Contains("Payload") && "hanamuradata".Contains(map) && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if (attributes.Contains("Heroic") && descriptors.Contains("PowerfulLaner") && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if (attributes.Contains("AITargetableStructure") && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.Structures;
                                continue;
                            }

                            if (attributes.Count == 1 && attributes.Contains("Minion") && name.EndsWith("Minion") && !ignore.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.Minions;
                                continue;
                            }

                            if (unitGroups.FirstOrDefault(c => o.Name.Contains(c.Key)).Key != null)
                            {
                                unitGroups[name] = name.Contains("Talent") ? UnitGroup.HeroTalentSelection : UnitGroup.HeroAbilityUse;
                                continue;
                            }

                            if (!unitGroups.ContainsKey(name))
                            {
                                unitGroups[name] = UnitGroup.Miscellaneous;
                                continue;
                            }
                        }
                    }
                }
            }

            this.UnitGroups = new ReadOnlyDictionary<string, UnitGroup>(unitGroups);
        }

        public UnitGroup GetUnitGroup(string name)
        {
            if (UnitGroups == null)
                throw new InvalidOperationException("The data must be loaded before getting a unit group.");

            if (name == null)
                throw new ArgumentNullException(nameof(name));

            return UnitGroups.ContainsKey(name) ? UnitGroups[name] : UnitGroup.Unknown;
        }

        public async Task LoadDataAsync()
        {
            await DownloadReleaseIfEmpty();
            await LoadUnitGroupsAsync();
            await LoadMapsAsync();
            await LoadHeroesAsync();
            await LoadCoreNamesAsync();
        }
    }
}