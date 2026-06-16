#!/usr/bin/env python3
"""Confere o que o backend ENVIA no START_PROGRAM (RS422): os SEGMENTOS
(forma, duracao, tempo acumulado, temperatura-alvo) e como sao fatiados em
chunks — sem os bytes COBS/CRC, so os pontos. Espelha exatamente
Rs422PowerBoard.StartProgramAsync (chunk de ate 48 seg, start_temp no 1o).

Uso:
  python3 tools/dump-program.py <programId>     # confere um programa salvo
  python3 tools/dump-program.py --create-max    # cria o de 50 seg (100% parabolas) e confere
  python3 tools/dump-program.py --list          # lista id + nome dos programas
  (acrescente --curve para tambem imprimir a curva amostrada t/temp do simulador)
"""
import json, sys, urllib.request, urllib.error

BASE = "http://localhost:5248"
USER, PWD = "lucas.silva", "reflow1234"

SEGS_PER_CHUNK = 48            # Rs422PowerBoard.StartProgramSegsPerChunk
START_TEMP = 0                 # DomainConstants.StartTemp
SHAPE_CODE = {"Linear": 0, "Fixo": 1, "Parábola positiva": 2, "Parábola negativa": 3}

def api(method, path, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {path} -> HTTP {e.code}: {e.read().decode(errors='replace')}")

def login():
    r = api("POST", "/api/auth/login", body={"username": USER, "password": PWD})
    if not r or not r.get("token"):
        sys.exit("login falhou: " + str(r))
    return r["token"]

def clamp(v, lo, hi):
    return max(lo, min(hi, v))

def make_max_parabola():
    # 50 segmentos, TODOS parabola, 60 s cada; alterna sobe (Parabola+ ->250) e desce (Parabola- ->50).
    segs = []
    for i in range(50):
        if i % 2 == 0:
            segs.append({"ramp": "Parábola positiva", "durationSec": 60, "temp": 250})
        else:
            segs.append({"ramp": "Parábola negativa", "durationSec": 60, "temp": 50})
    return {"name": "TESTE MAX-PARABOLA (50 seg)",
            "description": "50 segmentos, 100% parabolas (+/- alternadas) — teste de comunicacao",
            "segments": segs, "profile": None}

def dump(prog, show_curve=False):
    segs = prog.get("segments")
    if not segs:
        sys.exit("programa sem 'segments' (seed?). Use um id criado via API.")
    n = len(segs)
    npar = sum(1 for s in segs if "Parábola" in s["ramp"])
    print(f"\nPrograma: {prog['name']}   (id {prog['id']})")
    print(f"segmentos: {n}   parabolas: {npar}/{n}   pts amostrados (simulador): {len(prog.get('profile', []))}")

    off = 0; chunk = 0; t = 0
    while off < n:
        count = min(SEGS_PER_CHUNK, n - off)
        first = off == 0; last = off + count >= n
        flags = (1 if first else 0) | (2 if last else 0)
        size = 4 + (2 if first else 0) + count * 5
        ftxt = "+".join([x for x, b in (("first", first), ("last", last)) if b]) or "-"
        hdr = (f"\n== CHUNK {chunk}  total={n} offset={off} count={count} "
               f"flags=0x{flags:02X}({ftxt}) payload={size}B")
        if first:
            hdr += f"  start_temp_x10={round(START_TEMP * 10)}"
        print(hdr)
        print("  seg  forma               cod  dur(s)   alvo C / x10(i16)   t_ini -> t_fim")
        for i in range(count):
            s = segs[off + i]
            code = SHAPE_CODE.get(s["ramp"], 1)
            dur = clamp(int(s["durationSec"]), 0, 65535)
            x10 = clamp(round(s["temp"] * 10), -32768, 32767)
            t_end = t + dur
            print(f"  {off+i:3d}  {s['ramp']:<18} {code:>3}  {dur:>5}   "
                  f"{s['temp']:>6} / {x10:<6}   {t:>5}s -> {t_end:<5}s")
            t = t_end
        off += count; chunk += 1

    total_t = sum(int(s["durationSec"]) for s in segs)
    tmin = min(s["temp"] for s in segs); tmax = max(s["temp"] for s in segs)
    print(f"\nresumo: {chunk} chunk(s) | tempo total {total_t}s "
          f"({total_t//60}min{total_t%60:02d}) | alvo {tmin}..{tmax} C")

    if show_curve:
        prof = prog.get("profile", [])
        print(f"\ncurva amostrada (simulador) — {len(prof)} pts (t, C):")
        for p in prof:
            print(f"  {p['t']:>7.1f}s  {p['temp']:>7.2f}")

def main():
    args = sys.argv[1:]
    curve = "--curve" in args
    args = [a for a in args if a != "--curve"]
    token = login()
    if not args or args[0] == "--list":
        r = api("GET", "/api/programs", token)
        for it in r.get("items", []):
            print(f"{it['id']}  {it['name']}")
        return
    if args[0] == "--create-max":
        prog = api("POST", "/api/programs", token, make_max_parabola())
        print(f"criado: {prog['id']}")
        dump(prog, curve)
        return
    prog = api("GET", "/api/programs/" + args[0], token)
    dump(prog, curve)

if __name__ == "__main__":
    main()
