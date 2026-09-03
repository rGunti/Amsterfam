namespace Amsterfam.Api.Dtos;

public record UpdatePollRangeRequest(DateOnly? PollRangeStart, DateOnly? PollRangeEnd);

public record DatePollEntryDto(DateOnly WeekStart, string Status);

public record UpdateDatePollEntriesRequest(List<DatePollEntryDto> Entries);

public record DatePollWeekSummary(
    DateOnly WeekStart,
    int Available,
    int Unavailable,
    int Partial,
    int NoResponse
);

public record DatePollSummaryResponse(
    DateOnly? PollRangeStart,
    DateOnly? PollRangeEnd,
    List<DatePollWeekSummary> Weeks
);
