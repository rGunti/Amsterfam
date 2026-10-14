namespace Amsterfam.Api.Dtos;

public record Birthday(int Month, int Day, int? Year);

public record DietaryOptionResponse(int Id, string Key, string Label);

public record UserResponse(
    int Id,
    string Handle,
    string? AuthSource,
    string ProfileHandle,
    string? DisplayName,
    string Email,
    string? AvatarUrl,
    string? Pronouns,
    string? Location,
    string? Bio,
    Birthday? Birthday,
    IReadOnlyList<DietaryOptionResponse> DietaryOptions,
    string? DietaryNotes
);

public record UpdateUserRequest(string? DisplayName, string? AvatarUrl);

/// <summary>
/// The "about me" fields, saved separately from <see cref="UpdateUserRequest"/> so an older
/// client that only knows about name and avatar can never wipe them. Replaces all of them.
/// </summary>
public record UpdateAboutRequest(
    string? Pronouns = null,
    string? Location = null,
    string? Bio = null,
    Birthday? Birthday = null,
    int[]? DietaryOptionIds = null,
    string? DietaryNotes = null
);

/// <summary>What other members see on someone's profile page; deliberately has no email.</summary>
public record UserProfileResponse(
    int Id,
    string Handle,
    string? AuthSource,
    string ProfileHandle,
    string? DisplayName,
    string? AvatarUrl,
    string? Pronouns,
    string? Location,
    string? Bio,
    Birthday? Birthday,
    IReadOnlyList<DietaryOptionResponse> DietaryOptions,
    string? DietaryNotes
);

/// <summary>
/// News by email (ADR-018). <c>EmailAvailable</c> is false while the server has no SMTP set up.
/// <c>LoginEmail</c> is null when the login sent no address. <c>EffectiveEmail</c> is where
/// news goes right now: a confirmed custom address, else the login address.
/// </summary>
public record NotificationSettingsResponse(
    bool EmailAvailable,
    bool EmailNotifications,
    string? LoginEmail,
    string? NotificationEmail,
    bool NotificationEmailConfirmed,
    string? EffectiveEmail
);

public record UpdateNotificationSettingsRequest(bool EmailNotifications);

public record SetNotificationEmailRequest(string? Email);

public record ConfirmNotificationEmailRequest(string? Token);
