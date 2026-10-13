using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace HeroesReplay.CLI.Commands.Check;

/// <summary>
/// The only OCR left in HeroesReplay (#292): <c>check battlenet</c> reads the Play/Update button
/// of the Battle.net launcher's window (a Chromium page, so the button has no window text). The game
/// client is never OCR'd: its screens and dialogs come from HeroesClientSDK memory reads and the
/// client's windows. Windows.Media.Ocr is created here, only when that check runs.
/// </summary>
public sealed class LauncherOcr
{
    private readonly OcrEngine engine;

    private LauncherOcr(OcrEngine engine) => this.engine = engine;

    /// <summary>OCR in the user profile's languages, or null when Windows has none.</summary>
    public static LauncherOcr TryCreate()
    {
        try
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            return engine == null ? null : new LauncherOcr(engine);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The text Windows OCR reads in <paramref name="bitmap"/>.</summary>
    public async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
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
