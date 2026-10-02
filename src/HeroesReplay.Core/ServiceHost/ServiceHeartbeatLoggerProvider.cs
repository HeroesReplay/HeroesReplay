using System;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Error and critical logs become the role's <c>lastError</c> in its heartbeat, so a role's
/// own error handling does not need a second call. Outside a service role this does nothing.
/// </summary>
public sealed class ServiceHeartbeatLoggerProvider : ILoggerProvider
{
    private readonly Action<string> record;

    public ServiceHeartbeatLoggerProvider()
        : this(ServiceHeartbeat.RecordError) { }

    internal ServiceHeartbeatLoggerProvider(Action<string> record)
    {
        this.record = record ?? throw new ArgumentNullException(nameof(record));
    }

    public ILogger CreateLogger(string categoryName) => new ErrorLogger(record);

    public void Dispose() { }

    private sealed class ErrorLogger : ILogger
    {
        private readonly Action<string> record;

        public ErrorLogger(Action<string> record)
        {
            this.record = record;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

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

            string message = formatter(state, exception);
            if (exception != null)
            {
                message = $"{message} ({exception.GetType().Name}: {exception.Message})";
            }

            record(message);
        }
    }
}
