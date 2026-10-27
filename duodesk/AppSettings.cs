using System.Text.Json;

namespace DuoDesk;

internal sealed class AppSettings
{
    public string PersonalDownloadFolder { get; set; } = "";
    public string BusinessDownloadFolder { get; set; } = "";
    public string LayoutMode { get; set; } = "split";
    public string ActiveAccount { get; set; } = "personal";
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
