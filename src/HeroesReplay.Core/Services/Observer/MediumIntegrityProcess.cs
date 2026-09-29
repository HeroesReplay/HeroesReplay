using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// An elevated HeroesSwitcher cannot see the medium-integrity Battle.net session,
/// so the client stops on the login screen. Start the replay from a medium token.
/// </summary>
public static class MediumIntegrityProcess
{
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint TokenAdjustDefault = 0x0080;
    private const uint TokenAdjustSessionId = 0x0100;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint PrimaryTokenAccess =
        TokenAssignPrimary
        | TokenDuplicate
        | TokenQuery
        | TokenAdjustDefault
        | TokenAdjustSessionId;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int TokenElevationClass = 20;
    private const uint PrivilegeEnabled = 0x00000002;

    public static bool IsCurrentProcessElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out IntPtr token))
        {
            return false;
        }

        try
        {
            return IsTokenElevated(token);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static string Start(string fileName, string arguments, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        if (!IsCurrentProcessElevated())
        {
            StartDirect(fileName, arguments, workingDirectory);
            return "direct";
        }

        EnableLaunchPrivileges();
        string error = TryStartUnelevated(fileName, arguments, workingDirectory, out string source);
        if (error != null)
        {
            throw new InvalidOperationException(error);
        }

        return source;
    }

    private static void StartDirect(string fileName, string arguments, string workingDirectory)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? string.Empty
                    : workingDirectory,
                UseShellExecute = false,
            }
        );
    }

    private static string TryStartUnelevated(
        string fileName,
        string arguments,
        string workingDirectory,
        out string source
    )
    {
        source = null;
        IntPtr raw = IntPtr.Zero;
        IntPtr primary = IntPtr.Zero;
        try
        {
            string opened = OpenMediumToken(out raw, out string tokenSource);
            if (opened != null)
            {
                return opened;
            }

            source = tokenSource;
            if (!TryDuplicatePrimary(raw, out primary, out int duplicateError))
            {
                return "DuplicateTokenEx failed: " + new Win32Exception(duplicateError).Message;
            }

            IntPtr launch = primary;
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            var command = new StringBuilder(fileName.Length + (arguments?.Length ?? 0) + 8);
            command.Append('"').Append(fileName).Append('"');
            if (!string.IsNullOrEmpty(arguments))
            {
                command.Append(' ').Append(arguments);
            }

            command.EnsureCapacity(command.Length + 1);
            string directory = string.IsNullOrWhiteSpace(workingDirectory)
                ? null
                : workingDirectory;
            if (
                !TryCreateProcess(
                    launch,
                    fileName,
                    command,
                    directory,
                    ref startup,
                    out ProcessInformation process,
                    out int error
                )
                && !TryCreateProcess(
                    launch,
                    fileName,
                    command,
                    null,
                    ref startup,
                    out process,
                    out error
                )
            )
            {
                return "CreateProcessWithTokenW failed: "
                    + new Win32Exception(error).Message
                    + " file="
                    + fileName
                    + " dir="
                    + (directory ?? "(null)");
            }

            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
            return null;
        }
        finally
        {
            if (primary != IntPtr.Zero)
            {
                CloseHandle(primary);
            }

            if (raw != IntPtr.Zero)
            {
                CloseHandle(raw);
            }
        }
    }

    private static bool TryCreateProcess(
        IntPtr token,
        string fileName,
        StringBuilder command,
        string directory,
        ref StartupInfo startup,
        out ProcessInformation process,
        out int error
    )
    {
        if (
            CreateProcessWithTokenW(
                token,
                0,
                fileName,
                command,
                0,
                IntPtr.Zero,
                directory,
                ref startup,
                out process
            )
        )
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static bool TryDuplicatePrimary(IntPtr raw, out IntPtr primary, out int error)
    {
        primary = IntPtr.Zero;
        if (DuplicateTokenEx(raw, PrimaryTokenAccess, IntPtr.Zero, 2, 1, out IntPtr created))
        {
            primary = created;
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        if (DuplicateTokenEx(raw, PrimaryTokenAccess, IntPtr.Zero, 3, 1, out created))
        {
            primary = created;
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static string OpenMediumToken(out IntPtr raw, out string source)
    {
        raw = OpenProcessTokenFromMediumProcess("Battle.net", out source);
        if (raw == IntPtr.Zero)
        {
            raw = OpenShellToken();
            source = "explorer";
        }

        if (raw == IntPtr.Zero)
        {
            source = null;
            return "No medium-integrity token was found. The replay was not started elevated.";
        }

        if (IsTokenElevated(raw))
        {
            CloseHandle(raw);
            raw = IntPtr.Zero;
            source = null;
            return "The only available token is elevated. The replay was not started.";
        }

        return null;
    }

    private static IntPtr OpenProcessTokenFromMediumProcess(string processName, out string source)
    {
        source = processName;
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                IntPtr handle = OpenProcess(
                    ProcessQueryLimitedInformation,
                    false,
                    (uint)process.Id
                );
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (!OpenProcessToken(handle, PrimaryTokenAccess, out IntPtr token))
                    {
                        continue;
                    }

                    if (IsTokenElevated(token))
                    {
                        CloseHandle(token);
                        continue;
                    }

                    return token;
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        source = null;
        return IntPtr.Zero;
    }

    private static IntPtr OpenShellToken()
    {
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        GetWindowThreadProcessId(shell, out uint pid);
        if (pid == 0)
        {
            return IntPtr.Zero;
        }

        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            if (!OpenProcessToken(process, PrimaryTokenAccess, out IntPtr token))
            {
                return IntPtr.Zero;
            }

            if (IsTokenElevated(token))
            {
                CloseHandle(token);
                return IntPtr.Zero;
            }

            return token;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static void EnableLaunchPrivileges()
    {
        if (
            !OpenProcessToken(
                GetCurrentProcess(),
                TokenAdjustPrivileges | TokenQuery,
                out IntPtr token
            )
        )
        {
            return;
        }

        try
        {
            EnablePrivilege(token, "SeImpersonatePrivilege");
            EnablePrivilege(token, "SeIncreaseQuotaPrivilege");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static void EnablePrivilege(IntPtr token, string name)
    {
        if (!LookupPrivilegeValue(null, name, out Luid luid))
        {
            return;
        }

        var privileges = new TokenPrivileges
        {
            PrivilegeCount = 1,
            Privileges = new LuidAndAttributes { Luid = luid, Attributes = PrivilegeEnabled },
        };
        AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static bool IsTokenElevated(IntPtr token)
    {
        int size = Marshal.SizeOf<TokenElevation>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, TokenElevationClass, buffer, size, out _))
            {
                return false;
            }

            return Marshal.PtrToStructure<TokenElevation>(buffer).TokenIsElevated != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int tokenClass,
        IntPtr information,
        int length,
        out int returnLength
    );

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existing,
        uint access,
        IntPtr attributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken
    );

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr token,
        int logonFlags,
        string applicationName,
        StringBuilder commandLine,
        int creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startup,
        out ProcessInformation process
    );

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr token,
        bool disableAll,
        ref TokenPrivileges newState,
        int bufferLength,
        IntPtr previousState,
        IntPtr returnLength
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
