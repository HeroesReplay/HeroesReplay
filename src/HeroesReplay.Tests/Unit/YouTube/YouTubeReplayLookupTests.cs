using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Search;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class YouTubeReplayLookupTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-yt-lookup-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Lookup_TakesOnlySettings_SoTheSpectatorCannotCallYouTube()
    {
        var parameters = typeof(YouTubeReplayLookup)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal(new[] { typeof(AppSettings) }, parameters);
    }

    [Fact]
    public async Task AlreadyUploaded_ReadsTheCatalog()
    {
        YouTubeReplayCatalog.Remember(YouTubeReplayCatalog.PathFor(directory), 65389750);

        Assert.True(await Lookup().AlreadyUploadedAsync(Replay(65389750), CancellationToken.None));
        Assert.False(await Lookup().AlreadyUploadedAsync(Replay(65389751), CancellationToken.None));
    }

    [Fact]
    public async Task AlreadyUploaded_ReadsTheReceiptAndRemembersIt()
    {
        string context = Path.Combine(directory, "Contexts", "65550001");
        Directory.CreateDirectory(context);
        File.WriteAllText(Path.Combine(context, "youtube-entry-uploaded.json"), "{}");

        Assert.True(await Lookup().AlreadyUploadedAsync(Replay(65550001), CancellationToken.None));
        Directory.Delete(context, recursive: true);
        Assert.True(await Lookup().AlreadyUploadedAsync(Replay(65550001), CancellationToken.None));
    }

    [Fact]
    public async Task AlreadyUploaded_UnknownReplayIsFalseWithDryRunOff()
    {
        Assert.False(await Lookup().AlreadyUploadedAsync(Replay(42), CancellationToken.None));
        Assert.False(await Lookup().AlreadyUploadedAsync(null, CancellationToken.None));
    }

    private YouTubeReplayLookup Lookup() =>
        new(
            new AppSettings
            {
                Location = new LocationSettings { DataDirectory = directory },
                YouTube = new YouTubeSettings
                {
                    DryRun = false,
                    EntryFileNameUploaded = "youtube-entry-uploaded.json",
                },
            }
        );

    private static LoadedReplay Replay(int id) => new() { ReplayId = id };
}
