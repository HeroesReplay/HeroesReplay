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
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Spectator
{
    /// <summary>
    /// A wrapper around the heroes of the storm process and heroes switcher process
    /// </summary>
    public class HeroesOfTheStorm : ProcessWrapper
    {
        private readonly Key[] KEYS_HEROES = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
        private readonly Key[] KEYS_CONSOLE_PANEL = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8 };

        public HeroesOfTheStorm(CancellationTokenProvider tokenProvider, ILogger<HeroesOfTheStorm> logger) : base(tokenProvider, logger, Constants.Heroes.HEROES_PROCESS_NAME, string.Empty)
        {

        }

        public async Task SetGameVariables()
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

        public async Task<TimeSpan?> TryGetTimerAsync()
        {
            Bitmap? bitmap = TryBitBlt();

            try
            {
                if (bitmap == null) return null;

                using (Bitmap centerTimer = GetTimer(bitmap))
                {
                    OcrResult? result = await TryGetOcrResult(centerTimer, Constants.Ocr.TIMER_COLON);

                    if (result != null)
                    {
                        TimeSpan? timer = TryParseTimeSpan(result);

                        if (timer != null)
                        {
                            return timer.Value.AddPositiveOffset();
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
                bitmap?.Dispose();
            }

            return null;
        }

        private TimeSpan? TryParseTimeSpan(OcrResult ocrResult)
        {
            try
            {
                string time = ocrResult.Lines[0].Text;
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
                    .WaitAndRetryAsync(5, count => TimeSpan.FromSeconds(10))
                    .ExecuteAsync(async (t) => Process.GetProcessesByName(ProcessName).Any(p => p.MainModule.FileVersionInfo.FileVersion == stormReplay.Replay.ReplayVersion), token);
            }
        }

        private FileInfo GetSwitcherPath()
        {
            DirectoryInfo current = new DirectoryInfo(Path.GetDirectoryName(WrappedProcess.MainModule.FileName));
            FileInfo? switcher = null;

            return Policy
                .Timeout(TimeSpan.FromSeconds(10))
                .Execute(() =>
                {
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
            // check for a total of 30 seconds to see if the Loading Screen appears

            return await Policy
                .Handle<Exception>() // Issue getting Process information (terminated?)
                .OrResult<bool>(loaded => loaded == false) // it's not the game version that supports the replay
                .WaitAndRetryAsync(15, count => TimeSpan.FromSeconds(2))
                .ExecuteAsync(async (t) =>
                {
                    // stormReplay.Replay.Players.Select(p => p.Name) // We 'could' check stormPlayer names too.
                    return await GetWindowContainsAsync(CaptureMethod.BitBlt, Constants.Ocr.LOADING_SCREEN_TEXT, stormReplay.Replay.Map);

                }, token);
        }

        public void SendFocusHero(int index)
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, KEYS_HEROES[index], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, KEYS_HEROES[index], IntPtr.Zero);
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
            // NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.Z, IntPtr.Zero);
            // NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.Z, IntPtr.Zero);
            // NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
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
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
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

        public void SendEnterByHandle()
        {
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYDOWN, Key.Return, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, Constants.WM_KEYUP, Key.Return, IntPtr.Zero);
        }

        // Mini map
        private Bitmap GetBottomRight(Bitmap bitmap)
        {
            return bitmap.Clone(Rectangle.FromLTRB(left: bitmap.Width / 2, top: bitmap.Height / 2, right: bitmap.Width, bottom: bitmap.Height), bitmap.PixelFormat);
        }

        // Observer Controls
        private Bitmap GetBottomLeft(Bitmap bitmap)
        {
            return bitmap.Clone(Rectangle.FromLTRB(left: 0, top: bitmap.Height / 2, right: bitmap.Width / 2, bottom: bitmap.Height), bitmap.PixelFormat);
        }

        // Observer Controls
        private Bitmap GetControlsOpen(Bitmap bitmap)
        {
            return bitmap.Clone(
                new Rectangle(
                    new Point(x: 0, y: bitmap.Height - Convert.ToInt32(bitmap.Height * 0.15)), // The controls are roughly 10% of the height of the game
                    new Size(width: Convert.ToInt32(bitmap.Width * 0.20), height: Convert.ToInt32(bitmap.Height * 0.15))), bitmap.PixelFormat); // they roughly make up 20% of the width of the game
        }

        // Observer Controls
        private Bitmap GetControlsClosed(Bitmap bitmap)
        {
            return bitmap.Clone(
                new Rectangle(new Point(x: 0, y: bitmap.Height - Convert.ToInt32(bitmap.Height * 0.10)),
                    new Size(width: Convert.ToInt32(bitmap.Width * 0.20),
                        height: Convert.ToInt32(bitmap.Height * 0.10))), bitmap.PixelFormat);
        }

        // This doesn't seem to work so well with the Windows.Media.Ocr 
        // Maybe I need to cleanup and manipulate the Bitmap before sending it to Ocr processing
        // I think its also to do with the resolution of the bitmap, i may need to enlarge it?
        private Bitmap GetTimer(Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(new Point(bitmap.Width / 2 - 50, 10), new Size(100, 50)), bitmap.PixelFormat);
        }

    }
}