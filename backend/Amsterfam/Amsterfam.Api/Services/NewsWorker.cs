using Microsoft.Extensions.Options;

namespace Amsterfam.Api.Services;

/// <summary>
/// Publishes scheduled news posts once they're due (<see cref="NewsPublisher.PublishDueAsync"/>),
/// every <see cref="NewsOptions.WorkerInterval"/> and once at startup.
/// </summary>
public class NewsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NewsOptions> options,
    TimeProvider time,
    ILogger<NewsWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(options.Value.WorkerInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<NewsPublisher>();
            await publisher.PublishDueAsync(time.GetUtcNow(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep ticking; unpublished posts are picked up again next time.
            logger.LogError(ex, "Scheduled news run failed");
        }
    }
}
