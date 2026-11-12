#!/usr/bin/env bash
# Prove the desktop AppImage actually starts: launch it against a virtual X server and
# require it to still be running when the timer expires.
#
#   ./scripts/verify-appimage.sh out/LightSync-x86_64.AppImage [seconds]
#
# Needs xvfb-run (Debian: xvfb and xauth). A GUI that cannot open a window exits before
# the timeout, which is the failure this catches; PipWire and GStreamer are only needed
# once sync starts, so their absence does not fail the check.
set -euo pipefail

image="${1:?usage: verify-appimage.sh <appimage> [seconds]}"
seconds="${2:-20}"

if [ ! -f "${image}" ]; then
    echo "error: ${image} does not exist" >&2
    exit 1
fi

if ! command -v xvfb-run >/dev/null 2>&1; then
    echo "error: xvfb-run not found; install xvfb and xauth" >&2
    exit 1
fi

log=$(mktemp)
trap 'rm -f "${log}"' EXIT

chmod +x "${image}"

set +e
APPIMAGE_EXTRACT_AND_RUN=1 timeout --signal=TERM "${seconds}" xvfb-run -a "${image}" \
    >"${log}" 2>&1
status=$?
set -e

# 124 is the timeout telling us the application was still alive, which is the pass case.
if [ "${status}" -ne 124 ]; then
    echo "error: exited with ${status} instead of staying up for ${seconds}s" >&2
    cat "${log}" >&2
    exit 1
fi

if grep -qi 'unhandled exception' "${log}"; then
    echo "error: unhandled exception while running" >&2
    cat "${log}" >&2
    exit 1
fi

echo "ok: stayed running for ${seconds}s" >&2
