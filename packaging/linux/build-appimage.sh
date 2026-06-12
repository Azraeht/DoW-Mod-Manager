#!/usr/bin/env bash
#
# Packages the self-contained Linux publish output into an AppImage.
#
# Usage: packaging/linux/build-appimage.sh <publish-dir>
#   <publish-dir>  directory produced by `dotnet publish ... -r linux-x64 --self-contained`
#                  (must contain the `DowModManager` executable). Defaults to
#                  artifacts/linux-x64.
#
# Produces: artifacts/DoW_Mod_Manager-x86_64.AppImage
#
# Requires network access on first run to fetch appimagetool; runs it with
# --appimage-extract-and-run so no FUSE is needed (CI-friendly).
set -euo pipefail

PUBLISH_DIR="${1:-artifacts/linux-x64}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
OUT_DIR="${REPO_ROOT}/artifacts"
APPDIR="${OUT_DIR}/AppDir"
BIN_NAME="DowModManager"

if [ ! -x "${PUBLISH_DIR}/${BIN_NAME}" ]; then
    echo "error: ${PUBLISH_DIR}/${BIN_NAME} not found. Publish first:" >&2
    echo "  dotnet publish \"DoW Mod Manager/DowModManager.csproj\" -f net8.0 -r linux-x64 --self-contained -p:PublishSingleFile=true -o ${PUBLISH_DIR}" >&2
    exit 1
fi

echo ">> Assembling AppDir at ${APPDIR}"
rm -rf "${APPDIR}"
mkdir -p "${APPDIR}/usr/bin" "${APPDIR}/usr/share/applications" "${APPDIR}/usr/share/icons/hicolor/256x256/apps"

cp -a "${PUBLISH_DIR}/." "${APPDIR}/usr/bin/"

install -m 0755 "${SCRIPT_DIR}/AppRun" "${APPDIR}/AppRun"
cp "${SCRIPT_DIR}/DowModManager.desktop" "${APPDIR}/DowModManager.desktop"
cp "${SCRIPT_DIR}/DowModManager.desktop" "${APPDIR}/usr/share/applications/"

# Icon: prefer a PNG if ImageMagick is available, otherwise ship the .ico.
ICON_SRC="${REPO_ROOT}/DoW Mod Manager/Assets/app-icon.ico"
if command -v convert >/dev/null 2>&1; then
    convert "${ICON_SRC}[0]" -resize 256x256 "${APPDIR}/dowmodmanager.png" 2>/dev/null \
        && cp "${APPDIR}/dowmodmanager.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/dowmodmanager.png" \
        || cp "${ICON_SRC}" "${APPDIR}/dowmodmanager.png"
else
    cp "${ICON_SRC}" "${APPDIR}/dowmodmanager.png"
fi

echo ">> Fetching appimagetool"
TOOL="${OUT_DIR}/appimagetool-x86_64.AppImage"
if [ ! -f "${TOOL}" ]; then
    curl -fsSL -o "${TOOL}" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "${TOOL}"
fi

echo ">> Building AppImage"
ARCH=x86_64 "${TOOL}" --appimage-extract-and-run "${APPDIR}" "${OUT_DIR}/DoW_Mod_Manager-x86_64.AppImage"

echo ">> Done: ${OUT_DIR}/DoW_Mod_Manager-x86_64.AppImage"
