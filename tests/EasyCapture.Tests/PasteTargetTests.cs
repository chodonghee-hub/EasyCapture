using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class PasteTargetTests
{
    // ---- Judge ----

    [Theory]
    [InlineData(0, false, 0u, 0u, PasteTargetVerdict.NoWindow)]
    [InlineData(0, true, 7u, 7u, PasteTargetVerdict.NoWindow)]
    [InlineData(0, true, 7u, 8u, PasteTargetVerdict.NoWindow)]
    [InlineData(1, false, 7u, 7u, PasteTargetVerdict.WindowClosed)]
    [InlineData(1, false, 7u, 8u, PasteTargetVerdict.WindowClosed)]
    [InlineData(1, true, 0u, 0u, PasteTargetVerdict.WindowClosed)]
    [InlineData(1, true, 0u, 8u, PasteTargetVerdict.WindowClosed)]
    [InlineData(1, true, 7u, 7u, PasteTargetVerdict.OwnProcess)]
    [InlineData(1, true, 7u, 8u, PasteTargetVerdict.ShellWindow)]
    public void Judge_OverlappingRejections_UsesDefinedPriority(
        int handle, bool exists, uint owner, uint own, PasteTargetVerdict expected)
    {
        Assert.Equal(expected, PasteTarget.Judge((nint)handle, exists, owner, own, "Shell_TrayWnd"));
    }

    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("TrayNotifyWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void Judge_ShellClass_RejectsRegardlessOfCase(string className)
    {
        Assert.Equal(PasteTargetVerdict.ShellWindow, PasteTarget.Judge(1, true, 7, 8, className));
        Assert.Equal(PasteTargetVerdict.ShellWindow, PasteTarget.Judge(1, true, 7, 8, className.ToLowerInvariant()));
        Assert.Equal(PasteTargetVerdict.ShellWindow, PasteTarget.Judge(1, true, 7, 8, className.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Notepad")]
    [InlineData("Shell_TrayWndExtra")]
    [InlineData(" Shell_TrayWnd")]
    public void Judge_OtherWindow_ReturnsOk(string? className)
    {
        Assert.Equal(PasteTargetVerdict.Ok, PasteTarget.Judge(1, true, 7, 8, className));
    }
}
