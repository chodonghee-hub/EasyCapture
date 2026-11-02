namespace EasyCapture.Core;

public sealed class AppSettings
{
    public const int DefaultMaxItems = 200;

    public static IReadOnlyDictionary<HotkeyAction, string> DefaultHotkeys { get; } =
        new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.StartCapture] = "Ctrl+Shift+S",
            [HotkeyAction.OpenPanel] = "Ctrl+Shift+V",
            [HotkeyAction.CopyLast] = "Ctrl+Shift+C",
        };

    public AppSettings()
    {
        var preset = CapturePreset.CreateDefault();
        Presets = [preset];
        DefaultPresetId = preset.Id;
    }

    /// <summary>"Ctrl+Shift+S" 형태 문자열. 빈 문자열은 해당 동작을 끈 상태(비활성)다. 해석은 호출 측 몫이다.</summary>
    public Dictionary<HotkeyAction, string> Hotkeys { get; set; } = new(DefaultHotkeys);

    public List<CapturePreset> Presets { get; set; }

    public Guid? DefaultPresetId { get; set; }

    public CopyMode CopyMode { get; set; } = CopyMode.FileAndImage;

    /// <summary>캡처 직후 시스템 클립보드에 자동 복사한다.</summary>
    public bool AutoCopyLatest { get; set; } = true;

    public int MaxItems { get; set; } = DefaultMaxItems;

    /// <summary>0은 무제한.</summary>
    public int MaxAgeDays { get; set; }

    public bool StartWithWindows { get; set; }

    public int Version { get; set; } = 1;
}
