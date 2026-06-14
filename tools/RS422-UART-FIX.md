# Fix do clock da UART RS422 (Raspberry Pi 4 / PL011)

## Sintoma
O link RS422 entre a placa de controle (Raspberry Pi 4) e a placa de potência (STM32)
**embaralha a 115200**: a STM32 recebe lixo (`0x11 0x19 0x21 0x29 ...`) com erros de
`framing`/`noise` subindo. Vale tanto pra backend (.NET) quanto pro `rs422-send.py` (pyserial).

## Causa-raiz
Neste Pi 4 o **PL011 (ttyAMA0) roda o clock a ~38.4 MHz, mas o kernel assume 48 MHz**.
Como `baud = clock / (16 × divisor)`, todo baud sai a **0.8× (= 38.4/48)** do pedido:
pede-se 115200 e saem ~92160 no fio. É hardware/clock — **não** é bug do .NET nem do cabo.

## Correção (a que funciona)
Um **device-tree overlay `uart0-fixedclk`** que declara um `fixed-clock` de **38.4 MHz** como
o `uartclk` do PL011. Um `fixed-clock` é só um número (não reprograma o hardware), então o PL011
fica no clock real de 38.4 MHz e o driver passa a calcular o divisor certo → **115200 é 115200 de
verdade**. É o mesmo mecanismo do overlay oficial `midi-uart0`.

## Como aplicar numa Pi nova (1 comando + reboot)
Copie a pasta `tools/` pra Pi e rode:

```bash
sudo bash ~/reflow-oven-backend/tools/pi-setup-uart.sh
sudo reboot
```

O script é **idempotente e auto-contido** (embute o overlay). Ele:
1. instala `enable_uart=1` e `dtoverlay=disable-bt` (põe o PL011 nos GPIO14/15);
2. compila e instala `/boot/firmware/overlays/uart0-fixedclk.dtbo` e adiciona `dtoverlay=uart0-fixedclk`;
3. remove as tentativas furadas (`init_uart_clock`, `force_turbo`, `dtparam=uart0_clkrate`).

> O overlay `tools/uart0-fixedclk.dts` também está no repo como fonte avulsa, mas o script já o embute —
> não precisa dele à parte.

## Verificar depois do boot
```bash
ls -l /dev/serial0                                     # -> ttyAMA0
sudo grep -i uart /sys/kernel/debug/clk/clk_summary    # 'uart' vira 'deviceless' + aparece uart0_fixedclk
# envio cru a 115200 — a placa de potência tem que receber 0x41 0x42 0x43 0x44 0x45 LIMPO, sem framing:
python3 ~/reflow-oven-backend/tools/rs422-send.py ABCDEABCDE --port /dev/serial0
```

Com o overlay ativo, a backend roda em **115200 honesto** (`HardwareOptions.BaudRate = 115200`).

## Se uma placa específica ainda embaralhar
O fator pode variar por unidade/firmware. Meça o real: mande um baud conhecido e veja o que a STM32
recebe; o clock real ≈ `48000000 × (baud_recebido / baud_pedido)`. Ajuste `CLK_HZ` no topo do
`pi-setup-uart.sh` e rode de novo. (No caso típico, `0.8 × 48 MHz = 38.4 MHz`.)

## Becos sem saída (NÃO tente — já testado, não resolve)
- `init_uart_clock=...` → **ignorado no Pi 4** (era knob de Pi 1/2/3).
- `force_turbo=1` / `core_freq=...` → afetam só o **mini UART (ttyS0)**, não o PL011.
- `sudo rpi-update` → firmware novo, mesmo 0.8×.
- `dtparam=uart0_clkrate=38400000` → reprograma o cprman e **re-aplica** o 0.8× (o overlay funciona
  justamente por **não** reprogramar o hardware).
- Trocar pro mini UART (ttyS0) → pior (clock preso ao `core_freq`). Fique no ttyAMA0.
