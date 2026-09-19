using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.Logging;
using Polly;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using static PInvoke.User32;

namespace HeroesReplay.Core.Services.Observer;

public class GameController : IGameController
{
    private const string VersionsFolder = "Versions";
    private const int MaxBattlenetLaunchAttempts = 3;

    private readonly OcrEngine ocrEngine;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly ILogger<GameController> logger;
    private readonly IReplayContext context;
    private readonly AppSettings settings;
    private readonly CaptureStrategy captureStrategy;

    private readonly object controllerLock = new object();
    private Process cachedProcess;
    private IntPtr cachedHandle;

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
        VirtualKey.VK_KEY_0,
    };

    public GameController(
        ILogger<GameController> logger,
        IReplayContext context,
        AppSettings settings,
        CaptureStrategy captureStrategy,
        OcrEngine engine,
        CancellationTokenProvider tokenProvider
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.captureStrategy =
            captureStrategy ?? throw new ArgumentNullException(nameof(captureStrategy));
        this.ocrEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public async Task LaunchAsync()
    {
        var replay = context.Current.LoadedReplay.Replay;

        string versionFolder = Path.Combine(settings.Location.GameInstallDirectory, VersionsFolder);
        int latestBuild = Directory
            .EnumerateDirectories(versionFolder)
            .Select(x => x)
            .Select(x => int.Parse(Path.GetFileName(x).Replace("Base", string.Empty)))
            .Max();
        var requiresAuth = replay.ReplayBuild == latestBuild;

        if (IsLaunched() && await IsReplay().ConfigureAwait(false))
        {
            return;
        }
        else if (IsLaunched() && await IsHomeScreen().ConfigureAwait(false))
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
        for (int attempt = 1; attempt <= MaxBattlenetLaunchAttempts; attempt++)
        {
            logger.LogInformation(
                "Launching battlenet because this replay is the latest build and requires auth. Attempt {Attempt}/{Max}.",
                attempt,
                MaxBattlenetLaunchAttempts
            );

            using (
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = settings.Location.BattlenetPath,
                        Arguments = "--exec=\"launch Hero\"",
                        UseShellExecute = true,
                    }
                )
            ) { }

            var loggedIn = await Policy
                .Handle<Exception>()
                .OrResult<bool>(loaded => loaded == false)
                .WaitAndRetryAsync(retryCount: 60, retry => settings.OCR.CheckSleepDuration)
                .ExecuteAsync((token) => IsHomeScreen(), tokenProvider.Token)
                .ConfigureAwait(false);

            if (loggedIn)
            {
                logger.LogInformation("Heroes of the Storm Home Screen detected");
                return;
            }

            if (IsGameProcessRunning())
            {
                logger.LogWarning(
                    "Home-screen OCR did not find PLAY/COLLECTION/LOOT/WATCH, but the game window is running. Continuing."
                );
                return;
            }

            logger.LogInformation(
                "The game was launched, but we did not end up on the home screen. Killing game."
            );
            Kill();
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Failed to reach the Heroes of the Storm home screen after {MaxBattlenetLaunchAttempts} Battle.net launches."
        );
    }

    private async Task LaunchAndWait()
    {
        using (
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = context.Current.LoadedReplay.FileInfo.FullName,
                    UseShellExecute = true,
                }
            )
        ) { }

        bool versionMatched = Policy
            .Handle<Exception>()
            .OrResult<bool>(result => result == false)
            .WaitAndRetry(
                retryCount: 150,
                sleepDurationProvider: retry => settings.OCR.CheckSleepDuration
            )
            .Execute(() => IsMatchingClientVersion());

        if (!versionMatched)
        {
            logger.LogWarning("Launched client version did not match the replay version.");
        }

        var searchTerms = context
            .Current.LoadedReplay.Replay.Players.Select(x => x.Name)
            .Concat(context.Current.LoadedReplay.Replay.Players.Select(x => x.Character))
            .Concat(settings.OCR.LoadingScreenText)
            .Concat(new[] { context.Current.LoadedReplay.Replay.Map });

        await Policy
            .Handle<Exception>()
            .OrResult<bool>(result => result == false)
            .WaitAndRetryAsync(
                retryCount: 60,
                sleepDurationProvider: retry => settings.OCR.CheckSleepDuration
            )
            .ExecuteAsync((t) => ContainsAnyAsync(searchTerms), tokenProvider.Token)
            .ConfigureAwait(false);
    }

    public async Task<TimeSpan?> TryGetTimerAsync()
    {
        try
        {
            using (Bitmap timerBitmap = GetNegativeOffsetTimer())
            {
                if (timerBitmap == null)
                    return null;

                if (settings.Capture.SaveTimerRegion)
                {
                    timerBitmap.Save(
                        Path.Combine(
                            settings.CapturesPath,
                            "timer-" + Guid.NewGuid().ToString() + ".bmp"
                        )
                    );
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
            using (
                SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(resized)
                    .ConfigureAwait(false)
            )
            {
                OcrResult ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
                TimeSpan? timer = TryParseTimeSpan(ocrResult.Text);

                if (timer.HasValue)
                    return timer;
                else if (settings.Capture.SaveCaptureFailureCondition)
                {
                    Directory.CreateDirectory(settings.CapturesPath);
                    resized.Save(
                        Path.Combine(settings.CapturesPath, Guid.NewGuid().ToString() + ".bmp")
                    );
                }

                return null;
            }
        }
    }

    private Bitmap GetNegativeOffsetTimer()
    {
        if (!TryGetGameHandle(out IntPtr handle))
        {
            return null;
        }

        const int MIN_HEIGHT_FOR_OCR_TO_WORK = 50;
        Rectangle dimensions = captureStrategy.GetDimensions(handle);
        var width = dimensions.Width;
        var column = dimensions.Width / 50;
        var start = width / 2 - column;
        var end = column * 2;

        return captureStrategy.Capture(
            handle,
            new Rectangle(start, 0, end, MIN_HEIGHT_FOR_OCR_TO_WORK)
        );
    }

    private bool IsMatchingClientVersion()
    {
        try
        {
            if (TryGetGameHandle(out _) && cachedProcess != null)
            {
                logger.LogInformation(
                    $"Current: {cachedProcess.MainModule.FileVersionInfo.FileVersion}"
                );
                logger.LogInformation(
                    $"Required: {context.Current.LoadedReplay.Replay.ReplayVersion}"
                );
                return cachedProcess.MainModule.FileVersionInfo.FileVersion
                    == context.Current.LoadedReplay.Replay.ReplayVersion;
            }

            logger.LogInformation("Game not launched.");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not retrieve process version information.");
        }

        return false;
    }

    private static async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        using (var stream = new InMemoryRandomAccessStream())
        using (Stream netStream = stream.AsStream())
        {
            bitmap.Save(netStream, ImageFormat.Bmp);
            netStream.Flush();
            stream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(
                BitmapDecoder.BmpDecoderId,
                stream
            );
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
            else if (
                segments.Length == settings.OCR.TimerMinutes
                && segments[0].StartsWith(settings.OCR.TimerNegativePrefix)
            )
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
            logger.LogInformation($"Could not parse the timer: {text ?? string.Empty}");
        }

        return null;
    }

    private static char[] SanitizeOcrTimer(string text)
    {
        return text.Replace("O", "0", StringComparison.OrdinalIgnoreCase)
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

    private async Task<bool> IsHomeScreen() =>
        IsGameProcessRunning()
        && await ContainsAnyAsync(settings.OCR.HomeScreenText).ConfigureAwait(false);

    private bool IsGameProcessRunning()
    {
        Process[] processes = Process.GetProcessesByName(settings.Process.HeroesOfTheStorm);
        try
        {
            return processes.Any(process =>
            {
                try
                {
                    return !process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            });
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private async Task<bool> IsReplay() =>
        IsLaunched() && (await TryGetTimerAsync().ConfigureAwait(false)) != null;

    private async Task<bool> ContainsAnyAsync(IEnumerable<string> words)
    {
        if (!TryGetGameHandle(out IntPtr handle))
        {
            return false;
        }

        using (Bitmap capture = captureStrategy.Capture(handle))
        {
            if (capture == null)
            {
                return false;
            }

            using (
                SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(capture)
                    .ConfigureAwait(false)
            )
            {
                OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);
                logger.LogInformation(
                    "Window OCR ({Width}x{Height}): {Text}",
                    capture.Width,
                    capture.Height,
                    string.IsNullOrWhiteSpace(result.Text) ? "(empty)" : result.Text
                );

                foreach (var word in words)
                {
                    if (result.Text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("{Word} has been found.", word);
                        return true;
                    }
                }

                if (settings.Capture.SaveCaptureFailureCondition)
                {
                    Directory.CreateDirectory(settings.CapturesPath);
                    capture.Save(
                        Path.Combine(settings.CapturesPath, Guid.NewGuid().ToString() + ".bmp")
                    );
                }
            }
        }

        return false;
    }

    public void SendFocus(int index)
    {
        lock (controllerLock)
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                return;
            }

            IntPtr key = (IntPtr)Keys[index];
            SendMessage(handle, WindowMessage.WM_KEYDOWN, key, IntPtr.Zero);
            SendMessage(handle, WindowMessage.WM_CHAR, key, IntPtr.Zero);
            SendMessage(handle, WindowMessage.WM_KEYUP, key, IntPtr.Zero);
        }
    }

    public void SendPanel(Panel panel)
    {
        lock (controllerLock)
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                return;
            }

            IntPtr Key = (IntPtr)Keys[(int)panel];
            SendMessage(
                handle,
                WindowMessage.WM_KEYDOWN,
                (IntPtr)VirtualKey.VK_CONTROL,
                IntPtr.Zero
            );
            SendMessage(handle, WindowMessage.WM_KEYDOWN, Key, IntPtr.Zero);
            SendMessage(handle, WindowMessage.WM_KEYUP, Key, IntPtr.Zero);
            SendMessage(handle, WindowMessage.WM_KEYUP, (IntPtr)VirtualKey.VK_CONTROL, IntPtr.Zero);
        }
    }

    public void HideReplayTimeline()
    {
        SendChord(
            "hide replay control panel (Ctrl+Shift+O)",
            VirtualKey.VK_CONTROL,
            VirtualKey.VK_SHIFT,
            VirtualKey.VK_O
        );
    }

    public void ZoomOut()
    {
        SendChord("zoom out (Ctrl+Z)", VirtualKey.VK_CONTROL, VirtualKey.VK_Z);
    }

    private void SendChord(string description, params VirtualKey[] keys)
    {
        lock (controllerLock)
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                logger.LogWarning("Could not {Description}; no game window.", description);
                return;
            }

            SetForegroundWindow(handle);
            const uint keyUp = 0x0002;
            foreach (VirtualKey key in keys)
            {
                NativeMethods.keybd_event((byte)key, 0, 0, UIntPtr.Zero);
            }

            for (int i = keys.Length - 1; i >= 0; i--)
            {
                NativeMethods.keybd_event((byte)keys[i], 0, keyUp, UIntPtr.Zero);
            }

            logger.LogInformation("Sent {Description}.", description);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(
            byte bVk,
            byte bScan,
            uint dwFlags,
            UIntPtr dwExtraInfo
        );
    }

    public void Kill()
    {
        ClearProcessCache();
        try
        {
            bool killed = Policy
                .Handle<Win32Exception>()
                .Or<InvalidOperationException>()
                .Or<NotSupportedException>()
                .OrResult(false)
                .WaitAndRetry(
                    retryCount: 5,
                    sleepDurationProvider: retry => TimeSpan.FromSeconds(Math.Pow(2, retry)),
                    OnRetry
                )
                .Execute(() =>
                {
                    Process[] processes = Process.GetProcessesByName(
                        settings.Process.HeroesOfTheStorm
                    );
                    try
                    {
                        foreach (var process in processes)
                        {
                            using (process)
                            {
                                if (!process.HasExited)
                                {
                                    process.Kill(entireProcessTree: true);
                                    process.WaitForExit(5000);
                                }
                            }
                        }
                    }
                    catch
                    {
                        foreach (var process in processes)
                        {
                            try
                            {
                                process.Dispose();
                            }
                            catch { }
                        }

                        throw;
                    }

                    Process[] remaining = Process.GetProcessesByName(
                        settings.Process.HeroesOfTheStorm
                    );
                    try
                    {
                        return remaining.Length == 0;
                    }
                    finally
                    {
                        foreach (var process in remaining)
                        {
                            process.Dispose();
                        }
                    }
                });

            logger.Log(
                killed ? LogLevel.Information : LogLevel.Error,
                $"Game process killed: {killed}"
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not kill game process.");
        }
    }

    private bool IsLaunched() => TryGetGameHandle(out _);

    private void ClearProcessCache()
    {
        cachedHandle = IntPtr.Zero;
        cachedProcess?.Dispose();
        cachedProcess = null;
    }

    private bool TryGetGameHandle(out IntPtr handle)
    {
        handle = IntPtr.Zero;

        if (cachedProcess != null)
        {
            try
            {
                if (!cachedProcess.HasExited && cachedProcess.MainWindowHandle != IntPtr.Zero)
                {
                    handle = cachedProcess.MainWindowHandle;
                    cachedHandle = handle;
                    return true;
                }
            }
            catch (InvalidOperationException) { }

            ClearProcessCache();
        }

        Process[] all = Process.GetProcessesByName(settings.Process.HeroesOfTheStorm);
        try
        {
            foreach (var candidate in all)
            {
                if (cachedProcess == null && !candidate.HasExited)
                {
                    cachedProcess = candidate;
                    cachedHandle = candidate.MainWindowHandle;
                    handle = cachedHandle;
                }
                else
                {
                    candidate.Dispose();
                }
            }

            return cachedProcess != null;
        }
        catch
        {
            foreach (var candidate in all)
            {
                if (!ReferenceEquals(candidate, cachedProcess))
                {
                    try
                    {
                        candidate.Dispose();
                    }
                    catch { }
                }
            }

            ClearProcessCache();
            throw;
        }
    }

    private void OnRetry(DelegateResult<bool> result, TimeSpan arg2)
    {
        if (result.Exception != null)
        {
            logger.LogError(result.Exception, "Could not kill game process.");
        }
    }
}
