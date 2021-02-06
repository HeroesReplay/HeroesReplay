using Heroes.ReplayParser;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Models;
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
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

using static PInvoke.User32;
using HeroesReplay.Core.Configuration;
using Polly.CircuitBreaker;

namespace HeroesReplay.Core
{
    public class GameController : IGameController
    {
        private const string ExplorerProcess = "explorer.exe";
        private const string VersionsFolder = "Versions";

        private readonly OcrEngine engine;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ILogger<GameController> logger;
        private readonly AppSettings settings;
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

        private bool IsLaunched => Game != null;
        private Process Game => Process.GetProcessesByName(settings.Process.HeroesOfTheStorm).FirstOrDefault(x => !string.IsNullOrEmpty(x.MainWindowTitle));
        private IntPtr Handle => Game?.MainWindowHandle ?? IntPtr.Zero;

        public GameController(ILogger<GameController> logger, AppSettings settings, CaptureStrategy captureStrategy, OcrEngine engine, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(settings));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.captureStrategy = captureStrategy ?? throw new ArgumentNullException(nameof(settings));
            this.engine = engine ?? throw new ArgumentNullException(nameof(settings)); ;
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<StormReplay> LaunchAsync(StormReplay stormReplay)
        {
            if (stormReplay == null)
            {
                throw new ArgumentNullException(nameof(stormReplay));
            }

            string versionFolder = Path.Combine(settings.Location.GameInstallPath, VersionsFolder);
            int latestBuild = Directory.EnumerateDirectories(versionFolder).Select(x => x).Select(x => int.Parse(Path.GetFileName(x).Replace("Base", string.Empty))).Max();
            var requiresAuth = stormReplay.Replay.ReplayBuild == latestBuild;

            if (IsLaunched && await IsReplay().ConfigureAwait(false))
            {
                return stormReplay; // already authenticated or in a replay
            }
            else if (IsLaunched && await IsHomeScreen().ConfigureAwait(false))
            {
                await LaunchAndWait(stormReplay).ConfigureAwait(false);
            }
            else if (requiresAuth)
            {
                await LaunchGameFromBattlenet().ConfigureAwait(false);
                await LaunchAndWait(stormReplay).ConfigureAwait(false);
            }
            else
            {
                await LaunchAndWait(stormReplay).ConfigureAwait(false);
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
                .ExecuteAsync(async (t) => await IsHomeScreen().ConfigureAwait(false), this.tokenProvider.Token).ConfigureAwait(false);

            if (!loggedIn)
            {
                logger.LogInformation("The game was launched, but we did not end up on the home screen. Killing game.");

                KillGame();

                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await LaunchGameFromBattlenet().ConfigureAwait(false);
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
                    .WaitAndRetry(retryCount: 150, sleepDurationProvider: retry => settings.OCR.CheckSleepDuration)
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
                    .WaitAndRetryAsync(retryCount: 60, sleepDurationProvider: retry => settings.OCR.CheckSleepDuration)
                    .ExecuteAsync((t) => ContainsAnyAsync(searchTerms), this.tokenProvider.Token).ConfigureAwait(false);
        }

        public async Task<TimeSpan?> TryGetTimerAsync()
        {
            if (Handle == IntPtr.Zero) return null;

            Bitmap timerBitmap = null;

            try
            {
                timerBitmap = GetNegativeOffsetTimer();
                if (timerBitmap == null) return null;

                if (settings.Capture.SaveTimerRegion)
                {
                    timerBitmap.Save(Path.Combine(settings.CapturesPath, "timer-" + Guid.NewGuid().ToString() + ".bmp"));
                }

                return await ConvertBitmapTimerToTimeSpan(timerBitmap).ConfigureAwait(false);
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
            using (Bitmap resized = bitmap.GetResized(zoom: 4))
            {
                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(resized).ConfigureAwait(false))
                {
                    OcrResult ocrResult = await engine.RecognizeAsync(softwareBitmap);
                    TimeSpan? timer = TryParseTimeSpan(ocrResult.Text);

                    if (timer.HasValue) return timer;
                    else if (settings.Capture.SaveCaptureFailureCondition)
                    {
                        Directory.CreateDirectory(settings.CapturesPath);
                        resized.Save(Path.Combine(settings.CapturesPath, Guid.NewGuid().ToString() + ".bmp"));
                    }

                    return null;
                }
            }
        }

        private Bitmap GetNegativeOffsetTimer()
        {
            const int MIN_HEIGHT_FOR_OCR_TO_WORK = 50;
            Rectangle dimensions = captureStrategy.GetDimensions(Handle);
            var width = dimensions.Width;
            var column = dimensions.Width / 50;
            var start = width / 2 - column;
            var end = column * 2;

            return captureStrategy.Capture(Handle, new Rectangle(start, 0, end, MIN_HEIGHT_FOR_OCR_TO_WORK));
        }

        private bool IsMatchingClientVersion(Replay replay)
        {
            try
            {
                if (Game != null)
                {
                    logger.LogInformation($"Current: {Game.MainModule.FileVersionInfo.FileVersion}");
                    logger.LogInformation($"Required: {replay.ReplayVersion}");
                    return Game.MainModule.FileVersionInfo.FileVersion == replay.ReplayVersion;
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

                if (segments.Length == settings.OCR.TimerHours)
                {
                    return time.ParseTimerHours(settings.OCR.TimeSpanFormatHours);
                }
                else if (segments.Length == settings.OCR.TimerMinutes && segments[0].StartsWith(settings.OCR.TimerNegativePrefix))
                {
                    return time.ParseNegativeTimerMinutes(settings.OCR.TimeSpanFormatMatchStart);
                }
                else if (segments.Length == settings.OCR.TimerMinutes)
                {
                    return time.ParsePositiveTimerMinutes(settings.OCR.TimerSeperator);
                }

                throw new Exception($"Unhandled segments: {segments.Length}");
            }
            catch (Exception)
            {
                logger.LogWarning($"Could not parse the timer: {text ?? string.Empty}");                
            }

            return null;
        }

        private static char[] SanitizeOcrTimer(string text)
        {
            return text
                .Replace("O", "0", StringComparison.OrdinalIgnoreCase)
                .Replace("L", "1", StringComparison.OrdinalIgnoreCase)
                .Replace("Z", "2", StringComparison.OrdinalIgnoreCase)
                .Replace("E", "3", StringComparison.OrdinalIgnoreCase)
                .Replace("A", "4", StringComparison.OrdinalIgnoreCase)
                .Replace("S", "5", StringComparison.OrdinalIgnoreCase)
                .Replace("G", "6", StringComparison.OrdinalIgnoreCase)
                .Replace("T", "7", StringComparison.OrdinalIgnoreCase)
                .Replace("B", "13", StringComparison.OrdinalIgnoreCase)
                .Replace(".", ":", StringComparison.OrdinalIgnoreCase)
                .Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Where(c => char.IsDigit(c) || c.Equals(':') || c.Equals('-'))
                .ToArray();
        }

        private async Task<bool> IsHomeScreen() => IsLaunched && await ContainsAnyAsync(settings.OCR.HomeScreenText).ConfigureAwait(false);

        private async Task<bool> IsReplay() => IsLaunched && (await TryGetTimerAsync().ConfigureAwait(false)) != null;

        private async Task<bool> ContainsAnyAsync(IEnumerable<string> words)
        {
            using (Bitmap capture = captureStrategy.Capture(Handle))
            {
                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(capture).ConfigureAwait(false))
                {
                    OcrResult result = await engine.RecognizeAsync(softwareBitmap);
                    var containsAny = words.Any(word => result.Text.Contains(word));

                    if (containsAny) return true;

                    if (settings.Capture.SaveCaptureFailureCondition)
                    {
                        Directory.CreateDirectory(settings.CapturesPath);
                        capture.Save(Path.Combine(settings.CapturesPath, Guid.NewGuid().ToString() + ".bmp"));
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
                Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == true)
                    .WaitAndRetry(retryCount: 10, sleepDurationProvider: retry => settings.OCR.CheckSleepDuration)
                    .Execute(() =>
                    {
                        foreach(var process in Process.GetProcessesByName(this.settings.Process.HeroesOfTheStorm))
                        {
                            process.Kill();
                        }

                        return !Process.GetProcessesByName(this.settings.Process.HeroesOfTheStorm).Any();
                    });

                logger.LogInformation("Game process has been killed.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not kill game process.");
            }
        }
    }
}