namespace EasyCapture.Core;

/// <summary>설정에 지정된 단축키 한 건과 그 주인(동작 또는 프리셋).</summary>
public sealed record HotkeyEntry(string Name, Hotkey Hotkey, HotkeyAction? Action, CapturePreset? Preset);

/// <summary>같은 조합이 둘 이상에 쓰인 경우.</summary>
public sealed record HotkeyConflict(Hotkey Hotkey, IReadOnlyList<string> Names);

public static class HotkeyConflicts
{
    public static string ActionName(HotkeyAction action) => action switch
    {
        HotkeyAction.StartCapture => "캡처 시작",
        HotkeyAction.OpenPanel => "클립보드 패널 열기",
        HotkeyAction.CopyLast => "마지막 캡처 다시 복사",
        _ => action.ToString(),
    };

    /// <summary>동작 단축키 → 프리셋 단축키 순서로, 해석 가능한 항목만 모은다.</summary>
    public static IReadOnlyList<HotkeyEntry> CollectEntries(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var entries = new List<HotkeyEntry>();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (settings.Hotkeys.TryGetValue(action, out var text) && Hotkey.TryParse(text, out var hotkey))
            {
                entries.Add(new HotkeyEntry(ActionName(action), hotkey, action, null));
            }
        }

        foreach (var preset in settings.Presets)
        {
            if (Hotkey.TryParse(preset.Hotkey, out var hotkey))
            {
                entries.Add(new HotkeyEntry($"프리셋 '{preset.Name}'", hotkey, null, preset));
            }
        }

        return entries;
    }

    public static IReadOnlyList<HotkeyConflict> Find(AppSettings settings) => Find(CollectEntries(settings));

    public static IReadOnlyList<HotkeyConflict> Find(IEnumerable<HotkeyEntry> entries) =>
        entries
            .GroupBy(e => e.Hotkey)
            .Where(g => g.Count() > 1)
            .Select(g => new HotkeyConflict(g.Key, g.Select(e => e.Name).ToList()))
            .ToList();
}
