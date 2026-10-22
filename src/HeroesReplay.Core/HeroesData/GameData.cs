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
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.Unit;
using ElementHero = Heroes.Element.Models.Hero;
using ElementRatings = Heroes.Element.Models.HeroRatings;
using ElementUnit = Heroes.Element.Models.Unit;

namespace HeroesReplay.Core.HeroesData;

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
        // The map catalog is settings only, so it is there before LoadDataAsync. The request
        // queue page names the map rewards from it when the queue is first opened.
        Maps = CatalogMaps(settings);
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
                        ReleaseDay(hero.ReleaseDate),
                        hero.IsMelee,
                        Ratings(hero.Ratings),
                        hero.HeroUnits == null
                            ? Array.Empty<string>()
                            : hero.HeroUnits.Keys.ToArray(),
                        HeroTalents.Read(hero)
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

    private static HeroRatings Ratings(ElementRatings ratings)
    {
        if (ratings == null)
        {
            return null;
        }

        return new HeroRatings(
            ratings.Damage,
            ratings.Survivability,
            ratings.Utility,
            ratings.Complexity
        );
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
            if (!InStaging(file) && IsHeroesData2Document(file))
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

            if (InStaging(file) || !IsHeroesData2Document(file))
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
        Maps = CatalogMaps(settings);
        return Task.CompletedTask;
    }

    private static ReadOnlyCollection<Map> CatalogMaps(AppSettings settings)
    {
        IEnumerable<MapDefinition> catalog = settings.Maps?.Catalog ?? Array.Empty<MapDefinition>();

        return new ReadOnlyCollection<Map>(
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
    }

    /// <summary>The file whose write time is the last check for a newer heroes-data2 release (#493).</summary>
    public const string RefreshStampFileName = "refresh-checked.txt";

    /// <summary>
    /// Held with no sharing while a process refreshes or loads heroes-data2 (#493): the handle is
    /// the process's own, so a crash releases it.
    /// </summary>
    public const string LockFileName = ".heroes-data.lock";

    /// <summary>A download is fetched and extracted under one of these; no reader looks inside.</summary>
    public const string StagingPrefix = ".staging-";

    /// <summary>Each downloaded build is moved in whole, by one rename, to <c>builds\&lt;build&gt;</c>.</summary>
    public const string BuildsFolderName = "builds";

    private static readonly TimeSpan LockWait = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The heroes-data2 lock, waiting up to <paramref name="wait"/>. Null when another process
    /// still holds it then.
    /// </summary>
    internal static async Task<FileStream> LockAsync(string path, TimeSpan wait)
    {
        Directory.CreateDirectory(path);
        string lockPath = Path.Combine(path, LockFileName);
        DateTimeOffset until = DateTimeOffset.UtcNow + wait;
        while (true)
        {
            FileStream held = DurableFile.TryLock(lockPath);
            if (held != null || DateTimeOffset.UtcNow >= until)
            {
                return held;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// True while a process refreshes or loads heroes-data2 in <paramref name="path"/>. The
    /// housekeeping pass leaves the folder alone then.
    /// </summary>
    public static bool IsBusy(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        using FileStream probe = DurableFile.TryLock(Path.Combine(path, LockFileName));
        return probe == null;
    }

    /// <summary>
    /// Downloads heroes-data2 when the folder has none. With data present, at most once per
    /// <c>HeroesToolChest:RefreshInterval</c> (off when zero) it asks for the latest release and
    /// downloads it when it is a newer live build than the local data (#493): the cache was never
    /// refreshed, so the stream PC still read 2.57.0.98304 data for 98348 replays, and a patch
    /// that adds a hero would leave it out. The caller holds the lock; a failed refresh keeps the
    /// data there is.
    /// </summary>
    private async Task RefreshAsync()
    {
        string path = settings.HeroesDataPath;
        bool present = HasHeroesData2(path);
        TimeSpan interval = settings.HeroesToolChest?.RefreshInterval ?? TimeSpan.Zero;
        if (present && !RefreshDue(path, interval, DateTimeOffset.UtcNow))
        {
            logger.LogDebug("heroes-data2 is already present. No download needed.");
            return;
        }

        if (!present)
        {
            logger.LogInformation(
                "heroes-data2 is not in {Path}. Downloading the latest HeroesToolChest/heroes-data2 release.",
                path
            );
            await DownloadLatestAsync(onlyNewerThan: null).ConfigureAwait(false);
            return;
        }

        try
        {
            int local = BuildNumber(NewestDocument(path, "herodata_*.json", false) ?? string.Empty);
            await DownloadLatestAsync(local).ConfigureAwait(false);
            File.WriteAllText(
                Path.Combine(path, RefreshStampFileName),
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            );
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or JsonException
                        or TaskCanceledException
                        or InvalidDataException
            )
        {
            logger.LogWarning(
                e,
                "Could not check for a newer heroes-data2 release. The data in {Path} stays in use.",
                path
            );
        }
    }

    /// <summary>True when the last refresh check is older than <paramref name="interval"/>; never while it is zero.</summary>
    internal static bool RefreshDue(string path, TimeSpan interval, DateTimeOffset utcNow)
    {
        if (interval <= TimeSpan.Zero)
        {
            return false;
        }

        var stamp = new FileInfo(Path.Combine(path, RefreshStampFileName));
        return !stamp.Exists || utcNow - stamp.LastWriteTimeUtc >= interval;
    }

    /// <summary>
    /// The build of a heroes-data2 release tag (<c>v2.57.0.98348</c> is 98348), or -1 for a PTR
    /// build or a tag that is not one: only a live build replaces the local data.
    /// </summary>
    internal static int ReleaseBuild(string tag)
    {
        if (
            string.IsNullOrWhiteSpace(tag)
            || tag.Contains("ptr", StringComparison.OrdinalIgnoreCase)
        )
        {
            return -1;
        }

        string last = tag.Trim().Split('.')[^1];
        return int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out int build)
            ? build
            : -1;
    }

    /// <summary>
    /// Moves an extracted build into <c>builds\&lt;build&gt;</c> by one rename, so a reader sees
    /// all of its files or none (#493). A build already there is kept as it is.
    /// </summary>
    internal static string Publish(string path, string extracted, int build)
    {
        string builds = Path.Combine(path, BuildsFolderName);
        Directory.CreateDirectory(builds);
        string target = Path.Combine(
            builds,
            build > 0
                ? build.ToString(CultureInfo.InvariantCulture)
                : "unknown-" + Guid.NewGuid().ToString("N")
        );
        if (!Directory.Exists(target))
        {
            Directory.Move(extracted, target);
        }

        return target;
    }

    private static bool InStaging(string file) =>
        file.Contains(
            Path.DirectorySeparatorChar + StagingPrefix,
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// Downloads and extracts the latest release, or with <paramref name="onlyNewerThan"/> only a
    /// live build newer than that one. The zip is fetched and extracted under a staging folder,
    /// then the build is published in whole (<see cref="Publish"/>) and the staging folder goes.
    /// </summary>
    private async Task DownloadLatestAsync(int? onlyNewerThan)
    {
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
                    if (onlyNewerThan is int local)
                    {
                        string tag =
                            document.RootElement.TryGetProperty("tag_name", out JsonElement name)
                            && name.ValueKind == JsonValueKind.String
                                ? name.GetString()
                                : null;
                        int remote = ReleaseBuild(tag);
                        if (remote <= local)
                        {
                            logger.LogDebug(
                                "heroes-data2 {Tag} is not a newer live build than the local {Local}.",
                                tag,
                                local
                            );
                            return;
                        }

                        logger.LogInformation(
                            "heroes-data2 {Tag} is newer than the local data ({Local}). Downloading it; the housekeeping pass removes the older build's files.",
                            tag,
                            local
                        );
                    }

                    if (!TrySelectArchive(document.RootElement, out HeroesDataArchive archive))
                    {
                        throw new InvalidOperationException(
                            "The heroes-data2 release did not include a data zip."
                        );
                    }

                    string staging = Path.Combine(
                        settings.HeroesDataPath,
                        StagingPrefix + Guid.NewGuid().ToString("N")
                    );
                    try
                    {
                        Directory.CreateDirectory(staging);
                        string zipPath = Path.Combine(staging, archive.FileName);
                        using (
                            Stream data = await client
                                .GetStreamAsync(archive.Uri)
                                .ConfigureAwait(false)
                        )
                        using (FileStream write = File.Create(zipPath))
                        {
                            await data.CopyToAsync(write).ConfigureAwait(false);
                            logger.LogInformation("Saving heroes-data2 {File}.", archive.FileName);
                        }

                        string extracted = Path.Combine(staging, "build");
                        using (FileStream reader = File.OpenRead(zipPath))
                        using (ZipArchive zip = new ZipArchive(reader))
                        {
                            logger.LogInformation("Extracting heroes-data2...");
                            zip.ExtractToDirectory(extracted, true);
                        }

                        string published = Publish(
                            settings.HeroesDataPath,
                            extracted,
                            BuildNumber(archive.FileName)
                        );
                        logger.LogInformation("heroes-data2 is in {Path}.", published);
                    }
                    finally
                    {
                        try
                        {
                            if (Directory.Exists(staging))
                            {
                                Directory.Delete(staging, recursive: true);
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            logger.LogWarning(
                                "Could not remove {Staging}: {Message} The housekeeping pass removes it.",
                                staging,
                                e.Message
                            );
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

    /// <summary>
    /// Refreshes heroes-data2 when due, then loads it into memory, holding the heroes-data2 lock
    /// throughout (#493). Only one process downloads at a time, so spectate, download and youtube
    /// never fetch the same release together (the new-patch race), and no process loads while
    /// another moves a build in. A role keeps what it loaded until it restarts: nothing reads the
    /// files again.
    /// </summary>
    public async Task LoadDataAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.data.load");
        using (
            FileStream held = await LockAsync(settings.HeroesDataPath, LockWait)
                .ConfigureAwait(false)
        )
        {
            if (held == null)
            {
                logger.LogWarning(
                    "Another process held heroes-data2 for {Wait}. This one loads the data there is and does not refresh it.",
                    LockWait
                );
            }
            else
            {
                await RefreshAsync().ConfigureAwait(false);
            }

            await LoadUnitsAsync().ConfigureAwait(false);
            await LoadMapsAsync().ConfigureAwait(false);
            await LoadHeroesAsync().ConfigureAwait(false);
        }

        activity?.SetTag("data.heroes", Heroes?.Count ?? 0);
        activity?.SetTag("data.maps", Maps?.Count ?? 0);
    }
}
