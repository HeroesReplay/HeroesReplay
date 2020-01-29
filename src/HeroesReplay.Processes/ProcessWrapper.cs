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
using HeroesReplay.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Processes
{
    public class ProcessWrapper : IDisposable
    {
        public bool IsRunning => Process.GetProcessesByName(ProcessName).Any();

        protected string ProcessName { get; }

        protected ILogger<ProcessWrapper> Logger { get; }

        protected IConfiguration Configuration { get; }

        protected Process ActualProcess => Process.GetProcessesByName(ProcessName)[0];

        protected IntPtr WindowHandle => ActualProcess.MainWindowHandle;

        private readonly OcrEngine ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

        private readonly CancellationTokenProvider tokenProvider;

        protected DeviceContextHolder DeviceContextHolder { get; }

        protected CancellationToken Token => tokenProvider.Token;

        public ProcessWrapper(CancellationTokenProvider tokenProvider, DeviceContextHolder contextHolder, ILogger<ProcessWrapper> logger, IConfiguration configuration, string processName)
        {
            this.Logger = logger;
            this.Configuration = configuration;
            this.tokenProvider = tokenProvider;
            this.DeviceContextHolder = contextHolder;
            this.ProcessName = processName;
        }

        public DeviceContextHolder AquireDeviceContext()
        {
            return this.DeviceContextHolder.AquireDeviceContext(this.ActualProcess);
        }

        // https://docs.microsoft.com/en-us/dotnet/framework/winforms/advanced/how-to-set-jpeg-compression-level
        // The only way to convert from Bitmap to SoftwareBitmap is to save the Bitmap into a Stream
        protected async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                ImageCodecInfo? imageCodecInfo = GetEncoder(ImageFormat.Bmp);

                using (EncoderParameters encoderParameters = new EncoderParameters(1))
                {
                    EncoderParameter encoderParameter = new EncoderParameter(Encoder.Quality, 0L);
                    encoderParameters.Param[0] = encoderParameter;
                    bitmap.Save(stream.AsStream(), imageCodecInfo, encoderParameters);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, stream);
                    return await decoder.GetSoftwareBitmapAsync();
                }
            }
        }

        protected async Task<bool> GetWindowContainsAnyAsync(CaptureMethod captureMethod, params string[] lines)
        {
            using (DeviceContextHolder deviceContext = DeviceContextHolder.AquireDeviceContext(this.ActualProcess))
            {
                Bitmap? bitmap = captureMethod switch
                {
                    CaptureMethod.PrintScreen => deviceContext.TryPrintWindow(),
                    CaptureMethod.BitBlt => deviceContext.TryBitBlt()
                };

                try
                {
                    OcrResult? result = await TryGetOcrResult(bitmap, lines);

                    return result != null;
                }
                finally
                {
                    bitmap?.Dispose();
                }
            }
        }

        protected async Task<OcrResult?> TryGetOcrResult(Bitmap? bitmap, IEnumerable<string> lines)
        {
            if (bitmap == null) return null;

            using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(bitmap))
            {
                OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);

                if (result != null && lines.Any(line => result.Lines.Any(ocrLine => Contains(ocrLine, line))))
                {
                    return result;
                }

                return null;
            }
        }

        protected async Task<OcrResult?> TryGetOcrResult(Bitmap? bitmap, params string[] lines)
        {
            return await TryGetOcrResult(bitmap, (IEnumerable<string>)lines);
        }

        private ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
        }

        protected bool Contains(OcrLine ocrLine, string text)
        {
            if (ocrLine.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInformation($"[FOUND][{text}]");
                return true;
            }

            return false;
        }

        public void Dispose()
        {

        }
    }
}