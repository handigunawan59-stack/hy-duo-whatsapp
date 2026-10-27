using System.Reflection;

var historyType = Assembly.GetExecutingAssembly().GetType("DuoDesk.DownloadHistory");
if (historyType is null)
{
    Console.Error.WriteLine("FAIL: download history must remember completed downloads for each account.");
    return 1;
}

var testRoot = Path.Combine(Path.GetTempPath(), "hy-duo-download-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var historyPath = Path.Combine(testRoot, "history.json");
var document = Path.Combine(testRoot, "document.pdf");
File.WriteAllText(document, "test data");
object History() => Activator.CreateInstance(historyType, historyPath)!;
object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method)!.Invoke(target, args);
object? Read(object target, string property) => target.GetType().GetProperty(property)!.GetValue(target);
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
try
{
    var history = History();
    var item = Call(history, "Start", "personal", document)!;
    var id = (string)Read(item, "Id")!;
    Call(history, "Update", id, 12L, 20L, "completed", document);
    var reopened = History();
    var items = ((System.Collections.IEnumerable)Read(reopened, "Items")!).Cast<object>().ToArray();
    Check(items.Length == 1 && (string)Read(items[0], "Account")! == "personal", "Completed downloads persist with their account.");
    Check((string)Read(items[0], "Path")! == document && (string)Read(items[0], "State")! == "completed", "Completed file path and status are saved.");
    Call(reopened, "Clear");
    Check(File.Exists(document), "Clearing history never deletes the downloaded file.");
    Check(!((System.Collections.IEnumerable)Read(History(), "Items")!).Cast<object>().Any(), "Cleared history stays cleared after restart.");
    var active = History();
    Call(active, "Start", "business", Path.Combine(testRoot, "other.pdf"));
    Call(active, "Clear");
    Check(((System.Collections.IEnumerable)Read(active, "Items")!).Cast<object>().Count() == 1, "Clearing history keeps active download progress visible.");
    var resumed = ((System.Collections.IEnumerable)Read(History(), "Items")!).Cast<object>().Single();
    Check((string)Read(resumed, "State")! == "interrupted", "A download in progress at shutdown is not reported as completed.");
    var searchable = History();
    Call(searchable, "Start", "personal", Path.Combine(testRoot, "invoice.pdf"));
    var picture = Call(searchable, "Start", "business", Path.Combine(testRoot, "photo.png"))!;
    Call(searchable, "Update", (string)Read(picture, "Id")!, 5L, 5L, "completed", Path.Combine(testRoot, "photo.png"));
    var matches = ((System.Collections.IEnumerable)Call(searchable, "Filter", "INVOICE", "personal", "downloading")!).Cast<object>().ToArray();
    Check(matches.Length == 1 && (string)Read(matches[0], "Account")! == "personal", "Search filters file names without case sensitivity and by account and status.");
    var none = ((System.Collections.IEnumerable)Call(searchable, "Filter", "invoice", "business", null)!).Cast<object>().ToArray();
    Check(none.Length == 0, "A filter must not return files from the other account.");
    picture.GetType().GetProperty("Path")!.SetValue(picture, null);
    var safe = ((System.Collections.IEnumerable)Call(searchable, "Filter", "photo", null, null)!).Cast<object>().ToArray();
    Check(safe.Length == 0, "Filtering ignores an invalid missing file path without crashing.");
    return 0;
}
finally { Directory.Delete(testRoot, true); }
