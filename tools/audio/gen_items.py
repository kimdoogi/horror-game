#!/usr/bin/env python3
"""Items, doors and the Engineer's tools — procedural generation.

Run:  tools/audio/.venv/bin/python tools/audio/gen_items.py
Out:  unity/HorrorGame/Assets/Audio/Items/*.wav   (48 kHz, 16-bit PCM)

Everything here is synthesised from `synth.py`. Nothing is sampled or licensed,
which is a shipping requirement, not a style choice (see synth.py's header).

WHAT EACH SOUND IS FOR
──────────────────────
These are not flavour. §03 makes light the lock on the objective, §04 hands the
Engineer the power to trap his own team, and §08 prices several items in *noise*.
So each clip below has a job, and for a good number of them the job is to be
loud enough to hurt.

* `flashlight_on/off` — §05 binds the light to `F`, and §10 lists "손전등을 켠다"
  as a dilemma the player re-takes constantly. It is therefore the most-played
  clip in the game: crisp, short, and deliberately dull above 9 kHz so a hundred
  presses an hour never turn grating.
* `battery_insert` — §03's round trip exists because the battery runs out. This is
  the sound of buying yourself more time; it should feel mechanical and reassuring.
* `battery_low_warning` — three flickers with the driver whine bending down. §03:
  "배터리가 떨어지면 단서를 읽을 수 없다." The warning is the player's cue that the
  clue-reading window is closing.
* `battery_dead` — a real setback, per §03: a dead light means clues cannot be read
  at all. The whine collapses, the driver clunks, one hollow low thud, and then the
  clip deliberately spends its last 0.7 s on almost nothing. §06 says silence is the
  weapon ("침묵이 가장 무서운 소리다"); a dead flashlight is the player being handed
  that weapon pointed the wrong way.
* `door_open / door_close` — §04 constrains the 청음사: "자기가 소리를 내면 못
  듣는다. 뛰거나 문을 열면 정보가 끊긴다." A door has to be genuinely noisy or that
  constraint is fiction. Hinge creak is stick-slip synthesis, not a filtered sweep.
* `door_lock` — §04 gives the Engineer door locking and §12 puts a lockable door on
  the neck of a 순환로, which means locking one can cut a teammate's only escape.
  §04's design note is explicit that the Engineer must be able to trap teammates:
  "실수가 아군을 죽인다". This is what the trapped player hears, so it resolves into
  one weighted bolt slam and then stops dead. No tail. The abrupt end is the point.
* `barricade_place / barricade_break` — §04 차단물. Placing is quiet work; breaking
  is the monster coming through, so it is a threat cue and mixed loud.
* `noisetrap_arm / noisetrap_trigger` — §04 소음 함정, and §04 warns the trap can
  catch the 주자. Arming is quiet (§04: "즉석 사용 불가 — 사전 준비형"); the trigger
  is the loudest positional clip in this set on purpose.
* `safe_dial_turn_loop / safe_open` — §08's 금고 속 문서 (무게 2, 가치 높음) needs the
  Engineer, and §04 lists "금고를 연다" as his objective involvement.
* `breaker_throw / zone_hum_loop` — §04 구역 조명, §03's table: "구역 전체가 밝다 ·
  여러 명이 동시에 읽는다 · **괴물도 그쪽으로 온다**", and §10 prices it as
  "구역을 밝힌다 → 괴물이 그쪽으로 온다". The hum is therefore not ambience; it is a
  standing warning that this room is bait. It is the loudest *sustained* clip here.
* `flare_ignite / flare_burn_loop / flare_die` — §08: 조명탄, "1회용 · 소리를 낸다",
  and §11 makes it the substitute for a missing Engineer. The burn loop is where the
  "소리를 낸다" price is actually paid, so it is mixed hot and never stops.
* `chalk_mark` — §08 분필: the cost is "괴물도 흔적을 따라온다", a *visual* trail, not
  noise. So this is the quietest clip in the set, on purpose.
* `rope_deploy` — §08 밧줄, 층 사이 지름길, 편도만.
* `loot_pickup_*` — four material classes straight off §08's 전리품 table:
  metal_small = 은수저·잡동사니 (무게 1), glass_jewel = 회중시계·반지 (무게 1, 효율
  최고), paper = 금고 속 문서 (무게 2), wood_heavy = 대형 초상화·궤짝 (무게 5, 2인
  운반). Weight is audible: the chest is eight times the level of the spoons.
* `loot_sell_credit` — §08's 지상 차량. The loot lands in the truck bed, then a short
  warm two-note credit tone. Restrained: the truck is the only safe place in the game
  and does not need a slot-machine.
* `shop_purchase_confirm` — the only non-diegetic clip here, and the only stereo one.
* `detector_ping` — §11 makes 감지기 the substitute for a missing 청음사 and §08 prices
  it "작동 시 소리를 낸다". That noise *is* the item's whole drawback, so the ping is
  mixed at −5 dBFS with a long tail: everyone nearby hears you looking.
* `muffler_equip` — §08 소음기: "자기도 못 듣게 됨 → 청음사 무효." The clip enacts it —
  fabric, a buckle, and then the band audibly closes down to 500 Hz and dies. It is
  quiet because a loud muffler would be a joke.

CONVENTIONS THIS FILE HOLDS TO
──────────────────────────────
* Mono everywhere except `shop_purchase_confirm`. Unity will not spatialise a stereo
  clip, and the Listener (§04/§12) and §13's proximity voice both need 3D attenuation.
* Every random source is seeded from `SEED` with a fixed per-sound offset, so a
  rebuild is byte-identical. `main()` prints a SHA-256 per file to prove it.
* Peak levels come from `LEVELS`, one documented ladder rather than per-sound
  guesswork. Peak is not loudness though, so `main()` also verifies the *measured
  RMS ratios* between the items §08 prices in noise and the items it does not.
  §10's rule is that every gain is paid for; an item whose noisy drawback is
  inaudible is a free item, and that is a balance bug you cannot see in a diff.
* Loops (`*_loop`, `safe_dial_turn_loop`) are shaped so their amplitude minimum sits
  at both ends — see `loop_breathe` for why that matters given write_wav's fades.
"""

from __future__ import annotations

import hashlib
import os
import sys

import numpy as np
from scipy import signal as sg

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import source_bank  # noqa: E402
import synth  # noqa: E402
from synth import Mode  # noqa: E402

DOOR_REAL = 1.15
"""How much of a door is a recording, as a fraction of the synthesised body's
energy. See `source_bank.mix_real`.

The three door gestures are the only clips in this file where a recording beats
the model outright, and they are also the ones §04 leans on hardest: opening a
door is what makes the 청음사 deaf to the thing he is listening for, so the
player has to believe it enough to weigh it.

What synthesis misses is the same thing in all three. `hinge_creak` is a
modulated sweep, and a hinge does not glide — it seizes, releases, seizes again,
and the pitch *jumps*. `door_close` places a strike plate, a leaf and a settle as
three impacts, and a real door is one object whose parts arrive in an order the
door decides. `door_lock` clicks a mechanism three times, and a real lock is a
dozen small steel parts moving at once, which is a texture rather than a count.
None of those is a tuning error; all three are the model being a model."""

SR = synth.SAMPLE_RATE

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "unity", "HorrorGame", "Assets", "Audio", "Items",
)

SEED = 90_210
"""Base seed. Every generator derives its seeds from this, so the whole set is
reproducible from one number."""


# ── Level ladder ────────────────────────────────────────────────────────────
#
# Peak dBFS per clip. One table so the mix is a decision instead of an accident.
#
# The ordering encodes design intent, loudest first:
#   §04 noise trap trigger        — the loudest thing an item can do
#   §08 items priced in noise      — flare, detector: the drawback must be audible
#   §04 door lock                  — what a trapped teammate hears
#   consequence sounds             — barricade breaking, safe opening, battery dying
#   ordinary interaction           — doors, barricades, loot
#   sustained beds                 — quieter in peak, always present, high RMS
#   quiet work                     — clicks, chalk, the muffler
LEVELS = {
    "noisetrap_trigger": -3.0,
    "door_lock_01": -4.0,
    "flare_ignite": -4.0,
    "barricade_break_02": -4.0,
    "door_lock_02": -4.5,
    "barricade_break_01": -4.5,
    "detector_ping": -5.0,
    "flare_burn_loop": -6.0,
    "safe_open": -6.0,
    "breaker_throw": -6.5,
    "battery_dead": -6.5,
    "door_close_02": -7.0,
    "door_close_01": -8.0,
    "door_open_02": -8.0,
    "door_open_01": -8.5,
    "loot_pickup_wood_heavy_01": -8.5,
    "loot_pickup_wood_heavy_02": -9.0,
    "flare_die": -9.0,
    "rope_deploy": -9.5,
    "loot_sell_credit": -10.5,
    "barricade_place_01": -11.0,
    "barricade_place_02": -11.5,
    "loot_pickup_glass_jewel_01": -12.0,
    "loot_pickup_glass_jewel_02": -12.5,
    "shop_purchase_confirm": -12.0,
    "safe_dial_turn_loop": -11.5,
    "muffler_equip": -13.0,
    "battery_insert_01": -13.0,
    "battery_insert_02": -13.5,
    "noisetrap_arm": -13.5,
    "flashlight_on_01": -14.0,
    "zone_hum_loop": -14.5,
    "flashlight_on_02": -14.5,
    "flashlight_off_01": -15.0,
    "battery_low_warning": -15.0,
    "flashlight_off_02": -15.5,
    "loot_pickup_paper_01": -15.0,
    "loot_pickup_paper_02": -15.5,
    "loot_pickup_metal_small_01": -15.5,
    "loot_pickup_metal_small_02": -16.0,
    "chalk_mark_01": -20.0,
    "chalk_mark_02": -20.5,
    "chalk_mark_03": -20.0,
}

STEREO = {"shop_purchase_confirm"}
"""Only the non-diegetic UI confirm. Everything else is positional, so mono."""

LOOPS = {"zone_hum_loop", "flare_burn_loop", "safe_dial_turn_loop"}

REPORTS: list[synth.ClipReport] = []
ROSTER: list[tuple[str, str]] = []


# ── Local helpers (nothing here duplicates synth.py) ────────────────────────


def _nn(seconds: float) -> int:
    return synth.n_samples(seconds, SR)


def norm(buf: np.ndarray) -> np.ndarray:
    """Peak-normalises to 1.0 without touching headroom. Public stand-in for
    synth's private `_safe_norm`, used while building intermediate buffers."""
    return synth.normalize(buf, 0.0)


def fm_sine(freq_curve: np.ndarray) -> np.ndarray:
    """A sine whose frequency follows a per-sample curve.

    `synth.sweep` is monotonic, which cannot express a hinge: a creak's pitch
    wanders up and down as the metal binds and releases.
    """
    phase = np.cumsum(2.0 * np.pi * np.asarray(freq_curve, dtype=np.float64) / SR)
    return np.sin(phase).astype(np.float32)


def ctrl(seconds: float, seed: int, hz: float) -> np.ndarray:
    """A smooth random control signal in roughly [-1, 1]."""
    y = synth.lowpass(synth.white(seconds, seed, SR), hz, order=2, sr=SR)
    peak = float(np.max(np.abs(y)))
    return (y / (peak if peak > 1e-9 else 1.0)).astype(np.float32)


def grain_env(seconds: float, seed: int, per_second: float, decay: float,
              sharp: float = 1.0) -> np.ndarray:
    """A crackle/rasp amplitude texture: sparse impulses smeared by a short decay.

    Multiplying noise by this is what separates a *scrape* from a hiss. Chalk,
    paper, splintering wood and a burning flare all differ mainly in grain
    density and grain decay.
    """
    n = _nn(seconds)
    g = synth.rng(seed)
    out = np.zeros(n, dtype=np.float64)
    count = max(1, int(round(seconds * per_second)))
    idx = g.integers(0, n, count)
    np.add.at(out, idx, g.uniform(0.25, 1.0, count) ** sharp)
    kernel = np.exp(-np.arange(_nn(max(decay * 6.0, 0.0005))) / max(decay * SR, 1.0))
    out = np.convolve(out, kernel)[:n]
    peak = out.max()
    return (out / (peak if peak > 1e-12 else 1.0)).astype(np.float32)


def stick_slip(seconds: float, rate_curve: np.ndarray, seed: int) -> np.ndarray:
    """Amplitude texture of a surface that binds and releases.

    A creak is not a tone; it is a few dozen micro-slips per second, each one
    plucking the same resonance. Driving a wandering sine with this is why the
    doors here sound like hinges instead of theremins.
    """
    n = _nn(seconds)
    g = synth.rng(seed)
    out = np.zeros(n, dtype=np.float64)
    i = 0
    while i < n:
        rate = float(rate_curve[min(i, n - 1)])
        out[i] = float(g.uniform(0.35, 1.0))
        i += max(1, int(SR / max(rate, 1.0) * float(g.uniform(0.55, 1.5))))
    kernel = np.exp(-np.arange(_nn(0.024)) / (0.0045 * SR))
    out = np.convolve(out, kernel)[:n]
    peak = out.max()
    return (out / (peak if peak > 1e-12 else 1.0)).astype(np.float32)


def hinge_creak(seconds: float, seed: int, f0: float, f1: float,
                rate0: float = 26.0, rate1: float = 46.0, wander: float = 0.14,
                rough: float = 1.0) -> np.ndarray:
    """A binding hinge. Stick-slip texture on a wandering inharmonic resonance."""
    n = _nn(seconds)
    t = np.linspace(0.0, 1.0, n, dtype=np.float64)
    base = f0 * (f1 / f0) ** t
    freq = base * (1.0 + wander * ctrl(seconds, seed + 11, 3.2)[:n]
                   + 0.035 * rough * ctrl(seconds, seed + 12, 45.0)[:n])
    tone = fm_sine(freq)
    tone = tone + 0.45 * fm_sine(2.0 * freq) + 0.22 * fm_sine(3.04 * freq)
    rate = rate0 * (rate1 / rate0) ** t
    out = tone * (0.22 + 0.78 * stick_slip(seconds, rate, seed + 13))
    out = synth.bandpass(out, max(70.0, min(f0, f1) * 0.55),
                         min(11000.0, max(f0, f1) * 8.0), order=2, sr=SR)
    return synth.fade(norm(out), 0.012, 0.03, SR)


def lp_sweep(buf: np.ndarray, f_start: float, f_end: float, stages: int = 6) -> np.ndarray:
    """A time-varying low-pass, built by crossfading statically filtered copies.

    Used for anything whose brightness changes as it happens: a rope falling away,
    and the muffler closing the world down (§08).
    """
    freqs = np.geomspace(max(f_start, 40.0), max(f_end, 40.0), stages)
    copies = [synth.lowpass(buf, f, order=4, sr=SR) for f in freqs]
    pos = np.linspace(0.0, stages - 1.0, len(buf))
    out = np.zeros(len(buf), dtype=np.float64)
    for i, c in enumerate(copies):
        out += c * np.clip(1.0 - np.abs(pos - i), 0.0, 1.0)
    return out.astype(np.float32)


def loop_breathe(buf: np.ndarray, cycles: int, depth: float) -> np.ndarray:
    """Puts the buffer's amplitude minimum at both ends, `cycles` dips in between.

    `synth.write_wav` applies 2 ms / 6 ms edge fades unconditionally — correct for
    one-shots (a non-zero first sample is a click, and in a quiet mix a click is
    the loudest thing there) but for a seamless loop those fades are a notch at
    the seam. Since write_wav is shared and not mine to change, the fix lives
    here: the content is written so the seam already sits in a trough, and the
    fades then ride on a signal that was quiet anyway. The dip becomes a breath
    instead of a glitch — and a flickering fluorescent or a guttering flare is
    supposed to breathe.
    """
    ph = np.linspace(0.0, 2.0 * np.pi * cycles, len(buf), endpoint=False)
    env = (1.0 - depth) + depth * 0.5 * (1.0 - np.cos(ph))
    return (buf.astype(np.float32) * env.astype(np.float32)).astype(np.float32)


def scaled(modes: list[Mode], k: float, amp: float = 1.0) -> list[Mode]:
    """Pitch-scales a mode set. How variants of one object are made."""
    return [Mode(m.freq * k, m.tau, m.amp * amp) for m in modes]


# Material mode sets. §12's floor table is the Listener's map; these are the
# object-scale equivalents, kept in one place so "wood" means the same thing
# whether it is a door, a plank or a chest.
WOOD_LIGHT = [Mode(215, 0.045, 1.0), Mode(348, 0.030, 0.62), Mode(560, 0.019, 0.34),
              Mode(905, 0.011, 0.16)]
WOOD_HEAVY = [Mode(88, 0.155, 1.0), Mode(139, 0.105, 0.70), Mode(226, 0.068, 0.42),
              Mode(390, 0.038, 0.20), Mode(615, 0.020, 0.09)]
# The top mode is deliberately weak. At 0.17 the spoons measured a 4255 Hz centroid,
# only 1.29x below the jewellery's — too close for two rows of §08's loot table that
# a player is meant to identify instantly by ear. See verify_loot_material_contrast.
METAL_THIN = [Mode(2120, 0.090, 1.0), Mode(3410, 0.058, 0.66), Mode(5230, 0.034, 0.30),
              Mode(7100, 0.019, 0.07)]
METAL_HEAVY = [Mode(155, 0.055, 1.0), Mode(311, 0.036, 0.72), Mode(622, 0.022, 0.45),
               Mode(1244, 0.013, 0.26), Mode(2480, 0.008, 0.12)]
GLASS = [Mode(3080, 0.280, 1.0), Mode(4670, 0.185, 0.55), Mode(6910, 0.100, 0.26)]
LATCH = [Mode(1180, 0.006, 1.0), Mode(2360, 0.004, 0.6), Mode(3900, 0.0022, 0.3)]


def impact(modes: list[Mode], seconds: float, seed: int, noise: float = 0.35,
           noise_tau: float = 0.012) -> np.ndarray:
    return synth.modal_impact(modes, seconds, seed, noise_amount=noise,
                              noise_tau=noise_tau, sr=SR)


def band_noise(seconds: float, seed: int, lo: float, hi: float,
               kind: str = "white") -> np.ndarray:
    src = {"white": synth.white, "pink": synth.pink, "brown": synth.brown}[kind]
    return synth.bandpass(src(seconds, seed, SR), lo, hi, order=2, sr=SR)


LEAD_IN = 0.006
"""Silence prepended to every one-shot.

`synth.write_wav` fades the first 2 ms unconditionally — right in general, since a
non-zero first sample is a click. But most clips here *start* on an impact whose
peak lands inside that window, so the fade both softened the transient and pulled
the measured peak 0.3–3.1 dB below the level `LEVELS` asked for (first run:
loot_sell_credit wanted −10.5 dBFS and measured −13.6). 6 ms of lead-in puts the
fade on silence instead. Not applied to loops, where prepended silence would be a
hole at the seam.
"""


def emit(name: str, buf: np.ndarray, note: str) -> synth.ClipReport:
    """Writes, measures, and refuses to ship anything unusable."""
    if name not in LEVELS:
        raise KeyError(f"{name}: add a peak level to LEVELS before emitting it")
    if name not in LOOPS:
        buf = synth.concat(synth.silence(LEAD_IN, SR), buf)
    stereo = name in STEREO
    path = os.path.join(OUT_DIR, name + ".wav")
    synth.write_wav(path, buf, sr=SR, headroom_db=LEVELS[name], stereo=stereo)
    r = synth.assert_usable(path)
    expect = 2 if stereo else 1
    if r.channels != expect:
        raise AssertionError(f"{path}: {r.channels} channels, expected {expect}")
    REPORTS.append(r)
    ROSTER.append((name, note))
    return r


# ── §05 · §10 — the flashlight switch ───────────────────────────────────────


# Two structurally different snaps per switch direction. Seeds alone were not
# enough: the first run's two `on` variants measured 0.75 waveform correlation —
# the same click at a different gain, which is exactly the "cheap" repeat the
# variants exist to avoid. A switch pressed harder or softer changes which mode
# dominates, so the variants differ in mode balance and decay, not just in noise.
SWITCH_BODY = {
    ("on", 0): [Mode(1450, 0.0026, 0.55), Mode(2750, 0.0019, 1.00),
                Mode(4300, 0.0013, 0.70), Mode(6100, 0.0009, 0.30)],
    ("on", 1): [Mode(1640, 0.0018, 0.28), Mode(2960, 0.0015, 0.80),
                Mode(4680, 0.0017, 1.00), Mode(6560, 0.0008, 0.22)],
    ("off", 0): [Mode(1270, 0.0030, 0.60), Mode(2410, 0.0022, 1.00),
                 Mode(3760, 0.0014, 0.46), Mode(5300, 0.0009, 0.12)],
    ("off", 1): [Mode(1155, 0.0036, 1.00), Mode(2255, 0.0025, 0.78),
                 Mode(3480, 0.0012, 0.34), Mode(4980, 0.0007, 0.09)],
}

SWITCH_SHAPE = {
    # variant: (tick freq, tick gain, tick at, snap at, spring gain)
    ("on", 0): (3200.0, 0.22, 0.000, 0.0085, 0.0),
    ("on", 1): (2780.0, 0.38, 0.004, 0.0130, 0.0),
    ("off", 0): (2820.0, 0.20, 0.000, 0.0110, 0.16),
    ("off", 1): (2450.0, 0.31, 0.005, 0.0155, 0.24),
}


def flashlight_click(seed: int, on: bool, variant: int) -> np.ndarray:
    """A tactile switch. Pre-travel tick, then the snap.

    §05 binds this to `F` and §10 makes turning the light on a dilemma the player
    re-takes every few seconds, so this is the most-repeated clip in the game.
    Two things keep it from becoming grating: it is over in 66 ms, and everything
    above 9 kHz is gone. The `off` set is pitched down and loses its brightest mode
    — a return spring is duller than a detent snapping over, and it should be
    obvious by ear which direction a teammate just threw.
    """
    state = "on" if on else "off"
    tick_f, tick_g, tick_at, snap_at, spring_g = SWITCH_SHAPE[(state, variant)]

    snap = impact(SWITCH_BODY[(state, variant)], 0.045, seed, noise=0.5, noise_tau=0.0013)
    tick = impact([Mode(tick_f, 0.0009, 1.0)], 0.012, seed + 3, noise=0.85, noise_tau=0.0006)

    out = synth.silence(0.06, SR)
    synth.place(out, tick, tick_at, tick_g, SR)
    synth.place(out, snap, snap_at, 1.0, SR)
    if spring_g > 0.0:
        spring = impact([Mode(2050, 0.0012, 1.0)], 0.010, seed + 5, noise=0.7, noise_tau=0.0005)
        synth.place(out, spring, snap_at + 0.015, spring_g, SR)

    out = synth.lowpass(out, 9000.0, order=2, sr=SR)
    return synth.highpass(out, 320.0, order=2, sr=SR)


# ── §03 — battery: the lock on progress ─────────────────────────────────────


def battery_insert(seed: int, k: float = 1.0) -> np.ndarray:
    """Cell slides down the barrel, seats, contacts spring home. §03's 보충."""
    out = synth.silence(0.42, SR)

    slide = band_noise(0.17, seed, 640.0 * k, 4200.0 * k, "pink")
    slide = slide * grain_env(0.17, seed + 1, 900.0, 0.0016, 1.4)
    slide = slide * synth.adsr(0.17, 0.02, 0.05, 0.75, 0.06, SR)
    synth.place(out, slide, 0.0, 0.45, SR)

    seat = impact(scaled([Mode(900, 0.0042, 1.0), Mode(1800, 0.0028, 0.6),
                          Mode(2900, 0.0016, 0.3)], k), 0.05, seed + 2, noise=0.45)
    synth.place(out, seat, 0.175, 1.0, SR)

    contact = impact(scaled(LATCH, 1.15 * k), 0.03, seed + 3, noise=0.6, noise_tau=0.0008)
    synth.place(out, contact, 0.245, 0.30, SR)

    cap = impact(scaled([Mode(720, 0.006, 1.0), Mode(1500, 0.0035, 0.5)], k),
                 0.06, seed + 4, noise=0.4)
    synth.place(out, cap, 0.30, 0.55, SR)
    return synth.highpass(out, 120.0, order=2, sr=SR)


def battery_low_warning(seed: int) -> np.ndarray:
    """Three flickers, the LED driver whine bending down through each one.

    §03: "배터리가 떨어지면 단서를 읽을 수 없다." This is the cue that the window for
    reading a clue is closing — enough to make a player decide whether to spend
    another twenty seconds in the room, not enough to be a jump scare.
    """
    dur = 1.25
    n = _nn(dur)
    t = np.linspace(0.0, 1.0, n, dtype=np.float64)

    dips = [0.22, 0.55, 0.92]
    bend = np.zeros(n, dtype=np.float64)
    gate = np.ones(n, dtype=np.float64)
    for i, centre in enumerate(dips):
        width = 0.055 + 0.015 * i
        shape = np.exp(-((t * dur - centre) ** 2) / (2.0 * width ** 2))
        bend += shape * (0.30 + 0.08 * i)
        gate -= shape * (0.72 + 0.08 * i)
    gate = np.clip(gate, 0.06, 1.0)

    whine = 1180.0 * (1.0 - 0.55 * bend) * (1.0 + 0.02 * ctrl(dur, seed + 1, 6.0)[:n])
    tone = fm_sine(whine) + 0.35 * fm_sine(2.0 * whine) + 0.14 * fm_sine(0.5 * whine)
    tone = tone * gate.astype(np.float32)

    crackle = band_noise(dur, seed + 2, 1800.0, 9000.0) * grain_env(dur, seed + 3, 26.0, 0.004, 2.2)

    out = synth.mix(tone.astype(np.float32), crackle, gains=[1.0, 0.30])
    out = out * (0.35 + 0.65 * gate).astype(np.float32)
    out = synth.highpass(out, 200.0, order=2, sr=SR)
    # The whine settles as the light steadies. Also keeps the clip's peak away from
    # write_wav's 6 ms tail fade, which otherwise pulled this 1.3 dB under its level.
    return synth.fade(out, 0.004, 0.11, SR)


def battery_dead(seed: int) -> np.ndarray:
    """The light dies. §03: no light, no clue, and the trip was for nothing.

    Whine collapses 2100 → 140 Hz, the driver clunks, one hollow low thud — and
    then the last 0.7 s is almost empty. §06 calls silence the game's weapon; this
    is the moment it gets turned on the players. Nothing here is a musical sting,
    because the setback is the absence, not a cue telling you to feel bad.
    """
    dur = 1.75
    out = synth.silence(dur, SR)

    gasp = 1150.0 * (1.0 + 0.05 * ctrl(0.10, seed + 1, 30.0))
    flick = fm_sine(gasp) * synth.adsr(0.10, 0.004, 0.02, 0.55, 0.05, SR)
    synth.place(out, flick.astype(np.float32), 0.0, 0.35, SR)

    coll_n = _nn(0.24)
    fall = np.geomspace(2100.0, 140.0, coll_n)
    collapse = (fm_sine(fall) + 0.3 * fm_sine(2.0 * fall)) * synth.exp_decay(0.24, 0.085, SR)
    synth.place(out, collapse.astype(np.float32), 0.13, 0.75, SR)

    clunk = impact(scaled(METAL_HEAVY, 1.25), 0.16, seed + 2, noise=0.45, noise_tau=0.006)
    synth.place(out, clunk, 0.36, 0.62, SR)

    thud = impact([Mode(70, 0.26, 1.0), Mode(105, 0.17, 0.55), Mode(172, 0.08, 0.22)],
                  0.55, seed + 3, noise=0.18, noise_tau=0.02)
    synth.place(out, thud, 0.40, 1.0, SR)

    # The tail: a room with nothing in it. Present enough that the file does not
    # read as truncated, quiet enough that the player hears the light is gone.
    room = synth.lowpass(synth.brown(0.75, seed + 4, SR), 260.0, order=4, sr=SR)
    room = room * np.linspace(1.0, 0.0, len(room), dtype=np.float32) ** 1.8
    synth.place(out, room, 0.98, 0.035, SR)

    return synth.highpass(out, 32.0, order=2, sr=SR)


# ── §04 · §12 — doors ───────────────────────────────────────────────────────


def _with_real(out: np.ndarray, key: str, seed: int, dur: float,
               band: tuple[float, float], at: float = 0.0,
               amount: float = DOOR_REAL, tame: float = 1.0) -> np.ndarray:
    """Lays the vendored recording of this gesture under the synthesised one.

    Band-limited and centroid-matched to the synthesised body first, for the
    reason `source_bank.match_centroid` gives: a recording brings the room and
    the microphone position along with the object, and the level relationships
    in `LEVELS` were tuned against the model's spectrum, not a recordist's.
    `at` places the recording where the gesture actually starts — a door's creak
    begins after its latch has already released.

    Returns `out` untouched when nothing is vendored, so `gen_items.py` still
    builds every clip on a clean checkout.
    """
    real = source_bank.pick("items", key, seed + 5501, max(0.05, dur - at))
    if real is None:
        return out
    real = synth.bandpass(real, band[0], band[1], sr=SR)
    real = source_bank.match_centroid(
        real, source_bank.band_centroid(out, band[0], band[1]), band[0], band[1])
    real = source_bank.taper_tail(real, 0.30)
    placed = synth.place(synth.silence(len(out) / SR, SR), real, at, sr=SR)
    mixed = source_bank.mix_real(out, placed, amount)

    # Recover the crest the recording spent.
    #
    # `LEVELS` is a table of *peak* targets, so a clip that gets peakier gets
    # quieter: a real door contributes a harder transient than the modelled one,
    # and measured against the synthesised originals that cost door_close 5.3 dB
    # and door_open 4.7 dB of RMS at an identical peak. §04's whole reason for
    # these clips is that using a door is loud enough to blind the 청음사, so
    # losing 5 dB of loudness to gain a transient is the wrong trade.
    #
    # tanh rather than a limiter because it is what this file already uses for
    # weight (see door_close's body and door_lock's bolt), and because a gesture
    # this short has no room for a release envelope.
    return synth.saturate(norm(mixed), 1.0 + 1.9 * tame) if tame > 0.0 else mixed


def door_open(seed: int, f0: float, f1: float, k: float) -> np.ndarray:
    """Latch release, wood taking its own weight, then a long binding creak.

    §04's 청음사 loses his own information when he opens a door. That drawback only
    exists if the door is loud, so this runs 1.4 s and is mixed near the top of the
    ordinary-interaction band.
    """
    dur = 1.45
    out = synth.silence(dur, SR)

    synth.place(out, impact(scaled(LATCH, 0.95 * k), 0.05, seed + 1, noise=0.55,
                            noise_tau=0.0012), 0.0, 0.55, SR)
    synth.place(out, impact(scaled(WOOD_HEAVY, 1.1 * k), 0.30, seed + 2, noise=0.22),
                0.03, 0.45, SR)

    synth.place(out, hinge_creak(0.92, seed + 3, f0, f1, 24.0, 44.0, 0.16), 0.10, 0.85, SR)

    rub = band_noise(0.85, seed + 4, 190.0, 1250.0, "pink")
    rub = rub * grain_env(0.85, seed + 5, 260.0, 0.003, 1.3)
    rub = rub * synth.adsr(0.85, 0.12, 0.25, 0.6, 0.3, SR)
    synth.place(out, rub, 0.12, 0.30, SR)

    synth.place(out, impact(scaled(WOOD_LIGHT, 0.85 * k), 0.24, seed + 6, noise=0.3),
                1.06, 0.42, SR)
    out = _with_real(out, "hinge", seed, dur, band=(140.0, 6200.0), at=0.10, tame=0.6)
    return synth.highpass(out, 60.0, order=2, sr=SR)


def door_close(seed: int, k: float, firm: float) -> np.ndarray:
    """Swing, strike plate, wood body. `firm` decides gentle push vs solid shut."""
    dur = 0.95
    out = synth.silence(dur, SR)

    swing = band_noise(0.40, seed + 1, 110.0, 950.0, "pink")
    swing = swing * (np.linspace(0.0, 1.0, len(swing), dtype=np.float32) ** 2.2)
    synth.place(out, swing, 0.0, 0.34 * firm, SR)

    synth.place(out, hinge_creak(0.34, seed + 2, 520.0 * k, 380.0 * k, 30.0, 20.0, 0.10),
                0.05, 0.22, SR)

    strike = impact(scaled(LATCH, 1.05 * k), 0.05, seed + 3, noise=0.6, noise_tau=0.0011)
    synth.place(out, strike, 0.415, 0.85, SR)

    body = impact(scaled(WOOD_HEAVY, 1.0 * k), 0.36, seed + 4, noise=0.4, noise_tau=0.008)
    body = synth.saturate(norm(body), 1.4 + 0.8 * firm)
    synth.place(out, body, 0.40, 1.0, SR)

    settle = impact(scaled(WOOD_LIGHT, 0.9 * k), 0.14, seed + 5, noise=0.35)
    synth.place(out, settle, 0.58, 0.16, SR)
    out = _with_real(out, "doorbody", seed, dur, band=(90.0, 7000.0), at=0.20, tame=0.6)
    return synth.highpass(out, 45.0, order=2, sr=SR)


def door_lock(seed: int, k: float) -> np.ndarray:
    """A deadbolt. §04's Engineer, and what a teammate on the wrong side hears.

    Shape is the whole design: mechanism clicks build expectation, the bolt slam
    resolves it with real low weight, and then the clip *stops*. No reverb tail,
    no ring-out — 0.86 s and gone. §12 puts a lockable door on the neck of a
    순환로, so this sound is frequently the moment a route stopped existing.
    Finality is an abrupt ending, not a long one.
    """
    dur = 0.86
    out = synth.silence(dur, SR)

    for i, at in enumerate((0.0, 0.072, 0.148)):
        clk = impact(scaled(LATCH, (0.92 + 0.10 * i) * k), 0.04, seed + 10 + i,
                     noise=0.6, noise_tau=0.0010)
        synth.place(out, clk, at, 0.30 + 0.06 * i, SR)

    turn = band_noise(0.30, seed + 2, 850.0, 4600.0)
    turn = turn * grain_env(0.30, seed + 3, 130.0, 0.0025, 1.8)
    synth.place(out, turn, 0.01, 0.22, SR)

    bolt = impact(scaled(METAL_HEAVY, 1.0 * k), 0.34, seed + 4, noise=0.5, noise_tau=0.004)
    bolt = synth.saturate(norm(bolt), 2.2)
    synth.place(out, bolt, 0.30, 1.0, SR)

    # The low end is what makes the bolt read as final rather than merely sharp.
    # First run had door_lock at 3 dB more peak than door_close but *less* RMS —
    # a slam with no body is a tick. This is the weight, and it is the reason the
    # sound survives being heard from the wrong side of the door.
    sub = impact([Mode(62, 0.135, 1.0), Mode(96, 0.082, 0.5), Mode(148, 0.045, 0.22)],
                 0.34, seed + 5, noise=0.1)
    synth.place(out, sub, 0.298, 0.80, SR)

    ring = synth.resonator(impact(scaled(METAL_THIN, 0.72 * k), 0.16, seed + 6, noise=0.4),
                           1580.0 * k, q=26.0, sr=SR)
    synth.place(out, norm(ring), 0.315, 0.20, SR)

    synth.place(out, impact(scaled(LATCH, 0.8 * k), 0.05, seed + 7, noise=0.5,
                            noise_tau=0.0012), 0.52, 0.26, SR)

    # Before the drive, so the saturation compresses the whole assembled bolt —
    # the recording included — rather than leaving it sitting on top untouched.
    out = _with_real(out, "bolt", seed, dur, band=(110.0, 8000.0), at=0.0, tame=0.0)
    out = synth.highpass(out, 38.0, order=2, sr=SR)
    # Drive, then a hard 35 ms cut. The drive is why the bolt out-reads the close it
    # follows (measured, not assumed — see verify_cost_ladder); the cut is why it
    # sounds like a door that is not opening again.
    out = synth.saturate(norm(out), 1.8)
    return synth.fade(out, 0.001, 0.035, SR)


# ── §04 — barricades ────────────────────────────────────────────────────────


def barricade_place(seed: int, k: float) -> np.ndarray:
    """Planks dragged into the frame, dropped, braced. Quiet, patient work (§04)."""
    dur = 1.35
    out = synth.silence(dur, SR)

    scrape = band_noise(0.32, seed + 1, 280.0, 2600.0, "pink")
    scrape = scrape * grain_env(0.32, seed + 2, 320.0, 0.0028, 1.4)
    scrape = scrape * synth.adsr(0.32, 0.05, 0.10, 0.7, 0.12, SR)
    synth.place(out, scrape, 0.0, 0.42, SR)

    synth.place(out, impact(scaled(WOOD_HEAVY, 1.15 * k), 0.32, seed + 3, noise=0.42),
                0.30, 1.0, SR)
    synth.place(out, impact(scaled(WOOD_HEAVY, 0.92 * k), 0.30, seed + 4, noise=0.40),
                0.47, 0.78, SR)

    shove = impact(scaled(WOOD_HEAVY, 1.35 * k), 0.40, seed + 5, noise=0.30, noise_tau=0.02)
    synth.place(out, shove, 0.68, 0.70, SR)
    synth.place(out, hinge_creak(0.36, seed + 6, 300.0 * k, 235.0 * k, 18.0, 12.0, 0.12),
                0.72, 0.30, SR)

    synth.place(out, impact(scaled(WOOD_LIGHT, 1.0 * k), 0.20, seed + 7, noise=0.3),
                1.02, 0.20, SR)
    return synth.highpass(out, 55.0, order=2, sr=SR)


def barricade_break(seed: int, k: float) -> np.ndarray:
    """The monster comes through. A threat cue, so it is mixed loud.

    Strain, a burst of splintering cracks, then planks clattering down. If a player
    hears this they have seconds, which is why it sits 4 dB above placing it.
    """
    dur = 1.55
    out = synth.silence(dur, SR)
    g = synth.rng(seed)

    strain_n = _nn(0.28)
    sf = np.geomspace(96.0 * k, 74.0 * k, strain_n) * (
        1.0 + 0.12 * ctrl(0.28, seed + 1, 8.0)[:strain_n])
    strain = fm_sine(sf) + 0.5 * fm_sine(2.7 * sf)
    strain = strain * stick_slip(0.28, np.full(strain_n, 30.0), seed + 2)
    strain = strain * synth.adsr(0.28, 0.08, 0.10, 0.8, 0.08, SR)
    synth.place(out, norm(strain), 0.0, 0.55, SR)

    for i in range(6):
        at = 0.25 + float(g.uniform(0.0, 0.30))
        pitch = float(g.uniform(0.85, 1.75)) * k
        crack = impact(scaled(WOOD_LIGHT, pitch), 0.13, seed + 20 + i, noise=0.7,
                       noise_tau=0.0022)
        synth.place(out, synth.saturate(norm(crack), 2.0), at, float(g.uniform(0.5, 1.0)), SR)

    splinter = band_noise(0.78, seed + 3, 700.0, 9500.0)
    splinter = splinter * grain_env(0.78, seed + 4, 260.0, 0.0035, 2.2)
    splinter = splinter * synth.exp_decay(0.78, 0.26, SR)
    synth.place(out, splinter, 0.26, 0.72, SR)

    for i in range(12):
        at = 0.48 + float(g.uniform(0.0, 0.72))
        clatter = impact(scaled(WOOD_LIGHT, float(g.uniform(0.6, 1.3)) * k), 0.22,
                         seed + 40 + i, noise=0.45)
        synth.place(out, clatter, at, float(g.uniform(0.15, 0.60)) * (1.0 - 0.5 * (at / dur)), SR)

    debris = impact([Mode(76, 0.14, 1.0), Mode(124, 0.09, 0.5)], 0.35, seed + 5, noise=0.25)
    synth.place(out, debris, 0.50, 0.55, SR)

    # Dust and falling grit under it all. Without this the break is a sequence of
    # transients whose RMS lands under a barricade being *placed* — measured on the
    # first run at 1.29x, where the threat cue has to dominate the prep sound.
    rumble = band_noise(0.95, seed + 6, 50.0, 300.0, "brown")
    rumble = rumble * (np.linspace(1.0, 0.0, _nn(0.95), dtype=np.float32) ** 1.3)
    synth.place(out, rumble, 0.27, 0.55, SR)

    out = synth.highpass(out, 45.0, order=2, sr=SR)
    # Splintering is a harsh, compressed event, and driving it is also what lifts
    # the RMS clear of `barricade_place`: at −4.5 dBFS peak against place's −11 the
    # measured ratio was still only 1.71x, because a burst of transients averages
    # low. The barricade coming apart has to dominate the barricade going up.
    return synth.saturate(norm(out), 1.5)


# ── §04 — the noise trap ────────────────────────────────────────────────────


def noisetrap_arm(seed: int) -> np.ndarray:
    """Spring tensioned one ratchet tooth at a time.

    §04: "즉석 사용 불가 — 사전 준비형." Arming is quiet and slow. The clicks slow
    down and rise in pitch as the spring gets harder to pull, which is the only
    cue the player has that it is nearly set.
    """
    dur = 1.05
    out = synth.silence(dur, SR)

    at = 0.02
    for i, gap in enumerate((0.078, 0.084, 0.094, 0.108, 0.126, 0.150)):
        pitch = 1.0 + 0.055 * i
        clk = impact(scaled(LATCH, pitch), 0.045, seed + 10 + i, noise=0.55, noise_tau=0.0011)
        zing = synth.resonator(clk, 1750.0 * pitch, q=34.0, sr=SR)
        synth.place(out, synth.mix(clk, norm(zing), gains=[1.0, 0.28]), at,
                    0.55 + 0.07 * i, SR)
        at += gap

    lock = impact(scaled([Mode(760, 0.008, 1.0), Mode(1520, 0.005, 0.55),
                          Mode(2700, 0.0025, 0.3)], 1.0), 0.09, seed + 3, noise=0.5)
    synth.place(out, lock, at + 0.03, 1.0, SR)

    tick = impact(scaled(LATCH, 1.4), 0.03, seed + 4, noise=0.6, noise_tau=0.0008)
    synth.place(out, tick, at + 0.14, 0.22, SR)
    return synth.highpass(out, 260.0, order=2, sr=SR)


def noisetrap_trigger(seed: int) -> np.ndarray:
    """Loud on purpose (§04). A can of bolts going over.

    §04 notes the trap can catch the 주자 — the Engineer's own teammate — so this
    has to be unmistakable through a wall and at range: a snap, then ~30 metal
    impacts thinning out over a second, a harsh rattle bed, a piercing 2.9 kHz
    ring, and low body so it still carries after distance attenuation eats the
    top. This is the loudest positional clip in the set (−3 dBFS) and the highest
    RMS of any one-shot; `verify_cost_ladder` pins that against the flashlight.
    """
    dur = 1.65
    out = synth.silence(dur, SR)
    g = synth.rng(seed)

    snap = impact(scaled(METAL_THIN, 0.9), 0.12, seed + 1, noise=0.8, noise_tau=0.0016)
    synth.place(out, synth.saturate(norm(snap), 2.4), 0.0, 1.0, SR)
    whip = band_noise(0.09, seed + 2, 1400.0, 12000.0) * synth.exp_decay(0.09, 0.014, SR)
    synth.place(out, whip, 0.0, 0.55, SR)
    synth.place(out, impact([Mode(138, 0.06, 1.0), Mode(92, 0.09, 0.7)], 0.28, seed + 3,
                            noise=0.2), 0.004, 0.75, SR)

    clatter = synth.silence(dur, SR)
    for i in range(30):
        # Dense at the front, thinning: the bolts land, bounce, and settle.
        at = 0.03 + 1.10 * float(g.uniform(0.0, 1.0)) ** 1.7
        pitch = float(g.uniform(0.75, 1.65))
        hit = impact(scaled(METAL_THIN, pitch), 0.20, seed + 50 + i, noise=0.5,
                     noise_tau=0.0025)
        synth.place(clatter, hit, at, float(g.uniform(0.35, 1.0)), SR)
    for i in range(6):
        at = 0.02 + 0.9 * float(g.uniform(0.0, 1.0)) ** 1.5
        low = impact(scaled(METAL_HEAVY, float(g.uniform(0.85, 1.4))), 0.26,
                     seed + 90 + i, noise=0.4, noise_tau=0.004)
        synth.place(clatter, low, at, float(g.uniform(0.3, 0.7)), SR)
    clatter = synth.saturate(norm(clatter), 2.0)
    synth.place(out, clatter, 0.0, 1.0, SR)

    rattle = band_noise(1.10, seed + 4, 1100.0, 7500.0)
    rattle = rattle * grain_env(1.10, seed + 5, 420.0, 0.0035, 1.6)
    rattle = rattle * synth.exp_decay(1.10, 0.34, SR)
    synth.place(out, rattle, 0.01, 0.60, SR)

    ring = synth.resonator(clatter, 2900.0, q=40.0, sr=SR)
    synth.place(out, norm(ring), 0.0, 0.28, SR)

    out = synth.highpass(out, 55.0, order=2, sr=SR)
    return synth.saturate(norm(out), 1.7)


# ── §04 · §08 — the safe (금고 속 문서) ─────────────────────────────────────


def safe_dial_turn_loop(seed: int) -> np.ndarray:
    """One second of steady dial rotation. Loopable while the player holds it.

    §08 puts 금고 속 문서 behind the Engineer and §04 lists "금고를 연다" as his
    objective role. Detents land on a 100 ms grid so the loop reads as a constant
    rate; the friction bed breathes to a trough at the seam (see `loop_breathe`).
    """
    dur = 1.0
    out = synth.silence(dur, SR)

    bed = band_noise(dur, seed + 1, 190.0, 1500.0, "pink")
    bed = bed * (0.55 + 0.45 * (0.5 + 0.5 * ctrl(dur, seed + 2, 7.0)))
    synth.place(out, bed, 0.0, 0.30, SR)

    for i in range(10):
        pitch = 1.0 + 0.02 * (i % 3)
        clk = impact(scaled(LATCH, 0.82 * pitch), 0.035, seed + 20 + i, noise=0.5,
                     noise_tau=0.0009)
        ring = synth.resonator(clk, 2350.0 * pitch, q=30.0, sr=SR)
        synth.place(out, synth.mix(clk, norm(ring), gains=[1.0, 0.22]),
                    0.05 + 0.10 * i, 0.85 + 0.05 * ((i * 7) % 3), SR)

    out = synth.highpass(out, 150.0, order=2, sr=SR)
    return loop_breathe(out, 1, 0.30)


def safe_open(seed: int) -> np.ndarray:
    """Handle, three bolts retracting, a heavy door swinging on a cavity.

    The payoff for §08's highest-value carryable loot. Weighty rather than
    triumphant — the document still has to be carried out (무게 2).
    """
    dur = 2.25
    out = synth.silence(dur, SR)

    grind = band_noise(0.26, seed + 1, 420.0, 3400.0)
    grind = grind * grain_env(0.26, seed + 2, 200.0, 0.003, 1.6)
    grind = grind * synth.adsr(0.26, 0.03, 0.08, 0.7, 0.10, SR)
    synth.place(out, grind, 0.0, 0.40, SR)

    for i, (at, k) in enumerate(((0.30, 1.0), (0.425, 0.88), (0.555, 1.12))):
        thunk = impact(scaled(METAL_HEAVY, k), 0.30, seed + 10 + i, noise=0.45,
                       noise_tau=0.004)
        synth.place(out, synth.saturate(norm(thunk), 1.8), at, 0.85 - 0.08 * i, SR)

    synth.place(out, hinge_creak(0.80, seed + 3, 132.0, 224.0, 16.0, 30.0, 0.18),
                0.62, 0.70, SR)

    mass = band_noise(0.70, seed + 4, 55.0, 420.0, "brown")
    mass = mass * synth.adsr(0.70, 0.12, 0.20, 0.6, 0.30, SR)
    synth.place(out, mass, 0.62, 0.45, SR)

    cavity = impact([Mode(178, 0.34, 1.0), Mode(268, 0.24, 0.6), Mode(431, 0.16, 0.35),
                     Mode(690, 0.09, 0.18)], 0.9, seed + 5, noise=0.2, noise_tau=0.02)
    synth.place(out, cavity, 0.30, 0.35, SR)

    stop = impact(scaled(METAL_HEAVY, 0.8), 0.45, seed + 6, noise=0.35, noise_tau=0.01)
    synth.place(out, synth.saturate(norm(stop), 1.6), 1.58, 0.70, SR)
    return synth.highpass(out, 42.0, order=2, sr=SR)


# ── §03 · §04 · §10 — zone lighting ─────────────────────────────────────────


def breaker_throw(seed: int) -> np.ndarray:
    """An industrial knife switch. §10: "구역을 밝힌다 → 괴물이 그쪽으로 온다."

    Lever travel, the contactor slamming, an arc, and the hum starting to rise.
    The arc is what makes this feel like a decision with a consequence rather than
    a UI toggle — the Engineer has just told the monster where the team is.
    """
    dur = 1.0
    out = synth.silence(dur, SR)

    travel = band_noise(0.10, seed + 1, 380.0, 3000.0)
    travel = travel * grain_env(0.10, seed + 2, 260.0, 0.0025, 1.5)
    synth.place(out, travel, 0.0, 0.45, SR)

    slam = impact(scaled(METAL_HEAVY, 1.05), 0.28, seed + 3, noise=0.5, noise_tau=0.003)
    synth.place(out, synth.saturate(norm(slam), 2.1), 0.10, 1.0, SR)
    synth.place(out, impact([Mode(58, 0.09, 1.0), Mode(88, 0.055, 0.5)], 0.22, seed + 4,
                            noise=0.12), 0.10, 0.55, SR)

    arc = band_noise(0.22, seed + 5, 2400.0, 13000.0)
    arc = arc * grain_env(0.22, seed + 6, 150.0, 0.0018, 2.6)
    arc = arc * synth.exp_decay(0.22, 0.07, SR)
    synth.place(out, arc, 0.105, 0.60, SR)

    swell_n = _nn(0.62)
    swell_env = np.clip(np.linspace(-0.3, 1.0, swell_n), 0.0, 1.0) ** 1.4
    hum = (synth.sine(60.0, 0.62, sr=SR) * 0.55 + synth.sine(120.0, 0.62, sr=SR)
           + synth.sine(180.0, 0.62, sr=SR) * 0.42 + synth.sine(360.0, 0.62, sr=SR) * 0.16)
    synth.place(out, (hum * swell_env.astype(np.float32)), 0.16, 0.38, SR)
    return synth.highpass(out, 40.0, order=2, sr=SR)


def zone_hum_loop(seed: int) -> np.ndarray:
    """A lit zone, humming. §03 warns the monster comes toward it — so this doubles
    as a standing warning, not ambience.

    3.000 s is exactly 180 cycles of 60 Hz, so every harmonic and the ballast whine
    (1200/1800 Hz, both multiples of 60) closes its phase at the seam. Two flicker
    dips, minima at both ends. Peak sits mid-ladder because the clip never stops:
    its RMS is the highest of any sustained clip here, which is what "the monster
    is coming toward this room" should cost you.
    """
    dur = 3.0
    mains = (synth.sine(60.0, dur, sr=SR) * 0.62
             + synth.sine(120.0, dur, sr=SR) * 1.0
             + synth.sine(180.0, dur, sr=SR) * 0.40
             + synth.sine(240.0, dur, sr=SR) * 0.22
             + synth.sine(360.0, dur, sr=SR) * 0.12
             + synth.sine(480.0, dur, sr=SR) * 0.07)

    whine = (synth.sine(1200.0, dur, sr=SR) * 1.0
             + synth.sine(1800.0, dur, sr=SR) * 0.35
             + synth.sine(2400.0, dur, sr=SR) * 0.14)
    whine = whine * (0.55 + 0.45 * (0.5 + 0.5 * ctrl(dur, seed + 1, 4.0)))

    room = band_noise(dur, seed + 2, 300.0, 3200.0, "pink")

    out = synth.mix(mains, whine, room, gains=[1.0, 0.075, 0.045])
    out = synth.highpass(out, 42.0, order=2, sr=SR)
    return loop_breathe(out, 2, 0.26)


# ── §08 · §11 — the flare (조명탄) ──────────────────────────────────────────


def flare_ignite(seed: int) -> np.ndarray:
    """Striker, then ignition. §08: 1회용 · 소리를 낸다.

    Mixed second-loudest in the set. §11 makes the flare the purchase that covers a
    missing Engineer, so its price has to be felt: everything within earshot knows
    a flare just went off.
    """
    dur = 1.20
    out = synth.silence(dur, SR)

    for i, at in enumerate((0.0, 0.055)):
        strike = band_noise(0.055, seed + 1 + i, 1500.0, 11000.0)
        strike = strike * grain_env(0.055, seed + 10 + i, 900.0, 0.0012, 2.0)
        strike = strike * synth.exp_decay(0.055, 0.018, SR)
        synth.place(out, strike, at, 0.5 + 0.2 * i, SR)

    synth.place(out, impact([Mode(80, 0.095, 1.0), Mode(131, 0.06, 0.6),
                             Mode(212, 0.035, 0.3)], 0.30, seed + 3, noise=0.25), 0.12, 0.85, SR)

    roar_n = _nn(1.05)
    roar = band_noise(1.05, seed + 4, 210.0, 8500.0, "pink")
    roar = synth.resonator(roar, 1400.0, q=1.6, sr=SR)
    swell = np.clip(np.linspace(-0.12, 1.0, roar_n) * 6.0, 0.0, 1.0)
    swell = swell * (0.72 + 0.28 * (0.5 + 0.5 * ctrl(1.05, seed + 5, 9.0)))
    roar = roar * swell.astype(np.float32)
    roar = roar * (0.7 + 0.3 * grain_env(1.05, seed + 6, 40.0, 0.008, 1.8))
    synth.place(out, roar, 0.12, 1.0, SR)

    low = band_noise(1.0, seed + 7, 55.0, 240.0, "brown")
    synth.place(out, low * np.clip(np.linspace(-0.1, 1.0, len(low)), 0, 1).astype(np.float32),
                0.14, 0.35, SR)
    out = synth.highpass(out, 48.0, order=2, sr=SR)
    # Mild drive. §08 charges for this item in noise, and a peak level alone does
    # not buy loudness — the first run measured −4 dBFS peak at only 0.080 RMS
    # because the swell left the clip mostly quiet.
    return synth.saturate(norm(out), 1.6)


def flare_burn_loop(seed: int) -> np.ndarray:
    """3 s of chemical roar. This is where §08's "소리를 낸다" is actually paid.

    Loops for the flare's whole life, so it is the item's real drawback: a lit
    flare is a beacon in both channels the monster uses. High RMS deliberately —
    `verify_cost_ladder` pins it against chalk, whose §08 cost is a visual trail
    instead of noise.
    """
    dur = 3.0
    bed = band_noise(dur, seed + 1, 250.0, 7000.0, "pink")
    bed = synth.resonator(bed, 1420.0, q=1.5, sr=SR)
    bed = norm(bed) * (0.84 + 0.16 * grain_env(dur, seed + 2, 30.0, 0.010, 1.7))

    sputter = band_noise(dur, seed + 3, 900.0, 9000.0)
    sputter = sputter * grain_env(dur, seed + 4, 26.0, 0.005, 2.4)

    rumble = band_noise(dur, seed + 5, 58.0, 300.0, "brown")

    out = synth.mix(bed, sputter, rumble, gains=[1.0, 0.30, 0.28])
    out = synth.highpass(out, 48.0, order=2, sr=SR)
    # Drive before the loop envelope: a burning flare is a harsh, dense sound, and
    # density is what keeps its RMS above the zone hum's. A flare that measures
    # quieter than a fluorescent tube is not paying §08's price.
    out = synth.saturate(norm(out), 1.9)
    return loop_breathe(out, 3, 0.16)


def flare_die(seed: int) -> np.ndarray:
    """The flare gutters out — two dying surges, a fizzle, then ember hiss.

    §08 makes it single-use, so this sound is also "your light source is gone",
    which under §03 means clue-reading in this zone just ended.
    """
    dur = 1.75
    out = synth.silence(dur, SR)

    bed = band_noise(1.05, seed + 1, 240.0, 6800.0, "pink")
    bed = synth.resonator(bed, 1380.0, q=1.5, sr=SR)
    decay = np.linspace(1.0, 0.0, len(bed), dtype=np.float32) ** 1.6
    surge = np.ones(len(bed), dtype=np.float32)
    tt = np.arange(len(bed), dtype=np.float32) / SR
    for centre, amp in ((0.30, 0.45), (0.58, 0.30)):
        surge = surge + amp * np.exp(-((tt - centre) ** 2) / (2.0 * 0.045 ** 2)).astype(np.float32)
    bed = norm(bed) * decay * surge * (0.6 + 0.4 * grain_env(1.05, seed + 2, 34.0, 0.007, 2.0))
    synth.place(out, bed, 0.0, 1.0, SR)

    fizz = band_noise(0.22, seed + 3, 1200.0, 10500.0)
    fizz = fizz * grain_env(0.22, seed + 4, 320.0, 0.0025, 2.2)
    fizz = fizz * synth.exp_decay(0.22, 0.055, SR)
    synth.place(out, fizz, 0.96, 0.55, SR)

    ember = band_noise(0.70, seed + 5, 700.0, 5200.0)
    ember = ember * grain_env(0.70, seed + 6, 14.0, 0.006, 2.6)
    ember = ember * (np.linspace(1.0, 0.0, _nn(0.70), dtype=np.float32) ** 2.0)
    synth.place(out, ember, 1.02, 0.16, SR)
    return synth.highpass(out, 120.0, order=2, sr=SR)


# ── §08 — chalk, rope ───────────────────────────────────────────────────────


def chalk_mark(seed: int, length: float, bright: float) -> np.ndarray:
    """A single dry stroke. §08's 분필 costs a trail the monster follows, not noise,
    so this is the quietest clip in the set (−22 dBFS) and stays that way."""
    out = band_noise(length, seed + 1, 1700.0 * bright, 11000.0)
    out = out * grain_env(length, seed + 2, 1600.0, 0.0011, 1.5)
    dust = band_noise(length, seed + 3, 550.0, 1800.0)
    dust = dust * grain_env(length, seed + 4, 700.0, 0.0018, 1.8)
    out = synth.mix(out, dust, gains=[1.0, 0.35])
    env = synth.adsr(length, 0.012, 0.05, 0.72, 0.06, SR)
    # Brightness tilts down across the stroke as the chalk wears flat.
    out = lp_sweep(out * env, 10000.0, 4200.0, 4)
    return synth.highpass(out, 500.0, order=2, sr=SR)


def rope_deploy(seed: int) -> np.ndarray:
    """A coil thrown over an edge, unravelling away, the end hitting below.

    §08: 층 사이 지름길, 편도만. The falling half uses `lp_sweep` — the rustle gets
    darker as it drops, which is the only distance cue a rope has.
    """
    dur = 1.40
    out = synth.silence(dur, SR)

    toss = band_noise(0.22, seed + 1, 420.0, 4200.0, "pink")
    toss = toss * grain_env(0.22, seed + 2, 520.0, 0.0022, 1.5)
    toss = toss * synth.adsr(0.22, 0.02, 0.06, 0.7, 0.08, SR)
    synth.place(out, toss, 0.0, 0.60, SR)

    fall = band_noise(0.62, seed + 3, 300.0, 5200.0, "pink")
    fall = fall * grain_env(0.62, seed + 4, 260.0, 0.0035, 1.7)
    fall = lp_sweep(fall, 6000.0, 900.0, 6)
    fall = fall * (np.linspace(1.0, 0.45, _nn(0.62), dtype=np.float32))
    synth.place(out, fall, 0.16, 0.75, SR)

    synth.place(out, impact([Mode(95, 0.05, 1.0), Mode(152, 0.032, 0.55),
                             Mode(248, 0.018, 0.25)], 0.24, seed + 5, noise=0.45), 0.78, 0.85, SR)

    tension = hinge_creak(0.30, seed + 6, 260.0, 320.0, 12.0, 9.0, 0.20)
    synth.place(out, tension, 0.92, 0.22, SR)
    return synth.highpass(out, 90.0, order=2, sr=SR)


# ── §08 — loot, by material class ───────────────────────────────────────────


def loot_metal_small(seed: int, k: float) -> np.ndarray:
    """은수저 · 잡동사니 (무게 1). Thin cutlery: two bright clinks and a handful."""
    dur = 0.36
    out = synth.silence(dur, SR)
    synth.place(out, impact(scaled(METAL_THIN, k), 0.22, seed + 1, noise=0.4,
                            noise_tau=0.0022), 0.0, 1.0, SR)
    synth.place(out, impact(scaled(METAL_THIN, 1.11 * k), 0.20, seed + 2, noise=0.45,
                            noise_tau=0.002), 0.058, 0.62, SR)
    cloth = band_noise(0.20, seed + 3, 700.0, 5000.0, "pink")
    cloth = cloth * grain_env(0.20, seed + 4, 300.0, 0.0025, 1.6)
    synth.place(out, cloth, 0.10, 0.22, SR)
    return synth.highpass(out, 400.0, order=2, sr=SR)


def loot_glass_jewel(seed: int, k: float) -> np.ndarray:
    """회중시계 · 반지 (무게 1, 효율 최고). One clean ting, a chain, two faint
    escapement ticks — the watch is still running."""
    dur = 0.62
    out = synth.silence(dur, SR)
    synth.place(out, impact(scaled(GLASS, k), 0.55, seed + 1, noise=0.22,
                            noise_tau=0.0016), 0.0, 1.0, SR)
    chain = band_noise(0.24, seed + 2, 2200.0, 9500.0)
    chain = chain * grain_env(0.24, seed + 3, 90.0, 0.0016, 2.2)
    synth.place(out, chain, 0.03, 0.30, SR)
    for i, at in enumerate((0.20, 0.33)):
        tick = impact([Mode(4100 * k, 0.0016, 1.0), Mode(6300 * k, 0.0009, 0.5)],
                      0.02, seed + 10 + i, noise=0.5, noise_tau=0.0005)
        synth.place(out, tick, at, 0.14, SR)
    return synth.highpass(out, 900.0, order=2, sr=SR)


def loot_paper(seed: int, k: float) -> np.ndarray:
    """금고 속 문서 (무게 2, 가치 높음). Dry crinkle, grabbed then folded.

    No tonal content at all — that is the material's identity, and it keeps the
    document from sounding like a treasure chime when it is the thing §04's
    Engineer had to open a safe for.
    """
    dur = 0.52
    out = synth.silence(dur, SR)
    grab = band_noise(0.18, seed + 1, 1200.0 * k, 9000.0)
    grab = grab * grain_env(0.18, seed + 2, 420.0, 0.0016, 2.1)
    grab = grab * synth.adsr(0.18, 0.008, 0.05, 0.6, 0.07, SR)
    synth.place(out, grab, 0.0, 0.9, SR)
    fold = band_noise(0.26, seed + 3, 900.0 * k, 7500.0)
    fold = fold * grain_env(0.26, seed + 4, 240.0, 0.0022, 2.3)
    fold = fold * synth.adsr(0.26, 0.02, 0.08, 0.55, 0.10, SR)
    synth.place(out, fold, 0.21, 1.0, SR)
    return synth.highpass(out, 700.0, order=2, sr=SR)


def loot_wood_heavy(seed: int, k: float) -> np.ndarray:
    """대형 초상화 · 궤짝 (무게 5, 2인 운반 or 극심한 감속).

    §08 calls the big loot the reason its best scene exists — two players carrying
    a chest down a corridor when the monster arrives. Weight is audible here: a
    dragged scrape, a strained creak, and a body eight times the level of the
    spoons. A player should hear their own greed.
    """
    dur = 1.0
    out = synth.silence(dur, SR)

    drag = band_noise(0.36, seed + 1, 150.0, 1300.0, "pink")
    drag = drag * grain_env(0.36, seed + 2, 220.0, 0.0035, 1.4)
    drag = drag * synth.adsr(0.36, 0.05, 0.12, 0.7, 0.12, SR)
    synth.place(out, drag, 0.0, 0.55, SR)

    synth.place(out, impact(scaled(WOOD_HEAVY, k), 0.55, seed + 3, noise=0.35,
                            noise_tau=0.012), 0.26, 1.0, SR)
    synth.place(out, hinge_creak(0.34, seed + 4, 220.0 * k, 178.0 * k, 15.0, 11.0, 0.16),
                0.30, 0.35, SR)
    synth.place(out, impact(scaled(WOOD_HEAVY, 0.82 * k), 0.34, seed + 5, noise=0.3),
                0.62, 0.45, SR)
    out = synth.highpass(out, 45.0, order=2, sr=SR)
    # Driven so 무게 5 measures at least twice the RMS of 무게 1 (verify_cost_ladder).
    # Weight has to be audible at the moment of the decision, because §08's whole
    # loot design is 무게 vs 가치 and the player commits before feeling the slowdown.
    return synth.saturate(norm(out), 1.5)


# ── §08 — the vehicle shop ──────────────────────────────────────────────────


def loot_sell_credit(seed: int) -> np.ndarray:
    """Loot lands in the truck bed, then a short warm credit tone.

    §08's 공용 지갑 is the negotiation engine of the game, so the confirmation is
    kept to two notes a fifth apart, low-passed, no bright bell. The truck is the
    only safe place in the match; it does not need a slot-machine.
    """
    dur = 1.0
    out = synth.silence(dur, SR)

    panel = impact([Mode(105, 0.070, 1.0), Mode(232, 0.048, 0.62), Mode(410, 0.032, 0.34),
                    Mode(781, 0.018, 0.16)], 0.40, seed + 1, noise=0.45, noise_tau=0.005)
    synth.place(out, synth.saturate(norm(panel), 1.5), 0.0, 1.0, SR)

    for i, (freq, at) in enumerate(((622.25, 0.26), (932.33, 0.40))):
        tone = (synth.sine(freq, 0.34, sr=SR)
                + 0.22 * synth.sine(freq * 2.0, 0.34, sr=SR)
                + 0.08 * synth.sine(freq * 3.0, 0.34, sr=SR))
        tone = tone * synth.adsr(0.34, 0.010, 0.10, 0.42, 0.20, SR)
        synth.place(out, synth.lowpass(tone, 4200.0, order=2, sr=SR), at, 0.42 - 0.06 * i, SR)

    return synth.highpass(out, 60.0, order=2, sr=SR)


def shop_purchase_confirm(seed: int) -> np.ndarray:
    """The one non-diegetic clip here, and the only stereo one (§08's 상점 UI).

    Stereo because Unity must not spatialise it — a UI acknowledgement that
    attenuates with distance is a bug. It is dual-mono: `write_wav` duplicates the
    channel, and width is not required, only the channel count that tells Unity to
    leave it alone. Descending two-tone, because ascending reads as a reward and
    §08's purchases are trades, not prizes.
    """
    dur = 0.72
    out = synth.silence(dur, SR)

    relay = impact([Mode(880, 0.0045, 1.0), Mode(1760, 0.0028, 0.5),
                    Mode(3100, 0.0014, 0.22)], 0.05, seed + 1, noise=0.5, noise_tau=0.0011)
    synth.place(out, relay, 0.0, 0.55, SR)

    for i, (freq, at) in enumerate(((784.0, 0.045), (523.25, 0.20))):
        tone = (synth.sine(freq, 0.34, sr=SR) + 0.18 * synth.sine(freq * 2.0, 0.34, sr=SR))
        tone = tone * synth.adsr(0.34, 0.008, 0.09, 0.40, 0.22, SR)
        synth.place(out, tone, at, 0.55 - 0.10 * i, SR)

    body = synth.sine(130.81, 0.30, sr=SR) * synth.exp_decay(0.30, 0.09, SR)
    synth.place(out, body, 0.045, 0.22, SR)
    return synth.lowpass(synth.highpass(out, 90.0, order=2, sr=SR), 6500.0, order=2, sr=SR)


# ── §08 · §11 — detector and muffler ────────────────────────────────────────


def detector_ping(seed: int) -> np.ndarray:
    """§11's substitute for a missing 청음사, and §08 prices it "작동 시 소리를 낸다".

    The noise *is* the item, so this is mixed at −5 dBFS with a 100 ms decay
    constant and a repeat: while the detector is on, everyone in the zone knows
    someone is looking. One clip, no variants — a device that pings differently
    each time is not a reference the player can read direction from, and reading
    direction is the whole substitute for the Listener.
    """
    dur = 0.55
    out = synth.silence(dur, SR)

    relay = impact([Mode(1350, 0.0022, 1.0), Mode(2600, 0.0013, 0.4)], 0.02, seed + 1,
                   noise=0.55, noise_tau=0.0006)
    synth.place(out, relay, 0.0, 0.28, SR)

    ping_n = _nn(0.42)
    bend = np.geomspace(2060.0, 1900.0, ping_n)
    ping = (fm_sine(bend) + 0.26 * fm_sine(2.0 * bend) + 0.10 * fm_sine(3.0 * bend))
    ping = ping * synth.exp_decay(0.42, 0.10, SR)
    ping = ping * np.clip(np.linspace(0.0, 1.0, ping_n) * 30.0, 0.0, 1.0).astype(np.float32)
    synth.place(out, ping, 0.012, 1.0, SR)

    echo_n = _nn(0.30)
    ebend = np.geomspace(1940.0, 1860.0, echo_n)
    echo = (fm_sine(ebend) + 0.18 * fm_sine(2.0 * ebend)) * synth.exp_decay(0.30, 0.055, SR)
    synth.place(out, synth.lowpass(echo, 4200.0, order=2, sr=SR), 0.205, 0.30, SR)

    return synth.highpass(out, 400.0, order=2, sr=SR)


def muffler_equip(seed: int) -> np.ndarray:
    """§08 소음기: "발소리 감소 / 자기도 못 듣게 됨 → 청음사 무효."

    The clip enacts its own drawback. Fabric and a buckle at full bandwidth, then
    `lp_sweep` closes the band from 7 kHz down to 380 Hz and the level collapses:
    the world goes dull, which is exactly what the item does to the player. Quiet
    on purpose — a loud muffler would be a joke, and §08 already charges enough by
    cancelling a whole role.
    """
    dur = 1.10
    out = synth.silence(dur, SR)

    wrap = band_noise(0.40, seed + 1, 300.0, 6500.0, "pink")
    wrap = wrap * grain_env(0.40, seed + 2, 380.0, 0.0030, 1.4)
    wrap = wrap * synth.adsr(0.40, 0.03, 0.12, 0.7, 0.14, SR)
    synth.place(out, wrap, 0.0, 0.85, SR)

    buckle = impact([Mode(950, 0.005, 1.0), Mode(1900, 0.003, 0.45),
                     Mode(3200, 0.0015, 0.18)], 0.06, seed + 3, noise=0.45)
    synth.place(out, synth.lowpass(buckle, 5000.0, order=2, sr=SR), 0.42, 0.70, SR)

    close_n = _nn(0.52)
    bed = band_noise(0.52, seed + 4, 200.0, 7000.0, "pink")
    bed = bed * (0.5 + 0.5 * grain_env(0.52, seed + 5, 120.0, 0.004, 1.6))
    bed = lp_sweep(bed, 7000.0, 380.0, 6)
    bed = bed * (np.linspace(1.0, 0.0, close_n, dtype=np.float32) ** 2.4)
    synth.place(out, bed, 0.50, 0.55, SR)

    return synth.highpass(out, 100.0, order=2, sr=SR)


# ── Build ───────────────────────────────────────────────────────────────────


def build() -> None:
    """Generates every clip. Seed offsets are fixed so a rebuild is byte-identical."""
    S = SEED

    # §05 · §10 — flashlight, the most-repeated interaction in the game.
    emit("flashlight_on_01", flashlight_click(S + 100, True, 0), "§05 F key, light on")
    emit("flashlight_on_02", flashlight_click(S + 110, True, 1), "§05 F key, light on (var)")
    emit("flashlight_off_01", flashlight_click(S + 120, False, 0), "§05 F key, light off")
    emit("flashlight_off_02", flashlight_click(S + 130, False, 1), "§05 F key, light off (var)")

    # §03 — battery is the lock on progress.
    emit("battery_insert_01", battery_insert(S + 200, 1.0), "§03 보충: light restored")
    emit("battery_insert_02", battery_insert(S + 210, 1.06), "§03 보충: light restored (var)")
    emit("battery_low_warning", battery_low_warning(S + 220), "§03 clue-reading window closing")
    emit("battery_dead", battery_dead(S + 230), "§03 no light → clues unreadable")

    # §04 · §12 — doors. Two different doors, not one door twice.
    emit("door_open_01", door_open(S + 300, 385.0, 640.0, 1.0), "§04 청음사 goes deaf to open it")
    emit("door_open_02", door_open(S + 310, 525.0, 905.0, 0.92), "§04 second door, higher hinge")
    emit("door_close_01", door_close(S + 320, 1.0, 0.55), "§04 gentle close")
    emit("door_close_02", door_close(S + 330, 0.94, 1.0), "§04 solid close")
    emit("door_lock_01", door_lock(S + 340, 1.0), "§04 Engineer traps a route (§12 순환로 neck)")
    emit("door_lock_02", door_lock(S + 350, 0.93), "§04 second bolt, heavier door")

    # §04 — barricades.
    emit("barricade_place_01", barricade_place(S + 400, 1.0), "§04 차단물 set: quiet prep")
    emit("barricade_place_02", barricade_place(S + 410, 0.91), "§04 차단물 set (var)")
    emit("barricade_break_01", barricade_break(S + 420, 1.0), "monster coming through — threat cue")
    emit("barricade_break_02", barricade_break(S + 430, 1.12), "monster coming through (var)")

    # §04 — noise trap. Quiet to arm, deafening to trip.
    emit("noisetrap_arm", noisetrap_arm(S + 500), "§04 사전 준비형: quiet on purpose")
    emit("noisetrap_trigger", noisetrap_trigger(S + 510), "§04 loud on purpose; can catch the 주자")

    # §04 · §08 — the safe.
    emit("safe_dial_turn_loop", safe_dial_turn_loop(S + 600), "§08 금고 속 문서 (LOOP)")
    emit("safe_open", safe_open(S + 610), "§04 금고를 연다")

    # §03 · §10 — zone lighting: the switch that both reveals and betrays.
    emit("breaker_throw", breaker_throw(S + 700), "§10 밝히면 괴물이 온다")
    emit("zone_hum_loop", zone_hum_loop(S + 710), "§03 lit zone = standing warning (LOOP)")

    # §08 · §11 — flare.
    emit("flare_ignite", flare_ignite(S + 800), "§08 1회용 · 소리를 낸다")
    emit("flare_burn_loop", flare_burn_loop(S + 810), "§08 the noise cost, paid continuously (LOOP)")
    emit("flare_die", flare_die(S + 820), "§08 single use spent; §03 light gone")

    # §08 — chalk (3 variants: a marked route is many strokes) and rope.
    emit("chalk_mark_01", chalk_mark(S + 900, 0.30, 1.0), "§08 분필: cost is the trail, not noise")
    emit("chalk_mark_02", chalk_mark(S + 910, 0.26, 1.08), "§08 분필 (var)")
    emit("chalk_mark_03", chalk_mark(S + 920, 0.34, 0.94), "§08 분필 (var)")
    emit("rope_deploy", rope_deploy(S + 930), "§08 밧줄: 지름길, 편도만")

    # §08 — loot, one clip pair per row of the 전리품 table.
    emit("loot_pickup_metal_small_01", loot_metal_small(S + 1000, 1.0), "§08 은수저 무게1")
    emit("loot_pickup_metal_small_02", loot_metal_small(S + 1010, 1.05), "§08 은수저 무게1 (var)")
    emit("loot_pickup_glass_jewel_01", loot_glass_jewel(S + 1020, 1.0), "§08 회중시계·반지 무게1")
    emit("loot_pickup_glass_jewel_02", loot_glass_jewel(S + 1030, 0.94), "§08 회중시계·반지 (var)")
    emit("loot_pickup_paper_01", loot_paper(S + 1040, 1.0), "§08 금고 속 문서 무게2")
    emit("loot_pickup_paper_02", loot_paper(S + 1050, 1.07), "§08 금고 속 문서 (var)")
    emit("loot_pickup_wood_heavy_01", loot_wood_heavy(S + 1060, 1.0), "§08 궤짝 무게5, 2인 운반")
    emit("loot_pickup_wood_heavy_02", loot_wood_heavy(S + 1070, 0.9), "§08 궤짝 무게5 (var)")

    # §08 — the vehicle shop.
    emit("loot_sell_credit", loot_sell_credit(S + 1100), "§08 지상 차량: 공용 지갑 credit")
    emit("shop_purchase_confirm", shop_purchase_confirm(S + 1110), "§08 상점 UI (STEREO, non-diegetic)")

    # §08 · §11 — the two items that trade with the Listener.
    emit("detector_ping", detector_ping(S + 1200), "§11 청음사 substitute; §08 cost = this noise")
    emit("muffler_equip", muffler_equip(S + 1210), "§08 소음기: 청음사 무효")


# ── Verification ────────────────────────────────────────────────────────────


def verify_cost_ladder() -> str:
    """Checks the measured mix against §10's rule that every gain is paid for.

    Peak level is not loudness. A clip normalised to −3 dBFS can still measure
    quieter in RMS than one at −13, which is how an item whose §08 drawback is
    "소리를 낸다" ends up effectively free. So these ratios are asserted against the
    written files rather than assumed from the LEVELS table.
    """
    by_name = {os.path.basename(r.path)[:-4]: r for r in REPORTS}
    checks = [
        ("noisetrap_trigger", "flashlight_on_01", 6.0,
         "§04 the trigger is loud on purpose and can catch the 주자"),
        ("flare_burn_loop", "chalk_mark_01", 5.0,
         "§08 flare pays in noise, 분필 pays in a trail"),
        ("detector_ping", "muffler_equip", 3.0,
         "§08 감지기 작동 시 소리를 낸다 / 소음기 is the quiet one"),
        ("zone_hum_loop", "chalk_mark_01", 3.0,
         "§03 a lit zone announces itself to the monster"),
        ("barricade_break_01", "barricade_place_01", 2.0,
         "breaking in must out-read setting up"),
        ("loot_pickup_wood_heavy_01", "loot_pickup_metal_small_01", 2.0,
         "§08 무게 5 vs 무게 1 — greed should be audible"),
        ("flare_burn_loop", "zone_hum_loop", 1.2,
         "a burning flare must out-read a lit room"),
        ("door_lock_01", "door_close_02", 1.1,
         "§04 the bolt has to dominate the closing it follows"),
        ("battery_dead", "battery_low_warning", 1.1,
         "§03 the setback must out-read its own warning"),
    ]

    lines = ["RMS cost ladder (measured from the written files):",
             f"  {'loud clip':<28} {'/ quiet clip':<28} {'ratio':>7} {'min':>5}  design reason"]
    for loud, quiet, factor, why in checks:
        rl, rq = by_name[loud], by_name[quiet]
        ratio = rl.rms / rq.rms
        if ratio < factor:
            raise AssertionError(
                f"cost ladder: {loud} is only {ratio:.2f}x the RMS of {quiet} "
                f"(need {factor}x) — {why}")
        lines.append(f"  {loud:<28} {quiet:<28} {ratio:>6.2f}x {factor:>4.1f}x  {why}")

    lock = by_name["door_lock_01"].peak_db
    close = by_name["door_close_01"].peak_db
    if lock < close + 1.5:
        raise AssertionError(
            f"§04: door_lock_01 ({lock:.1f} dB) must read louder than door_close_01 "
            f"({close:.1f} dB) — the lock is what a trapped teammate hears")
    lines.append(f"  door_lock_01 {lock:.1f} dBFS vs door_close_01 {close:.1f} dBFS  "
                 f"(§04 the lock must land harder than a close)")
    return "\n".join(lines)


VARIANT_MAX_SIMILARITY = 0.60
"""Ceiling on waveform correlation between two variants of the same sound.

Distinct bytes prove nothing — a gain change gives distinct bytes and an identical
sound. Two takes of the same object should share a *spectrum* and not a *waveform*,
so the test is correlation, and it earned its place: the first version of
`flashlight_click` varied only the seed and measured 0.75 on the clip the player
hears more than any other in the game.
"""


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
    corr = sg.fftconvolve(x, y[::-1], mode="full")
    lag = min(n - 1, _nn(max_lag_s))
    return float(np.max(np.abs(corr[n - 1 - lag: n + lag])) / denom)


def verify_variants() -> str:
    """Confirms repeated sounds are genuinely different renders, not copies.

    A repeated identical footstep is the fastest way to make a game feel cheap;
    the same is true of a flashlight the player presses hundreds of times.
    """
    groups: dict[str, list[str]] = {}
    for r in REPORTS:
        name = os.path.basename(r.path)[:-4]
        if name[-3:-2] == "_" and name[-2:].isdigit():
            groups.setdefault(name[:-3], []).append(r.path)

    lines = [f"Variant groups (distinct bytes, and waveform correlation < "
             f"{VARIANT_MAX_SIMILARITY:.2f}):"]
    for stem, paths in sorted(groups.items()):
        digests = {hashlib.sha256(open(p, "rb").read()).hexdigest() for p in paths}
        if len(digests) != len(paths):
            raise AssertionError(f"{stem}: duplicate variants — {len(paths)} files, "
                                 f"{len(digests)} distinct")
        worst = 0.0
        for i in range(len(paths)):
            for j in range(i + 1, len(paths)):
                worst = max(worst, waveform_similarity(paths[i], paths[j]))
        if worst > VARIANT_MAX_SIMILARITY:
            raise AssertionError(
                f"{stem}: variants correlate at {worst:.2f} — that is one sound at two "
                "gains, not two takes. Vary the modes or the timing, not just the seed.")
        lines.append(f"  {stem:<32} {len(paths)} takes   worst correlation {worst:.3f}")
    return "\n".join(lines)


def spectral_flatness(path: str) -> float:
    """Geometric over arithmetic mean of the power spectrum above 80 Hz.

    Near 0 is tonal — a struck object ringing. Near 1 is noise. This is the axis
    that separates §08's document (paper crackle) from its pocket watch (a ring),
    and the two are almost identical in spectral centroid, so centroid alone would
    have called them the same material.
    """
    data, sr = synth.read_wav(path)
    mag = np.abs(np.fft.rfft(data.astype(np.float64) * np.hanning(len(data))))
    mag = mag[np.fft.rfftfreq(len(data), 1.0 / sr) > 80.0]
    power = mag ** 2 + 1e-20
    return float(np.exp(np.mean(np.log(power))) / np.mean(power))


def decay_rate_db_per_s(path: str, window: float = 0.15) -> float:
    """How fast the clip dies after its peak, in dB/s.

    The third material axis, and for §08's spoons versus its pocket watch the
    decisive one: both are bright metal-ish rings, but a teaspoon is dead in a
    tenth of a second and a watch case keeps going. Centroid cannot see that.
    """
    data, sr = synth.read_wav(path)
    env = synth.lowpass(np.abs(data), 60.0, order=2, sr=sr)
    start = int(np.argmax(env))
    seg = env[start: start + synth.n_samples(window, sr)]
    if len(seg) < 32:
        return 0.0
    db = 20.0 * np.log10(np.maximum(seg.astype(np.float64), 1e-6))
    t = np.arange(len(seg)) / float(sr)
    return float(np.polyfit(t, db, 1)[0])


def verify_loot_material_contrast() -> str:
    """The four §08 loot classes must be identifiable by ear, not just by name.

    §12 already makes material a gameplay channel for floors; loot is the same idea
    at object scale — a player who hears a pickup should know whether a teammate
    just took a 무게 1 spoon or a 무게 5 chest, because §08's whole economy is
    무게 vs 가치 and §03 makes the answer change what the team can still carry.

    Three dimensions, because one is not enough: brightness (spectral centroid),
    tonality (flatness) and decay rate. Each pair has to be clearly apart on at
    least one, and the table names which. Measuring only centroid would have passed
    the document and the pocket watch as the same material — they landed 5 Hz apart.
    """
    classes = ["loot_pickup_wood_heavy_01", "loot_pickup_metal_small_01",
               "loot_pickup_glass_jewel_01", "loot_pickup_paper_01"]
    by_name = {os.path.basename(r.path)[:-4]: r for r in REPORTS}
    measured = {n: (by_name[n].spectral_centroid, spectral_flatness(by_name[n].path),
                    abs(decay_rate_db_per_s(by_name[n].path))) for n in classes}

    lines = ["§08 loot material contrast:",
             f"  {'class':<30} {'centroid Hz':>11} {'flatness':>9} {'decay dB/s':>11}  reads as"]
    reads = {"loot_pickup_wood_heavy_01": "궤짝 — low, tonal, heavy",
             "loot_pickup_metal_small_01": "은수저 — mid-bright, short ring",
             "loot_pickup_glass_jewel_01": "회중시계·반지 — bright, long ring",
             "loot_pickup_paper_01": "문서 — bright, pure noise, no ring"}
    for n in classes:
        c, f, d = measured[n]
        lines.append(f"  {n:<30} {c:>11.0f} {f:>9.4f} {d:>11.1f}  {reads[n]}")

    lines.append(f"  {'pair':<44} {'bright':>7} {'tonal':>9} {'decay':>7}  separated by")
    for i, a in enumerate(classes):
        for b in classes[i + 1:]:
            ratios = [max(x, y) / max(min(x, y), 1e-12) for x, y in zip(measured[a], measured[b])]
            names = ["brightness", "tonality", "decay"]
            floors = [1.25, 4.0, 1.6]
            by = [nm for nm, r, fl in zip(names, ratios, floors) if r >= fl]
            if not by:
                raise AssertionError(
                    f"{a} vs {b}: brightness {ratios[0]:.2f}x, tonality {ratios[1]:.1f}x, "
                    f"decay {ratios[2]:.2f}x — indistinguishable, and §08 needs these four "
                    "material classes to be told apart by ear")
            pair = f"{a[12:]} / {b[12:]}"
            lines.append(f"  {pair:<44} {ratios[0]:>6.2f}x {ratios[1]:>8.1f}x "
                         f"{ratios[2]:>6.2f}x  {' + '.join(by)}")
    return "\n".join(lines)


def verify_loop_seams() -> str:
    """Measures the loop point of every `*_loop` clip.

    Two things can wreck a loop. A step between the last and first sample is a
    click on every repeat. And `write_wav`'s 6 ms fade-out is an amplitude notch
    unless the content was already quiet there — which is what `loop_breathe`
    arranges. Both are checked on the written file, because both are inaudible in
    the generator and obvious to a player standing in a lit room for two minutes.
    """
    lines = ["Loop seams (measured on the written file):",
             f"  {'clip':<24} {'sec':>6} {'seam step':>10} {'seam rms':>9} {'body rms':>9} {'ratio':>6}"]
    for name in sorted(LOOPS):
        path = os.path.join(OUT_DIR, name + ".wav")
        data, _ = synth.read_wav(path)
        step = abs(float(data[0]) - float(data[-1]))
        win = _nn(0.005)
        seam = np.concatenate([data[-win:], data[:win]])
        seam_rms = float(np.sqrt(np.mean(seam.astype(np.float64) ** 2)))
        body_rms = float(np.sqrt(np.mean(data.astype(np.float64) ** 2)))
        ratio = seam_rms / body_rms if body_rms > 0 else 0.0
        if step > 0.02:
            raise AssertionError(f"{name}: {step:.4f} step across the loop seam — clicks")
        if ratio > 1.0:
            raise AssertionError(
                f"{name}: seam is {ratio:.2f}x the body RMS — write_wav's 6 ms fade "
                "will notch it; shape the content so the seam sits in a trough")
        lines.append(f"  {name:<24} {len(data)/SR:>6.3f} {step:>10.5f} {seam_rms:>9.4f} "
                     f"{body_rms:>9.4f} {ratio:>5.2f}x")
    return "\n".join(lines)


def verify_channels() -> str:
    """Mono for everything positional; stereo only for the UI confirm."""
    bad = []
    for r in REPORTS:
        name = os.path.basename(r.path)[:-4]
        want = 2 if name in STEREO else 1
        if r.channels != want:
            bad.append(f"{name}: {r.channels}ch, want {want}ch")
    if bad:
        raise AssertionError("channel layout: " + "; ".join(bad))
    return (f"Channels: {sum(1 for r in REPORTS if r.channels == 1)} mono (positional), "
            f"{sum(1 for r in REPORTS if r.channels == 2)} stereo (UI only) — "
            "Unity will not spatialise stereo, and §04's Listener needs 3D attenuation.")


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
          f"sample rates {sorted({r.sample_rate for r in REPORTS})}")
    print()
    print(verify_channels())
    print()
    print(verify_variants())
    print()
    print(verify_loot_material_contrast())
    print()
    print(verify_cost_ladder())
    print()
    print(verify_loop_seams())

    print("\nRoster — what each clip is for in play:")
    for name, note in ROSTER:
        print(f"  {name:<30} {note}")

    print("\nSHA-256 (a second run must print these unchanged):")
    for r in sorted(REPORTS, key=lambda x: x.path):
        digest = hashlib.sha256(open(r.path, "rb").read()).hexdigest()
        print(f"  {digest[:16]}  {os.path.basename(r.path)}")

    print(f"\nOK — {len(REPORTS)} clips written and verified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
