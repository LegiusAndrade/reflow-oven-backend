using System.Buffers.Binary;
using System.IO.Hashing;

namespace ReflowOven.Infrastructure.Hardware;

/// <summary>
/// Pure, I/O-free codec for the RS422 power-board wire format — a faithful C# port of the firmware peer
/// (../reflow-oven-firmware <c>framework/comm/{proto,cobs,crc32}</c>). A frame is
/// <c>header(6) + payload + CRC-32(4)</c>, COBS-encoded and closed by a <c>0x00</c> delimiter; the header is
/// <c>ver(1)=1, type(1), seq(1), cmd(1), status(1), len(1)</c>. The CRC-32 is the standard reflected algorithm
/// (poly 0xEDB88320) appended <b>big-endian</b>; payload fields are little-endian. Kept separate from the
/// driver's serial I/O so the framing can be unit-tested in isolation (see Rs422WireTests).
/// </summary>
internal static class Rs422Wire
{
    public const byte ProtoVersion = 1;
    public const byte TypeRequest = 0x01, TypeResponse = 0x02, TypeNack = 0x03, TypeNotify = 0x04;
    public const int HeaderSize = 6;
    public const int CrcSize = 4;
    public const int MaxPayload = 255;                 // the len field is one byte
    public const int MaxFrame = HeaderSize + MaxPayload + CrcSize;
    public const int MaxWire = MaxFrame + MaxFrame / 254 + 2; // COBS worst case + delimiter

    /// <summary>Serialise a frame to its on-wire bytes into <paramref name="dst"/> (header+payload, CRC-32
    /// big-endian, COBS, then the 0x00 delimiter). Returns the number of bytes written.</summary>
    public static int BuildFrame(byte type, byte seq, byte cmd, byte status, ReadOnlySpan<byte> payload, Span<byte> dst)
    {
        if (payload.Length > MaxPayload) throw new ArgumentException("payload excede 255 bytes", nameof(payload));

        Span<byte> frame = stackalloc byte[HeaderSize + payload.Length + CrcSize];
        frame[0] = ProtoVersion;
        frame[1] = type;
        frame[2] = seq;
        frame[3] = cmd;
        frame[4] = status;
        frame[5] = (byte)payload.Length;
        payload.CopyTo(frame[HeaderSize..]);
        var crc = Crc32Value(frame[..(HeaderSize + payload.Length)]);
        BinaryPrimitives.WriteUInt32BigEndian(frame[(HeaderSize + payload.Length)..], crc);

        var enc = CobsEncode(frame, dst);
        dst[enc++] = 0x00;
        return enc;
    }

    /// <summary>Standard CRC-32 (reflected, poly 0xEDB88320, init/xor 0xFFFFFFFF) as a uint. System.IO.Hashing
    /// emits the hash little-endian; the numeric value matches the firmware's CRC32_Compute.</summary>
    public static uint Crc32Value(ReadOnlySpan<byte> data)
    {
        var crc = new Crc32();
        crc.Append(data);
        Span<byte> le = stackalloc byte[4];
        crc.GetCurrentHash(le);
        return BinaryPrimitives.ReadUInt32LittleEndian(le);
    }

    /// <summary>COBS-encode <paramref name="src"/> into <paramref name="dst"/> (output has no 0x00; no
    /// delimiter appended). Returns the encoded length. Mirrors framework/comm/cobs.cpp COBS_Encode.</summary>
    public static int CobsEncode(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        var write = 1;
        var codePos = 0;
        byte code = 1;
        foreach (var b in src)
        {
            if (b == 0)
            {
                dst[codePos] = code;
                code = 1;
                codePos = write++;
            }
            else
            {
                dst[write++] = b;
                code++;
                if (code == 0xFF) { dst[codePos] = code; code = 1; codePos = write++; }
            }
        }
        dst[codePos] = code;
        return write;
    }

    /// <summary>COBS-decode one frame's bytes (without the trailing 0x00) into <paramref name="dst"/>.
    /// Returns the decoded length, or -1 on malformed input / overflow. Mirrors COBS_Decode.</summary>
    public static int CobsDecode(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        var write = 0;
        var read = 0;
        while (read < src.Length)
        {
            int code = src[read++];
            if (code == 0) return -1; // 0x00 must not appear inside COBS data
            for (var i = 1; i < code; i++)
            {
                if (read >= src.Length || write >= dst.Length) return -1;
                dst[write++] = src[read++];
            }
            if (code != 0xFF && read < src.Length)
            {
                if (write >= dst.Length) return -1;
                dst[write++] = 0;
            }
        }
        return write;
    }
}

/// <summary>A decoded protocol frame (payload copied out, owned by the caller).</summary>
internal readonly record struct Rs422Frame(byte Type, byte Seq, byte Cmd, byte Status, byte[] Payload);

/// <summary>
/// Streaming frame parser: feed received bytes one at a time; it emits a whole, CRC-valid frame on each
/// 0x00 delimiter and self-resynchronises after a corrupt one (mirrors the firmware PROTO_PARSER_t).
/// </summary>
internal sealed class Rs422FrameParser
{
    private readonly byte[] _accum = new byte[Rs422Wire.MaxWire];
    private readonly byte[] _decoded = new byte[Rs422Wire.MaxWire];
    private int _len;
    private bool _overflow;

    /// <summary>Feed one received byte; returns true (with <paramref name="frame"/> set) when a delimiter
    /// closes a complete, CRC-valid frame.</summary>
    public bool PushByte(byte b, out Rs422Frame frame)
    {
        frame = default;
        if (b != 0x00)
        {
            if (_len < _accum.Length) _accum[_len++] = b;
            else _overflow = true; // longer than any valid frame — fail it at the delimiter
            return false;
        }

        var n = _len;
        var overflowed = _overflow;
        _len = 0;
        _overflow = false;
        if (overflowed || n == 0) return false;

        var dec = Rs422Wire.CobsDecode(_accum.AsSpan(0, n), _decoded);
        if (dec < Rs422Wire.HeaderSize + Rs422Wire.CrcSize) return false;
        if (_decoded[0] != Rs422Wire.ProtoVersion) return false;

        int plen = _decoded[5];
        if (Rs422Wire.HeaderSize + plen + Rs422Wire.CrcSize != dec) return false;

        var want = BinaryPrimitives.ReadUInt32BigEndian(_decoded.AsSpan(dec - Rs422Wire.CrcSize, Rs422Wire.CrcSize));
        if (Rs422Wire.Crc32Value(_decoded.AsSpan(0, dec - Rs422Wire.CrcSize)) != want) return false;

        frame = new Rs422Frame(_decoded[1], _decoded[2], _decoded[3], _decoded[4],
                               _decoded.AsSpan(Rs422Wire.HeaderSize, plen).ToArray());
        return true;
    }
}
