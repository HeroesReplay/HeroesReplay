using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Spectator
{
    /// <summary>
    /// A wrapper around the Battle.net process and Battle.net launcher
    /// </summary>
    public class BattleNet : ProcessWrapper
    {
        private string LauncherPath => Path.Combine(Configuration.GetValue<string>("bnet"), Constants.Bnet.BATTLE_NET_LAUNCHER_EXE);

        private string ProcessPath => Path.Combine(Configuration.GetValue<string>("bnet"), Constants.Bnet.BATTLE_NET_EXE);

        public BattleNet(CancellationTokenProvider tokenProvider, IConfiguration configuration, ILogger<BattleNet> logger) : base(tokenProvider, logger, configuration, Constants.Bnet.BATTLE_NET_PROCESS_NAME)
        {
            
        }

        public async Task<bool> WaitForBattleNetAsync()
        {
            if (IsRunning) return true;

            using (Process p = Process.Start(LauncherPath))
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
                .ExecuteAsync(async (t) =>
                {
                    var selected = await WaitForGameSelectedAsync();

                    if (selected)
                    {
                        ActivatePlayNowButton();
                        return await WaitForGameRunningAsync();
                    }

                    return false;

                }, Token);
        }

        private async Task<bool> IsPlayButtonEnabledAsync()
        {
            return await Policy
                .HandleResult<bool>(enabled => enabled == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(5))
                .ExecuteAsync(async t => await GetWindowContainsAsync(CaptureMethod.PrintScreen, Constants.Ocr.PLAY_BUTTON_TEXT), Token);
        }

        private async Task<bool> WaitForGameRunningAsync()
        {
            return await Policy
                .HandleResult<bool>(running => running == false)
                .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(5))
                .ExecuteAsync(async t => await GetWindowContainsAsync(CaptureMethod.PrintScreen, Constants.Ocr.GAME_RUNNING_TEXT, Constants.Ocr.SHOP_HEROES_TEXT), Token);
        }

        private async Task<bool> WaitForGameSelectedAsync()
        {
            if (IsRunning)
            {
                using (var control = Process.Start(ProcessPath, arguments: Constants.Bnet.BATTLE_NET_SELECT_HEROES_ARG))
                {
                    control.WaitForExit();

                    return await GetWindowContainsAsync(CaptureMethod.PrintScreen, Constants.Ocr.SHOP_HEROES_TEXT);
                }
            }

            return false;
        }

        private void ActivatePlayNowButton()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.Return, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.Return, IntPtr.Zero);
        }
    }
}