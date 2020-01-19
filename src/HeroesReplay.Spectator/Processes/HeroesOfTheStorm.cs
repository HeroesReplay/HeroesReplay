using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Ocr;
using HeroesReplay.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Spectator
{
    /// <summary>
    /// A wrapper around the heroes of the storm process and heroes switcher process
    /// </summary>
    public class HeroesOfTheStorm : ProcessWrapper
    {
        public HeroesOfTheStorm(CancellationTokenProvider tokenProvider, ILogger<HeroesOfTheStorm> logger, IConfiguration configuration) : base(tokenProvider, logger, configuration, Constants.Heroes.HEROES_PROCESS_NAME)
        {

        }

        public async Task SetVariablesAsync()
        {
            string variablesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm", "Variables.txt");

            string[] lines = await File.ReadAllLinesAsync(variablesPath);
            Dictionary<string, string> values = lines.ToDictionary(keySelector => keySelector.Split('=')[0], elementSelector => elementSelector.Split('=')[1]);

            values["observerinterface"] = Constants.STORM_INTERFACE_NAME;
            values["replayinterface"] = Constants.STORM_INTERFACE_NAME;
            values["displayreplaytime"] = "false";
            values["camerafollow"] = "true";
            values["camerasmartpan"] = "true";

            //values["width"] = "1920";
            //values["height"] = "1080";
            //values["windowx"] = "0";
            //values["windowy"] = "0";
            //values["windowstate"] = "1";

            await File.WriteAllLinesAsync(variablesPath, values.OrderBy(kv => kv.Key).Select(pair => $"{pair.Key}={pair.Value}"));
        }

        private Rectangle TimerRegion => new Rectangle(new Point(Dimensions.Value.Width / 2 - 50, 10), new Size(100, 50));

        public async Task<TimeSpan?> TryGetTimerAsync()
        {
            Bitmap? centerTimer = TryBitBlt(TimerRegion);

            try
            {
                if (centerTimer == null) return null;

                // now we don't copy the entire screen with BitBlt, we only copy the TimerRegion region
                //  = GetTimer(centerTimer)
                using (centerTimer)
                {
                    OcrResult? result = await TryGetOcrResult(centerTimer, Constants.Ocr.TIMER_COLON);

                    if (result != null)
                    {
                        TimeSpan? timer = TryParseTimeSpan(result);

                        if (timer != null)
                        {
                            return timer.Value;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, e.Message);
            }
            finally
            {
                centerTimer?.Dispose();
            }

            return null;
        }

        private TimeSpan? TryParseTimeSpan(OcrResult ocrResult)
        {
            try
            {
                // sometimes OCR adds quotemarks, or mixes up 0's with O's
                char[] sanitze = SanitizeOcrResult(ocrResult);

                string time = new string(sanitze);

                string[] segments = time.Split(':');

                TimeSpan timeSpan = segments.Length switch
                {
                    Constants.Ocr.TIMER_HOURS => time.ParseTimerHours(),
                    Constants.Ocr.TIMER_MINUTES when segments[0].StartsWith('-') => time.ParseNegativeTimer(),
                    Constants.Ocr.TIMER_MINUTES => time.ParseTimerMinutes(),
                    _ => throw new Exception($"Unhandled segments: {segments.Length}")
                };

                return timeSpan;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Could not parse '{ocrResult.Text}' in Ocr result");
            }

            return null;
        }

        private static char[] SanitizeOcrResult(OcrResult ocrResult)
        {
            return ocrResult.Lines[0].Text
                .Replace('O', '0')
                .Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace('o', '0')
                .Where(c => char.IsDigit(c) || c.Equals(':'))
                .ToArray();
        }

        public bool KillGame()
        {
            try
            {
                WrappedProcess.Kill();
                Logger.LogInformation($"Killed {ProcessName} process at " + DateTime.Now);
                return true;
            }
            catch (Exception)
            {

            }

            return false;
        }

        public async Task<bool> WaitForSelectedReplayAsync(StormReplay stormReplay, CancellationToken token = default)
        {
            // Execute the switcher program
            FileInfo switcherFileInfo = GetSwitcherPath();

            using (var switcher = Process.Start(switcherFileInfo.FullName, $"\"{stormReplay.FilePath}\""))
            {
                switcher.WaitForExit();

                return await Policy
                    .Handle<InvalidOperationException>()
                        .Or<Win32Exception>()
                        .Or<NullReferenceException>()
                        .OrResult<bool>(result => result == false)
                    .WaitAndRetryAsync(retryCount: 15, count => TimeSpan.FromSeconds(2))
                    .ExecuteAsync(async (t) => Process.GetProcessesByName(ProcessName).Any(p => p.MainModule.FileVersionInfo.FileVersion == stormReplay.Replay.ReplayVersion), token);
            }
        }

        private FileInfo GetSwitcherPath()
        {   
            return Policy
                .Handle<Exception>()
                .WaitAndRetry(retryCount: 5, (retry) => TimeSpan.FromSeconds(10))
                .Execute(() =>
                {
                    DirectoryInfo current = new DirectoryInfo(Path.GetDirectoryName(WrappedProcess.MainModule.FileName));
                    FileInfo? switcher = null;

                    while (switcher == null)
                    {
                        switcher = current.GetFiles(Constants.Heroes.HEROES_SWITCHER_PROCESS, SearchOption.AllDirectories).FirstOrDefault();
                        current = current.Parent;
                    }

                    return switcher;
                });
        }

        public async Task<bool> WaitForMapLoadingAsync(StormReplay stormReplay, CancellationToken token = default)
        {
            return await Policy
                .Handle<Exception>() // Issue getting Process information (terminated?)
                .OrResult<bool>(loaded => loaded == false) // it's not the game version that supports the replay
                .WaitAndRetryAsync(15, count => TimeSpan.FromSeconds(2))
                .ExecuteAsync(async (t) => await GetWindowContainsAsync(CaptureMethod.BitBlt, Constants.Ocr.LOADING_SCREEN_TEXT, stormReplay.Replay.Map), token);
        }

        public void SendFocusHero(int index)
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Constants.Heroes.KEYS_HEROES[index], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Constants.Heroes.KEYS_HEROES[index], IntPtr.Zero);
        }

        public void SendTogglePause()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_CHAR, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.P, IntPtr.Zero);
        }

        public void SendToggleChat()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleZoom()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_CHAR, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.Z, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
        }

        public void SendToggleTime()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_CHAR, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleControls()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_CHAR, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendPanelChange(Panel gamePanel)
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Constants.Heroes.KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Constants.Heroes.KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleBottomConsole()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_CHAR, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleInfoPanel()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }
    }
}