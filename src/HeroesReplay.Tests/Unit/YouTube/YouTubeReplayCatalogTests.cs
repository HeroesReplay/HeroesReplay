using System.IO;
using HeroesReplay.Core.Services.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeReplayCatalogTests
{
    [Fact]
    public void Remember_IsIdempotent()
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-yt-" + Path.GetRandomFileName());
        try
        {
            Assert.False(YouTubeReplayCatalog.Contains(path, 65389750));
            YouTubeReplayCatalog.Remember(path, 65389750);
            YouTubeReplayCatalog.Remember(path, 65389750);
            Assert.True(YouTubeReplayCatalog.Contains(path, 65389750));
            Assert.False(YouTubeReplayCatalog.Contains(path, 1));
            Assert.Single(File.ReadAllLines(path));
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
