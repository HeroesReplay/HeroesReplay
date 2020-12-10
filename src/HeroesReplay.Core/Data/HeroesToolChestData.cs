using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
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
    public class HeroesToolChestData : IHeroesToolChestData
    {
        private readonly ILogger<HeroesToolChestData> logger;
        private readonly Settings settings;

        private IDictionary<string, UnitGroup> unitGroups;

        public HeroesToolChestData(ILogger<HeroesToolChestData> logger, Settings settings)
        {
            this.logger = logger;
            this.settings = settings;
        }

        private async Task DownloadReleaseIfEmpty()
        {
            var downloadPath = settings.HeroesToolChest.HeroesDataPath;
            var exists = Directory.Exists(downloadPath);
            var release = settings.HeroesToolChest.HeroesDataReleaseUri;

            if (exists && Directory.EnumerateFiles(downloadPath, "*.json", SearchOption.AllDirectories).Any())
            {
                logger.LogInformation("Heroes Data exists. No need to download HeroesToolChest hero-data.");
            }
            else
            {
                logger.LogInformation($"heroes-data does not exists. Downloading files to: {downloadPath}");

                Directory.CreateDirectory(downloadPath);

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
                                using (var write = File.OpenWrite(Path.Combine(downloadPath, name)))
                                {
                                    await data.CopyToAsync(write);
                                    await write.FlushAsync();
                                }
                            }

                            using (var reader = File.OpenRead(Path.Combine(downloadPath, name)))
                            {
                                ZipArchive zip = new ZipArchive(reader);
                                zip.ExtractToDirectory(downloadPath);
                            }
                        }
                    }                    
                }
            }
        }

        public async Task LoadDataAsync()
        {
            await DownloadReleaseIfEmpty();

            var unitGroups = new Dictionary<string, UnitGroup>();
            var ignore = settings.HeroesToolChest.IgnoreUnits.ToList();

            var files = Directory
                .GetFiles(settings.HeroesToolChest.HeroesDataPath, "*.json", SearchOption.AllDirectories)
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

                            if (name.EndsWith("CaptureBeacon") || name.EndsWith("ControlBeacon"))
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

                            if (attributes.Contains("ImmuneToAOE") && attributes.Contains("ImmuneToFriendlyAbilities") && attributes.Contains("ImmuneToSkillshots") && attributes.Contains("NoMinionAggro"))
                            {
                                var character = unitGroups.FirstOrDefault(c => o.Name.Contains(c.Key));

                                if (character.Key != null)
                                {
                                    unitGroups[name] = name.Contains("Talent") ? UnitGroup.HeroTalentSelection : UnitGroup.HeroAbilityUse;
                                }
                                else
                                {
                                    var found = unitGroups.ContainsKey(name);

                                    if (!found)
                                        unitGroups[name] = UnitGroup.Miscellaneous;
                                }
                            }
                        }
                    }
                }
            }

            this.unitGroups = unitGroups;
        }

        public UnitGroup GetUnitGroup(Unit unit)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));

            return unitGroups.ContainsKey(unit.Name) ? unitGroups[unit.Name] : UnitGroup.Unknown;
        }
    }
}