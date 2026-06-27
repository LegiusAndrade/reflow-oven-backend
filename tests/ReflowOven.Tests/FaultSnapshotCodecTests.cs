using System.Buffers.Binary;
using ReflowOven.Infrastructure.Hardware;

namespace ReflowOven.Tests;

/// <summary>
/// Verifies the GET_FAULT_SNAPSHOT (0x10) decode byte-for-byte — the lock-step contract with
/// ../reflow-oven-firmware: the 13-byte chunk header, the 32-byte packed sample layout and every scale
/// (temp ÷10, vbus/vreg centivolts ÷100, pd/vdda mV ÷1000, current mA ÷1000, fans RPM, duties %, power W),
/// including the signed (i16) channels.
/// </summary>
public class FaultSnapshotCodecTests
{
    [Fact]
    public void Header_decodes_all_fields_little_endian()
    {
        var p = new byte[FaultSnapshotCodec.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), 300);     // total_samples
        p[2] = 32;                                                      // sample_size
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(3), 200);     // trigger_index
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(5), 0x0102);  // fault_code
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(7), 123_456); // base_ms
        p[11] = 1;                                                      // chunk_index
        p[12] = 5;                                                      // chunk_count

        var h = FaultSnapshotCodec.DecodeHeader(p);
        Assert.Equal(300, h.TotalSamples);
        Assert.Equal(32, h.SampleSize);
        Assert.Equal(200, h.TriggerIndex);
        Assert.Equal(0x0102, h.FaultCode);
        Assert.Equal(123_456, h.BaseMs);
        Assert.Equal(1, h.ChunkIndex);
        Assert.Equal(5, h.ChunkCount);
    }

    [Fact]
    public void Sample_decodes_to_engineering_units_with_correct_scales()
    {
        var s = PackSample(
            ovenX10: 1503, boardX10: 452, vbusCv: 16000, vregCv: 1200, pdMv: 20000, currentMa: -250,
            fanIntake: 4200, fanExhaust: 4100, fanBoard: 3300, dutyIntake: 80, dutyExhaust: 75, dutyBoard: 60,
            mcuX10: 421, vddaMv: 3300, faultFlags: 1 << 2, setpointX10: 1450, buckDuty: 88, powerW: 2320);

        var d = FaultSnapshotCodec.DecodeSample(s);

        Assert.Equal(150.3, d.OvenTempC, 3);
        Assert.Equal(45.2, d.BoardTempC, 3);
        Assert.Equal(160.00, d.VbusV, 3);   // centivolts
        Assert.Equal(12.00, d.VregV, 3);    // centivolts
        Assert.Equal(20.000, d.PdV, 3);     // millivolts
        Assert.Equal(-0.250, d.CurrentA, 3); // milliamps, signed
        Assert.Equal(4200, d.FanIntakeRpm);
        Assert.Equal(4100, d.FanExhaustRpm);
        Assert.Equal(3300, d.FanBoardRpm);
        Assert.Equal(80, d.DutyIntakePct);
        Assert.Equal(75, d.DutyExhaustPct);
        Assert.Equal(60, d.DutyBoardPct);
        Assert.Equal(42.1, d.McuTempC, 3);
        Assert.Equal(3.300, d.VddaV, 3);    // millivolts
        Assert.Equal(1 << 2, d.FaultFlags);
        Assert.Equal(145.0, d.SetpointC, 3);
        Assert.Equal(88, d.BuckDutyPct);
        Assert.Equal(2320, d.PowerW);
    }

    [Fact]
    public void Negative_temperatures_decode_as_signed()
    {
        // i16 oven/board/mcu/setpoint channels must round-trip below zero (a cold board, a frozen TC).
        var s = PackSample(
            ovenX10: -55, boardX10: -120, vbusCv: 0, vregCv: 0, pdMv: 0, currentMa: 0,
            fanIntake: 0, fanExhaust: 0, fanBoard: 0, dutyIntake: 0, dutyExhaust: 0, dutyBoard: 0,
            mcuX10: -10, vddaMv: 0, faultFlags: 0, setpointX10: -250, buckDuty: 0, powerW: 0);

        var d = FaultSnapshotCodec.DecodeSample(s);
        Assert.Equal(-5.5, d.OvenTempC, 3);
        Assert.Equal(-12.0, d.BoardTempC, 3);
        Assert.Equal(-1.0, d.McuTempC, 3);
        Assert.Equal(-25.0, d.SetpointC, 3);
    }

    [Fact]
    public void Samples_decode_at_their_offset_after_the_chunk_header()
    {
        // A full chunk payload is header(13) + N×sample(32); the driver slices past the header before decoding
        // samples. Build header + two samples and confirm the second one decodes from its own offset.
        var payload = new byte[FaultSnapshotCodec.HeaderSize + 2 * FaultSnapshotCodec.SampleSize];
        payload[2] = 32; // sample_size
        var s0 = PackSample(ovenX10: 1000, boardX10: 0, vbusCv: 0, vregCv: 0, pdMv: 0, currentMa: 0,
            fanIntake: 0, fanExhaust: 0, fanBoard: 0, dutyIntake: 0, dutyExhaust: 0, dutyBoard: 0,
            mcuX10: 0, vddaMv: 0, faultFlags: 0, setpointX10: 0, buckDuty: 0, powerW: 0);
        var s1 = PackSample(ovenX10: 2000, boardX10: 0, vbusCv: 0, vregCv: 0, pdMv: 0, currentMa: 0,
            fanIntake: 0, fanExhaust: 0, fanBoard: 0, dutyIntake: 0, dutyExhaust: 0, dutyBoard: 0,
            mcuX10: 0, vddaMv: 0, faultFlags: 0, setpointX10: 0, buckDuty: 0, powerW: 0);
        s0.CopyTo(payload.AsSpan(FaultSnapshotCodec.HeaderSize));
        s1.CopyTo(payload.AsSpan(FaultSnapshotCodec.HeaderSize + FaultSnapshotCodec.SampleSize));

        var body = payload.AsSpan(FaultSnapshotCodec.HeaderSize);
        var d0 = FaultSnapshotCodec.DecodeSample(body.Slice(0, FaultSnapshotCodec.SampleSize));
        var d1 = FaultSnapshotCodec.DecodeSample(body.Slice(FaultSnapshotCodec.SampleSize, FaultSnapshotCodec.SampleSize));
        Assert.Equal(100.0, d0.OvenTempC, 3);
        Assert.Equal(200.0, d1.OvenTempC, 3);
    }

    private static byte[] PackSample(
        short ovenX10, short boardX10, ushort vbusCv, ushort vregCv, ushort pdMv, short currentMa,
        ushort fanIntake, ushort fanExhaust, ushort fanBoard, byte dutyIntake, byte dutyExhaust, byte dutyBoard,
        short mcuX10, ushort vddaMv, ushort faultFlags, short setpointX10, byte buckDuty, ushort powerW)
    {
        var s = new byte[FaultSnapshotCodec.SampleSize];
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(0), ovenX10);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(2), boardX10);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(4), vbusCv);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(6), vregCv);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(8), pdMv);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(10), currentMa);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(12), fanIntake);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(14), fanExhaust);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(16), fanBoard);
        s[18] = dutyIntake;
        s[19] = dutyExhaust;
        s[20] = dutyBoard;
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(21), mcuX10);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(23), vddaMv);
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(25), faultFlags);
        BinaryPrimitives.WriteInt16LittleEndian(s.AsSpan(27), setpointX10);
        s[29] = buckDuty;
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(30), powerW);
        return s;
    }
}
