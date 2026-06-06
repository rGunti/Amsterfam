namespace Amsterfam.Api.Dtos;

public record AttendeeResponse(
    int UserId,
    string DisplayName,
    string Role,
    DateOnly? PlannedArrival,
    DateOnly? PlannedDeparture
);

public record UpdateAttendanceRequest(
    DateOnly? PlannedArrival,
    DateOnly? PlannedDeparture,
    decimal? CostOverride
);
