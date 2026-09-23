using System;
using System.Collections.Generic;
using System.IO;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// The 98025 match clock is `movd` of the tick global, `cvtdq2ps`, then `mulss` by the 1/4096
/// speed global. The opcodes stay put when a patch moves those globals. Two sites must agree.
/// </summary>
public static class MatchClockPattern
{
    private static readonly byte[] Pattern =
    {
        0x84,
        0xC0,
        0x74,
        0x00,
        0x66,
        0x0F,
        0x6E,
        0x05,
        0x00,
        0x00,
        0x00,
        0x00,
        0x0F,
        0x5B,
        0xC0,
        0xF3,
        0x0F,
        0x59,
        0x05,
        0x00,
        0x00,
        0x00,
        0x00,
    };

    private static readonly byte[] Mask =
    {
        1,
        1,
        1,
        0,
        1,
        1,
        1,
        1,
        0,
        0,
        0,
        0,
        1,
        1,
        1,
        1,
        1,
        1,
        1,
        0,
        0,
        0,
        0,
    };

    public const int TickDisplacement = 8;
    public const int SpeedDisplacement = 19;
    public const int MovdEnd = 12;
    public const int MulssEnd = 23;
    private const uint Executable = 0x20000000;

    public readonly record struct Site(long TickRva, long SpeedRva);

    public readonly record struct Section(
        long VirtualAddress,
        int VirtualSize,
        int RawPointer,
        int RawSize
    );

    public static List<Site> Find(ReadOnlySpan<byte> bytes, long byteRva)
    {
        var sites = new List<Site>();
        int width = Pattern.Length;
        if (bytes.Length < width)
        {
            return sites;
        }

        for (int i = 0; i + width <= bytes.Length; i++)
        {
            if (!Matches(bytes, i))
            {
                continue;
            }

            int tickDisp = BitConverter.ToInt32(bytes.Slice(i + TickDisplacement, 4));
            int speedDisp = BitConverter.ToInt32(bytes.Slice(i + SpeedDisplacement, 4));
            long site = byteRva + i;
            sites.Add(new Site(site + MovdEnd + tickDisp, site + MulssEnd + speedDisp));
        }

        return sites;
    }

    public static bool TryAgree(IReadOnlyList<Site> sites, out long tickRva, out long speedRva)
    {
        tickRva = 0;
        speedRva = 0;
        if (sites == null || sites.Count == 0)
        {
            return false;
        }

        tickRva = sites[0].TickRva;
        speedRva = sites[0].SpeedRva;
        if (tickRva <= 0 || speedRva <= 0)
        {
            return false;
        }

        for (int i = 1; i < sites.Count; i++)
        {
            if (sites[i].TickRva != tickRva || sites[i].SpeedRva != speedRva)
            {
                tickRva = 0;
                speedRva = 0;
                return false;
            }
        }

        return true;
    }

    public static bool TryResolveFile(
        string path,
        out long tickRva,
        out long speedRva,
        out int sites
    )
    {
        tickRva = 0;
        speedRva = 0;
        sites = 0;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        byte[] file = File.ReadAllBytes(path);
        if (!TryExecutableSections(file, out List<Section> sections))
        {
            return false;
        }

        var found = new List<Site>();
        foreach (Section section in sections)
        {
            int size = section.RawSize > 0 ? section.RawSize : section.VirtualSize;
            if (section.VirtualSize > 0 && section.VirtualSize < size)
            {
                size = section.VirtualSize;
            }

            if (size <= 0 || section.RawPointer < 0 || section.RawPointer + size > file.Length)
            {
                continue;
            }

            found.AddRange(Find(file.AsSpan(section.RawPointer, size), section.VirtualAddress));
        }

        sites = found.Count;
        return TryAgree(found, out tickRva, out speedRva);
    }

    public static bool TryExecutableSections(ReadOnlySpan<byte> headers, out List<Section> sections)
    {
        sections = new List<Section>();
        if (headers.Length < 0x40 || headers[0] != (byte)'M' || headers[1] != (byte)'Z')
        {
            return false;
        }

        int lfanew = BitConverter.ToInt32(headers.Slice(0x3C, 4));
        if (lfanew <= 0 || lfanew + 24 > headers.Length)
        {
            return false;
        }

        if (
            headers[lfanew] != (byte)'P'
            || headers[lfanew + 1] != (byte)'E'
            || headers[lfanew + 2] != 0
            || headers[lfanew + 3] != 0
        )
        {
            return false;
        }

        int sectionCount = BitConverter.ToUInt16(headers.Slice(lfanew + 6, 2));
        int optionalSize = BitConverter.ToUInt16(headers.Slice(lfanew + 20, 2));
        int table = lfanew + 24 + optionalSize;
        if (sectionCount <= 0 || table + sectionCount * 40 > headers.Length)
        {
            return false;
        }

        for (int i = 0; i < sectionCount; i++)
        {
            int at = table + i * 40;
            uint characteristics = BitConverter.ToUInt32(headers.Slice(at + 36, 4));
            if ((characteristics & Executable) == 0)
            {
                continue;
            }

            sections.Add(
                new Section(
                    BitConverter.ToUInt32(headers.Slice(at + 12, 4)),
                    BitConverter.ToInt32(headers.Slice(at + 8, 4)),
                    BitConverter.ToInt32(headers.Slice(at + 20, 4)),
                    BitConverter.ToInt32(headers.Slice(at + 16, 4))
                )
            );
        }

        return sections.Count > 0;
    }

    private static bool Matches(ReadOnlySpan<byte> bytes, int at)
    {
        for (int j = 0; j < Pattern.Length; j++)
        {
            if (Mask[j] != 0 && bytes[at + j] != Pattern[j])
            {
                return false;
            }
        }

        return true;
    }
}
