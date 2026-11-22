using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>
/// Photo editor: rotate, flip, crop (free or fixed ratio), undo, save in place or as a copy.
/// Runs on top of the player inside the overlay; the image is decoded with WIC, not shown through mpv.
/// </summary>
public partial class OverlayWindow
{
    private enum CropDrag { None, New, Move, N, S, E, W, NW, NE, SW, SE }

    private Photo? _photo;
    private BitmapSource? _edit;
    private readonly List<BitmapSource> _undo = new();
    private string _editPath = "";
    private bool _cropMode;
    private Rect _crop;            // in image pixels
    private double _cropAspect;    // 0 = free
    private CropDrag _cropDrag;
    private Point _anchor;         // fixed corner while resizing (image pixels)
    private Point _dragStart;
    private Rect _cropStart;
    private const int MaxUndo = 12;

    public bool IsEditing => EditorLayer.Visibility == Visibility.Visible;

    private void InitPhotoEditor()
    {
        // swallow mouse input so it never reaches the player (drag window, pause, zoom...)
        EditorLayer.MouseLeftButtonDown += (_, e) => e.Handled = true;
        EditorLayer.MouseDown += (_, e) => e.Handled = true;
        EditorLayer.MouseWheel += (_, e) => e.Handled = true;
        CropCanvas.MouseLeftButtonDown += OnCropDown;
        CropCanvas.MouseMove += OnCropMove;
        CropCanvas.MouseLeftButtonUp += OnCropUp;
        EditImage.SizeChanged += (_, _) => { if (_cropMode) DrawCrop(); };
    }

    /// <summary>Opens the editor on a photo. quarterTurns: rotate right right away (the "rotate" action).</summary>
    public void OpenEditor(string path, int quarterTurns = 0)
    {
        try { _photo = PhotoIO.Load(path); }
        catch (Exception ex)
        {
            Osd("This image can't be edited: " + ex.Message, 3500, force: true);
            return;
        }
        _editPath = path;
        _edit = _photo.Bitmap;
        _undo.Clear();
        _cropMode = false;
        EditTitle.Text = IOPath.GetFileName(path);
        EditorLayer.Visibility = Visibility.Visible;
        BrightPanel.Visibility = Visibility.Collapsed;
        Root.Cursor = null;
        if (quarterTurns != 0) Apply(PhotoIO.Transform(_edit, quarterTurns));
        RefreshEditor();
    }

    private void CloseEditor(bool askIfChanged)
    {
        if (askIfChanged && _undo.Count > 0 &&
            MessageBox.Show(this, "Discard your changes to this photo?", "DrewPlayer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        EditorLayer.Visibility = Visibility.Collapsed;
        EditImage.Source = null;
        _photo = null;
        _edit = null;
        _undo.Clear();
        _cropMode = false;
        Activity();
    }

    // ---------------------------------------------------------------- edits

    private void Apply(BitmapSource next)
    {
        _undo.Add(_edit!);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _edit = next;
    }

    private void Rotate(int quarterTurns) { Apply(PhotoIO.Transform(_edit!, quarterTurns)); RefreshEditor(); }
    private void Flip(bool horizontal) { Apply(PhotoIO.Transform(_edit!, 0, flipH: horizontal, flipV: !horizontal)); RefreshEditor(); }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RefreshEditor();
    }

    private void ResetEdits()
    {
        if (_undo.Count == 0 || _photo == null) return;
        _edit = _photo.Bitmap;
        _undo.Clear();
        RefreshEditor();
    }

    private void SaveEdit(bool asCopy)
    {
        if (_edit == null || _photo == null) return;
        var ext = IOPath.GetExtension(_editPath);
        bool inPlace = !asCopy && PhotoIO.CanEncode(ext);
        var target = inPlace ? _editPath
            : PhotoIO.CopyPath(_editPath, PhotoIO.CanEncode(ext) ? ext : (_photo.HasAlpha ? ".png" : ".jpg"));
        try
        {
            if (inPlace) _main.ReleaseCurrentFile(); // mpv must let go of the file before it is replaced
            PhotoIO.Save(_edit, _photo.Metadata, target);
        }
        catch (Exception ex)
        {
            if (inPlace) _main.ReloadCurrent();
            Osd("Couldn't save: " + ex.Message, 4000, force: true);
            return;
        }
        CloseEditor(askIfChanged: false);
        if (inPlace) { _main.ReloadCurrent(); Osd("Photo saved", 1500, force: true); }
        else { _main.Open(target); Osd("Saved as " + IOPath.GetFileName(target), 2500, force: true); }
    }

    /// <summary>Keys while the editor is open. Returns true if handled (nothing reaches the player).</summary>
    private bool HandleEditorKey(Key key, ModifierKeys mods)
    {
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        if (key == Key.F4 && mods == ModifierKeys.Alt) return false;
        switch (key)
        {
            case Key.Escape: if (_cropMode) { _cropMode = false; RefreshEditor(); } else CloseEditor(true); break;
            case Key.Return: if (_cropMode) ApplyCrop(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.S when ctrl: SaveEdit(asCopy: shift); break;
            case Key.R when !_cropMode: Rotate(shift ? 3 : 1); break;
            case Key.H when !_cropMode: Flip(true); break;
            case Key.V when !_cropMode: Flip(false); break;
            case Key.C when !ctrl: if (_cropMode) { _cropMode = false; RefreshEditor(); } else StartCrop(); break;
        }
        return true;
    }

    // ---------------------------------------------------------------- UI

    private void RefreshEditor()
    {
        if (_edit == null) return;
        EditImage.Source = _edit;
        EditInfo.Text = $"{_edit.PixelWidth} × {_edit.PixelHeight}" + (_undo.Count > 0 ? "   ·   edited" : "");
        CropCanvas.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
        BuildEditBar();
        if (_cropMode) Dispatcher.BeginInvoke(DrawCrop, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BuildEditBar()
    {
        var p = EditButtons.Children;
        p.Clear();
        Button Tool(string icon, string tip, Action a, bool enabled = true)
        {
            var b = new Button { Style = (Style)FindResource("IconButton"), Content = Icons.Make(icon, 19), ToolTip = tip, IsEnabled = enabled, Opacity = enabled ? 1 : 0.35 };
            b.Click += (_, _) => a();
            return b;
        }
        Button Pill(string text, Action a, bool accent = false, bool enabled = true, string? tip = null)
        {
            var b = new Button { Style = (Style)FindResource(accent ? "PillAccent" : "PillButton"), Content = text, IsEnabled = enabled, ToolTip = tip };
            b.Click += (_, _) => a();
            return b;
        }
        UIElement Gap() => new Border { Width = 1, Height = 22, Margin = new Thickness(8, 0, 8, 0), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) };

        if (_cropMode)
        {
            foreach (var (label, ratio) in new[] { ("Free", 0.0), ("1:1", 1.0), ("4:3", 4 / 3.0), ("3:2", 1.5), ("16:9", 16 / 9.0) })
            {
                var r = ratio;
                var b = Pill(label, () => { _cropAspect = r; FitCropToAspect(); BuildEditBar(); }, accent: Math.Abs(_cropAspect - r) < 0.001);
                p.Add(b);
            }
            p.Add(Gap());
            p.Add(Pill("Cancel", () => { _cropMode = false; RefreshEditor(); }));
            p.Add(Pill("Apply crop", ApplyCrop, accent: true, tip: "Enter"));
            return;
        }

        p.Add(Tool(Icons.RotateLeft, "Rotate left (Shift+R)", () => Rotate(3)));
        p.Add(Tool(Icons.RotateRight, "Rotate right (R)", () => Rotate(1)));
        p.Add(Tool(Icons.FlipH, "Flip horizontally (H)", () => Flip(true)));
        p.Add(Tool(Icons.FlipV, "Flip vertically (V)", () => Flip(false)));
        p.Add(Tool(Icons.Crop, "Crop (C)", StartCrop));
        p.Add(Gap());
        p.Add(Tool(Icons.Undo, "Undo (Ctrl+Z)", Undo, enabled: _undo.Count > 0));
        p.Add(Pill("Reset", ResetEdits, enabled: _undo.Count > 0));
        p.Add(Gap());
        p.Add(Pill("Cancel", () => CloseEditor(true), tip: "Esc"));
        bool canOverwrite = PhotoIO.CanEncode(IOPath.GetExtension(_editPath));
        p.Add(Pill("Save as copy", () => SaveEdit(asCopy: true), enabled: _undo.Count > 0, tip: "Ctrl+Shift+S"));
        p.Add(Pill("Save", () => SaveEdit(asCopy: false), accent: true, enabled: _undo.Count > 0 && canOverwrite,
            tip: canOverwrite ? "Overwrite the original (Ctrl+S)" : "This format can only be saved as a copy"));
    }

    // ---------------------------------------------------------------- crop

    private void StartCrop()
    {
        if (_edit == null) return;
        _cropMode = true;
        _cropAspect = 0;
        _crop = new Rect(0, 0, _edit.PixelWidth, _edit.PixelHeight);
        RefreshEditor();
    }

    private void ApplyCrop()
    {
        if (_edit == null) return;
        var r = Rect.Intersect(_crop, new Rect(0, 0, _edit.PixelWidth, _edit.PixelHeight));
        var ir = new Int32Rect((int)Math.Round(r.X), (int)Math.Round(r.Y), Math.Max(1, (int)Math.Round(r.Width)), Math.Max(1, (int)Math.Round(r.Height)));
        ir.Width = Math.Min(ir.Width, _edit.PixelWidth - ir.X);
        ir.Height = Math.Min(ir.Height, _edit.PixelHeight - ir.Y);
        _cropMode = false;
        if (ir.X != 0 || ir.Y != 0 || ir.Width != _edit.PixelWidth || ir.Height != _edit.PixelHeight)
            Apply(PhotoIO.Crop(_edit, ir));
        RefreshEditor();
    }

    /// <summary>Largest centered rectangle with the chosen ratio.</summary>
    private void FitCropToAspect()
    {
        if (_edit == null) return;
        double w = _edit.PixelWidth, h = _edit.PixelHeight;
        if (_cropAspect > 0)
        {
            if (w / h > _cropAspect) w = h * _cropAspect; else h = w / _cropAspect;
        }
        _crop = new Rect((_edit.PixelWidth - w) / 2, (_edit.PixelHeight - h) / 2, w, h);
        DrawCrop();
    }

    /// <summary>Where the image is drawn inside the crop canvas, and its scale (screen px per image px).</summary>
    private (Rect area, double scale) ImageArea()
    {
        var origin = EditImage.TranslatePoint(new Point(0, 0), CropCanvas);
        var area = new Rect(origin, new Size(EditImage.ActualWidth, EditImage.ActualHeight));
        double scale = _edit == null || _edit.PixelWidth == 0 ? 1 : EditImage.ActualWidth / _edit.PixelWidth;
        return (area, scale);
    }

    private Rect ToScreen(Rect r)
    {
        var (a, s) = ImageArea();
        return new Rect(a.X + r.X * s, a.Y + r.Y * s, r.Width * s, r.Height * s);
    }

    private Point ToImage(Point p)
    {
        var (a, s) = ImageArea();
        double x = Math.Clamp((p.X - a.X) / s, 0, _edit!.PixelWidth);
        double y = Math.Clamp((p.Y - a.Y) / s, 0, _edit.PixelHeight);
        return new Point(x, y);
    }

    private void DrawCrop()
    {
        CropCanvas.Children.Clear();
        if (!_cropMode || _edit == null || EditImage.ActualWidth < 1) return;
        var full = new Rect(0, 0, CropCanvas.ActualWidth, CropCanvas.ActualHeight);
        var c = ToScreen(_crop);
        var white = Brushes.White;

        CropCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0)), IsHitTestVisible = false,
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(c)),
        });
        void Line(double x1, double y1, double x2, double y2, double op) => CropCanvas.Children.Add(new Line
        { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = white, StrokeThickness = 1, Opacity = op, IsHitTestVisible = false });
        for (int i = 1; i <= 2; i++) // rule of thirds
        {
            Line(c.X + c.Width * i / 3, c.Y, c.X + c.Width * i / 3, c.Bottom, 0.35);
            Line(c.X, c.Y + c.Height * i / 3, c.Right, c.Y + c.Height * i / 3, 0.35);
        }
        var border = new Rectangle { Width = c.Width, Height = c.Height, Stroke = white, StrokeThickness = 1.5, IsHitTestVisible = false };
        Canvas.SetLeft(border, c.X); Canvas.SetTop(border, c.Y);
        CropCanvas.Children.Add(border);
        foreach (var (x, y) in new[] { (c.X, c.Y), (c.Right, c.Y), (c.X, c.Bottom), (c.Right, c.Bottom),
                                       (c.X + c.Width / 2, c.Y), (c.X + c.Width / 2, c.Bottom), (c.X, c.Y + c.Height / 2), (c.Right, c.Y + c.Height / 2) })
        {
            var h = new Rectangle { Width = 10, Height = 10, Fill = white, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false };
            Canvas.SetLeft(h, x - 5); Canvas.SetTop(h, y - 5);
            CropCanvas.Children.Add(h);
        }
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0B, 0x17, 0x14)), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2, 6, 2), IsHitTestVisible = false,
            Child = new TextBlock { Text = $"{Math.Round(_crop.Width)} × {Math.Round(_crop.Height)}", Foreground = white, FontSize = 11.5, FontFamily = (FontFamily)FindResource("MonoFont") },
        };
        Canvas.SetLeft(label, c.X + 6); Canvas.SetTop(label, Math.Max(2, c.Y - 24));
        CropCanvas.Children.Add(label);
    }

    private CropDrag HitCrop(Point p)
    {
        var c = ToScreen(_crop);
        const double t = 10;
        bool l = Math.Abs(p.X - c.X) < t, r = Math.Abs(p.X - c.Right) < t, top = Math.Abs(p.Y - c.Y) < t, b = Math.Abs(p.Y - c.Bottom) < t;
        bool inX = p.X > c.X - t && p.X < c.Right + t, inY = p.Y > c.Y - t && p.Y < c.Bottom + t;
        if (top && l) return CropDrag.NW;
        if (top && r) return CropDrag.NE;
        if (b && l) return CropDrag.SW;
        if (b && r) return CropDrag.SE;
        if (top && inX) return CropDrag.N;
        if (b && inX) return CropDrag.S;
        if (l && inY) return CropDrag.W;
        if (r && inY) return CropDrag.E;
        return c.Contains(p) ? CropDrag.Move : CropDrag.New;
    }

    private static Cursor CursorFor(CropDrag d) => d switch
    {
        CropDrag.N or CropDrag.S => Cursors.SizeNS,
        CropDrag.E or CropDrag.W => Cursors.SizeWE,
        CropDrag.NW or CropDrag.SE => Cursors.SizeNWSE,
        CropDrag.NE or CropDrag.SW => Cursors.SizeNESW,
        CropDrag.Move => Cursors.SizeAll,
        _ => Cursors.Cross,
    };

    private void OnCropDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var p = e.GetPosition(CropCanvas);
        _cropDrag = HitCrop(p);
        _dragStart = ToImage(p);
        _cropStart = _crop;
        _anchor = _cropDrag switch
        {
            CropDrag.NW => _crop.BottomRight,
            CropDrag.NE => _crop.BottomLeft,
            CropDrag.SW => _crop.TopRight,
            CropDrag.SE => _crop.TopLeft,
            _ => _dragStart,
        };
        CropCanvas.CaptureMouse();
    }

    private void OnCropMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(CropCanvas);
        if (_cropDrag == CropDrag.None) { CropCanvas.Cursor = CursorFor(HitCrop(p)); return; }
        var q = ToImage(p);
        double W = _edit!.PixelWidth, H = _edit.PixelHeight;
        Rect r = _cropStart;
        switch (_cropDrag)
        {
            case CropDrag.Move:
                double x = Math.Clamp(_cropStart.X + q.X - _dragStart.X, 0, W - _cropStart.Width);
                double y = Math.Clamp(_cropStart.Y + q.Y - _dragStart.Y, 0, H - _cropStart.Height);
                r = new Rect(x, y, _cropStart.Width, _cropStart.Height);
                break;
            case CropDrag.New or CropDrag.NW or CropDrag.NE or CropDrag.SW or CropDrag.SE:
                r = FromAnchor(_anchor, q);
                break;
            case CropDrag.N: r = Edge(top: Math.Min(q.Y, _cropStart.Bottom - 8)); break;
            case CropDrag.S: r = Edge(bottom: Math.Max(q.Y, _cropStart.Top + 8)); break;
            case CropDrag.W: r = Edge(left: Math.Min(q.X, _cropStart.Right - 8)); break;
            case CropDrag.E: r = Edge(right: Math.Max(q.X, _cropStart.Left + 8)); break;
        }
        if (r.Width >= 4 && r.Height >= 4) _crop = r;
        DrawCrop();

        Rect Edge(double? left = null, double? top = null, double? right = null, double? bottom = null)
        {
            double L = left ?? _cropStart.Left, T = top ?? _cropStart.Top, R = right ?? _cropStart.Right, B = bottom ?? _cropStart.Bottom;
            var e2 = new Rect(new Point(L, T), new Point(R, B));
            if (_cropAspect <= 0) return e2;
            // keep the ratio: grow the other side around the center
            if (left != null || right != null)
            {
                double h = Math.Min(e2.Width / _cropAspect, H);
                double cy = Math.Clamp(_cropStart.Y + _cropStart.Height / 2, h / 2, H - h / 2);
                return new Rect(e2.X, cy - h / 2, h * _cropAspect, h);
            }
            double w = Math.Min(e2.Height * _cropAspect, W);
            double cx = Math.Clamp(_cropStart.X + _cropStart.Width / 2, w / 2, W - w / 2);
            return new Rect(cx - w / 2, e2.Y, w, w / _cropAspect);
        }
    }

    /// <summary>Rectangle from a fixed corner to the mouse, honoring the ratio and the image bounds.</summary>
    private Rect FromAnchor(Point a, Point q)
    {
        double dx = q.X - a.X, dy = q.Y - a.Y;
        double w = Math.Abs(dx), h = Math.Abs(dy);
        if (_cropAspect > 0)
        {
            if (w / Math.Max(h, 0.001) > _cropAspect) w = h * _cropAspect; else h = w / _cropAspect;
            // stay inside the image in the drag direction
            double maxW = dx >= 0 ? _edit!.PixelWidth - a.X : a.X, maxH = dy >= 0 ? _edit!.PixelHeight - a.Y : a.Y;
            if (w > maxW) { w = maxW; h = w / _cropAspect; }
            if (h > maxH) { h = maxH; w = h * _cropAspect; }
        }
        double x = dx >= 0 ? a.X : a.X - w, y = dy >= 0 ? a.Y : a.Y - h;
        return new Rect(x, y, w, h);
    }

    private void OnCropUp(object sender, MouseButtonEventArgs e)
    {
        _cropDrag = CropDrag.None;
        CropCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }
}
