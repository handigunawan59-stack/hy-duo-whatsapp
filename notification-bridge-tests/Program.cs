using System.Reflection;

var bridge = Assembly.GetExecutingAssembly().GetType("DuoDesk.NotificationBridge");
if (bridge is null)
{
    Console.Error.WriteLine("FAIL: notification bridge is not implemented");
    return 1;
}
var format = bridge.GetMethod("FormatMessage", BindingFlags.Static | BindingFlags.NonPublic);
var origin = bridge.GetMethod("IsTrustedOrigin", BindingFlags.Static | BindingFlags.NonPublic);
if (format is null || origin is null)
{
    Console.Error.WriteLine("FAIL: notification formatting and origin checks are missing");
    return 1;
}
var failures = 0;
void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
    if (!condition) failures++;
}
(string Title, string Body) Format(string account, string? title, string? body) =>
    ((string, string))format.Invoke(null, new object?[] { account, title, body })!;
bool Trusted(string source, string allowed) => (bool)origin.Invoke(null, new object[] { source, allowed })!;
var message = Format("Usaha", "Nadia", "Halo, saya mau pesan 50 pcs.");
Check(message.Title == "Usaha • Nadia" && message.Body == "Halo, saya mau pesan 50 pcs.", "Account, sender, and content are preserved");
var privacyFormat = bridge.GetMethod("FormatForPrivacy", BindingFlags.Static | BindingFlags.NonPublic);
Check(privacyFormat is not null, "Locked workspace can suppress notification message previews");
if (privacyFormat is not null)
{
    var hidden = ((string Title, string Body))privacyFormat.Invoke(null, new object?[] { "Usaha", "Nadia", "Isi rahasia", true })!;
    var shown = ((string Title, string Body))privacyFormat.Invoke(null, new object?[] { "Usaha", "Nadia", "Isi rahasia", false })!;
    Check(hidden.Title == "Usaha • Nadia" && hidden.Body == "Pesan baru diterima.", "Privacy option hides content but keeps sender");
    Check(shown.Body == "Isi rahasia", "Unlocked notifications show the message content");
}
var soundChoice = bridge.GetMethod("SoundForAccount", BindingFlags.Static | BindingFlags.NonPublic);
Check(soundChoice is not null, "Each account can select its own notification sound");
if (soundChoice is not null)
{
    string Sound(string key) => (string)soundChoice.Invoke(null, new object[] { key, "Asterisk", "Exclamation" })!;
    Check(Sound("personal") == "Asterisk" && Sound("business") == "Exclamation", "Sound selection is isolated by account");
    Check(Sound("unknown") == "Asterisk", "Unexpected account uses the default sound");
}
Check(bridge.GetMethod("Configure") is not null && bridge.GetMethod("TestNotification") is not null,
    "Notification options and a per-account diagnostic action are available");
var empty = Format("Pribadi", "", "");
Check(empty.Title == "Pribadi • Pesan baru" && empty.Body == "Pesan baru diterima.", "Empty title and body have safe placeholders");
Check(Trusted("https://web.whatsapp.com/some-path", "https://web.whatsapp.com"), "WhatsApp page is trusted");
Check(!Trusted("https://web.whatsapp.com.evil.test/", "https://web.whatsapp.com"), "Similar attacker hostname is rejected");
Check(!Trusted("https://web.whatsapp.com:444/", "https://web.whatsapp.com"), "Wrong HTTPS port is rejected");
Check(!Trusted("http://web.whatsapp.com/", "https://web.whatsapp.com"), "Insecure WhatsApp origin is rejected");
Check(Trusted("http://127.0.0.1:18888/", "http://127.0.0.1:18888"), "Explicit loopback fixture origin is accepted");
Check(!Trusted("http://127.0.0.1:18889/", "http://127.0.0.1:18888"), "Different loopback port is rejected");
var quietAudioConstructor = bridge.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
    new[] { typeof(NotifyIcon), typeof(Action<string>), typeof(Action<string, string>), typeof(string), typeof(Action) }, null);
Check(quietAudioConstructor is not null, "Audio delivery can be observed without playing sound in a hidden integration fixture");
var accountStillMatchesVisibleBalloon = false;
var clickAdvancesQueue = false;
var diagnosticIsRoutedToAccount = false;
Exception? notificationTestError = null;
var uiThread = new Thread(() =>
{
    try
    {
        using var tray = new NotifyIcon();
        string? selected = null;
        using var instance = new DuoDesk.NotificationBridge(tray, key => selected = key, (_, _) => { });
        var pendingType = bridge.GetNestedType("PendingNotification", BindingFlags.NonPublic)!;
        var webNotification = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Microsoft.Web.WebView2.Core.CoreWebView2Notification));
        var visible = Activator.CreateInstance(pendingType, new object[] { "business", "Usaha", "Pesan", webNotification })!;
        pendingType.GetProperty("BalloonShown")!.SetValue(visible, true);
        pendingType.GetProperty("ReportedShown")!.SetValue(visible, true);
        pendingType.GetProperty("Clicked")!.SetValue(visible, true);
        var current = bridge.GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)!;
        current.SetValue(instance, visible);
        bridge.GetMethod("OnDisplayTimeout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, new object?[] { null, EventArgs.Empty });
        accountStillMatchesVisibleBalloon = ReferenceEquals(current.GetValue(instance), visible);
        var clicked = Activator.CreateInstance(pendingType, new object[] { "personal", "Pribadi", "Pesan", webNotification })!;
        pendingType.GetProperty("BalloonShown")!.SetValue(clicked, true);
        current.SetValue(instance, clicked);
        bridge.GetMethod("OnBalloonClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, new object?[] { null, EventArgs.Empty });
        clickAdvancesQueue = selected == "personal" && current.GetValue(instance) is null;
        using var secondTray = new NotifyIcon();
        var soundCount = 0;
        using var diagnostic = new DuoDesk.NotificationBridge(secondTray, _ => { }, (_, _) => { }, "https://web.whatsapp.com", () => soundCount++);
        diagnostic.Configure(key => key == "business" ? "Exclamation" : "Asterisk", () => false);
        diagnostic.TestNotification("business", "Usaha");
        var testPending = current.GetValue(diagnostic);
        diagnosticIsRoutedToAccount = soundCount == 1 && testPending is not null &&
            (string?)pendingType.GetProperty("AccountKey")!.GetValue(testPending) == "business";
    }
    catch (Exception ex) { notificationTestError = ex; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (notificationTestError is not null) Console.Error.WriteLine("Notification fixture error: " + notificationTestError.GetType().Name);
Check(notificationTestError is null && accountStillMatchesVisibleBalloon, "Visible notification keeps its account association beyond timeout");
Check(notificationTestError is null && clickAdvancesQueue, "Click selects the associated account and frees the notification slot");
Check(notificationTestError is null && diagnosticIsRoutedToAccount, "Notification test plays sound and keeps the requested account");
return failures == 0 ? 0 : 1;
