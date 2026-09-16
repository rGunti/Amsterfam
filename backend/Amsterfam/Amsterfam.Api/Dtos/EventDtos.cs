using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Dtos;

public record EventResponse(
    Guid Id,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Location,
    DateOnly? PollRangeStart,
    DateOnly? PollRangeEnd,
    decimal? CostPerNight,
    string Status,
    DateTime CreatedAt,
    string? CurrentUserRole,
    bool IsMember,
    int CreatedById,
    IReadOnlyList<OrganiserSummary> Organisers
);

public record OrganiserSummary(int UserId, string DisplayName, string? AvatarUrl);

public record CreateEventRequest(
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Location,
    decimal? CostPerNight
);

public record UpdateEventRequest(
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Location,
    decimal? CostPerNight
);
