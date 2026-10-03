using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Dtos;

public record EventResponse(
    Guid Id,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Location,
    string Currency,
    DateOnly? PollRangeStart,
    DateOnly? PollRangeEnd,
    string Status,
    DateTime CreatedAt,
    string? CurrentUserRole,
    bool IsMember,
    int CreatedById,
    IReadOnlyList<OrganiserSummary> Organisers,
    IReadOnlyList<string> AllowedTransitions,
    bool AutoTransitionsPaused,
    Guid? BannerFileId = null,
    int? PendingAttendeeCount = null
);

public record OrganiserSummary(int UserId, string DisplayName, string? AvatarUrl);

public record CreateEventRequest(
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Location,
    string? Currency = null
);

/// <summary>A null <see cref="Currency"/> leaves the event's currency unchanged.</summary>
public record UpdateEventRequest(
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Location,
    string? Currency = null
);

public record TransitionEventRequest(string Target);
