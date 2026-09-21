using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Processes;

public sealed class ServiceStopLink : IDisposable
{
    private readonly CancellationTokenSource linked;

    public ServiceStopLink(CancellationTokenSource linked)
    {
        this.linked = linked ?? throw new ArgumentNullException(nameof(linked));
    }

    public CancellationToken Token => linked.Token;

    public void Dispose()
    {
        try
        {
            linked.Cancel();
        }
        catch (ObjectDisposedException) { }

        linked.Dispose();
    }
}

public static class ServiceStopFile
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "services.stop"
        );

    public static void Request(string path = null)
    {
        string file = path ?? DefaultPath;
        string directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(file, DateTimeOffset.UtcNow.ToString("O"));
    }

    public static void Clear(string path = null)
    {
        string file = path ?? DefaultPath;
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    public static ServiceStopLink Link(CancellationToken console, string path = null)
    {
        string file = path ?? DefaultPath;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(console);
        _ = Task.Run(() => WatchAsync(linked, file));
        return new ServiceStopLink(linked);
    }

    private static async Task WatchAsync(CancellationTokenSource linked, string path)
    {
        try
        {
            while (!linked.IsCancellationRequested)
            {
                if (File.Exists(path))
                {
                    linked.Cancel();
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), linked.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
}
