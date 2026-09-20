namespace Amsterfam.Api.Dtos;

public record AttendeeResponse(
    int UserId,
    string DisplayName,
    string? AvatarUrl,
    string Role,
    DateOnly? PlannedArrival,
    DateOnly? PlannedDeparture,
    bool RequestedOrganiser
);

public record UpdateAttendanceRequest(
    DateOnly? PlannedArrival,
    DateOnly? PlannedDeparture,
    decimal? CostOverride
);
