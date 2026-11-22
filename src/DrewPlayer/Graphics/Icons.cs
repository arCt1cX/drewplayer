using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace DrewPlayer;

/// <summary>Stroke icons (24x24 grid, rounded outline style).</summary>
internal static class Icons
{
    public const string Play = "M8,5.5 L18.5,12 L8,18.5 Z";
    public const string Pause = "M8,5 V19 M16,5 V19";
    public const string Volume = "M4,9.5 H7.5 L12,5.5 V18.5 L7.5,14.5 H4 Z M15.5,9 A4,4 0 0 1 15.5,15 M18,6.5 A7.5,7.5 0 0 1 18,17.5";
    public const string Mute = "M4,9.5 H7.5 L12,5.5 V18.5 L7.5,14.5 H4 Z M16,9.5 L21,14.5 M21,9.5 L16,14.5";
    public const string Fullscreen = "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15";
    public const string ExitFullscreen = "M9,4 V9 H4 M20,9 H15 V4 M15,20 V15 H20 M4,15 H9 V20";
    public const string Close = "M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5";
    public const string Minimize = "M6,12 H18";
    public const string Settings = "M4,7 H11 M15,7 H20 M13,5 V9 M4,17 H7 M11,17 H20 M9,15 V19";
    public const string Folder = "M4,6.5 H9.5 L11.5,8.5 H20 V18 H4 Z";
    public const string Sun = "M8.5,12 A3.5,3.5 0 1 0 15.5,12 A3.5,3.5 0 1 0 8.5,12 Z M12,3 V5 M12,19 V21 M3,12 H5 M19,12 H21 M5.6,5.6 L7,7 M17,17 L18.4,18.4 M5.6,18.4 L7,17 M17,7 L18.4,5.6";
    /// <summary>Next / previous file: play + bar.</summary>
    public const string SkipNext = "M6,6 L14.5,12 L6,18 Z M18,6 V18";
    public const string SkipPrev = "M18,6 L9.5,12 L18,18 Z M6,6 V18";
    /// <summary>"Auto-advance at end of file": list + play.</summary>
    public const string AutoNext = "M4,6 H15 M4,11 H15 M4,16 H9 M13.5,14 L19.5,17.5 L13.5,21 Z";
    public const string Plus = "M12,5 V19 M5,12 H19";
    public const string Prev = "M15,5 L8,12 L15,19";
    public const string Next = "M9,5 L16,12 L9,19";
    public const string Zen = "M3,12 C6,7 9,5.5 12,5.5 C15,5.5 18,7 21,12 C18,17 15,18.5 12,18.5 C9,18.5 6,17 3,12 Z M4,4 L20,20";

    public static Viewbox Make(string data, double size = 20, bool filled = false, double stroke = 2)
    {
        var path = new Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = Brushes.White,
            StrokeThickness = stroke,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = filled ? Brushes.White : null,
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
