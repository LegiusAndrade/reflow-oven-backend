# TODO — Backend (em aberto)

O histórico completo do que já foi entregue está em **`DONE.md`**. Tarefas do front vão para
`../reflow-oven-front/TODO.md`.

## Em aberto

- **🔁 Factory reset: responder ANTES de reiniciar (+ instabilidade do `:5248`).** O
  `POST /api/maintenance/factory-reset` deve devolver **204 antes** de limpar/reiniciar — hoje, quando o
  servidor cai/reinicia, a conexão é derrubada e o front recebe um **erro de conexão** (parece falha). O front
  já trata o status 0 como "equipamento reiniciando" (FactoryResetModal, 2026‑06‑04), mas o ideal é o ack limpo.
  Durante o teste o backend **caiu** (`:5248` → 000) ao acionar o reset — investigar se é o reset reiniciando ou
  a **instabilidade geral do `:5248`** (que já caiu sozinho algumas vezes na sessão).

- **🔒 Lockout: enviar o tempo restante.** A resposta de "Muitas tentativas de login" deve incluir os
  **segundos restantes** (ex.: `retryAfterSeconds` no `LoginResult`, ou o header `Retry-After`) — hoje só diz
  "aguarde alguns minutos". Com isso o front mostra um **countdown** ("tente novamente em Xs") no lugar da
  mensagem vaga. (Front 2026‑06‑04: o erro do login já quebra linha + vira toast; falta só o tempo preciso.)

- **#3 — Notificações faltantes.** "Execução abortada" → `kind: warning` já feito; faltam **falha da placa**
  (E-1xx) e **OTA disponível** — gerar a entrada no feed do sino com o `kind` correto.
- **#7 — RS422 / hardware real + `LinuxSystemController`.** Grande, futuro: o SO do OrangePi
  (`LinuxSystemController`) e o **protocolo STM32 (RS422)** — hoje `SimulatedPowerBoard` /
  `SimulatedSystemController`. O firmware da power (`../reflow-oven-firmware`) **já implementa o lado
  peer** deste protocolo — responde a REQUESTs (slave) **e empurra o status a 1 Hz** (NOTIFY)
  (`reflow_oven/inc/comms.h` + `src/comms.cpp`, modelo em `model.h`); falta o `Rs422PowerBoard`
  (`Infrastructure/Hardware`) falar o mesmo wire format (escutar+cachear o push, mandar comandos e um
  keep-alive). Manter os dois em lock-step.

  ### Como o backend deve se comunicar com a power (RS422)

  - **Link.** RS422 full-duplex, **115200 8N1**, sem DE. **Peer-to-peer:** cada lado **responde** a
    REQUESTs (slave) **e** envia por conta própria (master). A **power empurra o status a cada 1 s**
    via NOTIFY — **não há poll**; o backend **escuta e cacheia** (`ReadAsync` devolve o último status
    recebido, sem round-trip). O **control também manda algo a cada ~1 s** (um comando ou um keep-alive
    NOTIFY) pra alimentar o watchdog da power. Cada lado watchdoga o outro (timeout 2 s).
  - **Frame.** Cada frame é **COBS-encodado** e terminado por **`0x00`** (delimitador). Decodificado:
    `header(6) + payload(0..N) + CRC-32(4)`.
    - **header (6 B):** `ver(1)=1, type(1), seq(1), cmd(1), status(1), len(1)`.
    - `type`: **REQUEST=0x01**, RESPONSE=0x02, NACK=0x03, **NOTIFY=0x04** (push one-way, sem resposta).
      `seq`: quem inicia numera (REQUEST/NOTIFY), o respondedor ecoa. Em REQUEST/NOTIFY `status=0`; em
      RESPONSE/NACK é o status. `len` = tamanho do payload.
    - **CRC-32** (poly `0xEDB88320`, = `System.IO.Hashing.Crc32`) sobre `header+payload`, anexado
      **big-endian** (MSB primeiro; o `Crc32` do .NET dá little-endian → inverter). Depois COBS no
      conjunto, depois o `0x00`.
  - **Status** (RESPONSE/NACK): `OK=0x00, UNKNOWN_CMD=0x01, BAD_PARAM=0x02, BUSY=0x03, ERROR=0xFF`.
  - **Endianness do payload:** campos **little-endian**; floats IEEE-754 LE (`BitConverter`). Só o
    trailer CRC-32 é big-endian.
  - **Unidade de tensão:** o barramento é **0–180 VDC**, carregado em **centivolts** (×100, 2 casas:
    180,00 V ↔ `18000`) pra caber num `u16` — vale pra `vbus`, `min/max_vbus` (config) e o sweep do
    `DRIVE_OUTPUT`, **apesar do sufixo `_mv`** nos nomes. Os rails de baixa tensão (`vreg`/`pd`/`vdda`)
    seguem em **mV**.

  **Comandos** (um por método de `IPowerBoard`):

  | `cmd` | método | REQUEST payload | RESPONSE payload |
  |------|--------|-----------------|------------------|
  | `0x01` GET_STATUS | `ReadAsync` (cache) | — | status (35 B) — **a power empurra ~1 Hz (NOTIFY)** |
  | `0x02` SET_CONFIGURATION | `ApplyControlConfigAsync` | Config (24 B) | — |
  | `0x03` GET_SERIAL_NUMBER | (identidade) | — | `serial:u32` |
  | `0x04` SET_SERIAL_NUMBER | — | `serial:u32` | — |
  | `0x05` START_PROGRAM | `StartProgramAsync` | `count:u8` + count×`{time_s:u16, target_c:i16}` | — |
  | `0x06` STOP | `StopAsync` | — | — |
  | `0x07` SET_CALIBRATION | `ApplyCalibrationAsync` | blob (layout reservado) | — |
  | `0x08` SELF_TEST | `RunSelfTestAsync` | `id:u8` | `id:u8, result:u8` (0=pass,1=fail,2=not-run) |
  | `0x09` DRIVE_OUTPUT | `DriveOutputAsync` | `set_mv:u16` | `set_mv:u16, measured_mv:u16` |
  | `0x0A` GET_IDENTITY | `GetIdentityAsync` | — | `serial:u32, hours_min:u32, ver_len:u8, version[ver_len]` |
  | `0x0B` GET_RUN_STATUS | (run telemetry) | — | RunStatus (11 B) — poll só durante um run |
  | `0x0C` GET_COMMS_STATS | (link health) | — | `rx_ok:u32, rx_err:u32, tx_resp:u32, tx_nack:u32` |

  - **GET_STATUS → status periódico (35 B, LE) — a power EMPURRA isto a cada 1 s (NOTIFY, cmd=0x01),
    não responde a poll:** `state:u8, fault_code:u16, fault_flags:u16, oven_temp_x10:i16,
    board_temp_x10:i16, vbus_mv:u16, vreg_mv:u16, pd_mv:u16, current_ma:i16, fan_intake_rpm:u16,
    fan_exhaust_rpm:u16, fan_board_rpm:u16, duty_intake:u8, duty_exhaust:u8, duty_board:u8,
    mcu_temp_x10:i16, vdda_mv:u16, reset_reason:u8, hours_min:u32`. Escala: temp `/10` (°C), **`vbus`
    `/100`** (centivolts — 0–180 VDC cabe num u16), demais tensões (`vreg`/`pd`/`vdda`) `/1000` (mV),
    corrente `/1000` (A). `fault_flags` é bitfield (bit0 TC, 1 NTC, 2 over-temp, 3 OV, 4 UV,
    5 OC, 6 gate, 7 PG, 8 fan, 9 comms-loss); `fault_code≠0` ⇒ `FaultRaised`. O backend **cacheia o
    último push** e o `ReadAsync` devolve essa cópia. **SensorReadings precisa crescer** (VREG/PD, os 3
    fans + os 3 duties, o fault bitfield e a saúde do MCU).
  - **GET_RUN_STATUS → telemetria de run (11 B, LE):** `phase:u8, setpoint_x10:i16, buck_duty_pct:u8,
    power_w:u16, elapsed_s:u16, remaining_s:u16, profile_index:u8`. Polled ~1 Hz **enquanto um run roda**
    (medido × alvo, drive do aquecedor, progresso). `phase`/`state`: 0 idle, 1 preheat, 2 soak, 3 reflow,
    4 cool, 5 fault.
  - **GET_COMMS_STATS → qualidade do link (16 B, LE):** `rx_ok:u32, rx_err:u32, tx_resp:u32, tx_nack:u32`.
    Taxa de erro = `rx_err/(rx_ok+rx_err)`. A power roda um **watchdog**: sem frame válido **do control**
    por 2 s ela seta o bit `comms-loss` no `fault_flags` (e, futuramente, vai a estado seguro). **Por isso
    o control precisa mandar ≥1 frame/s** — um comando real ou, se não tiver nada a dizer, um **keep-alive
    NOTIFY** (qualquer `cmd`; a power só conta como tráfego válido). Simétrico: o backend também deve
    watchdoggar o push de 1 s da power e sinalizar "power offline" se ele parar.
  - **SET_CONFIGURATION → Config (24 B, LE):** `kp:f32, ki:f32, kd:f32, max_oven_temp_c:f32,
    max_vbus_mv:u16, min_vbus_mv:u16, max_fan_rpm:u16, max_extra_time_sec:u16` (PID de `Settings` +
    `ProcessLimits`: MaxTemp→max_oven_temp_c, VoltageMax→max_vbus_mv, VoltageMin→min_vbus_mv,
    MaxFanRpm→max_fan_rpm, MaxExtraTimeSec→max_extra_time_sec).
  - **Fluxo de run.** `StartProgramAsync(profile, limits)` ⇒ enviar **SET_CONFIGURATION** (limits) e
    depois **START_PROGRAM** (profile). `StopAsync` ⇒ STOP.

  **Estado atual da power:** `GET_STATUS` / `SET_CONFIGURATION` / `GET_IDENTITY` completos fim-a-fim; os
  comandos que tocam o estágio de potência (START/STOP/DRIVE/SELF_TEST/CALIBRATION) já **decodificam e
  respondem OK**, mas a ação no aquecedor depende do controlador PID/reflow da power (ainda não
  construído). Então telemetria + config já funcionam; o resto ack sem acionar potência por enquanto.
- **#9 — Log de Operação: tela do front.** O backend está pronto e Master-only; falta o front montar o
  visualizador contra o contrato (`GET /api/operation-log`). *(time do front)*
- **Ops / Lucas.** `git push` da `develop`; segredos reais de produção (`Jwt`/`Master`/`Admin`/`Regular`) via
  ambiente; aplicar as migrations no deploy.
