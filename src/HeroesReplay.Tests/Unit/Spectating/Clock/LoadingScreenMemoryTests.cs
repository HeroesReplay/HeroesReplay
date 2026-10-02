using System;
using System.Collections.Generic;
using HeroesReplay.Core.Spectating.Clock.Memory;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Clock;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class LoadingScreenMemoryTests
{
    private const long ModuleBase = 0x140000000L;
    private const long SectionRva = 0x1000;
    private const int SectionSize = 0x400;
    private const long GlobalRva = 0x9000;
    private const long ModuleSize = 0x20000;
    private const long State = 0x2_0000_0000L;
    private const long Screen = 0x3_0000_0000L;

    [Fact]
    public void Find_ReadsTheGlobalWhenBothLoadsNameIt()
    {
        byte[] code = Site(0x100, GlobalRva, GlobalRva);

        List<long> globals = LoadingScreenPattern.Find(code, 0x100);

        Assert.Equal(new[] { GlobalRva }, globals);
    }

    [Fact]
    public void Find_IgnoresASiteWhoseLoadsNameDifferentGlobals()
    {
        byte[] code = Site(0x100, GlobalRva, GlobalRva + 8);

        Assert.Empty(LoadingScreenPattern.Find(code, 0x100));
    }

    [Fact]
    public void TryAgree_NeedsThreeSitesAndTwiceTheRunnerUp()
    {
        Assert.False(LoadingScreenPattern.TryAgree(new long[] { 1, 1 }, out _, out _));
        Assert.False(LoadingScreenPattern.TryAgree(new long[] { 1, 1, 1, 2, 2 }, out _, out _));

        bool agreed = LoadingScreenPattern.TryAgree(
            new long[] { 2, 1, 1, 1, 1, 1, 1 },
            out long rva,
            out int sites
        );

        Assert.True(agreed);
        Assert.Equal(1, rva);
        Assert.Equal(6, sites);
    }

    [Fact]
    public void Read_FollowsTheClientFromBootSplashToMenuToMapLoadingToMatch()
    {
        // 2026-10-02 on 2.57.0.98304: boot splash 1, home 0, map loading 1, match null.
        FakeClient client = FakeClient.WithSites(3);
        using var memory = new LoadingScreenMemory();
        StableClockModule module = Module(41);

        client.ShowScreen(loading: true);
        LoadingScreenSample boot = memory.Read(module, client.Read);
        client.ShowScreen(loading: false);
        LoadingScreenSample home = memory.Read(module, client.Read);
        client.ShowScreen(loading: true);
        LoadingScreenSample map = memory.Read(module, client.Read);
        client.EnterMatch();
        LoadingScreenSample match = memory.Read(module, client.Read);

        Assert.Equal(GlobalRva, memory.GlobalRva);
        Assert.Equal(ClientScreen.Loading, boot.Screen);
        Assert.Null(boot.MapLoading);
        Assert.Equal(ClientScreen.Menu, home.Screen);
        Assert.False(home.MapLoading);
        Assert.Equal(ClientScreen.Loading, map.Screen);
        Assert.True(map.MapLoading);
        Assert.Equal(ClientScreen.Match, match.Screen);
        Assert.False(match.MapLoading);
        Assert.Null(boot.OnMenu);
        Assert.True(home.OnMenu);
        Assert.False(map.OnMenu);
        Assert.False(match.OnMenu);
    }

    [Fact]
    public void Read_ANewClientProcessMustShowAMenuAgainBeforeLoadingCounts()
    {
        FakeClient client = FakeClient.WithSites(3);
        using var memory = new LoadingScreenMemory();
        client.ShowScreen(loading: false);
        memory.Read(Module(42), client.Read);

        client.ShowScreen(loading: true);
        LoadingScreenSample next = memory.Read(Module(43), client.Read);

        Assert.False(next.MenuSeen);
        Assert.Null(next.MapLoading);
    }

    [Fact]
    public void Read_NoPatternYet_CannotTellAndScansAgainLater()
    {
        FakeClient client = FakeClient.WithSites(0);
        using var memory = new LoadingScreenMemory();
        DateTimeOffset now = new(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);
        memory.UtcNow = () => now;
        StableClockModule module = Module(44);
        client.ShowScreen(loading: false);

        LoadingScreenSample unpacking = memory.Read(module, client.Read);
        client.AddSites(3);
        LoadingScreenSample tooSoon = memory.Read(module, client.Read);
        now = now.AddSeconds(11);
        LoadingScreenSample found = memory.Read(module, client.Read);

        Assert.Equal("unsupported-build", unpacking.Reason);
        Assert.Null(unpacking.MapLoading);
        Assert.Equal(ClientScreen.Unknown, tooSoon.Screen);
        Assert.Equal(ClientScreen.Menu, found.Screen);
        Assert.False(found.MapLoading);
    }

    [Fact]
    public void Read_APointerThatIsNotAUserAddress_CannotTell()
    {
        FakeClient client = FakeClient.WithSites(3);
        using var memory = new LoadingScreenMemory();
        client.WritePointer(ModuleBase + GlobalRva, 0x1234);

        LoadingScreenSample sample = memory.Read(Module(45), client.Read);

        Assert.Equal(ClientScreen.Unknown, sample.Screen);
        Assert.Null(sample.MapLoading);
    }

    private static StableClockModule Module(int pid) =>
        new(pid, ModuleBase, ModuleSize, "2.57.0.98304");

    private static byte[] Site(long siteRva, long firstGlobal, long secondGlobal)
    {
        byte[] b = new byte[LoadingScreenPattern.Width];
        b[0] = 0x48;
        b[1] = 0x8B;
        b[2] = 0x0D;
        BitConverter.GetBytes(checked((int)(firstGlobal - siteRva - 7))).CopyTo(b, 3);
        b[7] = 0x48;
        b[8] = 0x85;
        b[9] = 0xC9;
        b[10] = 0x74;
        b[11] = 0x25;
        b[12] = 0x33;
        b[13] = 0xD2;
        b[14] = 0xE8;
        b[19] = 0x84;
        b[20] = 0xC0;
        b[21] = 0x74;
        b[22] = 0x1A;
        b[23] = 0x48;
        b[24] = 0x8B;
        b[25] = 0x0D;
        BitConverter.GetBytes(checked((int)(secondGlobal - siteRva - 30))).CopyTo(b, 26);
        b[30] = 0xE8;
        return b;
    }

    private sealed class FakeClient
    {
        private readonly Dictionary<long, byte> bytes = new();

        public static FakeClient WithSites(int sites)
        {
            var client = new FakeClient();
            client.Write(ModuleBase, new byte[] { (byte)'M', (byte)'Z' });
            client.Write(ModuleBase + 0x3C, BitConverter.GetBytes(0x80));
            client.Write(ModuleBase + 0x80, new byte[] { (byte)'P', (byte)'E', 0, 0 });
            client.Write(ModuleBase + 0x80 + 6, BitConverter.GetBytes((ushort)1));
            client.Write(ModuleBase + 0x80 + 20, BitConverter.GetBytes((ushort)0));
            long section = ModuleBase + 0x80 + 24;
            client.Write(section + 8, BitConverter.GetBytes(SectionSize));
            client.Write(section + 12, BitConverter.GetBytes((uint)SectionRva));
            client.Write(section + 36, BitConverter.GetBytes(0x20000000u));
            client.AddSites(sites);
            client.WritePointer(ModuleBase + GlobalRva, State);
            return client;
        }

        public void AddSites(int sites)
        {
            for (int i = 0; i < sites; i++)
            {
                long rva = SectionRva + (i * 0x40);
                Write(ModuleBase + rva, Site(rva, GlobalRva, GlobalRva));
            }
        }

        public void ShowScreen(bool loading)
        {
            WritePointer(State + LoadingScreenMemory.ScreenOffset, Screen);
            Write(
                Screen + LoadingScreenMemory.FlagsOffset,
                new byte[] { loading ? (byte)123 : (byte)122 }
            );
        }

        public void EnterMatch() => WritePointer(State + LoadingScreenMemory.ScreenOffset, 0);

        public void WritePointer(long address, long value) =>
            Write(address, BitConverter.GetBytes(value));

        public bool Read(long address, byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = bytes.TryGetValue(address + i, out byte value) ? value : (byte)0;
            }

            return true;
        }

        private void Write(long address, byte[] value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                bytes[address + i] = value[i];
            }
        }
    }
}
