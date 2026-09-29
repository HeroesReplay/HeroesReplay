using System;
using System.IO;

namespace HeroesReplay.Core.Services.Queue;

/// <summary>
/// JSON state is written to a temp file and then renamed. A file that cannot be
/// parsed is moved aside so a later save does not erase it.
/// </summary>
public static class DurableFile
{
    public static void Replace(string path, string contents)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, contents ?? string.Empty);
        File.Move(temp, path, overwrite: true);
    }

    public static string ReadOrAside(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            Aside(path);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            Aside(path);
            return null;
        }
    }

    public static void Aside(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        string dest = path + ".corrupt-" + DateTime.UtcNow.Ticks;
        try
        {
            File.Move(path, dest, overwrite: false);
        }
        catch (IOException)
        {
            // The unreadable file stays where it is. The caller must not overwrite it.
        }
        catch (UnauthorizedAccessException) { }
    }
}
