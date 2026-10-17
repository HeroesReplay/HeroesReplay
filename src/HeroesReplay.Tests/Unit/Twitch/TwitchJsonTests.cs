using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.Twitch.Rewards;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

/// <summary>The Twitch files and the EventSub body keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchJsonTests
{
    [Fact]
    public void PredictionLedger_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() },
            },
            PredictionLedger.JsonOptions.PredictionLedgerDocument
        );

    [Fact]
    public void PredictionStreaks_KeepTheirBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions { WriteIndented = true },
            PredictionStreakBook.JsonOptions.PredictionStreakBook
        );

    [Fact]
    public void PredictionReport_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions(),
            TwitchJsonContext.Default.PredictionReport
        );

    [Fact]
    public void CustomRewards_KeepTheirBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
            },
            TwitchRewardsManager.Json.IEnumerableSupportedReward
        );

    [Fact]
    public void EventSubSubscribe_SendsTheSameBody()
    {
        var anonymous = new
        {
            type = "channel.channel_points_custom_reward_redemption.add",
            version = "1",
            condition = new { broadcaster_user_id = "123" },
            transport = new { method = "websocket", session_id = "abc" },
        };
        var body = new EventSubSubscribeBody(
            "channel.channel_points_custom_reward_redemption.add",
            "1",
            new EventSubCondition("123"),
            new EventSubTransport("websocket", "abc")
        );

        Assert.Equal(
            JsonParity.Written(new JsonSerializerOptions(), anonymous),
            JsonSerializer.Serialize(body, TwitchJsonContext.Default.EventSubSubscribeBody)
        );
    }
}
