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
    private void BuildMouse()
    {
        Title1("Mouse");
        Hint("Dragging the video always moves the window; the edges resize it.");
        var m = _s.Mouse;
        Card("Buttons",
            MouseRow("Left click", m.LeftClick, v => m.LeftClick = v),
            MouseRow("Double click", m.DoubleClick, v => m.DoubleClick = v),
            MouseRow("Right click", m.RightClick, v => m.RightClick = v),
            MouseRow("Middle click", m.MiddleClick, v => m.MiddleClick = v),
            MouseRow("Back side button", m.XButton1, v => m.XButton1 = v),
            MouseRow("Forward side button", m.XButton2, v => m.XButton2 = v));
        Card("Wheel",
            MouseRow("Wheel up", m.WheelUp, v => m.WheelUp = v),
            MouseRow("Wheel down", m.WheelDown, v => m.WheelDown = v));
    }

    private UIElement MouseRow(string label, string current, Action<string> set)
    {
        var (id, p) = ActionCatalog.Split(current);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var host = new ContentControl();
        void Refresh()
        {
            var info = ActionCatalog.Find(id);
            host.Content = info?.Unit != null
                ? ParamBox(p, info.Unit, v => { p = v; set(ActionCatalog.Join(id, p)); Commit(); })
                : null;
        }
        var picker = ActionPicker(id, nid =>
        {
            id = nid;
            p = ActionCatalog.Find(nid)?.DefaultParam ?? 0;
            set(ActionCatalog.Join(id, p));
            Commit();
            Refresh();
        });
        picker.Width = 250;
        host.Margin = new Thickness(10, 0, 0, 0);
        host.Width = 130;
        panel.Children.Add(picker);
        panel.Children.Add(host);
        Refresh();
        return Row(label, null, panel);
    }

}
