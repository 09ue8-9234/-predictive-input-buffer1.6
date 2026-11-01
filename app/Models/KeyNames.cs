using System.Windows.Input;

namespace TestingApp.Models;

// Key names exactly as the mod writes and accepts them (cfg::parseKey / cfg::keyName): "F8", "Shift+F6", "Ctrl+F1", "Alt+1".
public static class KeyNames
{
    static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "Space", ["tab"] = "Tab", ["enter"] = "Enter", ["esc"] = "Esc", ["backspace"] = "Backspace",
        ["insert"] = "Insert", ["delete"] = "Delete", ["home"] = "Home", ["end"] = "End", ["pageup"] = "PageUp",
        ["pagedown"] = "PageDown", ["left"] = "Left", ["up"] = "Up", ["right"] = "Right", ["down"] = "Down",
        ["pause"] = "Pause", ["scrolllock"] = "ScrollLock", ["backquote"] = "Backquote", ["minus"] = "Minus",
        ["equals"] = "Equals", ["comma"] = "Comma", ["period"] = "Period", ["slash"] = "Slash",
    };

    static string? KeyCanon(string lower)
    {
        if (lower.Length >= 2 && lower[0] == 'f' && lower.Skip(1).All(char.IsDigit))
        {
            int n = int.Parse(lower[1..]);
            return n >= 1 && n <= 24 ? "F" + n : null;
        }
        if (lower.Length == 1 && lower[0] >= 'a' && lower[0] <= 'z') return char.ToUpperInvariant(lower[0]).ToString();
        if (lower.Length == 1 && lower[0] >= '0' && lower[0] <= '9') return lower;
        if (lower.Length == 4 && lower.StartsWith("num", StringComparison.Ordinal) && char.IsDigit(lower[3])) return "Num" + lower[3];
        return Named.TryGetValue(lower, out var named) ? named : null;
    }

    // Normalizes a key text the way the mod would; false if the mod would refuse it.
    public static bool TryNormalize(string text, out string canon)
    {
        canon = "";
        bool ctrl = false, shift = false, alt = false;
        string? key = null;
        var parts = text.Replace(" ", "").Split('+');
        for (int i = 0; i < parts.Length; i++)
        {
            string l = parts[i].ToLowerInvariant();
            if (l == "ctrl" || l == "control") ctrl = true;
            else if (l == "shift") shift = true;
            else if (l == "alt") alt = true;
            else if (i == parts.Length - 1 && l.Length > 0 && KeyCanon(l) is { } k) key = k;
            else return false;
        }
        if (key == null) return false;
        canon = (ctrl ? "Ctrl+" : "") + (shift ? "Shift+" : "") + (alt ? "Alt+" : "") + key;
        return true;
    }

    public static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    // WPF key + modifiers -> the mod's key text (null = a key the mod can't bind)
    public static string? FromWpf(Key k, ModifierKeys mods)
    {
        string? name = k switch
        {
            >= Key.F1 and <= Key.F24 => "F" + ((int)k - (int)Key.F1 + 1),
            >= Key.A and <= Key.Z => ((char)('A' + ((int)k - (int)Key.A))).ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + ((int)k - (int)Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + ((int)k - (int)Key.NumPad0),
            Key.Space => "Space", Key.Tab => "Tab", Key.Return => "Enter", Key.Escape => "Esc", Key.Back => "Backspace",
            Key.Insert => "Insert", Key.Delete => "Delete", Key.Home => "Home", Key.End => "End",
            Key.Prior => "PageUp", Key.Next => "PageDown", Key.Left => "Left", Key.Up => "Up", Key.Right => "Right", Key.Down => "Down",
            Key.Pause => "Pause", Key.Scroll => "ScrollLock", Key.OemTilde => "Backquote", Key.OemMinus => "Minus",
            Key.OemPlus => "Equals", Key.OemComma => "Comma", Key.OemPeriod => "Period", Key.OemQuestion => "Slash",
            _ => null,
        };
        if (name == null) return null;
        return ((mods & ModifierKeys.Control) != 0 ? "Ctrl+" : "") + ((mods & ModifierKeys.Shift) != 0 ? "Shift+" : "") +
               ((mods & ModifierKeys.Alt) != 0 ? "Alt+" : "") + name;
    }
}
