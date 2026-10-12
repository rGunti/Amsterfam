namespace Amsterfam.Api.Services;

/// <summary>Where the app is reachable, for links that leave it (feeds, notifications).</summary>
public class AppOptions
{
    public const string SectionName = "App";

    /// <summary>The frontend's public origin, e.g. <c>https://amsterfam.example</c>.</summary>
    public string PublicUrl { get; set; } = "";

    public bool HasValidPublicUrl() =>
        Uri.TryCreate(PublicUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public string BaseUrl => PublicUrl.TrimEnd('/');

    public string NewsUrl(Guid eventId) => $"{BaseUrl}/events/{eventId}/news";

    public string NewsPostUrl(Guid eventId, int postId) => $"{NewsUrl(eventId)}/{postId}";
}
