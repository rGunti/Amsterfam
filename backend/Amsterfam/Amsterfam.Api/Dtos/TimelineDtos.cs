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
    JsonElement? Data,
    NewsPreview? News = null
);

/// <summary>
/// The current state of the post a <c>NewsPosted</c> entry is about, so the timeline can show
/// it as a block. Null once the post is deleted.
/// </summary>
public record NewsPreview(
    int PostId,
    string? Title,
    string Excerpt,
    bool Truncated,
    Guid? ImageFileId
);
