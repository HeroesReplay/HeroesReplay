using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace HeroesReplay
{
    public sealed class StateDetector
    {
        private readonly ILogger<StateDetector> logger;
        private readonly GameWrapper wrapper;
        private readonly OcrEngine ocrEngine;
        private readonly ImageCodecInfo imageCodecInfo;
        private readonly Encoder encoder = Encoder.Quality;

        public StateDetector(ILogger<StateDetector> logger, GameWrapper wrapper)
        {
            this.logger = logger;
            this.wrapper = wrapper;

            this.ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
            this.imageCodecInfo = GetEncoder(ImageFormat.Bmp);
        }

        public async Task<(bool Success, TimeSpan Elapsed)> TryGetTimerAsync()
        {
            try
            {
                if (wrapper.TryGetScreenshot(out Bitmap screenshot))
                {
                    using (screenshot)
                    {
                        using (var controls = GetControlsClosed(screenshot))
                        {
                            using(var softwareBitmap = await GetSoftwareBitmapAsync(controls))
                            {
                                var results = await ocrEngine.RecognizeAsync(softwareBitmap);

                                if (results != null && results.Lines.Count > 0 && results.Text.Contains(":"))
                                {
                                    var time = results.Lines[0].Text;
                                    var segments = time.Split(":");

                                    if (segments.Length == 3)
                                    {
                                        return (Success: true, Elapsed: new TimeSpan(hours: int.Parse(segments[0]), minutes: int.Parse(segments[1]), seconds: int.Parse(segments[2])));
                                    }
                                    else if (segments.Length == 2)
                                    {
                                        return (Success: true, Elapsed: new TimeSpan(hours: 0, minutes: int.Parse(segments[0]), seconds: int.Parse(segments[1])));
                                    }
                                }
                            }

                            return (Success: false, TimeSpan.Zero);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }

            return (Success: false, TimeSpan.Zero);
        }

        // Observer Controls
        private Bitmap GetControlsOpen(Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(new Point(20, bitmap.Height - 170), new Size(480, 160)), bitmap.PixelFormat);
        }

        // Observer Controls
        private Bitmap GetControlsClosed(Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(new Point(20, bitmap.Height - 135), new Size(200, 50)), bitmap.PixelFormat);
        }

        private Bitmap GetTopTimer(Bitmap bitmap)
        {
            return bitmap.Clone(new Rectangle(new Point(bitmap.Width / 2 - 50, 15), new Size(100, 30)), bitmap.PixelFormat);
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();

            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        private async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (EncoderParameters encoderParamters = new EncoderParameters(1))
                {
                    EncoderParameter parameter = new EncoderParameter(encoder, 50L);
                    encoderParamters.Param[0] = parameter;
                    bitmap.Save(stream.AsStream(), imageCodecInfo, encoderParamters);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, stream);
                    return await decoder.GetSoftwareBitmapAsync();
                }
            }
        }
    }
}
