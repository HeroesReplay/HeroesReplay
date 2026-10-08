using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.Dependencies;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Clock;
using HeroesReplay.Core.Telemetry;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.TwitchExtension;
using Microsoft.Extensions.DependencyInjection;
using OBSWebsocketDotNet;
using TwitchLib.Api.Interfaces;

namespace HeroesReplay.CLI.Commands.Check;

public class CheckCommand : Command
{
    public CheckCommand()
        : base(
            "check",
            "Validate configuration, connectivity to Heroes Profile, OBS, Twitch, and the internet, and the ffmpeg tools clips use."
        )
    {
        Subcommands.Add(
            Build("config", "Bind settings and report which secrets are present.", CheckConfigAsync)
        );
        Subcommands.Add(
            Build(
                "heroesprofile",
                "Call Heroes Profile GET /replays with the v1 Bearer key.",
                CheckHeroesProfileAsync
            )
        );
        Subcommands.Add(
            Build(
                "obs",
                "Connect to obs-websocket 5, read the server version, verify scene files, and check the active profile and scene collection.",
                CheckObsAsync
            )
        );
        Subcommands.Add(
            Build(
                "twitch",
                "Call Helix GetUsers (and Predictions when enabled) for the configured channel.",
                CheckTwitchAsync
            )
        );
        Subcommands.Add(
            Build(
                "client",
                "Verify windowed 1080p and AhliObs in Heroes of the Storm Variables.txt.",
                CheckClientAsync
            )
        );
        Subcommands.Add(
            Build(
                "connectivity",
                "Probe 1.1.1.1, Twitch, and Heroes Profile without starting an OBS stream.",
                CheckConnectivityAsync
            )
        );
        Subcommands.Add(
            Build(
                "timer",
                "Read the HeroesOfTheStorm_x64 match clock from memory (read-only) and report whether it is running.",
                CheckTimerAsync
            )
        );
        Subcommands.Add(
            Build(
                "twitch-extension",
                "Report TwitchExtension:Enabled, or call uploader/whoami when the extension is on (issue 49).",
                CheckTwitchExtensionAsync
            )
        );
        Subcommands.Add(
            Build(
                "battlenet",
                "Read the Battle.net window and report the Play or Update button.",
                CheckBattleNetAsync
            )
        );
        Subcommands.Add(
            Build(
                "ffmpeg",
                "Resolve ffmpeg and ffprobe (Clips:FfmpegDirectory, the deps install folder, C:\\ffmpeg\\bin, PATH) and report each path and -version line. Fails when one is missing, does not run, or ffmpeg cannot encode libx264; a working build that is not the pinned version is a warning.",
                CheckFfmpegAsync
            )
        );

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await RunAllAsync(cancellationToken);
            }
        );
    }

    private static Command Build(
        string name,
        string description,
        Func<CancellationToken, Task<CheckResult>> action
    )
    {
        var command = new Command(name, description);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                CheckResult result = await action(cancellationToken);
                Write(result);
                return result.Ok ? 0 : 1;
            }
        );
        return command;
    }

    private static async Task<int> RunAllAsync(CancellationToken cancellationToken)
    {
        CheckResult[] results =
        {
            await CheckConfigAsync(cancellationToken),
            await CheckHeroesProfileAsync(cancellationToken),
            await CheckObsAsync(cancellationToken),
            await CheckTwitchAsync(cancellationToken),
            await CheckClientAsync(cancellationToken),
            await CheckFfmpegAsync(cancellationToken),
            await CheckBattleNetAsync(cancellationToken),
            await CheckConnectivityAsync(cancellationToken),
        };

        bool ok = true;
        foreach (CheckResult result in results)
        {
            Write(result);
            ok &= result.Ok;
        }

        return ok ? 0 : 1;
    }

    public static async Task<CheckResult> CheckConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var provider = CreateProvider(cancellationToken);
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
            return new CheckResult(
                "config",
                hasProfile,
                hasProfile
                    ? string.Join(Environment.NewLine, lines)
                    : "Heroes Profile API key is missing. Set HeroesProfileApi:ApiKey to an op:// reference or a token."
            );
        }
        catch (Exception e)
        {
            return Fail("config", e);
        }
    }

    public static async Task<CheckResult> CheckHeroesProfileAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var provider = CreateProvider(cancellationToken);
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            if (string.IsNullOrWhiteSpace(settings.HeroesProfileApi?.ApiKey))
            {
                return new CheckResult(
                    "heroesprofile",
                    false,
                    "API key is missing. Put `op://Heroes Replay/Heroes Profile API Key/password` in appsettings.secrets.json or set HEROES_REPLAY_HeroesProfileApi__ApiKey."
                );
            }

            IHeroesProfileService api = provider.GetRequiredService<IHeroesProfileService>();
            using Activity activity = HeroesReplayTelemetry.StartSpan(
                "heroesreplay.check.heroesprofile"
            );
            int maxId = await api.GetMaxReplayIdAsync();
            activity?.SetTag("heroesprofile.max_id", maxId);
            bool ok = maxId > 0 && maxId != settings.HeroesProfileApi.FallbackMaxReplayId;
            return new CheckResult(
                "heroesprofile",
                ok,
                ok
                    ? $"GET /replays max_replay_id returned {maxId}."
                    : $"GET /replays max_replay_id returned {maxId} (fallback {settings.HeroesProfileApi.FallbackMaxReplayId}). Check the v1 Bearer key."
            );
        }
        catch (Exception e)
        {
            return Fail("heroesprofile", e);
        }
    }

    public static async Task<CheckResult> CheckObsAsync(CancellationToken cancellationToken)
    {
        string files = null;
        bool filesOk = false;
        try
        {
            using var provider = CreateProvider(cancellationToken);
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
                if (
                    !obs.IsIdentified
                    && !identified.Wait(TimeSpan.FromSeconds(8), cancellationToken)
                )
                {
                    return new CheckResult(
                        "obs",
                        false,
                        files
                            + " "
                            + $"No Identify from {settings.OBS.WebSocketEndpoint}. Enable Tools → WebSocket Server Settings (port 4455)."
                    );
                }

                var version = obs.GetVersion();
                string connected =
                    $"Connected. OBS {version.OBSStudioVersion}, websocket {version.PluginVersion}.";
                ObsSelectionResult selection = ReadSelection(obs, settings.OBS);
                return new CheckResult(
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

            return new CheckResult("obs", false, detail);
        }
    }

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

    public static async Task<CheckResult> CheckTwitchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var provider = CreateProvider(cancellationToken);
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            if (
                string.IsNullOrWhiteSpace(settings.Twitch?.AccessToken)
                || string.IsNullOrWhiteSpace(settings.Twitch?.ClientId)
            )
            {
                return new CheckResult(
                    "twitch",
                    false,
                    "Twitch AccessToken or ClientId is missing. Helix was not called."
                );
            }

            ITwitchAPI api = provider.GetRequiredService<ITwitchAPI>();
            string login = string.IsNullOrWhiteSpace(settings.Twitch.Channel)
                ? settings.Twitch.Account
                : settings.Twitch.Channel;
            var users = await api.Helix.Users.GetUsersAsync(logins: new List<string> { login });
            if (users?.Users == null || users.Users.Length == 0)
            {
                return new CheckResult("twitch", false, $"Helix returned no user for `{login}`.");
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

            string scopes = await ReadTwitchScopesAsync(
                    settings.Twitch.AccessToken,
                    cancellationToken
                )
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
                $"Helix OK for {users.Users[0].DisplayName} ({users.Users[0].Id}).{extra}"
            );
        }
        catch (Exception e)
        {
            return Fail("twitch", e);
        }
    }

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

    public static async Task<CheckResult> CheckConnectivityAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var provider = CreateProvider(cancellationToken);
            IConnectivityWatchdog watchdog = provider.GetRequiredService<IConnectivityWatchdog>();
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            using Activity activity = HeroesReplayTelemetry.StartSpan(
                "heroesreplay.check.connectivity"
            );
            ConnectivitySnapshot snapshot = await watchdog.ProbeAsync(cancellationToken);
            activity?.SetTag("connectivity.internet", snapshot.Internet);
            activity?.SetTag("connectivity.twitch", snapshot.Twitch);
            activity?.SetTag("connectivity.heroesprofile", snapshot.HeroesProfile);
            activity?.SetTag("obs.streaming_enabled", settings.OBS?.StreamingEnabled == true);

            bool ok = snapshot.Internet || snapshot.Twitch || snapshot.HeroesProfile;
            string streamNote =
                settings.OBS?.StreamingEnabled == true
                    ? " OBS:StreamingEnabled is true (this check does not StartStream)."
                    : " OBS:StreamingEnabled is false (StartStream will not run).";
            return new CheckResult("connectivity", ok, snapshot.Describe() + streamNote);
        }
        catch (Exception e)
        {
            return Fail("connectivity", e);
        }
    }

    public static async Task<CheckResult> CheckTimerAsync(CancellationToken cancellationToken)
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
                return new CheckResult("timer", false, "HeroesOfTheStorm_x64 is not running.");
            }

            // The spectator's own clock: read-only memory, no HUD crop and no OCR.
            using var clock = new StableMatchClock();
            TimeSpan? first = null;
            TimeSpan? last = null;
            StableClockSample sample = default;
            for (int read = 1; read <= 5 && !cancellationToken.IsCancellationRequested; read++)
            {
                sample = clock.Read(process);
                Console.WriteLine(
                    $"sample {read}: reason={sample.Reason} seconds={sample.Seconds:0.00} ticks={sample.Ticks} scale={sample.Scale}"
                );
                if (sample.Ok)
                {
                    TimeSpan time = TimeSpan.FromSeconds(sample.Seconds);
                    first ??= time;
                    last = time;
                }

                await Task.Delay(1000, cancellationToken);
            }

            bool running = StableMatchClock.IsRunning(first, last);
            string detail = running
                ? $"pid={process.Id} match clock {last} is running."
                : $"pid={process.Id} match clock is not running (last reason {sample.Reason}). The menu and loading screen read near-zero; a match must be playing.";
            return new CheckResult("timer", running, detail);
        }
        catch (Exception e)
        {
            return Fail("timer", e);
        }
    }

    public static async Task<CheckResult> CheckTwitchExtensionAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var provider = CreateProvider(cancellationToken);
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
                    "Uploader key is missing. Put `op://Heroes Replay/Heroes Profile Twitch Uploader Key/password` in TwitchExtension:ApiKey. This is not the v1 Bearer key."
                );
            }

            ITwitchExtensionService extension =
                provider.GetRequiredService<ITwitchExtensionService>();
            return TwitchExtensionWhoAmI(await extension.WhoAmIAsync(cancellationToken));
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
                who?.Message ?? "uploader/whoami failed."
            );
        }

        string channel = who.TwitchDisplayName ?? who.TwitchLogin ?? "unknown";
        return new CheckResult(
            "twitch-extension",
            true,
            $"Connected to {channel}. entitlement.active={who.EntitlementActive}. player_linked={who.PlayerLinked}."
        );
    }

    public static CheckResult TwitchExtensionDisabled(bool enabled)
    {
        if (enabled)
        {
            return null;
        }

        return new CheckResult("twitch-extension", true, "Twitch extension is disabled.");
    }

    public static Task<CheckResult> CheckClientAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var provider = CreateProvider(cancellationToken);
            StormClientConfigurator configurator =
                provider.GetRequiredService<StormClientConfigurator>();
            ClientStatusResult status = configurator.GetStatus();
            string extra = status.HotSRunning ? " HotS is running." : string.Empty;
            if (status.MatchesPreset)
            {
                return Task.FromResult(
                    new CheckResult("client", true, $"Windowed 1080p + AhliObs match.{extra}")
                );
            }

            return Task.FromResult(
                new CheckResult("client", false, string.Join("; ", status.Mismatches) + extra)
            );
        }
        catch (Exception e)
        {
            return Task.FromResult(Fail("client", e));
        }
    }

    public static async Task<CheckResult> CheckFfmpegAsync(CancellationToken cancellationToken)
    {
        try
        {
            (ClipSettings clips, DependencySettings dependencies) =
                ServiceCollectionExtensions.LoadToolSettings();
            FfmpegLocator locator = FfmpegLocator.From(clips, dependencies);
            var tools = new List<FfmpegToolStatus>();
            foreach (string tool in FfmpegLocator.Tools)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
        new("ffmpeg", report.Ok, report.Warning ? WarningPrefix + report.Detail : report.Detail);

    public static async Task<CheckResult> CheckBattleNetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var provider = CreateProvider(cancellationToken);
            return await BattleNetLauncherCheck
                .ReadAsync(provider.GetService<Windows.Media.Ocr.OcrEngine>())
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            return Fail("battlenet", e);
        }
    }

    private static ServiceProvider CreateProvider(CancellationToken cancellationToken)
    {
        return new ServiceCollection()
            .AddCheckServices(cancellationToken)
            .BuildHeroesReplayProvider();
    }

    /// <summary>A passing result whose detail starts with this prints as <c>[WARN]</c>.</summary>
    public const string WarningPrefix = "Warning: ";

    private static void Write(CheckResult result)
    {
        string status =
            !result.Ok ? "FAIL"
            : result.Detail?.StartsWith(WarningPrefix, StringComparison.Ordinal) == true ? "WARN"
            : "OK";
        Console.WriteLine($"[{status}] {result.Name}: {result.Detail}");
    }

    private static CheckResult Fail(string name, Exception exception) =>
        new(name, false, exception.Message);

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

    public sealed record CheckResult(string Name, bool Ok, string Detail);
}
