namespace Amsterfam.Api.Dtos;

public record Birthday(int Month, int Day, int? Year);

public record DietaryOptionResponse(int Id, string Key, string Label);

public record UserResponse(
    int Id,
    string Handle,
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

public record UpdateUserRequest(
    string? DisplayName,
    string? AvatarUrl,
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
    string? DisplayName,
    string? AvatarUrl,
    string? Pronouns,
    string? Location,
    string? Bio,
    Birthday? Birthday,
    IReadOnlyList<DietaryOptionResponse> DietaryOptions,
    string? DietaryNotes
);
