using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Microsoft.Extensions.Logging;

namespace HeroesReplay
{
    public class ProcessWrapper : IDisposable
    {
        private IntPtr deviceContext = IntPtr.Zero;
        private IntPtr compatibleDeviceContext = IntPtr.Zero;
        private RECT? rect;

        private Rectangle? Dimensions
        {
            get
            {
                if (rect == null)
                {
                    if (NativeMethods.GetWindowRect(WindowHandle, out RECT value))
                    {
                        Logger.LogInformation("Getting Window Size");
                        rect = value;
                    }
                }

                if (rect == null) return null;

                return Rectangle.FromLTRB(rect.Value.Left, rect.Value.Top, rect.Value.Right, rect.Value.Bottom);
            }
        }


        private IntPtr CompatibleDeviceContext
        {
            get
            {
                if (compatibleDeviceContext == IntPtr.Zero)
                {
                    Logger.LogInformation("Caching CompatibleDeviceContext");
                    compatibleDeviceContext = NativeMethods.CreateCompatibleDC(deviceContext);
                }

                return compatibleDeviceContext;
            }
        }

        private IntPtr DeviceContext
        {
            get
            {
                if (deviceContext == IntPtr.Zero)
                {
                    Logger.LogInformation("Caching DeviceContext");
                    deviceContext = NativeMethods.GetWindowDC(WindowHandle);
                }
                return deviceContext;
            }
        }

        protected enum WindowScreenCapture
        {
            CPU, // We need to use PrintScreen for the launcher using CPU rendering
            GPU // We need to use BitBlt for GPU based rendering
        }

        public bool IsRunning => Process.GetProcessesByName(ProcessName).Any();

        protected string ProcessName { get; }
        protected string ProcessPath { get; }
        protected ILogger<ProcessWrapper> Logger { get; }

        protected const int WM_KEYDOWN = 0x100;
        protected const int WM_KEYUP = 0x101;
        protected const int WM_CHAR = 0x102;
        protected const int SRCCOPY = 0x00CC0020;

        protected Process WrappedProcess => Process.GetProcessesByName(ProcessName)[0];
        protected IntPtr WindowHandle => WrappedProcess.MainWindowHandle;

        private readonly OcrEngine ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

        public ProcessWrapper(ILogger<ProcessWrapper> logger, string processName, string processPath)
        {
            this.Logger = logger;
            this.ProcessName = processName;
            this.ProcessPath = processPath;
        }

        // Windows desktop applications
        protected Bitmap? TryPrintScreen()
        {
            if (Dimensions == null) return null;

            var bmp = new Bitmap(Dimensions.Value.Width, Dimensions.Value.Height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bmp))
            {
                IntPtr hdlDeviceContext = graphics.GetHdc();

                try
                {
                    if (NativeMethods.PrintWindow(WindowHandle, hdlDeviceContext, 0))
                    {
                        return bmp;
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e, $"Failed to capture bitmap of {WrappedProcess.ProcessName}");
                }
                finally
                {
                    graphics.ReleaseHdc(hdlDeviceContext);
                }
            }

            return null;
        }

        // DirectX/OpenGL render frame (but its not the latest painted frame either)
        // https://docs.microsoft.com/en-us/windows/win32/gdi/capturing-an-image
        protected Bitmap? TryBitBlt()
        {
            IntPtr bitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                if (Dimensions != null)
                {
                    bitmap = NativeMethods.CreateCompatibleBitmap(DeviceContext, Dimensions.Value.Width, Dimensions.Value.Height);
                    oldBitmap = NativeMethods.SelectObject(CompatibleDeviceContext, bitmap);

                    if (NativeMethods.BitBlt(CompatibleDeviceContext, 0, 0, Dimensions.Value.Width, Dimensions.Value.Height, DeviceContext, Dimensions.Value.Left, Dimensions.Value.Top, SRCCOPY))
                    {
                        return Image.FromHbitmap(bitmap);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to capture bitmap of {WrappedProcess.ProcessName}");
                throw;
            }
            finally
            {
                NativeMethods.SelectObject(CompatibleDeviceContext, oldBitmap);
                NativeMethods.DeleteObject(bitmap);
            }

            return null;
        }

        protected async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                ImageCodecInfo? imageCodecInfo = GetEncoder(ImageFormat.Bmp);

                using (EncoderParameters encoderParameters = new EncoderParameters(1))
                {
                    EncoderParameter encoderParameter = new EncoderParameter(Encoder.Quality, 50L);
                    encoderParameters.Param[0] = encoderParameter;

                    bitmap.Save(stream.AsStream(), imageCodecInfo, encoderParameters);

                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, stream);
                    return await decoder.GetSoftwareBitmapAsync();
                }
            }
        }

        protected async Task<bool> GetWindowContainsAsync(WindowScreenCapture capture, params string[] lines)
        {
            Bitmap? bitmap = capture switch
            {
                WindowScreenCapture.CPU => TryPrintScreen(),
                WindowScreenCapture.GPU => TryBitBlt(),
                _ => throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unhandled Capture method.")
            };

            try
            {
                if (bitmap == null) return false;

                using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(bitmap))
                {
                    OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);

                    bool found = result != null && lines.All(line => result.Lines.Any(ocrLine => ocrLine.Text.Equals(line, StringComparison.OrdinalIgnoreCase)));

                    Logger.LogInformation("{0}: {1}", found ? "found" : "not found", string.Join(", ", lines));

                    return found;
                }
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        protected async Task<OcrResult?> TryGetOcrResult(Bitmap bitmap, params string[] lines)
        {
            if (bitmap == null) return null;

            using (SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(bitmap))
            {
                OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);

                if (result != null && lines.All(line => result.Lines.Any(ocrLine => ocrLine.Text.Contains(line, StringComparison.OrdinalIgnoreCase))))
                {
                    return result;
                }

                return null;
            }
        }

        private ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            return codecs.FirstOrDefault(codec => codec.FormatID == format.Guid);
        }

        public void Dispose()
        {
            NativeMethods.DeleteDC(compatibleDeviceContext);
            NativeMethods.ReleaseDC(WindowHandle, deviceContext);
        }
    }
}