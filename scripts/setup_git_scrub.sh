#!/usr/bin/env bash
#
# setup_git_scrub.sh - one-time setup so commits never contain real secrets in appsettings.json.
#
# Registers the "secretscrub" git clean filter referenced by .gitattributes. After this, any time git stages
# src/ReflowOven.Api/appsettings.json it stores a copy with the secrets reset to their dev placeholders, while
# your working file keeps the real local values the app actually runs with.
#
# Run this ONCE per clone (it only writes local git config; nothing is committed). Without it git ignores the
# filter and a commit of appsettings.json would carry your real secrets -- so do not skip it on a fresh clone.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

log() { printf '\033[1;36m[setup_git_scrub]\033[0m %s\n' "$*"; }

# clean = applied when content goes INTO the repo (git add / commit / diff).
# smudge defaults to identity, so a checkout keeps the (already scrubbed) committed value.
git -C "${REPO_DIR}" config filter.secretscrub.clean "python3 scripts/scrub_appsettings.py"
git -C "${REPO_DIR}" config filter.secretscrub.required true

log "Filtro 'secretscrub' registrado."
log "Commits de src/ReflowOven.Api/appsettings.json terao os segredos resetados para placeholders de dev;"
log "seu arquivo local mantem os valores reais para rodar a aplicacao."
