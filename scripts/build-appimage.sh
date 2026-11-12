#!/usr/bin/env bash
# Wrap a published LightSync.Desktop directory in an AppImage.
#
#   ./scripts/build-appimage.sh 1.4.1-preview.5 out/desktop out/LightSync-x86_64.AppImage
#
# The desktop is the only frontend that needs this. The CLI is a single native AOT
# binary, so it ships as a plain tarball instead of a FUSE-mounted squashfs.
#
# Set APPIMAGETOOL to point at appimagetool when it is not on PATH.
set -euo pipefail

if [ "$#" -ne 3 ]; then
    echo "usage: $0 <version> <publish-dir> <output-appimage>" >&2
    exit 2
fi

version="$1"
publish_dir="$2"
output="$3"

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
assets="${repo_root}/packaging/appimage"
tool="${APPIMAGETOOL:-appimagetool}"

if ! [[ "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "error: '${version}' is not a semver version from scripts/compute-version.sh" >&2
    exit 1
fi

if [ ! -x "${publish_dir}/light-sync-desktop" ]; then
    echo "error: ${publish_dir}/light-sync-desktop missing; publish the desktop first" >&2
    exit 1
fi

# APPIMAGETOOL may be an absolute path to a downloaded AppImage rather than a name on PATH.
if [ -x "${tool}" ]; then
    tool=$(readlink -f "${tool}")
elif command -v "${tool}" >/dev/null 2>&1; then
    tool=$(command -v "${tool}")
else
    echo "error: appimagetool not found; install it or set APPIMAGETOOL" >&2
    exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "${work}"' EXIT

appdir="${work}/LightSync.AppDir"
install -d \
    "${appdir}/usr/bin" \
    "${appdir}/usr/lib/light-sync-desktop" \
    "${appdir}/usr/share/applications" \
    "${appdir}/usr/share/icons/hicolor/256x256/apps" \
    "${appdir}/usr/share/doc/light-sync"

# Runtime payload. Symbols stay out of the image: they roughly triple its size and no
# user opens a crash dump from an AppImage.
(cd "${publish_dir}" && tar -cf - --exclude='*.pdb' --exclude='*.dbg' --exclude='*.xml' .) \
    | tar -xf - -C "${appdir}/usr/lib/light-sync-desktop"

install -m 0755 "${assets}/AppRun" "${appdir}/AppRun"
install -m 0644 "${assets}/light-sync.png" "${appdir}/light-sync.png"
install -m 0644 "${assets}/light-sync.png" \
    "${appdir}/usr/share/icons/hicolor/256x256/apps/light-sync.png"

sed "s/@VERSION@/${version}/" "${assets}/light-sync.desktop" >"${appdir}/light-sync.desktop"
install -m 0644 "${appdir}/light-sync.desktop" \
    "${appdir}/usr/share/applications/light-sync.desktop"

install -m 0644 "${repo_root}/LICENSE" "${appdir}/usr/share/doc/light-sync/LICENSE"
install -m 0644 "${repo_root}/README.md" "${appdir}/usr/share/doc/light-sync/README.md"

mkdir -p "$(dirname "${output}")"
# APPIMAGE_EXTRACT_AND_RUN keeps the build working on machines without FUSE; the tool is
# itself an AppImage. --no-appstream skips metadata validation, which needs an AppStream
# file this project does not publish.
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "${tool}" --no-appstream "${appdir}" "${output}"

echo "built ${output}" >&2
