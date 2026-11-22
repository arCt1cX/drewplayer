using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DrewPlayer;

public partial class SettingsWindow
{
    private void Title1(string text) =>
        Page.Children.Add(new TextBlock { Text = text, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });

    private void Hint(string text) =>
        Page.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = DimBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -6, 0, 16) });

    private static TextBlock Hint2(string text) =>
        new() { Text = text, FontSize = 13, Foreground = DimBrush, TextWrapping = TextWrapping.Wrap };

    private static UIElement Pad(UIElement e) { if (e is FrameworkElement f) f.Margin = new Thickness(0, 8, 0, 8); return e; }

    private static StackPanel HStack(params UIElement[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items)
        {
            if (i is FrameworkElement f) f.Margin = new Thickness(0, 0, 10, 0);
            sp.Children.Add(i);
        }
        return sp;
    }

    private void Card(string title, params UIElement[] rows)
    {
        Page.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 6, 0, 8),
            Foreground = (Brush)FindResource("Accent"),
        });
        var sp = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            sp.Children.Add(rows[i]);
            if (i < rows.Length - 1) sp.Children.Add(new Border { Height = 1, Background = LineBrush });
        }
        Page.Children.Add(CardRaw(sp));
    }

    private Border CardRaw(UIElement content) => new()
    {
        Background = (Brush)Resources["Card"], CornerRadius = new CornerRadius(12),
        Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(0, 0, 0, 18), Child = content,
    };

    private static UIElement Row(string label, string? hint, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        left.Children.Add(new TextBlock { Text = label, FontSize = 14 });
        if (hint != null) left.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = DimBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        g.Children.Add(left);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private CheckBox Toggle(bool value, Action<bool> set)
    {
        var cb = new CheckBox { IsChecked = value, Style = (Style)Resources["Toggle"] };
        cb.Checked += (_, _) => { set(true); Commit(); };
        cb.Unchecked += (_, _) => { set(false); Commit(); };
        return cb;
    }

    private UIElement SliderBox(double min, double max, double step, double value, Action<double> set, Func<double, string> fmt)
    {
        var sl = new Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220,
            TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step,
        };
        var label = new TextBlock { Text = fmt(sl.Value), Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 13 };
        sl.ValueChanged += (_, e) => { label.Text = fmt(e.NewValue); set(e.NewValue); Commit(); };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(sl);
        sp.Children.Add(label);
        return sp;
    }

    private Button FieldButton(string text, Action? onClick)
    {
        var b = new Button { Content = text, Style = (Style)Resources["Field"] };
        if (onClick != null) b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Drop-down with fixed options.</summary>
    private Button Choice(string current, (string id, string label)[] options, Action<string> set)
    {
        var b = FieldButton("", null);
        b.Width = 260;
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var text = new TextBlock { Text = options.FirstOrDefault(o => o.id == current).label ?? options[0].label, TextTrimming = TextTrimming.CharacterEllipsis };
        var dock = new DockPanel();
        var arrow = new TextBlock { Text = "▾", Foreground = DimBrush, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(arrow, Dock.Right);
        dock.Children.Add(arrow);
        dock.Children.Add(text);
        b.Content = dock;

        var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
        foreach (var (id, label) in options)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => { text.Text = label; set(id); Commit(); };
            menu.Items.Add(mi);
        }
        b.Click += (_, _) => { menu.MinWidth = b.ActualWidth; menu.IsOpen = true; };
        return b;
    }

    private Button ActionPicker(string id, Action<string> onPick)
    {
        var b = FieldButton("", null);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var grid = new DockPanel { LastChildFill = true };
        var arrow = new TextBlock { Text = "▾", Foreground = DimBrush, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(arrow, Dock.Right);
        grid.Children.Add(arrow);
        grid.Children.Add(new TextBlock { Text = ActionCatalog.Find(id)?.Label ?? id, TextTrimming = TextTrimming.CharacterEllipsis });
        b.Content = grid;

        var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
        foreach (var a in ActionCatalog.All)
        {
            var mi = new MenuItem { Header = a.Label };
            if (a.Id == id) mi.FontWeight = FontWeights.SemiBold;
            var aid = a.Id;
            mi.Click += (_, _) => onPick(aid);
            menu.Items.Add(mi);
        }
        b.Click += (_, _) => { menu.MinWidth = b.ActualWidth; menu.IsOpen = true; };
        return b;
    }

    private static UIElement ParamBox(double value, string unit, Action<double> set)
    {
        var tb = new TextBox { Text = Num.Inv(value), Width = 70, TextAlignment = TextAlignment.Right };
        tb.TextChanged += (_, _) => { if (Num.TryParse(tb.Text, out var v)) set(v); };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        sp.Children.Add(tb);
        sp.Children.Add(new TextBlock { Text = unit, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = DimBrush, FontSize = 12 });
        return sp;
    }

    private UIElement Swatches(string current, Action<string> set)
    {
        var wrap = new StackPanel { Orientation = Orientation.Horizontal };
        var hex = new TextBox { Text = current.ToUpperInvariant(), Width = 94, Margin = new Thickness(8, 0, 0, 0), FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 13 };
        var rings = new List<(Ellipse ring, string color)>();

        void Select(string c, bool fromBox)
        {
            foreach (var (ring, col) in rings)
                ring.Stroke = string.Equals(col, c, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
            if (!fromBox) hex.Text = c.ToUpperInvariant();
            set(c);
            Commit();
        }

        foreach (var c in Palette)
        {
            var g = new Grid { Width = 24, Height = 24, Margin = new Thickness(0, 0, 1, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = c };
            var ring = new Ellipse { StrokeThickness = 2, Stroke = string.Equals(c, current, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent };
            var dot = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(App.ParseColor(c)) };
            g.Children.Add(ring);
            g.Children.Add(dot);
            var col = c;
            g.MouseLeftButtonUp += (_, _) => Select(col, false);
            rings.Add((ring, c));
            wrap.Children.Add(g);
        }
        hex.TextChanged += (_, _) =>
        {
            var t = hex.Text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if (t.Length is 7 or 9)
            {
                try { ColorConverter.ConvertFromString(t); Select(t, true); } catch { }
            }
        };
        wrap.Children.Add(hex);
        return wrap;
    }
}
