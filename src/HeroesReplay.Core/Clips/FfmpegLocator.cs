using System.IO;

namespace HeroesReplay.Core.Clips;

public static class FfmpegLocator
{
    public static string Find(string tool)
    {
        string installed = Path.Combine(@"C:\ffmpeg\bin", tool + ".exe");
        if (File.Exists(installed))
        {
            return installed;
        }

        return tool;
    }
}
