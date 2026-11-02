using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using EasyCapture.Core;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace EasyCapture.App;

public enum ClipboardCopyStatus
{
    /// <summary>클립보드에 넣었다.</summary>
    Success,

    /// <summary>복사할 파일이 하나도 없다(선택이 비었거나 파일이 사라졌다).</summary>
    NothingToCopy,

    /// <summary>다른 프로세스가 클립보드를 잡고 있어 여러 번 시도해도 실패했다.</summary>
    ClipboardBusy,
}

/// <param name="Status">결과 상태.</param>
/// <param name="ItemCount">클립보드에 담은 캡처 수(이미지만 복사하면 1).</param>
public readonly record struct ClipboardCopyResult(ClipboardCopyStatus Status, int ItemCount);

/// <summary>
/// 캡처를 클립보드·드래그용 DataObject로 만들고 클립보드에 넣는다. CopyMode(파일+이미지 등) 해석을 한 곳에 모아 두어
/// 패널뿐 아니라 이후 "마지막 캡처 다시 복사"·"캡처 직후 자동 복사"도 그대로 쓴다. UI 스레드(STA)에서 호출해야 한다.
/// </summary>
public static class CaptureClipboard
{
    // WPF의 SetDataObject가 이미 내부에서 재시도(약 1초)하므로 바깥 재시도는 짧게만 한다.
    private const int MaxAttempts = 3;
    private const int RetryDelayMs = 100;

    /// <summary>
    /// 선택 항목(목록 순서)으로 DataObject를 만든다. 파일이 사라진 항목은 건너뛰고, 남은 항목으로 <see cref="ClipboardCopyPlan"/>대로 구성한다.
    /// forClipboard가 true면 붙여넣기가 이동이 아니라 복사로 처리되도록 "Preferred DropEffect=Copy"를 넣는다
    /// (드래그는 DoDragDrop의 허용 효과로 제한하므로 넣지 않는다).
    /// 담을 것이 없으면 false.
    /// </summary>
    public static bool TryBuildDataObject(
        CopyMode mode,
        IReadOnlyList<Guid> selectedIds,
        CaptureLibrary library,
        bool forClipboard,
        out DataObject data,
        out int itemCount)
    {
        data = new DataObject();
        itemCount = 0;

        var paths = new Dictionary<Guid, string>();
        var existing = new List<Guid>();
        foreach (var id in selectedIds)
        {
            if (library.TryGetFilePath(id, out var path))
            {
                paths[id] = path;
                existing.Add(id);
            }
        }

        var plan = ClipboardCopyPlan.Create(mode, existing);
        if (plan.IsEmpty)
        {
            return false;
        }

        if (plan.FileIds.Count > 0)
        {
            var files = new StringCollection();
            foreach (var id in plan.FileIds)
            {
                files.Add(paths[id]);
            }

            data.SetFileDropList(files);
            if (forClipboard)
            {
                // 4바이트 DROPEFFECT_COPY(1). 이게 없으면 받는 앱이 이동으로 처리할 수 있어 보관소 파일이 사라질 위험이 있다.
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
            }

            itemCount = plan.FileIds.Count;
        }

        if (plan.ImageId is { } imageId)
        {
            var image = CaptureImageLoader.TryLoadFull(paths[imageId]);
            if (image is not null)
            {
                data.SetImage(image);
                itemCount = Math.Max(itemCount, 1);
            }
            else if (plan.FileIds.Count == 0)
            {
                // 이미지만 복사하는 모드인데 읽지 못했다.
                return false;
            }
        }

        return true;
    }

    /// <summary>DataObject를 클립보드에 영속(copy:true)으로 넣는다. 다른 프로세스가 점유 중이면 몇 번 재시도하고, 끝내 실패하면 false.</summary>
    public static bool TrySetClipboard(DataObject data)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (ExternalException) when (attempt < MaxAttempts)
            {
                // CLIPBRD_E_CANT_OPEN 등 일시적 점유. 잠깐 쉬고 다시 시도한다.
                Thread.Sleep(RetryDelayMs);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>선택 항목을 CopyMode대로 클립보드에 복사한다.</summary>
    public static ClipboardCopyResult Copy(CopyMode mode, IReadOnlyList<Guid> selectedIds, CaptureLibrary library)
    {
        if (!TryBuildDataObject(mode, selectedIds, library, forClipboard: true, out var data, out var count))
        {
            return new ClipboardCopyResult(ClipboardCopyStatus.NothingToCopy, 0);
        }

        return TrySetClipboard(data)
            ? new ClipboardCopyResult(ClipboardCopyStatus.Success, count)
            : new ClipboardCopyResult(ClipboardCopyStatus.ClipboardBusy, 0);
    }
}
