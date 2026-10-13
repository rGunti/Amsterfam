using System.Text.Json;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Amsterfam.Tests.Services;

public class PushChannelTests
{
    private static readonly Event Ev = new() { Name = "Trip", Location = "Amsterdam" };

    private static PushChannel Channel() =>
        new(
            new HttpClient(),
            null!,
            TimeProvider.System,
            Options.Create(new PushOptions()),
            Options.Create(new AppOptions { PublicUrl = "https://amsterfam.test/" }),
            NullLogger<PushChannel>.Instance
        );

    private static JsonElement Notification(NewsPost post) =>
        JsonDocument.Parse(Channel().Payload(post, Ev)).RootElement.GetProperty("notification");

    [Fact]
    public void Payload_IsInTheServiceWorkerFormat()
    {
        var n = Notification(
            new()
            {
                Id = 7,
                EventId = Ev.Id,
                Title = "Hello",
                Body = "Table at **8**.",
            }
        );

        Assert.Equal("Hello", n.GetProperty("title").GetString());
        Assert.Equal("Table at 8.", n.GetProperty("body").GetString());
        Assert.Equal("/icons/icon-192.png", n.GetProperty("icon").GetString());
        var click = n.GetProperty("data").GetProperty("onActionClick").GetProperty("default");
        Assert.Equal("navigateLastFocusedOrOpen", click.GetProperty("operation").GetString());
        Assert.Equal(
            $"https://amsterfam.test/events/{Ev.Id}/news/7",
            click.GetProperty("url").GetString()
        );
    }

    [Fact]
    public void Payload_FallsBackToTheEventNameAndKeepsTheBodyShort()
    {
        var n = Notification(
            new()
            {
                Id = 7,
                EventId = Ev.Id,
                Title = null,
                Body = string.Join(" ", Enumerable.Repeat("word", 100)),
            }
        );

        Assert.Equal("Trip", n.GetProperty("title").GetString());
        Assert.True(n.GetProperty("body").GetString()!.Length <= PushChannel.MaxBodyLength + 1);
    }
}
