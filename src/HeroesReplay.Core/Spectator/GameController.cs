using Heroes.ReplayParser;

using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

using static PInvoke.User32;

namespace HeroesReplay.Core
{
    public class GameController : IGameController
    {
        private const string ExplorerProcess = "explorer.exe";
        private const string VersionsFolder = "Versions";

        private readonly OcrEngine engine;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ILogger<GameController> logger;
        private readonly Settings settings;
        private readonly CaptureStrategy captureStrategy;

        public static readonly VirtualKey[] Keys =
        {
            VirtualKey.VK_KEY_1,
            VirtualKey.VK_KEY_2,
            VirtualKey.VK_KEY_3,
            VirtualKey.VK_KEY_4,
            VirtualKey.VK_KEY_5,
            VirtualKey.VK_KEY_6,
            VirtualKey.VK_KEY_7,
            VirtualKey.VK_KEY_8,
            VirtualKey.VK_KEY_9,
            VirtualKey.VK_KEY_0
        };

        private bool IsLaunched => Process.GetProcessesByName(settings.Process.HeroesOfTheStorm).Any();
        private Process? Game => Process.GetProcessesByName(settings.Process.HeroesOfTheStorm).FirstOrDefault(x => !string.IsNullOrEmpty(x.MainWindowTitle));
        private IntPtr Handle => Game?.MainWindowHandle ?? IntPtr.Zero;

        public GameController(ILogger<GameController> logger, Settings settings, CaptureStrategy captureStrategy, OcrEngine engine, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.settings = settings;
            this.captureStrategy = captureStrategy;
            this.engine = engine;
            this.tokenProvider = tokenProvider;
        }

        public async Task<StormReplay> LaunchAsync(StormReplay stormReplay)
        {
            if (stormReplay == null) throw new ArgumentNullException(nameof(stormReplay));

            string versionFolder = Path.Combine(settings.Location.GameInstallPath, VersionsFolder);
            int latestBuild = Directory.EnumerateDirectories(versionFolder).Select(x => x).Select(x => int.Parse(Path.GetFileName(x).Replace("Base", string.Empty))).Max();
            var requiresAuth = stormReplay.Replay.ReplayBuild == latestBuild;

            if (IsLaunched && await IsReplay())
            {
                return stormReplay; // already authenticated or in a replay
            }
            else if (IsLaunched && await IsHomeScreen())
            {
                await LaunchAndWait(stormReplay);
            }
            else if (requiresAuth)
            {
                await LaunchGameFromBattlenet();
                await LaunchAndWait(stormReplay);
            }
            else
            {
                await LaunchAndWait(stormReplay);
            }

            return stormReplay;
        }

        private async Task LaunchGameFromBattlenet()
        {
            logger.LogInformation("Launching battlenet because this replay is the latest build and requires auth.");

            using (var process = Process.Start(new ProcessStartInfo(settings.Location.BattlenetPath, $"--game=heroes --gamepath=\"{settings.Location.GameInstallPath}\" --sso=1 -launch -uid heroes")))
            {
                process.WaitForExit();

                var window = Process.GetProcessesByName(settings.Process.Battlenet).Single(x => !string.IsNullOrWhiteSpace(x.MainWindowTitle)).MainWindowHandle;
                SendMessage(window, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_RETURN, IntPtr.Zero);
                SendMessage(window, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_RETURN, IntPtr.Zero);

                logger.LogInformation("Heroes of the Storm launched from Battlenet.");
            }

            // Wait for home screen before launching replay
            var loggedIn = await Policy
                .Handle<Exception>()
                .OrResult<bool>(loaded => loaded == false)
                .WaitAndRetryAsync(retryCount: 60, retry => settings.OCR.CheckSleepDuration)
                .ExecuteAsync(async (t) => await IsHomeScreen(), this.tokenProvider.Token);

            if (!loggedIn)
            {
                logger.LogInformation("The game was launched, but we did not end up on the home screen. Killing game.");

                KillGame();

                await Task.Delay(settings.Spectate.WaitingTime);

                await LaunchGameFromBattlenet();
            }

            logger.LogInformation("Heroes of the Storm Home Screen detected");
        }

        private async Task LaunchAndWait(StormReplay stormReplay)
        {
            // This will make the HeroSwitcher communicate with existing game to launch selected replay
            using (var defaultLaunch = Process.Start(ExplorerProcess, stormReplay.Path))
            {
                Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == false)
                    .WaitAndRetry(retryCount: 150, retry => settings.OCR.CheckSleepDuration)
                    .Execute(() => IsMatchingClientVersion(stormReplay.Replay));
            }

            var searchTerms = stormReplay.Replay.Players
                .Select(x => x.Name)
                .Concat(stormReplay.Replay.Players.Select(x => x.Character))
                .Concat(settings.OCR.LoadingScreenText)
                .Concat(new[] { stormReplay.Replay.Map });

            await Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == false)
                    .WaitAndRetryAsync(retryCount: 60, retry => settings.OCR.CheckSleepDuration)
                    .ExecuteAsync(async (t) => await ContainsAnyAsync(searchTerms), this.tokenProvider.Token);
        }

        public async Task<TimeSpan?> TryGetTimerAsync()
        {
            if (Handle == IntPtr.Zero) return null;

            Bitmap? timerBitmap = null;

            try
            {
                Rectangle dimensions = captureStrategy.GetDimensions(Handle);
                timerBitmap = GetTopTimerAhliObsInterface(dimensions); // settings.Toggles.DefaultInterface ? GetTopTimerDefaultInterface(dimensions) : 
                if (timerBitmap == null) return null;
                return await ConvertBitmapTimerToTimeSpan(timerBitmap);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get timer from bitmap");
            }
            finally
            {
                timerBitmap?.Dispose();
            }

            return null;
        }

        private async Task<TimeSpan?> ConvertBitmapTimerToTimeSpan(Bitmap bitmap)
        {
            using (Bitmap resized = bitmap.GetResized(zoom: 2))
            {
                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(resized))
                {
                    OcrResult ocrResult = await engine.RecognizeAsync(softwareBitmap);
                    TimeSpan? timer = TryParseTimeSpan(ocrResult.Text);

                    if (timer.HasValue)
                    {
                        return timer.RemoveNegativeOffset(settings.Spectate.GameLoopsOffset, settings.Spectate.GameLoopsPerSecond);
                    }
                    else if (settings.Capture.SaveCaptureFailureCondition)
                    {
                        resized.Save(Path.Combine(settings.CapturesPath, DateTime.Now.ToString(), ".bmp"));
                    }

                    return null;
                }
            }
        }

        private Bitmap GetTopTimerAhliObsInterface(Rectangle dimensions)
        {
            var width = dimensions.Width;
            var column = dimensions.Width / 50;
            var start = width / 2 - column;
            var end = column * 2;

            return captureStrategy.Capture(Handle, new Rectangle(dimensions.Width / 2 - 50, 0, 100, 50));
        }

        private Bitmap GetTopTimerDefaultInterface(Rectangle dimensions)
        {
            Bitmap timerBitmap;
            var width = dimensions.Width;
            var column = dimensions.Width / 50;
            var start = width / 2 - column;
            var end = column * 2;

            timerBitmap = captureStrategy.Capture(Handle, new Rectangle(start, 0, end, 200));
            return timerBitmap;
        }

        private bool IsMatchingClientVersion(Replay replay)
        {
            try
            {
                if (Game != null)
                {
                    logger.LogInformation($"Current: {Game?.MainModule.FileVersionInfo.FileVersion}");
                    logger.LogInformation($"Required: {replay.ReplayVersion}");
                    return Game?.MainModule.FileVersionInfo.FileVersion == replay.ReplayVersion;
                }
                else
                {
                    logger.LogInformation($"Game not launched.");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not retrieve process version information.");
            }

            return false;
        }

        private static async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                bitmap.Save(stream.AsStream(), ImageFormat.Bmp);
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, stream);
                return await decoder.GetSoftwareBitmapAsync();
            }
        }

        private TimeSpan? TryParseTimeSpan(string text)
        {
            try
            {
                string time = new string(SanitizeOcrTimer(text));
                string[] segments = time.Split(settings.OCR.TimerSeperator);

                if (segments.Length == settings.OCR.TimerHours) return time.ParseTimerHours(settings.OCR.TimeSpanFormatHours);
                else if (segments.Length == settings.OCR.TimerMinutes && segments[0].StartsWith(settings.OCR.TimerNegativePrefix)) return time.ParseNegativeTimerMinutes(settings.OCR.TimeSpanFormatMatchStart);
                else if (segments.Length == settings.OCR.TimerMinutes) return time.ParsePositiveTimerMinutes(settings.OCR.TimerSeperator);

                throw new Exception($"Unhandled segments: {segments.Length}");
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static char[] SanitizeOcrTimer(string text)
        {
            return text
                .Replace("O", "0", StringComparison.OrdinalIgnoreCase)
                .Replace("S", "5", StringComparison.OrdinalIgnoreCase)
                .Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Where(c => char.IsDigit(c) || c.Equals(':') || c.Equals('-'))
                .ToArray();
        }

        private async Task<bool> ContainsAllAsync(IEnumerable<string> words)
        {
            try
            {
                using (Bitmap capture = captureStrategy.Capture(Handle))
                {
                    using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(capture))
                    {
                        OcrResult result = await engine.RecognizeAsync(softwareBitmap);
                        var containsAll = words.All(word => result.Text.Contains(word));

                        if (containsAll) return true;

                        if (settings.Capture.SaveCaptureFailureCondition)
                        {
                            capture.Save(Path.Combine(settings.CapturesPath, DateTime.Now.ToString()));
                        }
                    }
                }
            }
            catch
            {

            }

            return false;
        }

        private async Task<bool> IsHomeScreen() => await ContainsAnyAsync(settings.OCR.HomeScreenText);

        private async Task<bool> IsReplay() => (await TryGetTimerAsync()) != null;

        private async Task<bool> ContainsAnyAsync(IEnumerable<string> words)
        {
            using (Bitmap capture = captureStrategy.Capture(Handle))
            {
                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(capture))
                {
                    OcrResult result = await engine.RecognizeAsync(softwareBitmap);
                    var containsAny = words.Any(word => result.Text.Contains(word));

                    if (containsAny) return true;

                    if (settings.Capture.SaveCaptureFailureCondition)
                    {
                        capture.Save(Path.Combine(settings.CapturesPath, DateTime.Now.ToString()));
                    }
                }
            }

            return false;
        }

        public void SendFocus(int index)
        {
            IntPtr key = (IntPtr)Keys[index];
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, key, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, key, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, key, IntPtr.Zero);
        }

        public void ToggleChatWindow()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_C, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_C, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_C, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void CameraFollow()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_L, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_L, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_L, IntPtr.Zero);
        }

        public void ToggleTimer()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_T, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_T, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_T, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void ToggleControls()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_O, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_O, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_O, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void SendToggleMaximumZoom()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
        }

        public void SendToggleMediumZoom()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
        }

        public void SendPanel(Panel panel)
        {
            IntPtr Key = (IntPtr)Keys[(int)panel];
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, Key, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, Key, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void ToggleUnitPanel()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_MENU, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_K, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_K, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_K, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_MENU, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
        }

        public void KillGame()
        {
            try
            {
                Game?.Kill();
                logger.LogInformation("Game process has been killed.");
            }
            catch(Exception e)
            {
                logger.LogError(e, "Could not kill game process.");
            }
        }
    }
}