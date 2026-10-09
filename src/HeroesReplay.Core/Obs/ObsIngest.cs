using System;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The parts of GetStreamServiceSettings the ingest check needs: the service type, the named
/// service of a <c>rtmp_common</c> type, and the server. <see cref="From"/> never reads the key,
/// a username, or a password.
/// </summary>
public sealed record ObsStreamServer(string Type, string Service, string Server)
{
    public static ObsStreamServer From(JObject response)
    {
        var settings = response?["streamServiceSettings"] as JObject;
        return new ObsStreamServer(
            Text(response?["streamServiceType"]),
            Text(settings?["service"]),
            Text(settings?["server"])
        );
    }

    private static string Text(JToken token) =>
        token?.Type == JTokenType.String ? (string)token : null;
}

/// <summary>The ingest host and port a TCP connect checks before StartStream (#407).</summary>
public sealed record ObsIngestTarget(string Host, int Port)
{
    public override string ToString() => Host + ":" + Port;
}

/// <summary>
/// Opens a TCP connection to the ingest. The real one is <see cref="TcpIngestProbe"/>; tests use
/// a fake, so no unit test opens a socket.
/// </summary>
internal interface IObsIngestProbe
{
    /// <summary>Null when a connection opened within <paramref name="timeout"/>; else why not.</summary>
    string Connect(string host, int port, TimeSpan timeout);
}

/// <summary>
/// The ingest check before StartStream (#407). On ASA-SERVER on 2026-10-09 every StartStream
/// into an unreachable ingest made OBS 32.2.2 build its 5 multitrack encoders on its UI thread
/// and open a modal "Failed to connect" dialog; under a match's load the fourth one hung OBS.
/// A TCP connect to the same host and port fails in milliseconds and costs OBS nothing.
/// </summary>
public static class ObsIngest
{
    /// <summary>The cause code of a start that was not sent because the ingest refused a TCP connection.</summary>
    public const string UnreachableCode = "obs.ingest_unreachable";

    /// <summary>Twitch's global ingest: where <c>rtmp_common</c> Twitch with server <c>auto</c> connects.</summary>
    public static readonly ObsIngestTarget TwitchGlobal = new(
        "ingest.global-contribute.live-video.net",
        RtmpPort
    );

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private const int RtmpPort = 1935;
    private const int RtmpsPort = 443;

    /// <summary>
    /// The host and port OBS will connect to, or null when the settings name none a TCP connect
    /// can check (no server, <c>auto</c> on a service other than Twitch, or a protocol other than
    /// RTMP or RTMPS, such as SRT over UDP). Then the start is not checked.
    /// </summary>
    public static ObsIngestTarget Resolve(ObsStreamServer server)
    {
        string address = server?.Server?.Trim();
        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        if (string.Equals(address, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return
                string.Equals(server.Type, "rtmp_common", StringComparison.Ordinal)
                && string.Equals(server.Service, "Twitch", StringComparison.OrdinalIgnoreCase)
                ? TwitchGlobal
                : null;
        }

        string url = address.Contains("://", StringComparison.Ordinal)
            ? address
            : "rtmp://" + address;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        int defaultPort = uri.Scheme.ToLowerInvariant() switch
        {
            "rtmp" => RtmpPort,
            "rtmps" => RtmpsPort,
            _ => 0,
        };
        if (defaultPort == 0)
        {
            return null;
        }

        return new ObsIngestTarget(uri.Host, uri.Port > 0 ? uri.Port : defaultPort);
    }
}

/// <summary>A plain TCP connect with a deadline; nothing is sent on it.</summary>
internal sealed class TcpIngestProbe : IObsIngestProbe
{
    public string Connect(string host, int port, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var client = new TcpClient();
        try
        {
            client.ConnectAsync(host, port, deadline.Token).AsTask().GetAwaiter().GetResult();
            return null;
        }
        catch (OperationCanceledException)
        {
            return "no answer within " + timeout.TotalSeconds.ToString("0.#") + " s";
        }
        catch (SocketException e)
        {
            return e.SocketErrorCode + ": " + e.Message;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
