#!/usr/bin/env bash
# pi-setup-uart.sh -- provisiona a UART RS422 (PL011/ttyAMA0) do Raspberry Pi 4 pra falar com a placa de potencia.
# Idempotente e auto-contido: pode rodar em qualquer Pi 4 novo (embute o overlay aqui, nao depende de outros arquivos).
#
# O QUE FAZ no /boot/firmware/config.txt:
#   1. enable_uart=1             -> habilita a UART no header de 40 pinos
#   2. dtoverlay=disable-bt      -> tira o Bluetooth do PL011 e poe o PL011 (ttyAMA0) nos GPIO14/15
#   3. dtoverlay=uart0-fixedclk  -> CORRIGE o clock do PL011 (compila+instala o overlay embutido abaixo)
#   ...e remove as tentativas furadas (init_uart_clock, force_turbo, dtparam=uart0_clkrate).
#
# POR QUE O OVERLAY (o bug e a cura):
#   Neste Pi 4 o PL011 roda o clock a ~38.4 MHz, mas o kernel acha que e 48 MHz -> TODO baud sai a 0.8x
#   (pede 115200, sai ~92160 -> o link embaralha). init_uart_clock e ignorado no Pi 4; force_turbo/core_freq
#   so afetam o mini UART; dtparam=uart0_clkrate reprograma o cprman e re-aplica o 0.8x. O que FUNCIONA e um
#   overlay 'fixed-clock' que declara o clock REAL (38.4 MHz) como uartclk do PL011: ele NAO mexe no hardware
#   (deixa em 38.4 MHz) e so diz a verdade pro driver -> 115200 vira 115200 de verdade no fio. Mesmo mecanismo
#   do overlay oficial midi-uart0.
#
# USO:   sudo bash pi-setup-uart.sh   ->   sudo reboot   ->   verificar (instrucoes no fim)
set -euo pipefail

# Clock REAL do PL011 nesta familia de placas (= 0.8 x 48 MHz). Se um Pi especifico ainda embaralhar depois
# deste script, meca o fator real (manda um baud conhecido, veja o que a placa de potencia recebe) e ajuste:
#   CLK_HZ = 48000000 * (baud_recebido / baud_pedido)
CLK_HZ=38400000

if [[ $EUID -ne 0 ]]; then
  echo "Rode como root:  sudo bash $0" >&2
  exit 1
fi

# Compilador de device-tree (pra gerar o .dtbo)
if ! command -v dtc >/dev/null 2>&1; then
  echo "Instalando device-tree-compiler..."
  apt-get update -qq && apt-get install -y device-tree-compiler
fi

CFG=/boot/firmware/config.txt
[[ -f "$CFG" ]] || CFG=/boot/config.txt
[[ -f "$CFG" ]] || { echo "Nao achei config.txt em /boot/firmware/ nem /boot/." >&2; exit 1; }

OVERLAY_DIR=/boot/firmware/overlays
[[ -d "$OVERLAY_DIR" ]] || OVERLAY_DIR=/boot/overlays
[[ -d "$OVERLAY_DIR" ]] || { echo "Nao achei a pasta de overlays." >&2; exit 1; }

BAK="$CFG.bak.$(date +%Y%m%d-%H%M%S)"
cp -a "$CFG" "$BAK"
echo "config:   $CFG"
echo "overlays: $OVERLAY_DIR"
echo "backup:   $BAK"
echo "clock:    ${CLK_HZ} Hz"
echo

# 1) Compila e instala o overlay uart0-fixedclk (fonte embutida)
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
cat > "$TMP/uart0-fixedclk.dts" <<DTS
/dts-v1/;
/plugin/;
/ {
    compatible = "brcm,bcm2711";
    fragment@0 {
        target-path = "/";
        __overlay__ {
            uart0_fixedclk: uart0_fixedclk {
                compatible = "fixed-clock";
                #clock-cells = <0>;
                clock-frequency = <${CLK_HZ}>;
            };
        };
    };
    fragment@1 {
        target = <&uart0>;
        __overlay__ {
            clocks = <&uart0_fixedclk>, <&clocks 5>;   /* uartclk = fixed real ; apb_pclk = VPU(5) */
            clock-names = "uartclk", "apb_pclk";
        };
    };
};
DTS
dtc -@ -I dts -O dtb -o "$OVERLAY_DIR/uart0-fixedclk.dtbo" "$TMP/uart0-fixedclk.dts" 2>/dev/null
echo "  + overlay instalado: $OVERLAY_DIR/uart0-fixedclk.dtbo"

# 2) Remove linhas gerenciadas por este script + as tentativas furadas (idempotente)
sed -i -E '/^[[:space:]]*init_uart_clock=/d; /^[[:space:]]*force_turbo=/d; /^[[:space:]]*dtparam=uart0_clkrate=/d; /^[[:space:]]*enable_uart=/d; /^[[:space:]]*dtoverlay=disable-bt[[:space:]]*$/d; /^[[:space:]]*dtoverlay=uart0-fixedclk[[:space:]]*$/d' "$CFG"

# 3) Adiciona as 3 linhas corretas
{ echo 'enable_uart=1'; echo 'dtoverlay=disable-bt'; echo 'dtoverlay=uart0-fixedclk'; } >> "$CFG"
echo "  + config.txt: enable_uart=1 / dtoverlay=disable-bt / dtoverlay=uart0-fixedclk"

echo
echo "Pronto. Aplique com:   sudo reboot"
echo
echo "Verificar depois do boot:"
echo "  ls -l /dev/serial0                                       # -> ttyAMA0"
echo "  sudo grep -i uart /sys/kernel/debug/clk/clk_summary      # 'uart' deviceless + uart0_fixedclk em ${CLK_HZ}"
echo "  # envio cru a 115200 deve chegar LIMPO na placa de potencia (0x41 0x42 0x43 0x44 0x45):"
echo "  python3 \$HOME/reflow-oven-backend/tools/rs422-send.py ABCDEABCDE --port /dev/serial0"
