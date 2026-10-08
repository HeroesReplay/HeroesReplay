using System;
using System.Collections.Generic;
using System.ComponentModel;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary>
/// "Preparing game data" from the client's windows, over a fake window enumeration. The dialog's
/// shape is the client's own <c>DLGTEMPLATEEX</c> "Progress" (the same in 2.55.17.98025,
/// 2.57.0.98285, 2.57.0.98304 and 2.57.0.98348): standard dialog class <c>#32770</c>, 404x143
/// client pixels, a message static (30101), a <c>msctls_progress32</c> bar (30102), a status
/// static (30103) and Cancel (2).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class GameDataProgressWindowTests
{
    private const int Pid = 4242;

    private static readonly ClientWindow MainWindow = new(
        (IntPtr)0x1001,
        Pid,
        "Heroes of the Storm",
        "Heroes of the Storm",
        Visible: true,
        1920,
        1080,
        Array.Empty<ClientWindowChild>()
    );

    private static ClientWindow Preparing(bool visible = true, string message = "") =>
        new(
            (IntPtr)0x2002,
            Pid,
            "#32770",
            "Progress",
            visible,
            404,
            143,
            new[]
            {
                new ClientWindowChild("Static", 30101, message),
                new ClientWindowChild("msctls_progress32", 30102, ""),
                new ClientWindowChild("Static", 30103, ""),
                new ClientWindowChild("Button", 2, "Cancel"),
            }
        );

    // A plain message box: the same dialog class, no progress bar.
    private static readonly ClientWindow MessageBox = new(
        (IntPtr)0x3003,
        Pid,
        "#32770",
        "Heroes of the Storm",
        Visible: true,
        360,
        120,
        new[]
        {
            new ClientWindowChild("Static", 65535, ""),
            new ClientWindowChild("Button", 1, "OK"),
        }
    );

    [Fact]
    public void ShownWhileTheProgressDialogIsVisible()
    {
        var windows = new FakeClientWindows(MainWindow, Preparing());

        GameDataWindowSample sample = GameDataProgressWindow.Read(windows, Pid);

        Assert.True(sample.Shown);
        Assert.Contains("#32770", sample.Reason);
        Assert.Contains("404x143", sample.Reason);
        Assert.Equal(Pid, windows.AskedFor);
    }

    [Fact]
    public void ShownBeforeTheGameWindowExists()
    {
        GameDataWindowSample sample = GameDataProgressWindow.Read(
            new FakeClientWindows(Preparing()),
            Pid
        );

        Assert.True(sample.Shown);
    }

    [Fact]
    public void NotShownWithOnlyTheGameWindow()
    {
        GameDataWindowSample sample = GameDataProgressWindow.Read(
            new FakeClientWindows(MainWindow),
            Pid
        );

        Assert.False(sample.Shown);
        Assert.Contains("no progress dialog", sample.Reason);
    }

    [Fact]
    public void AHiddenProgressDialogIsNotShown()
    {
        GameDataWindowSample sample = GameDataProgressWindow.Read(
            new FakeClientWindows(MainWindow, Preparing(visible: false)),
            Pid
        );

        Assert.False(sample.Shown);
    }

    [Fact]
    public void AMessageBoxIsNotTheProgressDialog()
    {
        Assert.False(GameDataProgressWindow.IsProgressDialog(MessageBox));
        Assert.False(
            GameDataProgressWindow.Read(new FakeClientWindows(MainWindow, MessageBox), Pid).Shown
        );
    }

    [Fact]
    public void AProgressBarInAnotherWindowClassIsNotTheDialog()
    {
        ClientWindow other = Preparing() with { ClassName = "SomeOtherWindow" };

        Assert.False(GameDataProgressWindow.IsProgressDialog(other));
    }

    [Fact]
    public void NoProcessIsUnknown()
    {
        var windows = new FakeClientWindows(Preparing());

        Assert.Null(GameDataProgressWindow.Read(windows, null).Shown);
        Assert.Null(GameDataProgressWindow.Read(windows, 0).Shown);
        Assert.Null(GameDataProgressWindow.Read(null, Pid).Shown);
        Assert.Null(windows.AskedFor);
    }

    [Fact]
    public void AFailedEnumerationIsUnknown()
    {
        GameDataWindowSample sample = GameDataProgressWindow.Read(
            new FakeClientWindows(new Win32Exception(5, "Access is denied")),
            Pid
        );

        Assert.Null(sample.Shown);
        Assert.Contains("Access is denied", sample.Reason);
    }

    [Fact]
    public void DescribeNamesTheMessageWhenWindowsKeepsIt()
    {
        string described = GameDataProgressWindow.Describe(
            Preparing(message: "Preparing game data")
        );

        Assert.Equal(
            "progress dialog #32770 \"Progress\" 404x143, pid 4242, message \"Preparing game data\"",
            described
        );
        Assert.Equal(
            "progress dialog #32770 \"Progress\" 404x143, pid 4242",
            GameDataProgressWindow.Describe(Preparing())
        );
    }

    private sealed class FakeClientWindows : IClientWindows
    {
        private readonly IReadOnlyList<ClientWindow> windows;
        private readonly Exception failure;

        public FakeClientWindows(params ClientWindow[] windows) => this.windows = windows;

        public FakeClientWindows(Exception failure) => this.failure = failure;

        public int? AskedFor { get; private set; }

        public IReadOnlyList<ClientWindow> TopLevel(int processId)
        {
            AskedFor = processId;
            if (failure != null)
            {
                throw failure;
            }

            return windows;
        }
    }
}
