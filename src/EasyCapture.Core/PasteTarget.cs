namespace EasyCapture.Core;

/// <summary>"즉시 붙여넣기" 대상 창을 판정한 결과.</summary>
public enum PasteTargetVerdict
{
    /// <summary>붙여넣어도 된다.</summary>
    Ok,

    /// <summary>핸들이 없다(0).</summary>
    NoWindow,

    /// <summary>창이 이미 닫혔다(또는 소유 프로세스를 알 수 없다).</summary>
    WindowClosed,

    /// <summary>이 앱의 창이다. 자기 자신에게 붙여넣지 않는다.</summary>
    OwnProcess,

    /// <summary>작업 표시줄·트레이·바탕화면 같은 셸 창이다. 바탕화면에 Ctrl+V하면 파일이 바탕화면에 복사되므로 건너뛴다.</summary>
    ShellWindow,
}

/// <summary>패널을 열기 직전 활성 창으로 붙여넣어도 되는지 판정한다. Win32 조회는 호출 측이 하고 값만 넘긴다.</summary>
public static class PasteTarget
{
    private static readonly HashSet<string> ShellClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TrayNotifyWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
        "Progman",
        "WorkerW",
    };

    /// <summary>
    /// handle: 창 핸들(0이면 없음), windowExists: 지금도 존재하는 창인지, ownerProcessId: 창을 만든 프로세스 id(0이면 알 수 없음),
    /// ownProcessId: 이 앱의 프로세스 id, className: 창 클래스 이름(모르면 null).
    /// 위에서부터 차례로 검사해 처음 걸리는 결과를 돌려준다(NoWindow, WindowClosed, OwnProcess, ShellWindow, Ok).
    /// </summary>
    public static PasteTargetVerdict Judge(nint handle, bool windowExists, uint ownerProcessId, uint ownProcessId, string? className)
    {
        if (handle == 0)
        {
            return PasteTargetVerdict.NoWindow;
        }

        if (!windowExists || ownerProcessId == 0)
        {
            return PasteTargetVerdict.WindowClosed;
        }

        if (ownerProcessId == ownProcessId)
        {
            return PasteTargetVerdict.OwnProcess;
        }

        if (className is not null && ShellClassNames.Contains(className))
        {
            return PasteTargetVerdict.ShellWindow;
        }

        return PasteTargetVerdict.Ok;
    }
}
