namespace DrewPlayer;

/// <summary>
/// Order of the files in a folder, for "previous/next".
/// "explorer" = same sorting as the File Explorer window open on that folder
/// (name, date, size, type... ascending or descending); if none is open, sort by name.
/// </summary>
internal static class FolderOrder
{
    public static readonly (string id, string label)[] Modes =
    [
        ("explorer", "Same as File Explorer"),
        ("name", "Name"),
        ("date_new", "Date modified (newest first)"),
        ("date_old", "Date modified (oldest first)"),
        ("size", "Size (largest first)"),
    ];

    public static void Sort(List<string> files, string dir, string mode)
    {
        var (prop, desc) = mode switch
        {
            "name" => ("System.ItemNameDisplay", false),
            "date_new" => ("System.DateModified", true),
            "date_old" => ("System.DateModified", false),
            "size" => ("System.Size", true),
            _ => ExplorerSort(dir) ?? ("System.ItemNameDisplay", false),
        };
        Comparison<string> cmp = Comparer(prop);
        files.Sort(desc ? (a, b) => cmp(b, a) : cmp);
    }

    private static Comparison<string> Comparer(string prop)
    {
        static int ByName(string a, string b) => Native.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
        Comparison<string> then(Func<string, IComparable> key) => (a, b) =>
        {
            int c = key(a).CompareTo(key(b));
            return c != 0 ? c : ByName(a, b);
        };
        return prop switch
        {
            "System.DateModified" or "System.Media.DateEncoded" or "System.Photo.DateTaken" or "System.ItemDate"
                => then(f => File.GetLastWriteTimeUtc(f)),
            "System.DateCreated" => then(f => File.GetCreationTimeUtc(f)),
            "System.DateAccessed" => then(f => File.GetLastAccessTimeUtc(f)),
            "System.Size" => then(f => new FileInfo(f).Length),
            "System.ItemTypeText" or "System.ItemType" or "System.FileExtension"
                => then(f => Path.GetExtension(f).ToLowerInvariant()),
            _ => ByName,
        };
    }

    /// <summary>
    /// Sorting of the File Explorer window open on this folder, e.g. "prop:-System.DateModified;".
    /// null if no window shows the folder.
    /// </summary>
    private static (string prop, bool desc)? ExplorerSort(string dir)
    {
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return null;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic windows = shell.Windows();
            int n = windows.Count;
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            for (int i = 0; i < n; i++)
            {
                try
                {
                    dynamic? w = windows.Item(i);
                    if (w == null) continue;
                    string full = w.FullName ?? "";
                    if (!full.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    dynamic doc = w.Document;
                    string path = doc.Folder.Self.Path;
                    if (!string.Equals(Path.TrimEndingDirectorySeparator(path), target, StringComparison.OrdinalIgnoreCase)) continue;
                    string sort = doc.SortColumns ?? "";
                    // first column: "prop:-System.DateModified;prop:System.ItemNameDisplay;"
                    var first = sort.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    if (first.StartsWith("prop:")) first = first[5..];
                    bool desc = first.StartsWith('-');
                    return (first.TrimStart('-', '+'), desc);
                }
                catch { /* window closed in the meantime, etc. */ }
            }
        }
        catch { }
        return null;
    }
}
