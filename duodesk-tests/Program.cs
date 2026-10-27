using DuoDesk;

var failures = new List<string>();
void Check(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

var root = Path.Combine(Path.GetTempPath(), "duodesk-path-test-root");
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
