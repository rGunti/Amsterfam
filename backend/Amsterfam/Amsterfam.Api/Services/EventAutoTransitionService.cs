using Microsoft.Extensions.Options;

namespace Amsterfam.Api.Services;

/// <summary>
/// Runs <see cref="EventAutoTransitioner"/> on the configured cron schedule, plus once
/// at startup to catch up on anything missed while the server was down.
/// </summary>
public class EventAutoTransitionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AutoTransitionOptions> options,
    TimeProvider time,
    ILogger<EventAutoTransitionService> logger
) : BackgroundService
{
    // Task.Delay can't wait longer than ~49 days, so long gaps are slept in chunks.
    private static readonly TimeSpan MaxDelayChunk = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = options.Value.ParseSchedule();

        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = schedule.GetNextOccurrence(time.GetUtcNow(), time.LocalTimeZone);
            if (next is null)
            {
                logger.LogWarning(
                    "Auto-transition schedule '{Schedule}' has no future occurrences; stopping",
                    options.Value.Schedule
                );
                return;
            }

            logger.LogInformation("Next event auto-transition run at {NextRun}", next.Value);

            TimeSpan remaining;
            while ((remaining = next.Value - time.GetUtcNow()) > TimeSpan.Zero)
                await Task.Delay(
                    remaining < MaxDelayChunk ? remaining : MaxDelayChunk,
                    time,
                    stoppingToken
                );

            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var transitioner = scope.ServiceProvider.GetRequiredService<EventAutoTransitioner>();
            var changed = await transitioner.RunAsync(time.Today(), ct);
            logger.LogInformation("Event auto-transition run finished, {Count} changed", changed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the schedule alive; the next run is idempotent and will retry.
            logger.LogError(ex, "Event auto-transition run failed");
        }
    }
}
