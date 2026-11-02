namespace EasyCapture.Core;

/// <summary>물리 픽셀 사각형(불변). 오른쪽·아래 끝은 포함하지 않는다.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Empty => default;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>겹치는 부분. 겹치지 않으면 <see cref="Empty"/>.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromEdges(left, top, right, bottom) : Empty;
    }

    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        return other.IsEmpty
            ? this
            : FromEdges(Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>bounds 안으로 들어오게 옮긴다. bounds보다 크면 bounds 크기로 줄인다.</summary>
    public PixelRect Clamp(PixelRect bounds)
    {
        var width = Math.Min(Width, bounds.Width);
        var height = Math.Min(Height, bounds.Height);
        var x = Math.Clamp(X, bounds.X, bounds.Right - width);
        var y = Math.Clamp(Y, bounds.Y, bounds.Bottom - height);
        return new PixelRect(x, y, width, height);
    }
}
