using System;
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

namespace HeroesReplay.Spectator
{
    public class ProcessWrapper : IDisposable
    {
        private IntPtr deviceContext = IntPtr.Zero;
        private IntPtr compatibleDeviceContext = IntPtr.Zero;
        private RECT? windowDimensions;
        private Process process;

        protected Rectangle? Dimensions
        {
            get
            {
                if (windowDimensions == null)
                {
                    if (NativeMethods.GetWindowRect(WindowHandle, out RECT value))
                    {
                        Logger.LogInformation("Getting Window Size");
                        windowDimensions = value;
                    }
                }

                if (windowDimensions == null) return null;

                return Rectangle.FromLTRB(windowDimensions.Value.Left, windowDimensions.Value.Top, windowDimensions.Value.Right, windowDimensions.Value.Bottom);
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

        public bool IsRunning => Process.GetProcessesByName(ProcessName).Any();

        protected string ProcessName { get; }

        protected ILogger<ProcessWrapper> Logger { get; }

        protected IConfiguration Configuration { get; }

        protected Process WrappedProcess => Process.GetProcessesByName(ProcessName)[0];

        protected IntPtr WindowHandle => WrappedProcess.MainWindowHandle;

        private readonly OcrEngine ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

        private readonly CancellationTokenProvider provider;

        protected CancellationToken Token => provider.Token;

        public ProcessWrapper(CancellationTokenProvider provider, ILogger<ProcessWrapper> logger, IConfiguration configuration, string processName)
        {
            this.Logger = logger;
            this.Configuration = configuration;
            this.provider = provider;
            this.ProcessName = processName;
        }

        // Windows desktop applications
        protected Bitmap? TryPrintWindow()
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

                    // To speed this up, don't copy the entire width/height of the application
                    if (NativeMethods.BitBlt(CompatibleDeviceContext, 0, 0, Dimensions.Value.Width, Dimensions.Value.Height, DeviceContext, Dimensions.Value.Left, Dimensions.Value.Top, Constants.SRCCOPY))
                    {
                        return Image.FromHbitmap(bitmap);
                    }
                    else
                    {
                        NativeMethods.DeleteDC(compatibleDeviceContext);
                        NativeMethods.ReleaseDC(WindowHandle, deviceContext);
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


        protected Bitmap? TryBitBlt(Rectangle region)
        {
            IntPtr bitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                if (Dimensions != null)
                {
                    bitmap = NativeMethods.CreateCompatibleBitmap(DeviceContext, region.Width, region.Height);
                    oldBitmap = NativeMethods.SelectObject(CompatibleDeviceContext, bitmap);

                    if (NativeMethods.BitBlt(CompatibleDeviceContext, 0, 0, region.Width, region.Height, DeviceContext, region.X, region.Y, Constants.SRCCOPY))
                    {
                        return Image.FromHbitmap(bitmap);
                    }
                    else
                    {
                        NativeMethods.DeleteDC(compatibleDeviceContext);
                        NativeMethods.ReleaseDC(WindowHandle, deviceContext);
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

        // https://docs.microsoft.com/en-us/dotnet/framework/winforms/advanced/how-to-set-jpeg-compression-level
        // The only way to convert from Bitmap to SoftwareBitmap is to save the Bitmap into a Stream
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

        protected async Task<bool> GetWindowContainsAsync(CaptureMethod captureMethod, params string[] lines)
        {
            Bitmap? bitmap = captureMethod switch
            {
                CaptureMethod.PrintScreen => TryPrintWindow(),
                CaptureMethod.BitBlt => TryBitBlt(),
                _ => throw new ArgumentOutOfRangeException(nameof(captureMethod), captureMethod, "Unhandled Capture method.")
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

                Logger.LogDebug("Could not extract Ocr result for: " + string.Join(", ", lines));

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