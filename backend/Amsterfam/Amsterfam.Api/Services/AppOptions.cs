namespace Amsterfam.Api.Services;

/// <summary>Where the app is reachable, for links that leave it (feeds, notifications).</summary>
public class AppOptions
{
    public const string SectionName = "App";

    /// <summary>The frontend's public origin, e.g. <c>https://amsterfam.example</c>.</summary>
    public string PublicUrl { get; set; } = "";

    /// <summary>
    /// Where the API is reachable from outside, for links to API routes (the Atom feed).
    /// Defaults to <see cref="PublicUrl"/>, which is right when a proxy serves <c>/api</c> on
    /// the app's origin; set it when they differ, as in local development.
    /// </summary>
    public string? ApiUrl { get; set; }

    public bool HasValidPublicUrl() =>
        IsHttpUrl(PublicUrl) && (string.IsNullOrEmpty(ApiUrl) || IsHttpUrl(ApiUrl));

    public string BaseUrl => PublicUrl.TrimEnd('/');

    public string ApiBaseUrl => string.IsNullOrEmpty(ApiUrl) ? BaseUrl : ApiUrl.TrimEnd('/');

    public string NewsUrl(Guid eventId) => $"{BaseUrl}/events/{eventId}/news";

    public string NewsPostUrl(Guid eventId, int postId) => $"{NewsUrl(eventId)}/{postId}";

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
