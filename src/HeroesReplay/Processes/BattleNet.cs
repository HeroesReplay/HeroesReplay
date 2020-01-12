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
    /// <summary>
    /// A wrapper around the Battle.net process and Battle.net launcher
    /// </summary>
    public class BattleNet : ProcessWrapper
    {
        private readonly string launcherPath;
        
        private const string CLIENT_GAME_RUNNING_TEXT = "Game is running.";
        private const string CLIENT_PLAY_BUTTON_TEXT = "PLAY";
        private const string CLIENT_SHOP_HEROES_TEXT = "Shop Heroes of the Storm";

        private const string BATTLE_NET_LAUNCHER_EXE = "Battle.net Launcher.exe";
        private const string BATTLE_NET_EXE = "Battle.net.exe";
        private const string PROCESS_NAME = "Battle.net";
        private const string SELECT_HEROES_ARG = "--game heroes";

        public BattleNet(IConfiguration configuration, ILogger<BattleNet> logger, CancellationTokenSource source) : base(logger, PROCESS_NAME, Path.Combine(configuration.GetValue<string>("bnet", string.Empty), BATTLE_NET_EXE), source)
        {
            launcherPath = Path.Combine(configuration.GetValue<string>("bnet"), BATTLE_NET_LAUNCHER_EXE);
        }

        public async Task<bool> WaitForBattleNetAsync()
        {
            if (IsRunning) return true;

            using (Process p = Process.Start(launcherPath))
            {
                p.WaitForExit();

                return await Policy
                    .HandleResult<bool>(result => result == false)
                    .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(10))
                    .ExecuteAsync(async t => IsRunning, Token);
            }
        }

        public async Task<bool> WaitForGameLaunchedAsync()
        {
            return await Policy
                .HandleResult<bool>(result => result == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(10))
                .ExecuteAsync(async () =>
                {
                    var selected = await WaitForGameSelectedAsync();

                    if (selected)
                    {
                        ActivatePlayNowButton();
                        return await WaitForGameRunningAsync();
                    }

                    return false;

                });
        }

        private async Task<bool> IsPlayButtonEnabledAsync()
        {
            return await Policy
                .HandleResult<bool>(enabled => enabled == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(5))
                .ExecuteAsync(async t => await GetWindowContainsAsync(WindowScreenCapture.CPU, CLIENT_PLAY_BUTTON_TEXT), Token);
        }

        private async Task<bool> WaitForGameRunningAsync()
        {
            return await Policy
                .HandleResult<bool>(running => running == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(5))
                .ExecuteAsync(async t => await GetWindowContainsAsync(WindowScreenCapture.CPU, CLIENT_GAME_RUNNING_TEXT, CLIENT_SHOP_HEROES_TEXT), Token);
        }

        private async Task<bool> WaitForGameSelectedAsync()
        {
            if (IsRunning)
            {
                using (var p = Process.Start(ProcessPath, arguments: SELECT_HEROES_ARG))
                {
                    p.WaitForExit();

                    return await GetWindowContainsAsync(WindowScreenCapture.CPU, CLIENT_SHOP_HEROES_TEXT);
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