using System.Globalization;
using System.Runtime.InteropServices;

namespace DrewPlayer;

/// <summary>Thread-safe wrapper: commands from any thread, events on a dedicated thread.</summary>
internal sealed class Mpv : IDisposable
{
    private IntPtr _h;
    private Thread? _loop;
    private volatile bool _stop;

    /// <summary>Raised on the mpv event thread (NOT the UI thread).</summary>
    public event Action<string, object?>? PropertyChanged;
    /// <summary>Raised on the mpv event thread (NOT the UI thread).</summary>
    public event Action<MpvEventId>? EventRaised;
    /// <summary>(reason, error). reason 4 = error. Event thread.</summary>
    public event Action<int, int>? EndFile;

    public Mpv()
    {
        _h = MpvNative.mpv_create();
        if (_h == IntPtr.Zero) throw new InvalidOperationException("mpv_create fallito");
    }

    public bool SetOption(string name, string value) => MpvNative.mpv_set_option_string(_h, name, value) >= 0;

    public void Initialize()
    {
        Check(MpvNative.mpv_initialize(_h), "mpv_initialize");
        _loop = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _loop.Start();
    }

    public void Observe(string name, MpvFormat format) =>
        MpvNative.mpv_observe_property(_h, 0, name, (int)format);

    /// <summary>Asynchronous command (does not block the UI).</summary>
    public void Command(params string[] args) => Run(args, async: true);

    /// <summary>Synchronous command (waits for mpv to run it).</summary>
    public int CommandSync(params string[] args) => Run(args, async: false);

    public int CommandString(string cmd) => MpvNative.mpv_command_string(_h, cmd);

    public bool Set(string name, string value) => MpvNative.mpv_set_property_string(_h, name, value) >= 0;
    public bool Set(string name, double value) => Set(name, value.ToString(CultureInfo.InvariantCulture));
    public bool Set(string name, bool value) => Set(name, value ? "yes" : "no");

    public string? GetString(string name)
    {
        var p = MpvNative.mpv_get_property_string(_h, name);
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); }
        finally { MpvNative.mpv_free(p); }
    }

    public double? GetDouble(string name) =>
        MpvNative.mpv_get_property_double(_h, name, (int)MpvFormat.Double, out var v) >= 0 ? v : null;

    public bool? GetFlag(string name) =>
        MpvNative.mpv_get_property_int(_h, name, (int)MpvFormat.Flag, out var v) >= 0 ? v != 0 : null;

    private int Run(string[] args, bool async)
    {
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            ptrs[^1] = IntPtr.Zero;
            return async ? MpvNative.mpv_command_async(_h, 0, ptrs) : MpvNative.mpv_command(_h, ptrs);
        }
        finally
        {
            for (int i = 0; i < args.Length; i++)
                if (ptrs[i] != IntPtr.Zero) Marshal.FreeCoTaskMem(ptrs[i]);
        }
    }

    private void EventLoop()
    {
        while (!_stop)
        {
            var ev = MpvNative.mpv_wait_event(_h, -1);
            if (ev == IntPtr.Zero) continue;
            var id = (MpvEventId)Marshal.ReadInt32(ev, 0);
            if (id == MpvEventId.None) continue;
            if (id == MpvEventId.Shutdown) break;

            try
            {
                if (id == MpvEventId.PropertyChange)
                {
                    // struct mpv_event { int id; int error; uint64 reply; void* data; }
                    // struct mpv_event_property { char* name; int format; void* data; }
                    var data = Marshal.ReadIntPtr(ev, 16);
                    var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 0)) ?? "";
                    var fmt = (MpvFormat)Marshal.ReadInt32(data, 8);
                    var vp = Marshal.ReadIntPtr(data, 16);
                    object? val = null;
                    if (vp != IntPtr.Zero)
                    {
                        val = fmt switch
                        {
                            MpvFormat.String => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(vp)),
                            MpvFormat.Flag => Marshal.ReadInt32(vp) != 0,
                            MpvFormat.Int64 => Marshal.ReadInt64(vp),
                            MpvFormat.Double => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(vp)),
                            _ => null,
                        };
                    }
                    PropertyChanged?.Invoke(name, val);
                }
                else if (id == MpvEventId.EndFile)
                {
                    // struct mpv_event_end_file { int reason; int error; ... }
                    var data = Marshal.ReadIntPtr(ev, 16);
                    int reason = data != IntPtr.Zero ? Marshal.ReadInt32(data, 0) : 0;
                    int error = data != IntPtr.Zero ? Marshal.ReadInt32(data, 4) : 0;
                    EndFile?.Invoke(reason, error);
                }
                else
                {
                    EventRaised?.Invoke(id);
                }
            }
            catch
            {
                // never let the event thread die
            }
        }
    }

    private static void Check(int err, string what)
    {
        if (err < 0)
            throw new InvalidOperationException($"{what}: {Marshal.PtrToStringUTF8(MpvNative.mpv_error_string(err))}");
    }

    public void Dispose()
    {
        if (_h == IntPtr.Zero) return;
        _stop = true;
        MpvNative.mpv_wakeup(_h);
        _loop?.Join(1500);
        MpvNative.mpv_terminate_destroy(_h);
        _h = IntPtr.Zero;
    }
}
