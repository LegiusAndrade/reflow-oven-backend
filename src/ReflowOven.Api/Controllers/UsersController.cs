namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserDto>> List(CancellationToken ct) => users.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<UserDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        var user = await users.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    public Task<UserDto> Update(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct) => users.UpdateAsync(id, req, ct);

    // The dev Master may NOT soft-delete an active user — that is Admin-only (AdminStrict excludes Master).
    // The Master still manages the user trash (restore/purge) below.
    [Authorize(Policy = AuthPolicies.AdminStrict)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }

    // --- Master "trash" (soft-deleted users): only the dev Master may view/restore/purge -------------
    [Authorize(Policy = AuthPolicies.MasterOnly)]
    [HttpGet("deleted")]
    public Task<IReadOnlyList<DeletedUserDto>> ListDeleted(CancellationToken ct) => users.ListDeletedAsync(ct);

    [Authorize(Policy = AuthPolicies.MasterOnly)]
    [HttpPost("{id:guid}/restore")]
    public Task<UserDto> Restore(Guid id, CancellationToken ct) => users.RestoreAsync(id, ct);

    [Authorize(Policy = AuthPolicies.MasterOnly)]
    [HttpDelete("{id:guid}/purge")]
    public async Task<IActionResult> Purge(Guid id, CancellationToken ct)
    {
        await users.PurgeAsync(id, ct);
        return NoContent();
    }
}
