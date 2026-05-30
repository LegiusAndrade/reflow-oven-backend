namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet]
    public Task<SettingsDto> Get(CancellationToken ct) => settings.GetAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut]
    public Task<SettingsDto> Update([FromBody] SettingsDto dto, CancellationToken ct) => settings.UpdateAsync(dto, ct);
}
