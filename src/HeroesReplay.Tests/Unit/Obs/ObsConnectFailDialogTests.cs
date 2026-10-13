using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Obs;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// #407: OBS's "Failed to connect" dialog is closed after a failed start. Only a visible
/// top-level window of the OBS pid with exactly the configured title is touched.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsConnectFailDialogTests
{
    private const int ObsPid = 7012;

    [Fact]
    public void OnlyTheObsPidsWindowWithTheExactTitle_IsClosed()
    {
        var windows = new FakeWindows();
        IntPtr dialog = windows.Add(ObsPid, "Failed to connect");
        windows.Add(ObsPid, "OBS 32.2.2 - Profile: HeroesReplay - Scenes: HeroesReplay");
        windows.Add(ObsPid, "Failed to connect ");
        windows.Add(ObsPid, "failed to connect");
        windows.Add(ObsPid, "Failed to connect", visible: false);
        windows.Add(ObsPid + 1, "Failed to connect");
        windows.Add(4, "Failed to connect");
        var logger = new ListLogger();

        int closed = ObsConnectFailDialog.Close(windows, ObsPid, "Failed to connect", logger);

        Assert.Equal(1, closed);
        Assert.Equal(new[] { dialog }, windows.Closed);
        string line = Assert
            .Single(logger.Entries, entry => entry.Level == LogLevel.Information)
            .Message;
        Assert.Equal(
            "Closed OBS's \"Failed to connect\" dialog (window 0x1000, OBS pid 7012) after a stream start that did not connect.",
            line
        );
    }

    [Fact]
    public void EveryStackedDialog_IsClosedAndLoggedOnce()
    {
        var windows = new FakeWindows();
        for (int i = 0; i < 4; i++)
        {
            windows.Add(ObsPid, "Failed to connect");
        }

        var logger = new ListLogger();

        Assert.Equal(4, ObsConnectFailDialog.Close(windows, ObsPid, "Failed to connect", logger));
        Assert.Equal(4, windows.Closed.Count);
        Assert.Equal(4, logger.Entries.Count(entry => entry.Level == LogLevel.Information));
    }

    [Fact]
    public void TheConfiguredTitle_IsTheOneMatched()
    {
        var windows = new FakeWindows();
        windows.Add(ObsPid, "Failed to connect");
        IntPtr german = windows.Add(ObsPid, "Verbindung fehlgeschlagen");

        ObsConnectFailDialog.Close(windows, ObsPid, "Verbindung fehlgeschlagen", new ListLogger());

        Assert.Equal(new[] { german }, windows.Closed);
    }

    [Fact]
    public void ANotRespondingDialog_IsLeftOpen()
    {
        var windows = new FakeWindows();
        windows.Add(ObsPid, "Failed to connect", hung: true);
        var logger = new ListLogger();

        Assert.Equal(0, ObsConnectFailDialog.Close(windows, ObsPid, "Failed to connect", logger));
        Assert.Empty(windows.Closed);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(null, "Failed to connect")]
    [InlineData(0, "Failed to connect")]
    [InlineData(ObsPid, "")]
    [InlineData(ObsPid, null)]
    public void NoObsPidOrNoTitle_TouchesNothing(int? pid, string title)
    {
        var windows = new FakeWindows();
        windows.Add(ObsPid, "Failed to connect");

        Assert.Equal(0, ObsConnectFailDialog.Close(windows, pid, title, new ListLogger()));
        Assert.Empty(windows.Closed);
        Assert.Equal(0, windows.Listed);
    }

    [Fact]
    public void AWindowListThatFails_ClosesNothingAndDoesNotThrow()
    {
        var windows = new FakeWindows { ListError = new InvalidOperationException("EnumWindows") };

        Assert.Equal(
            0,
            ObsConnectFailDialog.Close(windows, ObsPid, "Failed to connect", new ListLogger())
        );
    }

    [Fact]
    public void TheDefaultTitle_IsObsEnglishConnectFailTitle()
    {
        Assert.Equal("Failed to connect", new OBSSettings().ConnectFailDialogTitle);
        Assert.Equal(TimeSpan.FromSeconds(3), new OBSSettings().IngestPreflightTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), new OBSSettings().StreamStartTimeout);
    }

    private sealed class FakeWindows : IObsWindows
    {
        private readonly List<ObsTopWindow> windows = new();

        public List<IntPtr> Closed { get; } = new();
        public Exception ListError { get; set; }
        public int Listed { get; private set; }

        public IntPtr Add(int pid, string title, bool visible = true, bool hung = false)
        {
            var handle = new IntPtr(0x1000 + windows.Count);
            windows.Add(new ObsTopWindow(handle, pid, title, visible, hung));
            return handle;
        }

        public IReadOnlyList<ObsTopWindow> TopLevel()
        {
            Listed++;
            if (ListError != null)
            {
                throw ListError;
            }

            return windows.ToList();
        }

        public bool Close(IntPtr handle)
        {
            Closed.Add(handle);
            return true;
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
