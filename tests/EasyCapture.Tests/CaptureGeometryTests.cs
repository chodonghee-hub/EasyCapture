using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class PixelRectTests
{
    [Fact]
    public void RightBottom_AreExclusiveEdges()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        Assert.Equal(40, rect.Right);
        Assert.Equal(60, rect.Bottom);
        Assert.True(rect.Contains(10, 20));
        Assert.True(rect.Contains(39, 59));
        Assert.False(rect.Contains(40, 59));
        Assert.False(rect.Contains(39, 60));
    }

    [Fact]
    public void Intersect_Overlap_And_Disjoint()
    {
        var a = new PixelRect(0, 0, 100, 100);
        Assert.Equal(new PixelRect(50, 60, 50, 40), a.Intersect(new PixelRect(50, 60, 200, 200)));
        Assert.True(a.Intersect(new PixelRect(100, 0, 10, 10)).IsEmpty);
        Assert.True(a.Intersect(new PixelRect(-50, -50, 10, 10)).IsEmpty);
    }

    [Fact]
    public void Union_IgnoresEmpty()
    {
        var a = new PixelRect(-1920, 0, 1920, 1080);
        var b = new PixelRect(0, 0, 2560, 1440);
        Assert.Equal(new PixelRect(-1920, 0, 4480, 1440), a.Union(b));
        Assert.Equal(a, PixelRect.Empty.Union(a));
        Assert.Equal(a, a.Union(PixelRect.Empty));
    }

    [Fact]
    public void Offset_MovesOnly()
    {
        Assert.Equal(new PixelRect(3, -2, 10, 20), new PixelRect(1, 1, 10, 20).Offset(2, -3));
    }

    [Theory]
    [InlineData(-50, 10, 0, 10)]
    [InlineData(10, -50, 10, 0)]
    [InlineData(950, 10, 900, 10)]
    [InlineData(10, 950, 10, 900)]
    [InlineData(20, 30, 20, 30)]
    public void Clamp_ShiftsInside(int x, int y, int expectedX, int expectedY)
    {
        var result = new PixelRect(x, y, 100, 100).Clamp(new PixelRect(0, 0, 1000, 1000));
        Assert.Equal(new PixelRect(expectedX, expectedY, 100, 100), result);
    }

    [Fact]
    public void Clamp_ShrinksWhenLargerThanBounds()
    {
        var result = new PixelRect(-10, -10, 5000, 300).Clamp(new PixelRect(-100, 50, 1000, 200));
        Assert.Equal(new PixelRect(-100, 50, 1000, 200), result);
    }
}

public sealed class PresetGeometryTests
{
    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", new PixelRect(0, 0, 1920, 1080), true);
    private static readonly MonitorInfo Left = new(@"\\.\DISPLAY2", new PixelRect(-1280, -200, 1280, 1024), false);

    private static CapturePreset Preset(string? monitor, int x, int y, int w, int h) =>
        new() { Name = "p", MonitorDeviceName = monitor, X = x, Y = y, Width = w, Height = h };

    [Fact]
    public void VirtualBounds_UnionIncludesNegativeMonitor()
    {
        Assert.Equal(new PixelRect(-1280, -200, 3200, 1280), PresetGeometry.VirtualBounds([Primary, Left]));
        Assert.True(PresetGeometry.VirtualBounds([]).IsEmpty);
    }

    [Fact]
    public void Resolve_NullMonitor_UsesPrimary()
    {
        var rect = PresetGeometry.Resolve(Preset(null, 200, 150, 320, 200), [Left, Primary]);
        Assert.Equal(new PixelRect(200, 150, 320, 200), rect);
    }

    [Fact]
    public void Resolve_NamedMonitor_AddsMonitorOrigin_NegativeCoordinates()
    {
        var rect = PresetGeometry.Resolve(Preset(@"\\.\display2", 100, 50, 300, 200), [Primary, Left]);
        Assert.Equal(new PixelRect(-1180, -150, 300, 200), rect);
    }

    [Fact]
    public void Resolve_UnknownMonitorName_FallsBackToPrimary()
    {
        var rect = PresetGeometry.Resolve(Preset(@"\\.\DISPLAY9", 10, 20, 300, 200), [Left, Primary]);
        Assert.Equal(new PixelRect(10, 20, 300, 200), rect);
    }

    [Fact]
    public void Resolve_NoPrimaryFlag_UsesFirstMonitor()
    {
        var onlyLeft = new MonitorInfo(@"\\.\DISPLAY5", new PixelRect(500, 500, 800, 600), false);
        var rect = PresetGeometry.Resolve(Preset(null, 10, 20, 100, 100), [onlyLeft]);
        Assert.Equal(new PixelRect(510, 520, 100, 100), rect);
    }

    [Fact]
    public void Resolve_OffScreenPreset_IsClampedIntoMonitor()
    {
        var rect = PresetGeometry.Resolve(Preset(null, 1800, 1000, 400, 300), [Primary]);
        Assert.Equal(new PixelRect(1520, 780, 400, 300), rect);

        var negative = PresetGeometry.Resolve(Preset(null, -500, -500, 400, 300), [Primary]);
        Assert.Equal(new PixelRect(0, 0, 400, 300), negative);
    }

    [Fact]
    public void Resolve_PresetLargerThanMonitor_IsShrunk()
    {
        var rect = PresetGeometry.Resolve(Preset(null, 0, 0, 5000, 4000), [Primary]);
        Assert.Equal(Primary.Bounds, rect);
    }

    [Fact]
    public void Resolve_NoMonitors_KeepsCoordinates()
    {
        Assert.Equal(new PixelRect(5, 6, 7, 8), PresetGeometry.Resolve(Preset(null, 5, 6, 7, 8), []));
    }

    [Fact]
    public void ToRelative_UsesMonitorWithLargestOverlap()
    {
        var (name, x, y) = PresetGeometry.ToRelative(new PixelRect(-1000, 0, 500, 300), [Primary, Left]);
        Assert.Equal(@"\\.\DISPLAY2", name);
        Assert.Equal(280, x);
        Assert.Equal(200, y);

        var spanning = PresetGeometry.ToRelative(new PixelRect(-100, 100, 600, 200), [Primary, Left]);
        Assert.Equal(@"\\.\DISPLAY1", spanning.DeviceName);
        Assert.Equal(-100, spanning.X);
        Assert.Equal(100, spanning.Y);
    }

    [Fact]
    public void ToRelative_NoOverlap_UsesPrimary_AndNoMonitorsGivesNullName()
    {
        var far = PresetGeometry.ToRelative(new PixelRect(9000, 9000, 10, 10), [Left, Primary]);
        Assert.Equal(@"\\.\DISPLAY1", far.DeviceName);

        var none = PresetGeometry.ToRelative(new PixelRect(5, 6, 10, 10), []);
        Assert.Null(none.DeviceName);
        Assert.Equal(5, none.X);
    }

    [Fact]
    public void Resolve_SpanningRegion_RoundTripsSaveThenRestore()
    {
        // 두 모니터 경계(X=0)를 가로지르는 영역: 저장 → 복원이 같아야 한다.
        var saved = new PixelRect(-100, 100, 600, 200);
        var (name, x, y) = PresetGeometry.ToRelative(saved, [Primary, Left]);
        var restored = PresetGeometry.Resolve(Preset(name, x, y, saved.Width, saved.Height), [Primary, Left]);
        Assert.Equal(saved, restored);
    }

    [Fact]
    public void Resolve_SpanningRegion_NotMovedWhenMonitorNameUnknown()
    {
        // 이름을 못 찾으면 주 모니터 기준으로 위치를 해석하지만 클램프는 가상 화면이다.
        var rect = PresetGeometry.Resolve(Preset(@"\\.\DISPLAY9", -100, 100, 600, 200), [Left, Primary]);
        Assert.Equal(new PixelRect(-100, 100, 600, 200), rect);
    }

    [Fact]
    public void Resolve_RegionLargerThanOneMonitor_IsKeptWithinVirtualScreen()
    {
        var rect = PresetGeometry.Resolve(Preset(null, -600, 100, 2500, 300), [Primary, Left]);
        Assert.Equal(new PixelRect(-600, 100, 2500, 300), rect);
    }

    [Fact]
    public void Resolve_OutsideVirtualScreen_IsClampedToVirtualScreen()
    {
        var virtualBounds = PresetGeometry.VirtualBounds([Primary, Left]);

        var farRight = PresetGeometry.Resolve(Preset(null, 5000, 100, 400, 300), [Primary, Left]);
        Assert.Equal(new PixelRect(virtualBounds.Right - 400, 100, 400, 300), farRight);

        var farLeftTop = PresetGeometry.Resolve(Preset(null, -9000, -9000, 400, 300), [Primary, Left]);
        Assert.Equal(new PixelRect(virtualBounds.X, virtualBounds.Y, 400, 300), farLeftTop);

        var huge = PresetGeometry.Resolve(Preset(null, 0, 0, 9000, 9000), [Primary, Left]);
        Assert.Equal(virtualBounds, huge);
    }

    [Fact]
    public void Resolve_ThenToRelative_RoundTrips()
    {
        var preset = Preset(@"\\.\DISPLAY2", 100, 50, 300, 200);
        var rect = PresetGeometry.Resolve(preset, [Primary, Left]);
        var (name, x, y) = PresetGeometry.ToRelative(rect, [Primary, Left]);
        Assert.Equal(@"\\.\DISPLAY2", name);
        Assert.Equal(100, x);
        Assert.Equal(50, y);
    }
}

public sealed class CaptureSelectionTests
{
    private static readonly PixelRect Bounds = new(-500, -100, 2500, 1200);

    private static CaptureSelection Create(int x = 100, int y = 100, int w = 50, int h = 50) =>
        new(Bounds, new PixelRect(x, y, w, h));

    [Fact]
    public void Constructor_EnforcesMinSizeAndClamps()
    {
        Assert.Equal(new PixelRect(100, 100, 8, 8), Create(100, 100, 1, 0).Rect);
        Assert.Equal(new PixelRect(-500, -100, 8, 8), new CaptureSelection(Bounds, new PixelRect(-9000, -9000, 3, 3)).Rect);
        Assert.Equal(Bounds, new CaptureSelection(Bounds, new PixelRect(0, 0, 99999, 99999)).Rect);
    }

    [Fact]
    public void Move_ClampsToBounds_KeepsSize()
    {
        var selection = Create();
        selection.Move(-5000, -5000);
        Assert.Equal(new PixelRect(-500, -100, 50, 50), selection.Rect);
        selection.Move(99999, 99999);
        Assert.Equal(new PixelRect(1950, 1050, 50, 50), selection.Rect);
    }

    [Fact]
    public void MoveFrom_UsesOrigin_NotAccumulatedClamp()
    {
        var selection = Create();
        var origin = selection.Rect;
        selection.MoveFrom(origin, 99999, 0);
        selection.MoveFrom(origin, 10, 5);
        Assert.Equal(new PixelRect(110, 105, 50, 50), selection.Rect);
    }

    [Theory]
    [InlineData(ResizeHandle.Right, 20, 99, 100, 100, 70, 50)]
    [InlineData(ResizeHandle.Bottom, 99, 20, 100, 100, 50, 70)]
    [InlineData(ResizeHandle.BottomRight, 5, 6, 100, 100, 55, 56)]
    [InlineData(ResizeHandle.Left, -10, 99, 90, 100, 60, 50)]
    [InlineData(ResizeHandle.Top, 99, -10, 100, 90, 50, 60)]
    [InlineData(ResizeHandle.TopLeft, -10, -20, 90, 80, 60, 70)]
    [InlineData(ResizeHandle.TopRight, 10, -20, 100, 80, 60, 70)]
    [InlineData(ResizeHandle.BottomLeft, -10, 20, 90, 100, 60, 70)]
    public void Resize_EachHandle(ResizeHandle handle, int dx, int dy, int x, int y, int w, int h)
    {
        var selection = Create();
        selection.Resize(handle, dx, dy);
        Assert.Equal(new PixelRect(x, y, w, h), selection.Rect);
    }

    [Fact]
    public void Resize_EdgeHandleIgnoresOtherAxis()
    {
        var selection = Create();
        selection.Resize(ResizeHandle.Right, 10, 500);
        Assert.Equal(new PixelRect(100, 100, 60, 50), selection.Rect);
    }

    [Fact]
    public void Resize_ShrinkStopsAtMinSize()
    {
        var selection = Create();
        selection.Resize(ResizeHandle.BottomRight, -49, -3);
        Assert.Equal(new PixelRect(100, 100, 8, 47), selection.Rect);
        selection.Resize(ResizeHandle.BottomRight, -3, -40);
        Assert.Equal(new PixelRect(100, 100, 8, 8), selection.Rect);
    }

    [Fact]
    public void Resize_LeftHandlePastRight_FlipsAroundRightEdge()
    {
        var selection = Create(100, 100, 50, 50);
        selection.Resize(ResizeHandle.Left, 80, 0);
        Assert.Equal(new PixelRect(150, 100, 30, 50), selection.Rect);
    }

    [Fact]
    public void Resize_TopHandlePastBottom_Flips()
    {
        var selection = Create(100, 100, 50, 50);
        selection.Resize(ResizeHandle.Top, 0, 70);
        Assert.Equal(new PixelRect(100, 150, 50, 20), selection.Rect);
    }

    [Fact]
    public void Resize_RightHandlePastLeft_Flips_AndAnchorKept()
    {
        var selection = Create(100, 100, 50, 50);
        selection.Resize(ResizeHandle.Right, -80, 0);
        Assert.Equal(new PixelRect(70, 100, 30, 50), selection.Rect);
    }

    [Fact]
    public void Resize_FlipJustPastAnchor_KeepsMinSizeOnNewSide()
    {
        var selection = Create(100, 100, 50, 50);
        selection.Resize(ResizeHandle.Left, 51, 0);
        Assert.Equal(new PixelRect(150, 100, 8, 50), selection.Rect);
    }

    [Fact]
    public void Resize_ClampsAtBoundsEdges()
    {
        var selection = Create(100, 100, 50, 50);
        selection.Resize(ResizeHandle.BottomRight, 99999, 99999);
        Assert.Equal(new PixelRect(100, 100, 1900, 1000), selection.Rect);

        selection.Resize(ResizeHandle.TopLeft, -99999, -99999);
        Assert.Equal(new PixelRect(-500, -100, 2500, 1200), selection.Rect);
    }

    [Fact]
    public void Resize_FlipAtBoundsEdge_StaysInside()
    {
        var selection = new CaptureSelection(Bounds, new PixelRect(1950, 100, 50, 50));
        selection.Resize(ResizeHandle.Left, 99999, 0);
        Assert.Equal(new PixelRect(2000 - 8, 100, 8, 50), selection.Rect);
    }

    [Fact]
    public void ResizeFrom_UsesOrigin_SoDragBackRestores()
    {
        var selection = Create();
        var origin = selection.Rect;
        selection.ResizeFrom(origin, ResizeHandle.Right, 500, 0);
        selection.ResizeFrom(origin, ResizeHandle.Right, 10, 0);
        Assert.Equal(new PixelRect(100, 100, 60, 50), selection.Rect);
    }

    [Theory]
    [InlineData(10, 10, 60, 40)]
    [InlineData(60, 40, 10, 10)]
    [InlineData(10, 40, 60, 10)]
    [InlineData(60, 10, 10, 40)]
    public void SetFromDrag_AnyDirection(int x1, int y1, int x2, int y2)
    {
        var selection = Create();
        selection.SetFromDrag(x1, y1, x2, y2);
        Assert.Equal(new PixelRect(10, 10, 50, 30), selection.Rect);
    }

    [Fact]
    public void SetFromDrag_TinyDrag_GetsMinSize_AndClampsPoints()
    {
        var selection = Create();
        selection.SetFromDrag(300, 300, 301, 300);
        Assert.Equal(new PixelRect(300, 300, 8, 8), selection.Rect);

        selection.SetFromDrag(1999, 1099, 1999, 1099);
        Assert.Equal(new PixelRect(1992, 1092, 8, 8), selection.Rect);

        selection.SetFromDrag(-9999, -9999, 50, 50);
        Assert.Equal(new PixelRect(-500, -100, 550, 150), selection.Rect);
    }
}

public sealed class PresetListTests
{
    private static List<CapturePreset> Presets(int count) =>
        Enumerable.Range(0, count).Select(i => new CapturePreset { Id = Guid.NewGuid(), Name = $"p{i}" }).ToList();

    [Fact]
    public void Cycle_Empty_ReturnsNull()
    {
        Assert.Null(PresetList.Cycle([], null, 1));
    }

    [Fact]
    public void Cycle_Single_StaysOnSame()
    {
        var presets = Presets(1);
        Assert.Same(presets[0], PresetList.Cycle(presets, presets[0].Id, 1));
        Assert.Same(presets[0], PresetList.Cycle(presets, presets[0].Id, -1));
    }

    [Fact]
    public void Cycle_Multiple_WrapsBothWays()
    {
        var presets = Presets(3);
        Assert.Same(presets[1], PresetList.Cycle(presets, presets[0].Id, 1));
        Assert.Same(presets[0], PresetList.Cycle(presets, presets[2].Id, 1));
        Assert.Same(presets[2], PresetList.Cycle(presets, presets[0].Id, -1));
        Assert.Same(presets[1], PresetList.Cycle(presets, presets[2].Id, -1));
    }

    [Fact]
    public void Cycle_UnknownCurrent_StartsFromEnds()
    {
        var presets = Presets(3);
        Assert.Same(presets[0], PresetList.Cycle(presets, Guid.NewGuid(), 1));
        Assert.Same(presets[2], PresetList.Cycle(presets, null, -1));
    }

    [Fact]
    public void NextAreaName_StartsAtOne_AndSkipsUsed()
    {
        Assert.Equal("영역 1", PresetList.NextAreaName([]));
        Assert.Equal("영역 1", PresetList.NextAreaName(["기본 영역", "내 영역 1"]));
        Assert.Equal("영역 3", PresetList.NextAreaName(["영역 1", "영역 2"]));
        Assert.Equal("영역 2", PresetList.NextAreaName(["영역 1", "영역 3"]));
        Assert.Equal("영역 2", PresetList.NextAreaName([" 영역 1 "]));
    }
}
