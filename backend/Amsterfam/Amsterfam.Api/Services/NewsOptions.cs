namespace Amsterfam.Api.Services;

public class NewsOptions
{
    public const string SectionName = "News";

    /// <summary>Runs <see cref="NewsWorker"/>. Tests turn it off and call the publisher directly.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>How often the worker looks for scheduled posts that are due.</summary>
    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromSeconds(30);
}
