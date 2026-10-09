using System;
using System.Diagnostics;
using System.Threading;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// Reads GetStreamStatus once for <c>services stop</c>. It never starts or stops an output.
/// Call it only after the roles exited, so it is not a second websocket next to the spectator's.
/// </summary>
internal static class ServiceStreamProbe
{
    private static readonly TimeSpan IdentifyTimeout = TimeSpan.FromSeconds(5);

    public static ServiceStreamCheck Read()
    {
        if (!ObsRunning())
        {
            return ServiceStreamCheck.NotRunning();
        }

        string endpoint;
        string password;
        bool streamedHere;
        try
        {
            AppSettings settings = ServiceCollectionExtensions.LoadAppSettings();
            endpoint = settings.OBS?.WebSocketEndpoint;
            password = settings.OBS?.WebSocketPassword;
            streamedHere = SessionMedia.ShouldStream(settings.OBS);
        }
        catch (Exception e)
        {
            return ServiceStreamCheck.Unknown(
                "OBS is running, but the websocket settings could not be read. " + e.Message
            );
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ServiceStreamCheck.WhenUnreachable(
                streamedHere,
                "No OBS websocket endpoint is configured."
            );
        }

        var obs = new OBSWebsocket();
        using var settled = new ManualResetEventSlim(false);
        EventHandler connected = (_, _) => settled.Set();
        EventHandler<ObsDisconnectionInfo> disconnected = (_, _) => settled.Set();
        obs.Connected += connected;
        obs.Disconnected += disconnected;
        try
        {
            try
            {
                obs.ConnectAsync(endpoint, password ?? string.Empty);
                if (!obs.IsIdentified)
                {
                    settled.Wait(IdentifyTimeout);
                }
            }
            catch (Exception e)
            {
                return ServiceStreamCheck.WhenUnreachable(
                    streamedHere,
                    $"OBS websocket at {endpoint} did not connect. {e.Message}"
                );
            }

            if (!obs.IsIdentified)
            {
                return ServiceStreamCheck.WhenUnreachable(
                    streamedHere,
                    $"OBS websocket at {endpoint} did not identify within {IdentifyTimeout.TotalSeconds:0}s."
                );
            }

            OutputStatus status;
            try
            {
                status = obs.GetStreamStatus();
            }
            catch (Exception e)
            {
                return ServiceStreamCheck.Unknown("GetStreamStatus failed. " + e.Message);
            }

            if (status == null)
            {
                return ServiceStreamCheck.Unknown("GetStreamStatus returned no status.");
            }

            return ServiceStreamCheck.From(
                ObsStreamHealth.Next(
                    null,
                    new ObsStreamSample(status.IsActive, status.IsReconnecting, status.BytesSent),
                    DateTimeOffset.UtcNow
                )
            );
        }
        finally
        {
            obs.Connected -= connected;
            obs.Disconnected -= disconnected;
            try
            {
                obs.Disconnect();
            }
            catch (Exception)
            {
                // The process exits next. A failed disconnect does not change the stream state.
            }
        }
    }

    private static bool ObsRunning()
    {
        Process[] processes = Process.GetProcessesByName("obs64");
        foreach (Process process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
