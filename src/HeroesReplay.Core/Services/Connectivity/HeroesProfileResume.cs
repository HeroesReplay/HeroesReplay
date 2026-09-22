using System;
using System.IO;
using System.Threading;

namespace HeroesReplay.Core.Services.Connectivity;

public sealed class HeroesProfileResume : IHeroesProfileResume
{
    public static string SharedPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "heroesprofile.resume"
        );

    private readonly string path;
    private int pending;

    public HeroesProfileResume()
        : this(null) { }

    public HeroesProfileResume(string path)
    {
        this.path = path;
    }

    public bool IsPending => Volatile.Read(ref pending) != 0 || FileExists();

    public void Arm()
    {
        Interlocked.Exchange(ref pending, 1);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public bool Consume()
    {
        bool memory = Interlocked.Exchange(ref pending, 0) != 0;
        return memory || TakeFile();
    }

    private bool FileExists() => !string.IsNullOrEmpty(path) && File.Exists(path);

    private bool TakeFile()
    {
        if (!FileExists())
        {
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return !File.Exists(path);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
