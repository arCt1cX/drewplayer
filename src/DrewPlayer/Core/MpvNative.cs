using System.Globalization;
using System.Runtime.InteropServices;

namespace DrewPlayer;

/// <summary>Minimal P/Invoke over libmpv-2.dll (the mpv C client API).</summary>
internal static class MpvNative
{
    private const string Lib = "libmpv-2.dll";

    [DllImport(Lib)] public static extern IntPtr mpv_create();
    [DllImport(Lib)] public static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Lib)] public static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Lib)] public static extern void mpv_wakeup(IntPtr ctx);
    [DllImport(Lib)] public static extern void mpv_free(IntPtr data);
    [DllImport(Lib)] public static extern IntPtr mpv_error_string(int error);
    [DllImport(Lib)] public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

    [DllImport(Lib)]
    public static extern int mpv_set_option_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(Lib)]
    public static extern int mpv_set_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(Lib)]
    public static extern IntPtr mpv_get_property_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_double(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);

    [DllImport(Lib, EntryPoint = "mpv_get_property")]
    public static extern int mpv_get_property_int(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out int data);

    [DllImport(Lib)]
    public static extern int mpv_observe_property(IntPtr ctx, ulong reply,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    [DllImport(Lib)] public static extern int mpv_command(IntPtr ctx, IntPtr[] args);
    [DllImport(Lib)] public static extern int mpv_command_async(IntPtr ctx, ulong reply, IntPtr[] args);

    [DllImport(Lib)]
    public static extern int mpv_command_string(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string args);
}

internal enum MpvFormat { None = 0, String = 1, Flag = 3, Int64 = 4, Double = 5 }

internal enum MpvEventId
{
    None = 0, Shutdown = 1, LogMessage = 2, StartFile = 6, EndFile = 7, FileLoaded = 8,
    VideoReconfig = 17, AudioReconfig = 18, Seek = 20, PlaybackRestart = 21, PropertyChange = 22,
}
