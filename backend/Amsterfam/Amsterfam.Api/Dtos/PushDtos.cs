namespace Amsterfam.Api.Dtos;

/// <summary>The VAPID public key the browser subscribes with.</summary>
public record PushConfigResponse(string PublicKey);

public record PushSubscriptionKeys(string? P256dh, string? Auth);

/// <summary>Shaped like the browser's <c>PushSubscription.toJSON()</c>.</summary>
public record SavePushSubscriptionRequest(string? Endpoint, PushSubscriptionKeys? Keys);

public record DeletePushSubscriptionRequest(string? Endpoint);
