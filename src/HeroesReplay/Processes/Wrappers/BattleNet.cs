using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay
{
    public class BattleNet : ProcessWrapper
    {
        private readonly string launcherPath;
        private readonly string battlenetDirectory;
        private readonly string selectHeroesArgument;
        private readonly string battlenetExe;

        public BattleNet(IConfiguration configuration, ILogger<BattleNet> logger) : base(logger, "Battle.net", Path.Combine(configuration.GetValue<string>("bnet")))
        {
            battlenetDirectory = configuration.GetValue<string>("bnet");
            launcherPath = Path.Combine(battlenetDirectory, "Battle.net Launcher.exe");
            battlenetExe = Path.Combine(battlenetDirectory, "Battle.net.exe");
            selectHeroesArgument = "--game heroes";
        }

        public async Task<bool> WaitForBattleNetAsync(CancellationToken token = default)
        {
            if (IsRunning) return true;

            using (Process p = Process.Start(launcherPath))
            {
                p.WaitForExit();

                return await Policy
                    .HandleResult<bool>(result => result == false)
                    .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(10))
                    .ExecuteAsync(async t => IsRunning, token);
            }
        }

        public async Task<bool> WaitForGameLaunchedAsync(CancellationToken token = default)
        {
            return await Policy
                .HandleResult<bool>(result => result == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(10))
                .ExecuteAsync(async () =>
                {
                    var selected = await WaitForGameSelectedAsync(token);

                    if (selected)
                    {
                        ActivatePlayNowButton();
                        return await WaitForGameRunningAsync(token);
                    }

                    return false;

                });
        }

        private async Task<bool> IsPlayButtonEnabledAsync(CancellationToken token = default)
        {
            return await Policy
                .HandleResult<bool>(enabled => enabled == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(5))
                .ExecuteAsync(async t => await GetWindowContainsAsync(WindowScreenCapture.CPU, "PLAY"), token);
        }

        private async Task<bool> WaitForGameRunningAsync(CancellationToken token = default)
        {
            return await Policy
                .HandleResult<bool>(running => running == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(5))
                .ExecuteAsync(async t => await GetWindowContainsAsync(WindowScreenCapture.CPU, "Game is running.", "Shop Heroes of the Storm"), token);
        }

        private async Task<bool> WaitForGameSelectedAsync(CancellationToken token = default)
        {
            if (IsRunning)
            {
                using (var p = Process.Start(battlenetExe, arguments: selectHeroesArgument))
                {
                    p.WaitForExit();

                    return await GetWindowContainsAsync(WindowScreenCapture.CPU, "Shop Heroes of the Storm");
                }
            }

            return false;
        }

        private void ActivatePlayNowButton()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.Return, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.Return, IntPtr.Zero);
        }
    }
}