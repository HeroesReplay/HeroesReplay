using System;
using System.Collections.Generic;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StableMatchClockTests
{
    private const long ModuleBase = 0x140000000L;
    private const long SectionRva = 0x1000;
    private const int SectionSize = 0x100;
    private const long PatternTickRva = 0x9000;
    private const long PatternSpeedRva = 0xA000;
    private const long LargeModule = 0x3400000;
    private const long SmallModule = 0x20000;
    private const float Scale = 1f / 4096f;

    [Fact]
    public void Read_Non98025Version_UsesPatternPath()
    {
        MappedModule memory = MappedModule.WithPattern();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(11, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 12);

        StableClockSample first = clock.Read(module, memory.Read);

        Assert.False(first.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("confirming", first.Reason);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 13);
        StableClockSample locked = clock.Read(module, memory.Read);

        Assert.True(locked.Ok);
        Assert.True(clock.IsLocked);
        Assert.Equal(13, locked.Seconds, precision: 2);
        Assert.Equal(PatternTickRva, clock.CandidateTickRva);
        Assert.NotEqual(MatchTickClock.MatchTickRva, clock.CandidateTickRva);
        Assert.Equal(0, memory.FixedTickReads);
        Assert.True(memory.WideReads > 0);
    }

    [Fact]
    public void Read_Build98025_PrefersAgreedPatternOverFixedRvas()
    {
        MappedModule memory = MappedModule.WithPattern();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(12, LargeModule, MatchTickClock.SupportedBuild);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 12);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 50);

        Assert.False(clock.Read(module, memory.Read).Ok);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 13);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 51);
        StableClockSample locked = clock.Read(module, memory.Read);

        Assert.True(locked.Ok);
        Assert.Equal(13, locked.Seconds, precision: 2);
        Assert.Equal(PatternTickRva, clock.CandidateTickRva);
        Assert.Equal(0, memory.FixedTickReads);
    }

    [Fact]
    public void Read_FixedRva_IsValidatedBeforeLock()
    {
        MappedModule memory = MappedModule.Empty();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(13, LargeModule, MatchTickClock.SupportedBuild);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 15);

        StableClockSample first = clock.Read(module, memory.Read);

        Assert.False(first.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("confirming", first.Reason);
        Assert.Equal(15 * 4096, first.Ticks);
        Assert.Equal(MatchTickClock.MatchTickRva, clock.CandidateTickRva);
        Assert.True(memory.WideReads > 0);
        Assert.True(memory.FixedTickReads > 0);
        int scans = memory.WideReads;
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 16);
        StableClockSample locked = clock.Read(module, memory.Read);

        Assert.True(locked.Ok);
        Assert.True(clock.IsLocked);
        Assert.Equal(16, locked.Seconds, precision: 2);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_FixedRva_OutsideModule_DoesNotLock()
    {
        MappedModule memory = MappedModule.Empty();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(14, SmallModule, MatchTickClock.SupportedBuild);
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 15);
        StableClockSample first = clock.Read(module, memory.Read);
        int scans = memory.WideReads;
        memory.SetSeconds(MatchTickClock.MatchTickRva, MatchTickClock.GameSpeedFactorRva, 16);
        StableClockSample second = clock.Read(module, memory.Read);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("out-of-range", first.Reason);
        Assert.Equal("out-of-range", second.Reason);
        Assert.Equal(0, memory.FixedTickReads);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_IncoherentSample_DoesNotLock()
    {
        MappedModule memory = MappedModule.WithPattern();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(15, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 30);
        StableClockSample first = clock.Read(module, memory.Read);
        int scans = memory.WideReads;
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 10);
        StableClockSample second = clock.Read(module, memory.Read);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.Equal("incoherent", second.Reason);
        Assert.False(clock.IsLocked);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_InvalidSample_DoesNotLock()
    {
        MappedModule memory = MappedModule.WithPattern();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(16, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 12);
        memory.SetSingle(PatternSpeedRva, float.NaN);
        StableClockSample first = clock.Read(module, memory.Read);
        int scans = memory.WideReads;
        StableClockSample second = clock.Read(module, memory.Read);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.Equal("bad-scale", first.Reason);
        Assert.Equal("bad-scale", second.Reason);
        Assert.False(clock.IsLocked);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_CachedFingerprint_DoesNotScanAgain()
    {
        MappedModule memory = MappedModule.WithPattern();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(17, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 20);
        Assert.False(clock.Read(module, memory.Read).Ok);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 21);
        StableClockSample locked = clock.Read(module, memory.Read);
        Assert.True(locked.Ok);
        int scans = memory.WideReads;

        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 22);
        StableClockSample again = clock.Read(module, memory.Read);

        Assert.True(again.Ok);
        Assert.True(clock.IsLocked);
        Assert.Equal(22, again.Seconds, precision: 2);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_FailedDiscovery_DoesNotScanAgain()
    {
        MappedModule memory = MappedModule.Empty();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule module = Module(18, SmallModule, "2.55.17.97771");
        StableClockSample first = clock.Read(module, memory.Read);
        int scans = memory.WideReads;
        StableClockSample second = clock.Read(module, memory.Read);

        Assert.False(first.Ok);
        Assert.False(second.Ok);
        Assert.False(clock.IsLocked);
        Assert.Equal("unsupported-build", first.Reason);
        Assert.Equal("unsupported-build", second.Reason);
        Assert.True(scans > 0);
        Assert.Equal(scans, memory.WideReads);
    }

    [Fact]
    public void Read_ProcessChange_ResetsDiscovery()
    {
        MappedModule memory = MappedModule.WithPattern();
        using StableMatchClock clock = new StableMatchClock();
        StableClockModule firstProcess = Module(19, SmallModule, "2.55.17.97771");
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 20);
        Assert.False(clock.Read(firstProcess, memory.Read).Ok);
        memory.SetSeconds(PatternTickRva, PatternSpeedRva, 21);
        Assert.True(clock.Read(firstProcess, memory.Read).Ok);
        Assert.True(clock.IsLocked);
        int scans = memory.WideReads;

        StableClockModule nextProcess = Module(20, SmallModule, "2.55.17.97771");
        StableClockSample restarted = clock.Read(nextProcess, memory.Read);

        Assert.False(restarted.Ok);
        Assert.False(clock.IsLocked);
        Assert.True(memory.WideReads > scans);
    }

    private static StableClockModule Module(int pid, long size, string version)
    {
        return new StableClockModule(pid, ModuleBase, size, version);
    }

    private sealed class MappedModule
    {
        private readonly Dictionary<long, byte> bytes = new Dictionary<long, byte>();

        public int WideReads { get; private set; }

        public int FixedTickReads { get; private set; }

        public static MappedModule WithPattern()
        {
            MappedModule module = Empty();
            int tickDisp = checked((int)(PatternTickRva - SectionRva - MatchClockPattern.MovdEnd));
            int speedDisp = checked(
                (int)(PatternSpeedRva - SectionRva - MatchClockPattern.MulssEnd)
            );
            module.Write(SectionRva, Pattern(tickDisp, speedDisp));
            return module;
        }

        public static MappedModule Empty()
        {
            MappedModule module = new MappedModule();
            module.Write(0, new byte[] { (byte)'M', (byte)'Z' });
            module.WriteInt32(0x3C, 0x80);
            module.Write(0x80, new byte[] { (byte)'P', (byte)'E', 0, 0 });
            module.WriteUInt16(0x80 + 6, 1);
            module.WriteUInt16(0x80 + 20, 0);
            long section = 0x80 + 24;
            module.WriteInt32(section + 8, SectionSize);
            module.WriteUInt32(section + 12, (uint)SectionRva);
            module.WriteUInt32(section + 36, 0x20000000);
            return module;
        }

        public void SetSeconds(long tickRva, long speedRva, int seconds)
        {
            WriteInt32(tickRva, seconds * 4096);
            SetSingle(speedRva, Scale);
        }

        public void SetSingle(long rva, float value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        public bool Read(long address, byte[] buffer)
        {
            if (address <= 0 || buffer == null || buffer.Length == 0)
            {
                return false;
            }

            if (buffer.Length > 16)
            {
                WideReads++;
            }

            long fixedTick = ModuleBase + MatchTickClock.MatchTickRva;
            if (address < fixedTick + 4 && address + buffer.Length > fixedTick)
            {
                FixedTickReads++;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                byte value;
                buffer[i] = bytes.TryGetValue(address + i, out value) ? value : (byte)0;
            }

            return true;
        }

        private void WriteInt32(long rva, int value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        private void WriteUInt16(long rva, ushort value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        private void WriteUInt32(long rva, uint value)
        {
            Write(rva, BitConverter.GetBytes(value));
        }

        private void Write(long rva, byte[] value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                bytes[ModuleBase + rva + i] = value[i];
            }
        }

        private static byte[] Pattern(int tickDisplacement, int speedDisplacement)
        {
            byte[] bytes = new byte[MatchClockPattern.MulssEnd];
            bytes[0] = 0x84;
            bytes[1] = 0xC0;
            bytes[2] = 0x74;
            bytes[3] = 0x0A;
            bytes[4] = 0x66;
            bytes[5] = 0x0F;
            bytes[6] = 0x6E;
            bytes[7] = 0x05;
            BitConverter
                .GetBytes(tickDisplacement)
                .CopyTo(bytes, MatchClockPattern.TickDisplacement);
            bytes[12] = 0x0F;
            bytes[13] = 0x5B;
            bytes[14] = 0xC0;
            bytes[15] = 0xF3;
            bytes[16] = 0x0F;
            bytes[17] = 0x59;
            bytes[18] = 0x05;
            BitConverter
                .GetBytes(speedDisplacement)
                .CopyTo(bytes, MatchClockPattern.SpeedDisplacement);
            return bytes;
        }
    }
}
