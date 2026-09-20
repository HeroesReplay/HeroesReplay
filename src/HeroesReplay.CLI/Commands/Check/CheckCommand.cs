using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Client;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OBSWebsocketDotNet;
using TwitchLib.Api.Interfaces;

namespace HeroesReplay.CLI.Commands.Check;

public class CheckCommand : Command
{
    public CheckCommand()
        : base(
            "check",
            "Validate configuration and connectivity to Heroes Profile, OBS, and Twitch."
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
            Build("twitch", "Call Helix GetUsers for the configured channel.", CheckTwitchAsync)
        );
        Subcommands.Add(
            Build(
                "client",
                "Verify windowed 1080p and AhliObs in Heroes of the Storm Variables.txt.",
                CheckClientAsync
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
                    "API key is missing. Put `op://Private/HeroesProfileAPI/V1 API KEY/password` in appsettings.secrets.json or set HEROES_REPLAY_HeroesProfileApi__ApiKey."
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

            return new CheckResult(
                "twitch",
                true,
                $"Helix OK for {users.Users[0].DisplayName} ({users.Users[0].Id})."
            );
        }
        catch (Exception e)
        {
            return Fail("twitch", e);
        }
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

    public sealed record CheckResult(string Name, bool Ok, string Detail);
}
