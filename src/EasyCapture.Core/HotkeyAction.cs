namespace EasyCapture.Core;

public enum HotkeyAction
{
    /// <summary>기본 프리셋으로 캡처를 시작한다.</summary>
    StartCapture,

    /// <summary>보관소 패널을 연다.</summary>
    OpenPanel,

    /// <summary>가장 최근 캡처를 클립보드로 복사한다.</summary>
    CopyLast,
}
