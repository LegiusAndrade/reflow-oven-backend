namespace ReflowOven.Api.Controllers;

/// <summary>
/// OrangePi OS control: metrics, network/Wi-Fi/IP, clock/NTP, software update, central-server
/// reachability and power. Reads are authenticated; every mutation is <c>AdminOnly</c>. Backed by
/// <see cref="ISystemController"/> (Simulated on a dev box, Linux on the Pi — by <c>System:Mode</c>).
/// </summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(SystemService system) : ControllerBase
{
    [HttpGet("status")]
    public Task<SystemStatusDto> Status(CancellationToken ct) => system.StatusAsync(ct);

    [HttpGet("metrics")]
    public Task<SystemMetricsDto> Metrics(CancellationToken ct) => system.MetricsAsync(ct);

    [HttpGet("network")]
    public Task<NetworkStatusDto> Network(CancellationToken ct) => system.NetworkAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("network")]
    public async Task<IActionResult> ApplyNetwork([FromBody] ApplyNetworkRequest req, CancellationToken ct)
    {
        await system.ApplyNetworkAsync(req, ct);
        return NoContent();
    }

    // Scanning is part of network administration (and triggers an OS scan), so it matches the other
    // network/Wi-Fi endpoints in being AdminOnly.
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpGet("wifi")]
    public Task<IReadOnlyList<WifiNetworkDto>> Wifi(CancellationToken ct) => system.ScanWifiAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("wifi/connect")]
    public async Task<IActionResult> ConnectWifi([FromBody] ConnectWifiRequest req, CancellationToken ct)
    {
        await system.ConnectWifiAsync(req, ct);
        return NoContent();
    }

    [HttpGet("interfaces")]
    public Task<IReadOnlyList<NetworkInterfaceDto>> Interfaces(CancellationToken ct) => system.InterfacesAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("interfaces/priority")]
    public async Task<IActionResult> SetPriorityInterface([FromBody] SetPriorityInterfaceRequest req, CancellationToken ct)
    {
        await system.SetPriorityInterfaceAsync(req, ct);
        return NoContent();
    }

    [HttpGet("time")]
    public Task<TimeStatusDto> Time(CancellationToken ct) => system.TimeAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("time")]
    public async Task<IActionResult> SetTime([FromBody] SetTimeRequest req, CancellationToken ct)
    {
        await system.SetTimeAsync(req, ct);
        return NoContent();
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("ntp")]
    public async Task<IActionResult> SetNtp([FromBody] SetNtpRequest req, CancellationToken ct)
    {
        await system.SetNtpAsync(req, ct);
        return NoContent();
    }

    [HttpGet("update")]
    public Task<UpdateStatusDto> Update(CancellationToken ct) => system.UpdateStatusAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("update")]
    public async Task<IActionResult> ApplyUpdate(CancellationToken ct)
    {
        await system.ApplyUpdateAsync(ct);
        return NoContent();
    }

    [HttpGet("connectivity")]
    public Task<ConnectivityDto> Connectivity(CancellationToken ct) => system.ConnectivityAsync(ct);

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("reboot")]
    public async Task<IActionResult> Reboot(CancellationToken ct)
    {
        await system.RebootAsync(ct);
        return NoContent();
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("shutdown")]
    public async Task<IActionResult> Shutdown(CancellationToken ct)
    {
        await system.ShutdownAsync(ct);
        return NoContent();
    }
}
