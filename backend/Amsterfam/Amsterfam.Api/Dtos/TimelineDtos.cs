using System.Text.Json;

namespace Amsterfam.Api.Dtos;

public record TimelineUser(int Id, string DisplayName, string? AvatarUrl);

public record TimelineEntryResponse(
    long Id,
    string Type,
    string Visibility,
    DateTimeOffset OccurredAt,
    TimelineUser? Actor,
    TimelineUser? Subject,
    JsonElement? Data
);
