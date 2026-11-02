using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class PixelMathTests
{
    [Theory]
    [InlineData(799.9999, 800)]
    [InlineData(800.0001, 800)]
    [InlineData(0.0001, 0)]
    [InlineData(-0.0001, 0)]
    [InlineData(0, 0)]
    [InlineData(-199.9999, -200)]
    [InlineData(-200.0001, -200)]
    [InlineData(2559.9999999, 2560)]
    [InlineData(0.4, 0)]
    [InlineData(0.6, 1)]
    public void ToPixel_RoundsToNearestInteger(double value, int expected)
    {
        Assert.Equal(expected, PixelMath.ToPixel(value));
    }

    [Fact]
    public void ToPixel_ScaleRoundTrip_IsExactFor150Percent()
    {
        // 물리 픽셀 → DIP(÷1.5) → 레이아웃 배율(×1.5)을 거쳐도 원래 정수가 나와야 한다.
        const double scale = 1.5;
        for (var pixel = -3000; pixel <= 5000; pixel++)
        {
            var dip = pixel / scale;
            Assert.Equal(pixel, PixelMath.ToPixel(dip * scale));
        }
    }

    [Fact]
    public void ToPixel_DoesNotOverflowOnExtremes()
    {
        Assert.Equal(int.MaxValue, PixelMath.ToPixel(double.PositiveInfinity));
        Assert.Equal(int.MinValue, PixelMath.ToPixel(double.NegativeInfinity));
        Assert.Equal(0, PixelMath.ToPixel(double.NaN));
    }
}
