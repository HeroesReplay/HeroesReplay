using System;
using System.IO;
using System.Linq;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost.Logs;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceStartFailureLogTests
{
    [Fact]
    public void FailedStart_IsWrittenToTheSupervisorLog()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-start-failure-" + Path.GetRandomFileName()
        );
        try
        {
            ServiceStartFailureLog.Write(
                new ServiceLogSettings { Directory = directory },
                1,
                "twitch failed: Twitch scopes are not valid.\nRolled back spectate pid 8740.\n"
            );

            string log = Directory
                .GetFiles(directory, "supervisor-*.log")
                .Select(File.ReadAllText)
                .Single();
            Assert.Contains("services start exited 1", log, StringComparison.Ordinal);
            Assert.Contains("Twitch scopes are not valid.", log, StringComparison.Ordinal);
            Assert.Contains("Rolled back spectate pid 8740.", log, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Capture_KeepsWhatWasPrintedAndStillPrintsIt()
    {
        TextWriter before = Console.Out;
        var shown = new StringWriter();
        Console.SetOut(shown);
        try
        {
            string kept;
            using (var capture = new ConsoleCapture())
            {
                Console.WriteLine("Started spectate pid 1.");
                Console.Error.WriteLine("twitch failed: scopes.");
                kept = capture.Text;
            }

            Console.WriteLine("after");

            Assert.Contains("Started spectate pid 1.", kept, StringComparison.Ordinal);
            Assert.Contains("twitch failed: scopes.", kept, StringComparison.Ordinal);
            Assert.DoesNotContain("after", kept, StringComparison.Ordinal);
            Assert.Contains("Started spectate pid 1.", shown.ToString(), StringComparison.Ordinal);
            Assert.Contains("after", shown.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(before);
        }
    }

    [Theory]
    [InlineData("spectate", "hr-spectate")]
    [InlineData("twitch", "hr-twitch")]
    [InlineData("supervisor", "hr-supervisor")]
    [InlineData(null, null)]
    [InlineData("..\\evil", null)]
    public void RoleConsole_IsTitledByItsRole(string role, string title)
    {
        Assert.Equal(title, ServiceConsoleTitle.For(role));
    }
}
