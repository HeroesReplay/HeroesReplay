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
        private readonly OcrEngine engine;
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

        public GameController(ILogger<GameController> logger, Settings settings, CaptureStrategy captureStrategy, OcrEngine engine)
        {
            this.logger = logger;
            this.settings = settings;
            this.captureStrategy = captureStrategy;
            this.engine = engine;
        }

        public async Task<StormReplay> LaunchAsync(StormReplay stormReplay)
        {
            int latestBuild = Directory.EnumerateDirectories(Path.Combine(settings.Location.GameInstallPath, "Versions")).Select(x => x).Select(x => int.Parse(Path.GetFileName(x).Replace("Base", string.Empty))).Max();
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
            logger.LogInformation("Launching battlenet because replay requires auth.");

            using (var process = Process.Start(new ProcessStartInfo(settings.Location.BattlenetPath, $"--game=heroes --gamepath=\"{settings.Location.GameInstallPath}\" --sso=1 -launch -uid heroes")))
            {
                process.WaitForExit();

                var window = Process.GetProcessesByName(settings.Process.Battlenet).Single(x => !string.IsNullOrWhiteSpace(x.MainWindowTitle)).MainWindowHandle;
                SendMessage(window, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_RETURN, (IntPtr)IntPtr.Zero);
                SendMessage(window, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_RETURN, (IntPtr)IntPtr.Zero);

                logger.LogInformation("Heroes of the Storm launched from Battlenet.");
            }

            // Wait for home screen before launching replay
            await Policy.Handle<Exception>()
                .OrResult<bool>(loaded => loaded == false)
                .WaitAndRetryAsync(retryCount: 20, retry => TimeSpan.FromSeconds(1))
                .ExecuteAsync(async (t) => await IsHomeScreen(), CancellationToken.None);

            logger.LogInformation("Heroes of the Storm Home Screen detected");
        }

        private async Task LaunchAndWait(StormReplay stormReplay)
        {
            // This will make the HeroSwitcher communicate with existing game to launch selected replay
            using (var defaultLaunch = Process.Start("explorer.exe", stormReplay.Path))
            {
                Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == false)
                    .WaitAndRetry(retryCount: 300, retry => TimeSpan.FromSeconds(2))
                    .Execute(() => IsMatchingClientVersion(stormReplay.Replay));
            }

            var searchTerms = stormReplay.Replay.Players.Select(x => x.Name).Concat(stormReplay.Replay.Players.Select(x => x.Character)).Concat(settings.OCR.LoadingScreenText).Concat(new[] { stormReplay.Replay.Map });

            await Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == false)
                    .WaitAndRetryAsync(retryCount: 300, retry => TimeSpan.FromSeconds(2))
                    .ExecuteAsync(async (t) => await ContainsAnyAsync(searchTerms), CancellationToken.None);
        }

        public async Task<TimeSpan?> TryGetTimerAsync()
        {
            if (Handle == IntPtr.Zero) return null;

            Rectangle dimensions = captureStrategy.GetDimensions(Handle);

            Bitmap timerBitmap = settings.Toggles.DefaultInterface ? GetTopTimerDefaultInterface(dimensions) : GetTopTimerAhliObsInterface(dimensions);

            if (timerBitmap == null) return null;

            try
            {
                return await ConvertBitmapTimerToTimeSpan(timerBitmap);
            }
            finally
            {
                timerBitmap.Dispose();
            }
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
                    else if (settings.Toggles.SaveCaptureFailureCondition)
                    {
                        resized.Save(Path.Combine(settings.Capture.ConditionFailurePath, DateTime.Now.ToString()));
                    }

                    return null;
                }
            }
        }

        private Bitmap GetTopTimerAhliObsInterface(Rectangle dimensions) => captureStrategy.Capture(Handle, new Rectangle(dimensions.Width / 2 - 50, 0, 100, 50));

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

        private async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
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

        private char[] SanitizeOcrTimer(string text)
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

                        if (settings.Toggles.SaveCaptureFailureCondition)
                        {
                            capture.Save(Path.Combine(settings.Capture.ConditionFailurePath, DateTime.Now.ToString()));
                        }
                    }
                }
            }
            catch
            {

            }

            return false;
        }

        private async Task<bool> IsHomeScreen() => await ContainsAllAsync(settings.OCR.HomeScreenText);

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

                    if (settings.Toggles.SaveCaptureFailureCondition)
                    {
                        capture.Save(Path.Combine(settings.Capture.ConditionFailurePath, DateTime.Now.ToString()));
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

        public void SendToggleZoom()
        {
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
        }

        public void SendPanel(int index)
        {
            IntPtr Key = (IntPtr)Keys[index];
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYDOWN, Key, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, Key, IntPtr.Zero);
            SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void KillGame()
        {
            try
            {
                Game?.Kill();
            }
            catch
            {

            }
        }
    }
}