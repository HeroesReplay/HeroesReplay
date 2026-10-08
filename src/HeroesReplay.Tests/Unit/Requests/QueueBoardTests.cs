using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
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
        Assert.Contains("12345678,Name#1234 follows Name#1234", html);
        Assert.DoesNotContain("Hero numbers.", html);
        Assert.Contains("before that match starts", html);
        Assert.Contains("while they are alive", html);
        Assert.Contains("while they are dead", html);
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
        Assert.Contains("Follow one player.", html);
    }

    /// <summary>#351: a retried download and a request that could not be played are both shown.</summary>
    [Fact]
    public void Write_RetriedAndFailedRequests_SayWhatHappened()
    {
        string html = Render(
            new[]
            {
                new RewardQueueItem
                {
                    Request = new RewardRequest { Login = "waiting", ReplayId = 65625300 },
                    HeroesProfileReplay = new HeroesProfileReplay
                    {
                        Id = 65625300,
                        Map = "Cursed Hollow",
                    },
                    Download = new RequestDownload { Attempts = 2, LastError = "HTTP 503" },
                },
            },
            failed: new[]
            {
                new RewardQueueItem
                {
                    Request = new RewardRequest { Login = "zemill <3", ReplayId = 65625279 },
                    HeroesProfileReplay = new HeroesProfileReplay
                    {
                        Id = 65625279,
                        Map = "Braxis Holdout",
                    },
                    Download = new RequestDownload
                    {
                        FailureReason = "Heroes Profile no longer has the replay file (HTTP 404)",
                        RefundRequested = true,
                    },
                },
            }
        );

        Assert.Contains("1 request waiting", html);
        Assert.Contains("65625300 · download retry 2", html);
        Assert.DoesNotContain("HTTP 503", html);
        Assert.Contains("<h2>Could not play</h2>", html);
        Assert.Contains(
            "<span class=\"who\">zemill &lt;3</span> — <span class=\"map\">Braxis Holdout</span> <span class=\"meta\">replay 65625279 · Heroes Profile no longer has the replay file (HTTP 404) · refund requested</span>",
            html
        );
    }

    [Fact]
    public void Write_NoFailures_HasNoCouldNotPlaySection()
    {
        Assert.DoesNotContain("Could not play", Render(null));
    }

    private static string Render(
        IReadOnlyList<RewardQueueItem> items,
        IReadOnlyList<SupportedReward> rewards = null,
        IReadOnlyList<RewardQueueItem> failed = null
    )
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-queue-" + Path.GetRandomFileName());
        try
        {
            QueueBoard.Write(path, items, rewards, failed);
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
