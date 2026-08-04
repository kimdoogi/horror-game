#!/usr/bin/env python3
"""Films the descent in-engine and encodes a Steam-format trailer.

Replaces ``party_film.py``
-------------------------
That tool staged four bodies, painted each one a §04 role colour, and encoded at
1280x720 / 24 fps / CRF 18.  §04's roles and the four-player co-op game they
belonged to were deleted on 2026-08-02, and the file it produced —
``docs/store/party.mp4`` — misses Valve's format on three axes at once.

Why the bitrate is set and not inferred
---------------------------------------
``party_film.py`` asked for ``-crf 18`` and got **1.62 Mbps** against Valve's
5,000+ Kbps floor.  That is not a bug in ffmpeg: this game is a dark corridor lit
by one torch, so a quality-targeted encoder spends almost nothing on it.  Any
constant-quality setting will fail the same way on the same footage, however high
the quality.  The bitrate is therefore a target, not an outcome, and
:func:`encode` asserts the result with ``ffprobe`` rather than trusting it.

Why frames are hardlinked into one run
--------------------------------------
The rig writes one folder per shot so a bad take can be re-shot alone.  Encoding
per-shot segments and concatenating them puts a keyframe boundary and a possible
timestamp seam at every cut.  Hardlinking every frame into one sequentially
numbered directory costs no disk (same filesystem, no copy) and gives ffmpeg a
single continuous input.

    python3 tools/render/descent_film.py --out docs/store/trailer_descent.mp4
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

RIG_SOURCE = os.path.join(os.path.dirname(__file__), "unity", "DescentFilmRig.cs")

# A folder of its own, named so a crashed run leaves something obviously
# disposable rather than a file that looks like it belongs to the project.
STAGE_DIR = os.path.join(PROJECT, "Assets", "DescentFilmStaging")
STAGE_FILE = os.path.join(STAGE_DIR, "Editor", "DescentFilmRig.cs")

LOCKFILE = os.path.join(PROJECT, "Temp", "UnityLockfile")

DEFAULT_SPEC = os.path.join(os.path.dirname(__file__), "trailer_shots.json")


def unstage():
    """Removes the staged rig and every .meta Unity minted for it."""
    for path in (STAGE_DIR, STAGE_DIR + ".meta"):
        if os.path.isdir(path):
            shutil.rmtree(path)
        elif os.path.exists(path):
            os.remove(path)


def stage():
    unstage()
    os.makedirs(os.path.dirname(STAGE_FILE), exist_ok=True)
    shutil.copyfile(RIG_SOURCE, STAGE_FILE)


def wait_for_lock(timeout=2700):
    """Blocks while another Unity process holds the project.

    Five agents work this repository in parallel and a batch run that dies on a
    held lock writes a log with zero errors in it — which is indistinguishable
    from success unless the exit code is read first.  Serialising here is cheaper
    than reading a meaningless report.
    """
    waited = 0
    while os.path.exists(LOCKFILE) and waited < timeout:
        print("[descent_film] project lock held; waiting...", flush=True)
        time.sleep(15)
        waited += 15
    if os.path.exists(LOCKFILE):
        raise SystemExit("[descent_film] project lock still held after %d s" % timeout)


def run_unity(spec_path, logfile):
    """Runs the rig once.  Deliberately no ``-nographics``: it blacks every frame."""
    wait_for_lock()
    command = [
        UNITY,
        "-batchmode",
        "-quit",
        "-silent-crashes",
        "-projectPath", PROJECT,
        "-executeMethod", "HorrorGame.EditorTools.Film.DescentFilmRig.Film",
        "-filmSpec", spec_path,
        "-logFile", logfile,
    ]
    print("[descent_film] %s" % " ".join(command[-6:]), flush=True)
    result = subprocess.run(command)

    if os.path.exists(logfile):
        with open(logfile, errors="replace") as handle:
            lines = handle.readlines()
        for line in lines:
            if "[DescentFilm]" in line or "error CS" in line:
                sys.stdout.write("  " + line)

    return result.returncode


def assemble(book, frames_root, staging):
    """Hardlinks every shot's frames into one sequence, in shot-book order."""
    if os.path.isdir(staging):
        shutil.rmtree(staging)
    os.makedirs(staging)

    index = 0
    per_shot = []
    for shot in book["shots"]:
        folder = os.path.join(frames_root, shot["name"])
        if not os.path.isdir(folder):
            raise SystemExit("[descent_film] no frames for shot %r — the rig did not "
                             "reach it. Read the log before anything else." % shot["name"])
        names = sorted(n for n in os.listdir(folder) if n.endswith(".png"))
        if not names:
            raise SystemExit("[descent_film] shot %r rendered zero frames." % shot["name"])
        for name in names:
            os.link(os.path.join(folder, name),
                    os.path.join(staging, "f_%05d.png" % index))
            index += 1
        per_shot.append((shot["name"], len(names)))

    return index, per_shot


def encode(staging, out, fps, bitrate_kbps, fade_in, fade_out, total_frames):
    """PNG sequence to H.264 at a *targeted* bitrate, then verifies it."""
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    seconds = total_frames / float(fps)
    out_start = max(0.0, seconds - fade_out)

    filters = "fade=t=in:st=0:d=%.3f,fade=t=out:st=%.3f:d=%.3f" % (
        fade_in, out_start, fade_out)

    cmd = [
        "ffmpeg", "-y",
        "-framerate", str(fps),
        "-i", os.path.join(staging, "f_%05d.png"),
        "-vf", filters,
        "-c:v", "libx264",
        "-profile:v", "high",
        "-level", "4.2",
        "-pix_fmt", "yuv420p",
        # A target, not a quality setting. See the module docstring: constant-quality
        # on torch-lit corridors is exactly how the previous file reached 1.62 Mbps.
        "-b:v", "%dk" % bitrate_kbps,
        "-maxrate", "%dk" % int(bitrate_kbps * 1.25),
        "-bufsize", "%dk" % (bitrate_kbps * 2),
        "-x264-params", "keyint=%d:min-keyint=%d" % (fps * 2, fps),
        "-movflags", "+faststart",
        out,
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        sys.stderr.write(result.stderr[-3000:])
        raise SystemExit("[descent_film] ffmpeg failed")
    return out


def probe(path):
    """What the artefact actually is, read back off disk with ffprobe."""
    fields = "stream=width,height,r_frame_rate,nb_frames,codec_name:format=duration,bit_rate,size"
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", fields, "-of", "json", path],
        capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit("[descent_film] ffprobe failed on " + path)
    data = json.loads(result.stdout)
    stream = data["streams"][0]
    fmt = data["format"]
    return {
        "codec": stream.get("codec_name"),
        "width": int(stream["width"]),
        "height": int(stream["height"]),
        "fps": stream.get("r_frame_rate"),
        "frames": stream.get("nb_frames"),
        "duration": float(fmt["duration"]),
        "bit_rate_kbps": int(fmt["bit_rate"]) / 1000.0,
        "size": int(fmt["size"]),
    }


def check_against_valve(info):
    """STEAM-RELEASE.md §2.3: 1920x1080, 30/29.97 or 60/59.94 fps, 5,000+ Kbps, H.264."""
    verdicts = []
    verdicts.append(("resolution 1920x1080",
                     info["width"] == 1920 and info["height"] == 1080,
                     "%dx%d" % (info["width"], info["height"])))
    verdicts.append(("frame rate 30 or 60",
                     info["fps"] in ("30/1", "60/1", "30000/1001", "60000/1001"),
                     str(info["fps"])))
    verdicts.append(("bitrate >= 5000 Kbps",
                     info["bit_rate_kbps"] >= 5000.0,
                     "%.0f Kbps" % info["bit_rate_kbps"]))
    verdicts.append(("codec h264", info["codec"] == "h264", str(info["codec"])))
    verdicts.append(("30-60 s of runtime",
                     30.0 <= info["duration"] <= 60.0,
                     "%.2f s" % info["duration"]))
    return verdicts


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--spec", default=DEFAULT_SPEC)
    p.add_argument("--frames-dir", default="/tmp/descent_frames")
    p.add_argument("--staging-dir", default="/tmp/descent_sequence")
    p.add_argument("--out", default=os.path.join(REPO, "docs", "store", "trailer_descent.mp4"))
    p.add_argument("--log", default="/tmp/descent_film.log")
    p.add_argument("--bitrate", type=int, default=12000, help="target Kbps")
    p.add_argument("--fade-in", type=float, default=0.6)
    p.add_argument("--fade-out", type=float, default=1.0)
    p.add_argument("--encode-only", action="store_true",
                   help="skip Unity and re-cut the frames already on disk")
    p.add_argument("--shots", default="",
                   help="comma-separated shot names to re-shoot alone, keeping every other "
                        "shot's frames. The cut is still assembled from the whole book.")
    args = p.parse_args()

    with open(args.spec) as handle:
        book = json.load(handle)

    book["outputDir"] = args.frames_dir

    # Re-shooting one take is what the per-shot folders are FOR, and until 2026-08-04
    # there was no way to ask for it: a framing that failed on shot 21 of 23 cost the
    # twenty good takes in front of it as well. The book Unity is handed is filtered;
    # the book `assemble` reads is not, so the cut is always the whole thing.
    only = [s.strip() for s in args.shots.split(",") if s.strip()]
    if only:
        known = {s["name"] for s in book["shots"]}
        unknown = [s for s in only if s not in known]
        if unknown:
            raise SystemExit("[descent_film] no such shot(s): %s" % ", ".join(unknown))

    shoot_book = dict(book)
    if only:
        shoot_book["shots"] = [s for s in book["shots"] if s["name"] in only]

    resolved = "/tmp/descent_film_resolved.json"
    with open(resolved, "w") as handle:
        json.dump(shoot_book, handle, indent=2, ensure_ascii=False)

    if not args.encode_only:
        # Only a full run starts from an empty folder. A partial run must not delete the
        # takes it is not re-shooting.
        if not only and os.path.isdir(args.frames_dir):
            shutil.rmtree(args.frames_dir)
        os.makedirs(args.frames_dir, exist_ok=True)

        if only:
            for name in only:
                stale = os.path.join(args.frames_dir, name)
                if os.path.isdir(stale):
                    shutil.rmtree(stale)
            print("[descent_film] re-shooting %d of %d shot(s): %s"
                  % (len(only), len(book["shots"]), ", ".join(only)), flush=True)

        stage()
        try:
            code = run_unity(resolved, args.log)
        finally:
            unstage()

        # Exit code before frame count, always.
        if code != 0:
            raise SystemExit("[descent_film] Unity exited %d — read %s" % (code, args.log))

    total, per_shot = assemble(book, args.frames_dir, args.staging_dir)
    print("[descent_film] %d frame(s) over %d shot(s):" % (total, len(per_shot)))
    for name, count in per_shot:
        print("    %-28s %4d" % (name, count))

    encode(args.staging_dir, args.out, book.get("fps", 30),
           args.bitrate, args.fade_in, args.fade_out, total)

    info = probe(args.out)
    print("\n[descent_film] %s" % args.out)
    for key in ("codec", "width", "height", "fps", "frames", "duration", "bit_rate_kbps", "size"):
        print("    %-14s %s" % (key, info[key]))

    print("\n[descent_film] against Valve's trailer requirements:")
    failed = 0
    for label, ok, measured in check_against_valve(info):
        print("    %-24s %-5s %s" % (label, "PASS" if ok else "FAIL", measured))
        failed += 0 if ok else 1

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
