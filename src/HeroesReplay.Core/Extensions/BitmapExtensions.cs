﻿using System;
using System.Drawing;

namespace HeroesReplay.Core.Shared
{
    public static class BitmapExtensions
    {
        public static Bitmap GetResized(this Bitmap bmp, int zoom)
        {
            if (bmp == null)
                throw new ArgumentNullException(nameof(bmp));

            Bitmap result = new Bitmap(bmp.Width * zoom, bmp.Height * zoom);

            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(bmp, 0, 0, bmp.Width * zoom, bmp.Height * zoom);
            }

            return result;
        }
    }
}
