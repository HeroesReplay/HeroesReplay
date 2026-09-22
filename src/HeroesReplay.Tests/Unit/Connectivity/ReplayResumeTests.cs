using System.IO;
using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Tests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Connectivity;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayResumeTests
{
    [Fact]
    public void ShouldReplay_OnlyWhenTheUnfinishedReplayHasNoGame()
    {
        string path = Path.GetTempFileName();
        try
        {
            Assert.False(ReplayResumeRules.ShouldReplay(true, 12, null, path));
            Assert.False(ReplayResumeRules.ShouldReplay(false, 12, 12, path));
            Assert.False(ReplayResumeRules.ShouldReplay(false, null, null, path));
            Assert.False(
                ReplayResumeRules.ShouldReplay(false, 12, null, "Z:\\missing.StormReplay")
            );
            Assert.True(ReplayResumeRules.ShouldReplay(false, 12, 11, path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void File_RequestIsTakenOnce()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "hr-replay-resume-" + Path.GetRandomFileName()
        );
        var store = new ReplayResumeFile(path);
        try
        {
            store.Request(65268467, @"C:\heroesreplay\Data\Standard\one.StormReplay");
            Assert.True(store.TryTake(out int id, out string replayPath));
            Assert.Equal(65268467, id);
            Assert.EndsWith("one.StormReplay", replayPath);
            Assert.False(store.TryTake(out _, out _));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void HeroesProfileResume_FileIsVisibleToAnotherInstance()
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-hp-resume-" + Path.GetRandomFileName());
        var writer = new HeroesProfileResume(path);
        var reader = new HeroesProfileResume(path);
        try
        {
            Assert.False(reader.IsPending);
            writer.Arm();
            Assert.True(reader.IsPending);
            Assert.True(reader.Consume());
            Assert.False(reader.IsPending);
            Assert.False(new HeroesProfileResume(path).IsPending);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
