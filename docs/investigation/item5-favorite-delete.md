# item5-favorite-delete

## Files

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Api/Controllers/ProgramsController.cs

```csharp
// lines 33-50
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await programs.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id}/favorite")]
    public async Task<ActionResult<FavoriteResult>> ToggleFavorite(string id, CancellationToken ct)
    {
        if (current.UserId is not { } userId)
            throw new ForbiddenAppException("Sessão sem usuário não pode favoritar.");
        var favorite = await programs.ToggleFavoriteAsync(id, userId, ct);
        return new FavoriteResult(favorite);
    }

    public sealed record FavoriteResult(bool Favorite);
```

Note: the controller is declared `public sealed class ProgramsController(ProgramService programs, ICurrentUser current) : ControllerBase` (line 5). `FavoriteResult` is a nested record on the controller (line 50). `Delete` carries `[Authorize(Policy = AuthPolicies.AdminOnly)]` (line 33). `ToggleFavorite` has NO `[Authorize]` attribute beyond the project-wide authenticated-by-default fallback (it only requires a logged-in user, not admin).

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Services/ProgramService.cs

```csharp
// lines 109-139
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var program = await db.Programs.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Programa não encontrado.");

        program.IsDeleted = true;
        program.DeletedAt = clock.UtcNow;
        audit.RecordProgramChange(ChangeAction.Removido, program, BuildPoints(program, ChangePointRole.Removed));
        await audit.PruneProgramChangesAsync(program.Id, ct);
        await audit.BumpActivityAsync(Defaults.ActivityLabels[6], ct); // programas deletados
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Toggle the program's favorite flag for the user; returns the new state.</summary>
    public async Task<bool> ToggleFavoriteAsync(string id, Guid userId, CancellationToken ct = default)
    {
        if (!await db.Programs.AnyAsync(p => p.Id == id, ct))
            throw new NotFoundException("Programa não encontrado.");

        var fav = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ProgramId == id, ct);
        if (fav is null)
        {
            db.Favorites.Add(new FavoriteProgram { UserId = userId, ProgramId = id });
            await db.SaveChangesAsync(ct);
            return true;
        }

        db.Favorites.Remove(fav);
        await db.SaveChangesAsync(ct);
        return false;
    }
```

Service signature (line 7): `public sealed class ProgramService(IAppDbContext db, IClock clock, AuditService audit)`.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Domain/Entities/ReflowProgram.cs

```csharp
// lines 55-62
/// <summary>Per-user favorite (replaces the flat localStorage set). Works for seed and user programs.</summary>
public class FavoriteProgram
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ProgramId { get; set; } = "";
    public ReflowProgram? Program { get; set; }
}
```

The favorite is keyed per-user: composite (`UserId`, `ProgramId`). No `Favorite`/`IsFavorite` boolean column exists — favorite state is the presence/absence of a `FavoriteProgram` row.

### /home/lsilva/ProjetosLucas/reflow-oven-backend/src/ReflowOven.Application/Dtos/ProgramDtos.cs

```csharp
// lines 11-19  (ProgramDto carries the per-user Favorite bool)
public sealed record ProgramDto(
    string Id,
    string Name,
    string? Description,
    int RunCount,
    DateTimeOffset? LastUsed,
    IReadOnlyList<ProfilePointDto> Profile,
    IReadOnlyList<ProfileSegmentDto>? Segments,
    bool Favorite);
```

There is NO existing favorite-request DTO in this file. `FavoriteResult(bool Favorite)` lives as a nested record in the controller (see above), not here.

## Change plan

1. **File `src/ReflowOven.Application/Dtos/ProgramDtos.cs`** — add a new request DTO. After the existing `ProgramListQuery` record (end of file, after line 41), insert:
   ```csharp
   /// <summary>
   /// Optional body for POST /api/programs/{id}/favorite. When <c>Favorite</c> is provided the call is
   /// idempotent (set to that exact state); when the body is absent/null the endpoint toggles.
   /// </summary>
   public sealed record SetFavoriteRequest(bool? Favorite);
   ```
   No nested namespace; the file already uses `namespace ReflowOven.Application.Dtos;`.

2. **File `src/ReflowOven.Application/Services/ProgramService.cs`** — change the symbol `ToggleFavoriteAsync` to accept an optional desired state and do an idempotent upsert/delete. Replace the method (lines 122-139):
   - OLD signature: `public async Task<bool> ToggleFavoriteAsync(string id, Guid userId, CancellationToken ct = default)`
   - NEW signature: `public async Task<bool> ToggleFavoriteAsync(string id, Guid userId, bool? desired = null, CancellationToken ct = default)`
   - Replace the body so: after the existence check and the `fav` lookup, compute the target. New body:
     ```csharp
     /// <summary>
     /// Set or toggle the program's favorite flag for the user; returns the new state.
     /// When <paramref name="desired"/> is given it is an idempotent set (no-op if already in that
     /// state); when null it toggles the current state.
     /// </summary>
     public async Task<bool> ToggleFavoriteAsync(string id, Guid userId, bool? desired = null, CancellationToken ct = default)
     {
         if (!await db.Programs.AnyAsync(p => p.Id == id, ct))
             throw new NotFoundException("Programa não encontrado.");

         var fav = await db.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ProgramId == id, ct);
         var isFav = fav is not null;
         var target = desired ?? !isFav;

         if (target == isFav)
             return isFav; // idempotent no-op

         if (target)
             db.Favorites.Add(new FavoriteProgram { UserId = userId, ProgramId = id });
         else
             db.Favorites.Remove(fav!);

         await db.SaveChangesAsync(ct);
         return target;
     }
     ```
   - Note: keep the existing default-arg ordering valid — `bool? desired = null` must come before `CancellationToken ct = default`, which the new signature above does. The existing controller call `programs.ToggleFavoriteAsync(id, userId, ct)` will now bind `ct` to the new `desired` param positionally and break — so step 3 updates that call site (it is the only caller of this method in the repo).

3. **File `src/ReflowOven.Api/Controllers/ProgramsController.cs`** — extend the `ToggleFavorite` action (lines 41-48) to accept an optional body and pass the desired state through. Replace:
   - OLD:
     ```csharp
     [HttpPost("{id}/favorite")]
     public async Task<ActionResult<FavoriteResult>> ToggleFavorite(string id, CancellationToken ct)
     {
         if (current.UserId is not { } userId)
             throw new ForbiddenAppException("Sessão sem usuário não pode favoritar.");
         var favorite = await programs.ToggleFavoriteAsync(id, userId, ct);
         return new FavoriteResult(favorite);
     }
     ```
   - NEW:
     ```csharp
     [HttpPost("{id}/favorite")]
     public async Task<ActionResult<FavoriteResult>> ToggleFavorite(string id, [FromBody] SetFavoriteRequest? req, CancellationToken ct)
     {
         if (current.UserId is not { } userId)
             throw new ForbiddenAppException("Sessão sem usuário não pode favoritar.");
         var favorite = await programs.ToggleFavoriteAsync(id, userId, req?.Favorite, ct);
         return new FavoriteResult(favorite);
     }
     ```
   - This passes `req?.Favorite` (a `bool?`) as the new `desired` arg by name-position, then `ct`. Absent body → `req` is null → `null` desired → toggle (back-compat). The `[FromBody] ... ?` marks the body optional so an empty POST still binds.
   - `SetFavoriteRequest` is in `ReflowOven.Application.Dtos`; controllers already resolve `SaveProgramRequest`/`ProgramDto` from that namespace via `GlobalUsings.cs`, so no new `using` is needed (verify `Application.Dtos` is in the Api project's `GlobalUsings.cs`).

4. **No other call sites.** Confirm `ToggleFavoriteAsync` is invoked only from `ProgramsController` (it is, per the code above). The optional `desired = null` default also keeps any test/caller that passes only `(id, userId)` compiling, but any caller passing `(id, userId, ct)` positionally must be updated (only the controller does this — handled in step 3).

5. **Delete stays unchanged.** `DeleteAsync` / the `Delete` action keep `[Authorize(Policy = AuthPolicies.AdminOnly)]` and `204 NoContent`. No edit.

## Migration?

**No.** Favorite state is modeled as the presence/absence of a `FavoriteProgram` row (composite `UserId`+`ProgramId`), not a boolean column on any table. The change only adds a request DTO, an optional method parameter, and idempotent branch logic — no entity, column, table, index, or constraint change. There is no CHECK/enum constraint involved (the singleton `Id = 1` checks and `PtBrEnumConverter` enum columns are unrelated to `FavoriteProgram`). Therefore no `dotnet ef migrations add` is required.

## Contract/enum notes

- No pt-BR enum literals or `[JsonStringEnumMemberName]` attributes are touched. `FavoriteProgram`/favorites carry no enum on the wire.
- The response shape `FavoriteResult(bool Favorite)` is unchanged — serialized as `{ "favorite": true|false }`. The frontend depends on this exact response from the favorite endpoint; keep it.
- New request DTO `SetFavoriteRequest(bool? Favorite)` serializes as `{ "favorite": true }` / `{ "favorite": false }`. The body is OPTIONAL: an empty POST (no body) must still toggle, preserving the current contract so the existing frontend (which sends no body today) keeps working.
- `ProgramDto.Favorite` (the per-user bool the gallery reads) is unchanged.
- pt-BR error strings preserved verbatim: `"Programa não encontrado."` (NotFound), `"Sessão sem usuário não pode favoritar."` (Forbidden).

## Risks / open questions

- **Call-site break from param reordering:** adding `bool? desired = null` before `CancellationToken ct` means the existing positional call `ToggleFavoriteAsync(id, userId, ct)` would silently bind `ct` to `desired`. Step 3 fixes the only caller; a grep for other callers (tests, etc.) should be run before applying. Quick check: `grep -rn "ToggleFavoriteAsync" src tests`.
- **Optional `[FromBody]` binding:** ASP.NET Core treats `[FromBody]` as required by default and can 415/400 on an empty body with no `Content-Type`. Marking the param nullable (`SetFavoriteRequest? req`) makes the body optional; confirm a bodyless POST (current frontend behavior) still binds to `null` rather than 400. May need `[FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]` if a 415 appears in testing.
- **Authorization asymmetry (by design, worth confirming):** `Delete` is `AdminOnly`; `ToggleFavorite` is NOT — it only requires an authenticated user, because favorite is per-user. The idempotent `set` variant inherits this (any logged-in user sets their own favorite). Confirm this is intended and that the technician/calibration session (config login, not a `User` row) is correctly rejected — `current.UserId is not { } userId` already throws `ForbiddenAppException` for a session without a user id.
- **Audit/activity:** favorites are not audited or activity-counted today (unlike create/edit/delete). The set variant keeps that behavior; confirm no audit entry is desired for favorite changes.
- **GlobalUsings:** assumes `ReflowOven.Application.Dtos` is globally imported in the Api project (it must be, since `SaveProgramRequest`/`ProgramDto` are used unqualified in the controller). If `SetFavoriteRequest` doesn't resolve, add the using — low risk.
- **Idempotent no-op return value:** when `desired == isFav` the method returns the current state without a DB write. This is correct/intended for idempotency, but means a "set true" on an already-favorited program returns `true` with no `SaveChangesAsync` — verify any frontend expecting a write echo is fine (the response body is identical either way).