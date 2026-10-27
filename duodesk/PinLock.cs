using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

namespace DuoDesk;

/// <summary>Provides a local application screen lock; it does not encrypt account data.</summary>
public static class PinLock
{
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const int Iterations = 210_000;
    private const int MaximumFileBytes = 4096;

    private sealed class StoredPin
    {
        public int Version { get; set; }
        public int Iterations { get; set; }
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
    }

    /// <summary>A malformed or inaccessible existing file still means a lock is configured.</summary>
    public static bool IsConfigured(string pinFilePath)
    {
        try
        {
            _ = File.GetAttributes(pinFilePath);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>Prompts for a new PIN and confirmation; existing PINs are never replaced.</summary>
    public static bool TryCreatePin(IWin32Window owner, string pinFilePath)
    {
        if (IsConfigured(pinFilePath))
        {
            MessageBox.Show(owner, "PIN sudah diatur. Hapus PIN lama sebelum membuat yang baru.", "Kunci aplikasi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        using var dialog = new PinDialog(PinDialogMode.Create, null);
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        if (CreatePinFile(pinFilePath, dialog.EnteredPin)) return true;
        MessageBox.Show(owner, "PIN tidak dapat disimpan. Periksa izin folder atau apakah PIN sudah diatur.", "Kunci aplikasi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    /// <summary>Prompts for the configured PIN. Cancel, missing, or unreadable PIN files return false.</summary>
    public static bool Unlock(IWin32Window owner, string pinFilePath)
    {
        if (!IsConfigured(pinFilePath)) return false;
        using var dialog = new PinDialog(PinDialogMode.Unlock, pin => VerifyPin(pinFilePath, pin));
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    /// <summary>Removes the PIN only after a successful verification.</summary>
    public static bool RemovePin(IWin32Window owner, string pinFilePath)
    {
        if (!IsConfigured(pinFilePath)) return false;
        using var dialog = new PinDialog(PinDialogMode.Remove, pin => VerifyPin(pinFilePath, pin));
        if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
        try
        {
            File.Delete(pinFilePath);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show(owner, "PIN tidak dapat dihapus. Periksa izin folder dan coba lagi.", "Kunci aplikasi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    internal static bool CreatePinFile(string path, string pin)
    {
        if (!IsValidPin(pin)) return false;
        byte[]? hash = null;
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
            hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashLength);
            var stored = new StoredPin { Version = 1, Iterations = Iterations, Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(hash) };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(stored);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(json);
            output.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or CryptographicException)
        {
            return false;
        }
        finally
        {
            if (hash is not null) CryptographicOperations.ZeroMemory(hash);
        }
    }

    internal static bool VerifyPin(string path, string pin)
    {
        if (!IsValidPin(pin)) return false;
        byte[]? actual = null;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length == 0 || input.Length > MaximumFileBytes) return false;
            StoredPin? stored = JsonSerializer.Deserialize<StoredPin>(input);
            if (stored is null || stored.Version != 1 || stored.Iterations < 100_000 || stored.Iterations > 1_000_000) return false;
            byte[] salt = Convert.FromBase64String(stored.Salt);
            byte[] expected = Convert.FromBase64String(stored.Hash);
            if (salt.Length != SaltLength || expected.Length != HashLength) return false;
            actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, stored.Iterations, HashAlgorithmName.SHA256, HashLength);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or FormatException or CryptographicException)
        {
            return false;
        }
        finally
        {
            if (actual is not null) CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static bool IsValidPin(string? pin) => pin is { Length: >= 6 and <= 12 } && pin.All(character => character is >= '0' and <= '9');

    private enum PinDialogMode { Create, Unlock, Remove }

    private sealed class PinDialog : Form
    {
        private readonly PinDialogMode mode;
        private readonly Func<string, bool>? verify;
        private readonly TextBox pinInput = new() { UseSystemPasswordChar = true, MaxLength = 12, Dock = DockStyle.Fill };
        private readonly TextBox confirmInput = new() { UseSystemPasswordChar = true, MaxLength = 12, Dock = DockStyle.Fill };
        private readonly Label errorLabel = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleLeft };
        public string EnteredPin { get; private set; } = "";

        public PinDialog(PinDialogMode mode, Func<string, bool>? verify)
        {
            this.mode = mode;
            this.verify = verify;
            Text = mode switch { PinDialogMode.Create => "Buat PIN", PinDialogMode.Remove => "Hapus PIN", _ => "Buka kunci aplikasi" };
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(410, mode == PinDialogMode.Create ? 250 : 205);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = mode == PinDialogMode.Create ? 6 : 5, Padding = new Padding(18), BackColor = Color.White };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (mode == PinDialogMode.Create) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Text = mode switch
            {
                PinDialogMode.Create => "Buat PIN 6–12 angka untuk mengunci tampilan aplikasi.",
                PinDialogMode.Remove => "Masukkan PIN saat ini untuk menghapus kunci aplikasi.",
                _ => "Masukkan PIN untuk membuka aplikasi."
            }};
            layout.Controls.Add(intro);
            layout.Controls.Add(InputRow("PIN", pinInput));
            if (mode == PinDialogMode.Create) layout.Controls.Add(InputRow("Ulangi PIN", confirmInput));
            layout.Controls.Add(errorLabel);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var accept = new Button { Text = mode switch { PinDialogMode.Create => "Simpan PIN", PinDialogMode.Remove => "Hapus PIN", _ => "Buka" }, AutoSize = true, MinimumSize = new Size(90, 34) };
            var cancel = new Button { Text = "Batal", AutoSize = true, MinimumSize = new Size(90, 34), DialogResult = DialogResult.Cancel };
            accept.Click += (_, _) => AcceptPin();
            buttons.Controls.Add(accept);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            AcceptButton = accept;
            CancelButton = cancel;
            pinInput.KeyPress += DigitsOnly;
            confirmInput.KeyPress += DigitsOnly;
            Shown += (_, _) => pinInput.Focus();
        }

        private static Control InputRow(string label, TextBox input)
        {
            var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 9) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 0, 0) });
            row.Controls.Add(input);
            return row;
        }

        private static void DigitsOnly(object? sender, KeyPressEventArgs eventArgs)
        {
            if (!char.IsControl(eventArgs.KeyChar) && eventArgs.KeyChar is not (>= '0' and <= '9')) eventArgs.Handled = true;
        }

        private void AcceptPin()
        {
            string pin = pinInput.Text;
            if (!IsValidPin(pin)) { errorLabel.Text = "PIN harus terdiri dari 6–12 angka."; pinInput.Focus(); return; }
            if (mode == PinDialogMode.Create && pin != confirmInput.Text) { errorLabel.Text = "PIN yang dimasukkan belum sama."; confirmInput.Focus(); return; }
            if (mode != PinDialogMode.Create && (verify is null || !verify(pin)))
            {
                errorLabel.Text = "PIN salah atau data PIN tidak dapat dibaca.";
                pinInput.Clear();
                pinInput.Focus();
                return;
            }
            EnteredPin = pin;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
