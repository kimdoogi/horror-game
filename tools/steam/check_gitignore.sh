#!/usr/bin/env bash
# ═════════════════════════════════════════════════════════════════════════════
#  Verifies that no Steam credential file can be committed.
#
#  Run standalone, or automatically by upload.sh in every mode. Exit 0 = safe.
#
#  What is being protected: steamcmd's `config.vdf` holds a login token that has
#  already passed Steam Guard, and an `ssfn*` sentry file is what makes a machine
#  "remembered" by Steam Guard. Either one, committed, lets anyone with repo
#  access upload a build to the app. There is no way to revoke just that; you
#  rotate the build account.
#
#  This checks behaviour, not text: `git check-ignore --no-index` asks git the
#  same question git will ask at `git add` time, which is the only answer that
#  matters. Grepping .gitignore for patterns would pass while a later
#  un-ignore rule ("!config.vdf") silently undid them.
# ═════════════════════════════════════════════════════════════════════════════

set -euo pipefail

readonly SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
readonly STEAM_DIR_REL="tools/steam"

if [[ -t 1 ]]; then
    readonly C_RESET=$'\033[0m'
    readonly C_RED=$'\033[31m'
    readonly C_YELLOW=$'\033[33m'
    readonly C_GREEN=$'\033[32m'
else
    readonly C_RESET="" C_RED="" C_YELLOW="" C_GREEN=""
fi

VERBOSE=0
if [[ "${1:-}" == "-v" || "${1:-}" == "--verbose" ]]; then
    VERBOSE=1
fi

FAILURES=0
GAPS=0

fail()
{
    printf '  %sFAIL%s   %s\n' "${C_RED}" "${C_RESET}" "$*" >&2
    FAILURES=$((FAILURES + 1))
}

# A gap is a pattern this tool cannot close itself, because closing it means
# editing the root .gitignore, which belongs to the whole project. Counted and
# summarised; detail only on -v, so it does not drown out the dry run that calls
# this on every invocation.
gap()
{
    if [[ "${VERBOSE}" == "1" ]]; then
        printf '  %sGAP%s    %s\n' "${C_YELLOW}" "${C_RESET}" "$*" >&2
    fi
    GAPS=$((GAPS + 1))
}

pass()
{
    if [[ "${VERBOSE}" == "1" ]]; then
        printf '  %sok%s     %s\n' "${C_GREEN}" "${C_RESET}" "$*"
    fi
}

heading()
{
    if [[ "${VERBOSE}" == "1" ]]; then
        printf '\n  %s\n' "$*"
    fi
}

cd "${REPO_ROOT}"

if ! git rev-parse --git-dir >/dev/null 2>&1; then
    printf '  %sFAIL%s   %s is not a git work tree; cannot check.\n' \
        "${C_RED}" "${C_RESET}" "${REPO_ROOT}" >&2
    exit 1
fi

# --no-index makes this a pure pattern question, so it answers correctly for
# paths that do not exist yet — which is the case we care about, since the whole
# point is to be protected before steamcmd creates the file.
is_ignored()
{
    git check-ignore --no-index --quiet -- "$1"
}

# ── 1. Inside tools/steam — where steamcmd actually runs ─────────────────────
#
# These must be ignored or the check fails hard. tools/steam/.gitignore covers
# them and that file is in this area's ownership, so a failure here is a
# regression someone introduced, not a pre-existing gap.

heading "credential files under ${STEAM_DIR_REL}/"

STEAM_LOCAL_PATHS=(
    "${STEAM_DIR_REL}/config.vdf"
    "${STEAM_DIR_REL}/loginusers.vdf"
    "${STEAM_DIR_REL}/ssfn1234567890123456789"
    "${STEAM_DIR_REL}/sentry"
    "${STEAM_DIR_REL}/sentry.bin"
    "${STEAM_DIR_REL}/builder.sentryfile"
    "${STEAM_DIR_REL}/builder.ssfn"
    "${STEAM_DIR_REL}/steamcmd/config/config.vdf"
    "${STEAM_DIR_REL}/steamcmd/ssfn987654321"
    "${STEAM_DIR_REL}/sdk/tools/ContentBuilder/builder_osx/config.vdf"
    "${STEAM_DIR_REL}/output/vdf/app_build.vdf"
    "${STEAM_DIR_REL}/output/logs/20260101T000000Z-upload.log"
    "${STEAM_DIR_REL}/app_build.vdf.local"
)

for candidate in "${STEAM_LOCAL_PATHS[@]}"; do
    if is_ignored "${candidate}"; then
        pass "${candidate}"
    else
        fail "${candidate} is NOT ignored - add a rule to ${STEAM_DIR_REL}/.gitignore"
    fi
done

# ── 2. Repo-wide ─────────────────────────────────────────────────────────────
#
# ssfn* and config.vdf are covered repo-wide by the root .gitignore. Sentry files
# under other names are only covered inside tools/steam, because the root
# .gitignore belongs to the whole project rather than to this tool. A gap here is
# reported with the exact lines to add rather than failing the build: steamcmd
# never writes outside its own directory, so the realistic exposure is already
# closed, and this script does not get to edit shared files.

heading "credential files elsewhere in the repo"

REPO_WIDE_PATHS=(
    "config.vdf"
    "ssfn1234567890123456789"
    "unity/HorrorGame/config.vdf"
    "tools/ci/ssfn55555555"
    "sentry"
    "tools/ci/builder.sentryfile"
    "unity/HorrorGame/loginusers.vdf"
)

for candidate in "${REPO_WIDE_PATHS[@]}"; do
    if is_ignored "${candidate}"; then
        pass "${candidate}"
    else
        gap "${candidate} is not ignored outside ${STEAM_DIR_REL}/"
    fi
done

if [[ "${GAPS}" -gt 0 && "${VERBOSE}" == "1" ]]; then
    cat >&2 <<'ADVICE'

  The gaps above are outside this tool's directory, so they are reported rather
  than failed. steamcmd only ever writes inside tools/steam/, which is covered.
  To close them anyway, add to the "── Steam ──" section of the root .gitignore:

      sentry
      sentry.*
      *.sentryfile
      *.ssfn
      loginusers.vdf

ADVICE
fi

# ── 3. Nothing forbidden is already tracked ──────────────────────────────────
#
# .gitignore does not apply to a file git already tracks, so patterns alone prove
# nothing about history. This is the check that would catch the mistake having
# already happened.

heading "files already tracked by git"

TRACKED_OFFENDERS="$(git ls-files \
    | grep -Ei '(^|/)(ssfn[^/]*|config\.vdf|loginusers\.vdf|sentry|sentry\..*|[^/]*\.sentryfile|[^/]*\.ssfn)$' \
    || true)"

if [[ -n "${TRACKED_OFFENDERS}" ]]; then
    while IFS= read -r tracked; do
        fail "TRACKED: ${tracked} - remove it from history, then rotate the build account"
    done <<< "${TRACKED_OFFENDERS}"
else
    pass "no tracked file matches a Steam credential name"
fi

# ── 4. No tracked file in this tool holds a literal secret ───────────────────
#
# The rule for this repo is that a credential never lands in a file. The way that
# rule gets broken is someone pasting a password into a script "just to test",
# so look for an assignment whose value is a literal rather than a placeholder or
# a variable reference.

heading "literal secrets in ${STEAM_DIR_REL}/"

# Listed first and guarded: with an empty list, `xargs grep` would run grep with
# no file arguments, and grep with no files reads stdin and hangs forever.
TRACKED_STEAM_FILES="$(git ls-files -- "${STEAM_DIR_REL}")"
SECRET_HITS=""
if [[ -n "${TRACKED_STEAM_FILES}" ]]; then
    SECRET_HITS="$(printf '%s\n' "${TRACKED_STEAM_FILES}" \
        | tr '\n' '\0' \
        | xargs -0 grep -nEi '(password|passwd|secret|guard_?code)[[:space:]]*=[[:space:]]*"?[A-Za-z0-9][A-Za-z0-9!@#%^&*_.-]{5,}' 2>/dev/null \
        | grep -v '\$' \
        || true)"
fi

if [[ -n "${SECRET_HITS}" ]]; then
    while IFS= read -r hit; do
        fail "possible literal credential: ${hit}"
    done <<< "${SECRET_HITS}"
else
    pass "no literal credential assignment in tracked files"
fi

# ── Result ──────────────────────────────────────────────────────────────────

if [[ "${FAILURES}" -gt 0 ]]; then
    printf '\n  %s%d credential-safety failure(s).%s\n\n' \
        "${C_RED}" "${FAILURES}" "${C_RESET}" >&2
    exit 1
fi

SUMMARY="ssfn / config.vdf / sentry files cannot be committed under ${STEAM_DIR_REL}/"
if [[ "${GAPS}" -gt 0 ]]; then
    SUMMARY="${SUMMARY}; ${GAPS} advisory gap(s) elsewhere (-v for detail)"
fi
printf '  %s%s%s\n' "${C_GREEN}" "${SUMMARY}" "${C_RESET}"
exit 0
