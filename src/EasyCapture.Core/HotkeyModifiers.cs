namespace EasyCapture.Core;

/// <summary>Win32 RegisterHotKey의 MOD_* 값과 같다.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}
