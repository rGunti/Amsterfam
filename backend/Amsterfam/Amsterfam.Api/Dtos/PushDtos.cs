namespace Amsterfam.Api.Dtos;

/// <summary>The VAPID public key the browser subscribes with.</summary>
public record PushConfigResponse(string PublicKey);

public record PushSubscriptionKeys(string? P256dh, string? Auth);

/// <summary>
/// Shaped like the browser's <c>PushSubscription.toJSON()</c>, plus a name for the device.
/// The name is only used for a new subscription; re-saving keeps the one it has.
/// </summary>
public record SavePushSubscriptionRequest(
    string? Endpoint,
    PushSubscriptionKeys? Keys,
    string? Name = null
);

public record DeletePushSubscriptionRequest(string? Endpoint);

/// <summary>
/// One of the member's devices. The endpoint is only ever returned to its owner, so the app
/// can tell which entry is the device it's running on.
/// </summary>
public record PushDeviceResponse(
    int Id,
    string Name,
    string Endpoint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSuccessAt
);

public record RenamePushDeviceRequest(string? Name);
