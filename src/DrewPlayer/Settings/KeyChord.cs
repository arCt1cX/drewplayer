using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace DrewPlayer;

/// <summary>"Ctrl+Shift+Right" ↔ (Key, ModifierKeys).</summary>
public static class KeyChord
{
    private static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = Key.Escape, ["Enter"] = Key.Return, ["Backspace"] = Key.Back,
        ["Plus"] = Key.OemPlus, ["+"] = Key.OemPlus, ["Minus"] = Key.OemMinus, ["-"] = Key.OemMinus,
        ["Comma"] = Key.OemComma, [","] = Key.OemComma, ["Period"] = Key.OemPeriod, ["."] = Key.OemPeriod,
        ["PgUp"] = Key.PageUp, ["PgDn"] = Key.PageDown, ["PageUp"] = Key.PageUp, ["PageDown"] = Key.PageDown,
        ["Del"] = Key.Delete, ["Ins"] = Key.Insert,
    };

    public static bool TryParse(string text, out Key key, out ModifierKeys mods)
    {
        key = Key.None;
        mods = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (text.EndsWith("++")) parts = [.. text[..^2].Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), "+"];

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (i < parts.Length - 1)
            {
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                    case "shift": mods |= ModifierKeys.Shift; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "win": mods |= ModifierKeys.Windows; break;
                    default: return false;
                }
                continue;
            }
            if (Aliases.TryGetValue(p, out key)) return true;
            if (p.Length == 1 && char.IsDigit(p[0])) return Enum.TryParse("D" + p, out key);
            return Enum.TryParse(p, true, out key) && key != Key.None;
        }
        return false;
    }

    public static string ToText(Key key, ModifierKeys mods)
    {
        var sb = new System.Text.StringBuilder();
        if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        sb.Append(key switch
        {
            Key.OemPlus => "Plus", Key.OemMinus => "Minus", Key.OemComma => "Comma", Key.OemPeriod => "Period",
            Key.Return => "Enter", Key.PageUp => "PageUp", Key.PageDown => "PageDown", Key.Back => "Back",
            _ => key.ToString(),
        });
        return sb.ToString();
    }

    /// <summary>Human-readable form: "Shift + →".</summary>
    public static string Pretty(string chord)
    {
        if (!TryParse(chord, out var key, out var mods)) return chord;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(key switch
        {
            Key.Right => "→", Key.Left => "←", Key.Up => "↑", Key.Down => "↓",
            Key.Space => "Space", Key.Return => "Enter", Key.Escape => "Esc", Key.Back => "Backspace",
            Key.OemPlus => "+", Key.OemMinus => "−", Key.OemComma => ",", Key.OemPeriod => ".",
            Key.PageUp => "Page ↑", Key.PageDown => "Page ↓", Key.Delete => "Del", Key.Tab => "Tab",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(key - Key.NumPad0),
            _ => key.ToString(),
        });
        return string.Join(" + ", parts);
    }
}
