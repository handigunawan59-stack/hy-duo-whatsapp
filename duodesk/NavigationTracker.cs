namespace DuoDesk;

internal sealed class NavigationTracker
{
    private ulong? currentNavigationId;
    private string currentUri = "";
    private bool intentionalInterruption;

    public void Start(ulong navigationId, string uri)
    {
        if (currentNavigationId != navigationId) intentionalInterruption = false;
        currentNavigationId = navigationId;
        currentUri = uri;
    }

    public bool Download(string uri)
    {
        // A main-frame navigation can turn into a download after the abort event.
        if (currentNavigationId is null || !string.Equals(currentUri, uri, StringComparison.Ordinal)) return false;
        intentionalInterruption = true;
        return true;
    }

    public void CancelIntentionally(ulong navigationId)
    {
        if (currentNavigationId == navigationId) intentionalInterruption = true;
    }

    public bool IsCurrent(ulong navigationId) => currentNavigationId == navigationId;

    public bool ShouldReportFailure(ulong navigationId, bool isSuccess) =>
        IsCurrent(navigationId) && !isSuccess && !intentionalInterruption;
}
