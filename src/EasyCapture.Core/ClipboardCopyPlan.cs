namespace EasyCapture.Core;

/// <summary>
/// 선택 항목을 클립보드(또는 드래그)에 어떻게 담을지 정한 결과. 값만 담으며 실제 클립보드 접근은 App이 한다.
/// </summary>
/// <param name="FileIds">파일 드롭 목록(CF_HDROP)에 넣을 항목 id. 입력 순서를 유지한다. 비어 있으면 파일은 넣지 않는다.</param>
/// <param name="ImageId">이미지로 넣을 항목 id. null이면 이미지는 넣지 않는다. 다중 선택이어도 항상 첫 항목 하나다.</param>
public sealed record ClipboardCopyPlan(IReadOnlyList<Guid> FileIds, Guid? ImageId)
{
    /// <summary>넣을 것이 아무것도 없다(선택이 비었다).</summary>
    public bool IsEmpty => FileIds.Count == 0 && ImageId is null;

    /// <summary>
    /// 복사 방식과 선택(목록 순서) 항목으로 계획을 만든다.
    /// FileAndImage: 파일은 전부 + 이미지는 첫 항목, FileOnly: 파일만, ImageOnly: 첫 항목의 이미지만.
    /// 정의되지 않은 값(잘못된 설정 파일)은 기본값인 FileAndImage로 본다. 선택이 비면 <see cref="IsEmpty"/>인 계획을 돌려준다.
    /// </summary>
    public static ClipboardCopyPlan Create(CopyMode mode, IReadOnlyList<Guid> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(selectedIds);

        if (selectedIds.Count == 0)
        {
            return new ClipboardCopyPlan([], null);
        }

        var files = selectedIds.ToArray();
        var first = selectedIds[0];
        return mode switch
        {
            CopyMode.FileOnly => new ClipboardCopyPlan(files, null),
            CopyMode.ImageOnly => new ClipboardCopyPlan([], first),
            _ => new ClipboardCopyPlan(files, first),
        };
    }
}
