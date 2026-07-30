#!/usr/bin/env bash
# ═════════════════════════════════════════════════════════════════════════════
#  Steam depot upload — the only thing in this repo that runs steamcmd.
#
#  §13 chose manual `steamcmd` for release ("초기엔 수동 steamcmd"), which is the
#  right call: zero infrastructure, zero monthly cost. The cost of manual is that
#  a single mistyped argument is unrecoverable, so the guard rails live here
#  rather than in a person's memory.
#
#  Usage
#    upload.sh --dry-run [--fixture] [--branch NAME]
#        Assembles the depot content, renders and validates every VDF, checks
#        .gitignore, and prints the exact steamcmd command it would run.
#        Never touches the network and never needs a credential.
#
#    upload.sh --upload [--branch NAME] [--desc TEXT]
#        The real thing. Requires STEAM_BUILD_ACCOUNT in the environment.
#
#    upload.sh --preview [--branch NAME]
#        steamcmd's own preview: logs in, computes the build, uploads nothing.
#        Useful once, to confirm Steam agrees with our depot layout.
#
#  Credentials come from the environment and nowhere else:
#      STEAM_BUILD_ACCOUNT      required for --upload / --preview
#      STEAM_BUILD_PASSWORD     optional; only for an unattended run
#      STEAM_BUILD_GUARD_CODE   optional; a fresh Steam Guard code
#  Nothing in this repo stores any of them. See docs/STEAM-RELEASE.md,
#  "Credentials and Steam Guard" — the first login on a machine MUST be
#  interactive.
# ═════════════════════════════════════════════════════════════════════════════

set -euo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
readonly CONFIG_PATH="${SCRIPT_DIR}/steam.config"
readonly STEAMPIPE="${SCRIPT_DIR}/lib/steampipe.py"
readonly GITIGNORE_CHECK="${SCRIPT_DIR}/check_gitignore.sh"

readonly SPACEWAR_APP_ID=480

# Branch names an upload may target while APP_ID is still Spacewar's. Hard-coded
# on purpose: putting this list in steam.config would let a future edit add
# "default" to it, and the whole point of the guard is that it cannot be argued
# with. A test branch is one whose name says, in its name, that it is a test.
readonly TEST_BRANCH_PATTERN='^(test|tests|testing|internal|internal-test|dev|devtest|ci|staging-test)(-[A-Za-z0-9._-]+)?$'

PYTHON_BIN="${PYTHON_BIN:-python3}"

MODE=""
BRANCH=""
DESC=""
USE_FIXTURE=0
LIST_LIMIT=25

# ── Output helpers ──────────────────────────────────────────────────────────

if [[ -t 1 ]]; then
    readonly C_RESET=$'\033[0m'
    readonly C_BOLD=$'\033[1m'
    readonly C_RED=$'\033[31m'
    readonly C_YELLOW=$'\033[33m'
    readonly C_GREEN=$'\033[32m'
else
    readonly C_RESET="" C_BOLD="" C_RED="" C_YELLOW="" C_GREEN=""
fi

section()
{
    printf '\n%s── %s%s\n' "${C_BOLD}" "$1" "${C_RESET}"
}

note()
{
    if [[ $# -eq 0 || -z "$*" ]]; then
        printf '\n'
    else
        printf '  %s\n' "$*"
    fi
}

warn()
{
    printf '  %sWARN%s   %s\n' "${C_YELLOW}" "${C_RESET}" "$*" >&2
}

die()
{
    printf '\n%sREFUSING%s %s\n\n' "${C_RED}${C_BOLD}" "${C_RESET}" "$1" >&2
    shift || true
    for line in "$@"; do
        if [[ -z "${line}" ]]; then
            printf '\n' >&2
        else
            printf '  %s\n' "${line}" >&2
        fi
    done
    printf '\n' >&2
    exit 1
}

# Prints this file's header block, so the usage text and the documentation of the
# guard rails cannot drift apart.
usage()
{
    awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' \
        "${BASH_SOURCE[0]}"
}

# macOS ships bash 3.2 and that is what /usr/bin/env bash finds here, so this
# script stays inside 3.2: no ${var,,}, no ${var@Q}, no associative arrays.
lower()
{
    printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

# ── Arguments ───────────────────────────────────────────────────────────────

set_mode()
{
    if [[ -n "${MODE}" ]]; then
        die "--${1} conflicts with --${MODE}." "Pick exactly one mode."
    fi
    MODE="$1"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dry-run)  set_mode "dry-run" ;;
        --upload)   set_mode "upload" ;;
        --preview)  set_mode "preview" ;;
        --fixture)  USE_FIXTURE=1 ;;
        --branch)
            [[ $# -ge 2 ]] || die "--branch needs a value."
            BRANCH="$2"
            shift
            ;;
        --branch=*) BRANCH="${1#--branch=}" ;;
        --desc)
            [[ $# -ge 2 ]] || die "--desc needs a value."
            DESC="$2"
            shift
            ;;
        --desc=*)   DESC="${1#--desc=}" ;;
        --list-limit)
            [[ $# -ge 2 ]] || die "--list-limit needs a value."
            LIST_LIMIT="$2"
            shift
            ;;
        -h|--help)  usage; exit 0 ;;
        *)          die "unknown argument: $1" "Run with --help." ;;
    esac
    shift
done

# No default mode. A script whose default is "upload" eventually uploads because
# someone hit return; a script whose default is "dry run" quietly does nothing
# when you meant to ship. Requiring the word removes both.
if [[ -z "${MODE}" ]]; then
    usage
    die "no mode given." "Pass exactly one of --dry-run, --upload, --preview."
fi

if [[ -n "${BRANCH}" && ! "${BRANCH}" =~ ^[A-Za-z0-9._-]+$ ]]; then
    die "branch name '${BRANCH}' contains characters Steam does not allow." \
        "Steam branch names are letters, digits, dot, underscore and hyphen."
fi

# ── Config ──────────────────────────────────────────────────────────────────

command -v "${PYTHON_BIN}" >/dev/null 2>&1 \
    || die "${PYTHON_BIN} is not on PATH." \
           "This tool needs Python 3.9+, which macOS already ships. Set PYTHON_BIN to override."

steampipe()
{
    "${PYTHON_BIN}" "${STEAMPIPE}" --repo-root "${REPO_ROOT}" --config "${CONFIG_PATH}" "$@"
}

section "Configuration"
if ! CONFIG_ENV="$(steampipe env)"; then
    die "tools/steam/steam.config did not validate." \
        "Fix the errors above. Every App ID and depot ID in the release path comes" \
        "from that one file, so nothing runs until it parses."
fi
eval "${CONFIG_ENV}"

APP_ID_LABEL="${HG_APP_ID}"
if [[ "${HG_APP_ID_IS_SPACEWAR}" == "1" ]]; then
    APP_ID_LABEL="${HG_APP_ID}   (Spacewar - Valve's test app, not ours)"
fi
MODE_LABEL="${MODE}"
if [[ "${USE_FIXTURE}" == "1" ]]; then
    MODE_LABEL="${MODE} (synthetic fixture content)"
fi

note "config      ${CONFIG_PATH#"${REPO_ROOT}/"}"
note "App ID      ${APP_ID_LABEL}"
note "depots      ${HG_DEPOT_WINDOWS} windows / ${HG_DEPOT_MACOS} macos"
note "mode        ${MODE_LABEL}"
note "branch      ${BRANCH:-(none - build uploads unassigned)}"

# ── Guard rails ─────────────────────────────────────────────────────────────
#
# Ordered cheapest-first so a mistake is caught before anything is written.

section "Guard rails"

is_test_branch()
{
    [[ -n "$1" && "$1" =~ ${TEST_BRANCH_PATTERN} ]]
}

# 1. 'default' is never a script target. SteamPipe itself refuses to set the
#    default branch live from a build script, and that is a design we agree with:
#    promotion is the one step that should require looking at the build you are
#    about to make public. docs/STEAM-RELEASE.md, "Promoting a build to default".
if [[ "$(lower "${BRANCH}")" == "default" ]]; then
    die "--branch default is not allowed." \
        "SteamPipe cannot promote the default branch from a build script, and this" \
        "script will not pretend otherwise. Upload to a branch, verify it, then" \
        "promote on the Builds page at partner.steamgames.com." \
        "See docs/STEAM-RELEASE.md -> Promoting a build to default."
fi
note "ok          branch is not 'default'"

# 2. THE guard. App ID 480 is Spacewar — Valve's shared test app, which §13 uses
#    so lobbies, P2P and voice can be developed before the real app exists. It is
#    not ours. An upload aimed at an app you do not own is not something you can
#    take back, and the same mistake with a real-but-wrong App ID is worse. So
#    while the placeholder is in place, only an explicitly-named test branch may
#    be targeted. There is deliberately no override flag.
if [[ "${HG_APP_ID_IS_SPACEWAR}" == "1" && "${MODE}" != "dry-run" ]]; then
    if ! is_test_branch "${BRANCH}"; then
        die "App ID is still ${SPACEWAR_APP_ID} (Spacewar) and the target branch is '${BRANCH:-<none>}'." \
            "" \
            "480 is Valve's public test app. It is not this game's app, and a build" \
            "sent to the wrong app cannot be recalled by you." \
            "" \
            "Either:" \
            "  * put the real App ID in tools/steam/steam.config (one line), or" \
            "  * target a branch whose name says it is a test, e.g." \
            "        ${0##*/} --${MODE} --branch internal-test" \
            "" \
            "Allowed while the App ID is 480:" \
            "  test* tests* testing* internal* internal-test* dev* devtest* ci* staging-test*" \
            "" \
            "There is no flag that turns this check off."
    fi
    warn "App ID is ${SPACEWAR_APP_ID} and the branch is a test branch, so this is allowed."
    warn "Steam will still reject it: nobody has upload rights to Spacewar. That is fine -"
    warn "the point is that the refusal never depended on Steam's permission check."
fi
note "ok          App ID / branch combination permitted"

# 3. Fixture content is synthetic. It exists so the pipeline can be tested with
#    no Unity installed; it must never leave the machine.
if [[ "${USE_FIXTURE}" == "1" && "${MODE}" != "dry-run" ]]; then
    die "--fixture only works with --dry-run." \
        "Fixture content is text files pretending to be a game build. Uploading it" \
        "would publish a broken install to whoever is on that branch."
fi

# 4. A real upload needs an account name, and it must come from the environment.
if [[ "${MODE}" != "dry-run" ]]; then
    if [[ -z "${STEAM_BUILD_ACCOUNT:-}" ]]; then
        die "STEAM_BUILD_ACCOUNT is not set." \
            "The Steamworks build account name is read from the environment and is" \
            "never stored in this repo:" \
            "" \
            "    export STEAM_BUILD_ACCOUNT=\"<your-steamworks-build-account>\"" \
            "" \
            "The password is not required here if this machine has already completed" \
            "one interactive login - steamcmd caches the session. If it has not, run" \
            "the interactive login first:" \
            "" \
            "    steamcmd +login \"\$STEAM_BUILD_ACCOUNT\" +quit" \
            "" \
            "See docs/STEAM-RELEASE.md -> Credentials and Steam Guard."
    fi
    note "ok          STEAM_BUILD_ACCOUNT is set (value not printed)"
fi

# 5. Nothing that could be a credential may be committable. Runs in every mode,
#    including dry runs, because the cheapest time to notice is before anyone has
#    a reason to hurry.
GITIGNORE_SUMMARY="$(bash "${GITIGNORE_CHECK}")" || die \
    "the credential .gitignore check failed." \
    "Run tools/steam/check_gitignore.sh -v and fix it before uploading anything." \
    "A committed config.vdf or ssfn file hands over the build account's already-" \
    "Steam-Guard-approved session, and the only revocation is rotating the account."
note "ok         ${GITIGNORE_SUMMARY# }"

# ── Content ─────────────────────────────────────────────────────────────────

section "Depot content"
STAGE_SOURCE="build"
if [[ "${USE_FIXTURE}" == "1" ]]; then
    STAGE_SOURCE="fixture"
fi
if ! steampipe stage --source "${STAGE_SOURCE}" --list-limit "${LIST_LIMIT}"; then
    die "depot staging failed." \
        "The staged tree is what gets uploaded, so a structural problem here is a" \
        "problem players would hit."
fi

# ── Build scripts ───────────────────────────────────────────────────────────

section "Build scripts"
RENDER_ARGS=(render --branch "${BRANCH}")
if [[ -n "${DESC}" ]]; then
    RENDER_ARGS+=(--desc "${DESC}")
fi
if [[ "${MODE}" == "preview" ]]; then
    RENDER_ARGS+=(--preview)
fi
steampipe "${RENDER_ARGS[@]}" || die "could not render the VDFs."

section "Validation"
VALIDATE_ARGS=(validate --branch "${BRANCH}")
if [[ "${MODE}" != "dry-run" ]]; then
    # An empty depot is a legitimate dry-run state (Unity has not built yet) and
    # never a legitimate upload: SteamPipe would happily publish a build whose
    # depot contains nothing, and the next player to update gets an empty folder.
    VALIDATE_ARGS+=(--require-content)
fi
steampipe "${VALIDATE_ARGS[@]}" || die "the rendered build scripts did not validate."

# ── The steamcmd command ────────────────────────────────────────────────────
#
# Built as an array so no value is ever re-split by the shell, and printed in a
# redacted form so a terminal recording of a release cannot leak a password.

STEAM_ARGS=("+login" "${STEAM_BUILD_ACCOUNT:-<STEAM_BUILD_ACCOUNT>}")
REDACTED_ARGS=("+login" "\$STEAM_BUILD_ACCOUNT")

if [[ -n "${STEAM_BUILD_PASSWORD:-}" ]]; then
    STEAM_ARGS+=("${STEAM_BUILD_PASSWORD}")
    REDACTED_ARGS+=("<redacted>")
    if [[ -n "${STEAM_BUILD_GUARD_CODE:-}" ]]; then
        STEAM_ARGS+=("${STEAM_BUILD_GUARD_CODE}")
        REDACTED_ARGS+=("<redacted>")
    fi
fi

STEAM_ARGS+=("+run_app_build" "${HG_APP_BUILD_VDF}" "+quit")
REDACTED_ARGS+=("+run_app_build" "${HG_APP_BUILD_VDF#"${REPO_ROOT}/"}" "+quit")

section "steamcmd"
note "${HG_STEAMCMD} ${REDACTED_ARGS[*]}"

if [[ "${MODE}" == "dry-run" ]]; then
    # Reported rather than fatal. The whole point of a dry run is that it works on
    # a machine that cannot upload, so a missing steamcmd is information, not a
    # failure — but it is information you want now rather than on release day.
    if command -v "${HG_STEAMCMD}" >/dev/null 2>&1; then
        note "found       $(command -v "${HG_STEAMCMD}")"
    elif [[ -x "${HG_STEAMCMD}" ]]; then
        note "found       ${HG_STEAMCMD}"
    else
        note "not found   install before uploading: brew install --cask steamcmd"
    fi

    section "Dry run complete"
    note "Nothing contacted Steam. Nothing logged in. Nothing uploaded."
    note ""
    note "Rendered scripts   ${HG_VDF_DIR#"${REPO_ROOT}/"}/"
    note "Staged content     ${HG_STAGE_ROOT#"${REPO_ROOT}/"}/{windows,macos}/"
    note "File manifests     ${HG_MANIFEST_DIR#"${REPO_ROOT}/"}/"
    if [[ "${HG_APP_ID_IS_SPACEWAR}" == "1" ]]; then
        note ""
        note "App ID is still ${SPACEWAR_APP_ID}. Real uploads are blocked until"
        note "tools/steam/steam.config names the real app."
    fi
    printf '\n%sDRY RUN OK%s\n\n' "${C_GREEN}${C_BOLD}" "${C_RESET}"
    exit 0
fi

# ── Upload ──────────────────────────────────────────────────────────────────

command -v "${HG_STEAMCMD}" >/dev/null 2>&1 || [[ -x "${HG_STEAMCMD}" ]] \
    || die "steamcmd not found: ${HG_STEAMCMD}" \
           "Install it (macOS: brew install --cask steamcmd) or point STEAMCMD in" \
           "tools/steam/steam.config at the binary."

mkdir -p "${HG_LOG_DIR}"
LOG_FILE="${HG_LOG_DIR}/$(date -u '+%Y%m%dT%H%M%SZ')-${MODE}.log"

section "Running steamcmd"
note "log         ${LOG_FILE#"${REPO_ROOT}/"}"
if [[ -z "${STEAM_BUILD_PASSWORD:-}" ]]; then
    note "no password in the environment: steamcmd will use its cached session, or"
    note "prompt on this terminal if there is none. A Steam Guard prompt here is"
    note "expected on a machine's first login and cannot be scripted around."
fi
printf '\n'

set +e
"${HG_STEAMCMD}" "${STEAM_ARGS[@]}" 2>&1 | tee "${LOG_FILE}"
STEAM_STATUS="${PIPESTATUS[0]}"
set -e

section "Result"
# steamcmd's exit status is not trustworthy — it has historically returned 0 after
# a failed app build — so the log is the authority. "Successfully finished" is the
# line SteamPipe prints once the build is committed.
if grep -q "Successfully finished" "${LOG_FILE}"; then
    note "${C_GREEN}build committed${C_RESET}"
    grep -E "Successfully finished|BuildID" "${LOG_FILE}" | while IFS= read -r line; do
        note "${line}"
    done
    if [[ -n "${BRANCH}" ]]; then
        note ""
        note "Set live on branch '${BRANCH}'. It is NOT on default."
        note "Promote from the Builds page when it has been played through:"
        note "  https://partner.steamgames.com/apps/builds/${HG_APP_ID}"
    else
        note ""
        note "The build is uploaded but unassigned. Assign it from the Builds page:"
        note "  https://partner.steamgames.com/apps/builds/${HG_APP_ID}"
    fi
    exit 0
fi

printf '  %sFAILED%s steamcmd exit status %s, and the log has no "Successfully finished".\n' \
    "${C_RED}${C_BOLD}" "${C_RESET}" "${STEAM_STATUS}" >&2
note "log: ${LOG_FILE}"
grep -iE "error|failed|denied|rate limit" "${LOG_FILE}" | tail -n 20 >&2 || true
exit 1
