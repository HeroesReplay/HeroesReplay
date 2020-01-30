using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace HeroesReplay.Processes
{
    public class ScreenCapture
    {
        private readonly CaptureMethod captureMethod;

        public ScreenCapture(CaptureMethod captureMethod)
        {
            this.captureMethod = captureMethod;
        }

        public Bitmap Capture(IntPtr handle, Rectangle? region = null)
        {
            return captureMethod switch
            {
                CaptureMethod.BitBlt => BitBlt(region, handle),
                CaptureMethod.PrintWindow => PrintWindow(region, handle),
                CaptureMethod.CopyFromScreen => CopyFromScreen(region, handle)
            };
        }

        public Rectangle GetDimensions(IntPtr handle)
        {
            NativeMethods.GetClientRect(handle, out RECT rect);
            return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public Bitmap BitBlt(Rectangle? region, IntPtr handle)
        {
            Rectangle bounds = region ?? GetDimensions(handle);

            using (Graphics source = Graphics.FromHwnd(handle))
            {
                Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, source);

                using (Graphics destination = Graphics.FromImage(bitmap))
                {
                    IntPtr deviceContextSource = source.GetHdc();
                    IntPtr deviceContextDestination = destination.GetHdc();

                    NativeMethods.BitBlt(deviceContextDestination, 0, 0, bounds.Width, bounds.Height, deviceContextSource, bounds.Left, bounds.Top, TernaryRasterOperations.SRCCOPY);

                    source.ReleaseHdc(deviceContextSource);
                    destination.ReleaseHdc(deviceContextDestination);
                }

                return bitmap;
            }
        }

        public Bitmap PrintWindow(Rectangle? region, IntPtr handle)
        {
            NativeMethods.GetWindowRect(handle, out RECT rect);
            Rectangle bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                IntPtr deviceContext = graphics.GetHdc();
                NativeMethods.PrintWindow(handle, deviceContext, 0);
                graphics.ReleaseHdc(deviceContext);

                if (region == null) return bitmap;

                using (bitmap)
                {
                    return bitmap.Clone(region.Value, bitmap.PixelFormat);
                }
            }
        }

        public Bitmap CopyFromScreen(Rectangle? region, IntPtr handle)
        {
            Rectangle bounds = region ?? GetDimensions(handle);

            NativeMethods.SetForegroundWindow(handle);

            Bitmap result = new Bitmap(bounds.Width, bounds.Height);

            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            return result;
        }
    }
}