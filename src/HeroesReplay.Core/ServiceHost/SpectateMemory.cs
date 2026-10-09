using System;
using System.Diagnostics;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>Spectate's own memory when one replay session ended (#399).</summary>
public sealed record SpectateMemorySample
{
    /// <summary>The process's private bytes: its commit charge.</summary>
    public long PrivateBytes { get; init; }
    public long WorkingSetBytes { get; init; }

    /// <summary>The managed heap after the last collection (<see cref="GCMemoryInfo.HeapSizeBytes"/>).</summary>
    public long ManagedHeapBytes { get; init; }

    /// <summary>The large object heap after the last collection. It is never compacted by default.</summary>
    public long LargeObjectHeapBytes { get; init; }

    /// <summary>Free space between live objects in the heap (<see cref="GCMemoryInfo.FragmentedBytes"/>).</summary>
    public long FragmentedBytes { get; init; }

    /// <summary>What the GC has committed, free space included (<see cref="GCMemoryInfo.TotalCommittedBytes"/>).</summary>
    public long GcCommittedBytes { get; init; }
    public int Gen2Collections { get; init; }

    /// <summary>
    /// Private bytes that are not the GC's: native allocations, images, and handles. Growth here
    /// with a flat managed heap points away from managed code.
    /// </summary>
    public long OtherPrivateBytes => Math.Max(0, PrivateBytes - GcCommittedBytes);

    public static SpectateMemorySample ReadCurrent()
    {
        using Process process = Process.GetCurrentProcess();
        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        // Generations 0, 1, 2, then the large and the pinned object heaps.
        ReadOnlySpan<GCGenerationInfo> generations = gc.GenerationInfo;
        return new SpectateMemorySample
        {
            PrivateBytes = process.PrivateMemorySize64,
            WorkingSetBytes = process.WorkingSet64,
            ManagedHeapBytes = gc.HeapSizeBytes,
            LargeObjectHeapBytes = generations.Length > 3 ? generations[3].SizeAfterBytes : 0,
            FragmentedBytes = gc.FragmentedBytes,
            GcCommittedBytes = gc.TotalCommittedBytes,
            Gen2Collections = GC.CollectionCount(2),
        };
    }
}

/// <summary>What <see cref="SpectateMemoryTrend.Add"/> made of one session end's private bytes.</summary>
public sealed record SpectateMemoryVerdict
{
    /// <summary>Above <see cref="ServiceHealthSettings.SpectatePrivateBytesWarn"/>.</summary>
    public bool AboveCeiling { get; init; }
    public long CeilingBytes { get; init; }

    /// <summary>
    /// A gen2 collection ran since the last session end, so this sample counts toward the run.
    /// Without one, the heap still holds the garbage of the sessions before it, and the sample
    /// neither extends nor ends the run.
    /// </summary>
    public bool AfterCollection { get; init; }

    /// <summary>
    /// Counted session ends in a row, up to this one, whose private bytes rose above the counted
    /// one before.
    /// </summary>
    public int RisingSessions { get; init; }

    /// <summary>How far private bytes rose across those sessions.</summary>
    public long GrowthBytes { get; init; }

    /// <summary>
    /// This sample counted, <see cref="RisingSessions"/> reached
    /// <see cref="ServiceHealthSettings.SpectateMemoryGrowthSessions"/>, and the rise is at least
    /// <see cref="SpectateMemoryTrend.MinimumGrowthBytes"/>.
    /// </summary>
    public bool SustainedGrowth { get; init; }
}

/// <summary>
/// Spectate's private bytes at each replay session end, and whether they are above the ceiling
/// or rising session after session (#399). Each parsed replay outlives its session, so it reaches
/// gen2, and the workstation GC can go more than one session without a gen2 collection: on
/// ASA-SERVER on 2026-10-09 private bytes went 597 to 913 MB across a session with no gen2
/// collection while the live heap stayed about 350 MB. So a session end counts toward a run only
/// when a gen2 collection ran since the last one, and a counted drop starts the run over.
/// </summary>
public sealed class SpectateMemoryTrend
{
    /// <summary>A run of rises smaller than this in all is GC noise, not growth.</summary>
    public const long MinimumGrowthBytes = 128L * 1024 * 1024;

    private readonly long ceiling;
    private readonly int growthRun;
    private long? lastCounted;
    private int? lastGen2;
    private long runStart;
    private int rising;

    public SpectateMemoryTrend(ServiceHealthSettings settings)
    {
        settings ??= new ServiceHealthSettings();
        ceiling = settings.SpectatePrivateBytesCeiling();
        growthRun = settings.SpectateMemoryGrowthRun();
    }

    /// <param name="privateBytes">Spectate's private bytes at this session end.</param>
    /// <param name="gen2Collections">The process's gen2 collections so far (<see cref="GC.CollectionCount"/>).</param>
    public SpectateMemoryVerdict Add(long privateBytes, int gen2Collections)
    {
        // The first sample is the baseline. After it, only a sample with a gen2 collection
        // since the last one says what the collector kept.
        bool counted = lastGen2 is not int previousGen2 || gen2Collections > previousGen2;
        lastGen2 = gen2Collections;
        if (counted)
        {
            if (lastCounted is long previous && privateBytes > previous)
            {
                rising++;
            }
            else
            {
                rising = 0;
                runStart = privateBytes;
            }

            lastCounted = privateBytes;
        }

        long growth = counted ? privateBytes - runStart : 0;
        return new SpectateMemoryVerdict
        {
            AboveCeiling = privateBytes > ceiling,
            CeilingBytes = ceiling,
            AfterCollection = counted,
            RisingSessions = rising,
            GrowthBytes = growth,
            SustainedGrowth = counted && rising >= growthRun && growth >= MinimumGrowthBytes,
        };
    }
}

/// <summary>
/// One INF per spectate replay session end with spectate's own memory, and a WRN when it is above
/// <c>ServiceHealth:SpectatePrivateBytesWarn</c> or kept rising (#399). A warning only: nothing
/// restarts spectate for it.
/// </summary>
public sealed class SpectateMemoryLog
{
    private const long Megabyte = 1024 * 1024;

    private readonly SpectateMemoryTrend trend;
    private readonly Func<SpectateMemorySample> read;
    private readonly ILogger logger;
    private int sessions;

    public SpectateMemoryLog(AppSettings settings, ILogger<SpectateMemoryLog> logger)
        : this(settings?.ServiceHealth, SpectateMemorySample.ReadCurrent, logger) { }

    internal SpectateMemoryLog(
        ServiceHealthSettings settings,
        Func<SpectateMemorySample> read,
        ILogger logger
    )
    {
        trend = new SpectateMemoryTrend(settings);
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Logs spectate's memory for the session that just ended. Null when it could not be read.</summary>
    public SpectateMemoryVerdict SessionEnded(int? replayId, string outcome)
    {
        SpectateMemorySample sample;
        try
        {
            sample = read();
        }
        catch (Exception e)
            when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(e, "Could not read spectate's memory at the session end.");
            return null;
        }

        sessions++;
        SpectateMemoryVerdict verdict = trend.Add(sample.PrivateBytes, sample.Gen2Collections);
        logger.LogInformation(
            "Spectate memory after session {Session} (replay {ReplayId}, {Outcome}): {PrivateMegabytes} MB private, {WorkingSetMegabytes} MB working set, {ManagedHeapMegabytes} MB managed heap ({LargeObjectHeapMegabytes} MB large objects, {FragmentedMegabytes} MB fragmented), {GcCommittedMegabytes} MB GC committed, {OtherPrivateMegabytes} MB other private, {Gen2Collections} gen2 collections.",
            sessions,
            replayId,
            outcome,
            sample.PrivateBytes / Megabyte,
            sample.WorkingSetBytes / Megabyte,
            sample.ManagedHeapBytes / Megabyte,
            sample.LargeObjectHeapBytes / Megabyte,
            sample.FragmentedBytes / Megabyte,
            sample.GcCommittedBytes / Megabyte,
            sample.OtherPrivateBytes / Megabyte,
            sample.Gen2Collections
        );
        if (verdict.AboveCeiling)
        {
            logger.LogWarning(
                "Spectate private bytes are {PrivateMegabytes} MB, above ServiceHealth:SpectatePrivateBytesWarn ({CeilingMegabytes} MB). This is a warning only; spectate is not restarted.",
                sample.PrivateBytes / Megabyte,
                verdict.CeilingBytes / Megabyte
            );
        }

        if (verdict.SustainedGrowth)
        {
            logger.LogWarning(
                "Spectate private bytes rose at {RisingSessions} session ends in a row, each after a gen2 collection, by {GrowthMegabytes} MB to {PrivateMegabytes} MB (ServiceHealth:SpectateMemoryGrowthSessions). This is a warning only; spectate is not restarted.",
                verdict.RisingSessions,
                verdict.GrowthBytes / Megabyte,
                sample.PrivateBytes / Megabyte
            );
        }

        return verdict;
    }
}
