using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Obs.Collection;
using static HeroesReplay.Tests.Unit.Obs.Collection.ObsCollectionFixture;

namespace HeroesReplay.Tests.Unit.Obs.Collection;

/// <summary>
/// A small install <c>obs</c> folder for the #330 tests: a template with a rank image, a local
/// browser page, and a data file, those files, and a <c>bundle.manifest</c>.
/// </summary>
internal static class StableAssetsFixture
{
    public static readonly string[] Files = ["Ranks/gold.png", "countdown/index.html"];

    /// <summary>
    /// <c>&lt;root&gt;\&lt;folder&gt;\obs</c>, with a schema 2 manifest (a release) or the plain
    /// list (a source checkout).
    /// </summary>
    public static string Install(string root, string folder, bool versioned)
    {
        string obs = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar), "obs");
        Directory.CreateDirectory(Path.Combine(obs, "Ranks"));
        Directory.CreateDirectory(Path.Combine(obs, "countdown"));
        File.WriteAllBytes(Path.Combine(obs, "Ranks", "gold.png"), [0x89, 0x50, 0x4E, 0x47, 7]);
        File.WriteAllText(Path.Combine(obs, "countdown", "index.html"), "<html>countdown</html>");
        File.WriteAllText(Path.Combine(obs, ObsCollectionBundle.CollectionFileName), Template());
        string[] listed = Files.Append(ObsCollectionBundle.CollectionFileName).ToArray();
        File.WriteAllText(
            Path.Combine(obs, ObsCollectionBundle.FileName),
            versioned
                ? ObsCollectionBundle.Serialize(
                    ObsCollectionBundle.Create(obs, listed, new ObsContract([], [], []))
                )
                : string.Join("\n", listed) + "\n"
        );
        return obs;
    }

    public static string Template() =>
        Document(
            Source("gold-image", "image_source", "{\"file\":\"Ranks/gold.png\"}"),
            Source(
                "countdown",
                settings: "{\"is_local_file\":true,\"local_file\":\"countdown/index.html\"}"
            ),
            Source(
                "current-replay",
                "text_gdiplus",
                "{\"read_from_file\":true,\"file\":\"C:/heroesreplay/Data/OBS.txt\"}"
            ),
            Scene(
                "game-scene",
                Item("gold-image", id: 1),
                Item("countdown", id: 2),
                Item("current-replay", id: 3)
            )
        );

    /// <summary>A source's setting in a collection file.</summary>
    public static string Setting(string collectionPath, string source, string property)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(collectionPath));
        return document
            .RootElement.GetProperty("sources")
            .EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == source)
            .GetProperty("settings")
            .GetProperty(property)
            .GetString();
    }

    /// <summary>Forward slashes, as the collection writes paths.</summary>
    public static string Forward(string path) => path.Replace('\\', '/');
}
