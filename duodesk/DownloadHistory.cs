using System.Text.Json;

namespace DuoDesk;

internal sealed class DownloadRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Account { get; set; } = "";
    public string Path { get; set; } = "";
    public string State { get; set; } = "downloading";
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>Stores local metadata only; no messages, media contents or download URLs.</summary>
internal sealed class DownloadHistory
{
    private readonly string path;
    private readonly List<DownloadRecord> items;
    public IReadOnlyList<DownloadRecord> Items => items;
    public string? StorageProblem { get; private set; }
    public event Action? Changed;

    public IReadOnlyList<DownloadRecord> Filter(string? search, string? account, string? state)
    {
        string query = search?.Trim() ?? "";
        return items.Where(item =>
            (query.Length == 0 || (System.IO.Path.GetFileName(item.Path) ?? "").Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
            (string.IsNullOrEmpty(account) || item.Account == account) &&
            (string.IsNullOrEmpty(state) || item.State == state)).ToArray();
    }

    public DownloadHistory(string path)
    {
        this.path = path;
        try
        {
            items = File.Exists(path)
                ? JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(path)) ?? new()
                : new();
            items.RemoveAll(item => item is null || item.Account is not ("personal" or "business") || string.IsNullOrWhiteSpace(item.Id));
            if (items.Count > 200) items.RemoveRange(200, items.Count - 200);
            bool wasActive = false;
            foreach (var item in items.Where(item => item.State == "downloading")) { item.State = "interrupted"; wasActive = true; }
            if (wasActive) Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            items = new();
            StorageProblem = "Riwayat tidak dapat dibaca.";
        }
    }

    public DownloadRecord Start(string account, string path)
    {
        if (account is not ("personal" or "business")) throw new ArgumentException("Unknown account", nameof(account));
        var item = new DownloadRecord { Account = account, Path = path };
        items.Insert(0, item);
        if (items.Count > 200) items.RemoveAt(items.Count - 1);
        Save();
        Changed?.Invoke();
        return item;
    }

    public void Update(string id, long received, long? total, string state, string path)
    {
        if (state is not ("downloading" or "completed" or "interrupted")) throw new ArgumentException("Unknown state", nameof(state));
        var item = items.Find(i => i.Id == id);
        if (item is null) return;
        item.BytesReceived = Math.Max(0, received);
        item.TotalBytes = total is > 0 ? total : null;
        item.State = state;
        item.Path = path;
        if (state != "downloading") Save();
        Changed?.Invoke();
    }

    public void Clear()
    {
        // Active downloads remain visible and continue tracking after history is cleared.
        items.RemoveAll(item => item.State != "downloading");
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        string? temp = null;
        try
        {
            string dir = System.IO.Path.GetDirectoryName(path) ?? throw new IOException("Missing directory.");
            Directory.CreateDirectory(dir);
            temp = System.IO.Path.Combine(dir, ".download-history-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(items));
            File.Move(temp, path, true);
            StorageProblem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StorageProblem = "Riwayat tidak dapat disimpan.";
        }
        finally
        {
            if (temp is not null)
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
