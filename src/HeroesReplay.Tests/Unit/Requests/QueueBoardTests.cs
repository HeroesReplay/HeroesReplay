using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch.Rewards;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Requests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class QueueBoardTests
{
    [Fact]
    public void Write_EmptyQueue_ExplainsRewardsReplayIdAndHeroFocus()
    {
        string html = Render(null);

        Assert.Contains("0 requests waiting", html);
        Assert.Contains("The queue is empty.", html);
        Assert.Contains("Check out the channel-point rewards.", html);
        Assert.Contains("There are several rewards to claim.", html);
        Assert.Contains("Pick a random match, a map, or a rank.", html);
        Assert.Contains("Request a specific replay.", html);
        Assert.Contains("Redeem the replay reward", html);
        Assert.Contains("Heroes Profile replay ID, for example 12345678", html);
        Assert.Contains("recent patch", html);
        Assert.Contains("12345678,3 follows hero 3", html);
        Assert.Contains("before that match starts", html);
        Assert.Contains("while they are alive", html);
        Assert.Contains("while they are dead", html);
    }

    [Fact]
    public void Write_ExplainsTheHeroNumbersAboveThePortraits()
    {
        string html = Render(null);

        Assert.Contains(
            "<div class=\"team blue\"><i>Blue</i><b>1</b><b>2</b><b>3</b><b>4</b><b>5</b></div>",
            html
        );
        Assert.Contains(
            "<div class=\"team red\"><b>6</b><b>7</b><b>8</b><b>9</b><b>0</b><i>Red</i></div>",
            html
        );
        Assert.Contains("above each hero portrait at the top of the game screen", html);
        Assert.Contains("1 to 5 are the Blue team on the left", html);
        Assert.Contains("6 to 9 and 0 are the Red team on the right", html);
        Assert.Contains("0 is the tenth hero", html);
    }

    [Fact]
    public void Write_HeroNumbers_MatchTheReplayIdPlayerSlots()
    {
        Assert.True(PlayerPriorityRequest.TrySlot("1", out int first));
        Assert.True(PlayerPriorityRequest.TrySlot("5", out int lastBlue));
        Assert.True(PlayerPriorityRequest.TrySlot("6", out int firstRed));
        Assert.True(PlayerPriorityRequest.TrySlot("0", out int tenth));

        Assert.Equal(0, first);
        Assert.Equal(4, lastBlue);
        Assert.Equal(5, firstRed);
        Assert.Equal(9, tenth);
    }

    [Fact]
    public void Write_SupportedRewards_NamesTheConfiguredRewardTitles()
    {
        var holder = new SupportedRewardsHolder(
            new MapOnlyGameData(
                new Map("Cursed Hollow", "CursedHollow", true, "standard", true),
                new Map("Silver City", "SilverCity", true, "ARAM", false)
            )
        );

        string html = Render(null, holder.Rewards);

        Assert.Contains(
            "<span class=\"label\">Random (QM)</span>, <span class=\"label\">Random (SL)</span> and <span class=\"label\">Random (ARAM)</span> play a random match.",
            html
        );
        Assert.Contains(
            "Map rewards such as <span class=\"label\">Cursed Hollow (SL)</span> pick the map.",
            html
        );
        Assert.Contains(
            "Rank rewards such as <span class=\"label\">Cursed Hollow (Rank SL)</span> also ask for a rank.",
            html
        );
        Assert.Contains(
            "Redeem <span class=\"label\">ReplayId</span> and enter its Heroes Profile replay ID",
            html
        );
        Assert.Contains(
            "<span class=\"label\">ReplayId + YouTube</span> also records the match and uploads it to YouTube.",
            html
        );
        Assert.DoesNotContain("Pick a random match, a map, or a rank.", html);
    }

    [Fact]
    public void Write_QueuedReplay_KeepsTheHowToWithTheWaitingList()
    {
        string html = Render(
            new[]
            {
                new RewardQueueItem { Request = new RewardRequest { Login = "Kazpa <coach>" } },
            }
        );

        Assert.Contains("1 request waiting", html);
        Assert.DoesNotContain("The queue is empty.", html);
        Assert.Contains("Kazpa &lt;coach&gt;", html);
        Assert.Contains("Request a specific replay.", html);
        Assert.Contains("Hero numbers.", html);
    }

    private static string Render(
        IReadOnlyList<RewardQueueItem> items,
        IReadOnlyList<SupportedReward> rewards = null
    )
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-queue-" + Path.GetRandomFileName());
        try
        {
            QueueBoard.Write(path, items, rewards);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class MapOnlyGameData : IGameData
    {
        public MapOnlyGameData(params Map[] maps)
        {
            Maps = maps;
        }

        public IReadOnlyDictionary<string, UnitGroup> UnitGroups => null;

        public IReadOnlyList<Hero> Heroes => null;

        public IReadOnlyCollection<string> CoreUnits => null;

        public IReadOnlyCollection<string> BossUnits => null;

        public IReadOnlyCollection<string> VehicleUnits => null;

        public IReadOnlyList<Map> Maps { get; }

        public UnitGroup GetUnitGroup(string unitName) => default;

        public Task LoadDataAsync() => Task.CompletedTask;
    }
}
