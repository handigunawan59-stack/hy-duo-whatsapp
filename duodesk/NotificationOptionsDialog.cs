using System.Net.NetworkInformation;

namespace DuoDesk;

internal static class NotificationOptionsDialog
{
    private static readonly string[] Sounds = { "Asterisk", "Beep", "Exclamation", "Hand", "Question" };

    // Status is deliberately supplied by the host: it describes a page load, not WhatsApp login.
    public static bool Show(IWin32Window owner, AppSettings settings, NotificationBridge bridge,
        Func<string, string> label, Func<string, string> accountStatus)
    {
        using var dialog = new Form { Text = "Notifikasi dan privasi", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            ShowInTaskbar = false, ClientSize = new Size(560, 505), Font = new Font("Segoe UI", 9F) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
        dialog.Controls.Add(flow);
        flow.Controls.Add(new Label { Text = "Pemeriksaan notifikasi", Font = new Font("Segoe UI", 13F, FontStyle.Bold), AutoSize = true });
        flow.Controls.Add(new Label { Text = "Aktifkan notifikasi di WhatsApp Web dan pengaturan Windows.\nTes di bawah memeriksa suara dan pemberitahuan aplikasi, bukan pengiriman pesan WhatsApp.", Width = 510, Height = 48 });
        flow.Controls.Add(new Label { Text = NetworkInterface.GetIsNetworkAvailable() ? "Jaringan Windows: tersedia" : "Jaringan Windows: tidak tersedia", AutoSize = true });
        flow.Controls.Add(new Label { Text = $"{label("personal")}: {accountStatus("personal")}", AutoSize = true });
        flow.Controls.Add(new Label { Text = $"{label("business")}: {accountStatus("business")}", AutoSize = true });
        flow.Controls.Add(new Label { Text = "Suara pemberitahuan", Font = new Font("Segoe UI", 10F, FontStyle.Bold), AutoSize = true, Margin = new Padding(3, 14, 3, 3) });

        ComboBox AddSoundRow(string key, string saved)
        {
            var row = new FlowLayoutPanel { Width = 500, Height = 38, WrapContents = false };
            row.Controls.Add(new Label { Text = label(key), Width = 140, Padding = new Padding(0, 8, 0, 0), AutoEllipsis = true });
            var combo = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(Sounds);
            combo.SelectedItem = Sounds.Contains(saved) ? saved : "Asterisk";
            row.Controls.Add(combo);
            var test = new Button { Text = "Tes notifikasi", Width = 135, Height = 30 };
            test.Click += (_, _) => bridge.TestNotification(key, label(key), combo.SelectedItem as string);
            row.Controls.Add(test);
            flow.Controls.Add(row);
            return combo;
        }
        var personal = AddSoundRow("personal", settings.PersonalSound);
        var business = AddSoundRow("business", settings.BusinessSound);
        var hidePreview = new CheckBox { Text = "Sembunyikan isi pesan saat aplikasi terkunci", Checked = settings.HidePreviewWhenLocked, AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
        flow.Controls.Add(hidePreview);
        var idleRow = new FlowLayoutPanel { Width = 500, Height = 38, WrapContents = false, Margin = new Padding(3, 7, 3, 3) };
        idleRow.Controls.Add(new Label { Text = "Kunci otomatis setelah", Width = 155, Padding = new Padding(0, 8, 0, 0) });
        var idle = new NumericUpDown { Minimum = 0, Maximum = 240, Width = 65, Value = Math.Clamp(settings.AutoLockMinutes, 0, 240) };
        idleRow.Controls.Add(idle);
        idleRow.Controls.Add(new Label { Text = "menit tidak aktif (0 = nonaktif)", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        flow.Controls.Add(idleRow);
        flow.Controls.Add(new Label { Text = "Kunci otomatis memerlukan PIN yang sudah dibuat. PIN tidak mengenkripsi data.", Width = 500, Height = 32, ForeColor = Color.DimGray });
        var buttons = new FlowLayoutPanel { Width = 500, Height = 40, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var ok = new Button { Text = "Simpan", Width = 95, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Batal", Width = 95, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); flow.Controls.Add(buttons);
        dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        settings.PersonalSound = (string)personal.SelectedItem!;
        settings.BusinessSound = (string)business.SelectedItem!;
        settings.HidePreviewWhenLocked = hidePreview.Checked;
        settings.AutoLockMinutes = (int)idle.Value;
        return true;
    }
}
