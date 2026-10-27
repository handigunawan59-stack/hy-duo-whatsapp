using DuoDesk;
using System.Text.Json;

var failures = 0;
void Check(bool pass, string name) { Console.WriteLine((pass ? "PASS " : "FAIL ") + name); if (!pass) failures++; }
var shortcuts = AdvancedOptions.DefaultShortcuts();
Check(AdvancedOptions.ValidShortcuts(shortcuts), "default shortcuts are valid and unique");
Check(!AdvancedOptions.ValidShortcuts([1, 1, 1, 1]), "duplicate or unmodified shortcuts are rejected");
var now = DateTimeOffset.UtcNow;
Check(!AdvancedOptions.UpdateDue(false, now.AddDays(-3), now), "automatic checks are disabled by default");
Check(!AdvancedOptions.UpdateDue(true, now.AddHours(-2), now), "automatic check is limited to once daily");
Check(AdvancedOptions.UpdateDue(true, now.AddDays(-2), now), "automatic check becomes due after a day");
var settings = new AppSettings { PersonalLabel = "Keluarga", BusinessLabel = "Usaha", PersonalDownloadFolder = @"C:\secret", AutoLockMinutes = 5 };
var json = AppearanceBackup.Export(settings);
using (var doc = JsonDocument.Parse(json)) {
    Check(doc.RootElement.TryGetProperty("PersonalLabel", out var label) && label.GetString() == "Keluarga", "appearance export includes account label");
    Check(!doc.RootElement.TryGetProperty("PersonalDownloadFolder", out _), "appearance export excludes download paths");
    Check(!doc.RootElement.TryGetProperty("AutoLockMinutes", out _), "appearance export excludes security settings");
}
AppearanceBackup.Import(settings, "{\"PersonalLabel\":\"Teman\",\"PersonalDownloadFolder\":\"C:\\\\bad\",\"AutoLockMinutes\":0}");
Check(settings.PersonalLabel == "Teman", "appearance import applies a supported label");
Check(settings.PersonalDownloadFolder == @"C:\secret" && settings.AutoLockMinutes == 5, "appearance import never alters local paths or security settings");
AppearanceBackup.Import(settings, "{\"PersonalZoom\":4,\"BusinessZoom\":0.1}");
Check(settings.PersonalZoom == 2 && settings.BusinessZoom == .5, "imported zoom values obey app bounds");
return failures == 0 ? 0 : 1;
