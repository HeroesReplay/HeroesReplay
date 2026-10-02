using System.Collections.Generic;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

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
            ["observerinterface"] = ClientSettings.AhliObsInterfaceFile,
            ["replayinterface"] = ClientSettings.AhliObsInterfaceFile,
        };

        string result = StormVariablesEditor.Apply(existing, updates);
        Dictionary<string, string> parsed = StormVariablesEditor.Parse(result);

        Assert.Equal("0", parsed["displaymode"]);
        Assert.Equal("1920", parsed["width"]);
        Assert.Equal("1080", parsed["height"]);
        Assert.Equal("1920", parsed["windowwidth"]);
        Assert.Equal("1080", parsed["windowheight"]);
        Assert.Equal("1", parsed["windowstate"]);
        Assert.Equal(ClientSettings.AhliObsInterfaceFile, parsed["observerinterface"]);
        Assert.Equal(ClientSettings.AhliObsInterfaceFile, parsed["replayinterface"]);
        Assert.Equal("Direct3D11", parsed["GraphicsApi"]);
    }

    [Fact]
    public void VariablesPreset_WritesWindowed1080pBackgroundAudioAndAhliObs()
    {
        IReadOnlyDictionary<string, string> preset = new ClientSettings().VariablesPreset;
        string applied = StormVariablesEditor.Apply(
            "soundglobal=false\r\nGraphicsApi=Direct3D11\r\nobserverinterface=AhliObs 0.75.StormInterface\r\n",
            preset
        );
        Dictionary<string, string> parsed = StormVariablesEditor.Parse(applied);

        Assert.Equal("0", parsed["displaymode"]);
        Assert.Equal("1920", parsed["width"]);
        Assert.Equal("1080", parsed["height"]);
        Assert.Equal("1920", parsed["windowwidth"]);
        Assert.Equal("1080", parsed["windowheight"]);
        Assert.Equal("1", parsed["windowstate"]);
        Assert.Equal("true", parsed["soundglobal"]);
        Assert.Equal(ClientSettings.AhliObsInterfaceFile, parsed["observerinterface"]);
        Assert.Equal(ClientSettings.AhliObsInterfaceFile, parsed["replayinterface"]);
        Assert.Equal("Direct3D11", parsed["GraphicsApi"]);
    }

    [Theory]
    [InlineData("AhliObs 0.75", "AhliObs 0.75", true)]
    [InlineData("AhliObs 0.75", "ahliobs 0.75", true)]
    [InlineData("AhliObs 0.75", "AhliObs 0.75.StormInterface", false)]
    [InlineData("AhliObs 0.75.StormInterface", "AhliObs 0.75", false)]
    [InlineData("AhliObs 0.75.StormInterface", "AhliObs 0.75.StormInterface", true)]
    public void InterfaceNameEquals_MatchesTheInterfaceFileNameExactly(
        string expected,
        string actual,
        bool same
    )
    {
        Assert.Equal(same, StormVariablesEditor.InterfaceNameEquals(expected, actual));
    }

    [Fact]
    public void InterfaceNameEquals_DoesNotMatchADifferentInterface()
    {
        Assert.False(StormVariablesEditor.InterfaceNameEquals("AhliObs 0.75", "SpazzoObsv40"));
        Assert.False(StormVariablesEditor.InterfaceNameEquals("AhliObs 0.75", ""));
        Assert.False(StormVariablesEditor.InterfaceNameEquals("AhliObs 0.75", null));
    }

    [Theory]
    [InlineData("1920", "1920", true)]
    [InlineData("1920", "1921", true)]
    [InlineData("1080", "1088", true)]
    [InlineData("1920", "2560", false)]
    [InlineData("1920", "wide", false)]
    public void WindowEdgeEquals_AllowsAFewPixelsOfBorder(string expected, string actual, bool same)
    {
        Assert.Equal(same, StormVariablesEditor.WindowEdgeEquals(expected, actual));
    }
}
