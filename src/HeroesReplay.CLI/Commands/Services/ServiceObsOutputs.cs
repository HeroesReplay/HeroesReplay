using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HeroesReplay.Core.Configuration;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// The OBS steps of <c>services shutdown</c>. Call them only after every role exited, so this is
/// not a second websocket next to the spectator's. Unlike <c>services stop</c>, this ends the
/// stream: the operator asked to take the machine down.
/// </summary>
internal static class ServiceObsOutputs
{
    private static readonly TimeSpan IdentifyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OutputsStopWait = TimeSpan.FromSeconds(15);

    public static IReadOnlyList<int> FindObs()
    {
        var pids = new List<int>();
        foreach (Process process in Process.GetProcessesByName("obs64"))
        {
            pids.Add(process.Id);
            process.Dispose();
        }

        return pids;
    }

    /// <summary>
    /// Sends StopStream and StopRecord for the outputs that are active and waits up to 15 s for
    /// both to end, so OBS closes without its "outputs are active" question and the recording is
    /// finalized. An OBS that does not answer is still closed afterwards.
    /// </summary>
    public static ObsOutputsStop Stop()
    {
        string endpoint;
        string password;
        try
        {
            AppSettings settings = ServiceCollectionExtensions.LoadAppSettings();
            endpoint = settings.OBS?.WebSocketEndpoint;
            password = settings.OBS?.WebSocketPassword;
        }
        catch (Exception e)
        {
            return ObsOutputsStop.Unreachable(
                "the websocket settings could not be read, so OBS is closed without stopping its outputs first. "
                    + e.Message
            );
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ObsOutputsStop.Unreachable(
                "no OBS websocket endpoint is configured, so OBS is closed without stopping its outputs first."
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
                return ObsOutputsStop.Unreachable(
                    $"the websocket at {endpoint} did not connect ({e.Message}), so OBS is closed without stopping its outputs first."
                );
            }

            if (!obs.IsIdentified)
            {
                return ObsOutputsStop.Unreachable(
                    $"the websocket at {endpoint} did not identify within {IdentifyTimeout.TotalSeconds:0} s, so OBS is closed without stopping its outputs first."
                );
            }

            bool streaming = TryRead(() => obs.GetStreamStatus()?.IsActive == true);
            bool recording = TryRead(() => obs.GetRecordStatus()?.IsRecording == true);
            var notes = new List<string>();
            if (streaming)
            {
                notes.Add(TrySend(obs.StopStream) ? "StopStream sent" : "StopStream failed");
            }

            if (recording)
            {
                notes.Add(TrySend(() => obs.StopRecord()) ? "StopRecord sent" : "StopRecord failed");
            }

            if (!streaming && !recording)
            {
                return new ObsOutputsStop(true, true, true, "not streaming or recording.");
            }

            var wait = Stopwatch.StartNew();
            bool streamDone = !streaming;
            bool recordDone = !recording;
            while (wait.Elapsed < OutputsStopWait && !(streamDone && recordDone))
            {
                Thread.Sleep(500);
                streamDone = streamDone || !TryRead(() => obs.GetStreamStatus()?.IsActive == true);
                recordDone = recordDone || !TryRead(() => obs.GetRecordStatus()?.IsRecording == true);
            }

            string state =
                streamDone && recordDone
                    ? $"stream and recording stopped in {wait.Elapsed.TotalSeconds:0.0} s"
                    : $"after {OutputsStopWait.TotalSeconds:0} s the "
                        + (streamDone ? "recording" : recordDone ? "stream" : "stream and recording")
                        + " still reported active; closing OBS anyway";
            return new ObsOutputsStop(
                true,
                streamDone,
                recordDone,
                string.Join(", ", notes) + "; " + state + "."
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
                // OBS closes next.
            }
        }
    }

    public static void Close(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            process.CloseMainWindow();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    public static bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public static void Kill(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (string.Equals(process.ProcessName, "obs64", StringComparison.OrdinalIgnoreCase))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static bool TryRead(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            // An OBS that does not answer the read is treated as not having that output.
            return false;
        }
    }

    private static bool TrySend(Action send)
    {
        try
        {
            send();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
