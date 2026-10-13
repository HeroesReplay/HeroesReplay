using System;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// <c>services stop</c> reads the stream's health, not only <c>outputActive</c> (#395). An output
/// stuck reconnecting is still up: OBS may put it back on air.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceStreamCheckTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, false, ServiceStreamState.Inactive, true)]
    [InlineData(true, false, ServiceStreamState.Active, false)]
    [InlineData(true, true, ServiceStreamState.Active, false)]
    public void TheHealth_DecidesWhetherTheStopIsConfirmed(
        bool active,
        bool reconnecting,
        ServiceStreamState state,
        bool confirmsStopped
    )
    {
        ServiceStreamCheck check = ServiceStreamCheck.From(
            ObsStreamHealth.Next(null, new ObsStreamSample(active, reconnecting, 1000), At)
        );

        Assert.Equal(state, check.State);
        Assert.Equal(confirmsStopped, check.ConfirmsStopped);
    }

    [Fact]
    public void AReconnectingOutput_SaysSo()
    {
        ServiceStreamCheck check = ServiceStreamCheck.From(
            ObsStreamHealth.Next(null, new ObsStreamSample(true, true, 199_373_948_174), At)
        );

        Assert.Contains("reconnecting", check.Describe(), StringComparison.Ordinal);
        Assert.StartsWith("STREAMING", check.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadStream_IsNotConfirmed()
    {
        ServiceStreamCheck check = ServiceStreamCheck.From(
            ObsStreamHealth.Unknown(null, "GetStreamStatus failed.", At)
        );

        Assert.Equal(ServiceStreamState.Unknown, check.State);
        Assert.False(check.ConfirmsStopped);
        Assert.Contains("GetStreamStatus failed.", check.Describe(), StringComparison.Ordinal);
    }
}
