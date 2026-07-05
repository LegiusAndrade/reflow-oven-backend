#!/usr/bin/env bash
#
# install.sh — real deploy of the Reflow Oven backend on the Pi (audit BE-1 + BE-3 + BE-8).
#
# What it does (idempotent — safe to re-run for upgrades):
#   1. Publishes the API self-contained for linux-arm64 using the .NET SDK Docker image
#      (the Pi host has no .NET) into /opt/reflow-oven/backend.
#   2. Creates /etc/reflow-oven/backend.env (0600) from deploy/backend.env.example on first run,
#      generating a strong JWT signing key, seed passwords and a strong PostgreSQL password —
#      and applies that password to the running cluster (ALTER USER).
#   3. Installs + enables reflow-backend.service (Restart=always, Production) and
#      reflow-rtc.service (bind the ISL1208 → /dev/rtc0 → hwclock --hctosys at boot).
#   4. Enables the I²C bus in /boot/firmware/config.txt if needed (RTC prerequisite; reboot required
#      the first time) and makes sure the hwclock binary exists.
#
# Usage:  sudo deploy/install.sh          (from the repo root on the Pi)
#
# The frontend unit (deploy/reflow-front.service) is a template — install it separately once the
# reflow-oven-front repo is built on the Pi (instructions in the unit header).

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR=/opt/reflow-oven/backend
ENV_DIR=/etc/reflow-oven
ENV_FILE="${ENV_DIR}/backend.env"
SERVICE_USER=reflow-oven
SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
CONFIG_TXT=/boot/firmware/config.txt

log()  { printf '\033[1;36m[install]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[install]\033[0m %s\n' "$*"; }

[ "$(id -u)" -eq 0 ] || { echo "Run as root: sudo deploy/install.sh" >&2; exit 1; }
id "${SERVICE_USER}" >/dev/null 2>&1 || { echo "User '${SERVICE_USER}' not found." >&2; exit 1; }

# --- 1) Publish (self-contained linux-arm64; host needs only Docker) -------------------------------
log "Publishing ReflowOven.Api (Release, linux-arm64, self-contained) via ${SDK_IMAGE}…"
mkdir -p "${PUBLISH_DIR}.next"
docker run --rm \
    -v "${REPO_DIR}:/src" \
    -v "${PUBLISH_DIR}.next:/out" \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    "${SDK_IMAGE}" \
    dotnet publish /src/src/ReflowOven.Api/ReflowOven.Api.csproj \
        -c Release -r linux-arm64 --self-contained true -o /out

# Swap the new publish in atomically-ish (the unit restarts onto the new tree at the end).
if [ -d "${PUBLISH_DIR}" ]; then rm -rf "${PUBLISH_DIR}.old"; mv "${PUBLISH_DIR}" "${PUBLISH_DIR}.old"; fi
mv "${PUBLISH_DIR}.next" "${PUBLISH_DIR}"
chown -R root:root "${PUBLISH_DIR}"
chmod +x "${PUBLISH_DIR}/ReflowOven.Api"
log "Publish at ${PUBLISH_DIR} ($(du -sh "${PUBLISH_DIR}" | cut -f1))."

# --- 2) Environment file with real secrets (first run only) ---------------------------------------
if [ ! -f "${ENV_FILE}" ]; then
    log "Generating ${ENV_FILE} with strong secrets…"
    mkdir -p "${ENV_DIR}"
    PG_PASS="$(openssl rand -hex 24)"
    sed -e "s|Password=CHANGE_ME|Password=${PG_PASS}|" \
        -e "s|Jwt__SigningKey=CHANGE_ME_32_BYTES_MINIMUM|Jwt__SigningKey=$(openssl rand -base64 48)|" \
        -e "s|Master__Password=CHANGE_ME|Master__Password=$(openssl rand -base64 18)|" \
        -e "s|Admin__Password=CHANGE_ME|Admin__Password=$(openssl rand -base64 18)|" \
        -e "s|Regular__Password=CHANGE_ME|Regular__Password=$(openssl rand -base64 18)|" \
        -e "s|Technician__Password=CHANGE_ME|Technician__Password=$(openssl rand -base64 18)|" \
        "${REPO_DIR}/deploy/backend.env.example" > "${ENV_FILE}"
    chmod 600 "${ENV_FILE}"; chown root:root "${ENV_FILE}"

    # Apply the generated password to the running cluster (POSTGRES_PASSWORD in the compose file only
    # counts at volume-init time). Harmless if it equals the current one.
    if docker ps --format '{{.Names}}' | grep -q '^reflowoven-postgres$'; then
        log "Setting the PostgreSQL password (ALTER USER reflow)…"
        docker exec reflowoven-postgres psql -U reflow -d reflowoven \
            -c "ALTER USER reflow WITH PASSWORD '${PG_PASS}';" >/dev/null
        # Keep dev compose recreations in sync (gitignored .env consumed by docker-compose.yml).
        echo "POSTGRES_PASSWORD=${PG_PASS}" > "${REPO_DIR}/.env"
        chown "${SERVICE_USER}:${SERVICE_USER}" "${REPO_DIR}/.env"; chmod 600 "${REPO_DIR}/.env"
    else
        warn "reflowoven-postgres is not running — set the DB password to match ${ENV_FILE} yourself."
    fi

    warn "Seed passwords were generated into ${ENV_FILE} (root-only). If the database was already"
    warn "seeded with dev passwords, change those users' passwords via the UI — env overrides only"
    warn "apply on the first seed of a fresh database (the Technician is config-live, though)."
else
    log "${ENV_FILE} already exists — keeping it (upgrade run)."
fi

# --- 3) RTC prerequisite: I²C bus + hwclock ---------------------------------------------------------
if ! command -v hwclock >/dev/null 2>&1; then
    log "Installing util-linux-extra (hwclock)…"
    apt-get update -qq && apt-get install -y -qq util-linux-extra
fi
REBOOT_NEEDED=0
if grep -Eq '^\s*dtparam=i2c_arm=on' "${CONFIG_TXT}"; then
    log "I²C already enabled in ${CONFIG_TXT}."
elif grep -Eq '^\s*#\s*dtparam=i2c_arm=on' "${CONFIG_TXT}"; then
    log "Enabling I²C in ${CONFIG_TXT} (uncomment dtparam=i2c_arm=on)…"
    sed -i 's|^\s*#\s*dtparam=i2c_arm=on|dtparam=i2c_arm=on|' "${CONFIG_TXT}"
    REBOOT_NEEDED=1
else
    log "Enabling I²C in ${CONFIG_TXT} (append dtparam=i2c_arm=on)…"
    printf '\ndtparam=i2c_arm=on\n' >> "${CONFIG_TXT}"
    REBOOT_NEEDED=1
fi

# --- 4) systemd units -------------------------------------------------------------------------------
log "Installing systemd units…"
cp "${REPO_DIR}/deploy/reflow-rtc.service" /etc/systemd/system/
cp "${REPO_DIR}/deploy/reflow-backend.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable reflow-rtc.service
systemctl enable reflow-backend.service
# The RTC unit no-ops until i2c-1 exists (first install: after the reboot below).
systemctl restart reflow-rtc.service || true
systemctl restart reflow-backend.service

log "Done. Verify with:"
log "  systemctl status reflow-backend   # active (running), Production"
log "  curl -s http://127.0.0.1:5248/health"
log "  ls -l /dev/rtc0 && hwclock -r -f /dev/rtc0 && timedatectl   # RTC bound + sane time"
if [ "${REBOOT_NEEDED}" -eq 1 ]; then
    warn "I²C was just enabled in ${CONFIG_TXT} — REBOOT ONCE so i2c-1 (GPIO2/3) appears and"
    warn "reflow-rtc.service can bind the ISL1208 at 0x6f."
fi
