using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Media.Ocr;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Processes
{
    public class DeviceContextHolder : IDisposable
    {
        private readonly ILogger<DeviceContextHolder> logger;

        private readonly OcrEngine ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

        private IntPtr WindowHandle { get; set; }
        private IntPtr CompatibleDeviceContext { get; set; }
        private IntPtr DeviceContext { get; set; }
        private Process Process { get; set; }

        public Rectangle Dimensions { get; private set; }

        public DeviceContextHolder(ILogger<DeviceContextHolder> logger)
        {
            this.logger = logger;
        }

        public DeviceContextHolder AquireDeviceContext(Process process)
        {
            logger.LogInformation("[GET][DeviceContext]");
            logger.LogInformation("[CREATE][CompatibleDeviceContext]");

            this.Process = process;
            this.WindowHandle = process.MainWindowHandle;
            DeviceContext = NativeMethods.GetDC(process.MainWindowHandle);
            CompatibleDeviceContext = NativeMethods.CreateCompatibleDC(DeviceContext);
            NativeMethods.GetClientRect(process.MainWindowHandle, out RECT value);
            Dimensions = Rectangle.FromLTRB(value.left, value.top, value.right, value.bottom);

            return this;
        }

        public Bitmap? TryPrintWindow()
        {
            Bitmap bitmap = new Bitmap(Dimensions.Width, Dimensions.Height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                IntPtr deviceContext = graphics.GetHdc();

                try
                {
                    if (NativeMethods.PrintWindow(WindowHandle, deviceContext, 0)) return bitmap;
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Failed to capture bitmap of {Process.ProcessName}");

                    bitmap.Dispose();
                }
                finally
                {
                    graphics.ReleaseHdc(deviceContext);
                }
            }

            return null;
        }

        public Bitmap? TryBitBlt(Rectangle? region = null)
        {
            IntPtr pBitmap = IntPtr.Zero;
            IntPtr pOldBitmap = IntPtr.Zero;
            Rectangle? dimensions = region ?? Dimensions;

            try
            {
                pBitmap = NativeMethods.CreateCompatibleBitmap(DeviceContext, dimensions.Value.Width, dimensions.Value.Height);
                pOldBitmap = NativeMethods.SelectObject(CompatibleDeviceContext, pBitmap);

                if (NativeMethods.BitBlt(
                    CompatibleDeviceContext,
                    0,
                    0,
                    dimensions.Value.Width,
                    dimensions.Value.Height,
                    DeviceContext,
                    dimensions.Value.Left,
                    dimensions.Value.Top,
                    TernaryRasterOperations.SRCCOPY))
                {
                    return Image.FromHbitmap(pBitmap);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Failed to capture bitmap of {Process.ProcessName}");
            }
            finally
            {
                NativeMethods.SelectObject(CompatibleDeviceContext, pOldBitmap);
                NativeMethods.DeleteObject(pBitmap);
            }

            return null;
        }

        public void Dispose()
        {
            logger.LogInformation("[DELETE][CompatibleDeviceContext]");
            logger.LogInformation("[RELEASE][DeviceContext]");

            NativeMethods.DeleteDC(CompatibleDeviceContext);
            NativeMethods.ReleaseDC(WindowHandle, DeviceContext);
        }
    }
}