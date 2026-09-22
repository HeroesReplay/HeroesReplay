using System;
using System.Diagnostics;

namespace HeroesReplay.Core.Services.Connectivity;

public static class NamedProcess
{
    public const string HeroesOfTheStorm = "HeroesOfTheStorm_x64";
    public const string BattleNet = "Battle.net";

    public static bool IsRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        Process[] processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException) { }
            }

            return false;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}
