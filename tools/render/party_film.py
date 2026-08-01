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


def spec_for(args):
    """The shot, in metres from the scene's own player: +Z ahead, +X right, Y up.

    Two takes were lost to world coordinates picked off the trailer's shot list — the
    first put three of the four off-screen, the second put all four through a wall.
    PartyFilmRig.AnchorOnScenePlayer converts everything here into world space around
    the player Map_FirstSketch_Solo already spawns, which is a spot the map generator
    cleared for a body.
    """
    # They walk toward the camera, so they face back down -Z and the camera looks +Z.
    roles = ("Listener", "Observer", "Runner", "Engineer")
    layout = ((5.6, -0.85), (6.3, 0.80), (8.4, -0.55), (9.1, 0.60))
    phases = (0.0, 0.37, 0.62, 0.15)

    walkers = [{
        "role": role,
        "start": v(lateral, 0.0, ahead),
        "travel": 4.4,
        "yaw": 180.0,
        "clip": "Walk",
        "phase": phase,
    } for role, (ahead, lateral), phase in zip(roles, layout, phases)]

    return {
        "anchorOnScenePlayer": True,
        "scene": "Assets/Scenes/Map_FirstSketch_Solo.unity",
        "outputDir": args.frames_dir,
        "width": args.width,
        "height": args.height,
        "frames": args.frames,
        # Eye height above the spawn point, backing off slightly as they approach.
        # The camera holds. A 1.5 m dolly back looked fine on paper and put the lens
        # inside a wall by frame 90 — the spawn point is clear, the metre and a half
        # behind it is not, and nothing here knows where the walls are.
        "cameraFrom": v(0.0, 1.62, 0.2),
        "cameraTo": v(0.0, 1.64, 0.2),
        "cameraEulerFrom": v(2.0, 0.0, 0.0),
        "cameraEulerTo": v(2.5, 2.0, 0.0),
        "fov": 62.0,
        "walkDirection": v(0.0, 0.0, -1.0),
        "walkers": walkers,
        "torches": True,
        "monster": True,
        "monsterStart": v(0.3, 0.0, 15.0),
        "monsterTravel": 7.0,
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
