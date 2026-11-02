namespace EasyCapture.Core;

public static class AppPaths
{
    public const string AppFolderName = "EasyCapture";

    /// <summary>설정 파일: %APPDATA%\EasyCapture\settings.json. 테스트에서는 appDataRoot를 지정한다.</summary>
    public static string SettingsFile(string? appDataRoot = null) =>
        Path.Combine(
            appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName,
            "settings.json");

    /// <summary>보관소 폴더: %LOCALAPPDATA%\EasyCapture\captures. 테스트에서는 localAppDataRoot를 지정한다.</summary>
    public static string CapturesDirectory(string? localAppDataRoot = null) =>
        Path.Combine(
            localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName,
            "captures");

    /// <summary>보관소 인덱스: 보관소 폴더의 index.json.</summary>
    public static string IndexFile(string? localAppDataRoot = null) =>
        Path.Combine(CapturesDirectory(localAppDataRoot), "index.json");
}
