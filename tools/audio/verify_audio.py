#!/usr/bin/env python3
"""Cross-family audio audit. Re-runnable after any retune.

Every generator in this directory verifies its own output. That is necessary and
not sufficient: the checks that decide whether the game's audio actually works
are the ones that span families and that no single generator can see.

    usage:  tools/audio/.venv/bin/python tools/audio/verify_audio.py
            ... --json out.json      also dump machine-readable results
            ... --quiet              only print defects and the verdict

Exit code is 0 only when there is no BLOCKING defect.

WHAT THIS CHECKS AND WHY
════════════════════════

[1] INVENTORY — every .wav on disk, with size and duration, grouped by family.
    Also flags files sitting outside their family's folder and basename
    collisions between families (Unity resolves clips by name in a lot of
    hand-written glue code; two `zone_hum_loop.wav` in different folders is a
    bug waiting for a refactor).

[2] §12 CROSS-MATERIAL SEPARATION — the one that decides whether a whole role
    ships. §04 gives the Listener (청음사) the ability to read the monster's
    position from sound, and its 맵 요구 is "구역별로 바닥 재질이 달라야 위치
    판별이 가능(§12)". §12 §직업별 맵 요구조건 is blunter: "구역별로 바닥 재질이
    달라야 청음사가 위치를 판별할 수 있다. 아트 결정이 아니라 시스템 결정이다."

    So the five surfaces are a five-symbol alphabet the player has to decode
    under stress, in the dark, through wall occlusion, one step at a time. This
    builds the full 5x5 spectral-centroid ratio matrix at four levels of
    strictness:
      (a) per surface across all clips — the headline matrix
      (b) within one actor (walk / run / monster step) — what a player actually
          compares, since the monster only ever produces monster steps
      (c) clip level, adjacent surfaces — a single step, not an average, is what
          reaches the ear

    Centroid alone would be a thin claim, so ring-out time and in-band spectral
    flatness are reported alongside it: §12 asks for 울림 on metal and 둔탁 on
    concrete, which is decay, and 부스럭 on gravel, which is noise-vs-pitch.

    (e) the same matrix low-passed to stand in for distance and wall occlusion.
        Every generator measured its clips dry; the Listener never hears them
        dry, and separation carried in the high end does not survive a wall.

[3] LOOP SEAMLESSNESS — for anything whose name says `_loop`, plus the presence
    bed. A loop click is a broadband transient at a fixed period. In a mix this
    quiet (§05 mandates headphones; §06 makes silence the monster's weapon) it
    becomes the loudest and most obviously synthetic event in the scene, and it
    destroys the 정지-state silence that §06 calls 침묵이 가장 무서운 소리다.
    Three failure modes, all measured per channel — because a stereo bed can be
    seamless summed to mono and still click in one ear:
      - CLICK: a sample step at the wrap
      - PULSE: a level step at the wrap, heard when the loop repeats
      - NOTCH: a short amplitude hole from an edge fade applied to loop material
    NOTCH is suppressed when the boundary sits in a designed trough, because
    placing it there is the correct answer to an unconditional edge fade rather
    than a defect — see SEAM_TROUGH_FLOOR_DB.

[4] LEVELS AND FORMAT — no clipping, nothing silent, no DC offset, 48 kHz
    throughout, 16-bit PCM.

[5] CHANNEL POLICY — the setting whose absence silently breaks the Listener.
    Unity will not spatialise a stereo clip: a 2-channel AudioClip on a 3D
    AudioSource plays at full level with no attenuation and no panning. So
    every positional clip MUST be mono. If a footstep or a monster growl ships
    stereo, the Listener hears the monster at constant volume from everywhere,
    the role produces no information, and §13's proximity voice loses its
    distance cue the same way. Non-diegetic clips (UI, 2D beds) are stereo on
    purpose and are checked for the opposite: that they are NOT mono by
    accident, which would waste the stereo image §05's headphones exist for.

[6] HUD vs EARS — the audio against `GameConstants.ListenerClarity*`.
    `ListenerAbility` does not analyse audio. It derives the fix's error radius
    from a hand-authored clarity per floor material and hands the player an
    estimate; the player's ears get the actual clip. Two independent channels
    answer the same question and nothing else in the repo compares them, so a
    disagreement ships silently and teaches the player to distrust the ability.
    Measured as A-weighted energy per surface, swept across occlusion, because
    `ListenerAbility` states outright that the role hears through walls.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import wave
from dataclasses import dataclass
from typing import Iterable, Sequence

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import synth  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
AUDIO = os.path.join(REPO, "unity", "HorrorGame", "Assets", "Audio")

# ── Thresholds ──────────────────────────────────────────────────────────────

#: §12's requirement, expressed as a ratio between two surfaces' spectral
#: centroids. Below this the two floors are the same symbol to a player and the
#: Listener's five-way readout collapses.
SEPARATION_MIN = 1.4

#: A seam step louder than this, relative to full scale, is audible as a click
#: in a quiet mix. -60 dBFS is roughly the floor of what a listener on
#: headphones will notice against §06's silence.
SEAM_STEP_DBFS = -60.0

#: A seam step this many times larger than the clip's own 99.9th-percentile
#: interior sample-to-sample step is a discontinuity rather than signal. This is
#: the scale-free version of the check and the one that actually catches clicks:
#: a loud noisy bed can survive a big absolute step, a quiet drone cannot.
SEAM_STEP_RATIO = 1.5

#: Level step at the wrap, measured on the clip played twice with 5 ms frames,
#: as a multiple of the clip's own 99th-percentile frame-to-frame step. Above
#: this the loop pulses once per period even if it never clicks.
SEAM_JOIN_RATIO = 1.5

#: An amplitude notch at the seam this many dB deeper than the clip's own 5th
#: percentile level is a hole the generator punched rather than content.
SEAM_NOTCH_EXCESS_DB = -6.0

#: ...but a notch is only audible if it lands on audible material. A seam level
#: this far below the clip's median is a *designed trough* — the mid-pause
#: between two breaths, the bottom of a bed's slow surge — and a fade there is
#: inaudible by construction. This guard is what stops the notch test from
#: condemning the correct way to handle a mandatory edge fade.
SEAM_TROUGH_FLOOR_DB = -25.0

MAX_DC = 0.02
CLIP_PEAK = 0.999
SILENT_RMS = 1e-4

SURFACES = ("wood", "tile", "gravel", "concrete", "metal")
ACTORS = ("player_walk", "player_run", "monster_step")

# ── Family / policy configuration ───────────────────────────────────────────


@dataclass(frozen=True)
class Family:
    name: str
    folder: str
    #: Filename patterns that belong in this folder. Anything matching none of
    #: them is reported as a stray.
    owns: tuple[str, ...]
    generator: str


FAMILIES: tuple[Family, ...] = (
    Family("Footsteps", "Footsteps", (r"^step_[a-z]+_[a-z_]+_\d\d\.wav$",), "gen_footsteps.py"),
    Family("Ambience", "Ambience", (r"^amb_.*_loop\.wav$", r"^sfx_(water_drip|creak_distant)_\d\d\.wav$"),
           "gen_ambience.py"),
    Family("Items", "Items", (r"^(flashlight|battery|door|barricade|noisetrap|safe|breaker|zone|flare|"
                              r"chalk|rope|loot|shop|detector|muffler)_.*\.wav$",), "gen_items.py"),
    Family("Monster", "Monster", (r"^monster_.*\.wav$",), "gen_monster_audio.py"),
    Family("UI", "UI", (r"^(clue|objective|death|ghost|threat|heartbeat|escape|match|shop|voice|"
                        r"descend|surface)_.*\.wav$",), "gen_ui.py"),
)

#: POSITIONAL — a thing at a place in the world. Unity must spatialise it, so it
#: MUST be mono. Anything here that ships stereo is a blocking defect.
POSITIONAL = (
    # §04/§12: the Listener's entire information channel.
    r"^step_",
    # §06 state machine: 발소리+포효 come from the monster's position.
    r"^monster_",
    # §03: 그것은 물이 있는 층에 있다 — dripping is a clue, so it must localise.
    r"^sfx_water_drip_",
    # §06 정지: the building has to stay audible from somewhere specific.
    r"^sfx_creak_distant_",
    # §03: the sound the player walks toward when the flashlight is dying.
    r"^amb_generator_hum_loop\.wav$",
    # §09: the ghost cannot speak, only shake a nearby object. If that does not
    # localise, the ghost has no channel at all.
    r"^ghost_rattle_\d\d\.wav$",
    # Every item action happens at a world position — doors the Listener hears,
    # the noise trap that catches the Runner, the flare burning where it landed.
    r"^(flashlight|battery|door|barricade|noisetrap|safe|breaker|zone_hum|flare|chalk|rope|"
    r"loot_pickup|detector|muffler)",
)

#: NON-DIEGETIC — plays in the player's head or as a fixed 2D layer. Stereo on
#: purpose; mono here means a wasted image, not a broken system.
NON_DIEGETIC = (
    r"^amb_zone_[a-d]_",
    r"^amb_stairwell_metal_loop\.wav$",
    r"^amb_surface_vehicle_loop\.wav$",
    r"^amb_tension_t\d_",
    r"^(clue_read|objective_|death_transition|threat_|heartbeat_|escape_success|"
    r"match_failure_wipe|shop_open|shop_close|shop_denied|voice_|descend_basement|"
    r"surface_reached|ghost_rattle_ready)",
    r"^(loot_sell_credit|shop_purchase_confirm)\.wav$",
)

#: Clips that must loop. Name-marked ones are found automatically; these are the
#: ones whose role is a loop without the name saying so.
EXTRA_LOOPS = (
    r"^monster_breath_loop_",          # name says loop, listed for clarity
    r"^monster_presence_bed\.wav$",    # a bed is a loop by definition
)


# ── Measurement ─────────────────────────────────────────────────────────────


def raw_channels(path: str) -> tuple[list[np.ndarray], int, int]:
    """Per-channel float arrays, sample rate, sample width in bytes.

    synth.read_wav averages stereo down to mono, which hides a seam step that
    exists in only one channel and hides an L/R DC imbalance. Loop and DC checks
    have to see the channels as Unity will.
    """
    with wave.open(path, "rb") as w:
        nch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if sw != 2:
        raise ValueError(f"{path}: expected 16-bit PCM, got {sw * 8}-bit")
    data = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    if nch > 1:
        data = data.reshape(-1, nch)
        chans = [data[:, i] for i in range(nch)]
    else:
        chans = [data]
    return chans, sr, sw


def ring_ms(path: str, floor_db: float = -30.0) -> float:
    """Time from peak until the envelope falls `floor_db` below it.

    §12 asks for 울림 (metal) versus 둔탁 (concrete). That is decay length, and
    it is a cue that survives distance and wall occlusion better than centroid
    does, so it is the second axis the Listener can lean on.
    """
    x, sr = synth.read_wav(path)
    env = np.abs(x.astype(np.float64))
    win = max(1, int(0.002 * sr))
    env = np.convolve(env, np.ones(win) / win, mode="same")
    if not len(env) or env.max() <= 0:
        return 0.0
    pk = int(np.argmax(env))
    thresh = env.max() * synth.db_to_gain(floor_db)
    below = np.nonzero(env[pk:] < thresh)[0]
    end = pk + (int(below[0]) if len(below) else len(env) - pk)
    return (end - pk) / sr * 1000.0


def inband_flatness(path: str, low: float, high: float) -> float:
    """Geometric/arithmetic mean of the magnitude spectrum inside a band.

    1.0 is noise, 0 is a pure tone. Measured inside the surface's own band so it
    reports tonality rather than bandwidth — this is what makes gravel's 부스럭
    measurably different from tile's 딱딱 even where centroids are close.
    """
    x, sr = synth.read_wav(path)
    mag = np.abs(np.fft.rfft(x.astype(np.float64) * np.hanning(len(x))))
    freqs = np.fft.rfftfreq(len(x), 1.0 / sr)
    band = np.maximum(mag[(freqs >= low) & (freqs <= high)], 1e-12)
    if not len(band):
        return 0.0
    return float(np.exp(np.mean(np.log(band))) / np.mean(band))


@dataclass
class SeamResult:
    path: str
    channel: int
    step_dbfs: float
    step_ratio: float
    join_ratio: float
    seam_level_db: float
    p5_level_db: float
    notch_excess_db: float

    @property
    def clicks(self) -> bool:
        """A sample-level discontinuity: a broadband tick once per period."""
        return self.step_dbfs > SEAM_STEP_DBFS and self.step_ratio > SEAM_STEP_RATIO

    @property
    def pulses(self) -> bool:
        """A level step across the wrap: the loop breathes once per period."""
        return self.join_ratio > SEAM_JOIN_RATIO

    @property
    def in_trough(self) -> bool:
        """The seam sits where the content was already effectively gone.

        This is the correct way to survive an unconditional edge fade, so it is a
        pass condition, not a defect.
        """
        return self.seam_level_db < SEAM_TROUGH_FLOOR_DB

    @property
    def notched(self) -> bool:
        """A fade-shaped hole punched into material that was still audible."""
        return self.notch_excess_db < SEAM_NOTCH_EXCESS_DB and not self.in_trough

    @property
    def verdict(self) -> str:
        bad = [n for n, f in (("CLICK", self.clicks), ("PULSE", self.pulses),
                              ("NOTCH", self.notched)) if f]
        if bad:
            return "+".join(bad)
        return "seamless (trough)" if self.in_trough else "seamless"


def seam(path: str, frame_ms: float = 5.0, fine_ms: float = 1.0) -> list[SeamResult]:
    """Measures the three ways a loop can fail, per channel.

    Per channel, because a stereo bed can be seamless summed to mono and still
    click in one ear — which is exactly what synth.read_wav's averaging hides.

    STEP — |x[0] - x[-1]|, absolute (dBFS) and as a multiple of the clip's own
        99.9th-percentile interior sample-to-sample delta. The ratio is the
        honest form: a wrap that jumps further than anything inside the waveform
        is a discontinuity regardless of level, and a quiet drone clicks at a
        step a loud noise bed would swallow. Note this test alone is close to
        vacuous on anything written through `synth.write_wav`, whose
        unconditional 2 ms / 6 ms fade forces both end samples to exactly zero.
        A generator reporting only this number would be claiming a guarantee it
        never tested, which is why the next two exist.

    JOIN — concatenate the clip with itself, take the short-time RMS envelope,
        and compare the frame-to-frame step at the join against the 99th
        percentile of every other step. This is the closest thing to actually
        listening to the loop twice: sample continuity says nothing about
        whether the *level* steps, and a level step is heard as a pulse.

    NOTCH — at 1 ms resolution, how far the level dips at the seam relative to
        the clip's own 5th-percentile level. This catches a one-shot fade run
        over loop material: an 8 ms full-depth hole that passes STEP perfectly
        (both ends are zero, so the step is zero) and is too short for JOIN's
        5 ms frames to resolve. It is only reported when the seam is *not* in a
        designed trough, because placing the boundary in a trough is the right
        answer to a mandatory fade, not a defect.
    """
    chans, sr, _ = raw_channels(path)
    out: list[SeamResult] = []
    hop = max(1, int(frame_ms / 1000.0 * sr))
    fine = max(1, int(fine_ms / 1000.0 * sr))
    for i, ch in enumerate(chans):
        step = float(abs(ch[0] - ch[-1]))
        d = np.abs(np.diff(ch))
        interior = float(np.percentile(d, 99.9)) if len(d) else 0.0

        doubled = np.concatenate([ch, ch])

        def envelope(h: int) -> np.ndarray:
            nf = len(doubled) // h
            if nf < 8:
                return np.array([])
            return np.sqrt(np.mean(doubled[: nf * h].reshape(nf, h) ** 2, axis=1))

        coarse = envelope(hop)
        if len(coarse) > 2:
            steps = np.abs(np.diff(coarse))
            j = len(ch) // hop
            # A widened window, because the wrap need not land on a frame edge.
            lo, hi = max(0, j - 2), min(len(steps), j + 3)
            join_step = float(np.max(steps[lo:hi])) if hi > lo else 0.0
            normal = float(np.percentile(steps, 99.0))
            join_ratio = join_step / normal if normal > 1e-12 else 0.0
        else:
            join_ratio = 0.0

        env = envelope(fine)
        if len(env) > 16:
            median = float(np.median(env[env > 0])) if np.any(env > 0) else 1e-12
            j = len(ch) // fine
            win = env[max(0, j - 8):j + 8]
            seam_level = synth.gain_to_db(max(float(win.min()), 1e-12) / max(median, 1e-12))
            p5 = synth.gain_to_db(max(float(np.percentile(env, 5)), 1e-12) / max(median, 1e-12))
        else:
            seam_level, p5 = 0.0, 0.0

        out.append(SeamResult(
            path=path, channel=i,
            step_dbfs=synth.gain_to_db(max(step, 1e-12)),
            step_ratio=step / interior if interior > 1e-12 else (0.0 if step < 1e-12 else 1e9),
            join_ratio=join_ratio,
            seam_level_db=seam_level,
            p5_level_db=p5,
            notch_excess_db=seam_level - p5,
        ))
    return out


# ── Defects ─────────────────────────────────────────────────────────────────

BLOCKING, WARN, INFO = "BLOCKING", "WARNING", "INFO"


@dataclass
class Defect:
    severity: str
    check: str
    target: str
    detail: str


class Audit:
    def __init__(self) -> None:
        self.defects: list[Defect] = []

    def add(self, severity: str, check: str, target: str, detail: str) -> None:
        self.defects.append(Defect(severity, check, target, detail))

    @property
    def blocking(self) -> list[Defect]:
        return [d for d in self.defects if d.severity == BLOCKING]

    @property
    def warnings(self) -> list[Defect]:
        return [d for d in self.defects if d.severity == WARN]


# ── Helpers ─────────────────────────────────────────────────────────────────


def matches(name: str, patterns: Iterable[str]) -> bool:
    return any(re.search(p, name) for p in patterns)


def geo_mean(values: Sequence[float]) -> float:
    v = np.asarray([x for x in values if x > 0], dtype=np.float64)
    return float(np.exp(np.mean(np.log(v)))) if len(v) else 0.0


def is_loop(name: str) -> bool:
    return "_loop" in name or matches(name, EXTRA_LOOPS)


def kind(name: str) -> str:
    if matches(name, POSITIONAL):
        return "positional"
    if matches(name, NON_DIEGETIC):
        return "non-diegetic"
    return "unclassified"


def human(n: int) -> str:
    return f"{n / 1024:.0f} KB" if n < 1024 * 1024 else f"{n / 1048576:.2f} MB"


def ratio_matrix(centroids: dict[str, float]) -> tuple[list[list[str]], float, tuple[str, str]]:
    order = sorted(centroids, key=lambda k: centroids[k])
    rows: list[list[str]] = []
    worst, pair = float("inf"), ("", "")
    for a in order:
        row = [a]
        for b in order:
            if a == b:
                row.append("—")
                continue
            hi, lo = max(centroids[a], centroids[b]), min(centroids[a], centroids[b])
            r = hi / lo
            row.append(f"{r:.2f}")
            if r < worst:
                worst, pair = r, (a, b)
        rows.append(row)
    return rows, worst, pair


def print_matrix(title: str, centroids: dict[str, float], note: str = "") -> tuple[float, tuple[str, str]]:
    order = sorted(centroids, key=lambda k: centroids[k])
    rows, worst, pair = ratio_matrix(centroids)
    print()
    print(title)
    if note:
        print(f"  {note}")
    print("  centroid Hz:  " + "   ".join(f"{k}={centroids[k]:.0f}" for k in order))
    print()
    head = "  " + f"{'ratio':<11}" + "".join(f"{k:>11}" for k in order)
    print(head)
    print("  " + "-" * (len(head) - 2))
    for row in rows:
        print(f"  {row[0]:<11}" + "".join(f"{c:>11}" for c in row[1:]))
    verdict = "PASS" if worst >= SEPARATION_MIN else "FAIL"
    print(f"  worst pair: {pair[0]} vs {pair[1]} = {worst:.2f}x "
          f"(need >= {SEPARATION_MIN:.2f}x)  [{verdict}]")
    return worst, pair


# ── Checks ──────────────────────────────────────────────────────────────────


def collect(audit: Audit) -> dict[str, list[str]]:
    """Walks the audio tree. Reports strays, collisions and unexpected folders."""
    by_family: dict[str, list[str]] = {}
    seen_basenames: dict[str, list[str]] = {}
    known_folders = {f.folder for f in FAMILIES}

    for entry in sorted(os.listdir(AUDIO)):
        p = os.path.join(AUDIO, entry)
        if os.path.isdir(p) and entry not in known_folders:
            audit.add(WARN, "layout", f"Audio/{entry}/", "folder belongs to no known family")
        if os.path.isfile(p) and entry.lower().endswith(".wav"):
            audit.add(BLOCKING, "layout", entry, "loose .wav at Audio/ root, outside any family folder")

    for fam in FAMILIES:
        folder = os.path.join(AUDIO, fam.folder)
        if not os.path.isdir(folder):
            audit.add(BLOCKING, "layout", fam.folder, f"family folder missing; {fam.generator} never ran")
            by_family[fam.name] = []
            continue
        names = sorted(f for f in os.listdir(folder) if f.lower().endswith(".wav"))
        by_family[fam.name] = names
        for n in names:
            if not matches(n, fam.owns):
                audit.add(WARN, "layout", f"{fam.folder}/{n}",
                          f"name does not match any pattern owned by the {fam.name} family")
            seen_basenames.setdefault(n, []).append(fam.folder)

    for base, folders in seen_basenames.items():
        if len(folders) > 1:
            audit.add(BLOCKING, "collision", base,
                      f"same basename in {len(folders)} families: {', '.join(folders)}")
    return by_family


def check_inventory(by_family: dict[str, list[str]], audit: Audit, quiet: bool) -> dict:
    records: dict[str, dict] = {}
    total_bytes = 0
    total_secs = 0.0
    for fam in FAMILIES:
        names = by_family.get(fam.name, [])
        if not quiet:
            print()
            print(f"── {fam.name}  ({len(names)} clips, {fam.generator}) "
                  + "─" * max(0, 40 - len(fam.name)))
            print(f"  {'file':<44}{'bytes':>10}{'sec':>8}{'ch':>4}{'kHz':>6}  role")
        fam_bytes = 0
        for n in names:
            path = os.path.join(AUDIO, fam.folder, n)
            size = os.path.getsize(path)
            chans, sr, sw = raw_channels(path)
            secs = len(chans[0]) / float(sr)
            fam_bytes += size
            total_secs += secs
            k = kind(n)
            if k == "unclassified":
                audit.add(WARN, "policy", f"{fam.folder}/{n}",
                          "not classified positional or non-diegetic — Unity import settings "
                          "cannot be decided for it")
            records[f"{fam.folder}/{n}"] = dict(
                family=fam.name, bytes=size, seconds=round(secs, 4),
                channels=len(chans), sample_rate=sr, bits=sw * 8,
                role=k, loop=is_loop(n),
            )
            if not quiet:
                print(f"  {n:<44}{size:>10}{secs:>8.3f}{len(chans):>4}{sr / 1000:>6.0f}  "
                      f"{k}{' LOOP' if is_loop(n) else ''}")
        total_bytes += fam_bytes
        if not quiet:
            print(f"  {'':<44}{human(fam_bytes):>10}")

    if not quiet:
        print()
        print(f"TOTAL: {len(records)} wav, {human(total_bytes)}, {total_secs:.1f}s of audio")
    return records


def check_levels(records: dict, audit: Audit, quiet: bool) -> None:
    if not quiet:
        print()
        print("=" * 92)
        print("[4] LEVELS AND FORMAT — clipping, silence, DC, sample rate, bit depth")
        print("=" * 92)
    worst_dc = ("", 0.0)
    worst_peak = ("", 0.0)
    for rel, rec in records.items():
        path = os.path.join(AUDIO, rel)
        r = synth.analyse(path)
        chans, sr, sw = raw_channels(path)

        if sr != synth.SAMPLE_RATE:
            audit.add(BLOCKING, "format", rel,
                      f"{sr} Hz, expected {synth.SAMPLE_RATE} Hz — Unity will resample and "
                      f"shift every centroid this audit measured")
        if sw != 2:
            audit.add(WARN, "format", rel, f"{sw * 8}-bit, house convention is 16-bit PCM")

        for i, ch in enumerate(chans):
            pk = float(np.max(np.abs(ch))) if len(ch) else 0.0
            rms = float(np.sqrt(np.mean(ch ** 2))) if len(ch) else 0.0
            dc = float(np.mean(ch)) if len(ch) else 0.0
            n_clip = int(np.sum(np.abs(ch) >= 0.9995))
            tag = rel if len(chans) == 1 else f"{rel}[ch{i}]"
            if pk > CLIP_PEAK:
                audit.add(BLOCKING, "level", tag,
                          f"peaks at full scale ({n_clip} samples at 0 dBFS) — no headroom")
            if rms < SILENT_RMS:
                audit.add(BLOCKING, "level", tag, f"effectively silent (rms {rms:.2e})")
            if abs(dc) > MAX_DC:
                audit.add(BLOCKING, "level", tag,
                          f"DC offset {dc:+.4f} exceeds {MAX_DC} — wastes headroom and thumps "
                          f"when the source starts")
            if abs(dc) > abs(worst_dc[1]):
                worst_dc = (tag, dc)
            if pk > worst_peak[1]:
                worst_peak = (tag, pk)

        rec["peak_db"] = round(r.peak_db, 2)
        rec["rms_db"] = round(synth.gain_to_db(max(r.rms, 1e-12)), 2)
        rec["dc"] = round(r.dc_offset, 6)
        rec["centroid_hz"] = round(r.spectral_centroid, 1)

    if not quiet:
        rates = sorted({r["sample_rate"] for r in records.values()})
        depths = sorted({r["bits"] for r in records.values()})
        print(f"  sample rates present: {rates}     bit depths: {depths}")
        print(f"  worst DC offset : {worst_dc[1]:+.5f}  ({worst_dc[0]})   limit ±{MAX_DC}")
        print(f"  highest peak    : {synth.gain_to_db(max(worst_peak[1], 1e-12)):.2f} dBFS  "
              f"({worst_peak[0]})")
        quietest = min(records.items(), key=lambda kv: kv[1]["rms_db"])
        print(f"  quietest clip   : {quietest[1]['rms_db']:.1f} dBFS rms  ({quietest[0]})")


def check_channel_policy(records: dict, audit: Audit, quiet: bool) -> None:
    if not quiet:
        print()
        print("=" * 92)
        print("[5] CHANNEL POLICY — positional MUST be mono or Unity will not spatialise it")
        print("=" * 92)
    pos_stereo, nd_mono = [], []
    counts = {"positional": [0, 0], "non-diegetic": [0, 0], "unclassified": [0, 0]}
    for rel, rec in records.items():
        role, ch = rec["role"], rec["channels"]
        counts[role][0 if ch == 1 else 1] += 1
        if role == "positional" and ch != 1:
            pos_stereo.append(rel)
            audit.add(BLOCKING, "channels", rel,
                      f"positional clip is {ch}-channel. Unity does not spatialise a stereo "
                      f"AudioClip: it plays at fixed level with no attenuation and no panning, "
                      f"so the Listener (§04) gets no distance or direction from it")
        if role == "non-diegetic" and ch == 1:
            nd_mono.append(rel)
            audit.add(INFO, "channels", rel,
                      "non-diegetic clip is mono — not broken, but the stereo image §05's "
                      "mandatory headphones exist for is unused")
    if not quiet:
        print(f"  {'role':<16}{'mono':>7}{'stereo':>8}   requirement")
        print("  " + "-" * 74)
        print(f"  {'positional':<16}{counts['positional'][0]:>7}{counts['positional'][1]:>8}"
              f"   MUST be mono (3D spatialisation)")
        print(f"  {'non-diegetic':<16}{counts['non-diegetic'][0]:>7}{counts['non-diegetic'][1]:>8}"
              f"   stereo preferred (2D)")
        print(f"  {'unclassified':<16}{counts['unclassified'][0]:>7}{counts['unclassified'][1]:>8}"
              f"   classify before import")
        print()
        print(f"  positional-but-stereo: {len(pos_stereo)}"
              + (f" → {', '.join(pos_stereo)}" if pos_stereo else "  (none)"))


def check_loops(records: dict, audit: Audit, quiet: bool) -> dict:
    loops = {rel: rec for rel, rec in records.items() if rec["loop"]}
    if not quiet:
        print()
        print("=" * 92)
        print(f"[3] LOOP SEAMLESSNESS — {len(loops)} loop clips")
        print("=" * 92)
        print(f"  {'file':<38}{'ch':>3}{'step dBFS':>10}{'x int':>7}{'join':>7}"
              f"{'seam dB':>9}{'notch':>7}   verdict")
        print("  " + "-" * 90)
    out: dict[str, list[dict]] = {}
    for rel in sorted(loops):
        results = seam(os.path.join(AUDIO, rel))
        out[rel] = []
        for s in results:
            if not quiet:
                print(f"  {os.path.basename(rel):<38}{s.channel:>3}{s.step_dbfs:>10.1f}"
                      f"{s.step_ratio:>7.2f}{s.join_ratio:>7.2f}{s.seam_level_db:>9.1f}"
                      f"{s.notch_excess_db:>7.1f}   {s.verdict}")
            tag = rel if len(results) == 1 else f"{rel}[ch{s.channel}]"
            if s.clicks:
                audit.add(BLOCKING, "loop", tag,
                          f"seam step {s.step_dbfs:.1f} dBFS is {s.step_ratio:.2f}x the clip's own "
                          f"99.9th-pct interior step — a broadband click once per period, and in "
                          f"this mix the loudest thing in the scene")
            if s.pulses:
                audit.add(BLOCKING, "loop", tag,
                          f"level steps {s.join_ratio:.2f}x the clip's own 99th-pct frame step at "
                          f"the wrap — the loop pulses once per period")
            if s.notched:
                audit.add(WARN, "loop", tag,
                          f"seam dips {s.notch_excess_db:.1f} dB below the clip's own 5th-pct level "
                          f"while still {s.seam_level_db:.1f} dB from median — an edge fade landed "
                          f"on audible material, so there is a short hole at every wrap")
            out[rel].append(dict(channel=s.channel, step_dbfs=round(s.step_dbfs, 2),
                                 step_ratio=round(s.step_ratio, 3),
                                 join_ratio=round(s.join_ratio, 3),
                                 seam_level_db=round(s.seam_level_db, 2),
                                 notch_excess_db=round(s.notch_excess_db, 2),
                                 clicks=s.clicks, pulses=s.pulses, notched=s.notched,
                                 in_trough=s.in_trough))
    if not quiet:
        print()
        print(f"  CLICK: step > {SEAM_STEP_DBFS:.0f} dBFS and > {SEAM_STEP_RATIO}x interior step")
        print(f"  PULSE: level step at the wrap > {SEAM_JOIN_RATIO}x the clip's own 99th pct")
        print(f"  NOTCH: seam > {abs(SEAM_NOTCH_EXCESS_DB):.0f} dB below the clip's 5th pct, and")
        print(f"         the seam is not already in a trough (< {SEAM_TROUGH_FLOOR_DB:.0f} dB from")
        print(f"         median). 'seamless (trough)' means the boundary was deliberately")
        print(f"         placed where an unconditional edge fade cannot be heard.")
    return out


def check_material_matrix(audit: Audit, quiet: bool) -> dict:
    """The §12 check. Returns the measured data for the JSON dump."""
    folder = os.path.join(AUDIO, "Footsteps")
    names = sorted(f for f in os.listdir(folder) if f.endswith(".wav"))

    per_clip: dict[str, dict] = {}
    by_surface: dict[str, list[float]] = {s: [] for s in SURFACES}
    by_pair: dict[tuple[str, str], list[float]] = {}
    rings: dict[str, list[float]] = {s: [] for s in SURFACES}
    flats: dict[str, list[float]] = {s: [] for s in SURFACES}
    rms_by_surface: dict[str, list[float]] = {s: [] for s in SURFACES}
    dur_by_surface: dict[str, list[float]] = {s: [] for s in SURFACES}
    missing: list[str] = []

    for s in SURFACES:
        for a in ACTORS:
            for v in range(1, 5):
                n = f"step_{s}_{a}_{v:02d}.wav"
                path = os.path.join(folder, n)
                if not os.path.exists(path):
                    missing.append(n)
                    continue
                r = synth.analyse(path)
                per_clip[n] = dict(surface=s, actor=a, variant=v,
                                   centroid_hz=round(r.spectral_centroid, 1),
                                   rms_db=round(synth.gain_to_db(max(r.rms, 1e-12)), 2),
                                   peak_db=round(r.peak_db, 2),
                                   seconds=round(r.seconds, 4))
                by_surface[s].append(r.spectral_centroid)
                by_pair.setdefault((s, a), []).append(r.spectral_centroid)
                rms_by_surface[s].append(r.rms)
                dur_by_surface[s].append(r.seconds)
                rings[s].append(ring_ms(path))
                # Band chosen wide enough to contain every surface, so flatness
                # reports tonality on a common footing across materials.
                flats[s].append(inband_flatness(path, 120.0, 9000.0))

    for n in missing:
        audit.add(BLOCKING, "§12", n, "footstep clip missing — the 5x5 matrix has a hole in it")

    if not quiet:
        print()
        print("=" * 92)
        print("[2] §12 CROSS-MATERIAL SEPARATION — does the Listener role have an alphabet?")
        print("=" * 92)
        print("  §12: 구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다.")
        print("       아트 결정이 아니라 시스템 결정이다.")
        print(f"  Requirement applied: every surface pair >= {SEPARATION_MIN}x spectral centroid.")

    overall = {s: geo_mean(by_surface[s]) for s in SURFACES if by_surface[s]}
    worst_overall, worst_pair = (float("nan"), ("", ""))
    if len(overall) == len(SURFACES):
        if quiet:
            _, worst_overall, worst_pair = ratio_matrix(overall)
        else:
            worst_overall, worst_pair = print_matrix(
                "[2a] Headline 5x5 — all 60 clips, geometric mean per surface",
                overall,
                note="12 clips per surface (3 actors x 4 variants)",
            )
        if worst_overall < SEPARATION_MIN:
            audit.add(BLOCKING, "§12", f"{worst_pair[0]} vs {worst_pair[1]}",
                      f"surface centroids within {worst_overall:.2f}x (need >= {SEPARATION_MIN}x). "
                      f"These two floors are one symbol to a player, so the Listener cannot read "
                      f"the monster's zone from them and §04's 목표 관여 (진입 타이밍을 결정한다) "
                      f"has no basis")

    per_actor: dict[str, dict] = {}
    for a in ACTORS:
        cents = {s: geo_mean(by_pair.get((s, a), [])) for s in SURFACES}
        if any(v <= 0 for v in cents.values()):
            continue
        if quiet:
            _, w, p = ratio_matrix(cents)
        else:
            w, p = print_matrix(
                f"[2b] Within one actor: {a}", cents,
                note="what a player actually compares — the monster only ever makes monster_step",
            )
        per_actor[a] = dict(centroids={k: round(v, 1) for k, v in cents.items()},
                            worst_ratio=round(w, 3), worst_pair=list(p))
        if w < SEPARATION_MIN:
            audit.add(BLOCKING, "§12", f"{a}: {p[0]} vs {p[1]}",
                      f"within a single actor the two surfaces are {w:.2f}x apart "
                      f"(need >= {SEPARATION_MIN}x). This is the comparison the Listener makes, "
                      f"so it is stricter evidence than the headline matrix")

    # Clip level: the darkest single clip of the brighter surface against the
    # brightest single clip of the darker one, adjacent pairs, within one actor.
    clip_rows = []
    clip_worst, clip_worst_label = float("inf"), ""
    for a in ACTORS:
        cents = {s: geo_mean(by_pair.get((s, a), [])) for s in SURFACES}
        if any(v <= 0 for v in cents.values()):
            continue
        order = sorted(cents, key=lambda k: cents[k])
        for i in range(len(order) - 1):
            dark, bright = order[i], order[i + 1]
            r = min(by_pair[(bright, a)]) / max(by_pair[(dark, a)])
            clip_rows.append((a, f"{dark} / {bright}", r))
            if r < clip_worst:
                clip_worst, clip_worst_label = r, f"{a}: {dark} / {bright}"
    if not quiet:
        print()
        print("[2c] Clip-level worst case — a single step, not an average")
        print("     (darkest clip of the brighter surface vs brightest clip of the darker)")
        print()
        print(f"  {'actor':<15}{'adjacent pair':<24}{'ratio':>8}")
        print("  " + "-" * 47)
        for a, pair_s, r in clip_rows:
            flag = "" if r >= 1.0 else "   ← overlap"
            print(f"  {a:<15}{pair_s:<24}{r:>8.2f}{flag}")
        print(f"  worst: {clip_worst:.2f}x  ({clip_worst_label})")
    if clip_worst < 1.0:
        audit.add(WARN, "§12", clip_worst_label,
                  f"individual clips overlap in centroid ({clip_worst:.2f}x): the brightest "
                  f"variant of the darker surface measures brighter than the darkest variant of "
                  f"the brighter one, so one isolated step can be misread even though the "
                  f"per-surface means are separated")

    secondary = {}
    if not quiet:
        print()
        print("[2d] Second and third axes — §12 asks for 울림 (metal) and 둔탁 (concrete),")
        print("     which is decay, and 부스럭 (gravel), which is noise-vs-pitch")
        print()
        print(f"  {'surface':<11}{'centroid Hz':>13}{'ring ms':>10}{'flatness':>10}"
              f"{'rms dBFS':>10}{'dur ms':>9}")
        print("  " + "-" * 65)
    for s in sorted(overall, key=lambda k: overall[k]):
        secondary[s] = dict(centroid_hz=round(overall[s], 1),
                            ring_ms=round(geo_mean(rings[s]), 1),
                            flatness=round(geo_mean(flats[s]), 4),
                            rms_db=round(synth.gain_to_db(max(geo_mean(rms_by_surface[s]), 1e-12)), 2),
                            duration_ms=round(geo_mean(dur_by_surface[s]) * 1000.0, 1))
        if not quiet:
            d = secondary[s]
            print(f"  {s:<11}{d['centroid_hz']:>13.0f}{d['ring_ms']:>10.0f}"
                  f"{d['flatness']:>10.3f}{d['rms_db']:>10.1f}{d['duration_ms']:>9.0f}")

    # Ring time is the cue that survives occlusion best; check it separates too.
    ring_vals = {s: secondary[s]["ring_ms"] for s in secondary}
    if len(ring_vals) == len(SURFACES):
        _, ring_worst, ring_pair = ratio_matrix(ring_vals)
        if not quiet:
            print(f"  ring-time worst pair: {ring_pair[0]} vs {ring_pair[1]} = {ring_worst:.2f}x")
        if ring_worst < 1.2:
            audit.add(INFO, "§12", f"{ring_pair[0]} vs {ring_pair[1]}",
                      f"ring times only {ring_worst:.2f}x apart — the decay cue does not help "
                      f"separate this pair, so it rests on centroid alone")

    occluded = check_occluded_separation(folder, audit, quiet)

    return dict(occluded=occluded,
                per_clip=per_clip,
                headline=dict(centroids={k: round(v, 1) for k, v in overall.items()},
                              worst_ratio=round(worst_overall, 3) if worst_overall == worst_overall else None,
                              worst_pair=list(worst_pair)),
                per_actor=per_actor,
                clip_level_worst=round(clip_worst, 3) if clip_worst < float("inf") else None,
                clip_level_worst_pair=clip_worst_label,
                secondary_axes=secondary,
                missing=missing)


def welch_power_centroid(path: str) -> float:
    """Power-weighted spectral centroid over a Welch-averaged spectrum.

    There are three defensible spectral centroids in this codebase and they
    disagree by up to 10x, so a cross-check has to name which one it is using or
    it will invent defects. `synth.analyse` is magnitude-weighted over one
    whole-clip Hann FFT — fine as a house report number, but it sums one term per
    bin, so tens of thousands of near-empty HF bins outvote the signal: it reads
    the monster presence bed at 309 Hz when the bed's power centroid is 32 Hz.
    The Monster manifest is written in the Welch power-weighted convention, so
    this reproduces that exactly (8192-sample Hann segments, 50% overlap, mean
    averaging) and compares like with like.
    """
    from scipy import signal

    x, sr = synth.read_wav(path)
    nperseg = int(min(8192, len(x)))
    freqs, pw = signal.welch(x.astype(np.float64), fs=sr, window="hann", nperseg=nperseg,
                             noverlap=nperseg // 2, scaling="spectrum", average="mean")
    total = float(np.sum(pw))
    return float(np.sum(freqs * pw) / total) if total > 0 else 0.0


#: Low-pass corners standing in for how far away and how occluded the monster is
#: when the Listener is actually using the cue. §12 puts 시야 차단 지점 간격 at
#: 15~25m and caps a straight corridor at 20m, so the useful range is a monster
#: you cannot see, one or two corners off. Air and material absorption are both
#: low-pass, so a single corner frequency per condition is a fair first model.
OCCLUSION_CASES: tuple[tuple[str, float | None], ...] = (
    ("dry / same room", None),
    ("~15m, one corner", 2000.0),
    ("~25m, through wall", 800.0),
)


def check_occluded_separation(folder: str, audit: Audit, quiet: bool) -> dict:
    """Does the §12 alphabet survive the distance it is used at?

    This is the check no generator can run on itself, and the one most likely to
    matter. Every generator measures its clips dry. The Listener never hears them
    dry: §04 gives the role 소리로 괴물의 위치 · 거리 · 이동 방향을 파악, and the
    information is only *worth* anything while the monster is still far enough
    away to act on — which is exactly when the high frequencies are gone.

    Separation carried by a bright, high-centroid surface is fragile under a
    low-pass in a way separation carried by a low one is not: two surfaces at
    2.7 kHz and 6.2 kHz both collapse toward the filter corner, while 240 Hz and
    570 Hz barely move. So a matrix that passes dry can still fail at range, and
    a role that only works when the monster is already on top of you is not the
    role §04 describes.
    """
    names = [f"step_{s}_monster_step_{v:02d}.wav" for s in SURFACES for v in range(1, 5)]
    paths = {n: os.path.join(folder, n) for n in names if os.path.exists(os.path.join(folder, n))}
    if len(paths) < len(names):
        return {}

    cache = {n: synth.read_wav(p) for n, p in paths.items()}
    results: dict[str, dict] = {}

    if not quiet:
        print()
        print("[2e] Does the alphabet survive the range it is used at?")
        print("     Every generator measured these dry. The Listener hears the monster")
        print("     through walls, and §12 caps a straight corridor at 20m — so the cue")
        print("     is read at 15~25m, one or two corners off, with the top end gone.")
        print()
        print(f"  {'condition':<22}" + "".join(f"{s[:8]:>9}" for s in SURFACES)
              + f"{'worst':>8}  pair")
        print("  " + "-" * 88)

    for label, cutoff in OCCLUSION_CASES:
        cents: dict[str, list[float]] = {s: [] for s in SURFACES}
        for n, (x, sr) in cache.items():
            surface = n.split("_")[1]
            y = synth.lowpass(x, cutoff, order=2, sr=sr) if cutoff else x
            mag = np.abs(np.fft.rfft(y.astype(np.float64) * np.hanning(len(y))))
            freqs = np.fft.rfftfreq(len(y), 1.0 / sr)
            total = float(np.sum(mag))
            cents[surface].append(float(np.sum(freqs * mag) / total) if total > 0 else 0.0)
        means = {s: geo_mean(v) for s, v in cents.items()}
        _, worst, pair = ratio_matrix(means)
        results[label] = dict(cutoff_hz=cutoff,
                              centroids={k: round(v, 1) for k, v in means.items()},
                              worst_ratio=round(worst, 3), worst_pair=list(pair))
        if not quiet:
            order = SURFACES
            print(f"  {label:<22}" + "".join(f"{means[s]:>9.0f}" for s in order)
                  + f"{worst:>8.2f}  {pair[0]} vs {pair[1]}"
                  + ("" if worst >= SEPARATION_MIN else "   ← FAIL"))

    hardest = results.get("~25m, through wall")
    if hardest and hardest["worst_ratio"] < SEPARATION_MIN:
        p = hardest["worst_pair"]
        audit.add(WARN, "§12", f"{p[0]} vs {p[1]} (occluded)",
                  f"the pair separates by {hardest['worst_ratio']:.2f}x once low-passed to "
                  f"800 Hz, below the {SEPARATION_MIN}x requirement. Dry separation passes, so "
                  f"this is not a generator bug — it is a range limit on the Listener cue, and "
                  f"it has to be answered in the Unity mix (occlusion filter strength, 3D "
                  f"rolloff) or by pushing these two surfaces apart in the low end rather than "
                  f"the high end")
    if not quiet:
        print("  low-pass order 2, one corner per condition; air and wall absorption are")
        print("  both low-pass, so this is a first-order model, not a room simulation.")
    return results


GAME_CONSTANTS = os.path.join(REPO, "unity", "HorrorGame", "Assets", "Scripts",
                              "Core", "GameConstants.cs")


def a_weighting_db(freqs: np.ndarray) -> np.ndarray:
    """IEC 61672 A-weighting curve, dB.

    Raw energy is the wrong scale for "can the player hear this". A-weighting is
    down about 26 dB at 60 Hz and up around 3 kHz, so an unweighted comparison
    flatters a low-frequency thud and punishes a bright scuff — the exact
    distinction that decides whether concrete or gravel gives the monster away.
    """
    f = np.maximum(np.asarray(freqs, dtype=np.float64), 1e-6)
    f2 = f * f
    ra = ((12194.0 ** 2) * f2 ** 2) / (
        (f2 + 20.6 ** 2)
        * np.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2))
        * (f2 + 12194.0 ** 2)
    )
    return 20.0 * np.log10(ra) + 2.0


def read_clarity_constants() -> dict[str, float]:
    """Pulls GameConstants.ListenerClarity* out of the C#.

    Read rather than hardcoded so this check keeps telling the truth after
    somebody retunes either side.
    """
    if not os.path.exists(GAME_CONSTANTS):
        return {}
    src = open(GAME_CONSTANTS, encoding="utf-8").read()
    out: dict[str, float] = {}
    for s in SURFACES:
        m = re.search(rf"ListenerClarity{s.capitalize()}\s*=\s*([0-9.]+)f", src)
        if m:
            out[s] = float(m.group(1))
    return out


def check_clarity_vs_audio(audit: Audit, quiet: bool) -> dict:
    """Does the audio agree with the number the HUD shows?

    `ListenerAbility` does not analyse the audio. It computes a fix whose error
    radius comes from `GameConstants.ListenerClarity*` — a hand-authored value per
    floor material — and hands the player an estimate. The player also *hears* the
    footsteps. So there are two independent channels claiming to answer the same
    question, and nothing in either the generators or the C# tests compares them.

    If they disagree, the failure is worse than either being wrong alone: the HUD
    says the fix on gravel is tight while the player's ears get nothing, or it
    says concrete is hopeless while concrete is the one they can actually hear.
    The player learns to distrust the ability, which is the whole role.

    `ListenerAbility` is explicit that this is heard through walls — "Sound is not
    blocked by geometry, so this never asks HasLineOfSight — hearing through a
    wall is the whole point of the role" — so the comparison is run occluded as
    well as dry, and the cutoff is swept rather than assumed, because a
    conclusion that only holds at one arbitrary corner frequency is not a
    conclusion.
    """
    clarity = read_clarity_constants()
    folder = os.path.join(AUDIO, "Footsteps")
    if not clarity or len(clarity) != len(SURFACES):
        audit.add(WARN, "consistency", "GameConstants.cs",
                  "could not read all five ListenerClarity constants — the audio cannot be "
                  "checked against the number the HUD will show")
        return {}

    clips: dict[str, list[tuple[np.ndarray, int]]] = {}
    for s in SURFACES:
        got = []
        for v in range(1, 5):
            p = os.path.join(folder, f"step_{s}_monster_step_{v:02d}.wav")
            if os.path.exists(p):
                got.append(synth.read_wav(p))
        if not got:
            return {}
        clips[s] = got

    def audibility(cutoff: float | None) -> dict[str, float]:
        out: dict[str, float] = {}
        for s, group in clips.items():
            vals = []
            for x, sr in group:
                y = synth.lowpass(x, cutoff, order=2, sr=sr) if cutoff else x
                mag = np.abs(np.fft.rfft(y.astype(np.float64)))
                fr = np.fft.rfftfreq(len(y), 1.0 / sr)
                w = 10.0 ** (a_weighting_db(fr) / 20.0)
                vals.append(float(np.sum((mag * w) ** 2)))
            out[s] = 10.0 * np.log10(max(float(np.mean(vals)), 1e-30))
        return out

    def ranks(d: dict[str, float]) -> dict[str, int]:
        order = sorted(d, key=lambda k: d[k])
        return {k: i for i, k in enumerate(order)}

    def spearman(a: dict[str, int], b: dict[str, int]) -> float:
        n = len(a)
        d2 = sum((a[k] - b[k]) ** 2 for k in a)
        return 1.0 - 6.0 * d2 / (n * (n * n - 1))

    clarity_rank = ranks(clarity)
    sweep = [None, 4000.0, 3000.0, 2000.0, 1500.0, 1000.0, 800.0, 600.0]
    table: dict[str, dict] = {}

    if not quiet:
        print()
        print("=" * 92)
        print("[6] HUD vs EARS — GameConstants.ListenerClarity* against measured audibility")
        print("=" * 92)
        print("  ListenerAbility hands the player an error radius derived from a hand-authored")
        print("  clarity per material. The player also hears the footsteps. Nothing else in the")
        print("  repo compares the two, and hearing through walls is stated to be the point of")
        print("  the role, so the comparison is run at several degrees of occlusion.")
        print()
        print("  clarity (GameConstants): "
              + "  ".join(f"{s}={clarity[s]:.2f}" for s in sorted(clarity, key=lambda k: clarity[k])))
        print()
        order = sorted(SURFACES, key=lambda k: -clarity[k])
        print(f"  {'occlusion':<20}" + "".join(f"{s[:8]:>10}" for s in order) + f"{'rho':>7}")
        print("  " + "-" * 79)

    for cutoff in sweep:
        aud = audibility(cutoff)
        loudest = max(aud.values())
        rho = spearman(clarity_rank, ranks(aud))
        label = "dry / same room" if cutoff is None else f"low-pass {cutoff:.0f} Hz"
        table[label] = dict(cutoff_hz=cutoff,
                            a_weighted_db={k: round(v - loudest, 2) for k, v in aud.items()},
                            spearman=round(rho, 3),
                            audibility_rank=sorted(aud, key=lambda k: aud[k]))
        if not quiet:
            order = sorted(SURFACES, key=lambda k: -clarity[k])
            print(f"  {label:<20}" + "".join(f"{aud[s] - loudest:>10.1f}" for s in order)
                  + f"{rho:>7.2f}")

    if not quiet:
        print("  dB are A-weighted energy relative to the loudest surface at that cutoff.")
        print("  Columns are ordered by the clarity the code claims, brightest-claim first, so")
        print("  a monotonically falling row means the audio agrees with the HUD.")
        print("  rho = Spearman rank correlation between claimed clarity and measured audibility.")

    # An inversion that only appears under occlusion is the interesting kind: it
    # means the surface's audibility lives in a band that a wall removes.
    inversions: list[tuple[str, str, str, float]] = []
    for label, row in table.items():
        aud = row["a_weighted_db"]
        for a, b in [(x, y) for i, x in enumerate(SURFACES) for y in SURFACES[i + 1:]]:
            claim = clarity[a] - clarity[b]
            heard = aud[a] - aud[b]
            if claim > 0.05 and heard < -3.0:
                inversions.append((label, a, b, heard))
            elif claim < -0.05 and heard > 3.0:
                inversions.append((label, b, a, -heard))

    worst_by_pair: dict[tuple[str, str], tuple[str, float]] = {}
    for label, a, b, heard in inversions:
        key = (a, b)
        if key not in worst_by_pair or heard < worst_by_pair[key][1]:
            worst_by_pair[key] = (label, heard)

    if not quiet and worst_by_pair:
        print()
        print("  INVERSIONS — the code claims one surface gives the monster away more than")
        print("  another, and the audio says the opposite by more than 3 dB:")
        for (a, b), (label, heard) in sorted(worst_by_pair.items(), key=lambda kv: kv[1][1]):
            print(f"    {a} (clarity {clarity[a]:.2f}) vs {b} (clarity {clarity[b]:.2f}): "
                  f"{a} measures {abs(heard):.1f} dB QUIETER at {label}")

    for (a, b), (label, heard) in worst_by_pair.items():
        dry = table["dry / same room"]["a_weighted_db"]
        dry_agrees = (dry[a] - dry[b]) > 0.0
        audit.add(BLOCKING if abs(heard) > 12.0 else WARN, "consistency", f"{a} vs {b}",
                  f"GameConstants says {a} (clarity {clarity[a]:.2f}) gives the monster away more "
                  f"than {b} (clarity {clarity[b]:.2f}), but {a} measures {abs(heard):.1f} dB "
                  f"quieter than {b} at {label}"
                  + (f". Dry, the two agree — so {a}'s audibility lives in a band a wall removes, "
                     f"and ListenerAbility is explicit that the role hears through walls"
                     if dry_agrees else
                     f". They disagree dry as well, so this is not an occlusion artefact"))

    return dict(clarity=clarity, sweep=table,
                inversions=[dict(pair=list(k), condition=v[0], delta_db=round(v[1], 2))
                            for k, v in worst_by_pair.items()])


def check_monster_manifest(audit: Audit, quiet: bool) -> None:
    """The Monster generator wrote a manifest. Cross-check it against disk.

    A generator that reports a clip it did not write is the failure mode no
    self-check catches, because the self-check runs on the in-memory buffer.
    """
    mpath = os.path.join(AUDIO, "Monster", "monster_audio.manifest.json")
    if not os.path.exists(mpath):
        return
    man = json.load(open(mpath))
    clips = man.get("clips", {})
    folder = os.path.dirname(mpath)
    on_disk = {f for f in os.listdir(folder) if f.endswith(".wav")}
    if not quiet:
        print()
        print("=" * 92)
        print("[1b] MANIFEST vs DISK — Monster")
        print("=" * 92)
    for name, meta in sorted(clips.items()):
        if name not in on_disk:
            audit.add(BLOCKING, "inventory", f"Monster/{name}",
                      "manifest reports this clip but it is not on disk")
            continue
        cpath = os.path.join(folder, name)
        r = synth.analyse(cpath)
        for field_name, measured, tol in (("seconds", r.seconds, 0.02),
                                          ("centroid_hz", welch_power_centroid(cpath), 0.05),
                                          ("peak_db", r.peak_db, 0.15)):
            if field_name not in meta:
                continue
            claimed = float(meta[field_name])
            denom = max(abs(claimed), 1e-6)
            if abs(measured - claimed) / denom > tol:
                audit.add(WARN, "inventory", f"Monster/{name}",
                          f"manifest claims {field_name}={claimed}, measured {measured:.2f}")
    for extra in sorted(on_disk - set(clips)):
        audit.add(WARN, "inventory", f"Monster/{extra}", "on disk but absent from the manifest")
    if not quiet:
        print(f"  manifest lists {len(clips)} clips, {len(on_disk)} wav on disk — "
              f"{'consistent' if len(clips) == len(on_disk) else 'MISMATCH'}")
    ch = man.get("channels")
    if ch is not None and ch != 1:
        audit.add(BLOCKING, "channels", "Monster/manifest",
                  f"manifest declares channels={ch}; monster audio is positional and must be mono")


# ── Main ────────────────────────────────────────────────────────────────────


def main(argv: Sequence[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--json", metavar="PATH", help="write machine-readable results here")
    ap.add_argument("--quiet", action="store_true", help="only print defects and the verdict")
    args = ap.parse_args(argv)
    quiet = args.quiet

    audit = Audit()

    if not quiet:
        print("=" * 92)
        print("CROSS-FAMILY AUDIO AUDIT")
        print("=" * 92)
        print(f"  root: {AUDIO}")
        print("  every check here spans families; per-clip checks live in the generators")
        print()
        print("=" * 92)
        print("[1] INVENTORY")
        print("=" * 92)

    by_family = collect(audit)
    records = check_inventory(by_family, audit, quiet)
    check_monster_manifest(audit, quiet)
    material = check_material_matrix(audit, quiet)
    loops = check_loops(records, audit, quiet)
    check_levels(records, audit, quiet)
    check_channel_policy(records, audit, quiet)
    clarity = check_clarity_vs_audio(audit, quiet)

    print()
    print("=" * 92)
    print("DEFECTS")
    print("=" * 92)
    if not audit.defects:
        print("  none")
    for sev in (BLOCKING, WARN, INFO):
        group = [d for d in audit.defects if d.severity == sev]
        if not group:
            continue
        print()
        print(f"  {sev} ({len(group)})")
        for d in group:
            print(f"    [{d.check}] {d.target}")
            print(f"        {d.detail}")

    print()
    print("=" * 92)
    print("VERDICT")
    print("=" * 92)
    h = material["headline"]
    worst = h["worst_ratio"]
    if worst is None:
        print("  §12 Listener alphabet: CANNOT ASSESS — footstep clips missing")
    elif worst >= SEPARATION_MIN:
        print(f"  §12 Listener alphabet: SUPPORTED — worst surface pair "
              f"{h['worst_pair'][0]} vs {h['worst_pair'][1]} at {worst:.2f}x "
              f"(need >= {SEPARATION_MIN}x)")
        per_actor_worst = min((v["worst_ratio"] for v in material["per_actor"].values()),
                              default=float("nan"))
        print(f"  worst within a single actor: {per_actor_worst:.2f}x")
    else:
        print(f"  §12 Listener alphabet: BROKEN — {h['worst_pair'][0]} vs {h['worst_pair'][1]} "
              f"at {worst:.2f}x, below the {SEPARATION_MIN}x requirement. The role does not "
              f"function. Retune gen_footsteps.py before this ships.")
    occ = material.get("occluded", {}).get("~25m, through wall")
    if occ:
        p = occ["worst_pair"]
        state = "holds" if occ["worst_ratio"] >= SEPARATION_MIN else "does NOT hold"
        print(f"  at 25m through a wall it {state}: worst pair {p[0]} vs {p[1]} at "
              f"{occ['worst_ratio']:.3f}x")
    inv = clarity.get("inversions") if clarity else None
    if inv:
        pairs = ", ".join(f"{i['pair'][0]}/{i['pair'][1]}" for i in inv)
        print(f"  HUD vs ears: {len(inv)} inverted pair(s) — {pairs}. The clarity the code "
              f"shows the")
        print(f"  player and the loudness their ears get disagree on these, which is a worse")
        print(f"  failure than either being wrong alone.")
    elif clarity:
        print("  HUD vs ears: GameConstants clarity ladder agrees with measured audibility")
    print(f"  clips: {len(records)}   loops checked: {len(loops)}   "
          f"blocking defects: {len(audit.blocking)}   warnings: {len(audit.warnings)}")
    print(f"  RESULT: {'FAIL' if audit.blocking else 'PASS'}")

    if args.json:
        json.dump(dict(records=records, material=material, loops=loops, clarity=clarity,
                       defects=[d.__dict__ for d in audit.defects],
                       separation_min=SEPARATION_MIN,
                       result="FAIL" if audit.blocking else "PASS"),
                  open(args.json, "w"), indent=2, ensure_ascii=False)
        print(f"  json → {args.json}")

    return 1 if audit.blocking else 0


if __name__ == "__main__":
    raise SystemExit(main())
