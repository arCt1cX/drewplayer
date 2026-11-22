#if DEBUG
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DrewPlayer;

/// <summary>Debug builds only: helpers for the headless screenshot/benchmark mode.</summary>
public partial class OverlayWindow
{
    internal void DebugShowControls()
    {
        _controlsVisible = false;
        ShowControls();
        TopBar.BeginAnimation(OpacityProperty, null);
        BottomBar.BeginAnimation(OpacityProperty, null);
        TopBar.Opacity = BottomBar.Opacity = 1;
        _hide.Stop();
    }

    /// <summary>Renders the whole UI layer at device pixels (the video itself is not part of it).</summary>
    internal BitmapSource DebugRender()
    {
        Root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var rtb = new RenderTargetBitmap(
            (int)(Root.ActualWidth * dpi.DpiScaleX), (int)(Root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(Root);
        rtb.Freeze();
        return rtb;
    }
}
#endif
