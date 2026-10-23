using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Nmoonfox.Core;

namespace Nmoonfox.Desktop;

internal static class DisplayCatalog
{
    internal static IReadOnlyList<DesktopDisplay> Read()
    {
        var displays = new List<DesktopDisplay>();
        Native.MonitorCallback callback = (IntPtr monitor, IntPtr dc, ref Native.Rectangle rect, IntPtr data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) return true;
            var area = info.Work;
            if (area.Right <= area.Left || area.Bottom <= area.Top) return true;
            // A never-shown native window reports exact effective DPI, including custom scaling.
            // GetDpiForMonitor is not recommended for per-monitor-aware calling threads.
            var probe = Native.CreateWindowEx((uint)(Native.ToolWindow | Native.NoActivate), "STATIC", "",
                0x80000000, (area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2, 1, 1,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            uint dpi;
            try { dpi = probe == IntPtr.Zero ? 0 : Native.GetDpiForWindow(probe); }
            finally { if (probe != IntPtr.Zero) Native.DestroyWindow(probe); }
            if (dpi == 0) dpi = Native.GetDpiForSystem();
            displays.Add(new(info.Device, new(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top),
                dpi / 96.0, (info.Flags & 1) != 0));
            return true;
        };
        if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || displays.Count == 0)
            throw new InvalidOperationException("ディスプレイの情報を取得できませんでした。");
        return displays;
    }
}
