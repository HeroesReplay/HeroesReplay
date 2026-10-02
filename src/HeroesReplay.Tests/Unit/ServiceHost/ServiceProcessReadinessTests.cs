using System;
using System.IO;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceProcessReadinessTests
{
    [Fact]
    public void Describe_NullOcrWrongPrivilegeAndMissingSecretsFailTheirRoles()
    {
        ServiceRoleFacts facts = ServiceRoleChecks.Describe(
            @"C:\heroesreplay\heroesreplay.exe",
            privilegeOk: false,
            ocrResult: null,
            captureOk: true,
            pathsOk: true,
            obsOk: true,
            twitch: new TwitchSettings(),
            cacheWritable: false,
            heroesProfile: new HeroesProfileApiSettings(),
            youtube: new YouTubeSettings { Enabled = false },
            contextWritable: false,
            oauthPresent: false
        );

        Assert.Contains(
            "privilege",
            ServiceRoleChecks.SpectateFailure(facts.Spectate.LaunchPath, facts.Spectate)
        );
        facts.Spectate.PrivilegeOk = true;
        Assert.Contains(
            "OCR",
            ServiceRoleChecks.SpectateFailure(facts.Spectate.LaunchPath, facts.Spectate)
        );
        Assert.Contains("Twitch", ServiceRoleChecks.TwitchFailure(facts.Twitch));
        Assert.Contains("credential", ServiceRoleChecks.DownloadFailure(facts.Download));
        Assert.Null(ServiceRoleChecks.YouTubeFailure(facts.YouTube));
    }

    [Fact]
    public void TwitchFailure_RejectsSuppliedScopeRewardAndPredictionResults()
    {
        Assert.Contains(
            "scopes",
            ServiceRoleChecks.TwitchFailure(
                new TwitchStartupFacts
                {
                    TokenOk = true,
                    ScopesOk = false,
                    RewardsOk = true,
                    PredictionsOk = true,
                }
            )
        );
        Assert.Contains(
            "reward",
            ServiceRoleChecks.TwitchFailure(
                new TwitchStartupFacts
                {
                    TokenOk = true,
                    ScopesOk = true,
                    RewardsOk = false,
                    PredictionsOk = true,
                }
            )
        );
        Assert.Contains(
            "prediction",
            ServiceRoleChecks.TwitchFailure(
                new TwitchStartupFacts
                {
                    TokenOk = true,
                    ScopesOk = true,
                    RewardsOk = true,
                    PredictionsOk = false,
                }
            )
        );
    }

    [Fact]
    public void DownloadFailure_RejectsUnreadyGameDataWithoutWritingMinReplayId()
    {
        Assert.Contains(
            "game data",
            ServiceRoleChecks.DownloadFailure(
                new DownloadStartupFacts
                {
                    CredentialOk = true,
                    CacheWritable = true,
                    GameDataReady = false,
                }
            )
        );
    }

    [Fact]
    public void YouTubeFailure_DisabledSkipsOAuthAndEnabledRequiresAWritableContext()
    {
        Assert.Null(
            ServiceRoleChecks.YouTubeFailure(
                new YouTubeStartupFacts
                {
                    Enabled = false,
                    ContextWritable = false,
                    OAuthRequired = false,
                    OAuthOk = false,
                }
            )
        );
        Assert.Contains(
            "context",
            ServiceRoleChecks.YouTubeFailure(
                new YouTubeStartupFacts
                {
                    Enabled = true,
                    ContextWritable = false,
                    OAuthRequired = true,
                    OAuthOk = false,
                }
            )
        );
        Assert.Contains(
            "OAuth",
            ServiceRoleChecks.YouTubeFailure(
                new YouTubeStartupFacts
                {
                    Enabled = true,
                    ContextWritable = true,
                    OAuthRequired = true,
                    OAuthOk = false,
                }
            )
        );
    }

    [Fact]
    public void TwitchFrom_RequiresScopesForChatEventSubAndPredictions()
    {
        var twitch = new TwitchSettings
        {
            AccessToken = "present",
            ClientId = "present",
            EnableChatBot = true,
            EnableRequests = true,
            EnablePredictions = true,
        };

        Assert.False(ServiceRoleChecks.ScopesCover(null, twitch));
        TwitchStartupFacts missing = ServiceRoleChecks.TwitchFrom(twitch);
        Assert.True(missing.TokenOk);
        Assert.False(missing.ScopesOk);
        Assert.Contains("scopes", ServiceRoleChecks.TwitchFailure(missing));

        twitch.GrantedScopes = "chat:edit channel:read:redemptions channel:manage:predictions";
        Assert.True(ServiceRoleChecks.ScopesCover(twitch.GrantedScopes, twitch));
        TwitchStartupFacts covered = ServiceRoleChecks.TwitchFrom(twitch);
        Assert.True(covered.ScopesOk);
        Assert.True(covered.RewardsOk);
        Assert.True(covered.PredictionsOk);
        Assert.Null(ServiceRoleChecks.TwitchFailure(covered));
    }

    [Fact]
    public void Describe_FailsWhenHeroesProfileGameDataIsNotReady()
    {
        Assert.False(ServiceRoleChecks.HeroesProfileGameDataReady(false, true));
        Assert.False(ServiceRoleChecks.HeroesProfileGameDataReady(true, false));
        Assert.True(ServiceRoleChecks.HeroesProfileGameDataReady(true, true));
        Assert.False(ServiceRoleChecks.MapCatalogPresent(null));
        Assert.True(
            ServiceRoleChecks.MapCatalogPresent(
                new MapSettings { Catalog = new[] { new MapDefinition { Name = "Alterac Pass" } } }
            )
        );

        ServiceRoleFacts facts = ServiceRoleChecks.Describe(
            @"C:\heroesreplay\heroesreplay.exe",
            privilegeOk: true,
            ocrResult: new object(),
            captureOk: true,
            pathsOk: true,
            obsOk: true,
            twitch: new TwitchSettings { AccessToken = "present", ClientId = "present" },
            cacheWritable: true,
            heroesProfile: new HeroesProfileApiSettings { ApiKey = "present" },
            youtube: new YouTubeSettings { Enabled = false },
            contextWritable: true,
            oauthPresent: false,
            gameDataReady: ServiceRoleChecks.HeroesProfileGameDataReady(false, true)
        );

        Assert.False(facts.Download.GameDataReady);
        Assert.Contains("game data", ServiceRoleChecks.DownloadFailure(facts.Download));
        Assert.Null(ServiceRoleChecks.YouTubeFailure(facts.YouTube));
        Assert.Contains(
            "heroesreplay",
            ServiceRoleChecks.SpectateFailure(
                "cmd.exe",
                new SpectateStartupFacts
                {
                    LaunchPath = "cmd.exe",
                    PrivilegeOk = true,
                    OcrResult = new object(),
                    CaptureOk = true,
                    PathsOk = true,
                    ObsOk = true,
                }
            )
        );
    }

    [Fact]
    public void YouTubeFrom_DryRunDoesNotRequireOAuth()
    {
        YouTubeStartupFacts facts = ServiceRoleChecks.YouTubeFrom(
            new YouTubeSettings { Enabled = true, DryRun = true },
            contextWritable: true,
            oauthPresent: false
        );

        Assert.True(facts.Enabled);
        Assert.False(facts.OAuthRequired);
        Assert.Null(ServiceRoleChecks.YouTubeFailure(facts));
    }

    [Fact]
    public void PathsExist_RequiresDataGameAndBattlenet()
    {
        string root = TempDir();
        try
        {
            string data = Path.Combine(root, "data");
            string game = Path.Combine(root, "game");
            string battlenet = Path.Combine(root, "Battle.net.exe");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(game);
            File.WriteAllText(battlenet, "x");

            Assert.True(ServiceRoleChecks.PathsExist(data, game, battlenet));
            Assert.False(
                ServiceRoleChecks.PathsExist(data, game, Path.Combine(root, "missing.exe"))
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ObsPrerequisites_DoNotLaunchObs()
    {
        string root = TempDir();
        try
        {
            string exe = Path.Combine(root, "obs64.exe");
            File.WriteAllText(exe, "x");

            Assert.True(ServiceRoleChecks.ObsPrerequisites(false, null, null));
            Assert.False(ServiceRoleChecks.ObsPrerequisites(true, null, exe));
            Assert.False(
                ServiceRoleChecks.ObsPrerequisites(
                    true,
                    "ws://127.0.0.1:4455",
                    Path.Combine(root, "missing.exe")
                )
            );
            Assert.True(ServiceRoleChecks.ObsPrerequisites(true, "ws://127.0.0.1:4455", exe));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CaptureAvailable_RejectsTheStub()
    {
        Assert.True(ServiceRoleChecks.CaptureAvailable(CaptureMethod.PrintWindow));
        Assert.True(ServiceRoleChecks.CaptureAvailable(CaptureMethod.BitBlt));
        Assert.False(ServiceRoleChecks.CaptureAvailable(CaptureMethod.None));
    }

    [Fact]
    public void DirectoryWritable_ProbeIsRemovedAndIsNotMinReplayId()
    {
        string root = TempDir();
        try
        {
            Assert.True(ServiceRoleChecks.DirectoryWritable(root));
            Assert.Empty(Directory.GetFiles(root));
            Assert.False(File.Exists(Path.Combine(root, "MinReplayId")));
            Assert.False(ServiceRoleChecks.DirectoryWritable(null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadyFile_IsKeyedByNonce()
    {
        string root = TempDir();
        try
        {
            DateTimeOffset readyAt = new DateTimeOffset(2026, 9, 29, 1, 2, 3, TimeSpan.Zero);
            ServiceReadyFile.Report(
                new ServiceReadyReport
                {
                    Role = "download",
                    Nonce = "abc123",
                    Version = "9",
                    ReadyAt = readyAt,
                    HeartbeatAt = readyAt,
                },
                root
            );

            ServiceReadyReport read = ServiceReadyFile.TryRead(
                new ServiceProcessRecord { Name = "download", Nonce = "abc123" },
                root
            );
            Assert.Equal("9", read.Version);
            Assert.Equal(readyAt, read.ReadyAt);
            Assert.Equal(readyAt, read.HeartbeatAt);
            Assert.Null(
                ServiceReadyFile.TryRead(
                    new ServiceProcessRecord { Name = "download", Nonce = "other" },
                    root
                )
            );
            Assert.Null(
                ServiceReadyFile.TryRead(
                    new ServiceProcessRecord { Name = "twitch", Nonce = "abc123" },
                    root
                )
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportFromEnvironment_IgnoresAnUnsafeNonce()
    {
        string previous = Environment.GetEnvironmentVariable(ServiceReadyFile.NonceVariable);
        Environment.SetEnvironmentVariable(ServiceReadyFile.NonceVariable, "../not-a-nonce");
        try
        {
            ServiceReadyFile.ReportFromEnvironment("spectate");
            Assert.False(ServiceReadyFile.IsSafeNonce("../not-a-nonce"));
            Assert.False(ServiceReadyFile.IsSafeNonce(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ServiceReadyFile.NonceVariable, previous);
        }
    }

    [Fact]
    public void TryWriteHeartbeat_IsLaterThanTheReadyFile()
    {
        string root = TempDir();
        try
        {
            DateTimeOffset readyAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
            var record = new ServiceProcessRecord { Name = "spectate", Nonce = "beat123" };
            ServiceReadyFile.Report(
                new ServiceReadyReport
                {
                    Role = "spectate",
                    Nonce = "beat123",
                    Version = "9",
                    ReadyAt = readyAt,
                },
                root
            );

            Assert.False(
                ServiceReadyFile.TryWriteHeartbeat(
                    new ServiceProcessRecord { Name = "spectate", Nonce = "missing" },
                    readyAt.AddSeconds(1),
                    root
                )
            );
            Assert.True(ServiceReadyFile.TryWriteHeartbeat(record, readyAt, root));

            ServiceReadyReport read = ServiceReadyFile.TryRead(record, root);
            Assert.Equal(readyAt, read.ReadyAt);
            Assert.Equal(readyAt.AddTicks(1), read.HeartbeatAt);
            Assert.True(ServiceChildHeartbeat.FollowsReady(read));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-ready-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }
}
