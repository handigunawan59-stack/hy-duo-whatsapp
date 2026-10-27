using System.Media;
using Microsoft.Web.WebView2.Core;

namespace DuoDesk;

/// <summary>
/// Routes WebView2 notification requests to the desktop tray. The host owns the tray
/// and must keep both browser instances alive while the window is hidden.
/// </summary>
public sealed class NotificationBridge : IDisposable
{
    private readonly NotifyIcon tray;
    private readonly Action<string> selectAccount;
    private readonly Action<string, string> status;
    private readonly Action playSound;
    private readonly bool hasCustomSound;
    private Func<string, string> soundForAccount = _ => "Asterisk";
    private Func<bool> hidePreview = () => false;
    private Func<string, string>? labelForAccount;
    private Action<string, string?, string?>? historyRecorder;
    private readonly string allowedOrigin;
    private readonly Queue<PendingNotification> pending = new();
    private readonly List<Attachment> attachments = new();
    private readonly System.Windows.Forms.Timer displayTimeout;
    private PendingNotification? current;
    private bool disposed;

    public NotificationBridge(
        NotifyIcon tray,
        Action<string> selectAccount,
        Action<string, string> status,
        string allowedOrigin = "https://web.whatsapp.com")
        : this(tray, selectAccount, status, allowedOrigin, () => SystemSounds.Asterisk.Play())
    {
        hasCustomSound = false;
    }

    internal NotificationBridge(
        NotifyIcon tray,
        Action<string> selectAccount,
        Action<string, string> status,
        string allowedOrigin,
        Action playSound)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(selectAccount);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(playSound);

        if (!IsAllowedConfiguredOrigin(allowedOrigin))
            throw new ArgumentException("Only WhatsApp Web or an explicit loopback test origin is allowed.", nameof(allowedOrigin));

        this.tray = tray;
        this.selectAccount = selectAccount;
        this.status = status;
        this.playSound = playSound;
        hasCustomSound = true;
        this.allowedOrigin = allowedOrigin;
        displayTimeout = new System.Windows.Forms.Timer { Interval = 12000 };
        displayTimeout.Tick += OnDisplayTimeout;
        tray.BalloonTipShown += OnBalloonShown;
        tray.BalloonTipClicked += OnBalloonClicked;
        tray.BalloonTipClosed += OnBalloonClosed;
    }

    /// <summary>Use live settings, so changes take effect for subsequent notifications.</summary>
    public void Configure(Func<string, string> soundSelector, Func<bool> hideMessagePreview, Func<string, string>? labelSelector = null)
    {
        soundForAccount = soundSelector ?? throw new ArgumentNullException(nameof(soundSelector));
        hidePreview = hideMessagePreview ?? throw new ArgumentNullException(nameof(hideMessagePreview));
        labelForAccount = labelSelector;
    }

    /// <summary>History is opt-in; the host supplies a local recorder that enforces the user's preference.</summary>
    public void ConfigureHistory(Action<string, string?, string?>? recorder) => historyRecorder = recorder;

    public void TestNotification(string accountKey, string accountLabel, string? soundOverride = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (accountKey is not ("personal" or "business")) throw new ArgumentException("Unknown account.", nameof(accountKey));
        pending.Enqueue(new PendingNotification(accountKey, $"{accountLabel} • Tes notifikasi", "Ini adalah notifikasi uji HY Duo WhatsApp.", null));
        PlayAccountSound(accountKey, soundOverride);
        ShowNext();
    }

    private void PlayAccountSound(string accountKey, string? soundOverride = null)
    {
        try
        {
            if (hasCustomSound) { playSound(); return; }
            switch (soundOverride ?? soundForAccount(accountKey))
            {
                case "Beep": SystemSounds.Beep.Play(); break;
                case "Exclamation": SystemSounds.Exclamation.Play(); break;
                case "Hand": SystemSounds.Hand.Play(); break;
                case "Question": SystemSounds.Question.Play(); break;
                default: SystemSounds.Asterisk.Play(); break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { status(accountKey, "Suara notifikasi tidak tersedia"); }
    }

    public void Attach(CoreWebView2 core, string accountKey, string accountLabel)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(core);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountLabel);
        if (attachments.Any(item => ReferenceEquals(item.Core, core)))
            throw new InvalidOperationException("This browser instance is already attached.");

        EventHandler<CoreWebView2PermissionRequestedEventArgs> permission = (_, args) =>
        {
            if (args.PermissionKind != CoreWebView2PermissionKind.Notifications ||
                !IsTrustedOrigin(args.Uri, allowedOrigin)) return;
            args.State = CoreWebView2PermissionState.Allow;
            args.Handled = true;
            status(accountKey, "Izin notifikasi aktif");
        };

        EventHandler<CoreWebView2NotificationReceivedEventArgs> received = (_, args) =>
        {
            if (!IsTrustedOrigin(args.SenderOrigin, allowedOrigin)) return;
            try
            {
                var notification = args.Notification;
                try { historyRecorder?.Invoke(accountKey, notification.Title, notification.Body); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { status(accountKey, "Riwayat notifikasi tidak tersedia"); }
                var message = FormatMessage(labelForAccount?.Invoke(accountKey) ?? accountLabel, notification.Title, notification.Body);
                args.Handled = true;
                pending.Enqueue(new PendingNotification(accountKey, message.Title, message.Body, notification));
                PlayAccountSound(accountKey);
                ShowNext();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
            {
                // Notification contents are deliberately never included in status messages.
                status(accountKey, "Notifikasi tidak dapat ditampilkan");
            }
        };

        core.PermissionRequested += permission;
        core.NotificationReceived += received;
        attachments.Add(new Attachment(core, permission, received));
    }

    private void ShowNext()
    {
        if (disposed || current is not null || pending.Count == 0) return;
        current = pending.Dequeue();
        try
        {
            tray.ShowBalloonTip(10000, current.Title, PreviewBody(current.Body, hidePreview()), ToolTipIcon.None);
            displayTimeout.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            var failedAccount = current.AccountKey;
            current = null;
            status(failedAccount, "Notifikasi tidak dapat ditampilkan");
            ShowNext();
        }
    }

    private void OnBalloonShown(object? sender, EventArgs e)
    {
        if (current is null || current.BalloonShown) return;
        current.BalloonShown = true;
        displayTimeout.Stop();
        try
        {
            current.Notification?.ReportShown();
            current.ReportedShown = current.Notification is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            status(current.AccountKey, "Status notifikasi tidak tersedia");
        }
    }

    private void OnBalloonClicked(object? sender, EventArgs e)
    {
        var clicked = current;
        if (clicked is null) return;
        try
        {
            selectAccount(clicked.AccountKey);
            if (clicked.ReportedShown)
                clicked.Notification?.ReportClicked();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            status(clicked.AccountKey, "Status notifikasi tidak tersedia");
        }
        finally
        {
            clicked.Clicked = true;
            // A mouse click dismisses the balloon without a later close event.
            if (ReferenceEquals(current, clicked)) CompleteCurrent();
        }
    }

    private void OnBalloonClosed(object? sender, EventArgs e) => CompleteCurrent();
    private void OnDisplayTimeout(object? sender, EventArgs e)
    {
        // Windows, rather than the requested timeout, controls how long a visible
        // balloon remains on screen. Do not reassign its click to another account.
        displayTimeout.Stop();
        if (current?.BalloonShown == true) return;
        CompleteCurrent();
    }

    private void CompleteCurrent()
    {
        displayTimeout.Stop();
        var completed = current;
        current = null;
        if (completed is not null && completed.ReportedShown && !completed.Clicked)
        {
            try { completed.Notification?.ReportClosed(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
            {
                status(completed.AccountKey, "Status notifikasi tidak tersedia");
            }
        }
        ShowNext();
    }

    internal static (string Title, string Body) FormatMessage(string accountLabel, string? title, string? body)
    {
        var sender = string.IsNullOrWhiteSpace(title) ? "Pesan baru" : title.Trim();
        var content = string.IsNullOrWhiteSpace(body) ? "Pesan baru diterima." : body.Trim();
        return ($"{accountLabel} • {sender}", content);
    }

    internal static (string Title, string Body) FormatForPrivacy(string accountLabel, string? title, string? body, bool hidePreview)
    {
        var message = FormatMessage(accountLabel, title, body);
        return (message.Title, PreviewBody(message.Body, hidePreview));
    }

    private static string PreviewBody(string body, bool hidePreview)
        => hidePreview ? "Pesan baru diterima." : body;

    internal static string SoundForAccount(string accountKey, string personalSound, string businessSound)
        => accountKey switch { "personal" => personalSound, "business" => businessSound, _ => "Asterisk" };

    internal static bool IsTrustedOrigin(string source, string allowedOrigin)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var actual) ||
            !Uri.TryCreate(allowedOrigin, UriKind.Absolute, out var allowed) ||
            !string.IsNullOrEmpty(actual.UserInfo) || !string.IsNullOrEmpty(allowed.UserInfo))
            return false;
        return string.Equals(actual.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(actual.IdnHost, allowed.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               actual.Port == allowed.Port;
    }

    private static bool IsAllowedConfiguredOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            parsed.AbsolutePath != "/" || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
            return false;
        if (IsTrustedOrigin(origin, "https://web.whatsapp.com")) return true;
        return (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) &&
               (string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                parsed.Host == "127.0.0.1" || parsed.Host == "[::1]");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        displayTimeout.Stop();
        displayTimeout.Tick -= OnDisplayTimeout;
        displayTimeout.Dispose();
        tray.BalloonTipShown -= OnBalloonShown;
        tray.BalloonTipClicked -= OnBalloonClicked;
        tray.BalloonTipClosed -= OnBalloonClosed;
        foreach (var item in attachments)
        {
            try
            {
                item.Core.PermissionRequested -= item.Permission;
                item.Core.NotificationReceived -= item.Received;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
            {
                // The host may already have disposed a WebView during shutdown.
            }
        }
        attachments.Clear();
        pending.Clear();
        current = null;
    }

    private sealed record Attachment(CoreWebView2 Core,
        EventHandler<CoreWebView2PermissionRequestedEventArgs> Permission,
        EventHandler<CoreWebView2NotificationReceivedEventArgs> Received);

    private sealed class PendingNotification(string accountKey, string title, string body, CoreWebView2Notification? notification)
    {
        public string AccountKey { get; } = accountKey;
        public string Title { get; } = title;
        public string Body { get; } = body;
        public CoreWebView2Notification? Notification { get; } = notification;
        public bool BalloonShown { get; set; }
        public bool ReportedShown { get; set; }
        public bool Clicked { get; set; }
    }
}
