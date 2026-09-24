using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Analysis;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FfmpegClipCutterTests
{
    [Fact]
    public void CutArguments_SeekAfterTheInputAndDoNotCrop()
    {
        IReadOnlyList<string> arguments = FfmpegClipCutter.CutArguments(
            "match.mp4",
            "pentakill.mp4",
            12.5,
            32
        );

        int input = arguments.ToList().IndexOf("-i");
        int seek = arguments.ToList().IndexOf("-ss");
        Assert.True(input >= 0 && seek > input);
        Assert.Equal("match.mp4", arguments[input + 1]);
        Assert.Equal("12.5", arguments[seek + 1]);
        Assert.DoesNotContain(
            arguments,
            argument => argument.Contains("crop", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            arguments,
            argument => argument.Contains("scale", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains("libx264", arguments);
    }

    [Fact]
    public void CutDirectory_KeepsTheSourceWidthAndHeight()
    {
        Assert.True(IsFfmpeg902(), "ffmpeg 9.0.2 is not on PATH.");

        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-clip-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "match.mp4");
            int code = Run(
                FfmpegClipCutter.Tool("ffmpeg"),
                "-y",
                "-f",
                "lavfi",
                "-i",
                "color=c=black:s=320x240:d=2",
                "-f",
                "lavfi",
                "-i",
                "anullsrc=r=48000:cl=stereo",
                "-shortest",
                "-c:v",
                "libx264",
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-t",
                "2",
                source
            );
            Assert.Equal(0, code);
            Assert.True(FfmpegClipCutter.TryProbe(source, out int width, out int height, out _));

            MatchClipList.Write(
                Path.Combine(directory, MatchClipList.FileName),
                new[]
                {
                    new MatchClipEntry
                    {
                        ReplayId = 1,
                        Kind = "pentakill",
                        Hero = "Li-Ming",
                        Description = "Li-Ming pentakill",
                        FileStartSeconds = 0.4,
                        FileEndSeconds = 1.1,
                        File = "pentakill-Li-Ming-1.mp4",
                    },
                }
            );

            IReadOnlyList<FfmpegCutResult> results = FfmpegClipCutter.CutDirectory(directory, null);
            FfmpegCutResult result = Assert.Single(results);
            Assert.True(result.Ok, result.Detail);
            Assert.True(
                FfmpegClipCutter.TryProbe(
                    result.OutputPath,
                    out int clipWidth,
                    out int clipHeight,
                    out double duration
                )
            );
            Assert.Equal(width, clipWidth);
            Assert.Equal(height, clipHeight);
            Assert.InRange(duration, 0.4, 2.2);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static bool IsFfmpeg902()
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = FfmpegClipCutter.Tool("ffmpeg"),
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process process = Process.Start(start);
            if (process == null)
            {
                return false;
            }

            string version = process.StandardOutput.ReadLine();
            process.WaitForExit(10000);
            return version != null
                && version.StartsWith("ffmpeg version 9.0.2", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int Run(string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(60000);
        stdout.GetAwaiter().GetResult();
        stderr.GetAwaiter().GetResult();
        return process.ExitCode;
    }
}
