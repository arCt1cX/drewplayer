using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>Every bindable action, with labels for the UI.</summary>
public sealed record ActionInfo(string Id, string Label, string? Unit = null, double DefaultParam = 0);

public static class ActionCatalog
{
    public static readonly ActionInfo[] All =
    [
        new("none", "No action"),
        new("pause", "Play / pause"),
        new("seek", "Seek forward/back", "seconds", 10),
        new("seek_percent", "Jump to percentage", "%", 50),
        new("volume", "Volume up/down", "%", 5),
        new("mute", "Mute"),
        new("fullscreen", "Fullscreen"),
        new("exit_fullscreen", "Exit fullscreen"),
        new("zen", "Zen mode"),
        new("speed", "Speed up/down", "×", 0.25),
        new("speed_reset", "Normal speed"),
        new("frame_step", "Next frame"),
        new("frame_back", "Previous frame"),
        new("next_file", "Next file"),
        new("autonext", "Auto-advance at end of file: on/off"),
        new("prev_file", "Previous file"),
        new("zoom", "Zoom (photos and video)", "steps", 1),
        new("brightness", "Brightness up/down", "points", 10),
        new("brightness_reset", "Reset brightness"),
        new("zoom_reset", "Reset zoom"),
        new("next_sub", "Cycle subtitles"),
        new("sub_toggle", "Show/hide subtitles"),
        new("sub_delay", "Subtitle delay", "seconds", 0.1),
        new("next_audio", "Cycle audio track"),
        new("loop", "Loop file on/off"),
        new("screenshot", "Screenshot"),
        new("menu", "Open the ⋯ menu"),
        new("edit_photo", "Edit photo (rotate, flip, crop)"),
        new("rotate", "Rotate (photo: editor, video: 90°)"),
        new("copy_image", "Copy image / current frame"),
        new("set_wallpaper", "Set photo as desktop background"),
        new("show_in_folder", "Show in folder"),
        new("delete_file", "Delete file (Recycle Bin)"),
        new("aspect", "Cycle aspect ratio"),
        new("ab_loop", "A-B loop"),
        new("audio_delay", "Audio delay", "seconds", 0.1),
        new("sub_load", "Load subtitle file"),
        new("deinterlace", "Deinterlace on/off"),
        new("info", "File info (codec, resolution, GPU)"),
        new("open", "Open file"),
        new("new_tab", "Open in new tab"),
        new("close_tab", "Close tab"),
        new("next_tab", "Next tab"),
        new("prev_tab", "Previous tab"),
        new("settings", "Settings"),
        new("minimize", "Minimize"),
        new("quit", "Quit DrewPlayer"),
    ];

    public static ActionInfo? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    public static (string id, double param) Split(string? action)
    {
        var t = (action ?? "none").Trim();
        int sp = t.IndexOf(' ');
        var id = (sp < 0 ? t : t[..sp]).ToLowerInvariant();
        Num.TryParse(sp < 0 ? "" : t[(sp + 1)..], out var p);
        return (id, p);
    }

    public static string Join(string id, double param) =>
        Find(id)?.Unit != null ? $"{id} {Num.Inv(param)}" : id;

    public static string Describe(string? action)
    {
        var (id, p) = Split(action);
        var a = Find(id);
        if (a == null) return action ?? "";
        if (a.Unit == null) return a.Label;
        return id switch
        {
            "seek" => p >= 0 ? $"Forward {Num.Inv(p)} s" : $"Back {Num.Inv(-p)} s",
            "volume" => p >= 0 ? $"Volume +{Num.Inv(p)}" : $"Volume −{Num.Inv(-p)}",
            "speed" => p >= 0 ? $"Speed +{Num.Inv(p)}" : $"Speed −{Num.Inv(-p)}",
            "zoom" => p >= 0 ? "Zoom in" : "Zoom out",
            "brightness" => p >= 0 ? $"Brightness +{Num.Inv(p)}" : $"Brightness −{Num.Inv(-p)}",
            "seek_percent" => $"Jump to {Num.Inv(p)}%",
            _ => $"{a.Label} {Num.Inv(p)}",
        };
    }
}
