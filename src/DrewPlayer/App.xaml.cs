using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace DrewPlayer;

public partial class App : Application
{
    internal static readonly string[] VideoExt =
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".3gp", ".ogv", ".vob", ".divx"];
    internal static readonly string[] AudioExt =
        [".mp3", ".wav", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".ape", ".mka", ".wv"];
    internal static readonly string[] ImageExt =
        [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".avif", ".heic", ".heif", ".jxl", ".jfif"];

    /// <summary>Extensions of files still being downloaded (Chrome/Edge, Firefox, qBittorrent, Opera...).</summary>
    internal static readonly string[] PartialExt = [".crdownload", ".part", ".partial", ".download", ".!qb", ".opdownload", ".!ut"];

    internal static bool IsPartial(string path) => PartialExt.Contains(IOPath.GetExtension(path).ToLowerInvariant());

    /// <summary>Real extension: video.mp4.crdownload → .mp4</summary>
    private static string Ext(string path)
    {
        var e = IOPath.GetExtension(path).ToLowerInvariant();
        return PartialExt.Contains(e) ? IOPath.GetExtension(path[..^e.Length]).ToLowerInvariant() : e;
    }

    internal static bool IsImage(string path) => !IsPartial(path) && ImageExt.Contains(Ext(path));
    internal static bool IsAv(string path) { var e = Ext(path); return VideoExt.Contains(e) || AudioExt.Contains(e); }
    internal static bool IsMedia(string path) => IsAv(path) || IsImage(path) || IsPartial(path);

    /// <summary>
    /// Is the file still being written? (download extension, or modified just now and growing)
    /// </summary>
    internal static bool IsGrowing(string path)
    {
        if (path.Contains("://")) return false;
        if (IsPartial(path)) return true;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || DateTime.UtcNow - fi.LastWriteTimeUtc > TimeSpan.FromSeconds(8)) return false;
            long a = fi.Length;
            Thread.Sleep(300);
            fi.Refresh();
            return fi.Length != a;
        }
        catch { return false; }
    }

    internal SettingsStore Store { get; } = new();

    /// <summary>Current app icon (colored according to settings).</summary>
    internal static BitmapSource? AppIcon { get; private set; }
    internal static event Action? IconChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        DrewPlayer.MainWindow.Mark("App.OnStartup");
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            LogError(ex.Exception);
            MessageBox.Show(ex.Exception.Message, "DrewPlayer - error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        Store.Load();
        DrewPlayer.MainWindow.Mark("settings loaded");

#if DEBUG
        // test only: renders the settings pages off-screen and saves PNGs
        var dump = Environment.GetEnvironmentVariable("DREWPLAYER_DUMP_SETTINGS");
        if (!string.IsNullOrEmpty(dump))
        {
            ApplyAccent(Store.Current.Ui.AccentColor);
            SyncIcon(Store.Current);
            var sw = new SettingsWindow(Store)
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0,
                ShowActivated = false, ShowInTaskbar = false,
            };
            sw.Show();
            sw.DebugDump(dump).ContinueWith(_ => Dispatcher.Invoke(() => Shutdown(0)));
            return;
        }
#endif
        // command-line modes (used by install.ps1)
        if (e.Args.Contains("--register"))
        {
            RegisterSelf(Store.Current);
            Registration.OpenDefaultAppsPage();
            Shutdown(0);
            return;
        }
        if (e.Args.Contains("--sync-icons"))
        {
            // used by install.ps1: refresh icons and registration without opening any window
            if (Registration.NeedsUpgrade &&
                string.Equals(Registration.RegisteredExe, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                Registration.Register(Environment.ProcessPath!, IconRef(Store.Current));
            SyncIcon(Store.Current, force: true);
            Shutdown(0);
            return;
        }
        if (e.Args.Contains("--unregister"))
        {
            Registration.Unregister();
            Shutdown(0);
            return;
        }

        if (!File.Exists(IOPath.Combine(AppContext.BaseDirectory, "libmpv-2.dll")))
        {
            MessageBox.Show("libmpv-2.dll is missing next to DrewPlayer.exe.\nWithout the mpv engine nothing can be played.",
                "DrewPlayer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var files = e.Args.Where(a => !a.StartsWith("--"))
            .Select(a => a.Contains("://") ? a : IOPath.GetFullPath(a)).ToList();

        // already running? hand the files to that instance (they become tabs) and exit
#if DEBUG
        if (DrewPlayer.MainWindow.BenchMode) { Store.Current.Ui.SingleWindow = false; Store.Current.Splash.Enabled = false; }
#endif
        if (Store.Current.Ui.SingleWindow)
        {
            if (SingleInstance.TryForward(files)) { Shutdown(0); return; }
            SingleInstance.StartServer(msg => Dispatcher.BeginInvoke(() => OnRemoteOpen(msg)));
        }

        ApplyAccent(Store.Current.Ui.AccentColor);
        // registered by an older version of this same copy: upgrade it (new types/icons)
        if (Registration.NeedsUpgrade &&
            string.Equals(Registration.RegisteredExe, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            Registration.Register(Environment.ProcessPath!, IconRef(Store.Current));
        // colored icons are not needed for the first frame: build them once the app is idle
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => SyncIcon(Store.Current));

        var file = files.FirstOrDefault();
        _pending.AddRange(files.Skip(1));

        if (Store.Current.Splash.Enabled)
        {
            // The player starts right away, hidden, while the intro plays; the intro fades out
            // once its animation is over AND the first video frame is on screen.
            var splash = new SplashWindow(Store.Current.Splash, Store.Current.Ui.AccentColor);
            bool animationDone = false, playerVisible = false;
            void TryDismiss() { if (animationDone && playerVisible) splash.Dismiss(); }
            splash.Finished += () => { animationDone = true; TryDismiss(); };
            splash.Show();
            ShowMain(file);
            _main!.Revealed += () => { playerVisible = true; TryDismiss(); };
        }
        else
        {
            ShowMain(file);
        }
    }

    private readonly List<string> _pending = new();
    private MainWindow? _main;
    private bool _mainReady;

    private void ShowMain(string? file)
    {
        var w = new MainWindow(Store, file);
        _main = w;
        MainWindow = w;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        w.Ready += () =>
        {
            _mainReady = true;
            foreach (var p in _pending) w.OpenInNewTab(p);
            _pending.Clear();
        };
        DrewPlayer.MainWindow.Mark("main window created");
        w.Show();
        DrewPlayer.MainWindow.Mark("main window shown");
    }

    /// <summary>File received from another instance (double-click in File Explorer).</summary>
    private void OnRemoteOpen(string msg)
    {
        if (_main == null || !_mainReady)
        {
            if (msg != "\u0001focus") _pending.Add(msg);
            return;
        }
        if (msg == "\u0001focus")
        {
            if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
            _main.Activate();
            return;
        }
        _main.OpenFromOutside(msg);
    }

    /// <summary>Registers this exe as a player (HKCU) with the icon in the chosen color.</summary>
    internal static void RegisterSelf(Settings s)
    {
        Registration.Register(Environment.ProcessPath!, IconRef(s));
        SyncIcon(s, force: true);
    }

    /// <summary>File icon color for each type.</summary>
    internal static Color FileIconColor(Settings s, IconKind k) => k switch
    {
        IconKind.Video => ParseColor(s.Ui.FileIconVideo, "#1D9E75"),
        IconKind.Audio => ParseColor(s.Ui.FileIconAudio, "#7F77DD"),
        IconKind.Image => ParseColor(s.Ui.FileIconImage, "#D85A30"),
        _ => ParseColor(s.Ui.FileIconDownload, "#378ADD"),
    };

    internal static Color IconColorOf(Settings s) =>
        ParseColor(string.IsNullOrWhiteSpace(s.Ui.IconColor) ? s.Ui.AccentColor : s.Ui.IconColor, "#1D9E75");

    /// <summary>Icon reference for the registry: the exe for the stock color, otherwise the generated icon.ico.</summary>
    private static string IconRef(Settings s) =>
        IconColorOf(s) == ParseColor("#1D9E75") ? $"\"{Environment.ProcessPath}\",0" : IconMaker.IconPath;

    /// <summary>Regenerates the icon in the chosen color and updates windows, file associations and Start menu.</summary>
    internal static void SyncIcon(Settings s, bool force = false)
    {
        var c = IconColorOf(s);
        AppIcon = IconMaker.Render(c, 256);
        bool changed = IconMaker.Ensure(c);
        // only touch the registry if this is the registered copy (not development builds)
        bool registered = Registration.IsRegistered &&
            string.Equals(Registration.RegisteredExe, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        if ((changed || force) && registered)
        {
            Registration.SetIcon(IconRef(s));
            Registration.SetShortcutIcon(IconRef(s).StartsWith('"') ? Environment.ProcessPath! : IconMaker.IconPath);
        }

        // file icons: one per type (video, audio, photo, download)
        bool filesChanged = changed;
        foreach (var k in new[] { IconKind.Video, IconKind.Audio, IconKind.Image, IconKind.Download })
        {
            filesChanged |= IconMaker.Ensure(FileIconColor(s, k), k);
            if (registered) filesChanged |= Registration.SetTypeIcon(k, IconMaker.PathFor(k));
        }
        if (registered && (filesChanged || force)) Registration.RefreshExplorer();
        IconChanged?.Invoke();
    }

    internal static Color ParseColor(string? hex, string fallback = "#5DCAA5")
    {
        try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    internal static void ApplyAccent(string hex) =>
        Current.Resources["Accent"] = new SolidColorBrush(ParseColor(hex));

    internal static void LogError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Dir);
            File.AppendAllText(IOPath.Combine(SettingsStore.Dir, "error.log"), $"[{DateTime.Now:s}] {ex}\n\n");
        }
        catch { }
    }
}
