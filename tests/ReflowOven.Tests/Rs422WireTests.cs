using System.Text;
using ReflowOven.Infrastructure.Hardware;

namespace ReflowOven.Tests;

/// <summary>
/// Verifies the RS422 wire codec (<see cref="Rs422Wire"/> + <see cref="Rs422FrameParser"/>) byte-for-byte:
/// the CRC-32 check value the firmware uses, COBS framing, and full build→parse round-trips including the
/// payload shapes the protocol actually sends (empty keep-alive, the 35-byte status push, zero-bearing
/// payloads) plus self-resync after a corrupt frame. This is the contract with ../reflow-oven-firmware.
/// </summary>
public class Rs422WireTests
{
    [Fact]
    public void Crc32_matches_the_standard_check_value()
    {
        // The firmware's CRC32_Compute is the standard reflected CRC-32 (poly 0xEDB88320); its documented
        // check value over the ASCII string "123456789" is 0xCBF43926. If this holds, both sides agree.
        Assert.Equal(0xCBF43926u, Rs422Wire.Crc32Value(Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void Cobs_output_never_contains_a_zero_byte()
    {
        // COBS exists so a single 0x00 can delimit frames — the encoded body must be zero-free.
        var dst = new byte[Rs422Wire.MaxWire];
        var n = Rs422Wire.CobsEncode([0, 0, 1, 0, 2, 3, 0], dst);
        Assert.DoesNotContain((byte)0, dst[..n]);
    }

    [Theory]
    [InlineData(new byte[0])]                                   // empty — the keep-alive NOTIFY
    [InlineData(new byte[] { 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x00 })]               // all zeros — COBS edge
    [InlineData(new byte[] { 0xFF, 0x00, 0x10, 0x00, 0x20 })]   // zeros interleaved
    public void Build_then_parse_round_trips(byte[] payload)
    {
        var wire = Build(Rs422Wire.TypeRequest, seq: 42, cmd: 0x05, status: 0x00, payload);

        var frame = Assert.Single(Feed(wire));
        Assert.Equal(Rs422Wire.TypeRequest, frame.Type);
        Assert.Equal(42, frame.Seq);
        Assert.Equal(0x05, frame.Cmd);
        Assert.Equal(0x00, frame.Status);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public void Status_notify_payload_round_trips()
    {
        // The power board's ~1 Hz push: a 35-byte status payload in a NOTIFY (cmd 0x01).
        var payload = new byte[35];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7);

        var frame = Assert.Single(Feed(Build(Rs422Wire.TypeNotify, 1, 0x01, 0x00, payload)));
        Assert.Equal(Rs422Wire.TypeNotify, frame.Type);
        Assert.Equal(0x01, frame.Cmd);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public void Corrupt_frame_is_dropped_and_parser_resyncs_to_the_next()
    {
        var corrupt = Build(Rs422Wire.TypeResponse, 8, 0x0A, 0x00, [9, 9, 9]);
        var good = Build(Rs422Wire.TypeResponse, 7, 0x0A, 0x00, [1, 2, 3]);

        // Flip an interior encoded byte to a different non-zero value (won't create a stray delimiter) so the
        // CRC check fails — the frame must be dropped at its delimiter and the next frame still parse cleanly.
        var i = corrupt.Length / 2;
        corrupt[i] = (byte)(corrupt[i] == 0x55 ? 0xAA : 0x55);

        var frame = Assert.Single(Feed([.. corrupt, .. good]));
        Assert.Equal(7, frame.Seq);
        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Payload);
    }

    private static byte[] Build(byte type, byte seq, byte cmd, byte status, byte[] payload)
    {
        var buf = new byte[Rs422Wire.MaxWire];
        return buf[..Rs422Wire.BuildFrame(type, seq, cmd, status, payload, buf)];
    }

    private static List<Rs422Frame> Feed(byte[] wire)
    {
        var parser = new Rs422FrameParser();
        var frames = new List<Rs422Frame>();
        foreach (var b in wire)
            if (parser.PushByte(b, out var f)) frames.Add(f);
        return frames;
    }
}
