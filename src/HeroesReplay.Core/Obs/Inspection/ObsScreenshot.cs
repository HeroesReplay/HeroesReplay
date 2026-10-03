using System;
using System.Buffers.Binary;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>
/// A PNG of the program scene or a named source from GetSourceScreenshot. OBS scales it to
/// <see cref="Width"/> and keeps the source's aspect ratio. The whole image is kept: the agent
/// tool returns it as an MCP image, not as truncated base64 text.
/// </summary>
public sealed record ObsScreenshot(
    string Source,
    bool ProgramScene,
    int Width,
    int Height,
    byte[] Png
)
{
    public const int DefaultWidth = 960;
    public const int MaxWidth = 1920;

    /// <summary>obs-websocket rejects an imageWidth below 8.</summary>
    public const int MinWidth = 8;

    public static int ClampWidth(int? width) =>
        width is null or <= 0 ? DefaultWidth : Math.Clamp(width.Value, MinWidth, MaxWidth);

    /// <summary>A blank <paramref name="source"/> takes the current program scene.</summary>
    /// <exception cref="ObsRequestException">OBS has no such source, or could not render it.</exception>
    public static ObsScreenshot Capture(IObsReadSession session, string source, int? width)
    {
        ArgumentNullException.ThrowIfNull(session);
        bool program = string.IsNullOrWhiteSpace(source);
        string name = program
            ? ObsInspector.ProgramSceneName(session.Get("GetCurrentProgramScene"))
            : source.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("OBS did not report a program scene.");
        }

        JObject response = session.Get(
            "GetSourceScreenshot",
            new JObject
            {
                ["sourceName"] = name,
                ["imageFormat"] = "png",
                ["imageWidth"] = ClampWidth(width),
            }
        );
        byte[] png = Decode(ObsResponse.String(response, "imageData"));
        (int pngWidth, int pngHeight) = Size(png);
        return new ObsScreenshot(name, program, pngWidth, pngHeight, png);
    }

    /// <summary>imageData is a data URI: <c>data:image/png;base64,…</c>.</summary>
    internal static byte[] Decode(string imageData)
    {
        if (string.IsNullOrWhiteSpace(imageData))
        {
            throw new InvalidOperationException("OBS returned no image data.");
        }

        int comma = imageData.IndexOf(',');
        string base64 =
            imageData.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
                ? imageData.Substring(comma + 1)
                : imageData;
        return Convert.FromBase64String(base64);
    }

    /// <summary>Width and height from the PNG IHDR chunk; zero when the bytes are not a PNG.</summary>
    internal static (int Width, int Height) Size(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png == null || png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(signature))
        {
            return (0, 0);
        }

        return (
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4))
        );
    }
}
