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

/// <summary>
/// Transparent window on top of the video: the whole UI and all input.
/// Needed because WPF cannot draw on top of a native Win32 window (mpv's).
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly MainWindow _main;
    private readonly SettingsStore _store;
    private Settings S => _store.Current;

    private readonly Dictionary<(Key, ModifierKeys), string> _keys = new();
    private readonly DispatcherTimer _ui, _hide, _osdTimer;

    private bool _zen, _controlsVisible = true, _allowClose;
    private Point _lastMouse = new(-1, -1);
    private long _lastDown;
    private Point _downPt;
    private int _edge;

    // waveform
    private Rectangle[] _bars = [];
    private double[] _shape = [];
    private int _playedIdx = -1, _hoverIdx = -1;
    private bool _waveDrag, _volDrag;
    private double _dragFrac;
    private long _lastDragSeek;
    private Brush _accent = Brushes.White, _dim = Brushes.Gray, _hoverBrush = Brushes.LightGray;

    // cached state to avoid useless redraws
    private bool? _shownPaused, _shownMuted;
    private bool _audioCard;

    private Rectangle[] _eq = [];

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void RestartHideTimer()
    {
        _hide.Stop();
        _hide.Start();
    }

    internal OverlayWindow(MainWindow main, SettingsStore store)
    {
        InitializeComponent();
        _main = main;
        _store = store;

        BtnOpen.Content = Icons.Make(Icons.Folder, 19);
        BtnSettings.Content = Icons.Make(Icons.Settings, 19);
        BtnZen.Content = Icons.Make(Icons.Zen, 19);
        BtnMin.Content = Icons.Make(Icons.Minimize, 19);
        BtnClose.Content = Icons.Make(Icons.Close, 19);
        BtnFs.Content = Icons.Make(Icons.Fullscreen, 20);
        BtnPrev.Content = Icons.Make(Icons.Prev, 20);
        BtnNext.Content = Icons.Make(Icons.Next, 20);
        BtnPrev.Click += (_, _) => _main.RunAction("prev_file");
        BtnNext.Click += (_, _) => _main.RunAction("next_file");
        EmptyIcon.Source = App.AppIcon;
        App.IconChanged += OnIconChanged;
        SetPlayIcon(true);
        SetVolIcon(false);

        BtnOpen.Click += (_, _) => _main.RunAction("open");
        BtnAutoNext.Click += (_, _) => _main.ToggleAutoNext();
        BtnPrevFile.Content = Icons.Make(Icons.SkipPrev, 18, filled: true);
        BtnNextFile.Content = Icons.Make(Icons.SkipNext, 18, filled: true);
        BtnPrevFile.Click += (_, _) => _main.RunAction("prev_file");
        BtnNextFile.Click += (_, _) => _main.RunAction("next_file");
        BtnBright.Content = Icons.Make(Icons.Sun, 19);
        BtnBright.Click += (_, _) =>
        {
            BrightPanel.Visibility = BrightPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            OnBrightnessChanged();
        };
        BtnBrightReset.Click += (_, _) => _main.SetBrightness(0);
        BrightBar.MouseLeftButtonDown += (_, e) => { _brightDrag = true; BrightBar.CaptureMouse(); SetBrightFromMouse(e); e.Handled = true; };
        BrightBar.MouseMove += (_, e) => { if (_brightDrag) SetBrightFromMouse(e); };
        BrightBar.MouseLeftButtonUp += (_, e) => { _brightDrag = false; BrightBar.ReleaseMouseCapture(); e.Handled = true; };
        BrightPanel.MouseLeftButtonDown += (_, e) => e.Handled = true; // clicks inside the panel: no drag/pause
        BrightPanel.MouseWheel += (_, e) => { _main.SetBrightness(_main.Brightness + (e.Delta > 0 ? 5 : -5), osd: false); e.Handled = true; };
        BtnSettings.Click += (_, _) => _main.RunAction("settings");
        BtnZen.Click += (_, _) => ToggleZen();
        BtnMin.Click += (_, _) => _main.RunAction("minimize");
        BtnClose.Click += (_, _) => _main.Close();
        BtnFs.Click += (_, _) => _main.ToggleFullscreen();
        BtnPlay.Click += (_, _) => _main.RunAction("pause");
        BtnVol.Click += (_, _) => _main.RunAction("mute");

        PreviewKeyDown += OnKeyDown;
        Root.MouseMove += OnRootMouseMove;
        Root.MouseLeftButtonDown += OnRootLeftDown;
        Root.MouseLeftButtonUp += OnRootLeftUp;
        Root.MouseDown += OnRootMouseDown;
        Root.MouseWheel += (_, e) => { _main.RunAction(e.Delta > 0 ? S.Mouse.WheelUp : S.Mouse.WheelDown); e.Handled = true; };
        Root.MouseLeave += (_, _) => RestartHideTimer();
        Root.SizeChanged += (_, e) => { UpdateClip(); if (e.WidthChanged && _main.Tabs.Count > 1) SetTabs(); };
        Drop += OnDrop;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };

        Wave.SizeChanged += (_, _) => BuildWave();
        Wave.MouseMove += OnWaveMove;
        Wave.MouseLeftButtonDown += OnWaveDown;
        Wave.MouseLeftButtonUp += OnWaveUp;
        Wave.MouseLeave += (_, _) => { if (!_waveDrag) { _hoverIdx = -1; HoverTip.Visibility = Visibility.Collapsed; RecolorBars(); } };

        VolBar.MouseLeftButtonDown += (_, e) => { _volDrag = true; VolBar.CaptureMouse(); SetVolFromMouse(e); e.Handled = true; };
        VolBar.MouseMove += (_, e) => { if (_volDrag) SetVolFromMouse(e); };
        VolBar.MouseLeftButtonUp += (_, e) => { _volDrag = false; VolBar.ReleaseMouseCapture(); e.Handled = true; };

        _ui = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => UpdateUi(), Dispatcher);
        _hide = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(2000) };
        _hide.Tick += OnHideTick;
        _osdTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            OsdBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
        };

        BuildEq();
        InitPhotoEditor();
        InitMoreMenu();
        ApplySettings();
        SetShape("drewplayer");
        UpdateHint();

        Closing += OnClosing;
        SourceInitialized += (_, _) => Native.DisableTransitions(Handle);
        Loaded += (_, _) =>
        {
            if (S.Ui.StartInZen) SetZen(true, announce: false);
            _ui.Start();
        };
    }


    public void ApplySettings()
    {
        _keys.Clear();
        var bad = new List<string>();
        foreach (var (k, action) in S.Keys)
        {
            if (KeyChord.TryParse(k, out var key, out var mods)) _keys[(key, mods)] = action;
            else bad.Add(k);
        }
        if (bad.Count > 0) Osd("Unrecognized keys: " + string.Join(", ", bad), 4000, force: true);

        _hide.Interval = TimeSpan.FromMilliseconds(Math.Clamp(S.Ui.ControlsHideDelayMs, 300, 60000));

        var c = App.ParseColor(S.Ui.AccentColor);
        _accent = Freeze(new SolidColorBrush(c));
        _dim = Freeze(new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)));
        _hoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));
        for (int i = 0; i < _eq.Length; i++)
            _eq[i].Fill = i % 2 == 0 ? _accent : Freeze(new SolidColorBrush(Color.FromArgb(0x99, c.R, c.G, c.B)));

        if (_controlsVisible && !_zen) { TopBar.Opacity = BarOpacity; BottomBar.Opacity = BarOpacity; }
        BuildWave();
        UpdateClip();
        UpdateHint();
        UpdateButtons();
    }

    private double BarOpacity => Math.Clamp(S.Ui.ControlsOpacity, 0.1, 1);

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    private void UpdateHint()
    {
        var k = KeyFor("open");
        EmptyHint.Text = k != null ? $"or press {k} to open a file" : "or use the button in the top right corner";
    }

    private string? KeyFor(string action) =>
        S.Keys.FirstOrDefault(kv => string.Equals(kv.Value.Trim(), action, StringComparison.OrdinalIgnoreCase)).Key;


    private void OnIconChanged() => EmptyIcon.Source = App.AppIcon;


    private void BuildEq()
    {
        _eq = new Rectangle[5];
        for (int i = 0; i < _eq.Length; i++)
        {
            var r = new Rectangle
            {
                Width = 9, Height = 56, RadiusX = 4.5, RadiusY = 4.5, Margin = new Thickness(4, 0, 4, 0),
                RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, 0.25),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            _eq[i] = r;
            Eq.Children.Add(r);
        }
    }

    private void UpdateEq()
    {
        bool run = _audioCard && !_main.Paused;
        for (int i = 0; i < _eq.Length; i++)
        {
            var st = (ScaleTransform)_eq[i].RenderTransform;
            if (run)
            {
                var a = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(420 + i * 90))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 110),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(250)));
            }
        }
    }


    private void UpdateClip()
    {
        bool round = !_main.IsFullscreen && S.Window.RoundedCorners;
        Root.Clip = round && Root.ActualWidth > 0
            ? new RectangleGeometry(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight), 8, 8)
            : null;
    }

    public void ForceClose()
    {
        _allowClose = true;
        App.IconChanged -= OnIconChanged;
        _ui.Stop();
        _hide.Stop();
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        _main.Close();
    }
}
