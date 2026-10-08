using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.ServiceHost;

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

    /// <summary>How often a linked token looks for the stop file.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static ServiceStopLink Link(
        CancellationToken console,
        string path = null,
        TimeSpan? pollInterval = null
    )
    {
        string file = path ?? DefaultPath;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(console);
        TimeSpan interval = pollInterval ?? PollInterval;
        _ = Task.Run(() => WatchAsync(linked, file, interval));
        return new ServiceStopLink(linked);
    }

    private static async Task WatchAsync(
        CancellationTokenSource linked,
        string path,
        TimeSpan interval
    )
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

                await Task.Delay(interval, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
}
