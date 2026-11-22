using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace DrewPlayer;

public partial class MainWindow
{
    private void OnMpvProperty(string name, object? v)
    {
        switch (name)
        {
            case "time-pos": Volatile.Write(ref _timePos, v is double t ? t : 0); break;
            case "duration": Volatile.Write(ref _duration, v is double d ? d : 0); break;
            case "pause": Paused = v is true; Dispatcher.BeginInvoke(() => _overlay?.OnPauseChanged()); break;
            case "mute": Muted = v is true; break;
            case "volume": if (v is double vol) Volatile.Write(ref _volume, vol); break;
            case "speed": if (v is double sp) Volatile.Write(ref _speed, sp); break;
            case "video-zoom": Volatile.Write(ref _zoom, v is double z ? z : 0); break;
            case "media-title":
                var title = v as string;
                Dispatcher.BeginInvoke(() => { if (!IsImage) _overlay?.SetTitle(title ?? IOPath.GetFileName(CurrentFile) ?? ""); });
                break;
            case "eof-reached":
                if (v is true) Dispatcher.BeginInvoke(OnEof);
                else _eofHandled = false;
                break;
        }
    }

    private void OnMpvEvent(MpvEventId id)
    {
        switch (id)
        {
            case MpvEventId.FileLoaded: Dispatcher.BeginInvoke(OnFileLoaded); break;
            case MpvEventId.VideoReconfig: Dispatcher.BeginInvoke(OnVideoReconfig); break;
            // first frame ready (after load/seek): the window can be shown now
            case MpvEventId.PlaybackRestart:
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { Mark("first frame (playback restart)"); if (_fileLoaded) Reveal(); });
                break;
        }
    }

    private void OnFileLoaded()
    {
        Mark("file loaded");
        if (_mpv == null) return;
        _fileLoaded = true;
        if (_restoreStart)
        {
            _restoreStart = false;
            _mpv.Set("start", "none");
            _mpv.Set("resume-playback", S.Playback.Resume);
        }
        bool hasVideo = _mpv.GetString("current-tracks/video/id") != null;
        bool albumArt = _mpv.GetFlag("current-tracks/video/albumart") == true;
        AudioOnly = !IsImage && (!hasVideo || albumArt);

        string? artist = _mpv.GetString("metadata/by-key/artist");
        _overlay?.OnFileLoaded(AudioOnly, hasVideo && albumArt, artist);

        if (AudioOnly && S.Window.AudioCompact && !IsFullscreen && !_userResized)
        {
            _fitPending = false;
            ResizeCentered(520, hasVideo ? 560 : 300, dips: true);
        }
    }

    private bool _fileLoaded;

    private void OnVideoReconfig()
    {
        if (!_fileLoaded) return; // spurious mpv signal (before the file loaded): ignore
        FitToVideo();
    }

    private void FitToVideo()
    {
        if (_mpv == null) return;
        if (IsImage)
        {
            var iw = _mpv.GetDouble("width");
            var ih = _mpv.GetDouble("height");
            if (iw > 0 && ih > 0)
                _overlay?.SetTitle($"{IOPath.GetFileName(CurrentFile)}     {iw:0} × {ih:0}");
        }
        if (!_fitPending || AudioOnly) return;
        _fitPending = false;
        if (!S.Window.Autofit || IsFullscreen || _userResized) return;

        var w = _mpv.GetDouble("dwidth");
        var h = _mpv.GetDouble("dheight");
        if (w is not > 0 || h is not > 0) return;

        var work = Native.MonitorOf(_hwnd).rcWork;
        double pct = Math.Clamp(S.Window.AutofitPercent, 20, 100) / 100.0;
        double maxW = work.Width * pct, maxH = work.Height * pct;
        // never larger than the available space: the file's resolution is untouched, only the window changes
        double scale = Math.Min(1.0, Math.Min(maxW / w.Value, maxH / h.Value));
        if (S.Window.UpscaleSmall && w.Value * scale < maxW * 0.5 && h.Value * scale < maxH * 0.5)
            scale = Math.Min(maxW * 0.5 / w.Value, maxH * 0.5 / h.Value);
        ResizeCentered((int)(w.Value * scale), (int)(h.Value * scale), dips: false);
    }

    private void OnEof()
    {
        if (_eofHandled || IsImage) return;
        _eofHandled = true;
        if (ActiveTab?.Growing == true && (File.Exists(ActiveTab.Path) && App.IsGrowing(ActiveTab.Path) || App.IsPartial(ActiveTab.Path)))
        {
            WaitForDownload();
            return;
        }
        if (S.Playback.AutoNext && _folderIndex >= 0 && _folderIndex < _folder.Count - 1)
            Step(+1);
    }

}
