#!/usr/bin/env python3
"""깜짝 — the startle stinger set, procedural generation.

Run:  tools/audio/.venv/bin/python tools/audio/gen_scares.py
Out:  unity/HorrorGame/Assets/Audio/Startle/*.wav   (48 kHz, 16-bit PCM, MONO)

Everything here is synthesised from `synth.py`. Nothing is sampled or licensed,
which is a shipping requirement, not a style choice (see synth.py's header).

WHAT A 깜짝 IS, AND WHAT IT IS FORBIDDEN TO BE
──────────────────────────────────────────────
A startle is the building lying to one player. It is triggered and rendered
locally, per player, from seeded fittings placed in the map, and it NEVER calls
MonsterAgent.ReportSound — §12 makes sound the map, so a placed noise would be a
forged footstep dropped by something that can see the whole building. That is
the exact system the pivot deleted (the §09 block in GameConstants.cs records
the reasoning), and this set exists on the condition that it stays a lie told
only to your own ears.

Two design consequences fall out of that and are enforced below by measurement:

* A 깜짝 must never be mistakable for the game's real information channels.
  Not a footstep cadence (the skitter is 6-8 taps inside ~0.65 s — far above
  any gait rate), not a creature call (the glimpse exhale is band-limited
  breath, no voiced pitch contour). The fittings sound like the *building*:
  sheet metal, steam, something small, and once, the dark itself.
* A 깜짝 must never outrank a true threat cue in the mix. It is a false alarm
  by construction — nothing real happened. If the fake ever lands harder than
  the real thing, the player learns the top of the mix can be ignored, and §12's
  sound-as-map is poisoned at the reading end even though the creature side was
  never touched. `verify_false_alarm_ceiling` pins this against the written
  Items files, loudest true threat cues first.

WHAT EACH SOUND IS FOR
──────────────────────
* `stl_cabinet_slam_01/02` — a locker fitting slams as the runner passes. Sharp
  strike, sheet-panel body ringing near 180 Hz, and two chipped-paint tinks
  landing in the ring-down — the flakes the slam shook loose. Two takes with
  different panel modes and tink timing, because a startle that repeats
  identically is a sound effect, not a building.
* `stl_pipe_vent_01` — a steam fitting lets go: hard attack, 0.9 s of broadband
  hiss with pipe-bore resonance, a low rumble under it, then the pressure tail
  falls in level and brightness as the line empties. One take: steam has no
  memory, and the trigger fires once per player per match anyway.
* `stl_skitter_01/02` — something small crosses ahead: a 7- and an 8-tap
  accelerando of short filtered noise bursts, centre frequency rising as it
  "passes". The clip is MONO — it ships positional and Unity pans it; the
  passing lives in time and pitch, not in a baked stereo image.
* `stl_glimpse_01` — the figure reveal. NOT a shriek: a low sub swell (46-57 Hz,
  1.4 s) with a breath-like filtered exhale over it, cut to true silence.
  §06: 침묵이 가장 무서운 소리다 — the scare is the cut, and what it cuts *to*
  is the game's own weapon.

NUMBERS, AND WHERE THEY COME FROM (repo rule: every tuned number carries its
derivation)
──────────────────────────────────────────────────────────────────────────────
* PACING IS NOT HERE. Trigger placement, per-player once-per-match, cooldown
  and the race-start grace all live in the C# integrator per the integrator's
  decisions (a)-(c). This file owns only what the clips themselves are.
* DURATION CAP 1.55 s (longest clip, the glimpse). §01's race is 12-20 minutes
  of continuous listening across eight storeys, and §12 makes the listening the
  map: while a stinger plays at your position it masks your own step-reading.
  A 깜짝 is punctuation in that stream, not a scene — so the whole set stays
  under the Items family's own one-shot ceiling (1.75 s), the skitters end
  inside a second, and nothing except the glimpse crosses 1.5 s.
* MONO, ALWAYS. Every clip is positional (a fitting at a place in the maze).
  Unity will not spatialise a stereo clip — it would play unattenuated from
  everywhere, which for a *startle* means a jump scare with no source to turn
  toward. The audit enforces this (verify_audio.py §[5]); `emit` refuses first.
* PEAK CEILING −5.5 dBFS, SET UNDER THE QUIETEST TRUE THREAT CUE THAT SHIPS.
  Measured off the built files: the creature's roar peaks at −3.0, its grab at
  −4.5/−4.6, and a door bolt closing a route at −4.0/−4.5. The false alarm
  must jolt — so it sits 1.5 dB above the ordinary door interactions (−7..
  −8.5) — but must never outrank truth, so its ceiling stays ≥0.75 dB under
  the quietest of those cues (`verify_false_alarm_ceiling` re-measures them
  from disk every run). Also well inside the task's hard cap of −1 dBFS peak.
* And every clip carries no DC and leaves headroom: `emit` runs
  `synth.assert_usable` plus this file's own stricter peak assert.

LEVEL LADDER
────────────
Peak dBFS per clip, one table so the mix is a decision instead of an accident.
Ordered loudest first; the derivations are the ladder.
"""

from __future__ import annotations

import hashlib
import os
import re
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import synth  # noqa: E402
from synth import Mode  # noqa: E402

SR = synth.SAMPLE_RATE

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "unity", "HorrorGame", "Assets", "Audio", "Startle",
)

ITEMS_DIR = os.path.join(os.path.dirname(OUT_DIR), "Items")
MONSTER_DIR = os.path.join(os.path.dirname(OUT_DIR), "Monster")
"""Read-only references: the true threat cues the ladder must stay under."""

SEED = 24_601
"""Base seed. Fixed offsets per clip, so a rebuild is byte-identical."""

LEVELS = {
    # The slam is the set's ceiling: −5.5 sits ≥0.89 dB under the quietest true
    # threat cue that ships (monster_grab_02 measures −4.61; door_lock_02
    # −4.50) and 1.5 dB over the ordinary door interactions (−7..−8.5). A
    # false alarm that jolts without ever outranking truth — see the header.
    "stl_cabinet_slam_01": -5.5,
    "stl_cabinet_slam_02": -6.0,
    # Sustained hiss reads louder than its peak suggests (high RMS), so the
    # vent gives back 1.5 dB of peak against the slam and still lands second
    # in perceived level. Pinned: vent RMS > skitter RMS in verify_family_mix.
    "stl_pipe_vent_01": -7.0,
    # A small body at floor level. Its startle value is temporal (the
    # accelerando), not level — it sits in the Items ordinary-interaction band
    # (−7..−8.5 there) so it stays deniable: "쥐였나?" is the intended read.
    "stl_skitter_01": -9.0,
    "stl_skitter_02": -9.5,
    # Dread, not a klaxon. §06 makes silence the weapon and this clip's scare
    # is the cut INTO that silence, so the swell itself stays down with the
    # quiet Items clips (the glass pickup sits at −12 there). The 46-57 Hz sub is
    # felt more than heard; peak level is not what carries it.
    "stl_glimpse_01": -12.0,
}

REPORTS: list[synth.ClipReport] = []
ROSTER: list[tuple[str, str]] = []


# ── Local helpers (nothing here duplicates synth.py) ────────────────────────


def _nn(seconds: float) -> int:
    return synth.n_samples(seconds, SR)


def norm(buf: np.ndarray) -> np.ndarray:
    """Peak-normalises to 1.0 without touching headroom. Public stand-in for
    synth's private `_safe_norm`, used while building intermediate buffers."""
    return synth.normalize(buf, 0.0)


def ctrl(seconds: float, seed: int, hz: float) -> np.ndarray:
    """A smooth random control curve in roughly [-1, 1]. Same idiom as
    gen_items: low-passed white noise, renormalised."""
    y = synth.lowpass(synth.white(seconds, seed, SR), hz, order=2, sr=SR)
    peak = float(np.max(np.abs(y)))
    return (y / (peak if peak > 1e-9 else 1.0)).astype(np.float32)


def env_of(x: np.ndarray, ms: float = 2.0) -> np.ndarray:
    """Rectified-and-smoothed amplitude envelope, for measurement."""
    win = max(1, int(ms / 1000.0 * SR))
    return np.convolve(np.abs(x.astype(np.float64)), np.ones(win) / win, mode="same")


def onsets(x: np.ndarray, rel: float = 0.22, min_gap: float = 0.030,
           ms: float = 1.5) -> list[float]:
    """Times (s) where the envelope pops above `rel` of its own max, one hit
    per `min_gap` window. The measuring half of the skitter and the tinks."""
    e = env_of(x, ms)
    th = float(e.max()) * rel
    g = max(1, int(min_gap * SR))
    hits: list[float] = []
    i = 0
    while i < len(e):
        if e[i] >= th:
            j = i + int(np.argmax(e[i:i + g]))
            hits.append(j / SR)
            i = j + g
        else:
            i += 1
    return hits


def band_noise(seconds: float, seed: int, lo: float, hi: float,
               kind: str = "white") -> np.ndarray:
    src = {"white": synth.white, "pink": synth.pink, "brown": synth.brown}[kind]
    return synth.bandpass(src(seconds, seed, SR), lo, hi, order=2, sr=SR)


def impact(modes: list[Mode], seconds: float, seed: int, noise: float = 0.35,
           noise_tau: float = 0.012) -> np.ndarray:
    return synth.modal_impact(modes, seconds, seed, noise_amount=noise,
                              noise_tau=noise_tau, sr=SR)


def magnitude_centroid(x: np.ndarray) -> float:
    """Magnitude-weighted spectral centroid of a buffer (house convention)."""
    if len(x) <= 8:
        return 0.0
    mag = np.abs(np.fft.rfft(x.astype(np.float64) * np.hanning(len(x))))
    freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
    total = float(np.sum(mag))
    return float(np.sum(freqs * mag) / total) if total > 0 else 0.0


LEAD_IN = 0.006
"""Silence prepended to every clip.

`synth.write_wav` fades the first 2 ms unconditionally — right in general, but
every clip here except the glimpse *opens* on its transient, and a fade over
the strike both softens the edge the spectrogram review demands and pulls the
measured peak under the LEVELS target (gen_items measured up to 3.1 dB of
loss). 6 ms of lead-in puts the fade on silence instead. Nothing here loops,
so there is no seam to protect.
"""


def emit(name: str, buf: np.ndarray, note: str) -> synth.ClipReport:
    """Writes, measures, and refuses to ship anything unusable — or stereo."""
    if name not in LEVELS:
        raise KeyError(f"{name}: add a peak level to LEVELS before emitting it")
    if not re.fullmatch(r"stl_[a-z_]+_\d\d", name):
        raise ValueError(f"{name}: startle names are stl_<stem>_<take>, lowercase")
    buf = synth.concat(synth.silence(LEAD_IN, SR), buf)
    path = os.path.join(OUT_DIR, name + ".wav")
    synth.write_wav(path, buf, sr=SR, headroom_db=LEVELS[name], stereo=False)
    r = synth.assert_usable(path)
    if r.channels != 1:
        raise AssertionError(
            f"{path}: {r.channels} channels — every 깜짝 is positional and Unity "
            f"will not spatialise stereo; a startle with no source direction is "
            f"a blocking audit defect, not a mix choice")
    if r.peak_db > -1.0:
        raise AssertionError(f"{path}: peak {r.peak_db:.2f} dBFS breaks the "
                             f"task's hard −1 dBFS ceiling")
    REPORTS.append(r)
    ROSTER.append((name, note))
    return r


# ── stl_cabinet_slam — sheet metal, ~180 Hz body, two paint tinks ───────────


# Two structurally different panels, not one panel at two seeds: variant
# correlation is checked at the same 0.60 ceiling gen_items earned on its
# most-repeated clip. The fundamental stays inside 150-215 Hz because the task
# names ~180 Hz as the body and `verify_slam` measures it off the written file.
SLAM_PANEL = {
    0: [Mode(181, 0.300, 1.00), Mode(288, 0.190, 0.55), Mode(454, 0.115, 0.42),
        Mode(742, 0.065, 0.28), Mode(1305, 0.032, 0.16), Mode(2710, 0.014, 0.09)],
    1: [Mode(176, 0.260, 1.00), Mode(305, 0.210, 0.68), Mode(438, 0.090, 0.30),
        Mode(801, 0.050, 0.30), Mode(1466, 0.028, 0.13), Mode(2380, 0.016, 0.11)],
}

SLAM_SHAPE = {
    # variant: (tink times, tink gains, tink pitch, frame boom gain)
    0: ((0.34, 0.55), (0.17, 0.12), 1.00, 0.34),
    1: ((0.30, 0.61), (0.14, 0.16), 1.12, 0.30),
}


def panel_ring(modes: list[Mode], seconds: float, seed: int) -> np.ndarray:
    """A struck panel with phase-ALIGNED modes: every mode starts at its crest.

    Not `synth.modal_impact`, and deliberately so: modal_impact randomises the
    phase of each mode (right for a footstep, where the strike point wanders),
    which lets the summed ring drift into a *later* constructive peak. Round 1
    measured the slam's loudest instant 26 ms after contact — a bloom, not a
    slam. A door hitting its frame excites every mode at the same instant from
    the same impulse, so here the phases are locked to the contact and the
    envelope's maximum is physically pinned to t=0.
    """
    n = _nn(seconds)
    t = np.arange(n, dtype=np.float64) / SR
    g = synth.rng(seed)
    out = np.zeros(n, dtype=np.float64)
    for m in modes:
        f = m.freq * (1.0 + float(g.normal(0.0, 0.003)))
        out += m.amp * np.cos(2.0 * np.pi * f * t) * np.exp(-t / max(m.tau, 1e-4))
    return synth.fade(norm(out.astype(np.float32)), 0.0002, 0.06, SR)


def cabinet_slam(seed: int, variant: int) -> np.ndarray:
    """A locker fitting slams: strike, panel ring near 180 Hz, two paint tinks.

    The strike is an 8 ms broadband edge — the spectrogram review requires a
    visible transient wall, and a slam that fades in is a swell, not a slam.
    The tinks land *in the ring-down* on purpose: they are the chipped flakes
    the slam shook off, and they read as aftermath, which is what sells a
    one-shot fitting as a physical object instead of a stinger.
    """
    tink_at, tink_g, tink_k, boom_g = SLAM_SHAPE[variant]
    dur = 1.25
    out = synth.silence(dur, SR)

    # The strike: broadband contact, faster than any mode, and the loudest
    # instant of the clip in ENERGY, not just in one sample. Round 1 blooomed
    # 26 ms late (random mode phases); round 1b had the peak sample right but
    # the ring still out-weighed the contact in 1 ms RMS, which reads as a
    # "whang" whose crack is an afterthought. Band reaches down to 260 Hz and
    # tau is 8 ms so the contact carries mean level, not only a spike.
    strike = band_noise(0.040, seed + 1, 260.0, 11000.0)
    strike = strike * synth.exp_decay(0.040, 0.008, SR)
    synth.place(out, norm(strike), 0.0, 2.3, SR)

    # The frame taking the hit: a short low boom, dead fast. Kept at a gain and
    # decay (tau 0.09 vs the panel's 0.30) that cannot outvote the 180 Hz panel
    # in the spectrum — verify_slam checks the panel wins.
    boom = impact([Mode(94, 0.090, 1.0), Mode(151, 0.060, 0.55)], 0.30, seed + 2,
                  noise=0.18, noise_tau=0.010)
    synth.place(out, boom, 0.002, boom_g, SR)

    # The panel body: the ~180 Hz ring the task names, phases locked to the
    # contact (see panel_ring). 1.18 s long: round 1's spectrogram showed the
    # ring chopped at 0.9 s while still ~-34 dB — a visible (and audible) hard
    # edge. At 1.18 s the fundamental has fallen past -40 dB of the clip peak
    # and the 60 ms fade lands on noise-floor material. The contact scrape
    # rides on top as its own burst rather than modal_impact's built-in noise.
    body = panel_ring(SLAM_PANEL[variant], 1.18, seed + 3)
    scrape = band_noise(0.06, seed + 7, 400.0, 6500.0) * synth.exp_decay(0.06, 0.006, SR)
    body = synth.mix(body, norm(scrape), gains=[1.0, 0.30])
    # A touch of comb gives the ring its sheet-metal flutter — the panel is not
    # a bell, it is a plate arguing with its own reflections. Fed forward only
    # at 0.28 so the flutter can never out-sum the contact instant.
    body = synth.mix(body, synth.comb(body, 1.0 / 430.0, 0.28), gains=[1.0, 0.30])
    synth.place(out, body, 0.004, 0.80, SR)

    # Two chipped-paint tinks in the ring-down. Small, bright, discrete.
    for at, g in zip(tink_at, tink_g):
        tink = impact([Mode(4150 * tink_k, 0.012, 1.0),
                       Mode(6420 * tink_k, 0.006, 0.5)], 0.05, seed + 4 + int(at * 100),
                      noise=0.40, noise_tau=0.0008)
        synth.place(out, tink, at, g, SR)

    out = synth.highpass(out, 38.0, order=2, sr=SR)
    # Mild drive for weight. Kept low (1.25) for two measured reasons: drive
    # compresses the strike's spikes harder than the ring's sinusoids (eroding
    # the transient the review demands), and its harmonics of the 180 Hz ring
    # must stay far under the tinks — verify_slam counts exactly two events
    # above 3 kHz in the tail, and drive is what could forge a third.
    return synth.saturate(norm(out), 1.25)


# ── stl_pipe_vent — steam letting go ────────────────────────────────────────


def pipe_vent(seed: int) -> np.ndarray:
    """A steam fitting vents: hard attack, 0.9 s of hiss, rumble, pressure tail.

    The hiss is *steam*, not test noise: band-limited (roll-off measured well
    below Nyquist by verify_vent — a spectrum flat to 24 kHz is a digital
    artefact, not a pipe), coloured by two bore resonances, and modulated by
    slow turbulence so the level breathes the way a real line does. The tail
    is the line emptying: level and brightness fall together, and a single
    cooling-metal tick closes it.
    """
    dur = 1.45
    hiss_len = 0.90  # the task's number: 0.9 s of broadband hiss.
    out = synth.silence(dur, SR)

    # The letting-go: a knock as the fitting gives, right on the attack.
    knock = impact([Mode(620, 0.020, 1.0), Mode(1240, 0.012, 0.5),
                    Mode(2350, 0.006, 0.25)], 0.09, seed + 1, noise=0.55,
                   noise_tau=0.0015)
    synth.place(out, knock, 0.0, 0.55, SR)

    # The hiss: pink base so the top end is already tilted down, band-limited,
    # then bore resonances. Hard attack: 8 ms rise, then a plateau that sags
    # slightly as pressure drops even before the tail proper.
    hiss = band_noise(hiss_len, seed + 2, 320.0, 7600.0, "pink")
    hiss = synth.lowpass(hiss, 8800.0, order=4, sr=SR)
    hiss = synth.mix(hiss,
                     synth.resonator(hiss, 1150.0, q=9.0, sr=SR),
                     synth.resonator(hiss, 2280.0, q=11.0, sr=SR),
                     gains=[1.0, 0.40, 0.28])
    n = _nn(hiss_len)
    rise = np.clip(np.linspace(0.0, 1.0, _nn(0.008)), 0.0, 1.0)
    plateau = np.linspace(1.0, 0.82, n - len(rise))
    envh = np.concatenate([rise, plateau]).astype(np.float32)
    turb = (0.78 + 0.22 * (0.5 + 0.5 * ctrl(hiss_len, seed + 3, 16.0)))[:n]
    synth.place(out, norm(hiss)[:n] * envh * turb, 0.0, 1.0, SR)

    # The rumble under it: the line itself shaking, 45-210 Hz.
    rum = band_noise(1.15, seed + 4, 45.0, 210.0, "brown")
    rum_env = np.concatenate([
        np.clip(np.linspace(0.0, 1.0, _nn(0.020)), 0, 1),
        np.linspace(1.0, 0.0, _nn(1.15) - _nn(0.020)) ** 1.5,
    ]).astype(np.float32)
    synth.place(out, rum[: len(rum_env)] * rum_env, 0.0, 0.42, SR)

    # The pressure tail: the same voice, darker and dying. 0.5 s, band closed
    # down to 2.4 kHz, exponential level fall — a line emptying, not a fade-out.
    tail = band_noise(0.50, seed + 5, 240.0, 2400.0, "pink")
    tail = synth.mix(tail, synth.resonator(tail, 980.0, q=8.0, sr=SR), gains=[1.0, 0.4])
    tail = norm(tail) * synth.exp_decay(0.50, 0.130, SR)
    synth.place(out, tail, hiss_len - 0.02, 0.60, SR)

    # The fitting ticks once as it cools. Aftermath, same job as the slam tinks.
    tick = impact([Mode(3050, 0.010, 1.0), Mode(4900, 0.005, 0.4)], 0.04, seed + 6,
                  noise=0.45, noise_tau=0.0008)
    synth.place(out, tick, 1.24, 0.10, SR)

    return synth.highpass(out, 40.0, order=2, sr=SR)


# ── stl_skitter — something small crosses ahead ─────────────────────────────


SKITTER_SHAPE = {
    # variant: (taps, first gap, last gap, centre f0 -> f1, tap seconds)
    # The f0->f1 span is 6x, not the 4.4x of the first draft: measured off the
    # written file, the wideband claw clicks and the scrabble bed dilute the
    # centroid rise, and round 1 landed at exactly the 1.25x floor. The wider
    # track plus a narrower per-tap band buys the margin honestly.
    0: (7, 0.150, 0.062, 700.0, 4200.0, 0.016),
    1: (8, 0.135, 0.050, 620.0, 4800.0, 0.020),
}


def skitter(seed: int, variant: int) -> np.ndarray:
    """A 6-8 tap accelerando of filtered noise bursts, pitch rising as it passes.

    The clip is mono; the "crossing" is carried by three time-domain cues that
    survive any panning Unity applies: the gaps shrink (approach), the centre
    frequency rises (the classic pass-by brightening), and the amplitude arcs
    up to the midpoint and recedes. 7-8 taps inside ~0.65 s is 10-13 Hz — an
    order of magnitude above any footstep cadence in the game, so it cannot be
    mistaken for the one channel §12 says is load-bearing.
    """
    taps, gap0, gap1, f0, f1, tap_len = SKITTER_SHAPE[variant]
    g = synth.rng(seed)

    gaps = np.geomspace(gap0, gap1, taps - 1)
    # 20 ms of room before the first tap: round 1's spectrogram showed tap one
    # half-swallowed by the file edge (its analysis window and write_wav's head
    # fade both bite into it at offset zero).
    times = 0.020 + np.concatenate([[0.0], np.cumsum(gaps)])
    span = float(times[-1])
    dur = span + 0.30
    out = synth.silence(dur, SR)

    centres = np.geomspace(f0, f1, taps)
    # Amplitude arc: builds to the midpoint, recedes after — the pass.
    arc = 0.55 + 0.45 * np.sin(np.pi * np.linspace(0.05, 0.95, taps))

    for i in range(taps):
        fc = float(centres[i]) * float(g.uniform(0.94, 1.06))
        burst = band_noise(tap_len, seed + 10 + i, fc * 0.68, fc * 1.60)
        burst = burst * synth.exp_decay(tap_len, 0.0055, SR)
        # A hair of claw on each contact: one tiny mode above the burst band.
        claw = impact([Mode(fc * 2.3, 0.0035, 1.0)], 0.012, seed + 40 + i,
                      noise=0.65, noise_tau=0.0006)
        hit = synth.mix(norm(burst), claw, gains=[1.0, 0.30])
        synth.place(out, hit, float(times[i]), float(arc[i]) * float(g.uniform(0.92, 1.0)), SR)

    # The scrabble under the taps: grit being disturbed, far below tap level.
    # 0.06, not more — the bed's spectrum is constant across the pass, so every
    # dB of bed flattens the centroid rise the taps worked for.
    bed = band_noise(span + 0.10, seed + 3, 1300.0, 6200.0)
    bed_arc = np.sin(np.pi * np.linspace(0.02, 0.98, _nn(span + 0.10))) ** 2
    synth.place(out, bed * bed_arc.astype(np.float32), 0.012, 0.06, SR)

    return synth.highpass(out, 300.0, order=2, sr=SR)


# ── stl_glimpse — the figure reveal ─────────────────────────────────────────


def glimpse(seed: int) -> np.ndarray:
    """A low sub swell with a breath over it, cut to true silence. Dread.

    46→57 Hz over 1.4 s: below the maze's footstep register, felt before it is
    placed. The exhale is pink noise through two vocal-tract-shaped resonances
    with a slow flutter — breath-like, deliberately NOT voiced: a pitch contour
    would read as the creature, and decision (a) forbids this system to touch
    that channel even by imitation. The cut is a 12 ms cosine at 1.40 s and the
    last 140 ms are digital zero: §06 owns what comes after.
    """
    dur = 1.55
    cut_at = 1.40
    out = synth.silence(dur, SR)

    # The sub swell. Slow attack (the figure was already there), no release —
    # the cut is the release. Second harmonic at 0.16 keeps a trace of it
    # audible on small drivers without turning it into a tone.
    swell_len = cut_at
    sub = synth.sweep(46.0, 57.0, swell_len, log=True, sr=SR)
    sub = synth.mix(sub, synth.sweep(92.0, 114.0, swell_len, log=True, sr=SR),
                    gains=[1.0, 0.16])
    t = np.linspace(0.0, 1.0, _nn(swell_len))
    rise = np.clip(t / 0.75, 0.0, 1.0) ** 2.2  # quiet for a long time, then present
    sub = synth.saturate(sub * rise.astype(np.float32), 1.25)
    synth.place(out, sub, 0.0, 1.0, SR)

    # The exhale: starts a third of the way in, peaks late, still sounding when
    # the cut takes it. Band-limited breath — no content that could klaxon.
    ex_len = swell_len - 0.42
    ex = band_noise(ex_len, seed + 1, 260.0, 2300.0, "pink")
    ex = synth.mix(ex,
                   synth.resonator(ex, 640.0, q=6.0, sr=SR),
                   synth.resonator(ex, 1180.0, q=7.0, sr=SR),
                   gains=[1.0, 0.5, 0.35])
    ex = synth.lowpass(ex, 3200.0, order=2, sr=SR)
    ex = synth.tremolo(ex, 5.2, 0.28, SR)  # unsteady breath, not a straight hiss
    tt = np.linspace(0.0, 1.0, _nn(ex_len))
    ex_env = np.clip(tt / 0.55, 0.0, 1.0) ** 1.6 * (1.0 - 0.25 * np.clip((tt - 0.8) / 0.2, 0, 1))
    synth.place(out, norm(ex) * ex_env.astype(np.float32), 0.42, 0.38, SR)

    # The cut: 12 ms raised cosine ending at cut_at, then digital zero. Fast
    # enough to read as a cut, shaped enough not to be its own broadband click
    # — the scare is the silence arriving, not a tick.
    cut_len = _nn(0.012)
    cut_end = _nn(cut_at)
    gate = np.ones(len(out), dtype=np.float32)
    gate[cut_end - cut_len:cut_end] = (0.5 + 0.5 * np.cos(
        np.pi * np.linspace(0.0, 1.0, cut_len))).astype(np.float32)
    gate[cut_end:] = 0.0
    out = out * gate

    return synth.highpass(out, 28.0, order=2, sr=SR) * gate  # keep the tail at true zero


# ── Build ───────────────────────────────────────────────────────────────────


def build() -> None:
    """Generates every clip. Seed offsets are fixed so a rebuild is byte-identical."""
    S = SEED
    emit("stl_cabinet_slam_01", cabinet_slam(S + 100, 0),
         "깜짝: locker fitting slams — local-only, never reported to the creature")
    emit("stl_cabinet_slam_02", cabinet_slam(S + 110, 1),
         "깜짝: second locker, different panel and tink timing")
    emit("stl_pipe_vent_01", pipe_vent(S + 200),
         "깜짝: steam fitting lets go — 0.9 s hiss, rumble, pressure tail")
    emit("stl_skitter_01", skitter(S + 300, 0),
         "깜짝: something small crosses — 7-tap accelerando, pitch rising")
    emit("stl_skitter_02", skitter(S + 310, 1),
         "깜짝: second crossing — 8 taps, wider band")
    emit("stl_glimpse_01", glimpse(S + 400),
         "깜짝: the figure — sub swell + exhale, cut to §06's silence")


# ── Verification — listen by measurement, on the written files ──────────────


def verify_false_alarm_ceiling() -> str:
    """No 깜짝 may outrank a true threat cue. Measured, not assumed.

    The shipped families are read back from disk (the artefact, not the
    source): if this family's loudest peak reaches within 0.75 dB of the
    quietest true threat cue, the false alarm has climbed into the truth band
    and the mix is lying about what matters. The cue list is what ships after
    the pivot: the creature's roar and grab, and a door bolt closing a route.
    """
    threat_cues = [(MONSTER_DIR, "monster_roar_01"), (MONSTER_DIR, "monster_roar_02"),
                   (MONSTER_DIR, "monster_roar_03"), (MONSTER_DIR, "monster_grab_01"),
                   (MONSTER_DIR, "monster_grab_02"), (ITEMS_DIR, "door_lock_01"),
                   (ITEMS_DIR, "door_lock_02")]
    peaks = {}
    for d, n in threat_cues:
        p = os.path.join(d, n + ".wav")
        if not os.path.exists(p):
            raise AssertionError(f"{p}: missing — the ceiling cannot be measured. "
                                 f"Run the owning generator first.")
        peaks[n] = synth.analyse(p).peak_db
    quietest_cue = min(peaks.items(), key=lambda kv: kv[1])
    ours = max(REPORTS, key=lambda r: r.peak_db)
    margin = quietest_cue[1] - ours.peak_db
    if margin < 0.75:
        raise AssertionError(
            f"false-alarm ceiling: {os.path.basename(ours.path)} peaks at "
            f"{ours.peak_db:.2f} dBFS, within {margin:.2f} dB of true threat cue "
            f"{quietest_cue[0]} ({quietest_cue[1]:.2f}) — the fake must never "
            f"outrank the real thing")
    lines = ["False-alarm ceiling (peaks measured from the written files):"]
    for n, db in sorted(peaks.items(), key=lambda kv: kv[1]):
        lines.append(f"  true threat cue   {n:<24} {db:>7.2f} dBFS")
    lines.append(f"  loudest 깜짝      {os.path.basename(ours.path):<24} "
                 f"{ours.peak_db:>7.2f} dBFS   margin {margin:.2f} dB below the "
                 f"quietest cue")
    return "\n".join(lines)


def verify_slam(name: str) -> str:
    """The slam's three claims: a transient wall, a ~180 Hz body, two tinks."""
    path = os.path.join(OUT_DIR, name + ".wav")
    x, _ = synth.read_wav(path)

    # Attack: 10% -> 90% of peak envelope inside 8 ms, or it is a swell — and
    # the envelope's MAXIMUM must sit at the contact (first 20 ms), or the ring
    # out-weighs the crack and the slam reads as a bloom (round 1 did exactly
    # that: peak sample at the strike, envelope crown 29 ms later).
    e = env_of(x, 1.0)
    pk = float(e.max())
    t_pk = float(np.argmax(e)) / SR
    if t_pk > 0.020:
        raise AssertionError(f"{name}: envelope maximum at {t_pk * 1000:.1f} ms — "
                             f"the ring outweighs the contact; that is a bloom, "
                             f"not a slam")
    t10 = float(np.argmax(e >= 0.10 * pk)) / SR
    t90 = float(np.argmax(e >= 0.90 * pk)) / SR
    attack = t90 - t10
    if not (0.0 <= attack <= 0.008):
        raise AssertionError(f"{name}: attack {attack * 1000:.1f} ms — a slam whose "
                             f"spectrogram shows no transient edge fails the review")

    # Body: the dominant spectral line between 120 and 400 Hz must sit near
    # 180 Hz. Below 120 is the frame boom, which is allowed to exist but not
    # to win — checked by measuring the full-band argmax too.
    mag = np.abs(np.fft.rfft(x.astype(np.float64) * np.hanning(len(x))))
    freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
    band = (freqs >= 120.0) & (freqs <= 400.0)
    body_hz = float(freqs[band][np.argmax(mag[band])])
    if not (150.0 <= body_hz <= 215.0):
        raise AssertionError(f"{name}: body resonance {body_hz:.0f} Hz, task asks ~180")
    low = (freqs >= 60.0) & (freqs <= 400.0)
    low_peak_hz = float(freqs[low][np.argmax(mag[low])])
    if low_peak_hz < 120.0:
        raise AssertionError(f"{name}: the frame boom ({low_peak_hz:.0f} Hz) outvotes "
                             f"the panel — the slam reads as a thud, not sheet metal")

    # Ring-down: the panel must still be speaking well after the strike. The
    # measure is the LAST time the envelope sits above −25 dB of the clip's
    # peak — a first-crossing measure (like the audit's ring_ms) is unstable
    # here because the 181/288 Hz beat pattern dips through any floor early
    # while the ring is plainly still audible either side of the null.
    ring = _last_above_ms(path, -25.0)
    if not (250.0 <= ring <= 1150.0):
        raise AssertionError(f"{name}: audible ring {ring:.0f} ms — under 250 the "
                             f"panel is dead and the slam reads as a thump; over "
                             f"1150 it outlives its own clip")

    # Tinks: exactly two discrete events above 3 kHz in the ring-down.
    hf = synth.highpass(x, 3000.0, order=4, sr=SR)
    tail_from = _nn(0.20)
    hits = onsets(hf[tail_from:], rel=0.30, min_gap=0.05, ms=1.0)
    if len(hits) != 2:
        raise AssertionError(f"{name}: {len(hits)} bright events in the ring-down, "
                             f"want exactly the two chipped-paint tinks")
    return (f"  {name:<22} attack {attack * 1000:>4.1f} ms   body {body_hz:>5.0f} Hz   "
            f"ring {ring:>4.0f} ms   tinks at "
            + ", ".join(f"{0.20 + h:.2f}s" for h in hits))


def _last_above_ms(path: str, floor_db: float) -> float:
    """Time (ms) of the LAST envelope sample above `floor_db` rel the clip's
    envelope peak. Robust where a first-crossing decay measure is not: a
    two-mode beat pattern dips through any floor early while the sound is plainly
    still there on both sides of the null."""
    x, sr = synth.read_wav(path)
    env = np.abs(x.astype(np.float64))
    win = max(1, int(0.002 * sr))
    env = np.convolve(env, np.ones(win) / win, mode="same")
    if not len(env) or env.max() <= 0:
        return 0.0
    above = np.nonzero(env >= env.max() * synth.db_to_gain(floor_db))[0]
    return float(above[-1]) / sr * 1000.0 if len(above) else 0.0


def verify_vent() -> str:
    """The vent's claims: hard attack, ~0.9 s hiss, rumble under, analogue top."""
    path = os.path.join(OUT_DIR, "stl_pipe_vent_01.wav")
    x, _ = synth.read_wav(path)

    e = env_of(x, 2.0)
    pk = float(e.max())
    t10 = float(np.argmax(e >= 0.10 * pk)) / SR
    t90 = float(np.argmax(e >= 0.90 * pk)) / SR
    attack = t90 - t10
    if not (0.0 <= attack <= 0.025):
        raise AssertionError(f"vent: attack {attack * 1000:.1f} ms — the task says "
                             f"hard attack, this is a swell")

    # Hiss body: time the envelope spends above -12 dB of its own peak. The
    # task names 0.9 s; the knock and tail add a little around it.
    above = float(np.sum(e >= pk * synth.db_to_gain(-12.0))) / SR
    if not (0.70 <= above <= 1.15):
        raise AssertionError(f"vent: {above:.2f} s above -12 dB — the 0.9 s hiss "
                             f"body is missing or the tail never lets go")

    # Analogue top: a spectrum still flat at 20 kHz is white noise wearing a
    # steam costume. Mean magnitude 16-22 kHz must sit >= 18 dB under 1-6 kHz.
    mag = np.abs(np.fft.rfft(x.astype(np.float64) * np.hanning(len(x))))
    freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
    mid = float(np.mean(mag[(freqs >= 1000.0) & (freqs <= 6000.0)]))
    top = float(np.mean(mag[(freqs >= 16000.0) & (freqs <= 22000.0)]))
    rolloff = synth.gain_to_db(top / mid) if mid > 0 else 0.0
    if rolloff > -18.0:
        raise AssertionError(f"vent: top end only {rolloff:.1f} dB under the mid "
                             f"band — digital, not steam")

    # The rumble must exist: 45-210 Hz energy within 26 dB of the hiss band.
    lowb = float(np.mean(mag[(freqs >= 45.0) & (freqs <= 210.0)]))
    low_vs_mid = synth.gain_to_db(lowb / mid) if mid > 0 else -99.0
    if low_vs_mid < -26.0:
        raise AssertionError(f"vent: rumble sits {low_vs_mid:.1f} dB under the hiss "
                             f"— the low line under the burst is inaudible")
    return (f"  stl_pipe_vent_01       attack {attack * 1000:>4.1f} ms   hiss body "
            f"{above:.2f} s   top rolloff {rolloff:>6.1f} dB   rumble {low_vs_mid:>6.1f} dB")


def verify_skitter(name: str) -> str:
    """The skitter's claims: 6-8 taps, accelerando, pitch rising across the pass."""
    path = os.path.join(OUT_DIR, name + ".wav")
    x, _ = synth.read_wav(path)

    hits = onsets(x, rel=0.20, min_gap=0.034, ms=1.5)
    if not (6 <= len(hits) <= 8):
        raise AssertionError(f"{name}: {len(hits)} taps measured, task says 6-8")
    gaps = np.diff(hits)
    for i in range(1, len(gaps)):
        if gaps[i] > gaps[i - 1] - 0.003:
            raise AssertionError(
                f"{name}: gap {i} is {gaps[i] * 1000:.0f} ms after "
                f"{gaps[i - 1] * 1000:.0f} ms — not an accelerando, taps must "
                f"close in by >= 3 ms each step")

    mid = _nn(hits[0] + (hits[-1] - hits[0]) * 0.5)
    endn = min(len(x), _nn(hits[-1] + 0.05))
    c1 = magnitude_centroid(x[:mid])
    c2 = magnitude_centroid(x[mid:endn])
    if c2 < c1 * 1.25:
        raise AssertionError(f"{name}: centroid {c1:.0f} -> {c2:.0f} Hz across the "
                             f"pass ({c2 / max(c1, 1e-9):.2f}x, need >= 1.25x) — "
                             f"it does not read as passing")
    rate = (len(hits) - 1) / (hits[-1] - hits[0])
    return (f"  {name:<22} taps {len(hits)}   gaps "
            + "→".join(f"{g * 1000:.0f}" for g in gaps)
            + f" ms   centroid {c1:.0f}→{c2:.0f} Hz   {rate:.1f} taps/s")


def verify_glimpse() -> str:
    """The glimpse's claims: 45-60 Hz swell, dread spectrum, a cut to true zero."""
    path = os.path.join(OUT_DIR, "stl_glimpse_01.wav")
    x, _ = synth.read_wav(path)

    # Fundamental: energy-dominant line below 120 Hz sits inside 43-62.
    mag = np.abs(np.fft.rfft(x.astype(np.float64) * np.hanning(len(x))))
    freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
    low = (freqs >= 20.0) & (freqs <= 120.0)
    f0 = float(freqs[low][np.argmax(mag[low])])
    if not (43.0 <= f0 <= 62.0):
        raise AssertionError(f"glimpse: sub fundamental {f0:.0f} Hz, task says 45-60")

    # Dread, not a klaxon: the whole clip's centroid stays low.
    cen = magnitude_centroid(x)
    if cen > 900.0:
        raise AssertionError(f"glimpse: centroid {cen:.0f} Hz — that is a sting, "
                             f"not dread")

    # The cut: >= 30 dB drop across 60 ms, and the tail is actually silent.
    e = env_of(x, 2.0)
    pk = float(e.max())
    drops = np.nonzero(e >= pk * 0.5)[0]
    late = float(drops[-1]) / SR if len(drops) else 0.0
    before = float(np.max(e[_nn(max(0.0, late - 0.10)):_nn(late) + 1]))
    after_i = _nn(late + 0.06)
    after = float(np.max(e[after_i:after_i + _nn(0.05)])) if after_i < len(e) else 0.0
    drop_db = synth.gain_to_db(max(after, 1e-7) / max(before, 1e-7))
    if drop_db > -30.0:
        raise AssertionError(f"glimpse: only {drop_db:.1f} dB across the cut — it "
                             f"fades where it should cut")
    tail = x[-_nn(0.10):]
    tail_rms = float(np.sqrt(np.mean(tail.astype(np.float64) ** 2)))
    if tail_rms > 1e-3:
        raise AssertionError(f"glimpse: tail rms {tail_rms:.2e} — the clip must end "
                             f"in §06's silence, not a residue")
    return (f"  stl_glimpse_01         sub {f0:.0f} Hz   centroid {cen:>4.0f} Hz   "
            f"cut {drop_db:>6.1f} dB at ~{late:.2f}s   tail rms {tail_rms:.1e}")


VARIANT_MAX_SIMILARITY = 0.60
"""Ceiling on waveform correlation between two takes of one fitting. The same
constant gen_items earned on its most-repeated clip: distinct bytes prove
nothing, two takes must differ in structure, not gain."""


def waveform_similarity(path_a: str, path_b: str, max_lag_s: float = 0.010) -> float:
    """Peak normalised cross-correlation within ±`max_lag_s`. 1.0 means clone."""
    a, _ = synth.read_wav(path_a)
    b, _ = synth.read_wav(path_b)
    n = min(len(a), len(b))
    x = a[:n].astype(np.float64) - float(np.mean(a[:n]))
    y = b[:n].astype(np.float64) - float(np.mean(b[:n]))
    denom = float(np.sqrt(np.sum(x * x) * np.sum(y * y)))
    if denom <= 0.0:
        return 1.0
    corr = np.correlate(x, y, mode="full")
    lag = min(n - 1, _nn(max_lag_s))
    return float(np.max(np.abs(corr[n - 1 - lag: n + lag])) / denom)


def verify_variants() -> str:
    """Two takes of a fitting must be different renders, not one at two gains."""
    lines = [f"Variant takes (distinct bytes, waveform correlation < "
             f"{VARIANT_MAX_SIMILARITY:.2f}):"]
    for stem in ("stl_cabinet_slam", "stl_skitter"):
        paths = [os.path.join(OUT_DIR, f"{stem}_{i:02d}.wav") for i in (1, 2)]
        digests = {hashlib.sha256(open(p, "rb").read()).hexdigest() for p in paths}
        if len(digests) != 2:
            raise AssertionError(f"{stem}: the two takes are byte-identical")
        sim = waveform_similarity(paths[0], paths[1])
        if sim > VARIANT_MAX_SIMILARITY:
            raise AssertionError(f"{stem}: takes correlate at {sim:.2f} — one sound "
                                 f"at two gains, not two fittings")
        lines.append(f"  {stem:<22} 2 takes   correlation {sim:.3f}")
    return "\n".join(lines)


def verify_family_mix() -> str:
    """In-family relations that peak levels alone cannot promise."""
    by_name = {os.path.basename(r.path)[:-4]: r for r in REPORTS}
    vent = by_name["stl_pipe_vent_01"]
    skit = by_name["stl_skitter_01"]
    ratio = vent.rms / skit.rms
    if ratio < 1.8:
        raise AssertionError(f"stl_pipe_vent_01 is only {ratio:.2f}x the RMS of "
                             f"stl_skitter_01 (need 1.8x) — a steam burst must "
                             f"dominate a small scurry or the ladder is noise")
    return (f"Family mix: vent RMS {ratio:.2f}x skitter RMS (>= 1.8x) — the burst "
            f"outweighs the scurry, as the ladder claims")


def main() -> int:
    build()

    print(f"\n{len(REPORTS)} clips → {OUT_DIR}\n")
    print(synth.report_table(REPORTS))
    print()
    worst_dc = max(REPORTS, key=lambda r: abs(r.dc_offset))
    worst_peak = max(REPORTS, key=lambda r: r.peak)
    print(f"Worst case across all {len(REPORTS)}: "
          f"DC {worst_dc.dc_offset:+.5f} ({os.path.basename(worst_dc.path)}), "
          f"peak {worst_peak.peak_db:.1f} dBFS ({os.path.basename(worst_peak.path)}), "
          f"clipped samples {sum(r.clipped_samples for r in REPORTS)}, "
          f"channels {sorted({r.channels for r in REPORTS})}, "
          f"sample rates {sorted({r.sample_rate for r in REPORTS})}")
    print()
    print(verify_false_alarm_ceiling())
    print()
    print("Per-fitting measurements (the written files, not the buffers):")
    print(verify_slam("stl_cabinet_slam_01"))
    print(verify_slam("stl_cabinet_slam_02"))
    print(verify_vent())
    print(verify_skitter("stl_skitter_01"))
    print(verify_skitter("stl_skitter_02"))
    print(verify_glimpse())
    print()
    print(verify_variants())
    print()
    print(verify_family_mix())

    print("\nRoster — what each clip is for in play:")
    for name, note in ROSTER:
        print(f"  {name:<24} {note}")

    print("\nSHA-256 (a second run must print these unchanged):")
    for r in sorted(REPORTS, key=lambda x: x.path):
        digest = hashlib.sha256(open(r.path, "rb").read()).hexdigest()
        print(f"  {digest[:16]}  {os.path.basename(r.path)}")

    print(f"\nOK — {len(REPORTS)} clips written and verified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
