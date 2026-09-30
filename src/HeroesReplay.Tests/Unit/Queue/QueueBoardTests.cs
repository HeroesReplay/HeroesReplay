using System.IO;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Queue;
using Xunit;

namespace HeroesReplay.Tests.Unit.Queue;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class QueueBoardTests
{
    [Fact]
    public void Write_EmptyQueue_ExplainsRedeemAndFocusedReplay()
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-queue-" + Path.GetRandomFileName());
        try
        {
            QueueBoard.Write(path, null);

            string html = File.ReadAllText(path);
            Assert.Contains("0 requests waiting", html);
            Assert.Contains("The queue is empty.", html);
            Assert.Contains("Redeem a replay.", html);
            Assert.Contains("channel-point reward", html);
            Assert.Contains("replayId,player", html);
            Assert.Contains("12345678,3", html);
            Assert.Contains("0 is the tenth hero", html);
            Assert.Contains("before that match starts", html);
            Assert.Contains("while they are alive", html);
            Assert.Contains("while they are dead", html);
            Assert.Contains("review a player, coach, or show one person's game", html);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Write_QueuedReplay_KeepsTheHowToWithTheWaitingList()
    {
        string path = Path.Combine(Path.GetTempPath(), "hr-queue-" + Path.GetRandomFileName());
        try
        {
            QueueBoard.Write(
                path,
                new[]
                {
                    new RewardQueueItem
                    {
                        Request = new RewardRequest { Login = "Kazpa <coach>" },
                    },
                }
            );

            string html = File.ReadAllText(path);
            Assert.Contains("1 request waiting", html);
            Assert.DoesNotContain("The queue is empty.", html);
            Assert.Contains("Kazpa &lt;coach&gt;", html);
            Assert.Contains("replayId,player", html);
            Assert.Contains("Coaching view.", html);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
