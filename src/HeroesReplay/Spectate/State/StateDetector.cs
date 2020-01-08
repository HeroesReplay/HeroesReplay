using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HeroesReplay
{
    public sealed class StateDetector : IDisposable
    {
        private readonly Bitmap zerosTimer = (Bitmap)Image.FromFile(Path.Combine(AssetsPath, "START.png"));
        private readonly Bitmap playButton = (Bitmap)Image.FromFile(Path.Combine(AssetsPath, "PLAY.png"));

        private readonly ILogger<StateDetector> logger;
        private readonly GameWrapper wrapper;

        private static Assembly Assembly => Assembly.GetExecutingAssembly();
        private static readonly string AssetsPath = Path.Combine(Path.GetDirectoryName(Assembly.Location), "Assets");

        public StateDetector(ILogger<StateDetector> logger, GameWrapper wrapper)
        {
            this.logger = logger;
            this.wrapper = wrapper;           
        }

        public bool TryIsRunning(out bool running)
        {
            running = false;

            try
            {
                if (wrapper.TryGetScreenshot(out Bitmap screenshot))
                {
                    var controls = screenshot.Clone(new Rectangle(0, screenshot.Height - 150, 150, 150), PixelFormat.Format32bppArgb);
                    running = !Find(controls, playButton).HasValue;
                    return true;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }

            return false;
        }

        public bool TryIsPaused(out bool paused)
        {
            paused = false;

            try
            {
                if (wrapper.TryGetScreenshot(out Bitmap screenshot))
                {
                    var controls = screenshot.Clone(new Rectangle(0, screenshot.Height - 150, 150, 150), PixelFormat.Format32bppArgb);
                    paused = Find(controls, playButton).HasValue;
                    return true;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }

            return false;
        }

        public bool TryDetectStart(out bool detected)
        {
            detected = false;

            try
            {
                if (wrapper.TryGetScreenshot(out Bitmap screenshot))
                {
                    // var timer = screenshot.Clone(new Rectangle(new Point((screenshot.Width / 2) - 100, 0), new Size(200, 100)), PixelFormat.Format32bppArgb);
                    // var controls = screenshot.Clone(new Rectangle(0, screenshot.Height - 150, 150, 150), PixelFormat.Format32bppArgb);
                    detected = Find(screenshot, zerosTimer).HasValue;
                    return true;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }

            return false;
        }


        public Point? Find(Bitmap haystack, Bitmap needle)
        {
            if (null == haystack || null == needle) return null;
            if (haystack.Width < needle.Width || haystack.Height < needle.Height) return null;

            var haystackArray = GetPixelArray(haystack);
            var needleArray = GetPixelArray(needle);

            foreach (var firstLineMatchPoint in FindMatch(haystackArray.Take(haystack.Height - needle.Height), needleArray[0]))
            {
                if (IsNeedlePresentAtLocation(haystackArray, needleArray, firstLineMatchPoint, 1))
                {
                    return firstLineMatchPoint;
                }
            }

            return null;
        }

        private static int[][] GetPixelArray(Bitmap bitmap)
        {
            var result = new int[bitmap.Height][];
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            for (int y = 0; y < bitmap.Height; ++y)
            {
                result[y] = new int[bitmap.Width];
                Marshal.Copy(bitmapData.Scan0 + y * bitmapData.Stride, result[y], 0, result[y].Length);
            }

            bitmap.UnlockBits(bitmapData);

            return result;
        }

        private static IEnumerable<Point> FindMatch(IEnumerable<int[]> haystackLines, int[] needleLine)
        {
            var y = 0;

            foreach (var haystackLine in haystackLines)
            {
                for (int x = 0, n = haystackLine.Length - needleLine.Length; x < n; ++x)
                {
                    if (ContainSameElements(haystackLine, x, needleLine, 0, needleLine.Length))
                    {
                        yield return new Point(x, y);
                    }

                }
                y += 1;
            }
        }

        private static bool ContainSameElements(int[] first, int firstStart, int[] second, int secondStart, int length)
        {
            for (int i = 0; i < length; ++i)
            {
                if (first[i + firstStart] != second[i + secondStart])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNeedlePresentAtLocation(int[][] haystack, int[][] needle, Point point, int alreadyVerified)
        {
            for (int y = alreadyVerified; y < needle.Length; ++y)
            {
                if (!ContainSameElements(haystack[y + point.Y], point.X, needle[y], 0, needle.Length)) return false;
            }

            return true;
        }

        public void Dispose()
        {
            zerosTimer?.Dispose();
            playButton?.Dispose();
        }
    }
}
