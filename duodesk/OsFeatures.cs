using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DuoDesk;

internal static class StartupRegistration
{
    internal const string ValueName = "HY Duo WhatsApp";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key is not null && IsEnabled(key, Application.ExecutablePath);
    }
    internal static bool StartsHidden()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key is not null && StartsHidden(key, Application.ExecutablePath);
    }
    internal static void SetEnabled(bool enabled, bool startHidden = false)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        SetEnabled(key, Application.ExecutablePath, enabled, startHidden);
    }
    internal static bool IsEnabled(RegistryKey key, string path) =>
        string.Equals(key.GetValue(ValueName) as string, Quote(path), StringComparison.OrdinalIgnoreCase) || StartsHidden(key, path);
    internal static void SetEnabled(RegistryKey key, string path, bool enabled) => SetEnabled(key, path, enabled, false);
    internal static void SetEnabled(RegistryKey key, string path, bool enabled, bool startHidden)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (enabled) key.SetValue(ValueName, Quote(path) + (startHidden ? " --start-hidden" : ""), RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
    internal static bool StartsHidden(RegistryKey key, string path) =>
        string.Equals(key.GetValue(ValueName) as string, Quote(path) + " --start-hidden", StringComparison.OrdinalIgnoreCase);
    private static string Quote(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"')) throw new ArgumentException("Path executable tidak valid.", nameof(path));
        return "\"" + path + "\"";
    }
}

internal sealed class HotkeyActions(Action open, Action personal, Action business, Action split)
{
    internal void Dispatch(int id)
    {
        switch (id)
        {
            case 1: open(); break;
            case 2: personal(); break;
            case 3: business(); break;
            case 4: split(); break;
        }
    }
}

/// <summary>Registers application-owned shortcuts and releases only successful registrations.</summary>
internal sealed class GlobalHotkeys : NativeWindow, IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const uint Modifiers = 0x0001 | 0x0002 | 0x0004 | 0x4000; // Alt, Ctrl, Shift, no repeat
    private const int BaseId = 0x4800;
    private readonly HotkeyActions actions;
    private readonly List<int> registered = new();
    private bool disposed;

    internal GlobalHotkeys(IntPtr window, HotkeyActions actions)
    {
        if (window == IntPtr.Zero) throw new ArgumentException("Window handle is required.", nameof(window));
        this.actions = actions;
        AssignHandle(window);
    }
    internal IReadOnlyList<string> Register()
    {
        if (disposed) throw new ObjectDisposedException(nameof(GlobalHotkeys));
        var failures = new List<string>();
        foreach (var (id, key, label) in new[]
        {
            (1, (uint)Keys.W, "Ctrl+Alt+Shift+W"),
            (2, (uint)Keys.D1, "Ctrl+Alt+Shift+1"),
            (3, (uint)Keys.D2, "Ctrl+Alt+Shift+2"),
            (4, (uint)Keys.D3, "Ctrl+Alt+Shift+3")
        })
        {
            var nativeId = BaseId + id;
            if (registered.Contains(nativeId)) continue;
            if (RegisterHotKey(Handle, nativeId, Modifiers, key)) registered.Add(nativeId);
            else failures.Add(label);
        }
        return failures;
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage)
        {
            var id = unchecked((int)message.WParam.ToInt64());
            if (registered.Contains(id)) { actions.Dispatch(id - BaseId); return; }
        }
        base.WndProc(ref message);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var id in registered) UnregisterHotKey(Handle, id);
        registered.Clear();
        ReleaseHandle();
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
}

internal sealed record AvailableRelease(string Tag, string PageUrl);

internal static class ReleaseChecker
{
    internal const string CurrentVersion = "1.1.0-preview";
    private const string Repository = "https://github.com/handigunawan59-stack/hy-duo-whatsapp";
    private const string ReleasesApi = "https://api.github.com/repos/handigunawan59-stack/hy-duo-whatsapp/releases?per_page=25";
    private static readonly Regex VersionPattern = new(@"^v?(\d{1,8})\.(\d{1,8})\.(\d{1,8})(?:-([0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?$", RegexOptions.CultureInvariant);

    internal static async Task<AvailableRelease?> CheckAsync(CancellationToken token = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HY-Duo-WhatsApp", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.GetAsync(ReleasesApi, token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
        return FindNewer(json, CurrentVersion);
    }

    internal static AvailableRelease? FindNewer(string json, string currentVersion)
    {
        var current = ParseVersion(currentVersion) ?? throw new ArgumentException("Invalid installed version.", nameof(currentVersion));
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a release list.");
        AvailableRelease? result = null;
        ParsedVersion? best = null;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !item.TryGetProperty("tag_name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            string? tag = name.GetString();
            var version = ParseVersion(tag);
            if (tag is null || version is null || Compare(version, current) <= 0 || (best is not null && Compare(version, best) <= 0)) continue;
            best = version;
            result = new AvailableRelease(tag, Repository + "/releases/tag/" + Uri.EscapeDataString(tag));
        }
        return result;
    }
    private static ParsedVersion? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = VersionPattern.Match(value);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int major) ||
            !int.TryParse(match.Groups[2].Value, out int minor) || !int.TryParse(match.Groups[3].Value, out int patch)) return null;
        return new ParsedVersion(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : null);
    }
    private static int Compare(ParsedVersion a, ParsedVersion b)
    {
        var numeric = a.Major.CompareTo(b.Major);
        if (numeric != 0) return numeric;
        numeric = a.Minor.CompareTo(b.Minor);
        if (numeric != 0) return numeric;
        numeric = a.Patch.CompareTo(b.Patch);
        if (numeric != 0) return numeric;
        if (a.Prerelease is null) return b.Prerelease is null ? 0 : 1;
        if (b.Prerelease is null) return -1;
        var left = a.Prerelease.Split('.');
        var right = b.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool leftNumber = long.TryParse(left[i], out long leftValue);
            bool rightNumber = long.TryParse(right[i], out long rightValue);
            int part = leftNumber && rightNumber ? leftValue.CompareTo(rightValue)
                : leftNumber ? -1 : rightNumber ? 1 : string.CompareOrdinal(left[i], right[i]);
            if (part != 0) return part;
        }
        return left.Length.CompareTo(right.Length);
    }
    private sealed record ParsedVersion(int Major, int Minor, int Patch, string? Prerelease);
}
