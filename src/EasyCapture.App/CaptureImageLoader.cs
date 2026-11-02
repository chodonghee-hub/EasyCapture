using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace EasyCapture.App;

/// <summary>
/// 보관소 PNG를 파일을 잠그지 않고 읽는다. 이름 변경·삭제가 막히지 않도록 스트림으로 한 번에 읽어 들인 뒤(OnLoad) 스트림을 닫고,
/// 결과는 Freeze해서 어느 스레드에서든 쓸 수 있게 한다. 실패하면 예외 대신 null을 돌려준다.
/// </summary>
public static class CaptureImageLoader
{
    /// <summary>목록 썸네일용 디코딩 폭(물리 픽셀). 원본이 더 좁으면 원본 폭을 쓴다.</summary>
    public const int ThumbnailDecodeWidth = 320;

    /// <summary>썸네일 크기로 줄여 읽는다. 큰 PNG를 통째로 디코딩하지 않아 메모리를 아낀다.</summary>
    public static BitmapSource? TryLoadThumbnail(string path, int originalWidth) =>
        TryLoad(path, originalWidth > 0 ? Math.Min(originalWidth, ThumbnailDecodeWidth) : ThumbnailDecodeWidth);

    /// <summary>클립보드 이미지용으로 원본 크기 그대로 읽는다.</summary>
    public static BitmapSource? TryLoadFull(string path) => TryLoad(path, 0);

    private static BitmapSource? TryLoad(string path, int decodePixelWidth)
    {
        try
        {
            // 다른 곳에서 이름을 바꾸거나 지우는 중이어도 읽을 수 있고, 읽는 동안 그 작업도 막지 않도록 공유 모드를 넓게 준다.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }

            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or InvalidOperationException or FormatException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            // 파일이 사라졌거나 깨졌다. 호출 측이 자리표시로 처리한다.
            return null;
        }
    }
}
