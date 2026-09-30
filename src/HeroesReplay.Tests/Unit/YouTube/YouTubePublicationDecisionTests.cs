using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Media;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubePublicationDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    [Fact]
    public void RequestedOnly_RefusesNotableAndOrdinaryAndAdmitsARequest()
    {
        ReplayMediaPolicySettings settings = Require(
            Policy(
                "All",
                "RequestedOnly",
                """
                , "MinimumHighSkillRank": "Master"
                """
            )
        );
        ReplayMediaDecision notable = Evaluate(settings, events: new[] { Pentakill() });
        ReplayMediaDecision ordinary = Evaluate(settings);
        ReplayMediaDecision request = Evaluate(settings, recordAndUpload: true);

        PublicationDecision notableSend = Send(settings, notable.Priority);
        PublicationDecision ordinarySend = Send(settings, ordinary.Priority);
        PublicationDecision requestSend = Send(settings, request.Priority);

        Assert.Equal(ReplayMediaPriority.Notable, notable.Priority);
        Assert.Equal(ReplayMediaPriority.Ordinary, ordinary.Priority);
        Assert.Equal(ReplayMediaPriority.Requested, request.Priority);
        Assert.False(notableSend.Allow);
        Assert.Equal("not-requested", notableSend.Reason);
        Assert.False(ordinarySend.Allow);
        Assert.Equal("not-requested", ordinarySend.Reason);
        Assert.True(requestSend.Allow);
        Assert.Equal("ready", requestSend.Reason);
    }

    [Fact]
    public void Curated_AdmitsNotableAndHighSkillUnderTheCapAndRefusesOrdinary()
    {
        ReplayMediaPolicySettings settings = Require(
            Policy(
                "All",
                "Curated",
                """
                , "MinimumHighSkillRank": "Master", "MinimumHighSkillMmr": 2800
                """
            )
        );
        ReplayMediaDecision pentakill = Evaluate(settings, events: new[] { Pentakill() });
        ReplayMediaDecision wipe = Evaluate(settings, events: new[] { Wipe() });
        ReplayMediaDecision ranked = Evaluate(settings, rank: "Master 1", mmr: 1000);
        ReplayMediaDecision rated = Evaluate(settings, rank: "Bronze 5", mmr: 2800);
        ReplayMediaDecision ordinary = Evaluate(settings, rank: "Diamond 3", mmr: 2500);
        ReplayMediaDecision request = Evaluate(settings, recordAndUpload: true);

        Assert.Equal(ReplayMediaPriority.Notable, pentakill.Priority);
        Assert.Equal(ReplayMediaPriority.Notable, wipe.Priority);
        Assert.Equal(ReplayMediaPriority.HighSkill, ranked.Priority);
        Assert.Equal(ReplayMediaPriority.HighSkill, rated.Priority);
        Assert.Equal(ReplayMediaPriority.Ordinary, ordinary.Priority);
        Assert.True(Send(settings, From(pentakill)).Allow);
        Assert.True(Send(settings, From(wipe)).Allow);
        Assert.True(Send(settings, From(ranked)).Allow);
        Assert.True(Send(settings, From(rated)).Allow);
        Assert.False(Send(settings, From(ordinary)).Allow);
        Assert.Equal("ordinary", Send(settings, From(ordinary)).Reason);
        Assert.True(Send(settings, From(request)).Allow);
    }

    [Fact]
    public void AllEligible_RefusesOrdinaryOlderThanTheConfiguredAge()
    {
        ReplayMediaPolicySettings settings = Require(
            Policy(
                "All",
                "AllEligible",
                """
                , "OrdinaryCandidateMaxAge": "10:00:00"
                """
            )
        );
        PublicationDecision stale = Send(
            settings,
            ReplayMediaPriority.Ordinary,
            recordedAt: Now.AddHours(-10).AddTicks(-1)
        );
        PublicationDecision fresh = Send(
            settings,
            ReplayMediaPriority.Ordinary,
            recordedAt: Now.AddHours(-10)
        );
        PublicationDecision request = Send(
            settings,
            ReplayMediaPriority.Requested,
            recordedAt: Now.AddDays(-30)
        );

        Assert.Equal(TimeSpan.FromHours(10), settings.OrdinaryCandidateMaxAge);
        Assert.False(stale.Allow);
        Assert.Equal("stale", stale.Reason);
        Assert.True(fresh.Allow);
        Assert.True(request.Allow);
    }

    [Fact]
    public void Disabled_RefusesARequest()
    {
        ReplayMediaPolicySettings settings = Require(Policy("Disabled", "Disabled"));
        var stored = new ReplayMediaDecision
        {
            PublicationCandidate = true,
            PublicationReason = ReplayMediaReason.EligibleRequested,
            Priority = ReplayMediaPriority.Requested,
            GameDateUtc = NowUtc,
        };

        PublicationDecision sent = Send(settings, PublicationSendFacts.FromDecision(stored, Now));

        Assert.False(sent.Allow);
        Assert.Equal("disabled", sent.Reason);
    }

    [Fact]
    public void InvalidValue_Refuses()
    {
        ReplayMediaPolicySettings unreadable = Bind(
            Policy(
                "All",
                "AllEligible",
                """
                , "MaxPublicPerDay": "abc"
                """
            ),
            out IReadOnlyList<string> unreadableErrors
        );
        ReplayMediaPolicySettings negative = Bind(
            Policy(
                "All",
                "AllEligible",
                """
                , "MinimumPublicInterval": "-01:00:00"
                """
            ),
            out IReadOnlyList<string> negativeErrors
        );

        Assert.Contains(ReplayMediaPolicyStartup.Unreadable + ":MaxPublicPerDay", unreadableErrors);
        Assert.Contains(
            ReplayMediaConfigurationError.MinimumPublicIntervalNegative,
            negativeErrors
        );
        Assert.False(Send(unreadable, ReplayMediaPriority.Requested).Allow);
        Assert.Equal("configuration", Send(unreadable, ReplayMediaPriority.Requested).Reason);
        Assert.False(Send(negative, ReplayMediaPriority.Requested).Allow);
        Assert.Equal("configuration", Send(negative, ReplayMediaPriority.Requested).Reason);
    }

    [Fact]
    public void DayCapOfOne_RefusesTheSecondPublicationInsideTheRollingDay()
    {
        ReplayMediaPolicySettings settings = Require(
            Policy(
                "All",
                "AllEligible",
                """
                , "MaxPublicPerDay": 1
                """
            )
        );
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-day-cap-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "publication-reservations.txt");
        try
        {
            PublicationReservationResult first = Reserve(path, settings, "replay-1");
            PublicationReservationResult second = Reserve(path, settings, "replay-2");

            Assert.Equal(1, settings.MaxPublicPerDay);
            Assert.True(first.Allow);
            Assert.False(second.Allow);
            Assert.Equal("day", second.Reason);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MinimumInterval_RefusesInsideAndAllowsOutside()
    {
        ReplayMediaPolicySettings settings = Require(
            Policy(
                "All",
                "Curated",
                """
                , "MinimumPublicInterval": "05:00:00"
                """
            )
        );
        PublicationDecision inside = Send(
            settings,
            ReplayMediaPriority.Notable,
            lastPublic: Now.AddHours(-5).AddTicks(1)
        );
        PublicationDecision outside = Send(
            settings,
            ReplayMediaPriority.Notable,
            lastPublic: Now.AddHours(-5)
        );

        Assert.Equal(TimeSpan.FromHours(5), settings.MinimumPublicInterval);
        Assert.False(inside.Allow);
        Assert.Equal("interval", inside.Reason);
        Assert.True(outside.Allow);
        Assert.Equal("ready", outside.Reason);
    }

    [Fact]
    public void RequestDoesNotBypassMediaGatesOrAlgorithmCaps()
    {
        ReplayMediaPolicySettings settings = Require(Policy("All", "AllEligible"));
        var published = new PublicationSendFacts
        {
            Criteria = ReplayMediaPriority.Requested,
            AlreadyPublished = true,
            RecordedAtUtc = Now,
        };
        var incomplete = new PublicationSendFacts
        {
            Criteria = ReplayMediaPriority.Requested,
            Incomplete = true,
            RecordedAtUtc = Now,
        };
        var uncorrelated = new PublicationSendFacts
        {
            Criteria = ReplayMediaPriority.Requested,
            Uncorrelated = true,
            RecordedAtUtc = Now,
        };

        Assert.False(Send(settings, published).Allow);
        Assert.Equal("already-published", Send(settings, published).Reason);
        Assert.False(Send(settings, incomplete).Allow);
        Assert.Equal("incomplete", Send(settings, incomplete).Reason);
        Assert.False(Send(settings, uncorrelated).Allow);
        Assert.Equal("uncorrelated", Send(settings, uncorrelated).Reason);
        Assert.False(
            Send(
                settings,
                ReplayMediaPriority.Requested,
                inserts: settings.MaxInsertsPerQuotaDay
            ).Allow
        );
        Assert.False(
            Send(
                settings,
                ReplayMediaPriority.Requested,
                publicAt: Recent(settings.MaxPublicPerDay)
            ).Allow
        );
        Assert.False(
            Send(
                settings,
                ReplayMediaPriority.Requested,
                publicAt: Week(settings.MaxPublicPerWeek)
            ).Allow
        );
        Assert.False(
            Send(
                settings,
                ReplayMediaPriority.Requested,
                lastPublic: Now.Add(-settings.MinimumPublicInterval).AddTicks(1)
            ).Allow
        );
    }

    [Fact]
    public void OmittedKeys_KeepCanaryDefaults()
    {
        ReplayMediaPolicySettings settings = Require(
            """
            { "ReplayMedia": { "Version": "1" } }
            """
        );

        AssertCanaryAlgorithm(settings);
        Assert.Equal(ReplayRecordingMode.Disabled, settings.RecordingMode);
        Assert.Equal(ReplayPublicationMode.Disabled, settings.PublicationMode);
    }

    [Fact]
    public void EnvironmentOverlay_OverridesOneAlgorithmKey()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-overlay-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        string baseFile = Path.Combine(root, "appsettings.json");
        string overlay = Path.Combine(root, "appsettings.env.json");
        File.WriteAllText(
            baseFile,
            Policy(
                "All",
                "AllEligible",
                """
                , "MaxPublicPerDay": 6, "MinimumPublicInterval": "02:00:00"
                """
            )
        );
        File.WriteAllText(
            overlay,
            """
            { "ReplayMedia": { "MaxPublicPerDay": 1 } }
            """
        );
        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(baseFile)
                .AddJsonFile(overlay)
                .Build();
            ReplayMediaPolicySettings settings = ReplayMediaPolicyStartup.Require(configuration);
            PublicationDecision second = Send(
                settings,
                ReplayMediaPriority.Requested,
                publicAt: Recent(1)
            );

            Assert.Equal(1, settings.MaxPublicPerDay);
            Assert.Equal(TimeSpan.FromHours(2), settings.MinimumPublicInterval);
            Assert.Equal(30, settings.MaxPublicPerWeek);
            Assert.False(second.Allow);
            Assert.Equal("day", second.Reason);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MapAndHeroCooldowns_UseSeparateThresholds()
    {
        ReplayMediaPolicySettings settings = Require(
            Policy(
                "All",
                "AllEligible",
                """
                , "MapCooldown": "01:00:00", "FeaturedHeroCooldown": "00:30:00"
                """
            )
        );
        PublicationDecision map = Send(
            settings,
            ReplayMediaPriority.Ordinary,
            map: "Alterac Pass",
            lastMap: "alterac pass",
            lastMapAt: Now.AddMinutes(-30),
            hero: "Li-Ming",
            lastHero: "Li-Ming",
            lastHeroAt: Now.AddMinutes(-45)
        );
        PublicationDecision hero = Send(
            settings,
            ReplayMediaPriority.Ordinary,
            map: "Alterac Pass",
            lastMap: "Alterac Pass",
            lastMapAt: Now.AddHours(-2),
            hero: "Li-Ming",
            lastHero: "li-ming",
            lastHeroAt: Now.AddMinutes(-10)
        );

        Assert.True(map.Allow);
        Assert.Equal(1, map.Penalty);
        Assert.Equal("cooldown", map.Reason);
        Assert.True(hero.Allow);
        Assert.Equal(1, hero.Penalty);
    }

    [Fact]
    public void CheckedInAppSettings_BindsCanaryDefaultsAndDoesNotSelectAMode()
    {
        string path = FindRepoFile(Path.Combine("src", "HeroesReplay.CLI", "appsettings.json"));
        string text = File.ReadAllText(path);
        IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(path).Build();

        ReplayMediaPolicySettings settings = ReplayMediaPolicyStartup.Require(configuration);

        AssertCanaryAlgorithm(settings);
        Assert.Equal(ReplayRecordingMode.Disabled, settings.RecordingMode);
        Assert.Equal(ReplayPublicationMode.Disabled, settings.PublicationMode);
        Assert.Contains("\"MaxPublicPerDay\": 6", text, StringComparison.Ordinal);
        Assert.Contains("\"MaxPublicPerWeek\": 30", text, StringComparison.Ordinal);
        Assert.Contains("\"MinimumPublicInterval\": \"02:00:00\"", text, StringComparison.Ordinal);
        Assert.Contains(
            "\"OrdinaryCandidateMaxAge\": \"3.00:00:00\"",
            text,
            StringComparison.Ordinal
        );
        Assert.Contains("\"MapCooldown\": \"08:00:00\"", text, StringComparison.Ordinal);
        Assert.Contains("\"FeaturedHeroCooldown\": \"08:00:00\"", text, StringComparison.Ordinal);
        Assert.Contains("\"ReservedRequestSlotsPerDay\": 2", text, StringComparison.Ordinal);
        Assert.Contains("\"MaxInsertsPerQuotaDay\": 80", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordingMode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicationMode", text, StringComparison.Ordinal);
        Assert.False(Send(settings, ReplayMediaPriority.Requested).Allow);
    }

    private static void AssertCanaryAlgorithm(ReplayMediaPolicySettings settings)
    {
        Assert.Equal(6, settings.MaxPublicPerDay);
        Assert.Equal(30, settings.MaxPublicPerWeek);
        Assert.Equal(TimeSpan.FromHours(2), settings.MinimumPublicInterval);
        Assert.Equal(TimeSpan.FromHours(72), settings.OrdinaryCandidateMaxAge);
        Assert.Equal(TimeSpan.FromHours(8), settings.MapCooldown);
        Assert.Equal(TimeSpan.FromHours(8), settings.FeaturedHeroCooldown);
        Assert.Equal(2, settings.ReservedRequestSlotsPerDay);
        Assert.Equal(80, settings.MaxInsertsPerQuotaDay);
    }

    private static PublicationSendFacts From(ReplayMediaDecision decision)
    {
        return PublicationSendFacts.FromDecision(decision, Now);
    }

    private static PublicationDecision Send(
        ReplayMediaPolicySettings settings,
        ReplayMediaPriority criteria,
        IReadOnlyList<DateTimeOffset> publicAt = null,
        DateTimeOffset? lastPublic = null,
        DateTimeOffset? recordedAt = null,
        int inserts = 0,
        string map = null,
        string lastMap = null,
        DateTimeOffset? lastMapAt = null,
        string hero = null,
        string lastHero = null,
        DateTimeOffset? lastHeroAt = null
    )
    {
        return Send(
            settings,
            new PublicationSendFacts { Criteria = criteria, RecordedAtUtc = recordedAt ?? Now },
            publicAt,
            lastPublic,
            inserts,
            map,
            lastMap,
            lastMapAt,
            hero,
            lastHero,
            lastHeroAt
        );
    }

    private static PublicationDecision Send(
        ReplayMediaPolicySettings settings,
        PublicationSendFacts facts,
        IReadOnlyList<DateTimeOffset> publicAt = null,
        DateTimeOffset? lastPublic = null,
        int inserts = 0,
        string map = null,
        string lastMap = null,
        DateTimeOffset? lastMapAt = null,
        string hero = null,
        string lastHero = null,
        DateTimeOffset? lastHeroAt = null
    )
    {
        return PublicationSchedule.Decide(
            settings,
            facts,
            true,
            inserts,
            Now,
            lastPublic,
            publicAt,
            0,
            map,
            lastMap,
            lastMapAt,
            hero,
            lastHero,
            lastHeroAt
        );
    }

    private static PublicationReservationResult Reserve(
        string path,
        ReplayMediaPolicySettings settings,
        string workKey
    )
    {
        return PublicationReservation.TryReserve(
            path,
            true,
            0,
            Now,
            null,
            new List<DateTimeOffset>(),
            0,
            true,
            Now,
            "Alterac Pass",
            null,
            null,
            "Li-Ming",
            null,
            null,
            workKey,
            settings,
            new PublicationSendFacts
            {
                Criteria = ReplayMediaPriority.Requested,
                RecordedAtUtc = Now,
            }
        );
    }

    private static ReplayMediaDecision Evaluate(
        ReplayMediaPolicySettings settings,
        string rank = "Diamond 3",
        double? mmr = 2500,
        bool recordAndUpload = false,
        IReadOnlyList<TeamKillClip> events = null
    )
    {
        return ReplayMediaPolicy.Evaluate(
            new ReplayMediaPolicyInput
            {
                ReplayId = 65550001,
                GameDateUtc = NowUtc,
                GameVersion = "2.57.0.98304",
                Map = "Alterac Pass",
                GameMode = "Storm League",
                Rank = rank,
                AverageMmr = mmr,
                RecordAndUpload = recordAndUpload,
                NotableEvents = events ?? Array.Empty<TeamKillClip>(),
                Completion = new ReplayMediaCompletion { IsVerifiedComplete = true },
                Media = new ReplayMediaFinalization { IsFinalized = true, IsCorrelated = true },
            },
            settings,
            NowUtc
        );
    }

    private static TeamKillClip Pentakill()
    {
        return new TeamKillClip(
            TeamKillClips.PentakillKind,
            "Li-Ming",
            30,
            34,
            30,
            38,
            "Li-Ming pentakill"
        );
    }

    private static TeamKillClip Wipe()
    {
        return new TeamKillClip(TeamKillClips.TeamWipeKind, "Zul'jin", 40, 44, 40, 48, "team wipe");
    }

    private static List<DateTimeOffset> Recent(int count)
    {
        var times = new List<DateTimeOffset>();
        for (int i = 0; i < count; i++)
        {
            times.Add(Now.AddMinutes(-i));
        }

        return times;
    }

    private static List<DateTimeOffset> Week(int count)
    {
        var times = new List<DateTimeOffset> { Now.AddHours(-1) };
        for (int i = 1; i < count; i++)
        {
            times.Add(Now.AddDays(-2).AddMinutes(-i));
        }

        return times;
    }

    private static string Policy(string recording, string publication, string extra = "")
    {
        return """
                {
                  "ReplayMedia": {
                    "Version": "1",
                    "RecordingMode": "
                """
            + recording
            + """
                ",
                    "PublicationMode": "
                """
            + publication
            + "\""
            + extra
            + """

                  }
                }
                """;
    }

    private static ReplayMediaPolicySettings Require(string json)
    {
        return ReplayMediaPolicyStartup.Require(Config(json));
    }

    private static ReplayMediaPolicySettings Bind(string json, out IReadOnlyList<string> errors)
    {
        return ReplayMediaPolicyStartup.Bind(Config(json), out errors);
    }

    private static IConfiguration Config(string json)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "hr-upload-policy-" + Guid.NewGuid().ToString("N") + ".json"
        );
        File.WriteAllText(path, json);
        try
        {
            return new ConfigurationBuilder().AddJsonFile(path).Build();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string FindRepoFile(string relative)
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
