#if DEBUG
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>
/// Debug builds only. DREWPLAYER_BENCH=folder plays the given file in a hidden (cloaked, never focused)
/// window, then writes a README screenshot (player.png) and a few performance numbers (bench.txt) and exits.
/// </summary>
public partial class MainWindow
{
    internal static string? BenchDir => Environment.GetEnvironmentVariable("DREWPLAYER_BENCH");
    internal static bool BenchMode => !string.IsNullOrEmpty(BenchDir);

    private static readonly List<string> Marks = new();

    static partial void MarkImpl(string what)
    {
        if (!BenchMode) return;
        var ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        lock (Marks) Marks.Add($"  {ms,6:0} ms  {what}");
    }

    private async void RunBench()
    {
        var dir = BenchDir!;
        Directory.CreateDirectory(dir);

        if (IsImage && _overlay != null)
        {
            // photo editor screenshots
            await Task.Delay(800);
            foreach (var crop in new[] { false, true })
            {
                _overlay.DebugEditor(CurrentFile!, crop);
                await Task.Delay(700);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(_overlay.DebugRender()));
                using (var fs = File.Create(IOPath.Combine(dir, crop ? "editor-crop.png" : "editor.png"))) png.Save(fs);
            }
            Close();
            return;
        }
        var proc = Process.GetCurrentProcess();
        var firstFrame = DateTime.Now - proc.StartTime;

        // let it play, then sample CPU and memory over a few seconds
        await Task.Delay(4000);
        proc.Refresh();
        var cpu0 = proc.TotalProcessorTime;
        var t0 = Stopwatch.StartNew();
        await Task.Delay(5000);
        proc.Refresh();
        double cpu = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / t0.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        string hw = _mpv?.GetString("hwdec-current") ?? "?";
        string codec = _mpv?.GetString("video-codec") ?? "?";
        string size = $"{_mpv?.GetString("width")}x{_mpv?.GetString("height")}";

        File.WriteAllText(IOPath.Combine(dir, "bench.txt"),
            $"file: {IOPath.GetFileName(CurrentFile)} ({size}, {codec})\n" +
            $"hwdec: {hw}\n" +
            $"time to first frame (process start → first frame, splash off): {firstFrame.TotalMilliseconds:0} ms\n" +
            $"cpu while playing: {cpu:0.0}% of total ({Environment.ProcessorCount} logical cores)\n" +
            $"working set: {proc.WorkingSet64 / 1048576.0:0} MB\n" +
            $"private memory: {proc.PrivateMemorySize64 / 1048576.0:0} MB\n" +
            "startup timeline:\n" + string.Join("\n", Marks) + "\n");

        // screenshot: paused, controls visible, video frame + UI composited
        _mpv?.Set("pause", true);
        _overlay?.DebugShowControls();
        await Task.Delay(800);
        var framePath = IOPath.Combine(dir, "frame.png");
        _mpv?.CommandSync("screenshot-to-file", framePath, "video");
        if (_overlay != null && File.Exists(framePath)) SaveComposite(framePath, IOPath.Combine(dir, "player.png"));
        Close();
    }

    private void SaveComposite(string framePath, string outPath)
    {
        var ui = _overlay!.DebugRender();
        var frame = new BitmapImage();
        frame.BeginInit();
        frame.CacheOption = BitmapCacheOption.OnLoad;
        frame.UriSource = new Uri(framePath);
        frame.EndInit();

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var r = new Rect(0, 0, ui.PixelWidth, ui.PixelHeight);
            dc.DrawRectangle(Brushes.Black, null, r);
            // video letterboxed like mpv does
            double s = Math.Min(r.Width / frame.PixelWidth, r.Height / frame.PixelHeight);
            double w = frame.PixelWidth * s, h = frame.PixelHeight * s;
            dc.DrawImage(frame, new Rect((r.Width - w) / 2, (r.Height - h) / 2, w, h));
            dc.DrawImage(ui, r);
        }
        var rtb = new RenderTargetBitmap(ui.PixelWidth, ui.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(outPath);
        enc.Save(fs);
    }
}
#endif
