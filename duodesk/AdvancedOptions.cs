using System.Text.Json;

namespace DuoDesk;

internal sealed class AdvancedSettings
{
    public int[] Shortcuts { get; set; } = AdvancedOptions.DefaultShortcuts();
    public bool CheckUpdatesAutomatically { get; set; }
    public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.MinValue;
}

internal static class AdvancedOptions
{
    internal static int[] DefaultShortcuts() => [(int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.W), (int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.D1), (int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.D2), (int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.D3)];
    internal static bool ValidShortcuts(int[]? keys) => keys is { Length: 4 } && keys.Distinct().Count() == 4 && keys.All(IsValidShortcut);
    internal static bool IsValidShortcut(int key)
    {
        var value = (Keys)key;
        var keyCode = value & Keys.KeyCode;
        var modifiers = value & Keys.Modifiers;
        return (modifiers & (Keys.Control | Keys.Alt)) != 0 && keyCode >= Keys.A && keyCode <= Keys.Z ||
            (modifiers & (Keys.Control | Keys.Alt)) != 0 && keyCode >= Keys.D0 && keyCode <= Keys.D9 ||
            (modifiers & (Keys.Control | Keys.Alt)) != 0 && keyCode >= Keys.F1 && keyCode <= Keys.F12;
    }
    internal static bool UpdateDue(bool enabled, DateTimeOffset lastChecked, DateTimeOffset now) => enabled && (lastChecked == DateTimeOffset.MinValue || now - lastChecked >= TimeSpan.FromDays(1));
    internal static AdvancedSettings Load(string path)
    {
        try
        {
            var value = File.Exists(path) ? JsonSerializer.Deserialize<AdvancedSettings>(File.ReadAllText(path)) : null;
            value ??= new AdvancedSettings();
            if (!ValidShortcuts(value.Shortcuts)) value.Shortcuts = DefaultShortcuts();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal static void Save(string path, AdvancedSettings settings)
    {
        if (!ValidShortcuts(settings.Shortcuts)) throw new ArgumentException("Pintasan tidak valid.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(settings)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal static class AppearanceBackup
{
    internal static string Export(AppSettings settings) => JsonSerializer.Serialize(new {
        Version = 1, settings.PersonalLabel, settings.BusinessLabel, settings.PersonalColor,
        settings.BusinessColor, settings.Theme, settings.LayoutMode, settings.SplitRatio, settings.PersonalZoom, settings.BusinessZoom
    }, new JsonSerializerOptions { WriteIndented = true });
    internal static void Import(AppSettings settings, string json)
    {
        if (json.Length > 32768) throw new ArgumentException("File pengaturan terlalu besar.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Format pengaturan tidak valid.");
        static string? ReadString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var label = ReadString(root, "PersonalLabel"); if (label is not null) settings.PersonalLabel = LayoutPreferences.Label(label, "Pribadi");
        label = ReadString(root, "BusinessLabel"); if (label is not null) settings.BusinessLabel = LayoutPreferences.Label(label, "Usaha");
        var color = ReadString(root, "PersonalColor"); if (color is not null) settings.PersonalColor = LayoutPreferences.Color(color, "#7D79D8");
        color = ReadString(root, "BusinessColor"); if (color is not null) settings.BusinessColor = LayoutPreferences.Color(color, "#27AE75");
        var theme = ReadString(root, "Theme"); if (theme is "system" or "light" or "dark") settings.Theme = theme;
        var mode = ReadString(root, "LayoutMode"); if (mode is "split" or "tabs" or "stack") settings.LayoutMode = mode;
        if (root.TryGetProperty("SplitRatio", out var ratio) && ratio.ValueKind == JsonValueKind.Number && ratio.TryGetDouble(out var r)) settings.SplitRatio = LayoutPreferences.Ratio(r);
        if (root.TryGetProperty("PersonalZoom", out var pz) && pz.ValueKind == JsonValueKind.Number && pz.TryGetDouble(out var p) && double.IsFinite(p)) settings.PersonalZoom = LayoutPreferences.Zoom(p);
        if (root.TryGetProperty("BusinessZoom", out var bz) && bz.ValueKind == JsonValueKind.Number && bz.TryGetDouble(out var b) && double.IsFinite(b)) settings.BusinessZoom = LayoutPreferences.Zoom(b);
    }
}
