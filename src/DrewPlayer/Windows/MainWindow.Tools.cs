using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>File and video tools: delete, show in folder, copy, wallpaper, rotation, aspect ratio, A-B loop...</summary>
public partial class MainWindow
{
    private static readonly (string value, string label)[] AspectRatios =
        [("no", "Auto"), ("16:9", "16:9"), ("4:3", "4:3"), ("21:9", "21:9"), ("1:1", "1:1"), ("9:16", "9:16")];
    private int _aspectIndex;

    public bool HasFile => CurrentFile != null && !CurrentFile.Contains("://");
    public string AspectLabel => AspectRatios[_aspectIndex].label;
    public double VideoRotation => _mpv?.GetDouble("video-rotate") ?? 0;
    public double AudioDelay => _mpv?.GetDouble("audio-delay") ?? 0;
    public bool Deinterlace => _mpv?.GetString("deinterlace") is "yes" or "auto";

    /// <summary>A-B loop state for the menu: "off", "A set" or "A → B".</summary>
    public string AbLoopState
    {
        get
        {
            var a = _mpv?.GetString("ab-loop-a");
            var b = _mpv?.GetString("ab-loop-b");
            if (a is null or "no") return "off";
            return b is null or "no" ? "A set" : "A → B";
        }
    }

    /// <summary>Lets mpv close the file (so it can be overwritten or deleted).</summary>
    public void ReleaseCurrentFile() => _mpv?.CommandSync("stop");

    /// <summary>Reloads the current tab's file (e.g. after the photo was overwritten).</summary>
    public void ReloadCurrent()
    {
        if (ActiveTab != null) Load(ActiveTab, null);
    }

    private void ShowInFolder()
    {
        if (!HasFile) return;
        Process.Start("explorer.exe", $"/select,\"{CurrentFile}\"");
    }

    private void DeleteCurrent()
    {
        if (!HasFile) return;
        var path = CurrentFile!;
        if (MessageBox.Show(_overlay!, $"Move \"{IOPath.GetFileName(path)}\" to the Recycle Bin?", "DrewPlayer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        BuildFolderList(path, askExplorer: false);
        int i = _folderIndex;
        string? next = i + 1 < _folder.Count ? _folder[i + 1] : i - 1 >= 0 ? _folder[i - 1] : null;
        if (next != null && string.Equals(next, path, StringComparison.OrdinalIgnoreCase)) next = null;

        ReleaseCurrentFile();
        if (!Native.RecycleFile(path, _hwnd))
        {
            ReloadCurrent();
            _overlay?.Osd("Couldn't delete the file", 2500, force: true);
            return;
        }
        _overlay?.Osd("Moved to the Recycle Bin", 1800, force: true);
        if (next != null) Open(next); else CloseTab(_active);
    }

    /// <summary>Photo → clipboard; video → the current frame, at full resolution.</summary>
    private void CopyImage()
    {
        if (!HasFile || _mpv == null) return;
        try
        {
            BitmapSource bmp;
            if (IsImage) bmp = PhotoIO.Load(CurrentFile!).Bitmap;
            else
            {
                if (AudioOnly) return;
                var tmp = IOPath.Combine(IOPath.GetTempPath(), $"drewplayer-frame-{Environment.ProcessId}.png");
                _mpv.CommandSync("screenshot-to-file", tmp, "video");
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(tmp);
                img.EndInit();
                img.Freeze();
                bmp = img;
                File.Delete(tmp);
            }
            Clipboard.SetImage(bmp);
            _overlay?.Osd(IsImage ? "Image copied" : "Frame copied", 1500, force: true);
        }
        catch (Exception ex)
        {
            _overlay?.Osd("Couldn't copy: " + ex.Message, 3000, force: true);
        }
    }

    private void SetAsWallpaper()
    {
        if (!HasFile || !IsImage) return;
        var path = CurrentFile!;
        try
        {
            // Windows handles JPG/PNG/BMP directly; anything else is converted to PNG first
            var ext = IOPath.GetExtension(path).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
            {
                var png = IOPath.Combine(SettingsStore.Dir, "wallpaper.png");
                PhotoIO.Save(PhotoIO.Load(path).Bitmap, null, png);
                path = png;
            }
            bool ok = Native.SetWallpaper(path);
            _overlay?.Osd(ok ? "Set as desktop background" : "Couldn't set the background", 2000, force: true);
        }
        catch (Exception ex)
        {
            _overlay?.Osd("Couldn't set the background: " + ex.Message, 3000, force: true);
        }
    }

    private void RotateVideo()
    {
        if (_mpv == null || AudioOnly) return;
        var deg = ((int)VideoRotation + 90) % 360;
        _mpv.Set("video-rotate", deg);
        _overlay?.Osd($"Rotation {deg}°");
    }

    private void CycleAspect()
    {
        if (_mpv == null || AudioOnly) return;
        _aspectIndex = (_aspectIndex + 1) % AspectRatios.Length;
        _mpv.Set("video-aspect-override", AspectRatios[_aspectIndex].value);
        _overlay?.Osd("Aspect ratio: " + AspectLabel);
    }

    private void AbLoop()
    {
        if (_mpv == null || CurrentFile == null) return;
        _mpv.CommandSync("ab-loop");
        var a = _mpv.GetDouble("ab-loop-a");
        var b = _mpv.GetDouble("ab-loop-b");
        _overlay?.Osd(AbLoopState switch
        {
            "A set" => $"Loop start (A): {FormatTime(a ?? 0)} — press again to set the end",
            "A → B" => $"Looping {FormatTime(a ?? 0)} → {FormatTime(b ?? 0)}",
            _ => "A-B loop off",
        }, 2200, force: true);
    }

    private void AddAudioDelay(double seconds)
    {
        if (_mpv == null) return;
        if (seconds == 0) _mpv.Set("audio-delay", 0.0);
        else _mpv.CommandSync("add", "audio-delay", Num.Inv(seconds));
        _overlay?.Osd($"Audio delay {AudioDelay * 1000:+0;−0;0} ms");
    }

    private void LoadSubtitles()
    {
        if (_mpv == null || CurrentFile == null || IsImage) return;
        var dlg = new OpenFileDialog
        {
            Title = "Load subtitles",
            Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.sup;*.idx|All files|*.*",
        };
        if (HasFile) dlg.InitialDirectory = IOPath.GetDirectoryName(CurrentFile);
        if (dlg.ShowDialog(this) != true) return;
        _mpv.Command("sub-add", dlg.FileName, "select");
        _overlay?.Osd("Subtitles: " + IOPath.GetFileName(dlg.FileName));
    }

    private void ToggleDeinterlace()
    {
        if (_mpv == null || AudioOnly || IsImage) return;
        _mpv.CommandSync("cycle", "deinterlace");
        _overlay?.Osd(Deinterlace ? "Deinterlace: on" : "Deinterlace: off");
    }
}
