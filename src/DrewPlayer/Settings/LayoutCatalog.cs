using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace DrewPlayer;

public sealed record LayoutItem(string Id, string Label, bool AvOnly);

/// <summary>Items that can be placed in the top / bottom bar.</summary>
public static class LayoutCatalog
{
    public static readonly LayoutItem[] All =
    [
        new("more", "More (⋯ menu)", false),
        new("open", "Open file", false),
        new("brightness", "Brightness", false),
        new("settings", "Settings", false),
        new("zen", "Zen mode", false),
        new("minimize", "Minimize", false),
        new("close", "Close", false),
        new("prev", "Previous file", true),
        new("play", "Play / pause", true),
        new("next", "Next file", true),
        new("wave", "Waveform bar", true),
        new("time", "Time", true),
        new("volume", "Volume", true),
        new("autonext", "Auto-advance", true),
        new("fullscreen", "Fullscreen", false),
    ];

    public static LayoutItem? Find(string id) => All.FirstOrDefault(i => i.Id == id);
}
