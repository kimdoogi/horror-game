#!/usr/bin/env bash
# Re-run every Blender generator headlessly and decide, correctly, whether it worked.
#
#     tools/ci/run_blender_generators.sh            # all four
#     tools/ci/run_blender_generators.sh gen_props  # one, while iterating
#     BLENDER=/path/to/blender tools/ci/run_blender_generators.sh
#
# What this protects: the .fbx and .glb files under Assets/Models are committed
# (docs/ASSETS.md — the project must open playable without a Blender install), so a
# generator can rot for weeks without anyone noticing. The committed asset keeps
# working while the only way to *change* it quietly stops existing, and that is
# discovered on the day someone needs to adjust §12's corridor width.
#
# WHY THE EXIT CODE IS NOT ENOUGH
# ═══════════════════════════════
# Blender's --background exits 0 after an uncaught Python exception. Measured on
# Blender 5.2.0 with a script whose only statement is `raise RuntimeError`: the
# traceback is printed and the process exits 0. So `set -e` alone would report a
# generator that died on line one as a success, and CI would be worse than nothing —
# a green tick over a broken toolchain.
#
# blendkit.fail() exists for this: it prints `ASSET_FAILED <reason>` to stderr and
# calls sys.exit(1), and that exit code *does* propagate (measured: 1). Every
# generator routes its failures through it. But the marker is what to trust, because
# the paths that skip blendkit.fail() are exactly the ones worth catching:
#
#   * an exception while *importing* the generator, before the module-level
#     try/except that funnels into blendkit.fail() is even installed;
#   * a failure inside a Blender operator that Blender reports and swallows;
#   * a future generator that forgets to wrap main().
#
# So three independent checks per generator, any of which fails the job:
#   1. `ASSET_FAILED` present in the output   — the generator said so itself
#   2. a Python traceback present            — it died without saying so
#   3. no `ASSET_REPORT` line present        — it exited early and wrote nothing
# The exit code is checked too, as the fourth and weakest signal.
#
# All three are load-bearing, measured on Blender 5.2.0:
#   * a module-level `raise` prints `Traceback (most recent call last)`, exit 0 → (2)
#   * a SyntaxError prints the offending line and `SyntaxError:` with *no* traceback
#     header at all, exit 0 → only (3) catches it
#   * blendkit.fail() prints ASSET_FAILED, exit 1 → (1) and (4)
#
# Written without bash arrays in the reporting path on purpose: macOS still ships
# bash 3.2, where expanding an empty array under `set -u` aborts the script, which
# would turn "no failures" into a crash.
set -Eeuo pipefail

CI_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "${CI_DIR}/../.." && pwd)"
GEN_DIR="${REPO}/tools/blender"
LOG_DIR="${BLENDER_LOG_DIR:-${REPO}/artifacts/blender}"

# The order docs/ASSETS.md §4.2 lists them in: the map kit first, because its
# measured corridor width is what the props and the dressing are then checked
# against.
#
# gen_dressing was added to this list after the fact — it shipped without being in
# it, which is the exact rot this script exists to prevent: 1074 pieces of the map's
# set dressing had no generator check at all. gen_mapkit_detail is deliberately
# absent; it is a library gen_mapkit imports, not a generator, and has no main().
#
# gen_monster_ai replaced gen_monster_model here when the sculpt was adopted: it is
# what writes Monster.fbx now. gen_monster_model is deliberately absent for the same
# reason gen_mapkit_detail is — it has become a library (the seven §06 clip authors and
# the procedural skin pipeline, both imported by gen_monster_ai) and refuses to write a
# shipping asset unless asked with `-- --hull`. Listing it would have both generators
# writing the same four files, with the winner decided by list order.
# gen_ghost writes §09's 유령 and is listed AFTER gen_player_model, because it imports
# gen_player_ai for the hands: a ghost is a dead player and building it a second, worse
# hand would be shipping the defect that pass removed, twice.
DEFAULT_GENERATORS="gen_mapkit gen_dressing gen_props gen_player_model gen_monster_ai gen_ghost"
GENERATORS="${*:-${DEFAULT_GENERATORS}}"

resolve_blender() {
    if [[ -n "${BLENDER:-}" ]]; then
        echo "${BLENDER}"
        return
    fi
    # The macOS path docs/ASSETS.md §4.2 documents, then whatever is on PATH (the
    # Linux runner, or a dev who installed it from a package manager).
    local from_path
    from_path="$(command -v blender 2>/dev/null || true)"
    local candidate
    for candidate in "/Applications/Blender.app/Contents/MacOS/Blender" "${from_path}"; do
        if [[ -n "${candidate}" && -x "${candidate}" ]]; then
            echo "${candidate}"
            return
        fi
    done
    echo "no Blender found. Set BLENDER=/path/to/blender, or on Linux run" >&2
    echo "tools/ci/install_blender_linux.sh and pass its output as BLENDER." >&2
    exit 127
}

BLENDER_BIN="$(resolve_blender)"
mkdir -p "${LOG_DIR}"

echo "── blender ─────────────────────────────────────────────────────────────────"
echo "  binary: ${BLENDER_BIN}"
"${BLENDER_BIN}" --version | head -2 | sed 's/^/  /'
echo "  logs:   ${LOG_DIR}"
echo

FAIL_COUNT=0
RUN_COUNT=0
SUMMARY=""

for generator in ${GENERATORS}; do
    RUN_COUNT=$((RUN_COUNT + 1))
    script="${GEN_DIR}/${generator}.py"
    log="${LOG_DIR}/${generator}.log"
    echo "── ${generator} ────────────────────────────────────────────────────────"

    if [[ ! -f "${script}" ]]; then
        echo "  FAILED — no such generator at ${script}"
        SUMMARY="${SUMMARY}FAIL ${generator}  no such generator"$'\n'
        FAIL_COUNT=$((FAIL_COUNT + 1))
        continue
    fi

    # --factory-startup so a user's own preferences, add-ons and unit settings cannot
    # change the geometry: 1 Blender unit must be 1 metre or §12's numbers mean
    # nothing. stderr is merged in because ASSET_FAILED is printed there.
    set +e
    "${BLENDER_BIN}" --background --factory-startup --python "${script}" >"${log}" 2>&1
    status=$?
    set -e

    failed_marker="$(grep -c 'ASSET_FAILED' "${log}" || true)"
    traceback="$(grep -c 'Traceback (most recent call last)' "${log}" || true)"
    reports="$(grep -c '^ASSET_REPORT ' "${log}" || true)"

    problems=""
    [[ "${failed_marker}" -gt 0 ]] && problems="${problems}ASSET_FAILED emitted; "
    [[ "${traceback}" -gt 0 ]] && problems="${problems}Python traceback in output; "
    [[ "${reports}" -eq 0 ]] && problems="${problems}no ASSET_REPORT line, wrote nothing; "
    [[ "${status}" -ne 0 ]] && problems="${problems}exit ${status}; "
    problems="${problems%; }"

    if [[ -z "${problems}" ]]; then
        echo "  OK — ${reports} asset(s) written, exit ${status}"
        SUMMARY="${SUMMARY}OK   ${generator}  ${reports} asset(s)"$'\n'
    else
        echo "  FAILED — ${problems}"
        echo "  ── last 40 lines of ${log} ──"
        tail -40 "${log}" | sed 's/^/  | /'
        SUMMARY="${SUMMARY}FAIL ${generator}  ${problems}"$'\n'
        FAIL_COUNT=$((FAIL_COUNT + 1))
    fi
    grep '^ASSET_REPORT ' "${log}" | sed 's/^ASSET_REPORT /  · /' || true
    echo
done

echo "── result ──────────────────────────────────────────────────────────────────"
printf '%s' "${SUMMARY}" | sed 's/^/  /'

# Reported, never gated: the mesh data is deterministic but the containers are not.
# Measured on Blender 5.2.0 — regenerating Crate.fbx produced a file of identical
# length differing in 53 bytes, all of them inside the FBX header's CreationTime
# (Hour / Minute / Second / Millisecond). Gating on `git diff` would therefore fail
# on every single run for a reason that has nothing to do with the game, while the
# real signal — a changed vertex count, triangle count or bounding box — is already
# in the ASSET_REPORT lines above, where a reviewer can diff it by eye.
if command -v git >/dev/null 2>&1 && git -C "${REPO}" rev-parse --git-dir >/dev/null 2>&1; then
    changed="$(git -C "${REPO}" status --porcelain -- unity/HorrorGame/Assets/Models \
        | wc -l | tr -d ' ')"
    echo "  ${changed} file(s) under Assets/Models differ from the checkout — expected:"
    echo "  FBX and glTF embed a creation timestamp, so bytes move even when the"
    echo "  geometry does not. Informational, not a gate."
fi

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    {
        echo "### Blender generators"
        echo
        echo '```'
        printf '%s' "${SUMMARY}"
        echo '```'
    } >>"${GITHUB_STEP_SUMMARY}"
fi

if [[ "${FAIL_COUNT}" -gt 0 ]]; then
    echo
    echo "  ${FAIL_COUNT} of ${RUN_COUNT} generator(s) failed"
    exit 1
fi

echo "  all ${RUN_COUNT} generator(s) ran clean"
