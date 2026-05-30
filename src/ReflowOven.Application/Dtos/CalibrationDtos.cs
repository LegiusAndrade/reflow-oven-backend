namespace ReflowOven.Application.Dtos;

public sealed record CalibrationDto(
    double ThermoOffset,
    double CurrentOffset,
    double CurrentGain,
    int FanPwmMin,
    int FanPwmMax);

public sealed record OutputCalStepDto(double SetVoltage, double MeasuredVoltage);

/// <summary>The wizard sweep samples (10→50→100→150→0 V) used to fit gain/offset.</summary>
public sealed record CalibrationWizardRequest(IReadOnlyList<OutputCalStepDto> Steps);

public sealed record CalibrationFitDto(double Gain, double Offset);
