using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DuoDesk;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(27, 41, 58);
    private static readonly Color Muted = Color.FromArgb(109, 124, 142);
    private static readonly Color Background = Color.FromArgb(242, 244, 247);
    private static readonly Color Green = Color.FromArgb(39, 174, 117);
    private static readonly Color Purple = Color.FromArgb(125, 121, 216);
    private readonly string dataDirectory;
    private readonly string settingsPath;
    private readonly string pinPath;
    private readonly Uri page;
    private readonly bool isTest;
    private readonly AppSettings settings;
    private readonly NotifyIcon tray;
    private readonly NotificationBridge notifications;
    private readonly Panel contentHost = new() { Dock = DockStyle.Fill, BackColor = Background, Padding = new Padding(16) };
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 14, BackColor = Background };
    private readonly Panel lockedCover = new() { Dock = DockStyle.Fill, BackColor = Background, Visible = false };
    private readonly Dictionary<string, BrowserPane> panes = new();
    private readonly Button personalButton = MakeButton("Pribadi", 100);
    private readonly Button businessButton = MakeButton("Usaha", 100);
    private readonly Button splitButton = MakeButton("Berdampingan", 130);
    private readonly Button lockButton = MakeButton("Kunci", 75);
    private readonly Button optionsButton = MakeButton("Pengaturan", 105);
    private readonly Button hideButton = MakeButton("Sembunyikan", 115);
    private readonly Label subtitle = new() { AutoSize = true, ForeColor = Muted, Text = "Pribadi dan Usaha, dalam satu ruang", Font = new Font("Segoe UI", 9F) };
    private readonly ContextMenuStrip options = new();
    private bool quitting;
    private bool locked;
    private bool initialized;
    private bool adjustingLayout;

    public MainForm(string dataDirectory, Uri? testPage)
    {
        this.dataDirectory = dataDirectory;
        isTest = testPage is not null;
        page = testPage ?? new Uri("https://web.whatsapp.com/");
        settingsPath = Path.Combine(dataDirectory, "settings.json");
        pinPath = Path.Combine(dataDirectory, "pin.json");
        settings = SettingsStore.Load(settingsPath);
        if (settings.ActiveAccount is not ("personal" or "business")) settings.ActiveAccount = "personal";
        if (settings.LayoutMode is not ("tabs" or "split")) settings.LayoutMode = "split";
        locked = PinLock.IsConfigured(pinPath);

        Text = isTest ? "DuoDesk — pengujian lokal" : "HY Duo WhatsApp";
        Name = "DuoDeskMainWindow";
        Font = new Font("Segoe UI", 9F);
        BackColor = Background;
        ForeColor = Ink;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1500, 900);
        MinimumSize = new Size(820, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Background, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateTitleBar(), 0, 0);
        root.Controls.Add(CreateToolbar(), 0, 1);
        contentHost.Controls.Add(split);
        contentHost.Controls.Add(lockedCover);
        root.Controls.Add(contentHost, 0, 2);
        Controls.Add(root);
        BuildLockedCover();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Buka HY Duo WhatsApp", null, (_, _) => RestoreWindow());
        trayMenu.Items.Add("Pribadi", null, (_, _) => SelectAccount("personal"));
        trayMenu.Items.Add("Usaha", null, (_, _) => SelectAccount("business"));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Keluar sepenuhnya", null, (_, _) => ExitApplication());
        tray = new NotifyIcon { Text = "HY Duo WhatsApp", Icon = Icon, Visible = true, ContextMenuStrip = trayMenu };
        tray.DoubleClick += (_, _) => RestoreWindow();
        notifications = new NotificationBridge(tray, SelectAccount, SetAccountStatus, page.GetLeftPart(UriPartial.Authority));

        panes["personal"] = new BrowserPane("personal", "Pribadi", Purple, this);
        panes["business"] = new BrowserPane("business", "Usaha", Green, this);
        split.Panel1.Controls.Add(panes["personal"]);
        split.Panel2.Controls.Add(panes["business"]);

        personalButton.Click += (_, _) => SelectAccount("personal");
        businessButton.Click += (_, _) => SelectAccount("business");
        splitButton.Click += (_, _) => { settings.LayoutMode = "split"; ApplyLayout(); SaveSettings(); };
        lockButton.Click += (_, _) => LockWorkspace();
        optionsButton.Click += (_, _) => options.Show(optionsButton, new Point(0, optionsButton.Height));
        hideButton.Click += (_, _) => Hide();
        ConfigureMenu();
        split.SizeChanged += (_, _) => CenterSplitter();
        FormClosing += OnClosing;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Shown += async (_, _) => await InitializeBrowsersAsync();
        ApplyLayout();
        ApplyLock();
    }

    private Control CreateTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(24, 12, 20, 8) };
        var mark = new Label { Text = "HY", TextAlign = ContentAlignment.MiddleCenter, BackColor = Green, ForeColor = Color.White, Font = new Font("Segoe UI", 12F, FontStyle.Bold), Bounds = new Rectangle(24, 16, 46, 46) };
        mark.Region = RoundedRegion(mark.ClientRectangle, 13);
        var title = new Label { AutoSize = true, Text = "HY Duo WhatsApp", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = Ink, Location = new Point(84, 13) };
        subtitle.Location = new Point(87, 46);
        bar.Controls.Add(mark);
        bar.Controls.Add(title);
        bar.Controls.Add(subtitle);
        return bar;
    }

    private Control CreateToolbar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(20, 8, 12, 8), BackColor = Color.White };
        foreach (var button in new[] { personalButton, businessButton, splitButton, optionsButton, lockButton, hideButton }) bar.Controls.Add(button);
        return bar;
    }

    private static Button MakeButton(string text, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 36, Margin = new Padding(3, 2, 5, 2), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(237, 240, 244), ForeColor = Ink, UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(225, 231, 237);
        void UpdateShape()
        {
            var previous = button.Region;
            button.Region = RoundedRegion(button.ClientRectangle, 11);
            previous?.Dispose();
        }
        button.SizeChanged += (_, _) => UpdateShape();
        UpdateShape();
        return button;
    }

    private static Region RoundedRegion(Rectangle rectangle, int radius)
    {
        using var path = new GraphicsPath();
        if (rectangle.Width <= 1 || rectangle.Height <= 1) return new Region(rectangle);
        var diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }

    private void ConfigureMenu()
    {
        options.Items.Add("Folder unduhan Pribadi…", null, (_, _) => ChooseFolder("personal"));
        options.Items.Add("Folder unduhan Usaha…", null, (_, _) => ChooseFolder("business"));
        options.Items.Add(new ToolStripSeparator());
        options.Items.Add("Buka folder Pribadi", null, (_, _) => OpenFolder("personal"));
        options.Items.Add("Buka folder Usaha", null, (_, _) => OpenFolder("business"));
        options.Items.Add(new ToolStripSeparator());
        options.Items.Add("Hapus PIN…", null, (_, _) =>
        {
            if (!PinLock.IsConfigured(pinPath)) { MessageBox.Show(this, "PIN belum diatur.", "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (PinLock.RemovePin(this, pinPath)) MessageBox.Show(this, "PIN berhasil dihapus.", "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        options.Items.Add(new ToolStripSeparator());
        options.Items.Add("Keluar sepenuhnya", null, (_, _) => ExitApplication());
    }

    private void BuildLockedCover()
    {
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var card = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(15), BackColor = Color.White };
        card.Controls.Add(new Label { Text = "HY Duo WhatsApp terkunci", ForeColor = Ink, Font = new Font("Segoe UI", 15F, FontStyle.Bold), AutoSize = true, Margin = new Padding(3, 3, 3, 8) });
        card.Controls.Add(new Label { Text = "Masukkan PIN untuk membuka kedua akun.", ForeColor = Muted, AutoSize = true, Margin = new Padding(3, 0, 3, 12) });
        var unlock = MakeButton("Buka kunci", 130);
        unlock.Click += (_, _) => { if (PinLock.Unlock(this, pinPath)) { locked = false; ApplyLock(); } };
        card.Controls.Add(unlock);
        center.Controls.Add(card, 1, 1);
        lockedCover.Controls.Add(center);
    }

    private async Task InitializeBrowsersAsync()
    {
        if (initialized) return;
        initialized = true;
        await Task.WhenAll(InitializeAccountAsync("personal"), InitializeAccountAsync("business"));
    }

    private async Task InitializeAccountAsync(string key)
    {
        var pane = panes[key];
        try
        {
            pane.SetStatus("Menyiapkan sesi…");
            string profilePath = AppPaths.ProfileDirectory(dataDirectory, key);
            Directory.CreateDirectory(profilePath);
            string downloadFolder = DownloadFolder(key);
            Directory.CreateDirectory(downloadFolder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profilePath);
            await pane.Browser.EnsureCoreWebView2Async(environment);
            var core = pane.Browser.CoreWebView2;
            core.Profile.DefaultDownloadFolderPath = downloadFolder;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            var navigation = new NavigationTracker();
            core.NavigationStarting += (_, e) =>
            {
                navigation.Start(e.NavigationId, e.Uri);
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) &&
                    target.Scheme is "http" or "https" &&
                    !string.Equals(target.GetLeftPart(UriPartial.Authority), page.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    navigation.CancelIntentionally(e.NavigationId);
                    if (!e.IsUserInitiated) { pane.SetStatus("Tautan luar diblokir"); return; }
                    try { Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { pane.SetStatus("Tidak dapat membuka tautan"); }
                }
            };
            core.DownloadStarting += (_, e) =>
            {
                if (navigation.Download(e.DownloadOperation.Uri)) pane.SetStatus("Halaman terbuka");
            };
            core.NavigationCompleted += async (_, e) =>
            {
                var navigationId = e.NavigationId;
                if (e.IsSuccess)
                {
                    if (navigation.IsCurrent(navigationId)) pane.SetStatus("Halaman terbuka");
                    return;
                }
                // WebView2 can report ConnectionAborted before DownloadStarting.
                // Allow that event to identify the same navigation before showing an error.
                await Task.Delay(1000);
                if (!IsDisposed && navigation.ShouldReportFailure(navigationId, isSuccess: false))
                    pane.SetStatus("Koneksi halaman bermasalah — muat ulang");
            };
            core.DocumentTitleChanged += (_, _) => UpdateUnread(key, core.DocumentTitle);
            core.ProcessFailed += (_, e) => pane.SetStatus(e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited
                ? "Sesi berhenti — keluar dan buka lagi"
                : "Halaman berhenti — muat ulang");
            notifications.Attach(core, key, pane.AccountName);
            core.Navigate(page.AbsoluteUri);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException or WebView2RuntimeNotFoundException)
        {
            pane.SetStatus("Tidak dapat membuka sesi. Muat ulang atau periksa WebView2.");
            MessageBox.Show(this, $"Akun {pane.AccountName} tidak dapat dibuka. Periksa folder unduhan dan Microsoft Edge WebView2 Runtime.\n\n{ex.Message}", "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateUnread(string key, string title)
    {
        // WhatsApp may expose an unread count in the page title; no message contents are read.
        var match = Regex.Match(title ?? "", @"^\s*\((\d{1,4})\)");
        var caption = key == "personal" ? "Pribadi" : "Usaha";
        var button = key == "personal" ? personalButton : businessButton;
        button.Text = match.Success ? caption + " (" + match.Groups[1].Value + ")" : caption;
    }

    private void SelectAccount(string key)
    {
        if (key is not ("personal" or "business")) return;
        settings.ActiveAccount = key;
        settings.LayoutMode = "tabs";
        ApplyLayout();
        SaveSettings();
        RestoreWindow();
    }

    private void ApplyLayout()
    {
        if (adjustingLayout) return;
        adjustingLayout = true;
        try
        {
            split.Panel1Collapsed = false;
            split.Panel2Collapsed = false;
            if (settings.LayoutMode == "tabs")
            {
                if (settings.ActiveAccount == "personal") split.Panel2Collapsed = true;
                else split.Panel1Collapsed = true;
            }
            HighlightButton(personalButton, settings.LayoutMode == "tabs" && settings.ActiveAccount == "personal", Purple);
            HighlightButton(businessButton, settings.LayoutMode == "tabs" && settings.ActiveAccount == "business", Green);
            HighlightButton(splitButton, settings.LayoutMode == "split", Green);
            CenterSplitter();
        }
        finally { adjustingLayout = false; }
    }

    private static void HighlightButton(Button button, bool selected, Color color)
    {
        button.BackColor = selected ? Color.FromArgb(218, 242, 231) : Color.FromArgb(237, 240, 244);
        button.ForeColor = selected ? Color.FromArgb(24, 117, 80) : Ink;
    }

    private void CenterSplitter()
    {
        if (settings.LayoutMode != "split" || split.Panel1Collapsed || split.Panel2Collapsed || split.Width < 300) return;
        try { split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(split.Width / 2, split.Width - split.Panel2MinSize - split.SplitterWidth)); }
        catch (InvalidOperationException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private string DownloadFolder(string key)
    {
        string? custom = key == "personal" ? settings.PersonalDownloadFolder : settings.BusinessDownloadFolder;
        if (!string.IsNullOrWhiteSpace(custom) && Path.IsPathFullyQualified(custom)) return custom;
        return isTest ? Path.Combine(dataDirectory, "downloads", key)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DuoDesk", key == "personal" ? "Pribadi" : "Usaha");
    }

    private void ChooseFolder(string key)
    {
        using var picker = new FolderBrowserDialog { Description = "Pilih folder unduhan untuk " + panes[key].AccountName, UseDescriptionForTitle = true, InitialDirectory = DownloadFolder(key) };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Directory.CreateDirectory(picker.SelectedPath);
            if (panes[key].Browser.CoreWebView2 is { } core) core.Profile.DefaultDownloadFolderPath = picker.SelectedPath;
            if (key == "personal") settings.PersonalDownloadFolder = picker.SelectedPath;
            else settings.BusinessDownloadFolder = picker.SelectedPath;
            SaveSettings();
            panes[key].SetStatus("Folder unduhan diperbarui");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, "Folder unduhan tidak dapat digunakan.\n\n" + ex.Message, "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void OpenFolder(string key)
    {
        try { var folder = DownloadFolder(key); Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        { MessageBox.Show(this, "Folder tidak dapat dibuka.\n\n" + ex.Message, "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void LockWorkspace()
    {
        if (locked) return;
        if (!PinLock.IsConfigured(pinPath) && !PinLock.TryCreatePin(this, pinPath)) return;
        locked = true;
        ApplyLock();
    }

    private void ApplyLock()
    {
        split.Visible = !locked;
        lockedCover.Visible = locked;
        if (locked) lockedCover.BringToFront(); else split.BringToFront();
        foreach (var button in new[] { personalButton, businessButton, splitButton, optionsButton, lockButton }) button.Enabled = !locked;
        subtitle.Text = locked ? "Ruang kerja terkunci" : "Pribadi dan Usaha, dalam satu ruang";
    }

    private void SetAccountStatus(string key, string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetAccountStatus(key, message)); return; }
        if (panes.TryGetValue(key, out var pane)) pane.SetStatus(message);
    }

    private void SaveSettings()
    {
        try { SettingsStore.Save(settingsPath, settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, "Pengaturan tidak dapat disimpan.\n\n" + ex.Message, "HY Duo WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void RestoreWindow()
    {
        if (IsDisposed) return;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        notifications.Dispose();
        tray.Visible = false;
        tray.Dispose();
    }

    private void ExitApplication() { quitting = true; Close(); }

    private sealed class BrowserPane : UserControl
    {
        public readonly WebView2 Browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
        private readonly Label status = new() { AutoSize = false, AutoEllipsis = true, ForeColor = Muted, Font = new Font("Segoe UI", 8.5F), Anchor = AnchorStyles.Left | AnchorStyles.Right, Location = new Point(21, 27), Height = 20 };
        public string AccountName { get; }

        public BrowserPane(string key, string name, Color accent, MainForm parent)
        {
            AccountName = name;
            Name = "Account_" + key;
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            Padding = new Padding(8, 0, 8, 8);
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 60, ColumnCount = 3, BackColor = Color.White, Padding = new Padding(8, 4, 4, 4) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titleArea = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var dot = new Panel { BackColor = accent, Bounds = new Rectangle(2, 13, 10, 10) };
            dot.Region = RoundedRegion(dot.ClientRectangle, 5);
            titleArea.Controls.Add(dot);
            titleArea.Controls.Add(new Label { Text = name, AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Ink, Location = new Point(19, 6) });
            status.Width = Math.Max(1, titleArea.Width - 21);
            titleArea.Controls.Add(status);
            header.Controls.Add(titleArea, 0, 0);
            var reload = MakeButton("Muat ulang", 90);
            reload.Click += (_, _) =>
            {
                try
                {
                    if (Browser.CoreWebView2 is { } core) { SetStatus("Memuat ulang…"); core.Reload(); }
                    else _ = parent.InitializeAccountAsync(key);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
                { SetStatus("Sesi berhenti — keluar dan buka lagi"); }
            };
            header.Controls.Add(reload, 1, 0);
            var folder = MakeButton("Folder", 68);
            folder.Click += (_, _) => parent.OpenFolder(key);
            header.Controls.Add(folder, 2, 0);
            Controls.Add(Browser);
            Controls.Add(header);
            SetStatus("Belum dibuka");
        }

        public void SetStatus(string text) => status.Text = text;
    }
}
