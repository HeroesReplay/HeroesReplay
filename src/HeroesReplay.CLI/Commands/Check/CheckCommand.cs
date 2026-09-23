using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Client;
using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OBSWebsocketDotNet;
using TwitchLib.Api.Interfaces;

namespace HeroesReplay.CLI.Commands.Check;

public class CheckCommand : Command
{
    public CheckCommand()
        : base(
            "check",
            "Validate configuration and connectivity to Heroes Profile, OBS, Twitch, and the internet."
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
            Build("obs", "Connect to obs-websocket 5 and read the server version.", CheckObsAsync)
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
                "Read-only scan of HeroesOfTheStorm_x64 for a ticking match clock (issue 27).",
                CheckTimerAsync
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
        try
        {
            using var provider = CreateProvider(cancellationToken);
            AppSettings settings = provider.GetRequiredService<AppSettings>();
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
                        $"No Identify from {settings.OBS.WebSocketEndpoint}. Enable Tools → WebSocket Server Settings (port 4455)."
                    );
                }

                var version = obs.GetVersion();
                return new CheckResult(
                    "obs",
                    true,
                    $"Connected. OBS {version.OBSStudioVersion}, websocket {version.PluginVersion}."
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
            return Fail("obs", e);
        }
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
            if (settings.Twitch.EnablePredictions)
            {
                try
                {
                    await api.Helix.Predictions.GetPredictionsAsync(users.Users[0].Id, first: 1);
                    extra = " Predictions scope OK.";
                }
                catch (Exception e)
                {
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
                chatOk && rewardsOk,
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
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://id.twitch.tv/oauth2/validate"
        );
        request.Headers.TryAddWithoutValidation("Authorization", "OAuth " + token);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        string body = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return "validate-failed-" + (int)response.StatusCode;
        }

        using JsonDocument document = JsonDocument.Parse(body);
        if (
            !document.RootElement.TryGetProperty("scopes", out JsonElement scopes)
            || scopes.ValueKind != JsonValueKind.Array
        )
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (JsonElement scope in scopes.EnumerateArray())
        {
            string value = scope.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                names.Add(value);
            }
        }

        return string.Join(' ', names);
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

            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var clock = new MemoryMatchClock(loggerFactory.CreateLogger("MemoryMatchClock"));
            string statusPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeroesReplay",
                "status.json"
            );
            TimeSpan? hud = ReadStatusTimer(statusPath);

            if (hud == null)
            {
                return new CheckResult(
                    "timer",
                    false,
                    $"HotS pid {process.Id} is running but no HUD timer is available to seed a scan. Start spectate until TimerDetected, then re-run."
                );
            }

            // Follow status.json. Do not invent seconds: a stuck HUD cannot prove a memory clock.
            var deadline = DateTime.UtcNow.AddSeconds(35);
            int sample = 0;
            int stuck = 0;
            TimeSpan seed = hud.Value;
            TimeSpan? previous = null;
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                TimeSpan? live = ReadStatusTimer(statusPath);
                seed = live ?? seed;
                if (previous.HasValue && seed == previous.Value)
                {
                    stuck++;
                }
                else if (previous.HasValue)
                {
                    stuck = 0;
                }

                previous = seed;
                clock.Observe(process, seed, TimeSpan.FromSeconds(1));
                sample++;
                Console.WriteLine(
                    $"sample {sample}: seed={seed} memory={clock.LastRead} locked={clock.IsLocked} phase={clock.Phase} candidates={clock.CandidateCount}"
                );
                if (clock.IsLocked || clock.Phase == "cooldown")
                {
                    break;
                }

                if (stuck >= 3)
                {
                    break;
                }

                await Task.Delay(1000, cancellationToken);
            }

            bool found = clock.IsLocked;
            string stuckNote =
                stuck >= 3
                    ? $" HUD seed stayed {seed} (status.json is not advancing), so no ticking address could be confirmed."
                    : string.Empty;
            string detail =
                $"pid={process.Id} HUD seed={seed} memory={clock.LastRead} locked={clock.IsLocked} phase={clock.Phase} candidates={clock.CandidateCount}.{stuckNote} BitBlt HUD stays the clock until this address agrees across patches.";
            return new CheckResult("timer", found, detail);
        }
        catch (Exception e)
        {
            return Fail("timer", e);
        }
    }

    private static TimeSpan? ReadStatusTimer(string statusPath)
    {
        if (!File.Exists(statusPath))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(statusPath));
            if (
                doc.RootElement.TryGetProperty("timer", out JsonElement timer)
                && TimeSpan.TryParse(timer.GetString(), out TimeSpan parsed)
            )
            {
                return parsed;
            }
        }
        catch
        {
            // ignore stale status
        }

        return null;
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

    private static ServiceProvider CreateProvider(CancellationToken cancellationToken)
    {
        return new ServiceCollection()
            .AddCheckServices(cancellationToken)
            .BuildHeroesReplayProvider();
    }

    private static void Write(CheckResult result)
    {
        string status = result.Ok ? "OK" : "FAIL";
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
