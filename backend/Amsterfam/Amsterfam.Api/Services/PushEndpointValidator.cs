using System.Text.RegularExpressions;
using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Services;

/// <summary>
/// Checks a browser push subscription before it's stored (ADR-017). The server later posts to
/// the endpoint, so like Discord webhooks (ADR-015) only the browsers' own push services are
/// accepted: https, default port, no user info, a known host.
/// </summary>
public static partial class PushEndpointValidator
{
    private static readonly string[] Hosts =
    [
        "fcm.googleapis.com", // Chrome, Edge, Android
        "updates.push.services.mozilla.com", // Firefox
        "web.push.apple.com", // Safari
    ];

    private static readonly string[] HostSuffixes =
    [
        ".push.services.mozilla.com",
        ".notify.windows.com",
        ".push.apple.com",
    ];

    /// <summary>A P-256 public key is 65 bytes (87 base64url chars); the auth secret 16 (22).</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{16,200}={0,2}$")]
    private static partial Regex Base64Url();

    /// <summary>Returns null when the subscription is fine, otherwise why it isn't.</summary>
    public static string? Validate(string? endpoint, string? p256dh, string? auth)
    {
        if (ValidateEndpoint(endpoint) is { } error)
            return error;
        if (p256dh is null || !Base64Url().IsMatch(p256dh))
            return "The subscription's p256dh key is missing or malformed.";
        if (auth is null || !Base64Url().IsMatch(auth))
            return "The subscription's auth secret is missing or malformed.";
        return null;
    }

    public static string? ValidateEndpoint(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint))
            return "The subscription has no endpoint.";
        if (endpoint.Length > PushSubscription.MaxEndpointLength)
            return "The subscription's endpoint is too long.";
        if (
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
        )
            return "The subscription's endpoint must be an https URL.";

        var host = uri.IdnHost.ToLowerInvariant();
        if (!Hosts.Contains(host) && !HostSuffixes.Any(host.EndsWith))
            return "This browser's push service isn't supported.";
        return null;
    }
}
