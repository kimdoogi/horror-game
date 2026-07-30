#!/usr/bin/env bash
#
# tools/ci/build.sh — batch-mode wrapper around the Unity build pipeline.
#
# The editor code lives in Assets/Scripts/Editor/BuildPipeline*.cs; this script only
# finds Unity, hands it the right arguments, streams the log, and passes the exit code
# through. Every decision that affects the produced player is made in C#, so a build
# started here and a build started from the Horror menu are the same build.
#
#   ./tools/ci/build.sh windows release        # the build that ships (§13: Steam is Windows)
#   ./tools/ci/build.sh mac dev
#   ./tools/ci/build.sh all release --require-il2cpp
#   ./tools/ci/build.sh test                   # EditMode + PlayMode, non-zero on failure
#   ./tools/ci/build.sh bootstrap              # install the required Unity packages
#   ./tools/ci/build.sh doctor                 # what can this machine build?
#
# The Unity binary is DISCOVERED, never hard-coded:
#   1. $UNITY_PATH, if it points at an executable
#   2. the Unity Hub install matching ProjectSettings/ProjectVersion.txt
#      (override the Hub root with $UNITY_HUB_EDITORS)
#   3. any Hub install, with a loud warning that the version differs
#   4. unity / unity-editor on $PATH
#
# On a fresh CI runner Unity also needs a license activated before -batchmode works
# (ULF file, or -serial/-username/-password). That is deliberately not automated here:
# credentials do not belong in a repository.
#
# Exit codes come from the editor and are documented in BuildPipelineRunner and
# BuildPipelineTestRunner. 124 means this script's watchdog killed a hung editor.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT_PATH="$REPO_ROOT/unity/HorrorGame"
PROJECT_VERSION_FILE="$PROJECT_PATH/ProjectSettings/ProjectVersion.txt"
LOG_DIR="$REPO_ROOT/dist/logs"

METHOD_BUILD="HorrorGame.EditorTools.BuildPipelineRunner.BuildFromCommandLine"
METHOD_TESTS="HorrorGame.EditorTools.BuildPipelineTestRunner.RunFromCommandLine"
METHOD_BOOTSTRAP="HorrorGame.EditorTools.PackageBootstrap.InstallRequiredBatch"
METHOD_DOCTOR="HorrorGame.EditorTools.BuildPipelineRunner.ReportEnvironment"

# A cold IL2CPP release build compiles the whole game to C++ and links it. Twenty
# minutes is generous for that and still short enough that a hung editor does not hold
# a CI runner for an hour.
TIMEOUT_SECONDS="${HORROR_BUILD_TIMEOUT:-1200}"

usage() {
    sed -n '3,30p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    cat <<'EOF'

commands
  windows|win64 [dev|release]   build dist/windows-x64
  mac [dev|release]             build dist/macos-universal
  mac-arm64 | mac-x64           single-architecture macOS players
  all [dev|release]             Windows first, then macOS universal
  test [suites]                 EditMode + PlayMode; suites e.g. editmode
  bootstrap                     install the Unity packages the project needs
  doctor                        report what this machine can build, build nothing

options
  --config dev|release          same as the positional argument
  --version 0.1.0               override the repository VERSION file
  --output-root <path>          default: <repo>/dist
  --app-id <id>                 Steam App ID to stamp (default: §13's 480)
  --require-il2cpp              fail instead of falling back to Mono
  --no-clean                    keep the previous contents of the output folder
  --require-tests               'test' fails if no test ran
  --timeout <seconds>           watchdog, default 1200
EOF
}

die() {
    echo "[build.sh] error: $*" >&2
    exit 2
}

project_unity_version() {
    if [[ ! -f "$PROJECT_VERSION_FILE" ]]; then
        die "no $PROJECT_VERSION_FILE — is $PROJECT_PATH really the Unity project?"
    fi
    awk '/^m_EditorVersion:/ { print $2; exit }' "$PROJECT_VERSION_FILE"
}

hub_editor_root() {
    if [[ -n "${UNITY_HUB_EDITORS:-}" ]]; then
        echo "$UNITY_HUB_EDITORS"
        return
    fi
    case "$(uname -s)" in
        Darwin) echo "/Applications/Unity/Hub/Editor" ;;
        Linux)  echo "$HOME/Unity/Hub/Editor" ;;
        MINGW*|MSYS*|CYGWIN*) echo "/c/Program Files/Unity/Hub/Editor" ;;
        *)      echo "" ;;
    esac
}

# Path of the editor executable inside a Hub version directory, per host.
hub_executable() {
    local version_dir="$1"
    case "$(uname -s)" in
        Darwin) echo "$version_dir/Unity.app/Contents/MacOS/Unity" ;;
        Linux)  echo "$version_dir/Editor/Unity" ;;
        MINGW*|MSYS*|CYGWIN*) echo "$version_dir/Editor/Unity.exe" ;;
        *)      echo "$version_dir/Unity" ;;
    esac
}

find_unity() {
    if [[ -n "${UNITY_PATH:-}" ]]; then
        [[ -x "$UNITY_PATH" ]] || die "UNITY_PATH=$UNITY_PATH is not executable."
        echo "$UNITY_PATH"
        return
    fi

    local wanted root candidate
    wanted="$(project_unity_version)"
    root="$(hub_editor_root)"

    if [[ -n "$root" && -d "$root/$wanted" ]]; then
        candidate="$(hub_executable "$root/$wanted")"
        if [[ -x "$candidate" ]]; then
            echo "$candidate"
            return
        fi
    fi

    # Any other Hub install. Building with a different editor version reimports the whole
    # project and can rewrite asset metadata, so this is a warning, not a silent choice.
    if [[ -n "$root" && -d "$root" ]]; then
        local other
        while IFS= read -r other; do
            [[ -n "$other" ]] || continue
            candidate="$(hub_executable "$other")"
            if [[ -x "$candidate" ]]; then
                echo "[build.sh] WARNING: Unity $wanted is not installed; using $(basename "$other")." >&2
                echo "[build.sh]          A different editor version reimports assets and may rewrite" >&2
                echo "[build.sh]          .meta files. Install $wanted through Unity Hub, or set UNITY_PATH." >&2
                echo "$candidate"
                return
            fi
        done < <(find "$root" -mindepth 1 -maxdepth 1 -type d | sort -r)
    fi

    for name in unity unity-editor Unity; do
        if command -v "$name" >/dev/null 2>&1; then
            echo "[build.sh] WARNING: using '$name' from PATH; version unverified." >&2
            command -v "$name"
            return
        fi
    done

    die "Unity not found.
  wanted version : $wanted (from ProjectSettings/ProjectVersion.txt)
  hub root       : ${root:-<unknown host>}
  Set UNITY_PATH to the editor executable, or UNITY_HUB_EDITORS to the Hub install root.
  macOS example  : export UNITY_PATH=\"/Applications/Unity/Hub/Editor/$wanted/Unity.app/Contents/MacOS/Unity\""
}

explain_status() {
    case "$1" in
        0)   echo "success" ;;
        1)   echo "unexpected editor error — read the log" ;;
        2)   echo "bad arguments, or Unity could not start (license? another editor holding the project lock?)" ;;
        3)   echo "no scenes, or a scene in Build Settings is missing from disk" ;;
        4)   echo "the player build failed" ;;
        5)   echo "--require-il2cpp was passed and this host cannot produce IL2CPP" ;;
        6)   echo "tests failed" ;;
        7)   echo "scripts do not compile" ;;
        8)   echo "the target's build support module is not installed in this editor" ;;
        9)   echo "no tests ran and --require-tests was passed" ;;
        124) echo "the editor was killed by this script's ${TIMEOUT_SECONDS}s watchdog" ;;
        *)   echo "unrecognised exit code" ;;
    esac
}

run_unity() {
    local label="$1"
    shift

    local unity log status=0 tail_pid watchdog_pid unity_pid timeout_flag
    unity="$(find_unity)"
    mkdir -p "$LOG_DIR"
    log="$LOG_DIR/${label}-$(date -u +%Y%m%dT%H%M%SZ).log"
    timeout_flag="$log.timeout"
    : > "$log"

    echo "[build.sh] unity    : $unity"
    echo "[build.sh] project  : $PROJECT_PATH"
    echo "[build.sh] log      : $log"
    echo "[build.sh] arguments: $*"
    echo

    # Unity's own log is the only complete record, so it is written to a file and streamed
    # from there. Piping the editor's stdout instead would lose the exit code to the pipe.
    tail -f "$log" &
    tail_pid=$!
    # Off the job table: it is killed at the end of the run, and bash would otherwise print
    # "Terminated: 15  tail -f ..." after the build's own summary, which reads like a failure.
    disown "$tail_pid" 2>/dev/null || true

    "$unity" -projectPath "$PROJECT_PATH" -logFile "$log" "$@" &
    unity_pid=$!

    # The watchdog polls instead of sleeping for the whole timeout, and it closes the
    # inherited stdout first. Both matter: a single long `sleep` would outlive the build as
    # an orphan still holding this script's stdout, so `./build.sh … | tee build.log` would
    # sit there for twenty minutes after the build had already finished.
    (
        exec >/dev/null 2>&1
        deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
        while kill -0 "$unity_pid" 2>/dev/null; do
            if [[ "$(date +%s)" -ge "$deadline" ]]; then
                : > "$timeout_flag"
                kill -TERM "$unity_pid" 2>/dev/null || true
                sleep 30
                kill -KILL "$unity_pid" 2>/dev/null || true
                break
            fi
            sleep 2
        done
    ) &
    watchdog_pid=$!

    trap 'kill "$unity_pid" "$watchdog_pid" "$tail_pid" 2>/dev/null || true' INT TERM

    wait "$unity_pid" || status=$?

    kill "$watchdog_pid" 2>/dev/null || true
    wait "$watchdog_pid" 2>/dev/null || true
    kill "$tail_pid" 2>/dev/null || true
    trap - INT TERM

    if [[ -f "$timeout_flag" ]]; then
        rm -f "$timeout_flag"
        status=124
    fi

    echo
    echo "[build.sh] exit $status — $(explain_status "$status")"

    if [[ "$status" -ne 0 ]]; then
        echo "[build.sh] distilled log ($log):"
        grep -nE 'error CS|\[BuildPipeline\]|\[TestRunner\]|MONO-FALLBACK|BuildFailedException|Aborting batchmode' \
            "$log" | tail -n 60 || true
    fi

    return "$status"
}

command_name="${1:-help}"
[[ $# -gt 0 ]] && shift || true

config=""
platform=""
suites=""
extra=()

case "$command_name" in
    windows|win|win64) platform="win64" ;;
    mac|macos) platform="mac" ;;
    mac-arm64|arm64) platform="mac-arm64" ;;
    mac-x64|intel) platform="mac-x64" ;;
    all) platform="all" ;;
    test|tests) : ;;
    bootstrap) : ;;
    doctor) : ;;
    help|-h|--help) usage; exit 0 ;;
    *) usage; die "unknown command '$command_name'" ;;
esac

while [[ $# -gt 0 ]]; do
    case "$1" in
        dev|development|debug) config="development" ;;
        release|ship) config="release" ;;
        --config) config="${2:-}"; shift ;;
        --version) extra+=("-buildVersion" "${2:-}"); shift ;;
        --output-root) extra+=("-buildOutputRoot" "${2:-}"); shift ;;
        --app-id) extra+=("-buildSteamAppId" "${2:-}"); shift ;;
        --require-il2cpp) extra+=("-buildRequireIl2cpp") ;;
        --no-clean) extra+=("-buildNoClean") ;;
        --require-tests) extra+=("-testRequireTests") ;;
        --timeout) TIMEOUT_SECONDS="${2:-}"; shift ;;
        editmode|playmode|editmode,playmode|playmode,editmode|all-tests) suites="$1" ;;
        *) die "unknown option '$1'" ;;
    esac
    shift
done

case "$command_name" in
    test|tests)
        # PlayMode tests instantiate the real player loop, so -nographics is NOT passed:
        # it can change or disable rendering-dependent behaviour, and §12's monster pathing
        # and §05's movement are exactly the things PlayMode tests exist to check. Set
        # HORROR_NOGRAPHICS=1 on a headless runner with no display.
        args=(-batchmode -executeMethod "$METHOD_TESTS")
        [[ -n "${HORROR_NOGRAPHICS:-}" ]] && args=(-batchmode -nographics -executeMethod "$METHOD_TESTS")
        [[ -n "$suites" ]] && args+=("-testSuites" "$suites")
        run_unity "test" "${args[@]}" "${extra[@]+"${extra[@]}"}"
        ;;

    bootstrap)
        run_unity "bootstrap" -batchmode -nographics -executeMethod "$METHOD_BOOTSTRAP"
        ;;

    doctor)
        # ReportEnvironment only logs, so -quit provides the exit. Every other entry point
        # sets its own exit code and must never be given -quit.
        run_unity "doctor" -batchmode -nographics -quit -executeMethod "$METHOD_DOCTOR"
        ;;

    *)
        if [[ -z "$config" ]]; then
            die "say which configuration: '$command_name dev' or '$command_name release'.
  development keeps the debug symbols and the profiler; release strips them.
  There is no default, because guessing wrong here is how a debug build reaches a store page."
        fi

        run_unity "build-$platform-$config" \
            -batchmode -nographics \
            -executeMethod "$METHOD_BUILD" \
            -buildPlatform "$platform" \
            -buildConfig "$config" \
            "${extra[@]+"${extra[@]}"}"
        ;;
esac
