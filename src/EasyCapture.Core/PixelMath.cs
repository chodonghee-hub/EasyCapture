namespace EasyCapture.Core;

/// <summary>화면 좌표 변환에 쓰는 작은 수학 함수.</summary>
public static class PixelMath
{
    /// <summary>
    /// 마우스 위치(DIP↔물리 변환을 거친 실수)를 정수 물리 픽셀로 바꾼다.
    /// 실제 값은 정수 픽셀이고 변환 오차만 섞여 있으므로 내림이 아니라 반올림한다(799.9999 → 800).
    /// </summary>
    public static int ToPixel(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
    }
}
