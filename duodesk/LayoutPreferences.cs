namespace DuoDesk;

internal static class LayoutPreferences
{
    public static string Label(string? value, string fallback)
    {
        var cleaned = value?.Trim();
        return string.IsNullOrEmpty(cleaned) ? fallback : cleaned.Length <= 24 ? cleaned : cleaned[..24];
    }
    public static string Color(string? value, string fallback)
    {
        if (value is not { Length: 7 } || value[0] != '#' || !value[1..].All(Uri.IsHexDigit)) return fallback;
        return value.ToUpperInvariant();
    }
    public static double Ratio(double value) => double.IsFinite(value) ? Math.Clamp(value, .2, .8) : .5;
    public static bool IsDark(string? value, bool systemDark) => value == "dark" || (value == "system" && systemDark);
    public static double Zoom(double value) => double.IsFinite(value) ? Math.Clamp(value, .5, 2.0) : 1.0;
    public static string Layout(string? value) => value is "tabs" or "stack" or "split" ? value : "split";
}
