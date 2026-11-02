using EasyCapture.Core;

namespace EasyCapture.Tests;

public sealed class PanelPlacementTests
{
    // ---- Center ----

    [Theory]
    [InlineData(100, 80, 20, 10, 50, 55, 20, 10)]
    [InlineData(101, 81, 20, 10, 50, 55, 20, 10)]
    [InlineData(100, 80, 200, 10, 10, 55, 100, 10)]
    [InlineData(100, 80, 20, 200, 50, 20, 20, 80)]
    [InlineData(100, 80, 100, 80, 10, 20, 100, 80)]
    [InlineData(100, 80, int.MaxValue, int.MaxValue, 10, 20, 100, 80)]
    [InlineData(100, 80, 0, -5, 59, 59, 1, 1)]
    [InlineData(1, 1, -1, 0, 10, 20, 1, 1)]
    public void Center_NonemptyWorkArea_ClampsSizeAndCenters(
        int areaWidth, int areaHeight, int width, int height,
        int x, int y, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new PixelRect(x, y, expectedWidth, expectedHeight),
            PanelPlacement.Center(new PixelRect(10, 20, areaWidth, areaHeight), width, height));
    }

    [Fact]
    public void Center_NegativeWorkAreaOrigin_PreservesScreenCoordinates()
    {
        Assert.Equal(new PixelRect(-950, -350, 300, 200),
            PanelPlacement.Center(new PixelRect(-1200, -600, 800, 700), 300, 200));
    }

    [Theory]
    [InlineData(0, 50, 20, 30, 20, 30)]
    [InlineData(50, 0, 20, 30, 20, 30)]
    [InlineData(-1, 50, 20, 30, 20, 30)]
    [InlineData(50, -1, 20, 30, 20, 30)]
    [InlineData(0, 0, 0, -1, 1, 1)]
    public void Center_EmptyWorkArea_UsesOriginAndNormalizedRequest(
        int areaWidth, int areaHeight, int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new PixelRect(0, 0, expectedWidth, expectedHeight),
            PanelPlacement.Center(new PixelRect(100, -200, areaWidth, areaHeight), width, height));
    }
}
