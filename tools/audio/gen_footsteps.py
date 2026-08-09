"""Footsteps — the Listener's map.

Run:
    /Users/doogi/horror-game/tools/audio/.venv/bin/python \
        /Users/doogi/horror-game/tools/audio/gen_footsteps.py

    # one material only; the other seven are read from disk and still measured,
    # because the separation matrix is meaningless without all eight in it
    ... gen_footsteps.py --only carpet

WHAT THIS IS FOR IN GAMEPLAY TERMS
----------------------------------
§04 gives the 청음사 (Listener) exactly one ability: read the monster's
"위치 · 거리 · 이동 방향" from sound alone. §12 states how that is supposed to
work — "바닥 재질이 지도다" — five zones, five floor materials, and the
Listener knows *which zone* the monster is in from which surface its feet land
on:

    A 나무 삐걱   B 타일 딱딱+반향   C 자갈 부스럭   D 콘크리트 둔탁   계단 금속 울림

§04 then adds three more floors that §12's original table does not name, and adds
them on a *different* axis. 침수, 흙 and 카펫 are a loudness ladder rather than
three more timbres: water is the one surface that cannot be crossed unheard,
carpet is where the Listener's channel runs out entirely, and earth sits between
concrete and gravel. Their clarity numbers live in
`GameConstants.ListenerClarity*` — 1.00, 0.40 and 0.22 — and section [6] of this
file's output is where the shipped clips are checked against them.

    침수 첨벙(가장 큼)   흙 퍽(둔함)   카펫 거의 무음(청음사의 사각)

That makes these clips a **gameplay channel, not decoration**. §12 says it in as
many words: "아트 결정이 아니라 시스템 결정이다." A player who cannot tell tile
from concrete has no Listener role, so the deliverable here is not "footstep
sounds" but *eight mutually separable identities* — separable by spectrum for the
five §12 floors, and by spectrum *and level* for §04's three — verified by
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

Four variants of each, per surface (8 x 3 x 4 = 96 clips). A single repeated footstep is
what makes a horror game feel cheap, and worse here: a machine-gun-identical
step reads as a looping sound cue rather than as a creature walking, which is
exactly the information §04 asks the Listener to extract.

Where the sound starts, since 2026-08-09
---------------------------------------
The contact itself is now a **real recording** where one is vendored: a CC0
field recording of a foot on that material, sliced to a single contact,
denoised, and put into the actor's register by `source_bank`. Everything after
the contact is unchanged and still synthesised — the heel-toe roll, the
monster's tick and drag and sub, wood's creak, metal's comb, tile's early
reflections, the per-surface band and tilt, the per-variant jitter, the loudness
landing. See `Surface.real_amount` for how much of each surface is which, and
`fetch_sources.py` for where the recordings come from and why that source.

The reason is narrow and worth stating: `synth.modal_impact` is an excellent
model of one resonant body being struck and a poor model of a *foot*, which is
two contacts of different hardness arriving a few milliseconds apart onto a
floor that is itself sitting on something else. That last part is the one that
mattered — see `F-002` in `docs/BALANCE-FINDINGS.md`, where gravel measured 32 dB
quieter than concrete through a wall because a synthesised scatter has no
substrate under it to transmit anything below 1900 Hz. A recording of a boot on
gravel does, because the gravel was on ground.

If `tools/audio/source/` is absent this file still builds all 96 clips, fully
procedurally, and prints one line per missing surface saying so. The generator
is the source of truth; the recordings are a layer inside it.

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

import source_bank  # noqa: E402
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


CLARITY: Dict[str, float] = {
    "metal": 1.00, "water": 1.00, "tile": 0.85, "wood": 0.80,
    "gravel": 0.70, "concrete": 0.50, "earth": 0.40, "carpet": 0.22,
}
"""§04's per-surface clarity, mirroring GameConstants.ListenerClarity*.

Printed beside the measured levels so the two can be compared at a glance. It is
deliberately *not* a formula the levels are derived from: clarity drives how
accurate the Listener's position fix is, which is a Core rule, while the level
below drives what a player's ears get, and the five original floors already show
the two are only loosely coupled (금속 is clarity 1.00 at -0.5 dB, 타일 is 0.85 at
0.0 dB). What the three new surfaces must honour is the *ordering* at the ends —
침수 loudest, 카펫 quietest — because those two are the ones §04 makes promises
about."""


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
    """Gravel and water. Many tiny impacts, so there is no single struck object
    to model and `grains` replaces the modal body — §12's 자갈 부스럭 is a bed of
    stones shifting, and §04's 침수 is a sheet of water coming apart into spray.
    Both are scatters; what separates them is `grain_band`, how far the scatter
    is allowed to fly (`grain_spread`), and the slap underneath it."""

    grit: float = 0.0
    grit_count: int = 0
    grit_band: Tuple[float, float] = (900.0, 4200.0)
    grit_spread: float = 0.055
    """Earth. A scatter laid *over* a modal body rather than replacing it.

    Dug soil is not gravel: the foot compresses a mass that answers as one dull
    body, and only the loose particles on top rattle. Modelling it as pure
    grains loses the compression, and as a pure impact loses the 흙 entirely, so
    this is the one surface that needs both. It is also the whole reason earth
    measures clear of concrete — the grit is what lifts the centroid the ~1.6x
    that keeps zone D and a dug floor from being the same symbol."""

    sheet: float = 0.0
    sheet_band: Tuple[float, float] = (600.0, 5200.0)
    sheet_tau: float = 0.11
    """Water. The mass of water itself moving, under the spray.

    Every other surface in the set is an impact: energy arrives, the material
    rings, it stops. Water is the one floor that keeps *making* sound after the
    foot has landed, because what was displaced has to come back. Without this
    the clip is a burst and a gap, which measures loud on peak and reads quiet —
    and 침수 is the surface whose entire job is being impossible to miss."""

    glug: float = 0.0
    glug_count: int = 0
    glug_band: Tuple[float, float] = (240.0, 950.0)
    glug_at: Tuple[float, float] = (0.30, 0.55)
    """Water. The suck as the foot comes back out.

    A bubble resonates by Minnaert's relation and its pitch *rises* as it
    collapses, which is why `build_glug` sweeps upward — a falling sweep reads
    as a drain, not as a boot leaving mud. This is the half of the splash that
    makes 침수 unmistakable: a slap and a spray alone could be a bucket of
    gravel thrown at a wall, and only the release says the water closed again
    behind the foot."""

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

    real_amount: float = 0.0
    """How much of the vendored recording of this material sits under the
    synthesised contact, as a fraction of the synthesised body's peak.

    Not one global number, because the two halves are good at different things
    per material and the separation matrix is what pays for getting it wrong:

    * **High (gravel, earth, carpet, water).** These are the four surfaces whose
      body is an aggregate rather than a resonator. `modal_impact` has nothing
      to model — hence `grains`, `grit` and `sheet`, which are scatters of
      synthetic clicks. A scatter is a decent imitation of the *texture* and a
      poor one of the *mass*: it has no substrate, no compression, and its
      envelope is statistically flat where a real one is not.
    * **Low (tile, metal).** These two are exactly what modal synthesis is for,
      and both carry a designed acoustic on top — tile's early reflections and
      metal's comb — that a recording of *someone else's* stairwell fights
      rather than supports. The recording is here for the contact transient
      only, which is why it sits well under the modal body.

    Zero disables the base layer for that surface without touching the bank."""

    real_hp: float = 0.0
    """A high-pass on the recording before it is blended in, in Hz.

    Distinct from `band` on purpose. `band` is applied to the finished step and
    defines the material; this one only removes what the *recording* brought and
    the material should not have — chiefly the room the recordist was standing
    in. Left at 0 the surface's own band edge does the work."""

    substrate: float = 0.0
    """The recording's energy *below* this surface's band, added back after the
    band has been applied, as a fraction of the finished clip's RMS.

    The floor is not the only thing a foot hits. It hits the floor, and the floor
    hits whatever it is resting on, and that second contact radiates far lower
    than the first — which is why `band` is a description of the *material* and
    not of the whole event. For seven surfaces this costs nothing, because their
    band already reaches low enough to keep it. Gravel's does not: `band` starts
    at 1900 Hz, so everything the ground under the stones transmitted is
    high-passed away, and F-002 in `docs/BALANCE-FINDINGS.md` is the bill for
    that — through a wall, which is exactly where §04 says the Listener works,
    gravel measured 32 dB quieter than concrete while `GameConstants` promises
    the player the opposite.

    Synthesis could not answer that honestly, because there was nothing to model:
    `build_grains` is a scatter of clicks with no mass underneath it. A recording
    of a boot on gravel has one — 20% of its energy sits below 120 Hz, because
    the gravel was on ground — and this field is what lets that through.

    Sized by measurement rather than taste. The §12 centroid margin between
    gravel and water is what pays for it, and section [2] of this file's output
    is where the bill arrives."""

    substrate_hz: float = 0.0
    """Upper corner for the substrate. Defaults to the surface's band floor."""

    substrate_lo: float = 0.0
    """Lower corner for the substrate, in Hz. Below 34 Hz is always removed.

    Worth a paragraph, because the naive setting of this field is 0 and the naive
    setting is wrong twice over.

    F-002 is measured as **A-weighted** energy through a low-pass, which is the
    right way to measure it — the question the finding asks is what a player can
    hear through a wall, not what a spectrum analyser can see. A-weighting is
    -19 dB at 100 Hz and -3 dB at 500 Hz, so a substrate placed at the bottom of
    the recording pays roughly 16 dB for nothing.

    It also costs the most where the game can least afford it. The §12 alphabet
    is re-measured through the same wall, and gravel's occluded centroid is what
    a substrate drags down: the lower the substrate sits, the further it drags.
    Filling 320-620 Hz instead of 34-1900 Hz buys the same audibility for a
    fraction of the centroid, because it is nearer the energy it is being
    averaged against.

    Both effects point the same way, which is the only reason this is a band and
    not a shelf."""

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
        real_amount=1.05,
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
        real_amount=1.1,
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
        real_amount=0.42,
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
        real_amount=0.4,
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
        real_amount=1.65,
        substrate=0.16,
        substrate_lo=320.0,
        substrate_hz=620.0,
    ),
    # 침수 — 첨벙. §04 clarity 1.00: the loudest floor in the building, and the
    # only one that cannot be crossed quietly at any speed. Three gestures in one
    # clip — slap, spray, suck — because a splash that is only a burst of noise
    # reads as a surface, and water has to read as a *depth*.
    Surface(
        key="water",
        zone="침수 · 첨벙 (§04 1.00)",
        # Used only by the monster's tick: the grains path supplies the body. Kept
        # bright and near-dead so a claw entering water still sounds like water.
        modes=(
            Mode(2400.0, 0.006, 1.00),
            Mode(4000.0, 0.004, 0.70),
            Mode(6200.0, 0.003, 0.42),
        ),
        # The longest player-walk clip in the set bar metal. Water is the only
        # surface still making sound once the foot has stopped moving, and the
        # length has to cover the sheet's decay — at 0.52 s the tail was still
        # -33.1 dB at the buffer's end against a -32 dB limit, i.e. one reseed away
        # from a splash that ends by being switched off.
        seconds=0.56,
        noise_amount=0.95,
        noise_tau=0.020,
        band=(520.0, 13500.0),
        tilt_hz=3600.0,
        tilt_q=1.5,
        tilt_gain=0.22,
        # A sheet of water coming apart. Far more grains than gravel and more than
        # twice the spread: stones stop where they land, spray keeps travelling.
        grains=26,
        grain_band=(2000.0, 10000.0),
        grain_spread=0.17,
        grain_thud=0.10,
        # Tuned against the matrix, not by ear: at the first setting the spray ran
        # to 13 kHz and 침수 measured 5130 Hz, which is 1.21x from 자갈 — two
        # broadband bright surfaces the Listener would have had to tell apart by
        # level alone. Bringing the spray and the sheet down puts water at 4064,
        # a 1.57x walk from tile and a 1.53x walk from gravel.
        sheet=1.3,
        sheet_band=(1100.0, 6800.0),
        # 0.17 rather than 0.20: at the longer decay the sheet was still audible
        # when the buffer ended (-33.3 dB against a -32 limit) and 침수 was the
        # tightest truncation in the set. Shortening it costs 0.4 dB of RMS and
        # buys 4 dB of margin, and water is still the loudest floor by 1.6 dB.
        sheet_tau=0.17,
        # A boot in 10-15 cm of water displaces a fair volume, and Minnaert puts a
        # big bubble low: dropping the band from 260-900 to 150-600 is both the
        # more physical figure and the one that moves 침수's *occluded* centroid
        # away from 타일's, which is the pair a wall brings closest together.
        glug=0.55,
        glug_count=5,
        glug_band=(150.0, 600.0),
        glug_at=(0.32, 0.60),
        sub_scale=0.30,
        # Water's register is droplet size, and a heavier foot does not make bigger
        # droplets — it makes more of them, thrown further. Following the monster's
        # -6.5 semitones would drop 침수 onto 타일 for exactly the actor the
        # Listener cares about most; the same trap gravel documents below.
        pitch_follow=0.25,
        drag_reach=0.86,
        drag_scale=1.25,
        strike_spread=0.030,
        level_db=2.0,
        real_amount=0.0,  # see the note on Surface.real_amount — water stays synthetic
    ),
    # 파헤쳐진 흙 — 퍽. §04 clarity 0.40, under concrete. The only surface in the
    # set built from a body *and* a scatter: see Surface.grit.
    Surface(
        key="earth",
        zone="파헤쳐진 흙 · 퍽 (§04 0.40)",
        modes=(
            Mode(68.0, 0.018, 1.00),
            Mode(107.0, 0.014, 0.70),
            Mode(172.0, 0.009, 0.40),
            Mode(263.0, 0.006, 0.20),
        ),
        # Shorter than concrete, and the shortest in the set. Dug soil has nowhere
        # to put the energy: no slab to ring, no cavity to resonate.
        seconds=0.17,
        noise_amount=0.55,
        noise_tau=0.009,
        band=(44.0, 1500.0),
        tilt_hz=240.0,
        tilt_q=2.2,
        tilt_gain=0.24,
        grit=0.30,
        grit_count=11,
        grit_band=(850.0, 4200.0),
        grit_spread=0.045,
        sub_scale=0.62,
        pitch_follow=0.85,
        drag_reach=0.72,
        drag_scale=0.85,
        strike_spread=0.070,
        level_db=-4.0,
        real_amount=1.6,
    ),
    # 카펫 — §04 clarity 0.22, below even an unassigned floor. The quietest and
    # darkest surface in the game, and deliberately so: §04's blind spot has to be
    # a real hole in the channel, not a slightly softer version of one.
    Surface(
        key="carpet",
        zone="카펫 · 무음에 가까움 (§04 0.22)",
        # The shortest decays in the set. Pile does not store energy — it turns the
        # impact into heat, which is exactly why the room goes quiet when you step
        # onto it. Anything longer here reads as a rug over a hollow floor.
        modes=(
            Mode(46.0, 0.015, 1.00),
            Mode(74.0, 0.011, 0.62),
            Mode(117.0, 0.007, 0.30),
            Mode(178.0, 0.004, 0.13),
        ),
        # Long enough to contain the monster's drag. At 0.11 s the scrape was still
        # running when the buffer ended and 카펫's own monster clip was the worst
        # truncation in the set at -23.9 dB — a surface defined by having no tail,
        # ending on a hard edge.
        seconds=0.17,
        # Almost no contact noise at all. Pile is the one floor covering that mutes
        # the scrape rather than colouring it, and that missing hiss is most of why
        # a carpeted corridor reads as empty.
        noise_amount=0.14,
        noise_tau=0.006,
        # The narrowest band in the set, and the point of the surface: 15 mm of
        # wool over the same slab zone D is made of, so what reaches the room is
        # concrete's thud with everything above it taken away.
        band=(30.0, 250.0),
        tilt_hz=85.0,
        tilt_q=2.0,
        tilt_gain=0.22,
        # The slab under the pile still takes the weight — carpet muffles a
        # footstep, it does not levitate one. Held to half of concrete's share so
        # the clip does not trail a long, near-silent sub tail; that tail is
        # signal at roughly one bit, which is where dither lives.
        sub_scale=0.50,
        pitch_follow=1.00,
        drag_reach=0.70,
        drag_scale=0.45,
        # Tightest in the set. Pile is uniform in a way a slab or a board is not —
        # there is no joist to step over — and the narrow spread is also what keeps
        # 카펫's brightest variant clear of 콘크리트's darkest at clip level.
        strike_spread=0.030,
        # 10 dB under the next quietest floor. This is the number §04's blind spot
        # is actually made of: 카펫 measures -36.0 dBFS RMS against 자갈's -33.0
        # and 콘크리트's -26.5, so the Listener's channel does not merely get worse
        # here, it runs out.
        level_db=-12.0,
        real_amount=1.7,
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
    band: Optional[Tuple[float, float]] = None,
) -> np.ndarray:
    """§12's 부스럭 — a scatter of tiny stone impacts, not one struck object.

    Each grain is its own `modal_impact` with a handful of inharmonic high
    modes and a ~2 ms decay, so gravel has no pitch to speak of. Grain times
    are front-loaded: the foot lands, then the bed settles.

    `band` overrides the surface's own `grain_band`, which is what lets earth
    borrow the scatter for its grit without dragging the whole clip up into
    gravel's register — the particles on a dug floor are a layer on top of a
    body, not the body itself.
    """
    g = synth.rng(seed)
    canvas = synth.silence(seconds)
    grain_len = max(0.020, 0.030 * a.dur)
    src = band if band is not None else s.grain_band
    lo, hi = src[0] * s.pitch(a), src[1] * s.pitch(a)
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


def build_sheet(s: Surface, a: Actor, seed: int, seconds: float) -> np.ndarray:
    """The body of water closing back over the foot.

    A slow-attack, slow-decay band of noise rather than another transient: the
    displaced mass takes time to return, and that sustain is what puts 침수's
    RMS above every hard floor even though its peak is only a couple of dB up.
    Loudness here is carried by duration, not by level, which is also the only
    way to be the loudest surface without eating the mix's headroom.
    """
    g = synth.rng(seed)
    body = synth.white(seconds, seed + 1301)
    lo = s.sheet_band[0] * s.pitch(a)
    hi = s.sheet_band[1] * s.pitch(a) * float(g.uniform(0.88, 1.15))
    body = band_shape(body, lo, hi)
    # Churn: the surface is not a steady hiss, it is water breaking up.
    body = synth.tremolo(body, rate=float(g.uniform(9.0, 16.0)), depth=0.42)
    t = synth.t_axis(len(body) / float(synth.SAMPLE_RATE))
    env = (1.0 - np.exp(-t / 0.012)) * np.exp(-t / (s.sheet_tau * a.dur))
    return synth.normalize((body * env.astype(np.float32)).astype(np.float32), 0.0)


def build_glug(s: Surface, a: Actor, seed: int, seconds: float) -> np.ndarray:
    """§04's 침수 — the suck as the foot leaves standing water.

    Each bubble is a short sine whose pitch *rises* into its own collapse. That
    is not a stylisation: a gas bubble's resonance goes as the inverse of its
    radius (Minnaert), so a shrinking bubble goes up, and a sweep the other way
    reads as a plughole instead of a boot. A handful of them, scattered late in
    the clip and low in the surface's band, is the difference between a splash
    and a bucket of stones.

    Placed in the back half of the step on purpose — the slap and the spray are
    the foot going *in*, and this is it coming out, so the ordering carries the
    gesture even though the whole thing lasts a third of a second.
    """
    g = synth.rng(seed)
    canvas = synth.silence(seconds)
    lo, hi = s.glug_band[0] * s.pitch(a), s.glug_band[1] * s.pitch(a)
    for k in range(max(1, s.glug_count)):
        dur = float(g.uniform(0.020, 0.055)) * a.dur
        f0 = float(np.exp(g.uniform(np.log(lo), np.log(hi))))
        # A bubble climbs roughly a fifth to an octave as it collapses.
        body = synth.sweep(f0, f0 * float(g.uniform(1.5, 2.1)), dur, log=True)
        body = body * synth.exp_decay(dur, dur * float(g.uniform(0.22, 0.38)))
        # A trace of the surrounding water tearing, not a clean tone.
        body = blend(body, synth.white(dur, seed + k * 7717) * synth.exp_decay(dur, 0.004), 0.22)
        when = float(g.uniform(*s.glug_at)) * seconds
        synth.place(canvas, synth.normalize(body, 0.0), when, gain=float(g.uniform(0.45, 1.0)))
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


mix_real = source_bank.mix_real
spectral_tilt = source_bank.spectral_tilt
band_centroid = source_bank.band_centroid
match_centroid = source_bank.match_centroid
"""Shared with `gen_ambience.py`, which needs the same guarantee for its drips.
See `source_bank.match_centroid` — the short version is that a recording is not
allowed to bring its own spectral balance, because §12's alphabet is written in
exactly that and eight microphone positions land far closer together than eight
designed materials do."""


def build_real(
    s: Surface, a: Actor, tag: Tuple[object, ...], seconds: float, body: np.ndarray
) -> Optional[np.ndarray]:
    """The vendored contact for this surface, in this actor's register.

    `body` is the synthesised contact it is about to join, and is used only as
    the spectral target — see `match_centroid`.

    Returns `None` when the surface has no bank or has `real_amount` at zero, in
    which case `build_step` proceeds exactly as it did before any of this
    existed.
    """
    if s.real_amount <= 0.0:
        return None
    real = source_bank.pick(
        "footsteps", s.key, seed_for("real", *tag), seconds, pitch=s.pitch(a)
    )
    if real is None:
        return None

    low = s.band[0] * s.pitch(a)
    high = min(s.band[1] * s.pitch(a), 0.94 * 0.5 * synth.SAMPLE_RATE)
    if s.real_hp > 0.0:
        real = synth.highpass(real, s.real_hp * s.pitch(a), order=2)
    real = band_shape(real, low, high)
    real = match_centroid(real, band_centroid(body, low, high), low, high)

    # A run lands harder and shorter than the walk these were recorded as, and
    # the monster harder still. Sharpening the recording's own envelope is what
    # makes one bank serve three actors without three recordings: it is the same
    # contact, hit with more force.
    if a.noise > 1.0:
        env = synth.exp_decay(seconds, max(0.02, 0.16 / a.noise))
        real = real * (0.55 + 0.45 * env[: len(real)])

    # The recording has to finish inside the clip length the *material* asks for,
    # and it does not know that length — `Surface.seconds` is a design decision
    # (concrete is over before tile has started to ring, and the Listener uses
    # that) while the recording just runs until the recordist's floor stopped.
    # Truncation is not an option: TAIL_HEADROOM_DB exists because a decay cut
    # off mid-level reads as the audio being switched off, and earth's recording
    # was ending the clip at -25 dB against a -32 dB requirement. A raised-cosine
    # over the last third lands it decayed instead of gated.
    return taper_tail(real)


taper_tail = source_bank.taper_tail
"""Raised-cosine over the tail, so a recording finishes inside the clip length
`Surface.seconds` asks for. TAIL_HEADROOM_DB is the assertion that made this
necessary: earth's recording was ending its clip at -25 dB against a -32 dB
requirement, because a recording does not know how long it is allowed to be."""


def build_substrate(
    s: Surface, a: Actor, tag: Tuple[object, ...], seconds: float
) -> Optional[np.ndarray]:
    """What the ground under this floor transmitted, taken from the recording.

    Deliberately the *same* slice `build_real` used — same seed, same contact —
    because this is not a second sound, it is the low half of the one sound the
    band split off. Reading it from a different contact would put a thump under a
    footstep that did not make it.
    """
    if s.substrate <= 0.0:
        return None
    raw = source_bank.pick(
        "footsteps", s.key, seed_for("real", *tag), seconds, pitch=s.pitch(a)
    )
    if raw is None:
        return None
    corner = (s.substrate_hz or s.band[0]) * s.pitch(a)
    low = synth.lowpass(raw, corner, order=4)
    low = synth.highpass(low, max(34.0, s.substrate_lo * s.pitch(a)), order=2)
    return taper_tail(low, 0.40)


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

        # Loose particles riding on a body that answers as one mass. Earth only:
        # see Surface.grit for why this is a layer and not the gravel path.
        if s.grit > 0.0 and s.grit_count > 0:
            grit = build_grains(
                s, a, seed_for("grit", *tag), seconds,
                count=s.grit_count + int(g.integers(-2, 3)),
                spread=s.grit_spread * a.dur, at=0.0, front_load=1.4,
                band=s.grit_band,
            )
            acc = blend(acc, grit, s.grit * float(g.uniform(0.82, 1.22)))

    # ── the recording ───────────────────────────────────────────────────────
    # Placed here, between the contact body and everything that decorates it, so
    # the whole rest of the file still applies on top: the heel-toe roll, the
    # monster's tick and drag, the creak, the comb, the reflections, the band,
    # the tilt and the loudness landing all treat the combination as the contact.
    #
    # It goes in at the surface's *own* pitch scale, not the actor's raw one:
    # `Surface.pitch` is what keeps gravel from following the monster down a
    # sixth, and a recording that ignored it would drag gravel's centroid into
    # tile's on monster steps — the exact failure `pitch_follow` exists to stop.
    real = build_real(s, a, tag, seconds, acc)
    if real is not None:
        acc = mix_real(acc, real, s.real_amount * float(g.uniform(0.88, 1.14)))

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

    if s.sheet > 0.0:
        acc = blend(acc, build_sheet(s, a, seed_for("sheet", *tag), seconds),
                    s.sheet * float(g.uniform(0.85, 1.18)))

    if s.glug > 0.0 and s.glug_count > 0:
        # Heavier feet pull more water back in behind them, so the release grows
        # with the actor rather than staying a fixed garnish on top of a splash.
        amount = s.glug * float(g.uniform(0.70, 1.30)) * (1.35 if a.drag > 0 else 1.0)
        acc = blend(acc, build_glug(s, a, seed_for("glug", *tag), seconds), amount)

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

    # After the band, on purpose — the point of the substrate is that it lives
    # below the material's own band and the band would otherwise delete it.
    # See Surface.substrate, and F-002.
    sub = build_substrate(s, a, tag, seconds)
    if sub is not None:
        acc = mix_real(acc, sub, s.substrate * float(g.uniform(0.90, 1.12)))

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


SIGNAL_FLOOR_DB = -60.0
"""Bins this far below the loudest bin are the file's noise floor, not its content.

Needed because `synth.analyse`'s centroid is magnitude-weighted across the whole
0-24 kHz spectrum, and 16-bit rounding noise is *white*: it puts a little energy
in every one of ~12000 bins, and multiplying each of those by a frequency up to
24 kHz is enough to dominate the sum for a clip whose real content is a 90 Hz
thud. The effect is inaudible — the floor sits near -100 dBFS — but it is not
small in the metric, and the metric is what §12's gate is built on.

This is not a new problem introduced by the quiet surfaces. Measured against the
same clips before quantisation, the *shipped* set was already reading high:

    concrete 154 Hz true -> 228 Hz raw (1.48x)    wood  541 -> 579 (1.07x)
    metal   1168        -> 1290       (1.10x)     tile 2652 -> 2708 (1.02x)
    gravel  6231        -> 6236       (1.00x)

so the darkest floor on the map was already being credited with 74 Hz of dither.
Bright clips are unaffected because their content swamps the floor. At -60 dB the
threshold sits far above the rounding noise and far below anything designed, and
it reproduces the pre-quantisation centroid of all eight surfaces to within 3%
— including carpet, which reads 357 Hz raw against 94 Hz of actual signal.
"""


def signal_centroid(path: str, floor_db: float = SIGNAL_FLOOR_DB) -> float:
    """Spectral centroid of the clip's *content*, ignoring its noise floor.

    The number §12's separation matrix is built from. See SIGNAL_FLOOR_DB for why
    the raw full-band centroid cannot be used for the quietest surfaces.
    """
    data, sr = synth.read_wav(path)
    if len(data) <= 8:
        return 0.0
    mag = np.abs(np.fft.rfft(data.astype(np.float64) * np.hanning(len(data))))
    freqs = np.fft.rfftfreq(len(data), 1.0 / sr)
    peak_bin = float(np.max(mag)) if len(mag) else 0.0
    if peak_bin <= 0.0:
        return 0.0
    keep = mag >= peak_bin * (10.0 ** (floor_db / 20.0))
    total = float(np.sum(mag[keep]))
    return float(np.sum(freqs[keep] * mag[keep]) / total) if total > 0.0 else 0.0


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


def main(only: Optional[Sequence[str]] = None) -> int:
    """Writes the clip set and verifies §12's separation across all eight floors.

    `only` restricts which surfaces are *written*; everything is still measured,
    by reading whatever is already on disk for the rest. Regenerating one
    material must not rewrite the other seven — the seeds make that a no-op in
    principle, but "in principle" is not what you want standing between a
    one-surface change and 84 files somebody else is working against.
    """
    os.makedirs(OUT_DIR, exist_ok=True)
    write_keys = {s.key for s in SURFACES} if not only else set(only)
    unknown = write_keys - {s.key for s in SURFACES}
    if unknown:
        raise SystemExit(f"unknown surface(s): {', '.join(sorted(unknown))}")

    reports: List[synth.ClipReport] = []
    by_surface: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    by_pair: Dict[Tuple[str, str], List[float]] = {}
    rings: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    flats: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    rmss: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    peaks: Dict[str, List[float]] = {s.key: [] for s in SURFACES}
    tails: List[Tuple[str, float]] = []

    for s in SURFACES:
        for a in ACTORS:
            for v in range(1, VARIANTS + 1):
                name = f"step_{s.key}_{a.key}_{v:02d}.wav"
                path = os.path.join(OUT_DIR, name)
                if s.key in write_keys:
                    buf, level = build_step(s, a, v)
                    synth.write_wav(path, buf, headroom_db=level, stereo=False)
                elif not os.path.exists(path):
                    raise SystemExit(
                        f"{name} is not on disk and was not selected for writing. The "
                        f"separation matrix needs all eight surfaces to mean anything; "
                        f"run without --only to build the whole set."
                    )
                r = synth.assert_usable(path, min_seconds=0.05, max_seconds=2.0)
                if r.channels != 1:
                    raise AssertionError(
                        f"{name}: {r.channels} channels. §05 needs these spatialised, "
                        f"and Unity will not spatialise a stereo clip."
                    )
                reports.append(r)
                # signal_centroid, not r.spectral_centroid: see SIGNAL_FLOOR_DB.
                # The raw figure credits the darkest floors with their own dither.
                centroid = signal_centroid(path)
                by_surface[s.key].append(centroid)
                by_pair.setdefault((s.key, a.key), []).append(centroid)
                rings[s.key].append(ring_ms(path))
                rmss[s.key].append(r.rms)
                peaks[s.key].append(r.peak)
                flats[s.key].append(
                    inband_flatness(path, s.band[0] * s.pitch(a), s.band[1] * s.pitch(a))
                )
                tails.append((name, tail_db(path)))

    written = len(write_keys) * len(ACTORS) * VARIANTS
    print(f"{written} clips written to {OUT_DIR} "
          f"({', '.join(sorted(write_keys))}); {len(reports)} measured")
    print(f"all mono, {synth.SAMPLE_RATE} Hz, 16-bit PCM; every clip passed synth.assert_usable()")
    print()
    print(synth.report_table(reports))

    print()
    print("=" * 82)
    print("§12/§04 SEPARATION — can the Listener tell the eight floors apart?")
    print("=" * 82)
    print(f"  centroids below are signal_centroid (bins within {SIGNAL_FLOOR_DB:.0f} dB of the")
    print("  loudest), not the raw full-band figure in the table above — see SIGNAL_FLOOR_DB.")

    overall = {k: geo_mean(v) for k, v in by_surface.items()}
    worst_overall = print_matrix(
        f"[1] All {len(reports)} clips, per surface (the required centroid matrix)",
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

    # The fourth axis, and the one §04's three new floors are actually built on.
    # 침수/흙/카펫 are not three more timbres — they are a loudness ladder, and
    # ListenerClarity* in GameConstants promises the player it exists. A carpet
    # that measured like concrete would make §04's blind spot a lie told in the
    # HUD, which is worse than not having the surface at all.
    rms = {k: geo_mean(v) for k, v in rmss.items()}
    peak_of = {k: geo_mean(v) for k, v in peaks.items()}
    print()
    print("[6] Fourth axis: loudness. §04's clarity ladder, as shipped level")
    print()
    print(f"  {'surface':<10}{'rms dBFS':>10}{'peak dBFS':>11}{'§04 clarity':>13}")
    print("  " + "-" * 46)
    for key in sorted(rms, key=lambda k: rms[k], reverse=True):
        print(f"  {key:<10}{synth.gain_to_db(rms[key]):>10.1f}"
              f"{synth.gain_to_db(peak_of[key]):>11.1f}{CLARITY.get(key, float('nan')):>13.2f}")

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
    # Gravel must still be the most noise-like floor of all eight...
    assert flat["gravel"] == max(flat.values()), (
        f"§12 says 자갈 = 부스럭 — broadband, almost no pitch — so gravel must be the "
        f"most noise-like surface on the map. Got {flat}. Check that the grain "
        f"scatter has not collapsed into one pitched burst."
    )
    # ...but the 1.4x *margin* is only asserted against the surfaces it was
    # calibrated on. inband_flatness measures inside each surface's own band, and
    # a narrow band is flat almost by construction: fewer bins, less room for the
    # geometric and arithmetic means to diverge. 카펫 occupies 34-300 Hz — under
    # thirty bins — and reads 0.55 against 자갈's 0.64 while being, in fact, a
    # single damped thud. Comparing tonality across a 9x bandwidth difference
    # measures the bandwidth. The five §12 floors all span at least 570 Hz, so
    # among them the comparison still means what it says.
    ORIGINAL_FIVE = ("wood", "tile", "gravel", "concrete", "metal")
    others = sorted((flat[k] for k in ORIGINAL_FIVE if k != "gravel"), reverse=True)
    assert flat["gravel"] >= others[0] * SEPARATION_MIN, (
        f"§12 says 자갈 = 부스럭, so gravel must out-measure the other §12 floors by a "
        f"clear margin, not merely lead them. Got {flat['gravel']:.3f} against "
        f"{others[0]:.3f}."
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

    # §04's loudness ladder. Each of these is a promise GameConstants makes to the
    # player in a number they can read, so a clip set that inverts one is a HUD
    # that lies rather than audio that is merely off.
    assert rms["water"] == max(rms.values()), (
        f"§04 gives 침수 clarity 1.00 and calls it the loudest thing to stand on — "
        f"'you cannot cross it unheard'. Water measures "
        f"{synth.gain_to_db(rms['water']):.1f} dBFS against a loudest of "
        f"{synth.gain_to_db(max(rms.values())):.1f}."
    )
    assert rms["water"] > rms["tile"], (
        f"침수 must sit above 타일, the loudest of §12's hard floors. Got "
        f"{synth.gain_to_db(rms['water']):.1f} vs {synth.gain_to_db(rms['tile']):.1f} dBFS."
    )
    assert rms["carpet"] == min(rms.values()), (
        f"§04 makes 카펫 the quietest surface, below even an unassigned floor — the "
        f"whole reason the long way round is worth walking. Carpet measures "
        f"{synth.gain_to_db(rms['carpet']):.1f} dBFS against a quietest of "
        f"{synth.gain_to_db(min(rms.values())):.1f}."
    )
    assert rms["carpet"] < rms["concrete"], (
        f"카펫 must be quieter than 콘크리트. Got {synth.gain_to_db(rms['carpet']):.1f} "
        f"vs {synth.gain_to_db(rms['concrete']):.1f} dBFS."
    )
    assert rms["gravel"] < rms["earth"] < rms["concrete"], (
        f"§04 puts 흙 under 콘크리트 (clarity 0.40 against 0.50) and above 자갈, which "
        f"absorbs more. Got gravel {synth.gain_to_db(rms['gravel']):.1f}, earth "
        f"{synth.gain_to_db(rms['earth']):.1f}, concrete "
        f"{synth.gain_to_db(rms['concrete']):.1f} dBFS."
    )
    # A splash is the loudest thing in the building, not the loudest thing the
    # mixer can take. synth's house rule is that positional clips leave with at
    # least 3 dB of headroom so several can overlap without clipping.
    loudest_peak = max(peak_of.values())
    assert loudest_peak <= synth.db_to_gain(-3.0), (
        f"a footstep peaks at {synth.gain_to_db(loudest_peak):.1f} dBFS, above the "
        f"-3.0 dB headroom synth.write_wav reserves for positional audio."
    )

    print(f"PASS  eight surfaces separable: worst overall pair {worst_overall:.2f}x, "
          f"worst within-actor pair {min(per_actor_worst.values()):.2f}x, "
          f"worst clip-level {clip_worst:.2f}x, floor {SEPARATION_MIN:.2f}x")
    print(f"PASS  metal rings longest ({ring['metal']:.0f} ms) and is most tonal "
          f"({flat['metal']:.3f}); gravel is most noise-like ({flat['gravel']:.3f}); "
          f"concrete dies fastest of the hard floors ({ring['concrete']:.0f} ms)")
    ladder = " > ".join(
        f"{k} {synth.gain_to_db(rms[k]):.1f}"
        for k in sorted(rms, key=lambda k: rms[k], reverse=True))
    print(f"PASS  §04 loudness ladder (dBFS RMS): {ladder}")
    print(f"PASS  no clip truncated above {TAIL_HEADROOM_DB:.0f} dB "
          f"(worst {worst_tail[1]:.1f} dB)")
    print("=" * 82)
    return 0


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument(
        "--only", nargs="+", metavar="SURFACE", choices=sorted(BY_KEY),
        help="write only these surfaces; the rest are read from disk and still "
             "measured. Use when adding or retuning one material so a rebuild "
             "cannot touch the other seven.",
    )
    raise SystemExit(main(parser.parse_args().only))
