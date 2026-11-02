using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyCapture.Core;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace EasyCapture.App;

/// <summary>모니터 목록과 가상 화면 정지 캡처. 좌표는 모두 물리 픽셀(앱이 PerMonitorV2 DPI 인식).</summary>
public static class ScreenCapturer
{
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    public static IReadOnlyList<MonitorInfo> GetMonitors() =>
        Screen.AllScreens
            .Select(s => new MonitorInfo(
                s.DeviceName,
                new PixelRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height),
                s.Primary))
            .ToList();

    /// <summary>area 영역을 한 번에 찍는다. 마우스 커서는 포함하지 않는다.</summary>
    public static BitmapSource Capture(PixelRect area)
    {
        using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            // CopyFromScreen은 CaptureBlt(반투명 창 포함)와 SourceCopy를 함께 지정하면 예외를 던지므로 BitBlt를 직접 호출한다.
            var screenDc = GetDC(IntPtr.Zero);
            var targetDc = graphics.GetHdc();
            try
            {
                if (!BitBlt(targetDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, SrcCopy | CaptureBlt))
                {
                    throw new InvalidOperationException("화면을 복사하지 못했습니다.");
                }
            }
            finally
            {
                graphics.ReleaseHdc(targetDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        var data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, area.Width, area.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);
        try
        {
            var source = BitmapSource.Create(
                area.Width,
                area.Height,
                96,
                96,
                PixelFormats.Bgr32,
                null,
                data.Scan0,
                data.Stride * area.Height,
                data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);
}
