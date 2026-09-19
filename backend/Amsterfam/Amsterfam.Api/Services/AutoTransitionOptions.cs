using Cronos;

namespace Amsterfam.Api.Services;

public class AutoTransitionOptions
{
    public const string SectionName = "AutoTransitions";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Standard 5-field cron expression, evaluated in the server's local timezone.
    /// Transitions are date-based, so the default runs shortly after midnight.
    /// </summary>
    public string Schedule { get; set; } = "5 0 * * *";

    public bool HasValidSchedule() => CronExpression.TryParse(Schedule, out _);

    public CronExpression ParseSchedule() => CronExpression.Parse(Schedule);
}
