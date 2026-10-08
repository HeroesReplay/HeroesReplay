using System;
using System.IO;
using HeroesReplay.Core.Retention;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class PendingUploadSizeTests : IDisposable
{
    private const string Entry = "youtube-entry.json";
    private const string Uploaded = "youtube-entry-uploaded.json";

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-pending-" + Guid.NewGuid().ToString("N")
    );

    public PendingUploadSizeTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bytes_SkipsARecordingThatAlreadyHasAVideoId()
    {
        Context("1", 100, "{\"ReplayId\":1}");
        Context("2", 300, "{\"ReplayId\":2,\"VideoId\":\"abc\"}");

        long pending = PendingUploadSize.Bytes(root, Entry, Uploaded);

        Assert.Equal(100, pending);
    }

    /// <summary>
    /// #317: a dry run never sends a recording, planned or not, so the Disk pending-bytes gates
    /// see nothing waiting and a dev box keeps recording.
    /// </summary>
    [Fact]
    public void Bytes_InADryRunCountsNoRecording()
    {
        Context("1", 100, "{\"ReplayId\":1}");
        string planned = Context("2", 200, "{\"ReplayId\":2}");
        File.WriteAllText(Path.Combine(planned, DryRunRecordings.PlanFileName), "{}");
        Context("3", 300, "{\"ReplayId\":3,\"VideoId\":\"abc\"}");

        long pending = PendingUploadSize.Bytes(root, Entry, Uploaded, dryRun: true);

        Assert.Equal(0, pending);
    }

    /// <summary>
    /// Production is unchanged: with DryRun off every recording that waits for its insert
    /// counts, one an earlier dry run planned too, because a live uploader still sends it.
    /// </summary>
    [Fact]
    public void Bytes_WithDryRunOffCountsEveryWaitingRecordingAsBefore()
    {
        Context("1", 100, "{\"ReplayId\":1}");
        string planned = Context("2", 200, "{\"ReplayId\":2}");
        File.WriteAllText(Path.Combine(planned, DryRunRecordings.PlanFileName), "{}");
        Context("3", 300, "{\"ReplayId\":3,\"VideoId\":\"abc\"}");

        long live = PendingUploadSize.Bytes(root, Entry, Uploaded, dryRun: false);

        Assert.Equal(300, live);
        Assert.Equal(PendingUploadSize.Bytes(root, Entry, Uploaded), live);
        Assert.Equal(2, PendingUploadSize.Count(root, Entry, Uploaded));
    }

    private string Context(string id, int size, string entry)
    {
        string directory = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
        File.WriteAllBytes(Path.Combine(directory, "match.mp4"), new byte[size]);
        File.WriteAllText(Path.Combine(directory, Entry), entry);
        return directory;
    }
}
