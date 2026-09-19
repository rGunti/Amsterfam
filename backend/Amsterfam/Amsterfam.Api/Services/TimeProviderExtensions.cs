namespace Amsterfam.Api.Services;

public static class TimeProviderExtensions
{
    /// <summary>
    /// Today's date in the server's local timezone. Per-event timezones are tracked in #104.
    /// </summary>
    public static DateOnly Today(this TimeProvider time) =>
        DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
