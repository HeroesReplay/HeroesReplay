using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// A short-lived, read-only obs-websocket session for the agent tools (<c>obs_inspect</c>,
/// <c>obs_validate</c>, <c>obs_screenshot</c>). It is not the spectator's one connection per
/// replay: it identifies, sends a few Get requests, and disconnects on dispose.
/// </summary>
public interface IObsReadSession : IDisposable
{
    /// <summary>Sends one request from <see cref="ObsReadOnly.Requests"/> and returns its responseData.</summary>
    /// <exception cref="ObsRequestException">OBS answered with a failed request status.</exception>
    JObject Get(string requestType, JObject requestData = null);
}

public interface IObsReadSessionFactory
{
    /// <exception cref="ObsUnavailableException">OBS did not identify within a few seconds.</exception>
    IObsReadSession Open(string endpoint, string password);
}

/// <summary>OBS could not be reached or refused the session. <see cref="Code"/> is stable.</summary>
public sealed class ObsUnavailableException : Exception
{
    public const string Unreachable = "obs.unreachable";
    public const string AuthenticationFailed = "obs.auth_failed";

    public ObsUnavailableException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>OBS answered a request with a failure, for example a source that does not exist.</summary>
public sealed class ObsRequestException : Exception
{
    /// <summary>obs-websocket RequestStatus 600.</summary>
    public const int ResourceNotFound = 600;

    public ObsRequestException(string requestType, int status, string message)
        : base(requestType + " failed. " + message)
    {
        RequestType = requestType;
        Status = status;
    }

    public string RequestType { get; }

    /// <summary>obs-websocket RequestStatus code.</summary>
    public int Status { get; }
}

public sealed class ObsWebsocketReadSessionFactory : IObsReadSessionFactory
{
    /// <summary>A local OBS identifies in milliseconds. Fail fast when it is closed.</summary>
    public static readonly TimeSpan IdentifyTimeout = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public IObsReadSession Open(string endpoint, string password) =>
        new ObsWebsocketReadSession(Connect(endpoint, password));

    /// <summary>Connects and waits for OBS to identify the session.</summary>
    /// <exception cref="ObsUnavailableException">OBS did not identify within <see cref="IdentifyTimeout"/>.</exception>
    internal static OBSWebsocket Connect(string endpoint, string password)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ObsUnavailableException(
                ObsUnavailableException.Unreachable,
                "OBS:WebSocketEndpoint is empty. Set it to the obs-websocket URL, normally ws://127.0.0.1:4455."
            );
        }

        var obs = new OBSWebsocket { WSTimeout = RequestTimeout };
        using var settled = new ManualResetEventSlim(false);
        ObsDisconnectionInfo closed = null;
        EventHandler onConnected = (_, _) => settled.Set();
        EventHandler<ObsDisconnectionInfo> onDisconnected = (_, info) =>
        {
            closed = info;
            settled.Set();
        };
        obs.Connected += onConnected;
        obs.Disconnected += onDisconnected;
        try
        {
            obs.ConnectAsync(endpoint, password ?? string.Empty);
            if (!obs.IsIdentified)
            {
                settled.Wait(IdentifyTimeout);
            }
        }
        catch (Exception e) when (e is ArgumentException or UriFormatException)
        {
            throw new ObsUnavailableException(
                ObsUnavailableException.Unreachable,
                "OBS:WebSocketEndpoint '" + endpoint + "' is not a websocket URL. " + e.Message
            );
        }
        finally
        {
            obs.Connected -= onConnected;
            obs.Disconnected -= onDisconnected;
        }

        if (obs.IsIdentified)
        {
            return obs;
        }

        Close(obs);
        if (closed?.ObsCloseCode == ObsCloseCodes.AuthenticationFailed)
        {
            throw new ObsUnavailableException(
                ObsUnavailableException.AuthenticationFailed,
                "OBS at "
                    + endpoint
                    + " rejected the websocket password. Check OBS:WebSocketPassword against Tools > WebSocket Server Settings."
            );
        }

        string cause =
            closed == null
                ? "did not answer within " + IdentifyTimeout.TotalSeconds + " s"
                : "is not reachable ("
                    + (
                        closed.DisconnectReason
                        ?? closed.WebsocketDisconnectionInfo?.Exception?.Message
                        ?? closed.WebsocketDisconnectionInfo?.Type.ToString()
                    )
                    + ")";
        throw new ObsUnavailableException(
            ObsUnavailableException.Unreachable,
            "OBS at "
                + endpoint
                + " "
                + cause
                + ". Start OBS Studio and enable Tools > WebSocket Server Settings (port 4455), or check OBS:WebSocketEndpoint."
        );
    }

    internal static void Close(OBSWebsocket obs)
    {
        try
        {
            if (obs.IsConnected)
            {
                obs.Disconnect();
            }
        }
        catch
        {
            // The session is being dropped; a failed close changes nothing in OBS.
        }
    }
}

internal sealed class ObsWebsocketReadSession : IObsReadSession
{
    private readonly OBSWebsocket obs;

    public ObsWebsocketReadSession(OBSWebsocket obs)
    {
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
    }

    public JObject Get(string requestType, JObject requestData = null)
    {
        // The guard runs before anything is sent.
        ObsReadOnly.Require(requestType);
        try
        {
            return obs.SendRequest(requestType, requestData) ?? new JObject();
        }
        catch (ErrorResponseException e)
        {
            throw new ObsRequestException(requestType, e.ErrorCode, e.Message);
        }
    }

    public void Dispose() => ObsWebsocketReadSessionFactory.Close(obs);
}
