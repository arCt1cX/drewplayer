using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace DrewPlayer;

public partial class MainWindow
{
    /// <summary>Runs an action ("seek 10", "zen", ...).</summary>
    public void RunAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action) || _mpv == null) return;
        var (name, n) = ActionCatalog.Split(action);
        var text = action.Trim();
        var arg = text.Contains(' ') ? text[(text.IndexOf(' ') + 1)..].Trim() : "";
        var o = _overlay!;

        // on photos some actions change meaning
        if (IsImage)
        {
            switch (name)
            {
                case "pause": return;
                case "seek": if (n != 0) Step(n > 0 ? +1 : -1); return;
                case "volume":
                    if (S.Images.WheelZoom) ZoomStep(n > 0 ? 1 : -1);
                    else Step(n > 0 ? -1 : +1);
                    return;
                case "seek_percent": ResetZoom(); return;
            }
        }

        switch (name)
        {
            case "none": break;
            case "pause":
            case "play_pause":
                if (CurrentFile == null) { OpenDialog(); break; }
                _mpv.Command("cycle", "pause");
                o.FlashPlayPause(nowPaused: !Paused);
                break;
            case "seek":
                if (n == 0 || CurrentFile == null) break;
                _mpv.Command("seek", Num.Inv(n), "relative+exact");
                var target = Math.Clamp(TimePos + n, 0, Math.Max(0, Duration));
                o.Osd($"{(n > 0 ? "+" : "−")}{FormatSpan(Math.Abs(n))}   ·   {FormatTime(target)} / {FormatTime(Duration)}");
                break;
            case "seek_percent":
                _mpv.Command("seek", Num.Inv(Math.Clamp(n, 0, 100)), "absolute-percent+exact");
                o.Osd($"{n:0}%");
                break;
            case "volume":
                _mpv.Command("add", "volume", Num.Inv(n));
                var vol = Math.Clamp(Volume + n, 0, Math.Max(100, S.Playback.VolumeMax));
                if (Muted) _mpv.Set("mute", false);
                o.Osd($"Volume {vol:0}%");
                break;
            case "mute":
                _mpv.Command("cycle", "mute");
                o.Osd(Muted ? $"Volume {Volume:0}%" : "Muted");
                break;
            case "speed":
                var spd = Math.Clamp(Math.Round(Speed + n, 2), 0.1, 8);
                _mpv.Set("speed", spd);
                o.Osd($"Speed {spd:0.##}×");
                break;
            case "speed_reset":
                _mpv.Set("speed", 1.0);
                o.Osd("Speed 1×");
                break;
            case "autonext": ToggleAutoNext(); break;
            case "brightness": SetBrightness(Brightness + n); break;
            case "brightness_reset": SetBrightness(0); break;
            case "zoom": ZoomStep(n >= 0 ? 1 : -1); break;
            case "zoom_reset": ResetZoom(); break;
            case "fullscreen": ToggleFullscreen(); break;
            case "exit_fullscreen": if (IsFullscreen) ToggleFullscreen(); break;
            case "zen": o.ToggleZen(); break;
            case "frame_step": _mpv.Command("frame-step"); break;
            case "frame_back": _mpv.Command("frame-back-step"); break;
            case "next_sub": CycleTrack("sub", "Subtitles"); break;
            case "next_audio": CycleTrack("audio", "Audio"); break;
            case "sub_toggle":
                _mpv.CommandSync("cycle", "sub-visibility");
                o.Osd(_mpv.GetFlag("sub-visibility") == true ? "Subtitles shown" : "Subtitles hidden");
                break;
            case "sub_delay":
                _mpv.CommandSync("add", "sub-delay", Num.Inv(n));
                o.Osd($"Subtitle delay {(_mpv.GetDouble("sub-delay") ?? 0) * 1000:0} ms");
                break;
            case "loop":
                _mpv.CommandSync("cycle-values", "loop-file", "inf", "no");
                o.Osd(_mpv.GetString("loop-file") == "no" ? "Loop: off" : "Loop: on");
                break;
            case "screenshot":
                _mpv.Command("screenshot");
                o.Osd("Screenshot saved to Pictures\\DrewPlayer");
                break;
            case "info": ShowInfo(); break;
            case "open": OpenDialog(); break;
            case "new_tab": OpenDialog(newTabs: true); break;
            case "close_tab": if (_active >= 0) CloseTab(_active); break;
            case "next_tab": if (_tabs.Count > 1) SwitchTab((_active + 1) % _tabs.Count); break;
            case "prev_tab": if (_tabs.Count > 1) SwitchTab((_active - 1 + _tabs.Count) % _tabs.Count); break;
            case "next_file": Step(+1); break;
            case "prev_file": Step(-1); break;
            case "settings": OpenSettings(); break;
            case "minimize": WindowState = WindowState.Minimized; break;
            case "quit": Close(); break;
            case "mpv":
                if (_mpv.CommandString(arg) < 0) o.Osd("mpv command failed: " + arg);
                break;
            default:
                o.Osd($"Unknown action: \"{name}\"");
                break;
        }
    }

    /// <summary>Toggles auto-advance and saves it to the settings.</summary>
    public void ToggleAutoNext()
    {
        var s = S.Clone();
        s.Playback.AutoNext = !s.Playback.AutoNext;
        _store.Save(s); // applies immediately (to the settings window too, if open)
        _overlay?.Osd(s.Playback.AutoNext ? "End of file: play next" : "End of file: stop", 1500, force: true);
    }

    /// <summary>Video/photo brightness, from −100 to +100 (0 = original). Kept for the whole session.</summary>
    public double Brightness { get; private set; }

    public void SetBrightness(double v, bool osd = true)
    {
        if (_mpv == null) return;
        Brightness = Math.Clamp(Math.Round(v), -100, 100);
        _mpv.Set("brightness", Brightness);
        _overlay?.OnBrightnessChanged();
        if (osd) _overlay?.Osd(Brightness == 0 ? "Brightness reset" : $"Brightness {(Brightness > 0 ? "+" : "")}{Brightness:0}");
    }

    private void ZoomStep(int dir)
    {
        if (_mpv == null || CurrentFile == null) return;
        // video-zoom is log2: +0.25 = ×1.19
        double z = Math.Clamp(Zoom + dir * 0.25, -2, 4);
        _mpv.Set("video-zoom", z);
        if (z <= 0) { _mpv.Set("video-pan-x", 0.0); _mpv.Set("video-pan-y", 0.0); }
        _overlay?.Osd($"Zoom {Math.Pow(2, z) * 100:0}%");
    }

    private void ResetZoom()
    {
        if (_mpv == null) return;
        _mpv.Set("video-zoom", 0.0);
        _mpv.Set("video-pan-x", 0.0);
        _mpv.Set("video-pan-y", 0.0);
        _overlay?.Osd("Zoom 100%");
    }

    /// <summary>Pans the zoomed image. dx/dy in window pixels.</summary>
    public void Pan(double dxPx, double dyPx, double winW, double winH)
    {
        if (_mpv == null || Zoom <= 0) return;
        double f = Math.Pow(2, Zoom);
        double px = (_mpv.GetDouble("video-pan-x") ?? 0) + dxPx / (winW * f);
        double py = (_mpv.GetDouble("video-pan-y") ?? 0) + dyPx / (winH * f);
        _mpv.Set("video-pan-x", Math.Clamp(px, -1, 1));
        _mpv.Set("video-pan-y", Math.Clamp(py, -1, 1));
    }

    private void ShowInfo()
    {
        if (_mpv == null || CurrentFile == null) return;
        var w = _mpv.GetString("width"); var h = _mpv.GetString("height");
        var codec = _mpv.GetString("video-codec") ?? _mpv.GetString("audio-codec-name") ?? "?";
        var hw = _mpv.GetString("hwdec-current");
        var fps = _mpv.GetDouble("container-fps");
        var parts = new List<string>();
        if (w != null) parts.Add($"{w}×{h}");
        parts.Add(codec.Split(' ')[0]);
        if (fps > 0) parts.Add($"{fps:0.##} fps");
        if (!IsImage && !AudioOnly) parts.Add(string.IsNullOrEmpty(hw) || hw == "no" ? "CPU decoding" : $"GPU decoding ({hw})");
        _overlay?.Osd(string.Join("   ·   ", parts), 4000, force: true);
    }

    public void SeekAbsolute(double seconds, bool exact) =>
        _mpv?.Command("seek", Num.Inv(Math.Max(0, seconds)), exact ? "absolute+exact" : "absolute+keyframes");

    public void SetVolume(double v)
    {
        if (_mpv == null) return;
        _mpv.Set("volume", Math.Clamp(Math.Round(v), 0, Math.Max(100, S.Playback.VolumeMax)));
        if (Muted) _mpv.Set("mute", false);
    }

    private void CycleTrack(string type, string label)
    {
        _mpv!.CommandSync("cycle", type);
        var id = _mpv.GetString(type);
        if (id == null || id == "no") { _overlay!.Osd($"{label}: off"); return; }
        var lang = _mpv.GetString($"current-tracks/{type}/lang");
        var title = _mpv.GetString($"current-tracks/{type}/title");
        var desc = string.Join(" · ", new[] { lang, title }.Where(x => !string.IsNullOrWhiteSpace(x)));
        _overlay!.Osd($"{label}: {(desc.Length > 0 ? desc : "track " + id)}");
    }

    public void OpenSettings()
    {
        if (_settingsWin != null) { _settingsWin.Activate(); return; }
        _settingsWin = new SettingsWindow(_store) { Owner = _overlay };
        _settingsWin.Closed += (_, _) => { _settingsWin = null; _overlay?.Activate(); };
        _settingsWin.Show();
    }

    internal static void OpenJsonFile()
    {
        try { Process.Start(new ProcessStartInfo(SettingsStore.FilePath) { UseShellExecute = true }); }
        catch { Process.Start("notepad.exe", $"\"{SettingsStore.FilePath}\""); }
    }

    private string _lastIconColor = "";

    private void OnSettingsReloaded(bool fromUi)
    {
        App.ApplyAccent(S.Ui.AccentColor);
        var u = S.Ui;
        var ic = $"{App.IconColorOf(S)}|{u.FileIconVideo}|{u.FileIconAudio}|{u.FileIconImage}|{u.FileIconDownload}";
        if (ic != _lastIconColor) { _lastIconColor = ic; App.SyncIcon(S); }

        if (_mpv != null)
        {
            _mpv.Set("volume-max", Math.Max(100, S.Playback.VolumeMax));
            _mpv.Set("hwdec", S.Playback.Hwdec);
            _mpv.Set("sub-auto", S.Playback.SubAuto ? "fuzzy" : "no");
            _mpv.Set("sub-font-size", Math.Clamp(S.Playback.SubFontSize, 10, 120));
            _mpv.Set("resume-playback", S.Playback.Resume);
            ApplyQuality();
        }
        if (_hwnd != IntPtr.Zero && !IsFullscreen) Native.SetCorners(_hwnd, S.Window.RoundedCorners);
        _overlay?.ApplySettings();
        if (!fromUi)
            _overlay?.Osd(_store.LastError == null ? "Settings reloaded" : "Error in settings.json — " + _store.LastError,
                _store.LastError == null ? 1400 : 5000, force: true);
    }

}
