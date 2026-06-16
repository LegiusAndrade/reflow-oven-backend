#!/usr/bin/env python3
# Cria 2 programas de teste via API (backend no container) p/ o teste e2e do START_PROGRAM.
#   P1: 6 segmentos com as 4 shapes (Linear/Fixo/Parabola+/Parabola-) -> 1 chunk.
#   P2: 50 segmentos lineares -> forca upload chunked (48 + 2).
import json, urllib.request, urllib.error

BASE = "http://localhost:5248"

def post(path, body, token=None):
    data = json.dumps(body).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + path, data=data, method="POST", headers=headers)
    try:
        with urllib.request.urlopen(req) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        raise SystemExit(f"{path} -> HTTP {e.code}: {e.read().decode()}")

res = post("/api/auth/login", {"username": "lucas.silva", "password": "reflow1234"})
token = res.get("token")
if not token:
    raise SystemExit(f"login falhou: {res}")
print("login ok")

p1 = {
    "name": "TESTE P1 - shapes + parabola",
    "description": "preheat/soak/parabola+/parabola-/peak/cooldown (1 chunk)",
    "segments": [
        {"temp": 150, "durationSec": 90, "ramp": "Linear"},
        {"temp": 150, "durationSec": 60, "ramp": "Fixo"},
        {"temp": 200, "durationSec": 50, "ramp": "Parábola positiva"},
        {"temp": 245, "durationSec": 30, "ramp": "Parábola negativa"},
        {"temp": 245, "durationSec": 15, "ramp": "Fixo"},
        {"temp": 50,  "durationSec": 90, "ramp": "Linear"},
    ],
}
r1 = post("/api/programs", p1, token)
print("P1:", r1.get("id"), "|", r1.get("name"), "| segs:", len(r1.get("segments") or []), "| pts:", len(r1.get("profile") or []))

segs = [{"temp": 80 + (i % 20) * 10, "durationSec": 20, "ramp": "Linear"} for i in range(50)]
p2 = {"name": "TESTE P2 - 50 segmentos (2 chunks)", "description": "forca upload chunked", "segments": segs}
r2 = post("/api/programs", p2, token)
print("P2:", r2.get("id"), "|", r2.get("name"), "| segs:", len(r2.get("segments") or []), "| pts:", len(r2.get("profile") or []))
