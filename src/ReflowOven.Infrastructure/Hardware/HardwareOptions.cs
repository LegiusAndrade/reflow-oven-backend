namespace ReflowOven.Infrastructure.Hardware;

/// <summary>Selects the <see cref="IPowerBoard"/> implementation ("Simulated" | "Rs422").</summary>
public sealed class HardwareOptions
{
    public const string Section = "Hardware";
    public string Mode { get; set; } = "Simulated";
}
