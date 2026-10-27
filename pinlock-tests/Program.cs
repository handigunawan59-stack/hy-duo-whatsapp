using System.Reflection;

// These assertions catch authentication bypasses, loss of the configured
// lock after corruption, and accidental replacement of the existing PIN.
string scratch = Path.Combine(AppContext.BaseDirectory, "pin-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    Type? pinLock = Assembly.GetExecutingAssembly().GetType("DuoDesk.PinLock");
    if (pinLock is null)
    {
        Console.Error.WriteLine("FAIL: PIN lock implementation is missing.");
        return 1;
    }

    MethodInfo Require(string name) => pinLock.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing {name}.");
    bool Configured(string path) => (bool)Require("IsConfigured").Invoke(null, new object[] { path })!;
    bool Create(string path, string pin) => (bool)Require("CreatePinFile").Invoke(null, new object[] { path, pin })!;
    bool Verify(string path, string pin) => (bool)Require("VerifyPin").Invoke(null, new object[] { path, pin })!;
    void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("PASS: " + description);
    }

    string first = Path.Combine(scratch, "first.json");
    string second = Path.Combine(scratch, "second.json");
    Check(!Configured(first), "a missing PIN is reported as unconfigured");
    Check(!Create(first, "123"), "a short PIN cannot be configured");
    Check(Create(first, "673021"), "a valid PIN can be configured");
    Check(Configured(first), "the PIN file is recognized as configured");
    Check(Verify(first, "673021"), "the correct PIN unlocks");
    Check(!Verify(first, "673022"), "an incorrect PIN does not unlock");
    Check(!File.ReadAllText(first).Contains("673021"), "the stored file does not expose the PIN");
    Check(Create(second, "673021"), "the same PIN can configure a second independent file");
    Check(File.ReadAllText(first) != File.ReadAllText(second), "independent PIN files use different salts");
    Check(!Create(first, "555555"), "an existing PIN cannot be overwritten");
    Check(Verify(first, "673021"), "the original PIN still works after a failed replacement");
    File.WriteAllText(first, "not valid json");
    Check(Configured(first), "a corrupt PIN file remains configured");
    Check(!Verify(first, "673021"), "a corrupt PIN file fails closed");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL: " + (exception.InnerException ?? exception).Message);
    return 1;
}
finally
{
    if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
}
