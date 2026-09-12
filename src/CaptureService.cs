using System.Text;

namespace CpuScreenViewer;

internal sealed record WindowInfo(IntPtr Hwnd, string Title, uint Pid, string App, bool Minimized);

internal static class CaptureService
{
    private static readonly HashSet<string> SkipClasses =
    [
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow"
    ];

    public static List<WindowInfo> ListWindows(int excludePid)
    {
        var list = new List<WindowInfo>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!IsAltTab(hwnd))
                return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)excludePid)
                return true;
            list.Add(new WindowInfo(hwnd, GetTitle(hwnd), pid, GetProcessName(pid), Native.IsIconic(hwnd)));
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(w => w.App, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryGetWindowBounds(IntPtr hwnd, out Rectangle bounds)
    {
        bounds = default;
        if (!Native.IsWindow(hwnd) || Native.IsIconic(hwnd))
            return false;
        if (!Native.GetClientRect(hwnd, out var rect) || rect.Width < 8 || rect.Height < 8)
            return false;
        var origin = new Native.Point { X = rect.Left, Y = rect.Top };
        if (!Native.ClientToScreen(hwnd, ref origin))
            return false;
        bounds = new Rectangle(origin.X, origin.Y, rect.Width, rect.Height);
        return true;
    }

    private static bool IsAltTab(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd))
            return false;
        Native.DwmGetWindowAttribute(hwnd, Native.DwmwaCloaked, out var cloaked, sizeof(int));
        if (cloaked != 0)
            return false;
        var title = GetTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
            return false;
        var cls = GetClass(hwnd);
        if (SkipClasses.Contains(cls))
            return false;
        var style = Native.GetWindowLongPtr(hwnd, -16).ToInt64();
        if ((style & Native.WsVisible) == 0)
            return false;
        var ex = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle).ToInt64();
        if ((ex & Native.WsExToolwindow) != 0 && (ex & Native.WsExAppwindow) == 0)
            return false;
        return true;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        Native.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        Native.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(uint pid)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimited, false, pid);
        if (handle == IntPtr.Zero)
            return $"pid {pid}";
        try
        {
            var size = 32768;
            var sb = new StringBuilder(size);
            if (Native.QueryFullProcessImageName(handle, 0, sb, ref size))
                return Path.GetFileName(sb.ToString());
            return $"pid {pid}";
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }
}
