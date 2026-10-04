using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

[Trait(TestCategories.Category, TestCategories.Smoke)]
public class CliHelpTests
{
    [Fact]
    public void RootHelp_HasCheckAndSpectate()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("--help");
        Assert.Empty(result.Errors);
        Assert.Contains(root.Subcommands, c => c.Name == "check");
        Assert.Contains(root.Subcommands, c => c.Name == "spectate");
        Assert.Contains(root.Subcommands, c => c.Name == "calculators");
        Assert.Contains(root.Subcommands, c => c.Name == "mcp");
        Assert.Contains(root.Subcommands, c => c.Name == "client");
        Assert.Contains(root.Subcommands, c => c.Name == "otel");
        Assert.Contains(root.Subcommands, c => c.Name == "update");
        Assert.Contains(root.Subcommands, c => c.Name == "obs");
    }

    [Fact]
    public void ObsHelp_HasArmDisarmStatusPages()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("obs --help");
        Assert.Empty(result.Errors);
        Command obs = root.Subcommands.Single(c => c.Name == "obs");
        Assert.Contains("OBS:StreamingEnabled", obs.Description);
        Command arm = obs.Subcommands.Single(c => c.Name == "arm");
        Assert.Contains("stream-armed", arm.Description);
        Assert.Contains(obs.Subcommands, c => c.Name == "disarm");
        Assert.Contains(obs.Subcommands, c => c.Name == "status");
        Command pages = obs.Subcommands.Single(c => c.Name == "pages");
        Assert.Contains("queue.html", pages.Description);
        Assert.Contains("prediction-report.html", pages.Description);
        foreach (
            string help in new[]
            {
                "obs arm --help",
                "obs disarm --help",
                "obs status --help",
                "obs pages --help",
                "obs inspect --help",
                "obs validate --help",
                "obs inspect --output json",
                "obs validate -o json",
                "obs validate --output text",
            }
        )
        {
            Assert.Empty(root.Parse(help).Errors);
        }

        Assert.Contains(
            "stable codes",
            obs.Subcommands.Single(c => c.Name == "validate").Description
        );
        Assert.NotEmpty(root.Parse("obs validate --output yaml").Errors);
    }

    [Fact]
    public void UpdateHelp_HasTheReleaseHelpers()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("update --help");
        Assert.Empty(result.Errors);
        Command update = root.Subcommands.Single(c => c.Name == "update");
        Assert.Contains(update.Subcommands, c => c.Name == "check");
        Assert.Contains(update.Subcommands, c => c.Name == "preserve-min-replay-id");
        Assert.Contains(update.Subcommands, c => c.Name == "release-health");
        Assert.Contains(update.Subcommands, c => c.Name == "migrate-stream-arm");
        Assert.Contains(update.Subcommands, c => c.Name == "install-obs");
        Assert.Empty(root.Parse("update migrate-stream-arm --previous C:\\app").Errors);
        Assert.NotEmpty(root.Parse("update migrate-stream-arm").Errors);
        Assert.Empty(root.Parse("update install-obs --install C:\\app --environment prod").Errors);
        // The exact arguments apply-release.ps1 passes when it installs a release.
        Assert.Empty(
            root.Parse(
                "update install-obs --install C:\\app --previous C:\\app.previous --environment prod"
            ).Errors
        );
        // The exact arguments apply-release.ps1 passes to the health gate.
        Assert.Empty(
            root.Parse(
                "update release-health --since 2026-10-02T12:00:00.0000000Z --install C:\\app --environment prod --wait"
            ).Errors
        );
        Assert.Empty(
            root.Parse(
                "update release-health --since 2026-10-02T12:00:00Z --window 00:05:00"
            ).Errors
        );
        Assert.NotEmpty(root.Parse("update release-health").Errors);
        Assert.NotEmpty(root.Parse("update release-health --role-file role-ready.txt").Errors);
    }

    [Fact]
    public void CheckHelp_ListsServiceTargets()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("check --help");
        Assert.Empty(result.Errors);
        Command check = root.Subcommands.Single(c => c.Name == "check");
        Assert.Contains(check.Subcommands, c => c.Name == "config");
        Assert.Contains(check.Subcommands, c => c.Name == "heroesprofile");
        Assert.Contains(check.Subcommands, c => c.Name == "obs");
        Assert.Contains(check.Subcommands, c => c.Name == "twitch");
        Assert.Contains(check.Subcommands, c => c.Name == "client");
        Assert.Contains(check.Subcommands, c => c.Name == "connectivity");
        Assert.Contains(check.Subcommands, c => c.Name == "timer");
        Assert.Contains(check.Subcommands, c => c.Name == "twitch-extension");
        Assert.Contains(check.Subcommands, c => c.Name == "battlenet");
    }

    [Fact]
    public void ClientHelp_HasConfigureAndStatus()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("client --help");
        Assert.Empty(result.Errors);
        Command client = root.Subcommands.Single(c => c.Name == "client");
        Assert.Contains(client.Subcommands, c => c.Name == "configure");
        Assert.Contains(client.Subcommands, c => c.Name == "status");
    }

    [Fact]
    public void OtelHelp_HasUpDownStatus()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("otel --help");
        Assert.Empty(result.Errors);
        Command otel = root.Subcommands.Single(c => c.Name == "otel");
        Assert.Contains("No Docker", otel.Description);
        Assert.DoesNotContain("compose", otel.Description);
        Assert.DoesNotContain("container", otel.Description);
        Assert.Contains(otel.Subcommands, c => c.Name == "up");
        Assert.Contains(otel.Subcommands, c => c.Name == "down");
        Assert.Contains(otel.Subcommands, c => c.Name == "status");
        Command up = otel.Subcommands.Single(c => c.Name == "up");
        Assert.DoesNotContain("container", up.Description);
        Assert.DoesNotContain("Docker", up.Description);
        Assert.Contains("Aspire", up.Description);
    }

    [Fact]
    public void ServicesHelp_HasStartStopStatus()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("services --help");
        Assert.Empty(result.Errors);
        Command services = root.Subcommands.Single(c => c.Name == "services");
        Assert.Contains(services.Subcommands, c => c.Name == "start");
        Assert.Contains(services.Subcommands, c => c.Name == "stop");
        Assert.Contains(services.Subcommands, c => c.Name == "status");
    }

    [Fact]
    public void ServicesInstallTask_Parses()
    {
        var root = new HeroesReplayCommand();
        Assert.Empty(root.Parse("services install-task --help").Errors);
        Assert.Empty(root.Parse("services install-task --environment prod").Errors);
        Assert.Empty(
            root.Parse(
                "services install-task --environment dev --roles download,youtube --name HeroesReplay-live"
            ).Errors
        );
        Assert.Empty(root.Parse("services install-task --remove").Errors);
        Command task = root
            .Subcommands.Single(c => c.Name == "services")
            .Subcommands.Single(c => c.Name == "install-task");
        Assert.Contains("HeroesReplay-live", task.Description);
        Assert.Contains("--supervise", task.Description);
    }

    [Fact]
    public void ServicesStatusHelp_HasTheJsonOutput()
    {
        var root = new HeroesReplayCommand();
        Assert.Empty(root.Parse("services status --output json --help").Errors);
        Assert.Empty(root.Parse("services status --output json").Errors);
        Assert.Empty(root.Parse("services status -o text").Errors);
        Assert.NotEmpty(root.Parse("services status --output yaml").Errors);
        Command status = root
            .Subcommands.Single(c => c.Name == "services")
            .Subcommands.Single(c => c.Name == "status");
        Option output = status.Options.Single(o => o.Name == "--output");
        Assert.Contains("schemaVersion", output.Description);
        Assert.Contains("service.stale", output.Description);
    }

    [Fact]
    public void ServicesSupervise_AndStartSuperviseRolesHelp_Parse()
    {
        var root = new HeroesReplayCommand();
        Assert.Empty(root.Parse("services supervise --help").Errors);
        Assert.Empty(root.Parse("services start --supervise --help").Errors);
        Assert.Empty(
            root.Parse("services start --supervise --roles download,youtube --help").Errors
        );
        Assert.Empty(root.Parse("services start --roles download").Errors);
        Command services = root.Subcommands.Single(c => c.Name == "services");
        Command supervise = services.Subcommands.Single(c => c.Name == "supervise");
        Assert.Contains("service.restart_budget_exhausted", supervise.Description);
        Assert.Contains("services stop", supervise.Description);
        Command start = services.Subcommands.Single(c => c.Name == "start");
        Assert.Contains(
            "10s, 30s, 2m, 5m",
            start.Options.Single(o => o.Name == "--supervise").Description
        );
        Assert.Contains(
            "download,youtube",
            start.Options.Single(o => o.Name == "--roles").Description
        );
    }

    [Fact]
    public void HeroesProfileHelp_HasDownload()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("heroesprofile --help");
        Assert.Empty(result.Errors);
        Command heroesProfile = root.Subcommands.Single(c => c.Name == "heroesprofile");
        Assert.Contains(heroesProfile.Subcommands, c => c.Name == "download");
        Assert.Contains(heroesProfile.Subcommands, c => c.Name == "patch-index");
    }

    [Fact]
    public void TwitchHelp_HasPredictionsTest()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("twitch --help");
        Assert.Empty(result.Errors);
        Command twitch = root.Subcommands.Single(c => c.Name == "twitch");
        Assert.Contains(twitch.Subcommands, c => c.Name == "say");
        Command predictions = twitch.Subcommands.Single(c => c.Name == "predictions");
        Assert.Contains(predictions.Subcommands, c => c.Name == "test");
        Command rewards = twitch.Subcommands.Single(c => c.Name == "rewards");
        Assert.Contains(rewards.Subcommands, c => c.Name == "list");
        Assert.Contains(rewards.Subcommands, c => c.Name == "remove-unranked-draft");
        Assert.Contains(rewards.Subcommands, c => c.Name == "test");
    }

    [Fact]
    public void CalculatorsHelp_HasUnitsReport()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("calculators units --help");
        Assert.Empty(result.Errors);
        Command calculators = root.Subcommands.Single(c => c.Name == "calculators");
        Assert.Contains(calculators.Subcommands, c => c.Name == "report");
        Assert.Contains(calculators.Subcommands, c => c.Name == "coordinates");
        Assert.Contains(calculators.Subcommands, c => c.Name == "units");
        Assert.Contains(calculators.Subcommands, c => c.Name == "compositions");
    }

    [Fact]
    public void YouTubeHelp_HasLibrary()
    {
        var root = new HeroesReplayCommand();
        ParseResult result = root.Parse("youtube --help");
        Assert.Empty(result.Errors);
        Command youtube = root.Subcommands.Single(c => c.Name == "youtube");
        Assert.Contains(youtube.Subcommands, c => c.Name == "uploader");
        Assert.Contains(youtube.Subcommands, c => c.Name == "library");
        Command library = youtube.Subcommands.Single(c => c.Name == "library");
        Assert.Contains("services start", library.Description);
        ParseResult once = root.Parse("youtube library --help");
        Assert.Empty(once.Errors);
    }

    [Fact]
    public void ClientHelp_HasFirewall()
    {
        var root = new HeroesReplayCommand();
        Command client = root.Subcommands.Single(c => c.Name == "client");
        Command firewall = client.Subcommands.Single(c => c.Name == "firewall");
        Assert.Contains("elevated", firewall.Description);
        Assert.Empty(root.Parse("client firewall --help").Errors);
    }

    [Fact]
    public async Task SpectateHelp_WorksWithoutElevation()
    {
        Assert.Equal(0, await new CommandLineService().InvokeAsync(new[] { "spectate", "--help" }));
        Assert.Equal(
            0,
            await new CommandLineService().InvokeAsync(new[] { "spectate", "file", "--help" })
        );
    }
}
