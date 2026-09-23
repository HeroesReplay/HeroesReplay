using System;
using HeroesReplay.Core.Services.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class EventSubRedemptionTests
{
    private const string Notification = """
        {
          "metadata": {
            "message_type": "notification",
            "subscription_type": "channel.channel_points_custom_reward_redemption.add"
          },
          "payload": {
            "event": {
              "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
              "broadcaster_user_id": "1337",
              "user_login": "cooler_user",
              "user_name": "Cooler_User",
              "user_input": "65277396",
              "status": "unfulfilled",
              "reward": {
                "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                "title": "ReplayId",
                "cost": 500,
                "prompt": "Recent ReplayId"
              },
              "redeemed_at": "2020-07-15T17:16:03.17106713Z"
            }
          }
        }
        """;

    [Fact]
    public void TryReadReward_MapsTheRedemptionOntoTheExistingHandler()
    {
        Assert.True(EventSubRedemption.TryReadReward(Notification, out var args));
        Assert.Equal("ReplayId", args.RewardTitle);
        Assert.Equal("cooler_user", args.Login);
        Assert.Equal("Cooler_User", args.DisplayName);
        Assert.Equal("65277396", args.Message);
        Assert.Equal(500, args.RewardCost);
        Assert.Equal(Guid.Parse("17fa2df1-ad76-4804-bfa5-a40ef63efe63"), args.RedemptionId);
    }

    [Fact]
    public void TryReadWelcome_ReadsTheSession()
    {
        const string welcome = """
            {
              "metadata": { "message_type": "session_welcome" },
              "payload": { "session": { "id": "session-1", "keepalive_timeout_seconds": 30 } }
            }
            """;
        Assert.True(
            EventSubRedemption.TryReadWelcome(welcome, out string sessionId, out int keepalive)
        );
        Assert.Equal("session-1", sessionId);
        Assert.Equal(30, keepalive);
    }
}
