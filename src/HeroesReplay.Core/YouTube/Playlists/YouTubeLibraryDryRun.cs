using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// The library dry run's report (<c>YouTubeLibrary.DryRunFileName</c>): what a live pass would
/// insert and look up. Its names are the ones the report had as an anonymous type.
/// </summary>
internal sealed record YouTubeLibraryDryRun(
    bool Simulated,
    int Recorded,
    int Planned,
    int InsertUnits,
    IReadOnlyList<YouTubeLibraryDryRunPlaylist> Playlists,
    IReadOnlyList<YouTubeLibraryItem> Items,
    YouTubeLibraryDryRunDiscovery Discovery,
    YouTubeLibraryDryRunUnits Units
);

internal sealed record YouTubeLibraryDryRunPlaylist(string Title, int Videos);

internal sealed record YouTubeLibraryDryRunDiscovery(
    bool WouldList,
    bool UploadsPlaylistKnown,
    bool ListedToEnd,
    int KnownVideos,
    int Unresolved,
    int HeroesProfileLookups,
    IReadOnlyList<YouTubeLibraryDryRunLookup> WouldResolve
);

internal sealed record YouTubeLibraryDryRunLookup(string VideoId, int? ReplayId);

internal sealed record YouTubeLibraryDryRunUnits(
    DateTimeOffset QuotaDay,
    int UploadCalls,
    int LibraryUnits,
    int LibraryRoom
);
