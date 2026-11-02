namespace EasyCapture.Core;

/// <summary>단축키로 쓸 수 있는 키 이름 ↔ 가상 키 코드(VK) 표.</summary>
public static class HotkeyKeys
{
    private static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> ByVirtualKey = [];

    static HotkeyKeys()
    {
        // 같은 VK에 이름이 여럿이면 먼저 나온 이름이 정규 이름이다.
        foreach (var (name, vk) in BuildTable())
        {
            ByName[name] = vk;
            ByVirtualKey.TryAdd(vk, name);
        }
    }

    public static bool TryGetVirtualKey(string name, out int virtualKey) =>
        ByName.TryGetValue(name, out virtualKey);

    public static bool TryGetName(int virtualKey, out string name) =>
        ByVirtualKey.TryGetValue(virtualKey, out name!);

    /// <summary>수식어 없이 단독으로 써도 되는 키(F1–F24, PrintScreen).</summary>
    public static bool IsStandalone(int virtualKey) =>
        virtualKey is (>= 0x70 and <= 0x87) or 0x2C;

    /// <summary>Shift/Ctrl/Alt/Win 키 자체인지 여부.</summary>
    public static bool IsModifierKey(int virtualKey) =>
        virtualKey is (>= 0x10 and <= 0x12) or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    private static IEnumerable<(string Name, int Vk)> BuildTable()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            yield return (c.ToString(), c);
        }

        for (var c = '0'; c <= '9'; c++)
        {
            yield return (c.ToString(), c);
        }

        for (var i = 1; i <= 24; i++)
        {
            yield return ($"F{i}", 0x6F + i);
        }

        yield return ("Enter", 0x0D);
        yield return ("Space", 0x20);
        yield return ("Tab", 0x09);
        yield return ("Esc", 0x1B);
        yield return ("Escape", 0x1B);
        yield return ("Backspace", 0x08);
        yield return ("Delete", 0x2E);
        yield return ("Insert", 0x2D);
        yield return ("Home", 0x24);
        yield return ("End", 0x23);
        yield return ("PageUp", 0x21);
        yield return ("PageDown", 0x22);
        yield return ("Up", 0x26);
        yield return ("Down", 0x28);
        yield return ("Left", 0x25);
        yield return ("Right", 0x27);
        yield return ("PrintScreen", 0x2C);

        yield return ("`", 0xC0);
        yield return ("-", 0xBD);
        yield return ("=", 0xBB);
        yield return ("[", 0xDB);
        yield return ("]", 0xDD);
        yield return ("\\", 0xDC);
        yield return (";", 0xBA);
        yield return ("'", 0xDE);
        yield return (",", 0xBC);
        yield return (".", 0xBE);
        yield return ("/", 0xBF);
    }
}
