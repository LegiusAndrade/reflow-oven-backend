namespace ReflowOven.Domain.Entities;

/// <summary>
/// Device calibration singleton (Id always 1) set from the hidden Calibração tab and pushed to
/// the board over RS422. Net-new persistence — the frontend kept this only in the technician session.
/// </summary>
public class Calibration
{
    public int Id { get; set; } = 1;

    /// <summary>Thermocouple temperature offset (°C), -20..20.</summary>
    public double ThermoOffset { get; set; }

    /// <summary>Current-sensor zero offset (A), -5..5.</summary>
    public double CurrentOffset { get; set; }

    /// <summary>Sensor gain (%), 50..150.</summary>
    public double CurrentGain { get; set; } = 100;

    /// <summary>Fan PWM duty floor (%), 0..100.</summary>
    public int FanPwmMin { get; set; } = 20;

    /// <summary>Fan PWM duty ceiling (%), 0..100.</summary>
    public int FanPwmMax { get; set; } = 100;
}
