#!/usr/bin/env python3
"""Films four players in the shipped map and encodes the result with ffmpeg.

Every picture of this game so far has had one person in it, because the solo scene
spawns one. §01 is a four-player co-op game, so this stages four — each with a §04
role colour, each sampling the real clips out of ``Player.fbx`` at its own phase —
walks a camera past them, and hands the PNG sequence to ffmpeg.

The engine-side rig lives at ``tools/render/unity/PartyFilmRig.cs`` and is copied
into a staging folder under ``Assets/`` for the length of the run, the same way
``store_shots.py`` does it: a permanent editor script left behind in somebody
else's project is how a render tool becomes a maintenance burden.

    python3 tools/render/party_film.py --frames 96 --out docs/store/party.mp4
"""
import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROJECT = os.path.join(REPO, "unity", "HorrorGame")
UNITY = "/Applications/Unity/Hub/Editor/6000.3.21f1/Unity.app/Contents/MacOS/Unity"

RIG_SOURCE = os.path.join(os.path.dirname(__file__), "unity", "PartyFilmRig.cs")
STAGE_DIR = os.path.join(PROJECT, "Assets", "PartyFilmStaging")
STAGE_FILE = os.path.join(STAGE_DIR, "Editor", "PartyFilmRig.cs")
LOCKFILE = os.path.join(PROJECT, "Temp", "UnityLockfile")

FLOOR_Y = -7.5
"""B3 기계실's floor. The camera beat this shot is framed from is the trailer's
beat01, which was probed rather than guessed — see tools/render/trailer_frames.json."""


def unstage():
    for path in (STAGE_DIR, STAGE_DIR + ".meta"):
        if os.path.isdir(path):
            shutil.rmtree(path)
        elif os.path.exists(path):
            os.remove(path)


def stage():
    unstage()
    os.makedirs(os.path.dirname(STAGE_FILE), exist_ok=True)
    shutil.copyfile(RIG_SOURCE, STAGE_FILE)


def wait_for_lock(timeout=1800):
    waited = 0
    while os.path.exists(LOCKFILE) and waited < timeout:
        print("[party_film] project lock held; waiting...", flush=True)
        time.sleep(15)
        waited += 15


def v(x, y, z):
    return {"x": x, "y": y, "z": z}


RUN_SPEED = 4.5
"""GameConstants.RunSpeed. The Run clip's stance is authored against it — the planted
foot travels backward at exactly this — so a rig translated at any other speed skates."""

MONSTER_CHASE_SPEED = 4.6729
"""Monster.clips.json, measured from the weight-bearing foot rather than derived. The
first cut moved the creature at 2.0 m/s while playing a 4.67 m/s stride, which is the
waddle: two and a half strides of foot travel for every stride of ground."""


def spec_for(args):
    """A chase, in metres from the scene's own player: +Z ahead, +X right, Y up.

    Everything that moves travels at the speed its clip was authored at. That is not
    polish — it is the difference between running and skating, and it is the same rule
    MonsterAnimationDriver already applies at runtime ("the previous version assumed
    every clip was authored at 4.8 m/s, which is why the patrolling monster skated").
    """
    seconds = args.frames / float(args.fps)
    run = RUN_SPEED * seconds
    chase = MONSTER_CHASE_SPEED * seconds

    # They start far enough out to still be running when they reach the lens.
    lead = run + 1.6

    roles = ("Listener", "Observer", "Runner", "Engineer")
    layout = ((0.0, -0.95), (0.9, 0.85), (2.3, -0.55), (3.0, 0.70))
    phases = (0.0, 0.41, 0.66, 0.18)

    walkers = [{
        "role": role,
        "start": v(lateral, 0.0, lead + back),
        "travel": run,
        "yaw": 180.0,
        "clip": "Run",
        "phase": phase,
    } for role, (back, lateral), phase in zip(roles, layout, phases)]

    return {
        "anchorOnScenePlayer": True,
        "scene": "Assets/Scenes/Map_FirstSketch_Solo.unity",
        "outputDir": args.frames_dir,
        "width": args.width,
        "height": args.height,
        "frames": args.frames,
        # Held. A dolly back put the lens inside a wall on an earlier take: the spawn
        # point is cleared, the metre behind it is not, and nothing here knows that.
        "cameraFrom": v(0.0, 1.58, 0.2),
        "cameraTo": v(0.0, 1.60, 0.2),
        "cameraEulerFrom": v(3.0, 0.0, 0.0),
        "cameraEulerTo": v(4.0, 2.0, 0.0),
        "fov": 66.0,
        "walkDirection": v(0.0, 0.0, -1.0),
        "walkers": walkers,
        "torches": True,
        "monster": True,
        # Five metres behind the rearmost runner, and gaining: 4.67 against 4.5 is
        # §06's whole argument, and over this shot it closes 0.5 m of it.
        "monsterStart": v(0.25, 0.0, lead + 3.0 + 5.0 + (chase - run)),
        "monsterTravel": chase,
        "monsterYaw": 180.0,
    }


def run_unity(spec_path, logfile):
    wait_for_lock()
    result = subprocess.run(
        [UNITY, "-batchmode", "-projectPath", PROJECT,
         "-executeMethod", "HorrorGame.EditorTools.Film.PartyFilmRig.Film",
         "-filmSpec", spec_path, "-logFile", logfile],
        capture_output=True, text=True)
    if result.returncode != 0 and os.path.exists(logfile):
        with open(logfile, errors="replace") as handle:
            print("".join(handle.readlines()[-40:]), file=sys.stderr)
    return result.returncode


def encode(frames_dir, out, fps):
    """PNG sequence to h264. yuv420p because everything else refuses to play it."""
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    cmd = ["ffmpeg", "-y", "-framerate", str(fps),
           "-i", os.path.join(frames_dir, "frame_%04d.png"),
           "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18",
           "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", out]
    return subprocess.run(cmd, capture_output=True, text=True).returncode


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--frames", type=int, default=96)
    p.add_argument("--fps", type=int, default=24)
    p.add_argument("--width", type=int, default=1280)
    p.add_argument("--height", type=int, default=720)
    p.add_argument("--frames-dir", default="/tmp/party_frames")
    p.add_argument("--out", default=os.path.join(REPO, "docs", "store", "party.mp4"))
    p.add_argument("--log", default="/tmp/party_film.log")
    args = p.parse_args()

    if os.path.isdir(args.frames_dir):
        shutil.rmtree(args.frames_dir)
    os.makedirs(args.frames_dir, exist_ok=True)

    spec = spec_for(args)
    spec_path = "/tmp/party_film_spec.json"
    with open(spec_path, "w") as handle:
        json.dump(spec, handle, indent=2)

    stage()
    try:
        code = run_unity(spec_path, args.log)
    finally:
        unstage()

    written = len([f for f in os.listdir(args.frames_dir) if f.endswith(".png")])
    print("[party_film] unity exit %d, %d frame(s)" % (code, written))
    if written == 0:
        raise SystemExit(code or 1)

    if encode(args.frames_dir, args.out, args.fps) != 0:
        raise SystemExit("[party_film] ffmpeg failed")
    print("[party_film] wrote %s (%.1f MB)" % (args.out, os.path.getsize(args.out) / 1e6))
    return 0


if __name__ == "__main__":
    sys.exit(main())
