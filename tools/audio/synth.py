"""Procedural audio synthesis toolkit for the horror game.

Every sound the game ships is *assembled* by code in this directory, and this
module is the part that makes something out of nothing. It has no knowledge of
recordings and should keep none — `source_bank.py` is the only place that knows
a microphone was ever involved.

Since 2026-08-09 some generators lay a vendored CC0 field recording under what
they build here (see `fetch_sources.py`). That does not change what this file is
for, and it does not change the licence position a Steam release needs: every
vendored byte is CC0 1.0, public-domain-equivalent, no attribution to track, and
every generator still builds its whole family with the recordings absent.

Synthesis also still owns the part of the design that is a *decision*. §12 makes
floor material a gameplay channel — the Listener locates the monster by what its
footsteps land on, so the eight surfaces have to be reliably distinguishable by
ear. That distinguishability is tuned here, directly, rather than hoped for from
whichever sample pack happened to contrast well; `modal_impact` exists for
exactly that job, and `gen_footsteps.match_centroid` exists to stop a recording
overwriting the answer.

Conventions, and the reasons for them:

* **48 kHz, 16-bit PCM.** Unity's native rate; avoids a resample on import.
* **Mono for anything positional.** Unity will not spatialise a stereo clip, and
  §13's proximity voice plus the Listener both depend on 3D attenuation working.
  Stereo is only for UI and non-diegetic stingers.
* **Peak-normalised with headroom.** Clips leave here below full scale so the
  engine can mix several without clipping.
* Sample buffers are always float32 numpy arrays in [-1, 1] until the moment they
  are written.
"""

from __future__ import annotations

import math
import os
import struct
import wave
from dataclasses import dataclass
from typing import Iterable, Sequence

import numpy as np
from scipy import signal

# ── Constants ───────────────────────────────────────────────────────────────

SAMPLE_RATE = 48000
"""Unity's native rate. Writing anything else forces a resample on import."""

DEFAULT_HEADROOM_DB = -3.0
"""Peak target for positional sounds. Leaves room for several to overlap."""

UI_HEADROOM_DB = -6.0
"""UI sits quieter than the world so it never masks a footstep."""

DC_BLOCK_HZ = 20.0
"""High-pass corner for noise generators.

Below roughly 20 Hz nothing is audible, but the energy still consumes headroom and
shifts the waveform off centre. Integrating or summing noise produces exactly that
kind of offset, so the noise generators block it at the source.
"""


def db_to_gain(db: float) -> float:
    """Converts decibels to a linear amplitude factor."""
    return float(10.0 ** (db / 20.0))


def gain_to_db(gain: float) -> float:
    """Converts a linear amplitude factor to decibels. Returns -inf at zero."""
    return -math.inf if gain <= 0.0 else float(20.0 * math.log10(gain))


def n_samples(seconds: float, sr: int = SAMPLE_RATE) -> int:
    """Number of samples in a duration."""
    return max(1, int(round(seconds * sr)))


def t_axis(seconds: float, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Time axis in seconds, as float32."""
    return np.arange(n_samples(seconds, sr), dtype=np.float32) / float(sr)


def silence(seconds: float, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A silent buffer."""
    return np.zeros(n_samples(seconds, sr), dtype=np.float32)


# ── Random ──────────────────────────────────────────────────────────────────


def rng(seed: int) -> np.random.Generator:
    """A seeded generator.

    Asset generation must be reproducible: a rebuild has to produce the same
    bytes, or every regeneration shows up as a spurious diff and nobody can tell
    whether a sound actually changed.
    """
    return np.random.default_rng(seed)


# ── Oscillators ─────────────────────────────────────────────────────────────


def sine(freq: float, seconds: float, phase: float = 0.0, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A sine wave."""
    return np.sin(2.0 * np.pi * freq * t_axis(seconds, sr) + phase).astype(np.float32)


def sweep(f0: float, f1: float, seconds: float, log: bool = True, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A frequency sweep, logarithmic by default.

    Logarithmic matches how pitch is perceived, so a log sweep reads as a single
    gesture where a linear one reads as accelerating.
    """
    t = t_axis(seconds, sr)
    method = "logarithmic" if log and f0 > 0.0 and f1 > 0.0 else "linear"
    return signal.chirp(t, f0=max(f0, 1e-3), f1=max(f1, 1e-3), t1=seconds, method=method).astype(np.float32)


def saw(freq: float, seconds: float, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A band-limited-ish sawtooth."""
    return signal.sawtooth(2.0 * np.pi * freq * t_axis(seconds, sr)).astype(np.float32)


def square(freq: float, seconds: float, duty: float = 0.5, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A square wave with adjustable duty cycle."""
    return signal.square(2.0 * np.pi * freq * t_axis(seconds, sr), duty=duty).astype(np.float32)


def triangle(freq: float, seconds: float, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A triangle wave."""
    return signal.sawtooth(2.0 * np.pi * freq * t_axis(seconds, sr), width=0.5).astype(np.float32)


# ── Noise ───────────────────────────────────────────────────────────────────


def white(seconds: float, seed: int, sr: int = SAMPLE_RATE) -> np.ndarray:
    """White noise in [-1, 1]."""
    return rng(seed).uniform(-1.0, 1.0, n_samples(seconds, sr)).astype(np.float32)


def pink(seconds: float, seed: int, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Pink noise (-3 dB/octave), via the Voss-McCartney method.

    The workhorse for anything airy: breath, wind, the hiss under a drone. Flat
    per octave, so it sits behind other material without the harshness of white.
    """
    n = n_samples(seconds, sr)
    g = rng(seed)
    rows = 16
    out = np.zeros(n, dtype=np.float64)
    # Each row updates half as often as the one above it, so summing them gives
    # equal energy per octave.
    for r in range(rows):
        step = 1 << r
        count = n // step + 1
        values = g.uniform(-1.0, 1.0, count)
        out += np.repeat(values, step)[:n]
    out /= rows
    # The slowest rows hold only one or two values across the whole buffer, so they
    # contribute a random constant — a DC offset that eats headroom and is
    # inaudible, hence easy to ship by accident. Nothing below ~20 Hz is useful in
    # game audio, so removing it costs nothing.
    return _safe_norm(highpass(_safe_norm(out).astype(np.float32), DC_BLOCK_HZ, order=2, sr=sr)).astype(np.float32)


def brown(seconds: float, seed: int, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Brown noise (-6 dB/octave). Rumble, distant thunder, the low bed of a drone."""
    n = white(seconds, seed, sr).astype(np.float64)
    out = signal.detrend(np.cumsum(n))  # integrating white noise drifts; drop the ramp
    # detrend removes the linear trend but leaves low-frequency wander that still
    # reads as DC over a short clip. See the note in `pink`.
    return _safe_norm(highpass(_safe_norm(out).astype(np.float32), DC_BLOCK_HZ, order=2, sr=sr)).astype(np.float32)


# ── Envelopes ───────────────────────────────────────────────────────────────


def adsr(
    seconds: float,
    attack: float = 0.01,
    decay: float = 0.1,
    sustain: float = 0.7,
    release: float = 0.2,
    sr: int = SAMPLE_RATE,
) -> np.ndarray:
    """A classic ADSR envelope, clamped to fit `seconds`."""
    total = n_samples(seconds, sr)
    a = min(n_samples(attack, sr), total)
    d = min(n_samples(decay, sr), total - a)
    r = min(n_samples(release, sr), total - a - d)
    s = max(0, total - a - d - r)

    return np.concatenate([
        np.linspace(0.0, 1.0, a, endpoint=False, dtype=np.float32) if a else np.empty(0, np.float32),
        np.linspace(1.0, sustain, d, endpoint=False, dtype=np.float32) if d else np.empty(0, np.float32),
        np.full(s, sustain, dtype=np.float32),
        np.linspace(sustain, 0.0, r, dtype=np.float32) if r else np.empty(0, np.float32),
    ])[:total]


def exp_decay(seconds: float, tau: float, sr: int = SAMPLE_RATE) -> np.ndarray:
    """An exponential decay with time constant `tau`.

    The right envelope for anything struck. A linear fade on an impact sounds
    synthetic because real resonances lose energy proportionally to what is left.
    """
    return np.exp(-t_axis(seconds, sr) / max(tau, 1e-4)).astype(np.float32)


def fade(buf: np.ndarray, fade_in: float = 0.005, fade_out: float = 0.02, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Applies short fades to both ends.

    Always fade. A buffer that starts or ends on a non-zero sample produces an
    audible click, and in a quiet horror mix a click is the loudest thing there.
    """
    out = buf.astype(np.float32).copy()
    fi = min(n_samples(fade_in, sr), len(out) // 2)
    fo = min(n_samples(fade_out, sr), len(out) // 2)
    if fi:
        out[:fi] *= np.linspace(0.0, 1.0, fi, dtype=np.float32)
    if fo:
        out[-fo:] *= np.linspace(1.0, 0.0, fo, dtype=np.float32)
    return out


# ── Filters ─────────────────────────────────────────────────────────────────


def _nyq(sr: int) -> float:
    return 0.5 * sr


def lowpass(buf: np.ndarray, cutoff: float, order: int = 4, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Zero-phase Butterworth low-pass."""
    wn = min(max(cutoff / _nyq(sr), 1e-5), 0.999)
    b, a = signal.butter(order, wn, btype="low")
    return signal.filtfilt(b, a, buf).astype(np.float32)


def highpass(buf: np.ndarray, cutoff: float, order: int = 4, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Zero-phase Butterworth high-pass."""
    wn = min(max(cutoff / _nyq(sr), 1e-5), 0.999)
    b, a = signal.butter(order, wn, btype="high")
    return signal.filtfilt(b, a, buf).astype(np.float32)


def bandpass(buf: np.ndarray, low: float, high: float, order: int = 4, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Zero-phase Butterworth band-pass."""
    lo = min(max(low / _nyq(sr), 1e-5), 0.998)
    hi = min(max(high / _nyq(sr), lo + 1e-4), 0.999)
    b, a = signal.butter(order, [lo, hi], btype="band")
    return signal.filtfilt(b, a, buf).astype(np.float32)


def resonator(buf: np.ndarray, freq: float, q: float = 30.0, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A single resonant peak. Stack these to give noise a pitched character."""
    w0 = min(max(freq / _nyq(sr), 1e-5), 0.999)
    b, a = signal.iirpeak(w0, q)
    return signal.lfilter(b, a, buf).astype(np.float32)


# ── Material and impact synthesis ───────────────────────────────────────────


@dataclass(frozen=True)
class Mode:
    """One resonant mode of a struck object.

    A physical object rings at several frequencies at once, each fading at its
    own rate. That set of rates is what the ear reads as "wood" versus "tile" —
    which is precisely the distinction §12 asks the Listener to make.
    """

    freq: float
    """Frequency in Hz."""

    tau: float
    """Decay time constant in seconds. Short reads as dead and dull, long as ringing."""

    amp: float = 1.0
    """Relative amplitude."""


def modal_impact(
    modes: Sequence[Mode],
    seconds: float,
    seed: int,
    noise_amount: float = 0.35,
    noise_tau: float = 0.012,
    sr: int = SAMPLE_RATE,
) -> np.ndarray:
    """Synthesises a struck-object sound from resonant modes plus a contact transient.

    This is the core of the footstep set. §12 turns floor material into a
    gameplay signal — five surfaces the Listener must tell apart — so the five
    sounds need controlled, deliberate contrast rather than whatever a sample
    pack happened to contain.

    `modes` gives the body of the sound; `noise_amount` adds the brief broadband
    scrape of contact, without which a footstep sounds like a tuned bell.
    """
    out = np.zeros(n_samples(seconds, sr), dtype=np.float64)

    for i, m in enumerate(modes):
        # Detune each mode slightly per-instance so repeated steps are not clones.
        jitter = 1.0 + rng(seed + i * 977).normal(0.0, 0.004)
        osc = sine(m.freq * jitter, seconds, phase=float(rng(seed + i * 131).uniform(0, 2 * np.pi)), sr=sr)
        out += (osc * exp_decay(seconds, m.tau, sr)).astype(np.float64) * m.amp

    if noise_amount > 0.0:
        transient = white(seconds, seed + 7919, sr) * exp_decay(seconds, noise_tau, sr)
        # Shape the transient toward the modes' register so it reads as the same object.
        centre = float(np.mean([m.freq for m in modes])) if len(modes) else 1000.0
        transient = bandpass(transient, max(120.0, centre * 0.4), min(_nyq(sr) * 0.95, centre * 6.0), sr=sr)
        out += transient.astype(np.float64) * noise_amount

    return fade(_safe_norm(out).astype(np.float32), 0.0005, 0.01, sr)


# ── Space ───────────────────────────────────────────────────────────────────


def reverb(
    buf: np.ndarray,
    seconds: float = 1.6,
    mix: float = 0.3,
    seed: int = 11,
    damping: float = 4200.0,
    sr: int = SAMPLE_RATE,
) -> np.ndarray:
    """Convolution reverb against a synthesised impulse response.

    Used sparingly and deliberately. §12 gives zone B tiled floors that ring, so
    reverb is part of how a zone identifies itself — it should differ per zone
    rather than being a global polish pass.

    Baking reverb into a positional clip is normally wrong, since Unity's 3D
    attenuation then applies to the tail too. Use this for ambience beds and
    non-diegetic stingers; leave footsteps dry and let the engine place them.
    """
    n = n_samples(seconds, sr)
    ir = rng(seed).normal(0.0, 1.0, n).astype(np.float64)
    ir *= np.exp(-np.arange(n) / (seconds * sr / 4.0))
    ir = lowpass(ir.astype(np.float32), damping, sr=sr).astype(np.float64)
    ir[0] = 1.0

    wet = signal.fftconvolve(buf.astype(np.float64), ir, mode="full")[: len(buf)]
    wet = _safe_norm(wet)
    return _safe_norm((1.0 - mix) * buf.astype(np.float64) + mix * wet).astype(np.float32)


def comb(buf: np.ndarray, delay_s: float, feedback: float = 0.5, sr: int = SAMPLE_RATE) -> np.ndarray:
    """A feedback comb filter. Metallic ring, pipes, small hard spaces."""
    d = max(1, n_samples(delay_s, sr))
    out = buf.astype(np.float64).copy()
    for i in range(d, len(out)):
        out[i] += feedback * out[i - d]
    return _safe_norm(out).astype(np.float32)


# ── Shaping ─────────────────────────────────────────────────────────────────


def saturate(buf: np.ndarray, drive: float = 2.0) -> np.ndarray:
    """Soft-clipping saturation. Adds weight and grit without hard clipping."""
    return np.tanh(buf.astype(np.float32) * max(drive, 1e-3)).astype(np.float32)


def pitch_shift(buf: np.ndarray, semitones: float, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Resampling pitch shift. Length changes with pitch, as on tape.

    Good for making variants of one source (a monster growl at several pitches).
    Not a formant-preserving shift — do not use it to pitch speech.
    """
    ratio = 2.0 ** (semitones / 12.0)
    target = max(1, int(round(len(buf) / ratio)))
    return signal.resample(buf, target).astype(np.float32)


def stretch(buf: np.ndarray, factor: float) -> np.ndarray:
    """Resamples to `factor` times the length, changing pitch with it."""
    return signal.resample(buf, max(1, int(round(len(buf) * factor)))).astype(np.float32)


def tremolo(buf: np.ndarray, rate: float, depth: float = 0.5, sr: int = SAMPLE_RATE) -> np.ndarray:
    """Amplitude modulation. Slow rates make something feel alive and breathing."""
    lfo = 1.0 - depth * 0.5 * (1.0 - np.cos(2.0 * np.pi * rate * t_axis(len(buf) / sr, sr)))
    return (buf.astype(np.float32) * lfo[: len(buf)].astype(np.float32)).astype(np.float32)


# ── Assembly ────────────────────────────────────────────────────────────────


def mix(*buffers: np.ndarray, gains: Sequence[float] | None = None) -> np.ndarray:
    """Sums buffers, zero-padding to the longest, then normalises."""
    if not buffers:
        return np.zeros(1, dtype=np.float32)
    length = max(len(b) for b in buffers)
    acc = np.zeros(length, dtype=np.float64)
    for i, b in enumerate(buffers):
        g = 1.0 if gains is None else float(gains[i])
        acc[: len(b)] += b.astype(np.float64) * g
    return _safe_norm(acc).astype(np.float32)


def concat(*buffers: np.ndarray) -> np.ndarray:
    """Joins buffers end to end."""
    return np.concatenate([b.astype(np.float32) for b in buffers]) if buffers else np.zeros(1, np.float32)


def place(canvas: np.ndarray, buf: np.ndarray, at_seconds: float, gain: float = 1.0,
          sr: int = SAMPLE_RATE) -> np.ndarray:
    """Adds `buf` into `canvas` at a time offset, in place. Clips at the canvas end."""
    start = n_samples(at_seconds, sr) if at_seconds > 0 else 0
    if start >= len(canvas):
        return canvas
    end = min(len(canvas), start + len(buf))
    canvas[start:end] += buf[: end - start] * gain
    return canvas


def _safe_norm(buf: np.ndarray) -> np.ndarray:
    """Peak-normalises to 1.0, leaving an all-zero buffer alone."""
    peak = float(np.max(np.abs(buf))) if len(buf) else 0.0
    return buf if peak < 1e-12 else buf / peak


def normalize(buf: np.ndarray, headroom_db: float = DEFAULT_HEADROOM_DB) -> np.ndarray:
    """Peak-normalises to `headroom_db` below full scale."""
    return (_safe_norm(buf.astype(np.float64)) * db_to_gain(headroom_db)).astype(np.float32)


# ── Output ──────────────────────────────────────────────────────────────────


def write_wav(
    path: str,
    buf: np.ndarray,
    sr: int = SAMPLE_RATE,
    headroom_db: float | None = DEFAULT_HEADROOM_DB,
    stereo: bool = False,
) -> str:
    """Writes a 16-bit PCM WAV, normalising and fading first.

    Set `stereo=False` (the default) for anything the game positions in the
    world: Unity will not spatialise a stereo clip, and the Listener and
    proximity voice both depend on 3D attenuation.
    """
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)

    out = buf.astype(np.float32)
    if headroom_db is not None:
        out = normalize(out, headroom_db)
    out = fade(out, 0.002, 0.006, sr)

    # Round rather than truncate, and clamp before casting so a value at exactly
    # 1.0 does not wrap to a large negative sample.
    pcm = np.clip(np.rint(out * 32767.0), -32768, 32767).astype("<i2")
    frames = np.column_stack([pcm, pcm]).ravel() if stereo else pcm

    with wave.open(path, "wb") as w:
        w.setnchannels(2 if stereo else 1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(frames.tobytes())

    return path


def read_wav(path: str) -> tuple[np.ndarray, int]:
    """Reads a 16-bit PCM WAV back as float32 in [-1, 1], plus its sample rate."""
    with wave.open(path, "rb") as w:
        if w.getsampwidth() != 2:
            raise ValueError(f"{path}: expected 16-bit PCM, got {w.getsampwidth() * 8}-bit")
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())
        data = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
        if w.getnchannels() == 2:
            data = data.reshape(-1, 2).mean(axis=1)
    return data, sr


# ── Verification ────────────────────────────────────────────────────────────


@dataclass(frozen=True)
class ClipReport:
    """Measurements used to check a generated clip is actually usable."""

    path: str
    seconds: float
    sample_rate: int
    channels: int
    peak: float
    rms: float
    dc_offset: float
    clipped_samples: int
    spectral_centroid: float

    @property
    def peak_db(self) -> float:
        """Peak level in dBFS."""
        return gain_to_db(self.peak)

    @property
    def is_silent(self) -> bool:
        """True when the clip carries essentially no signal."""
        return self.rms < 1e-4


def analyse(path: str) -> ClipReport:
    """Measures a written clip.

    Generation code can succeed and still produce something unusable — silence
    from a bad envelope, a DC offset that wastes headroom, a spectral centroid so
    close to another material's that the Listener could never tell them apart.
    Checking the file rather than trusting the generator is the only way to catch
    those, so every generator is expected to call this on its own output.
    """
    data, sr = read_wav(path)
    with wave.open(path, "rb") as w:
        channels = w.getnchannels()

    peak = float(np.max(np.abs(data))) if len(data) else 0.0
    rms = float(np.sqrt(np.mean(np.square(data.astype(np.float64))))) if len(data) else 0.0
    dc = float(np.mean(data)) if len(data) else 0.0
    clipped = int(np.sum(np.abs(data) >= 0.9995))

    mag = np.abs(np.fft.rfft(data.astype(np.float64) * np.hanning(len(data)))) if len(data) > 8 else np.zeros(1)
    freqs = np.fft.rfftfreq(len(data), 1.0 / sr) if len(data) > 8 else np.zeros(1)
    centroid = float(np.sum(freqs * mag) / np.sum(mag)) if np.sum(mag) > 0 else 0.0

    return ClipReport(
        path=path,
        seconds=len(data) / float(sr),
        sample_rate=sr,
        channels=channels,
        peak=peak,
        rms=rms,
        dc_offset=dc,
        clipped_samples=clipped,
        spectral_centroid=centroid,
    )


def assert_usable(
    path: str,
    min_seconds: float = 0.02,
    max_seconds: float = 120.0,
    max_dc: float = 0.02,
) -> ClipReport:
    """Analyses a clip and raises if it is unusable. Returns the report.

    Deliberately strict about silence and DC: both are easy to produce by
    accident and neither is audible as a bug until the game is mixed.
    """
    r = analyse(path)

    if r.sample_rate != SAMPLE_RATE:
        raise AssertionError(f"{path}: sample rate {r.sample_rate}, expected {SAMPLE_RATE}")
    if not (min_seconds <= r.seconds <= max_seconds):
        raise AssertionError(f"{path}: duration {r.seconds:.3f}s outside [{min_seconds}, {max_seconds}]")
    if r.is_silent:
        raise AssertionError(f"{path}: effectively silent (rms {r.rms:.2e}) — check the envelope")
    if r.peak > 0.999:
        raise AssertionError(f"{path}: peaks at full scale ({r.clipped_samples} samples) — no headroom left")
    if abs(r.dc_offset) > max_dc:
        raise AssertionError(f"{path}: DC offset {r.dc_offset:+.4f} exceeds {max_dc} — high-pass it")

    return r


def report_table(reports: Iterable[ClipReport]) -> str:
    """Formats reports as a table for a generator's console output."""
    rows = [f"{'clip':<42} {'sec':>6} {'peak dB':>8} {'rms':>7} {'centroid Hz':>12} {'ch':>3}"]
    rows.append("-" * 82)
    for r in reports:
        rows.append(
            f"{os.path.basename(r.path):<42} {r.seconds:>6.3f} {r.peak_db:>8.1f} "
            f"{r.rms:>7.4f} {r.spectral_centroid:>12.0f} {r.channels:>3}"
        )
    return "\n".join(rows)
