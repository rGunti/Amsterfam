namespace Amsterfam.Api.Dtos;

public record NewsPostResponse(
    int Id,
    string? Title,
    string Body,
    TimelineUser Author,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? EditedAt,
    bool CanEdit
);

/// <summary>A page of the feed, newest first, plus when the caller last opened it.</summary>
public record NewsFeedResponse(IReadOnlyList<NewsPostResponse> Posts, DateTimeOffset? SeenAt);

/// <summary>An empty or blank title means "no title".</summary>
public record UpsertNewsPostRequest(string? Title, string Body);
