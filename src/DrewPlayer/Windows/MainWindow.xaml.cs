using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>
/// Video window (mpv renders here) + player controller.
/// All the UI and input live in OverlayWindow, which sits on top of it.
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private Settings S => _store.Current;
    private readonly string? _startFile;

    private readonly VideoHost _video = new();
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWin;
    private Mpv? _mpv;
    private IntPtr _hwnd;

    // state (written by the mpv thread, read by the UI)
    public volatile bool Paused;
    public volatile bool Muted;
    private double _timePos, _duration, _volume = 80, _speed = 1, _zoom;
    public double TimePos => Volatile.Read(ref _timePos);
    public double Duration => Volatile.Read(ref _duration);
    public double Volume => Volatile.Read(ref _volume);
    public double Speed => Volatile.Read(ref _speed);
    public double Zoom => Volatile.Read(ref _zoom);

    public string? CurrentFile { get; private set; }
    public bool IsFullscreen { get; private set; }
    public bool AudioOnly { get; private set; }
    public bool IsImage { get; private set; }
    public int FolderIndex => _folderIndex;
    public int FolderCount => _folder.Count;

    private List<string> _folder = new();
    private int _folderIndex = -1;
    private Native.RECT _restoreRect;
    private bool _userResized;
    private bool _fitPending;
    private bool _eofHandled;
    private readonly Dictionary<string, string> _defaultScalers = new();

    internal MainWindow(SettingsStore store, string? startFile)
    {
        InitializeComponent();
#if DEBUG
        if (BenchMode) ShowActivated = false; // headless benchmark: never steal focus
#endif
        _store = store;
        _startFile = startFile;
        Width = Math.Max(MinWidth, S.Window.Width);
        Height = Math.Max(MinHeight, S.Window.Height);
        if (S.Playback.StartFullscreen)
        {
            // fullscreen position/size up front, so WPF never shows it centered first
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0; Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
        Root.Children.Add(_video);
        if (App.AppIcon != null) Icon = App.AppIcon;
        App.IconChanged += OnIconChanged;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Start);
        Activated += (_, _) => { if (_settingsWin == null || !_settingsWin.IsActive) _overlay?.Activate(); };
        Closing += (_, _) => Shutdown();
        StateChanged += OnStateChanged;
    }

    private bool _pausedByMinimize;

    /// <summary>Minimize → pause (per settings); restore → resume.</summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_mpv == null || CurrentFile == null || IsImage) return;
        if (WindowState == WindowState.Minimized)
        {
            var mode = S.Playback.MinimizeAction;
            bool pause = mode == "pause_all" || (mode == "pause_video" && !AudioOnly);
            if (pause && !Paused)
            {
                _mpv.Set("pause", true);
                _pausedByMinimize = true;
            }
        }
        else if (_pausedByMinimize)
        {
            _pausedByMinimize = false;
            _mpv.Set("pause", false);
        }
    }

    /// <summary>mpv is ready: files can be opened.</summary>
    public event Action? Ready;

    private void OnIconChanged()
    {
        if (App.AppIcon == null) return;
        Icon = App.AppIcon;
        if (_overlay != null) _overlay.Icon = App.AppIcon;
    }


    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetDarkMode(_hwnd);
        Native.DisableTransitions(_hwnd);
        Native.SetCorners(_hwnd, S.Window.RoundedCorners);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        if (S.Playback.StartFullscreen) EnterFullscreenBeforeShow();
        // with a file to open: stay invisible until mpv has shown the first frame
        // (no "empty" window showing what's behind it, no size jumps)
        if (_startFile != null)
        {
            _hiddenUntilFit = true;
            Native.Cloak(_hwnd, true);
        }
    }

    private bool _hiddenUntilFit;
    private bool _revealedRaised;

    /// <summary>The window is visible (the intro can fade out).</summary>
    public event Action? Revealed;

    /// <summary>Shows window + UI (after the first frame).</summary>
    private void Reveal()
    {
#if DEBUG
        if (BenchMode)
        {
            // stays cloaked: measure + screenshot, then exit
            if (!_revealedRaised) { _revealedRaised = true; RunBench(); }
            return;
        }
#endif
        if (_hiddenUntilFit)
        {
            _hiddenUntilFit = false;
            Native.Cloak(_hwnd, false);
            if (_overlay != null) { Native.Cloak(_overlay.Handle, false); _overlay.Activate(); }
        }
        if (!_revealedRaised) { _revealedRaised = true; Revealed?.Invoke(); }
    }

    /// <summary>
    /// "Open in fullscreen": the window is born fullscreen (no jump from a small window).
    /// Called before the window becomes visible.
    /// </summary>
    private void EnterFullscreenBeforeShow()
    {
        var mi = Native.MonitorOf(_hwnd);
        var mon = mi.rcMonitor;
        var work = mi.rcWork;
        // where to go when leaving fullscreen: a normal centered window
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        int w = Math.Min(work.Width, (int)(Math.Max(MinWidth, S.Window.Width) * dpi.DpiScaleX));
        int h = Math.Min(work.Height, (int)(Math.Max(MinHeight, S.Window.Height) * dpi.DpiScaleY));
        _restoreRect = new Native.RECT
        {
            Left = work.Left + (work.Width - w) / 2, Top = work.Top + (work.Height - h) / 2,
            Right = work.Left + (work.Width - w) / 2 + w, Bottom = work.Top + (work.Height - h) / 2 + h,
        };
        IsFullscreen = true;
        Native.SetCorners(_hwnd, false);
        Native.SetWindowPos(_hwnd, IntPtr.Zero, mon.Left, mon.Top, mon.Width, mon.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_WINDOWPOSCHANGED: SyncOverlay(); break;
            case Native.WM_SIZING: _userResized = true; break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Startup timeline for the debug benchmark (no-op in release builds).</summary>
    internal static void Mark(string what) => MarkImpl(what);
    static partial void MarkImpl(string what);

    private async void Start()
    {
        Mark("main window loaded");
        _overlay = new OverlayWindow(this, _store) { Owner = this, ShowActivated = ShowActivated };
        if (App.AppIcon != null) _overlay.Icon = App.AppIcon;
        if (_hiddenUntilFit)
        {
            Native.Cloak(new WindowInteropHelper(_overlay).EnsureHandle(), true);
            // safety net: if the video never reports its size, show anyway
            var fallback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            fallback.Tick += (_, _) => { fallback.Stop(); Reveal(); };
            fallback.Start();
        }
        _overlay.Show();
        SyncOverlay();
        if (IsFullscreen) _overlay.OnFullscreenChanged(true);
        if (ShowActivated) _overlay.Activate();

        try
        {
            // mpv starts off the UI thread (loading the 100+ MB engine), so the intro animation stays smooth
            var handle = _video.Handle;
            Mark("mpv init start");
            _mpv = await Task.Run(() => InitMpv(handle));
            Mark("mpv init done");
            ApplyQuality();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            MessageBox.Show("Could not start mpv:\n" + ex.Message, "DrewPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        _store.Reloaded += OnSettingsReloaded;
        _store.Watch(Dispatcher);
        if (_store.LastError != null) _overlay.Osd("Error in settings.json — " + _store.LastError, 5000, force: true);

        if (_startFile != null) Open(_startFile);
        Ready?.Invoke();
        if (!_hiddenUntilFit) Reveal(); // no file: show the empty window right away
    }

    private Mpv InitMpv(IntPtr wid)
    {
        var m = new Mpv();
        var p = S.Playback;
        m.SetOption("wid", wid.ToInt64().ToString());
        m.SetOption("config-dir", SettingsStore.Dir);
        m.SetOption("config", "yes");
        m.SetOption("input-default-bindings", "no");
        m.SetOption("input-vo-keyboard", "no");
        m.SetOption("input-cursor", "no");
        m.SetOption("osc", "no");
        m.SetOption("osd-level", "0");
        m.SetOption("cursor-autohide", "no");
        m.SetOption("idle", "yes");
        m.SetOption("force-window", "yes");
        m.SetOption("keep-open", "yes");
        m.SetOption("ytdl", "no");
        m.SetOption("image-display-duration", "inf");
        m.SetOption("hwdec", p.Hwdec);
        m.SetOption("volume", Num.Inv(Math.Clamp(p.Volume, 0, p.VolumeMax)));
        m.SetOption("volume-max", Num.Inv(Math.Max(100, p.VolumeMax)));
        m.SetOption("loop-file", p.LoopFile ? "inf" : "no");
        m.SetOption("sub-auto", p.SubAuto ? "fuzzy" : "no");
        m.SetOption("sub-font-size", Num.Inv(Math.Clamp(p.SubFontSize, 10, 120)));
        m.SetOption("audio-display", "embedded-first");
        m.SetOption("hr-seek", "yes");
        // read cache: seeking inside the already-read part is exact even in files without an index
        m.SetOption("cache", "yes");
        m.SetOption("demuxer-max-bytes", "64MiB");
        m.SetOption("demuxer-max-back-bytes", "32MiB");
        m.SetOption("resume-playback", p.Resume ? "yes" : "no");
        var wl = IOPath.Combine(SettingsStore.Dir, "watch_later");
        Directory.CreateDirectory(wl);
        if (!m.SetOption("watch-later-dir", wl)) m.SetOption("watch-later-directory", wl);
        m.SetOption("watch-later-options", "start");
        var shots = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DrewPlayer");
        if (!m.SetOption("screenshot-dir", shots)) m.SetOption("screenshot-directory", shots);

        m.PropertyChanged += OnMpvProperty;
        m.EventRaised += OnMpvEvent;
        m.EndFile += OnEndFile;
        m.Initialize();

        foreach (var k in new[] { "scale", "cscale", "dscale", "deband" })
            _defaultScalers[k] = m.GetString(k) ?? "";

        m.Observe("time-pos", MpvFormat.Double);
        m.Observe("duration", MpvFormat.Double);
        m.Observe("pause", MpvFormat.Flag);
        m.Observe("mute", MpvFormat.Flag);
        m.Observe("volume", MpvFormat.Double);
        m.Observe("speed", MpvFormat.Double);
        m.Observe("video-zoom", MpvFormat.Double);
        m.Observe("eof-reached", MpvFormat.Flag);
        m.Observe("media-title", MpvFormat.String);
        return m;
    }

    /// <summary>High quality = better scalers (same source resolution, just cleaner scaling).</summary>
    private void ApplyQuality()
    {
        if (_mpv == null) return;
        if (S.Playback.HighQuality)
        {
            _mpv.Set("scale", "ewa_lanczossharp");
            _mpv.Set("cscale", "ewa_lanczossharp");
            _mpv.Set("dscale", "mitchell");
            _mpv.Set("deband", true);
        }
        else
        {
            foreach (var (k, v) in _defaultScalers)
                if (v.Length > 0) _mpv.Set(k, v);
        }
    }


    public static string FormatTime(double s)
    {
        if (double.IsNaN(s) || s < 0) s = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static string FormatSpan(double s) => s >= 60 && s % 60 == 0 ? $"{s / 60:0} min" : $"{s:0.#}s";

    private bool _shutdown;
    private void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        // disappear immediately, then clean up (save, shut mpv down) with the windows already hidden
        if (_overlay != null && _overlay.Handle != IntPtr.Zero) Native.ShowWindow(_overlay.Handle, 0);
        if (_hwnd != IntPtr.Zero) Native.ShowWindow(_hwnd, 0);
        App.IconChanged -= OnIconChanged;
        try { SaveResume(); } catch { }
        _settingsWin?.Close();
        _store.Dispose();
        _mpv?.Dispose();
        _mpv = null;
        _overlay?.ForceClose();
    }
}
