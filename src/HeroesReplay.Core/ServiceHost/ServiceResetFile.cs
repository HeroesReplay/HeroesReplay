using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// <c>services reset &lt;role&gt;</c> asks the running supervisor to clear one role's restart
/// budget (#480). The command appends the role to <c>services.reset</c>; the supervisor takes the
/// file on its next pass, so a role that used its budget comes back without a stop and start of
/// the whole stack.
/// </summary>
public static class ServiceResetFile
{
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "services.reset"
        );

    public static void Request(string role, string path = null)
    {
        string file = path ?? DefaultPath;
        string directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(file, role + Environment.NewLine);
    }

    /// <summary>True while the file still asks for <paramref name="role"/>.</summary>
    public static bool Pending(string role, string path = null) =>
        Read(path ?? DefaultPath).Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The roles asked for, each once, and the file is gone. A request appended while the file is
    /// taken lands in a new file and is taken on the next pass.
    /// </summary>
    public static IReadOnlyList<string> Take(string path = null)
    {
        string file = path ?? DefaultPath;
        string taking = file + ".taking";
        try
        {
            if (!File.Exists(file))
            {
                return Array.Empty<string>();
            }

            File.Move(file, taking, overwrite: true);
            IReadOnlyList<string> roles = Read(taking);
            File.Delete(taking);
            return roles;
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> Read(string file)
    {
        try
        {
            return File.Exists(file)
                ? File.ReadAllLines(file)
                    .Select(line => line.Trim().ToLowerInvariant())
                    .Where(line => line.Length > 0)
                    .Distinct()
                    .ToList()
                : Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
