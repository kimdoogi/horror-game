#!/usr/bin/env python3
"""SteamPipe support tool: resolve config, render VDFs, stage depots, validate.

`tools/steam/upload.sh` is the driver — it owns the guard rails and the one call
to `steamcmd`. This file owns everything that needs more than a shell
conditional, and in particular it owns *reading `steam.config`*. There is exactly
one parser for that file so the shell's guard ("is the App ID still 480?") and
the VDF check ("does the rendered AppID match?") can never disagree about which
app we are talking about.

Two facts shape every check below, and both come from §13's decision to ship via
manual `steamcmd`:

1. **An upload is irreversible.** A build pushed to the wrong App ID cannot be
   recalled by the person who pushed it. So the App ID is resolved once, from
   one file, and cross-checked against the rendered build script.
2. **A depot ships exactly what you hand it.** Unity leaves
   `*_BurstDebugInformation_DoNotShip` folders, `.pdb` symbol files and
   `.DS_Store` in its output. Pointing `ContentRoot` at a Unity build directory
   ships all of it. Hence the stager: content is *copied* into a clean tree, and
   the copy is what gets uploaded.

Nothing here contacts the network, and nothing here reads or writes a
credential. Subcommands:

    env       resolve steam.config and print shell assignments for upload.sh
    render    write the three VDFs from templates/
    stage     assemble the depot content tree from the Unity build output
    validate  parse the rendered VDFs and check them against config + filesystem
    fixture   synthesise a fake Unity build so the pipeline is testable with no
              Unity installed (§13's tooling has to work before the engine does)

Python 3.9 compatible on purpose: that is what macOS ships, and the release
pipeline must not need a virtualenv to run.
"""

from __future__ import annotations

import argparse
import fnmatch
import os
import re
import shlex
import shutil
import stat
import string
import subprocess
import sys
import time
from typing import Dict, List, Optional, Sequence, Tuple

# ── Constants ───────────────────────────────────────────────────────────────

SPACEWAR_APP_ID = 480
"""§13's 개발용 App ID. Valve's public test app — lobbies, P2P and voice work
against it before the real app exists. It is also not ours, which is why
upload.sh treats it as a tripwire rather than a default."""

CONFIG_KEYS = (
    "APP_ID",
    "DEPOT_WINDOWS",
    "DEPOT_MACOS",
    "WINDOWS_BUILD_DIR",
    "MACOS_BUILD_DIR",
    "WINDOWS_EXE_NAME",
    "MACOS_APP_NAME",
    "OUTPUT_ROOT",
    "STEAMCMD",
)
"""Every key steam.config must define. An unknown key is an error rather than a
warning: a typo'd key would otherwise silently fall back to a default, and one
of those defaults is an App ID."""

MAX_DESC_LENGTH = 200
"""Steam truncates long build descriptions in the Builds table. Anything past
this is invisible where it matters, so it is rejected rather than silently cut."""

EXCLUSIONS: Tuple[Tuple[str, str], ...] = (
    ("*_BurstDebugInformation_DoNotShip",
     "Unity Burst symbol data. Unity literally names it DoNotShip."),
    ("*_BackUpThisFolder_ButDontShipItWithYourGame",
     "IL2CPP symbol backup. Needed to symbolise a crash dump, never by a player."),
    ("*.pdb",
     "Debug symbols. Not used at runtime and they embed absolute build-machine paths."),
    ("*.mdb",
     "Old Mono debug symbols. Same reasoning as .pdb."),
    ("*.dSYM",
     "macOS debug symbols. Same reasoning as .pdb, and they are directories, so they "
     "quietly multiply depot size."),
    (".DS_Store",
     "Finder metadata. Created just by looking at the folder; ships as a broken file."),
    ("._*",
     "AppleDouble resource forks. Created when a macOS build is touched from a "
     "non-HFS filesystem; confuses Gatekeeper's bundle validation."),
    ("__MACOSX",
     "Archive artefact from unzipping a build on macOS."),
    ("*.log",
     "Build logs. Nothing a player installs should include one."),
    ("MONO-FALLBACK-DO-NOT-SHIP.txt",
     "The build pipeline's own marker that IL2CPP was unavailable and this player must not "
     "be published. Staging a folder that contains one would publish the warning instead of "
     "heeding it — and one has been sitting in dist/windows-x64 since the audit that named it."),
    ("steam_appid.txt",
     "Valve asks that a released depot not carry this: Steam tells a launched game its own "
     "App ID, and a file in the depot overrides it — a stale copy makes the game report "
     "itself as whatever it was built against. The build pipeline already refuses to write "
     "one into a release build and fails its shippable check if it finds one; this is the "
     "second lock, because the first one was open for a while and nothing noticed."),
    (".git*",
     "Version control metadata. Would leak branch names and remote URLs."),
)
"""What never enters a depot, and why. This is the primary defence; the
`FileExclusion` lines in the depot templates are the backstop. Matched against
each path component's name, so a directory match prunes the whole subtree."""

RUNTIME_APP_ID_SOURCE = os.path.join(
    "unity", "HorrorGame", "Assets", "Scripts", "Steam", "SteamAppConfig.cs")
"""Where the *shipped binary* learns its App ID.

Read only, never written — that file belongs to the Steam adapter layer. It has to
agree with `steam.config` all the same: `steam.config` decides which app the depot
is uploaded to, and `SteamAppConfig.AppId` decides which app the player's copy
initialises Steamworks against. Divergence means Steam installs the game from the
right depot and the game then talks to the wrong app — no lobbies, no voice, no
stats, and nothing in the error message points at the cause."""

FIXTURE_NOTICE = (
    "SYNTHETIC FIXTURE - tools/steam/lib/steampipe.py fixture.\n"
    "This is not a game build. It exists so the depot pipeline can be tested\n"
    "before Unity is installed. upload.sh refuses to upload fixture content.\n"
)


class SteamPipeError(Exception):
    """A condition that must stop the pipeline. Message is user-facing."""


# ── steam.config ────────────────────────────────────────────────────────────

_CONFIG_LINE = re.compile(r"^([A-Z][A-Z0-9_]*)=(.*)$")


def _strip_value(raw: str) -> str:
    """Takes the value side of a config line, honouring one level of quoting.

    A quoted value may be followed by a comment; an unquoted value may not
    contain '#' at all. Deliberately primitive — see the header of
    steam.config for why the format is kept this dumb.
    """
    text = raw.strip()
    if text[:1] in ('"', "'"):
        quote = text[0]
        end = text.find(quote, 1)
        if end < 0:
            raise SteamPipeError("unterminated quote in value: " + raw)
        return text[1:end]
    return text.split("#", 1)[0].strip()


def load_raw_config(path: str) -> Dict[str, str]:
    """Parses steam.config into a plain dict, rejecting anything surprising."""
    if not os.path.isfile(path):
        raise SteamPipeError("missing config file: " + path)

    values: Dict[str, str] = {}
    with open(path, "r", encoding="utf-8") as handle:
        for lineno, raw in enumerate(handle, start=1):
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            match = _CONFIG_LINE.match(line)
            if match is None:
                raise SteamPipeError(
                    "{0}:{1}: not KEY=\"value\": {2}".format(path, lineno, line))
            key = match.group(1)
            if key in values:
                raise SteamPipeError(
                    "{0}:{1}: {2} is defined twice".format(path, lineno, key))
            if key not in CONFIG_KEYS:
                raise SteamPipeError(
                    "{0}:{1}: unknown key {2} (expected one of: {3})".format(
                        path, lineno, key, ", ".join(CONFIG_KEYS)))
            values[key] = _strip_value(match.group(2))

    missing = [k for k in CONFIG_KEYS if k not in values]
    if missing:
        raise SteamPipeError(
            "{0}: missing required keys: {1}".format(path, ", ".join(missing)))
    return values


def _positive_int(text: str, what: str) -> int:
    if not re.fullmatch(r"[0-9]+", text or ""):
        raise SteamPipeError("{0} must be a positive integer, got {1!r}".format(what, text))
    value = int(text, 10)
    if value <= 0:
        raise SteamPipeError("{0} must be a positive integer, got {1!r}".format(what, text))
    return value


class Platform(object):
    """One shipped platform: its depot, its build directory, its depot script."""

    def __init__(self, key, label, depot_key, build_dir_key, template, vdf_name):
        # type: (str, str, str, str, str, str) -> None
        self.key = key
        self.label = label
        self.depot_key = depot_key
        self.build_dir_key = build_dir_key
        self.template = template
        self.vdf_name = vdf_name


PLATFORMS = (
    Platform("windows", "Windows", "DEPOT_WINDOWS", "WINDOWS_BUILD_DIR",
             "depot_windows.vdf.template", "depot_windows.vdf"),
    Platform("macos", "macOS", "DEPOT_MACOS", "MACOS_BUILD_DIR",
             "depot_macos.vdf.template", "depot_macos.vdf"),
)


class Config(object):
    """Resolved, validated release configuration.

    Constructing this is the only way any part of the pipeline learns the App ID
    or a depot ID.
    """

    def __init__(self, repo_root: str, config_path: str) -> None:
        raw = load_raw_config(config_path)

        self.repo_root = os.path.abspath(repo_root)
        self.config_path = os.path.abspath(config_path)
        self.app_id = _positive_int(raw["APP_ID"], "APP_ID")
        self.is_spacewar = self.app_id == SPACEWAR_APP_ID

        # "auto" tracks Steamworks' allocation order for a fresh app: content
        # depots are handed out from AppID+1 upwards. Keeping it derived means
        # swapping in the real App ID is genuinely a one-line edit (§13's
        # requirement), while still allowing explicit IDs when Valve's numbering
        # turns out different — which is why the doc tells you to check.
        self.depots: Dict[str, int] = {}
        for index, platform in enumerate(PLATFORMS, start=1):
            text = raw[platform.depot_key]
            if text == "auto":
                self.depots[platform.key] = self.app_id + index
            else:
                self.depots[platform.key] = _positive_int(text, platform.depot_key)

        ids = [self.depots[p.key] for p in PLATFORMS]
        if len(set(ids)) != len(ids):
            raise SteamPipeError(
                "depot IDs collide: " + ", ".join(str(i) for i in ids))
        if self.app_id in ids:
            raise SteamPipeError(
                "a depot ID equals the App ID ({0}); depots are separate IDs".format(
                    self.app_id))

        self.build_dirs: Dict[str, str] = {}
        for platform in PLATFORMS:
            rel = raw[platform.build_dir_key]
            if not rel or os.path.isabs(rel):
                raise SteamPipeError(
                    "{0} must be a non-empty repo-relative path, got {1!r}".format(
                        platform.build_dir_key, rel))
            self.build_dirs[platform.key] = os.path.join(self.repo_root, rel)

        self.windows_exe_name = raw["WINDOWS_EXE_NAME"]
        self.macos_app_name = raw["MACOS_APP_NAME"]
        if not self.windows_exe_name.endswith(".exe"):
            raise SteamPipeError("WINDOWS_EXE_NAME must end in .exe")
        if not self.macos_app_name.endswith(".app"):
            raise SteamPipeError("MACOS_APP_NAME must end in .app")

        output_rel = raw["OUTPUT_ROOT"]
        if not output_rel or os.path.isabs(output_rel):
            raise SteamPipeError("OUTPUT_ROOT must be a non-empty repo-relative path")
        self.output_root = os.path.join(self.repo_root, output_rel)
        self.output_rel = output_rel
        self.stage_root = os.path.join(self.output_root, "content")
        self.build_output = os.path.join(self.output_root, "build")
        self.vdf_dir = os.path.join(self.output_root, "vdf")
        self.log_dir = os.path.join(self.output_root, "logs")
        self.manifest_dir = os.path.join(self.output_root, "manifest")
        self.fixture_root = os.path.join(self.output_root, "fixture")

        self.steamcmd = raw["STEAMCMD"]
        if not self.steamcmd:
            raise SteamPipeError("STEAMCMD must not be empty")

        # Templates sit beside the config, so moving tools/steam/ wholesale keeps
        # working and nothing needs a path in two places.
        self.templates_dir = os.path.join(os.path.dirname(self.config_path), "templates")
        if not os.path.isdir(self.templates_dir):
            raise SteamPipeError("cannot find templates/ next to " + self.config_path)

    def stage_dir(self, platform_key: str) -> str:
        return os.path.join(self.stage_root, platform_key)

    def fixture_dir(self, platform_key: str) -> str:
        return os.path.join(self.fixture_root, platform_key)

    def app_build_vdf(self) -> str:
        return os.path.join(self.vdf_dir, "app_build.vdf")


# ── Small helpers ───────────────────────────────────────────────────────────

def human_size(byte_count: int) -> str:
    value = float(byte_count)
    for unit in ("B", "KiB", "MiB", "GiB", "TiB"):
        if value < 1024.0 or unit == "TiB":
            if unit == "B":
                return "{0:.0f} {1}".format(value, unit)
            return "{0:.1f} {1}".format(value, unit)
        value /= 1024.0
    return "{0:.1f} TiB".format(value)


def exclusion_for(name: str) -> Optional[Tuple[str, str]]:
    """Returns the matching exclusion rule for a path component, or None."""
    for pattern, reason in EXCLUSIONS:
        if fnmatch.fnmatch(name, pattern):
            return pattern, reason
    return None


def git_commit(repo_root: str) -> str:
    """Short commit for the build description. 'nogit' rather than failing —
    a release must not be blocked by the absence of a git checkout."""
    try:
        out = subprocess.run(
            ["git", "-C", repo_root, "rev-parse", "--short=12", "HEAD"],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=False)
    except OSError:
        return "nogit"
    text = out.stdout.decode("utf-8", "replace").strip()
    return text if out.returncode == 0 and text else "nogit"


def git_is_dirty(repo_root: str) -> bool:
    try:
        out = subprocess.run(
            ["git", "-C", repo_root, "status", "--porcelain"],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=False)
    except OSError:
        return False
    return out.returncode == 0 and bool(out.stdout.strip())


def ensure_dir(path: str) -> None:
    os.makedirs(path, exist_ok=True)


def strip_cs_comments(text: str) -> str:
    """Removes C# comments, leaving string literals intact.

    Needed because `SteamAppConfig.cs` documents the release edit by showing the
    line you are meant to write — inside a comment. A regex over the raw file
    finds that example and reports a completely fictitious App ID, so the comments
    have to go first. (`ARCHITECTURE.md` §1 notes the core's Unity-reference
    scanner does the same thing for the same reason.)
    """
    out = []
    index = 0
    length = len(text)
    while index < length:
        char = text[index]
        if char == '"':
            out.append(char)
            index += 1
            while index < length:
                out.append(text[index])
                if text[index] == "\\":
                    index += 2
                    continue
                if text[index] == '"':
                    index += 1
                    break
                index += 1
            continue
        if text.startswith("//", index):
            newline = text.find("\n", index)
            index = length if newline < 0 else newline
            continue
        if text.startswith("/*", index):
            end = text.find("*/", index + 2)
            index = length if end < 0 else end + 2
            continue
        out.append(char)
        index += 1
    return "".join(out)


def _cs_uint_const(body: str, name: str) -> Optional[str]:
    match = re.search(
        r"const\s+uint\s+" + re.escape(name) + r"\s*=\s*([A-Za-z0-9_]+)\s*;", body)
    return match.group(1) if match is not None else None


def runtime_app_id(repo_root: str) -> Tuple[Optional[int], Optional[str]]:
    """Reads the App ID the shipped player will initialise Steamworks with.

    Returns (app_id, problem). A problem rather than an exception, because the file
    is another area's and may legitimately be restructured: an unreadable answer is
    worth a warning, a *wrong* answer is worth stopping for.
    """
    path = os.path.join(repo_root, RUNTIME_APP_ID_SOURCE)
    if not os.path.isfile(path):
        return None, "not found: " + RUNTIME_APP_ID_SOURCE
    with open(path, "r", encoding="utf-8") as handle:
        body = strip_cs_comments(handle.read())

    token = _cs_uint_const(body, "AppId")
    if token is None:
        return None, "no `const uint AppId = ...;` in " + RUNTIME_APP_ID_SOURCE

    seen = set()
    while token is not None and not re.fullmatch(r"[0-9]+[uU]?", token):
        if token in seen:
            return None, "circular const chain at " + token
        seen.add(token)
        token = _cs_uint_const(body, token)
    if token is None:
        return None, "AppId resolves to something this tool cannot read"
    return int(token.rstrip("uU"), 10), None


# ── VDF (Valve KeyValues, KV1) parsing ──────────────────────────────────────
#
# Written out rather than pulled from a package: the whole point of this tool is
# to check the file steamcmd will read, and depending on a third-party parser to
# do it would add a dependency to the release path for about eighty lines of
# code. KV1 is a small grammar.

class VdfNode(object):
    """A KeyValues object. Keys may repeat (FileExclusion does), so pairs are
    kept in order and lookups say explicitly whether they expect one or many."""

    def __init__(self) -> None:
        self.pairs: List[Tuple[str, object]] = []

    def keys(self) -> List[str]:
        return [k for k, _ in self.pairs]

    def all(self, key: str) -> List[object]:
        lowered = key.lower()
        return [v for k, v in self.pairs if k.lower() == lowered]

    def one(self, key: str) -> object:
        found = self.all(key)
        if len(found) != 1:
            raise SteamPipeError(
                "expected exactly one {0!r}, found {1}".format(key, len(found)))
        return found[0]

    def string(self, key: str) -> str:
        value = self.one(key)
        if not isinstance(value, str):
            raise SteamPipeError("{0!r} must be a value, not a block".format(key))
        return value

    def block(self, key: str) -> "VdfNode":
        value = self.one(key)
        if not isinstance(value, VdfNode):
            raise SteamPipeError("{0!r} must be a block".format(key))
        return value


_TOKEN = re.compile(r'"((?:[^"\\]|\\.)*)"|([{}])|([^\s{}"]+)')


def _tokenize(text: str) -> List[str]:
    tokens: List[str] = []
    position = 0
    length = len(text)
    while position < length:
        char = text[position]
        if char in " \t\r\n":
            position += 1
            continue
        if text.startswith("//", position):
            newline = text.find("\n", position)
            position = length if newline < 0 else newline + 1
            continue
        match = _TOKEN.match(text, position)
        if match is None:
            raise SteamPipeError(
                "unparsable character {0!r} at offset {1}".format(char, position))
        if match.group(1) is not None:
            tokens.append(match.group(1).replace('\\"', '"').replace("\\\\", "\\"))
        elif match.group(2) is not None:
            tokens.append(match.group(2))
        else:
            tokens.append(match.group(3))
        position = match.end()
    return tokens


def parse_vdf(text: str) -> VdfNode:
    """Parses a KV1 document into a root node holding its top-level keys."""
    tokens = _tokenize(text)
    index = 0

    def parse_block(depth: int) -> Tuple[VdfNode, int]:
        nonlocal index
        node = VdfNode()
        while index < len(tokens):
            token = tokens[index]
            if token == "}":
                if depth == 0:
                    raise SteamPipeError("unmatched '}'")
                index += 1
                return node, index
            if token == "{":
                raise SteamPipeError("block with no key")
            key = token
            index += 1
            if index >= len(tokens):
                raise SteamPipeError("key {0!r} has no value".format(key))
            if tokens[index] == "{":
                index += 1
                child, _ = parse_block(depth + 1)
                node.pairs.append((key, child))
            else:
                node.pairs.append((key, tokens[index]))
                index += 1
        if depth != 0:
            raise SteamPipeError("unterminated block")
        return node, index

    root, _ = parse_block(0)
    return root


# ── render ──────────────────────────────────────────────────────────────────

def vdf_escape(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def build_description(config: Config, branch: str, given: Optional[str]) -> str:
    if given:
        description = given
    else:
        commit = git_commit(config.repo_root)
        if git_is_dirty(config.repo_root):
            commit += "-dirty"
        stamp = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        description = "{0} {1} {2}".format(branch or "unassigned", commit, stamp)
    if len(description) > MAX_DESC_LENGTH:
        raise SteamPipeError(
            "build description is {0} chars, limit is {1}: Steam truncates it in "
            "the Builds table where it is the only way to tell two uploads "
            "apart".format(len(description), MAX_DESC_LENGTH))
    return description


def render(config: Config, branch: str, description: str, preview: bool) -> List[str]:
    """Writes the three VDFs. Returns the paths written."""
    ensure_dir(config.vdf_dir)
    ensure_dir(config.build_output)

    mapping = {
        "APP_ID": str(config.app_id),
        "DEPOT_WINDOWS": str(config.depots["windows"]),
        "DEPOT_MACOS": str(config.depots["macos"]),
        "CONTENT_ROOT": vdf_escape(config.stage_root),
        "BUILD_OUTPUT": vdf_escape(config.build_output),
        "SET_LIVE": vdf_escape(branch),
        "PREVIEW": "1" if preview else "0",
        "DESC": vdf_escape(description),
        "OUTPUT_ROOT": config.output_rel,
    }

    written: List[str] = []
    sources = [("app_build.vdf.template", "app_build.vdf")]
    sources += [(p.template, p.vdf_name) for p in PLATFORMS]

    for template_name, output_name in sources:
        template_path = os.path.join(config.templates_dir, template_name)
        if not os.path.isfile(template_path):
            raise SteamPipeError("missing template: " + template_path)
        with open(template_path, "r", encoding="utf-8") as handle:
            template = string.Template(handle.read())
        try:
            # substitute (not safe_substitute): a typo'd placeholder must be a
            # hard error, because the alternative is a VDF containing the literal
            # text "${APP_ID}" and steamcmd happily treating it as an app name.
            body = template.substitute(mapping)
        except KeyError as error:
            raise SteamPipeError(
                "{0}: unknown placeholder ${1}".format(template_path, error))
        except ValueError as error:
            raise SteamPipeError("{0}: {1}".format(template_path, error))

        output_path = os.path.join(config.vdf_dir, output_name)
        with open(output_path, "w", encoding="utf-8") as handle:
            handle.write(body)
        os.chmod(output_path, 0o644)
        written.append(output_path)

    assert_no_credentials(written)
    return written


def assert_no_credentials(paths: Sequence[str]) -> None:
    """Refuses to leave a credential in a generated file.

    The credential rule for this repo is absolute — nothing in the working tree
    may contain one — and generated files are the easy way to break it by
    accident, because a template placeholder is only one edit away from being
    fed an environment variable. Checks for the literal values currently in the
    environment, and for a key that looks like it holds one.
    """
    secrets = []
    for name in ("STEAM_BUILD_PASSWORD", "STEAM_BUILD_GUARD_CODE"):
        value = os.environ.get(name, "")
        if len(value) >= 4:
            secrets.append((name, value))

    suspicious = re.compile(r'"(password|passwd|secret|guard_?code|ssfn)"',
                            re.IGNORECASE)
    for path in paths:
        with open(path, "r", encoding="utf-8") as handle:
            body = handle.read()
        for name, value in secrets:
            if value in body:
                raise SteamPipeError(
                    "{0} contains the value of ${1}. Generated files never hold "
                    "credentials; steamcmd receives them from the environment at "
                    "call time only.".format(path, name))
        match = suspicious.search(body)
        if match is not None:
            raise SteamPipeError(
                "{0} has a {1!r} key. A build script has no credential fields; "
                "remove it.".format(path, match.group(1)))


# ── stage ───────────────────────────────────────────────────────────────────

class StageResult(object):
    def __init__(self, platform_key: str, source: str, destination: str) -> None:
        self.platform_key = platform_key
        self.source = source
        self.destination = destination
        self.source_exists = False
        self.files: List[Tuple[str, int]] = []
        self.excluded: List[Tuple[str, str]] = []
        self.symlinks: List[str] = []
        self.total_bytes = 0


def stage_platform(config: Config, platform: Platform, source_root: str) -> StageResult:
    """Copies one platform's build into a clean depot tree, applying EXCLUSIONS.

    The destination is wiped first. A stale file from a previous build surviving
    into a depot is the exact class of mistake that makes a patch bigger than
    the change, and worse, it can resurrect a deleted asset.
    """
    destination = config.stage_dir(platform.key)
    result = StageResult(platform.key, source_root, destination)

    if os.path.isdir(destination):
        shutil.rmtree(destination)
    ensure_dir(destination)

    if not os.path.isdir(source_root):
        return result
    result.source_exists = True

    for current, dirnames, filenames in os.walk(source_root, followlinks=False):
        relative = os.path.relpath(current, source_root)
        relative = "" if relative == "." else relative

        keep_dirs = []
        for name in sorted(dirnames):
            rule = exclusion_for(name)
            if rule is not None:
                result.excluded.append((os.path.join(relative, name) + "/", rule[0]))
                continue
            keep_dirs.append(name)
        dirnames[:] = keep_dirs

        for name in keep_dirs:
            ensure_dir(os.path.join(destination, relative, name))

        for name in sorted(filenames):
            rule = exclusion_for(name)
            relative_path = os.path.join(relative, name)
            if rule is not None:
                result.excluded.append((relative_path, rule[0]))
                continue
            source_path = os.path.join(current, name)
            target_path = os.path.join(destination, relative_path)
            ensure_dir(os.path.dirname(target_path))
            if os.path.islink(source_path):
                # SteamPipe dereferences symlinks rather than recording them, so
                # a link inside an .app bundle becomes a duplicate file in the
                # depot. Copying the target keeps the install correct; the report
                # exists so an unexpected one gets noticed.
                result.symlinks.append(relative_path)
            shutil.copy2(source_path, target_path, follow_symlinks=True)
            size = os.path.getsize(target_path)
            result.files.append((relative_path, size))
            result.total_bytes += size

    return result


def write_manifest(config: Config, result: StageResult) -> str:
    ensure_dir(config.manifest_dir)
    path = os.path.join(config.manifest_dir, result.platform_key + ".txt")
    lines = [
        "# depot content manifest - {0}".format(result.platform_key),
        "# depot id   : {0}".format(config.depots[result.platform_key]),
        "# source     : {0}".format(result.source),
        "# staged to  : {0}".format(result.destination),
        "# files      : {0}".format(len(result.files)),
        "# bytes      : {0}".format(result.total_bytes),
        "",
    ]
    for relative_path, size in sorted(result.files):
        lines.append("{0:>12}  {1}".format(size, relative_path))
    if result.excluded:
        lines.append("")
        lines.append("# excluded")
        for relative_path, pattern in sorted(result.excluded):
            lines.append("{0:>12}  {1}   [{2}]".format("-", relative_path, pattern))
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    return path


def check_staged_layout(config: Config, result: StageResult) -> Tuple[List[str], List[str]]:
    """Structural checks on a staged tree. Returns (errors, warnings).

    These catch the failures that only show up for a player: a depot that
    installs and then will not start. Steam has no idea what your executable is
    called — the launch option on the partner site names it — so a mismatch here
    is invisible until someone clicks Play.
    """
    errors: List[str] = []
    warnings: List[str] = []
    if not result.files:
        return errors, warnings

    names = [f for f, _ in result.files]
    top_level = [n for n in names if os.sep not in n]

    if result.platform_key == "windows":
        executables = [n for n in top_level if n.lower().endswith(".exe")]
        if not executables:
            errors.append("no .exe at the root of the Windows depot")
        elif config.windows_exe_name not in executables:
            errors.append(
                "expected {0} at the depot root, found: {1}. Either Player "
                "Settings' Product Name changed or WINDOWS_EXE_NAME is stale; "
                "the partner site's launch option points at one exact name."
                .format(config.windows_exe_name, ", ".join(sorted(executables))))
        if not any(n.lower() == "unityplayer.dll" for n in top_level):
            warnings.append(
                "UnityPlayer.dll not at the depot root - normal only for a fully "
                "static build, suspicious otherwise")
        data_prefix = config.windows_exe_name[:-len(".exe")] + "_Data" + os.sep
        if not any(n.startswith(data_prefix) for n in names):
            warnings.append("no {0} directory in the depot".format(data_prefix.rstrip(os.sep)))

    if result.platform_key == "macos":
        bundle = config.macos_app_name
        if not any(n.startswith(bundle + os.sep) for n in names):
            errors.append(
                "no {0}/ bundle in the macOS depot (found: {1})".format(
                    bundle, ", ".join(sorted(set(n.split(os.sep)[0] for n in names))[:5])))
        else:
            binaries = [n for n in names
                        if n.startswith(os.path.join(bundle, "Contents", "MacOS") + os.sep)]
            if not binaries:
                errors.append("{0}/Contents/MacOS/ is empty".format(bundle))
            else:
                for relative_path in binaries:
                    mode = os.stat(os.path.join(result.destination, relative_path)).st_mode
                    if not mode & (stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH):
                        errors.append(
                            "{0} is not executable. SteamPipe records the mode it "
                            "sees, so the installed bundle will refuse to launch."
                            .format(relative_path))
            if not any(n == os.path.join(bundle, "Contents", "Info.plist") for n in names):
                errors.append("{0}/Contents/Info.plist missing".format(bundle))

    return errors, warnings


# ── validate ────────────────────────────────────────────────────────────────

def validate(config: Config, branch: str, require_content: bool) -> Tuple[List[str], List[str]]:
    """Parses the rendered VDFs and checks them against config and filesystem."""
    errors: List[str] = []
    warnings: List[str] = []

    # The depot's App ID and the App ID compiled into the player must be the same
    # number. Checked here rather than trusted, because the two live in different
    # areas of the repo and the failure is silent: the game installs correctly and
    # then has no lobbies, no voice and no stats.
    embedded, problem = runtime_app_id(config.repo_root)
    if problem is not None:
        warnings.append(
            "could not read the App ID compiled into the player ({0}); the depot "
            "App ID {1} is therefore unverified against the runtime".format(
                problem, config.app_id))
    elif embedded != config.app_id:
        errors.append(
            "App ID mismatch: steam.config uploads to {0}, but {1} compiles {2} "
            "into the player. The install would come from the right depot and then "
            "initialise Steamworks against the wrong app.".format(
                config.app_id, RUNTIME_APP_ID_SOURCE, embedded))

    app_path = config.app_build_vdf()
    if not os.path.isfile(app_path):
        return ["app_build.vdf has not been rendered: " + app_path], warnings

    # Re-run the credential scan here and not only at render time. Validation is
    # the last gate before steamcmd, and a rendered VDF can be hand-edited
    # between the two — which is exactly how a "just for one test" password ends
    # up in a file.
    if os.path.isdir(config.vdf_dir):
        present = sorted(os.path.join(config.vdf_dir, name)
                         for name in os.listdir(config.vdf_dir)
                         if name.endswith(".vdf"))
        try:
            assert_no_credentials(present)
        except SteamPipeError as error:
            errors.append(str(error))

    with open(app_path, "r", encoding="utf-8") as handle:
        app_text = handle.read()

    leftover = re.search(r"\$\{[A-Za-z_]+\}", app_text)
    if leftover is not None:
        errors.append("{0} still contains the placeholder {1}".format(
            app_path, leftover.group(0)))

    try:
        root = parse_vdf(app_text)
    except SteamPipeError as error:
        return ["{0}: {1}".format(app_path, error)], warnings

    if root.keys() != ["AppBuild"]:
        errors.append("{0}: root must be exactly one 'AppBuild' block, got {1}".format(
            app_path, root.keys()))
        return errors, warnings

    app = root.block("AppBuild")

    def field(node, key, where):
        try:
            return node.string(key)
        except SteamPipeError as error:
            errors.append("{0}: {1}".format(where, error))
            return None

    app_id_text = field(app, "AppID", app_path)
    if app_id_text is not None and app_id_text != str(config.app_id):
        errors.append(
            "{0}: AppID is {1} but steam.config says {2}. Re-render; never hand-edit "
            "a rendered VDF.".format(app_path, app_id_text, config.app_id))

    description = field(app, "Desc", app_path)
    if description is not None and not description.strip():
        errors.append(app_path + ": Desc is empty; the Builds page needs it to tell "
                                 "two uploads apart")

    content_root = field(app, "ContentRoot", app_path)
    if content_root is not None:
        if not os.path.isabs(content_root):
            errors.append(app_path + ": ContentRoot must be absolute")
        elif os.path.normpath(content_root) != os.path.normpath(config.stage_root):
            errors.append(
                "{0}: ContentRoot {1} is not the staging tree {2}. Uploading "
                "straight from a Unity build directory ships its DoNotShip "
                "folders.".format(app_path, content_root, config.stage_root))
        elif not os.path.isdir(content_root):
            errors.append(app_path + ": ContentRoot does not exist: " + content_root)

    build_output = field(app, "BuildOutput", app_path)
    if build_output is not None and not os.path.isdir(build_output):
        errors.append(app_path + ": BuildOutput does not exist: " + build_output)

    set_live = field(app, "SetLive", app_path)
    if set_live is not None:
        if set_live.lower() == "default":
            errors.append(
                app_path + ": SetLive is 'default'. SteamPipe will not promote the "
                "default branch from a build script; do it on the Builds page.")
        elif set_live != branch:
            errors.append(
                "{0}: SetLive is {1!r} but the requested branch is {2!r}".format(
                    app_path, set_live, branch))

    preview = field(app, "Preview", app_path)
    if preview is not None and preview not in ("0", "1"):
        errors.append(app_path + ": Preview must be 0 or 1, got " + preview)

    try:
        depots = app.block("Depots")
    except SteamPipeError as error:
        errors.append("{0}: {1}".format(app_path, error))
        return errors, warnings

    declared = {}
    for key, value in depots.pairs:
        if not re.fullmatch(r"[0-9]+", key):
            errors.append("{0}: depot key {1!r} is not numeric".format(app_path, key))
            continue
        if not isinstance(value, str):
            errors.append("{0}: depot {1} must map to a script name".format(app_path, key))
            continue
        if key in declared:
            errors.append("{0}: depot {1} declared twice".format(app_path, key))
        declared[key] = value

    expected = {str(config.depots[p.key]): p for p in PLATFORMS}
    for depot_id in sorted(expected):
        if depot_id not in declared:
            errors.append("{0}: depot {1} ({2}) is not in the Depots block".format(
                app_path, depot_id, expected[depot_id].label))
    for depot_id in sorted(declared):
        if depot_id not in expected:
            errors.append(
                "{0}: depot {1} is not one of the configured depots ({2}). A wrong "
                "depot ID uploads content to the wrong place.".format(
                    app_path, depot_id, ", ".join(sorted(expected))))

    for platform in PLATFORMS:
        depot_id = str(config.depots[platform.key])
        script_name = declared.get(depot_id)
        if script_name is None:
            continue
        if script_name != platform.vdf_name:
            warnings.append("{0}: depot {1} points at {2}, expected {3}".format(
                app_path, depot_id, script_name, platform.vdf_name))
        script_path = os.path.join(config.vdf_dir, script_name)
        if not os.path.isfile(script_path):
            errors.append("{0}: depot script missing: {1}".format(app_path, script_path))
            continue
        errors.extend(validate_depot(config, platform, script_path))

        staged = config.stage_dir(platform.key)
        count = 0
        if os.path.isdir(staged):
            for _, _, filenames in os.walk(staged):
                count += len(filenames)
        if count == 0:
            message = ("{0} depot ({1}): staged content tree {2} is empty".format(
                platform.label, depot_id, staged))
            if require_content:
                errors.append(message + " - refusing to upload an empty depot")
            else:
                warnings.append(message + " - no build has been produced yet")

    return errors, warnings


def validate_depot(config: Config, platform: Platform, path: str) -> List[str]:
    errors: List[str] = []
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read()

    leftover = re.search(r"\$\{[A-Za-z_]+\}", text)
    if leftover is not None:
        errors.append("{0} still contains the placeholder {1}".format(
            path, leftover.group(0)))

    try:
        root = parse_vdf(text)
    except SteamPipeError as error:
        return ["{0}: {1}".format(path, error)]

    if root.keys() != ["DepotBuild"]:
        return ["{0}: root must be exactly one 'DepotBuild' block, got {1}".format(
            path, root.keys())]

    depot = root.block("DepotBuild")
    expected_id = str(config.depots[platform.key])
    try:
        depot_id = depot.string("DepotID")
    except SteamPipeError as error:
        errors.append("{0}: {1}".format(path, error))
        depot_id = None
    if depot_id is not None and depot_id != expected_id:
        errors.append("{0}: DepotID is {1} but steam.config resolves to {2}".format(
            path, depot_id, expected_id))

    mappings = [v for v in depot.all("FileMapping") if isinstance(v, VdfNode)]
    if not mappings:
        errors.append(path + ": no FileMapping block; the depot would be empty")
    for mapping in mappings:
        try:
            local = mapping.string("LocalPath")
            depot_path = mapping.string("DepotPath")
            recursive = mapping.string("recursive")
        except SteamPipeError as error:
            errors.append("{0}: FileMapping {1}".format(path, error))
            continue
        prefix = platform.key + "/"
        if not local.startswith(prefix):
            errors.append(
                "{0}: LocalPath {1!r} must start with {2!r} so this depot can only "
                "see its own platform's staged files".format(path, local, prefix))
        if depot_path not in (".", "./"):
            errors.append("{0}: DepotPath should be '.', got {1!r}".format(path, depot_path))
        if recursive not in ("0", "1"):
            errors.append("{0}: recursive must be 0 or 1, got {1!r}".format(path, recursive))

    for value in depot.all("FileExclusion"):
        if not isinstance(value, str) or not value:
            errors.append(path + ": FileExclusion must be a non-empty string")

    return errors


# ── fixture ─────────────────────────────────────────────────────────────────

def make_fixture(config: Config) -> Dict[str, str]:
    """Synthesises a Unity-shaped build tree, including the junk Unity leaves.

    The Unity editor is not installed yet, and the release pipeline is worth
    having working before it is (§14 puts Steam work early on purpose). Every
    directory and file name here is one Unity really produces, and three of them
    are things that must never reach a depot — so a dry run over this fixture
    exercises the exclusion rules rather than just the happy path.
    """
    if os.path.isdir(config.fixture_root):
        shutil.rmtree(config.fixture_root)

    windows = config.fixture_dir("windows")
    stem = config.windows_exe_name[:-len(".exe")]
    data = os.path.join(windows, stem + "_Data")
    ensure_dir(os.path.join(data, "Managed"))
    ensure_dir(os.path.join(windows, stem + "_BurstDebugInformation_DoNotShip"))

    files = {
        os.path.join(windows, config.windows_exe_name): FIXTURE_NOTICE,
        os.path.join(windows, "UnityPlayer.dll"): FIXTURE_NOTICE,
        os.path.join(windows, "UnityCrashHandler64.exe"): FIXTURE_NOTICE,
        os.path.join(windows, stem + ".pdb"): "excluded: debug symbols\n",
        os.path.join(windows, ".DS_Store"): "excluded: finder metadata\n",
        os.path.join(data, "data.unity3d"): FIXTURE_NOTICE,
        os.path.join(data, "globalgamemanagers"): FIXTURE_NOTICE,
        os.path.join(data, "Managed", "Assembly-CSharp.dll"): FIXTURE_NOTICE,
        os.path.join(data, "Managed", "Mirror.dll"): FIXTURE_NOTICE,
        os.path.join(data, "Managed", "com.rlabrecque.steamworks.net.dll"): FIXTURE_NOTICE,
        os.path.join(windows, stem + "_BurstDebugInformation_DoNotShip", "symbols.txt"):
            "excluded: burst symbols\n",
    }

    macos = config.fixture_dir("macos")
    bundle = os.path.join(macos, config.macos_app_name)
    ensure_dir(os.path.join(bundle, "Contents", "MacOS"))
    ensure_dir(os.path.join(bundle, "Contents", "Resources", "Data", "Managed"))
    ensure_dir(os.path.join(bundle, "Contents", "Frameworks"))
    files.update({
        os.path.join(bundle, "Contents", "Info.plist"):
            "<!-- fixture Info.plist -->\n",
        os.path.join(bundle, "Contents", "MacOS", "HorrorGame"): FIXTURE_NOTICE,
        os.path.join(bundle, "Contents", "Resources", "Data", "data.unity3d"):
            FIXTURE_NOTICE,
        os.path.join(bundle, "Contents", "Resources", "Data", "Managed",
                     "Assembly-CSharp.dll"): FIXTURE_NOTICE,
        os.path.join(bundle, "Contents", "Frameworks", "libsteam_api.dylib"):
            FIXTURE_NOTICE,
        os.path.join(bundle, "Contents", "._Info.plist"):
            "excluded: appledouble sidecar\n",
        os.path.join(macos, ".DS_Store"): "excluded: finder metadata\n",
    })

    for path, body in files.items():
        ensure_dir(os.path.dirname(path))
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(body)

    # The executable bit is load-bearing on macOS: SteamPipe records the mode it
    # sees and the installed bundle will not launch without it.
    for path in (os.path.join(bundle, "Contents", "MacOS", "HorrorGame"),):
        os.chmod(path, 0o755)

    return {"windows": windows, "macos": macos}


# ── Subcommands ─────────────────────────────────────────────────────────────

def load(args: argparse.Namespace) -> Config:
    return Config(args.repo_root, args.config)


def cmd_env(args: argparse.Namespace) -> int:
    """Prints shell assignments. upload.sh evals this so there is exactly one
    parser for steam.config in the whole pipeline."""
    config = load(args)
    pairs = [
        ("HG_CONFIG_PATH", config.config_path),
        ("HG_APP_ID", str(config.app_id)),
        ("HG_APP_ID_IS_SPACEWAR", "1" if config.is_spacewar else "0"),
        ("HG_DEPOT_WINDOWS", str(config.depots["windows"])),
        ("HG_DEPOT_MACOS", str(config.depots["macos"])),
        ("HG_WINDOWS_BUILD_DIR", config.build_dirs["windows"]),
        ("HG_MACOS_BUILD_DIR", config.build_dirs["macos"]),
        ("HG_OUTPUT_ROOT", config.output_root),
        ("HG_STAGE_ROOT", config.stage_root),
        ("HG_BUILD_OUTPUT", config.build_output),
        ("HG_VDF_DIR", config.vdf_dir),
        ("HG_LOG_DIR", config.log_dir),
        ("HG_MANIFEST_DIR", config.manifest_dir),
        ("HG_FIXTURE_ROOT", config.fixture_root),
        ("HG_APP_BUILD_VDF", config.app_build_vdf()),
        ("HG_STEAMCMD", config.steamcmd),
    ]
    for name, value in pairs:
        sys.stdout.write("{0}={1}\n".format(name, shlex.quote(value)))
    return 0


def cmd_render(args: argparse.Namespace) -> int:
    config = load(args)
    description = build_description(config, args.branch, args.desc)
    written = render(config, args.branch, description, args.preview)
    print("  App ID      {0}{1}".format(
        config.app_id, "   (Spacewar - test app)" if config.is_spacewar else ""))
    print("  Depots      {0} windows, {1} macos".format(
        config.depots["windows"], config.depots["macos"]))
    print("  SetLive     {0}".format(args.branch or "(none - build stays unassigned)"))
    print("  Preview     {0}".format("1 (steamcmd would not upload)" if args.preview else "0"))
    print("  Desc        {0}".format(description))
    for path in written:
        print("  rendered    {0}".format(os.path.relpath(path, config.repo_root)))
    return 0


def cmd_stage(args: argparse.Namespace) -> int:
    config = load(args)
    if args.source == "fixture":
        paths = make_fixture(config)
        print("  fixture     synthesised at {0}".format(
            os.path.relpath(config.fixture_root, config.repo_root)))
        sources = paths
    else:
        sources = {p.key: config.build_dirs[p.key] for p in PLATFORMS}

    status = 0
    for platform in PLATFORMS:
        result = stage_platform(config, platform, sources[platform.key])
        manifest = write_manifest(config, result)
        depot_id = config.depots[platform.key]
        print("")
        print("  {0} depot {1}".format(platform.label, depot_id))
        print("    source    {0}{1}".format(
            os.path.relpath(result.source, config.repo_root),
            "" if result.source_exists else "   MISSING"))
        print("    staged    {0}".format(
            os.path.relpath(result.destination, config.repo_root)))
        print("    content   {0} files, {1}".format(
            len(result.files), human_size(result.total_bytes)))
        for relative_path, size in sorted(result.files)[:args.list_limit]:
            print("                {0:>10}  {1}".format(human_size(size), relative_path))
        if len(result.files) > args.list_limit:
            print("                {0:>10}  ... {1} more (see {2})".format(
                "", len(result.files) - args.list_limit,
                os.path.relpath(manifest, config.repo_root)))
        if result.excluded:
            print("    excluded  {0} entries".format(len(result.excluded)))
            for relative_path, pattern in sorted(result.excluded):
                print("                {0}   [{1}]".format(relative_path, pattern))
        if result.symlinks:
            print("    symlinks  {0} dereferenced (SteamPipe does not record links)"
                  .format(len(result.symlinks)))
        if not result.source_exists:
            print("    note      no build here yet - Unity has not produced one")

        errors, warnings = check_staged_layout(config, result)
        for message in warnings:
            print("    WARN      {0}".format(message))
        for message in errors:
            print("    ERROR     {0}".format(message))
            status = 1

    return status


def cmd_validate(args: argparse.Namespace) -> int:
    config = load(args)
    errors, warnings = validate(config, args.branch, args.require_content)
    for message in warnings:
        print("  WARN   {0}".format(message))
    for message in errors:
        print("  ERROR  {0}".format(message))
    if errors:
        print("  {0} error(s) in the rendered build scripts.".format(len(errors)))
        return 1
    print("  app_build.vdf, depot_windows.vdf, depot_macos.vdf parse and agree "
          "with steam.config.")
    return 0


def cmd_fixture(args: argparse.Namespace) -> int:
    config = load(args)
    paths = make_fixture(config)
    for key in sorted(paths):
        print("  {0:<8} {1}".format(key, os.path.relpath(paths[key], config.repo_root)))
    return 0


def main(argv: Sequence[str]) -> int:
    parser = argparse.ArgumentParser(
        prog="steampipe.py",
        description="Render, stage and validate SteamPipe build scripts. "
                    "Never contacts Steam.")
    parser.add_argument("--repo-root", required=True,
                        help="repository root; all config paths resolve against it")
    parser.add_argument("--config", required=True, help="path to steam.config")
    sub = parser.add_subparsers(dest="command")

    sub.add_parser("env", help="print shell assignments for upload.sh")

    render_parser = sub.add_parser("render", help="write the VDFs from templates/")
    render_parser.add_argument("--branch", default="",
                              help="branch to SetLive; empty leaves the build unassigned")
    render_parser.add_argument("--desc", default=None, help="build description override")
    render_parser.add_argument("--preview", action="store_true",
                               help="render Preview 1 (steamcmd builds but does not upload)")

    stage_parser = sub.add_parser("stage", help="assemble the depot content tree")
    stage_parser.add_argument("--source", choices=("build", "fixture"), default="build")
    stage_parser.add_argument("--list-limit", type=int, default=25)

    validate_parser = sub.add_parser("validate", help="check the rendered VDFs")
    validate_parser.add_argument("--branch", default="")
    validate_parser.add_argument("--require-content", action="store_true",
                                 help="fail when a depot's staged tree is empty")

    sub.add_parser("fixture", help="synthesise a fake Unity build")

    args = parser.parse_args(list(argv))
    if not args.command:
        parser.print_help()
        return 2

    handlers = {
        "env": cmd_env,
        "render": cmd_render,
        "stage": cmd_stage,
        "validate": cmd_validate,
        "fixture": cmd_fixture,
    }
    try:
        return handlers[args.command](args)
    except SteamPipeError as error:
        sys.stderr.write("steampipe: {0}\n".format(error))
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
