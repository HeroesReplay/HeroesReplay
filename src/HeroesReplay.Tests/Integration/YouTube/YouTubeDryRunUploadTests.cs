using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Integration.YouTube;

[Trait(TestCategories.Category, TestCategories.Integration)]
public class YouTubeDryRunUploadTests
{
    [Fact]
    public async Task ProcessRecording_DryRun_MarksTheEntryUploadedWithoutCallingYouTube()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-youtube-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string recordingPath = Path.Combine(directory, "match.mp4");
            await File.WriteAllBytesAsync(
                recordingPath,
                new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 }
            );
            var entry = new YouTubeEntry
            {
                Title = "Volskaya Foundry - 65389750 - Storm League - Diamond",
                DescriptionLines = new[]
                {
                    "Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65389750",
                },
                PrivacyStatus = "public",
                CategoryId = "20",
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "youtube-entry.json"),
                JsonSerializer.Serialize(entry)
            );

            var uploader = new YouTubeUploader(
                NullLogger<YouTubeUploader>.Instance,
                new AppSettings
                {
                    YouTube = new YouTubeSettings
                    {
                        DryRun = true,
                        Enabled = true,
                        EntryFileName = "youtube-entry.json",
                        EntryFileNameUploaded = "youtube-entry-uploaded.json",
                        ReadyStableReads = 1,
                        ReadyPollMilliseconds = 20,
                    },
                    Location = new LocationSettings { DataDirectory = directory },
                },
                new CancellationTokenSource()
            );

            await uploader.ProcessRecording(recordingPath);

            Assert.False(File.Exists(Path.Combine(directory, "youtube-entry.json")));
            Assert.True(File.Exists(Path.Combine(directory, "youtube-entry-uploaded.json")));
            string receipt = await File.ReadAllTextAsync(
                Path.Combine(directory, "youtube-dry-run.json")
            );
            Assert.Contains("Volskaya Foundry - 65389750", receipt, StringComparison.Ordinal);
            Assert.Contains("\"Simulated\": true", receipt, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(directory, "client_secrets.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
