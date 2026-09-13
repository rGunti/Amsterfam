namespace Amsterfam.Api.Dtos;

public record UserResponse(
    int Id,
    string Handle,
    string? DisplayName,
    string Email,
    string? AvatarUrl
);

public record UpdateUserRequest(string? DisplayName, string? AvatarUrl);
