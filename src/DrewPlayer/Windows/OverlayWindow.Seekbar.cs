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
    /// <summary>Decorative waveform, stable per file (same file = same shape).</summary>
    private void SetShape(string seedText)
    {
        uint h = 2166136261;
        foreach (var ch in seedText) { h ^= ch; h *= 16777619; }
        var rnd = new Random((int)h);
        const int n = 400;
        var raw = new double[n];
        double p1 = rnd.NextDouble() * 6, p2 = rnd.NextDouble() * 6, p3 = rnd.NextDouble() * 6;
        double f1 = 0.03 + rnd.NextDouble() * 0.03, f2 = 0.11 + rnd.NextDouble() * 0.08, f3 = 0.31 + rnd.NextDouble() * 0.2;
        for (int i = 0; i < n; i++)
        {
            double v = 0.55 + 0.25 * Math.Sin(i * f1 + p1) + 0.15 * Math.Sin(i * f2 + p2) + 0.1 * Math.Sin(i * f3 + p3)
                       + (rnd.NextDouble() - 0.5) * 0.35;
            double edge = Math.Min(1, Math.Min(i, n - 1 - i) / 14.0);
            raw[i] = Math.Clamp(v, 0.08, 1) * (0.35 + 0.65 * edge);
        }
        _shape = raw;
    }

    private void BuildWave()
    {
        Wave.Children.Clear();
        double w = Wave.ActualWidth, hgt = Wave.ActualHeight;
        if (w < 10 || hgt < 4 || _shape.Length == 0) { _bars = []; return; }

        int n = Math.Clamp(S.Ui.WaveformBars, 20, 400);
        n = Math.Max(10, Math.Min(n, (int)(w / 3)));
        double step = w / n;
        double barW = Math.Max(1.2, step * 0.62);
        _bars = new Rectangle[n];
        for (int i = 0; i < n; i++)
        {
            double v = _shape[(int)((double)i / n * _shape.Length)];
            double bh = Math.Max(3, v * hgt);
            var r = new Rectangle { Width = barW, Height = bh, RadiusX = barW / 2, RadiusY = barW / 2, Fill = _dim, IsHitTestVisible = false };
            Canvas.SetLeft(r, i * step + (step - barW) / 2);
            Canvas.SetTop(r, hgt - bh);
            Wave.Children.Add(r);
            _bars[i] = r;
        }
        _playedIdx = -2;
        RecolorBars();
    }

    private void RecolorBars()
    {
        int n = _bars.Length;
        if (n == 0) return;
        double d = _main.Duration;
        double frac = _waveDrag ? _dragFrac : d > 0 ? _main.TimePos / d : 0;
        int played = (int)Math.Round(Math.Clamp(frac, 0, 1) * n);
        if (played == _playedIdx && !_hoverDirty) return;
        _playedIdx = played;
        _hoverDirty = false;
        for (int i = 0; i < n; i++)
        {
            var b = i < played ? _accent : (_hoverIdx >= 0 && i < _hoverIdx ? _hoverBrush : _dim);
            if (!ReferenceEquals(_bars[i].Fill, b)) _bars[i].Fill = b;
        }
    }
    private bool _hoverDirty;

    private double WaveFrac(MouseEventArgs e) => Math.Clamp(e.GetPosition(Wave).X / Math.Max(1, Wave.ActualWidth), 0, 1);

    private void OnWaveMove(object sender, MouseEventArgs e)
    {
        double f = WaveFrac(e);
        int idx = (int)Math.Round(f * _bars.Length);
        if (idx != _hoverIdx) { _hoverIdx = idx; _hoverDirty = true; }

        double d = _main.Duration;
        if (d > 0)
        {
            HoverTipText.Text = MainWindow.FormatTime(f * d);
            HoverTip.Visibility = Visibility.Visible;
            HoverTip.UpdateLayout();
            var x = Wave.TranslatePoint(new Point(f * Wave.ActualWidth, 0), BottomBar).X - HoverTip.ActualWidth / 2;
            HoverTip.Margin = new Thickness(Math.Clamp(x, 4, Math.Max(4, BottomBar.ActualWidth - HoverTip.ActualWidth - 4)), 0, 0, 58);
        }

        if (_waveDrag)
        {
            _dragFrac = f;
            long now = Environment.TickCount64;
            if (now - _lastDragSeek > 70 && d > 0)
            {
                _lastDragSeek = now;
                _main.SeekAbsolute(f * d, exact: false);
            }
        }
        RecolorBars();
    }

    private void OnWaveDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_main.Duration <= 0) return;
        _waveDrag = true;
        _dragFrac = WaveFrac(e);
        Wave.CaptureMouse();
        _main.SeekAbsolute(_dragFrac * _main.Duration, exact: false);
        RecolorBars();
    }

    private void OnWaveUp(object sender, MouseButtonEventArgs e)
    {
        if (!_waveDrag) return;
        e.Handled = true;
        _dragFrac = WaveFrac(e);
        _main.SeekAbsolute(_dragFrac * _main.Duration, exact: true);
        _waveDrag = false;
        Wave.ReleaseMouseCapture();
        if (!Wave.IsMouseOver) { _hoverIdx = -1; HoverTip.Visibility = Visibility.Collapsed; }
        _hoverDirty = true;
    }


    private double VolMax => Math.Max(100, S.Playback.VolumeMax);

    private void SetVolFromMouse(MouseEventArgs e)
    {
        double f = Math.Clamp(e.GetPosition(VolBar).X / VolBar.ActualWidth, 0, 1);
        _main.SetVolume(f * VolMax);
        Osd($"Volume {f * VolMax:0}%");
    }


    private void UpdateUi()
    {
        if (_imageMode)
        {
            ImageCounter.Text = _main.FolderCount > 0 ? $"{_main.FolderIndex + 1} / {_main.FolderCount}" : "";
            DebugSnapshot();
            return;
        }
        if (_main.CurrentFile != null)
        {
            double d = _main.Duration, t = _main.TimePos;
            TimeText.Text = d > 0 ? $"{MainWindow.FormatTime(t)} / {MainWindow.FormatTime(d)}" : MainWindow.FormatTime(t);
        }
        else TimeText.Text = "";

        if (_shownPaused != _main.Paused) { _shownPaused = _main.Paused; SetPlayIcon(_main.Paused || _main.CurrentFile == null); }
        if (_shownMuted != _main.Muted) { _shownMuted = _main.Muted; SetVolIcon(_main.Muted); }

        double vf = _main.Muted ? 0 : Math.Clamp(_main.Volume / VolMax, 0, 1);
        VolFill.Width = Math.Max(0, vf * VolBar.ActualWidth);

        double sp = _main.Speed;
        bool showSp = Math.Abs(sp - 1) > 0.001;
        SpeedText.Visibility = showSp ? Visibility.Visible : Visibility.Collapsed;
        if (showSp) SpeedText.Text = $"{sp:0.##}×";

        RecolorBars();
        DebugSnapshot();
    }

    private int _snapTick;
    [System.Diagnostics.Conditional("DEBUG")]
    private void DebugSnapshot()
    {
        // Debug builds only: DREWPLAYER_SNAP=path.png -> saves the UI every 2s (for headless tests)
        var path = Environment.GetEnvironmentVariable("DREWPLAYER_SNAP");
        if (string.IsNullOrEmpty(path) || ++_snapTick % 20 != 0 || Root.ActualWidth < 1) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(Root.ActualWidth * dpi.DpiScaleX), (int)(Root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        try { using var fs = File.Create(path); enc.Save(fs); } catch { }
    }

    private void SetPlayIcon(bool showPlay) =>
        BtnPlay.Content = showPlay ? Icons.Make(Icons.Play, 22, filled: true) : Icons.Make(Icons.Pause, 22, stroke: 2.8);

    private void SetVolIcon(bool muted) => BtnVol.Content = Icons.Make(muted ? Icons.Mute : Icons.Volume, 20);

}
