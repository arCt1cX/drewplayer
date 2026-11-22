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
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed) return;

        if (IsEditing && HandleEditorKey(key, Keyboard.Modifiers)) { e.Handled = true; return; }

        if (_keys.TryGetValue((key, Keyboard.Modifiers), out var action))
        {
            _main.RunAction(action);
            e.Handled = true;
        }
        else if (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            _main.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.System)
        {
            e.Handled = true; // no system menu on Alt
        }
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Root);
        if (_panning)
        {
            _main.Pan(p.X - _panLast.X, p.Y - _panLast.Y, Root.ActualWidth, Root.ActualHeight);
            _panLast = p;
            return;
        }
        if (Math.Abs(p.X - _lastMouse.X) + Math.Abs(p.Y - _lastMouse.Y) > 1.5)
        {
            _lastMouse = p;
            Activity();
        }

        _edge = _main.IsFullscreen ? 0 : EdgeHit(p);
        Root.Cursor = _edge switch
        {
            Native.HTLEFT or Native.HTRIGHT => Cursors.SizeWE,
            Native.HTTOP or Native.HTBOTTOM => Cursors.SizeNS,
            Native.HTTOPLEFT or Native.HTBOTTOMRIGHT => Cursors.SizeNWSE,
            Native.HTTOPRIGHT or Native.HTBOTTOMLEFT => Cursors.SizeNESW,
            _ => Root.Cursor == Cursors.None ? Cursors.None : null,
        };
    }

    private int EdgeHit(Point p)
    {
        const double b = 6;
        double w = Root.ActualWidth, h = Root.ActualHeight;
        bool l = p.X < b, r = p.X > w - b, t = p.Y < b, bo = p.Y > h - b;
        if (t && l) return Native.HTTOPLEFT;
        if (t && r) return Native.HTTOPRIGHT;
        if (bo && l) return Native.HTBOTTOMLEFT;
        if (bo && r) return Native.HTBOTTOMRIGHT;
        if (l) return Native.HTLEFT;
        if (r) return Native.HTRIGHT;
        if (t) return Native.HTTOP;
        if (bo) return Native.HTBOTTOM;
        return 0;
    }

    private void OnRootLeftDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_edge != 0) { _main.BeginNativeDrag(_edge); return; }

        var p = e.GetPosition(Root);
        long now = Environment.TickCount64;
        if (now - _lastDown <= Native.GetDoubleClickTime() && (p - _downPt).Length < 6)
        {
            _lastDown = 0;
            // the first click already ran its action (e.g. pause): if it is a toggle, undo it
            if (_lastSingleAction != null && IsToggle(_lastSingleAction)) _main.RunAction(_lastSingleAction);
            _lastSingleAction = null;
            _main.RunAction(S.Mouse.DoubleClick);
            return;
        }
        _lastDown = now;
        _downPt = p;
        _lastSingleAction = null;

        // zoomed photo: drag = pan the image
        if (_main.IsImage && _main.Zoom > 0)
        {
            _panning = true;
            _panLast = p;
            Root.CaptureMouse();
            return;
        }

        Native.GetCursorPos(out var before);
        _main.BeginNativeDrag(Native.HTCAPTION); // blocks until the button is released
        Native.GetCursorPos(out var after);
        bool moved = Math.Abs(after.X - before.X) + Math.Abs(after.Y - before.Y) > 3;
        if (moved) { _lastDown = 0; return; }
        if (!string.Equals(S.Mouse.LeftClick?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            _main.RunAction(S.Mouse.LeftClick);
            _lastSingleAction = S.Mouse.LeftClick;
        }
    }

    private string? _lastSingleAction;
    private bool _panning;
    private Point _panLast;

    private static bool IsToggle(string action) =>
        ActionCatalog.Split(action).id is "pause" or "mute" or "zen" or "loop" or "sub_toggle";

    private void OnRootLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Root.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        var action = e.ChangedButton switch
        {
            MouseButton.Right => S.Mouse.RightClick,
            MouseButton.Middle => S.Mouse.MiddleClick,
            MouseButton.XButton1 => S.Mouse.XButton1,
            MouseButton.XButton2 => S.Mouse.XButton2,
            _ => null,
        };
        if (action == null) return;
        _main.RunAction(action);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var media = files.Where(App.IsMedia).ToList();
            if (media.Count == 0) media.Add(files[0]);
            // several dropped files = several tabs
            _main.OpenMany(media, newTabs: false);
            Activate();
        }
    }

}
