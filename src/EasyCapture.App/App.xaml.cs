using EasyCapture.Core;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs = System.Windows.ExitEventArgs;

namespace EasyCapture.App;

public partial class App : Application
{
    private static readonly TimeSpan TrayMenuCloseDelay = TimeSpan.FromMilliseconds(250);

    private SingleInstance? _singleInstance;
    private TrayService? _tray;
    private HotkeyService? _hotkeys;
    private CaptureService? _capture;
    private ClipboardPanelService? _panel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            Shutdown();
            return;
        }

        var tray = _tray = new TrayService();
        tray.CaptureRequested += OnTrayCaptureRequested;
        tray.PanelRequested += OnTrayPanelRequested;
        tray.SettingsRequested += () => tray.ShowNotice("설정은 준비 중입니다.");
        tray.ExitRequested += ExitApp;

        var dataRoot = ResolveDataRoot();
        var store = new SettingsStore(AppPaths.SettingsFile(dataRoot));
        var settings = store.Load();
        var library = new CaptureLibrary(AppPaths.CapturesDirectory(dataRoot));
        var capture = _capture = new CaptureService(settings, store, library, tray.ShowNotice);
        _panel = new ClipboardPanelService(library, () => capture.Settings.CopyMode, tray.ShowNotice);

        // 단축키 동작은 트레이 메뉴와 같은 핸들러를 쓴다. 이후 임무가 이 자리에서 실제 동작으로 교체한다.
        var handlers = new HotkeyHandlers
        {
            StartCapture = () => capture.Start(),
            OpenPanel = OnPanelRequested,
            CopyLast = () => tray.ShowNotice("마지막 캡처 다시 복사는 준비 중입니다."),
            CaptureWithPreset = preset => capture.Start(preset.Id),
        };

        _hotkeys = new HotkeyService();
        new HotkeyBinder(_hotkeys, handlers, tray.ShowNotice).Rebind(settings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _panel?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    // 메뉴가 완전히 사라진 뒤에 화면을 찍도록 잠깐 기다린다.
    private async void OnTrayCaptureRequested()
    {
        await Task.Delay(TrayMenuCloseDelay);
        _capture?.Start();
    }

    // 단축키로 열 때: 지금 활성 창이 곧 "이전 활성 창"이므로 바로 연다.
    private void OnPanelRequested() => _panel?.Toggle();

    // 트레이 메뉴로 열 때: 메뉴가 닫혀 포커스가 원래 창(또는 작업 표시줄)으로 돌아간 뒤에 열어야 이전 활성 창을 제대로 기억한다.
    // async void의 예외는 앱을 끝내므로 여기서 모두 알림으로 바꾼다.
    private async void OnTrayPanelRequested()
    {
        try
        {
            await Task.Delay(TrayMenuCloseDelay);
            _panel?.Toggle();
        }
        catch (Exception ex)
        {
            _tray?.ShowNotice($"클립보드 패널을 열지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>개발·검증용: EASYCAPTURE_DATA_DIR이 있으면 실제 %APPDATA% 대신 그 폴더를 쓴다.</summary>
    private static string? ResolveDataRoot()
    {
        var root = Environment.GetEnvironmentVariable("EASYCAPTURE_DATA_DIR");
        return string.IsNullOrWhiteSpace(root) ? null : root;
    }

    private void ExitApp()
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
        _capture = null;
        _panel?.Dispose();
        _panel = null;
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }
}
