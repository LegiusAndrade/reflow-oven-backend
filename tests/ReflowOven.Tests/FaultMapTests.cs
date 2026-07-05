using ReflowOven.Application.Common;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Hardware;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// Lock-step guard for the firmware fault contract: the RS422 driver's bit map must translate every
/// firmware fault flag to a catalogued E-code (<see cref="Defaults.FaultTypes"/> →
/// <see cref="FaultCatalog"/>), including the firmware's two newest bits — BOARD_OVER_TEMP (1&lt;&lt;11)
/// and PRECHARGE (1&lt;&lt;12, blink code 13).
/// </summary>
public sealed class FaultMapTests
{
    [Theory]
    [InlineData(1 << 2, "E-101")]  // OVER_TEMP
    [InlineData(1 << 0, "E-102")]  // TC
    [InlineData(1 << 11, "E-170")] // BOARD_OVER_TEMP (new)
    [InlineData(1 << 12, "E-180")] // PRECHARGE (new)
    [InlineData(1 << 9, "E-130")]  // COMMS_LOSS
    public void Fault_bits_map_to_their_catalogued_code(int bit, string expected)
    {
        Assert.Equal(expected, Rs422PowerBoard.MapFaultCode(1, (ushort)bit));

        // …and the code the bit lands on is genuinely catalogued (not the unknown-code fallback text).
        var (_, message) = FaultCatalog.Resolve(expected);
        Assert.DoesNotContain("não catalogada", message);
    }

    [Fact]
    public void No_latched_fault_maps_to_null() => Assert.Null(Rs422PowerBoard.MapFaultCode(0, 0));

    [Fact]
    public void Unknown_flag_falls_back_to_the_generic_critical() =>
        Assert.Equal("E-101", Rs422PowerBoard.MapFaultCode(1, 1 << 15));

    [Fact]
    public void Oven_over_temp_outranks_the_new_board_over_temp() =>
        Assert.Equal("E-101", Rs422PowerBoard.MapFaultCode(1, (1 << 11) | (1 << 2)));

    [Fact]
    public void New_codes_carry_Critico_severity_and_the_ptBR_labels()
    {
        var (boardSeverity, boardMessage) = FaultCatalog.Resolve("E-170");
        Assert.Equal(ErrorSeverity.Critico, boardSeverity); // wire literal "Crítico"
        Assert.Equal("Sobretemperatura da placa de potência", boardMessage);

        var (prechargeSeverity, prechargeMessage) = FaultCatalog.Resolve("E-180");
        Assert.Equal(ErrorSeverity.Critico, prechargeSeverity);
        Assert.Equal("Falha de pré-carga do barramento DC", prechargeMessage);
    }
}
