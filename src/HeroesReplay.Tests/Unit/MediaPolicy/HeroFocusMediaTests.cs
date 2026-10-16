using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

/// <summary>
/// A focus-hero replay the cycle picked (#433): its media priority sits between a request and a
/// notable match, it keeps a 7-day window, it is recorded while uploads keep up, and it is sent
/// right after the requests.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroFocusMediaTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc);
    private static readonly HeroFocusFacts LiMing = new(
        "Li-Ming",
        "Wiza",
        "Ranged Assassin",
        "Teleport Build"
    );

    [Fact]
    public void AFocusPick_IsFocusPriority_RecordedAndACuratedCandidate()
    {
        ReplayMediaDecision decision = Decide(Input(heroFocus: LiMing));

        Assert.Equal(ReplayMediaPriority.Focus, decision.Priority);
        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedFocus, decision.RecordingReason);
        Assert.True(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
        Assert.Equal(ReplayMediaPolicy.FocusScore, decision.Score.PriorityWeight);
    }

    [Fact]
    public void FocusRanksBelowARequest_AndAboveANotableMatch()
    {
        // Every component at its cap still leaves a focus pick under a request.
        Assert.True(
            ReplayMediaPolicy.FocusScore
                + ReplayMediaPolicy.RecencyHorizonMinutes
                + ReplayMediaPolicy.ScoreComponentCap * 2
                < ReplayMediaPolicy.RequestedScore
        );
        Assert.True(ReplayMediaPolicy.FocusScore > ReplayMediaPolicy.NotableScore);
        ReplayMediaDecision request = Decide(
            Input(heroFocus: LiMing, viewerRequested: true, recordAndUpload: true)
        );
        ReplayMediaDecision spectateOnly = Decide(Input(heroFocus: LiMing, viewerRequested: true));

        Assert.Equal(ReplayMediaPriority.Requested, request.Priority);
        Assert.NotEqual(ReplayMediaPriority.Focus, spectateOnly.Priority);
    }

    [Fact]
    public void AFocusPick_KeepsItsSevenDayWindow_WhereAnOrdinaryReplayIsStale()
    {
        DateTime played = Now.AddDays(-5);
        ReplayMediaDecision focus = Decide(Input(heroFocus: LiMing, gameDate: played));
        ReplayMediaDecision ordinary = Decide(Input(gameDate: played));
        ReplayMediaDecision past = Decide(Input(heroFocus: LiMing, gameDate: Now.AddDays(-8)));

        Assert.True(focus.Record);
        Assert.Equal(played.AddDays(7), focus.CandidateExpiresAtUtc);
        Assert.Equal(ReplayMediaReason.Expired, ordinary.PublicationReason);
        Assert.Equal(ReplayMediaReason.Expired, past.PublicationReason);
        Assert.False(
            ReplayMediaPolicy.IsPastWindow(
                Input(heroFocus: LiMing, gameDate: played),
                Settings(),
                Now
            )
        );
    }

    [Theory]
    [InlineData(-1, ReplayMediaConfigurationError.FocusMaxAgeNegative)]
    [InlineData(4000, ReplayMediaConfigurationError.FocusMaxAgeTooLarge)]
    public void ABadFocusWindow_FailsClosed(int days, string error)
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.FocusCandidateMaxAge = TimeSpan.FromDays(days);

        Assert.Contains(error, ReplayMediaPolicy.Validate(settings));
    }

    [Fact]
    public void CuratedPublication_AdmitsAFocusVideo_LikeANotableOne()
    {
        ReplayMediaDecision decision = Decide(Input(heroFocus: LiMing));
        PublicationDecision focus = Plan(ReplayMediaPriority.Focus);
        PublicationDecision ordinary = Plan(ReplayMediaPriority.Ordinary);

        Assert.True(PublicationAdmit.Decide(decision, 0, Settings()).Allow);
        Assert.True(focus.Allow);
        Assert.False(ordinary.Allow);
        Assert.Equal("ordinary", ordinary.Reason);
    }

    [Fact]
    public void AFocusReplay_IsRecordedWhenThePublicationWindowIsFull_ButNotPastADayOfUploads()
    {
        var full = new RecordingCapInput
        {
            Priority = ReplayMediaPriority.Focus,
            PendingUploads = 10,
            ScheduledAhead = 500,
            Live = true,
            PublicListing = true,
            UploadsPerDay = 20,
        };

        RecordingCapDecision room = RecordingCap.Decide(full, Settings());
        RecordingCapDecision backlog = RecordingCap.Decide(
            new RecordingCapInput
            {
                Priority = ReplayMediaPriority.Focus,
                PendingUploads = 20,
                Live = true,
                PublicListing = true,
                UploadsPerDay = 20,
            },
            Settings()
        );
        RecordingCapDecision ordinary = RecordingCap.Decide(
            new RecordingCapInput
            {
                Priority = ReplayMediaPriority.Ordinary,
                PendingUploads = 10,
                ScheduledAhead = 500,
                Live = true,
                PublicListing = true,
                UploadsPerDay = 20,
            },
            Settings()
        );

        Assert.True(room.Allow);
        Assert.Equal(RecordingCap.Focus, room.Reason);
        Assert.False(backlog.Allow);
        Assert.Equal(RecordingCap.UploadBacklog, backlog.Reason);
        Assert.False(ordinary.Allow);
    }

    [Fact]
    public void TheUploader_SendsRequestsThenFocusThenTheRest()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-focus-send-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            string ordinary = Recording(root, "1", new YouTubeEntry { ReplayId = 1 });
            string focus = Recording(
                root,
                "2",
                new YouTubeEntry
                {
                    ReplayId = 2,
                    HeroFocus = new YouTubeHeroFocus { Hero = "Li-Ming", Role = "Ranged Assassin" },
                }
            );
            string request = Recording(
                root,
                "3",
                new YouTubeEntry { ReplayId = 3, Requested = true }
            );

            IReadOnlyList<string> order = PendingYouTubeUpload.RequestsFirst(
                new[] { ordinary, focus, request },
                "entry.json"
            );

            Assert.Equal(new[] { request, focus, ordinary }, order);
        }
        finally
        {
            TestTemp.Delete(root);
        }
    }

    [Fact]
    public void TheManifest_KeepsTheFocusPriority()
    {
        ReplayMediaDecision decision = Decide(Input(heroFocus: LiMing));

        ReplayMediaDecision read = MediaPolicyManifest.ToDecision(
            new UploadAttemptManifest
            {
                ReplayId = 65550003,
                Policy = MediaPolicyManifest.FromDecision(decision, publicationEvaluated: true),
            }
        );

        Assert.Equal("Focus", MediaPolicyManifest.FromDecision(decision, true).Priority);
        Assert.Equal(ReplayMediaPriority.Focus, read.Priority);
    }

    [Fact]
    public void TheMediaFacts_CarryTheCyclesPick_ButNeverForARequest()
    {
        var loaded = new LoadedReplay
        {
            ReplayId = 65550003,
            Replay = new Replay { Timestamp = Now.AddHours(-2), Players = [] },
            FocusPick = new FocusPick(
                65550003,
                "65550003",
                0,
                "Star#1234",
                "Li-Ming",
                "Wiza",
                "Ranged Assassin",
                "Teleport Build"
            ),
        };

        ReplayMediaPolicyInput facts = ReplayMediaFacts.From(loaded, false, false, false);
        loaded.RewardQueueItem = new RewardQueueItem
        {
            Request = new RewardRequest { Login = "viewer", ReplayId = 65550003 },
        };
        ReplayMediaPolicyInput request = ReplayMediaFacts.From(loaded, false, false, false);

        Assert.Equal(LiMing, facts.HeroFocus);
        Assert.False(facts.ViewerRequested);
        Assert.Null(facts.FocusHero);
        Assert.Null(request.HeroFocus);
        Assert.True(request.ViewerRequested);
    }

    [Fact]
    public void AnEntryWithoutAFocus_WritesNoFocusField()
    {
        string json = JsonSerializer.Serialize(new YouTubeEntry { ReplayId = 1 });

        Assert.DoesNotContain("HeroFocus", json, StringComparison.Ordinal);
    }

    private static string Recording(string root, string name, YouTubeEntry entry)
    {
        string folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "entry.json"), JsonSerializer.Serialize(entry));
        string recording = Path.Combine(folder, "match.mp4");
        File.WriteAllText(recording, "video");
        return recording;
    }

    private static PublicationDecision Plan(ReplayMediaPriority criteria)
    {
        ReplayMediaPolicySettings settings = PublicationSchedule.CanarySettings();
        settings.PublicationMode = ReplayPublicationMode.Curated;
        return PublicationSchedule.Plan(
            settings,
            new PublicationSendFacts { Criteria = criteria, RecordedAtUtc = Now },
            publicListing: true,
            insertsThisQuotaDay: 0,
            new DateTimeOffset(Now),
            Array.Empty<PublicationSample>(),
            "Alterac Pass",
            "Diamond",
            "Li-Ming",
            Array.Empty<string>()
        );
    }

    private static ReplayMediaDecision Decide(ReplayMediaPolicyInput input) =>
        ReplayMediaPolicy.Evaluate(input, Settings(), Now);

    private static ReplayMediaPolicySettings Settings() =>
        new()
        {
            Version = "1",
            RecordingMode = ReplayRecordingMode.Selected,
            PublicationMode = ReplayPublicationMode.Curated,
            OrdinaryCandidateMaxAge = TimeSpan.FromDays(3),
            FocusCandidateMaxAge = TimeSpan.FromDays(7),
        };

    private static ReplayMediaPolicyInput Input(
        HeroFocusFacts heroFocus = null,
        bool viewerRequested = false,
        bool recordAndUpload = false,
        DateTime? gameDate = null
    ) =>
        new()
        {
            ReplayId = 65550003,
            GameDateUtc = gameDate ?? Now.AddHours(-2),
            GameVersion = "2.57.0.98348",
            Map = "Alterac Pass",
            GameMode = "Storm League",
            Rank = "Diamond 3",
            HeroFocus = heroFocus,
            ViewerRequested = viewerRequested,
            RecordAndUpload = recordAndUpload,
            Completion = new ReplayMediaCompletion { IsVerifiedComplete = true },
            Media = new ReplayMediaFinalization { IsFinalized = true, IsCorrelated = true },
        };
}
