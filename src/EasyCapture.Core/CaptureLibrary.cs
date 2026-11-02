using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyCapture.Core;

/// <summary>
/// 캡처 보관소. PNG 파일들을 한 폴더에 두고 index.json으로 목록·메타데이터를 관리한다.
/// </summary>
public sealed class CaptureLibrary
{
    private const string PngExtension = ".png";
    private const string IndexFileName = "index.json";
    private const int MaxNameLength = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly char[] ForbiddenChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _indexPath;
    private readonly TimeProvider _time;

    // 항상 최신순(CreatedAt 내림차순, 같으면 나중에 넣은 것이 앞).
    private List<CaptureItem> _items = new();

    // 인덱스 읽기 실패(잠금·권한)였으면, 다음 저장이 기존 파일을 덮어쓰기 전에 보존한다.
    private bool _lastLoadReadFailed;

    public CaptureLibrary(string directory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = Path.GetFullPath(directory);
        _indexPath = Path.Combine(_directory, IndexFileName);
        _time = timeProvider ?? TimeProvider.System;

        Load();
    }

    /// <summary>최신순 스냅샷.</summary>
    public IReadOnlyList<CaptureItem> Items
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    /// <summary>PNG 바이트를 보관소에 저장하고 목록에 추가한다.</summary>
    public CaptureItem Add(byte[] pngBytes, int width, int height, string? presetName = null)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (!HasPngSignature(pngBytes))
        {
            throw new ArgumentException("PNG 데이터가 아닙니다.", nameof(pngBytes));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("이미지 크기는 0보다 커야 합니다.", nameof(width));
        }

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);

            var now = _time.GetLocalNow();
            var baseName = $"캡처_{now:yyyyMMdd_HHmmss}";
            var fileName = MakeUniqueFileName(baseName, null);
            var path = Path.Combine(_directory, fileName);
            var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(pngBytes, 0, pngBytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, path, overwrite: false);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }

            var item = new CaptureItem
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                CreatedAt = now,
                Width = width,
                Height = height,
                Pinned = false,
                PresetName = presetName,
            };

            var previous = _items;
            _items = SortNewestFirst(_items.Prepend(item));
            try
            {
                SaveIndex();
            }
            catch
            {
                _items = previous;
                TryDelete(path);
                throw;
            }

            return item;
        }
    }

    public string GetFilePath(Guid id) =>
        TryGetFilePath(id, out var path) ? path : throw new KeyNotFoundException($"보관소에 없는 항목입니다: {id}");

    /// <summary>항목이 있고 파일이 실제로 존재할 때 절대 경로를 돌려준다.</summary>
    public bool TryGetFilePath(Guid id, out string path)
    {
        lock (_gate)
        {
            var item = _items.Find(i => i.Id == id);
            if (item is not null)
            {
                var candidate = Path.Combine(_directory, item.FileName);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    /// <summary>표시 이름과 디스크의 파일 이름을 함께 바꾼다. 이름이 정리 후 비거나 예약어면 false.</summary>
    public bool Rename(Guid id, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);

        var baseName = SanitizeName(newName);
        if (baseName is null)
        {
            return false;
        }

        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index < 0)
            {
                return false;
            }

            var item = _items[index];
            if (string.Equals(item.Name, baseName, StringComparison.Ordinal))
            {
                return true;
            }

            var oldPath = Path.Combine(_directory, item.FileName);
            if (!File.Exists(oldPath))
            {
                return false;
            }

            var newFileName = MakeUniqueFileName(baseName, item);
            var newPath = Path.Combine(_directory, newFileName);

            try
            {
                if (string.Equals(newFileName, item.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    // 대소문자만 바뀌는 이름 변경은 임시 이름을 거쳐 옮긴다.
                    var tempPath = oldPath + ".tmp-" + Guid.NewGuid().ToString("N");
                    File.Move(oldPath, tempPath);
                    try
                    {
                        File.Move(tempPath, newPath);
                    }
                    catch
                    {
                        // 두 번째 이동이 실패하면 첫 이동을 되돌려 원래 이름으로 복구한다.
                        // 되돌리기마저 실패하면 파일이 임시 이름에 남은 사실을 숨기지 않도록 전파한다.
                        try
                        {
                            File.Move(tempPath, oldPath, overwrite: false);
                        }
                        catch (Exception rollbackEx)
                        {
                            // 바깥 catch(IOException)가 삼켜 false로 바꾸지 않도록 다른 예외 형식을 쓴다.
                            throw new InvalidOperationException($"이름 변경을 되돌리지 못했습니다. 파일이 임시 이름에 남아 있습니다: {tempPath}", rollbackEx);
                        }

                        throw;
                    }
                }
                else
                {
                    File.Move(oldPath, newPath, overwrite: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            var renamed = item with { Name = Path.GetFileNameWithoutExtension(newFileName), FileName = newFileName };
            _items[index] = renamed;
            try
            {
                SaveIndex();
            }
            catch
            {
                _items[index] = item;
                try
                {
                    File.Move(newPath, oldPath, overwrite: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 되돌리지 못해도 다음 로드 때 인덱스 정리로 수습된다.
                }

                throw;
            }

            return true;
        }
    }

    public bool SetPinned(Guid id, bool pinned)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index < 0)
            {
                return false;
            }

            var item = _items[index];
            if (item.Pinned == pinned)
            {
                return true;
            }

            _items[index] = item with { Pinned = pinned };
            try
            {
                SaveIndex();
            }
            catch
            {
                _items[index] = item;
                throw;
            }

            return true;
        }
    }

    /// <summary>
    /// 파일과 항목을 지운다. 파일이 이미 없어도 항목은 정리한다.
    /// 파일을 지우지 못하면(잠금·권한) 항목을 유지하고 false.
    /// </summary>
    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            var item = _items.Find(i => i.Id == id);
            if (item is null || !TryDeleteFile(item))
            {
                return false;
            }

            _items.Remove(item);
            SaveIndex();
            return true;
        }
    }

    /// <summary>
    /// 보관 정책을 적용한다. 고정 항목은 지우지 않는다. 지운 개수를 돌려준다.
    /// </summary>
    public int ApplyRetention(int maxItems, int maxAgeDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);

        lock (_gate)
        {
            var deleted = 0;

            if (maxAgeDays > 0)
            {
                var cutoff = _time.GetUtcNow() - TimeSpan.FromDays(maxAgeDays);
                foreach (var item in _items.Where(i => !i.Pinned && i.CreatedAt < cutoff).ToList())
                {
                    if (TryDeleteFile(item))
                    {
                        _items.Remove(item);
                        deleted++;
                    }
                }
            }

            // 초과분만큼, 오래된 것부터(목록 뒤쪽부터) 고정 아닌 항목을 지운다.
            // 잠겨서 못 지운 항목은 건너뛸 뿐 더 새 항목으로 대신하지 않는다.
            var excess = _items.Count - maxItems;
            for (var i = _items.Count - 1; i >= 0 && excess > 0; i--)
            {
                var item = _items[i];
                if (item.Pinned)
                {
                    continue;
                }

                excess--;
                if (TryDeleteFile(item))
                {
                    _items.RemoveAt(i);
                    deleted++;
                }
            }

            if (deleted > 0)
            {
                SaveIndex();
            }

            return deleted;
        }
    }

    // ---- 인덱스 읽기 ----

    private void Load()
    {
        lock (_gate)
        {
            _lastLoadReadFailed = false;
            _items = new List<CaptureItem>();

            if (!File.Exists(_indexPath))
            {
                // 인덱스가 없어도 폴더에 PNG가 남아 있으면 목록에서 사라지지 않도록 다시 만든다.
                if (HasPngFiles())
                {
                    RebuildFromFolder();
                }

                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(_indexPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 읽기 자체가 실패했으면 깨진 파일로 보지 않고, 덮어쓰기 전에 보존하도록 표시만 한다.
                _lastLoadReadFailed = true;
                return;
            }

            IndexDocument? document = null;
            try
            {
                ReadOnlySpan<byte> json = bytes;
                ReadOnlySpan<byte> bom = new byte[] { 0xEF, 0xBB, 0xBF };
                if (json.StartsWith(bom))
                {
                    json = json[bom.Length..];
                }

                document = JsonSerializer.Deserialize<IndexDocument>(json, JsonOptions);
            }
            catch (JsonException)
            {
                // 아래에서 깨진 파일로 처리한다.
            }

            if (document?.Items is null)
            {
                RebuildFromFolder();
                return;
            }

            var seenIds = new HashSet<Guid>();
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changed = false;
            var loaded = new List<CaptureItem>();
            foreach (var raw in document.Items)
            {
                // 중복 집합에는 유효하다고 확정된 항목만 등록한다(파일 없는 앞 항목이 뒤 항목을 막지 않도록).
                if (raw is null
                    || raw.Id == Guid.Empty
                    || !IsSafeFileName(raw.FileName)
                    || seenIds.Contains(raw.Id)
                    || seenFiles.Contains(raw.FileName)
                    || !File.Exists(Path.Combine(_directory, raw.FileName)))
                {
                    changed = true;
                    continue;
                }

                seenIds.Add(raw.Id);
                seenFiles.Add(raw.FileName);

                loaded.Add(raw with
                {
                    Name = Path.GetFileNameWithoutExtension(raw.FileName),
                    Width = Math.Max(raw.Width, 0),
                    Height = Math.Max(raw.Height, 0),
                });
            }

            _items = SortNewestFirst(loaded);
            if (changed)
            {
                TrySaveIndex();
            }
        }
    }

    private bool HasPngFiles()
    {
        try
        {
            return Directory.EnumerateFiles(_directory, "*" + PngExtension)
                .Any(file => Path.GetFileName(file).EndsWith(PngExtension, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 폴더가 없거나 읽을 수 없으면 기존처럼 빈 목록으로 시작한다.
            return false;
        }
    }

    // 인덱스가 깨졌거나 없을 때: 깨진 파일은 .broken-* 으로 옮기고 폴더의 PNG로 목록을 다시 만든다.
    private void RebuildFromFolder()
    {
        var rebuilt = new List<CaptureItem>();
        var complete = true;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*" + PngExtension))
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.EndsWith(PngExtension, StringComparison.OrdinalIgnoreCase) || !IsSafeFileName(fileName))
                {
                    continue;
                }

                var (width, height) = ReadPngSize(file);
                rebuilt.Add(new CaptureItem
                {
                    Id = Guid.NewGuid(),
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    FileName = fileName,
                    CreatedAt = GetCreatedAtOrNow(file),
                    Width = width,
                    Height = height,
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            complete = false;
        }

        _items = SortNewestFirst(rebuilt);

        if (!complete)
        {
            // 폴더를 끝까지 읽지 못한 불완전한 목록은 메모리에서만 쓰고 저장하지 않는다.
            // 깨진 인덱스를 제자리에 두어 다음 로드 때 다시 재구성하고, 이후 저장 시에는 덮어쓰기 전에 보존한다.
            _lastLoadReadFailed = true;
            return;
        }

        var moved = true;
        if (File.Exists(_indexPath))
        {
            try
            {
                File.Move(_indexPath, NextBrokenPath());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 옮기지 못했으면 덮어쓰기 전에 복사 보존하도록 표시하고, 지금은 저장하지 않는다.
                moved = false;
                _lastLoadReadFailed = true;
            }
        }

        if (moved)
        {
            TrySaveIndex();
        }
    }

    // 생성 시각을 읽지 못한 파일은 건너뛰지 않고 현재 시각으로 포함시킨다.
    private DateTimeOffset GetCreatedAtOrNow(string file)
    {
        try
        {
            return new DateTimeOffset(File.GetCreationTimeUtc(file), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _time.GetUtcNow();
        }
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[24];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length
                || !header[..PngSignature.Length].SequenceEqual(PngSignature)
                || !header.Slice(12, 4).SequenceEqual("IHDR"u8))
            {
                return (0, 0);
            }

            var width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
            return width > 0 && height > 0 ? (width, height) : (0, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    // ---- 인덱스 쓰기 ----

    private void TrySaveIndex()
    {
        try
        {
            SaveIndex();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 정리 결과를 저장하지 못해도 메모리 목록으로 계속 동작한다. 다음 변경 때 다시 저장한다.
        }
    }

    private void SaveIndex()
    {
        Directory.CreateDirectory(_directory);

        if (_lastLoadReadFailed && File.Exists(_indexPath))
        {
            // 읽지 못한 기존 인덱스를 덮어써 잃지 않도록 먼저 복사해 둔다.
            // 복사에 실패하면 예외를 그대로 던져 저장을 중단한다(원본은 제자리에 남고 플래그도 유지).
            File.Copy(_indexPath, NextBrokenPath());
        }

        var document = new IndexDocument { Version = 1, Items = _items.Cast<CaptureItem?>().ToList() };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var tempPath = _indexPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _indexPath, overwrite: true);
            _lastLoadReadFailed = false;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private string NextBrokenPath()
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMddHHmmss");
        var target = $"{_indexPath}.broken-{stamp}";
        for (var i = 2; File.Exists(target); i++)
        {
            target = $"{_indexPath}.broken-{stamp}-{i}";
        }

        return target;
    }

    // ---- 파일·이름 도우미 ----

    private bool TryDeleteFile(CaptureItem item)
    {
        try
        {
            File.Delete(Path.Combine(_directory, item.FileName));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 정리 실패는 원래 예외를 가리지 않는다.
        }
    }

    /// <summary>base.png, base_2.png, ... 중 인덱스·디스크 어디에도 없는 첫 파일명. self는 충돌에서 제외한다.</summary>
    private string MakeUniqueFileName(string baseName, CaptureItem? self)
    {
        for (var n = 1; ; n++)
        {
            // 접미사를 붙여도 확장자 제외 최대 길이를 넘지 않도록 접미사 길이만큼 본문을 줄인다.
            var suffix = n == 1 ? string.Empty : $"_{n}";
            var fileName = TruncateName(baseName, MaxNameLength - suffix.Length) + suffix + PngExtension;
            if (self is not null && string.Equals(fileName, self.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            var inIndex = _items.Exists(i => string.Equals(i.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (!inIndex && !File.Exists(Path.Combine(_directory, fileName)))
            {
                return fileName;
            }
        }
    }

    /// <summary>사용자 입력을 파일 이름(확장자 제외)으로 정리한다. 쓸 수 없으면 null.</summary>
    private static string? SanitizeName(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input.Trim())
        {
            if (!char.IsControl(ch) && Array.IndexOf(ForbiddenChars, ch) < 0)
            {
                builder.Append(ch);
            }
        }

        var name = builder.ToString().Trim();
        if (name.EndsWith(PngExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^PngExtension.Length];
        }

        name = name.TrimEnd('.', ' ').TrimStart();
        name = TruncateName(name, MaxNameLength);

        if (name.Length == 0)
        {
            return null;
        }

        // "CON.txt"처럼 첫 점 앞부분이 예약어여도 Windows는 예약 장치로 취급한다.
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        return ReservedNames.Contains(stem.TrimEnd(' ')) ? null : name;
    }

    // 최대 UTF-16 단위 수로 자른다. 경계가 서로게이트 쌍 중간이면 한 칸 줄여 쌍을 온전히 둔다.
    private static string TruncateName(string name, int maxLength)
    {
        if (name.Length <= maxLength)
        {
            return name;
        }

        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(name[cut - 1]))
        {
            cut--;
        }

        return name[..cut].TrimEnd('.', ' ');
    }

    // 디렉터리 밖을 가리키는 값(경로 구분자, ..)이나 .png가 아닌 파일(index.json 등)은 거부한다.
    private static bool IsSafeFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName == Path.GetFileName(fileName)
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && fileName != ".."
        && fileName.EndsWith(PngExtension, StringComparison.OrdinalIgnoreCase)
        && fileName.Length > PngExtension.Length;

    private static bool HasPngSignature(byte[] bytes) =>
        bytes.Length > PngSignature.Length && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);

    private static List<CaptureItem> SortNewestFirst(IEnumerable<CaptureItem> items) =>
        items.OrderByDescending(i => i.CreatedAt).ToList();

    private sealed class IndexDocument
    {
        public int Version { get; set; } = 1;

        public List<CaptureItem?>? Items { get; set; }
    }
}
