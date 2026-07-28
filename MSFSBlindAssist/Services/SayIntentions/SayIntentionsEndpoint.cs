using System.Net;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Builds SayIntentions SAPI URLs. The hostname is read from a file this app
/// does not own (flight.json), so it is validated before the user's API key is
/// ever attached: https only, and only on sayintentions.ai. Anything else
/// silently falls back to the documented default host.
///
/// The key stays a query parameter because that is how SAPI documents its auth.
/// Never log a built URL directly — pass it through <see cref="Redact"/> first.
/// </summary>
public static class SayIntentionsEndpoint
{
    public const string DefaultHost = "https://apipri.sayintentions.ai";
    private const string AllowedDomain = "sayintentions.ai";

    private static readonly Regex ApiKeyQuery = new(
        @"([?&]api_key=)[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAllowedHost(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return false;
        if (!Uri.TryCreate(hostname.Trim(), UriKind.Absolute, out Uri? uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;

        string host = uri.Host;
        return host.Equals(AllowedDomain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + AllowedDomain, StringComparison.OrdinalIgnoreCase);
    }

    public static string Build(string? hostname, string endpoint, string apiKey)
    {
        string host = IsAllowedHost(hostname) ? hostname!.Trim().TrimEnd('/') : DefaultHost;
        if (!host.EndsWith("/sapi", StringComparison.OrdinalIgnoreCase))
            host += "/sapi";

        return $"{host}/{endpoint}?api_key={WebUtility.UrlEncode(apiKey)}";
    }

    /// <summary>Strips the API key so a request URL can be written to the log.</summary>
    public static string Redact(string url) => ApiKeyQuery.Replace(url, "$1***");
}
