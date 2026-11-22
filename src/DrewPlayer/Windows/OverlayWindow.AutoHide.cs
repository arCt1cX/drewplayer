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
    public void ToggleZen() => SetZen(!_zen, announce: true);

    private void SetZen(bool on, bool announce)
    {
        _zen = on;
        if (on)
        {
            FadeBars(0);
            _controlsVisible = false;
            if (announce)
            {
                var k = KeyFor("zen");
                Osd(k != null ? $"Zen — press {k} to exit" : "Zen", 1500, force: true);
            }
        }
        else
        {
            ShowControls();
            if (announce) Osd("Zen off", 900, force: true);
        }
        _hide.Stop();
        _hide.Start();
    }

    private void Activity()
    {
        if (Root.Cursor == Cursors.None) Root.Cursor = null;
        if (!_zen) ShowControls();
        _hide.Stop();
        _hide.Start();
    }

    private void ShowControls()
    {
        if (_zen) return;
        if (!_controlsVisible)
        {
            _controlsVisible = true;
            FadeBars(BarOpacity);
        }
    }

    private void HideControls()
    {
        if (_controlsVisible)
        {
            _controlsVisible = false;
            FadeBars(0);
        }
        if (_main.CurrentFile != null && IsMouseOver) Root.Cursor = Cursors.None;
    }

    private void FadeBars(double to)
    {
        var d = new Duration(TimeSpan.FromMilliseconds(to > 0 ? 160 : 380));
        TopBar.BeginAnimation(OpacityProperty, new DoubleAnimation(to, d));
        BottomBar.BeginAnimation(OpacityProperty, new DoubleAnimation(to, d));
        TopBar.IsHitTestVisible = BottomBar.IsHitTestVisible = to > 0;
        if (to == 0) { HoverTip.Visibility = Visibility.Collapsed; _hoverIdx = -1; BrightPanel.Visibility = Visibility.Collapsed; }
    }

    private void OnHideTick(object? sender, EventArgs e)
    {
        _hide.Stop();
        if (_main.CurrentFile == null || IsEditing) return;
        if (_waveDrag || _volDrag || _brightDrag) { _hide.Start(); return; }
        if (!_zen)
        {
            if (_main.Paused) return;
            if (IsMouseOver && (TopBar.IsMouseOver || BottomBar.IsMouseOver || BrightPanel.IsMouseOver)) { _hide.Start(); return; }
        }
        HideControls();
    }


    public void Osd(string text, int ms = 1200, bool force = false)
    {
        if (!force && (!S.Ui.OsdMessages || (_zen && !S.Ui.OsdInZen))) return;
        OsdText.Text = text;
        OsdBox.BeginAnimation(OpacityProperty, null);
        OsdBox.Opacity = 1;
        _osdTimer.Stop();
        _osdTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(300, ms));
        _osdTimer.Start();
    }

}
