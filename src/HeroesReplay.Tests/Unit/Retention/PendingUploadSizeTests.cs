using System;
using System.IO;
using HeroesReplay.Core.Services.Retention;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PendingUploadSizeTests
{
    [Fact]
    public void Bytes_SkipsARecordingThatAlreadyHasAVideoId()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-pending-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            Context(root, "1", 100, "{\"ReplayId\":1}");
            Context(root, "2", 300, "{\"ReplayId\":2,\"VideoId\":\"abc\"}");

            long pending = PendingUploadSize.Bytes(
                root,
                "youtube-entry.json",
                "youtube-entry-uploaded.json"
            );

            Assert.Equal(100, pending);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Context(string root, string id, int size, string entry)
    {
        string directory = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
        File.WriteAllBytes(Path.Combine(directory, "match.mp4"), new byte[size]);
        File.WriteAllText(Path.Combine(directory, "youtube-entry.json"), entry);
    }
}
