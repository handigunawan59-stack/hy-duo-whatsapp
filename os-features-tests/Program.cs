using DuoDesk;
using Microsoft.Win32;

var failures = new List<string>();
void Check(bool value, string name) { if (value) Console.WriteLine("PASS " + name); else { failures.Add(name); Console.Error.WriteLine("FAIL " + name); } }

var testKeyName = @"Software\HYDuoWhatsAppTests\" + Guid.NewGuid().ToString("N");
using (var key = Registry.CurrentUser.CreateSubKey(testKeyName))
{
    const string exe = @"C:\Program Files\HY Duo WhatsApp\DuoDesk.exe";
    StartupRegistration.SetEnabled(key, exe, true);
    Check(StartupRegistration.IsEnabled(key, exe), "autostart registers the expected quoted executable");
    Check(key.GetValue(StartupRegistration.ValueName) as string == "\"" + exe + "\"", "autostart quotes the executable path");
    StartupRegistration.SetEnabled(key, exe, true, startHidden: true);
    Check(StartupRegistration.IsEnabled(key, exe), "hidden autostart is recognized as enabled");
    Check(StartupRegistration.StartsHidden(key, exe), "hidden autostart preserves the tray option");
    Check(key.GetValue(StartupRegistration.ValueName) as string == "\"" + exe + "\" --start-hidden", "hidden autostart passes only the intended flag");
    StartupRegistration.SetEnabled(key, exe, false);
    Check(!StartupRegistration.IsEnabled(key, exe), "disabling autostart removes the registered entry");
}
Registry.CurrentUser.DeleteSubKeyTree(testKeyName);

var selected = new List<string>();
var shortcuts = new HotkeyActions(() => selected.Add("open"), () => selected.Add("personal"), () => selected.Add("business"), () => selected.Add("split"));
shortcuts.Dispatch(2);
Check(selected.SequenceEqual(new[] { "personal" }), "second shortcut selects personal account");
selected.Clear();
shortcuts.Dispatch(99);
Check(selected.Count == 0, "unknown hotkey never triggers an action");

const string json = "[{\"tag_name\":\"v1.2.0\",\"draft\":false,\"prerelease\":false},{\"tag_name\":\"v1.1.0\",\"draft\":false,\"prerelease\":false}]";
var release = ReleaseChecker.FindNewer(json, "1.0.0-preview");
Check(release?.Tag == "v1.2.0", "newer GitHub version is selected");
Check(release?.PageUrl == "https://github.com/handigunawan59-stack/hy-duo-whatsapp/releases/tag/v1.2.0", "release link is constructed from trusted repository");
Check(ReleaseChecker.FindNewer(json, "1.2.0") is null, "installed release is not offered as update");
Check(ReleaseChecker.FindNewer("[{\"tag_name\":\"v9.0.0\",\"draft\":true}]", "1.0.0") is null, "draft releases are ignored");
Check(ReleaseChecker.FindNewer("[{\"tag_name\":\"v1.0.0\",\"draft\":false}]", "1.0.0-preview") is not null, "stable release is newer than its preview");
Check(ReleaseChecker.FindNewer("[{\"tag_name\":\"v1.0.0-preview\",\"draft\":false}]", "1.0.0") is null, "preview is older than matching stable release");
Check(ReleaseChecker.FindNewer("[{\"tag_name\":\"v999.0.0/../../evil\",\"draft\":false}]", "1.0.0") is null, "malformed tags cannot generate an unsafe link");
return failures.Count == 0 ? 0 : 1;
