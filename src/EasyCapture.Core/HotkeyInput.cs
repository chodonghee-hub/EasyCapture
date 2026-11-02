namespace EasyCapture.Core;

public enum HotkeyInputKind
{
    /// <summary>수식어만 눌린 상태. 아직 키가 확정되지 않았다.</summary>
    Incomplete,

    /// <summary>유효한 단축키가 확정되었다.</summary>
    Set,

    /// <summary>지우기 요청(Backspace/Delete 단독).</summary>
    Clear,

    /// <summary>쓸 수 없는 조합(수식어 없는 일반 키, 미지원 키 등).</summary>
    Invalid,
}

public readonly record struct HotkeyInputResult(HotkeyInputKind Kind, Hotkey Hotkey = default);

/// <summary>단축키 입력 컨트롤이 쓰는 키 입력 판정(UI 비의존).</summary>
public static class HotkeyInput
{
    private const int VkBack = 0x08;
    private const int VkDelete = 0x2E;

    public static HotkeyInputResult Evaluate(HotkeyModifiers modifiers, int virtualKey)
    {
        if (HotkeyKeys.IsModifierKey(virtualKey))
        {
            return new HotkeyInputResult(HotkeyInputKind.Incomplete);
        }

        if (modifiers == HotkeyModifiers.None && virtualKey is VkBack or VkDelete)
        {
            return new HotkeyInputResult(HotkeyInputKind.Clear);
        }

        return Hotkey.TryCreate(modifiers, virtualKey, out var hotkey)
            ? new HotkeyInputResult(HotkeyInputKind.Set, hotkey)
            : new HotkeyInputResult(HotkeyInputKind.Invalid);
    }

    /// <summary>입력 도중 표시 문장. 예: "Ctrl+Shift+…". 수식어가 없으면 빈 문자열.</summary>
    public static string FormatProgress(HotkeyModifiers modifiers) =>
        modifiers == HotkeyModifiers.None ? string.Empty : Hotkey.FormatModifiers(modifiers) + "…";
}
