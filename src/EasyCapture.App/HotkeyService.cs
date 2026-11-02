using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using EasyCapture.Core;

namespace EasyCapture.App;

public enum HotkeyRegisterStatus
{
    Success,

    /// <summary>다른 프로그램이 이미 같은 조합을 쓰고 있다(Win32 오류 1409).</summary>
    InUse,

    Failed,
}

public readonly record struct HotkeyRegistration(HotkeyRegisterStatus Status, string? Message = null)
{
    public bool Succeeded => Status == HotkeyRegisterStatus.Success;
}

/// <summary>숨은 메시지 전용 창으로 WM_HOTKEY를 받아 UI 스레드에서 콜백을 호출한다.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = [];
    private int _nextId;
    private bool _disposed;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("EasyCapture.HotkeyWindow")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>단축키 콜백이 예외를 던졌을 때 호출된다. 예외는 삼키고 앱은 계속 동작한다.</summary>
    public Action<Exception>? CallbackFailed { get; set; }

    public HotkeyRegistration Register(Hotkey hotkey, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);

        var id = ++_nextId;
        var modifiers = (uint)hotkey.Win32Modifiers | ModNoRepeat;
        if (RegisterHotKey(_source.Handle, id, modifiers, (uint)hotkey.VirtualKey))
        {
            _callbacks[id] = callback;
            return new HotkeyRegistration(HotkeyRegisterStatus.Success);
        }

        var error = Marshal.GetLastWin32Error();
        return error == ErrorHotkeyAlreadyRegistered
            ? new HotkeyRegistration(HotkeyRegisterStatus.InUse, new Win32Exception(error).Message)
            : new HotkeyRegistration(HotkeyRegisterStatus.Failed, new Win32Exception(error).Message);
    }

    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _callbacks.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _callbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            handled = true;
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                // 콜백 오류가 UI 메시지 루프로 번져 앱이 종료되지 않게 막고, 알림 훅으로 알린다.
                try
                {
                    CallbackFailed?.Invoke(ex);
                }
                catch (Exception)
                {
                    // 알림 자체의 실패도 메시지 루프로 번지지 않게 한다.
                }
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
