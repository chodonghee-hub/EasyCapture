namespace EasyCapture.Core;

/// <summary>모니터 한 대. Bounds는 가상 화면 기준 물리 픽셀이다(보조 모니터는 음수 좌표일 수 있다).</summary>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, bool IsPrimary);
