using System;
using System.IO;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Control;
using HeroesReplay.Core.Telemetry;
using Xunit;

namespace HeroesReplay.Tests.Unit.Shared;

/// <summary>
/// #331: a test's temp file used to take the same session-wide mutex as the running stack's
/// file (<c>Local\HeroesReplay.PanelRequests</c>, <c>Local\HeroesReplay.ReplaySessions</c>), so
/// unit tests waited on spectate and on each other's runs.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FileMutexNameTests
{
    private const string Shared = @"Local\HeroesReplay.Example";
    private static readonly string SharedPath = Path.Combine(
        Path.GetTempPath(),
        "hr-mutex-shared",
        "example.json"
    );

    [Fact]
    public void TheSharedFile_KeepsTheNameEveryReleaseUses()
    {
        Assert.Equal(Shared, FileMutexName.For(Shared, SharedPath, SharedPath));
        Assert.Equal(Shared, FileMutexName.For(Shared, SharedPath, null));
        Assert.Equal(Shared, FileMutexName.For(Shared, SharedPath, SharedPath.ToUpperInvariant()));
    }

    [Fact]
    public void AnyOtherFile_HasALockOfItsOwn_TheSameForEveryProcess()
    {
        string one = Path.Combine(Path.GetTempPath(), "hr-mutex-" + Guid.NewGuid().ToString("N"));
        string two = Path.Combine(Path.GetTempPath(), "hr-mutex-" + Guid.NewGuid().ToString("N"));

        string name = FileMutexName.For(Shared, SharedPath, one);

        Assert.StartsWith(Shared + ".", name, StringComparison.Ordinal);
        Assert.NotEqual(name, FileMutexName.For(Shared, SharedPath, two));
        Assert.Equal(name, FileMutexName.For(Shared, SharedPath, one.ToUpperInvariant()));
        Assert.Equal(
            name,
            FileMutexName.For(Shared, SharedPath, Path.Combine(one, "..", Path.GetFileName(one)))
        );
    }

    [Fact]
    public void PanelRequestsAndReplaySessions_LockTheirDefaultFileAsBefore()
    {
        Assert.Equal(
            ObserverPanelRequests.SharedMutexName,
            ObserverPanelRequests.MutexNameFor(ObserverPanelRequests.DefaultPath())
        );
        Assert.Equal(@"Local\HeroesReplay.PanelRequests", ObserverPanelRequests.SharedMutexName);
        Assert.Equal(
            ReplaySessionFile.SharedMutexName,
            ReplaySessionFile.MutexNameFor(ReplaySessionFile.SharedPath)
        );
        Assert.Equal(@"Local\HeroesReplay.ReplaySessions", ReplaySessionFile.SharedMutexName);

        string temp = Path.Combine(Path.GetTempPath(), "hr-panels-" + Guid.NewGuid().ToString("N"));
        Assert.NotEqual(
            ObserverPanelRequests.SharedMutexName,
            ObserverPanelRequests.MutexNameFor(temp)
        );
        Assert.NotEqual(ReplaySessionFile.SharedMutexName, ReplaySessionFile.MutexNameFor(temp));
    }
}
