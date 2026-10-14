namespace Amsterfam.Core.Entities;

public class Event
{
    public const int MaxDiscordWebhookUrlLength = 300;
    public const int MaxTelegramBotTokenLength = 100;
    public const int MaxTelegramBotUsernameLength = 64;
    public const int MaxTelegramChatTitleLength = 255;

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public required string Location { get; set; }

    /// <summary>ISO 4217 code all of the event's expenses are in; see <see cref="Currencies"/>.</summary>
    public string Currency { get; set; } = Currencies.Default;
    public DateOnly? PollRangeStart { get; set; }
    public DateOnly? PollRangeEnd { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public bool AutoTransitionsPaused { get; set; }
    public Guid? BannerFileId { get; set; }

    /// <summary>
    /// Where news posts are mirrored (ADR-015). A secret: stored plain, never returned or logged.
    /// </summary>
    public string? DiscordWebhookUrl { get; set; }

    /// <summary>
    /// The bot that mirrors news to a Telegram group (ADR-015). A secret like
    /// <see cref="DiscordWebhookUrl"/>: stored plain, never returned or logged.
    /// </summary>
    public string? TelegramBotToken { get; set; }

    /// <summary>The bot's @username, from Telegram when the token was saved. Not a secret.</summary>
    public string? TelegramBotUsername { get; set; }

    /// <summary>The group news goes to; Telegram is only set up once this is set too.</summary>
    public long? TelegramChatId { get; set; }

    /// <summary>The group's name, from Telegram when the chat was picked.</summary>
    public string? TelegramChatTitle { get; set; }
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }

    public User CreatedBy { get; set; } = null!;
    public ICollection<EventAttendance> Attendances { get; set; } = [];
    public ICollection<Accommodation> Accommodations { get; set; } = [];
    public ICollection<AvailabilityEntry> AvailabilityEntries { get; set; } = [];
    public ICollection<DatePollEntry> DatePollEntries { get; set; } = [];
    public ICollection<Activity> Activities { get; set; } = [];
    public ICollection<ItineraryEntry> ItineraryEntries { get; set; } = [];
    public ICollection<ShoppingItem> ShoppingItems { get; set; } = [];
    public ICollection<EventComfortQuestion> ComfortQuestions { get; set; } = [];
    public ICollection<EventFile> Files { get; set; } = [];
}

public enum EventStatus
{
    Draft,
    LookingForDate,
    Open,
    InProgress,
    Closed,
    Archived,
    Cancelled,
}
