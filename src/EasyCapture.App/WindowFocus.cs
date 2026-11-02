using System.Runtime.InteropServices;
using System.Text;
using EasyCapture.Core;

namespace EasyCapture.App;

/// <summary>
/// 클립보드 패널이 쓰는 Win32 창 조작 모음: 포그라운드 조회·가져오기, 붙여넣기 대상 판정 정보 조회, Ctrl+V 전송, 창 위치 지정.
/// 모든 호출은 예외를 던지지 않고 실패하면 false/기본값을 돌려준다.
/// </summary>
public static class WindowFocus
{
    public const int VkShift = 0x10;
    public const int VkMenu = 0x12;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;

    private const int SwRestore = 9;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static IntPtr GetForeground() => NativeMethods.GetForegroundWindow();

    /// <summary>키가 지금 실제로 눌려 있는지(가상 키 코드).</summary>
    public static bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>
    /// 창을 최상위로 올리고 포그라운드로 만든다. 다른 앱이 활성일 때 SetForegroundWindow가 거부되지 않도록
    /// 현재 포그라운드 창의 입력 스레드에 잠깐 붙었다 떼는 방식이다(CaptureOverlayWindow와 같은 패턴).
    /// 최소화된 창은 먼저 복원한다. 결과적으로 포그라운드가 됐으면 true.
    /// </summary>
    public static bool Acquire(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, SwRestore);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    /// <summary>핸들이 가리키는 창이 지금도 있는지.</summary>
    public static bool Exists(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd);

    /// <summary>핸들의 창이 붙여넣기 대상으로 적합한지 판정한다(<see cref="PasteTarget"/>에 조회 값을 넘긴다).</summary>
    public static PasteTargetVerdict JudgePasteTarget(IntPtr hwnd)
    {
        var exists = Exists(hwnd);
        uint processId = 0;
        string? className = null;
        if (exists)
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            var buffer = new StringBuilder(256);
            className = NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
        }

        return PasteTarget.Judge(hwnd, exists, processId, (uint)Environment.ProcessId, className);
    }

    /// <summary>
    /// 포그라운드 창으로 Ctrl+V를 보낸다(SendInput 4건). 전부 주입됐으면 true.
    /// 실제 키 입력이 데스크톱으로 가므로 자동 시험에서 호출하지 않는다.
    /// </summary>
    public static bool SendCtrlV()
    {
        var inputs = new[]
        {
            KeyInput(VkControl, 0),
            KeyInput(VkV, 0),
            KeyInput(VkV, KeyEventKeyUp),
            KeyInput(VkControl, KeyEventKeyUp),
        };
        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
    }

    /// <summary>창의 바깥 사각형(물리 픽셀). 실패하면 false.</summary>
    public static bool TryGetBounds(IntPtr hwnd, out PixelRect bounds)
    {
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            bounds = PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
        }

        bounds = PixelRect.Empty;
        return false;
    }

    /// <summary>창을 물리 픽셀 사각형으로 옮기고 크기를 맞춘다(활성화·Z순서는 건드리지 않는다).</summary>
    public static void SetBounds(IntPtr hwnd, PixelRect bounds)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate);
        }
    }

    /// <summary>창을 크기는 그대로 둔 채 옮긴다.</summary>
    public static void MoveTo(IntPtr hwnd, int x, int y)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
    }

    private static NativeMethods.Input KeyInput(ushort virtualKey, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeyboardInput { VirtualKey = virtualKey, Flags = flags },
        },
    };

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        // 공용체 크기가 가장 큰 멤버(MouseInput) 기준이어야 INPUT 구조체 크기가 맞는다.
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MouseInput Mouse;

            [FieldOffset(0)]
            public KeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public uint Type;
            public InputUnion Data;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);
    }
}
