namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/calibration")]
[Authorize(Policy = AuthPolicies.CalibrationOnly)]
public sealed class CalibrationController(CalibrationService calibration) : ControllerBase
{
    [HttpGet]
    public Task<CalibrationDto> Get(CancellationToken ct) => calibration.GetAsync(ct);

    [HttpPut]
    public Task<CalibrationDto> Update([FromBody] CalibrationDto dto, CancellationToken ct) => calibration.UpdateAsync(dto, ct);

    /// <summary>Fit gain/offset from the output-calibration sweep.</summary>
    [HttpPost("wizard")]
    public ActionResult<CalibrationFitDto> Fit([FromBody] CalibrationWizardRequest req) => calibration.Fit(req);
}
