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

    // ---- 확정 효과(펄스 + 시계 방향 빛) 파라미터: 여기서만 조정한다 ----
    private static readonly TimeSpan EffectDuration = TimeSpan.FromMilliseconds(600);   // 전체 길이(빛이 한 바퀴 도는 시간)
    private static readonly TimeSpan RingDuration = TimeSpan.FromMilliseconds(350);     // 바깥으로 퍼지는 링
    private static readonly TimeSpan ThickenDuration = TimeSpan.FromMilliseconds(120);  // 테두리가 두꺼워지는 편도 시간(되돌아오는 시간 동일)
    private static readonly TimeSpan EffectGuardExtra = TimeSpan.FromMilliseconds(300); // 애니메이션이 끝나지 않을 때의 안전망 여유
    private const double RingSpread = 14;          // 링이 퍼지는 거리(물리 픽셀 기준, DPI 배율 적용)
    private const double ThickenFactor = 3;        // 테두리 최대 두께 배수
    private const double RingStartOpacity = 0.9;
    private const double SweepLengthFraction = 0.25; // 빛줄기 길이(둘레 대비)
    private const int SweepSegments = 8;             // 꼬리 그라데이션을 나누는 조각 수(조각마다 불투명도 감소)
    private static readonly Color BrightFrameColor = Color.FromRgb(0xA8, 0xD8, 0xFF);
    private static readonly Color SweepColor = Color.FromRgb(0xE6, 0xF6, 0xFF);

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

        // 링은 확정 효과에서 테두리 색을 따라간다(테두리 브러시는 효과 중 교체되므로 지금 같은 브러시를 잡아 둔다).
        PulseRing.Stroke = Frame.Stroke;

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
                Cancel();
                break;
            case FocusVerdict.NeverFocused:
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
        FinishEffect();
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
                    Confirm();
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
            return;
        }

        _finished = true;

        // 마우스를 누른 채 확정해도 효과 중에 선택 영역이 움직이지 않게 드래그를 끝낸다.
        _drag = DragMode.None;
        Surface.ReleaseMouseCapture();
        Confirmed?.Invoke(_selection.Rect, _preset);
    }

    /// <summary>
    /// 확정 효과(테두리 펄스 + 시계 방향 빛)를 재생하고, 끝나면 onDone을 정확히 한 번 호출한다.
    /// 창이 먼저 닫히거나 애니메이션이 꺼져 있거나 효과 준비에 실패하면 즉시 호출한다. 닫기는 받는 쪽이 한다.
    /// 효과는 정지 이미지 위 오버레이에만 그려지며, 저장 PNG는 호출 전에 이미 만들어져 있어야 한다.
    /// </summary>
    public void PlayConfirmEffect(Action onDone)
    {
        _effectDone = onDone;
        if (!SystemParameters.ClientAreaAnimation)
        {
            FinishEffect();
            return;
        }

        try
        {
            StartEffect();
        }
        catch (Exception)
        {
            // 효과 실패가 캡처 저장을 막으면 안 된다.
            FinishEffect();
        }
    }

    private void StartEffect()
    {
        var rect = _selection.Rect;
        double x = rect.X - _virtual.X;
        double y = rect.Y - _virtual.Y;
        var board = new Storyboard();
        _effect = board;

        // 1) 테두리: 두꺼워지며 밝아졌다가 원래대로 돌아온다(AutoReverse).
        var baseThickness = Frame.StrokeThickness;
        var baseColor = (Frame.Stroke as SolidColorBrush)?.Color ?? BrightFrameColor;
        Frame.Stroke = new SolidColorBrush(baseColor);
        var thicken = AddAnimation(board, Frame, new PropertyPath(Shape.StrokeThicknessProperty),
            baseThickness, baseThickness * ThickenFactor, ThickenDuration, new CubicEase { EasingMode = EasingMode.EaseOut });
        thicken.AutoReverse = true;
        var brighten = new ColorAnimation(baseColor, BrightFrameColor, ThickenDuration)
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(brighten, Frame);
        Storyboard.SetTargetProperty(brighten, new PropertyPath("(0).(1)", Shape.StrokeProperty, SolidColorBrush.ColorProperty));
        board.Children.Add(brighten);

        // 2) 링: 같은 모양의 테두리가 바깥으로 퍼지면서 투명해진다.
        var spread = RingSpread * _scale;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PulseRing.Visibility = Visibility.Visible;
        PulseRing.Opacity = RingStartOpacity;
        Canvas.SetLeft(PulseRing, x);
        Canvas.SetTop(PulseRing, y);
        PulseRing.Width = rect.Width;
        PulseRing.Height = rect.Height;
        PulseRing.StrokeThickness = baseThickness * 1.5;
        AddAnimation(board, PulseRing, new PropertyPath(Canvas.LeftProperty), x, x - spread, RingDuration, ease);
        AddAnimation(board, PulseRing, new PropertyPath(Canvas.TopProperty), y, y - spread, RingDuration, ease);
        AddAnimation(board, PulseRing, new PropertyPath(FrameworkElement.WidthProperty), rect.Width, rect.Width + 2 * spread, RingDuration, ease);
        AddAnimation(board, PulseRing, new PropertyPath(FrameworkElement.HeightProperty), rect.Height, rect.Height + 2 * spread, RingDuration, ease);
        AddAnimation(board, PulseRing, new PropertyPath(Shape.StrokeThicknessProperty), baseThickness * 1.5, baseThickness * 0.5, RingDuration, ease);
        AddAnimation(board, PulseRing, new PropertyPath(UIElement.OpacityProperty), RingStartOpacity, 0, RingDuration, new QuadraticEase { EasingMode = EasingMode.EaseIn });

        // 3) 시계 방향 빛.
        AddSweep(board, x, y, rect.Width, rect.Height, baseThickness / 2);

        board.Completed += OnEffectCompleted;
        board.Begin(this, true);

        // 렌더링이 멈추는 등 Completed가 오지 않는 경우에도 화면이 가려진 채 입력이 막혀 남지 않도록 하는 안전망.
        _effectGuard = new System.Windows.Threading.DispatcherTimer { Interval = EffectDuration + EffectGuardExtra };
        _effectGuard.Tick += (_, _) => FinishEffect();
        _effectGuard.Start();
    }

    // 12시 방향(상단 중앙)에서 출발해 상단→우측→하단→좌측 순서(시계 방향)로 이어지는 닫힌 경로를 그리고,
    // 그 위에 StrokeDashArray로 빛 조각 8개를 이어 붙인 뒤 StrokeDashOffset을 감소시켜 경로를 따라 앞으로 보낸다.
    // 대시 패턴의 위치 p는 경로의 p - offset 지점에 그려지므로, offset이 줄어들면 빛이 경로 진행 방향(시계 방향)으로 움직인다.
    private void AddSweep(Storyboard board, double x, double y, int width, int height, double inset)
    {
        // 폭·높이가 아주 작아도 대시 계산이 0으로 나뉘지 않도록 최소 1px로 보정한다.
        double left = x + inset;
        double top = y + inset;
        double w = Math.Max(1, width - 2 * inset);
        double h = Math.Max(1, height - 2 * inset);
        double perimeter = 2 * (w + h);
        double thickness = 3 * _scale;
        double segLength = perimeter * SweepLengthFraction / SweepSegments;

        var figure = new PathFigure { StartPoint = new Point(left + w / 2, top), IsClosed = true, IsFilled = false };
        figure.Segments.Add(new LineSegment(new Point(left + w, top), true));
        figure.Segments.Add(new LineSegment(new Point(left + w, top + h), true));
        figure.Segments.Add(new LineSegment(new Point(left, top + h), true));
        figure.Segments.Add(new LineSegment(new Point(left, top), true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();

        Sweep.Visibility = Visibility.Visible;
        var dashes = new DoubleCollection([segLength / thickness, (perimeter - segLength) / thickness]);
        dashes.Freeze();
        var travel = perimeter / thickness;
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        for (var i = 0; i < SweepSegments; i++)
        {
            // 조각 0이 맨 앞(밝음), 뒤로 갈수록 투명해지는 꼬리.
            var start = i * segLength / thickness;
            var piece = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(SweepColor),
                StrokeThickness = thickness,
                StrokeDashArray = dashes,
                StrokeDashCap = PenLineCap.Flat,
                StrokeDashOffset = start,
                Opacity = 1.0 - (double)i / SweepSegments,
                IsHitTestVisible = false,
            };
            Sweep.Children.Add(piece);
            AddAnimation(board, piece, new PropertyPath(Shape.StrokeDashOffsetProperty), start, start - travel, EffectDuration, ease);
        }

        // 시작과 끝은 부드럽게 나타나고 사라지게 한다.
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(EffectDuration * 0.15)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(EffectDuration * 0.85)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(EffectDuration)));
        Storyboard.SetTarget(fade, Sweep);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        board.Children.Add(fade);
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

    private void OnEffectCompleted(object? sender, EventArgs e) => FinishEffect();

    // 완료 이벤트·안전망 타이머·창 닫힘 중 무엇이 먼저 와도 한 번만 정리하고 알린다(이미 닫힌 뒤에 불려도 안전).
    private void FinishEffect()
    {
        var done = _effectDone;
        if (done is null)
        {
            return;
        }

        _effectDone = null;
        _effectGuard?.Stop();
        _effectGuard = null;
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
        done();
    }

    private void Cancel()
    {
        if (_finished)
        {
            return;
        }

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
