using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace EasyCapture.App;

public sealed class TrayService : IDisposable
{
    private const string ToolTip = "EasyCapture";

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;

    public TrayService()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("캡처 시작", null, (_, _) => CaptureRequested?.Invoke());
        _menu.Items.Add("클립보드 패널 열기", null, (_, _) => PanelRequested?.Invoke());
        _menu.Items.Add("설정", null, (_, _) => SettingsRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("종료", null, (_, _) => ExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = ToolTip,
            ContextMenuStrip = _menu,
            Visible = true,
        };
    }

    public event Action? CaptureRequested;

    public event Action? PanelRequested;

    public event Action? SettingsRequested;

    public event Action? ExitRequested;

    public void ShowNotice(string message)
    {
        _notifyIcon.ShowBalloonTip(2000, ToolTip, message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private static Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var stream = info.Stream;
        return new Icon(stream);
    }
}