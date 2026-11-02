namespace EasyCapture.Core;

/// <summary>패널 창을 놓을 위치 계산. 모두 물리 픽셀이다.</summary>
public static class PanelPlacement
{
    /// <summary>
    /// 작업 영역 중앙에 width×height 창을 놓는 사각형을 돌려준다. 창이 작업 영역보다 크면 작업 영역 크기로 줄인다.
    /// width·height는 1 미만이면 1로 본다. 작업 영역이 비어 있으면(IsEmpty) 요청 크기를 원점(0,0)에 둔다.
    /// </summary>
    public static PixelRect Center(PixelRect workArea, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (workArea.IsEmpty)
        {
            return new PixelRect(0, 0, width, height);
        }

        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        return new PixelRect(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height);
    }
}
