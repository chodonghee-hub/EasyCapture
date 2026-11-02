namespace EasyCapture.Core;

/// <summary>"Ctrl+Shift+S" 형태로 표기하는 전역 단축키 한 개.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    private const HotkeyModifiers AllModifiers =
        HotkeyModifiers.Alt | HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    /// <summary>RegisterHotKey에 넘기는 수식어 값.</summary>
    public int Win32Modifiers => (int)Modifiers;

    public bool IsValid =>
        (Modifiers & ~AllModifiers) == 0
        && HotkeyKeys.TryGetName(VirtualKey, out _)
        && (Modifiers != HotkeyModifiers.None || HotkeyKeys.IsStandalone(VirtualKey));

    public static bool TryCreate(HotkeyModifiers modifiers, int virtualKey, out Hotkey hotkey)
    {
        hotkey = new Hotkey(modifiers, virtualKey);
        if (hotkey.IsValid)
        {
            return true;
        }

        hotkey = default;
        return false;
    }

    public static Hotkey Parse(string? text) =>
        TryParse(text, out var hotkey)
            ? hotkey
            : throw new FormatException($"올바르지 않은 단축키입니다: '{text}'");

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        int? virtualKey = null;

        foreach (var rawPart in text.Split('+'))
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                return false;
            }

            if (TryParseModifier(part, out var modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    return false;
                }

                modifiers |= modifier;
            }
            else if (virtualKey is null && HotkeyKeys.TryGetVirtualKey(part, out var vk))
            {
                virtualKey = vk;
            }
            else
            {
                return false;
            }
        }

        return virtualKey is { } key && TryCreate(modifiers, key, out hotkey);
    }

    /// <summary>수식어 순서는 항상 Ctrl+Alt+Shift+Win+키.</summary>
    public override string ToString() =>
        HotkeyKeys.TryGetName(VirtualKey, out var keyName)
            ? FormatModifiers(Modifiers) + keyName
            : string.Empty;

    /// <summary>"Ctrl+Shift+" 처럼 끝에 +가 붙은 수식어 접두 문자열. 수식어가 없으면 빈 문자열.</summary>
    public static string FormatModifiers(HotkeyModifiers modifiers)
    {
        var text = string.Empty;
        if ((modifiers & HotkeyModifiers.Ctrl) != 0)
        {
            text += "Ctrl+";
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            text += "Alt+";
        }

        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            text += "Shift+";
        }

        if ((modifiers & HotkeyModifiers.Win) != 0)
        {
            text += "Win+";
        }

        return text;
    }

    private static bool TryParseModifier(string text, out HotkeyModifiers modifier)
    {
        modifier = text.ToLowerInvariant() switch
        {
            "ctrl" or "control" => HotkeyModifiers.Ctrl,
            "alt" => HotkeyModifiers.Alt,
            "shift" => HotkeyModifiers.Shift,
            "win" or "windows" or "super" => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None,
        };

        return modifier != HotkeyModifiers.None;
    }
}
