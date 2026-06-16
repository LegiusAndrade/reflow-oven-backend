#!/usr/bin/env python3
"""Dispara/aborta um run no backend = manda o programa pra placa de potencia
(START_PROGRAM via RS422 quando o backend esta em Hardware:Mode=Rs422).

Uso:
  python3 tools/run-program.py start <programId>   # manda os chunks e arma o run
  python3 tools/run-program.py stop                # aborta (CmdStop)
  python3 tools/run-program.py status              # estado atual do run
"""
import json, sys, urllib.request, urllib.error

BASE = "http://localhost:5248"
USER, PWD = "lucas.silva", "reflow1234"

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
        print(f"{method} {path} -> HTTP {e.code}: {e.read().decode(errors='replace')}")
        sys.exit(1)

def login():
    r = api("POST", "/api/auth/login", body={"username": USER, "password": PWD})
    if not r or not r.get("token"):
        sys.exit("login falhou: " + str(r))
    return r["token"]

def main():
    a = sys.argv[1:]
    if not a:
        sys.exit(__doc__)
    t = login()
    if a[0] == "start":
        if len(a) < 2:
            sys.exit("uso: run-program.py start <programId>")
        r = api("POST", "/api/runs/start", t, {"programId": a[1]})
        print("START ok:")
    elif a[0] == "stop":
        r = api("POST", "/api/runs/stop", t)
        print("STOP ok:")
    elif a[0] == "status":
        r = api("GET", "/api/runs/status", t)
    else:
        sys.exit(__doc__)
    print(json.dumps(r, indent=2, ensure_ascii=False))

if __name__ == "__main__":
    main()
