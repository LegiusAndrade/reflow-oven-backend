namespace ReflowOven.Application.Dtos;

public sealed record ProfilePointDto(double T, double Temp);

public sealed record ProfileSegmentDto(int Temp, int DurationSec, RampShape Ramp);

/// <summary>
/// A program as the gallery/editor consume it. <c>LastUsed</c> is ISO or null (client shows
/// "Nunca"); <c>Favorite</c> is scoped to the current user; <c>Segments</c> is null on seeds.
/// </summary>
public sealed record ProgramDto(
    string Id,
    string Name,
    string? Description,
    int RunCount,
    DateTimeOffset? LastUsed,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ProfilePointDto> Profile,
    IReadOnlyList<ProfileSegmentDto>? Segments,
    bool Favorite);

/// <summary>
/// Create/edit payload. Provide <c>Segments</c> (the editor's source of truth) and the server
/// derives <c>Profile</c>; if only <c>Profile</c> is sent (e.g. an imported curve) it is used as-is.
/// </summary>
public sealed record SaveProgramRequest(
    string Name,
    string? Description,
    IReadOnlyList<ProfileSegmentDto>? Segments,
    IReadOnlyList<ProfilePointDto>? Profile);

public enum ProgramFilter { All, Favorites, Unused, Used }

public enum ProgramSort { Default, Recent, MostUsed, Name, Temp, Duration }

/// <summary>Query options for the program gallery (search/filter/sort/paginate).</summary>
public sealed record ProgramListQuery(
    string? Search = null,
    ProgramFilter Filter = ProgramFilter.All,
    ProgramSort Sort = ProgramSort.Default,
    int Page = 1,
    int PageSize = 12);

/// <summary>
/// Optional body for POST /api/programs/{id}/favorite. When <c>Favorite</c> is provided the call is
/// idempotent (set to that exact state); when the body is absent/null the endpoint toggles.
/// </summary>
public sealed record SetFavoriteRequest(bool? Favorite);

/// <summary>A soft-deleted program as the Master's "Apagados" (trash) view consumes it.</summary>
public sealed record DeletedProgramDto(string Id, string Name, DateTimeOffset? DeletedAt, string? DeletedBy);
