using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analyzer;
using HeroesReplay.Service.Spectator.Core.Context;
using HeroesReplay.Service.Spectator.Core.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    using static PInvoke.User32;

    public class GameController : IGameController
    {
        private const string ExplorerProcess = "explorer.exe";
        private const string VersionsFolder = "Versions";

        private readonly OcrEngine ocrEngine;
        private readonly CancellationTokenSource cts;
        private readonly ILogger<GameController> logger;
        private readonly IReplayContext context;
        private readonly IOptions<AppSettings> settings;
        private readonly CaptureStrategy captureStrategy;

        private readonly object controllerLock = new object();

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

        private bool IsLaunched => GameProcess != null;
        private Process GameProcess => Process.GetProcessesByName(settings.Value.Process.HeroesOfTheStorm).FirstOrDefault(x => !string.IsNullOrEmpty(x.MainWindowTitle));
        private IntPtr Handle => GameProcess?.MainWindowHandle ?? IntPtr.Zero;

        public GameController(ILogger<GameController> logger, IReplayContext context, IOptions<AppSettings> settings, CaptureStrategy captureStrategy, OcrEngine engine, CancellationTokenSource cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(settings));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.captureStrategy = captureStrategy ?? throw new ArgumentNullException(nameof(settings));
            this.ocrEngine = engine ?? throw new ArgumentNullException(nameof(settings)); ;
            this.cts = cts ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task LaunchAsync()
        {
            var replay = context.Current.LoadedReplay.Replay;

            string versionFolder = Path.Combine(settings.Value.Location.GameInstallDirectory, VersionsFolder);
            int latestBuild = Directory.EnumerateDirectories(versionFolder).Select(x => x).Select(x => int.Parse(Path.GetFileName(x).Replace("Base", string.Empty))).Max();
            var requiresAuth = replay.ReplayBuild == latestBuild;

            if (IsLaunched && await IsReplay().ConfigureAwait(false))
            {
                return;
            }
            else if (IsLaunched && await IsHomeScreen().ConfigureAwait(false))
            {
                await LaunchAndWait().ConfigureAwait(false);
            }
            else if (requiresAuth)
            {
                await LaunchGameFromBattlenet().ConfigureAwait(false);
                await LaunchAndWait().ConfigureAwait(false);
            }
            else
            {
                await LaunchAndWait().ConfigureAwait(false);
            }
        }

        private async Task LaunchGameFromBattlenet()
        {
            logger.LogInformation("Launching battlenet because this replay is the latest build and requires auth.");

            using (Process process = Process.Start(new ProcessStartInfo(settings.Value.Location.BattlenetPath, $"--exec=\"launch Hero\"")))
            {
                logger.LogInformation("Heroes of the Storm launched from Battlenet.");
            }

            var loggedIn = await Policy
                .Handle<Exception>()
                .OrResult<bool>(loaded => loaded == false)
                .WaitAndRetryAsync(retryCount: 60, retry => settings.Value.Ocr.CheckSleepDuration, OnKillRetry)
                .ExecuteAsync((token) => IsHomeScreen(), cts.Token).ConfigureAwait(false);

            if (!loggedIn)
            {
                logger.LogWarning("The game was launched, but we did not end up on the home screen. Killing game.");

                Kill();

                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await LaunchGameFromBattlenet().ConfigureAwait(false);
            }

            logger.LogInformation("Heroes of the Storm Home Screen detected");
        }

        private async Task LaunchAndWait()
        {
            // This will make the HeroSwitcher communicate with existing game to launch selected replay
            using (var defaultLaunch = Process.Start(ExplorerProcess, context.Current.LoadedReplay.FileInfo.FullName))
            {
                Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == false)
                    .WaitAndRetry(retryCount: 150, sleepDurationProvider: retry => settings.Value.Ocr.CheckSleepDuration)
                    .Execute(() => IsMatchingClientVersion());
            }

            var searchTerms = context.Current.LoadedReplay.Replay.Players
                .Select(x => x.Name)
                .Concat(context.Current.LoadedReplay.Replay.Players.Select(x => x.Character))
                .Concat(settings.Value.Ocr.LoadingScreenText)
                .Concat(new[] { context.Current.LoadedReplay.Replay.Map });

            var contains = await Policy
                    .Handle<Exception>()
                    .OrResult<bool>(result => result == false)
                    .WaitAndRetryAsync(retryCount: 60, sleepDurationProvider: retry => settings.Value.Ocr.CheckSleepDuration)
                    .ExecuteAsync((t) => ContainsAnyAsync(searchTerms), cts.Token).ConfigureAwait(false);
        }

        public async Task<TimeSpan?> TryGetTimerAsync()
        {
            try
            {
                using (Bitmap timerBitmap = GetNegativeOffsetTimer())
                {
                    if (timerBitmap == null) return null;

                    if (settings.Value.Capture.SaveTimerRegion)
                    {
                        timerBitmap.Save(Path.Combine(settings.Value.CapturesPath, "timer-" + Guid.NewGuid().ToString() + ".bmp"));
                    }

                    return await ConvertBitmapTimerToTimeSpan(timerBitmap).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get timer from bitmap");
            }

            return null;
        }

        private async Task<TimeSpan?> ConvertBitmapTimerToTimeSpan(Bitmap bitmap)
        {
            using (Bitmap resized = bitmap.GetResized(zoom: 4))
            {
                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(resized).ConfigureAwait(false))
                {
                    OcrResult ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
                    TimeSpan? timer = TryParseTimeSpan(ocrResult.Text);

                    if (timer.HasValue) return timer;
                    else if (settings.Value.Capture.SaveCaptureFailureCondition)
                    {
                        Directory.CreateDirectory(settings.Value.CapturesPath);
                        resized.Save(Path.Combine(settings.Value.CapturesPath, Guid.NewGuid().ToString() + ".bmp"));
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

        private bool IsMatchingClientVersion()
        {
            try
            {
                if (GameProcess != null)
                {
                    logger.LogInformation($"Current: {GameProcess.MainModule.FileVersionInfo.FileVersion}");
                    logger.LogInformation($"Required: {context.Current.LoadedReplay.Replay.ReplayVersion}");
                    return GameProcess.MainModule.FileVersionInfo.FileVersion == context.Current.LoadedReplay.Replay.ReplayVersion;
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
                string[] segments = time.Split(settings.Value.Ocr.TimerSeperator);

                if (segments.Length == settings.Value.Ocr.TimerHours)
                {
                    return time.ParseTimerHours(settings.Value.Ocr.TimeSpanFormatHours);
                }
                else if (segments.Length == settings.Value.Ocr.TimerMinutes && segments[0].StartsWith(settings.Value.Ocr.TimerNegativePrefix))
                {
                    return time.ParseNegativeTimerMinutes(settings.Value.Ocr.TimeSpanFormatMatchStart);
                }
                else if (segments.Length == settings.Value.Ocr.TimerMinutes)
                {
                    return time.ParsePositiveTimerMinutes(settings.Value.Ocr.TimerSeperator);
                }

                throw new Exception($"Unhandled segments: {segments.Length}");
            }
            catch (Exception)
            {
                logger.LogInformation($"Could not parse the timer: {text ?? string.Empty}");
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

        private async Task<bool> IsHomeScreen() => IsLaunched && await ContainsAnyAsync(settings.Value.Ocr.HomeScreenText).ConfigureAwait(false);

        private async Task<bool> IsReplay() => IsLaunched && (await TryGetTimerAsync().ConfigureAwait(false)) != null;

        private async Task<bool> ContainsAnyAsync(IEnumerable<string> words)
        {
            using (Bitmap capture = captureStrategy.Capture(Handle))
            {
                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(capture).ConfigureAwait(false))
                {
                    OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);

                    foreach (var word in words)
                    {
                        if (result.Text.Contains(word))
                        {
                            logger.LogDebug($"{word} has been found.");
                            return true;
                        }
                        else
                        {
                            logger.LogDebug($"{word} not found.");
                        }
                    }

                    if (settings.Value.Capture.SaveCaptureFailureCondition)
                    {
                        Directory.CreateDirectory(settings.Value.CapturesPath);
                        capture.Save(Path.Combine(settings.Value.CapturesPath, Guid.NewGuid().ToString() + ".bmp"));
                    }
                }
            }

            return false;
        }

        public void SendFocus(int index)
        {
            lock (controllerLock)
            {
                IntPtr key = (IntPtr)Keys[index];
                SendMessage(Handle, WindowMessage.WM_KEYDOWN, key, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_CHAR, key, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYUP, key, IntPtr.Zero);
            }
        }

        public void CameraFollow()
        {
            lock (controllerLock)
            {
                SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_L, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_L, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_L, IntPtr.Zero);
            }
        }

        public void SendToggleMaximumZoom()
        {
            lock (controllerLock)
            {
                SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_SHIFT, IntPtr.Zero);
            }
        }

        public void SendToggleMediumZoom()
        {
            lock (controllerLock)
            {
                SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_CHAR, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_Z, IntPtr.Zero);
            }
        }

        public void SendPanel(int panel)
        {
            lock (controllerLock)
            {
                IntPtr Key = (IntPtr)Keys[panel];
                SendMessage(Handle, WindowMessage.WM_KEYDOWN, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYDOWN, Key, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYUP, Key, IntPtr.Zero);
                SendMessage(Handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
            }
        }

        public void Kill()
        {
            try
            {
                bool killed = Policy
                    .Handle<Win32Exception>()
                    .Or<InvalidOperationException>()
                    .Or<NotSupportedException>()
                    .OrResult(false)
                    .WaitAndRetry(retryCount: 5, sleepDurationProvider: (retry) => TimeSpan.FromSeconds(Math.Pow(2, retry)), OnKillRetry)
                    .Execute(() =>
                    {
                        foreach (var process in Process.GetProcessesByName(settings.Value.Process.HeroesOfTheStorm))
                        {
                            using (process)
                            {

                                // TODO:
                                // 1. Try to close window + select 'Leave'
                                // 2. Check if process is exited
                                // 3. If not exited, force kill the process.
                                process.Kill();
                            }
                        }

                        return !Process.GetProcessesByName(settings.Value.Process.HeroesOfTheStorm).Any();
                    });

                logger.Log(killed ? LogLevel.Information : LogLevel.Error, $"Game process killed: {killed}");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not kill game process.");
            }
        }

        private void OnKillRetry(DelegateResult<bool> result, TimeSpan arg2)
        {
            if (result.Exception != null)
            {
                logger.LogError(result.Exception, "Could not kill game process.");
            }
        }
    }
}