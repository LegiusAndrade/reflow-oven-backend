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
