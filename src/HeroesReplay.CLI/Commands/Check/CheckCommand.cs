using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesClientSDK;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.Dependencies;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Telemetry;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.TwitchExtension;
using Microsoft.Extensions.DependencyInjection;
using OBSWebsocketDotNet;
using TwitchLib.Api.Interfaces;

namespace HeroesReplay.CLI.Commands.Check;

public class CheckCommand : Command
{
    /// <summary>A passing result whose detail starts with this prints as <c>[WARN]</c>.</summary>
    public const string WarningPrefix = "Warning: ";

    /// <summary>
    /// Every target, in the order <c>check --help</c> lists them. <see cref="CheckTarget.InAll"/>
    /// marks the ones a bare <c>check</c> runs; <see cref="AllOrder"/> is their order.
    /// </summary>
    public static readonly IReadOnlyList<CheckTarget> Targets =
    [
        new(
            "config",
            "Bind settings and report which secrets are present.",
            CheckConfigAsync,
            InAll: true
        ),
        new(
            "heroesprofile",
            "Call Heroes Profile GET /replays with the v1 Bearer key.",
            CheckHeroesProfileAsync,
            InAll: true
        ),
        new(
            "obs",
            "Connect to obs-websocket 5, read the server version, verify scene files, and check the active profile and scene collection.",
            CheckObsAsync,
            InAll: true
        ),
        new(
            "twitch",
            "Call Helix GetUsers (and Predictions when enabled) for the configured channel.",
            CheckTwitchAsync,
            InAll: true
        ),
        new(
            "client",
            "Verify windowed 1080p and AhliObs in Heroes of the Storm Variables.txt.",
            CheckClientAsync,
            InAll: true
        ),
        new(
            "connectivity",
            "Probe 1.1.1.1, Twitch, and Heroes Profile without starting an OBS stream.",
            CheckConnectivityAsync,
            InAll: true
        ),
        new(
            "timer",
            "Read the HeroesOfTheStorm_x64 match clock from memory (read-only) and report whether it is running.",
            CheckTimerAsync,
            InAll: false
        ),
        new(
            "twitch-extension",
            "Report TwitchExtension:Enabled, or call uploader/whoami when the extension is on (issue 49).",
            CheckTwitchExtensionAsync,
            InAll: false
        ),
        new(
            "battlenet",
            "Read the Battle.net window and report the Play or Update button.",
            CheckBattleNetAsync,
            InAll: true
        ),
        new(
            "ffmpeg",
            "Resolve ffmpeg and ffprobe (Clips:FfmpegDirectory, the deps install folder, C:\\ffmpeg\\bin, PATH) and report each path and -version line. Fails when one is missing, does not run, or ffmpeg cannot encode libx264; a working build that is not the pinned version is a warning.",
            CheckFfmpegAsync,
            InAll: true
        ),
    ];

    /// <summary>What a bare <c>check</c> runs, in this order.</summary>
    public static readonly IReadOnlyList<string> AllOrder =
    [
        "config",
        "heroesprofile",
        "obs",
        "twitch",
        "client",
        "ffmpeg",
        "battlenet",
        "connectivity",
    ];

    public CheckCommand()
        : base(
            "check",
            "Validate configuration, connectivity to Heroes Profile, OBS, Twitch, and the internet, and the ffmpeg tools clips use."
        )
    {
        Option<string> output = CliOutput.CreateOption(
            "JSON: schemaVersion, ok, code, message, environment, details.checks[] (name, ok, status ok|warn|fail, code check.<target>.<reason>, detail). Applies to every target. Secret values never appear.",
            recursive: true
        );
        Options.Add(output);
        foreach (CheckTarget target in Targets)
        {
            var command = new Command(target.Name, target.Description);
            command.SetAction(
                (parseResult, cancellationToken) =>
                    RunAsync([target], parseResult, output, cancellationToken)
            );
            Subcommands.Add(command);
        }

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    AllOrder.Select(name => Targets.Single(target => target.Name == name)).ToList(),
                    parseResult,
                    output,
                    cancellationToken
                )
        );
    }

    private static async Task<int> RunAsync(
        IReadOnlyList<CheckTarget> targets,
        ParseResult parseResult,
        Option<string> option,
        CancellationToken cancellationToken
    )
    {
        CliOutputFormat format = CliOutput.Format(parseResult, option);
        TextWriter output = CliOutput.Out(parseResult);
        var run = new CheckRun(cancellationToken, CliOutput.Progress(format, parseResult, output));
        var results = new List<CheckResult>();
        foreach (CheckTarget target in targets)
        {
            CheckResult result = await RunTargetAsync(target, run).ConfigureAwait(false);
            results.Add(result);
            if (format == CliOutputFormat.Text)
            {
                WriteText(result, output);
            }
        }

        CliResult<CheckDetails> report = Report(results);
        return format == CliOutputFormat.Json
            ? CliOutput.WriteJson(report, output)
            : CliOutput.ExitCode(report);
    }

    /// <summary>
    /// One target as <c>check &lt;name&gt;</c> and the MCP <c>check_*</c> tools run it: the result
    /// always has a code, and every secret this run resolved is masked in its detail.
    /// </summary>
    public static Task<CheckResult> RunTargetAsync(
        string name,
        CancellationToken cancellationToken
    ) =>
        RunTargetAsync(
            Targets.Single(target => target.Name == name),
            new CheckRun(cancellationToken, TextWriter.Null)
        );

    private static async Task<CheckResult> RunTargetAsync(CheckTarget target, CheckRun run)
    {
        CheckResult result;
        try
        {
            result = await target.Run(run).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            result = Fail(target.Name, e);
        }

        return Finish(result, run.Redaction);
    }

    /// <summary>Fills a missing code (<c>.ok</c> or <c>.error</c>) and masks secrets in the detail.</summary>
    public static CheckResult Finish(CheckResult result, CliRedaction redaction) =>
        result with
        {
            Code = result.Code ?? CheckCodes.For(result.Name, result.Ok ? "ok" : "error"),
            Detail = redaction?.Redact(result.Detail) ?? result.Detail,
        };

    /// <summary>
    /// The <c>check --output json</c> envelope. <c>ok</c> when every check passed (a warning
    /// passes). <c>code</c> is the first failure's code, else the first warning's, else the only
    /// check's code, else <see cref="CheckCodes.AllOk"/>.
    /// </summary>
    public static CliResult<CheckDetails> Report(IReadOnlyList<CheckResult> results)
    {
        List<CheckEntry> checks = results.Select(CheckEntry.From).ToList();
        List<CheckEntry> failed = checks.Where(check => check.Status == CheckEntry.Fail).ToList();
        CheckEntry warned = checks.FirstOrDefault(check => check.Status == CheckEntry.Warn);
        string code =
            failed.FirstOrDefault()?.Code
            ?? warned?.Code
            ?? (checks.Count == 1 ? checks[0].Code : CheckCodes.AllOk);
        string message =
            failed.Count > 0
                ? $"{failed.Count} of {checks.Count} check(s) failed: {string.Join(", ", failed.Select(check => check.Name))}."
            : warned != null
                ? $"{checks.Count} check(s) passed, {checks.Count(check => check.Status == CheckEntry.Warn)} with a warning."
            : $"{checks.Count} check(s) passed.";
        return new CliResult<CheckDetails>
        {
            Ok = failed.Count == 0,
            Code = code,
            Message = message,
            Environment = CliJson.CurrentEnvironment(),
            Details = new CheckDetails(checks),
        };
    }

    public static Task<CheckResult> CheckConfigAsync(CheckRun run)
    {
        try
        {
            using var provider = run.CreateProvider();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            var lines = new List<string>
            {
                $"Heroes Profile API key: {SecretResolver.Describe(settings.HeroesProfileApi?.ApiKey)}",
                $"Heroes Profile v1 URI: {settings.HeroesProfileApi?.ExternalV1BaseUri}",
                $"OBS endpoint: {settings.OBS?.WebSocketEndpoint}",
                $"OBS password: {SecretResolver.Describe(settings.OBS?.WebSocketPassword)}",
                $"OBS streaming enabled: {settings.OBS?.StreamingEnabled == true}",
                $"OBS stream arm (this machine): {(new ObsStreamArm().IsArmed() ? "armed" : "not armed")}",
                $"OBS profile / scene collection: {ObsNames.Profile(settings.OBS)} / {ObsNames.SceneCollection(settings.OBS)}",
                $"OBS report scenes enabled: {DescribeReportScenes(settings)}",
                $"Twitch channel: {NullToMissing(settings.Twitch?.Channel)}",
                $"Twitch access token: {SecretResolver.Describe(settings.Twitch?.AccessToken)}",
                $"Twitch client id: {SecretResolver.Describe(settings.Twitch?.ClientId)}",
            };

            bool hasProfile = !string.IsNullOrWhiteSpace(settings.HeroesProfileApi?.ApiKey);
            return Task.FromResult(
                hasProfile
                    ? new CheckResult(
                        "config",
                        true,
                        string.Join(Environment.NewLine, lines),
                        CheckCodes.ConfigOk
                    )
                    : new CheckResult(
                        "config",
                        false,
                        "Heroes Profile API key is missing. Set HeroesProfileApi:ApiKey to an op:// reference or a token.",
                        CheckCodes.ConfigHeroesProfileKeyMissing
                    )
            );
        }
        catch (Exception e)
        {
            return Task.FromResult(Fail("config", e));
        }
    }

    public static async Task<CheckResult> CheckHeroesProfileAsync(CheckRun run)
    {
        try
        {
            using var provider = run.CreateProvider();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            if (string.IsNullOrWhiteSpace(settings.HeroesProfileApi?.ApiKey))
            {
                return new CheckResult(
                    "heroesprofile",
                    false,
                    "API key is missing. Put `op://Heroes Replay/Heroes Profile API Key/password` in appsettings.secrets.json or set HEROES_REPLAY_HeroesProfileApi__ApiKey.",
                    CheckCodes.HeroesProfileKeyMissing
                );
            }

            IHeroesProfileService api = provider.GetRequiredService<IHeroesProfileService>();
            using Activity activity = HeroesReplayTelemetry.StartSpan(
                "heroesreplay.check.heroesprofile"
            );
            int maxId = await api.GetMaxReplayIdAsync();
            activity?.SetTag("heroesprofile.max_id", maxId);
            bool ok = maxId > 0 && maxId != settings.HeroesProfileApi.FallbackMaxReplayId;
            return ok
                ? new CheckResult(
                    "heroesprofile",
                    true,
                    $"GET /replays max_replay_id returned {maxId}.",
                    CheckCodes.HeroesProfileOk
                )
                : new CheckResult(
                    "heroesprofile",
                    false,
                    $"GET /replays max_replay_id returned {maxId} (fallback {settings.HeroesProfileApi.FallbackMaxReplayId}). Check the v1 Bearer key.",
                    CheckCodes.HeroesProfileRequestFailed
                );
        }
        catch (Exception e)
        {
            return Fail("heroesprofile", e);
        }
    }

    public static Task<CheckResult> CheckObsAsync(CheckRun run)
    {
        string files = null;
        bool filesOk = false;
        try
        {
            using var provider = run.CreateProvider();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            ObsCollectionInspection inspection = InspectObsFiles(settings);
            files = inspection.Message;
            filesOk = inspection.Ok;
            OBSWebsocket obs = provider.GetRequiredService<OBSWebsocket>();

            using var identified = new ManualResetEventSlim(false);
            EventHandler handler = (_, _) => identified.Set();
            obs.Connected += handler;
            try
            {
                obs.ConnectAsync(
                    settings.OBS.WebSocketEndpoint,
                    settings.OBS.WebSocketPassword ?? string.Empty
                );
                if (!obs.IsIdentified && !identified.Wait(TimeSpan.FromSeconds(8), run.Token))
                {
                    return Task.FromResult(
                        new CheckResult(
                            "obs",
                            false,
                            files
                                + " "
                                + $"No Identify from {settings.OBS.WebSocketEndpoint}. Enable Tools → WebSocket Server Settings (port 4455).",
                            CheckCodes.ObsUnreachable
                        )
                    );
                }

                var version = obs.GetVersion();
                string connected =
                    $"Connected. OBS {version.OBSStudioVersion}, websocket {version.PluginVersion}.";
                ObsSelectionResult selection = ReadSelection(obs, settings.OBS);
                return Task.FromResult(
                    new CheckResult(
                        "obs",
                        filesOk && selection.Ok,
                        files
                            + " "
                            + connected
                            + " "
                            + (
                                selection.Ok
                                    ? selection.Detail
                                    : selection.Reason + ": " + selection.Detail
                            ),
                        ObsCode(selection, filesOk)
                    )
                );
            }
            finally
            {
                obs.Connected -= handler;
                try
                {
                    if (obs.IsConnected)
                    {
                        obs.Disconnect();
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }
        catch (Exception e)
        {
            string detail = e.Message;
            if (!string.IsNullOrWhiteSpace(files))
            {
                detail = files + " " + detail;
            }

            return Task.FromResult(new CheckResult("obs", false, detail, CheckCodes.ObsError));
        }
    }

    /// <summary>A wrong profile or collection is the first fix; then the install's own files.</summary>
    public static string ObsCode(ObsSelectionResult selection, bool filesOk) =>
        selection?.Ok != true
            ? selection?.Reason switch
            {
                ObsSelection.ProfileMismatch => CheckCodes.ObsProfileMismatch,
                ObsSelection.CollectionMismatch => CheckCodes.ObsCollectionMismatch,
                _ => CheckCodes.ObsSelectionUnreadable,
            }
        : filesOk ? CheckCodes.ObsOk
        : CheckCodes.ObsFilesInvalid;

    private static ObsSelectionResult ReadSelection(OBSWebsocket obs, OBSSettings settings)
    {
        try
        {
            return ObsSelection.Check(
                ObsNames.Profile(settings),
                ObsNames.SceneCollection(settings),
                obs.GetProfileList()?.CurrentProfileName,
                obs.GetCurrentSceneCollection()
            );
        }
        catch (Exception e)
        {
            return ObsSelection.NotRead(e.Message);
        }
    }

    private static ObsCollectionInspection InspectObsFiles(AppSettings settings)
    {
        ObsContract contract = ObsContract.From(settings?.OBS);
        return ObsCollectionPaths.Inspect(
            AppContext.BaseDirectory,
            contract.Scenes,
            contract.Sources
        );
    }

    public static async Task<CheckResult> CheckTwitchAsync(CheckRun run)
    {
        try
        {
            using var provider = run.CreateProvider();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            if (
                string.IsNullOrWhiteSpace(settings.Twitch?.AccessToken)
                || string.IsNullOrWhiteSpace(settings.Twitch?.ClientId)
            )
            {
                return new CheckResult(
                    "twitch",
                    false,
                    "Twitch AccessToken or ClientId is missing. Helix was not called.",
                    CheckCodes.TwitchCredentialsMissing
                );
            }

            ITwitchAPI api = provider.GetRequiredService<ITwitchAPI>();
            string login = string.IsNullOrWhiteSpace(settings.Twitch.Channel)
                ? settings.Twitch.Account
                : settings.Twitch.Channel;
            var users = await api.Helix.Users.GetUsersAsync(logins: new List<string> { login });
            if (users?.Users == null || users.Users.Length == 0)
            {
                return new CheckResult(
                    "twitch",
                    false,
                    $"Helix returned no user for `{login}`.",
                    CheckCodes.TwitchUserNotFound
                );
            }

            string extra = string.Empty;
            bool predictionsOk = true;
            if (settings.Twitch.EnablePredictions)
            {
                try
                {
                    await api.Helix.Predictions.GetPredictionsAsync(users.Users[0].Id, first: 1);
                    extra = " Predictions scope OK.";
                }
                catch (Exception e)
                {
                    predictionsOk = false;
                    extra =
                        " Predictions need channel:manage:predictions (Helix: " + e.Message + ").";
                }
            }

            string scopes = await ReadTwitchScopesAsync(settings.Twitch.AccessToken, run.Token)
                .ConfigureAwait(false);
            bool chatOk =
                !settings.Twitch.EnableChatBot
                || ScopeHas(scopes, "chat:edit")
                || ScopeHas(scopes, "user:write:chat");
            bool rewardsOk =
                !(settings.Twitch.EnablePubSub || settings.Twitch.EnableRequests)
                || ScopeHas(scopes, "channel:read:redemptions")
                || ScopeHas(scopes, "channel:manage:redemptions");
            extra += " Scopes: " + (string.IsNullOrWhiteSpace(scopes) ? "(none)" : scopes) + ".";
            if (!chatOk)
            {
                extra += " Chat send needs chat:edit or user:write:chat.";
            }

            if (!rewardsOk)
            {
                extra +=
                    " Channel-point redemptions need channel:read:redemptions or channel:manage:redemptions.";
            }

            return new CheckResult(
                "twitch",
                chatOk && rewardsOk && predictionsOk,
                $"Helix OK for {users.Users[0].DisplayName} ({users.Users[0].Id}).{extra}",
                TwitchCode(predictionsOk, chatOk, rewardsOk)
            );
        }
        catch (Exception e)
        {
            return Fail("twitch", e);
        }
    }

    public static string TwitchCode(bool predictionsOk, bool chatOk, bool rewardsOk) =>
        !predictionsOk ? CheckCodes.TwitchPredictionsScopeMissing
        : !chatOk ? CheckCodes.TwitchChatScopeMissing
        : !rewardsOk ? CheckCodes.TwitchRedemptionsScopeMissing
        : CheckCodes.TwitchOk;

    private static bool ScopeHas(string scopes, string name)
    {
        return !string.IsNullOrWhiteSpace(scopes)
            && scopes.Contains(name, StringComparison.Ordinal);
    }

    private static async Task<string> ReadTwitchScopesAsync(
        string token,
        CancellationToken cancellationToken
    )
    {
        TwitchTokenValidation read = await TwitchTokenScopes
            .ReadAsync(token, TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
        if (read.Known)
        {
            return read.Scopes;
        }

        return read.Status is int status ? "validate-failed-" + status : "validate-failed";
    }

    public static async Task<CheckResult> CheckConnectivityAsync(CheckRun run)
    {
        try
        {
            using var provider = run.CreateProvider();
            IConnectivityWatchdog watchdog = provider.GetRequiredService<IConnectivityWatchdog>();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            using Activity activity = HeroesReplayTelemetry.StartSpan(
                "heroesreplay.check.connectivity"
            );
            ConnectivitySnapshot snapshot = await watchdog.ProbeAsync(run.Token);
            activity?.SetTag("connectivity.internet", snapshot.Internet);
            activity?.SetTag("connectivity.twitch", snapshot.Twitch);
            activity?.SetTag("connectivity.heroesprofile", snapshot.HeroesProfile);
            activity?.SetTag("obs.streaming_enabled", settings.OBS?.StreamingEnabled == true);

            bool ok = snapshot.Internet || snapshot.Twitch || snapshot.HeroesProfile;
            string streamNote =
                settings.OBS?.StreamingEnabled == true
                    ? " OBS:StreamingEnabled is true (this check does not StartStream)."
                    : " OBS:StreamingEnabled is false (StartStream will not run).";
            return new CheckResult(
                "connectivity",
                ok,
                snapshot.Describe() + streamNote,
                ok ? CheckCodes.ConnectivityOk : CheckCodes.ConnectivityOffline
            );
        }
        catch (Exception e)
        {
            return Fail("connectivity", e);
        }
    }

    public static async Task<CheckResult> CheckTimerAsync(CheckRun run)
    {
        try
        {
            Process process = Process
                .GetProcessesByName("HeroesOfTheStorm_x64")
                .FirstOrDefault(p =>
                {
                    try
                    {
                        return !p.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                });
            if (process == null)
            {
                return new CheckResult(
                    "timer",
                    false,
                    "HeroesOfTheStorm_x64 is not running.",
                    CheckCodes.TimerGameNotRunning
                );
            }

            // The spectator's own clock: read-only memory, no HUD crop and no OCR.
            using var clock = new MatchClock();
            TimeSpan? first = null;
            TimeSpan? last = null;
            MatchClockSample sample = default;
            for (int read = 1; read <= 5 && !run.Token.IsCancellationRequested; read++)
            {
                sample = clock.Read(process);
                run.Progress.WriteLine(
                    $"sample {read}: reason={sample.Reason} seconds={sample.Seconds:0.00} ticks={sample.Ticks} scale={sample.Scale}"
                );
                if (sample.Ok)
                {
                    TimeSpan time = TimeSpan.FromSeconds(sample.Seconds);
                    first ??= time;
                    last = time;
                }

                await Task.Delay(1000, run.Token);
            }

            bool running = MatchClock.IsRunning(first, last);
            return running
                ? new CheckResult(
                    "timer",
                    true,
                    $"pid={process.Id} match clock {last} is running.",
                    CheckCodes.TimerOk
                )
                : new CheckResult(
                    "timer",
                    false,
                    $"pid={process.Id} match clock is not running (last reason {sample.Reason}). The menu and loading screen read near-zero; a match must be playing.",
                    CheckCodes.TimerClockNotRunning
                );
        }
        catch (Exception e)
        {
            return Fail("timer", e);
        }
    }

    public static async Task<CheckResult> CheckTwitchExtensionAsync(CheckRun run)
    {
        try
        {
            using var provider = run.CreateProvider();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            CheckResult disabled = TwitchExtensionDisabled(
                settings.TwitchExtension?.Enabled == true
            );
            if (disabled != null)
            {
                return disabled;
            }

            if (string.IsNullOrWhiteSpace(settings.TwitchExtension?.ApiKey))
            {
                return new CheckResult(
                    "twitch-extension",
                    false,
                    "Uploader key is missing. Put `op://Heroes Replay/Heroes Profile Twitch Uploader Key/password` in TwitchExtension:ApiKey. This is not the v1 Bearer key.",
                    CheckCodes.TwitchExtensionKeyMissing
                );
            }

            ITwitchExtensionService extension =
                provider.GetRequiredService<ITwitchExtensionService>();
            return TwitchExtensionWhoAmI(await extension.WhoAmIAsync(run.Token));
        }
        catch (Exception e)
        {
            return Fail("twitch-extension", e);
        }
    }

    public static CheckResult TwitchExtensionWhoAmI(ExtensionWhoAmI who)
    {
        if (who == null || !who.Reachable)
        {
            return new CheckResult(
                "twitch-extension",
                false,
                who?.Message ?? "uploader/whoami failed.",
                who?.StatusCode switch
                {
                    401 or 403 => CheckCodes.TwitchExtensionKeyRejected,
                    429 => CheckCodes.TwitchExtensionRateLimited,
                    null or 0 => CheckCodes.TwitchExtensionUnreachable,
                    _ => CheckCodes.TwitchExtensionHttpError,
                }
            );
        }

        string channel = who.TwitchDisplayName ?? who.TwitchLogin ?? "unknown";
        return new CheckResult(
            "twitch-extension",
            true,
            $"Connected to {channel}. entitlement.active={who.EntitlementActive}. player_linked={who.PlayerLinked}.",
            CheckCodes.TwitchExtensionOk
        );
    }

    public static CheckResult TwitchExtensionDisabled(bool enabled)
    {
        if (enabled)
        {
            return null;
        }

        return new CheckResult(
            "twitch-extension",
            true,
            "Twitch extension is disabled.",
            CheckCodes.TwitchExtensionDisabled
        );
    }

    public static Task<CheckResult> CheckClientAsync(CheckRun run)
    {
        try
        {
            using var provider = run.CreateProvider();
            StormClientConfigurator configurator =
                provider.GetRequiredService<StormClientConfigurator>();
            ClientStatusResult status = configurator.GetStatus();
            string extra = status.HotSRunning ? " HotS is running." : string.Empty;
            if (status.MatchesPreset)
            {
                return Task.FromResult(
                    new CheckResult(
                        "client",
                        true,
                        $"Windowed 1080p + AhliObs match.{extra}",
                        CheckCodes.ClientOk
                    )
                );
            }

            return Task.FromResult(
                new CheckResult(
                    "client",
                    false,
                    string.Join("; ", status.Mismatches) + extra,
                    CheckCodes.ClientPresetMismatch
                )
            );
        }
        catch (Exception e)
        {
            return Task.FromResult(Fail("client", e));
        }
    }

    public static async Task<CheckResult> CheckFfmpegAsync(CheckRun run)
    {
        try
        {
            (ClipSettings clips, DependencySettings dependencies) =
                ServiceCollectionExtensions.LoadToolSettings();
            FfmpegLocator locator = FfmpegLocator.From(clips, dependencies);
            var tools = new List<FfmpegToolStatus>();
            foreach (string tool in FfmpegLocator.Tools)
            {
                run.Token.ThrowIfCancellationRequested();
                tools.Add(
                    await FfmpegCheck.ProbeAsync(locator.Resolve(tool)).ConfigureAwait(false)
                );
            }

            return ToCheckResult(
                FfmpegCheck.Evaluate(
                    DependencyManifest.Ffmpeg.Version,
                    tools,
                    locator.DescribeSearch()
                )
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Fail("ffmpeg", e);
        }
    }

    /// <summary>A warning passes (exit 0) and prints as <c>[WARN]</c>.</summary>
    public static CheckResult ToCheckResult(FfmpegCheckReport report) =>
        new(
            "ffmpeg",
            report.Ok,
            report.Warning ? WarningPrefix + report.Detail : report.Detail,
            report.Reason == null ? CheckCodes.FfmpegOk : CheckCodes.For("ffmpeg", report.Reason)
        );

    public static async Task<CheckResult> CheckBattleNetAsync(CheckRun run)
    {
        // The Battle.net launcher, not the game client: the one check that still OCRs (#292).
        _ = run;
        try
        {
            return await BattleNetLauncherCheck
                .ReadAsync(LauncherOcr.TryCreate())
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            return Fail("battlenet", e);
        }
    }

    private static void WriteText(CheckResult result, TextWriter output)
    {
        output.WriteLine($"[{CheckEntry.Label(result)}] {result.Name}: {result.Detail}");
    }

    private static CheckResult Fail(string name, Exception exception) =>
        new(name, false, exception.Message, CheckCodes.For(name, "error"));

    private static string NullToMissing(string value) =>
        string.IsNullOrWhiteSpace(value) ? "missing" : value;

    private static string DescribeReportScenes(AppSettings settings)
    {
        IReadOnlyList<string> names = (settings.OBS?.ReportScenes ?? [])
            .Where(scene => scene.Enabled)
            .Select(scene => scene.SceneName)
            .ToList();
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>
    /// One target's result. <see cref="Code"/> is stable (<see cref="CheckCodes"/>); a detail that
    /// starts with <see cref="WarningPrefix"/> on a passing result is a warning.
    /// </summary>
    public sealed record CheckResult(string Name, bool Ok, string Detail, string Code = null);
}

/// <summary>One <c>check</c> target: its subcommand name, help text, and the read it runs.</summary>
public sealed record CheckTarget(
    string Name,
    string Description,
    Func<CheckRun, Task<CheckCommand.CheckResult>> Run,
    bool InAll
);

/// <summary>
/// What one <c>check</c> invocation shares across its targets: the cancellation token, where
/// <c>check timer</c> prints its samples (stderr in JSON mode), and the secrets the targets
/// resolved, so <see cref="CliRedaction"/> can mask them in every detail.
/// </summary>
public sealed class CheckRun
{
    public CheckRun(CancellationToken token, TextWriter progress)
    {
        Token = token;
        Progress = progress ?? TextWriter.Null;
    }

    public CancellationToken Token { get; }
    public TextWriter Progress { get; }
    public CliRedaction Redaction { get; } = new();

    /// <summary>The check services with their settings bound; their secrets are remembered for masking.</summary>
    public ServiceProvider CreateProvider()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddCheckServices(Token)
            .BuildHeroesReplayProvider();
        Redaction.Remember(provider.GetRequiredService<AppSettings>());
        return provider;
    }
}

/// <summary><c>check --output json</c> details: one entry per target that ran, in run order.</summary>
public sealed record CheckDetails(IReadOnlyList<CheckEntry> Checks);

/// <summary>
/// One check in JSON. <see cref="Status"/> is <c>ok</c>, <c>warn</c> (passes), or <c>fail</c>;
/// <see cref="Detail"/> has no <c>Warning:</c> prefix, because the status says it.
/// </summary>
public sealed record CheckEntry(string Name, bool Ok, string Status, string Code, string Detail)
{
    public const string Pass = "ok";
    public const string Warn = "warn";
    public const string Fail = "fail";

    public static CheckEntry From(CheckCommand.CheckResult result)
    {
        bool warning =
            result.Ok
            && result.Detail?.StartsWith(CheckCommand.WarningPrefix, StringComparison.Ordinal)
                == true;
        return new CheckEntry(
            result.Name,
            result.Ok,
            !result.Ok ? Fail
                : warning ? Warn
                : Pass,
            result.Code,
            warning ? result.Detail[CheckCommand.WarningPrefix.Length..] : result.Detail
        );
    }

    /// <summary>The text-mode tag: <c>OK</c>, <c>WARN</c>, or <c>FAIL</c>.</summary>
    public static string Label(CheckCommand.CheckResult result) =>
        From(result).Status switch
        {
            Fail => "FAIL",
            Warn => "WARN",
            _ => "OK",
        };
}
