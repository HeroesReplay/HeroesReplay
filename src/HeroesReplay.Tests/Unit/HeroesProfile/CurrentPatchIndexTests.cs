using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Services.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class CurrentPatchIndexTests
{
    [Fact]
    public void FindFirst_ReturnsTheEarliestIdOfTheLatestVersion()
    {
        var rows = new List<CurrentPatchIndex.Row>();
        for (int id = 1; id <= 10; id++)
        {
            rows.Add(new CurrentPatchIndex.Row(id, "2.55.17.97771"));
        }

        for (int id = 11; id <= 20; id++)
        {
            rows.Add(new CurrentPatchIndex.Row(id, "2.55.17.98025"));
        }

        int first = CurrentPatchIndex.FindFirst(
            20,
            "2.55.17.98025",
            after => rows.FirstOrDefault(row => row.Id > after)
        );

        Assert.Equal(11, first);
    }

    [Fact]
    public void TryReplace_UpdatesMinReplayId()
    {
        const string json = "{ \"HeroesProfileApi\": { \"MinReplayId\": 65267450 } }";
        Assert.True(MinReplayIdFile.TryReplace(json, 65300000, out string updated));
        Assert.Contains("\"MinReplayId\": 65300000", updated);
    }
}
