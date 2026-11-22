using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroPositionFillTests
{
    [Fact]
    public void AViking_IsPlacedBetweenTwoMeasuredPlaces_ButNotAcrossALongGap()
    {
        var olaf = new ReplayUnit
        {
            Name = "HeroOlaf",
            TimeSpanBorn = TimeSpan.Zero,
            Positions = new List<Position> { At(10, 0, 0), At(20, 100, 50), At(60, 0, 0) },
        };
        var erik = new ReplayUnit
        {
            Name = "HeroErik",
            TimeSpanBorn = TimeSpan.Zero,
            Positions = new List<Position>(),
        };
        var vikings = new Player
        {
            Name = "Vikings",
            Team = 0,
            HeroUnits = new List<ReplayUnit> { olaf, erik },
        };

        HeroPositionFill.Apply(new Replay { Players = new[] { vikings } });

        Position middle = olaf.Positions.Single(position => position.TimeSpan.TotalSeconds == 15);
        Assert.Equal(50, middle.Point.X);
        Assert.Equal(25, middle.Point.Y);
        Assert.True(middle.IsEstimated);
        Assert.Equal(11, olaf.Positions.Count(position => position.TimeSpan.TotalSeconds <= 20));
        Assert.DoesNotContain(olaf.Positions, position => position.TimeSpan.TotalSeconds == 40);
        Assert.Empty(erik.Positions);
    }

    private static Position At(int second, int x, int y) =>
        new()
        {
            TimeSpan = TimeSpan.FromSeconds(second),
            Point = new Point { X = x, Y = y },
        };
}
