using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Outbox;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayMediaObservationTests
{
    private const int ReplayId = 65389750;
    private static readonly DateTime GameDate = new DateTime(
        2026,
        9,
        28,
        18,
        0,
        0,
        DateTimeKind.Utc
    );
    private static readonly DateTime Now = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Stamp = new DateTimeOffset(
        2026,
        9,
        29,
        18,
        0,
        0,
        TimeSpan.Zero
    );

    [Theory]
    [InlineData(
        ReplayRecordingMode.Disabled,
        ReplayPublicationMode.Disabled,
        false,
        ReplayMediaReason.RecordingDisabled,
        ReplayMediaReason.PublicationDisabled
    )]
    [InlineData(
        ReplayRecordingMode.RequestedOnly,
        ReplayPublicationMode.RequestedOnly,
        false,
        ReplayMediaReason.NotRequested,
        ReplayMediaReason.NotRequested
    )]
    [InlineData(
        ReplayRecordingMode.Selected,
        ReplayPublicationMode.Disabled,
        false,
        ReplayMediaReason.NotSelected,
        ReplayMediaReason.PublicationDisabled
    )]
    [InlineData(
        ReplayRecordingMode.Selected,
        ReplayPublicationMode.RequestedOnly,
        false,
        ReplayMediaReason.NotSelected,
        ReplayMediaReason.NotRequested
    )]
    [InlineData(
        ReplayRecordingMode.Selected,
        ReplayPublicationMode.Curated,
        true,
        ReplayMediaReason.RecordedOrdinary,
        ReplayMediaReason.AwaitingCompletion
    )]
    [InlineData(
        ReplayRecordingMode.All,
        ReplayPublicationMode.AllEligible,
        true,
        ReplayMediaReason.RecordedAll,
        ReplayMediaReason.AwaitingCompletion
    )]
    public async Task PreLaunch_PersistsTheRecordingModeWithoutStartingRecording(
        ReplayRecordingMode recording,
        ReplayPublicationMode publication,
        bool record,
        string recordingReason,
        string publicationReason
    )
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        UploadOutbox outbox = new UploadOutbox(temp.Root);

        MediaPolicySnapshot snapshot = await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(recording, publication),
            Now,
            CancellationToken.None
        );
        UploadAttemptManifest manifest = await ReloadAsync(outbox, "replay-" + ReplayId);

        Assert.Equal(record, snapshot.Decision.Record);
        Assert.Equal(recordingReason, snapshot.Decision.RecordingReason);
        Assert.Equal(publicationReason, snapshot.Decision.PublicationReason);
        Assert.False(snapshot.Decision.PublicationCandidate);
        Assert.False(snapshot.RecordingStarted);
        Assert.True(snapshot.Persisted);
        Assert.False(snapshot.Reused);
        Assert.Equal(UploadAttemptState.Prepared, manifest.State);
        Assert.Equal(recordingReason, manifest.Policy.RecordingReason);
        Assert.Equal(ReplayMediaPolicy.PolicyVersion, manifest.Policy.PolicyVersion);
        Assert.False(manifest.Policy.PublicationEvaluated);
        AssertNoLegacyReceipts(temp.Root);
        Assert.Empty(await outbox.ListOpenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RequestedReplay_PersistsTheRequestedDecision()
    {
        using var temp = new TempAttempts();
        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                Loaded(RecordRequest(upload: true)),
                Settings(ReplayRecordingMode.RequestedOnly, ReplayPublicationMode.RequestedOnly),
                Now,
                CancellationToken.None
            );

        Assert.True(snapshot.Decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedRequested, snapshot.Decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.AwaitingCompletion, snapshot.Decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, snapshot.Decision.Priority);
        Assert.Equal("Li-Ming", snapshot.Decision.FocusHero);
    }

    [Fact]
    public async Task NotableReplay_IsSelectedWhenPublicationIsDisabled()
    {
        using var temp = new TempAttempts();
        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                Loaded(pentakill: true),
                Settings(ReplayRecordingMode.Selected, ReplayPublicationMode.Disabled),
                Now,
                CancellationToken.None
            );

        Assert.True(snapshot.Decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedNotable, snapshot.Decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.PublicationDisabled, snapshot.Decision.PublicationReason);
        Assert.True(snapshot.Decision.Score.NotableStrength > 0);
        Assert.Empty(snapshot.Decision.NotableEvents);
    }

    [Fact]
    public async Task PreLaunch_PersistsIdentityAndScoreEvidence()
    {
        using var temp = new TempAttempts();
        LoadedReplay loaded = Loaded(RecordRequest(upload: false));
        ReplayMediaPolicySettings settings = Settings(
            ReplayRecordingMode.All,
            ReplayPublicationMode.AllEligible
        );
        ReplayMediaDecision fresh = ReplayMediaPolicy.Evaluate(
            ReplayMediaFacts.From(loaded, false, false, false),
            settings,
            Now
        );
        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(loaded, settings, Now, CancellationToken.None);
        UploadAttemptManifest manifest = await ReloadAsync(
            new UploadOutbox(temp.Root),
            "replay-" + ReplayId
        );

        Assert.Equal("Li-Ming", ReplayMediaFacts.From(loaded, false, false, false).FocusHero);
        Assert.Equal(fresh.Score, snapshot.Decision.Score);
        Assert.Equal(fresh.CandidateExpiresAtUtc, snapshot.Decision.CandidateExpiresAtUtc);
        Assert.Equal(ReplayId, snapshot.Decision.ReplayId);
        Assert.Equal(GameDate, snapshot.Decision.GameDateUtc);
        Assert.Equal(DateTimeKind.Utc, snapshot.Decision.GameDateUtc.Value.Kind);
        Assert.Equal("2.55", snapshot.Decision.GameVersion);
        Assert.Equal("Dragon Shire", snapshot.Decision.Map);
        Assert.Equal("Storm League", snapshot.Decision.GameMode);
        Assert.Equal("Diamond 3", snapshot.Decision.Rank);
        Assert.Equal(2500, snapshot.Decision.AverageMmr);
        Assert.Equal("Li-Ming", snapshot.Decision.FocusHero);
        Assert.Equal(ReplayId, manifest.Policy.ReplayId);
        Assert.Equal(fresh.Score.Total, manifest.Policy.Score.Total);
        Assert.Equal(Now, snapshot.Decision.EvaluatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, snapshot.Decision.EvaluatedAtUtc.Kind);
    }

    [Fact]
    public async Task MissingIdentity_IsNotPersisted()
    {
        using var temp = new TempAttempts();
        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                new LoadedReplay(),
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.False(snapshot.Persisted);
        Assert.False(snapshot.RecordingStarted);
        Assert.Equal(ReplayMediaReason.MissingIdentity, snapshot.Decision.RecordingReason);
        Assert.False(Directory.Exists(temp.Root));
    }

    [Fact]
    public async Task LocalClock_IsNotPersisted()
    {
        using var temp = new TempAttempts();
        DateTime local = DateTime.SpecifyKind(Now, DateTimeKind.Local);
        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                Loaded(),
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                local,
                CancellationToken.None
            );

        Assert.False(snapshot.Persisted);
        Assert.Equal(ReplayMediaReason.ClockNotUtc, snapshot.Decision.RecordingReason);
        Assert.False(snapshot.Decision.Record);
        Assert.False(Directory.Exists(temp.Root));
    }

    [Fact]
    public async Task Restart_ReusesThePersistedDecisionUnderNewSettings()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        LoadedReplay loaded = Loaded();
        MediaPolicySnapshot first = await log.RecordPreLaunchAsync(
            loaded,
            Settings(ReplayRecordingMode.Selected, ReplayPublicationMode.Disabled),
            Now,
            CancellationToken.None
        );
        ReplayMediaDecision changed = ReplayMediaPolicy.Evaluate(
            ReplayMediaFacts.From(loaded, false, false, false),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now
        );

        MediaPolicySnapshot second = await log.RecordPreLaunchAsync(
            loaded,
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now.AddHours(1),
            CancellationToken.None
        );

        Assert.True(second.Reused);
        Assert.True(second.Persisted);
        Assert.Equal(first.Decision.PolicyVersion, second.Decision.PolicyVersion);
        Assert.Equal(first.Decision.Record, second.Decision.Record);
        Assert.Equal(first.Decision.RecordingReason, second.Decision.RecordingReason);
        Assert.Equal(first.Decision.Score, second.Decision.Score);
        Assert.Equal(first.Decision.CandidateExpiresAtUtc, second.Decision.CandidateExpiresAtUtc);
        Assert.NotEqual(changed.RecordingReason, second.Decision.RecordingReason);
        Assert.Single(Directory.GetDirectories(temp.Root));
    }

    [Fact]
    public async Task Restart_KeepsAPlantedPolicyVersion()
    {
        using var temp = new TempAttempts();
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        await Require(
            outbox.SavePolicyAsync(
                "replay-" + ReplayId,
                ReplayId,
                Stamp,
                Planted("0", ReplayMediaReason.AwaitingCompletion),
                replaceOpen: false,
                CancellationToken.None
            )
        );
        LoadedReplay loaded = Loaded();
        ReplayMediaPolicySettings newer = Settings(
            ReplayRecordingMode.All,
            ReplayPublicationMode.AllEligible
        );
        ReplayMediaDecision fresh = ReplayMediaPolicy.Evaluate(
            ReplayMediaFacts.From(loaded, false, false, false),
            newer,
            Now
        );

        MediaPolicySnapshot reused = await Log(temp)
            .RecordPreLaunchAsync(loaded, newer, Now, CancellationToken.None);

        Assert.True(reused.Reused);
        Assert.Equal("0", reused.Decision.PolicyVersion);
        Assert.Equal(42, reused.Decision.Score.Total);
        Assert.Equal(GameDate.AddDays(3), reused.Decision.CandidateExpiresAtUtc);
        Assert.Equal(ReplayMediaReason.RecordedOrdinary, reused.Decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.AwaitingCompletion, reused.Decision.PublicationReason);
        Assert.Equal("1", fresh.PolicyVersion);
        Assert.NotEqual(reused.Decision.Score.Total, fresh.Score.Total);
    }

    [Fact]
    public async Task Publication_PromotesTheStoredDecisionAndIgnoresNewerSettings()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        await Require(
            outbox.SavePolicyAsync(
                "replay-" + ReplayId,
                ReplayId,
                Stamp,
                Planted("0", ReplayMediaReason.AwaitingCompletion),
                false,
                CancellationToken.None
            )
        );
        await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );

        MediaPolicySnapshot published = await log.RecordPublicationAsync(
            Loaded(),
            Verified(ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "match.mp4"))),
            CancellationToken.None
        );
        UploadAttemptManifest manifest = await ReloadAsync(outbox, "replay-" + ReplayId);
        MediaPolicySnapshot again = await log.RecordPublicationAsync(
            Loaded(),
            Verified(
                ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "match.mp4")),
                MatchOutcome.Canceled
            ),
            CancellationToken.None
        );

        Assert.True(published.Persisted);
        Assert.False(published.RecordingStarted);
        Assert.True(published.Decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, published.Decision.PublicationReason);
        Assert.Equal("0", published.Decision.PolicyVersion);
        Assert.Equal(42, published.Decision.Score.Total);
        Assert.Equal(Now, published.Decision.EvaluatedAtUtc);
        Assert.True(published.Decision.Record);
        Assert.Equal(UploadAttemptState.Prepared, manifest.State);
        Assert.True(manifest.Policy.PublicationEvaluated);
        Assert.Empty(await outbox.ListOpenAsync(CancellationToken.None));
        Assert.True(again.Reused);
        Assert.Equal(ReplayMediaReason.EligibleCurated, again.Decision.PublicationReason);
        AssertNoLegacyReceipts(temp.Root);
    }

    [Theory]
    [InlineData(MatchOutcome.VersionMismatch)]
    [InlineData(MatchOutcome.RegionUnavailable)]
    [InlineData(MatchOutcome.BuildNotInstalled)]
    [InlineData(MatchOutcome.ClientCrashed)]
    [InlineData(MatchOutcome.ClientHung)]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.Stopped)]
    [InlineData(MatchOutcome.Canceled)]
    [InlineData(MatchOutcome.None)]
    public async Task Publication_UnverifiedOutcome_StaysIneligible(MatchOutcome outcome)
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );

        MediaPolicySnapshot published = await log.RecordPublicationAsync(
            Loaded(),
            Verified(ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "match.mp4")), outcome),
            CancellationToken.None
        );
        UploadAttemptManifest manifest = await ReloadAsync(
            new UploadOutbox(temp.Root),
            "replay-" + ReplayId
        );

        Assert.False(published.Decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Incomplete, published.Decision.PublicationReason);
        Assert.Equal(ReplayMediaReason.RecordedAll, published.Decision.RecordingReason);
        Assert.Equal(UploadAttemptState.Prepared, manifest.State);
    }

    [Fact]
    public async Task Publication_MissingHudClock_IsNotEligible()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );

        MediaPolicySnapshot published = await log.RecordPublicationAsync(
            Loaded(),
            new MediaPublicationFacts
            {
                Outcome = MatchOutcome.VerifiedCompleted,
                MatchClockSeen = false,
                HudSamples = 4,
                RecordedFor = TimeSpan.FromMinutes(3),
                Recording = ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "match.mp4")),
            },
            CancellationToken.None
        );

        Assert.Equal(ReplayMediaReason.Incomplete, published.Decision.PublicationReason);
        Assert.False(published.Decision.PublicationCandidate);
    }

    [Fact]
    public async Task Publication_ShortRecording_IsNotEligible()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );

        MediaPolicySnapshot published = await log.RecordPublicationAsync(
            Loaded(),
            Verified(
                ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "match.mp4")),
                samples: 1,
                recordedFor: TimeSpan.FromSeconds(90)
            ),
            CancellationToken.None
        );

        Assert.Equal(ReplayMediaReason.Incomplete, published.Decision.PublicationReason);
    }

    [Fact]
    public async Task Publication_AbsentMedia_IsNotEligible()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );

        MediaPolicySnapshot missing = await log.RecordPublicationAsync(
            Loaded(),
            Verified(null),
            CancellationToken.None
        );

        Assert.Equal(ReplayMediaReason.MediaNotFinalized, missing.Decision.PublicationReason);
        Assert.False(missing.Decision.PublicationCandidate);
        Assert.Equal(
            UploadAttemptState.Prepared,
            (await ReloadAsync(new UploadOutbox(temp.Root), "replay-" + ReplayId)).State
        );
    }

    [Fact]
    public async Task AlreadyOnYouTube_IsNotEligible()
    {
        using var temp = new TempAttempts();
        LoadedReplay loaded = Loaded();
        loaded.AlreadyOnYouTube = true;
        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                loaded,
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.False(snapshot.Decision.Record);
        Assert.Equal(ReplayMediaReason.AlreadyPublished, snapshot.Decision.RecordingReason);
        Assert.False(snapshot.Decision.PublicationCandidate);
        Assert.False(
            SessionMedia.ShouldRecord(new OBSSettings { RecordingEnabled = true }, loaded)
        );
    }

    [Fact]
    public async Task AnotherPreparedAttempt_IsInTheOutbox()
    {
        using var temp = new TempAttempts();
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        await Require(
            outbox.PrepareAsync("other-attempt", ReplayId, Stamp, CancellationToken.None)
        );

        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                Loaded(),
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.Equal(ReplayMediaReason.InOutbox, snapshot.Decision.RecordingReason);
        Assert.False(snapshot.Decision.PublicationCandidate);
        Assert.Equal(
            UploadAttemptState.Prepared,
            (await ReloadAsync(outbox, "replay-" + ReplayId)).State
        );
    }

    [Fact]
    public async Task OwnAttempt_IsNotAnOutboxDuplicate()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );

        MediaPolicySnapshot published = await log.RecordPublicationAsync(
            Loaded(),
            Verified(ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "owned.mp4"))),
            CancellationToken.None
        );

        Assert.Equal(ReplayMediaReason.EligibleAll, published.Decision.PublicationReason);
        Assert.True(published.Decision.PublicationCandidate);
    }

    [Fact]
    public async Task UploadedSibling_IsAlreadyPublished()
    {
        using var temp = new TempAttempts();
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        await ReachUploadedAsync(outbox, "uploaded-sibling", ReplayId, temp.Root);

        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                Loaded(),
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.Equal(ReplayMediaReason.AlreadyPublished, snapshot.Decision.RecordingReason);
        Assert.NotEqual(ReplayMediaReason.InOutbox, snapshot.Decision.RecordingReason);
        AssertNoLegacyReceipts(temp.Root);
    }

    [Fact]
    public async Task AlreadyScheduled_IsNotEligible()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );
        MediaPublicationFacts verified = Verified(
            ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "match.mp4"))
        );

        MediaPolicySnapshot published = await log.RecordPublicationAsync(
            Loaded(),
            new MediaPublicationFacts
            {
                Outcome = verified.Outcome,
                MatchClockSeen = true,
                HudSamples = verified.HudSamples,
                RecordedFor = verified.RecordedFor,
                Recording = verified.Recording,
                AlreadyScheduled = true,
            },
            CancellationToken.None
        );

        Assert.Equal(ReplayMediaReason.AlreadyScheduled, published.Decision.PublicationReason);
        Assert.False(published.Decision.PublicationCandidate);
    }

    [Fact]
    public async Task InvalidSettings_AreReusedAndStayIneligible()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        LoadedReplay loaded = Loaded();
        var invalid = new ReplayMediaPolicySettings
        {
            Version = "",
            RecordingMode = (ReplayRecordingMode)(-1),
            PublicationMode = (ReplayPublicationMode)(-1),
        };
        MediaPolicySnapshot first = await log.RecordPreLaunchAsync(
            loaded,
            invalid,
            Now,
            CancellationToken.None
        );
        MediaPolicySnapshot reused = await log.RecordPreLaunchAsync(
            loaded,
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );
        MediaPolicySnapshot published = await log.RecordPublicationAsync(
            loaded,
            Verified(ObsRecordingResult.FinalizedAt(Path.Combine(temp.Root, "match.mp4"))),
            CancellationToken.None
        );

        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, first.Decision.RecordingReason);
        Assert.True(first.Persisted);
        Assert.False(first.Decision.Record);
        Assert.True(reused.Reused);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, reused.Decision.RecordingReason);
        Assert.False(published.Decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, published.Decision.PublicationReason);
        Assert.Equal(
            UploadAttemptState.Prepared,
            (await ReloadAsync(new UploadOutbox(temp.Root), "replay-" + ReplayId)).State
        );
    }

    [Fact]
    public async Task CorruptManifest_IsNotOverwrittenOrReevaluated()
    {
        using var temp = new TempAttempts();
        string attemptId = "replay-" + ReplayId;
        string directory = Path.Combine(temp.Root, attemptId);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, UploadAttemptStore.ManifestFileName);
        await File.WriteAllTextAsync(path, "{not-json");
        string before = await File.ReadAllTextAsync(path);

        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                Loaded(),
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.False(snapshot.Persisted);
        Assert.Equal(ReplayMediaReason.MissingInput, snapshot.Decision.RecordingReason);
        Assert.NotEqual(ReplayMediaReason.RecordedAll, snapshot.Decision.RecordingReason);
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task MalformedPolicy_IsNotOverwritten()
    {
        using var temp = new TempAttempts();
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        string attemptId = "replay-" + ReplayId;
        await Require(outbox.PrepareAsync(attemptId, ReplayId, Stamp, CancellationToken.None));
        string path = outbox.ManifestPath(attemptId);
        string broken = await BrokenPolicyAsync(path);
        await File.WriteAllTextAsync(path, broken);

        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                Loaded(),
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.False(snapshot.Persisted);
        Assert.Equal(broken, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ManifestWithoutPolicy_StillLoadsAndOmitsTheField()
    {
        using var temp = new TempAttempts();
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        await Require(
            outbox.PrepareAsync("replay-" + ReplayId, ReplayId, Stamp, CancellationToken.None)
        );
        string json = await File.ReadAllTextAsync(outbox.ManifestPath("replay-" + ReplayId));
        UploadAttemptManifest loaded = await ReloadAsync(outbox, "replay-" + ReplayId);

        Assert.Null(loaded.Policy);
        Assert.DoesNotContain("\"Policy\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnspecifiedPolicyDate_RoundTripsAsUtc()
    {
        using var temp = new TempAttempts();
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        DateTime unspecified = DateTime.SpecifyKind(GameDate.AddDays(3), DateTimeKind.Unspecified);
        UploadAttemptPolicy planted = Planted(
            ReplayMediaPolicy.PolicyVersion,
            ReplayMediaReason.AwaitingCompletion
        );
        planted = new UploadAttemptPolicy
        {
            PolicyVersion = planted.PolicyVersion,
            ConfigurationVersion = planted.ConfigurationVersion,
            Record = planted.Record,
            RecordingReason = planted.RecordingReason,
            PublicationCandidate = planted.PublicationCandidate,
            PublicationReason = planted.PublicationReason,
            Priority = planted.Priority,
            RecordingMode = planted.RecordingMode,
            PublicationMode = planted.PublicationMode,
            Score = planted.Score,
            ExpiresAtUtc = unspecified,
            EvaluatedAtUtc = unspecified,
            ReplayId = planted.ReplayId,
            GameDateUtc = unspecified,
        };
        await Require(
            outbox.SavePolicyAsync(
                "replay-" + ReplayId,
                ReplayId,
                Stamp,
                planted,
                false,
                CancellationToken.None
            )
        );

        UploadAttemptManifest loaded = await ReloadAsync(outbox, "replay-" + ReplayId);
        ReplayMediaDecision decision = MediaPolicyManifest.ToDecision(loaded);

        Assert.Equal(DateTimeKind.Unspecified, loaded.Policy.ExpiresAtUtc.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, decision.CandidateExpiresAtUtc.Value.Kind);
        Assert.Equal(unspecified.Ticks, decision.CandidateExpiresAtUtc.Value.Ticks);
        Assert.Equal(DateTimeKind.Utc, decision.EvaluatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, decision.GameDateUtc.Value.Kind);
    }

    [Fact]
    public async Task LocalPolicyDate_IsCorruptAndUnchanged()
    {
        using var temp = new TempAttempts();
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        string attemptId = "replay-" + ReplayId;
        await Require(
            outbox.SavePolicyAsync(
                attemptId,
                ReplayId,
                Stamp,
                Planted(ReplayMediaPolicy.PolicyVersion, ReplayMediaReason.AwaitingCompletion),
                false,
                CancellationToken.None
            )
        );
        string path = outbox.ManifestPath(attemptId);
        string original = await File.ReadAllTextAsync(path);
        string broken = original.Replace(
            "2026-10-01T18:00:00",
            "2026-10-01T18:00:00+02:00",
            StringComparison.Ordinal
        );
        Assert.NotEqual(original, broken);
        await File.WriteAllTextAsync(path, broken);

        UploadAttemptResult loaded = await outbox.LoadAsync(attemptId, CancellationToken.None);

        Assert.False(loaded.Succeeded);
        Assert.Equal(UploadAttemptReasons.ManifestCorrupt, loaded.Reason);
        Assert.Equal(broken, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task LaterRecordingTransition_KeepsThePolicy()
    {
        using var temp = new TempAttempts();
        MediaPolicyAttemptLog log = Log(temp);
        UploadOutbox outbox = new UploadOutbox(temp.Root);
        MediaPolicySnapshot snapshot = await log.RecordPreLaunchAsync(
            Loaded(),
            Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
            Now,
            CancellationToken.None
        );
        Assert.Equal(
            UploadAttemptState.Prepared,
            (await ReloadAsync(outbox, "replay-" + ReplayId)).State
        );

        await Require(
            outbox.BeginRecordingAsync(
                "replay-" + ReplayId,
                Stamp.AddMinutes(1),
                CancellationToken.None
            )
        );
        UploadAttemptManifest moved = await ReloadAsync(outbox, "replay-" + ReplayId);

        Assert.False(snapshot.RecordingStarted);
        Assert.Equal(UploadAttemptState.Recording, moved.State);
        Assert.Equal(snapshot.Decision.RecordingReason, moved.Policy.RecordingReason);
        Assert.Equal(snapshot.Decision.Score.Total, moved.Policy.Score.Total);
        Assert.True(moved.Policy.Record);
    }

    [Fact]
    public async Task PlainReplayIdRequest_IsARequestedRecordingAndPublication()
    {
        // #165: replay 65625279 was redeemed with "ReplayId" and queued Ordinary. Both
        // ReplayId rewards now record and upload with request priority.
        using var temp = new TempAttempts();
        LoadedReplay loaded = Loaded(RecordRequest(upload: false));

        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                loaded,
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.True(snapshot.Decision.Record);
        Assert.Equal(ReplayMediaPriority.Requested, snapshot.Decision.Priority);
        Assert.Equal(ReplayMediaReason.RecordedAll, snapshot.Decision.RecordingReason);
        Assert.NotEqual(ReplayMediaReason.SpectateOnly, snapshot.Decision.PublicationReason);
    }

    [Fact]
    public async Task SpectateOnlyRequest_KeepsTheCurrentRecordFlag()
    {
        using var temp = new TempAttempts();
        LoadedReplay loaded = Loaded(
            new RewardRequest
            {
                Login = "viewer",
                RewardTitle = "Cursed Hollow (SL)",
                RecordAndUpload = false,
            }
        );
        OBSSettings obs = new OBSSettings { RecordingEnabled = true };
        YouTubeSettings youtube = new YouTubeSettings { Enabled = true };

        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                loaded,
                Settings(ReplayRecordingMode.All, ReplayPublicationMode.AllEligible),
                Now,
                CancellationToken.None
            );

        Assert.True(SessionMedia.ShouldRecord(obs, loaded));
        Assert.True(SessionMedia.ShouldWriteYouTubeEntry(youtube, loaded));
        Assert.False(snapshot.Decision.Record);
        Assert.Equal(ReplayMediaReason.SpectateOnly, snapshot.Decision.RecordingReason);
        Assert.False(snapshot.Decision.PublicationCandidate);
        Assert.False(snapshot.RecordingStarted);
        AssertNoLegacyReceipts(temp.Root);
    }

    [Fact]
    public async Task RecordAndUploadRequest_KeepsTheCurrentRecordFlagWhenPolicyIsDisabled()
    {
        using var temp = new TempAttempts();
        LoadedReplay loaded = Loaded(RecordRequest(upload: true));
        OBSSettings obs = new OBSSettings
        {
            RecordingEnabled = false,
            RecordRequestedReplays = true,
        };
        YouTubeSettings youtube = new YouTubeSettings
        {
            Enabled = false,
            UploadRequestedReplays = true,
        };

        MediaPolicySnapshot snapshot = await Log(temp)
            .RecordPreLaunchAsync(
                loaded,
                Settings(ReplayRecordingMode.Disabled, ReplayPublicationMode.Disabled),
                Now,
                CancellationToken.None
            );

        Assert.True(SessionMedia.ShouldRecord(obs, loaded));
        Assert.True(SessionMedia.ShouldWriteYouTubeEntry(youtube, loaded));
        Assert.False(snapshot.Decision.Record);
        Assert.Equal(ReplayMediaReason.RecordingDisabled, snapshot.Decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.PublicationDisabled, snapshot.Decision.PublicationReason);
        AssertNoLegacyReceipts(temp.Root);
    }

    private static MediaPolicyAttemptLog Log(TempAttempts temp)
    {
        Assert.False(
            temp.Root.StartsWith(@"C:\heroesreplay\Data", StringComparison.OrdinalIgnoreCase)
        );
        return new MediaPolicyAttemptLog(temp.Root, logger: null);
    }

    private static ReplayMediaPolicySettings Settings(
        ReplayRecordingMode recording,
        ReplayPublicationMode publication
    )
    {
        return new ReplayMediaPolicySettings
        {
            Version = "1",
            RecordingMode = recording,
            PublicationMode = publication,
            RequireCurrentPatch = false,
        };
    }

    private static LoadedReplay Loaded(RewardRequest request = null, bool pentakill = false)
    {
        return new LoadedReplay
        {
            ReplayId = ReplayId,
            Replay = pentakill ? PentakillReplay() : OrdinaryReplay(),
            HeroesProfileReplay = Profile(),
            RewardQueueItem = request == null ? null : new RewardQueueItem(request, Profile()),
        };
    }

    private static RewardRequest RecordRequest(bool upload)
    {
        return new RewardRequest
        {
            Login = "viewer",
            ReplayId = ReplayId,
            RecordAndUpload = upload,
            PlayerIndex = 0,
        };
    }

    private static HeroesProfileReplay Profile()
    {
        return new HeroesProfileReplay
        {
            Id = ReplayId,
            GameType = "Storm League",
            GameVersion = "2.55",
            Map = "Dragon Shire",
            Rank = "Diamond 3",
            AverageMmr = 2500,
            GameDate = "2026-09-28T18:00:00.0000000Z",
        };
    }

    private static Replay OrdinaryReplay()
    {
        return new Replay
        {
            Timestamp = GameDate,
            ReplayVersion = "2.55",
            Map = "Dragon Shire",
            GameMode = GameMode.StormLeague,
            Players = new[] { Hero("Li-Ming", 0, null) },
        };
    }

    private static Replay PentakillReplay()
    {
        var killerUnit = new ReplayUnit { Name = "HeroWizard" };
        Player killer = Hero("Li-Ming", 0, killerUnit);
        string[] victims = { "Artanis", "Butcher", "Chromie", "Diablo", "E.T.C." };
        var players = new List<Player> { killer };
        int second = 100;
        foreach (string victim in victims)
        {
            players.Add(
                Hero(
                    victim,
                    1,
                    new ReplayUnit
                    {
                        TimeSpanDied = TimeSpan.FromSeconds(second),
                        PlayerKilledBy = killer,
                        UnitKilledBy = killerUnit,
                    }
                )
            );
            second += 3;
        }

        return new Replay
        {
            Timestamp = GameDate,
            ReplayVersion = "2.55",
            Map = "Dragon Shire",
            GameMode = GameMode.StormLeague,
            Players = players.ToArray(),
        };
    }

    private static Player Hero(string name, int team, ReplayUnit death)
    {
        var player = new Player
        {
            Name = name,
            Character = name,
            Team = team,
            PlayerType = PlayerType.Human,
        };
        if (death != null)
        {
            player.HeroUnits = new List<ReplayUnit> { death };
        }

        return player;
    }

    private static UploadAttemptPolicy Planted(string policyVersion, string publicationReason)
    {
        return new UploadAttemptPolicy
        {
            PolicyVersion = policyVersion,
            ConfigurationVersion = "9",
            Record = true,
            RecordingReason = ReplayMediaReason.RecordedOrdinary,
            PublicationCandidate = false,
            PublicationReason = publicationReason,
            Priority = ReplayMediaPriority.Ordinary.ToString(),
            RecordingMode = ReplayRecordingMode.Selected.ToString(),
            PublicationMode = ReplayPublicationMode.Curated.ToString(),
            Score = new UploadAttemptScoreEvidence
            {
                PriorityWeight = 100000,
                Recency = 9,
                NotableStrength = 0,
                Skill = 0,
                Total = 42,
                TieBreakReplayId = ReplayId,
                TieBreakGameDateTicks = GameDate.Ticks,
            },
            ExpiresAtUtc = GameDate.AddDays(3),
            EvaluatedAtUtc = Now,
            PublicationEvaluated = false,
            ReplayId = ReplayId,
            GameDateUtc = GameDate,
            GameVersion = "2.55",
            Map = "Dragon Shire",
            GameMode = "Storm League",
            Rank = "Diamond 3",
            AverageMmr = 2500,
            FocusHero = "Li-Ming",
        };
    }

    private static MediaPublicationFacts Verified(
        ObsRecordingResult recording,
        MatchOutcome outcome = MatchOutcome.VerifiedCompleted,
        int samples = 2,
        TimeSpan? recordedFor = null
    )
    {
        return new MediaPublicationFacts
        {
            Outcome = outcome,
            MatchClockSeen = true,
            HudSamples = samples,
            RecordedFor = recordedFor ?? TimeSpan.FromMinutes(2),
            Recording = recording,
        };
    }

    private static async Task ReachUploadedAsync(
        UploadOutbox outbox,
        string attemptId,
        int replayId,
        string root
    )
    {
        string media = Path.Combine(root, attemptId, "final.mp4");
        await Require(outbox.PrepareAsync(attemptId, replayId, Stamp, CancellationToken.None));
        await Require(
            outbox.BeginRecordingAsync(attemptId, Stamp.AddMinutes(1), CancellationToken.None)
        );
        await Require(
            outbox.FinalizeMediaAsync(
                attemptId,
                media,
                4096,
                "hash-" + attemptId,
                Stamp.AddMinutes(2),
                CancellationToken.None
            )
        );
        await Require(
            outbox.MarkUploadPendingAsync(attemptId, Stamp.AddMinutes(3), CancellationToken.None)
        );
        await Require(
            outbox.DispatchAsync(
                attemptId,
                youtubeEnabled: true,
                dryRun: false,
                operatorRetry: false,
                Stamp.AddMinutes(4),
                CancellationToken.None
            )
        );
        await Require(
            outbox.CompleteAsync(
                attemptId,
                "video-observed",
                Stamp.AddMinutes(5),
                CancellationToken.None
            )
        );
    }

    private static async Task<string> BrokenPolicyAsync(string path)
    {
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WritePropertyName("Policy");
        writer.WriteStartObject();
        writer.WriteString("Record", "yes");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<UploadAttemptManifest> ReloadAsync(
        UploadOutbox outbox,
        string attemptId
    )
    {
        return await Require(outbox.LoadAsync(attemptId, CancellationToken.None));
    }

    private static async Task<UploadAttemptManifest> Require(Task<UploadAttemptResult> call)
    {
        UploadAttemptResult result = await call;
        Assert.True(result.Succeeded, result.Reason);
        Assert.NotNull(result.Manifest);
        return result.Manifest;
    }

    private static void AssertNoLegacyReceipts(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);
            Assert.NotEqual("youtube-entry.json", name);
            Assert.NotEqual("youtube-entry-uploaded.json", name);
            Assert.NotEqual("youtube-dry-run.json", name);
        }
    }

    private sealed class TempAttempts : IDisposable
    {
        public TempAttempts()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "hr-media-policy-" + Guid.NewGuid().ToString("N")
            );
        }

        public string Root { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
