"""Generates the non-diegetic audio set: feedback, stingers, and the ghost's one channel.

Everything here is synthesised. Nothing is sampled or downloaded — §13 ships this on
Steam and a clip of unclear provenance is a legal problem, not a mixing problem.

Run:
    tools/audio/.venv/bin/python tools/audio/gen_ui.py

Output: unity/HorrorGame/Assets/Audio/UI/

--------------------------------------------------------------------------------
WHAT EACH SOUND IS FOR
--------------------------------------------------------------------------------

`clue_read_success` / `clue_read_failed` — §03.
    Reading a clue costs sustained light and stillness in a dangerous room, and the
    clue cannot be carried out: "그 자리에서 보고, 기억해서, 말로 전달해야 한다."
    Both sounds start with the same faint metering ticks so they are recognisably
    the same act; failure is that texture *cut off mid-tick*, followed by a long
    empty tail. §07 makes time the only currency, so the failure has to read as
    spent time rather than as a buzzer. It is the more expensive sound of the two.

`objective_found` / `objective_pickup` — §03.
    The objective needs both hands: no flashlight, no loot, no sprint for the 주자.
    §03 calls picking it up the match's decision point — "지금 들고 나갈까, 전리품
    더 챙길까?" So `objective_found` is mass *arriving*: a sub that swells in before
    anything lands, then a low strike settling onto it, then a tail that drifts
    slightly downward. Nothing ascends and nothing resolves; a reward jingle would
    answer the question the design wants left open. `objective_pickup` is the
    commitment — physical, close, and almost tailless, because the options just
    narrowed.

`death_transition_01/02` — §09.
    Death is not an ending, it is the ghost state: full map vision, no speech, no
    escape. So the sound is a severance. The shared soundscape (a band of noise
    standing in for everything the team is doing) closes with a falling low-pass,
    is hard-gated to true silence, and then the ghost side fades in — a low drone
    and a thin high thread that belong to nobody. Two variants because dying twice
    in a session should not sound like a repeated cue.

`ghost_rattle_01..04` — §09. **MONO — positional.**
    The ghost's only channel, on a 45 s cooldown (GhostRattleCooldownSeconds) at
    GhostRattleRange = 4 m. §09's best moment is "방금 뭔가 흔들렸어?" answered with
    "바람이겠지", so ambiguity is the deliverable, and it is engineered rather than
    hoped for:
      * peak −18 dBFS and RMS held in a −46..−30 dBFS window, all four inside a
        5 dB spread — plainly present on headphones (§05 requires them), plainly
        dismissible as the building, and no variant a worse bet than another;
      * a soft global onset (measured, ≥ 25 ms to the envelope peak) so it never
        startles; a snap would read as a signal and §09 breaks;
      * energy in 600–4500 Hz, where hearing is most sensitive, so low level does
        not mean inaudible;
      * broadband texture rather than tones, because the player must be able to
        turn and localise it — hence mono, so Unity's 3D audio can place it;
      * four different objects (glass in a frame, small chain, wood shelf, tin
        debris) with separated spectral centroids, so a repeat is not a tell.

`ghost_rattle_ready` — §09. Only the ghost hears it, and the ghost has nothing else
    to listen to, so it is the quietest thing here (−22 dBFS). Deliberately in a
    different register from the rattles themselves, with no granular content: if
    the ghost's own cue could be mistaken for its rattle it would waste the 45 s.

`threat_night` / `threat_late_night` / `threat_pre_dawn` / `threat_before_sunrise`
    — §07, one per tier boundary. Four sounds, not five: 초저녁 is where the match
    starts, so there is no transition *into* it. Names match the NightPhase enum.
    "시간이 유일한 통화다" — these are the interest payments, so each is darker than
    the last by construction and by measurement: the root drops 98 → 82 → 69 → 55 Hz,
    the interval opens from a fifth to a tritone, air rolls off 5200 → 1600 Hz while
    the noise bed fades and the sub grows, and the tail lengthens 2.2 → 4.2 s. The
    check pass asserts the centroid *and* the high-band power share both fall
    monotonically — centroid alone would happily report four sounds that are equally
    featureless. Two carry their tier's specific loss: 심야 gets a
    shutter closing (손전등 반경 −30%), 새벽 a distant dull knock (괴물이 출입구를
    안다).

`heartbeat_low` / `heartbeat_mid` / `heartbeat_high` — proximity and danger bed.
    All three are exactly 4.000 s with the downbeat at +50 ms, at 60 / 90 / 120 BPM
    (4 / 6 / 8 beats). Equal length and a shared downbeat mean an engine crossfade
    between intensities never drifts and never lands mid-thump. The loop seam sits
    inside the gap after the last dub, so the fade write_wav applies to both ends is
    inaudible — verified as edge RMS relative to peak.

`escape_success` / `match_failure_wipe` — §02, and they must not be interchangeable.
    §02's asymmetry is the whole lever: one survivor keeps what the match learned,
    a wipe erases loot, credits and knowledge together. So they are built as
    opposites. Escape opens: air low-pass rises 900 → 8200 Hz, a bare fifth lifts,
    the sub pressure *releases*, and the tail is long and open. The wipe closes: the
    sub *arrives* and stays, the cluster falls 110 → 52 Hz, air rolls shut, and the
    last half second is hard-gated to true silence — 전멸 leaves nothing. Their spectral
    centroids and tail behaviour are asserted to be on opposite sides.


`shop_open` / `shop_close` / `shop_denied` — §08. The shop is the surface vehicle,
    which is also the safe zone, so opening it lets a warm hum in and closing it
    takes the hum away. Denial is not a buzzer: the wallet is shared (§08's 공용
    지갑) and a refusal happens mid-negotiation, so it is two dead clicks and a
    muted low "no" with no tail at all.

`voice_activity_blip` / `voice_out_of_range` — §13. Voice is cut at the sender at
    VoiceCutoffDistance = 30 m. The blip fires constantly, so it is the quietest
    non-ghost clip here (−20 dBFS peak, 90 ms). The out-of-range cue is the channel
    losing carrier: voice-band noise (300–2600 Hz) closing down to 380 Hz with a
    slight downward glide, so a player learns where 30 m is instead of guessing why
    nobody answered. It is deliberately more insistent than a ghost rattle — a known
    teammate walking away is a fact, not a hint.

`descend_basement` / `surface_reached` — §03's round trip. Going down makes the mix
    smaller (air closes 6200 → 620 Hz, pressure arrives, tight dense tail); coming
    up releases it (620 → 8600 Hz, wind, long open tail). Purely environmental, with
    no pitched material, because per §03 surfacing is "숨 돌리기이지 리셋이 아니다" —
    a breath, not a win. `escape_success` is the win and owns the pitched gesture.

--------------------------------------------------------------------------------
LEVELS
--------------------------------------------------------------------------------
Every clip peaks at or below synth.UI_HEADROOM_DB (−6 dBFS), most of them far
below. §12 makes floor material a gameplay channel — the 청음사 locates the monster
by which surface its footsteps land on — so UI that masks a footstep is a
gameplay bug, not a taste question. Positional world audio ships at
DEFAULT_HEADROOM_DB (−3), which puts this whole set underneath it by design.
"""

from __future__ import annotations

import hashlib
import math
import os
from dataclasses import dataclass
from typing import Dict, List, Tuple

import numpy as np

import synth
from synth import Mode, SAMPLE_RATE as SR

# ── Where the files land ────────────────────────────────────────────────────

OUT_DIR = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "..",
                 "unity", "HorrorGame", "Assets", "Audio", "UI")
)

# ── Level budget, all at or under UI_HEADROOM_DB ────────────────────────────

LVL_CLUE_OK = -13.0
LVL_CLUE_FAIL = -11.0
LVL_OBJ_FOUND = -7.0
LVL_OBJ_PICKUP = -9.5
LVL_DEATH = synth.UI_HEADROOM_DB          # -6.0, the loudest thing here
LVL_RATTLE = -18.0   # mid-window rather than at its floor; see RATTLE_RMS_* below
LVL_RATTLE_READY = -22.0
LVL_THREAT = (-11.0, -10.5, -10.0, -9.5)  # slight rise across the four tiers
LVL_HEARTBEAT = (-16.0, -13.0, -10.0)     # a bed, and it plays for minutes
LVL_ESCAPE = -8.0
LVL_WIPE = -6.5
LVL_SHOP = -14.0
LVL_SHOP_DENIED = -13.0
LVL_VOICE_BLIP = -20.0
LVL_VOICE_OUT = -15.0
LVL_TRAVERSAL = -14.0

# §12's five floor materials are told apart above roughly this frequency, so the
# one clip here that plays continuously has to keep its energy below it.
FOOTSTEP_BAND_FLOOR_HZ = 400.0

# The ghost rattle's ambiguity window, in dBFS RMS. Below the floor the ghost is
# mute; above the ceiling it is obviously a signal and §09's exchange
# ("바람이겠지") stops happening.
RATTLE_RMS_FLOOR_DB = -46.0
RATTLE_RMS_CEIL_DB = -30.0

# Every random source is seeded off this so a rebuild is byte-identical; otherwise
# every regeneration shows up as a diff and nobody can tell what actually changed.
SEED = 0x9E3D
HEARTBEAT_LOOP_SECONDS = 4.0


# ── Small helpers, built on synth ───────────────────────────────────────────


def _env(seconds: float, attack: float, tau: float) -> np.ndarray:
    """Exponential decay with a raised-cosine attack.

    A linear attack on a soft sound still ticks; a cosine one does not. `tau`
    stays exponential because that is how real resonances lose energy.
    """
    e = synth.exp_decay(seconds, tau).copy()
    a = min(synth.n_samples(attack), len(e))
    if a > 1:
        e[:a] *= (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, a, dtype=np.float32))).astype(np.float32)
    return e


def _tone(freq: float, seconds: float, attack: float = 0.008, tau: float = 0.4,
          phase: float = 0.0) -> np.ndarray:
    """A sine with a soft attack and an exponential tail."""
    return (synth.sine(freq, seconds, phase) * _env(seconds, attack, tau)).astype(np.float32)


def _glide(f0: float, f1: float, seconds: float, log: bool = True) -> np.ndarray:
    """A tone whose pitch drifts, by phase integration.

    Used for slow drifts where `synth.sweep`'s chirp semantics are more gesture
    than the sound wants — a threat stinger's tail sinking, not a swoop.
    """
    t = synth.t_axis(seconds)
    span = max(seconds, 1e-6)
    if log and f0 > 0.0 and f1 > 0.0:
        f = f0 * (f1 / f0) ** (t / span)
    else:
        f = f0 + (f1 - f0) * (t / span)
    return np.sin(2.0 * np.pi * np.cumsum(f) / float(SR)).astype(np.float32)


def _moving_lowpass(buf: np.ndarray, f_start: float, f_end: float,
                    stages: int = 12, order: int = 4) -> np.ndarray:
    """Time-varying low-pass, as a crossfade between statically filtered copies.

    Filtering block by block leaves edge artefacts at every seam; filtering the
    whole buffer a dozen times and crossfading does not, and a dozen filtfilt
    passes over a few seconds costs nothing here.
    """
    n = len(buf)
    stages = max(2, stages)
    cuts = np.geomspace(max(f_start, 25.0), max(f_end, 25.0), stages)
    stack = np.stack([synth.lowpass(buf, float(c), order=order) for c in cuts])
    pos = np.linspace(0.0, stages - 1.0, n)
    idx = np.clip(np.floor(pos).astype(int), 0, stages - 2)
    frac = (pos - idx).astype(np.float32)
    cols = np.arange(n)
    lo = stack[idx, cols]
    hi = stack[idx + 1, cols]
    return (lo * (1.0 - frac) + hi * frac).astype(np.float32)


def _moving_highpass(buf: np.ndarray, f_start: float, f_end: float,
                     stages: int = 12, order: int = 4) -> np.ndarray:
    """Time-varying high-pass. Same construction as `_moving_lowpass`."""
    n = len(buf)
    stages = max(2, stages)
    cuts = np.geomspace(max(f_start, 25.0), max(f_end, 25.0), stages)
    stack = np.stack([synth.highpass(buf, float(c), order=order) for c in cuts])
    pos = np.linspace(0.0, stages - 1.0, n)
    idx = np.clip(np.floor(pos).astype(int), 0, stages - 2)
    frac = (pos - idx).astype(np.float32)
    cols = np.arange(n)
    lo = stack[idx, cols]
    hi = stack[idx + 1, cols]
    return (lo * (1.0 - frac) + hi * frac).astype(np.float32)


def _gate_off(buf: np.ndarray, at: float, fall: float = 0.012) -> np.ndarray:
    """Cuts everything after `at` to true silence over `fall` seconds.

    The interruption in `clue_read_failed` and the nothing at the end of
    `match_failure_wipe`. A fade would be a decision; a gate is a loss.
    """
    out = buf.astype(np.float32).copy()
    i = synth.n_samples(at)
    if i >= len(out):
        return out
    f = min(synth.n_samples(fall), len(out) - i)
    if f > 1:
        out[i:i + f] *= np.linspace(1.0, 0.0, f, dtype=np.float32)
    out[i + f:] = 0.0
    return out


def _rough_env(seconds: float, rate: float, seed: int, depth: float = 0.85) -> np.ndarray:
    """A jittery amplitude contour — stick-slip creak, canvas drag, debris shuffle."""
    n = synth.n_samples(seconds)
    steps = max(2, int(seconds * rate))
    g = synth.rng(seed)
    vals = g.uniform(0.0, 1.0, steps + 2)
    env = np.interp(np.linspace(0.0, float(steps + 1), n),
                    np.arange(steps + 2, dtype=np.float64), vals).astype(np.float32)
    env = synth.lowpass(env, max(rate * 2.5, 8.0), order=2)
    return ((1.0 - depth) + depth * np.clip(env, 0.0, None)).astype(np.float32)


def _contact_train(seconds: float, rate: float, seed: int, jitter: float = 0.3,
                   spike_tau: float = 0.0015) -> np.ndarray:
    """Noise gated into a jittered train of micro-contacts.

    What makes a rattle a rattle: an object touching another object many times in
    under a second, never on a grid.
    """
    g = synth.rng(seed)
    n = synth.n_samples(seconds)
    gate = np.zeros(n, dtype=np.float32)
    period = 1.0 / max(rate, 0.1)
    t = 0.0
    for _ in range(4096):
        if t >= seconds:
            break
        i = min(max(synth.n_samples(t) - 1 if t > 0 else 0, 0), n - 1)
        gate[i] += float(g.uniform(0.45, 1.0))
        t += max(period * 0.25, period * float(1.0 + g.normal(0.0, jitter)))
    tail = np.exp(-np.arange(synth.n_samples(spike_tau * 6.0)) / (spike_tau * SR)).astype(np.float32)
    exc = np.convolve(gate, tail)[:n].astype(np.float32)
    return (exc * synth.white(seconds, seed + 13)).astype(np.float32)


def _grains(seconds: float, count: int, seed: int, band: Tuple[float, float],
            dur: Tuple[float, float], start: float = 0.0, spread: float = 1.0) -> np.ndarray:
    """Short band-passed noise grains at jittered times. Debris, paper, scuff."""
    g = synth.rng(seed)
    canvas = synth.silence(seconds)
    for k in range(count):
        d = float(g.uniform(*dur))
        grain = synth.bandpass(synth.white(d, seed + 101 * k + 7), band[0], band[1])
        grain = (grain * _env(d, min(0.004, d * 0.3), max(d * 0.35, 0.004))).astype(np.float32)
        at = start + float(g.uniform(0.0, 1.0)) * spread * max(seconds - start - d, 1e-3)
        synth.place(canvas, grain, at, gain=float(g.uniform(0.35, 1.0)))
    return canvas


def _polish(buf: np.ndarray, hp_hz: float = 22.0) -> np.ndarray:
    """Blocks sub-audible energy that would otherwise eat headroom as DC."""
    return synth.highpass(buf.astype(np.float32), hp_hz, order=2)


def _settle(buf: np.ndarray, fade: float) -> np.ndarray:
    """Cosine-fades the tail to zero.

    `write_wav` applies a fixed 6 ms out-fade to everything, which is a de-click,
    not a tail. A one-shot still ringing at its final sample therefore gets
    truncated in 6 ms, and in a quiet mix that truncation is the loudest thing in
    the clip. Every one-shot here ends with a real fade so that never happens; the
    heartbeat loops are exempt because their seams already sit in silence.
    """
    out = buf.astype(np.float32).copy()
    f = min(synth.n_samples(fade), len(out))
    if f > 1:
        out[-f:] *= (0.5 + 0.5 * np.cos(np.linspace(0.0, np.pi, f, dtype=np.float32))).astype(np.float32)
    return out


# ── §03 · Clue reading ──────────────────────────────────────────────────────


def _read_ticks(seconds: float, count: int, spacing: float, seed: int,
                start: float = 0.0) -> np.ndarray:
    """The metering texture shared by clue success and clue failure.

    §03 makes reading take sustained light and stillness, so the act has duration,
    and duration wants a progress sound. Sharing it between the two outcomes is
    what makes the failure legible as *this read, interrupted*.
    """
    canvas = synth.silence(seconds)
    for k in range(count):
        d = 0.008
        tick = synth.bandpass(synth.white(d, seed + 37 * k), 1900.0, 3600.0)
        tick = (tick * _env(d, 0.0008, 0.0022)).astype(np.float32)
        pip = _tone(1580.0 + 22.0 * k, d, attack=0.001, tau=0.0025) * 0.35
        synth.place(canvas, (tick + pip).astype(np.float32), start + k * spacing,
                    gain=0.55 + 0.09 * k)
    return canvas


def clue_read_success() -> np.ndarray:
    """§03: the clue resolved. Quiet and informational — you still have to remember it."""
    sec = 1.30
    s = SEED + 1100

    ticks = _read_ticks(sec, 5, 0.085, s)

    # Paper and dust becoming legible rather than a menu confirm.
    paper = synth.bandpass(synth.pink(0.30, s + 5), 1700.0, 5600.0)
    paper = (paper * _env(0.30, 0.030, 0.150)).astype(np.float32)

    settle_at = 0.45
    lock_a = _tone(659.3, 0.60, attack=0.018, tau=0.42)      # E5
    lock_b = _tone(987.8, 0.55, attack=0.022, tau=0.55)      # B5, a bare fifth above
    body = _tone(164.8, 0.55, attack=0.020, tau=0.50)        # E3, so it is not thin

    canvas = synth.silence(sec)
    synth.place(canvas, ticks, 0.0, 0.32)
    synth.place(canvas, paper, settle_at - 0.03, 0.30)
    synth.place(canvas, lock_a, settle_at, 0.55)
    synth.place(canvas, lock_b, settle_at + 0.06, 0.32)
    synth.place(canvas, body, settle_at, 0.34)

    return _polish(synth.reverb(canvas, seconds=0.55, mix=0.14, seed=s + 9, damping=5200.0))


def clue_read_failed() -> np.ndarray:
    """§03: the read broke. It has to cost time (§07), not dignity."""
    sec = 1.60
    s = SEED + 1200
    cut = 0.46

    # The same meter as success — running, then truncated mid-tick.
    ticks = _gate_off(_read_ticks(sec, 7, 0.085, s), cut, fall=0.0015)
    paper = synth.bandpass(synth.pink(sec, s + 5), 1500.0, 5200.0)
    paper = _gate_off((paper * _env(sec, 0.040, 0.9)).astype(np.float32), cut, fall=0.0025)

    # Everything the read had built, falling out from under it.
    collapse = (_glide(520.0, 130.0, 0.55) * _env(0.55, 0.010, 0.30)).astype(np.float32)
    thud = synth.modal_impact(
        [Mode(96.0, 0.13, 1.0), Mode(143.0, 0.075, 0.55), Mode(207.0, 0.040, 0.28)],
        0.55, seed=s + 21, noise_amount=0.22, noise_tau=0.010)

    # The empty tail is the point: that was time, and it is gone.
    empty = _tone(61.7, 0.95, attack=0.10, tau=0.55)
    room = synth.lowpass(synth.pink(0.95, s + 33), 620.0) * _env(0.95, 0.12, 0.40)

    canvas = synth.silence(sec)
    synth.place(canvas, ticks, 0.0, 0.30)
    synth.place(canvas, paper, 0.0, 0.16)
    synth.place(canvas, collapse, cut, 0.30)
    synth.place(canvas, thud, cut + 0.02, 0.70)
    synth.place(canvas, empty, cut + 0.10, 0.34)
    synth.place(canvas, room.astype(np.float32), cut + 0.10, 0.10)

    return _polish(synth.reverb(canvas, seconds=1.1, mix=0.20, seed=s + 41, damping=2600.0))


# ── §03 · The objective ─────────────────────────────────────────────────────


def objective_found() -> np.ndarray:
    """§03: weight arriving. Nothing here ascends — the decision is still open."""
    sec = 2.70
    s = SEED + 1300

    sub = _tone(46.0, sec, attack=0.090, tau=1.10)
    sub2 = _tone(92.0, sec, attack=0.110, tau=0.85) * 0.30

    # A dull pair that drifts slightly flat over the tail. Drifting down is what
    # keeps it from reading as a reward.
    mass_a = (_glide(110.0, 107.4, 2.20) * _env(2.20, 0.060, 0.95)).astype(np.float32)
    mass_b = (_glide(146.6, 143.2, 2.20) * _env(2.20, 0.075, 0.80)).astype(np.float32)

    land = synth.modal_impact(
        [Mode(74.0, 0.22, 1.0), Mode(118.0, 0.13, 0.5), Mode(166.0, 0.07, 0.25)],
        1.00, seed=s + 7, noise_amount=0.20, noise_tau=0.014)

    bloom = synth.comb(synth.lowpass(synth.white(1.60, s + 11), 2600.0), 1.0 / 146.6, feedback=0.55)
    bloom = (bloom * _env(1.60, 0.150, 0.70)).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, sub, 0.0, 1.00)
    synth.place(canvas, sub2, 0.0, 1.00)
    synth.place(canvas, mass_a, 0.10, 0.42)
    synth.place(canvas, mass_b, 0.14, 0.26)
    synth.place(canvas, land, 0.22, 0.60)
    synth.place(canvas, bloom, 0.20, 0.13)

    return _polish(synth.reverb(canvas, seconds=1.8, mix=0.22, seed=s + 19, damping=3000.0))


def objective_pickup() -> np.ndarray:
    """§03: committed. Both hands are full — no flashlight, no loot, no sprint."""
    sec = 1.15
    s = SEED + 1400

    canvas_shift = synth.bandpass(synth.pink(0.34, s + 3), 240.0, 2200.0)
    canvas_shift = (canvas_shift * _rough_env(0.34, 14.0, s + 4) * _env(0.34, 0.015, 0.16)).astype(np.float32)

    contact = synth.modal_impact(
        [Mode(132.0, 0.095, 1.0), Mode(198.0, 0.050, 0.6), Mode(261.0, 0.028, 0.3)],
        0.45, seed=s + 9, noise_amount=0.38, noise_tau=0.008)

    creak = synth.bandpass(synth.white(0.22, s + 15), 300.0, 760.0)
    creak = (creak * _rough_env(0.22, 26.0, s + 16) * _env(0.22, 0.020, 0.09)).astype(np.float32)

    bump = _tone(52.0, 0.40, attack=0.012, tau=0.20)
    # Faint downward pitch: the answer to `objective_found`, and it is not a happy one.
    drop = (_glide(233.1, 174.6, 0.50) * _env(0.50, 0.030, 0.22)).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, contact, 0.055, 0.55)
    synth.place(canvas, canvas_shift, 0.0, 0.34)
    synth.place(canvas, creak, 0.150, 0.20)
    synth.place(canvas, bump, 0.060, 0.60)
    synth.place(canvas, drop, 0.120, 0.16)

    return _polish(synth.reverb(canvas, seconds=0.6, mix=0.12, seed=s + 23, damping=3400.0))


# ── §09 · Death, the transition into the ghost state ────────────────────────


def death_transition(variant: int) -> np.ndarray:
    """§09: cut off from the others. A transition, not an ending."""
    cfg = [
        dict(sec=3.45, cut=1.05, drone=58.0, thread=1900.0, scrape=False, seed=SEED + 1500),
        dict(sec=3.85, cut=1.45, drone=61.5, thread=2150.0, scrape=True, seed=SEED + 1600),
    ][variant]
    sec = float(cfg["sec"])
    cut = float(cfg["cut"])
    s = int(cfg["seed"])

    # The blackout itself.
    if cfg["scrape"]:
        hit = synth.bandpass(synth.white(0.35, s + 3), 90.0, 1400.0)
        hit = (hit * _rough_env(0.35, 34.0, s + 4) * _env(0.35, 0.004, 0.10)).astype(np.float32)
    else:
        hit = synth.modal_impact(
            [Mode(58.0, 0.16, 1.0), Mode(97.0, 0.085, 0.45), Mode(139.0, 0.045, 0.2)],
            0.40, seed=s + 3, noise_amount=0.30, noise_tau=0.012)
    thump = _tone(40.0, 0.45, attack=0.004, tau=0.16)

    # Everything the team is still doing, closing.
    world = synth.bandpass(synth.pink(cut + 0.10, s + 7), 200.0, 6200.0)
    world = _moving_lowpass(world, 5200.0, 380.0, stages=14)
    w_env = _env(cut + 0.10, 0.020, 3.0) * (1.0 - 0.35 * np.linspace(0.0, 1.0, synth.n_samples(cut + 0.10), dtype=np.float32))
    world = (world * w_env).astype(np.float32)

    # The ghost side: nobody's room, no speech (§09), just presence.
    g_len = sec - cut - 0.18
    drone = _tone(float(cfg["drone"]), g_len, attack=0.35, tau=1.7)
    drone2 = _tone(float(cfg["drone"]) * 1.5, g_len, attack=0.45, tau=1.3) * 0.22
    thread = _tone(float(cfg["thread"]), g_len, attack=0.50, tau=1.2)
    press = synth.lowpass(synth.brown(g_len, s + 21), 130.0) * _env(g_len, 0.40, 1.4)

    canvas = synth.silence(sec)
    synth.place(canvas, hit, 0.0, 0.85)
    synth.place(canvas, thump, 0.0, 0.80)
    synth.place(canvas, world, 0.010, 0.42)
    canvas = _gate_off(canvas, cut, fall=0.012)          # severance
    synth.place(canvas, drone, cut + 0.18, 0.40)
    synth.place(canvas, drone2, cut + 0.18, 0.40)
    synth.place(canvas, thread, cut + 0.22, 0.055)
    synth.place(canvas, press.astype(np.float32), cut + 0.18, 0.16)

    return _polish(canvas)


# ── §09 · The ghost rattle. Mono, positional, deliberately deniable ─────────


def ghost_rattle(variant: int) -> np.ndarray:
    """§09's one channel. Four different objects, one faintness budget.

    Ambiguity is the design requirement, so the shape is fixed across variants: a
    soft swell into a short body of micro-contacts, then gone. No variant opens
    with a snap — a transient reads as intent, and §09 needs "바람이겠지" to stay a
    reasonable answer.
    """
    s = SEED + 1700 + 40 * variant

    if variant == 0:
        # Loose pane in a dry frame: high-Q glass modes trembling. Glass is bright,
        # and keeping it bright is also what holds it clear of §13's voice cue.
        sec = 0.72
        exc = _contact_train(sec, 27.0, s, jitter=0.34, spike_tau=0.0012)
        body = np.zeros(synth.n_samples(sec), dtype=np.float32)
        for f, q, g in ((1180.0, 120.0, 1.0), (1735.0, 150.0, 0.72), (2460.0, 110.0, 0.48)):
            body += synth.resonator(exc, f, q=q) * g
        body = synth.bandpass(body, 1000.0, 5000.0)
        out = (body * _env(sec, 0.095, 0.24)).astype(np.float32)

    elif variant == 1:
        # A short chain or keys against metal: a few contacts, growing then dying.
        sec = 0.66
        g = synth.rng(s + 1)
        canvas = synth.silence(sec)
        n_hits = 6
        for k in range(n_hits):
            hit = synth.modal_impact(
                [Mode(2100.0 * float(g.uniform(0.94, 1.07)), 0.030, 1.0),
                 Mode(3050.0 * float(g.uniform(0.95, 1.06)), 0.018, 0.6),
                 Mode(4400.0 * float(g.uniform(0.95, 1.05)), 0.010, 0.3)],
                0.14, seed=s + 11 * k + 5, noise_amount=0.45, noise_tau=0.0015)
            # Rising then falling across the sequence, so the *global* onset is soft
            # even though each contact is a tick.
            shape = math.sin(math.pi * (k + 0.55) / n_hits) ** 1.6
            at = 0.045 + k * 0.052 + 0.020 * float(g.uniform(-1.0, 1.0))
            synth.place(canvas, hit, max(at, 0.0), gain=0.35 + 0.65 * shape)
        out = synth.bandpass(canvas, 900.0, 5200.0)
        out = (out * _env(sec, 0.055, 0.55)).astype(np.float32)

    elif variant == 2:
        # A wooden shelf or a hung frame shifting: stick-slip, then one dull tick.
        sec = 0.85
        creak = synth.bandpass(synth.white(0.55, s + 3), 290.0, 900.0)
        creak = (creak * _rough_env(0.55, 11.0, s + 4, depth=0.95)).astype(np.float32)
        creak = synth.tremolo(creak, 9.5, depth=0.45)
        creak = (creak * _env(0.55, 0.130, 0.34)).astype(np.float32)
        tick = synth.modal_impact(
            [Mode(640.0, 0.030, 1.0), Mode(1120.0, 0.016, 0.45), Mode(1780.0, 0.008, 0.2)],
            0.20, seed=s + 9, noise_amount=0.40, noise_tau=0.003)
        canvas = synth.silence(sec)
        # The creak, not the tick, sets the level. A dominant tick would spend the
        # whole faintness budget on one sample and leave the body inaudible.
        synth.place(canvas, creak, 0.0, 1.00)
        synth.place(canvas, tick, 0.44, 0.30)
        out = synth.bandpass(canvas, 260.0, 3200.0)

    else:
        # Tin and grit shifting on concrete.
        sec = 0.60
        grit = _grains(sec, 9, s + 3, band=(760.0, 4600.0), dur=(0.010, 0.030),
                       start=0.02, spread=0.85)
        # The scuff used to reach down to 200 Hz, which put a sixth of this variant's
        # energy under 300 Hz — straight into the heartbeat bed's register, where it
        # would be the one rattle that gets masked.
        scuff = synth.bandpass(synth.pink(sec, s + 5), 380.0, 1600.0)
        scuff = (scuff * _rough_env(sec, 19.0, s + 6) * _env(sec, 0.070, 0.22)).astype(np.float32)
        out = synth.mix(grit * _env(sec, 0.060, 0.30), scuff, gains=[0.80, 0.75])

    return _polish(out, hp_hz=220.0)


def ghost_rattle_ready() -> np.ndarray:
    """§09: the 45 s is up. Only the ghost hears it, so it can be nearly nothing.

    Deliberately tonal and hollow — no granules, no metal — so it cannot be
    confused with the rattle it is announcing.
    """
    sec = 0.72
    s = SEED + 1900

    hollow_a = _tone(208.0, sec, attack=0.130, tau=0.34)
    hollow_b = _tone(312.0, sec, attack=0.160, tau=0.28) * 0.40
    felt = _tone(66.0, sec, attack=0.180, tau=0.40) * 0.55   # felt more than heard
    # A breath of air, kept low: any brightness here would drag the cue up into the
    # rattles' own band, which is the one thing it must not sound like.
    rise = _moving_highpass(synth.pink(0.50, s + 3), 260.0, 700.0, stages=10)
    rise = (rise * _env(0.50, 0.250, 0.16)).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, hollow_a, 0.0, 0.60)
    synth.place(canvas, hollow_b, 0.0, 0.60)
    synth.place(canvas, felt, 0.0, 0.55)
    synth.place(canvas, rise, 0.06, 0.06)

    wet = synth.reverb(canvas, seconds=0.9, mix=0.16, seed=s + 7, damping=1500.0)
    return _polish(synth.lowpass(wet, 1600.0))


# ── §07 · Threat tier boundaries ────────────────────────────────────────────


def threat_stinger(tier: int) -> np.ndarray:
    """§07: an interest payment on the only currency there is.

    Four sounds for four boundaries. 초저녁 is where the match begins, so nothing
    announces it. Each is darker than the last by construction; the check pass
    asserts that both the centroid and the high-band power share actually fall.
    """
    # `bed` and `sub` ramp in opposite directions across the four. That is what makes
    # the darkening audible rather than merely measurable: the early boundaries still
    # have air to lose, and by 동트기 전 there is nothing left but weight.
    cfg = [
        # root, partner, seconds, air_cut, drift(semitones), bed, sub, rev_s, rev_mix
        dict(root=98.00, partner=146.50, sec=2.20, air=5200.0, drift=-0.30, bed=0.34, sub=0.55,
             rev=(1.6, 0.18), shutter=False, knock=False, grit=0.0, seed=SEED + 2100),
        dict(root=82.41, partner=116.54, sec=2.80, air=3400.0, drift=-0.60, bed=0.25, sub=0.68,
             rev=(2.0, 0.22), shutter=True, knock=False, grit=0.0, seed=SEED + 2200),
        dict(root=69.30, partner=98.00, sec=3.40, air=2200.0, drift=-0.90, bed=0.16, sub=0.80,
             rev=(2.4, 0.26), shutter=False, knock=True, grit=0.0, seed=SEED + 2300),
        dict(root=55.00, partner=77.78, sec=4.20, air=1600.0, drift=-1.60, bed=0.12, sub=0.92,
             rev=(2.8, 0.30), shutter=False, knock=False, grit=1.6, seed=SEED + 2400),
    ][tier]

    sec = float(cfg["sec"])
    s = int(cfg["seed"])
    root = float(cfg["root"])
    partner = float(cfg["partner"])
    ratio = 2.0 ** (float(cfg["drift"]) / 12.0)

    body = (_glide(root, root * ratio, sec) * _env(sec, 0.100, sec * 0.55)).astype(np.float32)
    body += (_glide(partner, partner * ratio, sec) * _env(sec, 0.130, sec * 0.45)).astype(np.float32) * 0.42
    sub = _tone(root * 0.5, sec, attack=0.120, tau=sec * 0.50) * float(cfg["sub"])
    if float(cfg["grit"]) > 0.0:
        # 동트기 전 — "생존 불가 수준". Grit and a beating partner, so it never settles.
        body = synth.saturate(body * 0.75, drive=float(cfg["grit"]))
        beat = _tone(root * 1.055, sec, attack=0.150, tau=sec * 0.45) * 0.30
        body = (body + beat).astype(np.float32)
        body = synth.tremolo(body, 0.9, depth=0.30)

    # Pink, not brown. Brown noise is already −6 dB/oct, so nearly all of its energy
    # sits under 100 Hz and `air` has almost nothing left to cut — the tiers measured
    # 99% below 120 Hz and 0.003% above 2 kHz, which is not "darker each time", it is
    # four sounds that are all sub. Pink is flat per octave, so rolling it from 5200
    # down to 1600 Hz across the four tiers is a change you can actually hear.
    bed = synth.pink(sec, s + 5)
    bed = _moving_lowpass(bed, float(cfg["air"]), float(cfg["air"]) * 0.35, stages=10)
    bed = (bed * _env(sec, 0.200, sec * 0.60)).astype(np.float32)

    strike = synth.modal_impact(
        [Mode(root * 0.75, 0.20, 1.0), Mode(root * 1.5, 0.10, 0.45), Mode(root * 2.6, 0.05, 0.2)],
        0.70, seed=s + 9, noise_amount=0.22, noise_tau=0.012)

    canvas = synth.silence(sec)
    synth.place(canvas, body, 0.0, 0.62)
    synth.place(canvas, sub, 0.0, 0.80)
    synth.place(canvas, bed, 0.0, float(cfg["bed"]))
    synth.place(canvas, strike, 0.045, 0.55)

    if cfg["shutter"]:
        # 심야: 손전등 반경 −30%. The light closing, as a sound.
        sh = synth.bandpass(synth.white(0.40, s + 13), 700.0, 6000.0)
        sh = _moving_lowpass(sh, 6000.0, 600.0, stages=10)
        sh = (sh * _env(0.40, 0.020, 0.16)).astype(np.float32)
        synth.place(canvas, sh, 0.10, 0.16)

    if cfg["knock"]:
        # 새벽: 괴물이 출입구를 안다. Something already knows where the door is.
        for k, at in enumerate((0.95, 1.13)):
            kn = synth.modal_impact(
                [Mode(126.0, 0.075, 1.0), Mode(196.0, 0.040, 0.4), Mode(305.0, 0.018, 0.15)],
                0.35, seed=s + 31 + k, noise_amount=0.25, noise_tau=0.006)
            synth.place(canvas, synth.lowpass(kn, 900.0), at, 0.20 - 0.05 * k)

    rev_s, rev_mix = cfg["rev"]
    return _polish(synth.reverb(canvas, seconds=float(rev_s), mix=float(rev_mix),
                                seed=s + 41, damping=1800.0))


# ── Heartbeat bed, three intensities ───────────────────────────────────────


def heartbeat(level: int) -> np.ndarray:
    """A crossfadable loop. Exactly 4.000 s, downbeat at +50 ms, in all three.

    Equal length and a shared downbeat are what make an engine crossfade safe:
    proximity and danger can move the blend at any moment without the beat
    sliding or two thumps landing on top of each other.
    """
    cfg = [
        dict(bpm=60.0, beats=4, thump_cut=200.0, rush=0.0, drive=0.0, seed=SEED + 2500),
        dict(bpm=90.0, beats=6, thump_cut=320.0, rush=0.085, drive=0.0, seed=SEED + 2600),
        dict(bpm=120.0, beats=8, thump_cut=460.0, rush=0.150, drive=1.35, seed=SEED + 2700),
    ][level]

    sec = HEARTBEAT_LOOP_SECONDS
    s = int(cfg["seed"])
    period = 60.0 / float(cfg["bpm"])
    first = 0.05                      # leaves the loop seam inside the quiet gap
    canvas = synth.silence(sec)

    for k in range(int(cfg["beats"])):
        t0 = first + k * period
        # lub — the louder, lower of the pair
        lub = (_tone(54.0, 0.34, attack=0.008, tau=0.055)
               + _tone(76.0, 0.34, attack=0.007, tau=0.038) * 0.45).astype(np.float32)
        chest = synth.lowpass(synth.white(0.26, s + 17 * k + 1), float(cfg["thump_cut"]))
        chest = (chest * _env(0.26, 0.004, 0.050)).astype(np.float32)
        # dub — the answer, quieter and a touch higher
        dub = (_tone(62.0, 0.26, attack=0.006, tau=0.042)
               + _tone(88.0, 0.26, attack=0.006, tau=0.030) * 0.40).astype(np.float32)
        chest2 = synth.lowpass(synth.white(0.20, s + 17 * k + 2), float(cfg["thump_cut"]) * 0.85)
        chest2 = (chest2 * _env(0.20, 0.004, 0.038)).astype(np.float32)

        synth.place(canvas, lub, t0, 1.00)
        synth.place(canvas, chest, t0, 0.34)
        dub_at = t0 + min(0.30 * period, 0.22)
        synth.place(canvas, dub, dub_at, 0.60)
        synth.place(canvas, chest2, dub_at, 0.20)

        if float(cfg["rush"]) > 0.0:
            # Blood in the ears. Beat-synced rather than continuous, so the loop
            # seam stays inside silence and needs no dip.
            rush = synth.bandpass(synth.pink(0.22, s + 17 * k + 3), 520.0, 1650.0)
            rush = (rush * _env(0.22, 0.022, 0.085)).astype(np.float32)
            synth.place(canvas, rush, t0 + 0.012, float(cfg["rush"]))

    if float(cfg["drive"]) > 0.0:
        canvas = synth.saturate(canvas * 0.8, drive=float(cfg["drive"]))

    return _polish(canvas, hp_hz=24.0)


# ── §02 · The two outcomes, which must not be interchangeable ───────────────


def escape_success() -> np.ndarray:
    """§02: someone got out, so the match's information survives. Relief, with cost."""
    sec = 3.40
    s = SEED + 2800

    # Outside air arriving.
    air = synth.pink(sec, s + 3)
    air = _moving_lowpass(air, 900.0, 8200.0, stages=14)
    air_env = _env(sec, 0.500, 2.4)
    air = (air * air_env).astype(np.float32)

    wind = synth.highpass(synth.pink(sec, s + 5), 2000.0)
    wind = synth.tremolo(wind, 0.55, depth=0.5) * _env(sec, 0.700, 2.0)

    # A bare fifth lifting. Bare, not major: §02's 생존 is not a victory.
    lift_a = _tone(146.8, 2.60, attack=0.250, tau=1.80)
    lift_b = _tone(220.0, 2.20, attack=0.300, tau=2.00) * 0.55
    bell = synth.modal_impact(
        [Mode(220.0, 1.40, 1.0), Mode(329.6, 0.90, 0.45), Mode(440.0, 0.60, 0.22)],
        2.10, seed=s + 9, noise_amount=0.08, noise_tau=0.010)

    # Pressure releasing rather than arriving — the opposite of the wipe. The short
    # attack matters: a 49 Hz tone that starts on a step is a thump, and write_wav's
    # 2 ms de-click fade is a fifth of a cycle at this frequency.
    release = (synth.sine(49.0, 1.40) * np.linspace(1.0, 0.0, synth.n_samples(1.40),
                                                    dtype=np.float32) ** 1.6).astype(np.float32)
    a = synth.n_samples(0.045)
    release[:a] *= (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, a, dtype=np.float32))).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, air, 0.0, 0.26)
    synth.place(canvas, wind.astype(np.float32), 0.0, 0.10)
    synth.place(canvas, lift_a, 0.10, 0.50)
    synth.place(canvas, lift_b, 0.65, 0.50)
    synth.place(canvas, bell, 1.10, 0.22)
    synth.place(canvas, release, 0.0, 0.45)

    return _polish(synth.reverb(canvas, seconds=2.6, mix=0.30, seed=s + 17, damping=5000.0))


def match_failure_wipe() -> np.ndarray:
    """§02 전멸: 그 판의 모든 것을 잃는다. Built as the inverse of `escape_success`."""
    sec = 4.40
    s = SEED + 2900
    gate_at = 3.90

    # The realisation: a swell that arrives from nowhere.
    swell = synth.bandpass(synth.pink(0.60, s + 3), 300.0, 3800.0)
    swell = (swell * (np.linspace(0.0, 1.0, synth.n_samples(0.60), dtype=np.float32) ** 2.2)).astype(np.float32)

    # Pressure arriving, and staying.
    sub = _tone(41.0, 3.60, attack=0.120, tau=2.20)
    fall_a = (_glide(110.0, 52.0, 2.80) * _env(2.80, 0.180, 1.80)).astype(np.float32)
    fall_b = (_glide(146.5, 69.0, 2.80) * _env(2.80, 0.220, 1.50)).astype(np.float32) * 0.45

    bed = synth.brown(3.60, s + 7)
    bed = _moving_lowpass(bed, 900.0, 220.0, stages=12)
    bed = (bed * _env(3.60, 0.250, 1.90)).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, swell, 0.0, 0.14)
    synth.place(canvas, sub, 0.50, 0.90)
    synth.place(canvas, fall_a, 0.50, 0.40)
    synth.place(canvas, fall_b, 0.55, 0.40)
    synth.place(canvas, bed, 0.50, 0.22)

    wet = synth.reverb(canvas, seconds=2.2, mix=0.20, seed=s + 23, damping=1500.0)
    # The gate goes last so the silence is genuinely silence, tail included.
    return _polish(_gate_off(wet, gate_at, fall=0.030))


# ── §08 · Shop UI ───────────────────────────────────────────────────────────


def shop_open() -> np.ndarray:
    """§08: the surface vehicle. Also the safe zone, so a warm hum comes with it."""
    sec = 1.30
    s = SEED + 3000

    latch = synth.modal_impact(
        [Mode(1450.0, 0.030, 1.0), Mode(2100.0, 0.018, 0.5), Mode(2950.0, 0.010, 0.25)],
        0.16, seed=s + 3, noise_amount=0.50, noise_tau=0.002)
    door = synth.bandpass(synth.pink(0.38, s + 5), 180.0, 1500.0)
    door = (door * _rough_env(0.38, 9.0, s + 6) * _env(0.38, 0.020, 0.18)).astype(np.float32)
    hum = (_tone(118.0, 0.75, attack=0.250, tau=0.80)
           + _tone(236.0, 0.75, attack=0.300, tau=0.60) * 0.30).astype(np.float32)
    settle = synth.modal_impact(
        [Mode(160.0, 0.055, 1.0), Mode(248.0, 0.028, 0.4)],
        0.20, seed=s + 9, noise_amount=0.30, noise_tau=0.005)

    canvas = synth.silence(sec)
    synth.place(canvas, latch, 0.0, 0.55)
    synth.place(canvas, door, 0.055, 0.40)
    synth.place(canvas, hum, 0.120, 0.30)
    synth.place(canvas, settle, 0.420, 0.35)

    return _polish(synth.reverb(canvas, seconds=0.5, mix=0.10, seed=s + 15, damping=4200.0))


def shop_close() -> np.ndarray:
    """§08: the hum goes away with it. Time keeps running either way (§07)."""
    sec = 0.95
    s = SEED + 3100

    door = synth.bandpass(synth.pink(0.32, s + 5), 170.0, 1400.0)
    door = (door * _rough_env(0.32, 10.0, s + 6) * _env(0.32, 0.018, 0.20)).astype(np.float32)
    hum = (_tone(118.0, 0.36, attack=0.030, tau=1.2)
           + _tone(236.0, 0.36, attack=0.040, tau=1.0) * 0.30).astype(np.float32)
    hum = _gate_off(hum, 0.30, fall=0.100)      # the warmth is withdrawn, not faded out
    latch = synth.modal_impact(
        [Mode(980.0, 0.028, 1.0), Mode(1520.0, 0.014, 0.45), Mode(2260.0, 0.008, 0.2)],
        0.18, seed=s + 3, noise_amount=0.55, noise_tau=0.002)
    thud = synth.modal_impact(
        [Mode(112.0, 0.070, 1.0), Mode(176.0, 0.035, 0.4)],
        0.26, seed=s + 9, noise_amount=0.28, noise_tau=0.006)

    canvas = synth.silence(sec)
    synth.place(canvas, door, 0.0, 0.42)
    synth.place(canvas, hum, 0.0, 0.26)
    synth.place(canvas, latch, 0.340, 0.55)
    synth.place(canvas, thud, 0.360, 0.45)

    return _polish(synth.reverb(canvas, seconds=0.45, mix=0.09, seed=s + 15, damping=3600.0))


def shop_denied() -> np.ndarray:
    """§08: not enough credits, and the wallet is shared. A flat refusal, no tail."""
    sec = 0.55
    s = SEED + 3200

    canvas = synth.silence(sec)
    for k, at in enumerate((0.0, 0.075)):
        click = synth.bandpass(synth.white(0.012, s + 5 * k + 1), 900.0, 2600.0)
        click = (click * _env(0.012, 0.0008, 0.0035)).astype(np.float32)
        synth.place(canvas, click, at, 0.60 - 0.12 * k)

    no = (_tone(196.0, 0.22, attack=0.010, tau=0.100)
          + _tone(185.0, 0.22, attack=0.010, tau=0.090) * 0.80).astype(np.float32)  # beating: unresolved
    no = synth.lowpass(no, 700.0)
    damp = synth.modal_impact(
        [Mode(124.0, 0.045, 1.0), Mode(192.0, 0.022, 0.35)],
        0.18, seed=s + 11, noise_amount=0.20, noise_tau=0.004)

    synth.place(canvas, no, 0.055, 0.55)
    synth.place(canvas, damp, 0.060, 0.40)
    return _polish(canvas)


# ── §13 · Proximity voice feedback ──────────────────────────────────────────


def voice_activity_blip() -> np.ndarray:
    """§13: mic is open. Fires constantly, so it is nearly the quietest clip here."""
    sec = 0.09
    pip = (_tone(1050.0, sec, attack=0.006, tau=0.022)
           + _tone(1575.0, sec, attack=0.007, tau=0.016) * 0.25).astype(np.float32)
    return _polish(synth.bandpass(pip, 600.0, 3200.0), hp_hz=200.0)


def voice_out_of_range() -> np.ndarray:
    """§13: past VoiceCutoffDistance (30 m). The channel losing carrier.

    §13 cuts voice at the sender, so the far player simply stops existing on the
    channel. Without a cue that reads as *the link*, players learn nothing and
    just repeat themselves — hence telephone band, closing.
    """
    sec = 0.62
    s = SEED + 3300

    ch = synth.bandpass(synth.pink(0.50, s + 3), 300.0, 2600.0)   # the voice band itself
    # Closing further than it needs to, for two reasons: it reads more like a
    # carrier dying, and it keeps the cue's centre of mass clear of the ghost
    # rattles. This is a statement about a known teammate, not a hint.
    ch = _moving_lowpass(ch, 2600.0, 380.0, stages=12)
    ch = (ch * _env(0.50, 0.012, 0.26)).astype(np.float32)
    carrier = (_glide(470.0, 385.0, 0.44) * _env(0.44, 0.020, 0.22)).astype(np.float32)
    dead = synth.lowpass(synth.white(0.010, s + 7), 1100.0) * _env(0.010, 0.0008, 0.003)

    canvas = synth.silence(sec)
    synth.place(canvas, ch, 0.0, 0.70)
    synth.place(canvas, carrier, 0.0, 0.26)
    synth.place(canvas, dead.astype(np.float32), 0.440, 0.35)
    return _polish(canvas, hp_hz=140.0)


# ── §03 · The round trip ────────────────────────────────────────────────────


def descend_basement() -> np.ndarray:
    """§03: back in. The mix gets smaller — air closes, pressure arrives."""
    sec = 2.45
    s = SEED + 3400

    air = synth.pink(sec, s + 3)
    air = _moving_lowpass(air, 6200.0, 620.0, stages=14)
    air = (air * _env(sec, 0.300, 1.60)).astype(np.float32)
    press = _tone(48.0, sec, attack=0.350, tau=1.40)
    scuff = synth.bandpass(synth.pink(0.30, s + 5), 200.0, 1300.0)
    scuff = (scuff * _rough_env(0.30, 16.0, s + 6) * _env(0.30, 0.020, 0.14)).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, air, 0.0, 0.30)
    synth.place(canvas, press, 0.0, 0.70)
    synth.place(canvas, scuff, 0.100, 0.26)

    # A small dense room, not a hall. §12 gives zones their own acoustics.
    return _polish(synth.reverb(canvas, seconds=0.8, mix=0.26, seed=s + 13, damping=2400.0))


def surface_reached() -> np.ndarray:
    """§03: out for a breath. "나가는 것은 숨 돌리기이지 리셋이 아니다" — so no fanfare."""
    sec = 2.65
    s = SEED + 3500

    air = synth.pink(sec, s + 3)
    air = _moving_lowpass(air, 620.0, 8600.0, stages=14)
    air = (air * _env(sec, 0.350, 1.90)).astype(np.float32)
    wind = synth.highpass(synth.pink(sec, s + 5), 1800.0)
    wind = (synth.tremolo(wind, 0.6, depth=0.55) * _env(sec, 0.500, 1.70)).astype(np.float32)
    # Pressure leaving, mirroring `descend_basement`'s arrival. Attack as in
    # `escape_success`: a sub starting on a step is a thump, not a release.
    release = (synth.sine(46.0, 1.10) * np.linspace(1.0, 0.0, synth.n_samples(1.10),
                                                    dtype=np.float32) ** 1.5).astype(np.float32)
    a = synth.n_samples(0.040)
    release[:a] *= (0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, a, dtype=np.float32))).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, air, 0.0, 0.30)
    synth.place(canvas, wind, 0.0, 0.13)
    synth.place(canvas, release, 0.0, 0.55)

    return _polish(synth.reverb(canvas, seconds=2.2, mix=0.28, seed=s + 13, damping=6000.0))


# ── Registry ────────────────────────────────────────────────────────────────


@dataclass(frozen=True)
class Clip:
    name: str
    buf: np.ndarray
    peak_db: float
    stereo: bool
    group: str

    @property
    def is_loop(self) -> bool:
        return self.group == "heartbeat"


def build_all() -> List[Clip]:
    clips: List[Clip] = [
        Clip("clue_read_success", clue_read_success(), LVL_CLUE_OK, True, "clue"),
        Clip("clue_read_failed", clue_read_failed(), LVL_CLUE_FAIL, True, "clue"),
        Clip("objective_found", objective_found(), LVL_OBJ_FOUND, True, "objective"),
        Clip("objective_pickup", objective_pickup(), LVL_OBJ_PICKUP, True, "objective"),
        Clip("death_transition_01", death_transition(0), LVL_DEATH, True, "death"),
        Clip("death_transition_02", death_transition(1), LVL_DEATH, True, "death"),
    ]
    for i in range(4):
        clips.append(Clip(f"ghost_rattle_{i + 1:02d}", ghost_rattle(i), LVL_RATTLE, False, "rattle"))
    clips.append(Clip("ghost_rattle_ready", ghost_rattle_ready(), LVL_RATTLE_READY, True, "ghost_ui"))

    for i, name in enumerate(("threat_night", "threat_late_night",
                              "threat_pre_dawn", "threat_before_sunrise")):
        clips.append(Clip(name, threat_stinger(i), LVL_THREAT[i], True, "threat"))

    for i, name in enumerate(("heartbeat_low", "heartbeat_mid", "heartbeat_high")):
        clips.append(Clip(name, heartbeat(i), LVL_HEARTBEAT[i], True, "heartbeat"))

    clips += [
        Clip("escape_success", escape_success(), LVL_ESCAPE, True, "outcome"),
        Clip("match_failure_wipe", match_failure_wipe(), LVL_WIPE, True, "outcome"),
        Clip("shop_open", shop_open(), LVL_SHOP, True, "shop"),
        Clip("shop_close", shop_close(), LVL_SHOP, True, "shop"),
        Clip("shop_denied", shop_denied(), LVL_SHOP_DENIED, True, "shop"),
        Clip("voice_activity_blip", voice_activity_blip(), LVL_VOICE_BLIP, True, "voice"),
        Clip("voice_out_of_range", voice_out_of_range(), LVL_VOICE_OUT, True, "voice"),
        Clip("descend_basement", descend_basement(), LVL_TRAVERSAL, True, "traversal"),
        Clip("surface_reached", surface_reached(), LVL_TRAVERSAL, True, "traversal"),
    ]
    return clips


# ── Measurement beyond assert_usable ────────────────────────────────────────


@dataclass(frozen=True)
class Extra:
    """Design-specific measurements `synth.analyse` does not cover."""

    rms_db: float
    onset_ms: float
    head_db: float      # first 10 ms RMS, relative to peak
    end_db: float       # last 10 ms RMS, relative to peak — truncation and loop seams
    tail_db: float      # last 300 ms RMS, relative to whole-clip RMS
    noticed_frac: float  # share of power in 500–6000 Hz, where a faint sound registers
    air_frac: float      # share of power above 2 kHz — how much definition is left
    sha256: str


def _measure(path: str) -> Extra:
    data, sr = synth.read_wav(path)
    peak = float(np.max(np.abs(data))) or 1e-12
    rms = float(np.sqrt(np.mean(np.square(data.astype(np.float64))))) or 1e-12

    # 5 ms moving-average envelope: onset is time from the first audible sample to
    # the envelope's peak. Measured globally, so a sound made of ticks that grows
    # into itself reads as soft, which is exactly the ghost rattle's construction.
    win = max(1, synth.n_samples(0.005, sr))
    env = np.convolve(np.abs(data.astype(np.float64)), np.ones(win) / win, mode="same")
    e_peak = float(np.max(env)) or 1e-12
    above = np.flatnonzero(env >= 0.03 * e_peak)
    i0 = int(above[0]) if len(above) else 0
    i1 = int(np.argmax(env))
    onset_ms = max(0.0, (i1 - i0) / float(sr) * 1000.0)

    edge_n = max(1, synth.n_samples(0.010, sr))
    head = float(np.sqrt(np.mean(np.square(data[:edge_n].astype(np.float64)))))
    end = float(np.sqrt(np.mean(np.square(data[-edge_n:].astype(np.float64)))))
    tail_n = min(len(data), max(1, synth.n_samples(0.300, sr)))
    tail = float(np.sqrt(np.mean(np.square(data[-tail_n:].astype(np.float64)))))

    # Power fractions. A spectral centroid alone is dominated by whatever sub is
    # present, so it can report "dark" for four clips that differ audibly and
    # "dark" for four that do not. These say where the energy actually is.
    spec = np.abs(np.fft.rfft(data.astype(np.float64) * np.hanning(len(data)))) ** 2
    fr = np.fft.rfftfreq(len(data), 1.0 / sr)
    total = float(spec.sum()) or 1e-30
    noticed = float(spec[(fr >= 500.0) & (fr < 6000.0)].sum()) / total
    air = float(spec[fr >= 2000.0].sum()) / total

    with open(path, "rb") as fh:
        digest = hashlib.sha256(fh.read()).hexdigest()

    return Extra(
        rms_db=synth.gain_to_db(rms),
        onset_ms=onset_ms,
        head_db=synth.gain_to_db(max(head, 1e-12) / peak),
        end_db=synth.gain_to_db(max(end, 1e-12) / peak),
        tail_db=synth.gain_to_db(max(tail, 1e-12) / rms),
        noticed_frac=noticed,
        air_frac=air,
        sha256=digest,
    )


# ── Design checks ───────────────────────────────────────────────────────────


def run_checks(clips: List[Clip], reports: Dict[str, synth.ClipReport],
               extras: Dict[str, Extra]) -> List[str]:
    """Asserts the design claims this set makes. Raises on the first violation."""
    lines: List[str] = []

    def ok(msg: str) -> None:
        lines.append(f"  PASS  {msg}")

    # ── §12: UI must never mask a footstep ────────────────────────────────
    worst = max(reports.values(), key=lambda r: r.peak_db)
    for r in reports.values():
        assert r.peak_db <= synth.UI_HEADROOM_DB + 0.05, \
            f"{r.path}: peak {r.peak_db:.2f} dB exceeds UI_HEADROOM_DB"
    ok(f"§12  every clip at or under UI_HEADROOM_DB ({synth.UI_HEADROOM_DB:.1f} dB); "
       f"loudest is {os.path.basename(worst.path)} at {worst.peak_db:.2f} dB")

    # ── Channel layout ────────────────────────────────────────────────────
    for name, r in reports.items():
        want = 1 if name.startswith("ghost_rattle_0") else 2
        assert r.channels == want, f"{name}: {r.channels} channels, expected {want}"
    ok("§09  the four ghost rattles are mono (Unity will not spatialise stereo); "
       "everything else is 2-channel non-positional")

    for name, r in reports.items():
        assert r.sample_rate == synth.SAMPLE_RATE, name
    ok(f"all clips at {synth.SAMPLE_RATE} Hz, 16-bit PCM")

    # ── No clip ends on a truncation ──────────────────────────────────────
    worst_end = max((c for c in clips if not c.is_loop), key=lambda c: extras[c.name].end_db)
    for c in clips:
        if c.is_loop:
            continue
        assert extras[c.name].end_db <= -30.0, \
            f"{c.name}: still at {extras[c.name].end_db:.1f} dB below peak at the final sample — " \
            f"write_wav's 6 ms out-fade will truncate it audibly"
    ok(f"every one-shot decays to silence before its last sample; the closest call is "
       f"{worst_end.name} at {extras[worst_end.name].end_db:.1f} dB below peak")

    # ── §09: the ghost rattle's ambiguity window ──────────────────────────
    rattles = [f"ghost_rattle_{i:02d}" for i in range(1, 5)]
    # Everything a *living* player can hear from this set. The cooldown cue is
    # excluded: only the ghost hears it (§09), and it is allowed to be clear.
    living = [n for n in extras if n not in rattles and n != "ghost_rattle_ready"]
    non_rattle_rms = sorted(extras[n].rms_db for n in living)
    median_rms = non_rattle_rms[len(non_rattle_rms) // 2]
    faintest_living = min(living, key=lambda n: extras[n].rms_db)
    for n in rattles:
        e, r = extras[n], reports[n]
        assert RATTLE_RMS_FLOOR_DB <= e.rms_db <= RATTLE_RMS_CEIL_DB, \
            f"{n}: RMS {e.rms_db:.1f} dB outside the ambiguity window " \
            f"[{RATTLE_RMS_FLOOR_DB}, {RATTLE_RMS_CEIL_DB}]"
        assert e.onset_ms >= 25.0, \
            f"{n}: onset {e.onset_ms:.1f} ms — too sharp, it reads as a signal"
        assert 600.0 <= r.spectral_centroid <= 4500.0, \
            f"{n}: centroid {r.spectral_centroid:.0f} Hz outside the sensitive band"
        assert e.noticed_frac >= 0.60, \
            f"{n}: only {e.noticed_frac * 100:.0f}% of its energy is in 500–6000 Hz — the " \
            f"rest is in the heartbeat bed's register, where this variant would be masked"
        # §09's ghost has no voice and no escape; the one thing it can do must be
        # the faintest thing in the game's UI vocabulary, or it stops being deniable.
        assert e.rms_db < extras[faintest_living].rms_db, \
            f"{n} ({e.rms_db:.1f} dB) is not quieter than {faintest_living} " \
            f"({extras[faintest_living].rms_db:.1f} dB) — the ghost is no longer the " \
            f"faintest thing a living player hears"
    span = (min(extras[n].rms_db for n in rattles), max(extras[n].rms_db for n in rattles))
    assert span[1] - span[0] <= 6.0, \
        f"rattle RMS spread {span[1] - span[0]:.1f} dB — one variant is markedly easier to " \
        f"dismiss than another, so the ghost's odds would depend on which object it picked"
    ok(f"§09  rattle RMS {span[0]:.1f}..{span[1]:.1f} dBFS, inside "
       f"[{RATTLE_RMS_FLOOR_DB}, {RATTLE_RMS_CEIL_DB}], spread {span[1] - span[0]:.1f} dB; "
       f"{median_rms - span[1]:.1f}..{median_rms - span[0]:.1f} dB under the median UI clip")
    ok(f"§09  every rattle is quieter than the faintest cue a living player hears "
       f"({faintest_living}, {extras[faintest_living].rms_db:.1f} dBFS) — "
       f"margin {extras[faintest_living].rms_db - span[1]:.1f} dB")
    ok(f"§09  softest rattle onset {min(extras[n].onset_ms for n in rattles):.0f} ms "
       f"(≥25 required) — no variant snaps")

    cents = sorted(reports[n].spectral_centroid for n in rattles)
    for a, b in zip(cents, cents[1:]):
        assert b / max(a, 1.0) >= 1.10, \
            f"rattle centroids {a:.0f}/{b:.0f} Hz too close — the variants are clones"
    ok("§09  four rattle centroids "
       + ", ".join(f"{c:.0f}" for c in cents)
       + " Hz — every pair ≥10% apart")

    # Faint only works if nothing louder is sitting on top of it. The heartbeat bed
    # is the only thing here that plays continuously, and it lives an octave and a
    # half below the rattles, so the ghost's channel is never masked by the game's
    # own UI — which is what lets §09 get away with −35 dBFS.
    hb_top = max(reports[n].spectral_centroid for n in
                 ("heartbeat_low", "heartbeat_mid", "heartbeat_high"))
    assert min(cents) > hb_top * 2.0, \
        f"the faintest rattle sits at {min(cents):.0f} Hz, too close to the continuous " \
        f"heartbeat bed at {hb_top:.0f} Hz — it would be masked exactly when it matters"
    ok(f"§09  rattles occupy {min(cents):.0f}–{max(cents):.0f} Hz, clear of the only "
       f"continuous bed ({hb_top:.0f} Hz) — faint here does not mean masked")
    ok("§09  rattle energy in 500–6000 Hz: "
       + ", ".join(f"{extras[n].noticed_frac * 100:.0f}%" for n in rattles)
       + " (≥60% required)")

    assert reports["ghost_rattle_ready"].spectral_centroid < min(cents), \
        "ghost_rattle_ready sits in the rattle band — the ghost could confuse its own cue"
    ok(f"§09  cooldown cue at {reports['ghost_rattle_ready'].spectral_centroid:.0f} Hz, "
       f"below every rattle — not mistakable for the rattle it announces")

    # ── §07: each tier darker than the last ───────────────────────────────
    tiers = ["threat_night", "threat_late_night", "threat_pre_dawn", "threat_before_sunrise"]
    for a, b in zip(tiers, tiers[1:]):
        assert reports[b].spectral_centroid < reports[a].spectral_centroid, \
            f"{b} is brighter than {a} — §07 requires each tier darker"
        assert reports[b].seconds > reports[a].seconds, \
            f"{b} is shorter than {a} — the tail should lengthen with the night"
        # Centroid alone can fall while every tier is equally sub-dominated and
        # therefore equally featureless. The air share is what a player hears go away.
        assert extras[b].air_frac < extras[a].air_frac, \
            f"{b} has as much air as {a} ({extras[b].air_frac:.4f} vs " \
            f"{extras[a].air_frac:.4f}) — the darkening is not audible, only measurable"
    # Absolute air fractions are tiny in anything with sub content — power goes as
    # amplitude², so even the brightest clip in this set holds well under 1% above
    # 2 kHz. What matters is the span: the last boundary must have lost most of what
    # the first one had, or "darker each time" is a rounding artefact.
    # The air share is measured only for monotonicity: by the last tier it is small
    # enough that a ratio between the ends is numerical noise, so the span test uses
    # the wider 500–6000 Hz band instead.
    noticed_span = extras[tiers[0]].noticed_frac / max(extras[tiers[-1]].noticed_frac, 1e-12)
    assert noticed_span >= 2.0, \
        f"the 500–6000 Hz share only spans {noticed_span:.1f}× across the four tiers — " \
        f"a player would not hear the difference between the first and the last"
    ok("§07  centroid falls "
       + " → ".join(f"{reports[n].spectral_centroid:.0f}" for n in tiers)
       + " Hz and duration grows "
       + " → ".join(f"{reports[n].seconds:.2f}" for n in tiers) + " s")
    ok("§07  air above 2 kHz drains "
       + " → ".join(f"{extras[n].air_frac * 100:.4f}%" for n in tiers)
       + " and the 500–6000 Hz share "
       + " → ".join(f"{extras[n].noticed_frac * 100:.2f}%" for n in tiers)
       + f" ({noticed_span:.1f}× span) — each boundary has less definition than the last")

    # ── Heartbeat: crossfade safety ───────────────────────────────────────
    hb = ["heartbeat_low", "heartbeat_mid", "heartbeat_high"]
    frames = {n: round(reports[n].seconds * synth.SAMPLE_RATE) for n in hb}
    assert len(set(frames.values())) == 1, f"heartbeat loops differ in length: {frames}"
    assert next(iter(frames.values())) == synth.n_samples(HEARTBEAT_LOOP_SECONDS), frames
    for n in hb:
        seam = max(extras[n].head_db, extras[n].end_db)
        assert seam <= -34.0, \
            f"{n}: loop seam only {seam:.1f} dB below peak — it will tick once per bar"
        assert reports[n].spectral_centroid < FOOTSTEP_BAND_FLOOR_HZ, \
            f"{n}: centroid {reports[n].spectral_centroid:.0f} Hz reaches into the band " \
            f"§12 needs for floor material"
    for a, b in zip(hb, hb[1:]):
        assert extras[b].rms_db > extras[a].rms_db, f"{b} is not more intense than {a}"
    ok(f"heartbeat: all three exactly {frames[hb[0]]} frames "
       f"({HEARTBEAT_LOOP_SECONDS:.3f} s), shared downbeat — crossfade cannot drift")
    ok("heartbeat: loop seams "
       + ", ".join(f"{max(extras[n].head_db, extras[n].end_db):.0f}" for n in hb)
       + " dB below peak (≤ −34 required)")
    ok("heartbeat: RMS rises "
       + " → ".join(f"{extras[n].rms_db:.1f}" for n in hb) + " dBFS")
    ok("§12  heartbeat centroids "
       + ", ".join(f"{reports[n].spectral_centroid:.0f}" for n in hb)
       + f" Hz — the one continuous bed sits under {FOOTSTEP_BAND_FLOOR_HZ:.0f} Hz, "
         "below where the five floor materials are told apart")

    # ── §02: the two outcomes are not interchangeable ─────────────────────
    esc, wipe = reports["escape_success"], reports["match_failure_wipe"]
    assert esc.spectral_centroid > wipe.spectral_centroid * 2.0, \
        "escape and wipe are spectrally similar — §02's asymmetry is not audible"
    assert extras["match_failure_wipe"].tail_db < -50.0, \
        "the wipe does not end in silence — 전멸 must leave nothing"
    assert extras["escape_success"].tail_db > extras["match_failure_wipe"].tail_db + 30.0, \
        "escape ends as dead as the wipe"
    ok(f"§02  escape centroid {esc.spectral_centroid:.0f} Hz vs wipe "
       f"{wipe.spectral_centroid:.0f} Hz ({esc.spectral_centroid / wipe.spectral_centroid:.1f}×); "
       f"wipe tail {extras['match_failure_wipe'].tail_db:.0f} dB vs escape "
       f"{extras['escape_success'].tail_db:.0f} dB")

    # ── §03: the round trip is directional ────────────────────────────────
    assert reports["surface_reached"].spectral_centroid > reports["descend_basement"].spectral_centroid, \
        "surfacing is not brighter than descending — the round trip has no direction"
    ok(f"§03  descend {reports['descend_basement'].spectral_centroid:.0f} Hz → surface "
       f"{reports['surface_reached'].spectral_centroid:.0f} Hz: the mix opens on the way up")

    # ── §03: failure costs more than success ──────────────────────────────
    assert reports["clue_read_failed"].seconds > reports["clue_read_success"].seconds, \
        "the failed read is not longer than the successful one — lost time is not audible"
    ok(f"§03  failed read {reports['clue_read_failed'].seconds:.2f} s vs success "
       f"{reports['clue_read_success'].seconds:.2f} s — failure occupies more time")

    # ── §13: the voice cues cannot be confused with the ghost ─────────────
    # Both are faint, so keep them apart on the two axes a player actually uses:
    # where the energy sits, and how insistent it is. Channel count is the third —
    # the rattle is mono and placed, this is centred — and is asserted above.
    vr = reports["voice_out_of_range"].spectral_centroid
    for n in rattles:
        assert abs(vr - reports[n].spectral_centroid) / max(vr, 1.0) >= 0.15, \
            f"voice_out_of_range ({vr:.0f} Hz) and {n} " \
            f"({reports[n].spectral_centroid:.0f} Hz) sit at the same centroid"
    assert extras["voice_out_of_range"].rms_db >= span[1] + 5.0, \
        f"voice_out_of_range ({extras['voice_out_of_range'].rms_db:.1f} dB) is no more " \
        f"insistent than the loudest rattle ({span[1]:.1f} dB) — a known teammate " \
        f"leaving range must not be as deniable as the ghost"
    ok(f"§13  voice_out_of_range at {vr:.0f} Hz, ≥15% clear of every rattle centroid, and "
       f"{extras['voice_out_of_range'].rms_db - span[1]:.1f} dB louder than the loudest rattle")
    assert extras["voice_activity_blip"].rms_db < median_rms - 6.0, \
        "the voice blip is not quiet enough for something that fires constantly"
    ok(f"§13  voice blip {extras['voice_activity_blip'].rms_db:.1f} dBFS RMS, "
       f"{median_rms - extras['voice_activity_blip'].rms_db:.1f} dB under the median UI clip")

    return lines


# ── Main ────────────────────────────────────────────────────────────────────


def main() -> int:
    clips = build_all()

    reports: Dict[str, synth.ClipReport] = {}
    extras: Dict[str, Extra] = {}
    ordered: List[synth.ClipReport] = []

    for c in clips:
        path = os.path.join(OUT_DIR, f"{c.name}.wav")
        buf = c.buf if c.is_loop else _settle(c.buf, min(0.30, 0.22 * len(c.buf) / SR))
        synth.write_wav(path, buf, headroom_db=c.peak_db, stereo=c.stereo)
        r = synth.assert_usable(path)
        reports[c.name] = r
        extras[c.name] = _measure(path)
        ordered.append(r)

    print(f"UI / stinger / ghost set → {OUT_DIR}")
    print(f"{len(clips)} clips\n")
    print(synth.report_table(ordered))

    print("\nDesign measurements  (head/end are 10 ms edge RMS relative to peak; tail is")
    print("                      the last 300 ms relative to the whole clip; 0.5-6k is the")
    print("                      power share in the band where a faint sound registers)")
    print(f"{'clip':<26} {'group':<10} {'rms dB':>7} {'onset ms':>9} "
          f"{'head dB':>8} {'end dB':>8} {'tail dB':>8} {'0.5-6k':>7}")
    print("-" * 90)
    for c in clips:
        e = extras[c.name]
        print(f"{c.name:<26} {c.group:<10} {e.rms_db:>7.1f} {e.onset_ms:>9.1f} "
              f"{e.head_db:>8.1f} {e.end_db:>8.1f} {e.tail_db:>8.1f} "
              f"{e.noticed_frac * 100:>6.2f}%")

    print("\nDesign checks")
    for line in run_checks(clips, reports, extras):
        print(line)

    print("\nsha256 (a rebuild must reproduce these)")
    for c in clips:
        print(f"  {extras[c.name].sha256[:16]}  {c.name}.wav")

    print("\nOK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
