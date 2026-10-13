namespace Amsterfam.Api.Dtos;

/// <summary>Whether the event mirrors news to Discord. The URL itself is never returned.</summary>
public record DiscordWebhookStatus(bool Configured, string? Hint);

public record SetDiscordWebhookRequest(string? Url);
