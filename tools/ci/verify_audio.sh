#!/usr/bin/env bash
# Run the cross-family audio audit and gate it against the known-defect baseline.
#
#     tools/ci/verify_audio.sh [--json PATH]
#
# The same script runs on a runner and on a laptop, so a contributor can reproduce a
# red build with one command instead of reverse-engineering a workflow file. It
# builds the venv at tools/audio/.venv — the path docs/ASSETS.md §4.1 already tells
# people to use — so running it locally leaves behind exactly the interpreter the
# generators expect, rather than a second, CI-only environment that drifts.
#
# What this protects: §12 requires the five floor materials to stay mutually
# distinguishable, because §04's 청음사 reads the monster's position from the floor it
# is walking on. That requirement is a property of the *set* of clips, so no single
# generator can check it — retune one surface and the check that breaks lives in
# another file. It is also invisible in play until someone reports that the role
# "doesn't work", which is the hardest kind of bug to attribute to a commit.
#
# The audit exits non-zero on any BLOCKING defect and one is currently expected
# (F-002). tools/ci/check_audio_baseline.py holds the reasoning for why that is
# baselined rather than ignored or left red.
set -Eeuo pipefail

CI_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "${CI_DIR}/../.." && pwd)"
VENV="${REPO}/tools/audio/.venv"
PYTHON="${PYTHON:-python3}"

AUDIT_JSON=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --json)
            AUDIT_JSON="$2"
            shift 2
            ;;
        *)
            echo "unknown argument: $1" >&2
            exit 2
            ;;
    esac
done
if [[ -z "${AUDIT_JSON}" ]]; then
    AUDIT_JSON="$(mktemp -d)/audio-audit.json"
fi

echo "── environment ─────────────────────────────────────────────────────────────"
if [[ ! -x "${VENV}/bin/python" ]]; then
    echo "creating ${VENV}"
    "${PYTHON}" -m venv "${VENV}"
fi
"${VENV}/bin/python" -m pip install --quiet --upgrade pip
"${VENV}/bin/python" -m pip install --quiet -r "${CI_DIR}/requirements-audio.txt"
"${VENV}/bin/python" - <<'PY'
import sys

import numpy
import scipy

print(f"  python {sys.version.split()[0]}   numpy {numpy.__version__}   "
      f"scipy {scipy.__version__}")
PY

echo
echo "── audit ───────────────────────────────────────────────────────────────────"
# The audit's own exit code is captured rather than propagated: a blocking defect is
# an expected state here, and the baseline gate below is what decides the build.
set +e
"${VENV}/bin/python" "${REPO}/tools/audio/verify_audio.py" --quiet --json "${AUDIT_JSON}"
AUDIT_STATUS=$?
set -e

# 0 = clean, 1 = blocking defects present. Anything else means the audit itself
# crashed (a missing folder, a corrupt wav, a scipy API change) and the JSON is not
# trustworthy, so there is nothing to compare against the baseline.
if [[ ${AUDIT_STATUS} -gt 1 ]]; then
    echo "verify_audio.py exited ${AUDIT_STATUS} — the audit crashed rather than" >&2
    echo "reporting defects. Not gating on a truncated result." >&2
    exit "${AUDIT_STATUS}"
fi

echo
"${VENV}/bin/python" "${CI_DIR}/check_audio_baseline.py" \
    --audit "${AUDIT_JSON}" \
    --baseline "${CI_DIR}/audio_baseline.json"
