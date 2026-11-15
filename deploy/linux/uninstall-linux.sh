#!/usr/bin/env bash
# Removes the RigForge user daemon, udev rules, desktop launcher, and icon.
# Profile data in ~/.config/rigforge is kept unless --purge is passed.
set -euo pipefail

PREFIX="${HOME}/.local/share/rigforge"
UNIT_PATH="${HOME}/.config/systemd/user/rigforge.service"
PURGE=0

for arg in "$@"; do
  case "${arg}" in
    --purge) PURGE=1 ;;
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

if systemctl --user list-unit-files rigforge.service >/dev/null 2>&1; then
  systemctl --user disable --now rigforge.service || true
fi

rm -f "${UNIT_PATH}"
systemctl --user daemon-reload || true

if [[ -f /etc/udev/rules.d/60-rigforge.rules || -f /etc/modules-load.d/rigforge-i2c.conf ]]; then
  as_root rm -f /etc/udev/rules.d/60-rigforge.rules /etc/modules-load.d/rigforge-i2c.conf
  as_root udevadm control --reload-rules || true
  as_root udevadm trigger || true
fi

if [[ -d "${PREFIX}" ]]; then
  rm -rf "${PREFIX}"
fi

rm -f "${HOME}/.local/share/applications/rigforge.desktop"
rm -f "${HOME}/.local/share/icons/hicolor/scalable/apps/rigforge.svg"

echo "RigForge user service, program files, desktop entry, and icon were removed."
if [[ "${PURGE}" == "1" ]]; then
  rm -rf "${HOME}/.config/rigforge"
  echo "Saved profiles in ~/.config/rigforge were removed."
else
  echo "Saved profiles in ~/.config/rigforge were kept."
fi
