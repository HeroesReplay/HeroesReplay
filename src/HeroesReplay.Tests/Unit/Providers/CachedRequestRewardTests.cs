using System;
using System.IO;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Providers;
using Xunit;

namespace HeroesReplay.Tests.Unit.Providers;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class CachedRequestRewardTests
{
    [Fact]
    public void Write_RoundTripsTheRedemptionBesideTheReplay()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-request-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string replay = Path.Combine(directory, "65389756.StormReplay");
            File.WriteAllText(replay, "replay");
            var redemption = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            CachedRequestReward.Write(
                replay,
                new RewardQueueItem
                {
                    Request = new RewardRequest
                    {
                        Login = "salty",
                        PlayerIndex = 3,
                        RedemptionId = redemption,
                    },
                }
            );

            RewardQueueItem read = CachedRequestReward.Read(replay);

            Assert.Equal("salty", read.Request.Login);
            Assert.Equal(3, read.Request.PlayerIndex);
            Assert.Equal(redemption, read.Request.RedemptionId);
            Assert.True(File.Exists(Path.Combine(directory, "65389756.request.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsNullWhenTheSidecarIsMissingOrUseless()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-request-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string replay = Path.Combine(directory, "1.StormReplay");
            File.WriteAllText(replay, "replay");

            Assert.Null(CachedRequestReward.Read(replay));

            CachedRequestReward.Write(replay, new RewardQueueItem());
            Assert.False(File.Exists(CachedRequestReward.PathFor(replay)));

            File.WriteAllText(CachedRequestReward.PathFor(replay), "{not json");
            Assert.Null(CachedRequestReward.Read(replay));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
