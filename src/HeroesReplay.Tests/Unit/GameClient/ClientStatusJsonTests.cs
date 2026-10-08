using System;
using HeroesReplay.CLI.Commands.Client;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary><c>client status --output json</c> (#311). Exit codes are the text mode's.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientStatusJsonTests
{
    private const string Variables = @"C:\Users\x\Documents\Heroes of the Storm\Variables.txt";

    [Fact]
    public void AMatchingPreset_IsOk()
    {
        CliResult<ClientStatusResult> status = ClientCommand.ReadStatus(() =>
            new ClientStatusResult(Variables, true, true, [], false)
        );

        Assert.True(status.Ok);
        Assert.Equal("client.preset_ok", status.Code);
        Assert.Equal(Variables, status.Details.VariablesPath);
        Assert.Equal(0, CliOutput.ExitCode(status));
    }

    [Fact]
    public void AMismatch_ExitsOneAndListsIt()
    {
        CliResult<ClientStatusResult> status = ClientCommand.ReadStatus(() =>
            new ClientStatusResult(Variables, false, false, ["windowmode=0"], true)
        );

        Assert.False(status.Ok);
        Assert.Equal("client.preset_mismatch", status.Code);
        Assert.Contains("windowmode=0", status.Message);
        Assert.Equal(1, CliOutput.ExitCode(status));
    }

    [Fact]
    public void AStatusThatCannotBeRead_IsClientError()
    {
        CliResult<ClientStatusResult> status = ClientCommand.ReadStatus(() =>
            throw new UnauthorizedAccessException("Documents is locked.")
        );

        Assert.False(status.Ok);
        Assert.Equal("client.error", status.Code);
        Assert.Null(status.Details);
        Assert.Contains("Documents is locked.", status.Message);
    }
}
