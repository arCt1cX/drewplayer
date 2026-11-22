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
    private void Build()
    {
        CancelCapture();
        Page.Children.Clear();
        switch (_page)
        {
            case "general": BuildGeneral(); break;
            case "look": BuildLook(); break;
            case "buttons": BuildButtons(); break;
            case "keys": BuildKeys(); break;
            case "mouse": BuildMouse(); break;
            case "playback": BuildPlayback(); break;
            case "photos": BuildPhotos(); break;
            case "system": BuildSystem(); break;
        }
        Scroll.ScrollToTop();
    }

    private void BuildGeneral()
    {
        Title1("General");
        Card("Startup",
            Row("Open in fullscreen", "Every file starts directly in fullscreen",
                Toggle(_s.Playback.StartFullscreen, v => _s.Playback.StartFullscreen = v)),
            Row("Start in zen mode", "Just the video: no controls, title or cursor",
                Toggle(_s.Ui.StartInZen, v => _s.Ui.StartInZen = v)),
            Row("Show intro", "The film-reel animation on launch",
                Toggle(_s.Splash.Enabled, v => _s.Splash.Enabled = v)),
            Row("Intro duration", null,
                SliderBox(0.5, 3, 0.1, _s.Splash.DurationMs / 1000.0, v => _s.Splash.DurationMs = (int)(v * 1000), v => $"{v:0.0} s")));

        Card("Playback",
            Row("When reopening a file", null,
                Choice(_s.Playback.Resume ? "resume" : "start",
                    [("start", "Start from the beginning"), ("resume", "Resume where you left off")],
                    v => _s.Playback.Resume = v == "resume")),
            Row("When minimized", "Background music is usually best left playing",
                Choice(_s.Playback.MinimizeAction,
                    [("continue", "Keep playing"), ("pause_video", "Pause videos only"), ("pause_all", "Pause everything (audio too)")],
                    v => _s.Playback.MinimizeAction = v)),
            Row("Auto-advance", "When a file ends, play the next one in the same folder",
                Toggle(_s.Playback.AutoNext, v => _s.Playback.AutoNext = v)),
            Row("Folder order", "Used by previous/next. \"Same as File Explorer\" follows the sorting of the open folder window; if it isn't open, files are sorted by name",
                Choice(_s.Playback.FolderOrder, FolderOrder.Modes, v => _s.Playback.FolderOrder = v)),
            Row("Always loop the file", null,
                Toggle(_s.Playback.LoopFile, v => _s.Playback.LoopFile = v)),
            Row("Single window with tabs", "If DrewPlayer is already running, new files open in a tab instead of a new window",
                Toggle(_s.Ui.SingleWindow, v => _s.Ui.SingleWindow = v)));

        Card("Window",
            Row("Fit window to video", "The window takes the video's size and aspect ratio",
                Toggle(_s.Window.Autofit, v => _s.Window.Autofit = v)),
            Row("Maximum size", "How much of the screen it may take",
                SliderBox(30, 100, 5, _s.Window.AutofitPercent, v => _s.Window.AutofitPercent = (int)v, v => $"{v:0}%")),
            Row("Enlarge small videos", "When off, a 480p video opens at its real size (1:1 pixels)",
                Toggle(_s.Window.UpscaleSmall, v => _s.Window.UpscaleSmall = v)),
            Row("Compact window for audio", null,
                Toggle(_s.Window.AudioCompact, v => _s.Window.AudioCompact = v)),
            Row("Rounded corners", null,
                Toggle(_s.Window.RoundedCorners, v => _s.Window.RoundedCorners = v)));
    }

    private Image? _iconPreview;

    private void BuildLook()
    {
        Title1("Appearance");
        Card("App color",
            Row("Accent color", "Waveform, volume, intro, highlights",
                Swatches(_s.Ui.AccentColor, v => { _s.Ui.AccentColor = v; App.ApplyAccent(v); })));

        bool iconSame = string.IsNullOrWhiteSpace(_s.Ui.IconColor);
        _iconPreview = new Image { Source = App.AppIcon, Width = 72, Height = 72, Margin = new Thickness(0, 4, 0, 4) };
        RenderOptions.SetBitmapScalingMode(_iconPreview, BitmapScalingMode.HighQuality);
        var iconSw = Swatches(iconSame ? _s.Ui.AccentColor : _s.Ui.IconColor, v =>
        {
            _s.Ui.IconColor = v;
            _iconPreview.Source = IconMaker.Render(App.ParseColor(v), 144);
        });
        iconSw.IsEnabled = !iconSame;
        iconSw.Opacity = iconSame ? 0.4 : 1;
        Card("Icon color",
            Row("Preview", "Changes the window, Start menu and file icons", _iconPreview),
            Row("Same as app color", null, Toggle(iconSame, v =>
            {
                _s.Ui.IconColor = v ? "" : "#1D9E75";
                iconSw.IsEnabled = !v;
                iconSw.Opacity = v ? 0.4 : 1;
                _iconPreview.Source = IconMaker.Render(App.IconColorOf(_s), 144);
            })),
            Row("Icon color", null, iconSw));

        UIElement FileIconRow(string label, IconKind kind, Func<string> get, Action<string> set)
        {
            // icon preview on the left, next to the label; swatches on the right
            var preview = new Image { Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0), Source = IconMaker.Render(App.ParseColor(get()), 60, kind) };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
            var sw = Swatches(get(), v => { set(v); preview.Source = IconMaker.Render(App.ParseColor(v), 60, kind); });
            var g = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(preview);
            var text = new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            Grid.SetColumn(sw, 2);
            g.Children.Add(sw);
            return g;
        }
        Card("File icons",
            Pad(Hint2("Each file type gets its own symbol and color, so you can tell them apart at a glance in File Explorer.")),
            FileIconRow("Video", IconKind.Video, () => _s.Ui.FileIconVideo, v => _s.Ui.FileIconVideo = v),
            FileIconRow("Audio", IconKind.Audio, () => _s.Ui.FileIconAudio, v => _s.Ui.FileIconAudio = v),
            FileIconRow("Photos", IconKind.Image, () => _s.Ui.FileIconImage, v => _s.Ui.FileIconImage = v),
            FileIconRow("Downloading", IconKind.Download, () => _s.Ui.FileIconDownload, v => _s.Ui.FileIconDownload = v));

        bool splashSame = string.IsNullOrWhiteSpace(_s.Splash.Color);
        var splashSw = Swatches(splashSame ? _s.Ui.AccentColor : _s.Splash.Color, v => _s.Splash.Color = v);
        splashSw.IsEnabled = !splashSame;
        splashSw.Opacity = splashSame ? 0.4 : 1;
        Card("Intro color",
            Row("Same as app color", "Turn off to pick a different color (e.g. amber)", Toggle(splashSame, v =>
            {
                _s.Splash.Color = v ? "" : "#EF9F27";
                splashSw.IsEnabled = !v;
                splashSw.Opacity = v ? 0.4 : 1;
            })),
            Row("Intro color", null, splashSw));

        Card("On-screen controls",
            Row("Hide controls after", "Without moving the mouse, while playing",
                SliderBox(0.5, 10, 0.5, _s.Ui.ControlsHideDelayMs / 1000.0, v => _s.Ui.ControlsHideDelayMs = (int)(v * 1000), v => $"{v:0.0} s")),
            Row("Controls opacity", null,
                SliderBox(20, 100, 5, _s.Ui.ControlsOpacity * 100, v => _s.Ui.ControlsOpacity = v / 100, v => $"{v:0}%")),
            Row("Waveform density", "Number of bars",
                SliderBox(40, 300, 10, _s.Ui.WaveformBars, v => _s.Ui.WaveformBars = (int)v, v => $"{v:0}")),
            Row("On-screen messages", "Volume, seeking, speed…",
                Toggle(_s.Ui.OsdMessages, v => _s.Ui.OsdMessages = v)),
            Row("Messages in zen mode too", null,
                Toggle(_s.Ui.OsdInZen, v => _s.Ui.OsdInZen = v)));
    }

    private void BuildPlayback()
    {
        Title1("Audio & video");
        Card("Audio",
            Row("Start volume", null,
                SliderBox(0, 100, 5, _s.Playback.Volume, v => _s.Playback.Volume = (int)v, v => $"{v:0}%")),
            Row("Maximum volume", "Above 100% the sound is amplified",
                SliderBox(100, 200, 10, _s.Playback.VolumeMax, v => _s.Playback.VolumeMax = (int)v, v => $"{v:0}%")));

        Card("Video",
            Row("Hardware decoding", "Uses the GPU, less CPU. Recommended",
                Toggle(_s.Playback.Hwdec != "no", v => _s.Playback.Hwdec = v ? "auto-safe" : "no")),
            Row("High quality scaling", "Sharper resizing. The file's resolution never changes, it is only scaled to the screen. Uses a bit more GPU",
                Toggle(_s.Playback.HighQuality, v => _s.Playback.HighQuality = v)));

        Card("Subtitles",
            Row("Load subtitles automatically", ".srt/.ass files with the same name as the video",
                Toggle(_s.Playback.SubAuto, v => _s.Playback.SubAuto = v)),
            Row("Subtitle size", null,
                SliderBox(20, 80, 1, _s.Playback.SubFontSize, v => _s.Playback.SubFontSize = (int)v, v => $"{v:0}")));
    }

    private void BuildPhotos()
    {
        Title1("Photos");
        Card("Navigation",
            Row("Mouse wheel zooms", "When off, the wheel goes to the previous/next photo",
                Toggle(_s.Images.WheelZoom, v => _s.Images.WheelZoom = v)));
        Hint("On photos, the ← → keys go to the previous/next photo in the folder, " +
             "the volume keys zoom, 0 resets zoom to 100%. While zoomed in, drag to pan around the image.");
    }

    private void BuildSystem()
    {
        Title1("System");

        var exe = Environment.ProcessPath ?? "";
        var regExe = Registration.RegisteredExe;
        bool registered = Registration.IsRegistered;
        bool devCopy = exe.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) || exe.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = DimBrush,
            Text = registered
                ? (string.Equals(regExe, exe, StringComparison.OrdinalIgnoreCase) ? "DrewPlayer is registered. If files still open in another app, click the button and confirm in Windows Settings."
                                                                                  : $"Another copy is registered: {regExe}")
                : "Not registered yet.",
        };
        var btnReg = FieldButton("Set as default player", () =>
        {
            if (devCopy && MessageBox.Show(this,
                    "You are running a development build (bin folder).\nInstall it first with install.ps1, otherwise files will point to this folder.\n\nRegister this copy anyway?",
                    "DrewPlayer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            App.RegisterSelf(_store.Current);
            Registration.OpenDefaultAppsPage();
            Build();
        });
        var btnUnreg = FieldButton("Remove file associations", () => { Registration.Unregister(); Build(); });
        Card("Default player",
            Pad(status),
            Pad(Hint2("Windows 11 does not let any app make itself the default. " +
                      "The button registers DrewPlayer and opens Settings › Default apps: there, click \"Set default\" at the top " +
                      "(or pick DrewPlayer for each file type).")),
            Pad(HStack(btnReg, btnUnreg)));

        Card("File",
            Pad(HStack(
                FieldButton("Open settings folder", () => Process.Start(new ProcessStartInfo(SettingsStore.Dir) { UseShellExecute = true })),
                FieldButton("Open settings.json (advanced)", MainWindow.OpenJsonFile),
                FieldButton("Reset everything", () =>
                {
                    if (MessageBox.Show(this, "Reset all settings (including keys) to their defaults?", "DrewPlayer",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    _s = new Settings { Version = 7, Keys = SettingsStore.DefaultKeys() };
                    App.ApplyAccent(_s.Ui.AccentColor);
                    _saving = true; _store.Save(_s); _saving = false;
                    Build();
                }))));

        var ver = typeof(App).Assembly.GetName().Version;
        Card("Info",
            Pad(Hint2($"DrewPlayer {ver?.ToString(3)}  ·  engine: libmpv\n{exe}")));
    }

}
