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
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Processes
{
    public class ProcessWrapper : IDisposable
    {
        public virtual bool IsRunning => Process.GetProcessesByName(ProcessName).Any();

        protected string ProcessName { get; }

        protected ILogger<ProcessWrapper> Logger { get; }

        protected IConfiguration Configuration { get; }

        protected Process ActualProcess => Process.GetProcessesByName(ProcessName)[0];

        protected IntPtr WindowHandle
        {
            get
            {
                var handle = ActualProcess.MainWindowHandle;

                if(handle == IntPtr.Zero)
                    throw new InvalidOperationException("Handle not set");

                return handle;
            }
        }

        protected CaptureStrategy CaptureStrategy { get; }

        protected CancellationToken Token => tokenProvider.Token;

        private readonly OcrEngine ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

        private readonly CancellationTokenProvider tokenProvider;

        protected static ImageCodecInfo BmpCodec = GetEncoder(ImageFormat.Bmp);

        public ProcessWrapper(CancellationTokenProvider tokenProvider, CaptureStrategy captureStrategy, ILogger<ProcessWrapper> logger, IConfiguration configuration, string processName)
        {
            Logger = logger;
            Configuration = configuration;
            this.tokenProvider = tokenProvider;
            CaptureStrategy = captureStrategy;
            ProcessName = processName;
        }

        protected async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (EncoderParameters encoderParameters = new EncoderParameters(1))
                {
                    EncoderParameter encoderParameter = new EncoderParameter(Encoder.Quality, 0L);
                    encoderParameters.Param[0] = encoderParameter;
                    bitmap.Save(stream.AsStream(), BmpCodec, encoderParameters);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, stream);
                    return await decoder.GetSoftwareBitmapAsync();
                }
            }
        }

        protected virtual async Task<bool> GetWindowContainsAnyAsync(params string[] lines)
        {
            using (Bitmap bitmap = CaptureStrategy.Capture(WindowHandle))
            {                
                return await ContainsAnyAsync(bitmap, lines) != null;
            }
        }

        protected virtual async Task<OcrResult?> ContainsAnyAsync(Bitmap bitmap, IEnumerable<string> lines)
        {
            this.Logger.LogDebug("checking bitmap for any of the following lines.");
            this.Logger.LogDebug(string.Join(Environment.NewLine, lines));

            using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(bitmap))
            {
                OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);

                var found = (result?.Lines ?? Enumerable.Empty<OcrLine>()).Select(x => x.Text).ToList();

                if (found.Any())
                {
                    Logger.LogDebug("OCR found");
                    Logger.LogDebug(string.Join(Environment.NewLine, found));
                }

                foreach (var line in lines)
                {
                    foreach (var ocrLine in result?.Lines ?? Enumerable.Empty<OcrLine>())
                    {
                        if (ocrLine.Text.Contains(line, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.LogDebug($"found: {line}");
                            return result;
                        }
                    }

                    Logger.LogDebug($"not found: {line}");
                }
            }

            if (Configuration.GetValue("Capture:SaveFailure", false))
            {
                string path = Path.Combine(Configuration.GetValue("Capture:SavePath", Guid.NewGuid().ToString() + ".bmp"));
                Logger.LogDebug("saving failed ocr result to: " + path);
                bitmap.Save(path);
            }

            return null;
        }

        protected virtual async Task<bool> ContainsAny(Bitmap bitmap, params string[] lines) => await ContainsAnyAsync(bitmap, lines.AsEnumerable()) != null;

        protected virtual async Task<OcrResult?> TryGetOcrResult(Bitmap bitmap, params string[] lines) => await ContainsAnyAsync(bitmap, lines.AsEnumerable());

        private static ImageCodecInfo GetEncoder(ImageFormat format) => ImageCodecInfo.GetImageDecoders().First(codec => codec.FormatID == format.Guid);

        public void Dispose() => ActualProcess?.Dispose();
    }
}