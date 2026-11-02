using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using EasyCapture.Core;

namespace EasyCapture.App;

/// <summary>캡처 한 번의 흐름: 정지 캡처 → 오버레이 → 확정 시 PNG 저장·보존 정책·알림.</summary>
public sealed class CaptureService
{
    private readonly SettingsStore _store;
    private readonly CaptureLibrary _library;
    private readonly Action<string> _notify;
    private CaptureOverlayWindow? _overlay;

    public CaptureService(AppSettings settings, SettingsStore store, CaptureLibrary library, Action<string> notify)
    {
        Settings = settings;
        _store = store;
        _library = library;
        _notify = notify;
    }

    /// <summary>현재 설정. 설정 화면이 바뀐 설정을 넘길 때 교체한다.</summary>
    public AppSettings Settings { get; set; }

    public bool IsActive => _overlay is not null;

    /// <summary>캡처가 보관소에 저장된 직후. 이후 임무(자동 클립보드 복사 등)가 연결한다.</summary>
    public event Action<CaptureItem>? Captured;

    /// <summary>캡처를 시작한다. presetId가 없거나 목록에 없으면 기본 프리셋. 이미 열려 있으면 새로 만들지 않고 기존 오버레이를 앞으로 가져온다.</summary>
    public void Start(Guid? presetId = null)
    {
        DiagnosticLog.Write($"Capture.Start called (presetId {(presetId.HasValue ? "지정" : "없음")}, overlayOpen={_overlay is not null})");
        if (_overlay is { } existing)
        {
            DiagnosticLog.Write("Capture.Start: overlay already open -> BringToForeground only");
            try
            {
                existing.BringToForeground();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 닫히는 중인 창이면 무시한다.
            }

            return;
        }

        CaptureOverlayWindow? overlay = null;
        try
        {
            var presets = Settings.Presets;
            var preset = presets.FirstOrDefault(p => p.Id == presetId)
                ?? presets.FirstOrDefault(p => p.Id == Settings.DefaultPresetId)
                ?? presets.FirstOrDefault();
            if (preset is null)
            {
                DiagnosticLog.Write("Capture.Start: no preset");
                _notify("캡처할 프리셋이 없습니다.");
                return;
            }

            var monitors = ScreenCapturer.GetMonitors();
            var virtualBounds = PresetGeometry.VirtualBounds(monitors);
            if (virtualBounds.IsEmpty)
            {
                DiagnosticLog.Write("Capture.Start: no monitors");
                _notify("모니터를 찾지 못했습니다.");
                return;
            }

            // 오버레이가 결과에 찍히지 않도록 창을 띄우기 전에 정지 캡처부터 한다.
            var image = ScreenCapturer.Capture(virtualBounds);

            var window = new CaptureOverlayWindow(image, virtualBounds, monitors, presets, preset, rect => SavePreset(rect, monitors));
            var confirmed = false; // 임시 진단: 확정 없이 닫혔는지(취소) 구분용
            window.Confirmed += (rect, chosen) =>
            {
                confirmed = true;
                OnConfirmed(window, image, virtualBounds, rect, chosen);
            };
            window.FocusNotAcquired += () =>
            {
                DiagnosticLog.Write("overlay FocusNotAcquired -> notice");
                _notify("포커스를 얻지 못해 캡처를 취소했습니다. 다른 창이 포커스를 잡고 있는지 확인하세요.");
            };
            window.Closed += (_, _) =>
            {
                DiagnosticLog.Write($"overlay Closed (confirmed={confirmed})");
                if (ReferenceEquals(_overlay, window))
                {
                    _overlay = null;
                }
            };
            overlay = _overlay = window;

            DiagnosticLog.Write($"Capture.Start: preset='{preset.Name}' virtual={virtualBounds.Width}x{virtualBounds.Height} monitors={monitors.Count}");
            window.Show();
            window.BringToForeground();
            DiagnosticLog.Write("overlay shown");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Capture.Start failed: {DiagnosticLog.Describe(ex)}");
            _overlay = null;
            TryClose(overlay);
            _notify($"캡처를 시작하지 못했습니다: {ex.Message}");
        }
    }

    // 선택 영역을 PNG로 만든 뒤(정지 이미지에서 자르므로 효과와 무관) 확정 효과를 재생하고,
    // 효과가 끝나면 오버레이를 닫고 보관소 저장·정리·알림을 한다.
    // 효과가 재생되는 동안 _overlay를 유지해, 새 캡처가 효과가 보이는 화면을 찍지 않게 한다.
    private void OnConfirmed(CaptureOverlayWindow overlay, BitmapSource image, PixelRect virtualBounds, PixelRect rect, CapturePreset preset)
    {
        var sinceConfirm = System.Diagnostics.Stopwatch.StartNew(); // 임시 진단: 확정 이후 경과 시간
        byte[] png;
        try
        {
            png = EncodePng(image, virtualBounds, rect);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"OnConfirmed: EncodePng failed: {DiagnosticLog.Describe(ex)}");
            CloseOverlay(overlay);
            _notify($"캡처를 저장하지 못했습니다: {ex.Message}");
            return;
        }

        DiagnosticLog.Write($"OnConfirmed: rect={rect.Width}x{rect.Height} png={png.Length}bytes encode={sinceConfirm.ElapsedMilliseconds}ms");
        DiagnosticLog.Write("PlayConfirmEffect call begin");
        overlay.PlayConfirmEffect(() =>
        {
            DiagnosticLog.Write($"effect done callback entered (+{sinceConfirm.ElapsedMilliseconds}ms since OnConfirmed)");
            CloseOverlay(overlay);
            SaveAndNotify(png, rect, preset);
        });
        DiagnosticLog.Write($"PlayConfirmEffect call returned (+{sinceConfirm.ElapsedMilliseconds}ms)");
    }

    private void SaveAndNotify(byte[] png, PixelRect rect, CapturePreset preset)
    {
        string notice;
        try
        {
            var item = Save(png, rect, preset);
            DiagnosticLog.Write($"Save ok {item.Width}x{item.Height}");
            notice = $"캡처됨: {preset.Name} ({item.Width}×{item.Height})";
            Captured?.Invoke(item);
            DiagnosticLog.Write("Captured subscribers done");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Save/Captured failed: {DiagnosticLog.Describe(ex)}");
            notice = $"캡처를 저장하지 못했습니다: {ex.Message}";
        }

        _notify(notice);
        DiagnosticLog.Write("notice sent");
    }

    private void CloseOverlay(CaptureOverlayWindow overlay)
    {
        DiagnosticLog.Write("CloseOverlay");
        TryClose(overlay);
        if (ReferenceEquals(_overlay, overlay))
        {
            _overlay = null;
        }
    }

    private static byte[] EncodePng(BitmapSource image, PixelRect virtualBounds, PixelRect rect)
    {
        var cropped = new CroppedBitmap(image, new Int32Rect(rect.X - virtualBounds.X, rect.Y - virtualBounds.Y, rect.Width, rect.Height));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private CaptureItem Save(byte[] png, PixelRect rect, CapturePreset preset)
    {
        var item = _library.Add(png, rect.Width, rect.Height, preset.Name);
        try
        {
            _library.ApplyRetention(Settings.MaxItems, Settings.MaxAgeDays);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 정리에 실패해도 방금 저장된 캡처는 유효하다. 다음 캡처 때 다시 정리한다.
        }

        return item;
    }

    private CapturePreset? SavePreset(PixelRect rect, IReadOnlyList<MonitorInfo> monitors)
    {
        var (deviceName, x, y) = PresetGeometry.ToRelative(rect, monitors);
        var preset = new CapturePreset
        {
            Id = Guid.NewGuid(),
            Name = PresetList.NextAreaName(Settings.Presets.Select(p => p.Name)),
            MonitorDeviceName = deviceName,
            X = x,
            Y = y,
            Width = rect.Width,
            Height = rect.Height,
        };

        Settings.Presets.Add(preset);
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Settings.Presets.Remove(preset);
            _notify($"프리셋을 저장하지 못했습니다: {ex.Message}");
            return null;
        }

        _notify($"프리셋 '{preset.Name}' 저장됨");
        return preset;
    }

    private static void TryClose(CaptureOverlayWindow? overlay)
    {
        try
        {
            overlay?.Close();
        }
        catch (InvalidOperationException)
        {
            // 이미 닫히는 중이면 무시한다.
        }
    }
}
