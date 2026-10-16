using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.CLI.Commands.Focus.Commands;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Tests.Unit.HeroFocus;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Smoke;

/// <summary><c>focus candidates</c> and <c>focus coverage</c> (#433).</summary>
[Trait(TestCategories.Category, TestCategories.Smoke)]
public sealed class FocusCommandTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-cli-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(folder);

    [Fact]
    public async Task Help_ListsTheOptions()
    {
        (int candidates, string candidatesHelp, _) = await InvokeAsync(
            "focus",
            "candidates",
            "--help"
        );
        (int coverage, string coverageHelp, _) = await InvokeAsync("focus", "coverage", "--help");

        Assert.Equal(0, candidates);
        foreach (
            string option in new[]
            {
                "--directory",
                "--ledger",
                "--top",
                "--simulate",
                "--any-mode",
            }
        )
        {
            Assert.Contains(option, candidatesHelp, StringComparison.Ordinal);
        }

        Assert.Equal(0, coverage);
        Assert.Contains("--ledger", coverageHelp, StringComparison.Ordinal);
        Assert.Contains("--library", coverageHelp, StringComparison.Ordinal);
        Assert.Contains("--output", coverageHelp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidates_AMissingFolderOrLedger_ExitsOneBeforeLoadingTheCatalog()
    {
        Directory.CreateDirectory(folder);

        (int missing, _, string missingError) = await InvokeAsync(
            "focus",
            "candidates",
            "--directory",
            Path.Combine(folder, "nowhere")
        );
        (int noLedger, _, string ledgerError) = await InvokeAsync(
            "focus",
            "candidates",
            "--directory",
            folder,
            "--ledger",
            Path.Combine(folder, "missing.json")
        );
        (int empty, _, string emptyError) = await InvokeAsync(
            "focus",
            "candidates",
            "--directory",
            folder
        );

        Assert.Equal(1, missing);
        Assert.Contains("was not found", missingError, StringComparison.Ordinal);
        Assert.Equal(1, noLedger);
        Assert.Contains("Ledger was not found", ledgerError, StringComparison.Ordinal);
        Assert.Equal(1, empty);
        Assert.Contains("No .StormReplay files", emptyError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coverage_ReportsTheLedgerAgainstTheRoster_AsTextOrJson()
    {
        string ledgerPath = Path.Combine(folder, "ledger.json");
        new FocusLedgerStore(ledgerPath).Update(ledger =>
        {
            ledger.Put(
                FocusFixtures.Entry("Valla", "Ranged Assassin", FocusFixtures.Now, key: "1")
            );
            return 0;
        });
        string library = Path.Combine(folder, "youtube-library.jsonl");

        (int text, string output, _) = await InvokeAsync(
            "focus",
            "coverage",
            "--ledger",
            ledgerPath,
            "--library",
            library
        );
        (int json, string document, _) = await InvokeAsync(
            "focus",
            "coverage",
            "--ledger",
            ledgerPath,
            "--library",
            library,
            "--output",
            "json"
        );

        Assert.Equal(0, text);
        Assert.Contains("1 focus picks (1 played, 0 uploaded)", output, StringComparison.Ordinal);
        Assert.Contains("1 of", output, StringComparison.Ordinal);
        Assert.Contains("Not covered yet:", output, StringComparison.Ordinal);
        Assert.Equal(0, json);
        using JsonDocument parsed = JsonDocument.Parse(document);
        Assert.Equal(CoverageCommand.Read, parsed.RootElement.GetProperty("code").GetString());
        Assert.True(parsed.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, parsed.RootElement.GetProperty("details").GetProperty("picks").GetInt32());
        Assert.True(
            parsed.RootElement.GetProperty("details").GetProperty("rosterSize").GetInt32() >= 90
        );
    }

    [Fact]
    public void Run_PrintsTheBestCandidates_AndASimulationThatVariesTheHero()
    {
        HeroSignatures signatures = HeroSignatures.Embedded();
        var settings = new HeroFocusSettings();
        FocusCard[] cards =
        {
            FocusFixtures.Card(
                1,
                FocusFixtures.Player(0, "Valla", "Demo", "Ranged Assassin", awards: new[] { "MVP" })
            ),
            FocusFixtures.Card(
                2,
                FocusFixtures.Player(0, "Valla", "Demo", "Ranged Assassin", awards: new[] { "MVP" })
            ),
            FocusFixtures.Card(
                3,
                FocusFixtures.Player(
                    0,
                    "Raynor",
                    "Rayn",
                    "Ranged Assassin",
                    awards: new[] { "MVP" }
                )
            ),
        };
        using var console = new StringWriter();

        CandidatesCommand.Run(
            cards,
            new FocusScorer(settings, signatures),
            CandidatesCommand.Baselines(cards, signatures, settings),
            new FocusLedger(),
            top: 2,
            simulate: 2,
            anyMode: false,
            FocusFixtures.Now,
            console
        );

        string text = console.ToString();
        Assert.Contains("3 cards, 30 players", text, StringComparison.Ordinal);
        Assert.Contains("Simulating 2 focus picks:", text, StringComparison.Ordinal);
        Assert.Contains("Heroes: 2 different in 2 picks.", text, StringComparison.Ordinal);
        Assert.Contains("perf ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "65550003_Storm League_Diamond 3_Alterac Pass_abc_.StormReplay",
        "65550003",
        65550003,
        "Diamond 3"
    )]
    [InlineData("my game.StormReplay", "my game", null, null)]
    public void Facts_TakeTheIdAndRankFromACacheFileName(
        string file,
        string key,
        int? id,
        string rank
    )
    {
        FocusReplayFacts facts = CandidatesCommand.Facts(Path.Combine("x", file));

        Assert.Equal(key, facts.Key);
        Assert.Equal(id, facts.ReplayId);
        Assert.Equal(rank, facts.Rank);
        Assert.Equal(file, facts.File);
    }

    private static async Task<(int Code, string Output, string Error)> InvokeAsync(
        params string[] args
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int code = await new CommandLineService().InvokeAsync(
            args,
            new InvocationConfiguration { Output = output, Error = error }
        );
        return (code, output.ToString(), error.ToString());
    }
}
