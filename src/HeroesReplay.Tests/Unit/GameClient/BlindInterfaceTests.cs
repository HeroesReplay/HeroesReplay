using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary>
/// #411: a Blind Rating replay's client loads the blind copy of AhliObs (hero names, no chat).
/// The signed-in client kept loading AhliObs when Variables.txt named the blind file (ASA-SERVER,
/// 2026-10-12), so the blind archive is installed under the AhliObs file name instead, and
/// AhliObs goes back for the next replay.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class BlindInterfaceTests : IDisposable
{
    private readonly string gameFolder = Path.Combine(
        Path.GetTempPath(),
        "hr-blind-" + Guid.NewGuid().ToString("N")
    );

    public BlindInterfaceTests()
    {
        Directory.CreateDirectory(Path.Combine(gameFolder, "Accounts", "1"));
        File.WriteAllText(Path.Combine(gameFolder, "Accounts", "1", "Variables.txt"), "");
    }

    public void Dispose()
    {
        if (Directory.Exists(gameFolder))
        {
            Directory.Delete(gameFolder, recursive: true);
        }
    }

    private string Installed =>
        Path.Combine(gameFolder, "Interfaces", ClientSettings.AhliObsInterfaceFile);

    private StormClientConfigurator Configurator(ClientSettings client = null) =>
        new(new AppSettings { Client = client ?? new ClientSettings() }, gameFolder);

    [Fact]
    public void Configure_Blind_InstallsTheBlindArchiveUnderTheAhliObsName()
    {
        StormClientConfigurator configurator = Configurator();

        ClientConfigureResult result = configurator.Configure(blind: true);

        Assert.True(result.InterfaceCopied);
        Assert.Equal(Installed, result.InterfaceDestination);
        Assert.Equal(File.ReadAllBytes(Source(blind: true)), File.ReadAllBytes(Installed));
        Assert.False(
            File.Exists(
                Path.Combine(gameFolder, "Interfaces", ClientSettings.AhliObsBlindInterfaceFile)
            )
        );
        Dictionary<string, string> variables = StormVariablesEditor.Parse(
            File.ReadAllText(Path.Combine(gameFolder, "Accounts", "1", "Variables.txt"))
        );
        Assert.Equal(ClientSettings.AhliObsInterfaceFile, variables["replayinterface"]);
        Assert.Equal(ClientSettings.AhliObsInterfaceFile, variables["observerinterface"]);
        Assert.True(configurator.InstalledInterfaceIs(blind: true));
        Assert.False(configurator.InstalledInterfaceIs(blind: false));
    }

    [Fact]
    public void GetStatus_AfterABlindReplay_AsksForAhliObsBack()
    {
        StormClientConfigurator configurator = Configurator();
        configurator.Configure(blind: true);

        ClientStatusResult normal = configurator.GetStatus(blind: false);
        ClientStatusResult blind = configurator.GetStatus(blind: true);

        Assert.False(normal.MatchesPreset);
        Assert.Contains(
            normal.Mismatches,
            line => line.StartsWith("interface file differs from " + Source(blind: false))
        );
        Assert.True(blind.MatchesPreset, string.Join("; ", blind.Mismatches));
    }

    [Fact]
    public void Configure_NotBlind_PutsAhliObsBack()
    {
        StormClientConfigurator configurator = Configurator();
        configurator.Configure(blind: true);

        configurator.Configure(blind: false);

        Assert.Equal(File.ReadAllBytes(Source(blind: false)), File.ReadAllBytes(Installed));
        Assert.True(configurator.GetStatus(blind: false).MatchesPreset);
        Assert.True(configurator.InstalledInterfaceIs(blind: false));
        Assert.False(configurator.InstalledInterfaceIs(blind: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Missing Blind.StormInterface")]
    public void Configure_BlindWithoutABlindArchive_InstallsAhliObsAndWarns(string blindFile)
    {
        StormClientConfigurator configurator = Configurator(
            new ClientSettings { BlindInterfaceFileName = blindFile }
        );

        ClientConfigureResult result = configurator.Configure(blind: true);

        Assert.Equal(File.ReadAllBytes(Source(blind: false)), File.ReadAllBytes(Installed));
        Assert.Contains(result.Warnings, line => line.Contains("shows player names and chat"));
        Assert.True(configurator.InstalledInterfaceIs(blind: true));
        Assert.True(configurator.GetStatus(blind: true).MatchesPreset);
    }

    /// <summary>
    /// The build output carries both archives (tools/new-blind-interface.ps1 makes the blind one).
    /// </summary>
    [Fact]
    public void BuildOutput_CarriesBothInterfaces()
    {
        string blind = Source(blind: true);
        byte[] header = new byte[4];
        using (FileStream stream = File.OpenRead(blind))
        {
            Assert.Equal(4, stream.Read(header, 0, 4));
        }

        Assert.Equal(new byte[] { (byte)'M', (byte)'P', (byte)'Q', 0x1A }, header);
        Assert.NotEqual(File.ReadAllBytes(Source(blind: false)), File.ReadAllBytes(blind));
    }

    private static string Source(bool blind)
    {
        string path = StormClientConfigurator.FindInterfaceFile(
            blind ? ClientSettings.AhliObsBlindInterfaceFile : ClientSettings.AhliObsInterfaceFile
        );
        Assert.NotNull(path);
        return path;
    }
}
