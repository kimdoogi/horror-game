"""Zone ambience beds, the surface, and the positional water/creak one-shots.

Everything here is synthesised. Nothing is sampled or downloaded — see synth.py's
header for why that matters for a Steam release.

WHAT EACH FILE IS FOR, in gameplay terms
════════════════════════════════════════

Zone beds — `amb_zone_{a,b,c,d}_*_loop.wav`, `amb_stairwell_metal_loop.wav`
    §12 turns floor material into a *gameplay channel*: the Listener (청음사)
    locates the monster by which surface its footsteps land on, and §12 says so
    explicitly — "바닥 재질이 지도다 … 아트 결정이 아니라 시스템 결정이다".
    That only works if every player already knows which zone they are standing
    in, which is what the bed is for. It carries the zone's identity before
    anybody takes a step, and it has to do so in the dark with no UI (§05: the
    game is first person and dark, headphones required).

    Per §12's zone table:
      A 나무 (wood)        creaking structure, dry — almost no tail
      B 타일 (tile)        §12's 개방 공간, the hall with 시야 20m — reverberant
      C 자갈 (gravel)      loose, shifting, dripping
      D 콘크리트 (concrete) dead, low rumble — this is the 출구 zone
      계단 금속 (stairs)    metal, resonant, with the sense of vertical space

Surface / vehicle — `amb_surface_vehicle_loop.wav`
    §08 makes the 지상 차량 the safe zone, the shop and the 보급소. This is the
    only bed in the game whose job is *relief*: warm, open air, a generator
    idling, no threat. It is the contrast that makes the other five oppressive.
    It is also where §07's clock is legible — 시각은 지상에서만 알 수 있다.

Generator hum — `amb_generator_hum_loop.wav`
    §03 makes the surface generator the battery source, and 배터리가 떨어지면
    단서를 읽을 수 없다 — darkness is the lock on progress. This is the sound the
    player walks *toward* when the flashlight is dying, so it is a positional
    point source, not part of the bed.

Water drips — `sfx_water_drip_0{1..4}.wav`
    §03's worked example of a clue is literally "그것은 물이 있는 층에 있다", so
    running water is diegetic information, not decoration. A player who hears
    dripping and remembers which floor they heard it on has solved half a clue.
    Positional, so the Listener's triangulation (§05: 몸을 돌려 삼각측량) works
    on them too.

Distant structural creaks — `sfx_creak_distant_0{1..5}.wav`
    Random placement, to keep the building alive during §06's 정지 state. When
    the monster stops it makes no sound at all, and §06 calls that the game's
    weapon — 침묵이 가장 무서운 소리다. That silence only reads as *ominous*
    rather than as *the audio dropped out* if the building itself is still
    audible. These are what remains when the footsteps stop, and they are also
    the false alarms: 어디 갔어? 방금 여기 있었는데.

Tension beds — `amb_tension_t{1..5}_*_loop.wav`
    §07 makes time the only currency and lists five threat stages (초저녁 / 밤 /
    심야 / 새벽 / 동트기 전) with the monster accelerating 4.4 → 5.2 m/s. §07 also
    says 안에서는 시간 감각이 없다: underground the player cannot read the clock.
    These beds *are* the clock. The game crossfades them as the tier advances so
    the pressure curve is felt continuously rather than read off a number. All
    five are built on one root (45 Hz) so a crossfade never sounds like a key
    change, and the loudness ladder is a measured 3 dB per tier.

CHANNEL POLICY — deliberate, per clip
═════════════════════════════════════
STEREO (11 files): the zone beds, the stairwell, the surface, and the five
    tension beds. None of these is a point source — they are 2D layers that play
    at fixed volume regardless of where the player looks — so a real stereo image
    costs nothing and buys the enclosing-space feeling that §05's mandatory
    headphones exist to deliver.
MONO (10 files): the four drips, the five creaks, and the generator hum. Every
    one of these is a thing at a place in the world. Unity will not spatialise a
    stereo clip, and both the Listener and §13's proximity voice depend on 3D
    attenuation working, so these must stay mono.

TWO DELIBERATE DEVIATIONS FROM THE HOUSE WRITER — both measured, not assumed
═══════════════════════════════════════════════════════════════════════════
`synth.write_wav` is used for the nine one-shots, where it is exactly right. The
twelve loops are written by `_write_loop_wav` below, for two reasons that were
verified against synth.py rather than guessed:

1. `write_wav` applies `fade(out, 0.002, 0.006)` to every buffer, which sets the
   first and last sample to exactly zero — an 8 ms hole punched precisely at the
   loop point. That is correct for a one-shot and fatal for a loop, whose whole
   contract is that the last sample flows into the first.
2. `write_wav(stereo=True)` duplicates one mono buffer into both channels. That
   is dual-mono: double the bytes, no image.

Everything else follows synth's conventions exactly — 48 kHz, 16-bit PCM,
peak-normalised with headroom, float32 in [-1,1] until the write — and every
file, loop or not, is checked with `synth.assert_usable`.

`synth.pink` / `synth.brown` are also not used for loop material. Both are
aperiodic by construction (Voss rows that hold one value across the buffer; a
cumulative sum), so a loop built from them steps at the seam — measured at 3.5x
the 99th-percentile interior step for `brown`. `_loop_noise` below is the
circular equivalent.

LEVEL CONVENTIONS
═════════════════
Beds are targeted by RMS, because they are continuous and what matters is how
loud they sit under everything else. Positional one-shots are targeted by peak,
matching synth's DEFAULT_HEADROOM_DB convention. Sounds are heard in a dark,
quiet mix, so all of it is restrained: no bed exceeds -24 dBFS RMS, and the beds
leave 15+ dB of peak headroom so several can overlap with footsteps on top.

Run:
    /Users/doogi/horror-game/tools/audio/.venv/bin/python \
        /Users/doogi/horror-game/tools/audio/gen_ambience.py
"""

from __future__ import annotations

import hashlib
import math
import os
import wave
from dataclasses import dataclass
from typing import Callable, Sequence

import numpy as np
from scipy.ndimage import uniform_filter1d

import synth as S

# ── Output ──────────────────────────────────────────────────────────────────

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "unity", "HorrorGame", "Assets", "Audio", "Ambience",
)

SR = S.SAMPLE_RATE

LOOP_SECONDS = 30.0
"""Zone, surface and tension beds. Long enough that the ear stops predicting it."""

HUM_LOOP_SECONDS = 12.0
"""The generator hum is steady by nature — it has no structure a longer loop
would reveal, so a shorter one saves memory on a clip that plays continuously."""

# Mirrors the fade synth.write_wav hard-codes. Only used to *document* what the
# loop writer deliberately does not do; if these drift the seam metrics below
# will still measure the truth, since they read the written file back.
WRITE_FADE_IN = 0.002
WRITE_FADE_OUT = 0.006

# ── Design levels ───────────────────────────────────────────────────────────

ZONE_RMS_DB = -31.0
"""All five §12 surfaces sit at one level: the player is in exactly one zone at a
time, so an equal bed level means a zone change reads as a change of *character*
rather than a change of volume."""

SURFACE_RMS_DB = -28.0
"""§08's vehicle is relief. 3 dB more present than the zones, which is most of
why arriving there feels like exhaling."""

TENSION_RMS_DB = (-37.0, -34.0, -31.0, -28.0, -25.0)
"""§07's five stages as an exact 3 dB ladder. The monster goes 4.4 → 5.2 m/s
across them; the bed has to move too, and level is the one axis nobody can
misread.

Sited one dB lower than first drafted. At -24 dBFS the top tier's 12.6 dB crest
pushed its peak into BED_CEILING_DB, which clamped the gain and flattened the
last step to +2.4 dB — the tier meant to feel worst arrived softer than designed.
Lowering the whole ladder keeps every step exactly 3 dB and keeps the headroom
guard intact, and a quieter tension bed is the right call anyway: §06 makes
silence the weapon (침묵이 가장 무서운 소리다), so this layer must never be the
loudest thing playing."""

DRIP_PEAK_DB = -9.0
"""Below a footstep (synth's positional default is -3) because a drip is a hint,
not an event — but sharp enough to notice in a dark, quiet mix."""

CREAK_PEAK_DB = -12.0
"""Distant by definition. Quieter still, and rolled off up top."""

HUM_PEAK_DB = -12.0
"""Positional and continuous. Steady sounds sit well under transients."""

BED_CEILING_DB = -12.0
"""No bed's peak may exceed this. A bed that peaks near full scale leaves nothing
for the footstep the Listener is trying to hear."""

# Internal gain staging, all pre-normalisation and therefore unitless.
BED_RMS = 0.05
EVENT_CREST = 4.0
"""Bed events (creaks, drips, taps) peak at 4x the steady bed's RMS — 12 dB of
crest. Holding this constant across beds is what makes an RMS target produce a
predictable peak, so the beds stay comparable to each other."""


# ── Loop-safe primitives ────────────────────────────────────────────────────
#
# A 30 s loop is a circle, not a line. Anything periodic must complete a whole
# number of cycles across the buffer, and any filter must be applied as if the
# buffer repeated forever. These helpers enforce both.


def _q(freq: float, loop_s: float = LOOP_SECONDS) -> float:
    """Quantises a frequency so it completes whole cycles across the loop.

    An oscillator at an unquantised frequency is mid-cycle at the loop point and
    steps when it wraps. Rounding to the nearest multiple of 1/loop_s costs at
    most 0.017 Hz of accuracy at 30 s, which nothing can hear.
    """
    return max(1.0 / loop_s, round(freq * loop_s) / loop_s)


def _loop_noise(seconds: float, seed: int, slope_db_oct: float = 0.0,
                hp_hz: float = 16.0, sr: int = SR) -> np.ndarray:
    """Spectrally tilted noise that is exactly periodic over `seconds`.

    `slope_db_oct`: 0 = white, -3 = pink, -6 = brown.

    This exists instead of synth.pink / synth.brown because neither is periodic.
    Voss-McCartney's slowest rows hold a single value across the whole buffer and
    brown noise is a cumulative sum, so both leave low-frequency drift that does
    not join up — measured at 3.5x the 99th-percentile interior step for brown,
    which is an audible click every 30 seconds. Tilting white noise in the
    frequency domain is circular by construction, because a multiply on the DFT
    *is* a circular convolution.

    The DC bin is zeroed outright and a smooth second-order shape rolls off below
    `hp_hz`, for the reason synth's DC_BLOCK_HZ exists: sub-audible energy eats
    headroom and offsets the waveform without ever being heard.
    """
    n = S.n_samples(seconds, sr)
    spec = np.fft.rfft(S.white(seconds, seed, sr).astype(np.float64))
    f = np.fft.rfftfreq(n, 1.0 / sr)

    gain = np.zeros_like(f)
    nz = f > 0.0
    # 10^(slope*log2(f/ref)/20) == (f/ref)^(slope/6.0206)
    gain[nz] = (f[nz] / 1000.0) ** (slope_db_oct / 6.0206)
    ratio = np.zeros_like(f)
    ratio[nz] = (f[nz] / hp_hz) ** 2
    gain *= ratio / (1.0 + ratio)

    return S.normalize(np.fft.irfft(spec * gain, n).astype(np.float32), 0.0)


def _noise_pair(seconds: float, seed: int, slope_db_oct: float = 0.0,
                corr: float = 0.5, sr: int = SR) -> tuple[np.ndarray, np.ndarray]:
    """A stereo noise pair with a chosen L/R correlation.

    Fully independent channels are unnaturally wide — real rooms are partly
    correlated because both ears hear the same sources. `corr` 1.0 is mono,
    0.0 is fully decorrelated; the beds mostly sit around 0.35-0.6.
    """
    common = _loop_noise(seconds, seed, slope_db_oct, sr=sr)
    a = _loop_noise(seconds, seed + 101, slope_db_oct, sr=sr)
    b = _loop_noise(seconds, seed + 202, slope_db_oct, sr=sr)
    c = math.sqrt(min(max(corr, 0.0), 1.0))
    s = math.sqrt(1.0 - min(max(corr, 0.0), 1.0))
    return (c * common + s * a).astype(np.float32), (c * common + s * b).astype(np.float32)


def _circ(x: np.ndarray, fn: Callable[[np.ndarray], np.ndarray],
          pad_s: float = 1.25, sr: int = SR) -> np.ndarray:
    """Applies a filter as if the buffer looped forever.

    synth's Butterworth helpers use filtfilt, which is a *linear* operation with
    edge handling — feed it a 30 s buffer and the first and last few milliseconds
    are wrong for a loop. Wrap-padding with the buffer's own tail and head makes
    the input genuinely periodic, so cropping the middle back out yields the
    circular result.
    """
    p = min(S.n_samples(pad_s, sr), len(x))
    padded = np.concatenate([x[-p:], x, x[:p]])
    out = fn(padded)
    if len(out) != len(padded):
        raise AssertionError("_circ requires a length-preserving filter")
    return out[p:p + len(x)].astype(np.float32)


def _circ_lp(x, cutoff, order=4): return _circ(x, lambda b: S.lowpass(b, cutoff, order))
def _circ_hp(x, cutoff, order=4): return _circ(x, lambda b: S.highpass(b, cutoff, order))
def _circ_bp(x, lo, hi, order=4): return _circ(x, lambda b: S.bandpass(b, lo, hi, order))
def _circ_res(x, freq, q=30.0, pad_s=2.5):
    """Circular resonator. The pad is generous because a high-Q peak rings for
    Q/(pi*f) seconds — the stairwell's Q=90 at 137 Hz needs well over a second
    of history before its output is the one a looping source would produce."""
    return _circ(x, lambda b: S.resonator(b, freq, q), pad_s=pad_s)


def _circ_dc_block(x: np.ndarray, fc: float = 20.0, sr: int = SR) -> np.ndarray:
    """Removes DC and sub-audible energy from a finished loop.

    synth.highpass is filtfilt, so applying it to a completed loop rewrites the
    exact samples that have to join up — which is how the surface bed first
    failed the seam check. Shaping on the DFT is circular by construction: the DC
    bin is zeroed outright and a smooth second-order slope rolls off below `fc`,
    which also guarantees the near-zero DC offset assert_usable insists on.
    """
    n = len(x)
    spec = np.fft.rfft(x.astype(np.float64))
    f = np.fft.rfftfreq(n, 1.0 / sr)
    ratio = np.zeros_like(f)
    nz = f > 0.0
    ratio[nz] = (f[nz] / fc) ** 2
    return np.fft.irfft(spec * (ratio / (1.0 + ratio)), n).astype(np.float32)


def _circ_reverb(x: np.ndarray, seconds: float, mix: float, seed: int,
                 damping: float, sr: int = SR) -> np.ndarray:
    """synth.reverb, but with the tail wrapped back to the head.

    synth.reverb is causal and truncates to the input length, so a loop would
    start with no tail and end with its tail cut off — the loop point would be
    the one moment the room stops existing. Pre-rolling with the buffer's own
    tail hands the convolution exactly the history a looping source would.
    """
    pad = min(S.n_samples(seconds + 0.25, sr), len(x))
    wet = S.reverb(np.concatenate([x[-pad:], x]), seconds=seconds, mix=mix,
                   seed=seed, damping=damping, sr=sr)
    return wet[pad:].astype(np.float32)


def _circ_comb(x: np.ndarray, delay_s: float, feedback: float,
               pad_s: float = 0.5, sr: int = SR) -> np.ndarray:
    """synth.comb with a circular pre-roll. Metallic flutter that survives the seam."""
    pad = min(S.n_samples(pad_s, sr), len(x))
    out = S.comb(np.concatenate([x[-pad:], x]), delay_s, feedback, sr=sr)
    return out[pad:].astype(np.float32)


def _lfo(seconds: float, rate: float, depth: float, phase: float = 0.0,
         sr: int = SR) -> np.ndarray:
    """A periodic gain LFO in [1-depth, 1], with a phase offset.

    synth.tremolo covers the no-phase case; counter-phased LFOs are what make air
    seem to *move* through a space rather than merely pulse, which the stairwell
    and the surface both need. `rate` must be quantised with _q().
    """
    t = S.t_axis(seconds, sr)
    return (1.0 - depth * 0.5 * (1.0 - np.cos(2.0 * np.pi * rate * t + phase))).astype(np.float32)


def _place_wrap(canvas: np.ndarray, buf: np.ndarray, at_s: float,
                gain: float = 1.0, sr: int = SR) -> np.ndarray:
    """Adds an event into a loop, wrapping its tail to the head.

    synth.place clips at the canvas end, which for a loop means every event near
    the seam loses its decay. Wrapping is what a looping source actually does.
    """
    n = len(canvas)
    if len(buf) > n:
        raise AssertionError("event longer than the loop")
    start = S.n_samples(at_s, sr) % n if at_s > 0 else 0
    first = min(len(buf), n - start)
    canvas[start:start + first] += buf[:first] * gain
    if first < len(buf):
        rest = buf[first:]
        canvas[:len(rest)] += rest * gain
    return canvas


BASS_MONO_HZ = 150.0
"""Crossover below which a bed's two channels are made identical.

Independent reverb IRs per channel are what make a space feel like it surrounds
you, but they decorrelate the low end along with everything else — measured L/R
correlation below 150 Hz came out at 0.11 for the tiled hall and 0.13 for the
stairwell. Decorrelated bass has no defined position, so on headphones it smears
into a phasey wash instead of sitting still underneath the sound. Since §05 makes
headphones mandatory, that is not a defect worth shipping.
"""


def _mono_bass(l: np.ndarray, r: np.ndarray, fc: float = BASS_MONO_HZ) -> tuple[np.ndarray, np.ndarray]:
    """Collapses a stereo bed's low end to a shared mono channel.

    Written as `x - lowpass(x) + lowpass(mid)` rather than as a highpass plus a
    lowpass: a complementary pair sums back to unity by construction, so the
    crossover cannot leave the level dip that two Butterworth sections would.
    Every filter here is circular, so a seamless loop stays seamless.
    """
    mid = (0.5 * (l + r)).astype(np.float32)
    shared = _circ_lp(mid, fc)
    return ((l - _circ_lp(l, fc) + shared).astype(np.float32),
            (r - _circ_lp(r, fc) + shared).astype(np.float32))


def _pan(buf: np.ndarray, pan: float) -> tuple[np.ndarray, np.ndarray]:
    """Constant-power pan. -1 hard left, 0 centre, +1 hard right."""
    a = (min(max(pan, -1.0), 1.0) + 1.0) * math.pi / 4.0
    return (buf * math.cos(a)).astype(np.float32), (buf * math.sin(a)).astype(np.float32)


# ── Levels ──────────────────────────────────────────────────────────────────


def _rms(x: np.ndarray) -> float:
    return float(np.sqrt(np.mean(np.square(x.astype(np.float64))))) if len(x) else 0.0


def _at_rms(x: np.ndarray, target: float) -> np.ndarray:
    """Scales a layer to a target RMS, so bed layers can be balanced by loudness
    rather than by peak — peak says nothing about how loud a noise bed sounds."""
    r = _rms(x)
    return x.astype(np.float32) if r < 1e-12 else (x * (target / r)).astype(np.float32)


def _at_peak(x: np.ndarray, target: float) -> np.ndarray:
    p = float(np.max(np.abs(x))) if len(x) else 0.0
    return x.astype(np.float32) if p < 1e-12 else (x * (target / p)).astype(np.float32)


# ── Sound builders ──────────────────────────────────────────────────────────


def _swept_impulse_train(seconds: float, seed: int, rate0: float, rate1: float,
                         jitter: float = 0.4, sr: int = SR) -> np.ndarray:
    """An impulse train whose rate sweeps from rate0 to rate1.

    This is the mechanism behind a creak. Wood under load does not slide, it
    grips and releases hundreds of times a second — stick-slip — and the release
    rate drifts as the load shifts. That drift is the whole character of §12's
    삐걱; a fixed rate reads as a buzz.
    """
    n = S.n_samples(seconds, sr)
    t = S.t_axis(seconds, sr).astype(np.float64)
    if abs(rate1 - rate0) < 1e-9:
        phase = rate0 * t
    else:
        k = math.log(rate1 / rate0) / seconds
        phase = rate0 * (np.exp(k * t) - 1.0) / k

    g = S.rng(seed)
    out = np.zeros(n, dtype=np.float32)
    idx = np.searchsorted(phase, np.arange(1.0, float(phase[-1])))
    for i in idx:
        if 0 <= i < n:
            out[i] += float(1.0 - jitter * g.random())
    return out


def _creak(seconds: float, seed: int, modes: Sequence[tuple[float, float, float]],
           rate0: float, rate1: float, attack: float, band: tuple[float, float],
           sr: int = SR) -> np.ndarray:
    """A stick-slip creak: a swept impulse train through fixed body resonances.

    The resonances are fixed because a beam's modes are fixed; only the slip rate
    moves. Getting that division right is what separates a creak from a chirp.
    """
    train = _swept_impulse_train(seconds, seed, rate0, rate1, sr=sr)
    body = np.zeros(len(train), dtype=np.float64)
    for f, amp, q in modes:
        body += S.resonator(train, f, q=q, sr=sr).astype(np.float64) * amp
    shaped = S.bandpass(body.astype(np.float32), band[0], band[1], sr=sr)
    env = S.adsr(seconds, attack=attack, decay=seconds * 0.28,
                 sustain=0.42, release=seconds * 0.45, sr=sr)
    return S.normalize(shaped * env, 0.0)


DRIP_LEAD_S = 0.004
"""Silence before a drip's transient.

write_wav normalises and *then* fades 2 ms in, and a drip peaks inside its first
millisecond — so without a lead-in the fade lands squarely on the attack, costing
up to 6 dB of peak and blunting the one feature that makes the sound read as
water. Measured before this existed: -15.3 dBFS against a -9.0 dBFS request.
4 ms of leading silence puts the transient safely past the fade and is far too
short to be heard as latency.
"""


def _drip(seed: int, f0: float, f1: float, tau: float, seconds: float,
          click_db: float, pool_hz: float | None = None,
          echo_at: float | None = None, sr: int = SR) -> np.ndarray:
    """One water drip.

    A drip is not an impact. What you hear is the bubble the droplet entrains
    oscillating as it collapses, and because the bubble shrinks the pitch *rises*
    — that upward chirp is the entire tell. A flat-pitched blip sounds like a
    woodblock. synth.sweep does the chirp; the exponential decay does the rest.

    Dry on purpose. synth.reverb's own docstring says not to bake space into a
    positional clip, and these are positional: §03's clue "그것은 물이 있는 층에
    있다" only works if the player can tell *where* the water is.
    """
    # The chirp has to finish while the drip is still audible.
    #
    # Sweeping f0->f1 across the whole buffer reads correctly on paper and is
    # inaudible in practice: with tau = 34 ms in a 240 ms buffer the pitch had
    # risen 5% by the time the envelope was already 70 dB down. Measured
    # dominant frequency over the drip's first two 20 ms windows: 1200 Hz then
    # 1200 Hz — no glissando at all, which leaves a woodblock, not water. So
    # extend the endpoint such that the sweep reaches the intended f1 at ~3.5
    # tau, i.e. inside the part anybody can hear. It keeps rising past that, but
    # by then there is nothing left to rise.
    chirp_s = min(seconds, 3.5 * tau)
    f1_ext = min(f0 * (f1 / f0) ** (seconds / chirp_s), 0.45 * sr)
    body = S.sweep(f0, f1_ext, seconds, log=True, sr=sr) * S.exp_decay(seconds, tau, sr)

    click = S.white(seconds, seed + 31, sr) * S.exp_decay(seconds, 0.0018, sr)
    click = S.bandpass(click, max(700.0, f0 * 0.8), min(14000.0, f1 * 7.0), sr=sr)

    layers = [body.astype(np.float64), click.astype(np.float64) * S.db_to_gain(click_db)]

    if pool_hz is not None:
        # The body of water the drop lands in, not the drop. Dull and short.
        pool = S.sine(pool_hz, seconds, sr=sr) * S.exp_decay(seconds, tau * 2.4, sr)
        layers.append(S.lowpass(pool, pool_hz * 3.0, sr=sr).astype(np.float64) * 0.30)

    out = np.zeros(S.n_samples(seconds, sr), dtype=np.float32)
    for lay in layers:
        out += lay.astype(np.float32)

    if echo_at is not None:
        # A second, smaller droplet off the same drip — irregular water, not a
        # metronome. Two clips in a variant set that differ only in pitch still
        # read as one sound; a different *rhythm* is what breaks the pattern.
        s_dur, s_tau = seconds * 0.6, tau * 0.8
        s_f1 = min(f0 * 0.86 * (f1 / f0) ** (s_dur / min(s_dur, 3.5 * s_tau)), 0.45 * sr)
        second = S.sweep(f0 * 0.86, s_f1, s_dur, log=True, sr=sr) \
            * S.exp_decay(s_dur, s_tau, sr)
        S.place(out, second.astype(np.float32) * 0.55, echo_at, sr=sr)

    out = S.normalize(S.highpass(out, 120.0, sr=sr), 0.0)
    return S.concat(S.silence(DRIP_LEAD_S, sr), out)


def _grain_layer(seconds: float, seed: int, count: int, bands: Sequence[tuple[float, float]],
                 sr: int = SR) -> np.ndarray:
    """Loose granular texture — §12's 자갈, 부스럭.

    Gravel is not a noise band, it is thousands of tiny independent contacts. A
    handful of prototype grains placed at wrapped random times gives that, and
    keeps the loop circular because every grain's tail wraps with it.
    """
    g = S.rng(seed)
    prototypes = []
    for i, (lo, hi) in enumerate(bands):
        dur = 0.0012 + 0.0028 * (i / max(1, len(bands) - 1))
        grain = S.white(dur, seed + 977 + i, sr) * S.exp_decay(dur, dur * 0.35, sr)
        prototypes.append(S.normalize(S.bandpass(grain, lo, hi, sr=sr), 0.0))

    out = np.zeros(S.n_samples(seconds, sr), dtype=np.float32)
    times = np.sort(g.uniform(0.0, seconds, count))
    picks = g.integers(0, len(prototypes), count)
    gains = g.uniform(0.10, 0.85, count) ** 1.7  # mostly faint, a few close by
    for t, p, gn in zip(times, picks, gains):
        _place_wrap(out, prototypes[int(p)], float(t), float(gn), sr=sr)
    return out


def _metal_tap(seed: int, seconds: float, modes: Sequence[S.Mode],
               noise: float = 0.3, sr: int = SR) -> np.ndarray:
    """A struck-metal tap, via synth.modal_impact. Inharmonic modes = metal."""
    return S.modal_impact(modes, seconds, seed, noise_amount=noise, noise_tau=0.006, sr=sr)


# ── Zone beds (§12) ─────────────────────────────────────────────────────────
#
# Each returns (left, right) at LOOP_SECONDS. Reverb is where a zone's identity
# lives, so it differs per zone rather than being a global polish pass: §12 gives
# B a tiled hall and D dead concrete, and that has to be audible.

REVERB_SECONDS_TO_RT60 = 1.727
"""synth.reverb's `seconds` is the IR *length*, not the reverberation time.

Its envelope is exp(-4n / (seconds*sr)), so the amplitude time constant is
seconds/4 and RT60 = ln(1000) * seconds/4 = 1.727 * seconds. Passing
`seconds=3.0` for "a 3 second hall" actually asks for RT60 5.1 s — measured at
5.11 s on the first run, which is cathedral, not §12's tiled hall. The values
below are therefore chosen as RT60 targets and divided through.
"""


def _rt(rt60: float) -> float:
    """Converts a wanted RT60 into synth.reverb's `seconds` argument."""
    return rt60 / REVERB_SECONDS_TO_RT60


ZONE_SPACES = {
    # name:            (reverb seconds,  mix,  damping, ir_seed)   ← wanted RT60
    "zone_a_wood":     (_rt(0.45), 0.09, 2500.0, 4101),   # dry, subdivided, absorbent
    "zone_b_tile":     (_rt(2.25), 0.45, 3400.0, 4202),   # §12's 개방 공간: hard tiled hall
    "zone_c_gravel":   (_rt(1.30), 0.28, 2000.0, 4303),   # gravel and water both absorb
    "zone_d_concrete": (_rt(0.75), 0.13, 900.0, 4404),    # 둔탁: short and very dark
    "stairwell_metal": (_rt(3.10), 0.42, 6500.0, 4505),   # a tall shaft rings longest
    "surface_vehicle": (_rt(0.28), 0.07, 2200.0, 4606),   # outdoors: only the vehicle body
}


def _space(l: np.ndarray, r: np.ndarray, key: str) -> tuple[np.ndarray, np.ndarray]:
    """Applies a zone's room. Independent IR seeds per channel — decorrelated
    tails are most of what makes a space feel like it surrounds you."""
    rt, mix, damp, seed = ZONE_SPACES[key]
    return (_circ_reverb(l, rt, mix, seed, damp), _circ_reverb(r, rt, mix, seed + 7, damp))


def zone_a_wood() -> tuple[np.ndarray, np.ndarray]:
    """A 나무 — creaking structure, dry.

    Dry is the point. Wood absorbs and the zone is subdivided, so there is almost
    no tail; what fills the space instead is the structure working. §12 gives this
    zone 삐걱, and the creaks here are the same mechanism as the standalone
    one-shots, just closer and quieter.
    """
    n = S.n_samples(LOOP_SECONDS)
    lo_l, lo_r = _noise_pair(LOOP_SECONDS, 1001, -6.0, corr=0.80)
    mid_l, mid_r = _noise_pair(LOOP_SECONDS, 1011, -3.0, corr=0.45)

    def bed(lo, mid, ph):
        # A joist under load: a slow breathing weight, not a drone.
        low = _at_rms(_circ_lp(lo, 220.0), BED_RMS * 0.62) * _lfo(LOOP_SECONDS, _q(0.0667), 0.45, ph)
        # Dry air in a boarded-up room. Rolled off early — no glare.
        air = _at_rms(_circ_bp(mid, 260.0, 1900.0), BED_RMS * 0.50) * _lfo(LOOP_SECONDS, _q(0.1), 0.25, ph + 1.9)
        return (low + air).astype(np.float32)

    left, right = bed(lo_l, mid_l, 0.0), bed(lo_r, mid_r, math.pi * 0.7)
    ev = EVENT_CREST * _rms(left)

    # Six creaks and a scatter of settling ticks across 30 s. Sparse: the zone
    # should sound like it is holding still, occasionally failing to.
    creaks = [
        (2.6, 1.35, ((178.0, 1.0, 34.0), (297.0, 0.55, 26.0), (521.0, 0.22, 20.0)), 44.0, 26.0, -0.55),
        (9.1, 0.95, ((214.0, 1.0, 30.0), (356.0, 0.48, 24.0), (612.0, 0.18, 18.0)), 58.0, 33.0, 0.62),
        (14.7, 1.80, ((151.0, 1.0, 38.0), (263.0, 0.60, 28.0), (447.0, 0.20, 22.0)), 36.0, 21.0, 0.18),
        (19.4, 0.72, ((248.0, 1.0, 27.0), (409.0, 0.42, 21.0), (703.0, 0.15, 17.0)), 71.0, 41.0, -0.74),
        (24.2, 1.15, ((193.0, 1.0, 32.0), (318.0, 0.52, 25.0), (556.0, 0.19, 19.0)), 50.0, 29.0, 0.44),
        (28.9, 1.55, ((166.0, 1.0, 36.0), (281.0, 0.57, 27.0), (486.0, 0.21, 21.0)), 40.0, 24.0, -0.30),
    ]
    for i, (at, dur, modes, r0, r1, pan) in enumerate(creaks):
        c = _creak(dur, 1200 + i * 17, modes, r0, r1, attack=dur * 0.22, band=(90.0, 3200.0))
        cl, cr = _pan(c, pan)
        _place_wrap(left, cl, at, ev * 0.80)
        _place_wrap(right, cr, at, ev * 0.80)

    g = S.rng(1300)
    for i in range(11):
        # Nail-in-board ticks. Wood modes, fast decay, no ring.
        tick = _metal_tap(1400 + i, 0.09, [S.Mode(_q(340.0 + 260.0 * g.random()), 0.014, 1.0),
                                           S.Mode(_q(880.0 + 520.0 * g.random()), 0.007, 0.4)],
                          noise=0.55)
        tick = S.lowpass(tick, 3600.0)
        tl, tr = _pan(tick, float(g.uniform(-0.85, 0.85)))
        at = float(g.uniform(0.0, LOOP_SECONDS))
        gn = ev * float(g.uniform(0.10, 0.30))
        _place_wrap(left, tl, at, gn)
        _place_wrap(right, tr, at, gn)

    return _space(left, right, "zone_a_wood")


def zone_b_tile() -> tuple[np.ndarray, np.ndarray]:
    """B 타일 — §12's 개방 공간: the hall with 시야 20m, hard and reverberant.

    This is the zone the Runner uses to take aggro from a distance, so it has to
    *sound* open before the player can see that it is. Everything is committed to
    that: a 3 s tail at the highest mix of any zone, low room modes for size, and
    events chosen for how well they show the tail off rather than for themselves.
    """
    lo_l, lo_r = _noise_pair(LOOP_SECONDS, 2001, -6.0, corr=0.72)
    air_l, air_r = _noise_pair(LOOP_SECONDS, 2011, -3.0, corr=0.30)

    def bed(lo, air, ph):
        # Axial modes of a big empty hall. Not a note — a size.
        room = np.zeros(len(lo), dtype=np.float32)
        for f, amp, q in ((_q(58.0), 1.0, 9.0), (_q(87.0), 0.7, 11.0), (_q(139.0), 0.42, 13.0)):
            room += _circ_res(lo, f, q) * amp
        room = _at_rms(_circ_lp(room, 320.0), BED_RMS * 0.50)
        # The hiss of a large volume of still air. Present up top, never bright.
        vol = _at_rms(_circ_bp(air, 400.0, 5200.0), BED_RMS * 0.46) * _lfo(LOOP_SECONDS, _q(0.0333), 0.30, ph)
        return (room + vol * _lfo(LOOP_SECONDS, _q(0.1333), 0.18, ph + 2.4)).astype(np.float32)

    left, right = bed(lo_l, air_l, 0.0), bed(lo_r, air_r, math.pi * 0.55)
    ev = EVENT_CREST * _rms(left)

    g = S.rng(2100)
    # A loose tile ticking somewhere across the hall — a hard transient is the
    # only thing that reveals a 3 s tail.
    for i in range(7):
        tap = _metal_tap(2200 + i, 0.22,
                         [S.Mode(_q(1180.0 + 900.0 * g.random()), 0.030, 1.0),
                          S.Mode(_q(2360.0 + 1400.0 * g.random()), 0.017, 0.5),
                          S.Mode(_q(4100.0 + 1900.0 * g.random()), 0.009, 0.22)],
                         noise=0.42)
        tl, tr = _pan(tap, float(g.uniform(-0.95, 0.95)))
        at = float(g.uniform(0.0, LOOP_SECONDS))
        gn = ev * float(g.uniform(0.20, 0.52))
        _place_wrap(left, tl, at, gn)
        _place_wrap(right, tr, at, gn)

    # A far-off door or slab settling. Low, slow, and it fills the whole hall.
    for i, (at, pan) in enumerate(((6.3, -0.62), (21.8, 0.58))):
        thud = _metal_tap(2300 + i, 0.55, [S.Mode(_q(74.0 + 22.0 * i), 0.12, 1.0),
                                           S.Mode(_q(131.0 + 30.0 * i), 0.07, 0.45)], noise=0.30)
        hl, hr = _pan(S.lowpass(thud, 900.0), pan)
        _place_wrap(left, hl, at, ev * 0.42)
        _place_wrap(right, hr, at, ev * 0.42)

    return _space(left, right, "zone_b_tile")


def zone_c_gravel() -> tuple[np.ndarray, np.ndarray]:
    """C 자갈 — loose, shifting, dripping.

    Two jobs. §12's 부스럭 comes from the granular layer: hundreds of tiny
    contacts, scree that never quite settles. The dripping is §03 doing work —
    the worked clue example is "그것은 물이 있는 층에 있다", so water in the bed
    is a standing hint that this zone is near water, audible to anyone who walks
    through with their ears open.
    """
    lo_l, lo_r = _noise_pair(LOOP_SECONDS, 3001, -6.0, corr=0.68)
    wet_l, wet_r = _noise_pair(LOOP_SECONDS, 3011, -3.0, corr=0.38)

    def bed(lo, wet, seed, ph):
        earth = _at_rms(_circ_lp(lo, 300.0), BED_RMS * 0.44) * _lfo(LOOP_SECONDS, _q(0.0667), 0.35, ph)
        # A thin trickle running somewhere out of sight.
        trickle = _at_rms(_circ_bp(wet, 620.0, 3100.0), BED_RMS * 0.30) \
            * _lfo(LOOP_SECONDS, _q(0.2333), 0.42, ph + 1.2)
        # Dense on purpose. At 760 grains (25/s) the layer was a string of
        # separate ticks rather than a surface, and its 24.5 dB crest factor drove
        # the whole bed into BED_CEILING_DB — zone C came out 5.5 dB quieter than
        # the other four, so walking between zones would have read as a volume
        # change instead of §12's change of material. Grain count sets peak and
        # not level here, because the layer is normalised to a target RMS either
        # way, so raising density buys headroom and a better texture at once.
        grains = _at_rms(_grain_layer(LOOP_SECONDS, seed, 2600,
                                      ((1400.0, 3200.0), (2200.0, 5200.0),
                                       (3400.0, 7600.0), (5200.0, 11000.0))),
                         BED_RMS * 0.52)
        return (earth + trickle + grains).astype(np.float32)

    left = bed(lo_l, wet_l, 3100, 0.0)
    right = bed(lo_r, wet_r, 3200, math.pi * 0.8)
    ev = EVENT_CREST * _rms(left)

    # Seven drips over 30 s, irregular. Distance rolled off up top, then the
    # zone's own reverb (applied below) puts them in the space.
    g = S.rng(3300)
    drip_specs = (
        (1180.0, 1560.0, 0.036, 0.24, -8.0, None),
        (860.0, 1080.0, 0.052, 0.32, -11.0, 195.0),
        (1420.0, 1830.0, 0.028, 0.20, -6.0, None),
        (700.0, 900.0, 0.064, 0.38, -13.0, 158.0),
    )
    for i, at in enumerate((2.15, 6.05, 10.9, 13.4, 18.7, 23.1, 27.6)):
        f0, f1, tau, dur, cdb, pool = drip_specs[i % len(drip_specs)]
        d = _drip(3400 + i * 13, f0, f1, tau, dur, cdb, pool_hz=pool)
        d = S.lowpass(d, float(g.uniform(3400.0, 6200.0)))
        dl, dr = _pan(d, float(g.uniform(-0.9, 0.9)))
        gn = ev * float(g.uniform(0.30, 0.70))
        _place_wrap(left, dl, at, gn)
        _place_wrap(right, dr, at, gn)

    # Scree shifting: a short burst of dense grains, as if something slid.
    for i, (at, pan) in enumerate(((8.4, 0.55), (16.2, -0.68), (25.9, 0.22))):
        slide = _grain_layer(0.42, 3500 + i, 110, ((1800.0, 5000.0), (3000.0, 8000.0)))
        slide = (slide * S.adsr(0.42, 0.02, 0.10, 0.45, 0.24)).astype(np.float32)
        sl, sr_ = _pan(S.normalize(slide, 0.0), pan)
        _place_wrap(left, sl, at, ev * 0.58)
        _place_wrap(right, sr_, at, ev * 0.58)

    return _space(left, right, "zone_c_gravel")


def zone_d_concrete() -> tuple[np.ndarray, np.ndarray]:
    """D 콘크리트 — dead, low rumble. §12's 출구 zone.

    둔탁: dull. Almost no tail, almost no top end — concrete neither rings nor
    reflects brightly, and the difference from B is the whole point of having two
    zones. What is left is weight: a low rumble that sits on the chest.

    This is the zone the objective leaves through, and §12 puts the exit here. It
    should read as *close to out* without ever reading as safe, so the rumble
    breathes but never resolves.
    """
    lo_l, lo_r = _noise_pair(LOOP_SECONDS, 5001, -6.0, corr=0.88)  # lows are correlated in life
    mid_l, mid_r = _noise_pair(LOOP_SECONDS, 5011, -3.0, corr=0.55)

    def bed(lo, mid, ph):
        rumble = _at_rms(_circ_lp(lo, 110.0, order=6), BED_RMS * 0.86) \
            * _lfo(LOOP_SECONDS, _q(0.0333), 0.30, ph)
        # Plant somewhere behind the wall. 100 Hz because mains hum through
        # concrete is what a basement actually sounds like.
        plant = _at_rms(_circ_lp(_circ_res(lo, _q(100.0), 14.0), 260.0), BED_RMS * 0.26)
        # A dull, closed mid. Deliberately capped low: no air, no glare.
        dull = _at_rms(_circ_bp(mid, 150.0, 780.0), BED_RMS * 0.30) \
            * _lfo(LOOP_SECONDS, _q(0.1), 0.20, ph + 2.1)
        return (rumble + plant + dull).astype(np.float32)

    left, right = bed(lo_l, mid_l, 0.0), bed(lo_r, mid_r, math.pi * 0.45)
    ev = EVENT_CREST * _rms(left)

    g = S.rng(5100)
    # The building taking its own weight. Four events in 30 s, all sub-bass.
    for i, at in enumerate((4.7, 12.3, 19.8, 26.4)):
        thud = _metal_tap(5200 + i, 0.85,
                          [S.Mode(_q(46.0 + 13.0 * g.random()), 0.19, 1.0),
                           S.Mode(_q(79.0 + 21.0 * g.random()), 0.11, 0.42),
                           S.Mode(_q(143.0 + 40.0 * g.random()), 0.05, 0.14)],
                          noise=0.22)
        thud = S.lowpass(thud, 420.0)
        tl, tr = _pan(thud, float(g.uniform(-0.5, 0.5)))
        gn = ev * float(g.uniform(0.44, 0.78))
        _place_wrap(left, tl, at, gn)
        _place_wrap(right, tr, at, gn)

    return _space(left, right, "zone_d_concrete")


def stairwell_metal() -> tuple[np.ndarray, np.ndarray]:
    """계단 금속 — metal, resonant, vertical.

    The stairwell is the one place in the map that is taller than it is wide, and
    §12 lists 계단실 as 개방 공간 alongside the hall. Vertical space is not a
    reverb setting, it is a *behaviour*: air rises through a shaft, and a tap
    comes back as a flutter of closely spaced echoes rather than a smooth tail.
    synth.comb does the flutter; three counter-phased bands do the draught.
    """
    lo_l, lo_r = _noise_pair(LOOP_SECONDS, 6001, -6.0, corr=0.60)
    hi_l, hi_r = _noise_pair(LOOP_SECONDS, 6011, -3.0, corr=0.25)

    def bed(lo, hi, ph):
        # Draught up the shaft: three bands breathing out of phase, so the air
        # seems to travel rather than pulse.
        draught = np.zeros(len(hi), dtype=np.float32)
        for k, (band, rate) in enumerate((((180.0, 900.0), 0.0667),
                                          ((700.0, 2600.0), 0.1),
                                          ((2000.0, 6400.0), 0.1667))):
            draught += _circ_bp(hi, band[0], band[1]) * _lfo(
                LOOP_SECONDS, _q(rate), 0.55, ph + k * 2.09)
        draught = _at_rms(draught, BED_RMS * 0.52)

        # The structure itself, ringing very faintly. Inharmonic ratios = metal.
        ring = np.zeros(len(lo), dtype=np.float32)
        for f, amp in ((137.0, 1.0), (219.0, 0.62), (388.0, 0.40), (611.0, 0.26), (977.0, 0.15)):
            ring += _circ_res(lo, _q(f), 90.0) * amp
        ring = _at_rms(ring, BED_RMS * 0.34)

        body = _at_rms(_circ_lp(lo, 240.0), BED_RMS * 0.30)
        return (draught + ring + body).astype(np.float32)

    left, right = bed(lo_l, hi_l, 0.0), bed(lo_r, hi_r, math.pi * 0.6)

    # Flutter echo: the signature of a hard parallel-walled shaft. Short delay,
    # modest feedback — enough to colour, not enough to sing.
    left = _at_rms(_circ_comb(left, 0.0094, 0.42), _rms(left))
    right = _at_rms(_circ_comb(right, 0.0111, 0.40), _rms(right))
    ev = EVENT_CREST * _rms(left)

    g = S.rng(6100)
    # Something ticking on the steel — the cue that the shaft goes up past you.
    for i in range(9):
        tap = _metal_tap(6200 + i, 0.40,
                         [S.Mode(_q(430.0 + 380.0 * g.random()), 0.075, 1.0),
                          S.Mode(_q(1130.0 + 700.0 * g.random()), 0.048, 0.52),
                          S.Mode(_q(2470.0 + 1300.0 * g.random()), 0.026, 0.28),
                          S.Mode(_q(4300.0 + 1800.0 * g.random()), 0.013, 0.12)],
                         noise=0.34)
        tl, tr = _pan(tap, float(g.uniform(-0.8, 0.8)))
        at = float(g.uniform(0.0, LOOP_SECONDS))
        gn = ev * float(g.uniform(0.16, 0.46))
        _place_wrap(left, tl, at, gn)
        _place_wrap(right, tr, at, gn)

    return _space(left, right, "stairwell_metal")


def surface_vehicle() -> tuple[np.ndarray, np.ndarray]:
    """§08's 지상 차량 — safe zone, shop, 보급소. This one is relief.

    Every other bed in this file is built to make the player uneasy. This one is
    built to let them stop being uneasy, because §08's economy only works if
    coming up here feels like something. So: warm spectral tilt, a generator
    idling steadily, moving air with no reverb behind it (outdoors has no tail),
    and — the part that matters — nothing dissonant and nothing that swells. The
    hum holds a fifth. Threat music resolves nowhere; this resolves.

    It is also where §07's clock is readable at all (시각은 지상에서만 알 수 있다),
    so it must not fight the tension bed that keeps playing over it.
    """
    lo_l, lo_r = _noise_pair(LOOP_SECONDS, 7001, -6.0, corr=0.75)
    air_l, air_r = _noise_pair(LOOP_SECONDS, 7011, -3.0, corr=0.28)

    # Generator, shared between channels: a real machine 4 m away is a point
    # source, so it belongs near the centre. Slight detune on the octave gives a
    # 0.9 Hz beat — a real engine never holds perfectly steady, and the wobble
    # is what stops this reading as a synth pad.
    hum = np.zeros(S.n_samples(LOOP_SECONDS), dtype=np.float32)
    for f, amp in ((50.0, 1.0), (100.0, 0.46), (150.0, 0.22), (200.0, 0.11), (250.0, 0.05)):
        hum += S.sine(_q(f), LOOP_SECONDS) * amp
    hum += S.sine(_q(100.9), LOOP_SECONDS) * 0.20          # beats against 100
    hum += S.sine(_q(75.0), LOOP_SECONDS) * 0.13           # a fifth above 50: consonant
    hum = _at_rms(_circ_lp(hum, 620.0), BED_RMS * 0.60)
    hum = (hum * _lfo(LOOP_SECONDS, _q(0.1667), 0.10)).astype(np.float32)

    def bed(lo, air, seed, ph):
        # Open air over a field at night. Wide, soft, capped below 4 kHz — no
        # hiss, because hiss reads as tension.
        wind = _at_rms(_circ_bp(air, 190.0, 3800.0), BED_RMS * 0.44) \
            * _lfo(LOOP_SECONDS, _q(0.0667), 0.50, ph) * _lfo(LOOP_SECONDS, _q(0.1667), 0.22, ph + 2.6)
        warmth = _at_rms(_circ_bp(lo, 90.0, 700.0), BED_RMS * 0.36)
        return (wind + warmth).astype(np.float32)

    left = (bed(lo_l, air_l, 7100, 0.0) + hum * 0.94).astype(np.float32)
    right = (bed(lo_r, air_r, 7200, math.pi * 0.5) + hum * 1.0).astype(np.float32)
    ev = EVENT_CREST * _rms(left)

    g = S.rng(7300)
    # The vehicle's body ticking as it cools. Domestic, slow, unthreatening.
    for i in range(8):
        tick = _metal_tap(7400 + i, 0.16,
                          [S.Mode(_q(620.0 + 480.0 * g.random()), 0.022, 1.0),
                           S.Mode(_q(1480.0 + 900.0 * g.random()), 0.012, 0.38)],
                          noise=0.30)
        tick = S.lowpass(tick, 4200.0)
        tl, tr = _pan(tick, float(g.uniform(-0.6, 0.6)))
        at = float(g.uniform(0.0, LOOP_SECONDS))
        gn = ev * float(g.uniform(0.12, 0.34))
        _place_wrap(left, tl, at, gn)
        _place_wrap(right, tr, at, gn)

    return _space(left, right, "surface_vehicle")


def generator_hum() -> np.ndarray:
    """The surface generator, as a positional point source. MONO.

    §03: 배터리가 떨어지면 단서를 읽을 수 없다, and the generator is where
    batteries come from. In the dark that makes this a *navigation* sound — the
    thing you walk toward when the flashlight starts to go. That only works if
    Unity can attenuate and pan it, so it is mono and it is dry. The surface bed
    above carries its own quieter copy of this hum for atmosphere; this is the
    machine itself, up close.
    """
    L = HUM_LOOP_SECONDS
    hum = np.zeros(S.n_samples(L), dtype=np.float32)
    for f, amp in ((50.0, 1.0), (100.0, 0.52), (150.0, 0.30), (200.0, 0.17),
                   (250.0, 0.09), (300.0, 0.05)):
        hum += S.sine(_q(f, L), L) * amp
    hum += S.sine(_q(100.75, L), L) * 0.22   # 0.75 Hz beat: a load that never settles
    hum += S.sine(_q(150.5, L), L) * 0.11

    # Combustion roughness. A generator is a series of small explosions, and the
    # firing rate is what tells you it is an engine and not a transformer.
    fire = _swept_impulse_train(L, 8100, 24.0, 24.0, jitter=0.55)
    fire = _circ_lp(_circ_res(fire, _q(118.0, L), 12.0), 900.0)

    # Casing rattle and fan wash.
    mech = _circ_bp(_loop_noise(L, 8200, -3.0), 260.0, 2600.0)
    mech = (mech * _lfo(L, _q(0.5, L), 0.30)).astype(np.float32)

    out = (_at_rms(hum, 0.10) + _at_rms(fire, 0.030) + _at_rms(mech, 0.022)).astype(np.float32)
    out = (out * _lfo(L, _q(0.25, L), 0.08)).astype(np.float32)
    return _circ_dc_block(out, 22.0)


# ── Tension beds (§07) ──────────────────────────────────────────────────────

TENSION_ROOT = 45.0
"""One root for all five tiers. The game crossfades these as §07's clock
advances, and a crossfade between two unrelated pitches reads as a key change —
an event. The curve is not an event, it is a slope, so every tier is built on
45 Hz and its relatives and only the *character* moves."""

TENSION_NAMES = ("t1_dusk", "t2_night", "t3_deepnight", "t4_dawn", "t5_predawn")
TENSION_KOREAN = ("초저녁", "밤", "심야", "새벽", "동트기 전")


def _sub_pulse(seed: int, seconds: float, freq: float, tau: float) -> np.ndarray:
    """A soft sub thump. Not a heartbeat — a heartbeat is a cliché and, worse, it
    tells the player their character is afraid instead of letting them be."""
    body = (S.sine(freq, seconds) * S.exp_decay(seconds, tau)).astype(np.float32)
    knock = S.white(seconds, seed, SR) * S.exp_decay(seconds, 0.010)
    knock = S.lowpass(knock, 190.0)
    shaped = S.normalize(S.lowpass((body + knock * 0.34).astype(np.float32), 420.0), 0.0)
    # An event dropped into a loop has to start and end at zero, or it welds a
    # step into wherever it lands — and the first pulse lands on sample 0, i.e.
    # exactly on the seam. synth.modal_impact fades its own ends for this reason;
    # the noise knock here means this one has to as well. 0.6 ms is short enough
    # to leave the thump's attack intact.
    return S.fade(shaped, 0.0006, 0.004)


def tension_bed(tier: int) -> tuple[np.ndarray, np.ndarray]:
    """One of §07's five threat stages, as a bed.

    tier is 1-5 for 초저녁 / 밤 / 심야 / 새벽 / 동트기 전.

    The escalation is deliberately built on four independent axes so it does not
    just get louder: more partials, more dissonance, a faster pulse, and a
    brighter top. §07's monster goes 4.4 → 5.2 m/s and loses its 정지 behaviour
    entirely by tier 4 — by then the bed should feel like it has stopped
    breathing too.
    """
    R = TENSION_ROOT
    lo_l, lo_r = _noise_pair(LOOP_SECONDS, 9000 + tier * 31, -6.0, corr=0.70)
    hi_l, hi_r = _noise_pair(LOOP_SECONDS, 9100 + tier * 31, -3.0, corr=0.30)

    # Drone partials per tier. Ratios chosen for consonance early and friction
    # late: 1 and 2 are the root and octave, 1.5 a fifth, 2.02 a slow beat,
    # 2.83 a tritone, 1.0667 a 5.5 Hz roughness you feel rather than hear.
    partials: list[tuple[float, float]] = [(1.0, 1.0), (2.0, 0.34)]
    if tier >= 2:
        partials += [(1.5, 0.24), (2.02, 0.26)]
    if tier >= 3:
        partials += [(2.83, 0.20), (3.0, 0.14)]
    if tier >= 4:
        partials += [(4.22, 0.13), (1.0667, 0.22)]
    if tier >= 5:
        # The sub-octave was 0.5 (22.5 Hz), which nothing reproduces and nobody
        # hears — it existed only as peak, and it cost T5 the top of §07's ladder
        # by holding the crest factor at 12.7 dB against a 12.0 dB budget. 0.75
        # puts it at 33.75 Hz, where it is actually felt.
        partials += [(5.66, 0.10), (6.33, 0.08), (0.75, 0.26)]

    drone = np.zeros(S.n_samples(LOOP_SECONDS), dtype=np.float32)
    for k, (ratio, amp) in enumerate(partials):
        drone += S.sine(_q(R * ratio), LOOP_SECONDS, phase=0.37 * k) * amp
    drone = _circ_lp(drone, 900.0)
    if tier >= 4:
        # Saturation adds harmonics that were not there, which is exactly the
        # feeling: the same sound, but it has started to distort under load.
        drone = S.saturate(drone, 1.0 + 0.55 * (tier - 3))
        drone = _circ_lp(drone, 1400.0)

    def bed(lo, hi, ph):
        low = _at_rms(_circ_lp(lo, 150.0), 0.030 + 0.012 * tier) \
            * _lfo(LOOP_SECONDS, _q(0.0333 + 0.0333 * (tier - 1)), max(0.10, 0.55 - 0.09 * tier), ph)
        layer = low
        # Shimmer, on a geometric ramp and present from tier 1.
        #
        # This started at tier 2 and the measured spectral centroid went 231 Hz
        # (T1) to 3497 Hz (T2) and then barely moved: 3839, 4124, 4556. That is a
        # step at T2 followed by a plateau, and §07's threat curve is a slope, not
        # an event — the tier the player crosses should never be the loud one.
        # Brightness is extremely sensitive to a faint wideband top, so the ramp
        # has to be geometric and has to start audible-but-barely at T1.
        band = (2600.0 + 500.0 * tier, 6200.0 + 1400.0 * tier)
        sh = _at_rms(_circ_bp(hi, band[0], band[1]), 0.0010 * (2.15 ** (tier - 1)))
        layer = (layer + sh * _lfo(LOOP_SECONDS, _q(0.1 * tier), 0.45, ph + 1.7)).astype(np.float32)
        if tier >= 3:
            mid = _at_rms(_circ_bp(hi, 300.0, 1500.0), 0.008 * (tier - 2))
            layer = (layer + mid).astype(np.float32)
        return layer.astype(np.float32)

    left = (bed(lo_l, hi_l, 0.0) + _at_rms(drone, 0.052 + 0.006 * tier) * 0.97).astype(np.float32)
    right = (bed(lo_r, hi_r, math.pi * 0.5) + _at_rms(drone, 0.052 + 0.006 * tier)).astype(np.float32)

    # Pulse. Silent at tier 1 — 초저녁 has 정지 잦음 and the player should still
    # believe the night might go well. It arrives at tier 3 and accelerates.
    pulse_counts = {3: 20, 4: 28, 5: 38}     # whole pulses across 30 s, so it loops
    if tier in pulse_counts:
        count = pulse_counts[tier]
        p = _sub_pulse(9500 + tier, 0.75, _q(R * 1.07), 0.16 + 0.02 * (5 - tier))
        # Held down deliberately. At (2.0 + 0.55*(tier-3)) the T5 bed reached a
        # 13.3 dB crest factor, which pushed its peak into BED_CEILING_DB and cost
        # 1.3 dB off the top of §07's ladder — the tier meant to feel worst came
        # out only 1.6 dB above tier 4. A peaky bed also masks the footsteps the
        # Listener is working from, so the fix belongs here, not in the ceiling.
        amp = _rms(left) * (1.65 + 0.30 * (tier - 3))
        for i in range(count):
            _place_wrap(left, p, i * LOOP_SECONDS / count, amp * 0.96)
            _place_wrap(right, p, i * LOOP_SECONDS / count, amp)

    if tier >= 4:
        # A sub sweep that keeps starting over and never arrives. Six 5 s
        # segments, each windowed to zero at both ends so the repeats join.
        seg = 5.0
        sw = (S.sweep(_q(28.0), _q(76.0), seg, log=True)
              * np.hanning(S.n_samples(seg)).astype(np.float32))
        sw = S.lowpass(sw, 260.0)
        amp = _rms(left) * 0.85
        for i in range(int(LOOP_SECONDS / seg)):
            _place_wrap(left, sw, i * seg, amp)
            _place_wrap(right, sw, i * seg, amp * 0.94)

    if tier >= 5:
        # 동트기 전 is described as 생존 불가 수준. A faint fixed high tone that
        # the ear cannot localise or get used to — the sound of your own hearing
        # under stress, restrained enough to stay a feeling rather than a beep.
        tone = S.sine(_q(3140.0), LOOP_SECONDS) * _lfo(LOOP_SECONDS, _q(0.2), 0.35)
        t_amp = _rms(left) * 0.055
        left = (left + tone * t_amp).astype(np.float32)
        right = (right + tone * t_amp * 0.9).astype(np.float32)

    return _circ_dc_block(left, 20.0), _circ_dc_block(right, 20.0)


# ── Writing and verification ────────────────────────────────────────────────


@dataclass(frozen=True)
class SeamReport:
    """How well a written loop joins up, measured on the file that ships.

    Eyeballing a waveform cannot answer this. The two failure modes are a step
    (a click) and a hole (a dropout), and they need different measurements.
    """

    path: str
    wrap_step: float
    """|y[0] - y[N-1]| — the jump the DAC makes when the loop repeats."""

    p99_step: float
    """99th percentile of |diff(y)| inside the clip. The wrap step has to fit in
    the population of steps the clip already contains, or it is a click."""

    median_step: float
    wrap_curvature: float
    """Second-difference error across the wrap. A signal can join in value and
    still kink in slope, which is quieter but still audible."""

    p99_curvature: float
    seam_env_db: float
    """1 ms envelope at the seam vs the clip median, in dB. Catches a hole."""

    env_p1_db: float
    """1st-percentile envelope of the clip, same reference. The seam only counts
    as a hole if it is quieter than this — otherwise it is just a quiet moment."""

    hf_percentile: float
    """Where the seam's high-frequency energy ranks among every instant in the
    clip, 0-100. This is the test that matches what a click actually *is*: a step
    in the waveform splatters energy across the whole spectrum, so a real seam
    click is an HF outlier. An unremarkable rank means there is nothing to hear."""

    @property
    def step_ratio(self) -> float:
        return self.wrap_step / self.median_step if self.median_step > 0 else 0.0

    @property
    def ok(self) -> bool:
        return (self.wrap_step <= self.p99_step
                and self.wrap_curvature <= self.p99_curvature
                and self.seam_env_db >= self.env_p1_db
                and self.hf_percentile <= 99.5)


def _hf_seam_percentile(ch: np.ndarray, sr: int = SR) -> float:
    """Percentile rank of the loop point's high-frequency energy.

    Highpassing on the DFT keeps the analysis circular, so the measurement itself
    cannot manufacture the edge artefact it is looking for, and uniform_filter1d
    with mode="wrap" places window 0 straddling the seam exactly as a looping
    player hears it.
    """
    n = len(ch)
    spec = np.fft.rfft(ch)
    spec[np.fft.rfftfreq(n, 1.0 / sr) < 6000.0] = 0.0
    hf = np.fft.irfft(spec, n)
    energy = uniform_filter1d(np.square(hf), size=max(4, S.n_samples(0.005, sr)), mode="wrap")
    return float(np.count_nonzero(energy <= energy[0]) * 100.0 / n)


def _channels(path: str) -> list[np.ndarray]:
    """Reads a WAV back as a list of per-channel float arrays.

    synth.read_wav averages stereo to mono, which would hide a discontinuity
    that exists in only one channel — so the seam check reads the frames itself.
    """
    with wave.open(path, "rb") as w:
        if w.getsampwidth() != 2:
            raise AssertionError(f"{path}: expected 16-bit PCM")
        nch = w.getnchannels()
        data = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768.0
    return [data] if nch == 1 else [data[0::2], data[1::2]]


def measure_seam(path: str, sr: int = SR) -> SeamReport:
    """Measures the loop point of a written file, per channel, worst case wins."""
    worst: SeamReport | None = None
    for ch in _channels(path):
        d1 = np.abs(np.diff(ch))
        d2 = np.abs(np.diff(ch, n=2))
        wrap_step = abs(float(ch[0] - ch[-1]))
        # Slope entering the wrap vs slope leaving it.
        wrap_curv = abs(float((ch[0] - ch[-1]) - (ch[-1] - ch[-2])))

        # mode="wrap" makes the moving average circular, so env[0] is computed
        # across the loop point exactly as a looping player would hear it.
        win = max(4, S.n_samples(0.001, sr))
        env = np.sqrt(uniform_filter1d(np.square(ch), size=win, mode="wrap"))
        med = float(np.median(env))
        ref = med if med > 1e-12 else 1e-12
        seam_env = float(np.min(np.concatenate([env[:2], env[-2:]])))

        r = SeamReport(
            path=path,
            wrap_step=wrap_step,
            p99_step=float(np.percentile(d1, 99.0)),
            median_step=float(np.median(d1)),
            wrap_curvature=wrap_curv,
            p99_curvature=float(np.percentile(d2, 99.0)),
            seam_env_db=S.gain_to_db(seam_env / ref),
            env_p1_db=S.gain_to_db(float(np.percentile(env, 1.0)) / ref),
            hf_percentile=_hf_seam_percentile(ch, sr),
        )
        if worst is None or r.step_ratio > worst.step_ratio:
            worst = r
    assert worst is not None
    return worst


def _write_loop_wav(path: str, chans: Sequence[np.ndarray], rms_db: float | None = None,
                    peak_db: float | None = None, ceiling_db: float = BED_CEILING_DB,
                    sr: int = SR) -> tuple[str, float, float]:
    """Writes a loop as 48 kHz 16-bit PCM, with the ends left alone.

    This is the one thing synth.write_wav cannot do. It fades every buffer by
    2 ms in and 6 ms out, which sets the first and last sample to exactly zero —
    an 8 ms hole at the loop point, and a hole is what a loop is defined by not
    having. Everything else here matches synth's conventions: float32 in [-1,1]
    until the write, one gain for the whole pair so the stereo image survives,
    round-then-clamp so a sample at 1.0 cannot wrap negative.

    Channels are scaled by a *common* gain. Normalising them separately would
    shift the image, and for a bed the image is the product.

    Returns (path, applied_peak_dbfs, applied_rms_dbfs).
    """
    if not chans or len({len(c) for c in chans}) != 1:
        raise AssertionError("loop channels must be present and equal length")
    if (rms_db is None) == (peak_db is None):
        raise AssertionError("give exactly one of rms_db / peak_db")

    stacked = np.concatenate([c.astype(np.float64) for c in chans])
    peak = float(np.max(np.abs(stacked)))
    rms = float(np.sqrt(np.mean(np.square(stacked))))
    if peak < 1e-9 or rms < 1e-12:
        raise AssertionError(f"{path}: nothing to write")

    if rms_db is not None:
        gain = S.db_to_gain(rms_db) / rms
        # A bed must never eat the headroom a footstep needs (§12: the Listener
        # has to hear the floor). Cap the peak and report the shortfall.
        if peak * gain > S.db_to_gain(ceiling_db):
            gain = S.db_to_gain(ceiling_db) / peak
    else:
        gain = S.db_to_gain(peak_db) / peak

    out = [np.clip(np.rint(c.astype(np.float64) * gain * 32767.0), -32768, 32767).astype("<i2")
           for c in chans]
    frames = out[0] if len(out) == 1 else np.column_stack(out).ravel()

    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(len(out))
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(frames.tobytes())

    return path, S.gain_to_db(peak * gain), S.gain_to_db(rms * gain)


def measure_rt60(key: str, sr: int = SR) -> float:
    """Measured RT60 of a zone's room, by probing the actual reverb chain.

    §12 claims B is a reverberant tiled hall and D is dead concrete. That claim
    should be a number somebody can check, not an adjective in a comment, so this
    pushes an impulse through the same call the bed used and reads the decay off
    a Schroeder integration (fitted over -5 to -25 dB, extrapolated to -60).
    """
    rt, mix, damp, seed = ZONE_SPACES[key]
    n = S.n_samples(rt * 2.0 + 0.5, sr)
    imp = np.zeros(n, dtype=np.float32)
    imp[0] = 1.0
    wet = S.reverb(imp, seconds=rt, mix=mix, seed=seed, damping=damp, sr=sr)

    energy = np.square(wet.astype(np.float64))
    decay = np.cumsum(energy[::-1])[::-1]
    decay_db = 10.0 * np.log10(np.maximum(decay / decay[0], 1e-20))
    try:
        i0 = int(np.argmax(decay_db <= -5.0))
        i1 = int(np.argmax(decay_db <= -25.0))
        if i1 <= i0:
            return 0.0
        slope = (decay_db[i1] - decay_db[i0]) / ((i1 - i0) / sr)   # dB per second
        return float(-60.0 / slope)
    except (ValueError, IndexError, ZeroDivisionError):
        return 0.0


def _sha(path: str) -> str:
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()[:12]


# ── Main ────────────────────────────────────────────────────────────────────


def main() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)
    reports: list[S.ClipReport] = []
    seams: list[SeamReport] = []
    levels: dict[str, tuple[float, float]] = {}
    channels: dict[str, int] = {}

    def out(name: str) -> str:
        return os.path.join(OUT_DIR, name)

    def emit_loop(name: str, chans: Sequence[np.ndarray], **kw) -> None:
        # One place, so no bed can be shipped with a smeared low end by omission.
        if len(chans) == 2:
            chans = list(_mono_bass(chans[0], chans[1]))
        path, pk, rm = _write_loop_wav(out(name), chans, **kw)
        r = S.assert_usable(path)
        s = measure_seam(path)
        if not s.ok:
            raise AssertionError(
                f"{name}: loop point is not seamless — "
                f"wrap step {s.wrap_step:.6f} vs p99 {s.p99_step:.6f}, "
                f"curvature {s.wrap_curvature:.6f} vs p99 {s.p99_curvature:.6f}, "
                f"seam env {s.seam_env_db:+.1f} dB vs clip 1st pct {s.env_p1_db:+.1f} dB, "
                f"HF rank {s.hf_percentile:.1f} pct")
        reports.append(r)
        seams.append(s)
        levels[name] = (pk, rm)
        channels[name] = len(chans)

    def emit_oneshot(name: str, buf: np.ndarray, peak_db: float) -> None:
        # synth.write_wav is exactly right here: a one-shot *should* be faded to
        # zero at both ends, and these are mono positional clips.
        path = S.write_wav(out(name), buf, headroom_db=peak_db, stereo=False)
        r = S.assert_usable(path)
        if r.channels != 1:
            raise AssertionError(f"{name}: positional clip must be mono")
        reports.append(r)
        levels[name] = (r.peak_db, S.gain_to_db(r.rms))
        channels[name] = 1

    print("building zone beds (§12) ...")
    emit_loop("amb_zone_a_wood_loop.wav", zone_a_wood(), rms_db=ZONE_RMS_DB)
    emit_loop("amb_zone_b_tile_loop.wav", zone_b_tile(), rms_db=ZONE_RMS_DB)
    emit_loop("amb_zone_c_gravel_loop.wav", zone_c_gravel(), rms_db=ZONE_RMS_DB)
    emit_loop("amb_zone_d_concrete_loop.wav", zone_d_concrete(), rms_db=ZONE_RMS_DB)
    emit_loop("amb_stairwell_metal_loop.wav", stairwell_metal(), rms_db=ZONE_RMS_DB)

    print("building the surface (§08) ...")
    emit_loop("amb_surface_vehicle_loop.wav", surface_vehicle(), rms_db=SURFACE_RMS_DB)
    emit_loop("amb_generator_hum_loop.wav", [generator_hum()], peak_db=HUM_PEAK_DB,
              ceiling_db=0.0)

    print("building tension beds (§07) ...")
    for tier in range(1, 6):
        emit_loop(f"amb_tension_{TENSION_NAMES[tier - 1]}_loop.wav",
                  tension_bed(tier), rms_db=TENSION_RMS_DB[tier - 1])

    print("building water drips (§03) ...")
    drips = (
        # (seed, f0,     f1,     tau,   seconds, click_db, pool_hz, echo_at)
        (4101, 1180.0, 1580.0, 0.034, 0.24, -7.0, None, None),
        (4202, 820.0, 1030.0, 0.055, 0.36, -11.0, 192.0, None),
        (4303, 505.0, 640.0, 0.088, 0.46, -15.0, 143.0, None),
        (4404, 1390.0, 1810.0, 0.029, 0.34, -6.0, None, 0.085),
    )
    for i, (seed, f0, f1, tau, dur, cdb, pool, echo) in enumerate(drips, start=1):
        emit_oneshot(f"sfx_water_drip_{i:02d}.wav",
                     _drip(seed, f0, f1, tau, dur, cdb, pool_hz=pool, echo_at=echo),
                     DRIP_PEAK_DB)

    print("building distant creaks (§06) ...")
    creaks = (
        # (seed, seconds, modes, rate0, rate1, groan_hz, lp)
        (5101, 1.45, ((124.0, 1.0, 40.0), (208.0, 0.58, 30.0), (367.0, 0.22, 22.0)), 38.0, 20.0, 68.0, 2600.0),
        (5202, 2.15, ((97.0, 1.0, 44.0), (171.0, 0.62, 33.0), (289.0, 0.26, 24.0)), 27.0, 14.0, 54.0, 2100.0),
        (5303, 0.85, ((162.0, 1.0, 34.0), (271.0, 0.50, 26.0), (463.0, 0.20, 20.0)), 55.0, 31.0, 88.0, 3300.0),
        (5404, 1.75, ((113.0, 1.0, 42.0), (194.0, 0.60, 31.0), (338.0, 0.24, 23.0)), 32.0, 17.0, 61.0, 2400.0),
        (5505, 1.10, ((141.0, 1.0, 37.0), (238.0, 0.54, 28.0), (409.0, 0.21, 21.0)), 46.0, 25.0, 76.0, 2900.0),
    )
    for i, (seed, dur, modes, r0, r1, groan, lp) in enumerate(creaks, start=1):
        body = _creak(dur, seed, modes, r0, r1, attack=dur * 0.26, band=(70.0, lp))
        # A low groan under the slip: the beam bending, not just slipping.
        g = (S.sweep(groan, groan * 0.78, dur, log=True)
             * S.adsr(dur, dur * 0.3, dur * 0.3, 0.5, dur * 0.4)).astype(np.float32)
        # Distance is a low-pass, not a reverb. These are positional one-shots
        # for random placement, so Unity owns the space — synth.reverb's own
        # docstring warns against baking a tail into a clip the engine will
        # attenuate. §06's 정지 state is what these are really for: when the
        # monster stops it makes no sound at all, and the building has to keep
        # being audible or the silence reads as a bug instead of a threat.
        mixed = S.mix(body, S.lowpass(g, groan * 4.0), gains=[1.0, 0.34])
        emit_oneshot(f"sfx_creak_distant_{i:02d}.wav", S.lowpass(mixed, lp), CREAK_PEAK_DB)

    # ── Report ──────────────────────────────────────────────────────────────
    print()
    print("MEASURED CLIPS  (synth.report_table)")
    print(S.report_table(reports))

    print()
    print("APPLIED LEVELS AND CHANNEL POLICY")
    print(f"{'clip':<42} {'ch':>3} {'peak dB':>8} {'rms dB':>8}  role")
    print("-" * 92)
    role = {1: "positional (mono, 3D)", 2: "bed (stereo, 2D)"}
    for r in reports:
        name = os.path.basename(r.path)
        pk, rm = levels[name]
        print(f"{name:<42} {channels[name]:>3} {pk:>8.1f} {rm:>8.1f}  {role[channels[name]]}")

    print()
    print("LOOP SEAM  — measured on the written file, worst channel")
    print(f"{'clip':<42} {'wrap step':>10} {'p99 step':>10} {'x median':>9} "
          f"{'curv':>9} {'p99 curv':>9} {'seam env':>9} {'clip p1':>8} {'HF pct':>7}  ok")
    print("-" * 131)
    for s in seams:
        print(f"{os.path.basename(s.path):<42} {s.wrap_step:>10.6f} {s.p99_step:>10.6f} "
              f"{s.step_ratio:>9.2f} {s.wrap_curvature:>9.6f} {s.p99_curvature:>9.6f} "
              f"{s.seam_env_db:>+8.1f}d {s.env_p1_db:>+7.1f}d {s.hf_percentile:>7.1f}  "
              f"{'yes' if s.ok else 'NO'}")

    print()
    print("ROOM PER ZONE — RT60 measured by probing the actual reverb chain")
    print(f"{'space':<20} {'reverb(seconds=)':>17} {'mix':>6} {'damping Hz':>11} "
          f"{'measured RT60 s':>16}")
    print("-" * 76)
    for key in ZONE_SPACES:
        rt, mix, damp, _ = ZONE_SPACES[key]
        print(f"{key:<20} {rt:>17.2f} {mix:>6.2f} {damp:>11.0f} {measure_rt60(key):>16.2f}")
    rt_b, rt_a, rt_d = measure_rt60("zone_b_tile"), measure_rt60("zone_a_wood"), measure_rt60("zone_d_concrete")
    if not (rt_b > 2.0 * rt_a and rt_b > 2.0 * rt_d):
        raise AssertionError(
            f"§12 calls B a reverberant tiled hall and A dry / D dead, but RT60 measures "
            f"B {rt_b:.2f}s vs A {rt_a:.2f}s and D {rt_d:.2f}s — not an audible difference")

    # §12 gives each zone a different material and a different room. If two beds
    # measure the same, the zone identity the Listener relies on is not there.
    print()
    print("§12 ZONE SEPARATION — spectral centroid of each bed")
    zone_files = ("amb_zone_a_wood_loop.wav", "amb_zone_b_tile_loop.wav",
                  "amb_zone_c_gravel_loop.wav", "amb_zone_d_concrete_loop.wav",
                  "amb_stairwell_metal_loop.wav")
    cents = {f: next(r.spectral_centroid for r in reports if r.path.endswith(f)) for f in zone_files}
    for f, c in sorted(cents.items(), key=lambda kv: kv[1]):
        print(f"  {f:<40} {c:>8.0f} Hz")
    ratios = []
    keys = list(cents)
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            a, b = cents[keys[i]], cents[keys[j]]
            ratios.append((max(a, b) / max(min(a, b), 1e-9), keys[i], keys[j]))
    ratios.sort()
    print(f"  closest pair: {ratios[0][1]} vs {ratios[0][2]} — ratio {ratios[0][0]:.2f}x")
    if ratios[0][0] < 1.15:
        raise AssertionError(
            f"§12 zone beds too alike: {ratios[0][1]} and {ratios[0][2]} differ by only "
            f"{ratios[0][0]:.2f}x in centroid — the Listener's zone map needs them distinct")

    # §07's curve must be monotonic. If tier 4 is not more oppressive than tier
    # 3, the crossfade tells the player the night is improving.
    # §07's stages must escalate on every axis that carries them. "ch rms" is the
    # per-channel level this script applied; "sum rms" is what analyse() reports,
    # which is lower because synth.read_wav averages stereo down to mono — the two
    # are different measurements of the same file, not a discrepancy.
    print()
    print("§07 TENSION LADDER — level, brightness and pulse must all rise")
    print(f"{'tier':<6} {'stage':<12} {'ch rms dB':>10} {'step':>6} {'sum rms dB':>11} "
          f"{'peak dB':>8} {'crest dB':>9} {'centroid Hz':>12} {'pulses/30s':>11}")
    print("-" * 100)
    pulses = {1: 0, 2: 0, 3: 20, 4: 28, 5: 38}
    prev_ch, prev_sum, prev_cent = -math.inf, -math.inf, -math.inf
    for tier in range(1, 6):
        name = f"amb_tension_{TENSION_NAMES[tier - 1]}_loop.wav"
        r = next(x for x in reports if x.path.endswith(name))
        pk, ch_rms = levels[name]
        sum_rms = S.gain_to_db(r.rms)
        step = f"{ch_rms - prev_ch:+.1f}" if tier > 1 else "—"
        print(f"  T{tier:<3} {TENSION_KOREAN[tier - 1]:<12} {ch_rms:>10.1f} {step:>6} "
              f"{sum_rms:>11.1f} {pk:>8.1f} {pk - ch_rms:>9.1f} "
              f"{r.spectral_centroid:>12.0f} {pulses[tier]:>11}")
        if ch_rms <= prev_ch or sum_rms <= prev_sum:
            raise AssertionError(
                f"§07 tension ladder not monotonic in level at T{tier}: "
                f"per-channel {ch_rms:.1f} dB vs {prev_ch:.1f}, "
                f"mono-sum {sum_rms:.1f} dB vs {prev_sum:.1f}")
        if r.spectral_centroid <= prev_cent:
            raise AssertionError(
                f"§07 tension ladder not monotonic in brightness at T{tier}: "
                f"centroid {r.spectral_centroid:.0f} Hz does not exceed {prev_cent:.0f} Hz")
        prev_ch, prev_sum, prev_cent = ch_rms, sum_rms, r.spectral_centroid

    print()
    print("FILES WRITTEN")
    total = 0
    for r in reports:
        size = os.path.getsize(r.path)
        total += size
        print(f"  {_sha(r.path)}  {size / 1024:>8.0f} KiB  {r.path}")
    print(f"  {len(reports)} files, {total / 1048576:.1f} MiB total")
    print()
    print("all clips passed synth.assert_usable; all loops passed the seam check")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
