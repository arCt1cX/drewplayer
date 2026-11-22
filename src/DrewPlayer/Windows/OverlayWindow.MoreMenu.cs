using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DrewPlayer;

/// <summary>The "⋯" menu: actions that fit the open file (photo or video/audio), with their shortcuts.</summary>
public partial class OverlayWindow
{
    private void InitMoreMenu()
    {
        BtnMore.Content = Icons.Make(Icons.More, 20, stroke: 3);
        BtnMore.Click += (_, _) => ShowMoreMenu();
    }

    public void ShowMoreMenu()
    {
        if (IsEditing) return;
        var menu = new ContextMenu { Style = (Style)FindResource("DarkMenu") };
        bool file = _main.HasFile, photo = _main.IsImage, video = !photo && !_main.AudioOnly && _main.CurrentFile != null;
        bool media = _main.CurrentFile != null && !photo;

        void Item(string text, string action, bool enabled = true, Brush? color = null)
        {
            var key = KeyFor(action);
            var mi = new MenuItem
            {
                Header = text, Style = (Style)FindResource("DarkMenuItem"), IsEnabled = enabled,
                InputGestureText = key != null ? KeyChord.Pretty(key) : "",
            };
            if (color != null) mi.Foreground = color;
            mi.Click += (_, _) => _main.RunAction(action);
            menu.Items.Add(mi);
        }
        void Sep() => menu.Items.Add(new Separator { Style = (Style)FindResource("DarkSeparator") });

        if (photo)
        {
            Item("Edit photo…", "edit_photo", file);
            Item("Rotate", "rotate", file);
            Sep();
            Item("Copy image", "copy_image", file);
            Item("Set as desktop background", "set_wallpaper", file);
        }
        else if (media)
        {
            if (video)
            {
                Item($"Rotate video  ({_main.VideoRotation:0}°)", "rotate");
                Item($"Aspect ratio: {_main.AspectLabel}", "aspect");
                Item($"Deinterlace: {(_main.Deinterlace ? "on" : "off")}", "deinterlace");
                Sep();
            }
            Item($"A-B loop: {_main.AbLoopState}", "ab_loop");
            Item($"Audio delay −100 ms  ({_main.AudioDelay * 1000:+0;−0;0} ms)", "audio_delay -0.1");
            Item("Audio delay +100 ms", "audio_delay 0.1");
            Item("Reset audio delay", "audio_delay 0", enabled: Math.Abs(_main.AudioDelay) > 0.0001);
            if (video)
            {
                Item("Load subtitles…", "sub_load");
                Sep();
                Item("Copy current frame", "copy_image", file);
                Item("Screenshot", "screenshot");
            }
        }
        if (_main.CurrentFile != null) Sep();
        Item("Show in folder", "show_in_folder", file);
        Item("File info", "info", _main.CurrentFile != null);
        Item("Delete (Recycle Bin)…", "delete_file", file, new SolidColorBrush(Color.FromRgb(0xF0, 0x95, 0x95)));

        bool fromButton = BtnMore.IsVisible;
        menu.PlacementTarget = fromButton ? BtnMore : Root;
        menu.Placement = fromButton ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.Closed += (_, _) => Activity();
        menu.IsOpen = true;
        _hide.Stop(); // keep the controls up while the menu is open
    }
}
