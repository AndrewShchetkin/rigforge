#!/usr/bin/env bash
# Installs the RigForge user daemon on Debian/Ubuntu, Fedora/RHEL, Arch/CachyOS, and openSUSE.
# Prebuilt payloads in artifacts/rigforge-linux-x64 are copied when present; otherwise the daemon is published.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PREFIX="${HOME}/.local/share/rigforge"
UNIT_DIR="${HOME}/.config/systemd/user"
PREBUILT="${ROOT}/artifacts/rigforge-linux-x64"

as_root() {
  if [[ "$(id -u)" -eq 0 ]]; then
    "$@"
  else
    sudo "$@"
  fi
}

install_payload() {
  mkdir -p "${PREFIX}"
  if [[ -x "${PREBUILT}/RigForge.Daemon" && "${FORCE_PUBLISH:-0}" != "1" ]]; then
    echo "Installing prebuilt daemon from ${PREBUILT}"
    cp -a "${PREBUILT}/." "${PREFIX}/"
  else
    echo "Publishing RigForge.Daemon (linux-x64)"
    if [[ ! -f "${ROOT}/src/Host.Daemon/wwwroot/index.html" || "${FORCE_UI:-0}" == "1" ]]; then
      (cd "${ROOT}/ui" && npm ci && npm run build)
    fi
    dotnet publish "${ROOT}/src/Host.Daemon/RigForge.Host.Daemon.csproj" \
      -c Release -r linux-x64 --self-contained false -o "${PREFIX}"
  fi
  if [[ -f "${PREFIX}/RigForge.Daemon" ]]; then
    chmod +x "${PREFIX}/RigForge.Daemon"
  fi
}

install_udev() {
  as_root install -Dm644 "${ROOT}/deploy/linux/60-rigforge.rules" /etc/udev/rules.d/60-rigforge.rules
  as_root udevadm control --reload-rules
  as_root udevadm trigger
}

install_i2c() {
  local tmp
  tmp="$(mktemp)"
  printf '%s\n' 'i2c-dev' > "${tmp}"
  as_root install -Dm644 "${tmp}" /etc/modules-load.d/rigforge-i2c.conf
  rm -f "${tmp}"
  as_root modprobe i2c-dev || true
  as_root modprobe i2c-piix4 || true
  as_root modprobe i2c-i801 || true
}

install_user_service() {
  mkdir -p "${UNIT_DIR}"
  install -Dm644 "${ROOT}/deploy/linux/rigforge.service" "${UNIT_DIR}/rigforge.service"
  systemctl --user daemon-reload
  systemctl --user enable --now rigforge.service
}

install_payload
install_udev
install_i2c
install_user_service

echo "RigForge is installed for ${USER}."
echo "Configurator: http://127.0.0.1:5757"
echo "Profiles stay in ~/.config/rigforge and were not touched."
