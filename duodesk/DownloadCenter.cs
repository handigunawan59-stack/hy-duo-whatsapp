using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace DuoDesk;

/// <summary>Tracks download metadata without replacing WebView2's normal save behavior.</summary>
internal sealed class DownloadCenter : IDisposable
{
    private readonly DownloadHistory history;
    private readonly List<(CoreWebView2 Core, EventHandler<CoreWebView2DownloadStartingEventArgs> Handler)> cores = new();
    private readonly List<(CoreWebView2DownloadOperation Operation, EventHandler<object> Progress, EventHandler<object> State)> operations = new();
    private bool disposed;

    public DownloadCenter(string dataDirectory) => history = new DownloadHistory(Path.Combine(dataDirectory, "download-history.json"));

    public void Attach(CoreWebView2 core, string account)
    {
        if (disposed) throw new ObjectDisposedException(nameof(DownloadCenter));
        if (account is not ("personal" or "business")) throw new ArgumentException("Unknown account", nameof(account));
        if (cores.Any(c => ReferenceEquals(c.Core, core))) return;
        EventHandler<CoreWebView2DownloadStartingEventArgs> handler = (_, e) => Track(e.DownloadOperation, account);
        core.DownloadStarting += handler;
        cores.Add((core, handler));
    }

    private void Track(CoreWebView2DownloadOperation operation, string account)
    {
        try
        {
            var record = history.Start(account, operation.ResultFilePath);
            EventHandler<object>? onProgress = null;
            EventHandler<object>? onState = null;
            void Refresh()
            {
                if (disposed) return;
                try
                {
                    var state = operation.State switch
                    {
                        CoreWebView2DownloadState.Completed => "completed",
                        CoreWebView2DownloadState.Interrupted => "interrupted",
                        _ => "downloading"
                    };
                    long? total = operation.TotalBytesToReceive is ulong totalBytes
                        ? totalBytes > long.MaxValue ? long.MaxValue : (long)totalBytes
                        : null;
                    history.Update(record.Id, operation.BytesReceived, total, state, operation.ResultFilePath);
                    // WebView2 may automatically resume an interrupted download.
                    // Keep its listeners until completion or application shutdown.
                    if (state == "completed" && onProgress is not null && onState is not null)
                    {
                        operation.BytesReceivedChanged -= onProgress;
                        operation.StateChanged -= onState;
                        operations.RemoveAll(x => ReferenceEquals(x.Operation, operation));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException) { }
            }
            onProgress = (_, _) => Refresh();
            onState = (_, _) => Refresh();
            operation.BytesReceivedChanged += onProgress;
            operation.StateChanged += onState;
            operations.Add((operation, onProgress, onState));
            Refresh();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            // Download remains controlled by WebView2 if tracking is unavailable.
        }
    }

    public void Show(IWin32Window owner, Func<string, string>? accountName = null)
    {
        if (disposed) throw new ObjectDisposedException(nameof(DownloadCenter));
        using var dialog = new DownloadCenterDialog(history, accountName ?? (key => key == "personal" ? "Pribadi" : "Usaha"));
        dialog.ShowDialog(owner);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var item in cores)
        {
            try { item.Core.DownloadStarting -= item.Handler; }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException) { }
        }
        foreach (var item in operations)
        {
            try { item.Operation.BytesReceivedChanged -= item.Progress; item.Operation.StateChanged -= item.State; }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException) { }
        }
        cores.Clear();
        operations.Clear();
    }
}

internal sealed class DownloadCenterDialog : Form
{
    private readonly DownloadHistory history;
    private readonly Func<string, string> accountName;
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly Label storageStatus = new() { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly TextBox search = new() { Width = 200, PlaceholderText = "Cari nama file…" };
    private readonly ComboBox accountFilter = new() { Width = 135, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox stateFilter = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 500 };
    private bool dirty;

    public DownloadCenterDialog(DownloadHistory history, Func<string, string> accountName)
    {
        this.history = history;
        this.accountName = accountName;
        Text = "Pusat unduhan";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(850, 500);
        MinimumSize = new Size(650, 350);
        Font = new Font("Segoe UI", 9F);
        list.Columns.Add("Waktu", 130);
        list.Columns.Add("Akun", 100);
        list.Columns.Add("Nama file", 270);
        list.Columns.Add("Progres", 120);
        list.Columns.Add("Status", 110);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, Padding = new Padding(8, 6, 8, 4), WrapContents = false };
        accountFilter.Items.AddRange(new object[] { "Semua akun", accountName("personal"), accountName("business") }); accountFilter.SelectedIndex = 0;
        stateFilter.Items.AddRange(new object[] { "Semua status", "Mengunduh", "Selesai", "Terhenti" }); stateFilter.SelectedIndex = 0;
        search.TextChanged += (_, _) => RebuildList();
        accountFilter.SelectedIndexChanged += (_, _) => RebuildList();
        stateFilter.SelectedIndexChanged += (_, _) => RebuildList();
        filters.Controls.Add(search); filters.Controls.Add(accountFilter); filters.Controls.Add(stateFilter);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), WrapContents = false };
        var open = new Button { Text = "Buka file", AutoSize = true };
        var folder = new Button { Text = "Buka folder", AutoSize = true };
        var clear = new Button { Text = "Hapus riwayat", AutoSize = true };
        open.Click += (_, _) => OpenSelected(openFile: true);
        folder.Click += (_, _) => OpenSelected(openFile: false);
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Hapus riwayat unduhan selesai dan terhenti? Unduhan yang sedang berjalan tetap ditampilkan, dan file tidak akan dihapus.", "Pusat unduhan", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) history.Clear();
        };
        buttons.Controls.Add(open);
        buttons.Controls.Add(folder);
        buttons.Controls.Add(clear);
        buttons.Controls.Add(storageStatus);
        Controls.Add(list);
        Controls.Add(buttons);
        Controls.Add(filters);
        history.Changed += MarkDirty;
        refreshTimer.Tick += (_, _) => { if (dirty) RebuildList(); };
        refreshTimer.Start();
        RebuildList();
    }

    private void MarkDirty() => dirty = true;

    private void RebuildList()
    {
        dirty = false;
        string? selected = list.SelectedItems.Count > 0 ? (string?)list.SelectedItems[0].Tag : null;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            string? account = accountFilter.SelectedIndex switch { 1 => "personal", 2 => "business", _ => null };
            string? stateFilterKey = stateFilter.SelectedIndex switch { 1 => "downloading", 2 => "completed", 3 => "interrupted", _ => null };
            foreach (var record in history.Filter(search.Text, account, stateFilterKey))
            {
                string progress = record.TotalBytes is > 0 ? $"{Math.Min(100, (double)record.BytesReceived / record.TotalBytes.Value * 100):0}%" : $"{record.BytesReceived / 1024} KB";
                string state = record.State switch { "completed" => "Selesai", "interrupted" => "Terhenti", _ => "Mengunduh" };
                var row = new ListViewItem(record.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")) { Tag = record.Id };
                row.SubItems.Add(accountName(record.Account));
                row.SubItems.Add(Path.GetFileName(record.Path));
                row.SubItems.Add(progress);
                row.SubItems.Add(state);
                list.Items.Add(row);
                if (record.Id == selected) row.Selected = true;
            }
            storageStatus.Text = history.StorageProblem ?? "";
        }
        finally { list.EndUpdate(); }
    }

    private void OpenSelected(bool openFile)
    {
        var id = list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as string : null;
        var record = history.Items.FirstOrDefault(item => item.Id == id);
        if (record is null) { MessageBox.Show(this, "Pilih unduhan terlebih dahulu.", Text); return; }
        try
        {
            if (openFile)
            {
                if (record.State != "completed" || !File.Exists(record.Path)) { MessageBox.Show(this, "File belum selesai atau tidak ditemukan.", Text); return; }
                Process.Start(new ProcessStartInfo(record.Path) { UseShellExecute = true });
            }
            else
            {
                string? directory = Path.GetDirectoryName(record.Path);
                if (directory is null || !Directory.Exists(directory)) { MessageBox.Show(this, "Folder tidak ditemukan.", Text); return; }
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, "Tidak dapat membuka file atau folder.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { history.Changed -= MarkDirty; refreshTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
