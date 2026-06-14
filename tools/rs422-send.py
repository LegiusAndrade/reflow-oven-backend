#!/usr/bin/env python3
"""
rs422-send.py — enviador/sniffer RAW para bring-up do link RS422 (placa de CONTROLE -> placa de POTENCIA).

Manda bytes CRUS (default "ABCDE") -- NAO usa o protocolo COBS+CRC do driver. Serve pra conferir a
chegada na placa de potencia (scope / analisador logico / breakpoint no RX / rx_ring da STM32).
Opcionalmente mostra o que volta pela linha (--rx), util pra ver o NOTIFY ~1 Hz da potencia (vem como hex).

Pre-requisitos
  - O backend NAO pode estar rodando em Hardware__Mode=Rs422 -- ele abre a /dev/ttyAMA0 e segura a porta.
  - DE/TX-enable em GPIO4 precisa ficar HIGH (RS485->RS422 full-duplex). O script reafirma via pinctrl.

Exemplos
  python3 rs422-send.py                              # manda "ABCDE" uma vez
  python3 rs422-send.py OLA                          # manda "OLA"
  python3 rs422-send.py ABCDE --loop --interval 0.5  # repete a cada 0.5 s (Ctrl-C pra parar)
  python3 rs422-send.py --hex 4142430D0A             # manda os bytes 41 42 43 0D 0A
  python3 rs422-send.py ABCDE --loop --rx            # manda e tambem imprime o que chegar de volta
"""
import argparse
import subprocess
import sys
import time

import serial


def assert_de_high(pin: int) -> None:
    """Garante o DE/TX-enable em HIGH. pinctrl nao 'segura' a linha, entao nao briga com mais nada."""
    try:
        subprocess.run(["pinctrl", "set", str(pin), "op", "dh"],
                       check=True, capture_output=True, text=True)
    except Exception as e:  # noqa: BLE001 -- bring-up tool: degrade com aviso, nao aborta
        print(f"[aviso] nao forcei DE (GPIO{pin}) em HIGH via pinctrl: {e}", file=sys.stderr)


def main() -> int:
    ap = argparse.ArgumentParser(description="Enviador RAW de bytes pelo RS422 (bring-up do link).")
    ap.add_argument("payload", nargs="?", default="ABCDE", help='texto a enviar (default: "ABCDE")')
    ap.add_argument("--port", default="/dev/ttyAMA0", help="porta serial (default /dev/ttyAMA0)")
    ap.add_argument("--baud", type=int, default=115200, help="baud (default 115200, 8N1)")
    ap.add_argument("--de-pin", type=int, default=4, help="GPIO do DE/TX-enable (-1 = nao mexer)")
    ap.add_argument("--hex", action="store_true", help="interpreta o payload como hex (ex.: 414243)")
    ap.add_argument("--newline", action="store_true", help=r"acrescenta '\n' (0x0A) no fim")
    ap.add_argument("--loop", action="store_true", help="repete ate Ctrl-C")
    ap.add_argument("--interval", type=float, default=1.0, help="segundos entre envios no --loop")
    ap.add_argument("--rx", action="store_true", help="imprime os bytes que chegarem (hex)")
    args = ap.parse_args()

    try:
        data = bytes.fromhex(args.payload) if args.hex else args.payload.encode("latin-1")
    except ValueError as e:
        print(f"[erro] payload hex invalido: {e}", file=sys.stderr)
        return 2
    if args.newline:
        data += b"\n"

    if args.de_pin >= 0:
        assert_de_high(args.de_pin)

    try:
        ser = serial.Serial(args.port, args.baud,
                            bytesize=serial.EIGHTBITS, parity=serial.PARITY_NONE,
                            stopbits=serial.STOPBITS_ONE, timeout=0.2,
                            write_timeout=2, exclusive=True)
    except serial.SerialException as e:
        print(f"[erro] nao abri {args.port}: {e}\n"
              f"       O backend esta em Rs422? Ele segura a porta -- pare-o e tente de novo.",
              file=sys.stderr)
        return 1

    ascii_repr = data.decode("latin-1").replace("\n", "\\n")
    hexs = " ".join(f"{b:02X}" for b in data)
    print(f"porta={args.port} baud={args.baud} 8N1  DE=GPIO{args.de_pin}  "
          f"payload={len(data)}B ascii='{ascii_repr}' hex={hexs}")

    def dump_rx() -> None:
        if not args.rx:
            return
        chunk = ser.read(256)
        if chunk:
            print("    << RX {}B hex=".format(len(chunk)) + " ".join(f"{b:02X}" for b in chunk))

    n = 0
    try:
        while True:
            ser.write(data)
            ser.flush()
            n += 1
            print(f"[{n}] >> TX ok")
            dump_rx()
            if not args.loop:
                break
            time.sleep(args.interval)
            dump_rx()
    except KeyboardInterrupt:
        print("\n[fim] interrompido.")
    finally:
        ser.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
