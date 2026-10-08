using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Rewards;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Rewards;

/// <summary>
/// #165: the redemption for replay 65625279 stayed UNFULFILLED. The spectator only wrote a
/// local line and nothing ever told Twitch.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class RedemptionFulfillerTests : IDisposable
{
    private static readonly Guid Redemption = Guid.Parse("0a018521-19d9-437d-904b-4096c971d461");
    private static readonly Guid Reward = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-fulfil-" + Guid.NewGuid().ToString("N")
    );

    public RedemptionFulfillerTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifiedRequest_IsFulfilledOnTwitchOnce()
    {
        Append(65625279, Redemption);
        var twitch = new ScriptedTwitch(RedemptionUpdate.Updated);
        RedemptionFulfiller fulfiller = Fulfiller(twitch);

        Assert.Equal(1, await fulfiller.SendPendingAsync(CancellationToken.None));
        Assert.Equal(0, await fulfiller.SendPendingAsync(CancellationToken.None));

        (string Broadcaster, Guid RewardId, Guid RedemptionId, string Status) call = Assert.Single(
            twitch.Calls
        );
        Assert.Equal("123456", call.Broadcaster);
        Assert.Equal(Reward, call.RewardId);
        Assert.Equal(Redemption, call.RedemptionId);
        Assert.Equal(RewardRedemptionStatus.Fulfilled, call.Status);
        Assert.Contains(
            Redemption.ToString("D") + " fulfilled 65625279",
            File.ReadAllText(Path.Combine(root, RedemptionFulfiller.SentFileName))
        );
    }

    [Fact]
    public async Task NetworkFailure_IsRetriedAndARefusalIsNot()
    {
        Guid refused = Guid.NewGuid();
        Append(65625279, Redemption);
        Append(65625280, refused);
        var twitch = new ScriptedTwitch(RedemptionUpdate.Retry);
        twitch.Results[refused] = RedemptionUpdate.Refused;
        RedemptionFulfiller fulfiller = Fulfiller(twitch);

        Assert.Equal(0, await fulfiller.SendPendingAsync(CancellationToken.None));
        twitch.Results[Redemption] = RedemptionUpdate.Updated;
        Assert.Equal(1, await fulfiller.SendPendingAsync(CancellationToken.None));
        Assert.Equal(0, await fulfiller.SendPendingAsync(CancellationToken.None));

        Assert.Equal(3, twitch.Calls.Count);
        Assert.Single(twitch.Calls, call => call.RedemptionId == refused);
    }

    [Fact]
    public async Task UnverifiedAndOldLines_AreNotSent()
    {
        File.WriteAllLines(
            Path.Combine(root, RedemptionDispositionLog.FileName),
            new[]
            {
                // Written by older builds: a refund for an unplayed session, and a verified
                // session without the reward id.
                "65659620 Refund " + Guid.NewGuid().ToString("D"),
                "65582813 Fulfill " + Guid.NewGuid().ToString("D"),
                "not a line",
            }
        );
        var twitch = new ScriptedTwitch(RedemptionUpdate.Updated);

        Assert.Equal(0, await Fulfiller(twitch).SendPendingAsync(CancellationToken.None));
        Assert.Empty(twitch.Calls);
    }

    [Fact]
    public void Append_SkipsAnUnfulfilledSessionAndRoundTripsTheReward()
    {
        string path = Path.Combine(root, RedemptionDispositionLog.FileName);
        RewardRequest request = Request(Redemption);

        RedemptionDispositionLog.Append(path, 65625279, request, RedemptionEnd.None);
        Assert.False(File.Exists(path));

        RedemptionDispositionLog.Append(path, 65625279, request, RedemptionEnd.Fulfill);
        RedemptionDispositionLine line = Assert.Single(RedemptionDispositionLog.Read(path));

        Assert.Equal(
            new RedemptionDispositionLine(
                65625279,
                RedemptionEnd.Fulfill,
                Redemption,
                Reward,
                "123456"
            ),
            line
        );
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Enabled_OnlyWhereRedemptionsAreHandledAndNotInADryRun(
        bool pubSub,
        bool requests,
        bool dryRun,
        bool expected
    )
    {
        var twitch = new TwitchSettings
        {
            EnablePubSub = pubSub,
            EnableRequests = requests,
            DryRunMode = dryRun,
        };

        Assert.Equal(expected, RedemptionFulfiller.Enabled(twitch));
        Assert.False(RedemptionFulfiller.Enabled(null));
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, RedemptionUpdate.Updated)]
    [InlineData(HttpStatusCode.OK, RedemptionUpdate.Updated)]
    [InlineData(HttpStatusCode.BadRequest, RedemptionUpdate.Refused)]
    [InlineData(HttpStatusCode.Forbidden, RedemptionUpdate.Refused)]
    [InlineData(HttpStatusCode.NotFound, RedemptionUpdate.Refused)]
    [InlineData(HttpStatusCode.Unauthorized, RedemptionUpdate.Retry)]
    [InlineData(HttpStatusCode.TooManyRequests, RedemptionUpdate.Retry)]
    [InlineData(HttpStatusCode.ServiceUnavailable, RedemptionUpdate.Retry)]
    public void Classify_RetriesOnlyWhatCanSucceedLater(
        HttpStatusCode code,
        RedemptionUpdate expected
    )
    {
        Assert.Equal(expected, HelixRedemptionStatus.Classify(code));
    }

    /// <summary>
    /// #351: the download role records a cancel for a request whose replay can never be
    /// downloaded. twitch connect sends it once, through the redemption canceller.
    /// </summary>
    [Fact]
    public async Task UnplayableRequest_IsCancelledOnTwitchOnce()
    {
        Append(65625279, Redemption, RedemptionEnd.Cancel);
        var twitch = new ScriptedTwitch(RedemptionUpdate.Updated);
        RedemptionFulfiller fulfiller = Fulfiller(twitch);

        Assert.Equal(1, await fulfiller.SendPendingAsync(CancellationToken.None));
        Assert.Equal(0, await fulfiller.SendPendingAsync(CancellationToken.None));

        (string Broadcaster, Guid RewardId, Guid RedemptionId, string Status) call = Assert.Single(
            twitch.Calls
        );
        Assert.Equal("123456", call.Broadcaster);
        Assert.Equal(Reward, call.RewardId);
        Assert.Equal(Redemption, call.RedemptionId);
        Assert.Equal(RewardRedemptionStatus.Canceled, call.Status);
        Assert.Contains(
            Redemption.ToString("D") + " canceled 65625279",
            File.ReadAllText(Path.Combine(root, RedemptionFulfiller.SentFileName))
        );
    }

    [Fact]
    public async Task CancelNetworkFailure_IsRetriedAndARefusalIsNot()
    {
        Guid refused = Guid.NewGuid();
        Append(65625279, Redemption, RedemptionEnd.Cancel);
        Append(65625280, refused, RedemptionEnd.Cancel);
        var twitch = new ScriptedTwitch(RedemptionUpdate.Retry);
        twitch.Results[refused] = RedemptionUpdate.Refused;
        RedemptionFulfiller fulfiller = Fulfiller(twitch);

        Assert.Equal(0, await fulfiller.SendPendingAsync(CancellationToken.None));
        twitch.Results[Redemption] = RedemptionUpdate.Updated;
        Assert.Equal(1, await fulfiller.SendPendingAsync(CancellationToken.None));
        Assert.Equal(0, await fulfiller.SendPendingAsync(CancellationToken.None));

        Assert.Equal(3, twitch.Calls.Count);
        Assert.Single(twitch.Calls, call => call.RedemptionId == refused);
        Assert.All(
            twitch.Calls,
            call => Assert.Equal(RewardRedemptionStatus.Canceled, call.Status)
        );
    }

    /// <summary>A played and verified match is never refunded, whichever line came first.</summary>
    [Fact]
    public async Task VerifiedRedemption_IsNeverCancelled()
    {
        Append(65625279, Redemption, RedemptionEnd.Cancel);
        Append(65625279, Redemption, RedemptionEnd.Fulfill);
        var twitch = new ScriptedTwitch(RedemptionUpdate.Updated);

        Assert.Equal(1, await Fulfiller(twitch).SendPendingAsync(CancellationToken.None));

        (string Broadcaster, Guid RewardId, Guid RedemptionId, string Status) call = Assert.Single(
            twitch.Calls
        );
        Assert.Equal(RewardRedemptionStatus.Fulfilled, call.Status);
    }

    [Fact]
    public void Recorded_FulfilWinsOverCancelAndAnOlderBuildSkipsTheCancelWord()
    {
        string path = Path.Combine(root, RedemptionDispositionLog.FileName);
        Guid other = Guid.NewGuid();

        Assert.Equal(RedemptionEnd.None, RedemptionDispositionLog.Recorded(path, Redemption));
        Append(65625279, Redemption, RedemptionEnd.Cancel);
        Append(65625280, other, RedemptionEnd.Cancel);
        Append(65625280, other, RedemptionEnd.Fulfill);

        Assert.Equal(RedemptionEnd.Cancel, RedemptionDispositionLog.Recorded(path, Redemption));
        Assert.Equal(RedemptionEnd.Fulfill, RedemptionDispositionLog.Recorded(path, other));
        Assert.Equal(RedemptionEnd.None, RedemptionDispositionLog.Recorded(path, Guid.Empty));
        Assert.StartsWith(
            "65625279 Cancel " + Redemption.ToString("D"),
            File.ReadAllLines(path)[0],
            StringComparison.Ordinal
        );
    }

    private void Append(int replayId, Guid redemption, RedemptionEnd end = RedemptionEnd.Fulfill)
    {
        RedemptionDispositionLog.Append(
            Path.Combine(root, RedemptionDispositionLog.FileName),
            replayId,
            Request(redemption),
            end
        );
    }

    private static RewardRequest Request(Guid redemption) =>
        new()
        {
            Login = "zemill",
            RedemptionId = redemption,
            RewardId = Reward,
            BroadcasterId = "123456",
            RewardTitle = "ReplayId",
            ReplayId = 65625279,
        };

    /// <summary>The dev box (requests off, Twitch:DryRunMode) never cancels or fulfils (#146).</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task DryRunOrRequestsOff_SendsNothing(bool requests, bool dryRun)
    {
        Append(65625279, Redemption, RedemptionEnd.Cancel);
        Append(65625280, Guid.NewGuid(), RedemptionEnd.Fulfill);
        var twitch = new ScriptedTwitch(RedemptionUpdate.Updated);
        RedemptionFulfiller fulfiller = Fulfiller(
            twitch,
            new TwitchSettings { EnableRequests = requests, DryRunMode = dryRun }
        );

        await fulfiller.RunAsync(CancellationToken.None);

        Assert.Empty(twitch.Calls);
        Assert.False(File.Exists(Path.Combine(root, RedemptionFulfiller.SentFileName)));
    }

    private RedemptionFulfiller Fulfiller(
        IRedemptionStatusClient twitch,
        TwitchSettings settings = null
    ) =>
        new(
            NullLogger<RedemptionFulfiller>.Instance,
            new AppSettings
            {
                Location = new LocationSettings { DataDirectory = root },
                Twitch = settings ?? new TwitchSettings { EnableRequests = true },
            },
            twitch,
            new RedemptionCanceller(twitch)
        );

    private sealed class ScriptedTwitch : IRedemptionStatusClient
    {
        private readonly RedemptionUpdate fallback;

        public ScriptedTwitch(RedemptionUpdate fallback)
        {
            this.fallback = fallback;
        }

        public Dictionary<Guid, RedemptionUpdate> Results { get; } = new();

        public List<(
            string Broadcaster,
            Guid RewardId,
            Guid RedemptionId,
            string Status
        )> Calls { get; } = new();

        public Task<RedemptionUpdate> UpdateAsync(
            string broadcasterId,
            Guid rewardId,
            Guid redemptionId,
            string status,
            CancellationToken cancellationToken
        )
        {
            Calls.Add((broadcasterId, rewardId, redemptionId, status));
            return Task.FromResult(
                Results.TryGetValue(redemptionId, out RedemptionUpdate result) ? result : fallback
            );
        }
    }
}
