using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

public sealed class UploadAttemptStore
{
    public const string ManifestFileName = "attempt-manifest.json";
    public const string LockFileName = "attempt-manifest.lock";

    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false
    );
    private readonly string root;

    public UploadAttemptStore(string attemptsRoot)
    {
        if (string.IsNullOrWhiteSpace(attemptsRoot))
        {
            throw new ArgumentException("An attempt root is required.", nameof(attemptsRoot));
        }

        root = attemptsRoot;
    }

    public string Root
    {
        get { return root; }
    }

    public string AttemptDirectory(string attemptId)
    {
        if (!UploadAttemptIds.IsSafe(attemptId))
        {
            return null;
        }

        return Path.Combine(root, attemptId);
    }

    public async Task<UploadAttemptResult> CreateAsync(
        UploadAttemptManifest initial,
        CancellationToken cancellationToken
    )
    {
        string problem = InitialProblem(initial);
        if (problem != null)
        {
            return UploadAttemptResult.Failure(problem, null);
        }

        string directory = AttemptDirectory(initial.AttemptId);
        Directory.CreateDirectory(directory);
        return await LockedAsync(
                directory,
                async () =>
                {
                    UploadAttemptResult existing = await ReadAsync(
                            directory,
                            initial.AttemptId,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    if (existing.Reason != UploadAttemptReasons.ManifestMissing)
                    {
                        if (existing.Succeeded)
                        {
                            return UploadAttemptResult.Failure(
                                UploadAttemptReasons.ManifestConflict,
                                existing.Manifest
                            );
                        }

                        return existing;
                    }

                    await WriteAtomicAsync(directory, initial.WithRevision(1), cancellationToken)
                        .ConfigureAwait(false);
                    return await ReadAsync(directory, initial.AttemptId, cancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Attaches an observe-only policy. Upload state, media, and video id stay as they are.
    /// A decision that was already published is not replaced.
    /// </summary>
    public async Task<UploadAttemptResult> SavePolicyAsync(
        string attemptId,
        int? replayId,
        DateTimeOffset at,
        UploadAttemptPolicy policy,
        bool replaceOpen,
        CancellationToken cancellationToken
    )
    {
        if (policy == null)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, null);
        }

        UploadAttemptResult prepared = UploadAttemptMachine.Prepare(attemptId, replayId, at);
        if (!prepared.Succeeded)
        {
            return prepared;
        }

        string directory = AttemptDirectory(attemptId);
        if (directory == null)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.AttemptIdInvalid, null);
        }

        Directory.CreateDirectory(directory);
        return await LockedAsync(
                directory,
                async () =>
                {
                    UploadAttemptResult existing = await ReadAsync(
                            directory,
                            attemptId,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    if (existing.Reason == UploadAttemptReasons.ManifestMissing)
                    {
                        await WriteAtomicAsync(
                                directory,
                                ApplyPolicy(prepared.Manifest, policy, at, replayId)
                                    .WithRevision(1),
                                cancellationToken
                            )
                            .ConfigureAwait(false);
                        return await ReadAsync(directory, attemptId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!existing.Succeeded)
                    {
                        return existing;
                    }

                    if (ReplayIdsConflict(existing.Manifest.ReplayId, replayId))
                    {
                        return UploadAttemptResult.Failure(
                            UploadAttemptReasons.ManifestConflict,
                            existing.Manifest
                        );
                    }

                    UploadAttemptPolicy current = existing.Manifest.Policy;
                    if (current != null && (!replaceOpen || current.PublicationEvaluated))
                    {
                        return existing;
                    }

                    await WriteAtomicAsync(
                            directory,
                            ApplyPolicy(existing.Manifest, policy, at, replayId)
                                .WithRevision(existing.Manifest.Revision + 1),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    return await ReadAsync(directory, attemptId, cancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task<UploadAttemptResult> LoadAsync(
        string attemptId,
        CancellationToken cancellationToken
    )
    {
        return AdvanceAsync(attemptId, null, null, cancellationToken);
    }

    public Task<UploadAttemptResult> UpdateAsync(
        string attemptId,
        Func<UploadAttemptManifest, UploadAttemptResult> transition,
        CancellationToken cancellationToken
    )
    {
        return AdvanceAsync(attemptId, null, transition, cancellationToken);
    }

    public Task<UploadAttemptResult> AdvanceFromAsync(
        string attemptId,
        long observedRevision,
        Func<UploadAttemptManifest, UploadAttemptResult> transition,
        CancellationToken cancellationToken
    )
    {
        return AdvanceAsync(attemptId, observedRevision, transition, cancellationToken);
    }

    public async Task<IReadOnlyList<UploadAttemptManifest>> ListOpenAsync(
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<UploadAttemptManifest>();
        }

        var found = new List<UploadAttemptManifest>();
        foreach (string directory in Directory.GetDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string attemptId = Path.GetFileName(directory);
            string manifestPath = Path.Combine(directory, ManifestFileName);
            if (!UploadAttemptIds.IsSafe(attemptId) || !File.Exists(manifestPath))
            {
                continue;
            }

            UploadAttemptResult loaded = await LoadAsync(attemptId, cancellationToken)
                .ConfigureAwait(false);
            if (!loaded.Succeeded || !IsOpen(loaded.Manifest.State))
            {
                continue;
            }

            found.Add(loaded.Manifest);
        }

        found.Sort(
            (left, right) => StringComparer.Ordinal.Compare(left.AttemptId, right.AttemptId)
        );
        return found;
    }

    private async Task<UploadAttemptResult> AdvanceAsync(
        string attemptId,
        long? observedRevision,
        Func<UploadAttemptManifest, UploadAttemptResult> transition,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.AttemptIdRequired, null);
        }

        if (!UploadAttemptIds.IsSafe(attemptId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.AttemptIdInvalid, null);
        }

        string directory = AttemptDirectory(attemptId);
        if (!Directory.Exists(directory))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestMissing, null);
        }

        return await LockedAsync(
                directory,
                async () =>
                {
                    UploadAttemptResult loaded = await ReadAsync(
                            directory,
                            attemptId,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    if (transition == null)
                    {
                        return loaded;
                    }

                    if (!loaded.Succeeded)
                    {
                        return loaded;
                    }

                    if (
                        observedRevision.HasValue
                        && loaded.Manifest.Revision != observedRevision.Value
                    )
                    {
                        return UploadAttemptResult.Failure(
                            UploadAttemptReasons.ManifestConflict,
                            loaded.Manifest
                        );
                    }

                    if (
                        loaded.Manifest.State == UploadAttemptState.Uploaded
                        && observedRevision.HasValue
                    )
                    {
                        return UploadAttemptResult.Failure(
                            UploadAttemptReasons.ManifestConflict,
                            loaded.Manifest
                        );
                    }

                    UploadAttemptResult proposed = transition(loaded.Manifest);
                    if (proposed == null || !proposed.Succeeded)
                    {
                        return proposed
                            ?? UploadAttemptResult.Failure(
                                UploadAttemptReasons.IllegalTransition,
                                loaded.Manifest
                            );
                    }

                    string rejection = UploadAttemptMachine.SuccessorRejection(
                        loaded.Manifest,
                        proposed.Manifest
                    );
                    if (rejection != null)
                    {
                        return UploadAttemptResult.Failure(rejection, loaded.Manifest);
                    }

                    if (loaded.Manifest.State == UploadAttemptState.Uploaded)
                    {
                        return UploadAttemptResult.Failure(
                            UploadAttemptReasons.ManifestConflict,
                            loaded.Manifest
                        );
                    }

                    UploadAttemptManifest proposedManifest = proposed.Manifest;
                    if (proposedManifest == null)
                    {
                        return UploadAttemptResult.Failure(
                            UploadAttemptReasons.IllegalTransition,
                            loaded.Manifest
                        );
                    }

                    if (proposedManifest.Policy == null && loaded.Manifest.Policy != null)
                    {
                        proposedManifest = proposedManifest.WithPolicy(loaded.Manifest.Policy);
                    }

                    await WriteAtomicAsync(
                            directory,
                            proposedManifest.WithRevision(loaded.Manifest.Revision + 1),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    return await ReadAsync(directory, attemptId, cancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<UploadAttemptResult> ReadAsync(
        string directory,
        string attemptId,
        CancellationToken cancellationToken
    )
    {
        string destination = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(destination))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestMissing, null);
        }

        try
        {
            string json = await File.ReadAllTextAsync(destination, cancellationToken)
                .ConfigureAwait(false);
            return UploadAttemptManifestCodec.Read(json, attemptId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestUnreadable, null);
        }
        catch (UnauthorizedAccessException)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestUnreadable, null);
        }
    }

    private static async Task WriteAtomicAsync(
        string directory,
        UploadAttemptManifest manifest,
        CancellationToken cancellationToken
    )
    {
        DeleteLeftoverTemps(directory);
        string json = UploadAttemptManifestCodec.Write(manifest);
        string destination = Path.Combine(directory, ManifestFileName);
        string temporary = Path.Combine(
            directory,
            ManifestFileName + "." + Guid.NewGuid().ToString("N") + ".tmp"
        );
        try
        {
            byte[] payload = Utf8.GetBytes(json);
            using (
                FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough
                )
            )
            {
                await stream
                    .WriteAsync(payload.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Replace keeps readers on the previous complete manifest until the new bytes are in place.
            if (File.Exists(destination))
            {
                File.Replace(
                    temporary,
                    destination,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true
                );
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void DeleteLeftoverTemps(string directory)
    {
        foreach (string leftover in Directory.GetFiles(directory, "*.tmp"))
        {
            string name = Path.GetFileName(leftover);
            if (name.StartsWith(ManifestFileName + ".", StringComparison.Ordinal))
            {
                File.Delete(leftover);
            }
        }
    }

    private async Task<UploadAttemptResult> LockedAsync(
        string directory,
        Func<Task<UploadAttemptResult>> body,
        CancellationToken cancellationToken
    )
    {
        FileStream gate = await AcquireAsync(directory, cancellationToken).ConfigureAwait(false);
        if (gate == null)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestBusy, null);
        }

        using (gate)
        {
            return await body().ConfigureAwait(false);
        }
    }

    private static async Task<FileStream> AcquireAsync(
        string directory,
        CancellationToken cancellationToken
    )
    {
        string lockPath = Path.Combine(directory, LockFileName);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous
                );
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return null;
    }

    private static string InitialProblem(UploadAttemptManifest manifest)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.AttemptId))
        {
            return UploadAttemptReasons.AttemptIdRequired;
        }

        if (manifest.UpdatedAtUtc.Offset != TimeSpan.Zero)
        {
            return UploadAttemptReasons.ClockNotUtc;
        }

        if (!UploadAttemptIds.IsSafe(manifest.AttemptId))
        {
            return UploadAttemptReasons.AttemptIdInvalid;
        }

        if (
            manifest.Schema != UploadAttemptManifest.SchemaVersion
            || manifest.State != UploadAttemptState.Prepared
            || manifest.MediaSize != 0
            || UploadAttemptReceipt.HasExactText(manifest.MediaPath)
            || UploadAttemptReceipt.HasExactText(manifest.MediaHash)
            || UploadAttemptReceipt.HasExactText(manifest.VideoId)
            || UploadAttemptReceipt.HasExactText(manifest.ReceiptKind)
        )
        {
            return UploadAttemptReasons.IllegalTransition;
        }

        return null;
    }

    private static UploadAttemptManifest ApplyPolicy(
        UploadAttemptManifest manifest,
        UploadAttemptPolicy policy,
        DateTimeOffset updatedAtUtc,
        int? replayId
    )
    {
        int? id = manifest.ReplayId;
        if ((id == null || id <= 0) && replayId is int incoming && incoming > 0)
        {
            id = incoming;
        }

        return new UploadAttemptManifest
        {
            Schema = manifest.Schema,
            AttemptId = manifest.AttemptId,
            ReplayId = id,
            State = manifest.State,
            MediaPath = manifest.MediaPath,
            MediaSize = manifest.MediaSize,
            MediaHash = manifest.MediaHash,
            VideoId = manifest.VideoId,
            Revision = manifest.Revision,
            UpdatedAtUtc = updatedAtUtc,
            ReceiptKind = manifest.ReceiptKind,
            Policy = policy,
        };
    }

    private static bool ReplayIdsConflict(int? existing, int? incoming)
    {
        return existing is int left
            && left > 0
            && incoming is int right
            && right > 0
            && left != right;
    }

    private static bool IsOpen(UploadAttemptState state)
    {
        return state == UploadAttemptState.UploadPending
            || state == UploadAttemptState.Uploading
            || state == UploadAttemptState.AmbiguousUpload;
    }
}
