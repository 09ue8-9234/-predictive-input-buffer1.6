using System.Globalization;
using System.Text;

namespace TestingApp.Models;

public static class Fmt
{
    // "0.25", "4", "1.5" (no trailing zeros)
    public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ticks -> frames (1 frame = 4 ticks at 240 steps/s), e.g. 16 -> "4f", 1 -> "0.25f"
    public static string Frames(double ticks) => Num(ticks / 4.0) + "f";

    // same number layout the mod writes (whole numbers without a decimal point, otherwise up to 10 digits)
    public static string Json(double v)
    {
        if (Math.Floor(v) == v && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("G10", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string text, out double value)
    {
        text = text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    // "sim-budget-ms" -> "Sim budget ms"
    public static string Humanize(string key)
    {
        string s = key.Replace("bind.", "").Replace("toggle:", "").Replace('-', ' ').Replace('_', ' ');
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    // "sessionSaves" -> "Session saves"
    public static string HumanizeCamel(string key)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (i > 0 && char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }
}
