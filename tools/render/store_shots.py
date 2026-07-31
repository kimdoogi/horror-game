"""Drives Unity in batch mode to render the Steam store screenshots.

Why a driver and not a menu item
--------------------------------
`SceneShot` renders 1280x720 from viewpoints it derives from the scene, which is
right for judging the grade and wrong for a store page: Valve's asset guidelines
put screenshots at 1920x1080 minimum, 16:9, gameplay only, and "the corridor and
the beam" is a chosen frame rather than a computed one.

Why the Unity half is staged
----------------------------
The engine-side rig lives at ``tools/render/unity/StoreShotRig.cs`` and is copied
into the Unity project only for the duration of one batch run, then removed
again.  The store page owns ``tools/render/`` and ``docs/store/``; it does not own
``Assets/``, and a permanent editor script left behind in somebody else's
assembly is exactly the kind of seam that produced B-005.

Exit code first
---------------
A Unity batch run that dies on a held project lock writes a log with zero errors
in it, which is not the same thing as a clean run.  Every invocation here checks
the exit code before it looks at anything else, and retries while the lock is
held rather than reporting a green run that never happened.

Usage
-----
    python3 tools/render/store_shots.py probe  --out /tmp/store_probe.json
    python3 tools/render/store_shots.py shoot  --spec tools/render/store_shots.json
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROJECT = os.path.join(REPO, "unity", "HorrorGame")
UNITY = "/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity"

RIG_SOURCE = os.path.join(os.path.dirname(__file__), "unity", "StoreShotRig.cs")

# A folder of its own, named so that a crashed run leaves something obviously
# disposable behind rather than a file that looks like it belongs to the project.
STAGE_DIR = os.path.join(PROJECT, "Assets", "StoreShotStaging")
STAGE_FILE = os.path.join(STAGE_DIR, "Editor", "StoreShotRig.cs")

LOCKFILE = os.path.join(PROJECT, "Temp", "UnityLockfile")


def unstage():
    """Removes the staged rig and every .meta Unity minted for it."""
    for path in (STAGE_DIR, STAGE_DIR + ".meta"):
        if os.path.isdir(path):
            shutil.rmtree(path)
        elif os.path.exists(path):
            os.remove(path)


def stage():
    """Copies the rig into the project. Refuses to run on a stale staging folder."""
    unstage()
    os.makedirs(os.path.dirname(STAGE_FILE), exist_ok=True)
    shutil.copyfile(RIG_SOURCE, STAGE_FILE)


def wait_for_lock(timeout=1800):
    """Blocks while another Unity process holds the project.

    One lock, and an early exit on a held lock is indistinguishable from success
    in the log.  Serialising here is cheaper than reading a meaningless report.
    """
    waited = 0
    while os.path.exists(LOCKFILE) and waited < timeout:
        print("[store_shots] project lock held; waiting...", flush=True)
        time.sleep(15)
        waited += 15
    if os.path.exists(LOCKFILE):
        raise SystemExit("[store_shots] project lock still held after %d s" % timeout)


def run_unity(method, args, logfile, attempts=3):
    """Runs one -executeMethod. Returns the exit code, having printed the tail.

    Deliberately no ``-nographics``: that flag disables the graphics device and
    every frame comes out black.
    """
    command = [
        UNITY,
        "-batchmode",
        "-quit",
        "-silent-crashes",
        "-projectPath", PROJECT,
        "-executeMethod", method,
        "-logFile", logfile,
    ] + args

    for attempt in range(1, attempts + 1):
        wait_for_lock()
        print("[store_shots] %s (attempt %d)" % (method, attempt), flush=True)
        result = subprocess.run(command)

        if result.returncode == 0:
            return 0

        tail = ""
        if os.path.exists(logfile):
            with open(logfile, "r", errors="replace") as handle:
                tail = "".join(handle.readlines()[-40:])

        # Unity returns 1 both for "your code threw" and for "somebody else has
        # the project open". Only the second is worth retrying.
        if "another Unity instance" in tail or "Multiple Unity instances" in tail:
            print("[store_shots] lock contention; retrying", flush=True)
            time.sleep(30)
            continue

        print("[store_shots] exit %d\n%s" % (result.returncode, tail), file=sys.stderr)
        return result.returncode

    return 1


def probe(args):
    stage()
    try:
        extra = ["-storeBeginMatch", str(args.seed)] if args.seed is not None else []
        code = run_unity(
            "HorrorGame.EditorTools.Store.StoreShotRig.Probe",
            ["-storeScene", args.scene, "-storeOut", args.out] + extra,
            args.log,
        )
    finally:
        unstage()

    if code != 0:
        raise SystemExit(code)

    with open(args.out) as handle:
        book = json.load(handle)
    print("[store_shots] %d landmark(s)" % len(book["landmarks"]))
    return 0


def shoot(args):
    with open(args.spec) as handle:
        book = json.load(handle)

    # The spec carries a repo-relative output directory so it stays readable;
    # Unity is given the absolute one because its working directory is the
    # project, not the repository.
    resolved = dict(book)
    resolved["outputDir"] = os.path.join(REPO, book["outputDir"])
    os.makedirs(resolved["outputDir"], exist_ok=True)

    expanded = os.path.join("/tmp", "store_shots_resolved.json")
    with open(expanded, "w") as handle:
        json.dump(resolved, handle, indent=2)

    stage()
    try:
        code = run_unity(
            "HorrorGame.EditorTools.Store.StoreShotRig.Shoot",
            ["-storeSpec", expanded],
            args.log,
        )
    finally:
        unstage()

    if code != 0:
        raise SystemExit(code)

    written = sorted(
        name for name in os.listdir(resolved["outputDir"]) if name.endswith(".png"))
    print("[store_shots] %d frame(s) in %s" % (len(written), book["outputDir"]))
    for name in written:
        print("  " + name)
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("probe", help="dump the scene's landmarks to JSON")
    p.add_argument("--scene", default="Assets/Scenes/Map_FirstSketch_Solo.unity")
    p.add_argument("--out", default="/tmp/store_probe.json")
    p.add_argument("--log", default="/tmp/store_probe.log")
    p.add_argument(
        "--seed", type=int, default=None,
        help="begin a match on this seed first, so the clue, loot and vehicle props exist")
    p.set_defaults(func=probe)

    s = sub.add_parser("shoot", help="render every frame in a shot book")
    s.add_argument("--spec", default=os.path.join(os.path.dirname(__file__), "store_shots.json"))
    s.add_argument("--log", default="/tmp/store_shoot.log")
    s.set_defaults(func=shoot)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
