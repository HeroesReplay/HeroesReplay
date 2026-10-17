using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.Requests;

/// <summary><c>requests.json</c> and the request sidecars keep their bytes on source-generated JSON (#454).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class RequestsJsonTests
{
    [Fact]
    public void RequestQueue_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
            },
            RequestQueue.Options.ListRewardQueueItem
        );

    [Fact]
    public void RequestSidecar_KeepsItsBytes() =>
        JsonParity.AssertSame(
            new JsonSerializerOptions { WriteIndented = true },
            CachedRequestReward.Json.RewardQueueItem
        );
}
