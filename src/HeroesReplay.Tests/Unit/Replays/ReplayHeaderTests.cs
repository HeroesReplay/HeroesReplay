using System;
using System.IO;
using Heroes.ReplayParser;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Replays;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayHeaderTests
{
    /// <summary>#280: the downloader judges each waiting file by the game date in its header.</summary>
    [Fact]
    public void Load_ReadsTheGameDateTheMediaPolicyJudges()
    {
        string path = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Assets",
            "hour-long-replay-provided-by-mgatner.StormReplay"
        );

        Replay header = ReplayHeader.Load(path);

        Assert.NotNull(header);
        Assert.NotEqual(default, header.Timestamp);
        Assert.False(string.IsNullOrWhiteSpace(header.ReplayVersion));
        ReplayMediaPolicyInput facts = ReplayMediaFacts.From(
            new LoadedReplay { ReplayId = 1, Replay = header },
            alreadyPublished: false,
            alreadyScheduled: false,
            inOutbox: false
        );
        Assert.Equal(header.Timestamp, facts.GameDateUtc);
        Assert.True(
            ReplayMediaPolicy.IsPastWindow(
                facts,
                new ReplayMediaPolicySettings(),
                header.Timestamp.AddDays(30)
            )
        );
    }

    [Fact]
    public void Load_IsNullForAFileThatIsNotAReplay()
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-header-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        try
        {
            Assert.Null(ReplayHeader.Load(path));
            Assert.Null(ReplayHeader.Load(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
