"""Monster voice and presence — procedural generation for §06's state machine.

Everything here is synthesised from scratch on top of `synth.py`. Nothing is
sampled or licensed, which is the whole point for a Steam release: there is no
clip in the build whose provenance anyone has to defend.

WHAT THE MONSTER SOUNDS LIKE, AND WHY
─────────────────────────────────────
§01 fixes two facts that the voice has to carry:

  * **It cannot be killed.** Every deterrent is temporary (섬광 · 문 · 함정). So the
    voice must never sound *hurt in a way that resolves*. Nothing here ends on a
    dying fall; the stun clips in particular end on the threat re-forming.
  * **It is only 0.3 m/s faster than a running player** (§06: 달리기 4.5 < 괴물 4.8).
    The terror is that it is permanently *almost* catching you. Audio-wise that
    means the voice is close-mic'd and unhurried — never strained, never winded.
    A monster that sounded out of breath would be a monster you could outrun.

The voice is built as a physical model rather than a texture: a glottal flow pulse
train (`voiced`), roughened three ways that real large-animal larynges are rough —
jitter (period-to-period pitch instability), shimmer (amplitude instability) and
diplophonia (alternate pulses attenuated, which puts a strong subharmonic at f0/2
and is what the ear reads as a *growl* rather than a hum) — plus biphonation, a
second incommensurate source that makes it read as not-human. That excitation then
passes a parallel formant bank (`tract`) whose gains are morphed over the length of
the utterance, so the mouth audibly opens and closes inside a single sound.

Formants are scaled to roughly 0.6× human, i.e. a vocal tract about 1.6× longer
than ours. That single number is what makes it read as *big* at any pitch.

WHAT EACH CLIP IS FOR, IN GAMEPLAY TERMS
────────────────────────────────────────
monster_roar_01..03      §06 추격 entry. This is the player's cue to RUN, so it is
                         the most legible thing in the game's mix: fastest attack,
                         highest level, front-loaded energy. A player must be able
                         to classify it inside ~150 ms, before the clip is even
                         half done, because 0.3 m/s of margin (§06) means a late
                         reaction is a caught player. Deliberately the widest-band
                         and loudest monster sound so it cannot be confused with
                         the alert growl.
monster_growl_01..03     §06 경계 — "it heard something and is coming". Mouth
                         closed: low, muffled, dark. It must be *unmistakably not*
                         a roar, because the correct response is different (freeze
                         / break line of sight, not sprint). Restrained on purpose.
monster_search_01..02    §06 수색 — frustrated hunting around the last known
                         position for 15 s. Articulated rather than sustained:
                         sniffs plus a creaking downward grumble. The sniff is the
                         payload — it says "it is looking for *you*", which is what
                         makes hiding tense instead of safe.
monster_breath_loop_01..02  Present while it is near, crossfaded by distance. The
                         cue the 청음사 (§04) leans on when footsteps stop. Seamless.
monster_presence_bed     §06/§07. Very low, near-subsonic, crossfaded by proximity
                         for "close but unseen". Confined below ~250 Hz on purpose:
                         §12 makes floor material a *gameplay channel* and the
                         Listener reads it from footstep timbre, so the bed is not
                         allowed to occupy the band those cues live in. A pretty
                         ambience that masked a gravel-vs-tile distinction would be
                         a balance bug, not a polish win.
monster_grab_01..02      The moment a player is caught. Body impact + jaw + one
                         compressed vocal burst. Punctuation, highest crest factor.
monster_stun_01..02      §04 섬광수. Exactly 2.50 s long, matching
                         GameConstants.FlashStunSeconds — the clip *is* the timer.
                         Shape: pain shriek → disoriented wavering moan → the
                         growl re-gathering into a steady low threat. That last
                         third is the design requirement from §01 ("저지는 전부
                         일시적") made audible: the player hears the window closing
                         instead of reading a UI bar. If FlashStunSeconds changes,
                         regenerate these two clips or the cue starts lying.

DELIBERATELY ABSENT: 정지 (Standstill). DO NOT ADD ONE.
──────────────────────────────────────────────────────
§06's state table gives 정지 the sound value **없음**, and the design note calls
that the game's weapon:

    "괴물이 멈추면 소리를 내지 않는다. 그러면 청음사가 위치를 잃고, 플레이어들은
     '어디 갔어? 방금 여기 있었는데' 하게 되고, 방심하고 나오는 순간 걸린다."
    "침묵이 가장 무서운 소리다."

There is no monster_standstill clip and there must never be one. The silence is not
a missing asset — it is the mechanic that makes 15~30 초마다 걸리는 정지 terrifying
and it is what makes the Listener's information *lossy*, which is the point of the
role's constraint. A future pass that "fills the gap" deletes the scariest five
seconds in the game. The presence bed does not cover for it either: the bed is
crossfaded by *proximity*, not by state, and the Gameplay layer must keep it that
way — if the bed were gated on Standstill it would leak the position that §06
intends to hide.

CONVENTIONS
───────────
* 48 kHz / 16-bit / **MONO** everywhere. All of these are world-positional, and
  Unity will not spatialise a stereo clip (§05: 3D 오디오는 카메라 기준 → 헤드폰 필수).
* No baked reverb. Size comes from the tract model and from low formants, not from
  a tail — a tail baked into a positional clip gets distance-attenuated along with
  the dry sound and stops reading as space.
* Levels are authored as a hierarchy, not normalised flat (see PEAK_DB). A roar and
  a breath are separated by ~12 dB at the source because vocal effort differs by
  that much; Unity's 3D attenuation then works on honest material.
* Everything is seeded. A rebuild is byte-identical, so a regeneration that shows a
  diff means a sound actually changed.

LOOPS
─────
`monster_breath_loop_*` and `monster_presence_bed` are true loops, built circularly:
noise is shaped in the FFT domain (circular convolution, so the buffer is periodic
by construction rather than by crossfade), every modulator's frequency is an exact
integer multiple of 1/loop_length, and voiced f0 contours are scaled so the phase
accumulates to a whole number of cycles. The loop boundary is additionally placed at
a designed amplitude trough — mid-pause between breaths, trough of the bed's slow
surge — because `synth.write_wav` fades 2 ms in / 6 ms out unconditionally, and
that fade must land somewhere it cannot be heard. Both facts are measured and
asserted at the bottom of this file; see `seam_metrics` for why the naive
|last − first| test is vacuous here and what is checked instead.

Run:
  /Users/doogi/horror-game/tools/audio/.venv/bin/python \
      /Users/doogi/horror-game/tools/audio/gen_monster_audio.py
"""

from __future__ import annotations

import hashlib
import json
import os
from dataclasses import dataclass
from typing import Callable, Sequence

import numpy as np
from scipy import signal

import synth
from synth import Mode

SR = synth.SAMPLE_RATE

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(
    os.path.join(HERE, "..", "..", "unity", "HorrorGame", "Assets", "Audio", "Monster")
)

SEED = 60_060
"""Base seed. §06 is the monster's section; the number is a mnemonic, not magic."""

STUN_SECONDS = 2.50
"""Mirrors GameConstants.FlashStunSeconds (§04, §16-3 provisional).

The stun clip's length is a gameplay signal — the player learns the window by ear —
so if that constant moves, these clips have to be regenerated.
"""

PEAK_DB = {
    # A designed hierarchy, not flat normalisation. Vocal effort really does differ
    # by this much, and Unity's 3D falloff only sounds right on honest material.
    #
    # These are peak dBFS, i.e. meter readings, and the meter lies about low-frequency
    # material: see `a_weight_offset` and the LOUDNESS ORDER check. What the design
    # actually requires is an order in *perceived* level, and these numbers are chosen
    # to produce it — which is why the growl sits only 4 dB under the roar on the
    # meter yet ~9 dB under it to the ear.
    "roar": -3.0,       # the run cue; must dominate everything
    "grab": -4.5,       # the caught-you climax; allowed to sit beside the roar
    "stun": -5.5,       # pained, not triumphant
    "growl": -7.0,      # §06 경계 — a warning that has to be noticed, not a shout
    "search": -10.5,    # it is not addressing you yet
    "breath": -22.0,    # ambience; must never mask a footstep cue (§12)
    "bed": -18.0,       # near-subsonic, felt rather than heard
}


# ── Small numeric helpers ───────────────────────────────────────────────────


def _norm(buf: np.ndarray) -> np.ndarray:
    """Peak-normalises to 1.0, tolerating an all-zero buffer."""
    x = np.asarray(buf, dtype=np.float64)
    peak = float(np.max(np.abs(x))) if x.size else 0.0
    return (x if peak < 1e-15 else x / peak).astype(np.float32)


def _lp_noise(n: int, seed: int, cutoff: float) -> np.ndarray:
    """Unit-variance low-passed noise, for jitter/shimmer/wobble modulators."""
    x = np.random.default_rng(seed).standard_normal(n).astype(np.float32)
    y = synth.lowpass(x, cutoff, order=2).astype(np.float64)
    sd = float(np.std(y))
    return (y / sd if sd > 1e-12 else y).astype(np.float32)


def contour(points: Sequence[tuple[float, float]], seconds: float) -> np.ndarray:
    """Piecewise smoothstep curve through (time_seconds, value) points.

    Smoothstep rather than linear because a corner in a pitch or gain contour is
    audible as a tick, and smoothing afterwards with a filter would smear the
    fast attacks the roar depends on.
    """
    n = synth.n_samples(seconds)
    t = np.arange(n, dtype=np.float64) / SR
    ts = np.asarray([p[0] for p in points], dtype=np.float64)
    vs = np.asarray([p[1] for p in points], dtype=np.float64)

    out = np.empty(n, dtype=np.float64)
    idx = np.clip(np.searchsorted(ts, t, side="right") - 1, 0, len(ts) - 2)
    t0, t1 = ts[idx], ts[idx + 1]
    v0, v1 = vs[idx], vs[idx + 1]
    span = np.where(t1 > t0, t1 - t0, 1.0)
    u = np.clip((t - t0) / span, 0.0, 1.0)
    out = v0 + (v1 - v0) * (u * u * (3.0 - 2.0 * u))
    out[t <= ts[0]] = vs[0]
    out[t >= ts[-1]] = vs[-1]
    return out.astype(np.float32)


# ── Voice source: a larynx ──────────────────────────────────────────────────


@dataclass
class Voice:
    """One glottal excitation, plus the flow signal used to couple noise to it."""

    src: np.ndarray
    """Glottal derivative — the excitation fed to the tract."""

    flow: np.ndarray
    """Glottal flow in [0, 1]. Turbulence rides this so breath pulses with voice."""

    f0: np.ndarray
    """Realised fundamental in Hz, after jitter."""


def _glottal_flow(frac: np.ndarray, open_q: float, fall_q: float) -> np.ndarray:
    """Rosenberg-style glottal flow pulse over a phase fraction in [0, 1).

    Continuous in value (only the slope jumps at closure), which keeps aliasing
    down at the low fundamentals this creature uses without needing oversampling.
    """
    rise = 0.5 * (1.0 - np.cos(np.pi * np.clip(frac / open_q, 0.0, 1.0)))
    fall = np.cos(0.5 * np.pi * np.clip((frac - open_q) / fall_q, 0.0, 1.0))
    return np.where(frac < open_q, rise, np.where(frac < open_q + fall_q, fall, 0.0))


def voiced(
    seconds: float,
    f0: float | np.ndarray,
    seed: int,
    *,
    open_q: float = 0.42,
    fall_q: float = 0.14,
    jitter: float = 0.02,
    shimmer: float = 0.14,
    sub_depth: float = 0.0,
    biphonic: float = 0.0,
    biphonic_ratio: float = 1.31,
    loop_periodic: bool = False,
) -> Voice:
    """Synthesises a rough, large-animal glottal source.

    `sub_depth` attenuates alternate pulses (diplophonia). That single parameter is
    the difference between a hum and a growl: it puts a strong component at f0/2 and
    a comb of odd half-harmonics between the harmonics, which is exactly the
    "period-doubled" larynx of a big animal under load.

    `biphonic` adds a second source at an incommensurate ratio. Two independent
    pitches in one throat is anatomically impossible for a human, and the ear knows
    it — it is the cheapest way to make something read as *not a person shouting*.

    `loop_periodic` scales f0 so the accumulated phase closes on a whole number of
    cycles across the buffer, which the loops need for circular continuity.
    """
    n = synth.n_samples(seconds)
    f0_arr = (
        np.full(n, float(f0), dtype=np.float64)
        if np.isscalar(f0)
        else np.asarray(f0, dtype=np.float64)[:n]
    )
    if len(f0_arr) < n:  # a shorter contour holds its last value
        f0_arr = np.concatenate([f0_arr, np.full(n - len(f0_arr), f0_arr[-1])])

    if jitter > 0.0:
        f0_arr = f0_arr * (1.0 + jitter * _lp_noise(n, seed + 1_301, 22.0))
    f0_arr = np.maximum(f0_arr, 8.0)

    if loop_periodic:
        cycles = float(np.sum(f0_arr) / SR)
        f0_arr *= max(1.0, round(cycles)) / max(cycles, 1e-9)

    phase = np.cumsum(f0_arr) / SR
    flow = _glottal_flow(phase % 1.0, open_q, fall_q)
    src = np.gradient(flow)

    if shimmer > 0.0:
        src = src * (1.0 + shimmer * _lp_noise(n, seed + 2_609, 9.0))

    if sub_depth > 0.0:
        # Alternate-pulse attenuation. Phase offset per-seed so variants of the
        # same growl do not doubling-lock in the same place.
        off = float(np.random.default_rng(seed + 4_099).uniform(0.0, 2.0 * np.pi))
        src = src * (1.0 - sub_depth * 0.5 * (1.0 - np.cos(np.pi * phase + off)))

    if biphonic > 0.0:
        phase2 = np.cumsum(f0_arr * biphonic_ratio) / SR
        src = src + biphonic * np.gradient(_glottal_flow(phase2 % 1.0, open_q * 0.9, fall_q))

    src = synth.lowpass(src.astype(np.float32), 9_000.0, order=4)
    return Voice(src=_norm(src), flow=flow.astype(np.float32), f0=f0_arr.astype(np.float32))


def turbulence(v: Voice, seed: int, lo: float, hi: float, coupling: float = 0.75) -> np.ndarray:
    """Band-limited noise amplitude-coupled to the glottal flow.

    Uncoupled noise sits *beside* a voice; noise gated by the flow sits *inside*
    it, which is what makes a roar sound wet rather than like a synth plus hiss.
    """
    n = len(v.src)
    noise = np.random.default_rng(seed).standard_normal(n).astype(np.float32)
    band = synth.bandpass(noise, lo, hi, order=4)
    env = (1.0 - coupling) + coupling * np.clip(v.flow, 0.0, 1.0)
    return _norm(band * env.astype(np.float32))


# ── Vocal tract: parallel formant bank ─────────────────────────────────────
#
# Formant sets, all scaled to ~0.6× human (a tract ~1.6× longer than ours). Each
# entry is (frequency Hz, Q, gain). Parallel, not cascaded: `synth.resonator` is a
# unity-gain resonant band, so cascading would pass only the intersection.

CLOSED = [(275.0, 3.6, 1.00), (615.0, 6.5, 0.60), (1_330.0, 9.0, 0.24), (2_150.0, 11.0, 0.07)]
"""Mouth shut. §06 경계 growl — muffled and dark, but not inaudible.

The upper two gains were 0.12 and absent in the first pass, which put 99% of the
growl's energy below 500 Hz and made it measure 6 dB *quieter to the ear* than the
idle breathing loop despite sitting 6 dB higher on the meter — see `a_weight_offset`.
A real closed-mouth growl does radiate 500–2000 Hz through the cheeks and nose; the
first pass was modelling a sealed box, not an animal. This is the fix, and it is why
§06's 경계 cue is now something a player can actually notice.
"""

HALF = [(385.0, 4.2, 1.00), (830.0, 7.0, 0.62), (1_760.0, 10.0, 0.26), (2_880.0, 12.0, 0.09)]
"""Mid-open. Transitional, and the resting shape for search grumbles."""

OPEN = [(625.0, 4.6, 1.00), (1_165.0, 7.5, 0.88), (2_430.0, 11.0, 0.42), (3_620.0, 13.0, 0.20)]
"""Wide open. §06 추격 roar — the only shape that gets the top two octaves."""

SCREAM = [(905.0, 6.5, 1.00), (1_915.0, 9.5, 0.92), (3_140.0, 12.0, 0.55), (4_650.0, 14.0, 0.28)]
"""Strained upper register, layered under a roar so it screams instead of rumbles."""

NASAL = [(320.0, 5.5, 0.70), (1_140.0, 11.0, 1.00), (2_360.0, 13.0, 0.48)]
"""Air through a nose. The sniff in the search vocalisation."""

Formants = Sequence[tuple[float, float, float]]


def tract(
    src: np.ndarray,
    formants: Formants,
    *,
    sub_gain: float = 0.85,
    sub_cut: float = 165.0,
    lp: float | None = None,
) -> np.ndarray:
    """Runs an excitation through a parallel formant bank plus a chest path.

    The explicit low-passed `sub` path exists because the formant bank starts at
    275 Hz and the fundamental is often below 70 Hz — without it the creature loses
    its chest and stops sounding large.
    """
    out = sub_gain * synth.lowpass(src, sub_cut, order=2).astype(np.float64)
    for freq, q, gain in formants:
        out = out + gain * synth.resonator(src, freq, q).astype(np.float64)
    shaped = out.astype(np.float32)
    if lp is not None:
        shaped = synth.lowpass(shaped, lp, order=4)
    return _norm(shaped)


def tract_morph(
    src: np.ndarray,
    a: Formants,
    b: Formants,
    morph: np.ndarray,
    *,
    sub_gain: float = 0.85,
    sub_cut: float = 165.0,
    lp: float | None = None,
) -> np.ndarray:
    """Crossfades two formant banks over time — the mouth opening inside one sound.

    Morphing gains rather than sliding frequencies is cheap and, at these speeds,
    indistinguishable by ear from a real articulation.
    """
    va = tract(src, a, sub_gain=sub_gain, sub_cut=sub_cut, lp=lp).astype(np.float64)
    vb = tract(src, b, sub_gain=sub_gain, sub_cut=sub_cut, lp=lp).astype(np.float64)
    m = np.asarray(morph, dtype=np.float64)[: len(va)]
    return _norm((1.0 - m) * va + m * vb)


def finish(buf: np.ndarray, *, hp: float = 30.0, drive: float | None = None) -> np.ndarray:
    """Final per-clip conditioning: DC/rumble block, then optional soft saturation.

    Saturation goes last so it compresses the assembled sound the way a chest and a
    throat compress a real one, instead of only squashing one layer.
    """
    out = np.asarray(buf, dtype=np.float32)
    if hp is not None:
        out = synth.highpass(out, hp, order=2)
    out = _norm(out)
    if drive is not None:
        out = _norm(synth.saturate(out, drive))
    return out


# ── Circular (loop-safe) building blocks ───────────────────────────────────


def circ_noise(seconds: float, seed: int) -> np.ndarray:
    """Gaussian noise of exactly the loop length."""
    return np.random.default_rng(seed).standard_normal(synth.n_samples(seconds)).astype(np.float32)


def circ_shape(x: np.ndarray, response: Callable[[np.ndarray], np.ndarray]) -> np.ndarray:
    """Applies a magnitude response in the FFT domain — i.e. circular convolution.

    This is why the loops need no crossfade. A time-domain filter has a start-up
    transient and an unresolved tail, so its output is *not* periodic even when its
    input is; multiplying a spectrum is periodic by construction. The DC bin is
    zeroed here as well, which is what keeps `assert_usable`'s DC check happy on
    long, very low material.
    """
    spec = np.fft.rfft(np.asarray(x, dtype=np.float64))
    freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
    spec *= response(freqs)
    spec[0] = 0.0
    return np.fft.irfft(spec, n=len(x)).astype(np.float32)


def resp_band(lo: float | None, hi: float | None, order: int = 2, tilt_db_oct: float = 0.0,
              tilt_ref: float = 100.0) -> Callable[[np.ndarray], np.ndarray]:
    """Butterworth-magnitude band with an optional spectral tilt."""

    def fn(f: np.ndarray) -> np.ndarray:
        fs = np.maximum(f, 1e-3)
        mag = np.ones_like(fs)
        if lo is not None:
            mag = mag / np.sqrt(1.0 + (lo / fs) ** (2 * order))
        if hi is not None:
            mag = mag / np.sqrt(1.0 + (fs / hi) ** (2 * order))
        if tilt_db_oct != 0.0:
            mag = mag * (fs / tilt_ref) ** (tilt_db_oct / 6.0206)
        return mag

    return fn


def loop_mod(seconds: float, cycles_per_loop: float, seed: int, depth: float) -> np.ndarray:
    """A slow modulator whose period divides the loop exactly.

    Any modulator inside a loop must complete a whole number of cycles across it,
    or the loop point steps. Rounding the cycle count is the whole trick.
    """
    k = max(1.0, round(cycles_per_loop))
    ph = float(np.random.default_rng(seed).uniform(0.0, 2.0 * np.pi))
    t = synth.t_axis(seconds)
    return (1.0 - depth + depth * 0.5 * (1.0 + np.cos(2.0 * np.pi * k * t / seconds + ph))).astype(np.float32)


def loop_partial(seconds: float, freq: float, seed: int) -> np.ndarray:
    """A sinusoid snapped to the nearest exact harmonic of the loop rate."""
    k = max(1.0, round(freq * seconds))
    ph = float(np.random.default_rng(seed).uniform(0.0, 2.0 * np.pi))
    return np.sin(2.0 * np.pi * k * synth.t_axis(seconds) / seconds + ph).astype(np.float32)


# ── §06 추격: roar / scream ────────────────────────────────────────────────


def roar(index: int) -> np.ndarray:
    """Chase-entry roar. The player's cue to run, so legibility beats subtlety.

    Front-loaded on purpose: with only 0.3 m/s of margin (§06) a player who needs
    the whole clip to classify the sound has already lost the distance. The onset
    carries a hard glottal attack plus a short breath crack so it punches through
    whatever else is in the mix.
    """
    seed = SEED + 100 + index * 37

    if index == 0:
        # Baseline "it has seen you": surge up, hold wide open, close down, and end
        # on an intake — it is not finished with you.
        secs = 2.10
        f0 = contour([(0.0, 78.0), (0.10, 118.0), (0.34, 132.0), (1.30, 122.0),
                      (1.72, 96.0), (2.10, 84.0)], secs)
        amp = contour([(0.0, 0.0), (0.022, 1.0), (0.30, 0.95), (1.25, 0.88),
                       (1.62, 0.55), (1.86, 0.30), (2.02, 0.16), (2.10, 0.0)], secs)
        morph = contour([(0.0, 0.55), (0.09, 1.0), (1.30, 0.95), (1.80, 0.35), (2.10, 0.2)], secs)
        sub_depth, biph, scream_gain = 0.34, 0.16, 0.30
    elif index == 1:
        # Shorter, higher, more scream than roar. The variant that reads as a
        # shriek at distance, where the low end has already fallen off.
        #
        # Its envelope also has to be a different *shape* from variant 1, not just a
        # different timbre: this one chokes off hard at 1.05 s on a glottal stop and
        # comes back for a clipped second cry. Two roars that rise and decay
        # identically are the repeated-sample problem with extra steps.
        secs = 1.76
        f0 = contour([(0.0, 96.0), (0.07, 150.0), (0.26, 168.0), (0.92, 160.0),
                      (1.04, 138.0), (1.16, 176.0), (1.44, 150.0), (1.76, 112.0)], secs)
        amp = contour([(0.0, 0.0), (0.016, 1.0), (0.24, 0.98), (0.88, 0.86),
                       (1.00, 0.30), (1.07, 0.07), (1.15, 0.92), (1.40, 0.72),
                       (1.62, 0.24), (1.76, 0.0)], secs)
        morph = contour([(0.0, 0.7), (0.06, 1.0), (0.95, 0.92), (1.06, 0.55),
                         (1.18, 1.0), (1.76, 0.5)], secs)
        sub_depth, biph, scream_gain = 0.24, 0.24, 0.52
    else:
        # Two-part: surge, a breath dip that sounds like it is drawing air, then a
        # bigger second surge. §01 — it does not run out.
        secs = 2.62
        f0 = contour([(0.0, 70.0), (0.09, 112.0), (0.55, 118.0), (0.98, 88.0),
                      (1.20, 128.0), (1.95, 138.0), (2.30, 104.0), (2.62, 82.0)], secs)
        amp = contour([(0.0, 0.0), (0.026, 0.98), (0.52, 0.85), (0.98, 0.34),
                       (1.18, 1.0), (1.90, 0.92), (2.28, 0.48), (2.50, 0.18), (2.62, 0.0)], secs)
        morph = contour([(0.0, 0.5), (0.08, 0.95), (0.95, 0.45), (1.22, 1.0),
                         (2.00, 0.9), (2.62, 0.3)], secs)
        sub_depth, biph, scream_gain = 0.40, 0.18, 0.26

    v = voiced(secs, f0, seed, open_q=0.36, fall_q=0.11, jitter=0.028, shimmer=0.20,
               sub_depth=sub_depth, biphonic=biph, biphonic_ratio=1.37)

    body = tract_morph(v.src, HALF, OPEN, morph, sub_gain=0.95, sub_cut=180.0)
    upper = tract(v.src, SCREAM, sub_gain=0.05, sub_cut=200.0)
    air = turbulence(v, seed + 5, 700.0, 6_500.0, coupling=0.8)

    # Hard glottal attack: a short broadband crack at the onset. Not an impact —
    # it is the vocal folds slamming shut, and it is what makes the first 20 ms
    # identifiable.
    crack = synth.bandpass(circ_noise(0.16, seed + 9), 320.0, 5_200.0, order=4)
    crack = crack * synth.exp_decay(0.16, 0.018)

    mixed = np.zeros(synth.n_samples(secs), dtype=np.float32)
    mixed += 1.00 * body[: len(mixed)]
    mixed += scream_gain * upper[: len(mixed)]
    mixed += 0.16 * air[: len(mixed)]
    synth.place(mixed, _norm(crack) * 0.34, 0.0)

    return finish(mixed * amp, hp=32.0, drive=1.9)


# ── §06 경계: low growl ────────────────────────────────────────────────────


def growl(index: int) -> np.ndarray:
    """Alert growl — "it heard something and is coming".

    Mouth closed, so the energy stays under ~2 kHz and the sound is dark and
    muffled. That contrast with the roar is a *gameplay* requirement, not taste:
    경계 and 추격 call for different player responses (break line of sight vs run),
    so they must not be confusable. Heavy diplophonia keeps it rattling rather than
    humming, and the pitch drifts upward across the clip because the thing is
    closing distance while it makes the sound.
    """
    seed = SEED + 200 + index * 53

    if index == 0:
        secs = 1.56
        f0 = contour([(0.0, 56.0), (0.40, 60.0), (1.10, 64.0), (1.56, 58.0)], secs)
        amp = contour([(0.0, 0.0), (0.16, 0.72), (0.60, 0.92), (1.16, 0.86),
                       (1.42, 0.42), (1.56, 0.0)], secs)
        sub_depth, biph, lp, nose_gain, air_gain = 0.52, 0.10, 3_000.0, 0.30, 0.30
    elif index == 1:
        # The long one. Lowest, slowest, most patient — the version that means it
        # has time. It breaks in the middle to listen, which is literally what §06
        # 경계 is doing (소리 방향으로 이동, 3초 무소득 → 순찰), and which gives this
        # variant an envelope nothing else in the set has.
        secs = 1.94
        f0 = contour([(0.0, 47.0), (0.50, 51.0), (0.86, 49.0), (1.16, 53.0),
                      (1.55, 57.0), (1.94, 50.0)], secs)
        amp = contour([(0.0, 0.0), (0.22, 0.62), (0.70, 0.92), (0.90, 0.20),
                       (1.02, 0.06), (1.20, 0.55), (1.56, 0.95), (1.80, 0.40),
                       (1.94, 0.0)], secs)
        # Lowest and most closed, so the largest share of it leaves through the nose
        # rather than the mouth. Without that this variant is the one that vanishes.
        sub_depth, biph, lp, nose_gain, air_gain = 0.60, 0.14, 2_600.0, 0.46, 0.42
    else:
        # Short and clipped — a single interrogative grunt. The one that plays when
        # 경계 fires off a small noise.
        secs = 1.28
        f0 = contour([(0.0, 66.0), (0.22, 74.0), (0.80, 70.0), (1.28, 60.0)], secs)
        amp = contour([(0.0, 0.0), (0.10, 0.88), (0.44, 0.94), (0.96, 0.62),
                       (1.16, 0.28), (1.28, 0.0)], secs)
        sub_depth, biph, lp, nose_gain, air_gain = 0.44, 0.08, 3_300.0, 0.34, 0.38

    v = voiced(secs, f0, seed, open_q=0.30, fall_q=0.18, jitter=0.022, shimmer=0.18,
               sub_depth=sub_depth, biphonic=biph, biphonic_ratio=1.29)

    body = tract(v.src, CLOSED, sub_gain=0.78, sub_cut=150.0, lp=lp)
    # A little nose in it: air has to go somewhere with the mouth shut. Carrying
    # real weight now — it is most of what makes the growl audible at all.
    nose = tract(v.src, NASAL, sub_gain=0.10, sub_cut=180.0, lp=lp)
    air = turbulence(v, seed + 7, 180.0, 1_400.0, coupling=0.85)

    # Audible breath inside the growl. Measured against the alternative — opening
    # F2/F3 — this lever buys 4x the audibility per unit of timbre change: going
    # 0.17 -> 0.30 here gains 1.3 dB A-weighted while the power centroid moves only
    # 151 -> 172 Hz, where doubling the upper formants gains 0.6 dB and pulls the
    # growl toward the roar's spectrum. Keeping 경계 clearly distinct from 추격
    # matters more than either, so the noise lever is the right one.
    mixed = 1.0 * body + nose_gain * nose + air_gain * air[: len(body)]
    return finish(mixed * amp[: len(mixed)], hp=28.0, drive=1.8)


# ── §06 수색: search vocalisation ──────────────────────────────────────────


def _sniff(seconds: float, seed: int, bright: float = 1.0) -> np.ndarray:
    """One inhaled sniff: air ramps up through a nose, then cuts off abruptly.

    The abrupt end is the whole character — an inhale stops when the intake stops,
    unlike an exhale which decays. Getting that backwards makes it sound like a
    hiss instead of a search.
    """
    n = synth.n_samples(seconds)
    noise = np.random.default_rng(seed).standard_normal(n).astype(np.float32)
    band = synth.bandpass(noise, 520.0 * bright, 5_400.0 * bright, order=4)
    nasal = tract(band, NASAL, sub_gain=0.06, sub_cut=250.0)
    env = contour([(0.0, 0.0), (0.30 * seconds, 0.45), (0.72 * seconds, 1.0),
                   (0.84 * seconds, 0.55), (1.0 * seconds, 0.0)], seconds)
    return _norm((0.75 * nasal + 0.30 * band) * env)


def search(index: int) -> np.ndarray:
    """Search-state vocalisation: frustrated, hunting, articulated.

    §06 gives 수색 fifteen seconds of sweeping the last known position. This is the
    sound that makes those fifteen seconds unbearable while you hold still — it is
    not aimed at the team, it is the sound of something *looking*. Built as
    syllables (sniff / grumble / snort) rather than one sustained tone, because a
    sustained tone reads as a threat display and a display means it already knows
    where you are.
    """
    seed = SEED + 300 + index * 71
    secs = 2.24 if index == 0 else 1.82
    canvas = np.zeros(synth.n_samples(secs), dtype=np.float32)

    if index == 0:
        # sniff · sniff · long creaking grumble that gives up downward
        synth.place(canvas, _sniff(0.20, seed + 1, 1.05) * 1.00, 0.05)
        synth.place(canvas, _sniff(0.17, seed + 2, 0.94) * 0.86, 0.34)

        # The creak bottoms out at 36 Hz, not lower: below that it is inaudible
        # rumble that still eats the headroom the sniffs need, and the sniff is the
        # part that says "it is looking for *you*".
        g_secs = 1.52
        f0 = contour([(0.0, 62.0), (0.30, 58.0), (0.80, 46.0), (1.20, 39.0), (1.52, 36.0)], g_secs)
        amp = contour([(0.0, 0.0), (0.14, 0.80), (0.60, 0.92), (1.05, 0.58),
                       (1.34, 0.24), (1.52, 0.0)], g_secs)
        v = voiced(g_secs, f0, seed + 3, open_q=0.26, fall_q=0.20, jitter=0.035,
                   shimmer=0.26, sub_depth=0.58, biphonic=0.12, biphonic_ratio=1.24)
        grumble = tract_morph(v.src, CLOSED, HALF,
                              contour([(0.0, 0.42), (0.55, 0.36), (1.52, 0.22)], g_secs),
                              sub_gain=0.72, sub_cut=155.0, lp=2_000.0)
        synth.place(canvas, _norm(grumble * amp) * 0.82, 0.62)
    else:
        # grumble · sniff · frustrated snort out
        g_secs = 0.92
        f0 = contour([(0.0, 70.0), (0.26, 64.0), (0.62, 50.0), (0.92, 42.0)], g_secs)
        amp = contour([(0.0, 0.0), (0.10, 0.86), (0.44, 0.90), (0.74, 0.44), (0.92, 0.0)], g_secs)
        v = voiced(g_secs, f0, seed + 3, open_q=0.28, fall_q=0.19, jitter=0.032,
                   shimmer=0.24, sub_depth=0.50, biphonic=0.10, biphonic_ratio=1.33)
        grumble = tract(v.src, CLOSED, sub_gain=0.78, sub_cut=160.0, lp=2_200.0)
        synth.place(canvas, _norm(grumble * amp) * 0.80, 0.02)

        synth.place(canvas, _sniff(0.19, seed + 4, 1.0) * 0.96, 1.02)

        # The snort: a voiced exhale forced through the nose. Frustration, audibly.
        s_secs = 0.52
        sf0 = contour([(0.0, 88.0), (0.14, 74.0), (0.52, 52.0)], s_secs)
        sv = voiced(s_secs, sf0, seed + 5, open_q=0.34, fall_q=0.16, jitter=0.045,
                    shimmer=0.30, sub_depth=0.38, biphonic=0.20, biphonic_ratio=1.41)
        snort_env = contour([(0.0, 0.0), (0.035, 1.0), (0.24, 0.62), (0.42, 0.22), (0.52, 0.0)], s_secs)
        snort = _norm(
            0.7 * tract(sv.src, NASAL, sub_gain=0.35, sub_cut=200.0, lp=4_200.0)
            + 0.55 * turbulence(sv, seed + 6, 400.0, 4_800.0, coupling=0.7)
        )
        synth.place(canvas, _norm(snort * snort_env) * 0.88, 1.30)

    # 45 Hz rather than the usual 30: the creak's fundamental is the lowest thing
    # the monster does and none of it is a cue. Trading it away buys level for the
    # sniffs, which are.
    return finish(canvas, hp=45.0, drive=1.4)


# ── The moment a player is caught: grab / attack ───────────────────────────


def grab(index: int) -> np.ndarray:
    """Caught. Body impact, jaw, and one compressed vocal burst.

    This is punctuation, so it is transient-dominated rather than sustained — the
    highest crest factor in the set. `synth.modal_impact` does the physical layers
    (a dead low thud for the body, a bright short-tau cluster for the jaw) because
    struck-object synthesis is what those are.
    """
    seed = SEED + 400 + index * 89

    if index == 0:
        # "Seize" — impact, then a rasping drag while it takes hold.
        secs = 1.26
        canvas = np.zeros(synth.n_samples(secs), dtype=np.float32)

        thud = synth.modal_impact(
            [Mode(56.0, 0.095, 1.0), Mode(88.0, 0.055, 0.62), Mode(146.0, 0.030, 0.32)],
            0.55, seed + 1, noise_amount=0.55, noise_tau=0.016)
        synth.place(canvas, thud * 0.92, 0.0)

        clack = synth.modal_impact(
            [Mode(870.0, 0.012, 1.0), Mode(1_410.0, 0.008, 0.70),
             Mode(2_580.0, 0.005, 0.42), Mode(4_050.0, 0.0032, 0.20)],
            0.22, seed + 2, noise_amount=0.85, noise_tau=0.004)
        synth.place(canvas, clack * 0.52, 0.035)

        b_secs = 0.72
        f0 = contour([(0.0, 168.0), (0.06, 132.0), (0.30, 92.0), (0.72, 66.0)], b_secs)
        amp = contour([(0.0, 0.0), (0.012, 1.0), (0.22, 0.78), (0.50, 0.40), (0.72, 0.0)], b_secs)
        v = voiced(b_secs, f0, seed + 3, open_q=0.34, fall_q=0.12, jitter=0.040,
                   shimmer=0.26, sub_depth=0.36, biphonic=0.22, biphonic_ratio=1.44)
        burst = tract_morph(v.src, OPEN, HALF, contour([(0.0, 0.0), (0.72, 1.0)], b_secs),
                            sub_gain=0.9, sub_cut=175.0)
        burst = _norm(burst + 0.28 * turbulence(v, seed + 4, 600.0, 6_000.0, coupling=0.8))
        synth.place(canvas, _norm(burst * amp) * 0.85, 0.02)

        # Drag: cloth and flesh moving under a grip. Tremolo makes it read as
        # something being pulled rather than as noise. Loud enough to be a second
        # hump in the envelope — that is what separates "seized and dragged" from
        # variant 2's single bite, in shape rather than only in timbre.
        drag = synth.bandpass(circ_noise(0.70, seed + 5), 240.0, 3_200.0, order=4)
        drag = synth.tremolo(drag, 11.0, 0.7)
        drag_env = contour([(0.0, 0.0), (0.14, 0.55), (0.34, 1.0), (0.52, 0.80),
                            (0.70, 0.0)], 0.70)
        synth.place(canvas, _norm(drag * drag_env) * 0.62, 0.44)

        # A second, weaker grip impact under the drag — it adjusts its hold.
        regrip = synth.modal_impact(
            [Mode(60.0, 0.060, 1.0), Mode(97.0, 0.038, 0.55), Mode(158.0, 0.022, 0.28)],
            0.36, seed + 7, noise_amount=0.5, noise_tau=0.014)
        synth.place(canvas, regrip * 0.44, 0.66)
    else:
        # "Bite" — jaw first, tighter, two clacks and a gnash.
        secs = 1.02
        canvas = np.zeros(synth.n_samples(secs), dtype=np.float32)

        for k, (at, gain, scale) in enumerate([(0.0, 0.95, 1.0), (0.085, 0.62, 1.09)]):
            clack = synth.modal_impact(
                [Mode(940.0 * scale, 0.010, 1.0), Mode(1_520.0 * scale, 0.007, 0.72),
                 Mode(2_760.0 * scale, 0.0045, 0.44), Mode(4_300.0 * scale, 0.003, 0.22)],
                0.20, seed + 10 + k, noise_amount=0.9, noise_tau=0.0035)
            synth.place(canvas, clack * gain, at)

        thud = synth.modal_impact(
            [Mode(62.0, 0.070, 1.0), Mode(101.0, 0.042, 0.58), Mode(163.0, 0.024, 0.30)],
            0.40, seed + 3, noise_amount=0.50, noise_tau=0.013)
        synth.place(canvas, thud * 0.80, 0.008)

        b_secs = 0.60
        f0 = contour([(0.0, 142.0), (0.05, 116.0), (0.26, 84.0), (0.60, 62.0)], b_secs)
        amp = contour([(0.0, 0.0), (0.010, 0.95), (0.16, 0.70), (0.40, 0.34), (0.60, 0.0)], b_secs)
        v = voiced(b_secs, f0, seed + 4, open_q=0.32, fall_q=0.13, jitter=0.044,
                   shimmer=0.28, sub_depth=0.42, biphonic=0.18, biphonic_ratio=1.36)
        bark = tract(v.src, HALF, sub_gain=0.95, sub_cut=170.0)
        bark = _norm(bark + 0.24 * turbulence(v, seed + 5, 500.0, 5_500.0, coupling=0.8))
        synth.place(canvas, _norm(bark * amp) * 0.78, 0.05)

        gnash = synth.bandpass(circ_noise(0.34, seed + 6), 900.0, 6_800.0, order=4)
        gnash = synth.tremolo(gnash, 26.0, 0.85)
        gnash_env = contour([(0.0, 0.0), (0.05, 0.8), (0.22, 0.5), (0.34, 0.0)], 0.34)
        synth.place(canvas, _norm(gnash * gnash_env) * 0.30, 0.30)

    return finish(canvas, hp=32.0, drive=2.1)


# ── §04 섬광수: stun reaction ──────────────────────────────────────────────


def stun(index: int) -> np.ndarray:
    """Flash-stun reaction. Pained, disoriented, and audibly TEMPORARY.

    Length is exactly STUN_SECONDS so the clip doubles as the player's timer.
    Three acts:
      1. a recoil shriek — the flash lands;
      2. a wavering moan with unstable pitch — disorientation, the part that makes
         the deterrent feel like it worked;
      3. the growl re-gathering into a steady low threat, still at strength when
         the clip ends.
    Act 3 is the requirement from §01 ("저지는 전부 일시적") made audible. It must not
    fade out: a fade says "it is over", and nothing about this monster is over.
    """
    seed = SEED + 500 + index * 97
    secs = STUN_SECONDS
    canvas = np.zeros(synth.n_samples(secs), dtype=np.float32)

    if index == 0:
        shriek_secs = 0.46
        sf0 = contour([(0.0, 118.0), (0.05, 262.0), (0.16, 238.0), (0.46, 150.0)], shriek_secs)
        samp = contour([(0.0, 0.0), (0.014, 1.0), (0.13, 0.80), (0.30, 0.34), (0.46, 0.0)],
                       shriek_secs)
        sv = voiced(shriek_secs, sf0, seed + 1, open_q=0.30, fall_q=0.10, jitter=0.055,
                    shimmer=0.34, sub_depth=0.10, biphonic=0.34, biphonic_ratio=1.47)
        shriek = _norm(
            0.85 * tract(sv.src, SCREAM, sub_gain=0.25, sub_cut=220.0)
            + 0.55 * tract(sv.src, OPEN, sub_gain=0.7, sub_cut=190.0)
            + 0.30 * turbulence(sv, seed + 2, 900.0, 8_000.0, coupling=0.85)
        )
        synth.place(canvas, _norm(shriek * samp) * 1.0, 0.0)
        moan_at, moan_secs = 0.40, 1.35
        wobble_rate = 7.5
    else:
        # Variant 2: less shriek, more stagger — it stumbles, then re-forms. Two
        # soft body thuds sell the loss of footing without a new asset.
        shriek_secs = 0.34
        sf0 = contour([(0.0, 132.0), (0.04, 224.0), (0.14, 196.0), (0.34, 138.0)], shriek_secs)
        samp = contour([(0.0, 0.0), (0.012, 1.0), (0.10, 0.72), (0.24, 0.30), (0.34, 0.0)],
                       shriek_secs)
        sv = voiced(shriek_secs, sf0, seed + 1, open_q=0.32, fall_q=0.11, jitter=0.060,
                    shimmer=0.36, sub_depth=0.14, biphonic=0.30, biphonic_ratio=1.52)
        shriek = _norm(
            0.70 * tract(sv.src, SCREAM, sub_gain=0.30, sub_cut=220.0)
            + 0.65 * tract(sv.src, OPEN, sub_gain=0.8, sub_cut=190.0)
            + 0.26 * turbulence(sv, seed + 2, 800.0, 7_200.0, coupling=0.85)
        )
        synth.place(canvas, _norm(shriek * samp) * 0.90, 0.0)

        for k, at in enumerate([0.62, 1.03]):
            stagger = synth.modal_impact(
                [Mode(54.0 + 6.0 * k, 0.085, 1.0), Mode(92.0, 0.045, 0.5), Mode(150.0, 0.024, 0.24)],
                0.42, seed + 20 + k, noise_amount=0.45, noise_tau=0.018)
            synth.place(canvas, stagger * (0.40 - 0.08 * k), at)

        moan_at, moan_secs = 0.30, 1.42
        wobble_rate = 6.2

    # Act 2 — disorientation. The pitch cannot hold still; the tract is slack.
    mf0_base = contour([(0.0, 104.0), (0.35, 82.0), (0.85, 66.0), (moan_secs, 58.0)], moan_secs)
    mf0 = mf0_base * (1.0 + 0.085 * _lp_noise(len(mf0_base), seed + 3, 9.0))
    mv = voiced(moan_secs, mf0, seed + 4, open_q=0.33, fall_q=0.16, jitter=0.048,
                shimmer=0.32, sub_depth=0.30, biphonic=0.26, biphonic_ratio=1.27)
    moan = tract_morph(mv.src, HALF, CLOSED,
                       contour([(0.0, 0.25), (0.7 * moan_secs, 0.7), (moan_secs, 0.95)], moan_secs),
                       sub_gain=0.9, sub_cut=170.0, lp=3_200.0)
    moan = synth.tremolo(moan, wobble_rate, 0.55)
    m_amp = contour([(0.0, 0.0), (0.12, 0.72), (0.45 * moan_secs, 0.60),
                     (0.80 * moan_secs, 0.42), (moan_secs, 0.20)], moan_secs)
    synth.place(canvas, _norm(moan * m_amp) * 0.80, moan_at)

    # Act 3 — the threat re-forms. Rising level, tightening pitch, no fade.
    r_at = moan_at + moan_secs - 0.30
    r_secs = max(0.45, secs - r_at)
    rf0 = contour([(0.0, 52.0), (0.35 * r_secs, 58.0), (r_secs, 63.0)], r_secs)
    rv = voiced(r_secs, rf0, seed + 5, open_q=0.29, fall_q=0.19, jitter=0.024,
                shimmer=0.18, sub_depth=0.56, biphonic=0.12, biphonic_ratio=1.29)
    regather = tract(rv.src, CLOSED, sub_gain=1.05, sub_cut=150.0, lp=2_000.0)
    r_amp = contour([(0.0, 0.10), (0.30 * r_secs, 0.62), (0.72 * r_secs, 0.96), (r_secs, 1.0)], r_secs)
    synth.place(canvas, _norm(regather * r_amp) * 0.95, r_at)

    return finish(canvas, hp=30.0, drive=1.7)


# ── Breathing loop (seamless) ──────────────────────────────────────────────


def breath_loop(index: int) -> tuple[np.ndarray, float]:
    """Seamless breathing loop for "it is near".

    Two dissimilar breath cycles per loop so the repeat is not obvious at the
    3.6 s scale, and the loop boundary sits mid-pause where the envelope is zero —
    that is what makes `synth.write_wav`'s unconditional 2 ms/6 ms fade land
    somewhere inaudible.

    Circularity comes from construction, not from a crossfade: the noise is shaped
    in the FFT domain (so it is periodic), the band gains are envelopes that return
    to zero at both ends, and the voiced rattle's f0 is phase-snapped to a whole
    number of cycles.

    §06 note — it breathes *slowly and evenly*. It is 0.3 m/s faster than a running
    player forever; nothing about it is out of breath.
    """
    seed = SEED + 600 + index * 113
    secs = 7.20 if index == 0 else 8.00

    noise = circ_noise(secs, seed)
    # Three fixed circular bands, cross-mixed by time-varying gains. Filtering per
    # band and then mixing keeps every component periodic, which a time-varying
    # time-domain filter would not.
    #
    # The bands are deliberately dark. A big airway makes low-frequency turbulence:
    # the first pass put 42% of the energy above 2 kHz and measured like a person
    # whispering close to the mic, which is the opposite of the §06 read — and it
    # also sat right on top of the 500 Hz–8 kHz band the Listener uses to tell
    # gravel from tile (§12). Warmer is both scarier and mechanically safer.
    low = circ_shape(noise, resp_band(70.0, 380.0, order=2))
    mid = circ_shape(noise, resp_band(260.0, 1_300.0, order=2))
    high = circ_shape(noise, resp_band(1_100.0, 4_400.0, order=2, tilt_db_oct=-4.0, tilt_ref=1_600.0))

    if index == 0:
        # cycle A: 0.00–3.60 · cycle B: 3.60–7.20, B shallower and slower
        amp_pts = [
            (0.00, 0.00), (0.24, 0.06),
            (0.62, 0.46), (1.02, 0.86), (1.16, 0.62),                 # inhale A
            (1.30, 0.30), (1.46, 0.98), (2.10, 0.66), (2.72, 0.22),   # exhale A
            (3.06, 0.05), (3.60, 0.00),                               # pause A
            (3.92, 0.05),
            (4.34, 0.38), (4.78, 0.70), (4.94, 0.50),                 # inhale B
            (5.08, 0.24), (5.26, 0.80), (5.94, 0.54), (6.52, 0.18),   # exhale B
            (6.88, 0.04), (7.20, 0.00),
        ]
        bright_pts = [
            (0.00, 0.30), (0.90, 0.92), (1.20, 0.70), (1.50, 0.30), (2.40, 0.22),
            (3.60, 0.30), (4.70, 0.86), (5.00, 0.62), (5.30, 0.26), (6.40, 0.20), (7.20, 0.30),
        ]
        rattle_pts = [
            (0.00, 0.0), (1.34, 0.0), (1.58, 0.85), (2.20, 0.55), (2.70, 0.0),
            (5.10, 0.0), (5.36, 0.60), (5.98, 0.36), (6.48, 0.0), (7.20, 0.0),
        ]
        rattle_f0 = [(0.0, 62.0), (1.6, 64.0), (2.7, 56.0), (5.4, 60.0), (6.5, 52.0), (7.2, 56.0)]
        rattle_gain, sub_gain = 0.34, 0.90
    else:
        # Slower, deeper, wetter — the variant for very close range.
        # Cycle 1 draws in slowly and holds at the top before letting go — the
        # breath of something that is not in a hurry (§06: it never needs to be).
        # Cycle 2 is a short catch and a long, low sigh. Deliberately dissimilar
        # to each other and to variant 1, so neither the 4 s cycle nor the 8 s loop
        # announces itself.
        amp_pts = [
            (0.00, 0.00), (0.34, 0.06),
            (0.90, 0.34), (1.42, 0.62), (1.66, 0.78), (1.92, 0.74),
            (2.06, 0.34), (2.26, 1.00), (2.62, 0.86), (3.30, 0.30),
            (3.70, 0.05), (4.02, 0.00),
            (4.30, 0.07),
            (4.62, 0.44), (4.90, 0.86), (5.04, 0.52),
            (5.22, 0.22), (5.44, 0.74), (6.10, 0.70), (6.92, 0.44), (7.44, 0.16),
            (7.76, 0.03), (8.00, 0.00),
        ]
        bright_pts = [
            (0.00, 0.24), (1.30, 0.72), (1.78, 0.80), (2.10, 0.44), (2.34, 0.20),
            (3.00, 0.16), (4.02, 0.24), (4.80, 0.86), (5.10, 0.56), (5.50, 0.22),
            (6.40, 0.14), (7.20, 0.16), (8.00, 0.24),
        ]
        rattle_pts = [
            (0.00, 0.0), (2.10, 0.0), (2.36, 0.95), (2.70, 0.72), (3.28, 0.0),
            (5.26, 0.0), (5.52, 0.62), (6.20, 0.78), (6.98, 0.40), (7.42, 0.0),
            (8.00, 0.0),
        ]
        rattle_f0 = [(0.0, 52.0), (2.4, 54.0), (3.3, 46.0), (6.2, 50.0), (7.4, 43.0), (8.0, 48.0)]
        rattle_gain, sub_gain = 0.46, 1.05

    amp = contour(amp_pts, secs)
    bright = contour(bright_pts, secs)

    air = (
        (sub_gain * low[: len(amp)])
        + (0.95 - 0.30 * bright) * mid[: len(amp)]
        + (0.10 + 0.42 * bright) * high[: len(amp)]
    )

    # Voiced rattle inside the exhale — the thing has something wrong with its
    # throat. Amplitude is zero at both ends, so the loop point never sees it.
    rf0 = contour(rattle_f0, secs)
    rv = voiced(secs, rf0, seed + 3, open_q=0.28, fall_q=0.20, jitter=0.020,
                shimmer=0.16, sub_depth=0.55, biphonic=0.08, biphonic_ratio=1.31,
                loop_periodic=True)
    rattle = tract(rv.src, CLOSED, sub_gain=1.0, sub_cut=150.0, lp=1_900.0)
    rattle = rattle * contour(rattle_pts, secs)

    out = _norm(_norm(air) * amp + rattle_gain * rattle[: len(amp)])
    # No end fades and no highpass: filtfilt would inject an edge transient that
    # breaks circularity, and circ_shape already removed DC.
    return out.astype(np.float32), secs


# ── Presence bed (seamless, near-subsonic) ─────────────────────────────────


def presence_bed() -> tuple[np.ndarray, float]:
    """Near-subsonic proximity bed — "it is close, and you cannot see it".

    Crossfaded by distance by the Gameplay layer, never by monster state (see the
    module docstring on 정지: a state-gated bed would leak the position §06 wants
    hidden).

    Confined below ~250 Hz deliberately. §12 makes floor material a gameplay
    channel and the Listener reads the monster's position from footstep timbre, so
    this bed is not permitted to live in the band those cues occupy. It is meant to
    be felt as pressure, not heard as music.

    Slow surges of 6.4 s (about ten per minute — the respiration rate of something
    very large) with narrow deep troughs. The loop boundary is one of those
    troughs, which is why it cannot be located by ear: every surge looks like the
    seam, so the seam looks like nothing.
    """
    seed = SEED + 700
    secs = 25.60
    surge = 6.40

    # Inharmonic sub partials, each snapped to an exact harmonic of the loop rate
    # so the buffer is periodic to the sample. Inharmonic because a harmonic stack
    # sounds like a note, and a note sounds composed rather than present.
    partials = [(26.5, 1.00), (31.7, 0.72), (38.3, 0.55), (44.1, 0.34), (57.3, 0.20)]
    sub = np.zeros(synth.n_samples(secs), dtype=np.float64)
    for k, (freq, gain) in enumerate(partials):
        # Each partial drifts on its own slow modulator, also loop-exact, so the
        # bed keeps moving without ever going anywhere.
        drift = loop_mod(secs, 1.0 + k, seed + 31 * k, 0.35)
        sub += gain * (loop_partial(secs, freq, seed + 7 * k) * drift).astype(np.float64)

    noise = circ_noise(secs, seed + 3)
    rumble = circ_shape(noise, resp_band(22.0, 95.0, order=2, tilt_db_oct=-6.0, tilt_ref=40.0))
    # A whisper of low-mid so the bed still exists on a laptop speaker. §05 requires
    # headphones, but a player who has not put them on yet should still feel warned.
    lowmid = circ_shape(noise, resp_band(150.0, 420.0, order=2))

    body = _norm(sub) + 0.62 * _norm(rumble) + 0.055 * _norm(lowmid)

    # Surge envelope: zero at t=0 (the loop point), wide flat top, narrow trough.
    # The 0.35 exponent is what makes the trough narrow — at 8 ms from the seam the
    # level is already below -36 dB, so write_wav's fade has nothing to grab.
    t = synth.t_axis(secs)
    raw = 0.5 - 0.5 * np.cos(2.0 * np.pi * t / surge)
    env = np.power(np.maximum(raw, 0.0), 0.35).astype(np.float32)
    # Vary surge depth across the loop (one cycle over the whole thing) so the four
    # surges are not identical.
    env = env * loop_mod(secs, 1.0, seed + 5, 0.30)

    return _norm(body.astype(np.float32) * env), secs


# ── Measurement ────────────────────────────────────────────────────────────


BAND_EDGES = (0.0, 120.0, 250.0, 500.0, 2_000.0, 8_000.0, 24_000.0)


@dataclass
class Feature:
    """Gameplay-relevant measurements of a written clip."""

    name: str
    seconds: float
    peak_db: float
    rms: float
    centroid_mag: float
    centroid_pow: float
    rolloff95: float
    crest: float
    attack_ms: float
    front_ratio: float
    tail_ratio: float
    mod_index: float
    rattle: float
    dba_offset: float
    bands: tuple[float, ...]
    sha12: str
    data: np.ndarray

    @property
    def loudness_db(self) -> float:
        """Peak level corrected for how little of it the ear is sensitive to."""
        return self.peak_db + self.dba_offset


def _envelope(x: np.ndarray, cutoff: float = 45.0) -> np.ndarray:
    return np.abs(synth.lowpass(np.abs(x).astype(np.float32), cutoff, order=2))


def power_spectrum(x: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Average power spectrum via Welch. The single source of every spectral number.

    Two earlier attempts at this were both wrong, in opposite directions, and they
    disagreed with each other — which is the only reason the bug was visible:

    * One FFT of the whole clip through a Hann window is position-biased. The window
      is near zero at both ends, so it deleted the two sniffs at the front of
      `monster_search_01` and reported that 95% of the clip's energy sat below
      290 Hz. The sniffs are 14% of the clip's energy and almost all of it is above
      500 Hz. A metric that cannot see the front of a clip cannot check a sound
      whose payload is an onset.
    * One FFT with no window has no position bias but leaks a skirt off every
      transient and off the very strong low-frequency content here, which inflates
      the high bands instead.

    Welch fixes both: short overlapping segments, each individually windowed, then
    averaged. Every part of the clip is weighted equally and nothing leaks far. Mean
    rather than summed averaging is fine because every number derived from this is a
    ratio, and the segment count cancels.

    `synth.analyse`'s magnitude-weighted centroid stays in the house report table for
    continuity, but it is not used for any check: it sums one term per FFT bin, so
    tens of thousands of near-empty HF bins (including the 16-bit quantisation floor)
    outvote the handful of bins holding the signal. It reads the presence bed at
    309 Hz; the bed's true power centroid is 32 Hz.
    """
    nperseg = int(min(8_192, len(x)))
    freqs, pw = signal.welch(
        x.astype(np.float64), fs=SR, window="hann", nperseg=nperseg,
        noverlap=nperseg // 2, scaling="spectrum", average="mean",
    )
    return freqs, pw


def band_fractions(freqs: np.ndarray, pw: np.ndarray) -> tuple[float, ...]:
    """Fraction of total energy in each BAND_EDGES bin."""
    total = float(np.sum(pw)) or 1.0
    return tuple(
        float(np.sum(pw[(freqs >= lo) & (freqs < hi)]) / total)
        for lo, hi in zip(BAND_EDGES[:-1], BAND_EDGES[1:])
    )


def spectrum_stats(freqs: np.ndarray, pw: np.ndarray) -> tuple[float, float]:
    """Power-weighted spectral centroid and the 95%-energy rolloff frequency."""
    total = float(np.sum(pw)) or 1.0
    centroid = float(np.sum(freqs * pw) / total)
    cum = np.cumsum(pw) / total
    return centroid, float(freqs[min(int(np.searchsorted(cum, 0.95)), len(freqs) - 1)])


def a_weight_offset(freqs: np.ndarray, pw: np.ndarray) -> float:
    """A-weighted level minus unweighted level, in dB — "how audible is this really?".

    Peak dBFS is a meter reading, not a loudness. A-weighting at 60 Hz is about
    −26 dB, so a growl that lives entirely under 250 Hz can measure identically to a
    roar and still be missed by the player. §06 needs 경계 to be *noticed* — it is
    the cue that something is coming — so this number, not peak level, is what says
    whether the warning works. A very negative value is correct for the presence bed
    (it is meant to be felt, §05 headphones) and a bug for a growl.
    """
    f = np.maximum(freqs, 1e-6)
    f2 = f * f
    num = (12_194.0 ** 2) * f2 * f2
    den = (f2 + 20.6 ** 2) * np.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2)) * (f2 + 12_194.0 ** 2)
    a = (num / den) * 10.0 ** (2.0 / 20.0)  # normalised so A(1 kHz) = 1
    total = float(np.sum(pw)) or 1e-30
    return float(10.0 * np.log10(max(float(np.sum(a * a * pw)) / total, 1e-30)))


def _env_spectrum(x: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    env = _envelope(x, 220.0).astype(np.float64)
    mean = float(np.mean(env)) or 1e-9
    ac = env - mean
    mag = np.abs(np.fft.rfft(ac)) * (2.0 / len(ac))
    return np.fft.rfftfreq(len(ac), 1.0 / SR), mag / mean


def mod_index(x: np.ndarray) -> float:
    """RMS amplitude modulation in the 15–120 Hz band, as a fraction of the mean.

    Length-independent, unlike the first version of this metric, which summed raw
    FFT power and therefore grew with sqrt(N) — it made a 7 s breath loop look
    rougher than a 1.5 s growl purely because it was longer.
    """
    env = _envelope(x, 220.0).astype(np.float64)
    mean = float(np.mean(env)) or 1e-9
    band = synth.bandpass((env - mean).astype(np.float32), 15.0, 120.0, order=2).astype(np.float64)
    return float(np.sqrt(np.mean(np.square(band))) / mean)


def rattle(x: np.ndarray) -> float:
    """Peakiness of the envelope spectrum in 15–120 Hz: peak over median.

    This is the growl metric, and it asks the right question. A period-doubled
    larynx modulates its own envelope at a *definite rate* (f0 and f0/2), so its
    envelope spectrum has a spike. Moving air modulates its envelope at every rate
    at once, so its envelope spectrum is flat. Total modulation energy cannot tell
    those apart; peak-over-median can.
    """
    freqs, mag = _env_spectrum(x)
    sel = (freqs >= 15.0) & (freqs <= 120.0)
    if not np.any(sel):
        return 0.0
    med = float(np.median(mag[sel])) or 1e-12
    return float(np.max(mag[sel]) / med)


def attack_ms(x: np.ndarray) -> float:
    """Time to first reach half the clip's peak envelope, in ms."""
    env = _envelope(x, 120.0)
    peak = float(np.max(env)) or 1.0
    hit = np.argmax(env >= 0.5 * peak)
    return float(hit) / SR * 1_000.0


def env_curve(x: np.ndarray, points: int = 1_500) -> np.ndarray:
    """Time-normalised amplitude envelope, for comparing variants of one sound."""
    e = _envelope(x, 22.0).astype(np.float64)
    return np.interp(np.linspace(0.0, len(e) - 1.0, points), np.arange(len(e)), e)


def env_corr(a: np.ndarray, b: np.ndarray) -> float:
    """Pearson correlation of two time-normalised envelopes.

    The distinctness test that matters. Duration and centroid deltas miss the
    cheapest failure mode — the same gesture rendered twice with a tweak — and they
    also cannot judge the two stun clips at all, since §04 pins both to exactly
    2.50 s. Envelope shape is what a player actually recognises as "that sound
    again".
    """
    ea, eb = env_curve(a), env_curve(b)
    ea = ea - ea.mean()
    eb = eb - eb.mean()
    denom = float(np.linalg.norm(ea) * np.linalg.norm(eb))
    return float(np.dot(ea, eb) / denom) if denom > 1e-12 else 1.0


def oct_levels(x: np.ndarray, per_oct: int = 6, lo: float = 45.0, hi: float = 12_000.0) -> np.ndarray:
    """Sixth-octave band levels in dB, normalised to the clip's own total energy."""
    freqs, pw = power_spectrum(x)
    total = float(np.sum(pw)) or 1.0
    edges = lo * 2.0 ** (np.arange(0, int(np.log2(hi / lo) * per_oct) + 1) / per_oct)
    out = []
    for a, b in zip(edges[:-1], edges[1:]):
        e = float(np.sum(pw[(freqs >= a) & (freqs < b)]) / total)
        out.append(10.0 * np.log10(max(e, 1e-12)))
    return np.asarray(out)


def spec_dist(a: np.ndarray, b: np.ndarray) -> float:
    """RMS difference between two clips' sixth-octave spectra, in dB."""
    la, lb = oct_levels(a), oct_levels(b)
    return float(np.sqrt(np.mean(np.square(la - lb))))


def measure(path: str) -> Feature:
    x, sr = synth.read_wav(path)
    assert sr == SR
    rms = float(np.sqrt(np.mean(np.square(x.astype(np.float64)))))
    peak = float(np.max(np.abs(x)))
    n_front = min(len(x), synth.n_samples(0.20))
    n_tail = min(len(x), synth.n_samples(0.50))
    front = float(np.sqrt(np.mean(np.square(x[:n_front].astype(np.float64)))))
    tail = float(np.sqrt(np.mean(np.square(x[-n_tail:].astype(np.float64)))))
    freqs, pw = power_spectrum(x)
    centroid_pow, rolloff = spectrum_stats(freqs, pw)
    with open(path, "rb") as fh:
        sha = hashlib.sha256(fh.read()).hexdigest()[:12]
    return Feature(
        name=os.path.basename(path),
        seconds=len(x) / SR,
        peak_db=synth.gain_to_db(peak),
        rms=rms,
        centroid_mag=synth.analyse(path).spectral_centroid,
        centroid_pow=centroid_pow,
        rolloff95=rolloff,
        crest=peak / (rms or 1e-9),
        attack_ms=attack_ms(x),
        front_ratio=front / (rms or 1e-9),
        tail_ratio=tail / (rms or 1e-9),
        mod_index=mod_index(x),
        rattle=rattle(x),
        dba_offset=a_weight_offset(freqs, pw),
        bands=band_fractions(freqs, pw),
        sha12=sha,
        data=x,
    )


@dataclass
class Seam:
    """Loop-boundary measurements."""

    name: str
    pre_step: float
    pre_jump_ratio: float
    file_step: float
    wrap_rms_ratio: float
    fade_loss_db: float
    join_ratio: float


def _jump_ratio(x: np.ndarray) -> float:
    """|wrap step| against the 99.9th percentile of the clip's own sample steps.

    The honest no-click test. A loop point is inaudible when joining the end to the
    start is no more abrupt than the signal's own fastest legitimate motion.
    """
    d = np.abs(np.diff(x.astype(np.float64)))
    body = float(np.percentile(d, 99.9)) if len(d) else 0.0
    wrap = abs(float(x[0]) - float(x[-1]))
    return wrap / (body if body > 1e-12 else 1e-12)


def _write_wav_fade_env(n: int) -> np.ndarray:
    """Reconstructs the fade `synth.write_wav` applies unconditionally.

    2 ms in, 6 ms out, built exactly as `synth.fade` builds it. Needed because the
    interesting question about a loop is not whether the *file* wraps cleanly — it
    trivially does, see `seam_metrics` — but how much real signal that mandatory
    fade destroyed.
    """
    env = np.ones(n, dtype=np.float64)
    fi = min(synth.n_samples(0.002), n // 2)
    fo = min(synth.n_samples(0.006), n // 2)
    if fi:
        env[:fi] = np.linspace(0.0, 1.0, fi, endpoint=False)
    if fo:
        env[-fo:] = np.linspace(1.0, 0.0, fo)
    return env


def seam_metrics(name: str, pre: np.ndarray, path: str) -> Seam:
    """Measures the loop boundary before and after writing.

    `file_step` is reported for completeness but is structurally vacuous, and so is
    any file-level "jump" metric: every clip goes through `synth.write_wav`, whose
    unconditional fade forces sample 0 and the last sample to exactly zero. So
    |last − first| is always 0.0 no matter how bad the loop is. A generator that
    reported only that number would be claiming a guarantee it never tested.

    The three that carry weight:

    * `pre_step` / `pre_jump_ratio` — is the *synthesised* buffer genuinely
      circular, before anything is forced? The jump ratio compares the wrap step
      against the 99.9th percentile of the signal's own sample-to-sample motion, so
      "seamless" means the join is no more abrupt than the waveform already is.
    * `wrap_rms_ratio` — does the boundary sit in a designed trough, i.e. somewhere
      the forced fade has nothing to grab?
    * `fade_loss_db` — the fraction of total clip energy that write_wav's fade
      actually removed. This is the direct answer to "is the mandatory fade audible
      at the loop point?" and it is the number to trust.
    """
    x, _ = synth.read_wav(path)
    w = synth.n_samples(0.025)
    wrap = np.concatenate([x[-w:], x[:w]])
    rms_all = float(np.sqrt(np.mean(np.square(x.astype(np.float64))))) or 1e-9
    rms_wrap = float(np.sqrt(np.mean(np.square(wrap.astype(np.float64)))))

    # Play it twice and look at the join — the test closest to actually listening to
    # the loop. Sample-level continuity says nothing about whether the *level* steps,
    # and a level step across the boundary is what a player would hear as a pulse.
    hop = synth.n_samples(0.005)
    doubled = np.concatenate([x, x]).astype(np.float64)
    frames = len(doubled) // hop
    rms_frames = np.sqrt(np.mean(
        np.square(doubled[: frames * hop].reshape(frames, hop)), axis=1))
    steps = np.abs(np.diff(rms_frames))
    join = len(x) // hop
    join_step = float(steps[min(join, len(steps) - 1)])
    normal = float(np.percentile(steps, 99.0)) if len(steps) else 0.0

    ref = np.asarray(pre, dtype=np.float64)
    env = _write_wav_fade_env(len(ref))
    lost = float(np.sum(np.square(ref) * (1.0 - np.square(env))))
    total = float(np.sum(np.square(ref))) or 1e-30

    return Seam(
        name=name,
        pre_step=abs(float(pre[0]) - float(pre[-1])),
        pre_jump_ratio=_jump_ratio(pre),
        file_step=abs(float(x[0]) - float(x[-1])),
        wrap_rms_ratio=rms_wrap / rms_all,
        fade_loss_db=10.0 * np.log10(max(lost / total, 1e-30)),
        join_ratio=join_step / (normal if normal > 1e-12 else 1e-12),
    )


# ── Build ──────────────────────────────────────────────────────────────────


def main() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)

    reports = []
    feats: dict[str, Feature] = {}
    seams: list[Seam] = []
    manifest: dict[str, object] = {}

    def emit(name: str, buf: np.ndarray, kind: str, pre_loop: np.ndarray | None = None) -> None:
        path = os.path.join(OUT_DIR, name + ".wav")
        synth.write_wav(path, buf, headroom_db=PEAK_DB[kind], stereo=False)
        report = synth.assert_usable(path, min_seconds=0.2, max_seconds=40.0)
        if report.channels != 1:
            raise AssertionError(f"{path}: {report.channels} channels — positional audio must be mono")
        reports.append(report)
        feats[name] = measure(path)
        if pre_loop is not None:
            seams.append(seam_metrics(name, pre_loop, path))

    for i in range(3):
        emit(f"monster_roar_{i + 1:02d}", roar(i), "roar")
    for i in range(3):
        emit(f"monster_growl_{i + 1:02d}", growl(i), "growl")
    for i in range(2):
        emit(f"monster_search_{i + 1:02d}", search(i), "search")
    for i in range(2):
        emit(f"monster_grab_{i + 1:02d}", grab(i), "grab")
    for i in range(2):
        emit(f"monster_stun_{i + 1:02d}", stun(i), "stun")
    for i in range(2):
        buf, _ = breath_loop(i)
        emit(f"monster_breath_loop_{i + 1:02d}", buf, "breath", pre_loop=buf)
    bed, _ = presence_bed()
    emit("monster_presence_bed", bed, "bed", pre_loop=bed)

    # ── Report ────────────────────────────────────────────────────────────
    print(f"\nOUTPUT: {OUT_DIR}\n")
    print(synth.report_table(reports))

    print("\nGAMEPLAY FEATURES")
    print("  cent_mag = synth.analyse's magnitude-weighted centroid (house convention).")
    print("  cent_pow / r95 = power-weighted centroid and 95%-energy rolloff; checks use")
    print("  these, because cent_mag is dominated by empty HF bins on low-band material.")
    print(f"{'clip':<28} {'sec':>6} {'peakdB':>7} {'crest':>6} {'atk ms':>7} "
          f"{'front':>6} {'tail':>6} {'mod':>5} {'rattle':>7} {'cent_mag':>9} "
          f"{'cent_pow':>9} {'r95':>7} {'dBA off':>8} {'loud':>7} {'sha256':>13}")
    print("-" * 152)
    for name, f in feats.items():
        print(f"{name:<28} {f.seconds:>6.3f} {f.peak_db:>7.1f} {f.crest:>6.2f} "
              f"{f.attack_ms:>7.1f} {f.front_ratio:>6.2f} {f.tail_ratio:>6.2f} "
              f"{f.mod_index:>5.2f} {f.rattle:>7.1f} {f.centroid_mag:>9.0f} "
              f"{f.centroid_pow:>9.0f} {f.rolloff95:>7.0f} {f.dba_offset:>8.1f} "
              f"{f.loudness_db:>7.1f} {f.sha12:>13}")

    print("\nBAND ENERGY (fraction of total)")
    hdr = "  ".join(f"{lo:.0f}-{hi:.0f}" for lo, hi in zip(BAND_EDGES[:-1], BAND_EDGES[1:]))
    print(f"{'clip':<28} {hdr}")
    print("-" * 106)
    for name, f in feats.items():
        cells = "  ".join(f"{b:>8.4f}" for b in f.bands)
        print(f"{name:<28} {cells}")

    print("\nLOOP SEAMS  (file_step is always exactly 0 — write_wav's fade forces it,")
    print("  so it proves nothing; pre_jump, wrap_rms and fade_loss are the real tests)")
    print(f"{'clip':<28} {'pre_step':>10} {'pre_jump':>10} {'file_step':>10} "
          f"{'wrap_rms':>9} {'fade_loss_dB':>13} {'join':>7}")
    print("-" * 94)
    for s in seams:
        print(f"{s.name:<28} {s.pre_step:>10.2e} {s.pre_jump_ratio:>10.2e} "
              f"{s.file_step:>10.2e} {s.wrap_rms_ratio:>9.4f} {s.fade_loss_db:>13.1f} "
              f"{s.join_ratio:>7.3f}")

    # ── Design checks ─────────────────────────────────────────────────────
    checks: list[tuple[str, bool, str]] = []

    def check(label: str, ok: bool, detail: str) -> None:
        checks.append((label, bool(ok), detail))

    roars = [feats[f"monster_roar_{i + 1:02d}"] for i in range(3)]
    growls = [feats[f"monster_growl_{i + 1:02d}"] for i in range(3)]
    searches = [feats[f"monster_search_{i + 1:02d}"] for i in range(2)]
    grabs = [feats[f"monster_grab_{i + 1:02d}"] for i in range(2)]
    stuns = [feats[f"monster_stun_{i + 1:02d}"] for i in range(2)]
    breaths = [feats[f"monster_breath_loop_{i + 1:02d}"] for i in range(2)]
    bed_f = feats["monster_presence_bed"]

    # §06 추격 must be unmistakable, and must not be confusable with 경계.
    worst_atk = max(r.attack_ms for r in roars)
    check("§06 roar attack < 60 ms (reaction time is distance)", worst_atk < 60.0,
          f"worst {worst_atk:.1f} ms")
    worst_front = min(r.front_ratio for r in roars)
    check("§06 roar front-loaded: first 200 ms rms >= 0.85x clip rms", worst_front >= 0.85,
          f"worst {worst_front:.2f}x")
    gap_db = min(r.peak_db for r in roars) - max(g.peak_db for g in growls)
    check("§06 roar louder than growl on the meter too (>= 3.5 dB)", gap_db >= 3.5,
          f"{gap_db:+.1f} dB")
    loud_gap = min(r.loudness_db for r in roars) - max(g.loudness_db for g in growls)
    check("§06 roar >= 6 dB louder than growl A-WEIGHTED (perceived, not metered)",
          loud_gap >= 6.0, f"{loud_gap:+.1f} dB  "
          f"roar {min(r.loudness_db for r in roars):.1f} vs growl "
          f"{max(g.loudness_db for g in growls):.1f}")
    # -12 dB is a measured physical bound, not a convenience. A closed-mouth growl
    # puts its harmonics between 47 and 250 Hz, where A-weighting costs 20-35 dB.
    # Sweeping the fundamental with everything else fixed gives -11.5 dB at 47 Hz,
    # -11.7 at 52, -11.0 at 58, -10.4 at 66, -10.1 at 74 — so the family cannot do
    # better than about -10 even at the top, and the deliberately-lowest variant
    # cannot beat -11.5 at any pitch. Getting past that would mean opening the
    # formants until 경계 started to read as 추격, which trades a real design
    # property for a number. The binding constraints are the two ordering checks
    # below; this one only catches a growl that has collapsed entirely into sub-bass.
    check("§06 alert growl is audible, given the register (A-weighted offset >= -12 dB)",
          min(g.dba_offset for g in growls) >= -12.0,
          ", ".join(f"{g.dba_offset:.1f} dB" for g in growls))
    cent_ratio = min(r.centroid_pow for r in roars) / max(g.centroid_pow for g in growls)
    check("§06 roar centroid >= 1.4x growl (mouth open vs shut)", cent_ratio >= 1.4,
          f"{cent_ratio:.2f}x")
    check("§06 growl centroid < 400 Hz (muffled, closed mouth)",
          max(g.centroid_pow for g in growls) < 400.0,
          f"max {max(g.centroid_pow for g in growls):.0f} Hz")
    rattle_gap = min(g.rattle for g in growls) / max(b.rattle for b in breaths)
    check("§06 growl has a definite rattle rate, breath does not (>= 2.0x)",
          rattle_gap >= 2.0, f"{rattle_gap:.2f}x  "
          f"growl {min(g.rattle for g in growls):.1f} vs breath {max(b.rattle for b in breaths):.1f}")

    # §04 stun must read as temporary.
    check("§04 stun length == FlashStunSeconds (clip is the timer)",
          all(abs(s.seconds - STUN_SECONDS) < 0.01 for s in stuns),
          ", ".join(f"{s.seconds:.3f}s" for s in stuns))
    worst_tail = min(s.tail_ratio for s in stuns)
    check("§01 stun does not die away: last 500 ms rms >= 0.60x clip rms",
          worst_tail >= 0.60, f"worst {worst_tail:.2f}x")

    # Grab is punctuation.
    check("grab is transient-dominated (crest >= 4.0)",
          min(g.crest for g in grabs) >= 4.0,
          ", ".join(f"{g.crest:.2f}" for g in grabs))

    # §12 the bed must not occupy the footstep-material band.
    bed_low = bed_f.bands[0] + bed_f.bands[1]
    check("§12 presence bed >= 0.95 of energy below 250 Hz (keeps footstep band clear)",
          bed_low >= 0.95, f"{bed_low:.4f}")
    check("§12 presence bed near-subsonic: power centroid < 80 Hz, r95 < 120 Hz",
          bed_f.centroid_pow < 80.0 and bed_f.rolloff95 < 120.0,
          f"centroid {bed_f.centroid_pow:.0f} Hz, r95 {bed_f.rolloff95:.0f} Hz")
    check("breath is a large airway, not a whisper (power centroid 300-1800 Hz)",
          all(300.0 < b.centroid_pow < 1_800.0 for b in breaths),
          ", ".join(f"{b.centroid_pow:.0f} Hz" for b in breaths))
    sniff_energy = [s.bands[3] + s.bands[4] for s in searches]
    check("§06 search: sniff audible — >= 0.15 of energy in 500 Hz-8 kHz",
          min(sniff_energy) >= 0.15, ", ".join(f"{e:.4f}" for e in sniff_energy))
    check("breath quieter than growl by >= 4 dB (must never mask footsteps)",
          max(b.peak_db for b in breaths) <= min(g.peak_db for g in growls) - 4.0,
          f"breath {max(b.peak_db for b in breaths):.1f} dB vs growl "
          f"{min(g.peak_db for g in growls):.1f} dB")

    # ── Perceived loudness order ──────────────────────────────────────────
    # The check that catches the mistake peak-dBFS hides. The first pass had the
    # §06 경계 growl 6 dB *below* the idle breathing loop to the ear while sitting
    # 6 dB above it on the meter, i.e. the state-change cue was quieter than the
    # ambience it was supposed to interrupt. Nothing in a peak-level table shows
    # that; an A-weighted ordering does.
    families = [("roar", roars), ("grab", grabs), ("stun", stuns), ("growl", growls),
                ("search", searches), ("breath", breaths), ("bed", [bed_f])]
    print("\nPERCEIVED LOUDNESS (A-weighted; peak dBFS in brackets)")
    print("-" * 86)
    for label, group in families:
        med = float(np.median([f.loudness_db for f in group]))
        spread = max(f.loudness_db for f in group) - min(f.loudness_db for f in group)
        print(f"  {label:<8} median {med:>7.1f} dBA   spread {spread:>4.1f} dB   "
              f"[{', '.join(f'{f.peak_db:.1f}' for f in group)}]")

    roar_loud = min(r.loudness_db for r in roars)
    other_max = max(f.loudness_db for label, grp in families
                    if label not in ("roar", "grab") for f in grp)
    check("§06 nothing (except the grab) is perceptually louder than the chase roar",
          roar_loud >= other_max + 4.0,
          f"roar {roar_loud:.1f} dBA vs next {other_max:.1f} dBA")
    check("grab sits beside the roar, not above it (within 2 dB)",
          max(g.loudness_db for g in grabs) <= max(r.loudness_db for r in roars) + 2.0,
          f"grab {max(g.loudness_db for g in grabs):.1f} vs roar "
          f"{max(r.loudness_db for r in roars):.1f} dBA")
    check("§06 alert growl is perceptually ABOVE the breathing it interrupts (>= 3 dB)",
          min(g.loudness_db for g in growls) >= max(b.loudness_db for b in breaths) + 3.0,
          f"growl {min(g.loudness_db for g in growls):.1f} vs breath "
          f"{max(b.loudness_db for b in breaths):.1f} dBA")
    # A-weighting deliberately discounts 30–50 Hz by ~30 dB, so the bed's dBA figure
    # under-reads it badly: on headphones (§05 requires them) it is very present. The
    # bed's real level target is its peak dBFS. Do not "fix" this by raising the bed.
    check("presence bed reads lowest on A-weighting, as sub-bass must (felt, not heard)",
          bed_f.loudness_db <= min(f.loudness_db for label, grp in families
                                   if label != "bed" for f in grp) - 6.0,
          f"bed {bed_f.loudness_db:.1f} dBA at {bed_f.peak_db:.1f} dBFS peak")
    for label, group in families:
        if len(group) < 2:
            continue
        spread = max(f.loudness_db for f in group) - min(f.loudness_db for f in group)
        check(f"{label} variants are equally noticeable (loudness spread <= 3.5 dB)",
              spread <= 3.5, f"{spread:.1f} dB")

    # Loops.
    for s in seams:
        check(f"loop {s.name}: synthesised buffer is circular (pre_jump <= 1.0)",
              s.pre_jump_ratio <= 1.0, f"pre_jump {s.pre_jump_ratio:.2e}, "
              f"pre_step {s.pre_step:.2e}")
        check(f"loop {s.name}: boundary in a trough (wrap_rms <= 0.25x)",
              s.wrap_rms_ratio <= 0.25, f"wrap_rms {s.wrap_rms_ratio:.4f}")
        check(f"loop {s.name}: write_wav's forced fade costs < -60 dB of energy",
              s.fade_loss_db < -60.0, f"fade_loss {s.fade_loss_db:.1f} dB")
        check(f"loop {s.name}: played twice, the join does not step in level",
              s.join_ratio <= 1.0, f"join step {s.join_ratio:.3f}x the clip's own 99th pct")

    # Variants must actually differ — a repeated sample is the fastest way to make a
    # game feel cheap, and a pitch-shifted copy of one render is the same failure
    # wearing a hat. Envelope shape carries this test because it is what a player
    # recognises; the stun pair is pinned to one duration by §04 so nothing else can.
    print("\nVARIANT DISTINCTNESS  (env_corr = envelope correlation, lower is more distinct)")
    print(f"{'pair':<58} {'d_sec':>7} {'env_corr':>9} {'spec_dB':>8}")
    print("-" * 86)
    for label, group in (("roar", roars), ("growl", growls), ("search", searches),
                         ("grab", grabs), ("stun", stuns), ("breath", breaths)):
        ok = True
        worst = ""
        for a in range(len(group)):
            for b in range(a + 1, len(group)):
                fa, fb = group[a], group[b]
                d_sec = abs(fa.seconds - fb.seconds)
                corr = env_corr(fa.data, fb.data)
                dist = spec_dist(fa.data, fb.data)
                print(f"{fa.name + ' / ' + fb.name:<58} {d_sec:>7.3f} {corr:>9.3f} {dist:>8.2f}")
                pair_ok = (corr <= 0.85) or (dist >= 3.0)
                if not pair_ok:
                    ok = False
                    worst = f"{fa.name}/{fb.name} env_corr {corr:.3f} spec {dist:.2f} dB"
        check(f"{label} variants are distinct (env_corr <= 0.85 or spectra differ >= 3 dB)",
              ok, worst or "all pairs differ")

    # §06's silence, enforced rather than merely documented. If someone later decides
    # the missing 정지 sound is a gap and drops a clip in this folder, the build fails
    # here and they have to read why. A comment in a docstring would not have stopped
    # them; this will.
    strays = sorted(
        f for f in os.listdir(OUT_DIR)
        if f.lower().endswith(".wav") and any(
            token in f.lower() for token in ("standstill", "still", "idle", "listen", "정지")
        )
    )
    check("§06 NO Standstill clip exists — 침묵이 가장 무서운 소리다 (see module docstring)",
          not strays, "none present" if not strays else f"FOUND: {', '.join(strays)}")

    expected = {f"{n}.wav" for n in feats}
    unexpected = sorted(
        f for f in os.listdir(OUT_DIR) if f.lower().endswith(".wav") and f not in expected
    )
    check("no orphan wavs in the output folder (stale renders would ship)",
          not unexpected, "clean" if not unexpected else f"FOUND: {', '.join(unexpected)}")

    check("all files unique (no accidental duplicate render)",
          len({f.sha12 for f in feats.values()}) == len(feats),
          f"{len({f.sha12 for f in feats.values()})} distinct of {len(feats)}")
    check("all clips mono (Unity will not spatialise stereo)",
          all(r.channels == 1 for r in reports), "channels all 1")

    print("\nDESIGN CHECKS")
    print("-" * 106)
    for label, ok, detail in checks:
        print(f"  [{'PASS' if ok else 'FAIL'}] {label:<70} {detail}")

    # ── Manifest for the Audio layer ──────────────────────────────────────
    manifest = {
        "generator": "tools/audio/gen_monster_audio.py",
        "sample_rate": SR,
        "channels": 1,
        "states": {
            "Patrol": {"clips": [], "note": "§06 순찰 uses footsteps only — owned by the footstep generator."},
            "Alert": {"clips": [f"monster_growl_{i + 1:02d}.wav" for i in range(3)]},
            "Chase": {"clips": [f"monster_roar_{i + 1:02d}.wav" for i in range(3)]},
            "Search": {"clips": [f"monster_search_{i + 1:02d}.wav" for i in range(2)]},
            "Standstill": {
                "clips": [],
                "note": (
                    "§06: 소리 없음. Intentionally silent — 침묵이 가장 무서운 소리다. "
                    "Do not add a clip here. The Listener losing the monster is the mechanic."
                ),
            },
        },
        "proximity": {
            "breath": [f"monster_breath_loop_{i + 1:02d}.wav" for i in range(2)],
            "bed": "monster_presence_bed.wav",
            "note": (
                "Crossfade by distance only, never by monster state — a state-gated bed "
                "would leak the position §06's Standstill hides."
            ),
        },
        "events": {
            "Grab": [f"monster_grab_{i + 1:02d}.wav" for i in range(2)],
            "FlashStun": {
                "clips": [f"monster_stun_{i + 1:02d}.wav" for i in range(2)],
                "seconds": STUN_SECONDS,
                "note": "Length mirrors GameConstants.FlashStunSeconds (§04). Regenerate if it changes.",
            },
        },
        "loops": [f"monster_breath_loop_{i + 1:02d}.wav" for i in range(2)] + ["monster_presence_bed.wav"],
        "peak_db": PEAK_DB,
        "clips": {
            f.name: {"seconds": round(f.seconds, 4), "peak_db": round(f.peak_db, 2),
                     "centroid_hz": round(f.centroid_pow, 1), "sha256_12": f.sha12}
            for f in feats.values()
        },
    }
    man_path = os.path.join(OUT_DIR, "monster_audio.manifest.json")
    with open(man_path, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2, ensure_ascii=False, sort_keys=True)
        fh.write("\n")
    print(f"\nmanifest: {man_path}")

    failed = [c for c in checks if not c[1]]
    print(f"\n{len(feats)} clips written, {len(checks) - len(failed)}/{len(checks)} design checks passed.")
    if failed:
        raise AssertionError("design checks failed: " + "; ".join(c[0] for c in failed))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
