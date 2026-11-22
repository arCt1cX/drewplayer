using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DrewPlayer;

/// <summary>
/// "Buttons" page: a mini player screen where buttons are dragged
/// between the top bar, the bottom bar and the "Hidden" tray.
/// </summary>
public partial class SettingsWindow
{
    private enum Zone { Top, Bottom, Hidden }

    private StackPanel? _zoneTop, _zoneBottom;
    private WrapPanel? _zoneHidden;
    private Point _dragStart;
    private string? _dragId;

    private void BuildButtons()
    {
        Title1("Buttons");
        Hint("Drag buttons to move them between the top and bottom bar, or to reorder them. " +
             "Drop them on \"Hidden\" to remove them from the screen (double-click does the same). " +
             "Every action is still available from the keyboard.");

        _s.Layout.Normalize();

        // ---- mini screen ----
        var screen = new Grid { Width = 580, Height = 326, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        screen.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(Color.FromRgb(0x24, 0x34, 0x2F), Color.FromRgb(0x0B, 0x10, 0x0E), 70),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
        });
        // fake "video": hills + sun
        var scene = new Canvas { IsHitTestVisible = false, Opacity = 0.35 };
        scene.Children.Add(new Ellipse { Width = 46, Height = 46, Fill = new SolidColorBrush(Color.FromRgb(0xCF, 0xE9, 0xD9)) });
        Canvas.SetLeft(scene.Children[0], 410); Canvas.SetTop(scene.Children[0], 85);
        scene.Children.Add(new Polygon
        {
            Points = new PointCollection { new(0, 326), new(0, 235), new(130, 180), new(260, 235), new(400, 160), new(580, 225), new(580, 326) },
            Fill = new SolidColorBrush(Color.FromRgb(0x11, 0x24, 0x1F)),
        });
        screen.Children.Add(scene);

        // top bar
        var top = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = 54 };
        top.Background = new LinearGradientBrush(Color.FromArgb(0xA6, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90);
        top.Children.Add(new TextBlock { Text = "my-video.mp4", Foreground = DimBrush, FontSize = 12, Margin = new Thickness(14, 16, 0, 0), IsHitTestVisible = false });
        _zoneTop = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), MinWidth = 120, MinHeight = 38, Background = Brushes.Transparent };
        top.Children.Add(_zoneTop);
        screen.Children.Add(top);

        // bottom bar
        var bottom = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Height = 70 };
        bottom.Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xB3, 0, 0, 0), 90);
        _zoneBottom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 10, 10), MinHeight = 38, Background = Brushes.Transparent };
        bottom.Children.Add(_zoneBottom);
        screen.Children.Add(bottom);

        Page.Children.Add(screen);

        // ---- hidden tray ----
        Page.Children.Add(new TextBlock
        {
            Text = "Hidden", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 20, 0, 8),
            Foreground = (Brush)FindResource("Accent"),
        });
        _zoneHidden = new WrapPanel { MinHeight = 46, Background = Brushes.Transparent };
        var tray = new Border
        {
            Background = (Brush)Resources["Card"], CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8),
            Width = 580, HorizontalAlignment = HorizontalAlignment.Left, Child = _zoneHidden,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
        };
        Page.Children.Add(tray);

        var reset = FieldButton("Reset layout", () => { _s.Layout = new LayoutSettings(); Commit(); Build(); });
        reset.Margin = new Thickness(0, 16, 0, 0);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        Page.Children.Add(reset);

        foreach (var (zone, panel) in new (Zone, Panel)[] { (Zone.Top, _zoneTop), (Zone.Bottom, _zoneBottom), (Zone.Hidden, _zoneHidden) })
        {
            panel.AllowDrop = true;
            var z = zone;
            var host = z == Zone.Hidden ? (FrameworkElement)tray : panel;
            panel.DragOver += (_, e) =>
            {
                bool ok = e.Data.GetData(typeof(string)) is string id && (id != "wave" || z == Zone.Bottom);
                e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            panel.DragEnter += (_, _) => Highlight(host, true);
            panel.DragLeave += (_, _) => Highlight(host, false);
            panel.Drop += (_, e) =>
            {
                Highlight(host, false);
                if (e.Data.GetData(typeof(string)) is not string id) return;
                if (id == "wave" && z != Zone.Bottom) return;
                Move(id, z, InsertIndex(panel, e.GetPosition(panel), id));
            };
        }
        FillZones();
    }

    /// <summary>Highlights the drop zone under the dragged button.</summary>
    private void Highlight(FrameworkElement el, bool on)
    {
        if (el is Border b) b.Background = on ? new SolidColorBrush(Color.FromRgb(0x24, 0x2E, 0x2A)) : (Brush)Resources["Card"];
        else if (el is Panel p) p.Background = on ? new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
    }

    private void FillZones()
    {
        _zoneTop!.Children.Clear();
        _zoneBottom!.Children.Clear();
        _zoneHidden!.Children.Clear();
        foreach (var id in _s.Layout.Top) _zoneTop.Children.Add(Chip(id));
        foreach (var id in _s.Layout.Bottom) _zoneBottom.Children.Add(Chip(id));
        var hidden = LayoutCatalog.All.Select(i => i.Id).Where(i => !_s.Layout.Top.Contains(i) && !_s.Layout.Bottom.Contains(i)).ToList();
        foreach (var id in hidden) _zoneHidden.Children.Add(Chip(id));
        if (hidden.Count == 0)
            _zoneHidden.Children.Add(new TextBlock { Text = "Drop here the buttons you don't want to see", Foreground = DimBrush, FontSize = 12, Margin = new Thickness(4, 12, 0, 0), IsHitTestVisible = false });
    }

    /// <summary>Insertion index from the mouse position (ignores the dragged button).</summary>
    private static int InsertIndex(Panel panel, Point p, string draggedId)
    {
        int idx = 0;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not string cid) continue;
            if (cid == draggedId) continue;
            var pos = child.TranslatePoint(new Point(0, 0), panel);
            bool sameRow = panel is not WrapPanel || (p.Y >= pos.Y && p.Y <= pos.Y + child.ActualHeight);
            if (sameRow && p.X < pos.X + child.ActualWidth / 2) return idx;
            if (panel is WrapPanel && p.Y < pos.Y) return idx;
            idx++;
        }
        return idx;
    }

    private void Move(string id, Zone to, int index)
    {
        var lay = _s.Layout;
        lay.Top.Remove(id);
        lay.Bottom.Remove(id);
        if (to == Zone.Top) lay.Top.Insert(Math.Clamp(index, 0, lay.Top.Count), id);
        else if (to == Zone.Bottom) lay.Bottom.Insert(Math.Clamp(index, 0, lay.Bottom.Count), id);
        lay.Normalize();
        Commit();
        FillZones();
    }

    /// <summary>Draggable button, drawn like in the player.</summary>
    private FrameworkElement Chip(string id)
    {
        var item = LayoutCatalog.Find(id)!;
        UIElement content = id switch
        {
            "time" => new TextBlock { Text = "1:23 / 4:56", FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 11.5, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
            "volume" => VolumeChip(),
            "wave" => WaveChip(),
            "more" => Icons.Make(Icons.More, 18, stroke: 3),
            "open" => Icons.Make(Icons.Folder, 17),
            "brightness" => Icons.Make(Icons.Sun, 17),
            "settings" => Icons.Make(Icons.Settings, 17),
            "zen" => Icons.Make(Icons.Zen, 17),
            "minimize" => Icons.Make(Icons.Minimize, 17),
            "close" => Icons.Make(Icons.Close, 17),
            "play" => Icons.Make(Icons.Play, 18, filled: true),
            "autonext" => Icons.Make(Icons.AutoNext, 17),
            "prev" => Icons.Make(Icons.SkipPrev, 16, filled: true),
            "next" => Icons.Make(Icons.SkipNext, 16, filled: true),
            "fullscreen" => Icons.Make(Icons.Fullscreen, 17),
            _ => new TextBlock { Text = id },
        };
        var chip = new Border
        {
            Tag = id, Child = content, ToolTip = item.Label, Cursor = Cursors.SizeAll,
            Height = 34, MinWidth = 34, Padding = new Thickness(id is "time" or "volume" or "wave" ? 8 : 0, 0, id is "time" or "volume" or "wave" ? 8 : 0, 0),
            Margin = new Thickness(2), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
        };
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        if (id == "wave") chip.Width = 170;

        chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        chip.MouseLeave += (_, _) => chip.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        chip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(this);
            _dragId = id;
            if (e.ClickCount == 2 && id != "wave")
            {
                // double-click: hide / put back in the bottom bar
                bool hidden = !_s.Layout.Top.Contains(id) && !_s.Layout.Bottom.Contains(id);
                Move(id, hidden ? Zone.Bottom : Zone.Hidden, int.MaxValue);
                e.Handled = true;
            }
        };
        chip.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragId != id) return;
            var d = e.GetPosition(this) - _dragStart;
            if (Math.Abs(d.X) < 4 && Math.Abs(d.Y) < 4) return;
            _dragId = null;
            chip.Opacity = 0.4;
            DragDrop.DoDragDrop(chip, new DataObject(typeof(string), id), DragDropEffects.Move);
            chip.Opacity = 1;
        };
        return chip;
    }

    private static UIElement VolumeChip()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Icons.Make(Icons.Volume, 17));
        var bar = new Grid { Width = 46, Height = 4, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Width = 28, HorizontalAlignment = HorizontalAlignment.Left, Background = (Brush)Application.Current.Resources["Accent"] });
        sp.Children.Add(bar);
        return sp;
    }

    private static UIElement WaveChip()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        var accent = (Brush)Application.Current.Resources["Accent"];
        var dim = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        for (int i = 0; i < 30; i++)
        {
            double h = 4 + 14 * Math.Abs(Math.Sin(i * 0.55) * 0.7 + Math.Sin(i * 1.7) * 0.3);
            sp.Children.Add(new Rectangle
            {
                Width = 2.6, Height = Math.Min(20, h), Margin = new Thickness(1.1, 0, 1.1, 0), RadiusX = 1.3, RadiusY = 1.3,
                VerticalAlignment = VerticalAlignment.Bottom, Fill = i < 14 ? accent : dim,
            });
        }
        return sp;
    }
}
