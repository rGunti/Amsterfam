namespace Amsterfam.Api.Dtos;

/// <summary>
/// Whether the event mirrors news to a Telegram group. The bot token itself is never returned;
/// <see cref="TokenHint"/> is its last 4 characters. Configured needs both a token and a chat.
/// </summary>
public record TelegramStatus(
    bool Configured,
    bool TokenSaved,
    string? TokenHint,
    string? BotUsername,
    long? ChatId,
    string? ChatTitle
);

/// <summary>
/// Either field may be left out. A new token replaces the bot and clears the chat; a chat id
/// picks the group for the saved (or newly given) token.
/// </summary>
public record SetTelegramRequest(string? Token, long? ChatId);

/// <summary>A group the bot has heard from recently, for the organiser to pick.</summary>
public record TelegramChatOption(long Id, string Title, string Type);
