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
    private sealed class KeyRow { public string Chord = ""; public string Id = "none"; public double Param; }
    private List<KeyRow> _rows = new();
    private KeyRow? _capturing;
    private Button? _captureBtn;

    private void LoadRows()
    {
        var order = ActionCatalog.All.Select(a => a.Id).ToList();
        _rows = _s.Keys.Select(kv =>
        {
            var (id, p) = ActionCatalog.Split(kv.Value);
            return new KeyRow { Chord = kv.Key, Id = id, Param = p };
        })
        .OrderBy(r => order.IndexOf(r.Id) is var i && i < 0 ? 999 : i)
        .ThenBy(r => r.Param < 0 ? 1 : 0).ThenBy(r => Math.Abs(r.Param))
        .ToList();
    }

    private void SaveRows()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _rows.Where(r => r.Chord.Length > 0 && r.Id != "none"))
            d[r.Chord] = ActionCatalog.Join(r.Id, r.Param);
        _s.Keys = d;
        Commit();
    }

    private void BuildKeys()
    {
        LoadRows();
        Title1("Keys");
        Hint("Click a key to change it, then press the new combination (Ctrl, Shift and Alt work too). " +
             "For seeking you can choose the seconds: negative = backwards.");

        var add = FieldButton("+  Add shortcut", () =>
        {
            _rows.Insert(0, new KeyRow { Id = "seek", Param = 10 });
            SaveRowsAndRebuild(captureFirst: true);
        });
        var reset = FieldButton("Reset default keys", () =>
        {
            _s.Keys = SettingsStore.DefaultKeys();
            Commit();
            Build();
        });
        Page.Children.Add(HStack(add, reset));
        Page.Children.Add(new Border { Height = 14 });

        var list = new StackPanel();
        for (int i = 0; i < _rows.Count; i++) list.Children.Add(KeyRowUi(_rows[i], i == _rows.Count - 1));
        Page.Children.Add(CardRaw(list));
    }

    private void SaveRowsAndRebuild(bool captureFirst = false)
    {
        SaveRows();
        var off = Scroll.VerticalOffset;
        Page.Children.Clear();
        _capturing = null;
        // rebuild without reloading from the dictionary (keeps order / empty rows)
        Title1("Keys");
        Hint("Click a key to change it, then press the new combination (Ctrl, Shift and Alt work too). " +
             "For seeking you can choose the seconds: negative = backwards.");
        var add = FieldButton("+  Add shortcut", () => { _rows.Insert(0, new KeyRow { Id = "seek", Param = 10 }); SaveRowsAndRebuild(true); });
        var reset = FieldButton("Reset default keys", () => { _s.Keys = SettingsStore.DefaultKeys(); Commit(); Build(); });
        Page.Children.Add(HStack(add, reset));
        Page.Children.Add(new Border { Height = 14 });
        var list = new StackPanel();
        Button? first = null;
        for (int i = 0; i < _rows.Count; i++)
        {
            var ui = KeyRowUi(_rows[i], i == _rows.Count - 1);
            if (i == 0) first = _lastKeyBtn;
            list.Children.Add(ui);
        }
        Page.Children.Add(CardRaw(list));
        if (captureFirst && first != null) { Scroll.ScrollToTop(); StartCapture(_rows[0], first); }
        else Scroll.ScrollToVerticalOffset(off);
    }

    private Button? _lastKeyBtn;

    private UIElement KeyRowUi(KeyRow r, bool last)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6), MinHeight = 38 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var keyBtn = FieldButton(r.Chord.Length > 0 ? KeyChord.Pretty(r.Chord) : "No key", null);
        keyBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
        keyBtn.FontFamily = (FontFamily)FindResource("MonoFont");
        keyBtn.FontSize = 13;
        keyBtn.Margin = new Thickness(0, 0, 12, 0);
        if (r.Chord.Length == 0) keyBtn.Foreground = DimBrush;
        keyBtn.Click += (_, _) => StartCapture(r, keyBtn);
        _lastKeyBtn = keyBtn;
        g.Children.Add(keyBtn);

        var picker = ActionPicker(r.Id, id =>
        {
            r.Id = id;
            r.Param = ActionCatalog.Find(id)?.DefaultParam ?? 0;
            SaveRowsAndRebuild();
        });
        Grid.SetColumn(picker, 1);
        g.Children.Add(picker);

        var info = ActionCatalog.Find(r.Id);
        if (info?.Unit != null)
        {
            var pbox = ParamBox(r.Param, info.Unit, v => { r.Param = v; SaveRows(); });
            Grid.SetColumn(pbox, 2);
            g.Children.Add(pbox);
        }

        var del = FieldButton("✕", () => { _rows.Remove(r); SaveRowsAndRebuild(); });
        del.Width = 36; del.HorizontalContentAlignment = HorizontalAlignment.Center; del.Padding = new Thickness(0, 7, 0, 7);
        del.ToolTip = "Remove";
        del.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(del, 3);
        g.Children.Add(del);

        if (last) return g;
        var sp = new StackPanel();
        sp.Children.Add(g);
        sp.Children.Add(new Border { Height = 1, Background = LineBrush });
        return sp;
    }

    private void StartCapture(KeyRow r, Button b)
    {
        CancelCapture();
        _capturing = r;
        _captureBtn = b;
        b.Content = "Press a key…";
        b.Foreground = (Brush)FindResource("Accent");
        b.BorderBrush = (Brush)FindResource("Accent");
        b.Focus();
    }

    private void CancelCapture()
    {
        if (_capturing != null && _captureBtn != null)
        {
            _captureBtn.Content = _capturing.Chord.Length > 0 ? KeyChord.Pretty(_capturing.Chord) : "No key";
            _captureBtn.Foreground = _capturing.Chord.Length > 0 ? Brushes.White : DimBrush;
            _captureBtn.BorderBrush = Brushes.Transparent;
        }
        _capturing = null;
        _captureBtn = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing == null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.None) return;

        var chord = KeyChord.ToText(key, Keyboard.Modifiers);
        var row = _capturing;
        // key already in use: take it away from the other action
        foreach (var other in _rows.Where(o => o != row && string.Equals(o.Chord, chord, StringComparison.OrdinalIgnoreCase)))
            other.Chord = "";
        row.Chord = chord;
        _capturing = null;
        _captureBtn = null;
        SaveRowsAndRebuild();
    }

}
