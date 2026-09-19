using System.Collections.Generic;
using HeroesReplay.Core.Services.Client;
using Xunit;

namespace HeroesReplay.Tests.Unit.Client;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StormVariablesEditorTests
{
    [Fact]
    public void Apply_SetsWindowed1080pAndAhliObs_PreservesOtherKeys()
    {
        const string existing = """
            displaymode=1
            width=2560
            height=1440
            GraphicsApi=Direct3D11
            observerinterface=
            replayinterface=
            windowstate=3
            """;

        var updates = new Dictionary<string, string>
        {
            ["displaymode"] = "0",
            ["width"] = "1920",
            ["height"] = "1080",
            ["windowwidth"] = "1920",
            ["windowheight"] = "1080",
            ["windowstate"] = "1",
            ["observerinterface"] = "AhliObs 0.75",
            ["replayinterface"] = "AhliObs 0.75",
        };

        string result = StormVariablesEditor.Apply(existing, updates);
        Dictionary<string, string> parsed = StormVariablesEditor.Parse(result);

        Assert.Equal("0", parsed["displaymode"]);
        Assert.Equal("1920", parsed["width"]);
        Assert.Equal("1080", parsed["height"]);
        Assert.Equal("1920", parsed["windowwidth"]);
        Assert.Equal("1080", parsed["windowheight"]);
        Assert.Equal("1", parsed["windowstate"]);
        Assert.Equal("AhliObs 0.75", parsed["observerinterface"]);
        Assert.Equal("AhliObs 0.75", parsed["replayinterface"]);
        Assert.Equal("Direct3D11", parsed["GraphicsApi"]);
    }

    [Theory]
    [InlineData("AhliObs 0.75", "AhliObs 0.75")]
    [InlineData("AhliObs 0.75", "AhliObs 0.75.StormInterface")]
    [InlineData("AhliObs 0.75.StormInterface", "AhliObs 0.75")]
    [InlineData("AhliObs 0.75.StormInterface", "AhliObs 0.75.StormInterface")]
    public void InterfaceNameEquals_TreatsDropdownAndFilenameAsTheSame(
        string expected,
        string actual
    )
    {
        Assert.True(StormVariablesEditor.InterfaceNameEquals(expected, actual));
    }

    [Fact]
    public void InterfaceNameEquals_DoesNotMatchADifferentInterface()
    {
        Assert.False(StormVariablesEditor.InterfaceNameEquals("AhliObs 0.75", "SpazzoObsv40"));
        Assert.False(StormVariablesEditor.InterfaceNameEquals("AhliObs 0.75", ""));
        Assert.False(StormVariablesEditor.InterfaceNameEquals("AhliObs 0.75", null));
    }
}
