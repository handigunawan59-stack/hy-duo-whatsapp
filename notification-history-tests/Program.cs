using DuoDesk;

var path = Path.Combine(Path.GetTempPath(), "hy-notification-" + Guid.NewGuid().ToString("N") + ".json");
var failures = new List<string>();
void Check(bool condition, string label) { if (!condition) failures.Add(label); else Console.WriteLine("PASS " + label); }
try
{
    var history = new NotificationHistory(path);
    history.Record("personal", "Sender", "private content");
    Check(!history.Enabled && history.Items.Count == 0 && !File.Exists(path), "No messages are recorded before opt-in");
    history.SetEnabled(true);
    Check(typeof(NotificationHistory).GetEvent("Changed") is not null, "History provides change notifications so an open list can refresh");
    history.Record("personal", "Sender", "hello");
    var restored = new NotificationHistory(path);
    Check(restored.Enabled && restored.Items.Count == 1 && restored.Items[0].Body == "hello" && restored.Items[0].Account == "personal", "Opted-in message persists with the correct account");
    restored.SetEnabled(false);
    restored.Record("business", "Other", "not recorded");
    var disabled = new NotificationHistory(path);
    Check(!disabled.Enabled && disabled.Items.Count == 1, "Disabled history records no additional messages after restart");
    disabled.Clear();
    Check(new NotificationHistory(path).Items.Count == 0, "Clearing removes all stored history");
    for (int i = 0; i < 210; i++) { disabled.SetEnabled(true); disabled.Record("business", "sender", "item " + i); }
    Check(disabled.Items.Count == 200 && disabled.Items[0].Body == "item 209", "History is bounded and keeps the newest records");
    disabled.Record("unknown", "sender", "invalid");
    Check(disabled.Items.Count == 200 && disabled.Items[0].Body == "item 209", "Unknown account is never recorded");
}
finally { if (File.Exists(path)) File.Delete(path); }
if (failures.Count == 0) return 0;
foreach (var f in failures) Console.Error.WriteLine("FAIL " + f);
return 1;
