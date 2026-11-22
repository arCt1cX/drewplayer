using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DrewPlayer;

/// <summary>Intro: a film reel spinning in + sliding wordmark.</summary>
public partial class SplashWindow : Window
{
    public event Action? Finished;
    private readonly int _duration;

    internal SplashWindow(SplashSettings s, string accent)
    {
        InitializeComponent();
        _duration = Math.Clamp(s.DurationMs, 400, 5000);

        var brush = new SolidColorBrush(App.ParseColor(string.IsNullOrWhiteSpace(s.Color) ? accent : s.Color));
        brush.Freeze();
        Ring.Stroke = brush;
        foreach (var e in new Ellipse[] { Hub, D1, D2, D3, D4 }) e.Fill = brush;
        Word.Foreground = brush;

        Loaded += (_, _) => Play();
    }

    private void Play()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        double spin = _duration * 0.62;

        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        ReelRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-200, 0, TimeSpan.FromMilliseconds(spin)) { EasingFunction = ease });
        Reel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(spin * 0.8)));

        var wordStart = TimeSpan.FromMilliseconds(spin * 0.5);
        var wordDur = TimeSpan.FromMilliseconds(spin * 0.6);
        Word.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, wordDur) { BeginTime = wordStart, EasingFunction = ease });
        WordShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-10, 0, wordDur) { BeginTime = wordStart, EasingFunction = ease });

        // when the animation ends the player starts (invisible); the intro stays until the video is ready (Dismiss)
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_duration) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Finished?.Invoke();
            // safety net: fade out anyway
            var safety = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            safety.Tick += (_, _) => { safety.Stop(); Dismiss(); };
            safety.Start();
        };
        timer.Start();
    }

    private bool _dismissing;

    /// <summary>Fades the intro out (over the already visible video) and closes.</summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        var outAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        outAnim.Completed += (_, _) => Close();
        Card.BeginAnimation(OpacityProperty, outAnim);
    }
}
