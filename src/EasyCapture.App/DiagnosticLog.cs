using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EasyCapture.App;

// 원인 확인 뒤 제거할 임시 진단 로그: 캡처 확정 효과가 실제 실행에서 어디까지 진행되는지 파일로 남긴다.
// 이미지 내용·키 입력 내용·개인정보는 기록하지 않는다(이벤트 이름·크기·시간·예외 메시지만).
// 어떤 실패도 앱·효과·캡처를 방해하지 않도록 모든 예외를 삼킨다.
internal static class DiagnosticLog
{
    /// <summary>원인 확인 뒤 제거할 임시 진단 로그 스위치. false면 아무것도 쓰지 않는다.</summary>
    public static readonly bool Enabled = true;

    private const long MaxBytes = 256 * 1024;

    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static string? _path;
    private static bool _ready;
    private static bool _broken;

    /// <summary>로그 파일 경로. EASYCAPTURE_DATA_DIR이 있으면 그 아래, 없으면 %LOCALAPPDATA%\EasyCapture\effect.log.</summary>
    public static string ResolvePath()
    {
        var env = Environment.GetEnvironmentVariable("EASYCAPTURE_DATA_DIR");
        var root = string.IsNullOrWhiteSpace(env)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : env;
        return Path.Combine(root, "EasyCapture", "effect.log");
    }

    /// <summary>한 줄을 추가한다. 형식: yyyy-MM-dd HH:mm:ss.fff [pid:스레드ID] 메시지. 실패는 조용히 무시한다.</summary>
    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}:{Environment.CurrentManagedThreadId}] {OneLine(message)}{Environment.NewLine}";
            lock (Gate)
            {
                if (_broken)
                {
                    return;
                }

                if (!_ready)
                {
                    _ready = true;
                    try
                    {
                        _path = ResolvePath();
                        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                        RotateIfLarge(_path);
                    }
                    catch (Exception)
                    {
                        _broken = true;
                        return;
                    }
                }

                File.AppendAllText(_path!, line, Utf8NoBom);
            }
        }
        catch (Exception)
        {
            // 로그 실패는 무시한다.
        }
    }

    /// <summary>예외를 "형식: 메시지" 한 줄로 만든다. stackLines가 있으면 스택 앞쪽 줄을 덧붙인다.</summary>
    public static string Describe(Exception ex, int stackLines = 0)
    {
        try
        {
            var text = $"{ex.GetType().Name}: {ex.Message}";
            if (stackLines > 0 && ex.StackTrace is { } stack)
            {
                var lines = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                text += " @ " + string.Join(" | ", lines.Take(stackLines));
            }

            return text;
        }
        catch (Exception)
        {
            return "예외 설명 실패";
        }
    }

    /// <summary>시스템 DPI 배율(1.0 = 96dpi). 알 수 없으면 0.</summary>
    public static double SystemDpiScale()
    {
        try
        {
            return GetDpiForSystem() / 96.0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // 256KB를 넘으면 시작 시 한 번 .old 로 교체한다(이전 .old 는 덮어쓴다).
    private static void RotateIfLarge(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length > MaxBytes)
        {
            File.Move(path, path + ".old", overwrite: true);
        }
    }

    private static string OneLine(string message) =>
        message.Replace("\r\n", " | ").Replace('\n', ' ').Replace('\r', ' ');

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
