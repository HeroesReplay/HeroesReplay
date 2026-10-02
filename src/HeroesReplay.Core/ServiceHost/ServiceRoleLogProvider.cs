using System;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Writes a role's logs to its own rolling file (<see cref="ServiceRoleLogWriter"/>), one entry
/// per line with the local time, level, and category. Secrets are redacted with the same rules
/// as the heartbeat's <c>lastError</c>. The level comes from <c>Logging:RoleFile</c>.
/// </summary>
[ProviderAlias("RoleFile")]
public sealed class ServiceRoleLogProvider : ILoggerProvider
{
    private const int MaxEntryLength = 32 * 1024;
    private const string Indent = "    ";

    private readonly ServiceRoleLogWriter writer;

    public ServiceRoleLogProvider(
        string role,
        ServiceLogSettings settings,
        TimeProvider time = null,
        int? pid = null
    )
    {
        settings ??= new ServiceLogSettings();
        writer = new ServiceRoleLogWriter(
            role,
            settings.ResolvedDirectory,
            settings.MaxFileBytes,
            settings.Days,
            settings.MaxFiles,
            time,
            $"{role} pid {pid ?? Environment.ProcessId} version {ServiceReadyFile.CurrentVersion()}"
        );
    }

    /// <summary>The file the last entry went to. Null until the first entry.</summary>
    public string CurrentPath => writer.CurrentPath;

    public ILogger CreateLogger(string categoryName) => new RoleFileLogger(this, categoryName);

    public void Dispose() => writer.Dispose();

    /// <summary>One entry as it appears in the file, before redaction.</summary>
    public static string Format(
        DateTimeOffset at,
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception exception
    )
    {
        var entry = new StringBuilder();
        entry
            .Append(at.ToString(ServiceRoleLogWriter.TimestampFormat, CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Level(level))
            .Append(' ')
            .Append(category);
        if (eventId.Id != 0)
        {
            entry.Append('[').Append(eventId.Id.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        entry.Append(": ");
        AppendIndented(entry, message ?? string.Empty);
        if (exception != null)
        {
            entry.Append(Environment.NewLine).Append(Indent);
            AppendIndented(entry, exception.ToString());
        }

        return entry.ToString();
    }

    private void Write(
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception exception
    )
    {
        DateTimeOffset at = writer.LocalNow();
        string entry = Format(at, level, category, eventId, message, exception);
        writer.Write(at, ServiceLogRedaction.Redact(entry, MaxEntryLength));
    }

    // Continuation lines are indented, so every line that starts at column 0 is a new entry.
    private static void AppendIndented(StringBuilder entry, string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        entry.Append(lines[0]);
        for (int index = 1; index < lines.Length; index++)
        {
            entry.Append(Environment.NewLine).Append(Indent).Append(lines[index]);
        }
    }

    private static string Level(LogLevel level) =>
        level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "---",
        };

    private sealed class RoleFileLogger : ILogger
    {
        private readonly ServiceRoleLogProvider provider;
        private readonly string category;

        public RoleFileLogger(ServiceRoleLogProvider provider, string category)
        {
            this.provider = provider;
            this.category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (!IsEnabled(logLevel) || formatter == null)
            {
                return;
            }

            provider.Write(logLevel, category, eventId, formatter(state, null), exception);
        }
    }
}
