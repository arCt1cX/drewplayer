using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace DrewPlayer;

internal enum IconKind { App, Video, Audio, Image, Download }

/// <summary>
/// Draws the icons in any color and saves them as .ico.
/// App = bars + play; for files: Video = play, Audio = note, Photo = mountain, Download = arrow.
/// </summary>
internal static class IconMaker
{
    public static readonly string IconPath = IOPath.Combine(SettingsStore.Dir, "icon.ico");

    public static string PathFor(IconKind k) => k == IconKind.App
        ? IconPath
        : IOPath.Combine(SettingsStore.Dir, $"icon-{k.ToString().ToLowerInvariant()}.ico");

    private static string StampFor(IconKind k) => PathFor(k)[..^4] + ".color";

    public static Color Darken(Color c, double f) =>
        Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

    public static Color Lighten(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    public static BitmapSource Render(Color c, int size, IconKind kind = IconKind.App)
    {
        double k = size / 36.0;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = new LinearGradientBrush(c, Darken(c, 0.6), new Point(0, 0), new Point(1, 1));
            dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, size, size), 9 * k, 9 * k);
            Glyph(dc, kind, c, k, size);
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static void Glyph(DrawingContext dc, IconKind kind, Color c, double k, int size)
    {
        var white = Brushes.White;
        // thicker strokes at small sizes so they stay readable at 16px
        double sw = (size <= 24 ? 3.2 : 2.6) * k;
        Pen Stroke(double w) => new(white, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        Point P(double x, double y) => new(x * k, y * k);

        switch (kind)
        {
            case IconKind.App:
            {
                var bar = new SolidColorBrush(Lighten(c, 0.6));
                dc.DrawRoundedRectangle(bar, null, new Rect(8 * k, 16 * k, 3 * k, 10 * k), 1.5 * k, 1.5 * k);
                dc.DrawRoundedRectangle(bar, null, new Rect(13 * k, 11 * k, 3 * k, 15 * k), 1.5 * k, 1.5 * k);
                dc.DrawGeometry(white, Stroke(1.6 * k), Poly(P(19.5, 11.5), P(28.5, 18), P(19.5, 24.5)));
                break;
            }
            case IconKind.Video:
                // large play symbol in the center
                dc.DrawGeometry(white, Stroke(2 * k), Poly(P(13.5, 9.5), P(27, 18), P(13.5, 26.5)));
                break;

            case IconKind.Audio:
            {
                // musical note (eighth note)
                var head = new EllipseGeometry(P(14.5, 25), 4.6 * k, 3.6 * k, new RotateTransform(-22, 14.5 * k, 25 * k));
                dc.DrawGeometry(white, null, head);
                dc.DrawLine(Stroke(2.6 * k), P(18.6, 24.2), P(18.6, 8.5));
                var flag = new StreamGeometry();
                using (var g = flag.Open())
                {
                    g.BeginFigure(P(18.6, 8.5), false, false);
                    g.BezierTo(P(22, 9.5), P(26.5, 12), P(25, 17.5), true, true);
                }
                dc.DrawGeometry(null, Stroke(2.6 * k), flag);
                break;
            }
            case IconKind.Image:
            {
                // mountains + sun
                dc.DrawGeometry(white, Stroke(1.4 * k), Poly(P(7, 27), P(14.5, 16), P(19.5, 22.5), P(22.5, 19), P(29, 27)));
                dc.DrawEllipse(white, null, P(24, 11.5), 3 * k, 3 * k);
                break;
            }
            case IconKind.Download:
            {
                // down arrow + baseline
                var p = Stroke(sw);
                dc.DrawLine(p, P(18, 8), P(18, 22));
                dc.DrawGeometry(null, p, Poly(P(11.5, 16), P(18, 22.5), P(24.5, 16), close: false));
                dc.DrawLine(p, P(10, 28), P(26, 28));
                break;
            }
        }
    }

    private static Geometry Poly(Point a, Point b, Point c, bool close = true) => Poly([a, b, c], close);

    private static Geometry Poly(Point a, Point b, Point c, Point d, Point e) => Poly([a, b, c, d, e], true);

    private static Geometry Poly(Point[] pts, bool close)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], close, close);
            for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Regenerates the .ico only if the color changed or the file is missing. Returns true if rewritten.</summary>
    public static bool Ensure(Color c, IconKind kind = IconKind.App)
    {
        var path = PathFor(kind);
        var stamp = StampFor(kind);
        var tag = c.ToString() + "/v2";
        try
        {
            if (File.Exists(path) && File.Exists(stamp) && File.ReadAllText(stamp) == tag) return false;
            SaveIco(c, path, kind);
            File.WriteAllText(stamp, tag);
            return true;
        }
        catch { return false; }
    }

    public static void SaveIco(Color c, string path, IconKind kind = IconKind.App)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var pngs = new List<byte[]>();
        foreach (var s in sizes)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Render(c, s, kind)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            pngs.Add(ms.ToArray());
        }
        Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            byte b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write((uint)pngs[i].Length); w.Write((uint)offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }
}
