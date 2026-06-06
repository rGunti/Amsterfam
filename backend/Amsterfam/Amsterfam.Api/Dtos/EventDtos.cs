using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Dtos;

public record EventResponse(
    int Id,
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    string Location,
    decimal CostPerNight,
    string Status,
    DateTime CreatedAt
);

public record CreateEventRequest(
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    string Location,
    decimal CostPerNight
);

public record UpdateEventRequest(
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    string Location,
    decimal CostPerNight
);
