namespace Amsterfam.Api.Dtos;

/// <summary>
/// <see cref="CanAcknowledge"/> is false for the post's author, who has nothing to confirm.
/// <see cref="AckCount"/> and <see cref="AckTotal"/> are only filled in for organisers.
/// </summary>
public record NewsPostResponse(
    int Id,
    string? Title,
    string Body,
    TimelineUser Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? EditedAt,
    bool CanEdit,
    DateTimeOffset? PinnedAt,
    bool RequiresAck,
    bool CanAcknowledge,
    bool AcknowledgedByMe,
    IReadOnlyList<NewsReactionCount> Reactions,
    IReadOnlyList<string> MyReactions,
    int? AckCount,
    int? AckTotal,
    Guid? ImageFileId
);

public record NewsReactionCount(string Emoji, int Count);

/// <summary>
/// A page of the feed, newest first, plus when the caller last opened it. The first page
/// starts with all pinned posts; later pages never contain them.
/// </summary>
public record NewsFeedResponse(IReadOnlyList<NewsPostResponse> Posts, DateTimeOffset? SeenAt);

/// <summary>
/// An empty or blank title means "no title". <see cref="Pin"/> only applies when creating;
/// <see cref="ResetAcks"/> only when editing, and clears everyone's acknowledgements.
/// </summary>
public record UpsertNewsPostRequest(
    string? Title,
    string Body,
    bool RequiresAck = false,
    bool Pin = false,
    bool ResetAcks = false
);

public record NewsAckEntry(TimelineUser User, DateTimeOffset At);

/// <summary>Who has acknowledged a post, and which confirmed members haven't yet.</summary>
public record NewsAcksResponse(
    IReadOnlyList<NewsAckEntry> Acknowledged,
    IReadOnlyList<TimelineUser> Missing
);

/// <summary>
/// For the event overview: pinned posts, the newest post, and posts still waiting for the
/// caller's acknowledgement. A post can appear in more than one list.
/// </summary>
public record NewsSummaryResponse(
    IReadOnlyList<NewsPostResponse> Pinned,
    NewsPostResponse? Latest,
    IReadOnlyList<NewsPostResponse> NeedsAck
);
