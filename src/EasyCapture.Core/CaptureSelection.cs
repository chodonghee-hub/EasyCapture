namespace EasyCapture.Core;

public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>오버레이에서 고르는 현재 영역. 항상 bounds 안에 있고 최소 크기 이상이다.</summary>
public sealed class CaptureSelection
{
    public const int MinSize = 8;

    public CaptureSelection(PixelRect bounds, PixelRect initial)
    {
        Bounds = bounds;
        Set(initial);
    }

    public PixelRect Bounds { get; }

    public PixelRect Rect { get; private set; }

    public void Set(PixelRect rect)
    {
        var width = Math.Max(rect.Width, MinSize);
        var height = Math.Max(rect.Height, MinSize);
        Rect = new PixelRect(rect.X, rect.Y, width, height).Clamp(Bounds);
    }

    public void Move(int dx, int dy) => MoveFrom(Rect, dx, dy);

    /// <summary>드래그 시작 때의 영역을 기준으로 옮긴다(화면 끝에서 눌린 부분이 누적되지 않는다).</summary>
    public void MoveFrom(PixelRect origin, int dx, int dy) => Set(origin.Offset(dx, dy));

    public void Resize(ResizeHandle handle, int dx, int dy) => ResizeFrom(Rect, handle, dx, dy);

    /// <summary>드래그 시작 때의 영역을 기준으로 핸들 쪽 변을 (dx, dy)만큼 옮긴다. 반대편 변을 넘으면 뒤집힌다.</summary>
    public void ResizeFrom(PixelRect origin, ResizeHandle handle, int dx, int dy)
    {
        var movesLeft = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var movesRight = handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var movesTop = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var movesBottom = handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;

        var (left, right) = ResizeAxis(origin.X, origin.Right, movesLeft, movesRight, dx, Bounds.X, Bounds.Right);
        var (top, bottom) = ResizeAxis(origin.Y, origin.Bottom, movesTop, movesBottom, dy, Bounds.Y, Bounds.Bottom);
        Set(PixelRect.FromEdges(left, top, right, bottom));
    }

    /// <summary>두 점으로 영역을 만든다. 점은 어느 방향으로 찍어도 된다.</summary>
    public void SetFromDrag(int x1, int y1, int x2, int y2)
    {
        x1 = Math.Clamp(x1, Bounds.X, Bounds.Right);
        x2 = Math.Clamp(x2, Bounds.X, Bounds.Right);
        y1 = Math.Clamp(y1, Bounds.Y, Bounds.Bottom);
        y2 = Math.Clamp(y2, Bounds.Y, Bounds.Bottom);
        Set(PixelRect.FromEdges(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)));
    }

    private static (int Lo, int Hi) ResizeAxis(
        int originLo, int originHi, bool movesLo, bool movesHi, int delta, int boundsLo, int boundsHi)
    {
        if (!movesLo && !movesHi)
        {
            return (originLo, originHi);
        }

        var anchor = movesLo ? originHi : originLo;
        var moving = Math.Clamp((movesLo ? originLo : originHi) + delta, boundsLo, boundsHi);

        int lo;
        int hi;
        if (movesLo ? moving <= anchor : moving < anchor)
        {
            hi = anchor;
            lo = Math.Min(moving, anchor - MinSize);
        }
        else
        {
            lo = anchor;
            hi = Math.Max(moving, anchor + MinSize);
        }

        if (hi > boundsHi)
        {
            hi = boundsHi;
            lo = Math.Min(lo, hi - MinSize);
        }

        if (lo < boundsLo)
        {
            lo = boundsLo;
            hi = Math.Max(hi, lo + MinSize);
        }

        return (lo, hi);
    }
}
