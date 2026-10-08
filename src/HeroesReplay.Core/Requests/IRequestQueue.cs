using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Requests;

public interface IRequestQueue
{
    Task<RewardResponse> EnqueueItemAsync(RewardRequest request);
    Task<int> GetItemsInQueue();
    Task<RewardQueueItem> FindByIndexAsync(int index);
    Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login);
    Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login);

    /// <summary>
    /// The first request whose download is due at <paramref name="now"/>. It stays in the
    /// queue: only <see cref="CompleteDownloadAsync"/> or <see cref="FailDownloadAsync"/> removes
    /// it (#351). Null when none is due or the queue is busy.
    /// </summary>
    Task<RewardQueueItem> PeekDownloadAsync(DateTimeOffset now);

    /// <summary>
    /// Under the queue lock: when <paramref name="item"/> is still queued, runs
    /// <paramref name="publish"/> (which puts its replay on disk) and then removes it. When
    /// <paramref name="publish"/> throws, the request stays queued and the error is rethrown.
    /// </summary>
    Task<RequestCompletion> CompleteDownloadAsync(RewardQueueItem item, Action publish);

    /// <summary>
    /// Records a failed download attempt. The request stays queued and is not due again until
    /// the backoff has passed. Null when it is no longer queued or the queue is busy.
    /// </summary>
    Task<RequestDownload> RetryDownloadLaterAsync(
        RewardQueueItem item,
        string error,
        DateTimeOffset now
    );

    /// <summary>
    /// Gives the request up: it is kept in the failed file with <paramref name="reason"/> and
    /// leaves the queue. False when the queue was busy and it is still queued.
    /// </summary>
    Task<bool> FailDownloadAsync(
        RewardQueueItem item,
        string reason,
        bool refundRequested,
        DateTimeOffset now
    );
}

public enum RequestCompletion
{
    /// <summary>The replay was published and the request left the queue.</summary>
    Completed,

    /// <summary>The request was no longer queued (the viewer removed it). Nothing was published.</summary>
    NotQueued,

    /// <summary>The queue lock was not free. Nothing was published and the request stays queued.</summary>
    Busy,
}
