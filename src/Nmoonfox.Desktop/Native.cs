using System;
using System.Runtime.InteropServices;

namespace Nmoonfox.Desktop;

internal static class Native
{
    internal const int ExtendedStyle = -20;
    internal const long NoActivate = 0x08000000, ToolWindow = 0x80, Transparent = 0x20;
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    internal static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
    internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rectangle rect, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] internal struct Rectangle { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo
    {
        internal int Size;
        internal Rectangle Monitor, Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal static bool PointerButtonDown() =>
        (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x04) & 0x8000) != 0 || (GetAsyncKeyState(0x05) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x06) & 0x8000) != 0;
    [StructLayout(LayoutKind.Sequential)] internal struct CursorPoint { internal int X; internal int Y; }
}
