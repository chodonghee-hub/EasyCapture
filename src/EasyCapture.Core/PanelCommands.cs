namespace EasyCapture.Core;

/// <summary>선택 상태에서 각 패널 동작을 쓸 수 있는지와 고정 토글의 목표값.</summary>
/// <param name="CanCopy">복사·붙여넣기(1개 이상).</param>
/// <param name="CanDelete">삭제(1개 이상).</param>
/// <param name="CanTogglePin">고정 토글(1개 이상).</param>
/// <param name="CanRename">이름 변경(정확히 1개).</param>
/// <param name="CanOpenFolder">폴더 열기(정확히 1개).</param>
/// <param name="CanSaveAs">다른 이름으로 저장(정확히 1개).</param>
/// <param name="PinTarget">고정 토글을 누르면 모든 선택 항목에 적용할 값. 전부 고정돼 있으면 false(해제), 아니면 true.</param>
public sealed record PanelCommandState(
    bool CanCopy,
    bool CanDelete,
    bool CanTogglePin,
    bool CanRename,
    bool CanOpenFolder,
    bool CanSaveAs,
    bool PinTarget);

/// <summary>패널 메뉴·키 동작의 활성 여부와 확인 문구를 결정한다.</summary>
public static class PanelCommands
{
    /// <summary>선택된 항목들로 동작별 활성 여부를 계산한다. 비면 전부 비활성이고 PinTarget은 true다.</summary>
    public static PanelCommandState Evaluate(IReadOnlyList<CaptureItem> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        var any = selected.Count > 0;
        var single = selected.Count == 1;
        return new PanelCommandState(
            CanCopy: any,
            CanDelete: any,
            CanTogglePin: any,
            CanRename: single,
            CanOpenFolder: single,
            CanSaveAs: single,
            PinTarget: !(any && selected.All(i => i.Pinned)));
    }

    /// <summary>삭제 확인 문구(한국어). 휴지통 없이 영구 삭제됨과 고정 항목 포함 여부를 알린다. 비어 있으면 빈 문자열.</summary>
    public static string BuildDeleteMessage(IReadOnlyList<CaptureItem> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        if (selected.Count == 0)
        {
            return string.Empty;
        }

        var pinned = selected.Count(i => i.Pinned);
        var subject = selected.Count == 1 ? $"'{selected[0].Name}'" : $"선택한 {selected.Count}개";
        string pinNote;
        if (pinned == 0)
        {
            pinNote = string.Empty;
        }
        else if (selected.Count == 1)
        {
            pinNote = " 고정된 항목입니다.";
        }
        else
        {
            pinNote = $" 고정 {pinned}개 포함.";
        }

        return $"{subject}을(를) 삭제할까요? 파일도 지워지며 되돌릴 수 없습니다.{pinNote}";
    }
}
