using EasyCapture.Core;

namespace EasyCapture.App;

/// <summary>
/// 클립보드 패널의 수명과 "즉시 붙여넣기"를 맡는다. 패널을 열기 직전 활성 창을 기억해 두었다가,
/// Enter로 복사해 패널이 닫힌 뒤 그 창으로 돌아가 Ctrl+V를 보낸다. UI 스레드에서만 호출한다.
/// </summary>
public sealed class ClipboardPanelService : IDisposable
{
    private static readonly TimeSpan ForegroundTimeout = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(80);
    private static readonly int[] PasteBlockingKeys =
        [WindowFocus.VkShift, WindowFocus.VkMenu, WindowFocus.VkLWin, WindowFocus.VkRWin];

    private readonly CaptureLibrary _library;
    private readonly Func<CopyMode> _copyMode;
    private readonly Action<string> _notify;
    private ClipboardPanelWindow? _window;
    private IntPtr _previousWindow;
    private bool _disposed;

    public ClipboardPanelService(CaptureLibrary library, Func<CopyMode> copyMode, Action<string> notify)
    {
        _library = library;
        _copyMode = copyMode;
        _notify = notify;
    }

    /// <summary>
    /// 패널을 연다. 이미 열려 있으면 토글한다: 지금 포커스가 있으면 닫고(같은 단축키를 한 번 더 누르면 닫히는 팝업의 일반적인 동작),
    /// 열려는 있지만 포커스가 없으면 닫지 않고 앞으로 가져온다(포커스를 못 얻은 패널을 다시 살리는 경우).
    /// 이전 활성 창은 패널이 새로 만들어질 때만 기억한다(이미 열린 패널 자신이 덮어쓰지 않도록).
    /// </summary>
    public void Toggle()
    {
        if (_disposed)
        {
            return;
        }

        if (_window is { } existing)
        {
            try
            {
                if (existing.IsForeground)
                {
                    existing.Dismiss();
                }
                else
                {
                    existing.BringToForeground();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 닫히는 중인 창이면 무시한다.
            }

            return;
        }

        // 패널이 활성화되기 전에 기억해야 한다. 트레이 메뉴로 열 때는 호출 측이 메뉴가 닫힌 뒤에 부른다.
        _previousWindow = WindowFocus.GetForeground();

        var window = new ClipboardPanelWindow(_library, _copyMode);
        _window = window;
        window.Closed += (_, _) => OnWindowClosed(window);
        window.Show();
        window.BringToForeground();
    }

    /// <summary>열려 있는 패널을 닫는다. 앱 종료 때 쓰며, 이후 붙여넣기는 하지 않는다.</summary>
    public void Close()
    {
        var window = _window;
        _window = null;
        window?.Dismiss();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    private void OnWindowClosed(ClipboardPanelWindow window)
    {
        if (!ReferenceEquals(_window, window))
        {
            return;
        }

        _window = null;
        var previous = _previousWindow;
        _previousWindow = IntPtr.Zero;
        if (_disposed)
        {
            return;
        }

        var result = window.Result;
        switch (result.Outcome)
        {
            case PanelOutcome.Copied:
                _notify($"{result.ItemCount}개를 클립보드에 복사했습니다.");
                break;
            case PanelOutcome.CopiedForPaste:
                _ = PasteAsync(previous, result.ItemCount);
                break;
            case PanelOutcome.FocusNotAcquired:
                _notify("클립보드 패널에 포커스를 얻지 못했습니다. 다른 창이 포커스를 잡고 있는지 확인하세요.");
                break;
        }
    }

    // 패널이 닫힌 뒤 이전 창으로 돌아가 Ctrl+V를 보낸다. 어느 단계에서든 실패하면 복사만 된 상태로 알리고 끝낸다.
    private async Task PasteAsync(IntPtr target, int itemCount)
    {
        var copiedNotice = $"{itemCount}개를 클립보드에 복사했습니다.";
        try
        {
            // 작업 표시줄·트레이·바탕화면이거나 내 창이거나 이미 닫힌 창이면 붙여넣지 않는다.
            if (WindowFocus.JudgePasteTarget(target) != PasteTargetVerdict.Ok)
            {
                _notify(copiedNotice);
                return;
            }

            WindowFocus.Acquire(target);
            if (!await WaitUntilAsync(() => WindowFocus.GetForeground() == target, ForegroundTimeout))
            {
                _notify($"{copiedNotice} 이전 창으로 돌아가지 못해 붙여넣지 못했습니다.");
                return;
            }

            // 받는 창이 입력을 받을 준비를 하도록 잠깐 기다린다.
            await Task.Delay(SettleDelay);

            // 아직 눌려 있는 Shift/Alt/Win이 Ctrl+V와 합쳐져 다른 단축키(예: Ctrl+Shift+V)가 되지 않게 뗄 때까지 기다린다.
            if (!await WaitUntilAsync(() => !PasteBlockingKeys.Any(WindowFocus.IsKeyDown), ModifierReleaseTimeout))
            {
                _notify($"{copiedNotice} 키가 눌려 있어 붙여넣지 않았습니다.");
                return;
            }

            if (_disposed || WindowFocus.GetForeground() != target)
            {
                if (!_disposed)
                {
                    _notify($"{copiedNotice} 활성 창이 바뀌어 붙여넣지 않았습니다.");
                }

                return;
            }

            if (!WindowFocus.SendCtrlV())
            {
                _notify($"{copiedNotice} 붙여넣기 키를 보내지 못했습니다. Ctrl+V로 직접 붙여넣으세요.");
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            // async 메서드의 예외가 처리되지 않은 채 남지 않도록 알림으로 바꾼다.
            _notify($"붙여넣는 중 오류가 발생했습니다: {ex.Message}");
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(PollInterval);
        }

        return true;
    }
}
