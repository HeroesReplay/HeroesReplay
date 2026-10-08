using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Spectating.Capture;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace HeroesReplay.CLI.Commands.Check;

public static class BattleNetLauncherCheck
{
    public static async Task<CheckCommand.CheckResult> ReadAsync(OcrEngine engine)
    {
        Process[] processes = Process.GetProcessesByName("Battle.net");
        try
        {
            Process window = null;
            foreach (Process process in processes)
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    window = process;
                    break;
                }
            }

            if (window == null)
            {
                return new CheckCommand.CheckResult(
                    "battlenet",
                    false,
                    "Battle.net has no visible window.",
                    CheckCodes.BattleNetWindowMissing
                );
            }

            if (engine == null)
            {
                return new CheckCommand.CheckResult(
                    "battlenet",
                    false,
                    "OCR is not available, so the Play/Update button was not read.",
                    CheckCodes.BattleNetOcrUnavailable
                );
            }

            string text = null;
            bool captured = false;
            for (int attempt = 0; attempt < 3 && string.IsNullOrWhiteSpace(text); attempt++)
            {
                using Bitmap bitmap = new PrintWindowCapture(
                    NullLogger<PrintWindowCapture>.Instance
                ).Capture(window.MainWindowHandle);
                if (bitmap == null)
                {
                    continue;
                }

                captured = true;
                text = await RecognizeAsync(engine, bitmap).ConfigureAwait(false);
            }

            if (!captured)
            {
                return new CheckCommand.CheckResult(
                    "battlenet",
                    false,
                    "Could not capture the Battle.net window.",
                    CheckCodes.BattleNetCaptureFailed
                );
            }

            string button = LauncherButtonText.Classify(text);
            if (button is "Play" or "Playing" or "Update" or "Updating")
            {
                return new CheckCommand.CheckResult(
                    "battlenet",
                    true,
                    "Battle.net button is " + button + ".",
                    button is "Update" or "Updating"
                        ? CheckCodes.BattleNetUpdatePending
                        : CheckCodes.BattleNetOk
                );
            }

            if (button == "hidden")
            {
                return new CheckCommand.CheckResult(
                    "battlenet",
                    true,
                    "Battle.net is open. The Play or Update button is not on this page.",
                    CheckCodes.BattleNetButtonHidden
                );
            }

            return new CheckCommand.CheckResult(
                "battlenet",
                false,
                string.IsNullOrWhiteSpace(text)
                    ? "Battle.net window was captured but no text was read."
                    : "Battle.net window was captured but the Play/Update button was not read.",
                CheckCodes.BattleNetButtonUnread
            );
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static async Task<string> RecognizeAsync(OcrEngine engine, Bitmap bitmap)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (Stream netStream = stream.AsStream())
        {
            bitmap.Save(netStream, ImageFormat.Bmp);
            netStream.Flush();
            stream.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(
                BitmapDecoder.BmpDecoderId,
                stream
            );
            using SoftwareBitmap software = await decoder.GetSoftwareBitmapAsync();
            OcrResult result = await engine.RecognizeAsync(software);
            return result?.Text;
        }
    }
}
