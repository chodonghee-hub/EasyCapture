namespace EasyCapture.Core;

/// <summary>프리셋(모니터 기준 상대 좌표)과 가상 화면 절대 좌표 사이의 변환.</summary>
public static class PresetGeometry
{
    /// <summary>모든 모니터를 감싸는 가상 화면 영역. 모니터가 없으면 <see cref="PixelRect.Empty"/>.</summary>
    public static PixelRect VirtualBounds(IReadOnlyList<MonitorInfo> monitors)
    {
        var result = PixelRect.Empty;
        foreach (var monitor in monitors)
        {
            result = result.Union(monitor.Bounds);
        }

        return result;
    }

    /// <summary>장치 이름이 같은 모니터, 없으면 주 모니터, 그것도 없으면 첫 모니터.</summary>
    public static MonitorInfo? FindMonitor(string? deviceName, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var named = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (named is not null)
            {
                return named;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
    }

    /// <summary>영역과 가장 많이 겹치는 모니터. 어디와도 안 겹치면 주 모니터.</summary>
    public static MonitorInfo? MonitorContaining(PixelRect area, IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo? best = null;
        long bestArea = 0;
        foreach (var monitor in monitors)
        {
            var overlap = monitor.Bounds.Intersect(area).Area;
            if (overlap > bestArea)
            {
                best = monitor;
                bestArea = overlap;
            }
        }

        return best ?? FindMonitor(null, monitors);
    }

    /// <summary>
    /// 프리셋을 절대 영역으로 바꾼다. 위치는 해당 모니터(없으면 주 모니터) 기준으로 해석하고,
    /// 클램프는 모니터 경계를 가로지르는 영역이 유지되도록 전체 가상 화면 기준으로 한다(모니터가 없으면 좌표를 그대로 쓴다).
    /// </summary>
    public static PixelRect Resolve(CapturePreset preset, IReadOnlyList<MonitorInfo> monitors)
    {
        var monitor = FindMonitor(preset.MonitorDeviceName, monitors);
        var width = Math.Max(1, preset.Width);
        var height = Math.Max(1, preset.Height);
        if (monitor is null)
        {
            return new PixelRect(preset.X, preset.Y, width, height);
        }

        return new PixelRect(monitor.Bounds.X + preset.X, monitor.Bounds.Y + preset.Y, width, height).Clamp(VirtualBounds(monitors));
    }

    /// <summary>선택 영역을 (모니터 이름, 모니터 왼쪽 위 기준 X, Y)로 바꾼다. 모니터가 없으면 이름은 null.</summary>
    public static (string? DeviceName, int X, int Y) ToRelative(PixelRect selection, IReadOnlyList<MonitorInfo> monitors)
    {
        var monitor = MonitorContaining(selection, monitors);
        return monitor is null
            ? (null, selection.X, selection.Y)
            : (monitor.DeviceName, selection.X - monitor.Bounds.X, selection.Y - monitor.Bounds.Y);
    }
}
