namespace Amsterfam.Core.Entities;

/// <summary>
/// One device's web push subscription (ADR-017). Having one is the opt-in; there's no separate
/// preference. Not tied to an event: a member gets pushes for every event they're confirmed in.
/// </summary>
public class PushSubscription
{
    public const int MaxEndpointLength = 1024;
    public const int MaxKeyLength = 200;

    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>The push service URL for this device; unique.</summary>
    public required string Endpoint { get; set; }

    /// <summary>The browser's ECDH public key (base64url), for encrypting payloads.</summary>
    public required string P256dh { get; set; }

    /// <summary>The browser's auth secret (base64url), for encrypting payloads.</summary>
    public required string Auth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }

    public User User { get; set; } = null!;
}
