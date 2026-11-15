#!/usr/bin/env bash
# Publishes a self-contained RigForge Linux archive (and Windows, unless SKIP_WIN=1).
# Output: artifacts/rigforge-linux-x64-${VERSION}.tar.gz and artifacts/SHA256SUMS.txt.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "${ROOT}/Directory.Build.props" | head -n 1)"
if [[ -z "${VERSION}" ]]; then
  echo "Version was not found in Directory.Build.props" >&2
  exit 1
fi

if [[ "${SKIP_UI:-0}" != "1" ]]; then
  (cd "${ROOT}/ui" && npm ci && npm run build)
fi

publish_rid() {
  local rid="$1"
  local out="${ROOT}/artifacts/rigforge-${rid}"
  rm -rf "${out}"
  mkdir -p "${out}"
  dotnet publish "${ROOT}/src/Tools.HardwareScanner/RigForge.Tools.HardwareScanner.csproj" \
    -c Release -r "${rid}" --self-contained true -o "${out}" -p:PublishSingleFile=false -p:Version="${VERSION}"
  dotnet publish "${ROOT}/src/Host.Daemon/RigForge.Host.Daemon.csproj" \
    -c Release -r "${rid}" --self-contained true -o "${out}" -p:PublishSingleFile=false -p:Version="${VERSION}"
  echo "Published ${out}"
}

publish_rid linux-x64
if [[ "${SKIP_WIN:-0}" != "1" ]]; then
  publish_rid win-x64
fi

LINUX_OUT="${ROOT}/artifacts/rigforge-linux-x64"
mkdir -p "${LINUX_OUT}/deploy/linux" "${LINUX_OUT}/effects"
cp -a "${ROOT}/deploy/linux/." "${LINUX_OUT}/deploy/linux/"
cp -a "${ROOT}/effects/." "${LINUX_OUT}/effects/"
cp "${ROOT}/ui/public/favicon.svg" "${LINUX_OUT}/deploy/linux/rigforge.svg"

chmod +x "${LINUX_OUT}/RigForge.Daemon" "${LINUX_OUT}/rigforge-scan"
chmod +x "${LINUX_OUT}/deploy/linux/"*.sh "${LINUX_OUT}/deploy/linux/rigforge-open"

ARCHIVE="${ROOT}/artifacts/rigforge-linux-x64-${VERSION}.tar.gz"
rm -f "${ARCHIVE}"
tar -C "${ROOT}/artifacts" -czf "${ARCHIVE}" rigforge-linux-x64

(
  cd "${ROOT}/artifacts"
  sha256sum "rigforge-linux-x64-${VERSION}.tar.gz" > SHA256SUMS.txt
)

echo "Release ${VERSION} archive: ${ARCHIVE}"
