using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DrewPlayer;

internal static class Native
{
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_WINDOWPOSCHANGED = 0x0047;
    public const int WM_SIZING = 0x0214;
    public const int WM_EXITSIZEMOVE = 0x0232;

    public const int HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_NOOWNERZORDER = 0x0200;

    public const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000,
        WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000, SS_BLACKRECT = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int StrCmpLogicalW(string a, string b);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);

    /// <summary>Disables the Windows open/close animation (avoids a visible jump).</summary>
    public static void DisableTransitions(IntPtr hwnd)
    {
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref on, sizeof(int)); } catch { }
    }

    /// <summary>Invisible but "alive" window (mpv can already render into it). DWMWA_CLOAK.</summary>
    public static void Cloak(IntPtr hwnd, bool on)
    {
        int v = on ? 1 : 0;
        try { DwmSetWindowAttribute(hwnd, 13, ref v, sizeof(int)); } catch { }
    }

    public static IntPtr LParam(POINT p) => (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));

    /// <summary>Windows 11: 1 = square corners, 2 = rounded.</summary>
    public static void SetCorners(IntPtr hwnd, bool rounded)
    {
        int pref = rounded ? 2 : 1;
        try { DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int)); } catch { }
    }

    /// <summary>Use the dark title bar even though it is hidden (consistent borders).</summary>
    public static void SetDarkMode(IntPtr hwnd)
    {
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); } catch { }
    }

    public static MONITORINFO MonitorOf(IntPtr hwnd)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* NEAREST */), ref mi);
        return mi;
    }
}
