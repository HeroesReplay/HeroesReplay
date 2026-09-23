using System;
using HeroesReplay.Core.Services.Observer;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class GameTimerLogTests
{
    [Fact]
    public void Write_FirstClockDoesNotOverflow()
    {
        var log = new GameTimerLog(NullLogger<GameTimerLog>.Instance);
        log.Write(null, new GameTimerReading(true, "ocr", "ok", TimeSpan.FromMinutes(12)));
        log.Write(null, new GameTimerReading(true, "memory", "ok", TimeSpan.MaxValue));
        log.Write(null, new GameTimerReading(true, "ocr", "ok", TimeSpan.FromSeconds(1)));
    }
}
