using Heroes.ReplayParser;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Shared;

using Polly;
using Microsoft.Extensions.Configuration;

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
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core
{
    public class GameController : IGameController
    {
        private readonly OcrEngine engine;
        private readonly ILogger<GameController> logger;
        private readonly CaptureStrategy captureStrategy;
        private readonly int gameLoopsOffset;
        private readonly int gameLoopsPerSecond;
        private readonly string battleNetPath;
        private readonly string heroesInstallPath;
        private readonly bool isDefaultInterface;

        private bool IsLaunched => Process.GetProcessesByName(Constants.HEROES_PROCESS_NAME).Any();
        private Process Game => Process.GetProcessesByName(Constants.HEROES_PROCESS_NAME).FirstOrDefault(x => !string.IsNullOrEmpty(x.MainWindowTitle));
        private IntPtr Handle => Game?.MainWindowHandle ?? IntPtr.Zero;

        public GameController(ILogger<GameController> logger, IConfiguration configuration, CaptureStrategy captureStrategy, OcrEngine engine)
        {
            this.logger = logger;
            this.captureStrategy = captureStrategy;
            this.gameLoopsOffset = configuration.GetValue<int>("Settings:GameLoopsOffset");
            this.gameLoopsPerSecond = configuration.GetValue<int>("Settings:GameLoopsPerSecond");
            this.battleNetPath = configuration.GetValue<string>("Settings:BattlenetPath");
            this.heroesInstallPath = configuration.GetValue<string>("Settings:GameInstallPath");
            this.isDefaultInterface = configuration.GetValue<bool>("Settings:DefaultInterface");
            this.engine = engine;
        }

        public async Task<StormReplay> LaunchAsync(StormReplay stormReplay)
        {
            int latestBuild = Directory.EnumerateDirectories(Path.Combine(heroesInstallPath, "Versions")).Select(x => x).Select(x => int.Parse(Path.GetFileName(x).Replace("Base", string.Empty))).Max();
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
                await LaunchGameFromBattlenet(stormReplay);
                await LaunchAndWait(stormReplay);
            }
            else
            {
                await LaunchAndWait(stormReplay);
            }

            return stormReplay;
        }

        private async Task LaunchGameFromBattlenet(StormReplay stormReplay)
        {
            logger.LogInformation("Launching battlenet because replay requires auth.");

            using (var process = Process.Start(new ProcessStartInfo(battleNetPath, $"--game=heroes --gamepath=\"{heroesInstallPath}\" --sso=1 -launch -uid heroes")))
            {
                process.WaitForExit();

                var window = Process.GetProcessesByName(Constants.BATTLENET_PROCESS_NAME).Single(x => !string.IsNullOrWhiteSpace(x.MainWindowTitle)).MainWindowHandle;
                PInvoke.User32.SendMessage(window, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_RETURN, (IntPtr)IntPtr.Zero);
                PInvoke.User32.SendMessage(window, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_RETURN, (IntPtr)IntPtr.Zero);

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

            var searchTerms = stormReplay.Replay.Players.Select(x => x.Name).Concat(stormReplay.Replay.Players.Select(x => x.Character)).Concat(new[] { Constants.Ocr.LOADING_SCREEN_TEXT, stormReplay.Replay.Map });

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
            Bitmap timerBitmap = isDefaultInterface ? GetTopTimerDefaultInterface(dimensions) : GetTopTimerAhliObsInterface(dimensions);
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
                    return timer.RemoveNegativeOffset(gameLoopsOffset, gameLoopsPerSecond);
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
            bool match = Game.MainModule.FileVersionInfo.FileVersion == replay.ReplayVersion;
            Console.WriteLine($"Current: {Game.MainModule.FileVersionInfo.FileVersion}");
            Console.WriteLine($"Required: {replay.ReplayVersion}");
            return match;
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
                string[] segments = time.Split(Constants.Ocr.TIMER_HRS_MINS_SECONDS_SEPERATOR);

                return segments.Length switch
                {
                    Constants.Ocr.TIMER_HOURS => time.ParseTimerHours(),
                    Constants.Ocr.TIMER_MINUTES when segments[0].StartsWith(Constants.Ocr.TIMER_NEGATIVE_PREFIX) => time.ParseNegativeTimerMinutes(),
                    Constants.Ocr.TIMER_MINUTES => time.ParsePositiveTimerMinutes(),
                    _ => throw new Exception($"Unhandled segments: {segments.Length}")
                };
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
                        return words.All(word => result.Text.Contains(word));
                    }
                }
            }
            catch
            {

            }

            return false;
        }

        private async Task<bool> IsHomeScreen() => await ContainsAllAsync(Constants.Ocr.HOME_SCREEN_TEXT);

        private async Task<bool> IsReplay() => (await TryGetTimerAsync()) != null;

        private async Task<bool> ContainsAnyAsync(IEnumerable<string> words)
        {
            using (Bitmap capture = captureStrategy.Capture(Handle))
            {
                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(capture))
                {
                    OcrResult result = await engine.RecognizeAsync(softwareBitmap);
                    return words.Any(word => result.Text.Contains(word));
                }
            }
        }

        public void SendFocus(int index)
        {
            PInvoke.User32.VirtualKey key = Constants.KeysHeroes[index];
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)key, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_CHAR, (IntPtr)key, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)key, IntPtr.Zero);
        }

        public void ToggleChatWindow()
        {
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_SHIFT, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_C, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_CHAR, (IntPtr)PInvoke.User32.VirtualKey.VK_C, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_C, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_SHIFT, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void CameraFollow()
        {
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_L, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_CHAR, (IntPtr)PInvoke.User32.VirtualKey.VK_L, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_L, IntPtr.Zero);
        }

        public void ToggleTimer()
        {
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_T, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_CHAR, (IntPtr)PInvoke.User32.VirtualKey.VK_T, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_T, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void ToggleControls()
        {
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_SHIFT, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_O, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_CHAR, (IntPtr)PInvoke.User32.VirtualKey.VK_O, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_O, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_SHIFT, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
        }

        public void SendToggleZoom()
        {
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_SHIFT, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_Z, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_CHAR, (IntPtr)PInvoke.User32.VirtualKey.VK_Z, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_Z, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_SHIFT, IntPtr.Zero);
        }

        public void SendPanel(int index)
        {
            PInvoke.User32.VirtualKey Key = Constants.KeysPanels[index];
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYDOWN, (IntPtr)Key, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)Key, IntPtr.Zero);
            PInvoke.User32.SendMessage(Handle, PInvoke.User32.WindowMessage.WM_KEYUP, (IntPtr)PInvoke.User32.VirtualKey.VK_CONTROL, IntPtr.Zero);
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