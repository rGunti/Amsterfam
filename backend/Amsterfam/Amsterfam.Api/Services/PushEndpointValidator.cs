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

    /// <summary>An uncompressed P-256 public key: 0x04, then 32 bytes each of x and y.</summary>
    private const int P256dhLength = 65;

    private const int AuthLength = 16;

    [GeneratedRegex("^[A-Za-z0-9_-]+={0,2}$")]
    private static partial Regex Base64Url();

    /// <summary>Returns null when the subscription is fine, otherwise why it isn't.</summary>
    public static string? Validate(string? endpoint, string? p256dh, string? auth)
    {
        if (ValidateEndpoint(endpoint) is { } error)
            return error;
        // Keys that don't fit would only fail later, when a post is encrypted for the device.
        if (Decode(p256dh) is not { Length: P256dhLength } key || key[0] != 0x04)
            return "The subscription's p256dh key is missing or malformed.";
        if (Decode(auth) is not { Length: AuthLength })
            return "The subscription's auth secret is missing or malformed.";
        return null;
    }

    private static byte[]? Decode(string? base64Url)
    {
        if (base64Url is null || base64Url.Length > PushSubscription.MaxKeyLength)
            return null;
        if (!Base64Url().IsMatch(base64Url))
            return null;
        var base64 = base64Url.TrimEnd('=').Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
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
        if (
            !Hosts.Contains(host)
            && !HostSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal))
        )
            return "This browser's push service isn't supported.";
        return null;
    }
}
