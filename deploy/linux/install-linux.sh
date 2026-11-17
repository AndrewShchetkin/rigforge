#!/usr/bin/env bash
# Installs the RigForge user daemon on Debian/Ubuntu, Fedora/RHEL, Arch/CachyOS, and openSUSE.
# Works from a repository checkout and from an unpacked rigforge-linux-x64 release
# (RigForge.Daemon sits next to deploy/linux). Pass --no-autostart to skip enabling the user service.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PREFIX="${HOME}/.local/share/rigforge"
UNIT_DIR="${HOME}/.config/systemd/user"
PREBUILT="${ROOT}/artifacts/rigforge-linux-x64"
AUTOSTART=1

for arg in "$@"; do
  case "${arg}" in
    --no-autostart) AUTOSTART=0 ;;
    *)
      echo "Unknown argument: ${arg}" >&2
      exit 2
      ;;
  esac
done

as_root() {
  if [[ "$(id -u)" -eq 0 ]]; then
    "$@"
  else
    sudo "$@"
  fi
}

install_payload() {
  mkdir -p "${PREFIX}"
  if [[ "${ROOT}" == "${PREFIX}" ]]; then
    echo "Release payload is already in ${PREFIX}"
  elif [[ -x "${ROOT}/RigForge.Daemon" && ! -f "${ROOT}/Directory.Build.props" ]]; then
    echo "Installing release payload from ${ROOT}"
    cp -a "${ROOT}/." "${PREFIX}/"
  elif [[ -x "${PREBUILT}/RigForge.Daemon" && "${FORCE_PUBLISH:-0}" != "1" ]]; then
    echo "Installing prebuilt daemon from ${PREBUILT}"
    cp -a "${PREBUILT}/." "${PREFIX}/"
  else
    echo "Publishing RigForge.Daemon (linux-x64)"
    if [[ ! -f "${ROOT}/src/Host.Daemon/wwwroot/index.html" || "${FORCE_UI:-0}" == "1" ]]; then
      (cd "${ROOT}/ui" && npm ci && npm run build)
    fi
    dotnet publish "${ROOT}/src/Host.Daemon/RigForge.Host.Daemon.csproj" \
      -c Release -r linux-x64 --self-contained false -o "${PREFIX}"
    dotnet publish "${ROOT}/src/Tools.HardwareScanner/RigForge.Tools.HardwareScanner.csproj" \
      -c Release -r linux-x64 --self-contained false -o "${PREFIX}"
  fi
  if [[ -f "${PREFIX}/RigForge.Daemon" ]]; then
    chmod +x "${PREFIX}/RigForge.Daemon"
  fi
  if [[ -f "${PREFIX}/rigforge-scan" ]]; then
    chmod +x "${PREFIX}/rigforge-scan"
  fi
  install -m 0755 "${SCRIPT_DIR}/rigforge-open" "${PREFIX}/rigforge-open"
}

install_udev() {
  as_root install -Dm644 "${SCRIPT_DIR}/60-rigforge.rules" /etc/udev/rules.d/60-rigforge.rules
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
  install -Dm644 "${SCRIPT_DIR}/rigforge.service" "${UNIT_DIR}/rigforge.service"
  systemctl --user daemon-reload
  if [[ "${AUTOSTART}" == "1" ]]; then
    systemctl --user enable --now rigforge.service
  else
    systemctl --user disable rigforge.service || true
  fi
}

install_launcher() {
  local icon_src=""
  if [[ -f "${SCRIPT_DIR}/rigforge.svg" ]]; then
    icon_src="${SCRIPT_DIR}/rigforge.svg"
  elif [[ -f "${PREFIX}/deploy/linux/rigforge.svg" ]]; then
    icon_src="${PREFIX}/deploy/linux/rigforge.svg"
  elif [[ -f "${ROOT}/ui/public/favicon.svg" ]]; then
    icon_src="${ROOT}/ui/public/favicon.svg"
  else
    echo "rigforge.svg was not found." >&2
    exit 1
  fi

  install -Dm644 "${icon_src}" "${HOME}/.local/share/icons/hicolor/scalable/apps/rigforge.svg"
  mkdir -p "${HOME}/.local/share/applications"
  awk -v open="${PREFIX}/rigforge-open" '
    index($0, "Exec=") == 1 { print "Exec=\"" open "\""; next }
    { print }
  ' "${SCRIPT_DIR}/rigforge.desktop" > "${HOME}/.local/share/applications/rigforge.desktop"

  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "${HOME}/.local/share/icons/hicolor" || true
  fi
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${HOME}/.local/share/applications" || true
  fi
}

install_payload
install_udev
install_i2c
install_user_service
install_launcher

echo "RigForge is installed for ${USER}."
echo "Configurator: http://127.0.0.1:5757"
if [[ "${AUTOSTART}" == "1" ]]; then
  echo "The user service starts at graphical login."
else
  echo "Autostart is off. The dashboard launcher can still start rigforge.service."
fi
echo "Profiles stay in ~/.config/rigforge and were not touched."
