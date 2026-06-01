namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/programs")]
public sealed class ProgramsController(ProgramService programs, ICurrentUser current) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProgramDto>> List(
        [FromQuery] string? search,
        [FromQuery] string? filter,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
        => programs.ListAsync(new ProgramListQuery(search, ParseFilter(filter), ParseSort(sort), page, pageSize), current.UserId, ct);

    [HttpGet("{id}")]
    public Task<ProgramDto> Get(string id, CancellationToken ct) => programs.GetAsync(id, current.UserId, ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<ProgramDto>> Create([FromBody] SaveProgramRequest req, CancellationToken ct)
    {
        var program = await programs.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = program.Id }, program);
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("{id}")]
    public Task<ProgramDto> Update(string id, [FromBody] SaveProgramRequest req, CancellationToken ct)
        => programs.UpdateAsync(id, req, current.UserId, ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await programs.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id}/favorite")]
    public async Task<ActionResult<FavoriteResult>> ToggleFavorite(
        string id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SetFavoriteRequest? body,
        CancellationToken ct)
    {
        if (current.UserId is not { } userId)
            throw new ForbiddenAppException("Sessão sem usuário não pode favoritar.");
        var favorite = await programs.ToggleFavoriteAsync(id, userId, body?.Favorite, ct);
        return new FavoriteResult(favorite);
    }

    public sealed record FavoriteResult(bool Favorite);

    private static ProgramFilter ParseFilter(string? s) => s?.ToLowerInvariant() switch
    {
        "favorites" => ProgramFilter.Favorites,
        "unused" => ProgramFilter.Unused,
        "used" => ProgramFilter.Used,
        _ => ProgramFilter.All,
    };

    private static ProgramSort ParseSort(string? s) => s?.ToLowerInvariant() switch
    {
        "recent" => ProgramSort.Recent,
        "most-used" => ProgramSort.MostUsed,
        "name" => ProgramSort.Name,
        "temp" => ProgramSort.Temp,
        "duration" => ProgramSort.Duration,
        _ => ProgramSort.Default,
    };
}
