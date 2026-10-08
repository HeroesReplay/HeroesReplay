using System;

namespace HeroesReplay.Core.Dependencies;

/// <summary>Whether <c>deps install</c> has work to do for one pinned tool.</summary>
public sealed record DependencyInstallDecision(bool Install, string Reason)
{
    /// <summary>
    /// Up to date only when the record names this pin's version and archive hash and every pinned
    /// file is there at the size the record wrote. Anything else installs again.
    /// </summary>
    /// <param name="pin">The pinned build.</param>
    /// <param name="record">The tool folder's <c>installed.json</c>, or null.</param>
    /// <param name="fileLength">The length of a file in the tool folder, or null when it is missing.</param>
    public static DependencyInstallDecision Decide(
        DependencyPin pin,
        DependencyInstallRecord record,
        Func<string, long?> fileLength
    )
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(fileLength);
        if (record == null)
        {
            return new(true, $"{pin.Name} {pin.Version} is not installed.");
        }

        if (
            !string.Equals(record.Version, pin.Version, StringComparison.Ordinal)
            || !string.Equals(record.Sha256, pin.Sha256, StringComparison.OrdinalIgnoreCase)
        )
        {
            return new(
                true,
                $"{pin.Name} {record.Version ?? "(unknown)"} is installed; the pin is {pin.Version}."
            );
        }

        foreach (string name in pin.Files)
        {
            DependencyInstalledFile recorded = record.FindFile(name);
            long? length = fileLength(name);
            if (length == null)
            {
                return new(true, $"{name} is missing.");
            }

            if (recorded == null || recorded.Size != length.Value)
            {
                return new(true, $"{name} is not the file {pin.Name} {pin.Version} installed.");
            }
        }

        return new(false, $"{pin.Name} {pin.Version} is already installed.");
    }
}
