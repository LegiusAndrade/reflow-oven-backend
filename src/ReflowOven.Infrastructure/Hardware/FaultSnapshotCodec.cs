using System.Buffers.Binary;

namespace ReflowOven.Infrastructure.Hardware;

/// <summary>
/// Pure, I/O-free decoder for the GET_FAULT_SNAPSHOT (0x10) payloads — the lock-step contract with the
/// firmware peer (../reflow-oven-firmware). The board records a ring of <see cref="SampleSize"/>-byte samples
/// (one every <see cref="SampleIntervalMs"/> ms) around a protection fault and serves them as a chunked download:
/// each chunk replies with a <see cref="HeaderSize"/>-byte header followed by that chunk's packed samples.
/// Both the header and each sample are little-endian; kept separate from the driver's serial I/O so the exact
/// byte layout + scaling can be unit-tested in isolation (see FaultSnapshotCodecTests).
/// </summary>
internal static class FaultSnapshotCodec
{
    /// <summary>Packed bytes per sample (the firmware's fixed sample record).</summary>
    public const int SampleSize = 32;

    /// <summary>Chunk header bytes: total_samples:u16, sample_size:u8, trigger_index:u16, fault_code:u16,
    /// base_ms:u32, chunk_index:u8, chunk_count:u8.</summary>
    public const int HeaderSize = 13;

    /// <summary>Spacing between samples — the board captures one every 10 ms.</summary>
    public const int SampleIntervalMs = 10;

    /// <summary>The fixed metadata that prefixes every chunk's payload (repeated on each chunk so the host can
    /// validate the download and learn <c>chunk_count</c> from the very first reply).</summary>
    public readonly record struct ChunkHeader(
        int TotalSamples,
        int SampleSize,
        int TriggerIndex,
        int FaultCode,
        long BaseMs,
        int ChunkIndex,
        int ChunkCount);

    /// <summary>Decode the 13-byte chunk header (little-endian).</summary>
    public static ChunkHeader DecodeHeader(ReadOnlySpan<byte> p) => new(
        TotalSamples: BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(0, 2)),
        SampleSize: p[2],
        TriggerIndex: BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(3, 2)),
        FaultCode: BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(5, 2)),
        BaseMs: BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(7, 4)),
        ChunkIndex: p[11],
        ChunkCount: p[12]);

    /// <summary>Decode one 32-byte packed sample into engineering units. Scales mirror the rest of the protocol:
    /// temperatures ÷10 (°C), the bus/regulator rails ÷100 (centivolts → V), the PD/VDDA rails ÷1000 (mV → V),
    /// current ÷1000 (mA → A); fans are RPM, duties are %, power is W.</summary>
    public static FaultSnapshotSample DecodeSample(ReadOnlySpan<byte> s) => new(
        OvenTempC: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(0, 2)) / 10.0,
        BoardTempC: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(2, 2)) / 10.0,
        VbusV: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(4, 2)) / 100.0,    // centivolts
        VregV: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(6, 2)) / 100.0,    // centivolts
        PdV: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(8, 2)) / 1000.0,     // millivolts
        CurrentA: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(10, 2)) / 1000.0, // milliamps
        FanIntakeRpm: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(12, 2)),
        FanExhaustRpm: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(14, 2)),
        FanBoardRpm: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(16, 2)),
        DutyIntakePct: s[18],
        DutyExhaustPct: s[19],
        DutyBoardPct: s[20],
        McuTempC: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(21, 2)) / 10.0,
        VddaV: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(23, 2)) / 1000.0,  // millivolts
        FaultFlags: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(25, 2)),
        SetpointC: BinaryPrimitives.ReadInt16LittleEndian(s.Slice(27, 2)) / 10.0,
        BuckDutyPct: s[29],
        PowerW: BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(30, 2)));
}
