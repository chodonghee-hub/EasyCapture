namespace EasyCapture.Core;

public sealed class CapturePreset
{
    public const string DefaultName = "기본 영역";

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>모니터 장치 이름(예: \\.\DISPLAY1). null이면 주 모니터.</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>해당 모니터 왼쪽 위 기준 물리 픽셀 X.</summary>
    public int X { get; set; }

    /// <summary>해당 모니터 왼쪽 위 기준 물리 픽셀 Y.</summary>
    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>이 프리셋으로 바로 캡처를 시작하는 전용 단축키(선택).</summary>
    public string? Hotkey { get; set; }

    public static CapturePreset CreateDefault() => new()
    {
        Id = Guid.NewGuid(),
        Name = DefaultName,
        MonitorDeviceName = null,
        X = 100,
        Y = 100,
        Width = 800,
        Height = 600,
        Hotkey = null,
    };
}
