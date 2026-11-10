#!/usr/bin/env bash
# Builds the React UI and publishes Windows and Linux release folders.
# Output: artifacts/rigforge-win-x64 and artifacts/rigforge-linux-x64, including deploy/ and effects/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "${SKIP_UI:-0}" != "1" ]]; then
  (cd "${ROOT}/ui" && npm ci && npm run build)
fi

publish_rid() {
  local rid="$1"
  local out="${ROOT}/artifacts/rigforge-${rid}"
  rm -rf "${out}"
  dotnet publish "${ROOT}/src/Host.Daemon/RigForge.Host.Daemon.csproj" \
    -c Release -r "${rid}" --self-contained false -o "${out}"
  dotnet publish "${ROOT}/src/Tools.HardwareScanner/RigForge.Tools.HardwareScanner.csproj" \
    -c Release -r "${rid}" --self-contained false -o "${out}"
  mkdir -p "${out}/deploy" "${out}/effects"
  cp -a "${ROOT}/deploy/." "${out}/deploy/"
  cp -a "${ROOT}/effects/." "${out}/effects/"
  echo "Published ${out}"
}

publish_rid win-x64
publish_rid linux-x64
