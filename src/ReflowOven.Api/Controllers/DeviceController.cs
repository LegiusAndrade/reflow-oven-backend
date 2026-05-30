namespace ReflowOven.Api.Controllers;

[ApiController]
[Route("api/device")]
public sealed class DeviceController(DeviceService device) : ControllerBase
{
    [HttpGet]
    public Task<DeviceInfoDto> Get(CancellationToken ct) => device.GetAsync(ct);
}
