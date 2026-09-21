using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Analysis.Calculators;
using HeroesReplay.Tests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class CalculatorSettingsTests
{
    [Fact]
    public void Empty_EnablesAll()
    {
        var settings = new CalculatorSettings();
        Assert.True(settings.IsEnabled(typeof(KillCalculator)));
        Assert.True(settings.IsEnabled("EmotingCalculator"));
    }

    [Fact]
    public void ExplicitFalse_Disables()
    {
        var settings = new CalculatorSettings();
        settings.Enabled["EmotingCalculator"] = false;
        Assert.False(settings.IsEnabled(typeof(EmotingCalculator)));
        Assert.True(settings.IsEnabled(typeof(KillCalculator)));
    }
}
