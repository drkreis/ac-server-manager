using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ServerManager.App;

internal static class NativeWindow
{
    // Keep the native caption style for DWM transitions; WindowChrome draws the caption.
    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(Hook);
        var disabled = 0;
        _ = DwmSetWindowAttribute(handle, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref disabled, sizeof(int));
    }
    private static nint Hook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0x0024 /* WM_GETMINMAXINFO */) return 0;
        var monitor = MonitorFromWindow(handle, 2 /* nearest */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return 0;
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition = new PointInt(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
        bounds.MaxSize = new PointInt(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        Marshal.StructureToPtr(bounds, lParam, false);
        handled = true;
        return 0;
    }
    internal static RectInt WorkArea(nint handle)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) throw new InvalidOperationException("Monitor bounds unavailable.");
        return info.Work;
    }
    internal static Thickness MaximizedInsets(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var work = WorkArea(handle); GetWindowRect(handle, out var bounds);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        return new Thickness(Math.Max(0, work.Left - bounds.Left) / dpi.DpiScaleX,
            Math.Max(0, work.Top - bounds.Top) / dpi.DpiScaleY,
            Math.Max(0, bounds.Right - work.Right) / dpi.DpiScaleX,
            Math.Max(0, bounds.Bottom - work.Bottom) / dpi.DpiScaleY);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointInt(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public PointInt Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] internal struct RectInt { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectInt Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint handle, out RectInt rect);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);
}
