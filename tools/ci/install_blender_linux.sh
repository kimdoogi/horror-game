#!/usr/bin/env bash
# Install a pinned Blender on a Linux runner and print the path to its binary.
#
#     BLENDER="$(tools/ci/install_blender_linux.sh)"
#
# Progress goes to stderr; the last line of stdout is the binary path, so the caller
# can capture it. Idempotent — a second call with a warm cache does nothing.
#
# Version-pinned rather than "latest" because this job's question is "do the
# generators still run?", and an unpinned Blender answers a different question
# ("did Blender change?") in the same red X. When Blender is upgraded that should be
# a commit here, with the generator output re-reviewed, not a silent Tuesday.
#
# The digest below is not decoration: an interrupted 384 MB download over a CDN
# produces a truncated tarball that extracts far enough to give a confusing failure
# deep inside a generator. It is upstream's own published value — see the URL in
# EXPECTED_SHA256's comment for how to refresh it when the version moves.
set -Eeuo pipefail

#: Matches the Blender the assets were last generated with (docs/ASSETS.md).
BLENDER_VERSION="${BLENDER_VERSION:-5.2.0}"
BLENDER_SERIES="${BLENDER_VERSION%.*}"

#: From https://download.blender.org/release/Blender5.2/blender-5.2.0.sha256
#: (upstream publishes one manifest per release covering every platform's file).
#: Bumping BLENDER_VERSION without bumping this is a hard failure, on purpose.
EXPECTED_SHA256="${BLENDER_SHA256:-96f6c181a30f4950607839dc84d42a354b250d8a0231b098b59b7bc69c351c48}"

TARBALL="blender-${BLENDER_VERSION}-linux-x64.tar.xz"
URL="https://download.blender.org/release/Blender${BLENDER_SERIES}/${TARBALL}"

INSTALL_ROOT="${1:-${BLENDER_INSTALL_DIR:-${HOME}/.cache/blender}}"
INSTALL_DIR="${INSTALL_ROOT}/blender-${BLENDER_VERSION}-linux-x64"
BINARY="${INSTALL_DIR}/blender"

log() { echo "[install-blender] $*" >&2; }

# Blender needs these even in --background: the binary links against the X and GL
# client libraries unconditionally, and a headless runner image does not ship them.
# The failure without them is `error while loading shared libraries`, before any
# Python runs, which looks nothing like a missing dependency to whoever reads the log.
if [[ -z "${SKIP_SYSTEM_LIBS:-}" ]] && command -v apt-get >/dev/null 2>&1; then
    log "installing X/GL client libraries Blender links against"
    sudo apt-get update -qq
    sudo apt-get install -y -qq --no-install-recommends \
        libx11-6 libxi6 libxxf86vm1 libxfixes3 libxrender1 libxkbcommon0 \
        libsm6 libice6 libgl1 libglx0 libegl1 libgomp1 xz-utils >/dev/null
fi

if [[ -x "${BINARY}" ]]; then
    log "already installed: ${BINARY}"
    echo "${BINARY}"
    exit 0
fi

mkdir -p "${INSTALL_ROOT}"
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

log "downloading ${URL}"
curl --fail --location --silent --show-error --retry 3 --retry-delay 5 \
    --output "${WORK}/${TARBALL}" "${URL}"

log "verifying sha256"
echo "${EXPECTED_SHA256}  ${WORK}/${TARBALL}" | sha256sum --check --status || {
    log "FAILED — expected ${EXPECTED_SHA256}"
    log "         got      $(sha256sum "${WORK}/${TARBALL}" | cut -d' ' -f1)"
    log "         upstream manifest: https://download.blender.org/release/Blender${BLENDER_SERIES}/blender-${BLENDER_VERSION}.sha256"
    exit 1
}

log "extracting into ${INSTALL_ROOT}"
tar -xJf "${WORK}/${TARBALL}" -C "${INSTALL_ROOT}"

if [[ ! -x "${BINARY}" ]]; then
    log "extracted, but ${BINARY} is missing — upstream changed the archive layout"
    ls -la "${INSTALL_ROOT}" >&2
    exit 1
fi

log "$("${BINARY}" --version | head -1)"
echo "${BINARY}"
