using System;
using System.Diagnostics;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Shared;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ProcessTableTests
{
    [Fact]
    public void Find_ThisProcess_HasItsStartTime()
    {
        using Process self = Process.GetCurrentProcess();

        ProcessTableEntry entry = ProcessTable.Find(Environment.ProcessId);

        Assert.NotNull(entry);
        Assert.Equal(Environment.ProcessId, entry.Pid);
        Assert.False(string.IsNullOrWhiteSpace(entry.Name));
        Assert.NotNull(entry.StartTime);
        Assert.True(
            (entry.StartTime.Value - new DateTimeOffset(self.StartTime)).Duration()
                < TimeSpan.FromSeconds(1)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Find_NoSuchPid_IsNull(int pid) => Assert.Null(ProcessTable.Find(pid));
}
