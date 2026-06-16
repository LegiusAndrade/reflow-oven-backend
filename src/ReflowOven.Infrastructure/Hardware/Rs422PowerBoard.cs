using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Device.Gpio;
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ReflowOven.Infrastructure.Hardware;

/// <summary>
/// Real STM32 power-board driver over RS422 (selected by <c>Hardware:Mode=Rs422</c>). Speaks the same
/// wire format as the firmware peer (../reflow-oven-firmware <c>framework/comm/proto</c> + <c>reflow_oven/comms</c>):
/// each frame is <c>header(6) + payload + CRC-32(4)</c>, COBS-encoded and terminated by <c>0x00</c>;
/// header is <c>ver(1)=1, type(1), seq(1), cmd(1), status(1), len(1)</c>; payload fields are little-endian
/// (floats IEEE-754 LE), only the CRC-32 trailer is big-endian.
///
/// The link is a full-duplex PEER: the power board PUSHES its 35-byte status ~1 Hz as a one-way NOTIFY —
/// there is no poll — so this driver runs a background RX loop that caches the latest push (<see cref="ReadAsync"/>
/// returns that cache with no round-trip) and issues REQUEST/await-RESPONSE only for the mutating commands.
/// It sends a keep-alive NOTIFY whenever idle so the firmware's 2 s comms watchdog stays fed, and runs its
/// own watchdog the other way (no frame for <c>LinkTimeoutMs</c> ⇒ <see cref="FaultRaised"/> E-130). The serial
/// port is opened (and reconnected) on a background task, so a missing/late board never crashes startup.
/// </summary>
public sealed class Rs422PowerBoard : IPowerBoard, IDisposable
{
    // Command ids (reflow_oven/comms.h) + the OK status; the framing constants/codec live in Rs422Wire.
    private const byte StatusOk = 0x00; // RESPONSE/NACK status: OK=0, UNKNOWN_CMD=1, BAD_PARAM=2, BUSY=3, ERROR=0xFF
    private const byte CmdGetStatus = 0x01, CmdSetConfiguration = 0x02, CmdStartProgram = 0x05, CmdStop = 0x06,
                       CmdSetCalibration = 0x07, CmdSelfTest = 0x08, CmdDriveOutput = 0x09, CmdGetIdentity = 0x0A,
                       CmdGetRunStatus = 0x0B;

    private const int StatusPayloadSize = 35;        // periodic 1 Hz telemetry (SerializeStatus)
    private const int StartProgramSegsPerChunk = 48; // segments per START_PROGRAM chunk (≤6 B header + 48×5 ≤ 255 B)

    private readonly HardwareOptions _opts;
    private readonly IClock _clock;
    private readonly ILogger<Rs422PowerBoard> _logger;
    private readonly IBoardGpio _gpio;

    // One physical write at a time (keep-alive vs request vs reconnect must not interleave bytes).
    private readonly SemaphoreSlim _txGate = new(1, 1);
    // One in-flight REQUEST at a time; the byte seq still matches the RESPONSE in the RX loop.
    private readonly SemaphoreSlim _reqGate = new(1, 1);
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<RxResponse>> _pending = new();
    private byte _seq;

    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _stateGate = new();
    private SerialPort? _port;
    private GpioController? _deGpio;        // holds the RS422 transceiver DE/TX-enable pin HIGH (full-duplex)
    private readonly Task _linkTask;
    private readonly Task _houseTask;
    private readonly Rs422FrameParser _parser = new();   // persistent so the efficiency counters survive reconnects

    // --- link efficiency stats (Interlocked counters + snapshot for the periodic delta) ------------
    private long _bytesRx, _bytesTx, _framesTx, _reqTimeouts;
    private long _snapOk, _snapBad, _snapBytesRx, _snapBytesTx, _snapFramesTx, _snapTimeouts;
    private DateTimeOffset _statsAt;

    // --- cached state (guarded by _stateGate) -------------------------------------------------------
    private SensorReadings _last;          // last decoded status push (zeros until the first one arrives)
    private DateTimeOffset _lastRxAt;      // last valid frame of ANY kind (drives the offline watchdog)
    private DateTimeOffset _lastTxAt;      // last frame we sent (drives the keep-alive)
    private string? _raisedFaultCode;      // currently-signalled board fault (edge-triggered; null = none)
    private bool _offline;                 // currently-signalled comms-loss (edge-triggered)

    public event EventHandler<FaultRaised>? FaultRaised;

    /// <summary>True while the RS422 link is alive (a recent status frame). See <see cref="IPowerBoard.IsConnected"/>.</summary>
    public bool IsConnected
    {
        get { lock (_stateGate) return !_offline; }
    }

    public Rs422PowerBoard(IOptions<HardwareOptions> opts, IClock clock, IBoardGpio gpio, ILogger<Rs422PowerBoard> logger)
    {
        _opts = opts.Value;
        _clock = clock;
        _gpio = gpio;
        _logger = logger;
        var now = clock.UtcNow;
        _lastRxAt = now;   // grace period before the offline watchdog can trip
        _lastTxAt = now;
        _statsAt = now;
        AssertTransmitEnable(); // drive the transceiver into transmit mode before the link starts
        _linkTask = Task.Run(() => LinkLoopAsync(_cts.Token));
        _houseTask = Task.Run(() => HousekeepingLoopAsync(_cts.Token));
    }

    /// <summary>Hold the RS422 transceiver's direction pin HIGH so it stays in transmit / driver-enabled mode
    /// (full-duplex RS422 — no per-frame DE toggling, since the board was converted from RS485). Best-effort:
    /// missing GPIO just logs and the link still runs (a hardware pull-up may already hold it).</summary>
    private void AssertTransmitEnable()
    {
        if (_opts.DePin < 0) return;
        try
        {
            _deGpio = new GpioController();
            _deGpio.OpenPin(_opts.DePin, PinMode.Output, PinValue.High);
            _logger.LogInformation("RS422: DE/TX-enable em GPIO{Pin} = HIGH (full-duplex).", _opts.DePin);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RS422: não consegui acionar o DE/TX-enable (GPIO{Pin}); o transceiver pode não transmitir.", _opts.DePin);
            try { _deGpio?.Dispose(); } catch { /* ignore */ }
            _deGpio = null;
        }
    }

    // ============================================================================================
    // IPowerBoard
    // ============================================================================================

    /// <summary>Returns the last status the board pushed (no round-trip — the power streams it ~1 Hz).</summary>
    public Task<SensorReadings> ReadAsync(CancellationToken ct = default)
    {
        lock (_stateGate) return Task.FromResult(_last);
    }

    /// <summary>GET_RUN_STATUS: the firmware's live run state (real setpoint/phase the controller is driving).
    /// Returns null on a closed/silent link or while the firmware isn't controlling (running=0) — the run loop
    /// then uses its own interpolated setpoint. Best-effort: a failed poll never breaks a tick.
    /// Response (LE, ≥9 B): running:u8, phase:u8, setpoint_x10:i16, elapsed_s:u16, total_s:u16, duty:u8.</summary>
    public async Task<RunReadback?> GetRunStatusAsync(CancellationToken ct = default)
    {
        byte[] resp;
        try { resp = await RequestAsync(CmdGetRunStatus, [], ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
        if (resp.Length < 9 || resp[0] == 0) return null; // not running / short ⇒ fall back to local interpolation
        var phase = (RunPhase)Math.Clamp((int)resp[1], 0, 3);
        var setpoint = BinaryPrimitives.ReadInt16LittleEndian(resp.AsSpan(2)) / 10.0;
        var elapsed = BinaryPrimitives.ReadUInt16LittleEndian(resp.AsSpan(4));
        var total = BinaryPrimitives.ReadUInt16LittleEndian(resp.AsSpan(6));
        return new RunReadback(true, phase, setpoint, elapsed, total, resp[8]);
    }

    /// <summary>START_PROGRAM: upload the run profile as SEGMENTS and let the power board compute the setpoint
    /// curve itself (exact parabolas, no pre-sampling). The board holds ≤ <see cref="DomainConstants.ProfileMaxPoints"/>
    /// segments; larger profiles split into chunks so each frame stays ≤255 B payload. Every chunk is a CRC-checked
    /// REQUEST (ACK + retry via <see cref="RequestAsync"/>); the last chunk arms the run. <paramref name="profile"/>
    /// (the pre-sampled curve) is for the simulator and ignored here. Wire per chunk (little-endian):
    /// total:u8, offset:u8, count:u8, flags:u8 (bit0=first→carries start_temp, bit1=last),
    /// [start_temp_x10:i16 if first], then count × { shape:u8, duration_s:u16, target_x10:i16 }.
    /// shape: 0=Linear 1=Fixo 2=Parábola+ 3=Parábola− (firmware applies the same ease). Limits go via
    /// <see cref="ApplyControlConfigAsync"/>.</summary>
    public async Task StartProgramAsync(IReadOnlyList<ProfileSegment> segments, IReadOnlyList<ProfilePoint> profile, CancellationToken ct = default)
    {
        if (segments.Count == 0) throw new InvalidOperationException("RS422: programa sem segmentos.");
        if (segments.Count > DomainConstants.ProfileMaxPoints)
            throw new InvalidOperationException($"RS422: programa excede {DomainConstants.ProfileMaxPoints} segmentos.");

        var total = (byte)segments.Count;
        var startTempX10 = ClampI16(Math.Round(DomainConstants.StartTemp * 10.0));

        for (var offset = 0; offset < segments.Count; offset += StartProgramSegsPerChunk)
        {
            var count = Math.Min(StartProgramSegsPerChunk, segments.Count - offset);
            var first = offset == 0;
            var last = offset + count >= segments.Count;

            var payload = new byte[4 + (first ? 2 : 0) + count * 5];
            payload[0] = total;
            payload[1] = (byte)offset;
            payload[2] = (byte)count;
            payload[3] = (byte)((first ? 1 : 0) | (last ? 2 : 0));
            var n = 4;
            if (first) { BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(n), startTempX10); n += 2; }
            for (var i = 0; i < count; i++)
            {
                var s = segments[offset + i];
                payload[n++] = ShapeCode(s.Ramp);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(n), ClampU16(s.DurationSec));            n += 2;
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(n), ClampI16(Math.Round(s.Temp * 10.0))); n += 2;
            }
            await RequestAsync(CmdStartProgram, payload, ct); // per-chunk CRC + ACK + retry
        }
    }

    /// <summary>Wire shape code for a ramp (explicit so reordering RampShape can't silently shift the wire).</summary>
    private static byte ShapeCode(RampShape r) => r switch
    {
        RampShape.Linear => 0,
        RampShape.Fixo => 1,
        RampShape.ParabolaPositiva => 2,
        RampShape.ParabolaNegativa => 3,
        _ => 1,
    };

    /// <summary>STOP: abort the run (heater off, fans safe).</summary>
    public Task StopAsync(CancellationToken ct = default) => RequestAsync(CmdStop, [], ct);

    /// <summary>SET_CALIBRATION: push the sensor offsets/gain + fan-PWM band. The firmware reserves the blob
    /// layout (it acknowledges without decoding yet); this is the agreed 8-byte little-endian shape.</summary>
    public Task ApplyCalibrationAsync(Calibration calibration, CancellationToken ct = default)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0), ClampI16(calibration.ThermoOffset * 100));   // °C ×100
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2), ClampI16(calibration.CurrentOffset * 100));  // A  ×100
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), ClampU16(calibration.CurrentGain * 10));    // %  ×10
        payload[6] = (byte)Math.Clamp(calibration.FanPwmMin, 0, 100);
        payload[7] = (byte)Math.Clamp(calibration.FanPwmMax, 0, 100);
        return RequestAsync(CmdSetCalibration, payload, ct);
    }

    /// <summary>SET_CONFIGURATION: PID gains + ProcessLimits (24 B LE). This is the authoritative config push.</summary>
    public Task ApplyControlConfigAsync(Settings settings, CancellationToken ct = default)
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0), (float)settings.Pid.P);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4), (float)settings.Pid.I);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8), (float)settings.Pid.D);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12), settings.Oven.MaxTemp);
        // Bus over/under-voltage thresholds in centivolts (×100) — see VoltsToCentivolts. Settings.Voltage is volts.
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(16), VoltsToCentivolts(settings.Voltage.Max));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(18), VoltsToCentivolts(settings.Voltage.Min));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20), ClampU16(settings.Oven.MaxFanRpm));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(22), ClampU16(settings.Process.MaxExtraTimeSec));
        return RequestAsync(CmdSetConfiguration, payload, ct);
    }

    /// <summary>SELF_TEST: id ⇒ result (0=pass, 1=fail, 2=not-run).</summary>
    public async Task<SelfTestResult> RunSelfTestAsync(SelfTestId id, CancellationToken ct = default)
    {
        var resp = await RequestAsync(CmdSelfTest, [(byte)id], ct);
        // resp: id:u8, result:u8.
        var result = resp.Length >= 2 ? resp[1] : (byte)2;
        var state = result switch { 0 => SelfTestState.Ok, 1 => SelfTestState.Fail, _ => SelfTestState.Idle };
        return new SelfTestResult(id, state);
    }

    /// <summary>DRIVE_OUTPUT: command a target voltage (mV) and read the measured value back.</summary>
    public async Task<OutputCalStep> DriveOutputAsync(double setVoltage, CancellationToken ct = default)
    {
        var req = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(req, VoltsToCentivolts(setVoltage)); // 0–180 VDC bus (×100)
        var resp = await RequestAsync(CmdDriveOutput, req, ct);
        // resp: set:u16, measured:u16 — both centivolts of the bus output.
        var set = resp.Length >= 2 ? CentivoltsToVolts(BinaryPrimitives.ReadUInt16LittleEndian(resp.AsSpan(0))) : setVoltage;
        var measured = resp.Length >= 4 ? CentivoltsToVolts(BinaryPrimitives.ReadUInt16LittleEndian(resp.AsSpan(2))) : 0;
        return new OutputCalStep(Math.Round(set, 2), Math.Round(measured, 2));
    }

    /// <summary>GET_IDENTITY: the power board's serial + hour-meter + firmware version (from the wire). The
    /// control-side fields describe this host (the OrangePi running the backend) — the power link carries
    /// only the power board's identity.</summary>
    public async Task<BoardIdentity> GetIdentityAsync(CancellationToken ct = default)
    {
        var resp = await RequestAsync(CmdGetIdentity, [], ct);
        // resp: serial:u32, hours_min:u32, ver_len:u8, version[ver_len].
        var serial = resp.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(resp.AsSpan(0)) : 0;
        var hoursMin = resp.Length >= 8 ? BinaryPrimitives.ReadUInt32LittleEndian(resp.AsSpan(4)) : 0;
        var verLen = resp.Length >= 9 ? resp[8] : 0;
        var version = resp.Length >= 9 + verLen ? System.Text.Encoding.ASCII.GetString(resp, 9, verLen) : "";

        var hostVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "—";
        return new BoardIdentity(
            PowerVersion: version.Length > 0 ? version : "—",
            PowerSerial: serial.ToString(),
            PowerHours: (int)(hoursMin / 60),
            ControlVersion: hostVersion,
            ControlSerial: Environment.MachineName,
            ControlHours: 0);
    }

    // ============================================================================================
    // Background link: connect + RX loop, keep-alive + offline watchdog
    // ============================================================================================

    private async Task LinkLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SerialPort? port = null;
            try
            {
                port = new SerialPort(_opts.PortName, _opts.BaudRate, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None, // RS422 full-duplex, no DE line to toggle
                    ReadTimeout = SerialPort.InfiniteTimeout,
                    WriteTimeout = 1000,
                };
                port.Open();
                lock (_stateGate) { _port = port; _lastRxAt = _clock.UtcNow; }
                _logger.LogInformation("RS422: porta {Port} aberta a {Baud} baud.", _opts.PortName, _opts.BaudRate);

                await ReceiveLoopAsync(port, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break; // shutdown closed the port — not a real link failure
                // Expected operational failures (busy / denied / missing port) get a clean one-liner + hint,
                // not a stack trace — the loop just retries. Anything unexpected keeps the full exception.
                var busy = ex is UnauthorizedAccessException
                    && (ex.InnerException?.Message.Contains("busy", StringComparison.OrdinalIgnoreCase) ?? false);
                string? detalhe = ex switch
                {
                    UnauthorizedAccessException when busy =>
                        $"porta {_opts.PortName} ocupada — outra instância da API já está usando a serial?",
                    UnauthorizedAccessException =>
                        $"sem permissão para abrir {_opts.PortName} (sem acesso ao dispositivo — grupo dialout/privilégios)",
                    FileNotFoundException =>
                        $"porta {_opts.PortName} não encontrada — confira Hardware:PortName e se o dispositivo existe",
                    _ => null,
                };
                if (detalhe is not null)
                    _logger.LogWarning("RS422: {Detalhe}. Reconectando em {Delay} ms.", detalhe, _opts.ReconnectDelayMs);
                else
                    _logger.LogWarning(ex, "RS422: falha na porta {Port}; reconectando em {Delay} ms.",
                        _opts.PortName, _opts.ReconnectDelayMs);
                RaiseCommsLoss();
            }
            finally
            {
                lock (_stateGate) _port = null;
                try { port?.Dispose(); } catch { /* ignore */ }
            }

            if (!ct.IsCancellationRequested)
                try { await Task.Delay(_opts.ReconnectDelayMs, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Drain the serial port and feed every byte to the streaming frame parser until the port drops.</summary>
    private async Task ReceiveLoopAsync(SerialPort port, CancellationToken ct)
    {
        _parser.Reset(); // drop any partial frame left over from a previous connection
        var buffer = new byte[256];
        var stream = port.BaseStream;
        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) { await Task.Delay(5, ct); continue; }
            Interlocked.Add(ref _bytesRx, read);
            for (var i = 0; i < read; i++)
                if (_parser.PushByte(buffer[i], out var frame))
                    OnFrame(frame);
        }
    }

    /// <summary>Route a parsed frame: cache a status NOTIFY, complete a pending REQUEST on RESPONSE/NACK.</summary>
    private void OnFrame(Rs422Frame frame)
    {
        lock (_stateGate) _lastRxAt = _clock.UtcNow;
        _gpio.ToggleCommLed(); // blink the comms LED on every frame received from the power board
        ClearCommsLoss();

        switch (frame.Type)
        {
            case Rs422Wire.TypeNotify when frame.Cmd == CmdGetStatus && frame.Payload.Length >= StatusPayloadSize:
                DecodeStatus(frame.Payload);
                break;
            case Rs422Wire.TypeResponse:
            case Rs422Wire.TypeNack:
                if (_pending.TryRemove(frame.Seq, out var tcs))
                    tcs.TrySetResult(new RxResponse(frame.Type == Rs422Wire.TypeResponse, frame.Status, frame.Payload));
                break;
            // The power board never issues REQUESTs to us; any other NOTIFY is just keep-alive traffic.
        }
    }

    private async Task HousekeepingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var now = _clock.UtcNow;
                bool open;
                DateTimeOffset lastTx, lastRx;
                lock (_stateGate) { open = _port?.IsOpen == true; lastTx = _lastTxAt; lastRx = _lastRxAt; }

                // Keep the firmware's comms watchdog fed: a NOTIFY with no payload counts as traffic.
                if (open && (now - lastTx).TotalMilliseconds >= _opts.KeepAliveMs)
                    try { await SendFrameAsync(Rs422Wire.TypeNotify, NextSeq(), CmdGetStatus, StatusOk, [], ct); }
                    catch (Exception ex) { _logger.LogDebug(ex, "RS422: keep-alive falhou."); }

                // Offline watchdog: no valid frame for LinkTimeoutMs ⇒ the board is gone.
                if (open && (now - lastRx).TotalMilliseconds >= _opts.LinkTimeoutMs)
                    RaiseCommsLoss();

                // Periodic link-efficiency report.
                if (_opts.StatsLogMs > 0 && (now - _statsAt).TotalMilliseconds >= _opts.StatsLogMs)
                    LogLinkStats(now);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    /// <summary>Periodic link-efficiency report over the window since the last call — the control-side
    /// counterpart of the firmware's RX stats: RX frames ok/bad + error %, RX/TX throughput, request timeouts.</summary>
    private void LogLinkStats(DateTimeOffset now)
    {
        long ok = _parser.FramesOk, bad = _parser.FramesBad;
        long brx = Interlocked.Read(ref _bytesRx), btx = Interlocked.Read(ref _bytesTx);
        long ftx = Interlocked.Read(ref _framesTx), to = Interlocked.Read(ref _reqTimeouts);

        var secs = Math.Max(0.001, (now - _statsAt).TotalSeconds);
        long dOk = ok - _snapOk, dBad = bad - _snapBad, dBrx = brx - _snapBytesRx,
             dBtx = btx - _snapBytesTx, dFtx = ftx - _snapFramesTx, dTo = to - _snapTimeouts;
        long rxTotal = dOk + dBad;
        double errPct = rxTotal > 0 ? 100.0 * dBad / rxTotal : 0;

        _logger.LogInformation(
            "RS422 stats {Secs:F0}s @ {Baud} baud: RX {Ok} ok/{Bad} bad ({Err:F2}% err, {Rps:F1} fr/s, {Rbps:F0} B/s)"
            + " | TX {Ftx} fr ({Tbps:F0} B/s) | req timeouts {To}",
            secs, _opts.BaudRate, dOk, dBad, errPct, dOk / secs, dBrx / secs, dFtx, dBtx / secs, dTo);

        _snapOk = ok; _snapBad = bad; _snapBytesRx = brx; _snapBytesTx = btx;
        _snapFramesTx = ftx; _snapTimeouts = to; _statsAt = now;
    }

    // ============================================================================================
    // REQUEST / RESPONSE
    // ============================================================================================

    /// <summary>Send a REQUEST and await the matching RESPONSE, retransmitting on timeout. Throws on a NACK,
    /// a closed port, or exhausted retries — surfacing to RunManager/SettingsService as a board failure.</summary>
    private async Task<byte[]> RequestAsync(byte cmd, byte[] payload, CancellationToken ct)
    {
        await _reqGate.WaitAsync(ct);
        try
        {
            var seq = NextSeq();
            var attempts = 1 + Math.Max(0, _opts.RequestRetries);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                var tcs = new TaskCompletionSource<RxResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[seq] = tcs;
                try
                {
                    await SendFrameAsync(Rs422Wire.TypeRequest, seq, cmd, StatusOk, payload, ct);
                    var resp = await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(_opts.RequestTimeoutMs), ct);
                    if (!resp.Ok)
                        throw new InvalidOperationException(
                            $"RS422: placa rejeitou o comando 0x{cmd:X2} (status 0x{resp.Status:X2}).");
                    return resp.Payload;
                }
                catch (TimeoutException)
                {
                    _pending.TryRemove(seq, out _);
                    Interlocked.Increment(ref _reqTimeouts);
                    if (attempt == attempts)
                        throw new TimeoutException(
                            $"RS422: sem resposta ao comando 0x{cmd:X2} após {attempts} tentativas.");
                    _logger.LogWarning("RS422: timeout no comando 0x{Cmd:X2} (tentativa {Attempt}/{Total}).",
                        cmd, attempt, attempts);
                }
                finally
                {
                    _pending.TryRemove(seq, out _);
                }
            }
            throw new TimeoutException($"RS422: sem resposta ao comando 0x{cmd:X2}.");
        }
        finally
        {
            _reqGate.Release();
        }
    }

    private byte NextSeq()
    {
        lock (_stateGate) return _seq++;
    }

    // ============================================================================================
    // Serial TX — framing delegated to Rs422Wire.BuildFrame (COBS + CRC-32 + 0x00 delimiter)
    // ============================================================================================

    private async Task SendFrameAsync(byte type, byte seq, byte cmd, byte status, byte[] payload, CancellationToken ct)
    {
        var wire = new byte[Rs422Wire.MaxWire];
        var enc = Rs422Wire.BuildFrame(type, seq, cmd, status, payload, wire);

        await _txGate.WaitAsync(ct);
        try
        {
            SerialPort? port;
            lock (_stateGate) port = _port;
            if (port is null || !port.IsOpen) throw new InvalidOperationException("RS422: porta fechada.");
            await port.BaseStream.WriteAsync(wire.AsMemory(0, enc), ct);
            Interlocked.Add(ref _bytesTx, enc);
            Interlocked.Increment(ref _framesTx);
            lock (_stateGate) _lastTxAt = _clock.UtcNow;
        }
        finally
        {
            _txGate.Release();
        }
    }

    // ============================================================================================
    // Status decode (35 B LE) + fault mapping
    // ============================================================================================

    /// <summary>Decode the 35-byte status push into <see cref="SensorReadings"/> and edge-trigger faults.
    /// SensorReadings only carries the channels the frontend chart shows today; the richer status fields
    /// (VREG/PD, the three fans + duties, MCU health, the fault bitfield) await the SensorReadings expansion
    /// noted in TODO.md.</summary>
    private void DecodeStatus(ReadOnlySpan<byte> p)
    {
        // state:u8, fault_code:u16, fault_flags:u16, oven_x10:i16, board_x10:i16, vbus_mv:u16, vreg_mv:u16,
        // pd_mv:u16, current_ma:i16, fan_intake:u16, fan_exhaust:u16, fan_board:u16, duty×3:u8, mcu_x10:i16,
        // vdda_mv:u16, reset_reason:u8, hours_min:u32.
        var faultCode = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(1, 2));
        var faultFlags = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(3, 2));
        var ovenC = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(5, 2)) / 10.0;
        var boardC = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(7, 2)) / 10.0;
        var vbusV = CentivoltsToVolts(BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(9, 2))); // 0–180 VDC (×100)
        var currentA = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(15, 2)) / 1000.0;
        var fanIntake = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(17, 2));
        var fanBoard = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(21, 2));

        lock (_stateGate)
        {
            _last = new SensorReadings(boardC, fanBoard, ovenC, fanIntake, vbusV, currentA);
        }

        RaiseBoardFault(faultCode, faultFlags);
    }

    /// <summary>The firmware fault bitfield mapped to the catalogued E-codes, in raise-priority order
    /// (most severe first). <c>fault_code≠0</c> raises the first matching flag; the event is edge-triggered.</summary>
    private static readonly (ushort Bit, string Code)[] FaultBitMap =
    [
        (1 << 2, "E-101"), // OVER_TEMP  — oven over-temperature
        (1 << 0, "E-102"), // TC         — thermocouple open/short
        (1 << 5, "E-110"), // OVER_CURR  — output over-current
        (1 << 6, "E-110"), // GATE       — gate-driver fault (power stage)
        (1 << 3, "E-120"), // OVER_VOLT  — bus over-voltage
        (1 << 4, "E-120"), // UNDER_VOLT — bus under-voltage
        (1 << 7, "E-120"), // POWER_GOOD — power-good lost
        (1 << 1, "E-140"), // NTC        — heatsink NTC fault
        (1 << 9, "E-130"), // COMMS_LOSS — host link lost
        (1 << 8, "E-150"), // FAN        — a fan stalled
    ];

    private void RaiseBoardFault(ushort faultCode, ushort faultFlags)
    {
        string? code = null;
        if (faultCode != 0)
        {
            foreach (var (bit, c) in FaultBitMap)
                if ((faultFlags & bit) != 0) { code = c; break; }
            code ??= "E-101"; // a fault is set but no known flag bit — treat as a generic critical
        }

        bool changed;
        lock (_stateGate)
        {
            changed = code != _raisedFaultCode;
            _raisedFaultCode = code;
        }
        if (changed && code is not null)
        {
            _logger.LogWarning("RS422: placa reportou falha {Code} (flags 0x{Flags:X4}).", code, faultFlags);
            FaultRaised?.Invoke(this, new FaultRaised(code, _clock.UtcNow));
        }
    }

    private void RaiseCommsLoss()
    {
        bool first;
        lock (_stateGate) { first = !_offline; _offline = true; }
        if (first)
        {
            _logger.LogWarning("RS422: placa de potência offline (perda de comunicação).");
            FaultRaised?.Invoke(this, new FaultRaised("E-130", _clock.UtcNow));
        }
    }

    private void ClearCommsLoss()
    {
        bool was;
        lock (_stateGate) { was = _offline; _offline = false; }
        if (was) _logger.LogInformation("RS422: comunicação com a placa restabelecida.");
    }

    // ============================================================================================
    // Small helpers
    // ============================================================================================

    private static ushort ClampU16(double v) => (ushort)Math.Clamp(v, 0, ushort.MaxValue);
    private static short ClampI16(double v) => (short)Math.Clamp(v, short.MinValue, short.MaxValue);

    // The 0–180 VDC bus is carried as CENTIVOLTS (×100, two decimals): 180.00 V ↔ 18000, which fits a u16
    // (millivolts would overflow). vbus (status), the min/max-vbus config thresholds and the drive-output
    // sweep all use this; the low-voltage rails (vreg/pd/vdda) stay in millivolts.
    private static ushort VoltsToCentivolts(double volts) => ClampU16(volts * 100);
    private static double CentivoltsToVolts(int centivolts) => centivolts / 100.0;

    public void Dispose()
    {
        _cts.Cancel();
        try { _port?.Dispose(); } catch { /* ignore */ }
        try { _deGpio?.Dispose(); } catch { /* ignore */ } // releases the DE pin (back to input)
        try { Task.WaitAll([_linkTask, _houseTask], TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        _cts.Dispose();
        _txGate.Dispose();
        _reqGate.Dispose();
    }

    // The response handed back from the RX loop to a waiting RequestAsync (frame + CRC handled by Rs422Wire).
    private readonly record struct RxResponse(bool Ok, byte Status, byte[] Payload);
}
