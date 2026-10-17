using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroFocus.Ledger;

/// <summary>
/// <c>Data\Focus\ledger.json</c> (#433). <see cref="Update{T}"/> is the writer's read, change and
/// write under a session-wide lock named from the file, so a test's temp ledger never waits on
/// the running stack. <see cref="Read"/> is any other process's read: it shares the file and
/// treats one it cannot read as empty, never setting it aside while the writer may be mid-write.
/// </summary>
public sealed class FocusLedgerStore
{
    public const string FileName = "ledger.json";

    /// <summary>The prefix of the lock's name; the rest comes from the file's full path.</summary>
    public const string MutexPrefix = @"Local\HeroesReplay.FocusLedger";

    internal static readonly HeroFocusJsonContext Json = new(
        new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        }
    );

    private readonly TimeSpan lockWait;

    public FocusLedgerStore(string path, TimeSpan? lockWait = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = Path.GetFullPath(path);
        this.lockWait = lockWait ?? TimeSpan.FromSeconds(10);
    }

    public string FilePath { get; }

    public string MutexName => FileMutexName.ForFile(MutexPrefix, FilePath);

    public static string PathFor(string dataDirectory) =>
        Path.Combine(HeroFocusPaths.FolderFor(dataDirectory), FileName);

    /// <summary>The ledger as it is now, or an empty one when it is missing or unreadable.</summary>
    public FocusLedger Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new FocusLedger();
            }

            using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            return JsonSerializer.Deserialize(stream, Json.FocusLedger) ?? new FocusLedger();
        }
        catch (JsonException)
        {
            return new FocusLedger();
        }
        catch (IOException)
        {
            return new FocusLedger();
        }
        catch (UnauthorizedAccessException)
        {
            return new FocusLedger();
        }
    }

    /// <summary>
    /// Reads the ledger, applies <paramref name="change"/>, and writes it back, all under the
    /// lock. A ledger that does not parse is set aside first, so the write never erases it.
    /// </summary>
    public T Update<T>(Func<FocusLedger, T> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var gate = new Mutex(false, MutexName);
        bool held = false;
        try
        {
            try
            {
                held = gate.WaitOne(lockWait);
            }
            catch (AbandonedMutexException)
            {
                held = true;
            }

            if (!held)
            {
                throw new TimeoutException($"Could not lock {FilePath}.");
            }

            FocusLedger ledger = ReadForWrite();
            T result = change(ledger);
            Write(ledger);
            return result;
        }
        finally
        {
            if (held)
            {
                gate.ReleaseMutex();
            }
        }
    }

    private FocusLedger ReadForWrite()
    {
        string text = DurableFile.ReadOrAside(FilePath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new FocusLedger();
        }

        try
        {
            return JsonSerializer.Deserialize(text, Json.FocusLedger) ?? new FocusLedger();
        }
        catch (JsonException)
        {
            DurableFile.Aside(FilePath);
            return new FocusLedger();
        }
    }

    private void Write(FocusLedger ledger)
    {
        string json = JsonSerializer.Serialize(ledger ?? new FocusLedger(), Json.FocusLedger);
        // A reader in another process may hold the file for a moment; the move then fails.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                DurableFile.Replace(FilePath, json);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException) when (attempt < 20)
            {
                Thread.Sleep(25);
            }
        }
    }
}
