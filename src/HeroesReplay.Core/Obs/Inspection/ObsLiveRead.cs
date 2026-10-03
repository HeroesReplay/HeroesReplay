using System;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>What one <see cref="ObsLiveRead.Run"/> read, or the stable code it failed with.</summary>
public sealed record ObsLiveReadResult<T>(
    T Value,
    ObsInspectionSettings Settings,
    string Code,
    string Message
);

/// <summary>
/// One short read-only OBS session, as the <c>obs inspect</c> / <c>obs validate</c> commands and
/// the MCP tools open it: load the settings, resolve <c>OBS:WebSocketPassword</c>, open an
/// <see cref="IObsReadSession"/>, read, and disconnect. Every failure comes back as a stable code,
/// never as an exception.
/// </summary>
public static class ObsLiveRead
{
    public const string PasswordUnresolved = "obs.password_unresolved";
    public const string SettingsUnreadable = "obs.settings_unreadable";
    public const string RequestFailed = "obs.request_failed";
    public const string SourceNotFound = "obs.source_not_found";

    public static ObsLiveReadResult<T> Run<T>(
        IObsReadSessionFactory sessions,
        Func<ObsInspectionSettings> settings,
        Func<IObsReadSession, ObsInspectionSettings, T> read
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(read);
        ObsInspectionSettings current;
        try
        {
            current = settings();
        }
        catch (Exception e)
        {
            return new ObsLiveReadResult<T>(
                null,
                null,
                SettingsUnreadable,
                "Settings could not be loaded. " + e.Message
            );
        }

        string password;
        try
        {
            password = SecretResolver.Resolve(current.Obs?.WebSocketPassword);
        }
        catch (Exception e)
        {
            return new ObsLiveReadResult<T>(
                null,
                current,
                PasswordUnresolved,
                "OBS:WebSocketPassword could not be resolved. " + e.Message
            );
        }

        IObsReadSession session;
        try
        {
            session = sessions.Open(current.Obs?.WebSocketEndpoint, password);
        }
        catch (ObsUnavailableException e)
        {
            return new ObsLiveReadResult<T>(null, current, e.Code, e.Message);
        }

        using (session)
        {
            try
            {
                return new ObsLiveReadResult<T>(read(session, current), current, null, null);
            }
            catch (ObsRequestException e)
            {
                return new ObsLiveReadResult<T>(
                    null,
                    current,
                    e.Status == ObsRequestException.ResourceNotFound
                        ? SourceNotFound
                        : RequestFailed,
                    e.Message
                );
            }
            catch (Exception e)
            {
                // A dropped connection or a malformed answer. Report it instead of throwing; no
                // response body is part of the message.
                return new ObsLiveReadResult<T>(null, current, RequestFailed, e.Message);
            }
        }
    }
}
