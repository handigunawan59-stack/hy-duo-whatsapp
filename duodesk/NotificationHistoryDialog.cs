namespace DuoDesk;

internal static class NotificationHistoryDialog
{
    public static void Show(IWin32Window owner, NotificationHistory history, Func<string, string> accountLabel)
    {
        using var dialog = new Form { Text = "Riwayat notifikasi", StartPosition = FormStartPosition.CenterParent,
            Size = new Size(850, 500), MinimumSize = new Size(650, 350), Font = new Font("Segoe UI", 9F) };
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        list.Columns.Add("Waktu", 150); list.Columns.Add("Akun", 120); list.Columns.Add("Pengirim", 180); list.Columns.Add("Isi notifikasi", 330);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
        var enabled = new CheckBox { Text = "Simpan riwayat notifikasi di komputer ini", Checked = history.Enabled, AutoSize = true };
        var note = new Label { Text = "Menyimpan pengirim dan isi secara lokal tanpa enkripsi, maksimal 200 notifikasi. Matikan dan hapus riwayat jika tidak diperlukan.", Width = 790, Height = 34 };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), WrapContents = false };
        var clear = new Button { Text = "Hapus riwayat", Width = 125, Height = 30 };
        var status = new Label { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(10, 6, 0, 0) };
        void RefreshItems()
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var entry in history.Items)
                {
                    var row = new ListViewItem(entry.ReceivedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                    row.SubItems.Add(accountLabel(entry.Account)); row.SubItems.Add(entry.Sender); row.SubItems.Add(entry.Body); list.Items.Add(row);
                }
                status.Text = history.StorageProblem ?? "";
            }
            finally { list.EndUpdate(); }
        }
        enabled.CheckedChanged += (_, _) => { history.SetEnabled(enabled.Checked); RefreshItems(); };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(dialog, "Hapus semua riwayat notifikasi yang tersimpan di komputer ini?", dialog.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            history.Clear(); RefreshItems();
        };
        top.Controls.Add(enabled); top.Controls.Add(note); bottom.Controls.Add(clear); bottom.Controls.Add(status);
        dialog.Controls.Add(list); dialog.Controls.Add(top); dialog.Controls.Add(bottom);
        void OnChanged() { if (!dialog.IsDisposed) RefreshItems(); }
        history.Changed += OnChanged;
        try { RefreshItems(); dialog.ShowDialog(owner); }
        finally { history.Changed -= OnChanged; }
    }
}
