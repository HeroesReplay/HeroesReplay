using System;
using System.IO;
using HeroesReplay.Core.ServiceHost.Logs;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// Each role's console is titled <c>hr-&lt;role&gt;</c> (hr-spectate, hr-twitch, hr-download,
/// hr-youtube) and the supervisor's <c>hr-supervisor</c>, so an operator can find them behind
/// OBS and the game. Otherwise every window shows the exe path.
/// </summary>
public static class ServiceConsoleTitle
{
    public const string Prefix = "hr-";

    /// <summary>The title for <paramref name="role"/>, or null when it is not a role name.</summary>
    public static string For(string role) => ServiceRoleLog.IsSafeRole(role) ? Prefix + role : null;

    public static void Apply(string role)
    {
        string title = For(role);
        if (title == null)
        {
            return;
        }

        try
        {
            Console.Title = title;
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException) { }
    }
}
