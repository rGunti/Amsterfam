namespace Amsterfam.Api.Services;

/// <summary>
/// VAPID settings for web push (ADR-017). Generate the keys once with
/// <c>npx web-push generate-vapid-keys</c> and keep them: new keys invalidate every
/// subscription. If any value is missing, the push channel is off.
/// </summary>
public class PushOptions
{
    public const string SectionName = "Push";

    public string? VapidPublicKey { get; set; }
    public string? VapidPrivateKey { get; set; }

    /// <summary>A contact for push services, e.g. <c>mailto:admin@example.com</c>.</summary>
    public string? Subject { get; set; }

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(VapidPublicKey)
        && !string.IsNullOrWhiteSpace(VapidPrivateKey)
        && !string.IsNullOrWhiteSpace(Subject);

    public bool HasValidSubject() =>
        !IsEnabled
        || Subject!.StartsWith("mailto:", StringComparison.Ordinal)
        || Subject.StartsWith("https://", StringComparison.Ordinal);
}
