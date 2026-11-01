using System.Text;

namespace TestingApp.Models;

// The same checks the mod does when it loads or sets a value (cfg::Config::validateLocked), so the app never writes
// a value the mod would silently replace by its default.
public static class Rules
{
    public static string CleanName(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s)
            if (ch >= 0x20 && ch <= 0x7E && sb.Length < 24) sb.Append(ch);
        return sb.ToString().Trim(' ');
    }

    // bindOwner(canonicalKeyText, ownKey) -> name of another action that already uses this key, or null
    public static string Validate(SchemaDoc schema, string key, object value, Func<string, string, string?> bindOwner, out object normalized)
    {
        normalized = value;
        if (!schema.ByKey.TryGetValue(key, out var sp)) return "unknown setting '" + key + "'";
        if (key == "config-version") return "'config-version' is read-only";
        switch (sp.Kind)
        {
            case 'b':
                return value is bool ? "" : "'" + key + "' needs on or off";
            case 'i':
            case 'f':
            {
                if (value is not double v) return "'" + key + "' needs a number";
                if (sp.IsInt)
                {
                    if (Math.Abs(v - Math.Round(v)) > 1e-9) return "'" + key + "' needs a whole number";
                    v = Math.Round(v);
                }
                if (v < sp.Min || v > sp.Max) return "'" + key + "' must be between " + Fmt.Json(sp.Min) + " and " + Fmt.Json(sp.Max);
                normalized = v;
                return "";
            }
            default:
            {
                if (value is not string t) return "'" + key + "' needs text";
                if (!sp.IsBind)
                {
                    string clean = CleanName(t);
                    if (clean.Length == 0) return "needs a name (letters, digits, basic symbols)";
                    normalized = clean;
                    return "";
                }
                if (t.Length == 0) return "";   // unbound
                if (!KeyNames.TryNormalize(t, out var canon)) return "'" + t + "' is not a key the mod understands";
                normalized = canon;
                string? owner = bindOwner(canon, key);
                return owner == null ? "" : canon + " is already used by " + owner + " (free it first)";
            }
        }
    }
}
