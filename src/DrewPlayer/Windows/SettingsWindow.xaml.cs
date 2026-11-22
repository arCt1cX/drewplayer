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

/// <summary>Settings window: everything is editable without touching the JSON.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private Settings _s;
    private readonly DispatcherTimer _commit;
    private string _page = "general";

    private static readonly (string id, string label)[] Pages =
    [
        ("general", "General"), ("look", "Appearance"), ("buttons", "Buttons"), ("keys", "Keys"), ("mouse", "Mouse"),
        ("playback", "Audio & video"), ("photos", "Photos"), ("system", "System"),
    ];

    private static readonly string[] Palette =
        ["#5DCAA5", "#1D9E75", "#378ADD", "#7F77DD", "#D4537E", "#D85A30", "#EF9F27", "#E24B4A", "#97C459", "#E8E6E0"];

    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    internal SettingsWindow(SettingsStore store)
    {
        InitializeComponent();
        _store = store;
        _s = store.Current.Clone();
        _commit = new DispatcherTimer(TimeSpan.FromMilliseconds(180), DispatcherPriority.Normal, (_, _) =>
        {
            _commit!.Stop();
            _saving = true; _store.Save(_s); _saving = false;
        }, Dispatcher);
        _commit.Stop();

        if (App.AppIcon != null) { Icon = App.AppIcon; LogoIcon.Source = App.AppIcon; }
        App.IconChanged += OnIconChanged;
        _store.Reloaded += OnExternalReload;
        Closed += (_, _) =>
        {
            if (_commit.IsEnabled) { _commit.Stop(); _saving = true; _store.Save(_s); _saving = false; }
            App.IconChanged -= OnIconChanged;
            _store.Reloaded -= OnExternalReload;
        };
        SourceInitialized += (_, _) => Native.SetDarkMode(new WindowInteropHelper(this).Handle);
        PreviewKeyDown += OnPreviewKeyDown;

        foreach (var (id, label) in Pages)
        {
            var rb = new RadioButton { Content = label, Style = (Style)Resources["Nav"], GroupName = "nav", IsChecked = id == _page };
            rb.Checked += (_, _) => { _page = id; Build(); };
            NavPanel.Children.Add(rb);
        }
        Build();
    }

#if DEBUG
    /// <summary>Test only: saves a PNG of every page.</summary>
    internal async Task DebugDump(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var (id, label) in Pages)
        {
            // select the nav item too, so the screenshot shows the right page highlighted
            var rb = NavPanel.Children.OfType<RadioButton>().First(r => (string)r.Content == label);
            if (rb.IsChecked == true) Build(); else rb.IsChecked = true;
            await Task.Delay(300);
            var root = (FrameworkElement)Content;
            root.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(Scroll.ExtentHeight * dpi.DpiScaleY + 40),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            // full page: renders the scrollable content at full height
            var page = Page;
            page.Measure(new Size(Scroll.ActualWidth, double.PositiveInfinity));
            var host = new DrawingVisual();
            using (var dc = host.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, root.ActualWidth, Scroll.ExtentHeight + 40));
                dc.DrawRectangle(new VisualBrush(NavPanel.Parent as Visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(0, 0, 210, root.ActualHeight));
                dc.DrawRectangle(new VisualBrush(page) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect(210, 0, page.ActualWidth + 60, page.ActualHeight + 56));
            }
            rtb.Render(host);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = File.Create(System.IO.Path.Combine(dir, $"settings-{id}.png"));
            enc.Save(fs);
        }
    }
#endif

    private void OnIconChanged()
    {
        if (App.AppIcon == null) return;
        Icon = App.AppIcon;
        LogoIcon.Source = App.AppIcon;
        if (_iconPreview != null) _iconPreview.Source = App.AppIcon;
    }

    private bool _saving;

    private void OnExternalReload(bool fromUi)
    {
        if (_saving) return; // our own change (others, e.g. from the player's button, must be reloaded)
        _s = _store.Current.Clone();
        Build();
    }

    private void Commit() { _commit.Stop(); _commit.Start(); }

}
