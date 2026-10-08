using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>
/// Spectate's dependency probe (#305): while OBS runs, open one short read-only session
/// (<see cref="IObsReadSessionFactory"/>, the client the MCP tools use), identify, send
/// <c>GetVersion</c>, and disconnect. It never touches the spectator's own connection per replay,
/// never sends anything but a Get, and never starts OBS. A closed OBS is skipped, because spectate
/// starts it for the next replay. A refused password is <see cref="RejectedCode"/>; no identify
/// is <see cref="UnreachableCode"/>. Only a degraded state follows: the Connectivity watchdog and
/// the per-replay session handle an outage exactly as before.
/// </summary>
public sealed class ObsWebsocketProbe : IServiceDependencyProbe
{
    public const string RejectedCode = "spectate.obs_rejected";
    public const string UnreachableCode = "spectate.obs_unreachable";

    private const string Name = "OBS websocket";

    private const string RejectedFix =
        "Set OBS:WebSocketPassword to the password in OBS > Tools > WebSocket Server Settings (or its op:// reference), confirm with `heroesreplay obs inspect`, then restart the stack (`heroesreplay services stop`, then `heroesreplay services start --supervise`; on the stream PC in a downtime). Replays are spectated without OBS scenes or recordings until then.";

    private const string UnreachableFix =
        "Check that OBS answers on OBS:WebSocketEndpoint (Tools > WebSocket Server Settings, port 4455) with `heroesreplay obs inspect`. Spectate keeps playing replays and retries OBS for each one; this clears when a probe passes.";

    private readonly OBSSettings obs;
    private readonly bool probeOn;
    private readonly IObsReadSessionFactory sessions;
    private readonly Func<bool> obsRunning;

    /// <param name="obs">OBS:Enabled, the endpoint, and the password.</param>
    /// <param name="probeOn"><c>ServiceHealth:SpectateObsProbe</c>.</param>
    /// <param name="sessions">The read-only session factory. Tests replace it.</param>
    /// <param name="obsRunning">Whether obs64 runs. Tests replace it.</param>
    public ObsWebsocketProbe(
        OBSSettings obs,
        bool probeOn,
        IObsReadSessionFactory sessions = null,
        Func<bool> obsRunning = null
    )
    {
        this.obs = obs;
        this.probeOn = probeOn;
        this.sessions = sessions ?? new ObsWebsocketReadSessionFactory();
        this.obsRunning =
            obsRunning ?? (() => NamedProcess.IsRunning(ObsLaunchDecision.ProcessName));
    }

    public string Dependency => Name;

    public string NotUsedReason
    {
        get
        {
            if (obs?.Enabled != true)
            {
                return "OBS:Enabled is false, so spectate does not use OBS.";
            }

            return probeOn
                ? null
                : "ServiceHealth:SpectateObsProbe is off, so spectate does not probe the OBS websocket.";
        }
    }

    public ServiceDependencyResult Unreachable(string cause) =>
        ServiceDependencyResult.Unreachable(Name, UnreachableCode, cause, UnreachableFix);

    public async Task<ServiceDependencyResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (!obsRunning())
        {
            return ServiceDependencyResult.Skipped(
                Name,
                "OBS is not running. Spectate starts it for the next replay, so it is not probed now."
            );
        }

        string password;
        try
        {
            password = SecretResolver.Resolve(obs.WebSocketPassword);
        }
        catch (Exception e)
        {
            return ServiceDependencyResult.Rejected(
                Name,
                RejectedCode,
                "OBS:WebSocketPassword could not be resolved: "
                    + ServiceHeartbeat.Redact(e.Message),
                RejectedFix
            );
        }

        try
        {
            // The websocket client blocks; keep it off the caller's thread.
            JObject version = await Task.Run(
                    () =>
                    {
                        using IObsReadSession session = sessions.Open(
                            obs.WebSocketEndpoint,
                            password
                        );
                        return session.Get("GetVersion");
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return ServiceDependencyResult.Ok(
                Name,
                $"OBS {ObsResponse.String(version, "obsVersion") ?? "?"} identified the websocket (obs-websocket {ObsResponse.String(version, "obsWebSocketVersion") ?? "?"}); Get requests only."
            );
        }
        catch (ObsUnavailableException e)
            when (e.Code == ObsUnavailableException.AuthenticationFailed)
        {
            return ServiceDependencyResult.Rejected(Name, RejectedCode, e.Message, RejectedFix);
        }
        catch (ObsUnavailableException e)
        {
            return Unreachable(e.Message);
        }
        catch (ObsRequestException e)
        {
            return Unreachable("OBS identified but GetVersion failed: " + e.Message);
        }
    }
}
