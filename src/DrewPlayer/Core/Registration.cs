using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DrewPlayer;

/// <summary>
/// File associations for the current user (HKCU, no admin rights).
/// Windows 11 does not let an app make itself the default:
/// we register here, then open the Settings page where the user confirms.
/// </summary>
internal static class Registration
{
    private const string Classes = @"Software\Classes";
    private const string CapPath = @"Software\DrewPlayer\Capabilities";
    private const string ProgVideo = "DrewPlayer.Video", ProgAudio = "DrewPlayer.Audio", ProgImage = "DrewPlayer.Image",
        ProgDownload = "DrewPlayer.Download";
    private static readonly string[] AllProgs = [ProgVideo, ProgAudio, ProgImage, ProgDownload];

    public static string ProgFor(IconKind k) => k switch
    {
        IconKind.Video => ProgVideo, IconKind.Audio => ProgAudio, IconKind.Image => ProgImage, _ => ProgDownload,
    };

    /// <summary>Icon for a file type. Returns true if something changed.</summary>
    public static bool SetTypeIcon(IconKind kind, string iconPath)
    {
        if (!IsRegistered) return false;
        using var k = Registry.CurrentUser.CreateSubKey($@"{Classes}\{ProgFor(kind)}\DefaultIcon");
        if (k.GetValue("") as string == iconPath) return false;
        k.SetValue("", iconPath);
        return true;
    }

    /// <summary>Registered by an older version (without the Download type)?</summary>
    public static bool NeedsUpgrade
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Classes}\{ProgDownload}\shell\open\command");
            return IsRegistered && k == null;
        }
    }

    /// <summary>Makes File Explorer redraw its icons.</summary>
    public static void RefreshExplorer()
    {
        NotifyShell();
        try { Process.Start(new ProcessStartInfo("ie4uinit.exe", "-show") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
    }

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);
    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    public static bool IsRegistered
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
            return k?.GetValue("DrewPlayer") != null;
        }
    }

    /// <summary>Path of the currently registered exe (null if not registered).</summary>
    public static string? RegisteredExe
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Classes}\{ProgVideo}\shell\open\command");
            var cmd = k?.GetValue("") as string;
            if (cmd == null || !cmd.StartsWith('"')) return null;
            return cmd[1..cmd.IndexOf('"', 1)];
        }
    }

    public static void Register(string exe, string iconRef)
    {
        var cmd = $"\"{exe}\" \"%1\"";
        var cu = Registry.CurrentUser;

        foreach (var (id, name) in new[] { (ProgVideo, "Video (DrewPlayer)"), (ProgAudio, "Audio (DrewPlayer)"), (ProgImage, "Photo (DrewPlayer)"), (ProgDownload, "Downloading file (DrewPlayer)") })
        {
            using var p = cu.CreateSubKey($@"{Classes}\{id}");
            p.SetValue("", name);
            // per-type DefaultIcon: set by App.SyncIcon (video/audio/photo/download have different icons)
            using (var o = p.CreateSubKey(@"shell\open")) o.SetValue("FriendlyAppName", "DrewPlayer");
            using (var c = p.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
        }

        using (var app = cu.CreateSubKey($@"{Classes}\Applications\DrewPlayer.exe"))
        {
            app.SetValue("FriendlyAppName", "DrewPlayer");
            using (var i = app.CreateSubKey("DefaultIcon")) i.SetValue("", iconRef);
            using (var c = app.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
        }

        using (var cap = cu.CreateSubKey(CapPath))
        {
            cap.SetValue("ApplicationName", "DrewPlayer");
            cap.SetValue("ApplicationDescription", "Lightweight player for video, audio and photos");
            cap.SetValue("ApplicationIcon", iconRef);
        }

        void Assoc(IEnumerable<string> exts, string prog)
        {
            using var fa = cu.CreateSubKey($@"{CapPath}\FileAssociations");
            using var st = cu.CreateSubKey($@"{Classes}\Applications\DrewPlayer.exe\SupportedTypes");
            foreach (var e in exts)
            {
                fa.SetValue(e, prog);
                st.SetValue(e, "");
                using var ow = cu.CreateSubKey($@"{Classes}\{e}\OpenWithProgids");
                ow.SetValue(prog, Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        Assoc(App.VideoExt, ProgVideo);
        Assoc(App.AudioExt, ProgAudio);
        Assoc(App.ImageExt, ProgImage);
        // files still downloading (.crdownload, .part...): double-click = watch them while they download
        Assoc(App.PartialExt, ProgDownload);
        // older versions mapped downloads to the Video type
        foreach (var e in App.PartialExt)
        {
            using var ow = cu.OpenSubKey($@"{Classes}\{e}\OpenWithProgids", writable: true);
            ow?.DeleteValue(ProgVideo, false);
        }

        using (var ra = cu.CreateSubKey(@"Software\RegisteredApplications"))
            ra.SetValue("DrewPlayer", CapPath);

        NotifyShell();
    }

    public static void Unregister()
    {
        var cu = Registry.CurrentUser;
        foreach (var e in App.VideoExt.Concat(App.AudioExt).Concat(App.ImageExt).Concat(App.PartialExt))
        {
            using var ow = cu.OpenSubKey($@"{Classes}\{e}\OpenWithProgids", writable: true);
            if (ow == null) continue;
            foreach (var p in AllProgs) ow.DeleteValue(p, false);
        }
        foreach (var p in AllProgs.Append(@"Applications\DrewPlayer.exe"))
            cu.DeleteSubKeyTree($@"{Classes}\{p}", false);
        cu.DeleteSubKeyTree(@"Software\DrewPlayer", false);
        using (var ra = cu.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            ra?.DeleteValue("DrewPlayer", false);
        NotifyShell();
    }

    /// <summary>Updates the app icon (Open with, Default apps) after a color change.</summary>
    public static void SetIcon(string iconRef)
    {
        if (!IsRegistered) return;
        var cu = Registry.CurrentUser;
        using (var k = cu.CreateSubKey($@"{Classes}\Applications\DrewPlayer.exe\DefaultIcon")) k.SetValue("", iconRef);
        using (var cap = cu.CreateSubKey(CapPath)) cap.SetValue("ApplicationIcon", iconRef);
        NotifyShell();
    }

    /// <summary>Updates the Start menu shortcut icon, if present.</summary>
    public static void SetShortcutIcon(string iconPath)
    {
        try
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DrewPlayer.lnk");
            if (!File.Exists(lnk)) return;
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic sh = Activator.CreateInstance(t)!;
            dynamic sc = sh.CreateShortcut(lnk);
            sc.IconLocation = iconPath + ",0";
            sc.Save();
        }
        catch { }
    }

    public static void OpenDefaultAppsPage()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=DrewPlayer") { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
    }
}
