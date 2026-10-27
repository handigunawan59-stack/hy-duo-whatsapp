using System.Text.Json;

namespace DuoDesk;

internal sealed class AppSettings
{
    public string PersonalDownloadFolder { get; set; } = "";
    public string BusinessDownloadFolder { get; set; } = "";
    public string LayoutMode { get; set; } = "split";
    public string ActiveAccount { get; set; } = "personal";
    public string PersonalLabel { get; set; } = "Pribadi";
    public string BusinessLabel { get; set; } = "Usaha";
    public string PersonalColor { get; set; } = "#7D79D8";
    public string BusinessColor { get; set; } = "#27AE75";
    public string Theme { get; set; } = "system";
    public double SplitRatio { get; set; } = 0.5;
    public double PersonalZoom { get; set; } = 1.0;
    public double BusinessZoom { get; set; } = 1.0;
    public string PersonalSound { get; set; } = "Asterisk";
    public string BusinessSound { get; set; } = "Asterisk";
    public bool HidePreviewWhenLocked { get; set; }
    public int AutoLockMinutes { get; set; }
}

internal static class SettingsStore
{
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Settings path requires a parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
