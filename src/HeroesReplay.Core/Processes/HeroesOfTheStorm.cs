using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Ocr;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Core.Processes
{
    /// <summary>
    /// A wrapper around the heroes of the storm process and heroes switcher process
    /// </summary>
    public class HeroesOfTheStorm : ProcessWrapper
    {
        public static readonly Key[] KEYS_HEROES = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
        public static readonly Key[] KEYS_CONSOLE_PANEL = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8 };

        public HeroesOfTheStorm(CancellationTokenProvider tokenProvider, CaptureStrategy captureStrategy, ILogger<HeroesOfTheStorm> logger, IConfiguration configuration) : base(tokenProvider, captureStrategy, logger, configuration, Constants.HEROES_PROCESS_NAME)
        {

        }

        public virtual async Task ConfigureClientAsync()
        {
            string[] files = Directory.GetFiles(Constants.USER_GAME_FOLDER, Constants.VARIABLES_WILDCARD, SearchOption.AllDirectories).Where(p => Path.GetFileName(p).Equals("Variables.txt", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (!File.Exists(Constants.STORM_INTERFACE_USER_PATH))
            {
                Logger.LogInformation($"interface not found in: {Constants.STORM_INTERFACE_USER_PATH}");

                File.Copy(Constants.ASSETS_STORM_INTERFACE_PATH, destFileName: Constants.STORM_INTERFACE_USER_PATH, true);

                Logger.LogInformation($"copied interface to: {Constants.STORM_INTERFACE_USER_PATH}");
            }


            foreach (string file in files)
            {
                if (!File.Exists(file + ".bak")) File.Copy(file, file + ".bak");

                string[] lines = await File.ReadAllLinesAsync(file);
                Dictionary<string, string> values = lines.ToDictionary(keySelector => keySelector.Split('=')[0], elementSelector => elementSelector.Split('=')[1]);

                values["observerinterface"] = Constants.STORM_INTERFACE_NAME;
                values["replayinterface"] = Constants.STORM_INTERFACE_NAME;
                //values["camerafollow"] = "false";
                //values["camerasmartpan"] = "false";
                //values["soundglobal"] = "true";
                //values["sound"] = "true";
                //values["soundui"] = "false";
                //values["MusicHeard"] = "1";
                //values["width"] = "1920";
                //values["height"] = "1080";
                //values["windowx"] = "55";
                //values["windowy"] = "55";
                //values["windowstate"] = "1";
                //values["cursorconfinemode"] = "2";
                //values["mousescrollenabled"] = "false";
                //values["mousewheelzoomenabled"] = "false";
                //values["displayreplaytime"] = "false";
                //values["enableAlliedChat"] = "false";

                await File.WriteAllLinesAsync(file, values.OrderBy(kv => kv.Key).Select(pair => $"{pair.Key}={pair.Value}"));

                Logger.LogInformation($"settings in {file} have been updated.");
            }
        }

        public virtual async Task<TimeSpan?> TryGetTimerAsync()
        {
            Rectangle dimensions = CaptureStrategy.GetDimensions(WindowHandle);
            Bitmap? timer = CaptureStrategy.Capture(WindowHandle, new Rectangle(dimensions.Width / 2 - 50, 0, 100, 50));

            if (timer == null) return null;

            try
            {
                using (Bitmap resized = timer.GetResized(zoom: 2))
                {
                    OcrResult? result = await TryGetOcrResult(resized, Constants.Ocr.TIMER_HRS_MINS_SECONDS_SEPERATOR.ToString());
                    if (result != null) return TryParseTimeSpan(result);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, e.Message);
            }
            finally
            {
                timer.Dispose();
            }

            return null;
        }

        public async Task<bool> TryGetMatchAwardsAsync(IEnumerable<MatchAwardType> awards)
        {
            return await GetWindowContainsAnyAsync(awards.ToText().ToArray());
        }

        public virtual async Task<bool> TryKillGameAsync()
        {
            try
            {
                if (IsRunning)
                {
                    Logger.LogWarning($"killing: {ActualProcess.MainModule.FileName}");
                    ActualProcess.Kill();
                    await Task.Delay(TimeSpan.FromSeconds(1), Token);
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"could not kill process: {ProcessName}");
            }

            return false;
        }

        public virtual async Task<bool> LaunchSelectedReplayAsync(StormReplay stormReplay, CancellationToken token = default)
        {
            using (var defaultLaunch = Process.Start("explorer.exe", stormReplay.Path))
            {
                defaultLaunch.WaitForExit();

                return Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == false)
                    .WaitAndRetry(retryCount: 300, retry => TimeSpan.FromSeconds(1))
                    .Execute(t => Process.GetProcessesByName(ProcessName).Any(p => IsMatchingClientVersion(stormReplay, p)), token);
            }
        }

        public virtual async Task<bool> WaitForMapLoadingAsync(StormReplay stormReplay, CancellationToken token = default)
        {
            return await Policy
                .Handle<Exception>() // Issue getting Process information (terminated?)
                .OrResult<bool>(loaded => loaded == false) // it's not the game version that supports the replay
                .WaitAndRetryAsync(retryCount: 60, retry => TimeSpan.FromSeconds(1)) // this can time some time, especially if the game is downloading assets.
                .ExecuteAsync(async t =>
                {
                    string[] names = stormReplay.Replay.Players.Select(p => p.Name).ToArray();
                    return await GetWindowContainsAnyAsync(new[] { Constants.Ocr.LOADING_SCREEN_TEXT, stormReplay.Replay.Map }.Concat(names).ToArray());

                }, token);
        }

        public virtual void SendFocusHero(int index)
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, KEYS_HEROES[index], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, KEYS_HEROES[index], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, KEYS_HEROES[index], IntPtr.Zero);
        }

        public virtual void SendTogglePause()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.P, IntPtr.Zero);
        }

        public virtual void SendToggleChat()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        /// <summary>
        /// The 'Player Camera Follow' mode does NOT work with the Observer maximum zoom mode.
        /// </summary>
        public virtual void SendToggleZoom()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
        }

        public virtual void SendFollow()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.L, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.L, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.L, IntPtr.Zero);
        }

        public virtual void SendToggleTime()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public virtual void SendToggleControls()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public virtual void SendPanelChange(int index)
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, KEYS_CONSOLE_PANEL[index], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, KEYS_CONSOLE_PANEL[index], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public virtual void SendToggleBottomConsole()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public virtual void SendToggleInfoPanel()
        {
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WindowsMessage.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        private bool IsMatchingClientVersion(StormReplay stormReplay, Process p)
        {
            bool match = p.MainModule.FileVersionInfo.FileVersion == stormReplay.Replay.ReplayVersion;

            Logger.LogDebug($"current: {p.MainModule.FileVersionInfo.FileVersion}, required: {stormReplay.Replay.ReplayVersion}");

            return match;
        }

        private TimeSpan? TryParseTimeSpan(OcrResult ocrResult)
        {
            try
            {
                string time = new string(SanitizeOcrTimer(ocrResult));
                string[] segments = time.Split(Constants.Ocr.TIMER_HRS_MINS_SECONDS_SEPERATOR);

                return segments.Length switch
                {
                    Constants.Ocr.TIMER_HOURS => time.ParseTimerHours(),
                    Constants.Ocr.TIMER_MINUTES when segments[0].StartsWith(Constants.Ocr.TIMER_NEGATIVE_PREFIX) => time.ParseNegativeTimerMinutes(),
                    Constants.Ocr.TIMER_MINUTES => time.ParsePositiveTimerMinutes(),
                    _ => throw new Exception($"Unhandled segments: {segments.Length}")
                };
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"could not parse '{ocrResult.Text}' in Ocr result");
            }

            return null;
        }

        private static char[] SanitizeOcrTimer(OcrResult ocrResult)
        {
            return ocrResult.Lines[0].Text
                .Replace("O", "0", StringComparison.OrdinalIgnoreCase)
                .Replace("S", "5", StringComparison.OrdinalIgnoreCase)
                .Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Where(c => char.IsDigit(c) || c.Equals(':') || c.Equals('-'))
                .ToArray();
        }
    }
}