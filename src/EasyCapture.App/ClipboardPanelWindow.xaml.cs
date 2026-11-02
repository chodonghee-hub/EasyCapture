using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using EasyCapture.Core;
using Color = System.Windows.Media.Color;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace EasyCapture.App;

/// <summary>패널이 닫힌 이유. 서비스가 이 값으로 알림·붙여넣기를 이어간다.</summary>
public enum PanelOutcome
{
    /// <summary>그냥 닫혔다(Esc·포커스 잃음·폴더 열기 등).</summary>
    None,

    /// <summary>클립보드에만 복사했다.</summary>
    Copied,

    /// <summary>복사했고 이전 창에 붙여넣기까지 요청했다.</summary>
    CopiedForPaste,

    /// <summary>다른 앱으로 끌어다 놓았다.</summary>
    Dragged,

    /// <summary>제한 시간 안에 포커스를 얻지 못해 닫았다.</summary>
    FocusNotAcquired,
}

/// <param name="Outcome">닫힌 이유.</param>
/// <param name="ItemCount">복사·이동한 캡처 수.</param>
public readonly record struct PanelResult(PanelOutcome Outcome, int ItemCount);

/// <summary>
/// 캡처 보관소의 썸네일 그리드 팝업. 키보드·마우스로 다중 선택하고 복사·붙여넣기·끌어놓기·이름 변경·삭제·고정을 한다.
/// 포커스를 잃으면 닫힌다. 순수 선택·복사 계획 로직은 Core(PanelSelection 등)에 있고 여기서는 화면과 입력만 다룬다.
/// </summary>
public partial class ClipboardPanelWindow : Window
{
    private const string HintLine =
        "Enter 붙여넣기 · Ctrl+C 복사 · 드래그 앤 드롭 · F2 이름 변경 · Del 삭제 · Ctrl+P 고정 · Ctrl+A 전체 선택 · 우클릭 메뉴 · Esc 닫기";

    // 타일 하나가 차지하는 가로·세로(DIP): 타일 168×132 + 바깥 여백 4×2.
    private const double TileOuterWidth = 176;
    private const double TileOuterHeight = 140;

    // 자동 반복을 걸러낼 키(가상 키 코드): Enter, C, A, P, Delete, F2. 방향키는 반복 이동이 자연스러워 제외한다.
    private static readonly int[] GuardedKeys = [0x0D, 0x43, 0x41, 0x50, 0x2E, 0x71];

    private readonly CaptureLibrary _library;
    private readonly Func<CopyMode> _copyMode;
    private readonly PanelSelection _selection = new();
    private readonly ObservableCollection<PanelTile> _tiles = [];
    private readonly PixelRect _workArea;
    private readonly KeyRepeatGuard _repeatGuard = new();
    private readonly FocusMonitor _focus = new();
    private readonly Stopwatch _sinceShown = new();
    private readonly System.Windows.Threading.DispatcherTimer _focusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource _thumbnailCts = new();

    private IntPtr _hwnd;
    private PanelMode _mode;
    private List<Guid> _pendingDelete = [];
    private Guid _renameId;
    private int _suspendAutoClose;
    private bool _activatedOnce;
    private bool _focusSettled;
    private bool _closing;
    private PanelTile? _downTile;
    private bool _deferSelect;
    private Point _downPoint;

    private enum PanelMode
    {
        Normal,
        Rename,
        ConfirmDelete,
    }

    /// <summary>copyMode는 복사·끌어놓기 시점마다 호출해 현재 설정을 읽는다(설정이 바뀌어도 반영).</summary>
    public ClipboardPanelWindow(CaptureLibrary library, Func<CopyMode> copyMode)
    {
        InitializeComponent();

        _library = library;
        _copyMode = copyMode;
        Tiles.ItemsSource = _tiles;
        HintText.Text = HintLine;

        // 패널이 열릴 때 마우스 커서가 있는 모니터의 작업 영역(물리 픽셀). 앱이 PerMonitorV2라 Forms 좌표도 물리 픽셀이다.
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        _workArea = new PixelRect(area.X, area.Y, area.Width, area.Height);

        // 단축키를 누른 채 패널이 뜬 경우, 그 키(예: Enter가 단축키에 들어 있을 때)의 자동 반복이 동작으로 이어지지 않게 눌린 키를 기록한다.
        _repeatGuard.Arm(GuardedKeys.Where(WindowFocus.IsKeyDown));

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += (_, e) => _repeatGuard.Release(VirtualKey(RealKey(e)));
        Activated += (_, _) =>
        {
            _activatedOnce = true;
            MarkIfForeground();
        };
        Deactivated += OnDeactivated;
        ContentRendered += (_, _) => PlaceOnCursorMonitor();
        _focusTimer.Tick += OnFocusTick;

        Tiles.PreviewMouseLeftButtonDown += OnTilesMouseDown;
        Tiles.PreviewMouseMove += OnTilesMouseMove;
        Tiles.PreviewMouseLeftButtonUp += OnTilesMouseUp;
        Tiles.PreviewMouseRightButtonDown += OnTilesRightMouseDown;
        Tiles.PreviewMouseRightButtonUp += OnTilesRightMouseUp;
        TileMenu.Opened += (_, _) => _suspendAutoClose++;
        TileMenu.Closed += (_, _) => _suspendAutoClose = Math.Max(0, _suspendAutoClose - 1);
        MenuCopy.Click += (_, _) => CopySelection(paste: false);
        MenuPaste.Click += (_, _) => CopySelection(paste: true);
        MenuRename.Click += (_, _) => BeginRename();
        MenuOpenFolder.Click += (_, _) => OpenFolder();
        MenuSaveAs.Click += (_, _) => SaveAs();
        MenuPin.Click += (_, _) => TogglePin();
        MenuDelete.Click += (_, _) => BeginDelete();

        RenameBox.PreviewKeyDown += OnRenameKeyDown;
        DeleteConfirmButton.Click += (_, _) => ConfirmDelete();
        DeleteCancelButton.Click += (_, _) => EndMode();

        // 열 때마다 보관소를 다시 읽고, 가장 최근 캡처를 선택해 둔다(바로 Enter로 붙여넣을 수 있게).
        Reload();
        _selection.MoveTo(0, extend: false);
        ApplySelection();
    }

    /// <summary>닫힌 이유와 복사한 개수. Closed 이후에 읽는다.</summary>
    public PanelResult Result { get; private set; }

    /// <summary>이 창이 지금 포그라운드 창인지.</summary>
    public bool IsForeground => _hwnd != IntPtr.Zero && WindowFocus.GetForeground() == _hwnd;

    /// <summary>다른 앱이 활성인 상태에서도 앞으로 가져오고 키보드 포커스를 받는다. 못 얻어도 예외 없이 끝난다.</summary>
    public void BringToForeground()
    {
        if (_closing)
        {
            return;
        }

        if (!_sinceShown.IsRunning)
        {
            _sinceShown.Start();
        }

        WindowFocus.Acquire(_hwnd);
        Activate();
        Scroller.Focus();
        Keyboard.Focus(Scroller);
        MarkIfForeground();

        // 포커스를 얻는 과정의 일시적인 변화로 바로 닫히지 않도록, 활성화가 확정된 뒤부터 감시한다.
        Dispatcher.BeginInvoke(StartFocusWatch, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>안전하게 닫는다(이미 닫히는 중이면 무시). 앱 종료 때도 쓴다.</summary>
    public void Dismiss()
    {
        if (_closing)
        {
            return;
        }

        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // 닫히는 중인 창이면 무시한다.
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        PlaceOnCursorMonitor();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _focusTimer.Stop();
        _thumbnailCts.Cancel();
        _thumbnailCts.Dispose();
        base.OnClosed(e);
    }

    // ---- 위치·포커스 ----

    // 커서가 있는 모니터 작업 영역의 중앙에 놓는다. 다른 DPI의 모니터로 옮기면 WPF가 창 크기를 다시 맞추므로
    // 첫 배치(HWND 생성 직후) 뒤 첫 렌더링 후에 한 번 더, 실제 크기로 중앙을 다시 맞춘다.
    private void PlaceOnCursorMonitor()
    {
        if (_hwnd == IntPtr.Zero || !WindowFocus.TryGetBounds(_hwnd, out var current))
        {
            return;
        }

        WindowFocus.SetBounds(_hwnd, PanelPlacement.Center(_workArea, current.Width, current.Height));
    }

    private void StartFocusWatch()
    {
        if (_focusSettled || _closing)
        {
            return;
        }

        _focusSettled = true;
        _focusTimer.Start();
    }

    private void MarkIfForeground()
    {
        if (IsForeground)
        {
            _focus.MarkForeground();
        }
    }

    // 다른 창을 누르거나 Alt+Tab 하면 닫는다. 대화상자·끌어놓기·메뉴가 떠 있는 동안은 닫지 않는다.
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_activatedOnce && _focusSettled && !_closing)
        {
            Dispatcher.BeginInvoke(CloseIfFocusLost);
        }
    }

    private void CloseIfFocusLost()
    {
        if (!_closing && _suspendAutoClose == 0 && !IsActive)
        {
            Dismiss();
        }
    }

    // AttachThreadInput으로 포커스를 가져온 창은 Deactivated가 오지 않는 경우가 있어 실제 포그라운드 창도 주기적으로 확인한다.
    private void OnFocusTick(object? sender, EventArgs e)
    {
        if (_closing || _suspendAutoClose > 0)
        {
            return;
        }

        _repeatGuard.ReleaseIfUp(WindowFocus.IsKeyDown);

        switch (_focus.Tick(IsForeground, _sinceShown.Elapsed))
        {
            case FocusVerdict.LostFocus:
                Dismiss();
                break;
            case FocusVerdict.NeverFocused:
                Result = new PanelResult(PanelOutcome.FocusNotAcquired, 0);
                Dismiss();
                break;
        }
    }

    // ---- 목록·선택 ----

    // 보관소 변경 이벤트가 없으므로 열 때와 작업 후마다 다시 읽는다. 타일 객체는 id로 재사용해 스크롤 위치·썸네일을 유지한다.
    private void Reload()
    {
        var items = _library.Items;
        _selection.SetItems(items.Select(i => i.Id).ToList());

        var existing = _tiles.ToDictionary(t => t.Id);
        var newTiles = new List<PanelTile>(items.Count);
        var toLoad = new List<PanelTile>();
        foreach (var item in items)
        {
            if (existing.TryGetValue(item.Id, out var tile))
            {
                tile.Update(item);
            }
            else
            {
                tile = new PanelTile(item);
                toLoad.Add(tile);
            }

            newTiles.Add(tile);
        }

        var keep = newTiles.ToHashSet();
        for (var i = _tiles.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_tiles[i]))
            {
                _tiles.RemoveAt(i);
            }
        }

        if (!newTiles.SequenceEqual(_tiles))
        {
            _tiles.Clear();
            foreach (var tile in newTiles)
            {
                _tiles.Add(tile);
            }
        }

        if (toLoad.Count > 0)
        {
            _ = LoadThumbnailsAsync(toLoad, _thumbnailCts.Token);
        }

        ApplySelection();
    }

    // 썸네일은 백그라운드에서 한 장씩 읽어 오는 대로 채운다(창이 바로 뜨도록). 읽지 못하면 자리표시로 둔다.
    private async Task LoadThumbnailsAsync(List<PanelTile> tiles, CancellationToken token)
    {
        foreach (var tile in tiles)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!_library.TryGetFilePath(tile.Id, out var path))
            {
                tile.SetThumbnail(null, "파일 없음");
                continue;
            }

            var width = tile.Item.Width;
            var image = await Task.Run(() => CaptureImageLoader.TryLoadThumbnail(path, width));
            if (token.IsCancellationRequested)
            {
                return;
            }

            tile.SetThumbnail(image);
        }
    }

    private void ApplySelection()
    {
        var cursor = _selection.Cursor;
        foreach (var tile in _tiles)
        {
            tile.IsSelected = _selection.IsSelected(tile.Id);
            tile.IsCursor = cursor == tile.Id;
        }

        CountText.Text = _selection.Count > 0
            ? $"총 {_tiles.Count}개 · {_selection.Count}개 선택"
            : $"총 {_tiles.Count}개";
        EmptyText.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollCursorIntoView()
    {
        var index = _selection.CursorIndex;
        if (index >= 0 && index < _tiles.Count
            && Tiles.ItemContainerGenerator.ContainerFromItem(_tiles[index]) is FrameworkElement container)
        {
            container.BringIntoView();
        }
    }

    private int Columns => Math.Max(1, (int)(Tiles.ActualWidth / TileOuterWidth));

    private int VisibleRows => Math.Max(1, (int)(Scroller.ViewportHeight / TileOuterHeight));

    private List<PanelTile> SelectedTiles() => _tiles.Where(t => _selection.IsSelected(t.Id)).ToList();

    private List<CaptureItem> SelectedItems() => SelectedTiles().Select(t => t.Item).ToList();

    // ---- 키보드 ----

    // 한글 IME 등으로 ImeProcessed/System으로 들어온 키도 실제 키로 되돌린다.
    private static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.System => e.SystemKey,
        _ => e.Key,
    };

    private static int VirtualKey(Key key) => System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);

    private bool Act(Key key, KeyEventArgs e) => _repeatGuard.ShouldAct(VirtualKey(key), e.IsRepeat);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 이름 변경 중에는 입력 상자가 키를 처리한다(Enter/Esc는 OnRenameKeyDown).
        if (_mode == PanelMode.Rename || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return;
        }

        var key = RealKey(e);

        if (_mode == PanelMode.ConfirmDelete)
        {
            // 삭제 확인 중에는 다른 키가 목록을 건드리지 못하게 모두 삼킨다.
            switch (key)
            {
                case Key.Enter or Key.Y:
                    if (Act(key, e))
                    {
                        ConfirmDelete();
                    }

                    break;
                case Key.Escape or Key.N:
                    EndMode();
                    break;
            }

            e.Handled = true;
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (key)
        {
            case Key.Escape:
                Dismiss();
                break;
            case Key.Left:
                Navigate(() => _selection.MoveBy(-1, shift));
                break;
            case Key.Right:
                Navigate(() => _selection.MoveBy(1, shift));
                break;
            case Key.Up:
                Navigate(() => _selection.MoveRows(-1, Columns, shift));
                break;
            case Key.Down:
                Navigate(() => _selection.MoveRows(1, Columns, shift));
                break;
            case Key.PageUp:
                Navigate(() => _selection.MoveRows(-VisibleRows, Columns, shift));
                break;
            case Key.PageDown:
                Navigate(() => _selection.MoveRows(VisibleRows, Columns, shift));
                break;
            case Key.Home:
                Navigate(() => _selection.MoveTo(0, shift));
                break;
            case Key.End:
                Navigate(() => _selection.MoveTo(int.MaxValue, shift));
                break;
            case Key.A when ctrl:
                if (Act(key, e))
                {
                    Navigate(_selection.SelectAll);
                }

                break;
            case Key.C when ctrl && !shift:
                if (Act(key, e))
                {
                    ClearStatus();
                    CopySelection(paste: false);
                }

                break;
            case Key.Enter when !ctrl && !shift:
                // 단축키를 누른 채 유지할 때 생기는 자동 반복이 붙여넣기로 이어지지 않게 한다.
                if (Act(key, e))
                {
                    ClearStatus();
                    CopySelection(paste: true);
                }

                break;
            case Key.Delete:
                if (Act(key, e))
                {
                    ClearStatus();
                    BeginDelete();
                }

                break;
            case Key.F2:
                if (Act(key, e))
                {
                    ClearStatus();
                    BeginRename();
                }

                break;
            case Key.P when ctrl:
                if (Act(key, e))
                {
                    ClearStatus();
                    TogglePin();
                }

                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Navigate(Action move)
    {
        ClearStatus();
        move();
        ApplySelection();
        ScrollCursorIntoView();
    }

    // ---- 마우스·끌어놓기 ----

    // 클릭과 드래그를 구분한다. 이미 선택된 타일을 누르면 선택 정리를 마우스를 뗄 때까지 미뤄, 다중 선택을 그대로 끌 수 있게 한다.
    private void OnTilesMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode != PanelMode.Normal)
        {
            EndMode();
        }

        ClearStatus();
        Scroller.Focus();
        Keyboard.Focus(Scroller);

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _downTile = null;
        _deferSelect = false;

        var tile = FindTile(e.OriginalSource as DependencyObject);
        if (tile is null)
        {
            if (!ctrl && !shift)
            {
                _selection.Clear();
                ApplySelection();
            }

            return;
        }

        if (e.ClickCount >= 2 && !ctrl && !shift)
        {
            // 더블클릭 = Enter(복사 후 붙여넣기).
            if (!_selection.IsSelected(tile.Id))
            {
                _selection.SelectOnly(tile.Id);
                ApplySelection();
            }

            CopySelection(paste: true);
            e.Handled = true;
            return;
        }

        _downPoint = e.GetPosition(Tiles);
        _downTile = tile;
        if (ctrl || shift)
        {
            _selection.Click(tile.Id, ctrl, shift);
        }
        else if (_selection.IsSelected(tile.Id))
        {
            _deferSelect = true;
        }
        else
        {
            _selection.Click(tile.Id, ctrl: false, shift: false);
        }

        ApplySelection();
    }

    private void OnTilesMouseMove(object sender, MouseEventArgs e)
    {
        if (_downTile is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _downTile = null;
            return;
        }

        var position = e.GetPosition(Tiles);
        if (Math.Abs(position.X - _downPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _downPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var tile = _downTile;
        _downTile = null;
        _deferSelect = false;
        StartDrag(tile);
    }

    private void OnTilesMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_downTile is { } tile && _deferSelect)
        {
            _selection.Click(tile.Id, ctrl: false, shift: false);
            ApplySelection();
        }

        _downTile = null;
        _deferSelect = false;
    }

    private void OnTilesRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 선택되지 않은 타일을 우클릭하면 그 타일만 선택하고 메뉴를 연다. 이미 선택된 타일이면 선택을 유지한다.
        var tile = FindTile(e.OriginalSource as DependencyObject);
        if (tile is not null && !_selection.IsSelected(tile.Id))
        {
            _selection.SelectOnly(tile.Id);
            ApplySelection();
        }
    }

    private void StartDrag(PanelTile tile)
    {
        // 선택되지 않은 타일(예: Ctrl+클릭으로 막 해제한 타일)에서 시작하면 그 타일 하나를 선택하고 진행한다.
        if (!_selection.IsSelected(tile.Id))
        {
            _selection.SelectOnly(tile.Id);
            ApplySelection();
        }

        if (!CaptureClipboard.TryBuildDataObject(_copyMode(), _selection.SelectedIds, _library, forClipboard: false, out var data, out var count))
        {
            Reload();
            ShowStatus("끌어 놓을 파일을 찾지 못했습니다. 파일이 삭제되었을 수 있습니다.");
            return;
        }

        // 끌어놓는 동안 대상 앱으로 포커스가 넘어가도 패널이 닫히지 않게 한다.
        DragDropEffects effect;
        _suspendAutoClose++;
        try
        {
            effect = DragDrop.DoDragDrop(Tiles, data, DragDropEffects.Copy);
        }
        catch (ExternalException)
        {
            ShowStatus("끌어놓기를 시작하지 못했습니다.");
            return;
        }
        finally
        {
            _suspendAutoClose = Math.Max(0, _suspendAutoClose - 1);
        }

        if (effect != DragDropEffects.None)
        {
            Result = new PanelResult(PanelOutcome.Dragged, count);
            Dismiss();
        }
        else
        {
            CloseIfFocusLost();
        }
    }

    // 클릭된 요소에서 위로 올라가며 타일(DataContext가 PanelTile인 요소)을 찾는다. 빈 곳이면 null.
    private static PanelTile? FindTile(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: PanelTile tile })
            {
                return tile;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    // 메뉴는 자동으로 열리지 않게(ContextMenuService.IsEnabled=False) 해 두고 여기서 직접 연다.
    // 열기 직전에 선택 상태로 항목의 활성 여부를 정해야 하고, 선택이 없거나 이름 변경·삭제 확인 중에는 열지 않기 위해서다.
    private void OnTilesRightMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode != PanelMode.Normal || _selection.Count == 0)
        {
            return;
        }

        var state = PanelCommands.Evaluate(SelectedItems());
        MenuCopy.IsEnabled = state.CanCopy;
        MenuPaste.IsEnabled = state.CanCopy;
        MenuRename.IsEnabled = state.CanRename;
        MenuOpenFolder.IsEnabled = state.CanOpenFolder;
        MenuSaveAs.IsEnabled = state.CanSaveAs;
        MenuPin.IsEnabled = state.CanTogglePin;
        MenuPin.Header = state.PinTarget ? "고정 (Ctrl+P)" : "고정 해제 (Ctrl+P)";
        MenuDelete.IsEnabled = state.CanDelete;

        TileMenu.PlacementTarget = Tiles;
        TileMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        TileMenu.IsOpen = true;
        e.Handled = true;
    }

    // ---- 동작 ----

    private void CopySelection(bool paste)
    {
        var ids = _selection.SelectedIds;
        if (ids.Count == 0)
        {
            ShowStatus("복사할 캡처를 선택하세요.", error: false);
            return;
        }

        var result = CaptureClipboard.Copy(_copyMode(), ids, _library);
        switch (result.Status)
        {
            case ClipboardCopyStatus.Success:
                Result = new PanelResult(paste ? PanelOutcome.CopiedForPaste : PanelOutcome.Copied, result.ItemCount);
                Dismiss();
                break;
            case ClipboardCopyStatus.NothingToCopy:
                Reload();
                ShowStatus("복사할 파일을 찾지 못했습니다. 파일이 삭제되었을 수 있습니다.");
                break;
            default:
                ShowStatus("클립보드를 사용할 수 없습니다. 다른 프로그램이 사용 중일 수 있으니 잠시 후 다시 시도하세요.");
                break;
        }
    }

    private void BeginRename()
    {
        var items = SelectedItems();
        if (!PanelCommands.Evaluate(items).CanRename)
        {
            return;
        }

        _renameId = items[0].Id;
        RenameBox.Text = items[0].Name;
        _mode = PanelMode.Rename;
        ShowMode();
        RenameBox.Focus();
        Keyboard.Focus(RenameBox);
        RenameBox.SelectAll();
    }

    // IME 조합 중 Enter는 e.Key가 ImeProcessed로 오므로 RealKey를 쓰지 않는다(조합이 끝난 뒤의 진짜 Enter만 확정으로 본다).
    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (!e.IsRepeat)
            {
                // 확정 뒤 Enter를 계속 누르면 일반 모드의 Enter(붙여넣기)로 이어지므로, 이 Enter를 눌린 키로 기록해 반복을 막는다.
                _repeatGuard.ShouldAct(VirtualKey(Key.Enter), isRepeat: false);
                CommitRename();
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndMode();
        }
    }

    private void CommitRename()
    {
        var name = RenameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowStatus("이름을 입력하세요.");
            return;
        }

        bool renamed;
        try
        {
            renamed = _library.Rename(_renameId, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 인덱스 저장 실패. 보관소가 이름을 원래대로 되돌렸으므로 목록을 다시 읽어 맞춘다.
            EndMode();
            Reload();
            ShowStatus($"이름 변경을 저장하지 못했습니다: {ex.Message}");
            return;
        }
        catch (InvalidOperationException ex)
        {
            // 되돌리기마저 실패해 파일이 임시 이름에 남은 경우. 메시지에 위치가 들어 있으니 그대로 알린다.
            EndMode();
            Reload();
            ShowStatus(ex.Message);
            return;
        }

        if (!renamed)
        {
            // 입력 상자는 열어 둬서 이름을 고쳐 다시 시도하게 한다.
            ShowStatus("이름을 바꾸지 못했습니다. 사용할 수 없는 이름이거나 파일이 사용 중입니다.");
            return;
        }

        EndMode();
        Reload();
    }

    // 삭제는 파일이 영구 삭제(휴지통 없음)되고 키 한 번·자동 반복으로 실수하기 쉬우므로,
    // 선택 개수·고정 여부와 관계없이 항상 패널 안에서 확인한다(별도 대화상자는 포커스를 가져가 패널이 닫히므로 쓰지 않는다).
    private void BeginDelete()
    {
        var items = SelectedItems();
        if (!PanelCommands.Evaluate(items).CanDelete)
        {
            return;
        }

        _pendingDelete = items.Select(i => i.Id).ToList();
        DeleteText.Text = PanelCommands.BuildDeleteMessage(items);
        _mode = PanelMode.ConfirmDelete;
        ShowMode();
    }

    private void ConfirmDelete()
    {
        var ids = _pendingDelete;
        EndMode();

        var failed = 0;
        foreach (var id in ids)
        {
            try
            {
                if (!_library.Delete(id))
                {
                    failed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 인덱스 저장 실패. 계속 진행하되 실패로 센다.
                failed++;
            }
        }

        Reload();
        if (failed > 0)
        {
            ShowStatus($"{failed}개를 삭제하지 못했습니다. 파일이 사용 중이거나 목록을 저장하지 못했습니다.");
        }
    }

    private void TogglePin()
    {
        var items = SelectedItems();
        var state = PanelCommands.Evaluate(items);
        if (!state.CanTogglePin)
        {
            return;
        }

        var failed = false;
        foreach (var item in items)
        {
            try
            {
                if (!_library.SetPinned(item.Id, state.PinTarget))
                {
                    failed = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 인덱스 저장 실패(보관소가 값을 되돌렸다). 같은 이유로 나머지도 실패할 가능성이 커 멈춘다.
                failed = true;
                break;
            }
        }

        Reload();
        if (failed)
        {
            ShowStatus("고정 상태를 저장하지 못했습니다.");
        }
    }

    private void OpenFolder()
    {
        var items = SelectedItems();
        if (!PanelCommands.Evaluate(items).CanOpenFolder)
        {
            return;
        }

        if (!_library.TryGetFilePath(items[0].Id, out var path))
        {
            Reload();
            ShowStatus("파일을 찾지 못했습니다.");
            return;
        }

        try
        {
            // 경로는 보관소가 준 것만 쓰고, 공백이 있어도 한 인수로 가도록 따옴표로 감싼다.
            using var process = Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ShowStatus("탐색기를 열지 못했습니다.");
            return;
        }

        Dismiss();
    }

    private void SaveAs()
    {
        var items = SelectedItems();
        if (!PanelCommands.Evaluate(items).CanSaveAs)
        {
            return;
        }

        var item = items[0];
        if (!_library.TryGetFilePath(item.Id, out var source))
        {
            Reload();
            ShowStatus("파일을 찾지 못했습니다.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "다른 이름으로 저장",
            Filter = "PNG 이미지 (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = item.Name,
            OverwritePrompt = true,
        };

        // 대화상자가 포커스를 가져가도 패널이 닫히지 않게 한다.
        bool? picked;
        _suspendAutoClose++;
        try
        {
            picked = dialog.ShowDialog(this);
        }
        finally
        {
            _suspendAutoClose = Math.Max(0, _suspendAutoClose - 1);
        }

        if (picked != true)
        {
            return;
        }

        try
        {
            var destination = dialog.FileName;
            if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus("원본과 같은 파일입니다. 다른 위치나 이름을 고르세요.");
                return;
            }

            // 원본은 그대로 두고 복사한다.
            File.Copy(source, destination, overwrite: true);
            ShowStatus($"저장했습니다: {destination}", error: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowStatus($"저장하지 못했습니다: {ex.Message}");
        }
    }

    // ---- 바닥 영역 ----

    private void ShowMode()
    {
        RenameBar.Visibility = _mode == PanelMode.Rename ? Visibility.Visible : Visibility.Collapsed;
        DeleteBar.Visibility = _mode == PanelMode.ConfirmDelete ? Visibility.Visible : Visibility.Collapsed;
        HintText.Visibility = _mode == PanelMode.Normal ? Visibility.Visible : Visibility.Collapsed;
    }

    // 이름 변경·삭제 확인을 끝내고 키보드 포커스를 목록으로 돌린다.
    private void EndMode()
    {
        _mode = PanelMode.Normal;
        ShowMode();
        Scroller.Focus();
        Keyboard.Focus(Scroller);
    }

    private void ShowStatus(string message, bool error = true)
    {
        StatusText.Text = message;
        StatusText.Foreground = error
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x6B))
            : new SolidColorBrush(Color.FromRgb(0x8F, 0xD1, 0x8F));
        StatusText.Visibility = Visibility.Visible;
    }

    private void ClearStatus()
    {
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
    }
}
