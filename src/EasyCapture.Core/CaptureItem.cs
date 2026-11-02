namespace EasyCapture.Core;

/// <summary>보관소에 저장된 캡처 한 장. 값이 바뀌면 새 인스턴스로 교체한다(불변).</summary>
public sealed record CaptureItem
{
    public Guid Id { get; init; }

    /// <summary>표시 이름. 확장자 없는 파일명과 같다.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>보관소 폴더 기준 상대 파일명. 예: 캡처_20261023_013045.png</summary>
    public string FileName { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public bool Pinned { get; init; }

    public string? PresetName { get; init; }
}
