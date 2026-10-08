using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Spectating.Capture;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Check;

public static class BattleNetLauncherCheck
{
    public static async Task<CheckCommand.CheckResult> ReadAsync(LauncherOcr ocr)
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

            if (ocr == null)
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
                text = await ocr.RecognizeAsync(bitmap).ConfigureAwait(false);
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
}
