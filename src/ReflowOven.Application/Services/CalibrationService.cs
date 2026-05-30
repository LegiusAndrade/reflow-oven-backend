namespace ReflowOven.Application.Services;

/// <summary>Reads/updates the calibration singleton, pushes it to the board, and fits the output wizard sweep.</summary>
public sealed class CalibrationService(IAppDbContext db, IPowerBoard board)
{
    public async Task<CalibrationDto> GetAsync(CancellationToken ct = default) => Map(await LoadAsync(ct));

    public async Task<CalibrationDto> UpdateAsync(CalibrationDto dto, CancellationToken ct = default)
    {
        Validate(dto);
        var c = await LoadAsync(ct);
        c.ThermoOffset = dto.ThermoOffset;
        c.CurrentOffset = dto.CurrentOffset;
        c.CurrentGain = dto.CurrentGain;
        c.FanPwmMin = dto.FanPwmMin;
        c.FanPwmMax = dto.FanPwmMax;
        await db.SaveChangesAsync(ct);
        await board.ApplyCalibrationAsync(c, ct);
        return Map(c);
    }

    /// <summary>Least-squares fit of measured = gain·set + offset across the wizard sweep.</summary>
    public CalibrationFitDto Fit(CalibrationWizardRequest req)
    {
        var pts = req.Steps;
        if (pts is null || pts.Count < 2)
            throw new ValidationAppException("Forneça ao menos 2 pontos de medição.");

        int n = pts.Count;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var p in pts)
        {
            sx += p.SetVoltage;
            sy += p.MeasuredVoltage;
            sxx += p.SetVoltage * p.SetVoltage;
            sxy += p.SetVoltage * p.MeasuredVoltage;
        }
        var denom = n * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-9)
            throw new ValidationAppException("Pontos insuficientes para o ajuste (todos com a mesma tensão).");

        var gain = (n * sxy - sx * sy) / denom;
        var offset = (sy - gain * sx) / n;
        return new CalibrationFitDto(Math.Round(gain, 4), Math.Round(offset, 4));
    }

    private async Task<Calibration> LoadAsync(CancellationToken ct) =>
        await db.Calibrations.FirstOrDefaultAsync(x => x.Id == 1, ct)
        ?? throw new NotFoundException("Calibração não inicializada.");

    private static void Validate(CalibrationDto d)
    {
        Check(d.ThermoOffset, DomainConstants.CalibThermoOffsetMin, DomainConstants.CalibThermoOffsetMax, "Offset do termopar");
        Check(d.CurrentOffset, DomainConstants.CalibCurrentOffsetMin, DomainConstants.CalibCurrentOffsetMax, "Offset de corrente");
        Check(d.CurrentGain, DomainConstants.CalibGainMin, DomainConstants.CalibGainMax, "Ganho");
        Check(d.FanPwmMin, DomainConstants.CalibPwmMin, DomainConstants.CalibPwmMax, "PWM mínimo");
        Check(d.FanPwmMax, DomainConstants.CalibPwmMin, DomainConstants.CalibPwmMax, "PWM máximo");
    }

    private static void Check(double value, double min, double max, string label)
    {
        if (value < min || value > max)
            throw new ValidationAppException($"{label} fora da faixa {min}..{max}.");
    }

    private static CalibrationDto Map(Calibration c) =>
        new(c.ThermoOffset, c.CurrentOffset, c.CurrentGain, c.FanPwmMin, c.FanPwmMax);
}
