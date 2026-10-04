using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.EventLog;

namespace HeroesReplay.CLI.OpenTelemetry;

/// <summary>
/// The Windows Event Log as a log sink that never throws. <see cref="EventLogLoggerProvider"/>
/// only swallows a <see cref="System.Security.SecurityException"/>. When Windows refuses the
/// source any other way ("Cannot open log for source", access denied), the write throws, the
/// logger factory rethrows it to the caller, and a constructor that logs fails to build. The
/// first failure turns this sink off for the rest of the process. The console, the role file,
/// and OpenTelemetry still get every entry. It keeps the stock alias, so <c>Logging:EventLog</c>
/// still sets its level.
/// </summary>
[ProviderAlias("EventLog")]
public sealed class SafeEventLogProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ILoggerProvider inner;
    private volatile bool failed;

    public SafeEventLogProvider(string sourceName)
        : this(new EventLogLoggerProvider(new EventLogSettings { SourceName = sourceName })) { }

    public SafeEventLogProvider(ILoggerProvider inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (!failed)
        {
            try
            {
                return new SafeLogger(this, inner.CreateLogger(categoryName));
            }
            catch (Exception)
            {
                failed = true;
            }
        }

        return NullLogger.Instance;
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        (inner as ISupportExternalScope)?.SetScopeProvider(scopeProvider);

    public void Dispose() => inner.Dispose();

    private sealed class SafeLogger : ILogger
    {
        private readonly SafeEventLogProvider provider;
        private readonly ILogger inner;

        public SafeLogger(SafeEventLogProvider provider, ILogger inner)
        {
            this.provider = provider;
            this.inner = inner;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => !provider.failed && inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (provider.failed)
            {
                return;
            }

            try
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception)
            {
                provider.failed = true;
            }
        }
    }
}
