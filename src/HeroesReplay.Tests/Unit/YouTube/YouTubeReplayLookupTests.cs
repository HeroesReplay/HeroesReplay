using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeReplayLookupTests
{
    [Fact]
    public async Task AlreadyUploaded_RemembersAChannelHitAndSkipsTheNextSearch()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-yt-lookup-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var search = new CountingSearch(
            new YouTubeVideoText
            {
                Title = "Tomb of the Spider Queen - 65389750 - Storm League - Gold 3",
                Description =
                    "Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65389750",
            }
        );
        var lookup = new YouTubeReplayLookup(
            NullLogger<YouTubeReplayLookup>.Instance,
            new AppSettings
            {
                Location = new LocationSettings { DataDirectory = directory },
                YouTube = new YouTubeSettings
                {
                    EntryFileNameUploaded = "youtube-entry-uploaded.json",
                },
            },
            search
        );
        var replay = new LoadedReplay { ReplayId = 65389750 };

        try
        {
            Assert.True(await lookup.AlreadyUploadedAsync(replay, CancellationToken.None));
            Assert.True(await lookup.AlreadyUploadedAsync(replay, CancellationToken.None));
            Assert.Equal(1, search.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AlreadyUploaded_IgnoresALongerNumber()
    {
        var search = new CountingSearch(
            new YouTubeVideoText { Title = "Map - 653897501 - Storm League", Description = "" }
        );
        var lookup = new YouTubeReplayLookup(
            NullLogger<YouTubeReplayLookup>.Instance,
            new AppSettings
            {
                Location = new LocationSettings
                {
                    DataDirectory = Path.Combine(
                        Path.GetTempPath(),
                        "hr-yt-miss-" + Guid.NewGuid().ToString("N")
                    ),
                },
                YouTube = new YouTubeSettings(),
            },
            search
        );

        Assert.False(
            await lookup.AlreadyUploadedAsync(
                new LoadedReplay { ReplayId = 65389750 },
                CancellationToken.None
            )
        );
    }

    private sealed class CountingSearch : IYouTubeVideoSearch
    {
        private readonly YouTubeVideoText video;

        public CountingSearch(YouTubeVideoText video)
        {
            this.video = video;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<YouTubeVideoText>> SearchAsync(
            int replayId,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<YouTubeVideoText>>(new[] { video });
        }
    }
}
