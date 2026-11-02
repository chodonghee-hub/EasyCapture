using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class KeyRepeatGuardTests
{
    private const int Enter = 0x0D;
    private const int Tab = 0x09;

    [Fact]
    public void StaleRepeatOnFirstPress_IsTreatedAsNormalInput()
    {
        // 직전 오버레이가 Enter 누름에서 닫혀 뗌이 다른 창으로 갔다면 다음 첫 Enter가 반복으로 표시된다.
        var guard = new KeyRepeatGuard();
        guard.Arm([]);

        Assert.True(guard.ShouldAct(Enter, isRepeat: true));
    }

    [Fact]
    public void RepeatsAfterAcceptedPress_AreIgnoredUntilRelease()
    {
        var guard = new KeyRepeatGuard();

        Assert.True(guard.ShouldAct(Tab, isRepeat: false));
        Assert.False(guard.ShouldAct(Tab, isRepeat: true));
        Assert.False(guard.ShouldAct(Tab, isRepeat: true));

        guard.Release(Tab);
        Assert.True(guard.ShouldAct(Tab, isRepeat: false));
    }

    [Fact]
    public void KeyHeldWhenOverlayAppears_AllPressesAreIgnoredUntilRelease()
    {
        // Ctrl+Shift+Enter 같은 단축키를 누른 채 오버레이가 떴을 때, 이어지는 반복 Enter가 확정을 일으키면 안 된다.
        // 단축키가 첫 누름을 가져가므로 오버레이는 반복을 "반복 아님"으로 받을 수 있다.
        var guard = new KeyRepeatGuard();
        guard.Arm([Enter]);

        Assert.False(guard.ShouldAct(Enter, isRepeat: false));
        Assert.False(guard.ShouldAct(Enter, isRepeat: true));
        Assert.False(guard.ShouldAct(Enter, isRepeat: true));

        guard.Release(Enter);
        Assert.True(guard.ShouldAct(Enter, isRepeat: false));
    }

    [Fact]
    public void ReleaseIfUp_ClearsKeysReleasedBeforeOverlayHadFocus()
    {
        var guard = new KeyRepeatGuard();
        guard.Arm([Enter, Tab]);

        guard.ReleaseIfUp(key => key == Tab);

        Assert.True(guard.ShouldAct(Enter, isRepeat: false));
        Assert.False(guard.ShouldAct(Tab, isRepeat: true));
    }

    [Fact]
    public void ReleaseIfUp_DoesNotTouchKeysPressedInsideOverlay()
    {
        var guard = new KeyRepeatGuard();
        Assert.True(guard.ShouldAct(Tab, isRepeat: false));

        guard.ReleaseIfUp(_ => false);

        Assert.False(guard.ShouldAct(Tab, isRepeat: true));
    }

    [Fact]
    public void Keys_AreTrackedIndependently()
    {
        var guard = new KeyRepeatGuard();
        guard.Arm([Enter]);

        Assert.True(guard.ShouldAct(Tab, isRepeat: true));
        Assert.False(guard.ShouldAct(Enter, isRepeat: false));
    }

    [Fact]
    public void Arm_ReplacesPreviousState()
    {
        var guard = new KeyRepeatGuard();
        guard.Arm([Enter]);
        guard.Arm([]);

        Assert.True(guard.ShouldAct(Enter, isRepeat: true));
    }
}

public sealed class FocusMonitorTests
{
    private static readonly TimeSpan Early = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Late = FocusMonitor.InitialFocusTimeout;

    [Fact]
    public void ForegroundOverlay_IsKept()
    {
        var monitor = new FocusMonitor();

        Assert.Equal(FocusVerdict.Keep, monitor.Tick(true, Early));
        Assert.True(monitor.SeenForeground);
    }

    [Fact]
    public void NotYetForeground_IsKeptUntilTimeout_ThenNeverFocused()
    {
        var monitor = new FocusMonitor();

        Assert.Equal(FocusVerdict.Keep, monitor.Tick(false, Early));
        Assert.Equal(FocusVerdict.Keep, monitor.Tick(false, Late - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(FocusVerdict.NeverFocused, monitor.Tick(false, Late));
    }

    [Fact]
    public void ForegroundAtTimeoutTick_IsNotCancelled()
    {
        var monitor = new FocusMonitor();

        Assert.Equal(FocusVerdict.Keep, monitor.Tick(true, Late));
    }

    [Fact]
    public void LostAfterForeground_CancelsAfterConsecutiveTicks_NotNeverFocused()
    {
        var monitor = new FocusMonitor();
        monitor.Tick(true, Early);

        Assert.Equal(FocusVerdict.Keep, monitor.Tick(false, Late));
        Assert.Equal(FocusVerdict.LostFocus, monitor.Tick(false, Late));
    }

    [Fact]
    public void ReturningToForeground_ResetsLostCount()
    {
        var monitor = new FocusMonitor();
        monitor.Tick(true, Early);
        monitor.Tick(false, Early);
        monitor.Tick(true, Early);

        Assert.Equal(FocusVerdict.Keep, monitor.Tick(false, Early));
        Assert.Equal(FocusVerdict.LostFocus, monitor.Tick(false, Early));
    }

    [Fact]
    public void MarkForegroundBeforeFirstTick_LosingFocusStillCancels()
    {
        // 첫 폴링 전에 포커스를 얻었다가 잃어도 취소 조건이 성립해야 한다.
        var monitor = new FocusMonitor();
        monitor.MarkForeground();

        Assert.Equal(FocusVerdict.Keep, monitor.Tick(false, Early));
        Assert.Equal(FocusVerdict.LostFocus, monitor.Tick(false, Early));
    }
}
