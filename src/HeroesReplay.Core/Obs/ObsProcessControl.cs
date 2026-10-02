using System;
using System.Diagnostics;
using System.IO;
using HeroesReplay.Core.Connectivity;

namespace HeroesReplay.Core.Obs;

internal interface IObsProcess
{
    bool IsRunning();
    bool ExecutableExists(string path);
    bool IsOwned { get; }
    ObsLaunchDecision Start(ObsLaunchDecision decision);
    void CloseOwned();
}

/// <summary>
/// Production launcher. Tests must not use this type; they pass a fake <see cref="IObsProcess"/>.
/// </summary>
internal sealed class WindowsObsProcess : IObsProcess
{
    private Process owned;

    public bool IsOwned
    {
        get
        {
            if (owned == null)
            {
                return false;
            }

            try
            {
                return !owned.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public bool IsRunning() => NamedProcess.IsRunning(ObsLaunchDecision.ProcessName);

    public bool ExecutableExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public ObsLaunchDecision Start(ObsLaunchDecision decision)
    {
        if (decision == null || decision.Kind != ObsLaunchKind.Launch)
        {
            return decision
                ?? new ObsLaunchDecision
                {
                    Kind = ObsLaunchKind.Skipped,
                    Detail = "OBS launch was not requested.",
                };
        }

        string directory = Path.GetDirectoryName(decision.ExecutablePath);
        owned = Process.Start(
            new ProcessStartInfo
            {
                FileName = decision.ExecutablePath,
                Arguments = decision.Arguments ?? string.Empty,
                WorkingDirectory = string.IsNullOrEmpty(directory) ? "" : directory,
                UseShellExecute = true,
            }
        );
        return decision with { Started = owned != null };
    }

    public void CloseOwned()
    {
        Process process = owned;
        owned = null;
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
            }
        }
        catch (InvalidOperationException)
        {
            // The process we started is already gone. Do not kill obs64 by name.
        }
        catch (Exception)
        {
            // Close failed. Do not kill an OBS process to finish shutdown.
        }
        finally
        {
            process.Dispose();
        }
    }
}
