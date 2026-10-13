using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Reads another process's command line, so <c>services stop</c> can tell a hand-started
/// <c>spectate</c> from the other heroesreplay commands of the same install (#381). It opens the
/// process for limited query only, and returns null for one it cannot read.
/// </summary>
public static class ProcessCommandLine
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    private const int StatusBufferOverflow = unchecked((int)0x80000005);

    public static string TryRead(int pid)
    {
        if (pid <= 4)
        {
            return null;
        }

        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            int size = 1024;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int status = NtQueryInformationProcess(
                        process,
                        ProcessCommandLineInformation,
                        buffer,
                        size,
                        out int needed
                    );
                    if (
                        status
                        is StatusInfoLengthMismatch
                            or StatusBufferTooSmall
                            or StatusBufferOverflow
                    )
                    {
                        size = Math.Max(needed, size * 2);
                        continue;
                    }

                    if (status < 0)
                    {
                        return null;
                    }

                    // The buffer starts with a UNICODE_STRING whose Buffer points into it.
                    var text = Marshal.PtrToStructure<UnicodeString>(buffer);
                    return text.Buffer == IntPtr.Zero || text.Length == 0
                        ? null
                        : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return null;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>
    /// The arguments after the executable in <paramref name="commandLine"/>, split on white space
    /// outside double quotes, quotes removed. Enough to read <c>spectate file --path "C:\x y"</c>.
    /// </summary>
    public static string[] Arguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        bool inToken = false;
        foreach (char c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
                inToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }

                continue;
            }

            current.Append(c);
            inToken = true;
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens.Count <= 1
            ? Array.Empty<string>()
            : tokens.GetRange(1, tokens.Count - 1).ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process,
        int informationClass,
        IntPtr information,
        int length,
        out int returnLength
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
