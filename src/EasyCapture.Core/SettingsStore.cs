using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCapture.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static ReadOnlySpan<byte> Utf8Bom => new byte[] { 0xEF, 0xBB, 0xBF };

    private readonly string _path;

    // 마지막 Load가 읽기 실패(잠금·권한)였으면, 다음 Save가 기존 파일을 덮어쓰기 전에 보존한다.
    private bool _lastLoadReadFailed;

    public SettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile();
    }

    public string FilePath => _path;

    /// <summary>
    /// 설정을 읽는다. 파일이 없으면 기본값, 깨졌으면 파일을 .broken-* 으로 옮기고 기본값을 돌려준다.
    /// </summary>
    public AppSettings Load()
    {
        _lastLoadReadFailed = false;

        if (!File.Exists(_path))
        {
            return Normalize(new AppSettings());
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 읽기 자체가 실패(잠금·권한)했으면 파일은 그대로 두고 기본값만 쓴다.
            _lastLoadReadFailed = true;
            return Normalize(new AppSettings());
        }

        AppSettings? loaded = null;
        try
        {
            // Utf8JsonReader는 선행 UTF-8 BOM(EF BB BF)을 거부하므로 건너뛰고 파싱한다.
            ReadOnlySpan<byte> json = bytes;
            if (json.StartsWith(Utf8Bom))
            {
                json = json[Utf8Bom.Length..];
            }

            loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // 아래에서 깨진 파일로 처리한다.
        }

        if (loaded is null)
        {
            MoveBrokenFile();
            return Normalize(new AppSettings());
        }

        return Normalize(loaded);
    }

    /// <summary>임시 파일에 쓴 뒤 교체하는 방식으로 저장한다.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);

        if (_lastLoadReadFailed && File.Exists(_path))
        {
            // 읽지 못한 기존 파일을 기본값으로 덮어써 잃지 않도록 먼저 복사해 둔다.
            // 복사에 실패하면 예외를 그대로 던져 저장을 중단한다(원본은 제자리에 남고 플래그도 유지).
            File.Copy(_path, NextBrokenPath());
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var tempPath = System.IO.Path.Combine(directory, $"{System.IO.Path.GetFileName(_path)}.tmp-{Guid.NewGuid():N}");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _path, overwrite: true);
            _lastLoadReadFailed = false;
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // 임시 파일 정리 실패는 원래 예외를 가리지 않는다.
            }

            throw;
        }
    }

    /// <summary>누락·잘못된 값을 기본값으로 교정한다. 전달받은 객체를 고쳐서 그대로 돌려준다.</summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 단축키: 알 수 없는 동작·해석 불가 값은 버리고 키 자체가 없는 동작만 기본값으로 채운다.
        // 키가 있고 값이 null·빈 문자열·공백이면 "비활성"으로 보고 ""로 보존한다.
        var hotkeys = new Dictionary<HotkeyAction, string>();
        if (settings.Hotkeys is not null)
        {
            foreach (var (action, key) in settings.Hotkeys)
            {
                if (!Enum.IsDefined(action))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    hotkeys[action] = string.Empty;
                }
                else if (Hotkey.TryParse(key, out _))
                {
                    hotkeys[action] = key.Trim();
                }
            }
        }

        foreach (var (action, key) in AppSettings.DefaultHotkeys)
        {
            hotkeys.TryAdd(action, key);
        }

        settings.Hotkeys = hotkeys;

        // 프리셋: 크기가 잘못된 항목은 제거하고, 비면 기본 프리셋을 복구한다.
        var presets = new List<CapturePreset>();
        var seenIds = new HashSet<Guid>();
        var seenInstances = new HashSet<CapturePreset>(ReferenceEqualityComparer.Instance);
        if (settings.Presets is not null)
        {
            foreach (var preset in settings.Presets)
            {
                if (preset is null || preset.Width < 1 || preset.Height < 1)
                {
                    continue;
                }

                // 같은 인스턴스가 두 번 들어 있으면 첫 항목만 유지한다.
                if (!seenInstances.Add(preset))
                {
                    continue;
                }

                // 빈 Id·중복 Id는 새로 발급한다(중복은 먼저 나온 항목만 유지).
                if (preset.Id == Guid.Empty || !seenIds.Add(preset.Id))
                {
                    do
                    {
                        preset.Id = Guid.NewGuid();
                    }
                    while (!seenIds.Add(preset.Id));
                }

                if (string.IsNullOrWhiteSpace(preset.Name))
                {
                    preset.Name = CapturePreset.DefaultName;
                }

                preset.MonitorDeviceName = NullIfBlank(preset.MonitorDeviceName);
                preset.Hotkey = Hotkey.TryParse(preset.Hotkey, out _) ? preset.Hotkey!.Trim() : null;
                presets.Add(preset);
            }
        }

        if (presets.Count == 0)
        {
            presets.Add(CapturePreset.CreateDefault());
        }

        settings.Presets = presets;

        if (settings.DefaultPresetId is not { } defaultId || !presets.Exists(p => p.Id == defaultId))
        {
            settings.DefaultPresetId = presets[0].Id;
        }

        if (!Enum.IsDefined(settings.CopyMode))
        {
            settings.CopyMode = CopyMode.FileAndImage;
        }

        if (settings.MaxItems < 1)
        {
            settings.MaxItems = AppSettings.DefaultMaxItems;
        }

        if (settings.MaxAgeDays < 0)
        {
            settings.MaxAgeDays = 0;
        }

        if (settings.Version < 1)
        {
            settings.Version = 1;
        }

        return settings;
    }

    /// <summary>
    /// enum 이름과 정확히 일치할 때만 인정한다(대소문자 무시). "A, B" 같은 조합이나 "1" 같은 숫자 문자열은 거부한다.
    /// </summary>
    private static bool TryParseName<T>(string? text, out T value) where T : struct, Enum
    {
        value = default;
        if (text is null)
        {
            return false;
        }

        var trimmed = text.Trim();
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                value = Enum.Parse<T>(name);
                return true;
            }
        }

        return false;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string NextBrokenPath()
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var target = $"{_path}.broken-{stamp}";
        for (var i = 2; File.Exists(target); i++)
        {
            target = $"{_path}.broken-{stamp}-{i}";
        }

        return target;
    }

    private void MoveBrokenFile()
    {
        try
        {
            File.Move(_path, NextBrokenPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 옮기지 못해도 기본값으로 계속 동작한다.
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new LenientEnumConverter<CopyMode>());
        options.Converters.Add(new HotkeysConverter());
        return options;
    }

    /// <summary>문자열로 저장하고, 알 수 없는 값은 예외 대신 기본값으로 읽는다.</summary>
    private sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String
                && TryParseName<T>(reader.GetString(), out var value))
            {
                return value;
            }

            reader.Skip();
            return default;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>알 수 없는 동작 이름이나 문자열·null이 아닌 값은 건너뛰고 읽는다.</summary>
    private sealed class HotkeysConverter : JsonConverter<Dictionary<HotkeyAction, string>>
    {
        public override Dictionary<HotkeyAction, string> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<HotkeyAction, string>();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return result;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.Null
                    && TryParseName<HotkeyAction>(name, out var action))
                {
                    // null은 비활성(빈 문자열)으로 읽는다.
                    result[action] = reader.GetString() ?? string.Empty;
                }
                else
                {
                    reader.Skip();
                }
            }

            return result;
        }

        public override void Write(
            Utf8JsonWriter writer, Dictionary<HotkeyAction, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (action, key) in value)
            {
                writer.WriteString(action.ToString(), key);
            }

            writer.WriteEndObject();
        }
    }
}
