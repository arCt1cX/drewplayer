using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>Loads, saves and watches %APPDATA%\DrewPlayer\settings.json.</summary>
public sealed class SettingsStore : IDisposable
{
    public static readonly string Dir =
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DrewPlayer");
    public static readonly string FilePath = IOPath.Combine(Dir, "settings.json");

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Settings Current { get; private set; } = new() { Version = 7, Keys = DefaultKeys() };
    public string? LastError { get; private set; }

    /// <summary>On the UI thread. Argument: true if the change comes from the settings window.</summary>
    public event Action<bool>? Reloaded;

    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private long _ignoreUntil;

    public void Load()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(FilePath)) { Write(Current); return; }
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
            if (s.Keys.Count == 0) s.Keys = DefaultKeys();
            bool migrated = false;
            if (s.Version < 2)
            {
                // v1 had left click disabled: it now toggles pause by default
                if (string.Equals(s.Mouse.LeftClick, "none", StringComparison.OrdinalIgnoreCase)) s.Mouse.LeftClick = "pause";
                s.Version = 2;
                migrated = true;
            }
            if (s.Version < 3)
            {
                // v3: new shortcuts (info, tabs) added if the key is free
                var add = new Dictionary<string, string>
                {
                    ["I"] = "info", ["Enter"] = "fullscreen", ["Ctrl+T"] = "new_tab", ["Ctrl+W"] = "close_tab",
                    ["Ctrl+Tab"] = "next_tab", ["Ctrl+Shift+Tab"] = "prev_tab",
                };
                var keys = new Dictionary<string, string>(s.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in add) keys.TryAdd(k, v);
                s.Keys = keys;
                s.Version = 3;
                migrated = true;
            }
            if (s.Version < 4)
            {
                // v4: files start from the beginning by default (configurable)
                s.Playback.Resume = false;
                s.Version = 4;
                migrated = true;
            }
            if (s.Version < 5)
            {
                // v5: do NOT auto-advance at end of file (configurable)
                s.Playback.AutoNext = false;
                s.Version = 5;
                migrated = true;
            }
            if (s.Version < 6)
            {
                // v6: from "visible buttons" toggles to a free layout
                var b = s.Buttons;
                var hidden = new HashSet<string>();
                if (!b.Open) hidden.Add("open"); if (!b.Brightness) hidden.Add("brightness"); if (!b.Settings) hidden.Add("settings");
                if (!b.Zen) hidden.Add("zen"); if (!b.Minimize) hidden.Add("minimize"); if (!b.Close) hidden.Add("close");
                if (!b.Play) hidden.Add("play"); if (!b.Time) hidden.Add("time"); if (!b.Volume) hidden.Add("volume");
                if (!b.AutoNext) hidden.Add("autonext"); if (!b.Fullscreen) hidden.Add("fullscreen");
                s.Layout = new LayoutSettings();
                s.Layout.Top.RemoveAll(hidden.Contains);
                s.Layout.Bottom.RemoveAll(hidden.Contains);
                s.Version = 6;
                migrated = true;
            }
            if (s.Version < 7)
            {
                // v7: previous/next file buttons around play (unless already placed)
                var b = s.Layout.Bottom;
                bool placed(string id) => s.Layout.Top.Contains(id) || b.Contains(id);
                int play = b.IndexOf("play");
                if (!placed("prev")) b.Insert(play >= 0 ? play : 0, "prev");
                play = b.IndexOf("play");
                if (!placed("next")) b.Insert(play >= 0 ? play + 1 : Math.Min(1, b.Count), "next");
                s.Version = 7;
                migrated = true;
            }
            s.Layout ??= new LayoutSettings();
            s.Layout.Normalize();
            Current = s;
            LastError = null;
            if (migrated) Write(s);
        }
        catch (Exception ex)
        {
            LastError = ex is JsonException je ? $"line {je.LineNumber + 1}: {je.Message.Split('.')[0]}" : ex.Message;
        }
    }

    /// <summary>Saves and applies immediately (called by the settings window).</summary>
    public void Save(Settings s)
    {
        s.Layout.Normalize();
        Current = s.Clone();
        LastError = null;
        Write(Current);
        Reloaded?.Invoke(true);
    }

    private void Write(Settings s)
    {
        _ignoreUntil = Environment.TickCount64 + 1200;
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public void Watch(Dispatcher dispatcher)
    {
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            _debounce!.Stop();
            Load();
            Reloaded?.Invoke(false);
        }, dispatcher);
        _debounce.Stop();

        void Kick()
        {
            if (Environment.TickCount64 < _ignoreUntil) return; // our own write
            dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
        }

        _watcher = new FileSystemWatcher(Dir, "settings.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Kick();
        _watcher.Created += (_, _) => Kick();
        _watcher.Renamed += (_, _) => Kick();
    }

    public void Dispose() => _watcher?.Dispose();

    public static Dictionary<string, string> DefaultKeys() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = "pause",
        ["K"] = "pause",
        ["Right"] = "seek 10",
        ["Left"] = "seek -10",
        ["Shift+Right"] = "seek 30",
        ["Shift+Left"] = "seek -30",
        ["Ctrl+Right"] = "seek 120",
        ["Ctrl+Left"] = "seek -120",
        ["Up"] = "volume 5",
        ["Down"] = "volume -5",
        ["M"] = "mute",
        ["F"] = "fullscreen",
        ["Enter"] = "fullscreen",
        ["Escape"] = "exit_fullscreen",
        ["Z"] = "zen",
        ["Period"] = "frame_step",
        ["Comma"] = "frame_back",
        ["Plus"] = "speed 0.25",
        ["Minus"] = "speed -0.25",
        ["Back"] = "speed_reset",
        ["S"] = "screenshot",
        ["I"] = "info",
        ["J"] = "next_sub",
        ["V"] = "sub_toggle",
        ["A"] = "next_audio",
        ["L"] = "loop",
        ["O"] = "open",
        ["Ctrl+O"] = "open",
        ["Ctrl+T"] = "new_tab",
        ["Ctrl+W"] = "close_tab",
        ["Ctrl+Tab"] = "next_tab",
        ["Ctrl+Shift+Tab"] = "prev_tab",
        ["PageDown"] = "next_file",
        ["PageUp"] = "prev_file",
        ["Ctrl+Comma"] = "settings",
        ["Q"] = "quit",
        ["D0"] = "seek_percent 0",
        ["D1"] = "seek_percent 10",
        ["D2"] = "seek_percent 20",
        ["D3"] = "seek_percent 30",
        ["D4"] = "seek_percent 40",
        ["D5"] = "seek_percent 50",
        ["D6"] = "seek_percent 60",
        ["D7"] = "seek_percent 70",
        ["D8"] = "seek_percent 80",
        ["D9"] = "seek_percent 90",
    };
}
