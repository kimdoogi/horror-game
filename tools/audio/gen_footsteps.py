"""Footsteps — the Listener's map.

Run:
    /Users/doogi/horror-game/tools/audio/.venv/bin/python \
        /Users/doogi/horror-game/tools/audio/gen_footsteps.py

WHAT THIS IS FOR IN GAMEPLAY TERMS
----------------------------------
§04 gives the 청음사 (Listener) exactly one ability: read the monster's
"위치 · 거리 · 이동 방향" from sound alone. §12 states how that is supposed to
work — "바닥 재질이 지도다" — five zones, five floor materials, and the
Listener knows *which zone* the monster is in from which surface its feet land
on:

    A 나무 삐걱   B 타일 딱딱+반향   C 자갈 부스럭   D 콘크리트 둔탁   계단 금속 울림

That makes these clips a **gameplay channel, not decoration**. §12 says it in as
many words: "아트 결정이 아니라 시스템 결정이다." A player who cannot tell tile
from concrete has no Listener role, so the deliverable here is not "footstep
sounds" but *five mutually separable spectral identities*, verified by
measurement at the bottom of this file, plus a monster whose steps are
unmistakably not a teammate's.

Every clip in this set:

* **Mono.** §05 makes 3D audio load-bearing ("3D 오디오는 카메라 기준 →
  헤드폰 필수") and the Listener triangulates by turning their body. Unity will
  not spatialise a stereo clip, so stereo here would silently delete the role.
* **Dry.** The engine places these. The one exception is tile's short early
  reflections — see `Surface.early` below.
* **Quiet, with headroom.** §06 makes the monster's Standstill state silent
  ("침묵이 가장 무서운 소리다"). Silence only works as a weapon if the sound
  around it is restrained; a loud footstep bed makes its absence unreadable.
  Walk sits ~9 dB under the monster's step on purpose — §04 also penalises the
  Listener for their *own* noise ("자기가 소리를 내면 못 듣는다"), so the
  player's own feet must not mask the thing they are listening for.

The three actors, and why each exists
-------------------------------------
* `player_walk` — 2.0 m/s (§06). Soft heel-then-toe double contact. The
  quietest thing here.
* `player_run` — 4.5 m/s (§06). One hard slap, far more contact noise, no time
  for a heel-toe roll. Loud enough that a running teammate genuinely blinds the
  Listener, which is the §04 constraint made audible.
* `monster_step` — §06 gives the monster footsteps in Patrol, Alert, Chase and
  Search, and *nothing* in Standstill. This is the only clip the team tracks it
  by, so it is built to be identifiable in one step: modes transposed down
  ~6.5 semitones, decays roughly doubled, a sub-thump of body weight, a
  band-limited **drag/scrape** after the impact, and an off-beat second contact
  ~85 ms late that no human gait produces.

Four variants of each, per surface (60 clips). A single repeated footstep is
what makes a horror game feel cheap, and worse here: a machine-gun-identical
step reads as a looping sound cue rather than as a creature walking, which is
exactly the information §04 asks the Listener to extract.

Determinism: every random draw comes from a seed derived by CRC32 from the
clip's own name, so a rebuild produces byte-identical files. `hash()` is
deliberately not used — Python randomises string hashing per process.
"""

from __future__ import annotations

import os
import sys
import zlib
from dataclasses import dataclass
from typing import Dict, List, Optional, Sequence, Tuple

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import synth  # noqa: E402
from synth import Mode  # noqa: E402

# ── Paths and seeding ───────────────────────────────────────────────────────

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(REPO, "unity", "HorrorGame", "Assets", "Audio", "Footsteps")

MASTER_SEED = 0x0F007  # "foot"
"""Bumping this reshuffles every variant. Do not, casually."""

VARIANTS = 4
"""§12 makes these repeat constantly. Four per type is the floor for not
sounding sampled; more is better but blows up the import budget."""

LEAD_IN = 0.003
"""Silence prepended to every clip, to protect the attack transient.

`synth.write_wav` applies a 2 ms fade-in to everything it writes, which is the
right default — a buffer starting on a non-zero sample clicks, and a click is
the loudest thing in a dark mix. But a footstep's peak *is* inside those first
2 ms, so the fade was landing directly on the transient and shaving 2-3 dB off
it. That is the worst possible place to lose level here: the attack is what
carries both the material identity and the distance cue the Listener reads.

Three milliseconds of leading silence puts the fade on silence instead. The
transient survives intact, and as a side effect the written peak now actually
matches the requested headroom instead of landing a couple of dB under it."""

TAIL_HEADROOM_DB = -32.0
"""How far a clip's ring must have decayed before the clip is allowed to end.

A clip that stops while its tail is still at -15 dB does not sound like a
footstep decaying, it sounds like a footstep being switched off, and on metal —
the surface whose whole identity is 울림 — that removes the cue. Asserted in
`main`, because it is easy to break by lengthening a decay and forgetting the
clip that has to contain it."""

SEPARATION_MIN = 1.4
"""Required spectral-centroid ratio between any two surfaces.

The number the task pins, and it is not arbitrary: two materials inside ~1.4x
of each other are the same timbre to a listener under stress in a dark room, and
§12's zone-identification then fails silently — the Listener does not notice
being wrong, they just report the wrong zone."""


def seed_for(*parts: object) -> int:
    """A stable seed from a clip's identity. Reproducible across processes."""
    key = "|".join(str(p) for p in parts).encode("utf-8")
    return (MASTER_SEED + zlib.crc32(key)) % (2 ** 31 - 1)


# ── Small helpers ───────────────────────────────────────────────────────────


def peak(buf: np.ndarray) -> float:
    """Peak magnitude, never zero, so it is safe as a divisor."""
    p = float(np.max(np.abs(buf))) if len(buf) else 0.0
    return p if p > 1e-9 else 1.0


def blend(base: np.ndarray, extra: np.ndarray, amount: float) -> np.ndarray:
    """Adds `extra` at `amount` of `base`'s peak.

    Several helpers in synth.py peak-normalise their output, so adding two of
    them directly loses whatever balance was intended. Scaling by peak ratio
    makes `amount` mean the same thing everywhere.
    """
    if amount <= 0.0:
        return base
    n = max(len(base), len(extra))
    out = np.zeros(n, dtype=np.float32)
    out[: len(base)] += base
    out[: len(extra)] += extra * np.float32(amount * peak(base) / peak(extra))
    return out


def band_shape(buf: np.ndarray, low: float, high: float) -> np.ndarray:
    """The per-surface band limit that makes a material identifiable.

    Applied to the finished clip, tail included, because §12's requirement is
    about what the *whole* footstep sounds like. A dull surface whose decay tail
    is bright measures — and hears — as a bright surface.

    Split into high-pass + low-pass rather than one band-pass: a Butterworth
    band-pass with a 55 Hz lower edge at 48 kHz is numerically marginal, and a
    ringing filter here would be indistinguishable from the material.
    """
    out = synth.highpass(buf, low, order=2 if low < 120.0 else 4)
    return synth.lowpass(out, min(high, 0.94 * 0.5 * synth.SAMPLE_RATE), order=4)


# ── Surfaces (§12 · 청음사 → 바닥 재질이 지도다) ─────────────────────────────


@dataclass(frozen=True)
class Surface:
    """One floor material, i.e. one zone of the map as the Listener hears it."""

    key: str
    zone: str
    """§12's zone letter and the Korean word the design uses for the sound."""

    modes: Tuple[Mode, ...]
    """What the floor rings at when struck. This *is* the material."""

    seconds: float
    """Player-walk clip length. Also a cue in its own right — concrete is over
    before tile has finished, and the Listener uses that."""

    noise_amount: float
    noise_tau: float
    """The broadband contact scrape. Without it a footstep is a tuned bell."""

    band: Tuple[float, float]
    """Global band limit. The main lever on the separation matrix."""

    tilt_hz: float = 0.0
    tilt_q: float = 3.0
    tilt_gain: float = 0.0
    """A resonant emphasis inside the band — the material's "formant"."""

    creak: float = 0.0
    """Wood only. §12: 삐걱. Stick-slip, not resonance."""

    comb_hz: float = 0.0
    comb_fb: float = 0.0
    """Metal only. §12: 울림. A comb makes a struck plate sound like structure."""

    early: Tuple[Tuple[float, float], ...] = ()
    reverb_mix: float = 0.0
    reverb_seconds: float = 0.0
    reverb_damping: float = 4200.0
    """Tile only. §12: 반향."""

    grains: int = 0
    grain_band: Tuple[float, float] = (1500.0, 9000.0)
    grain_spread: float = 0.075
    grain_thud: float = 0.0
    """Gravel only. §12: 부스럭 — many tiny impacts, so there is no single
    struck object to model and `grains` replaces the modal body."""

    sub_scale: float = 1.0
    """How much of the monster's sub-thump this surface transmits.

    Scaled per material rather than fixed: a fixed low thump under all five
    would pull every centroid toward it and collapse the separation the whole
    role depends on. A gravel bed also physically absorbs a footfall's low end
    where a concrete slab transmits it, so this is not only a measurement fix."""

    drag_reach: float = 0.6
    """How far up its own band this material's scrape reaches, in log-band units.

    A scrape is not a small impact. It applies far less peak force over far longer
    contact, so it excites the low modes and barely touches the high ones — a boot
    dragged across steel grating growls, it does not ring. Modelling that is also
    what recovered the monster's metal-vs-tile margin: with the drag spread across
    metal's full band it measured 1725 Hz against 987 Hz for the impact alone,
    dragging zone 계단 to within 1.58x of zone B. Metal therefore keeps a
    deliberately dark rasp (0.5) while tile, which has separation to spare in the
    other direction, keeps a bright ceramic screech (0.78)."""

    drag_scale: float = 1.0
    """Per-material trim on the monster's scrape."""

    strike_spread: float = 0.030
    """How much this material's modal pitch moves depending where the foot lands.

    Not a uniform fudge factor — it differs by material for a physical reason. A
    timber floor's pitch depends strongly on whether you step over a joist or
    mid-span, and a concrete slab's on how large a panel you are standing on, so
    both vary widely. A tile is small, stiff and uniform, so it barely varies.

    Concrete needs the widest setting of the five for a second reason: with four
    modes and a 30 ms decay it has the fewest degrees of freedom in the set, and
    its four variants measured as the most alike of any group."""

    pitch_follow: float = 1.0
    """How much of the actor's pitch scale this material adopts.

    A struck slab or board does drop in pitch under a heavier, larger foot — more
    mass couples to more area and excites the lower modes. A loose aggregate does
    not: gravel's "pitch" is the size of the stones, and a monster does not make
    the stones bigger, it makes *more of them move, further*. So gravel keeps its
    register and expresses weight through grain count, spread and roll length
    instead.

    This is also what saved the separation matrix. With gravel following the
    monster's full -6.5 semitones it landed 1.37x from tile — below the 1.4x
    floor, i.e. a Listener could not have told zone C from zone B from a monster
    step. Measured, then fixed here rather than by widening a filter."""

    level_db: float = 0.0
    """Per-surface loudness trim. Gravel absorbs; tile slaps."""

    def pitch(self, a: "Actor") -> float:
        """This surface's effective frequency scale for a given actor."""
        return 1.0 + self.pitch_follow * (a.pitch - 1.0)


SURFACES: Tuple[Surface, ...] = (
    # D 콘크리트 — 둔탁. Dull, fast decay, minimal ring. The darkest of the five.
    Surface(
        key="concrete",
        zone="D 콘크리트 · 둔탁",
        modes=(
            Mode(72.0, 0.030, 1.00),
            Mode(116.0, 0.024, 0.72),
            Mode(187.0, 0.015, 0.44),
            Mode(298.0, 0.009, 0.22),
        ),
        seconds=0.20,
        noise_amount=0.45,
        noise_tau=0.007,
        band=(48.0, 620.0),
        tilt_hz=170.0,
        tilt_q=2.5,
        tilt_gain=0.30,
        sub_scale=1.00,
        drag_reach=0.88,
        strike_spread=0.065,
        level_db=-1.5,
    ),
    # A 나무 — 삐걱. Low-mid body, moderate decay, plus the creak.
    Surface(
        key="wood",
        zone="A 나무 · 삐걱",
        modes=(
            Mode(104.0, 0.055, 1.00),
            Mode(163.0, 0.048, 0.80),
            Mode(258.0, 0.038, 0.62),
            Mode(408.0, 0.030, 0.46),
            Mode(645.0, 0.022, 0.30),
            Mode(1010.0, 0.014, 0.16),
        ),
        seconds=0.30,
        noise_amount=0.40,
        noise_tau=0.009,
        band=(62.0, 2100.0),
        tilt_hz=880.0,
        tilt_q=3.5,
        tilt_gain=0.34,
        creak=0.30,
        sub_scale=0.80,
        drag_reach=0.78,
        strike_spread=0.050,
        level_db=-1.0,
    ),
    # 계단 금속 — 울림. Strong high modes, long decay, comb-filtered structure.
    Surface(
        key="metal",
        zone="계단 금속 · 울림",
        # Longest taus in the set by a wide margin — this is §12's 울림, and the
        # ring length is a second, independent cue the Listener reads alongside
        # pitch. Capped at 0.17 s rather than the 0.32 s first tried: at 0.32 the
        # tail was still at -15 dB when the clip ended, so the ring was being
        # gated off mid-decay. See TAIL_HEADROOM_DB.
        modes=(
            Mode(196.0, 0.072, 0.55),
            Mode(497.0, 0.110, 0.70),
            Mode(963.0, 0.170, 1.00),
            Mode(1520.0, 0.150, 0.86),
            Mode(2290.0, 0.115, 0.60),
            Mode(3380.0, 0.082, 0.38),
        ),
        seconds=0.72,
        noise_amount=0.32,
        noise_tau=0.006,
        band=(190.0, 8200.0),
        tilt_hz=1420.0,
        tilt_q=2.5,
        tilt_gain=0.30,
        comb_hz=505.0,
        comb_fb=0.52,
        sub_scale=0.42,
        drag_reach=0.50,
        strike_spread=0.035,
        level_db=-0.5,
    ),
    # B 타일 — 딱딱, 반향. Bright, long ring, and a real room slap.
    Surface(
        key="tile",
        zone="B 타일 · 딱딱+반향",
        modes=(
            Mode(1180.0, 0.066, 0.62),
            Mode(2050.0, 0.097, 1.00),
            Mode(3120.0, 0.115, 0.88),
            Mode(4400.0, 0.088, 0.62),
            Mode(5900.0, 0.062, 0.40),
        ),
        seconds=0.50,
        noise_amount=0.38,
        noise_tau=0.004,
        band=(760.0, 8600.0),
        tilt_hz=2700.0,
        tilt_q=2.5,
        tilt_gain=0.28,
        # §12 gives zone B tiled floors that ring, and calls the 반향 part of
        # how the zone identifies itself. synth.reverb warns against baking a
        # tail into a positional clip, and it is right — so this is four
        # discrete early reflections inside 42 ms plus a very short, very dry
        # diffuse skirt, not a room. It reads as "hard surface, hard walls" and
        # is over long before Unity's distance attenuation could smear it.
        early=((0.013, 0.34), (0.021, 0.24), (0.031, 0.17), (0.042, 0.11)),
        reverb_mix=0.16,
        reverb_seconds=0.16,
        reverb_damping=8000.0,
        sub_scale=0.26,
        drag_reach=0.78,
        strike_spread=0.026,
        level_db=0.0,
    ),
    # C 자갈 — 부스럭. Broadband, no pitch, granular. The brightest.
    Surface(
        key="gravel",
        zone="C 자갈 · 부스럭",
        modes=(
            Mode(3200.0, 0.004, 1.00),
            Mode(5100.0, 0.003, 0.70),
            Mode(7600.0, 0.002, 0.42),
        ),
        seconds=0.24,
        noise_amount=0.85,
        noise_tau=0.014,
        band=(1900.0, 12000.0),
        tilt_hz=4300.0,
        tilt_q=1.8,
        tilt_gain=0.20,
        grains=17,
        grain_band=(2500.0, 10500.0),
        grain_spread=0.070,
        grain_thud=0.045,
        sub_scale=0.07,
        pitch_follow=0.30,
        level_db=-2.0,
    ),
)

BY_KEY: Dict[str, Surface] = {s.key: s for s in SURFACES}


# ── Actors ──────────────────────────────────────────────────────────────────


@dataclass(frozen=True)
class Actor:
    """One way of putting a foot down."""

    key: str
    pitch: float
    """Frequency scale on both the modes and the band. Scaling the band with
    the modes is what keeps the separation matrix intact when the monster drops
    everything by a sixth — the five surfaces move together, not apart."""

    tau: float
    dur: float
    noise: float
    level_db: float

    heel_toe: Optional[Tuple[float, float]] = None
    """(delay, gain) of a second contact. A walk rolls heel→toe; a run slaps."""

    sub_amp: float = 0.0
    sub_hz: float = 52.0
    sub_tau: float = 0.09
    """Body weight arriving through the floor, not the shoe."""

    drag: float = 0.0
    drag_seconds: float = 0.0
    """Monster only. Band-limited to the surface so it reads as the same floor
    being scraped, not as a separate noise event layered on top."""

    tick: Optional[Tuple[float, float]] = None
    """Monster only. An off-beat second contact. Humans do not do this, and
    that is the entire point — §06 makes footsteps the monster's tell, so one
    step has to be enough to know it is not a teammate."""


ACTORS: Tuple[Actor, ...] = (
    Actor(
        key="player_walk",
        pitch=1.00,
        tau=1.00,
        dur=1.00,
        noise=1.00,
        level_db=-15.0,
        heel_toe=(0.044, 0.40),
    ),
    Actor(
        key="player_run",
        pitch=1.04,
        tau=0.88,
        dur=0.94,
        noise=1.70,
        level_db=-9.0,
        heel_toe=(0.026, 0.20),
        sub_amp=0.12,
        sub_hz=64.0,
        sub_tau=0.045,
    ),
    Actor(
        key="monster_step",
        pitch=0.685,  # ≈ -6.5 semitones
        tau=2.05,
        dur=1.85,
        noise=1.25,
        level_db=-6.0,
        heel_toe=None,
        sub_amp=0.34,
        sub_hz=47.0,
        sub_tau=0.115,
        drag=0.50,
        drag_seconds=0.20,
        tick=(0.085, 0.28),
    ),
)


# ── Component builders ──────────────────────────────────────────────────────


def scaled_modes(s: Surface, a: Actor, seed: int, tau_mul: float = 1.0) -> Tuple[Mode, ...]:
    """The surface's modes moved into this actor's register, varied per variant.

    Three independent draws, each standing for something that genuinely differs
    between two real footsteps:

    * `strike` — *where* on the floor the foot landed. A slab, a board or a stair
      tread is not homogeneous; a different spot means slightly different modal
      frequencies.
    * `tilt` — *how hard*. A firmer contact puts proportionally more energy into
      the high modes, so this rotates the mode balance rather than just changing
      level. It is the strongest of the three perceptually, because it changes
      the ratio of thud to click.
    * `detune` — per-mode wobble, so the set never sounds like one transposed
      copy of itself.

    `strike` and `tilt` matter most on concrete, which has only four modes and a
    short decay and therefore the fewest degrees of freedom in the set — its four
    variants were the most alike of any group before these were added.
    """
    g = synth.rng(seed)
    strike = 1.0 + float(g.normal(0.0, s.strike_spread))
    tilt = float(g.uniform(-0.28, 0.28))
    ref = s.modes[0].freq if s.modes else 1.0

    out: List[Mode] = []
    for m in s.modes:
        detune = 1.0 + float(g.normal(0.0, 0.013))
        out.append(Mode(
            freq=m.freq * s.pitch(a) * strike * detune,
            tau=m.tau * a.tau * tau_mul,
            amp=m.amp * (m.freq / ref) ** tilt,
        ))
    return tuple(out)


def build_creak(s: Surface, a: Actor, seed: int, seconds: float) -> np.ndarray:
    """§12's 삐걱 — stick-slip, the sound of a board releasing under load.

    Not a resonance: a slow pitch glide with hard amplitude modulation, which is
    what stick-slip actually is. A resonant peak alone reads as "hollow", and
    hollow is wood's *body*, already covered by the modes.
    """
    g = synth.rng(seed)
    dur = min(seconds * 0.62, 0.17 * a.dur)
    f0 = float(g.uniform(560.0, 700.0)) * s.pitch(a)
    f1 = f0 * float(g.uniform(1.20, 1.55))
    body = synth.sweep(f0, f1, dur, log=True)
    body = synth.tremolo(body, rate=float(g.uniform(30.0, 48.0)) / max(a.dur, 0.2), depth=0.9)
    body = body * synth.exp_decay(dur, dur * 0.42)
    body = synth.resonator(body, f1 * 1.25, q=6.0)
    body = synth.bandpass(body, f0 * 0.7, min(f1 * 4.0, 9000.0))
    canvas = synth.silence(seconds)
    return synth.place(canvas, synth.normalize(body, 0.0), float(g.uniform(0.010, 0.030)))


def build_grains(
    s: Surface,
    a: Actor,
    seed: int,
    seconds: float,
    count: int,
    spread: float,
    at: float,
    front_load: float = 1.7,
) -> np.ndarray:
    """§12's 부스럭 — a scatter of tiny stone impacts, not one struck object.

    Each grain is its own `modal_impact` with a handful of inharmonic high
    modes and a ~2 ms decay, so gravel has no pitch to speak of. Grain times
    are front-loaded: the foot lands, then the bed settles.
    """
    g = synth.rng(seed)
    canvas = synth.silence(seconds)
    grain_len = max(0.020, 0.030 * a.dur)
    lo, hi = s.grain_band[0] * s.pitch(a), s.grain_band[1] * s.pitch(a)
    for k in range(count):
        f = float(np.exp(g.uniform(np.log(lo), np.log(hi))))
        tau = float(g.uniform(0.0012, 0.0048)) * a.tau
        modes = (
            Mode(f, tau, 1.00),
            Mode(f * 1.71, tau * 0.68, 0.55),
            Mode(f * 2.43, tau * 0.48, 0.30),
        )
        grain = synth.modal_impact(
            modes, grain_len, seed=seed + k * 104729, noise_amount=0.9, noise_tau=0.0018
        )
        when = at + spread * float(g.uniform(0.0, 1.0)) ** front_load
        gain = float(g.uniform(0.30, 1.0)) * (1.0 - 0.45 * (when - at) / max(spread, 1e-4))
        synth.place(canvas, grain, when, gain=gain)
    return canvas


def build_drag(s: Surface, a: Actor, seed: int, seconds: float, at: float) -> np.ndarray:
    """The monster's foot not lifting cleanly.

    Band-limited to the surface's own register: a scrape that sits outside the
    material's band stops reading as *this floor* and starts reading as a
    separate hiss, which would also shift the surface's centroid and eat into
    the separation margin the Listener depends on.
    """
    g = synth.rng(seed)
    dur = min(a.drag_seconds * float(g.uniform(0.82, 1.18)), max(seconds - at, 0.03))
    if dur <= 0.02:
        return synth.silence(seconds)

    if s.grains:
        # Gravel does not scrape, it rolls. Sparse, quiet, long-tailed grains.
        body = build_grains(
            s, a, seed + 31337, dur, count=max(6, s.grains // 2), spread=dur * 0.92,
            at=0.0, front_load=1.0,
        )
    else:
        body = synth.white(dur, seed + 4523)
        lo = max(s.band[0] * s.pitch(a), 55.0)
        top = max(s.band[1] * s.pitch(a), lo * 2.5)
        hi = min(lo * (top / lo) ** s.drag_reach, 9000.0)
        body = band_shape(body, lo, max(hi, lo * 2.2))
        body = synth.tremolo(body, rate=float(g.uniform(17.0, 27.0)), depth=0.55)

    t = synth.t_axis(len(body) / float(synth.SAMPLE_RATE))
    env = (1.0 - np.exp(-t / 0.030)) * np.exp(-t / (dur * 0.45))
    body = (body * env.astype(np.float32)).astype(np.float32)

    canvas = synth.silence(seconds)
    return synth.place(canvas, synth.normalize(body, 0.0), at)


def build_sub(a: Actor, s: Surface, seed: int, seconds: float) -> np.ndarray:
    """Weight through the slab.

    Kept short and modest, and trimmed per surface (`Surface.sub_scale`). Enough
    to feel a mass land; not enough to make all five materials measure alike.
    """
    g = synth.rng(seed)
    f = a.sub_hz * float(g.uniform(0.93, 1.08))
    dur = min(seconds, a.sub_tau * 5.0)
    body = synth.sine(f, dur) * synth.exp_decay(dur, a.sub_tau)
    body = synth.saturate(body, 1.4)
    canvas = synth.silence(seconds)
    return synth.place(canvas, synth.normalize(body, 0.0), 0.0)


# ── The step ────────────────────────────────────────────────────────────────


def build_step(s: Surface, a: Actor, variant: int) -> Tuple[np.ndarray, float]:
    """One footstep. Returns the buffer and the peak target in dBFS."""
    tag = (s.key, a.key, variant)
    g = synth.rng(seed_for("shape", *tag))

    seconds = s.seconds * a.dur * float(g.uniform(0.94, 1.08))

    # ── contact body ────────────────────────────────────────────────────────
    if s.grains:
        acc = build_grains(
            s, a, seed_for("grains", *tag), seconds,
            count=s.grains + int(g.integers(-2, 3)) + (5 if a.drag > 0 else 0),
            spread=s.grain_spread * a.dur, at=0.0,
        )
        crush = synth.white(min(seconds, 0.09 * a.dur), seed_for("crush", *tag))
        crush = band_shape(crush, s.band[0] * s.pitch(a), s.band[1] * s.pitch(a))
        crush = crush * synth.exp_decay(len(crush) / float(synth.SAMPLE_RATE), 0.018 * a.tau)
        acc = blend(acc, np.pad(crush, (0, max(0, len(acc) - len(crush)))), 0.55 * a.noise)
        if s.grain_thud > 0.0:
            thud = synth.modal_impact(
                (Mode(128.0 * s.pitch(a), 0.030 * a.tau, 1.0), Mode(196.0 * s.pitch(a), 0.020 * a.tau, 0.6)),
                seconds, seed=seed_for("thud", *tag), noise_amount=0.2, noise_tau=0.006,
            )
            acc = blend(acc, thud, s.grain_thud)
    else:
        acc = synth.modal_impact(
            scaled_modes(s, a, seed_for("modes", *tag)),
            seconds,
            seed=seed_for("impact", *tag),
            noise_amount=s.noise_amount * a.noise * float(g.uniform(0.78, 1.28)),
            noise_tau=s.noise_tau * float(g.uniform(0.85, 1.20)),
        )

    # ── second contact: the heel-toe roll of a walk, or the monster's tick ───
    if a.heel_toe is not None:
        delay, gain = a.heel_toe
        delay *= float(g.uniform(0.72, 1.32))
        if s.grains:
            toe = build_grains(
                s, a, seed_for("toe", *tag), seconds, count=max(5, s.grains // 3),
                spread=s.grain_spread * 0.7 * a.dur, at=delay,
            )
            acc = blend(acc, toe, gain)
        else:
            toe = synth.modal_impact(
                scaled_modes(s, a, seed_for("toe-modes", *tag), tau_mul=0.62),
                seconds,
                seed=seed_for("toe", *tag),
                noise_amount=s.noise_amount * a.noise * 1.15,
                noise_tau=s.noise_tau * 0.8,
            )
            shifted = synth.place(synth.silence(seconds), toe, delay)
            acc = blend(acc, shifted, gain)

    if a.tick is not None:
        delay, gain = a.tick
        delay *= float(g.uniform(0.70, 1.35))  # irregular on purpose — not a gait
        # Hard and short — a claw or a hoof, not a shoe. Kept only modestly above
        # the surface's own register (x1.75): pushed higher it started to dominate
        # the measured centroid of the brighter surfaces and closed the gap
        # between tile and gravel that the Listener needs.
        tick_modes = tuple(
            Mode(m.freq * 1.75, m.tau * 0.28, m.amp) for m in scaled_modes(s, a, seed_for("tick-m", *tag))
        )
        tick = synth.modal_impact(
            tick_modes, seconds, seed=seed_for("tick", *tag),
            noise_amount=0.55, noise_tau=0.0025,
        )
        acc = blend(acc, synth.place(synth.silence(seconds), tick, delay), gain)

    # ── weight, drag, creak ─────────────────────────────────────────────────
    if a.sub_amp > 0.0 and s.sub_scale > 0.0:
        acc = blend(acc, build_sub(a, s, seed_for("sub", *tag), seconds), a.sub_amp * s.sub_scale)

    if a.drag > 0.0 and s.drag_scale > 0.0:
        at = float(g.uniform(0.030, 0.065)) * a.dur
        acc = blend(acc, build_drag(s, a, seed_for("drag", *tag), seconds, at), a.drag * s.drag_scale)

    if s.creak > 0.0:
        amount = s.creak * float(g.uniform(0.55, 1.35)) * (1.25 if a.drag > 0 else 1.0)
        acc = blend(acc, build_creak(s, a, seed_for("creak", *tag), seconds), amount)

    # ── material shaping ────────────────────────────────────────────────────
    if s.comb_hz > 0.0:
        rung = synth.comb(acc, 1.0 / (s.comb_hz * s.pitch(a)), feedback=s.comb_fb)
        acc = blend(acc, rung, 0.85)

    if s.early:
        # Reflect the dry step, not the accumulating result — otherwise each tap
        # echoes the previous ones and the "room" grows with every entry in the
        # list. Reflection times are absolute: they describe the room's geometry,
        # which does not change according to who is walking across it, so unlike
        # almost everything else here they are *not* scaled by the actor.
        dry = acc.copy()
        for when, gain in s.early:
            synth.place(acc, dry[: max(1, len(dry) - synth.n_samples(when))], when, gain=gain)
    if s.reverb_mix > 0.0:
        acc = synth.reverb(
            acc, seconds=s.reverb_seconds, mix=s.reverb_mix,
            seed=seed_for("verb", *tag), damping=s.reverb_damping,
        )

    acc = band_shape(acc, s.band[0] * s.pitch(a), s.band[1] * s.pitch(a))
    if s.tilt_gain > 0.0:
        acc = blend(acc, synth.resonator(acc, s.tilt_hz * s.pitch(a), q=s.tilt_q), s.tilt_gain)

    # Final DC guard. Sub-thumps and summed noise both bias the waveform, and
    # assert_usable is strict about it for good reason: inaudible, and it eats
    # the headroom a dark mix needs.
    acc = synth.highpass(acc, 30.0, order=2)
    acc = synth.fade(acc, 0.0008, min(0.02, seconds * 0.2))
    acc = synth.concat(synth.silence(LEAD_IN), acc)  # see LEAD_IN

    level = a.level_db + s.level_db + float(g.uniform(-0.9, 0.9))
    return acc, level


# ── Verification ────────────────────────────────────────────────────────────


def geo_mean(values: Sequence[float]) -> float:
    """Geometric mean — the right average when the thing being asserted is a ratio."""
    return float(np.exp(np.mean(np.log(np.asarray(values, dtype=np.float64)))))


def envelope_db(path: str, block: float = 0.004) -> Tuple[np.ndarray, int]:
    """Block-RMS envelope of a written clip, in dB relative to its own peak block."""
    data, sr = synth.read_wav(path)
    n = max(1, int(round(block * sr)))
    trimmed = data[: (len(data) // n) * n].reshape(-1, n).astype(np.float64)
    rms = np.sqrt(np.mean(np.square(trimmed), axis=1))
    ref = float(np.max(rms)) if len(rms) else 0.0
    if ref <= 0.0:
        return np.full(len(rms), -120.0), n
    return 20.0 * np.log10(np.maximum(rms / ref, 1e-9)), n


def ring_ms(path: str, floor_db: float = -40.0) -> float:
    """How long the clip stays audible after its peak, in ms.

    The second axis the ear separates these on, and the one §12 names directly:
    금속 울림 rings, 콘크리트 둔탁 does not. Two materials could in principle share
    a centroid and still be trivially distinguishable by this, so it is measured
    and reported rather than assumed.
    """
    env, n = envelope_db(path)
    if not len(env):
        return 0.0
    top = int(np.argmax(env))
    above = np.nonzero(env[top:] > floor_db)[0]
    last = top + int(above[-1]) if len(above) else top
    return (last - top) * n / synth.SAMPLE_RATE * 1000.0


def tail_db(path: str) -> float:
    """Level of the final block relative to the peak.

    Guards the truncation bug that shipping a long decay in a short clip
    produces: the ring is still loud when the buffer runs out, so the sound is
    gated rather than decayed. See TAIL_HEADROOM_DB.
    """
    env, _ = envelope_db(path)
    return float(env[-1]) if len(env) else -120.0


def inband_flatness(path: str, low: float, high: float) -> float:
    """Spectral flatness *inside the surface's own band*: 1.0 = noise, 0 = a tone.

    The third axis, and the one that separates §12's two extremes by their own
    definitions: 자갈 부스럭 is broadband with almost no pitch, 금속 울림 is almost
    nothing but pitch.

    Measured in-band on purpose. Flatness over the full 0-24 kHz spectrum mostly
    reports how *wide* a surface's band is — every bin outside it sits at the
    noise floor and drags the geometric mean down — so concrete came out looking
    as tonal as metal simply because it is narrow. Restricting the window to the
    band each material actually occupies makes the number mean tonality alone,
    which is the thing being claimed.
    """
    data, sr = synth.read_wav(path)
    mag = np.abs(np.fft.rfft(data.astype(np.float64) * np.hanning(len(data))))
    freqs = np.fft.rfftfreq(len(data), 1.0 / sr)
    band = np.maximum(mag[(freqs >= low) & (freqs <= high)], 1e-12)
    if not len(band):
        return 0.0
    return float(np.exp(np.mean(np.log(band))) / np.mean(band))


def print_matrix(title: str, keys: Sequence[str], centroids: Dict[str, float], note: str = "") -> float:
    """Prints the centroid table and the full ratio matrix. Returns the worst ratio."""
    order = sorted(keys, key=lambda k: centroids[k])
    print()
    print(title)
    if note:
        print(f"  {note}")
    print("  centroid Hz:  " + "   ".join(f"{k}={centroids[k]:.0f}" for k in order))
    print()
    head = "  " + f"{'ratio':<10}" + "".join(f"{k:>10}" for k in order)
    print(head)
    print("  " + "-" * (len(head) - 2))
    worst = float("inf")
    worst_pair = ("", "")
    for a in order:
        row = f"  {a:<10}"
        for b in order:
            if a == b:
                row += f"{'—':>10}"
                continue
            hi, lo = max(centroids[a], centroids[b]), min(centroids[a], centroids[b])
            r = hi / lo
            row += f"{r:>10.2f}"
            if r < worst:
                worst, worst_pair = r, (a, b)
        print(row)
    print(f"  worst pair: {worst_pair[0]} vs {worst_pair[1]} = {worst:.2f}x "
          f"(required >= {SEPARATION_MIN:.2f}x)")
    return worst


def main() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)

    reports: List[synth.ClipReport] = []
    by_surface: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    by_pair: Dict[Tuple[str, str], List[float]] = {}
    rings: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    flats: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    tails: List[Tuple[str, float]] = []

    for s in SURFACES:
        for a in ACTORS:
            for v in range(1, VARIANTS + 1):
                name = f"step_{s.key}_{a.key}_{v:02d}.wav"
                path = os.path.join(OUT_DIR, name)
                buf, level = build_step(s, a, v)
                synth.write_wav(path, buf, headroom_db=level, stereo=False)
                r = synth.assert_usable(path, min_seconds=0.05, max_seconds=2.0)
                if r.channels != 1:
                    raise AssertionError(
                        f"{name}: {r.channels} channels. §05 needs these spatialised, "
                        f"and Unity will not spatialise a stereo clip."
                    )
                reports.append(r)
                by_surface[s.key].append(r.spectral_centroid)
                by_pair.setdefault((s.key, a.key), []).append(r.spectral_centroid)
                rings[s.key].append(ring_ms(path))
                flats[s.key].append(
                    inband_flatness(path, s.band[0] * s.pitch(a), s.band[1] * s.pitch(a))
                )
                tails.append((name, tail_db(path)))

    print(f"{len(reports)} clips written to {OUT_DIR}")
    print(f"all mono, {synth.SAMPLE_RATE} Hz, 16-bit PCM; every clip passed synth.assert_usable()")
    print()
    print(synth.report_table(reports))

    print()
    print("=" * 82)
    print("§12 SEPARATION — can the Listener tell the five floors apart?")
    print("=" * 82)

    overall = {k: geo_mean(v) for k, v in by_surface.items()}
    worst_overall = print_matrix(
        "[1] All 60 clips, per surface (the required five-centroid matrix)",
        list(overall), overall,
        note="geometric mean of the 12 clips per surface",
    )

    per_actor_worst = {}
    for a in ACTORS:
        cents = {s.key: geo_mean(by_pair[(s.key, a.key)]) for s in SURFACES}
        per_actor_worst[a.key] = print_matrix(
            f"[2] Within one actor: {a.key}", list(cents), cents,
            note="the Listener compares one *kind* of step across zones, so this "
                 "matters more than [1]",
        )

    # Clip-level worst case: the brightest-measuring clip of the darker surface
    # against the darkest-measuring clip of the brighter one, inside one actor.
    # This is what actually reaches a player's ear — a single step, not an average.
    print()
    print("[3] Clip-level worst case (darkest clip of the brighter surface vs")
    print("    brightest clip of the darker surface, within one actor)")
    print()
    print(f"  {'actor':<14}{'pair':<22}{'ratio':>8}")
    print("  " + "-" * 42)
    clip_worst = float("inf")
    for a in ACTORS:
        cents = {s.key: geo_mean(by_pair[(s.key, a.key)]) for s in SURFACES}
        order = sorted(cents, key=lambda k: cents[k])
        for i in range(len(order) - 1):
            dark, bright = order[i], order[i + 1]
            r = min(by_pair[(bright, a.key)]) / max(by_pair[(dark, a.key)])
            clip_worst = min(clip_worst, r)
            print(f"  {a.key:<14}{dark + ' / ' + bright:<22}{r:>8.2f}")
    print(f"  worst adjacent clip-level ratio: {clip_worst:.2f}x")

    # Axes beyond pitch, because §12 does not only say "different frequency". It
    # says 울림 for metal and 둔탁 for concrete, which is decay length, and 부스럭
    # for gravel, which is noise-versus-pitch. Surfaces separated on centroid
    # alone would be a far weaker cue in a real room than these three together.
    print()
    print("[4] Second and third axes: ring length, and pitch vs noise")
    print()
    print(f"  {'surface':<10}{'centroid Hz':>13}{'ring ms':>10}{'flatness':>10}   §12")
    print("  " + "-" * 74)
    for key in sorted(overall, key=lambda k: overall[k]):
        s = next(x for x in SURFACES if x.key == key)
        print(f"  {key:<10}{overall[key]:>13.0f}{geo_mean(rings[key]):>10.0f}"
              f"{geo_mean(flats[key]):>10.3f}   {s.zone}")
    print("  flatness: 1.0 = pure noise, 0 = pure tone, measured inside each")
    print("  surface's own band so it reports tonality and not bandwidth")

    worst_tail = max(tails, key=lambda t: t[1])
    print()
    print(f"[5] Worst truncation: {worst_tail[0]} ends at {worst_tail[1]:.1f} dB below its "
          f"own peak (limit {TAIL_HEADROOM_DB:.0f} dB)")

    print()
    print("=" * 82)

    assert worst_overall >= SEPARATION_MIN, (
        f"§12 FAILED: two surfaces sit within {worst_overall:.2f}x of each other "
        f"(need >= {SEPARATION_MIN}x). The Listener role does not function; retune "
        f"the Mode sets and Surface.band."
    )
    for k, w in per_actor_worst.items():
        assert w >= SEPARATION_MIN, (
            f"§12 FAILED for actor {k}: two surfaces within {w:.2f}x. The Listener "
            f"cannot separate zones from this kind of step."
        )
    assert clip_worst >= 1.0, (
        f"§12 FAILED: surface centroid ranges overlap at clip level ({clip_worst:.2f}x); "
        f"a single step could be misread even though the averages separate."
    )

    ring = {k: geo_mean(v) for k, v in rings.items()}
    assert ring["metal"] == max(ring.values()), (
        f"§12 says 계단 금속 = 울림; metal must ring longest. Got {ring}."
    )
    assert ring["concrete"] < min(ring["metal"], ring["tile"]), (
        f"§12 says 콘크리트 = 둔탁; concrete must die faster than the two ringing "
        f"surfaces. Got {ring}."
    )
    flat = {k: geo_mean(v) for k, v in flats.items()}
    others = sorted((v for k, v in flat.items() if k != "gravel"), reverse=True)
    assert flat["gravel"] >= others[0] * SEPARATION_MIN, (
        f"§12 says 자갈 = 부스럭 — broadband, almost no pitch — so gravel must be the "
        f"most noise-like surface by a clear margin, not merely the highest. Got "
        f"{flat['gravel']:.3f} against {others[0]:.3f}. Check that the grain scatter "
        f"has not collapsed into one pitched burst."
    )
    tonal = sorted((v for k, v in flat.items() if k != "metal"))
    assert flat["metal"] * SEPARATION_MIN <= tonal[0], (
        f"§12 says 계단 금속 = 울림, so metal must be the most *tonal* surface by a "
        f"clear margin. Got {flat['metal']:.3f} against {tonal[0]:.3f}."
    )
    assert worst_tail[1] <= TAIL_HEADROOM_DB, (
        f"{worst_tail[0]} still at {worst_tail[1]:.1f} dB when the clip ends — the "
        f"decay is being gated off, not decaying. Lengthen Surface.seconds or "
        f"shorten the Mode taus."
    )

    print(f"PASS  five surfaces separable: worst overall pair {worst_overall:.2f}x, "
          f"worst within-actor pair {min(per_actor_worst.values()):.2f}x, "
          f"worst clip-level {clip_worst:.2f}x, floor {SEPARATION_MIN:.2f}x")
    print(f"PASS  metal rings longest ({ring['metal']:.0f} ms) and is most tonal "
          f"({flat['metal']:.3f}); gravel is most noise-like ({flat['gravel']:.3f}); "
          f"concrete dies fastest of the hard floors ({ring['concrete']:.0f} ms)")
    print(f"PASS  no clip truncated above {TAIL_HEADROOM_DB:.0f} dB "
          f"(worst {worst_tail[1]:.1f} dB)")
    print("=" * 82)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
