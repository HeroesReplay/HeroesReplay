using System;
using System.IO;
using HeroesReplay.Core.ServiceHost.Logs;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// A failed <c>services start</c> prints its reason to a console that a logon task or a release
/// restart closes at once. The same text goes to <c>supervisor-&lt;date&gt;.log</c> so it can be
/// read afterwards.
/// </summary>
public static class ServiceStartFailureLog
{
    public static void Write(ServiceLogSettings settings, int code, string output)
    {
        try
        {
            using var log = new ServiceRoleLogProvider(ServiceRoleLog.SupervisorRole, settings);
            log.CreateLogger("HeroesReplay.CLI.Commands.Services.ServicesCommand")
                .LogError(
                    "services start exited {Code} before any role was supervised. It printed:{NewLine}{Output}",
                    code,
                    Environment.NewLine,
                    string.IsNullOrWhiteSpace(output) ? "(nothing)" : output.Trim()
                );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
