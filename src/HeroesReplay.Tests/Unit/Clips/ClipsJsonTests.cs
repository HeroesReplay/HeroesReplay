using System.Collections.Generic;
using System.Text.Json;
using HeroesReplay.Core.Clips;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Clips;

/// <summary><c>clips.json</c> keeps its bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClipsJsonTests
{
    [Fact]
    public void ClipIndex_WritesWhatTheAnonymousRowsWrote()
    {
        List<object> anonymous =
        [
            new
            {
                kind = "Pentakill",
                hero = "Valla <a&b>",
                description = "5 kills",
                hudStart = 600,
                hudEnd = 612,
                fileStart = 590.25,
                duration = 22.5,
                file = @"C:\clips\clip.mp4",
                kills = new object[] { new { second = 601, victim = "Li-Ming" } },
            },
            new
            {
                kind = "Pentakill",
                hero = "Raynor",
                description = (string)null,
                hudStart = 0,
                hudEnd = 0,
                fileStart = 0.0,
                duration = 0.0,
                file = "clip.mp4",
                kills = new object[0],
            },
        ];
        List<ClipIndexRow> rows =
        [
            new(
                "Pentakill",
                "Valla <a&b>",
                "5 kills",
                600,
                612,
                590.25,
                22.5,
                @"C:\clips\clip.mp4",
                [new ClipIndexKill(601, "Li-Ming")]
            ),
            new("Pentakill", "Raynor", null, 0, 0, 0.0, 0.0, "clip.mp4", []),
        ];

        Assert.Equal(
            JsonParity.Written(new JsonSerializerOptions { WriteIndented = true }, anonymous),
            JsonSerializer.Serialize(rows, MatchClipExporter.Index.ListClipIndexRow)
        );
    }
}
