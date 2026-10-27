using System.Text.Json;

namespace DuoDesk;

internal sealed record NotificationHistoryEntry(DateTimeOffset ReceivedAt, string Account, string Sender, string Body);

internal sealed class NotificationHistory
{
    private sealed class Data
    {
        public bool Enabled { get; set; }
        public List<NotificationHistoryEntry> Entries { get; set; } = new();
    }
    private readonly string path;
    private readonly List<NotificationHistoryEntry> items = new();
    public NotificationHistory(string path)
    {
        this.path = path;
        try
        {
            if (!File.Exists(path)) return;
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path));
            Enabled = data?.Enabled == true;
            if (data?.Entries is not null)
                items.AddRange(data.Entries.Where(e => e is not null && e.Account is "personal" or "business").Take(200));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { Enabled = false; StorageProblem = "Riwayat notifikasi tidak dapat dibaca."; }
    }
    public bool Enabled { get; private set; }
    public string? StorageProblem { get; private set; }
    public event Action? Changed;
    public IReadOnlyList<NotificationHistoryEntry> Items => items;
    public void SetEnabled(bool enabled) { if (Enabled == enabled) return; Enabled = enabled; Save(); Changed?.Invoke(); }
    public void Record(string account, string? sender, string? body)
    {
        if (!Enabled || account is not ("personal" or "business")) return;
        static string Limit(string? value) { var clean = (value ?? "").Trim(); return clean.Length <= 512 ? clean : clean[..512]; }
        items.Insert(0, new NotificationHistoryEntry(DateTimeOffset.Now, account, Limit(sender), Limit(body)));
        if (items.Count > 200) items.RemoveRange(200, items.Count - 200);
        Save();
        Changed?.Invoke();
    }
    public void Clear() { items.Clear(); Save(); Changed?.Invoke(); }
    private void Save()
    {
        string? temporary = null;
        try
        {
            string directory = Path.GetDirectoryName(path) ?? throw new IOException("Missing directory");
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, ".notification-history-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Data { Enabled = Enabled, Entries = items }));
            File.Move(temporary, path, true);
            StorageProblem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { StorageProblem = "Riwayat notifikasi tidak dapat disimpan."; }
        finally { if (temporary is not null) { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } } }
    }
}
