using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Polly;

namespace HeroesReplay.Core.Dependencies;

public enum DependencyInstallOutcome
{
    AlreadyInstalled,
    Installed,
    Failed,
}

public sealed record DependencyInstallResult(
    DependencyInstallOutcome Outcome,
    string Directory,
    string Message
)
{
    public bool Ok => Outcome != DependencyInstallOutcome.Failed;
}

/// <summary>
/// <c>heroesreplay deps install</c> for one pinned tool. Downloads the archive into
/// <c>&lt;root&gt;\.staging</c>, checks its size and SHA-256 against the pin, extracts only the
/// pinned files there, then moves each one over <c>&lt;root&gt;\&lt;name&gt;</c> on the same volume.
/// A tool folder never holds a half-written exe, a failed run leaves the previous files, and a
/// second run with the pinned build in place does nothing.
/// </summary>
public sealed class DependencyInstaller
{
    public const string StagingFolder = ".staging";
    public const string LockFile = ".deps-install.lock";
    private const int DownloadRetries = 2;

    private readonly HttpClient http;
    private readonly Action<string> report;
    private readonly TimeSpan retryDelay;

    public DependencyInstaller(
        HttpClient http,
        Action<string> report = null,
        TimeSpan? retryDelay = null
    )
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.report = report ?? (_ => { });
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    public async Task<DependencyInstallResult> InstallAsync(
        DependencyPin pin,
        string root,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(pin);
        string target = Path.Combine(root, pin.Name);
        DependencyInstallDecision decision = Decide(pin, target);
        if (!decision.Install)
        {
            return new(DependencyInstallOutcome.AlreadyInstalled, target, decision.Reason);
        }

        Directory.CreateDirectory(root);
        using FileStream gate = TryLock(Path.Combine(root, LockFile));
        if (gate == null)
        {
            return new(
                DependencyInstallOutcome.Failed,
                target,
                $"Another deps install is running in {root}. Nothing was changed."
            );
        }

        // The run that held the lock may have installed it.
        decision = Decide(pin, target);
        if (!decision.Install)
        {
            return new(DependencyInstallOutcome.AlreadyInstalled, target, decision.Reason);
        }

        report(decision.Reason);
        string stagingRoot = Path.Combine(root, StagingFolder);
        string staging = Path.Combine(stagingRoot, pin.Name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string archive = Path.Combine(staging, "archive" + ArchiveExtension(pin));
            report(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Downloading {pin.Name} {pin.Version} ({pin.Size / 1048576.0:0.0} MB) from {pin.Url}"
                )
            );
            await DownloadWithRetryAsync(pin.Url, archive, cancellationToken).ConfigureAwait(false);

            long length = new FileInfo(archive).Length;
            if (length != pin.Size)
            {
                return new(
                    DependencyInstallOutcome.Failed,
                    target,
                    $"The download is {length} bytes; the pin is {pin.Size}. Nothing was installed."
                );
            }

            string actual = await Sha256Async(archive, cancellationToken).ConfigureAwait(false);
            if (!HashMatches(actual, pin.Sha256))
            {
                return new(
                    DependencyInstallOutcome.Failed,
                    target,
                    $"SHA-256 mismatch: the download is {actual}, the pin is {pin.Sha256}. Nothing was installed."
                );
            }

            report($"SHA-256 {actual} matches the pin.");
            IReadOnlyList<DependencyInstalledFile> files = Extract(archive, pin.Files, staging);
            Publish(staging, target, files);
            new DependencyInstallRecord(
                pin.Name,
                pin.Version,
                pin.Sha256,
                pin.Url,
                files,
                DateTimeOffset.UtcNow
            ).Write(target);
            return new(
                DependencyInstallOutcome.Installed,
                target,
                $"Installed {pin.Name} {pin.Version} ({string.Join(", ", files.Select(file => file.Name))}) into {target}."
            );
        }
        catch (Exception e)
            when (e
                    is HttpRequestException
                        or IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
            )
        {
            return new(
                DependencyInstallOutcome.Failed,
                target,
                $"{pin.Name} {pin.Version} was not installed: {e.Message}"
            );
        }
        finally
        {
            TryDelete(staging);
            TryDeleteEmpty(stagingRoot);
        }
    }

    public static bool HashMatches(string actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual)
        && !string.IsNullOrWhiteSpace(expected)
        && string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static DependencyInstallDecision Decide(DependencyPin pin, string target) =>
        DependencyInstallDecision.Decide(
            pin,
            DependencyInstallRecord.Read(target),
            name =>
            {
                var file = new FileInfo(Path.Combine(target, name));
                return file.Exists ? file.Length : null;
            }
        );

    /// <summary>A dropped connection or a server error is tried again; a hash mismatch is not.</summary>
    private async Task DownloadWithRetryAsync(
        string url,
        string path,
        CancellationToken cancellationToken
    )
    {
        ResiliencePipeline<bool> retry = ResilienceRetry.Constant<bool>(
            DownloadRetries,
            retryDelay,
            outcome => Transient(outcome.Exception),
            args =>
                report(
                    $"The download failed ({args.Outcome.Exception?.Message}). Trying again in {retryDelay.TotalSeconds:0} s."
                )
        );
        await retry
            .ExecuteAsync(
                async token =>
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    await DownloadAsync(url, path, token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static bool Transient(Exception exception) =>
        exception is HttpIOException
        || exception is HttpRequestException { StatusCode: null }
        || exception
            is HttpRequestException
            {
                StatusCode: >= System.Net.HttpStatusCode.InternalServerError
            };

    private async Task DownloadAsync(string url, string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            )
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using FileStream file = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
        );
        await body.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes each pinned file out of the archive by name, wherever it sits (Gyan: <c>bin\</c>).
    /// Exactly one entry may match. The file is written under the pin's own name in
    /// <paramref name="staging"/>, never at a path the archive chose.
    /// </summary>
    private static IReadOnlyList<DependencyInstalledFile> Extract(
        string archive,
        IReadOnlyList<string> names,
        string staging
    )
    {
        using ZipArchive zip = ZipFile.OpenRead(archive);
        var files = new List<DependencyInstalledFile>();
        foreach (string name in names)
        {
            ZipArchiveEntry[] matches = zip
                .Entries.Where(entry =>
                    !string.IsNullOrEmpty(entry.Name)
                    && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)
                )
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    matches.Length == 0
                        ? $"The archive has no {name}."
                        : $"The archive has {matches.Length} files named {name}."
                );
            }

            string staged = Path.Combine(staging, name);
            matches[0].ExtractToFile(staged, overwrite: false);
            using FileStream stream = File.OpenRead(staged);
            string sha = Convert.ToHexStringLower(SHA256.HashData(stream));
            files.Add(new DependencyInstalledFile(name, stream.Length, sha));
        }

        return files;
    }

    /// <summary>
    /// The record goes first, so an interrupted move installs again on the next run. Each file is
    /// a rename on the same volume: the old exe stays whole until the new one replaces it.
    /// </summary>
    private static void Publish(
        string staging,
        string target,
        IReadOnlyList<DependencyInstalledFile> files
    )
    {
        Directory.CreateDirectory(target);
        string record = DependencyInstallRecord.PathIn(target);
        if (File.Exists(record))
        {
            File.Delete(record);
        }

        foreach (DependencyInstalledFile file in files)
        {
            File.Move(
                Path.Combine(staging, file.Name),
                Path.Combine(target, file.Name),
                overwrite: true
            );
        }
    }

    /// <summary>One install per tools folder at a time. The lock file goes when it is closed.</summary>
    private static FileStream TryLock(string path)
    {
        try
        {
            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ArchiveExtension(DependencyPin pin)
    {
        string extension = Path.GetExtension(new Uri(pin.Url).AbsolutePath);
        return string.IsNullOrWhiteSpace(extension) ? ".zip" : extension;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A later run uses a new staging folder; this one is only disk space.
        }
    }

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            if (
                Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any()
            )
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
