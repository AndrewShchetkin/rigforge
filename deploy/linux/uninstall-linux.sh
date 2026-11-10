#!/usr/bin/env bash
# Removes the RigForge user daemon, udev rules, and i2c autoload entry.
# Profile data in ~/.config/rigforge is left in place.
set -euo pipefail

PREFIX="${HOME}/.local/share/rigforge"
UNIT_PATH="${HOME}/.config/systemd/user/rigforge.service"

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

echo "RigForge user service and program files were removed."
echo "Saved profiles in ~/.config/rigforge were kept."
