using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace DrewPlayer;

public sealed class Settings
{
    /// <summary>0/1 = old file (field missing); newer files carry the current schema version.</summary>
    public int Version { get; set; }
    public Dictionary<string, string> Keys { get; set; } = new();
    public MouseSettings Mouse { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public PlaybackSettings Playback { get; set; } = new();
    public ImageSettings Images { get; set; } = new();
    public SplashSettings Splash { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public ButtonSettings Buttons { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();

    public Settings Clone() =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, SettingsStore.Json), SettingsStore.Json)!;
}

public sealed class MouseSettings
{
    public string LeftClick { get; set; } = "pause";
    public string DoubleClick { get; set; } = "fullscreen";
    public string RightClick { get; set; } = "pause";
    public string MiddleClick { get; set; } = "zen";
    public string WheelUp { get; set; } = "volume 5";
    public string WheelDown { get; set; } = "volume -5";
    public string XButton1 { get; set; } = "seek -10";
    public string XButton2 { get; set; } = "seek 10";
}

public sealed class UiSettings
{
    public int ControlsHideDelayMs { get; set; } = 2000;
    public bool StartInZen { get; set; } = false;
    /// <summary>true = a single DrewPlayer: files opened later go to new tabs.</summary>
    public bool SingleWindow { get; set; } = true;
    public bool OsdMessages { get; set; } = true;
    public bool OsdInZen { get; set; } = false;
    public string AccentColor { get; set; } = "#5DCAA5";
    /// <summary>Empty = same as AccentColor.</summary>
    public string IconColor { get; set; } = "#1D9E75";
    /// <summary>File icon colors per type (so they are easy to tell apart).</summary>
    public string FileIconVideo { get; set; } = "#1D9E75";
    public string FileIconAudio { get; set; } = "#7F77DD";
    public string FileIconImage { get; set; } = "#D85A30";
    public string FileIconDownload { get; set; } = "#378ADD";
    public double ControlsOpacity { get; set; } = 1.0;
    public int WaveformBars { get; set; } = 140;
}

public sealed class PlaybackSettings
{
    public int Volume { get; set; } = 80;
    public int VolumeMax { get; set; } = 130;
    /// <summary>false = every file starts from the beginning; true = resume where you left off.</summary>
    public bool Resume { get; set; } = false;
    /// <summary>When minimized: "continue" = keep playing, "pause_video" = pause videos only, "pause_all" = pause everything.</summary>
    public string MinimizeAction { get; set; } = "pause_video";
    public bool AutoNext { get; set; } = false;
    /// <summary>Order for previous/next: explorer | name | date_new | date_old | size.</summary>
    public string FolderOrder { get; set; } = "explorer";
    public bool LoopFile { get; set; } = false;
    public bool StartFullscreen { get; set; } = false;
    public string Hwdec { get; set; } = "auto-safe";
    public bool HighQuality { get; set; } = false;
    public bool SubAuto { get; set; } = true;
    public int SubFontSize { get; set; } = 38;
}

public sealed class ImageSettings
{
    /// <summary>true = wheel zooms, false = wheel changes photo.</summary>
    public bool WheelZoom { get; set; } = true;
}

public sealed class SplashSettings
{
    public bool Enabled { get; set; } = true;
    public int DurationMs { get; set; } = 1300;
    /// <summary>Empty = use accentColor.</summary>
    public string Color { get; set; } = "";
}

public sealed class WindowSettings
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Autofit { get; set; } = true;
    public int AutofitPercent { get; set; } = 80;
    /// <summary>false = small videos/photos keep their real size (1:1).</summary>
    public bool UpscaleSmall { get; set; } = true;
    public bool AudioCompact { get; set; } = true;
    public bool RoundedCorners { get; set; } = true;
}

/// <summary>
/// Button layout: what goes in the top bar, what in the bottom bar, and in which order.
/// Anything in neither list is hidden. "wave" (the waveform bar) always stays in the bottom bar.
/// </summary>
public sealed class LayoutSettings
{
    public List<string> Top { get; set; } = ["open", "brightness", "settings", "zen", "minimize", "close"];
    public List<string> Bottom { get; set; } = ["prev", "play", "next", "wave", "time", "volume", "autonext", "fullscreen"];

    public void Normalize()
    {
        var known = LayoutCatalog.All.Select(i => i.Id).ToHashSet();
        Top = Top.Where(known.Contains).Where(i => i != "wave").Distinct().ToList();
        Bottom = Bottom.Where(known.Contains).Where(i => !Top.Contains(i)).Distinct().ToList();
        if (!Bottom.Contains("wave")) Bottom.Insert(Math.Min(1, Bottom.Count), "wave");
    }
}

/// <summary>Legacy system (visibility only): used to migrate to the layout.</summary>
public sealed class ButtonSettings
{
    // top bar
    public bool Open { get; set; } = true;
    public bool Brightness { get; set; } = true;
    public bool Settings { get; set; } = true;
    public bool Zen { get; set; } = true;
    public bool Minimize { get; set; } = true;
    public bool Close { get; set; } = true;
    // bottom bar
    public bool Play { get; set; } = true;
    public bool Time { get; set; } = true;
    public bool Volume { get; set; } = true;
    public bool AutoNext { get; set; } = true;
    public bool Fullscreen { get; set; } = true;
}
