using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using Microsoft.Win32;
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
    private readonly DownloadCenter downloads;
    private readonly System.Windows.Forms.Timer idleTimer = new() { Interval = 5000 };
    private GlobalHotkeys? hotkeys;
    private readonly bool startHidden;
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
    private readonly Dictionary<Control, (Color Back, Color Fore)> themeDefaults = new();
    private bool quitting;
    private bool locked;
    private bool initialized;
    private bool adjustingLayout;

    public MainForm(string dataDirectory, Uri? testPage, bool startHidden = false)
    {
        this.dataDirectory = dataDirectory;
        this.startHidden = startHidden;
        downloads = new DownloadCenter(dataDirectory);
        isTest = testPage is not null;
        page = testPage ?? new Uri("https://web.whatsapp.com/");
        settingsPath = Path.Combine(dataDirectory, "settings.json");
        pinPath = Path.Combine(dataDirectory, "pin.json");
        settings = SettingsStore.Load(settingsPath);
        if (settings.ActiveAccount is not ("personal" or "business")) settings.ActiveAccount = "personal";
        if (settings.LayoutMode is not ("tabs" or "split")) settings.LayoutMode = "split";
        settings.PersonalLabel = LayoutPreferences.Label(settings.PersonalLabel, "Pribadi");
        settings.BusinessLabel = LayoutPreferences.Label(settings.BusinessLabel, "Usaha");
        settings.PersonalColor = LayoutPreferences.Color(settings.PersonalColor, "#7D79D8");
        settings.BusinessColor = LayoutPreferences.Color(settings.BusinessColor, "#27AE75");
        settings.SplitRatio = LayoutPreferences.Ratio(settings.SplitRatio);
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
        var personalTrayItem = trayMenu.Items.Add("Pribadi", null, (_, _) => SelectAccount("personal")); personalTrayItem.Name = "personal";
        var businessTrayItem = trayMenu.Items.Add("Usaha", null, (_, _) => SelectAccount("business")); businessTrayItem.Name = "business";
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Keluar sepenuhnya", null, (_, _) => ExitApplication());
        tray = new NotifyIcon { Text = "HY Duo WhatsApp", Icon = Icon, Visible = true, ContextMenuStrip = trayMenu };
        tray.DoubleClick += (_, _) => RestoreWindow();
        notifications = new NotificationBridge(tray, SelectAccount, SetAccountStatus, page.GetLeftPart(UriPartial.Authority));
        notifications.Configure(key => NotificationBridge.SoundForAccount(key, settings.PersonalSound, settings.BusinessSound), () => locked && settings.HidePreviewWhenLocked, key => key == "personal" ? settings.PersonalLabel : settings.BusinessLabel);

        panes["personal"] = new BrowserPane("personal", settings.PersonalLabel, ColorTranslator.FromHtml(settings.PersonalColor), this);
        panes["business"] = new BrowserPane("business", settings.BusinessLabel, ColorTranslator.FromHtml(settings.BusinessColor), this);
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
        split.SplitterMoved += (_, _) =>
        {
            if (adjustingLayout || settings.LayoutMode != "split" || split.Width <= 0 || !initialized) return;
            settings.SplitRatio = LayoutPreferences.Ratio((double)split.SplitterDistance / split.Width);
            SaveSettings();
        };
        FormClosing += OnClosing;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Shown += async (_, _) =>
        {
            hotkeys = new GlobalHotkeys(Handle, new HotkeyActions(RestoreWindow, () => SelectAccount("personal"), () => SelectAccount("business"), () => { settings.LayoutMode = "split"; ApplyLayout(); SaveSettings(); RestoreWindow(); }));
            var unavailable = hotkeys.Register();
            if (unavailable.Count > 0) subtitle.Text = "Pintasan tidak tersedia: " + string.Join(", ", unavailable);
            if (this.startHidden) BeginInvoke(Hide);
            await InitializeBrowsersAsync();
        };
        idleTimer.Tick += (_, _) =>
        {
            if (!IdleMonitor.ShouldLock(PinLock.IsConfigured(pinPath), locked, settings.AutoLockMinutes, IdleMonitor.GetIdleMilliseconds())) return;
            locked = true;
            foreach (Form dialog in OwnedForms.ToArray()) dialog.Close();
            ApplyLock();
        };
        idleTimer.Start();
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        ApplyLayout();
        ApplyLock();
        ApplyPersonalization();
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
        options.Items.Add("Tampilan dan nama akun…", null, (_, _) => ShowPersonalization());
        options.Items.Add("Notifikasi dan kunci otomatis…", null, (_, _) => { if (NotificationOptionsDialog.Show(this, settings, notifications, key => key == "personal" ? settings.PersonalLabel : settings.BusinessLabel, key => panes[key].StatusText)) SaveSettings(); });
        options.Items.Add("Pusat unduhan…", null, (_, _) => downloads.Show(this, key => key == "personal" ? settings.PersonalLabel : settings.BusinessLabel));
        options.Items.Add("Startup dan pintasan…", null, (_, _) => ShowStartup());
        options.Items.Add("Periksa pembaruan…", null, async (_, _) => await CheckUpdatesAsync());
        options.Items.Add(new ToolStripSeparator());
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
            downloads.Attach(core, key);
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
        var caption = key == "personal" ? settings.PersonalLabel : settings.BusinessLabel;
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

    private void HighlightButton(Button button, bool selected, Color color)
    {
        bool dark = IsDarkTheme();
        button.BackColor = selected ? (dark ? Color.FromArgb(33, 86, 64) : Color.FromArgb(218, 242, 231)) : (dark ? Color.FromArgb(48, 52, 60) : Color.FromArgb(237, 240, 244));
        button.ForeColor = selected ? (dark ? Color.FromArgb(190, 240, 210) : Color.FromArgb(24, 117, 80)) : (dark ? Color.WhiteSmoke : Ink);
    }

    private void CenterSplitter()
    {
        if (settings.LayoutMode != "split" || split.Panel1Collapsed || split.Panel2Collapsed || split.Width < 300) return;
        try
        {
            bool wasAdjusting = adjustingLayout;
            adjustingLayout = true;
            try { split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min((int)(split.Width * LayoutPreferences.Ratio(settings.SplitRatio)), split.Width - split.Panel2MinSize - split.SplitterWidth)); }
            finally { adjustingLayout = wasAdjusting; }
        }
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

    private bool IsDarkTheme()
    {
        bool systemDark = false;
        try { systemDark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        return LayoutPreferences.IsDark(settings.Theme, systemDark);
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (settings.Theme != "system" || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(ApplyPersonalization); }
        catch (InvalidOperationException) { }
    }

    private void ApplyPersonalization()
    {
        panes["personal"].SetIdentity(settings.PersonalLabel, ColorTranslator.FromHtml(settings.PersonalColor));
        panes["business"].SetIdentity(settings.BusinessLabel, ColorTranslator.FromHtml(settings.BusinessColor));
        if (tray.ContextMenuStrip?.Items["personal"] is { } first) first.Text = settings.PersonalLabel;
        if (tray.ContextMenuStrip?.Items["business"] is { } second) second.Text = settings.BusinessLabel;
        UpdateUnread("personal", panes["personal"].Browser.CoreWebView2?.DocumentTitle ?? "");
        UpdateUnread("business", panes["business"].Browser.CoreWebView2?.DocumentTitle ?? "");
        bool dark = IsDarkTheme();
        void Paint(Control c)
        {
            if (c is WebView2 || c.Name == "AccountAccent") return;
            if (!themeDefaults.ContainsKey(c)) themeDefaults[c] = (c.BackColor, c.ForeColor);
            var original = themeDefaults[c];
            if (dark)
            {
                if (original.Back.A > 0 && original.Back.R > 220 && original.Back.G > 220 && original.Back.B > 220)
                    c.BackColor = c is Button ? Color.FromArgb(48, 52, 60) : Color.FromArgb(31, 34, 40);
                else c.BackColor = original.Back;
                if (original.Fore.A > 0 && original.Fore.GetBrightness() < .65f) c.ForeColor = Color.FromArgb(229, 232, 237);
            }
            else { c.BackColor = original.Back; c.ForeColor = original.Fore; }
            foreach (Control child in c.Controls) Paint(child);
        }
        Paint(this);
        HighlightButton(personalButton, settings.LayoutMode == "tabs" && settings.ActiveAccount == "personal", Purple);
        HighlightButton(businessButton, settings.LayoutMode == "tabs" && settings.ActiveAccount == "business", Green);
        HighlightButton(splitButton, settings.LayoutMode == "split", Green);
    }

    private void ShowPersonalization()
    {
        using var dialog = new Form { Text = "Tampilan dan nama akun", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(470, 260), Font = Font };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 3, RowCount = 5 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        for (int i = 0; i < 4; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var personalName = new TextBox { Text = settings.PersonalLabel, MaxLength = 24, Dock = DockStyle.Fill };
        var businessName = new TextBox { Text = settings.BusinessLabel, MaxLength = 24, Dock = DockStyle.Fill };
        string personalColor = settings.PersonalColor, businessColor = settings.BusinessColor;
        var personalColorButton = MakeButton("Warna", 70);
        var businessColorButton = MakeButton("Warna", 70);
        personalColorButton.Click += (_, _) => { using var picker = new ColorDialog { Color = ColorTranslator.FromHtml(personalColor), FullOpen = true }; if (picker.ShowDialog(dialog) == DialogResult.OK) personalColor = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; };
        businessColorButton.Click += (_, _) => { using var picker = new ColorDialog { Color = ColorTranslator.FromHtml(businessColor), FullOpen = true }; if (picker.ShowDialog(dialog) == DialogResult.OK) businessColor = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; };
        table.Controls.Add(new Label { Text = "Akun pertama", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(personalName, 1, 0); table.Controls.Add(personalColorButton, 2, 0);
        table.Controls.Add(new Label { Text = "Akun kedua", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(businessName, 1, 1); table.Controls.Add(businessColorButton, 2, 1);
        var themes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        themes.Items.AddRange(new object[] { "Ikuti Windows", "Terang", "Gelap" });
        themes.SelectedIndex = settings.Theme == "light" ? 1 : settings.Theme == "dark" ? 2 : 0;
        table.Controls.Add(new Label { Text = "Tema aplikasi", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(themes, 1, 2);
        var note = new Label { Text = "Tema WhatsApp diatur di dalam WhatsApp.", AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Left };
        table.Controls.Add(note, 1, 3); table.SetColumnSpan(note, 2);
        var save = MakeButton("Simpan", 80); save.DialogResult = DialogResult.OK; save.Anchor = AnchorStyles.Right;
        table.Controls.Add(save, 2, 4); dialog.Controls.Add(table); dialog.AcceptButton = save;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        settings.PersonalLabel = LayoutPreferences.Label(personalName.Text, "Pribadi");
        settings.BusinessLabel = LayoutPreferences.Label(businessName.Text, "Usaha");
        settings.PersonalColor = LayoutPreferences.Color(personalColor, "#7D79D8");
        settings.BusinessColor = LayoutPreferences.Color(businessColor, "#27AE75");
        settings.Theme = themes.SelectedIndex == 1 ? "light" : themes.SelectedIndex == 2 ? "dark" : "system";
        ApplyPersonalization(); SaveSettings();
    }

    private void ShowStartup()
    {
        bool isEnabled, startsHidden;
        try { isEnabled = StartupRegistration.IsEnabled(); startsHidden = StartupRegistration.StartsHidden(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { MessageBox.Show(this, "Pengaturan startup tidak dapat dibaca.\n\n" + ex.Message, "Startup", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        using var dialog = new Form { Text = "Startup dan pintasan", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(470, 250), Font = Font };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var enabled = new CheckBox { Text = "Jalankan saat masuk Windows", AutoSize = true, Checked = isEnabled };
        var hidden = new CheckBox { Text = "Mulai tersembunyi di tray", AutoSize = true, Checked = startsHidden };
        panel.Controls.Add(enabled); panel.Controls.Add(hidden);
        panel.Controls.Add(new Label { Text = "Pintasan: Ctrl+Alt+Shift+W buka aplikasi\nCtrl+Alt+Shift+1 / 2 memilih akun\nCtrl+Alt+Shift+3 menampilkan berdampingan", AutoSize = true, Margin = new Padding(3, 15, 3, 12) });
        var save = MakeButton("Simpan", 90); save.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(enabled.Checked, hidden.Checked); dialog.Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { MessageBox.Show(dialog, "Pengaturan startup tidak dapat disimpan.\n\n" + ex.Message, dialog.Text); }
        };
        panel.Controls.Add(save); dialog.Controls.Add(panel); dialog.ShowDialog(this);
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            var release = await ReleaseChecker.CheckAsync();
            if (IsDisposed) return;
            if (release is null) { MessageBox.Show(this, "Belum ada versi yang lebih baru.", "Pembaruan", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (MessageBox.Show(this, $"Versi {release.Tag} tersedia. Buka halaman unduhan GitHub?", "Pembaruan", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { if (!IsDisposed) MessageBox.Show(this, "Pembaruan tidak dapat diperiksa. Periksa koneksi internet.", "Pembaruan", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        downloads.Dispose();
        idleTimer.Dispose();
        hotkeys?.Dispose();
        tray.Visible = false;
        tray.Dispose();
    }

    private void ExitApplication() { quitting = true; Close(); }

    private sealed class BrowserPane : UserControl
    {
        public readonly WebView2 Browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
        private readonly Label status = new() { AutoSize = false, AutoEllipsis = true, ForeColor = Muted, Font = new Font("Segoe UI", 8.5F), Anchor = AnchorStyles.Left | AnchorStyles.Right, Location = new Point(21, 27), Height = 20 };
        private readonly Label accountTitle;
        private readonly Panel accountDot;
        public string AccountName { get; private set; }

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
            accountDot = new Panel { Name = "AccountAccent", BackColor = accent, Bounds = new Rectangle(2, 13, 10, 10) };
            accountDot.Region = RoundedRegion(accountDot.ClientRectangle, 5);
            titleArea.Controls.Add(accountDot);
            accountTitle = new Label { Text = name, AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Ink, Location = new Point(19, 6) };
            titleArea.Controls.Add(accountTitle);
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
        public string StatusText => status.Text;
        public void SetIdentity(string name, Color color) { AccountName = name; accountTitle.Text = name; accountDot.BackColor = color; }
    }
}
