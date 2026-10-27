using DuoDesk;

var failures = new List<string>();
void Check(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

var idleType = System.Reflection.Assembly.GetExecutingAssembly().GetType("DuoDesk.IdleMonitor");
var idleCheck = idleType?.GetMethod("ShouldLock", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
Check(idleCheck is not null, "Idle lock decision is available.");
if (idleCheck is not null)
{
    bool ShouldLock(bool pin, bool locked, int timeout, uint idle) => (bool)idleCheck.Invoke(null, new object[] { pin, locked, timeout, idle })!;
    Check(!ShouldLock(false, false, 5, 300000), "Idle lock requires a configured PIN.");
    Check(!ShouldLock(true, false, 0, 300000), "Idle lock may be disabled.");
    Check(!ShouldLock(true, true, 5, 300000), "Idle lock does not repeat after already locked.");
    Check(!ShouldLock(true, false, 5, 299999), "Idle lock waits for the threshold.");
    Check(ShouldLock(true, false, 5, 300000), "Idle lock starts at the configured threshold.");
}

var root = Path.Combine(Path.GetTempPath(), "duodesk-path-test-root");
Check(LayoutPreferences.Label("  Keluarga  ", "Pribadi") == "Keluarga", "Account labels are trimmed.");
Check(LayoutPreferences.Label("  ", "Pribadi") == "Pribadi", "Empty account labels use a safe default.");
Check(LayoutPreferences.Color("#aa11bb", "#000000") == "#AA11BB", "Valid account colors are normalized.");
Check(LayoutPreferences.Color("not-a-color", "#000000") == "#000000", "Invalid account colors use the default.");
Check(Math.Abs(LayoutPreferences.Ratio(.68) - .68) < .001, "Valid split positions are retained.");
Check(LayoutPreferences.Ratio(3) == .8 && LayoutPreferences.Ratio(-1) == .2, "Split positions remain usable.");
Check(LayoutPreferences.IsDark("dark", false), "Dark theme is selected explicitly.");
Check(LayoutPreferences.IsDark("system", true), "System theme follows Windows.");
Check(!LayoutPreferences.IsDark("light", true), "Light theme overrides Windows.");
Check(LayoutPreferences.Layout("stack") == "stack", "Top-bottom is a recognized layout.");
Check(LayoutPreferences.Layout("broken") == "split", "Invalid layouts fall back to split.");
Check(LayoutPreferences.Zoom(1.25) == 1.25, "Valid zoom is retained.");
Check(LayoutPreferences.Zoom(.1) == .5 && LayoutPreferences.Zoom(4) == 2.0, "Zoom remains within supported bounds.");
Check(LayoutPreferences.Zoom(double.NaN) == 1.0, "Invalid zoom uses default.");
Check(ConnectionAdvice.ForError("Disconnected").Contains("Jaringan", StringComparison.OrdinalIgnoreCase), "Disconnected status mentions the network.");
Check(ConnectionAdvice.ForError("Timeout").Contains("waktu", StringComparison.OrdinalIgnoreCase), "Timeout status explains what happened.");
Check(!string.IsNullOrWhiteSpace(ConnectionAdvice.ForError("UnexpectedStatus")), "Unknown errors have a safe message.");

// Independent zoom factors and vertical layout must survive the existing settings store.
var layoutPath = Path.Combine(Path.GetTempPath(), "hy-layout-" + Guid.NewGuid().ToString("N") + ".json");
File.WriteAllText(layoutPath, "{\"LayoutMode\":\"stack\",\"PersonalZoom\":1.25,\"BusinessZoom\":0.8}");
SettingsStore.Save(layoutPath, SettingsStore.Load(layoutPath));
using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(layoutPath)))
{
    Check(doc.RootElement.GetProperty("LayoutMode").GetString() == "stack", "Top-bottom layout must persist.");
    Check(doc.RootElement.TryGetProperty("PersonalZoom", out var pz) && Math.Abs(pz.GetDouble() - 1.25) < .001, "Personal zoom must persist.");
    Check(doc.RootElement.TryGetProperty("BusinessZoom", out var bz) && Math.Abs(bz.GetDouble() - .8) < .001, "Business zoom must persist separately.");
}
File.Delete(layoutPath);
var personal = AppPaths.ProfileDirectory(root, "personal");
var business = AppPaths.ProfileDirectory(root, "business");
Check(personal != business, "Account sessions must use distinct profile directories.");
Check(personal == Path.Combine(root, "profiles", "personal"), "Personal session must stay inside its dedicated profile directory.");
Check(business == Path.Combine(root, "profiles", "business"), "Business session must stay inside its dedicated profile directory.");
try { AppPaths.ProfileDirectory(root, "../escape"); failures.Add("Unexpected profile identifier should be rejected."); }
catch (ArgumentException) { }

Check(AppPaths.TryGetTestPage("http://127.0.0.1:42301/", out _), "The local HTTP test fixture must be accepted.");
Check(!AppPaths.TryGetTestPage("https://example.com/", out _), "Test mode must not accept remote sites.");
Check(!AppPaths.TryGetTestPage("file:///C:/private.txt", out _), "Test mode must not accept local files.");
Check(!AppPaths.TryGetTestPage("http://127.0.0.1.example.com/", out _), "A hostname resembling loopback must not be accepted.");

var settingsRoot = Path.Combine(Path.GetTempPath(), "duodesk-settings-" + Guid.NewGuid().ToString("N"));
var settingsPath = Path.Combine(settingsRoot, "settings.json");
var original = new AppSettings { PersonalDownloadFolder = "C:\\PersonalFiles", BusinessDownloadFolder = "C:\\BusinessFiles", LayoutMode = "tabs", ActiveAccount = "business" };
SettingsStore.Save(settingsPath, original);
var restored = SettingsStore.Load(settingsPath);
Check(restored.PersonalDownloadFolder == "C:\\PersonalFiles", "Personal download folder must survive restart.");
Check(restored.BusinessDownloadFolder == "C:\\BusinessFiles", "Business download folder must survive restart independently.");
Check(restored.LayoutMode == "tabs" && restored.ActiveAccount == "business", "Display mode and chosen account must survive restart.");

// New preferences must round-trip through the same settings file used by real accounts.
File.WriteAllText(settingsPath, "{\"PersonalLabel\":\"Keluarga\",\"BusinessLabel\":\"Toko\",\"PersonalColor\":\"#AABBCC\",\"BusinessColor\":\"#112233\",\"Theme\":\"dark\",\"SplitRatio\":0.63,\"PersonalSound\":\"Asterisk\",\"BusinessSound\":\"Exclamation\",\"HidePreviewWhenLocked\":true,\"AutoLockMinutes\":5}");
SettingsStore.Save(settingsPath, SettingsStore.Load(settingsPath));
using (var preferences = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath)))
{
    var p = preferences.RootElement;
    Check(p.TryGetProperty("PersonalLabel", out var personalLabel) && personalLabel.GetString() == "Keluarga", "Personal account label must persist.");
    Check(p.TryGetProperty("BusinessLabel", out var businessLabel) && businessLabel.GetString() == "Toko", "Business account label must persist.");
    Check(p.TryGetProperty("PersonalColor", out var personalColor) && personalColor.GetString() == "#AABBCC", "Personal color must persist.");
    Check(p.TryGetProperty("BusinessColor", out var businessColor) && businessColor.GetString() == "#112233", "Business color must persist.");
    Check(p.TryGetProperty("Theme", out var theme) && theme.GetString() == "dark", "Theme must persist.");
    Check(p.TryGetProperty("SplitRatio", out var ratio) && Math.Abs(ratio.GetDouble() - .63) < .001, "Split position must persist.");
    Check(p.TryGetProperty("BusinessSound", out var sound) && sound.GetString() == "Exclamation", "Account sound must persist.");
    Check(p.TryGetProperty("HidePreviewWhenLocked", out var hide) && hide.GetBoolean(), "Locked notification privacy must persist.");
    Check(p.TryGetProperty("AutoLockMinutes", out var idle) && idle.GetInt32() == 5, "Idle lock setting must persist.");
}

var navigation = new NavigationTracker();
navigation.Start(12, "http://127.0.0.1:8765/fixture.pdf");
navigation.Download("http://127.0.0.1:8765/fixture.pdf");
Check(!navigation.ShouldReportFailure(12, isSuccess: false), "A navigation converted to a download must not report a connection failure.");
navigation.Start(13, "http://127.0.0.1:8765/other");
Check(navigation.ShouldReportFailure(13, isSuccess: false), "An unrelated aborted page navigation must still report a connection failure.");
navigation.Start(14, "http://127.0.0.1:8765/");
Check(!navigation.ShouldReportFailure(14, isSuccess: true), "A successful navigation must not report a failure.");
navigation.Start(15, "https://example.com/");
navigation.CancelIntentionally(15);
Check(!navigation.ShouldReportFailure(15, isSuccess: false), "Opening an external link must not mark the account disconnected.");
navigation.Start(20, "http://127.0.0.1:8765/old");
navigation.Start(21, "http://127.0.0.1:8765/new");
Check(!navigation.ShouldReportFailure(20, isSuccess: false), "A late failure from an older navigation must not overwrite current status.");

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
    return 1;
}
Console.WriteLine("PASS: separate profile paths, restricted local test URL, and persisted preferences.");
return 0;
