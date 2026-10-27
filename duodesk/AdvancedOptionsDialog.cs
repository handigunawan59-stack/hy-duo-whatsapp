using System.Text.Json;

namespace DuoDesk;

internal static class AdvancedOptionsDialog
{
    internal static bool Show(Form owner, AdvancedSettings options, GlobalHotkeys hotkeys)
    {
        using var dialog = new Form { Text = "Pintasan dan pembaruan", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(530, 350), Font = owner.Font };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18), AutoScroll = true };
        var header = new Label { Text = "Klik kotak lalu tekan kombinasi Ctrl atau Alt dengan huruf, angka, atau F1–F12.", Width = 480, Height = 40 };
        panel.Controls.Add(header);
        var values = (int[])options.Shortcuts.Clone();
        var names = new[] { "Buka aplikasi", "Akun pertama", "Akun kedua", "Berdampingan" };
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var row = new FlowLayoutPanel { Width = 480, Height = 38, WrapContents = false };
            row.Controls.Add(new Label { Text = names[i], Width = 150, Padding = new Padding(0, 8, 0, 0) });
            var box = new TextBox { ReadOnly = true, Width = 270, Text = Display(values[i]) };
            box.KeyDown += (_, e) =>
            {
                e.SuppressKeyPress = true;
                int candidate = (int)(e.KeyCode | e.Modifiers);
                if (AdvancedOptions.IsValidShortcut(candidate)) { values[index] = candidate; box.Text = Display(candidate); }
            };
            row.Controls.Add(box); panel.Controls.Add(row);
        }
        var periodic = new CheckBox { Text = "Periksa versi baru secara berkala (sekitar sekali sehari)", AutoSize = true, Checked = options.CheckUpdatesAutomatically, Margin = new Padding(3, 10, 3, 8) };
        panel.Controls.Add(periodic);
        var save = new Button { Text = "Simpan", Width = 95, Height = 34 };
        save.Click += (_, _) =>
        {
            if (!AdvancedOptions.ValidShortcuts(values)) { MessageBox.Show(dialog, "Setiap pintasan harus berbeda dan memakai Ctrl atau Alt.", dialog.Text); return; }
            var failures = hotkeys.Rebind(values);
            if (failures.Count > 0) { MessageBox.Show(dialog, "Pintasan sudah digunakan atau tidak tersedia: " + string.Join(", ", failures) + ". Pintasan sebelumnya dipulihkan jika tersedia.", dialog.Text); return; }
            options.Shortcuts = (int[])values.Clone();
            options.CheckUpdatesAutomatically = periodic.Checked;
            dialog.DialogResult = DialogResult.OK; dialog.Close();
        };
        panel.Controls.Add(save); dialog.Controls.Add(panel);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    internal static void ExportAppearance(Form owner, AppSettings settings)
    {
        using var picker = new SaveFileDialog { Title = "Simpan pengaturan tampilan", Filter = "Pengaturan HY Duo (*.json)|*.json", FileName = "HY-Duo-pengaturan.json", AddExtension = true };
        if (picker.ShowDialog(owner) != DialogResult.OK) return;
        try { File.WriteAllText(picker.FileName, AppearanceBackup.Export(settings)); MessageBox.Show(owner, "Pengaturan tampilan disimpan. Sesi, PIN, dan isi chat tidak disertakan.", "Pengaturan"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { MessageBox.Show(owner, "Pengaturan tidak dapat disimpan.\n\n" + ex.Message, "Pengaturan"); }
    }

    internal static bool ImportAppearance(Form owner, AppSettings settings)
    {
        using var picker = new OpenFileDialog { Title = "Muat pengaturan tampilan", Filter = "Pengaturan HY Duo (*.json)|*.json", CheckFileExists = true };
        if (picker.ShowDialog(owner) != DialogResult.OK) return false;
        try
        {
            if (new FileInfo(picker.FileName).Length > 32768) throw new ArgumentException("File terlalu besar.");
            AppearanceBackup.Import(settings, File.ReadAllText(picker.FileName));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { MessageBox.Show(owner, "Pengaturan tidak dapat dibaca.\n\n" + ex.Message, "Pengaturan"); return false; }
    }
    private static string Display(int keys) => new KeysConverter().ConvertToString((Keys)keys) ?? ((Keys)keys).ToString();
}
