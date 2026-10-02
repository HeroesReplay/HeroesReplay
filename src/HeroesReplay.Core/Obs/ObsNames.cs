using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The OBS profile and scene collection HeroesReplay uses (<c>OBS:ProfileName</c>,
/// <c>OBS:SceneCollectionName</c>), and the files OBS keeps for them under
/// <c>%APPDATA%\obs-studio\basic</c>. The file and folder names are the configured names,
/// so a name must be file-safe (letters, digits, <c>-</c>, <c>_</c>).
/// </summary>
public static class ObsNames
{
    public const string Default = "HeroesReplay";

    private static readonly Regex ProfileNameLine = new(
        @"^Name=[^\r\n]*",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    );

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Profile(OBSSettings obs) => Pick(obs?.ProfileName);

    public static string SceneCollection(OBSSettings obs) => Pick(obs?.SceneCollectionName);

    public static string Pick(string name) =>
        string.IsNullOrWhiteSpace(name) ? Default : name.Trim();

    public static string ProfileIni(string appData, string profile) =>
        Path.Combine(Basic(appData), "profiles", Pick(profile), "basic.ini");

    public static string CollectionFile(string appData, string collection) =>
        Path.Combine(Basic(appData), "scenes", Pick(collection) + ".json");

    /// <summary>
    /// OBS lists a profile by <c>[General] Name</c> in its basic.ini, not by the folder name.
    /// </summary>
    public static string WithProfileName(string ini, string profile)
    {
        if (string.IsNullOrEmpty(ini))
        {
            return ini;
        }

        string name = Pick(profile);
        int general = ini.IndexOf("[General]", StringComparison.Ordinal);
        if (general < 0)
        {
            return "[General]\r\nName=" + name + "\r\n\r\n" + ini;
        }

        int end = ini.IndexOf("\n[", general, StringComparison.Ordinal);
        string section = end < 0 ? ini.Substring(general) : ini.Substring(general, end - general);
        string updated = ProfileNameLine.IsMatch(section)
            ? ProfileNameLine.Replace(section, "Name=" + name, 1)
            : section.Replace("[General]", "[General]\r\nName=" + name, StringComparison.Ordinal);
        return ini.Substring(0, general) + updated + (end < 0 ? "" : ini.Substring(end));
    }

    /// <summary>
    /// OBS lists a scene collection by its top-level <c>name</c>, not by the file name.
    /// The template is returned unchanged when it already carries the name.
    /// </summary>
    public static string WithCollectionName(string json, string collection)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        string name = Pick(collection);
        JsonNode root = JsonNode.Parse(json);
        if (root is not JsonObject document)
        {
            return json;
        }

        if (
            document["name"] is JsonValue current
            && current.TryGetValue(out string existing)
            && string.Equals(existing, name, StringComparison.Ordinal)
        )
        {
            return json;
        }

        document["name"] = name;
        return document.ToJsonString(Indented);
    }

    private static string Basic(string appData)
    {
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        return Path.Combine(appData, "obs-studio", "basic");
    }
}
