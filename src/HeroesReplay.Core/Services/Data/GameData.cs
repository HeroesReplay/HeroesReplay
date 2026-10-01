using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Heroes.Element;
using Heroes.LocaleText;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.Unit;
using ElementHero = Heroes.Element.Models.Hero;
using ElementUnit = Heroes.Element.Models.Unit;

namespace HeroesReplay.Core.Services.Data;

public class GameData : IGameData
{
    private const string ObjectNameSeperator = "-";

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
        string file = NewestDocument(settings.HeroesDataPath, "herodata_*.json", false);
        if (file == null)
        {
            throw new FileNotFoundException(
                "heroes-data2 herodata JSON was not found.",
                settings.HeroesDataPath
            );
        }

        string gamestringsPath = NewestDocument(
            settings.HeroesDataPath,
            "gamestrings_*_enus.json",
            true
        );
        GameStringsDocument gamestrings = null;
        if (gamestringsPath == null)
        {
            logger.LogWarning(
                "No English heroes-data2 gamestrings file found. Hero roles are unavailable."
            );
        }
        else
        {
            gamestrings = GameStringsDocument.Load(
                JsonDocument.Parse(
                    await File.ReadAllTextAsync(gamestringsPath).ConfigureAwait(false)
                )
            );
        }

        using (gamestrings)
        using (
            HeroDataDocument catalog = HeroDataDocument.Load(
                JsonDocument.Parse(await File.ReadAllTextAsync(file).ConfigureAwait(false)),
                gamestrings
            )
        )
        {
            var heroes = new List<Hero>();
            foreach (ElementHero hero in catalog.GetElements())
            {
                heroes.Add(
                    new Hero(
                        Plain(hero.Name) ?? hero.Id,
                        hero.UnitId,
                        hero.HyperlinkId,
                        hero.AttributeId,
                        hero.HeroPlayStyles == null
                            ? Array.Empty<string>()
                            : hero.HeroPlayStyles.ToArray(),
                        Plain(hero.ExpandedRole),
                        ReleaseDay(hero.ReleaseDate)
                    )
                );
            }

            Heroes = new ReadOnlyCollection<Hero>(heroes);
            logger.LogInformation(
                "Loaded {Count} heroes from heroes-data2 file {File}.",
                heroes.Count,
                file
            );
        }
    }

    internal static DateTime? ReadReleaseDate(JsonElement hero)
    {
        if (
            !hero.TryGetProperty("releaseDate", out JsonElement value)
            || value.ValueKind != JsonValueKind.String
        )
        {
            return null;
        }

        string text = value.GetString();
        if (
            string.IsNullOrWhiteSpace(text)
            || !DateTime.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime day
            )
        )
        {
            return null;
        }

        return DateTime.SpecifyKind(day, DateTimeKind.Utc);
    }

    private static DateTime? ReleaseDay(DateOnly? day)
    {
        if (day == null)
        {
            return null;
        }

        return DateTime.SpecifyKind(day.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    }

    private static string Plain(GameStringText text)
    {
        if (text == null)
        {
            return null;
        }

        string value = text.PlainText;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static int BuildNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        int value = 0;
        int build = 0;
        bool inNumber = false;
        foreach (char c in name)
        {
            if (c >= '0' && c <= '9')
            {
                inNumber = true;
                value = (value * 10) + (c - '0');
            }
            else if (inNumber)
            {
                build = value;
                value = 0;
                inNumber = false;
            }
        }

        return inNumber ? value : build;
    }

    internal readonly struct HeroesDataArchive
    {
        public HeroesDataArchive(string fileName, Uri uri)
        {
            FileName = fileName;
            Uri = uri;
        }

        public string FileName { get; }
        public Uri Uri { get; }
    }

    internal static bool TrySelectArchive(JsonElement release, out HeroesDataArchive archive)
    {
        archive = default;
        HeroesDataArchive? noMaps = null;
        HeroesDataArchive? full = null;
        if (
            release.ValueKind == JsonValueKind.Object
            && release.TryGetProperty("assets", out JsonElement assets)
            && assets.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (
                    !asset.TryGetProperty("name", out JsonElement nameElement)
                    || nameElement.ValueKind != JsonValueKind.String
                    || !asset.TryGetProperty("browser_download_url", out JsonElement urlElement)
                    || urlElement.ValueKind != JsonValueKind.String
                )
                {
                    continue;
                }

                string name = nameElement.GetString();
                string url = urlElement.GetString();
                if (
                    string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(url)
                    || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                var candidate = new HeroesDataArchive(name, new Uri(url));
                if (name.Contains("heroes-data-no-maps-", StringComparison.OrdinalIgnoreCase))
                {
                    noMaps = candidate;
                }
                else if (name.StartsWith("heroes-data-", StringComparison.OrdinalIgnoreCase))
                {
                    full = candidate;
                }
            }
        }

        if (noMaps.HasValue)
        {
            archive = noMaps.Value;
            return true;
        }

        if (full.HasValue)
        {
            archive = full.Value;
            return true;
        }

        if (
            release.ValueKind == JsonValueKind.Object
            && release.TryGetProperty("name", out JsonElement releaseName)
            && releaseName.ValueKind == JsonValueKind.String
            && release.TryGetProperty("zipball_url", out JsonElement zipball)
            && zipball.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(releaseName.GetString())
            && !string.IsNullOrWhiteSpace(zipball.GetString())
        )
        {
            archive = new HeroesDataArchive(releaseName.GetString(), new Uri(zipball.GetString()));
            return true;
        }

        return false;
    }

    internal static bool HasHeroesData2(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        foreach (
            string file in Directory.EnumerateFiles(
                path,
                "herodata_*.json",
                SearchOption.AllDirectories
            )
        )
        {
            if (IsHeroesData2Document(file))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsHeroesData2Document(string path)
    {
        try
        {
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] buffer = new byte[512];
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    return false;
                }

                string head = Encoding.UTF8.GetString(buffer, 0, read);
                return head.Contains("\"itemsType\"", StringComparison.Ordinal);
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NewestDocument(string root, string pattern, bool skipMapStrings)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        string best = null;
        int bestBuild = -1;
        foreach (string file in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (skipMapStrings && name.Contains("mapdata", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsHeroesData2Document(file))
            {
                continue;
            }

            int build = BuildNumber(file);
            if (
                best == null
                || build > bestBuild
                || (
                    build == bestBuild
                    && string.Compare(file, best, StringComparison.OrdinalIgnoreCase) > 0
                )
            )
            {
                best = file;
                bestBuild = build;
            }
        }

        return best;
    }

    private static string ObjectName(string id)
    {
        if (id != null && id.Contains(ObjectNameSeperator))
        {
            return id.Split(ObjectNameSeperator)[1];
        }

        return id;
    }

    private static string MapToken(string id)
    {
        if (id != null && id.Contains(ObjectNameSeperator))
        {
            return id.Split(ObjectNameSeperator)[0];
        }

        return string.Empty;
    }

    private Task LoadMapsAsync()
    {
        IEnumerable<MapDefinition> catalog = settings.Maps?.Catalog ?? Array.Empty<MapDefinition>();

        Maps = new ReadOnlyCollection<Map>(
            catalog
                .Select(item => new Map(
                    item.Name,
                    item.ShortName,
                    item.RankedRotation,
                    item.Type,
                    item.Playable
                ))
                .ToList()
        );

        return Task.CompletedTask;
    }

    private async Task DownloadIfEmptyAsync()
    {
        logger.LogInformation("Downloading heroes-data2 if needed.");

        if (HasHeroesData2(settings.HeroesDataPath))
        {
            logger.LogDebug("heroes-data2 is already present. No download needed.");
            return;
        }

        logger.LogInformation(
            "heroes-data2 is not in {Path}. Downloading the latest HeroesToolChest/heroes-data2 release.",
            settings.HeroesDataPath
        );
        Directory.CreateDirectory(settings.HeroesDataPath);

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("HeroesReplay", "1.0")
            );

            if (
                settings.Github != null
                && !string.IsNullOrWhiteSpace(settings.Github.User)
                && !string.IsNullOrWhiteSpace(settings.Github.AccessToken)
            )
            {
                var base64 = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{settings.Github.User}:{settings.Github.AccessToken}")
                );
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    base64
                );
            }

            Uri release = settings.HeroesToolChest.HeroesDataReleaseUri;
            using (
                HttpResponseMessage response = await client.GetAsync(release).ConfigureAwait(false)
            )
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    if (!TrySelectArchive(document.RootElement, out HeroesDataArchive archive))
                    {
                        throw new InvalidOperationException(
                            "The heroes-data2 release did not include a data zip."
                        );
                    }

                    string zipPath = Path.Combine(settings.HeroesDataPath, archive.FileName);
                    using (
                        Stream data = await client.GetStreamAsync(archive.Uri).ConfigureAwait(false)
                    )
                    using (FileStream write = File.Create(zipPath))
                    {
                        await data.CopyToAsync(write).ConfigureAwait(false);
                        logger.LogInformation("Saving heroes-data2 {File}.", archive.FileName);
                    }

                    using (FileStream reader = File.OpenRead(zipPath))
                    using (ZipArchive zip = new ZipArchive(reader))
                    {
                        logger.LogInformation("Extracting heroes-data2...");
                        zip.ExtractToDirectory(settings.HeroesDataPath, true);
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

        string heroFile = NewestDocument(settings.HeroesDataPath, "herodata_*.json", false);
        if (heroFile != null)
        {
            using (
                HeroDataDocument heroes = HeroDataDocument.Load(
                    JsonDocument.Parse(await File.ReadAllTextAsync(heroFile).ConfigureAwait(false))
                )
            )
            {
                foreach (ElementHero hero in heroes.GetElements())
                {
                    RecordScaling(hero.Id, hero.ScalingLinkIds, coreUnits, vehicleUnits);
                    if (!string.IsNullOrWhiteSpace(hero.UnitId))
                    {
                        unitGroups[hero.UnitId] = UnitGroup.Hero;
                    }

                    if (hero.HeroUnits != null)
                    {
                        foreach (string heroUnitId in hero.HeroUnits.Keys)
                        {
                            unitGroups[heroUnitId] = UnitGroup.Hero;
                        }
                    }
                }
            }
        }

        string unitFile = NewestDocument(settings.HeroesDataPath, "unitdata_*.json", false);
        if (unitFile != null)
        {
            using (
                UnitDataDocument units = UnitDataDocument.Load(
                    JsonDocument.Parse(await File.ReadAllTextAsync(unitFile).ConfigureAwait(false))
                )
            )
            {
                foreach (ElementUnit unit in units.GetElements())
                {
                    RecordScaling(unit.Id, unit.ScalingLinkIds, coreUnits, vehicleUnits);
                    ClassifyUnit(
                        unit.Id,
                        unit.Attributes,
                        unit.HeroPlayStyles,
                        ignoreUnits,
                        unitGroups,
                        bossUnits,
                        vehicleUnits
                    );
                }
            }
        }

        UnitGroups = new ReadOnlyDictionary<string, UnitGroup>(unitGroups);
        BossUnits = new ReadOnlyCollection<string>(bossUnits.ToList());
        CoreUnits = new ReadOnlyCollection<string>(coreUnits.ToList());
        VehicleUnits = new ReadOnlyCollection<string>(vehicleUnits.ToList());
    }

    private void RecordScaling(
        string id,
        IEnumerable<string> links,
        HashSet<string> coreUnits,
        HashSet<string> vehicleUnits
    )
    {
        if (links == null)
        {
            return;
        }

        string name = ObjectName(id);
        bool core = false;
        bool vehicle = false;
        foreach (string link in links)
        {
            if (
                !string.IsNullOrWhiteSpace(settings.HeroesToolChest.CoreScalingLinkId)
                && string.Equals(
                    link,
                    settings.HeroesToolChest.CoreScalingLinkId,
                    StringComparison.Ordinal
                )
            )
            {
                core = true;
            }
            else if (
                settings.HeroesToolChest.VehicleScalingLinkIds != null
                && settings.HeroesToolChest.VehicleScalingLinkIds.Contains(link)
            )
            {
                vehicle = true;
            }
        }

        if (core)
        {
            coreUnits.Add(name);
        }
        else if (vehicle)
        {
            vehicleUnits.Add(name);
        }
    }

    private void ClassifyUnit(
        string fullId,
        ICollection<string> attributes,
        ICollection<string> descriptors,
        List<string> ignoreUnits,
        Dictionary<string, UnitGroup> unitGroups,
        HashSet<string> bossUnits,
        HashSet<string> vehicleUnits
    )
    {
        attributes = attributes ?? (ICollection<string>)Array.Empty<string>();
        descriptors = descriptors ?? (ICollection<string>)Array.Empty<string>();
        string name = ObjectName(fullId);
        string map = MapToken(fullId);

        if (
            !ignoreUnits.Any(i => name.Contains(i))
            && MatchesAny(name, settings.HeroesToolChest.BossContains)
        )
        {
            bossUnits.Add(name);
            unitGroups[name] = UnitGroup.MercenaryCamp;
            return;
        }

        if (
            !ignoreUnits.Any(i => name.Contains(i))
            && MatchesAny(name, settings.HeroesToolChest.CampContains)
        )
        {
            unitGroups[name] = UnitGroup.MercenaryCamp;
            return;
        }

        if (
            !ignoreUnits.Any(i => name.Contains(i))
            && MatchesAny(name, settings.HeroesToolChest.VehicleContains)
        )
        {
            vehicleUnits.Add(name);
            unitGroups[name] = UnitGroup.MapObjective;
            return;
        }

        if (
            attributes.Contains(AttributeMapBoss)
            && name.EndsWith(UnitNameDefender)
            && !ignoreUnits.Any(i => name.Contains(i))
        )
        {
            bossUnits.Add(name);
            unitGroups[name] = UnitGroup.MercenaryCamp;
            return;
        }

        if (
            attributes.Contains(AttributeMapBoss)
            && name.EndsWith(UnitNameLaner)
            && !ignoreUnits.Any(i => name.Contains(i))
        )
        {
            unitGroups[name] = UnitGroup.MercenaryCamp;
            return;
        }

        if (
            attributes.Count == 1
            && attributes.Contains(AttributeMerc)
            && !ignoreUnits.Any(i => name.Contains(i))
        )
        {
            unitGroups[name] = UnitGroup.MercenaryCamp;
            return;
        }

        if (settings.HeroesToolChest.ObjectiveContains.Any(unitName => name.Contains(unitName)))
        {
            unitGroups[name] = UnitGroup.MapObjective;
            return;
        }

        if (
            (attributes.Contains(AttributeMapCreature) || attributes.Contains(AttributeMapBoss))
            && !(name.EndsWith(UnitNameLaner) || name.EndsWith(UnitNameDefender))
            && !ignoreUnits.Any(i => name.Contains(i))
        )
        {
            unitGroups[name] = UnitGroup.MapObjective;
            return;
        }

        if (
            name.Contains(UnitNamePayload)
            && "hanamuradata".Contains(map)
            && !ignoreUnits.Any(i => name.Contains(i))
        )
        {
            unitGroups[name] = UnitGroup.MapObjective;
            return;
        }

        if (
            attributes.Contains(AttributeHeroic)
            && descriptors.Contains(DescriptorPowerfulLaner)
            && !ignoreUnits.Any(i => name.Contains(i))
        )
        {
            unitGroups[name] = UnitGroup.MapObjective;
            return;
        }

        if (attributes.Contains(AttributeStructure) && !ignoreUnits.Any(i => name.Contains(i)))
        {
            unitGroups[name] = UnitGroup.Structures;
            return;
        }

        if (
            attributes.Count == 1
            && attributes.Contains(AttributeMinion)
            && name.EndsWith(AttributeMinion)
            && !ignoreUnits.Any(i => name.Contains(i))
        )
        {
            unitGroups[name] = UnitGroup.Minions;
            return;
        }

        if (unitGroups.FirstOrDefault(c => fullId.Contains(c.Key)).Key != null)
        {
            unitGroups[name] = name.Contains(HeroicTalent)
                ? UnitGroup.HeroTalentSelection
                : UnitGroup.HeroAbilityUse;
            return;
        }

        if (!unitGroups.ContainsKey(name))
        {
            unitGroups[name] = UnitGroup.Miscellaneous;
        }
    }

    public UnitGroup GetUnitGroup(string name)
    {
        if (UnitGroups == null)
            throw new InvalidOperationException(
                "The data must be loaded before getting a unit group."
            );

        if (name == null)
            throw new ArgumentNullException(nameof(name));

        return UnitGroups.ContainsKey(name) ? UnitGroups[name] : UnitGroup.Unknown;
    }

    private static bool MatchesAny(string name, IEnumerable<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(name) || tokens == null)
        {
            return false;
        }

        foreach (string token in tokens)
        {
            if (
                !string.IsNullOrWhiteSpace(token)
                && name.Contains(token, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    public async Task LoadDataAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.data.load");
        await DownloadIfEmptyAsync().ConfigureAwait(false);
        await LoadUnitsAsync().ConfigureAwait(false);
        await LoadMapsAsync().ConfigureAwait(false);
        await LoadHeroesAsync().ConfigureAwait(false);
        activity?.SetTag("data.heroes", Heroes?.Count ?? 0);
        activity?.SetTag("data.maps", Maps?.Count ?? 0);
    }
}
