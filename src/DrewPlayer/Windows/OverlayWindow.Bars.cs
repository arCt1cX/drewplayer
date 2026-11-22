using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DrewPlayer;

public partial class OverlayWindow
{
    public void SetDownloading(bool on) => DownloadBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    public void OnClosedAll()
    {
        _imageMode = false;
        EmptyState.Visibility = Visibility.Visible;
        AudioCard.Visibility = Visibility.Collapsed;
        ImageNav.Visibility = Visibility.Collapsed;
        UpdateButtons();
        TitleText.Text = "";
        SetDownloading(false);
        SetTabs();
        ShowControls();
    }

    /// <summary>Redraws the tab strip (visible only with 2+ open files).</summary>
    public void SetTabs()
    {
        var tabs = _main.Tabs;
        TabStrip.Children.Clear();
        bool show = tabs.Count > 1;
        TabStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (!show) return;

        double avail = Math.Max(200, TopBar.ActualWidth - 300);
        double maxTitle = Math.Clamp(avail / tabs.Count - 44, 50, 190);
        var accent = (Brush)FindResource("Accent");

        for (int i = 0; i < tabs.Count; i++)
        {
            int idx = i;
            bool active = i == _main.ActiveIndex;
            var title = new TextBlock
            {
                Text = tabs[i].Title, MaxWidth = maxTitle, TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
            };
            var close = new Button { Style = (Style)FindResource("IconButton"), Width = 22, Height = 22, Margin = new Thickness(6, 0, -4, 0), Content = Icons.Make(Icons.Close, 12, stroke: 2.4), ToolTip = "Close tab" };
            close.Click += (_, _) => _main.CloseTab(idx);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (tabs[i].Growing) row.Children.Add(new TextBlock { Text = "⬇ ", Foreground = accent, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(title);
            row.Children.Add(close);

            var underline = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = accent, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, -6), Visibility = active ? Visibility.Visible : Visibility.Hidden };
            var inner = new Grid();
            inner.Children.Add(row);
            inner.Children.Add(underline);

            var normal = new SolidColorBrush(Color.FromArgb((byte)(active ? 0x40 : 0x1A), 0xFF, 0xFF, 0xFF));
            var hover = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            var tab = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 10, 6), Margin = new Thickness(0, 0, 4, 0),
                Background = normal, Cursor = Cursors.Hand, Child = inner, ToolTip = tabs[i].Path,
            };
            tab.MouseEnter += (_, _) => { if (!active) tab.Background = hover; };
            tab.MouseLeave += (_, _) => tab.Background = normal;
            tab.MouseLeftButtonDown += (_, e) => { e.Handled = true; _main.SwitchTab(idx); };
            tab.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; _main.CloseTab(idx); } };
            TabStrip.Children.Add(tab);
        }

        var plus = new Button { Style = (Style)FindResource("IconButton"), Width = 30, Height = 30, Content = Icons.Make(Icons.Plus, 16), ToolTip = "Open in new tab (Ctrl+T)" };
        plus.Click += (_, _) => _main.RunAction("new_tab");
        TabStrip.Children.Add(plus);
    }

    private bool _imageMode;

    public void OnOpening(string name, bool isImage)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        TitleText.Text = name;
        _imageMode = isImage;
        UpdateButtons();
        ImageNav.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (isImage) { AudioCard.Visibility = Visibility.Collapsed; SpeedText.Visibility = Visibility.Collapsed; }
        SetShape(name);
        BuildWave();
        Activity();
    }

    /// <summary>Shows/hides buttons according to the settings (and the file type).</summary>
    private void UpdateButtons()
    {
        ApplyLayout();
        bool av = !_imageMode;
        // on photos: no play, time, volume, auto-advance, waveform
        foreach (var (id, el) in LayoutElements())
            if (LayoutCatalog.Find(id)?.AvOnly == true) el.Visibility = av ? Visibility.Visible : Visibility.Collapsed;
        Wave.Visibility = av ? Visibility.Visible : Visibility.Collapsed;
        UpdateAutoNextButton();
        if (!S.Layout.Top.Contains("brightness") && !S.Layout.Bottom.Contains("brightness"))
            BrightPanel.Visibility = Visibility.Collapsed;
    }

    private IEnumerable<(string id, FrameworkElement el)> LayoutElements() =>
    [
        ("open", BtnOpen), ("brightness", BtnBright), ("settings", BtnSettings), ("zen", BtnZen),
        ("minimize", BtnMin), ("close", BtnClose), ("prev", BtnPrevFile), ("play", BtnPlay), ("next", BtnNextFile), ("time", TimeText),
        ("volume", VolGroup), ("autonext", BtnAutoNext), ("fullscreen", BtnFs),
    ];

    private string _appliedLayout = "";

    /// <summary>Moves the buttons into the top / bottom bar in the chosen order. Unlisted ones stay hidden.</summary>
    private void ApplyLayout()
    {
        var lay = S.Layout;
        var key = string.Join(",", lay.Top) + "|" + string.Join(",", lay.Bottom);
        if (key == _appliedLayout) return;
        _appliedLayout = key;

        var map = LayoutElements().ToDictionary(x => x.id, x => x.el);
        foreach (var el in map.Values.Append(SpeedText))
            (el.Parent as Panel)?.Children.Remove(el);

        foreach (var id in lay.Top)
            if (map.TryGetValue(id, out var el)) TopButtons.Children.Add(el);

        int wave = lay.Bottom.IndexOf("wave");
        if (wave < 0) wave = lay.Bottom.Count;
        for (int i = 0; i < lay.Bottom.Count; i++)
        {
            if (!map.TryGetValue(lay.Bottom[i], out var el)) continue;
            (i < wave ? BottomLeft : BottomRight).Children.Add(el);
        }
        // speed indicator: always right after the waveform
        BottomRight.Children.Insert(0, SpeedText);

        // brightness panel next to its button
        bool bottom = lay.Bottom.Contains("brightness");
        BrightPanel.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        BrightPanel.Margin = bottom ? new Thickness(0, 0, 12, 60) : new Thickness(0, 52, 12, 0);
    }

    /// <summary>On = solid icon + colored dot; off = faded icon.</summary>
    private void UpdateAutoNextButton()
    {
        bool on = S.Playback.AutoNext;
        var icon = Icons.Make(Icons.AutoNext, 19);
        icon.Opacity = on ? 1 : 0.45;
        var g = new Grid();
        g.Children.Add(icon);
        if (on)
            g.Children.Add(new Ellipse
            {
                Width = 4, Height = 4, Fill = (Brush)FindResource("Accent"),
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, -7), IsHitTestVisible = false,
            });
        BtnAutoNext.Content = g;
        BtnAutoNext.ToolTip = on ? "Auto-advance: on (plays the next file when this one ends)" : "Auto-advance: off (click to play the next file when this one ends)";
    }

}
