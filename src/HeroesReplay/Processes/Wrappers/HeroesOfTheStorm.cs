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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay
{
    public class HeroesOfTheStorm : ProcessWrapper
    {
        private const int Hours = 3;
        private const int Minutes = 2;
        private const string TimeSeperator = ":";
        private const string StormInterface = "AhliObs 0.66.StormInterface";

        private readonly Key[] KEYS_HEROES = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 };
        private readonly Key[] KEYS_CONSOLE_PANEL = { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8 };

        public HeroesOfTheStorm(IConfiguration configuration, ILogger<HeroesOfTheStorm> logger) : base(logger, "HeroesOfTheStorm_x64", Path.Combine(configuration.GetValue<string>("hots"), "Support64", "HeroesSwitcher_x64.exe"))
        {

        }

        public async Task SetGameVariables()
        {
            string variablesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm", "Variables.txt");

            string[] lines = await File.ReadAllLinesAsync(variablesPath);
            Dictionary<string, string> values = lines.ToDictionary(keySelector => keySelector.Split('=')[0], elementSelector => elementSelector.Split('=')[1]);

            values["observerinterface"] = StormInterface;
            values["replayinterface"] = StormInterface;
            values["displayreplaytime"] = "false";

            await File.WriteAllLinesAsync(variablesPath, values.OrderBy(kv => kv.Key).Select(pair => $"{pair.Key}={pair.Value}"));
        }

        public async Task<TimeSpan?> TryGetTimerAsync()
        {
            Bitmap? bitmap = TryBitBlt();

            try
            {
                if (bitmap == null) return null;

                using (var controls = GetControlsClosed(bitmap))
                {
                    OcrResult? result = await TryGetOcrResult(controls, TimeSeperator);
                    return result != null ? TryParseTimeSpan(result) : null;
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
                var time = ocrResult.Lines[0].Text;
                var segments = time.Split(TimeSeperator);

                return segments.Length switch
                {
                    Hours => new TimeSpan(hours: int.Parse(segments[0]), minutes: int.Parse(segments[1]), seconds: int.Parse(segments[2])),
                    Minutes => new TimeSpan(hours: 0, minutes: int.Parse(segments[0]), seconds: int.Parse(segments[1])),
                    _ => throw new Exception($"Unhandled {TimeSeperator} count: {segments.Length}")
                };
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Could not parse '{ocrResult.Text}' in OCR result");
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
            using (var switcher = Process.Start(ProcessPath, $"\"{stormReplay.FilePath}\""))
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

        public async Task<bool> WaitForMapLoadingAsync(StormReplay stormReplay, CancellationToken token = default)
        {
            return await Policy
                .Handle<Exception>() // Issue getting Process information (terminated?)
                .OrResult<bool>(loaded => loaded == false) // it's not the game version that supports the replay
                .WaitAndRetryAsync(10, count => TimeSpan.FromSeconds(2))
                .ExecuteAsync(async (t) =>
                {
                    // stormReplay.Replay.Players.Select(p => p.Name) // We 'could' check player names too.
                    return await GetWindowContainsAsync(WindowScreenCapture.GPU, "WELCOME TO", stormReplay.Replay.Map);

                }, token);
        }

        public void SendFocusHero(int index)
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, KEYS_HEROES[index], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, KEYS_HEROES[index], IntPtr.Zero);
        }

        public void SendTogglePause()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.P, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.P, IntPtr.Zero);
        }

        public void SendToggleChat()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleZoom()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.Z, IntPtr.Zero);
            // NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.Z, IntPtr.Zero);
            // NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.Z, IntPtr.Zero);
            // NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
        }

        public void SendToggleTime()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.T, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleControls()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.O, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ShiftKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendPanelChange(Panel gamePanel)
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, KEYS_CONSOLE_PANEL[(int)gamePanel], IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleBottomConsole()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.W, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendToggleInfoPanel()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.ControlKey, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_CHAR, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.C, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.ControlKey, IntPtr.Zero);
        }

        public void SendEnterByHandle()
        {
            NativeMethods.SendMessage(WindowHandle, WM_KEYDOWN, Key.Return, IntPtr.Zero);
            NativeMethods.SendMessage(WindowHandle, WM_KEYUP, Key.Return, IntPtr.Zero);
        }

        // Observer Controls
        private Bitmap GetControlsOpen(Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(new Point(20, bitmap.Height - 170), new Size(480, 160)), bitmap.PixelFormat);
        }

        // Observer Controls
        private Bitmap GetControlsClosed(Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(new Point(20, bitmap.Height - 135), new Size(200, 50)), bitmap.PixelFormat);
        }

        private Bitmap GetTopTimer(Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(new Point(bitmap.Width / 2 - 50, 15), new Size(100, 30)), bitmap.PixelFormat);
        }
    }
}