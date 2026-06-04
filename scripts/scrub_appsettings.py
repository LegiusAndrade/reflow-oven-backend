#!/usr/bin/env python3
"""Git clean filter: reset every secret in appsettings.json to its dev placeholder before it enters the repo.

Registered as the "secretscrub" clean filter (see .gitattributes + scripts/setup_git_scrub.sh). Git runs it
whenever it stages or diffs src/ReflowOven.Api/appsettings.json: it reads the JSON from stdin and writes it
back with the sensitive fields below forced to the committed dev placeholders. Your working-tree file is never
touched, so the app you run keeps your real local values -- only the committed blob is scrubbed. This lets the
real secrets live in appsettings.json locally (no .env needed) while git only ever sees the dev placeholders.

If you intentionally change a placeholder (the dev default that SHOULD live in git), update it here too, or the
filter will keep rewriting it back. Unknown/extra keys pass through untouched, so adding config is safe.
"""
import json
import sys

# (path to the key) -> value that is safe to commit (the dev placeholder already tracked in git).
# Covers EVERY secret, not just user/password: JWT key, DB connection, SMTP creds and the seed accounts.
PLACEHOLDERS = {
    ("ConnectionStrings", "Default"): "Host=localhost;Port=5432;Database=reflowoven;Username=reflow;Password=reflow",
    ("Jwt", "SigningKey"): "dev-only-change-me-please-use-32-bytes-minimum!",
    ("Technician", "Username"): "calibracao",
    ("Technician", "Password"): "calibra",
    ("Master", "Username"): "dev.pandewilly",
    ("Master", "Password"): "pandewilly",
    ("Master", "Email"): "master@reflow.local",
    ("Admin", "Username"): "lucas.silva",
    ("Admin", "Password"): "reflow1234",
    ("Admin", "Email"): "lucas@reflow.local",
    ("Regular", "Username"): "vanessa",
    ("Regular", "Password"): "reflow1234",
    ("Regular", "Email"): "vanessa@reflow.local",
    ("Email", "Smtp", "User"): "",
    ("Email", "Smtp", "Password"): "",
    ("Email", "Smtp", "From"): "",
}

data = sys.stdin.read()
try:
    cfg = json.loads(data)
except Exception:
    # Never corrupt a commit: if it isn't valid JSON, pass it through unchanged.
    sys.stdout.write(data)
    sys.exit(0)

for path, safe_value in PLACEHOLDERS.items():
    node = cfg
    for key in path[:-1]:
        node = node.get(key) if isinstance(node, dict) else None
        if node is None:
            break
    if isinstance(node, dict) and path[-1] in node:
        node[path[-1]] = safe_value

json.dump(cfg, sys.stdout, indent=2, ensure_ascii=False)
sys.stdout.write("\n")
