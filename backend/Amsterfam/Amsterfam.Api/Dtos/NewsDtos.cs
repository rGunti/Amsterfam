namespace Amsterfam.Api.Dtos;

/// <summary>
/// <see cref="CanAcknowledge"/> is false for the post's author, who has nothing to confirm.
/// <see cref="AckCount"/> and <see cref="AckTotal"/> are only filled in for organisers.
/// <see cref="NewCommentCount"/> counts other people's comments since the caller last opened
/// the feed.
/// </summary>
public record NewsPostResponse(
    int Id,
    string? Title,
    string Body,
    TimelineUser Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset PublishAt,
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
    Guid? ImageFileId,
    bool CommentsEnabled,
    int CommentCount,
    int NewCommentCount
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
/// <see cref="PublishAt"/> more than a minute ahead schedules the post (organisers only see
/// it until then); null or sooner means "now". It can't change once the post is out.
/// </summary>
public record UpsertNewsPostRequest(
    string? Title,
    string Body,
    bool RequiresAck = false,
    bool Pin = false,
    bool ResetAcks = false,
    bool CommentsEnabled = true,
    DateTimeOffset? PublishAt = null
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

/// <summary>
/// A comment on a post. <see cref="CanEdit"/> is for its author; <see cref="CanDelete"/> also
/// for organisers. Both are false on a read-only event.
/// </summary>
public record NewsCommentResponse(
    int Id,
    TimelineUser Author,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    bool CanEdit,
    bool CanDelete,
    IReadOnlyList<NewsReactionCount> Reactions,
    IReadOnlyList<string> MyReactions
);

public record UpsertNewsCommentRequest(string Body);

/// <summary>Whether the caller has a feed link for the event. The link itself is never shown again.</summary>
public record NewsFeedTokenStatus(
    bool Active,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastUsedAt
);

/// <summary>A new feed link, returned once when it's created.</summary>
public record NewsFeedTokenCreated(string Url, DateTimeOffset CreatedAt);
