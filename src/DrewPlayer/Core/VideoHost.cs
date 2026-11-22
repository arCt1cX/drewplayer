using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DrewPlayer;

/// <summary>Black Win32 child window that mpv renders the video into (--wid).</summary>
internal sealed class VideoHost : HwndHost
{
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var h = Native.CreateWindowEx(0, "static", "",
            Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN | Native.WS_CLIPSIBLINGS | Native.SS_BLACKRECT,
            0, 0, 16, 16, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, h);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) => Native.DestroyWindow(hwnd.Handle);
}
