using System.Net;

namespace DuoDesk;

internal static class AppPaths
{
    public static string ProfileDirectory(string dataDirectory, string accountKey)
    {
        if (accountKey is not ("personal" or "business"))
            throw new ArgumentException("Unknown account.", nameof(accountKey));
        return Path.Combine(dataDirectory, "profiles", accountKey);
    }

    public static bool TryGetTestPage(string value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttp || candidate.UserInfo.Length > 0)
            return false;
        if (candidate.Host != "localhost" &&
            !(IPAddress.TryParse(candidate.Host, out var address) && IPAddress.IsLoopback(address)))
            return false;
        uri = candidate;
        return true;
    }
}
