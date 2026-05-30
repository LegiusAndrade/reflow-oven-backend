namespace ReflowOven.Application.Dtos;

/// <summary>
/// A user as the Usuários tab and detail modal consume it. Timestamps are ISO (the client
/// formats; null LastLogin renders "—"). <c>Events</c> are the activity counters.
/// </summary>
public sealed record UserDto(
    string Id,
    string Name,
    string Email,
    UserType Type,
    UserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLogin,
    IReadOnlyList<UserEventDto> Events);

public sealed record UserEventDto(string Label, int Count);

public sealed record CreateUserRequest(
    string Name,
    string Email,
    string Password,
    UserType Type = UserType.Regular,
    UserStatus Status = UserStatus.Ativo);

/// <summary>Name is immutable, so it is not part of the update. Password is optional (reset).</summary>
public sealed record UpdateUserRequest(
    string Email,
    UserType Type,
    UserStatus Status,
    string? Password = null);
