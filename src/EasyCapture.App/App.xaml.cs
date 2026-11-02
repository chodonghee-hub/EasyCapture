using System.IO;
using System.Windows;
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
        LogStartup();

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            DiagnosticLog.Write("second instance exiting");
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

        // 임시 진단: 실행된 빌드가 맞는지 사용자가 바로 알 수 있게 시작 알림을 한 번 보인다(원인 확인 뒤 제거).
        // 풍선은 나중 것이 앞의 것을 덮어쓰므로, 단축키 등록 실패 알림이 시작 알림에 가려지지 않게 Rebind 앞에서 보인다.
        try
        {
            tray.ShowNotice($"EasyCapture 시작 (빌드 {BuildTime():yyyy-MM-dd HH:mm})");
            DiagnosticLog.Write("startup notice shown");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"startup notice failed: {DiagnosticLog.Describe(ex)}");
        }

        _hotkeys = new HotkeyService();
        new HotkeyBinder(_hotkeys, handlers, tray.ShowNotice).Rebind(settings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Write($"OnExit exitCode={e.ApplicationExitCode}");
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

    // 임시 진단(원인 확인 뒤 제거): 시작 환경을 한 번 기록한다. 어떤 값이 실패해도 앱 시작에는 영향이 없다.
    private static DateTime BuildTime()
    {
        try
        {
            return File.GetLastWriteTime(typeof(App).Assembly.Location);
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    private static void LogStartup()
    {
        try
        {
            DiagnosticLog.Write($"startup exe={Environment.ProcessPath} pid={Environment.ProcessId}");
            DiagnosticLog.Write($"startup build={BuildTime():yyyy-MM-dd HH:mm:ss} os={Environment.OSVersion.Version} clr={Environment.Version}");
            DiagnosticLog.Write($"startup ClientAreaAnimation={SystemParameters.ClientAreaAnimation} systemDpiScale={DiagnosticLog.SystemDpiScale():0.##} log={DiagnosticLog.ResolvePath()}");
            DiagnosticLog.Write($"startup dataRoot={ResolveDataRoot() ?? "(기본 %APPDATA%/%LOCALAPPDATA%)"}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"startup log failed: {DiagnosticLog.Describe(ex)}");
        }
    }

    private void ExitApp()
    {
        DiagnosticLog.Write("ExitApp requested (tray menu)");
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
