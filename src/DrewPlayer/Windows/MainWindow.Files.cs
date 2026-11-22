using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace DrewPlayer;

public partial class MainWindow
{
    public sealed class Tab
    {
        public string Path = "";
        public double Pos;
        public bool Paused;
        public bool Growing;
        public string Title => IOPath.GetFileName(Path);
    }

    private readonly List<Tab> _tabs = new();
    private int _active = -1;
    private Tab? ActiveTab => _active >= 0 && _active < _tabs.Count ? _tabs[_active] : null;
    public IReadOnlyList<Tab> Tabs => _tabs;
    public int ActiveIndex => _active;
    public bool IsDownloading => ActiveTab?.Growing == true;

    /// <summary>Opens in the current tab (or creates one if there are none).</summary>
    public void Open(string path)
    {
        if (_mpv == null) return;
        if (ActiveTab == null) { OpenInNewTab(path); return; }
        SaveResume();
        ActiveTab.Path = path;
        ActiveTab.Pos = 0;
        ActiveTab.Paused = false;
        Load(ActiveTab, null);
    }

    public void OpenInNewTab(string path)
    {
        if (_mpv == null) return;
        if (ActiveTab != null) { SaveResume(); StoreTabState(); }
        _tabs.Add(new Tab { Path = path });
        _active = _tabs.Count - 1;
        Load(ActiveTab!, null);
    }

    public void SwitchTab(int i)
    {
        if (i < 0 || i >= _tabs.Count || i == _active || _mpv == null) return;
        SaveResume();
        StoreTabState();
        _active = i;
        Load(_tabs[i], _tabs[i].Pos);
    }

    public void CloseTab(int i)
    {
        if (i < 0 || i >= _tabs.Count || _mpv == null) return;
        if (i == _active) SaveResume();
        _tabs.RemoveAt(i);
        if (_tabs.Count == 0)
        {
            _active = -1;
            CurrentFile = null;
            IsImage = false;
            _mpv.Command("stop");
            _overlay?.OnClosedAll();
            Title = "DrewPlayer";
        }
        else if (i == _active)
        {
            _active = Math.Min(i, _tabs.Count - 1);
            Load(_tabs[_active], _tabs[_active].Pos);
        }
        else if (i < _active) _active--;
        _overlay?.SetTabs();
    }

    private void StoreTabState()
    {
        if (ActiveTab == null) return;
        ActiveTab.Pos = IsImage ? 0 : TimePos;
        ActiveTab.Paused = Paused;
    }

    private bool _restoreStart;

    /// <summary>Loads the tab's file into mpv. start = resume from here (tab switch).</summary>
    private void Load(Tab t, double? start)
    {
        if (_mpv == null) return;
        var path = t.Path;
        CurrentFile = path;
        IsImage = App.IsImage(path);
        t.Growing = App.IsGrowing(path);
        _fileLoaded = false;
        _fitPending = true;
        _eofHandled = false;
        // fast name order now; the File Explorer order (a slow COM query) is fetched once playback runs
        BuildFolderList(path, askExplorer: false);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => { if (CurrentFile == path) BuildFolderList(path); });
        _overlay?.OnOpening(t.Title, IsImage);
        _overlay?.SetTabs();
        _overlay?.SetDownloading(t.Growing);

        bool gif = IOPath.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        _mpv.Set("loop-file", gif || S.Playback.LoopFile ? "inf" : "no");
        _mpv.Set("video-zoom", 0.0);
        _mpv.Set("video-pan-x", 0.0);
        _mpv.Set("video-pan-y", 0.0);
        if (start is > 0.5)
        {
            // tab position takes precedence over the saved resume point
            _mpv.Set("resume-playback", false);
            _mpv.Set("start", Num.Inv(start.Value));
            _restoreStart = true;
        }
        // file still downloading: appending:// keeps reading while the file grows
        Mark("loadfile");
        _mpv.Command("loadfile", t.Growing ? "appending://" + path : path, "replace");
        _mpv.Set("pause", t.Paused);
        Title = $"{t.Title} — DrewPlayer";
    }

    private void OnEndFile(int reason, int error)
    {
        if (reason != 4) return; // 4 = error
        Dispatcher.BeginInvoke(() =>
        {
            Reveal();
            if (ActiveTab?.Growing == true)
                _overlay?.Osd("Not playable yet: this file keeps its index at the end (common with MP4). Wait for the download to finish.", 6000, force: true);
            else
                _overlay?.Osd("Could not open the file", 3000, force: true);
        });
    }

    /// <summary>Playback caught up with the download: wait for more data, then resume.</summary>
    private void WaitForDownload()
    {
        var tab = ActiveTab;
        if (tab == null) return;
        double pos = TimePos;
        _overlay?.Osd("Waiting for download…", 2500, force: true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ActiveTab != tab) return;
            // Chrome/Firefox rename the file when the download completes
            if (!File.Exists(tab.Path) && App.IsPartial(tab.Path))
            {
                var final = tab.Path[..^IOPath.GetExtension(tab.Path).Length];
                if (File.Exists(final)) tab.Path = final;
                else return;
            }
            tab.Pos = pos;
            tab.Paused = false;
            Load(tab, pos);
        };
        timer.Start();
    }

    private void BuildFolderList(string path, bool askExplorer = true)
    {
        try
        {
            var dir = IOPath.GetDirectoryName(path);
            if (dir == null || !Directory.Exists(dir)) { _folder = [path]; _folderIndex = 0; return; }
            // photos with photos, video/audio with video/audio
            Func<string, bool> sameKind = App.IsImage(path) ? App.IsImage : App.IsAv;
            _folder = Directory.EnumerateFiles(dir).Where(sameKind).ToList();
            var mode = S.Playback.FolderOrder;
            FolderOrder.Sort(_folder, dir, mode == "explorer" && !askExplorer ? "name" : mode);
            _folderIndex = _folder.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            if (_folderIndex < 0) { _folder.Insert(0, path); _folderIndex = 0; }
        }
        catch { _folder = [path]; _folderIndex = 0; }
    }

    private void Step(int delta)
    {
        // re-read the order now: the File Explorer sorting may have changed meanwhile
        if (CurrentFile != null && !CurrentFile.Contains("://")) BuildFolderList(CurrentFile);
        if (_folderIndex < 0) return;
        int i = _folderIndex + delta;
        if (i < 0 || i >= _folder.Count)
        {
            _overlay?.Osd(delta > 0 ? "Last file in folder" : "First file in folder");
            return;
        }
        Open(_folder[i]);
    }

    /// <summary>Open file(s). newTabs = all in new tabs; otherwise the first replaces the current tab.</summary>
    private void OpenDialog(bool newTabs = false)
    {
        static string Pat(IEnumerable<string> e) => string.Join(";", e.Select(x => "*" + x));
        var dlg = new OpenFileDialog
        {
            Title = "Open file",
            Multiselect = true,
            Filter = $"All supported|{Pat(App.VideoExt.Concat(App.AudioExt).Concat(App.ImageExt).Concat(App.PartialExt))}" +
                     $"|Video|{Pat(App.VideoExt)}|Audio|{Pat(App.AudioExt)}|Photos|{Pat(App.ImageExt)}" +
                     $"|Downloading|{Pat(App.PartialExt)}|All files|*.*",
        };
        if (CurrentFile != null && !CurrentFile.Contains("://")) dlg.InitialDirectory = IOPath.GetDirectoryName(CurrentFile);
        if (dlg.ShowDialog(this) != true) return;
        OpenMany(dlg.FileNames, newTabs);
    }

    public void OpenMany(IList<string> files, bool newTabs)
    {
        for (int i = 0; i < files.Count; i++)
        {
            if (i == 0 && !newTabs) Open(files[i]);
            else OpenInNewTab(files[i]);
        }
    }

    /// <summary>From another instance (double-click on a file while DrewPlayer is running).</summary>
    public void OpenFromOutside(string path)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (ActiveTab == null) Open(path); else OpenInNewTab(path);
        Activate();
        _overlay?.Activate();
    }

    private void SaveResume()
    {
        if (_mpv == null || CurrentFile == null || IsImage || !S.Playback.Resume) return;
        bool nearEnd = Duration > 0 && TimePos > Duration - 5;
        _mpv.CommandSync(nearEnd ? "delete-watch-later-config" : "write-watch-later-config");
    }

}
