namespace Amsterfam.Api.Dtos;

public record CreateJoinLinkRequest(
    string? Kind,
    DateTimeOffset? ExpiresAt,
    int? MaxUses,
    string? Label = null
);

public record JoinLinkResponse(
    int Id,
    string Token,
    string Kind,
    string Label,
    int CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    int? MaxUses,
    int UseCount,
    bool IsRevoked,
    bool IsUsable
);

public record JoinLinkPreviewResponse(
    Guid EventId,
    string EventName,
    string Location,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Kind,
    bool AlreadyMember
);
