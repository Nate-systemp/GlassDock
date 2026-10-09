using System.Runtime.InteropServices;
using System.Text;

namespace GlassDock.Windows.Interop;

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] internal static extern bool ValidateRect(nint window, nint rectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool KillTimer(nint window, nuint id);

    [DllImport("gdi32.dll")]
    internal static extern int CombineRgn(nint destination, nint first, nint second, int mode);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetFrameBounds(nint window, uint attribute, out Rect bounds, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        public uint Size;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Rectangle;
        public nint Parameter;
    }

    [DllImport("shell32.dll")]
    internal static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

    internal delegate nint KeyboardProc(int code, nuint message, nint data);

    [DllImport("user32.dll")]
    internal static extern nint SetWindowsHookExW(int type, KeyboardProc callback, nint module, uint thread);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    internal static extern bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardData
    {
        public uint Key;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    // x64 INPUT is 40 bytes, with its union aligned at offset 8.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Key;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(24)] public nuint ExtraInfo;
    }

    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect monitorRect, nint lParam);

    internal delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint hwnd,
        int objectId,
        int childId,
        uint threadId,
        uint time
    );

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint min,
        uint max,
        nint module,
        WinEventProc callback,
        uint process,
        uint thread,
        uint flags
    );

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Hwnd;
        public uint MessageId;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }

    [DllImport("user32.dll")]
    internal static extern uint MsgWaitForMultipleObjectsEx(
        uint count,
        nint handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        out Message message,
        nint hwnd,
        uint minimum,
        uint maximum,
        uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(nint hwnd);

    internal delegate nint SubclassProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint id,
        nuint data
    );

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BlurBehind
    {
        public uint Flags;
        public int Enable;
        public nint Region;
        public int Transition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint state);

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint hdc,
        nint clip,
        MonitorEnumProc callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint hwnd, StringBuilder name, int count);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hwnd,
        nint after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );

    [DllImport("gdi32.dll")]
    internal static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight
    );

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreatePolygonRgn([In] Point[] points, int count, int fillMode);

    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(
        nint hwnd,
        nint region,
        [MarshalAs(UnmanagedType.Bool)] bool redraw
    );

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowRgn(nint hwnd, nint region);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        nint hwnd,
        uint attribute,
        ref int value,
        int size
    );

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(
        nint hwnd,
        ref Margins margins
    );

    [DllImport("dwmapi.dll")]
    internal static extern int DwmEnableBlurBehindWindow(
        nint hwnd,
        ref BlurBehind blur
    );

    [DllImport("gdi32.dll")]
    internal static extern nint CreateRectRgn(
        int left,
        int top,
        int right,
        int bottom
    );

    [DllImport("gdi32.dll")]
    internal static extern nint GetStockObject(int index);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint hwnd, out Rect rectangle);

    [DllImport("user32.dll")]
    internal static extern int FillRect(nint dc, ref Rect rectangle, nint brush);

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(
        nint hwnd,
        int id,
        uint modifiers,
        uint key
    );

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSUnRegisterSessionNotification(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint RegisterPowerSettingNotification(
        nint recipient,
        ref Guid powerSettingGuid,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterPowerSettingNotification(nint handle);

    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(
        nint hwnd,
        SubclassProc callback,
        nuint id,
        nuint data
    );

    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(
        nint hwnd,
        SubclassProc callback,
        nuint id
    );

    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam
    );
}
