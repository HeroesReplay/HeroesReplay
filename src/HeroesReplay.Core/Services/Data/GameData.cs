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
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Core.Services.Data
{
    public class GameData : IGameData
    {
        private const string ScalingLinkIdProperty = "scalingLinkId";
        private const string HeroUnitsProperty = "heroUnits";
        private const string UnitIdProperty = "unitId";
        private const string DescriptorsProperty = "descriptors";
        private const string AttributesProperty = "attributes";
        private const string ObjectNameSeperator = "-";

        private const string HeroData = "herodata_";
        private const string UnitData = "unitdata_";

        private const string AttributeMapBoss = "MapBoss";
        private const string AttributeMapCreature = "MapCreature";
        private const string AttributeMerc = "Merc";
        private const string AttributeStructure = "AITargetableStructure";
        private const string AttributeHeroic = "Heroic";
        private const string AttributeMinion = "Minion";

        private const string DescriptorPowerfulLaner = "PowerfulLaner";

        private const string UnitNameLaner = "Laner";
        private const string UnitNameDefender = "Defender";
        private const string UnitNamePayload = "Payload";

        private const string HeroicTalent = "Talent";

        private readonly ILogger<GameData> logger;
        private readonly AppSettings settings;

        public IReadOnlyDictionary<string, UnitGroup> UnitGroups { get; private set; }
        public IReadOnlyList<Map> Maps { get; private set; }
        public IReadOnlyList<Hero> Heroes { get; private set; }
        public IReadOnlyCollection<string> CoreUnits { get; private set; }
        public IReadOnlyCollection<string> BossUnits { get; private set; }
        public IReadOnlyCollection<string> VehicleUnits { get; private set; }

        public GameData(ILogger<GameData> logger, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        private async Task LoadHeroesAsync()
        {
            var file = Directory
               .GetFiles(settings.HeroesDataPath, "herodata_*.json", SearchOption.AllDirectories)
               .OrderByDescending(x => int.Parse(Path.GetFileName(x).Split('_')[1])).FirstOrDefault();

            var heroData = await File.ReadAllTextAsync(file);

            List<Hero> heroes = new List<Hero>();

            using (JsonDocument document = JsonDocument.Parse(heroData))
            {
                foreach (JsonProperty hero in document.RootElement.EnumerateObject())
                {
                    if (hero.Value.ValueKind == JsonValueKind.Object)
                    {
                        heroes.Add(new Hero(hero.Name, hero.Value.GetProperty("unitId").GetString(), hero.Value.GetProperty("hyperlinkId").GetString()));
                    }
                }
            }

            Heroes = new ReadOnlyCollection<Hero>(heroes);
        }

        private async Task LoadMapsAsync()
        {
            var json = await File.ReadAllTextAsync(Path.Combine(settings.AssetsPath, "Maps.json")).ConfigureAwait(false);

            using (var mapJson = JsonDocument.Parse(json))
            {
                Maps = new ReadOnlyCollection<Map>((from item in mapJson.RootElement.EnumerateArray()
                                                    select new Map(
                                                        name: item.GetProperty("name").GetString(),
                                                        altName: item.GetProperty("short_name").GetString(),
                                                        rankedRotation: item.GetProperty("ranked_rotation").GetInt32() == 1,
                                                        type: item.GetProperty("type").GetString(),
                                                        playable: item.GetProperty("playable").GetInt32() == 1)).ToList());
            }
        }

        private async Task DownloadIfEmptyAsync()
        {
            logger.LogInformation("Downloading heroes-data if needed.");

            var release = settings.HeroesToolChest.HeroesDataReleaseUri;

            if (Directory.Exists(settings.HeroesDataPath) && Directory.EnumerateFiles(settings.HeroesDataPath, "*.json", SearchOption.AllDirectories).Any())
            {
                logger.LogDebug("Heroes Data exists. No need to download HeroesToolChest hero-data.");
            }
            else
            {
                logger.LogDebug($"heroes-data does not exists. Downloading files to: {settings.HeroesDataPath}");

                Directory.CreateDirectory(settings.HeroesDataPath);

                using (var client = new HttpClient())
                {
                    var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Github.User}:{settings.Github.AccessToken}"));

                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HeroesReplay", "1.0"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);

                    var response = await client.GetAsync(release).ConfigureAwait(false);
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    using (var document = JsonDocument.Parse(json))
                    {
                        if (document.RootElement.TryGetProperty("name", out JsonElement commit) && document.RootElement.TryGetProperty("zipball_url", out JsonElement link))
                        {
                            var name = commit.GetString();
                            var uri = link.GetString();

                            using (var data = await client.GetStreamAsync(new Uri(uri)).ConfigureAwait(false))
                            {
                                using (var write = File.OpenWrite(Path.Combine(settings.HeroesDataPath, name)))
                                {
                                    await data.CopyToAsync(write).ConfigureAwait(false);
                                    logger.LogInformation("Saving heroes-data...");
                                    await write.FlushAsync().ConfigureAwait(false);
                                }
                            }

                            using (var reader = File.OpenRead(Path.Combine(settings.HeroesDataPath, name)))
                            {
                                using (ZipArchive zip = new ZipArchive(reader))
                                {
                                    logger.LogInformation("Extracting heroes-data...");
                                    zip.ExtractToDirectory(settings.HeroesDataPath);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This method is used because we CANNOT rely on the UnitGroups inside Heroes.ReplayParser.
        /// </summary>
        private async Task LoadUnitsAsync()
        {
            var unitGroups = new Dictionary<string, UnitGroup>();
            var ignoreUnits = settings.HeroesToolChest.IgnoreUnits.ToList();
            var bossUnits = new HashSet<string>();
            var coreUnits = new HashSet<string>();
            var vehicleUnits = new HashSet<string>();

            var files = Directory
                .GetFiles(settings.HeroesDataPath, "*.json", SearchOption.AllDirectories)
                .Where(x => x.Contains(HeroData) || x.Contains(UnitData))
                .OrderByDescending(x => x.Contains(HeroData))
                .ThenBy(x => x.Contains(UnitData));

            foreach (var file in files)
            {
                using (var document = JsonDocument.Parse(await File.ReadAllTextAsync(file).ConfigureAwait(false), new JsonDocumentOptions() { AllowTrailingCommas = true }))
                {
                    foreach (var o in document.RootElement.EnumerateObject())
                    {
                        if (o.Value.TryGetProperty(ScalingLinkIdProperty, out JsonElement core) && settings.HeroesToolChest.CoreScalingLinkId.Equals(core.GetString()))
                        {
                            coreUnits.Add(o.Name.Contains(ObjectNameSeperator) ? o.Name.Split(ObjectNameSeperator)[1] : o.Name);
                        }
                        else if (o.Value.TryGetProperty(ScalingLinkIdProperty, out JsonElement vehicle) && settings.HeroesToolChest.VehicleScalingLinkIds.Contains(vehicle.GetString()))
                        {
                            vehicleUnits.Add(o.Name.Contains(ObjectNameSeperator) ? o.Name.Split(ObjectNameSeperator)[1] : o.Name);
                        }

                        if (o.Value.TryGetProperty(UnitIdProperty, out var unitId))
                        {
                            var descriptors = new List<string>();

                            if (o.Value.TryGetProperty(DescriptorsProperty, out var descriptorsElement))
                            {
                                descriptors.AddRange(descriptorsElement.EnumerateArray().Select(x => x.GetString()));
                            }

                            unitGroups[unitId.GetString()] = UnitGroup.Hero;

                            if (o.Value.TryGetProperty(HeroUnitsProperty, out var heroUnits))
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
                            var name = o.Name.Contains(ObjectNameSeperator) ? o.Name.Split(ObjectNameSeperator)[1] : o.Name;
                            var map = o.Name.Contains(ObjectNameSeperator) ? o.Name.Split(ObjectNameSeperator)[0] : string.Empty;

                            List<string> attributes = new List<string>();
                            List<string> descriptors = new List<string>();

                            if (o.Value.TryGetProperty(AttributesProperty, out var attributesElement))
                                attributes.AddRange(attributesElement.EnumerateArray().Select(x => x.GetString()));

                            if (o.Value.TryGetProperty(DescriptorsProperty, out var descriptorsElement))
                                descriptors.AddRange(descriptorsElement.EnumerateArray().Select(x => x.GetString()));

                            if (attributes.Contains(AttributeMapBoss) && name.EndsWith(UnitNameDefender) && !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                bossUnits.Add(name);
                                unitGroups[name] = UnitGroup.MercenaryCamp;
                                continue;
                            }

                            if (attributes.Contains(AttributeMapBoss) && name.EndsWith(UnitNameLaner) && !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MercenaryCamp;
                                continue;
                            }

                            if (attributes.Count == 1 && attributes.Contains(AttributeMerc) && !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MercenaryCamp;
                                continue;
                            }

                            if (settings.HeroesToolChest.ObjectiveContains.Any(unitName => name.Contains(unitName)))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if ((attributes.Contains(AttributeMapCreature) || attributes.Contains(AttributeMapBoss)) &&
                                                                           !(name.EndsWith(UnitNameLaner) || name.EndsWith(UnitNameDefender)) &&
                                                                           !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if (name.Contains(UnitNamePayload) && "hanamuradata".Contains(map) && !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if (attributes.Contains(AttributeHeroic) && descriptors.Contains(DescriptorPowerfulLaner) && !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.MapObjective;
                                continue;
                            }

                            if (attributes.Contains(AttributeStructure) && !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.Structures;
                                continue;
                            }

                            if (attributes.Count == 1 && attributes.Contains(AttributeMinion) && name.EndsWith(AttributeMinion) && !ignoreUnits.Any(i => name.Contains(i)))
                            {
                                unitGroups[name] = UnitGroup.Minions;
                                continue;
                            }

                            if (unitGroups.FirstOrDefault(c => o.Name.Contains(c.Key)).Key != null)
                            {
                                unitGroups[name] = name.Contains(HeroicTalent) ? UnitGroup.HeroTalentSelection : UnitGroup.HeroAbilityUse;
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

            UnitGroups = new ReadOnlyDictionary<string, UnitGroup>(unitGroups);
            BossUnits = new ReadOnlyCollection<string>(bossUnits.ToList());
            CoreUnits = new ReadOnlyCollection<string>(coreUnits.ToList());
            VehicleUnits = new ReadOnlyCollection<string>(vehicleUnits.ToList());
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
            await DownloadIfEmptyAsync().ConfigureAwait(false);
            await LoadUnitsAsync().ConfigureAwait(false);
            await LoadMapsAsync().ConfigureAwait(false);
            await LoadHeroesAsync().ConfigureAwait(false);
        }
    }
}