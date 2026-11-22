using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace DrewPlayer;

/// <summary>
/// One DrewPlayer per user: later instances hand their files to the first one over a named pipe
/// and exit right away (the file opens as a new tab).
/// </summary>
internal static class SingleInstance
{
    private static readonly string Id = "DrewPlayer-" + Environment.UserName;
    private static Mutex? _mutex;

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int pid);

    /// <summary>true = another instance is running and got our files (so this one must exit).</summary>
    public static bool TryForward(IEnumerable<string> files)
    {
        _mutex = new Mutex(true, Id, out bool first);
        if (first) return false;

        try
        {
            AllowSetForegroundWindow(-1); // allow the other instance to come to the foreground
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            client.Connect(2000);
            using var w = new StreamWriter(client, new UTF8Encoding(false));
            var list = files.ToList();
            if (list.Count == 0) w.WriteLine("\u0001focus");
            foreach (var f in list) w.WriteLine(f);
            w.Flush();
            return true;
        }
        catch
        {
            return false; // instance stuck/not responding: open a window anyway
        }
    }

    /// <summary>Background server. onMessage is called on the server thread.</summary>
    public static void StartServer(Action<string> onMessage)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                    server.WaitForConnection();
                    using var r = new StreamReader(server, Encoding.UTF8);
                    string? line;
                    while ((line = r.ReadLine()) != null)
                        if (line.Length > 0) onMessage(line);
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        })
        { IsBackground = true, Name = "single-instance" };
        t.Start();
    }
}
