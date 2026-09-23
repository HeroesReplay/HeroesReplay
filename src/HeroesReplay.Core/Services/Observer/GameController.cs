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
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
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
    private readonly IObsController obsController;
    private readonly IGameCapture capture;

    private readonly object controllerLock = new object();
    private Process cachedProcess;
    private IntPtr cachedHandle;
    private string lastRejectedTimer;

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
        IObsController obsController,
        IGameCapture capture,
        OcrEngine engine,
        CancellationTokenProvider tokenProvider
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.obsController =
            obsController ?? throw new ArgumentNullException(nameof(obsController));
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        this.ocrEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public async Task LaunchAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.launch");
        var replay = context.Current.LoadedReplay.Replay;
        HeroesReplayTelemetry.TagReplay(
            activity,
            context.Current.LoadedReplay.FileInfo?.FullName,
            replay?.Map,
            context.Current.LoadedReplay.ReplayId,
            replay?.ReplayVersion
        );

        string versionFolder = Path.Combine(settings.Location.GameInstallDirectory, VersionsFolder);
        int latestBuild = Directory
            .EnumerateDirectories(versionFolder)
            .Select(x => x)
            .Select(x => int.Parse(Path.GetFileName(x).Replace("Base", string.Empty)))
            .Max();
        var requiresAuth = replay.ReplayBuild == latestBuild;

        if (IsLaunched() && await IsReplay().ConfigureAwait(false))
        {
            logger.LogInformation("Client already in a replay (timer visible). Skipping launch.");
            ShowGameScene("timer already visible");
            return;
        }

        if (IsLaunched() && !await IsHomeScreen().ConfigureAwait(false))
        {
            logger.LogInformation(
                "Client is running and not on the home screen; attaching without waiting for loading OCR."
            );
            return;
        }

        if (IsLaunched() && await IsHomeScreen().ConfigureAwait(false))
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
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.launch.battlenet");
        for (int attempt = 1; attempt <= MaxBattlenetLaunchAttempts; attempt++)
        {
            activity?.SetTag("launch.attempt", attempt);
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
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.launch.replay");
        activity?.SetTag("replay.path", context.Current.LoadedReplay.FileInfo?.FullName);
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            activity?.SetTag("launch.replay_attempt", attempt);
            bool disconnected = await StartReplayAndWaitAsync().ConfigureAwait(false);
            if (!disconnected || attempt == 2)
            {
                return;
            }

            logger.LogWarning(
                "Battle.net disconnected while loading the replay. Closing the client and trying once more."
            );
            Kill();
            await Task.Delay(TimeSpan.FromSeconds(8), tokenProvider.Token).ConfigureAwait(false);
        }
    }

    private async Task<bool> StartReplayAndWaitAsync()
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
            .Concat(new[] { context.Current.LoadedReplay.Replay.Map })
            .ToArray();
        bool disconnected = false;
        await Policy
            .Handle<Exception>()
            .OrResult<bool>(result => result == false)
            .WaitAndRetryAsync(
                retryCount: 60,
                sleepDurationProvider: retry => settings.OCR.CheckSleepDuration
            )
            .ExecuteAsync(
                async (t) =>
                {
                    string text = await ReadWindowTextAsync().ConfigureAwait(false);
                    if (BattleNetDisconnect.IsShown(text))
                    {
                        disconnected = true;
                        logger.LogWarning("Battle.net disconnect dialog: {Text}", text);
                        return true;
                    }

                    bool loading = searchTerms.Any(word =>
                        !string.IsNullOrWhiteSpace(word)
                        && text.Contains(word, StringComparison.OrdinalIgnoreCase)
                    );
                    bool timer = await IsReplay().ConfigureAwait(false);
                    if (loading)
                    {
                        ShowGameScene("loading screen");
                    }
                    else if (timer)
                    {
                        ShowGameScene("timer visible");
                    }

                    return loading || timer;
                },
                tokenProvider.Token
            )
            .ConfigureAwait(false);
        return disconnected;
    }

    private async Task<string> ReadWindowTextAsync()
    {
        if (!TryGetGameHandle(out IntPtr handle))
        {
            return string.Empty;
        }

        using Bitmap frame = capture.Capture(handle);
        if (frame == null)
        {
            return string.Empty;
        }

        using SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(frame)
            .ConfigureAwait(false);
        OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);
        string text = result?.Text ?? string.Empty;
        logger.LogInformation(
            "Window OCR ({Width}x{Height}): {Text}",
            frame.Width,
            frame.Height,
            string.IsNullOrWhiteSpace(text) ? "(empty)" : text
        );
        return text;
    }

    private void ShowGameScene(string reason)
    {
        if (!settings.OBS.Enabled)
        {
            return;
        }

        logger.LogInformation("OBS game-scene ({Reason}).", reason);
        obsController.SwapToGameScene();
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

    public async Task<bool> TrySeeEndScreenAsync(bool nearCore)
    {
        try
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                return false;
            }

            using Bitmap frame = capture.Capture(handle);
            if (frame == null || frame.Width < 200 || frame.Height < 200)
            {
                return false;
            }

            var crop = new Rectangle(
                frame.Width / 5,
                frame.Height / 8,
                frame.Width * 3 / 5,
                frame.Height / 3
            );
            using Bitmap region = frame.Clone(crop, frame.PixelFormat);
            using Bitmap resized = region.GetResized(zoom: 2);
            using SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(resized)
                .ConfigureAwait(false);
            OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);
            string text = result?.Text ?? string.Empty;
            bool endScreen = MatchEndBanner.IsEnd(text, nearCore);
            if (endScreen)
            {
                logger.LogInformation("End-screen OCR saw: {Text}", text.Replace('\n', ' '));
            }

            return endScreen;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "End-screen OCR failed.");
            return false;
        }
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

                try
                {
                    Directory.CreateDirectory(settings.CapturesPath);
                    resized.Save(
                        Path.Combine(settings.CapturesPath, "timer-rejected.png"),
                        ImageFormat.Png
                    );
                }
                catch (Exception saveError)
                {
                    logger.LogDebug(saveError, "Could not save the rejected timer crop.");
                }

                if (settings.Capture.SaveCaptureFailureCondition)
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

        Rectangle dimensions = capture.GetClientSize(handle);
        int width = dimensions.Width;
        int height = dimensions.Height;
        // 1920x1080: the MM:SS digits sit at about x=910, y=14, 100x48.
        // y=22 and height 32 clipped the bottom of the digits, so OCR never returned a time.
        int cropWidth = Math.Clamp(width * 100 / 1920, 80, 110);
        int cropHeight = Math.Clamp(height * 48 / 1080, 36, 56);
        int top = Math.Clamp(height * 14 / 1080, 8, 24);
        int start = Math.Max(0, (width - cropWidth) / 2);
        if (start + cropWidth > width)
        {
            cropWidth = width - start;
        }

        return capture.Capture(handle, new Rectangle(start, top, cropWidth, cropHeight));
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
            if (!HudClock.TryParse(text, out TimeSpan clock))
            {
                string sanitized = HudClock.Sanitize(text);
                if (
                    !string.IsNullOrEmpty(sanitized)
                    && !string.Equals(sanitized, lastRejectedTimer, StringComparison.Ordinal)
                )
                {
                    lastRejectedTimer = sanitized;
                    logger.LogWarning(
                        "Timer OCR is not -MM:SS or MM:SS. Saw \"{Text}\", sanitized to \"{Sanitized}\".",
                        text,
                        sanitized
                    );
                }

                return null;
            }

            return clock;
        }
        catch (Exception)
        {
            logger.LogDebug("Could not parse the timer: {Text}", text ?? string.Empty);
        }

        return null;
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

        using (Bitmap frame = capture.Capture(handle))
        {
            if (frame == null)
            {
                return false;
            }

            using (
                SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(frame)
                    .ConfigureAwait(false)
            )
            {
                OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);
                logger.LogInformation(
                    "Window OCR ({Width}x{Height}): {Text}",
                    frame.Width,
                    frame.Height,
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
                    frame.Save(
                        Path.Combine(settings.CapturesPath, Guid.NewGuid().ToString() + ".bmp")
                    );
                }
            }
        }

        return false;
    }

    public void SendFocus(int index)
    {
        if (index < 0 || index >= Keys.Length)
        {
            logger.LogWarning("Focus index {Index} is out of range.", index);
            return;
        }

        SendChord($"focus slot {index} ({Keys[index]})", Keys[index]);
    }

    public void SendPanel(Panel panel)
    {
        int panelIndex = (int)panel;
        if (panelIndex < 0 || panelIndex >= Keys.Length)
        {
            logger.LogWarning("Panel {Panel} is out of range.", panel);
            return;
        }

        SendChord(
            $"panel {panel} (Ctrl+{panelIndex + 1})",
            VirtualKey.VK_CONTROL,
            Keys[panelIndex]
        );
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

            GameWindowInput.SendKeys(handle, keys, logger);
            logger.LogInformation("Sent {Description} to hwnd {Handle}.", description, handle);
        }
    }

    public void SaveEndScreenshot()
    {
        try
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                logger.LogWarning("Could not capture end screenshot; no game window.");
                return;
            }

            string directory = context.Current?.Directory?.FullName;
            if (string.IsNullOrWhiteSpace(directory))
            {
                logger.LogWarning("Could not capture end screenshot; no context directory.");
                return;
            }

            Directory.CreateDirectory(directory);
            using Bitmap bitmap = capture.Capture(handle);
            if (bitmap == null)
            {
                logger.LogWarning("End screenshot capture returned no bitmap.");
                return;
            }

            TimeSpan timer = context.Current.Timer ?? TimeSpan.Zero;
            string name =
                $"end-{((int)timer.TotalHours):D2}-{timer.Minutes:D2}-{timer.Seconds:D2}.png";
            string path = Path.Combine(directory, name);
            bitmap.Save(path, ImageFormat.Png);
            File.Copy(path, Path.Combine(directory, "end.png"), overwrite: true);
            logger.LogInformation(
                "Wrote end screenshot {Path} ({Width}x{Height}) timer={Timer}.",
                path,
                bitmap.Width,
                bitmap.Height,
                timer
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not write end screenshot.");
        }
    }

    public bool IsGameHung()
    {
        if (!TryGetGameHandle(out IntPtr handle) || handle == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.IsHungAppWindow(handle);
    }

    public bool IsGameRunning() => IsGameProcessRunning();

    public Process GetGameProcess()
    {
        if (cachedProcess != null)
        {
            try
            {
                if (!cachedProcess.HasExited)
                {
                    return cachedProcess;
                }
            }
            catch (InvalidOperationException) { }
        }

        foreach (Process process in Process.GetProcessesByName(settings.Process.HeroesOfTheStorm))
        {
            try
            {
                if (!process.HasExited)
                {
                    return process;
                }
            }
            catch (InvalidOperationException) { }

            process.Dispose();
        }

        return null;
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
        int minWidth = 1280;
        int minHeight = 720;
        if (int.TryParse(settings.Client?.Width, out int configuredWidth) && configuredWidth > 0)
        {
            minWidth = Math.Min(minWidth, configuredWidth);
        }

        if (int.TryParse(settings.Client?.Height, out int configuredHeight) && configuredHeight > 0)
        {
            minHeight = Math.Min(minHeight, configuredHeight);
        }

        if (cachedProcess != null)
        {
            try
            {
                if (!cachedProcess.HasExited)
                {
                    handle = GameWindowInput.FindLargestVisibleWindow(
                        cachedProcess.Id,
                        minWidth,
                        minHeight
                    );
                    if (handle != IntPtr.Zero)
                    {
                        cachedHandle = handle;
                        return true;
                    }
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
                    handle = GameWindowInput.FindLargestVisibleWindow(
                        candidate.Id,
                        minWidth,
                        minHeight
                    );
                    cachedHandle = handle;
                }
                else
                {
                    candidate.Dispose();
                }
            }

            return handle != IntPtr.Zero;
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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool IsHungAppWindow(IntPtr hwnd);
    }
}
