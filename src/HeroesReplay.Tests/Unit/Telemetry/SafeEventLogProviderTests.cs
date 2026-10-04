using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using HeroesReplay.CLI;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Replays;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Xunit;

namespace HeroesReplay.Tests.Unit.Telemetry;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SafeEventLogProviderTests
{
    [Fact]
    public void ARefusedEventLog_DoesNotFailAConstructorThatLogs()
    {
        var sink = new FakeEventLog { Refuse = true };
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging(builder => builder.AddProvider(new SafeEventLogProvider(sink)))
            .AddSingleton<LogsInItsConstructor>()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<LogsInItsConstructor>());
        Assert.Equal(1, sink.Attempts);
        Assert.Empty(sink.Written);
    }

    [Fact]
    public void ARefusedWrite_TurnsTheSinkOffForTheRestOfTheProcess()
    {
        var sink = new FakeEventLog { Refuse = true };
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder.AddProvider(new SafeEventLogProvider(sink))
        );
        ILogger logger = factory.CreateLogger("Spectate");

        logger.LogError("refused");
        sink.Refuse = false;
        logger.LogError("not retried");
        factory.CreateLogger("Another").LogError("new category");

        Assert.Equal(1, sink.Attempts);
        Assert.Empty(sink.Written);
        Assert.False(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void Writes_KeepTheEventLogLevelFromConfiguration()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["Logging:LogLevel:Default"] = "Debug",
                    ["Logging:EventLog:LogLevel:Default"] = "Warning",
                }
            )
            .Build();
        var sink = new FakeEventLog();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
            builder
                .AddConfiguration(configuration.GetSection("Logging"))
                .AddProvider(new SafeEventLogProvider(sink))
        );
        ILogger logger = factory.CreateLogger("Spectate");

        logger.LogInformation("information is filtered");
        logger.LogWarning("warning is kept");

        Assert.Equal(new[] { "warning is kept" }, sink.Written);
    }

    [Theory]
    [InlineData("report")]
    [InlineData("youtube")]
    [InlineData("twitch")]
    public void ServiceRegistrations_WriteTheEventLogOnlyThroughTheSafeSink(string services)
    {
        IServiceCollection collection = services switch
        {
            "report" => new ServiceCollection().AddReportServices(
                CancellationToken.None,
                typeof(ReplayFileProvider)
            ),
            "youtube" => new ServiceCollection().AddYouTubeServices(CancellationToken.None),
            _ => new ServiceCollection().AddTwitchServices(CancellationToken.None),
        };
        using ServiceProvider provider = collection.BuildServiceProvider();

        ILoggerProvider[] loggers = provider.GetServices<ILoggerProvider>().ToArray();
        Assert.Single(loggers.OfType<SafeEventLogProvider>());
        Assert.Empty(loggers.OfType<EventLogLoggerProvider>());
    }

    private sealed class LogsInItsConstructor
    {
        public LogsInItsConstructor(ILogger<LogsInItsConstructor> logger)
        {
            logger.LogError("Replay path does not exist: {Path}", @"C:\heroesreplay\Replays");
        }
    }

    private sealed class FakeEventLog : ILoggerProvider
    {
        public bool Refuse { get; set; }

        public int Attempts { get; private set; }

        public List<string> Written { get; } = new();

        public ILogger CreateLogger(string categoryName) => new FakeLogger(this);

        public void Dispose() { }

        private void Write(string message)
        {
            Attempts++;
            if (Refuse)
            {
                // What EventLogInternal.OpenForWrite throws when RegisterEventSource is denied.
                throw new InvalidOperationException(
                    "Cannot open log for source 'HeroesReplay.ReportService'. You may not have write access.",
                    new Win32Exception(5)
                );
            }

            Written.Add(message);
        }

        private sealed class FakeLogger : ILogger
        {
            private readonly FakeEventLog log;

            public FakeLogger(FakeEventLog log)
            {
                this.log = log;
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
            ) => log.Write(formatter(state, exception));
        }
    }
}
