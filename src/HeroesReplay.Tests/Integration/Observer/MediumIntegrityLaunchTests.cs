using System;
using System.IO;
using System.Threading;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Integration.Observer;

[Trait(TestCategories.Category, TestCategories.Integration)]
public class MediumIntegrityLaunchTests
{
    [Fact]
    public void Start_LaunchesAtMediumIntegrity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hr-il-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, "whoami.txt");
        try
        {
            bool parentElevated = MediumIntegrityProcess.IsCurrentProcessElevated();
            string cmd = Environment.GetEnvironmentVariable("ComSpec");
            string source = MediumIntegrityProcess.Start(
                cmd,
                "/c whoami /groups > " + ReplayStartCommand.Quote(output),
                directory
            );

            string text = ReadWhenReady(output);
            Assert.Contains("Medium Mandatory Level", text, StringComparison.Ordinal);
            Assert.DoesNotContain("High Mandatory Level", text, StringComparison.Ordinal);
            if (parentElevated)
            {
                Assert.NotEqual("direct", source);
            }
            else
            {
                Assert.Equal("direct", source);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReadWhenReady(string path)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    string text = File.ReadAllText(path);
                    if (text.Contains("Mandatory Label", StringComparison.Ordinal))
                    {
                        return text;
                    }
                }
                catch (IOException) { }
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("whoami did not write an integrity label to " + path);
    }
}
