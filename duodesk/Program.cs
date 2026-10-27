using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DuoDesk;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var testArg = args.FirstOrDefault(a => a.StartsWith("--test-base-url=", StringComparison.Ordinal));
        var dataArg = args.FirstOrDefault(a => a.StartsWith("--data-dir=", StringComparison.Ordinal));
        Uri? testPage = null;
        if (testArg is not null && (!AppPaths.TryGetTestPage(testArg[16..], out testPage) || dataArg is null))
        {
            MessageBox.Show("Mode pengujian hanya dapat memakai alamat HTTP lokal dan folder data terpisah.", "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        string dataDirectory = dataArg is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuoDesk")
            : Path.GetFullPath(dataArg[11..]);
        if (testPage is not null && string.Equals(dataDirectory.TrimEnd(Path.DirectorySeparatorChar),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuoDesk").TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Folder pengujian harus berbeda dari folder akun aplikasi.", "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Directory.CreateDirectory(dataDirectory);
        // Separate test profiles do not interfere with the user's normal session.
        var identity = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\DuoDesk-{hash}-Activate");
        using var instance = new Mutex(true, $@"Local\DuoDesk-{hash}-Instance", out bool firstInstance);
        if (!firstInstance)
        {
            activation.Set();
            return;
        }

        // The first DuoDesk release predates the named mutex and cannot signal us.
        // Recognize its exact window and executable identity before opening its data.
        // Local fixture sessions must stay isolated from a real legacy session.
        if (testPage is null && ActivateLegacyInstance())
        {
            instance.ReleaseMutex();
            return;
        }

        using var mainForm = new MainForm(dataDirectory, testPage);
        // Create the handle before listening, so an early second launch is not lost.
        _ = mainForm.Handle;
        var listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
        {
            try
            {
                mainForm.BeginInvoke(() =>
                {
                    if (mainForm.IsDisposed) return;
                    mainForm.Show();
                    if (mainForm.WindowState == FormWindowState.Minimized) mainForm.WindowState = FormWindowState.Normal;
                    mainForm.Activate();
                });
            }
            catch (InvalidOperationException) { /* The application is closing. */ }
        }, null, Timeout.Infinite, false);
        try { Application.Run(mainForm); }
        finally
        {
            listener.Unregister(null);
            instance.ReleaseMutex();
        }
    }

    private static bool ActivateLegacyInstance()
    {
        var window = FindWindow(null, "DuoDesk — Dua Akun WhatsApp");
        if (window == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0 || processId == Environment.ProcessId) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            using var current = Process.GetCurrentProcess();
            string? path = process.MainModule?.FileName;
            if (process.SessionId != current.SessionId ||
                !string.Equals(process.ProcessName, "DuoDesk", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(path), "DuoDesk.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var version = FileVersionInfo.GetVersionInfo(path!);
            if (version.ProductName != "DuoDesk" || version.OriginalFilename != "DuoDesk.dll") return false;
            _ = ShowWindowAsync(window, 9); // SW_RESTORE, including a hidden tray window.
            _ = SetForegroundWindow(window);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
