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
    private bool _brightDrag;

    private void SetBrightFromMouse(MouseEventArgs e)
    {
        double f = Math.Clamp(e.GetPosition(BrightBar).X / BrightBar.ActualWidth, 0, 1);
        double v = f * 200 - 100;
        if (Math.Abs(v) < 4) v = 0; // snap to zero
        _main.SetBrightness(v, osd: false);
    }

    public void OnBrightnessChanged()
    {
        double v = _main.Brightness;
        BrightValue.Text = v == 0 ? "0" : (v > 0 ? "+" : "") + v.ToString("0");
        double half = BrightBar.Width / 2;
        double x = half + v / 100 * half;
        Canvas.SetLeft(BrightKnob, x - BrightKnob.Width / 2);
        Canvas.SetLeft(BrightFill, Math.Min(x, half));
        BrightFill.Width = Math.Abs(x - half);
    }

    /// <summary>Play/pause icon that pops up and fades out in the center.</summary>
    public void FlashPlayPause(bool nowPaused)
    {
        CenterFlash.Child = nowPaused ? Icons.Make(Icons.Pause, 34, stroke: 3) : Icons.Make(Icons.Play, 34, filled: true);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(550);
        CenterFlash.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, d) { EasingFunction = ease });
        FlashScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1.25, d) { EasingFunction = ease });
        FlashScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1.25, d) { EasingFunction = ease });
    }

    public void SetTitle(string title)
    {
        TitleText.Text = title;
        AudioTitle.Text = title;
    }

    public void OnFileLoaded(bool audioOnly, bool hasArt, string? artist)
    {
        _audioCard = audioOnly && !hasArt;
        AudioCard.Visibility = _audioCard ? Visibility.Visible : Visibility.Collapsed;
        AudioArtist.Text = artist ?? "";
        AudioArtist.Visibility = string.IsNullOrWhiteSpace(artist) ? Visibility.Collapsed : Visibility.Visible;
        UpdateEq();
        Activity();
    }

    public void OnPauseChanged()
    {
        UpdateEq();
        if (_main.Paused && !_zen) ShowControls();
        else Activity();
    }

    public void OnFullscreenChanged(bool fs)
    {
        BtnFs.Content = Icons.Make(fs ? Icons.ExitFullscreen : Icons.Fullscreen, 20);
        UpdateClip();
        Activity();
    }

}
