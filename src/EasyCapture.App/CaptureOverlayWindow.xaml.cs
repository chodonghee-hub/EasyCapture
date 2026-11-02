using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using EasyCapture.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;
using ShapePath = System.Windows.Shapes.Path;
using Size = System.Windows.Size;

namespace EasyCapture.App;

/// <summary>
/// 가상 화면 전체를 덮는 단일 창. 정지 이미지 위에 어두운 막을 씌우고 선택 영역만 밝게 보여준다.
/// 캔버스 좌표 1이 물리 픽셀 1이므로 정지 이미지와 화면이 1:1로 정렬된다.
/// </summary>
public partial class CaptureOverlayWindow : Window
{
    public const string HelpLine =
        "Enter 캡처 · Esc 취소 · Tab 프리셋 전환 · R 새 영역 · S 프리셋으로 저장 · 방향키 이동(Ctrl+방향키 크기, Shift=10px)";

    private const string NewRegionHelpLine = "드래그해서 새 영역을 지정하세요 · Esc 취소";

    private const int WmDpiChanged = 0x02E0;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    // ---- 확정 효과(테두리 펄스 + 시계 방향으로 도는 혜성 빛) 파라미터: 여기서만 조정한다 ----
    // 길이·두께의 px는 물리 픽셀 기준이며 쓸 때 DPI 배율(_scale)을 곱한다. 모든 효과는 Effect(블러 등) 없이 겹 스트로크로 그린다.
    private static readonly TimeSpan EffectDuration = TimeSpan.FromMilliseconds(1000);  // 전체 길이(빛이 도는 시간)
    private static readonly TimeSpan EffectGuardExtra = TimeSpan.FromMilliseconds(300); // 애니메이션이 끝나지 않을 때의 안전망 여유
    private static readonly TimeSpan ThickenDuration = TimeSpan.FromMilliseconds(200);  // 테두리가 굵어지는 편도 시간(되돌아오는 시간 동일)
    private static readonly TimeSpan RingDuration = TimeSpan.FromMilliseconds(500);     // 링 하나가 퍼지는 시간
    private static readonly TimeSpan RingDelay = TimeSpan.FromMilliseconds(120);        // 다음 링의 시작 지연
    private static readonly TimeSpan RingFadeIn = TimeSpan.FromMilliseconds(50);        // 링이 투명에서 피크 밝기로 나타나는 시간
    private const int RingCount = 2;
    private const double EffectMargin = 8;          // 효과 레이어가 링 퍼짐 바깥에 더 두는 여유(선 두께·발광이 잘리지 않게)
    private const double MaxThicknessRatio = 0.25;  // 작은 영역에서 굵은 선이 영역을 삼키지 않게 하는 상한(짧은 변 대비)

    // 선택 테두리 반응
    private const double FramePeakThickness = 7;
    private const double FrameGlowWide = 7;         // 테두리 바깥으로 번지는 옅은 발광 폭(넓은 겹)
    private const double FrameGlowNarrow = 3.5;     // 같은 발광의 좁고 진한 겹
    private const double FrameRimWidth = 1.5;       // 밝은 화면에서 윤곽을 살리는 어두운 외곽 폭
    private const double FrameGlowOpacity = 0.28;
    private const double FrameGlowNarrowOpacity = 0.4;
    private const double FrameRimOpacity = 0.45;

    // 바깥으로 퍼지는 링
    private const double RingSpread = 30;           // 퍼지는 거리(사방)
    private const double RingStartThickness = 6;    // 퍼지며 이 두께에서
    private const double RingEndThickness = 1;      // 이 두께로 줄어든다
    private const double RingStartOpacity = 0.95;
    private const double RingHighlightRatio = 0.35; // 안쪽 흰 하이라이트 두께(링 두께 대비)
    private const double RingGlowFactor = 1.7;      // 옅은 발광 두께(링 두께 대비). 안쪽 가장자리는 링과 같고 바깥으로만 (값 - 1) x 링 두께만큼 번진다
    private const double RingOutlineWidth = 1.5;    // 링 바깥으로 더해지는 어두운 외곽 폭(바깥쪽 한쪽만)
    private const double RingGlowOpacity = 0.3;
    private const double RingOutlineOpacity = 0.5;
    private const double RingHighlightOpacity = 0.9;

    // 테두리를 따라 도는 혜성 빛
    private const double SweepLengthFraction = 0.35; // 빛 길이(둘레 대비)
    private const double SweepLaps = 1.6;            // 도는 바퀴 수
    private const double SweepEasePower = 1.1;       // 1이면 등속, 클수록 처음에 느리고 뒤로 갈수록 빨라진다(1.1: 100ms에 머리가 상단 변 끝 근처)
    private const double SweepMinPathSize = 1;       // 혜성 경로 사각형의 최소 폭·높이(극소 영역에서 대시가 0으로 나뉘지 않게)
    private const double SweepHeadThickness = 9;     // 머리 쪽 코어 두께
    private const double SweepTailThickness = 1.5;   // 꼬리 끝 코어 두께
    private const double SweepShellFactor = 1.8;     // 코어를 감싸는 포화 파랑 겹 두께(코어 대비)
    private const double SweepGlowFactor = 3;        // 넓고 옅은 헤일로 두께(코어 대비)
    private const double SweepOutlineWidth = 1.5;    // 파랑 겹 바깥 어두운 외곽 폭(한쪽)
    private const int SweepCoreLayers = 32;          // 길이가 다른 겹을 포개 연속 그라데이션을 만든다(많을수록 부드럽다)
    private const int SweepShellLayers = 10;
    private const int SweepOutlineLayers = 8;
    private const int SweepGlowLayers = 6;
    private const double SweepCoreOpacity = 0.13;    // 겹마다의 불투명도. 겹쳐진 머리 쪽이 진해진다
    private const double SweepShellOpacity = 0.2;
    private const double SweepOutlineOpacity = 0.12;
    private const double SweepGlowOpacity = 0.08;
    private const double SweepFadeIn = 0.10;         // 컨테이너가 나타나는 구간(전체 길이 대비)
    private const double SweepFadeOut = 0.15;        // 컨테이너가 사라지는 구간

    private static readonly Color BrightFrameColor = Color.FromRgb(0x7C, 0xD4, 0xFF);
    private static readonly Color GlowColor = Color.FromRgb(0x38, 0xA0, 0xFF);
    private static readonly Color StrongBlue = Color.FromRgb(0x2E, 0x8C, 0xFF);
    private static readonly Color ShellBlue = Color.FromRgb(0x1E, 0x78, 0xFF);
    private static readonly Color OutlineColor = Color.FromRgb(0x0B, 0x1F, 0x4D);
    private static readonly Color CometMidColor = Color.FromRgb(0x8F, 0xDC, 0xFF);
    private const double CometMidPosition = 0.35;    // 코어 색이 흰색에서 하늘색으로 바뀌는 지점(머리 0 ~ 꼬리 1)

    private static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseIn });
    private static readonly IEasingFunction SweepEase = Frozen(new PowerEase { Power = SweepEasePower, EasingMode = EasingMode.EaseIn });

    private static readonly ResizeHandle[] HandleOrder =
    [
        ResizeHandle.TopLeft, ResizeHandle.TopRight, ResizeHandle.BottomRight, ResizeHandle.BottomLeft,
        ResizeHandle.Top, ResizeHandle.Right, ResizeHandle.Bottom, ResizeHandle.Left,
    ];

    private readonly PixelRect _virtual;
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly IReadOnlyList<CapturePreset> _presets;
    private readonly Func<PixelRect, CapturePreset?> _savePreset;
    private readonly CaptureSelection _selection;
    private readonly RectangleGeometry _dimHole = new();
    private readonly Dictionary<ResizeHandle, Rectangle> _handles = [];

    private CapturePreset _preset;
    private IntPtr _hwnd;
    private double _scale = 1;
    private bool _finished;
    private bool _newRegionMode;
    private bool _activatedOnce;
    private bool _focusSettled;
    private readonly FocusMonitor _focus = new();
    private readonly System.Diagnostics.Stopwatch _sinceShown = new();
    private readonly KeyRepeatGuard _repeatGuard = new();
    private readonly System.Windows.Threading.DispatcherTimer _focusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Storyboard? _effect;
    private Action? _effectDone;
    private System.Windows.Threading.DispatcherTimer? _effectGuard;

    // 임시 진단(원인 확인 뒤 제거): 효과 경과 시간과 렌더 프레임 계측. 모두 UI 스레드에서만 쓴다.
    private readonly System.Diagnostics.Stopwatch _effectClock = new();
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private bool _frameWatching;
    private int _frames;
    private TimeSpan _lastRenderingTime;
    private double _firstFrameMs;
    private double _lastFrameMs;
    private double _gapSumMs;
    private double _gapMaxMs;

    private DragMode _drag;
    private ResizeHandle _dragHandle;
    private PixelRect _dragOrigin;
    private (int X, int Y) _dragStart;

    private enum DragMode
    {
        None,
        Move,
        Resize,
        NewRegion,
    }

    public CaptureOverlayWindow(
        BitmapSource image,
        PixelRect virtualBounds,
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<CapturePreset> presets,
        CapturePreset initialPreset,
        Func<PixelRect, CapturePreset?> savePreset)
    {
        InitializeComponent();

        _virtual = virtualBounds;
        _monitors = monitors;
        _presets = presets;
        _preset = initialPreset;
        _savePreset = savePreset;
        _selection = new CaptureSelection(virtualBounds, PresetGeometry.Resolve(initialPreset, monitors));

        Surface.Width = virtualBounds.Width;
        Surface.Height = virtualBounds.Height;
        Backdrop.Width = virtualBounds.Width;
        Backdrop.Height = virtualBounds.Height;
        Backdrop.Source = image;

        var dim = new GeometryGroup { FillRule = FillRule.EvenOdd };
        dim.Children.Add(new RectangleGeometry(new Rect(0, 0, virtualBounds.Width, virtualBounds.Height)));
        dim.Children.Add(_dimHole);
        Dim.Data = dim;

        foreach (var handle in HandleOrder)
        {
            var square = new Rectangle
            {
                Fill = Brushes.White,
                Stroke = Frame.Stroke,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            _handles[handle] = square;
            Surface.Children.Add(square);
        }

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Activated += (_, _) =>
        {
            _activatedOnce = true;
            MarkIfForeground();
        };
        Deactivated += OnDeactivated;
        _focusTimer.Tick += OnFocusTick;
        Surface.MouseLeftButtonDown += OnMouseDown;
        Surface.MouseMove += OnMouseMove;
        Surface.MouseLeftButtonUp += OnMouseUp;
        Surface.LostMouseCapture += (_, _) => _drag = DragMode.None;
    }

    /// <summary>Enter로 확정했을 때. 창은 닫지 않으므로 받는 쪽이 처리 후 닫는다.</summary>
    public event Action<PixelRect, CapturePreset>? Confirmed;

    /// <summary>제한 시간 안에 포그라운드가 되지 못해 캡처를 취소하고 닫을 때. 닫은 직후 호출된다.</summary>
    public event Action? FocusNotAcquired;

    /// <summary>다른 앱이 활성인 상태에서도 최상위로 올리고 키보드 포커스를 받는다.</summary>
    public void BringToForeground()
    {
        if (!_sinceShown.IsRunning)
        {
            // 단축키를 누른 채 오버레이가 뜬 경우, 그 키의 자동 반복이 동작으로 이어지지 않도록 눌린 키를 기록해 둔다.
            _repeatGuard.Arm(GuardedKeys.Where(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0));
            _sinceShown.Start();
        }

        PlaceOnVirtualScreen();

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(_hwnd);
            NativeMethods.SetForegroundWindow(_hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        Activate();
        Surface.Focus();
        Keyboard.Focus(Surface);
        MarkIfForeground();

        // 포커스를 얻는 과정에서 생기는 일시적인 포커스 변화로 곧바로 취소되지 않도록, 활성화가 확정된 뒤부터 감시한다.
        Dispatcher.BeginInvoke(StartFocusWatch, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void StartFocusWatch()
    {
        if (_focusSettled || _finished)
        {
            return;
        }

        _focusSettled = true;
        _focusTimer.Start();
    }

    // 다른 창으로 포커스가 넘어가면(Alt+Tab 등) 전체 화면을 가린 채 남지 않도록 캡처를 취소한다.
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_activatedOnce && _focusSettled && !_finished)
        {
            DiagnosticLog.Write("overlay Deactivated -> Cancel scheduled");
            Dispatcher.BeginInvoke(Cancel);
        }
    }

    // 첫 폴링 전에 포커스를 얻었다가 잃는 경우도 놓치지 않도록, 활성화되는 시점에 바로 포그라운드 여부를 기록한다.
    private void MarkIfForeground()
    {
        if (_hwnd != IntPtr.Zero && NativeMethods.GetForegroundWindow() == _hwnd)
        {
            _focus.MarkForeground();
        }
    }

    // AttachThreadInput으로 포커스를 가져온 창은 다른 앱으로 전환돼도 Deactivated가 오지 않는 경우가 있어,
    // 실제 포그라운드 창을 주기적으로 확인한다. 한 번이라도 포그라운드였다가 연속으로 벗어나면 취소하고,
    // 제한 시간 안에 한 번도 포그라운드가 되지 못하면(권한이 높은 창이 포커스를 잡고 있는 경우 등) 알리고 취소한다.
    private void OnFocusTick(object? sender, EventArgs e)
    {
        if (_finished)
        {
            return;
        }

        _repeatGuard.ReleaseIfUp(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0);

        switch (_focus.Tick(NativeMethods.GetForegroundWindow() == _hwnd, _sinceShown.Elapsed))
        {
            case FocusVerdict.LostFocus:
                DiagnosticLog.Write("focus watch: LostFocus -> Cancel");
                Cancel();
                break;
            case FocusVerdict.NeverFocused:
                DiagnosticLog.Write("focus watch: NeverFocused -> Cancel");
                Cancel();
                FocusNotAcquired?.Invoke();
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _focusTimer.Stop();
        base.OnClosed(e);

        // 효과 중에 창이 먼저 닫혀도 이미 확정된 캡처는 저장돼야 하므로 완료 처리를 여기서도 한 번 보장한다.
        FinishEffect("OnClosed");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        _scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        PixelScale.ScaleX = 1 / _scale;
        PixelScale.ScaleY = 1 / _scale;
        ApplySizes();
        PlaceOnVirtualScreen();
        Render();
    }

    // 모니터 사이를 옮겨 다니며 WPF가 창을 임의로 리사이즈하지 못하게 DPI 변경 알림을 막는다.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDpiChanged)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void PlaceOnVirtualScreen() =>
        NativeMethods.SetWindowPos(_hwnd, HwndTopmost, _virtual.X, _virtual.Y, _virtual.Width, _virtual.Height, SwpNoActivate);

    private void ApplySizes()
    {
        Frame.StrokeThickness = 2 * _scale;
        LabelText.FontSize = 14 * _scale;
        Label.Padding = new Thickness(8 * _scale, 3 * _scale, 8 * _scale, 3 * _scale);
        HelpText.FontSize = 14 * _scale;
        Help.Padding = new Thickness(14 * _scale, 6 * _scale, 14 * _scale, 6 * _scale);
        Help.Margin = new Thickness(0, 0, 0, 24 * _scale);
        foreach (var square in _handles.Values)
        {
            square.Width = 9 * _scale;
            square.Height = 9 * _scale;
        }
    }

    // ---- 렌더 ----

    private void Render()
    {
        var rect = _selection.Rect;
        double x = rect.X - _virtual.X;
        double y = rect.Y - _virtual.Y;

        _dimHole.Rect = new Rect(x, y, rect.Width, rect.Height);

        Canvas.SetLeft(Frame, x);
        Canvas.SetTop(Frame, y);
        Frame.Width = rect.Width;
        Frame.Height = rect.Height;

        foreach (var (handle, square) in _handles)
        {
            var (hx, hy) = HandlePoint(rect, handle);
            Canvas.SetLeft(square, hx - _virtual.X - square.Width / 2);
            Canvas.SetTop(square, hy - _virtual.Y - square.Height / 2);
        }

        LabelText.Text = $"{_preset.Name}  {rect.Width}×{rect.Height}";
        Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelSize = Label.DesiredSize;
        var gap = 8 * _scale;
        var labelY = y - labelSize.Height - gap;
        if (labelY < 0)
        {
            labelY = y + gap;
        }

        Canvas.SetLeft(Label, Math.Clamp(x, 0, Math.Max(0, _virtual.Width - labelSize.Width)));
        Canvas.SetTop(Label, labelY);

        var monitor = PresetGeometry.MonitorContaining(rect, _monitors);
        var area = monitor?.Bounds ?? _virtual;
        Canvas.SetLeft(HelpHost, area.X - _virtual.X);
        Canvas.SetTop(HelpHost, area.Y - _virtual.Y);
        HelpHost.Width = area.Width;
        HelpHost.Height = area.Height;
        HelpText.MaxWidth = Math.Max(100, area.Width - 80 * _scale);
        HelpText.Text = _newRegionMode ? NewRegionHelpLine : HelpLine;
    }

    private static (int X, int Y) HandlePoint(PixelRect rect, ResizeHandle handle)
    {
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        return handle switch
        {
            ResizeHandle.TopLeft => (rect.X, rect.Y),
            ResizeHandle.Top => (centerX, rect.Y),
            ResizeHandle.TopRight => (rect.Right, rect.Y),
            ResizeHandle.Right => (rect.Right, centerY),
            ResizeHandle.BottomRight => (rect.Right, rect.Bottom),
            ResizeHandle.Bottom => (centerX, rect.Bottom),
            ResizeHandle.BottomLeft => (rect.X, rect.Bottom),
            _ => (rect.X, centerY),
        };
    }

    // ---- 키보드 ----

    // 한글 IME 등으로 ImeProcessed/System으로 들어온 키도 실제 키로 되돌린다.
    private static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.System => e.SystemKey,
        _ => e.Key,
    };

    private static bool IsAltKey(Key key) => key is Key.LeftAlt or Key.RightAlt;

    // 자동 반복을 걸러낼 키(가상 키 코드): Enter, Tab, R, S.
    private static readonly int[] GuardedKeys = [0x0D, 0x09, 0x52, 0x53];

    private static int VirtualKey(Key key) => System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);

    // Alt 단독 입력이 WPF/시스템 메뉴 모드로 들어가 다음 키 하나를 삼키지 않도록 막는다.
    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = RealKey(e);
        _repeatGuard.Release(VirtualKey(key));
        if (IsAltKey(key))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 확정(효과 재생 중 포함)·취소가 끝난 뒤의 입력은 전부 무시한다. 이중 확정, 프리셋 저장 같은 부작용을 막는다.
        if (_finished)
        {
            e.Handled = true;
            return;
        }

        var key = RealKey(e);
        if (IsAltKey(key))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var step = shift ? 10 : 1;

        switch (key)
        {
            case Key.Enter:
                // 단축키를 누른 채 유지할 때 생기는 자동 반복 입력이 동작으로 이어지지 않게 한다(Tab/R/S도 같다).
                if (_repeatGuard.ShouldAct(VirtualKey(key), e.IsRepeat))
                {
                    DiagnosticLog.Write("key Enter accepted -> Confirm");
                    Confirm();
                }
                else
                {
                    DiagnosticLog.Write($"key Enter ignored by repeat guard (IsRepeat={e.IsRepeat})");
                }

                break;
            case Key.Escape:
                Cancel();
                break;
            case Key.Tab:
                if (_repeatGuard.ShouldAct(VirtualKey(key), e.IsRepeat))
                {
                    SwitchPreset(shift ? -1 : 1);
                }

                break;
            case Key.R:
                if (_repeatGuard.ShouldAct(VirtualKey(key), e.IsRepeat))
                {
                    _newRegionMode = true;
                    Surface.Cursor = Cursors.Cross;
                }

                break;
            case Key.S:
                if (_repeatGuard.ShouldAct(VirtualKey(key), e.IsRepeat))
                {
                    SaveAsPreset();
                }

                break;
            case Key.Left:
                Nudge(-step, 0, ctrl);
                break;
            case Key.Right:
                Nudge(step, 0, ctrl);
                break;
            case Key.Up:
                Nudge(0, -step, ctrl);
                break;
            case Key.Down:
                Nudge(0, step, ctrl);
                break;
            default:
                return;
        }

        e.Handled = true;
        if (!_finished)
        {
            Render();
        }
    }

    private void Nudge(int dx, int dy, bool resize)
    {
        if (resize)
        {
            _selection.Resize(ResizeHandle.BottomRight, dx, dy);
        }
        else
        {
            _selection.Move(dx, dy);
        }
    }

    private void Confirm()
    {
        if (_finished)
        {
            DiagnosticLog.Write("Confirm ignored (already finished)");
            return;
        }

        _finished = true;
        DiagnosticLog.Write($"Confirm: selection={_selection.Rect.Width}x{_selection.Rect.Height}");

        // 마우스를 누른 채 확정해도 효과 중에 선택 영역이 움직이지 않게 드래그를 끝낸다.
        _drag = DragMode.None;
        Surface.ReleaseMouseCapture();
        Confirmed?.Invoke(_selection.Rect, _preset);
    }

    /// <summary>
    /// 확정 효과(테두리 펄스·퍼지는 링 + 시계 방향 혜성 빛)를 재생하고, 끝나면 onDone을 정확히 한 번 호출한다.
    /// 창이 먼저 닫히거나 효과 준비에 실패하면 즉시 호출한다. 닫기는 받는 쪽이 한다.
    /// 효과는 정지 이미지 위 오버레이에만 그려지며, 저장 PNG는 호출 전에 이미 만들어져 있어야 한다.
    /// </summary>
    public void PlayConfirmEffect(Action onDone)
    {
        _effectDone = onDone;
        _effectClock.Restart();
        DiagnosticLog.Write($"PlayConfirmEffect enter: rect={_selection.Rect.Width}x{_selection.Rect.Height} scale={_scale:0.##} finished={_finished} windowVisible={IsVisible}");

        // 사용자 결정으로 Windows 애니메이션 설정과 무관하게 항상 재생한다.
        try
        {
            StartEffect();
            DiagnosticLog.Write("StartEffect ok");
        }
        catch (Exception ex)
        {
            // 효과 실패가 캡처 저장을 막으면 안 된다.
            DiagnosticLog.Write($"StartEffect failed: {DiagnosticLog.Describe(ex, 3)}");
            FinishEffect("StartEffect exception");
        }
    }

    private void StartEffect()
    {
        var rect = _selection.Rect;
        double x = rect.X - _virtual.X;
        double y = rect.Y - _virtual.Y;
        var board = new Storyboard();
        _effect = board;

        // 효과 레이어는 선택 영역 + 여백 크기로 한정한다. 레이어 안 좌표는 (margin, margin)이 선택 영역의 왼쪽 위다.
        var margin = Math.Ceiling((RingSpread + EffectMargin) * _scale);
        PlaceLayer(Pulse, x, y, margin, rect.Width, rect.Height);
        PlaceLayer(Sweep, x, y, margin, rect.Width, rect.Height);

        AddFrameReaction(board, margin, rect.Width, rect.Height);
        AddRings(board, margin, rect.Width, rect.Height);
        AddSweep(board, margin, rect.Width, rect.Height, Frame.StrokeThickness / 2);

        DiagnosticLog.Write($"effect built: pulseChildren={Pulse.Children.Count} sweepChildren={Sweep.Children.Count} animations={board.Children.Count} duration={EffectDuration.TotalMilliseconds:0}ms guard={(EffectDuration + EffectGuardExtra).TotalMilliseconds:0}ms");
        board.Completed += OnEffectCompleted;
        StartFrameWatch();
        board.Begin(this, true);

        // 렌더링이 멈추는 등 Completed가 오지 않는 경우에도 화면이 가려진 채 입력이 막혀 남지 않도록 하는 안전망.
        _effectGuard = new System.Windows.Threading.DispatcherTimer { Interval = EffectDuration + EffectGuardExtra };
        _effectGuard.Tick += (_, _) => FinishEffect("guard timer");
        _effectGuard.Start();
        LogEffectState("after Begin");
    }

    private static void PlaceLayer(Canvas layer, double x, double y, double margin, int width, int height)
    {
        Canvas.SetLeft(layer, x - margin);
        Canvas.SetTop(layer, y - margin);
        layer.Width = width + 2 * margin;
        layer.Height = height + 2 * margin;
        layer.Visibility = Visibility.Visible;
    }

    // 테두리: 굵어지며 밝은 하늘색이 됐다가(피크 1회) 돌아온다(AutoReverse).
    // 발광은 Effect 대신 폭이 다른 반투명 테두리 겹으로, 밝은 화면용 어두운 외곽은 가장 안쪽 겹으로 깐다.
    // Rectangle의 선은 도형 안쪽으로 그려지므로, 바깥으로 outset만큼 키운 사각형에 두께 + outset을 주면 바깥쪽 발광만 생긴다.
    private void AddFrameReaction(Storyboard board, double margin, int width, int height)
    {
        var baseThickness = Frame.StrokeThickness;
        var peak = Math.Max(baseThickness, Math.Min(FramePeakThickness * _scale, Math.Min(width, height) * MaxThicknessRatio));
        var baseColor = (Frame.Stroke as SolidColorBrush)?.Color ?? GlowColor;
        Frame.Stroke = new SolidColorBrush(baseColor);

        (double Outset, double Opacity, Color Color)[] layers =
        [
            (FrameGlowWide * _scale, FrameGlowOpacity, GlowColor),
            (FrameGlowNarrow * _scale, FrameGlowNarrowOpacity, GlowColor),
            (FrameRimWidth * _scale, FrameRimOpacity, OutlineColor),
        ];
        foreach (var layer in layers)
        {
            var shape = new Rectangle
            {
                Width = width + 2 * layer.Outset,
                Height = height + 2 * layer.Outset,
                Stroke = Solid(layer.Color),
                StrokeThickness = baseThickness + layer.Outset,
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(shape, margin - layer.Outset);
            Canvas.SetTop(shape, margin - layer.Outset);
            Pulse.Children.Add(shape);

            AddAnimation(board, shape, new PropertyPath(Shape.StrokeThicknessProperty),
                baseThickness + layer.Outset, peak + layer.Outset, ThickenDuration, EaseOut).AutoReverse = true;
            AddAnimation(board, shape, new PropertyPath(UIElement.OpacityProperty),
                0, layer.Opacity, ThickenDuration, EaseOut).AutoReverse = true;
        }

        AddAnimation(board, Frame, new PropertyPath(Shape.StrokeThicknessProperty),
            baseThickness, peak, ThickenDuration, EaseOut).AutoReverse = true;
        var brighten = new ColorAnimation(baseColor, BrightFrameColor, ThickenDuration)
        {
            AutoReverse = true,
            EasingFunction = EaseOut,
        };
        Storyboard.SetTarget(brighten, Frame);
        Storyboard.SetTargetProperty(brighten, new PropertyPath("(0).(1)", Shape.StrokeProperty, SolidColorBrush.ColorProperty));
        board.Children.Add(brighten);
    }

    // 링: 선택 영역 바깥으로 퍼지며 가늘어지고 투명해진다. 첫 링은 즉시, 다음 링은 RingDelay씩 늦게 시작한다. 시작은 짧게 서서히 나타난다(RingFadeIn).
    // 겹(뒤→앞): 옅은 발광, 어두운 외곽, 포화된 파랑, 안쪽 흰 하이라이트. 어두운/밝은 화면 모두에서 보이게 하려는 구성이다.
    // 파랑 링의 중심선을 (퍼진 거리 + 두께 / 2)로 보내 안쪽 가장자리가 선택 영역 안으로 들어오지 않게 한다.
    // 발광·외곽처럼 링보다 두꺼운 겹은 중심선을 (겹 두께 - 링 두께) / 2만큼 더 바깥으로 보내 안쪽 가장자리를 링과 같게 맞추고 바깥으로만 번지게 한다.
    private void AddRings(Storyboard board, double margin, int width, int height)
    {
        var spread = RingSpread * _scale;
        var startThickness = RingStartThickness * _scale;
        var endThickness = RingEndThickness * _scale;

        (Color Color, double Opacity, double Factor, double Add)[] layers =
        [
            (GlowColor, RingGlowOpacity, RingGlowFactor, 0),
            (OutlineColor, RingOutlineOpacity, 1, RingOutlineWidth * _scale),
            (StrongBlue, RingStartOpacity, 1, 0),
            (Colors.White, RingHighlightOpacity, RingHighlightRatio, 0),
        ];
        for (var ring = 0; ring < RingCount; ring++)
        {
            var delay = RingDelay * ring;
            foreach (var layer in layers)
            {
                var layerStart = startThickness * layer.Factor + layer.Add;
                var layerEnd = endThickness * layer.Factor + layer.Add;

                // 링보다 가는 겹(하이라이트)은 링 안에 중심을 맞추고, 두꺼운 겹만 바깥으로 민다.
                var startOffset = startThickness / 2 + Math.Max(0, (layerStart - startThickness) / 2);
                var endOffset = spread + endThickness / 2 + Math.Max(0, (layerEnd - endThickness) / 2);
                var startRect = new Rect(margin - startOffset, margin - startOffset, width + 2 * startOffset, height + 2 * startOffset);
                var endRect = new Rect(margin - endOffset, margin - endOffset, width + 2 * endOffset, height + 2 * endOffset);

                var shape = new ShapePath
                {
                    Data = new RectangleGeometry(startRect),
                    Stroke = Solid(layer.Color),
                    StrokeThickness = layerStart,
                    Opacity = 0, // 지연 시작하는 링은 시작 전까지 보이지 않는다.
                    IsHitTestVisible = false,
                };
                Pulse.Children.Add(shape);

                var move = new RectAnimation(startRect, endRect, RingDuration) { EasingFunction = EaseOut, BeginTime = delay };
                Storyboard.SetTarget(move, shape);
                Storyboard.SetTargetProperty(move, new PropertyPath("(0).(1)", ShapePath.DataProperty, RectangleGeometry.RectProperty));
                board.Children.Add(move);

                AddAnimation(board, shape, new PropertyPath(Shape.StrokeThicknessProperty),
                    layerStart, layerEnd, RingDuration, EaseIn).BeginTime = delay;
                AddRingOpacity(board, shape, layer.Opacity, delay);
            }
        }
    }

    // 투명에서 RingFadeIn 동안 피크 밝기로 나타났다가 RingDuration까지 서서히 사라진다.
    private static void AddRingOpacity(Storyboard board, Shape shape, double peak, TimeSpan delay)
    {
        var opacity = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromTimeSpan(RingFadeIn)));
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(RingDuration), EaseIn));
        Storyboard.SetTarget(opacity, shape);
        Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));
        board.Children.Add(opacity);
    }

    // 12시 방향(상단 중앙)에서 출발해 상단→우측→하단→좌측 순서(시계 방향)로 이어지는 닫힌 경로 위에 혜성 모양 빛을 그린다.
    // 빛은 길이가 다른 겹을 머리를 맞춰 포갠 것이다. 긴 겹일수록 가늘고 푸르게 하고 짧은 겹을 위에 쌓으면,
    // 머리는 겹이 많이 겹쳐 진하고 굵고 희며 꼬리는 한 겹만 남아 옅고 가늘고 푸르러 조각 경계 없이 연속 그라데이션처럼 보인다.
    // 겹(뒤→앞): 넓고 옅은 헤일로, 어두운 외곽, 포화된 파랑, 흰색→하늘색→파랑 코어.
    // StrokeDashArray로 겹마다 대시 하나를 만들고 StrokeDashOffset을 줄여 경로 진행 방향(시계 방향)으로 보낸다.
    // 대시 패턴 위치 p는 경로의 p - offset 지점에 그려지므로 offset이 줄면 빛이 앞으로 움직인다.
    private void AddSweep(Storyboard board, double margin, int width, int height, double inset)
    {
        // 경로는 선택 영역 테두리 선(안쪽 inset)에 둔다. 영역이 작아 폭·높이가 최소 크기로 보정되면 시작 좌표도 함께 보정해
        // 경로가 선택 영역 가운데 정렬로 남게 한다(대시 계산이 0으로 나뉘지 않고, 영역 밖으로 밀리지 않는다).
        double w = Math.Max(SweepMinPathSize, width - 2 * inset);
        double h = Math.Max(SweepMinPathSize, height - 2 * inset);
        double left = margin + (width - w) / 2;
        double top = margin + (height - h) / 2;
        double perimeter = 2 * (w + h);
        double length = perimeter * SweepLengthFraction;
        double travel = perimeter * SweepLaps;

        // 작은 영역에서는 두께가 짧은 변의 일부를 넘지 않게 제한한다(길이는 둘레 비율이라 자연히 작아진다).
        var head = Math.Max(1, Math.Min(SweepHeadThickness * _scale, Math.Min(width, height) * MaxThicknessRatio));
        var tail = Math.Min(SweepTailThickness * _scale, head);
        var outline = 2 * SweepOutlineWidth * _scale;

        var figure = new PathFigure { StartPoint = new Point(left + w / 2, top), IsClosed = true, IsFilled = false };
        figure.Segments.Add(new LineSegment(new Point(left + w, top), true));
        figure.Segments.Add(new LineSegment(new Point(left + w, top + h), true));
        figure.Segments.Add(new LineSegment(new Point(left, top + h), true));
        figure.Segments.Add(new LineSegment(new Point(left, top), true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();

        AddCometLayers(board, geometry, perimeter, length, travel, SweepGlowLayers, head * SweepGlowFactor, tail * SweepGlowFactor, _ => GlowColor, SweepGlowOpacity);
        AddCometLayers(board, geometry, perimeter, length, travel, SweepOutlineLayers,
            head * SweepShellFactor + outline, tail * SweepShellFactor + outline, _ => OutlineColor, SweepOutlineOpacity);
        AddCometLayers(board, geometry, perimeter, length, travel, SweepShellLayers,
            head * SweepShellFactor, tail * SweepShellFactor, _ => ShellBlue, SweepShellOpacity);
        AddCometLayers(board, geometry, perimeter, length, travel, SweepCoreLayers, head, tail, CometCoreColor, SweepCoreOpacity);

        // 빠르게 나타나고 끝에서 사라진다.
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(EffectDuration * SweepFadeIn)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(EffectDuration * (1 - SweepFadeOut))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(EffectDuration)));
        Storyboard.SetTarget(fade, Sweep);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        board.Children.Add(fade);
    }

    // 머리를 맞춘 길이 다른 겹 count개를 긴 것부터 추가한다(짧은 겹이 위로 쌓인다). 위치 f는 0이 머리, 1이 꼬리 쪽이다.
    private void AddCometLayers(
        Storyboard board, Geometry geometry, double perimeter, double length, double travel,
        int count, double headThickness, double tailThickness, Func<double, Color> colorAt, double layerOpacity)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            var f = (i + 0.5) / count;
            var layerLength = length * (i + 1) / count;
            var thickness = headThickness + (tailThickness - headThickness) * f;

            // 대시 길이와 오프셋은 선 두께 단위다. 오프셋 (길이 - 머리 위치) / 두께일 때 머리가 경로의 그 위치에 놓인다.
            var dashes = new DoubleCollection([layerLength / thickness, (perimeter - layerLength) / thickness]);
            dashes.Freeze();
            var from = layerLength / thickness;
            var shape = new ShapePath
            {
                Data = geometry,
                Stroke = Solid(colorAt(f)),
                StrokeThickness = thickness,
                StrokeDashArray = dashes,
                StrokeDashCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeDashOffset = from,
                Opacity = layerOpacity,
                IsHitTestVisible = false,
            };
            Sweep.Children.Add(shape);
            AddAnimation(board, shape, new PropertyPath(Shape.StrokeDashOffsetProperty),
                from, (layerLength - travel) / thickness, EffectDuration, SweepEase);
        }
    }

    // 코어 색: 머리는 흰색, 중간은 하늘색, 꼬리는 포화된 파랑.
    private static Color CometCoreColor(double f) => f < CometMidPosition
        ? LerpColor(Colors.White, CometMidColor, f / CometMidPosition)
        : LerpColor(CometMidColor, StrongBlue, (f - CometMidPosition) / (1 - CometMidPosition));

    private static Color LerpColor(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static DoubleAnimation AddAnimation(
        Storyboard board, DependencyObject target, PropertyPath property, double from, double to, TimeSpan duration, IEasingFunction ease)
    {
        var animation = new DoubleAnimation(from, to, duration) { EasingFunction = ease };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
        return animation;
    }

    private void OnEffectCompleted(object? sender, EventArgs e) => FinishEffect("Storyboard.Completed");

    // 완료 이벤트·안전망 타이머·창 닫힘 중 무엇이 먼저 와도 한 번만 정리하고 알린다(이미 닫힌 뒤에 불려도 안전).
    // cause는 임시 진단 로그용 호출 원인이다.
    private void FinishEffect(string cause)
    {
        var done = _effectDone;
        if (done is null)
        {
            if (_effectClock.IsRunning)
            {
                DiagnosticLog.Write($"FinishEffect({cause}) ignored: already finished (+{_effectClock.ElapsedMilliseconds}ms)");
            }

            return;
        }

        _effectDone = null;
        _effectGuard?.Stop();
        _effectGuard = null;
        LogFinish(cause);
        if (_effect is { } board)
        {
            _effect = null;
            board.Completed -= OnEffectCompleted;
            try
            {
                board.Remove(this);
            }
            catch (InvalidOperationException)
            {
                // 시작되지 않았거나 이미 정리된 스토리보드.
            }
        }

        Sweep.Children.Clear();
        Pulse.Children.Clear();
        done();
    }

    // ---- 임시 진단(원인 확인 뒤 제거): 로그 전용. 효과 동작에는 관여하지 않고 모든 예외를 삼킨다 ----

    // 효과 시작 시 구독해 효과 동안 화면이 실제로 몇 번 그려졌는지 센다. FinishEffect에서 반드시 해제한다.
    private void StartFrameWatch()
    {
        try
        {
            if (_frameWatching)
            {
                return;
            }

            _frames = 0;
            _lastRenderingTime = TimeSpan.Zero;
            _firstFrameMs = _lastFrameMs = _gapSumMs = _gapMaxMs = 0;
            _frameClock.Restart();
            CompositionTarget.Rendering += OnRenderFrame;
            _frameWatching = true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"frame watch start failed: {DiagnosticLog.Describe(ex)}");
        }
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        try
        {
            // 한 프레임에 여러 번 올 수 있으므로 같은 RenderingTime은 한 번으로 센다.
            if (e is RenderingEventArgs args)
            {
                if (_frames > 0 && args.RenderingTime == _lastRenderingTime)
                {
                    return;
                }

                _lastRenderingTime = args.RenderingTime;
            }

            var now = _frameClock.Elapsed.TotalMilliseconds;
            if (_frames == 0)
            {
                _firstFrameMs = now;
            }
            else
            {
                var gap = now - _lastFrameMs;
                _gapSumMs += gap;
                _gapMaxMs = Math.Max(_gapMaxMs, gap);
            }

            _lastFrameMs = now;
            _frames++;
        }
        catch (Exception)
        {
            // 계측 실패는 무시한다.
        }
    }

    private string StopFrameWatch()
    {
        try
        {
            if (!_frameWatching)
            {
                return "frames=(계측 안 함)";
            }

            _frameWatching = false;
            CompositionTarget.Rendering -= OnRenderFrame;
            var avg = _frames > 1 ? _gapSumMs / (_frames - 1) : 0;
            return $"frames={_frames} firstFrame={_firstFrameMs:0}ms avgGap={avg:0.0}ms maxGap={_gapMaxMs:0.0}ms lastFrame={_lastFrameMs:0}ms";
        }
        catch (Exception ex)
        {
            return $"frames=(계측 실패 {DiagnosticLog.Describe(ex)})";
        }
    }

    private void LogEffectState(string when)
    {
        try
        {
            DiagnosticLog.Write(
                $"effect state ({when}): window IsVisible={IsVisible} IsActive={IsActive} WindowState={WindowState} Topmost={Topmost} renderTier={RenderCapability.Tier >> 16}"
                + $" | Pulse vis={Pulse.Visibility} opacity={Pulse.Opacity:0.##} IsVisible={Pulse.IsVisible} size={Pulse.ActualWidth:0}x{Pulse.ActualHeight:0} children={Pulse.Children.Count}"
                + $" | Sweep vis={Sweep.Visibility} opacity={Sweep.Opacity:0.##} IsVisible={Sweep.IsVisible} size={Sweep.ActualWidth:0}x{Sweep.ActualHeight:0} children={Sweep.Children.Count}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"effect state failed: {DiagnosticLog.Describe(ex)}");
        }
    }

    // FinishEffect가 처음 정리를 시작할 때 한 번: 원인·경과 시간·스토리보드 진행도·렌더 프레임 요약을 한 줄씩 남기고 구독을 해제한다.
    private void LogFinish(string cause)
    {
        string progress;
        try
        {
            progress = _effect is { } board
                ? $"state={board.GetCurrentState(this)} progress={board.GetCurrentProgress(this)?.ToString("0.00") ?? "null"}"
                : "storyboard=(없음)";
        }
        catch (Exception ex)
        {
            progress = $"storyboard=(조회 실패 {ex.GetType().Name})";
        }

        DiagnosticLog.Write($"FinishEffect cause={cause} elapsed={_effectClock.ElapsedMilliseconds}ms {progress} {StopFrameWatch()}");
        LogEffectState("at finish");
    }

    private void Cancel()
    {
        if (_finished)
        {
            return;
        }

        DiagnosticLog.Write("Cancel (no effect)");
        _finished = true;
        Close();
    }

    private void SwitchPreset(int step)
    {
        var next = PresetList.Cycle(_presets, _preset.Id, step);
        if (next is null)
        {
            return;
        }

        _preset = next;
        _selection.Set(PresetGeometry.Resolve(next, _monitors));
    }

    private void SaveAsPreset()
    {
        if (_savePreset(_selection.Rect) is { } saved)
        {
            _preset = saved;
        }
    }

    // ---- 마우스 ----

    private (int X, int Y) ToPixel(MouseEventArgs e)
    {
        var point = e.GetPosition(Surface);
        return (PixelMath.ToPixel(point.X) + _virtual.X, PixelMath.ToPixel(point.Y) + _virtual.Y);
    }

    private ResizeHandle? HitTestHandle(int x, int y)
    {
        var tolerance = 8 * _scale;
        foreach (var handle in HandleOrder)
        {
            var (hx, hy) = HandlePoint(_selection.Rect, handle);
            if (Math.Abs(x - hx) <= tolerance && Math.Abs(y - hy) <= tolerance)
            {
                return handle;
            }
        }

        return null;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_finished)
        {
            e.Handled = true;
            return;
        }

        if (!IsActive)
        {
            Activate();
        }

        Surface.Focus();

        var (x, y) = ToPixel(e);
        if (_newRegionMode)
        {
            _drag = DragMode.NewRegion;
            _selection.SetFromDrag(x, y, x, y);
        }
        else if (HitTestHandle(x, y) is { } handle)
        {
            _drag = DragMode.Resize;
            _dragHandle = handle;
        }
        else if (_selection.Rect.Contains(x, y))
        {
            _drag = DragMode.Move;
        }
        else
        {
            return;
        }

        _dragOrigin = _selection.Rect;
        _dragStart = (x, y);
        Surface.CaptureMouse();
        e.Handled = true;
        Render();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_finished)
        {
            return;
        }

        var (x, y) = ToPixel(e);
        switch (_drag)
        {
            case DragMode.Move:
                _selection.MoveFrom(_dragOrigin, x - _dragStart.X, y - _dragStart.Y);
                break;
            case DragMode.Resize:
                _selection.ResizeFrom(_dragOrigin, _dragHandle, x - _dragStart.X, y - _dragStart.Y);
                break;
            case DragMode.NewRegion:
                _selection.SetFromDrag(_dragStart.X, _dragStart.Y, x, y);
                break;
            default:
                Surface.Cursor = CursorAt(x, y);
                return;
        }

        Render();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_finished || _drag == DragMode.None)
        {
            return;
        }

        if (_drag == DragMode.NewRegion)
        {
            _newRegionMode = false;
        }

        _drag = DragMode.None;
        Surface.ReleaseMouseCapture();
        var (x, y) = ToPixel(e);
        Surface.Cursor = CursorAt(x, y);
        Render();
    }

    private Cursor CursorAt(int x, int y)
    {
        if (_newRegionMode)
        {
            return Cursors.Cross;
        }

        return HitTestHandle(x, y) switch
        {
            ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
            ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
            ResizeHandle.Top or ResizeHandle.Bottom => Cursors.SizeNS,
            ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
            _ => _selection.Rect.Contains(x, y) ? Cursors.SizeAll : Cursors.Arrow,
        };
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
