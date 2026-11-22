using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace DrewPlayer;

public partial class MainWindow
{
    public void ToggleFullscreen()
    {
        if (!IsFullscreen)
        {
            Native.GetWindowRect(_hwnd, out _restoreRect);
            var mon = Native.MonitorOf(_hwnd).rcMonitor;
            IsFullscreen = true;
            // no Topmost: covers the taskbar while focused, but Alt+Tab brings other windows to the front
            Native.SetCorners(_hwnd, false);
            Native.SetWindowPos(_hwnd, IntPtr.Zero, mon.Left, mon.Top, mon.Width, mon.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }
        else
        {
            IsFullscreen = false;
            Native.SetCorners(_hwnd, S.Window.RoundedCorners);
            var r = _restoreRect;
            Native.SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }
        _overlay?.OnFullscreenChanged(IsFullscreen);
        SyncOverlay();
    }

    private void ResizeCentered(int w, int h, bool dips)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (dips)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            w = (int)(w * dpi.DpiScaleX);
            h = (int)(h * dpi.DpiScaleY);
        }
        Native.GetWindowRect(_hwnd, out var cur);
        var work = Native.MonitorOf(_hwnd).rcWork;
        int cx = (cur.Left + cur.Right) / 2, cy = (cur.Top + cur.Bottom) / 2;
        int x = Math.Clamp(cx - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        int y = Math.Clamp(cy - h / 2, work.Top, Math.Max(work.Top, work.Bottom - h));
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>Keeps the overlay exactly on top of the video window.</summary>
    private void SyncOverlay()
    {
        if (_overlay == null || _hwnd == IntPtr.Zero || Native.IsIconic(_hwnd)) return;
        var oh = _overlay.Handle;
        if (oh == IntPtr.Zero) return;
        Native.GetWindowRect(_hwnd, out var r);
        Native.SetWindowPos(oh, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    /// <summary>Called by the overlay: starts a drag (HTCAPTION) or resize (HT*) of the video window.</summary>
    public void BeginNativeDrag(int hit)
    {
        if (IsFullscreen) return;
        Native.ReleaseCapture();
        Native.GetCursorPos(out var p);
        Native.SendMessage(_hwnd, Native.WM_NCLBUTTONDOWN, (IntPtr)hit, Native.LParam(p));
    }

}
