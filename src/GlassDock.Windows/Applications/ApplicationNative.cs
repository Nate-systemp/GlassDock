using System.Runtime.InteropServices;
using System.Text;

namespace GlassDock.Windows.Applications;

internal static class ApplicationNative
{
    internal delegate bool EnumProc(nint window, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(nint parent, EnumProc callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] internal static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? text);
    [DllImport("kernel32.dll")] internal static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint flags, StringBuilder path, ref uint length);
    [DllImport("shell32.dll")] internal static extern int SHGetPropertyStoreForWindow(nint window, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("shell32.dll")] internal static extern int SHGetPropertyStoreFromIDList(nint pidl, uint flags, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] internal static extern int PSGetPropertyKeyFromName(string name, out PropertyKey key);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] internal static extern int PropVariantToStringAlloc(in PropVariant value, out nint text);
    [DllImport("ole32.dll")] internal static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shell32.dll")] internal static extern int SHGetNameFromIDList(nint pidl, uint name, out nint text);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint attributes, out uint found);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern nuint SHGetFileInfo(nint pidl, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] internal static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Value; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ShellFileInfo
    {
        public nint Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize;
        public int XPixels, YPixels; public uint Colors, Important, Color;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct ShellExecuteInfo
    {
        public uint Size, Mask; public nint Window;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Directory;
        public int Show; public nint Instance, IdList, Class; public nint ClassKey; public uint HotKey; public nint Icon, Process;
    }
    // Only the enumeration slot is declared: this adapter cannot mutate the pinned list.
    [ComImport, Guid("0DD79AE2-D156-45D4-9EEB-3B549769E940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPinnedList { [PreserveSig] int EnumObjects(out IEnumFullIdList items); }
    [ComImport, Guid("d0191542-7954-4908-bc06-b2360bbe45ba"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumFullIdList { [PreserveSig] int Next(uint count, out nint pidl, out uint fetched); }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, nint findData, uint flags);
        void GetIdList(out nint pidl);
        void SetIdList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
    }
}


