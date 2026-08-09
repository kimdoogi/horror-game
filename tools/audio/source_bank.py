"""Loads the vendored CC0 recordings the generators use as a base layer.

`synth.py` is the toolkit for making a sound out of nothing and stays that way.
This is the one place that knows a microphone was involved, so that:

* a generator asks for a base by name and gets `None` if it is not there, and
* a clean checkout with no `tools/audio/source/` still builds every clip, fully
  procedurally, with one printed line saying so.

That fallback is not politeness. `gen_*.py` are the source of truth for what the
game sounds like — `docs/ASSETS.md` §4 tells anyone rebuilding to run them — and
a generator that cannot run without a vendored binary would quietly make that
false. The recordings improve the result; they are not allowed to gate it.

See `fetch_sources.py` for where the recordings come from and why that source and
no other. Everything here is CC0 1.0.
"""

from __future__ import annotations

import os
from typing import Dict, List, Optional, Sequence, Tuple

import numpy as np
from scipy import signal

import synth

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE_DIR = os.environ.get("HORROR_AUDIO_SOURCE", os.path.join(HERE, "source"))

_cache: Dict[Tuple[str, str], List[np.ndarray]] = {}
_announced: set = set()


def load(category: str, key: str) -> List[np.ndarray]:
    """Every vendored contact for one key, loudest-first as written.

    Returns an empty list when the bank is missing, and says so once per key so a
    console log makes it obvious which families fell back to pure synthesis.
    """
    ck = (category, key)
    if ck in _cache:
        return _cache[ck]

    d = os.path.join(SOURCE_DIR, category)
    files = sorted(
        os.path.join(d, f) for f in os.listdir(d)
        if f.startswith(f"{key}_") and f.endswith(".wav")
    ) if os.path.isdir(d) else []

    bank: List[np.ndarray] = []
    for p in files:
        try:
            buf, sr = synth.read_wav(p)
        except Exception as exc:  # a corrupt vendored file must not break the build
            print(f"  ! source {os.path.basename(p)} unreadable ({exc}) — skipped")
            continue
        if sr != synth.SAMPLE_RATE:
            buf = signal.resample(buf, int(round(len(buf) * synth.SAMPLE_RATE / sr)))
        bank.append(buf.astype(np.float32))

    if not bank and ck not in _announced:
        _announced.add(ck)
        print(f"  · no vendored source for {category}/{key} — synthesising it whole")
    _cache[ck] = bank
    return bank


def available(category: str, key: str) -> bool:
    return bool(load(category, key))


def align(buf: np.ndarray, threshold: float = 0.18, lead: float = 0.002) -> np.ndarray:
    """Trims a slice so its attack sits `lead` seconds from the start.

    The extractor keeps 6 ms before the detected onset, and the detector fires a
    little early or late depending on how the spectrum moved. The synthesised
    body starts at sample zero, so without this the two contacts arrive at
    different times and the result reads as two footsteps rather than one.
    """
    if not len(buf):
        return buf
    pk = float(np.max(np.abs(buf)))
    if pk <= 1e-9:
        return buf
    hits = np.where(np.abs(buf) >= pk * threshold)[0]
    if not len(hits):
        return buf
    start = max(0, int(hits[0]) - synth.n_samples(lead))
    return buf[start:]


def shape(
    buf: np.ndarray,
    seconds: float,
    pitch: float = 1.0,
    seed: int = 0,
) -> np.ndarray:
    """Puts one vendored contact into a generated clip's register and length.

    `pitch` is a frequency ratio, applied by resampling — the same tape-style
    shift `synth.pitch_shift` does, and the right model here: a bigger, heavier
    thing striking the same floor really does excite it lower *and* longer, so
    the coupled length change is the physics rather than an artefact of the
    method.

    The result is faded and either truncated or zero-padded to `seconds`, since
    everything downstream in `build_step` assumes one fixed-length buffer.
    """
    out = align(buf)
    if pitch != 1.0:
        out = synth.stretch(out, 1.0 / max(pitch, 1e-3))

    # A small per-instance level and timing jitter, so four variants built from
    # the same six contacts do not repeat as a cycle of six.
    g = synth.rng(seed)
    skew = synth.n_samples(float(g.uniform(0.0, 0.004)))
    if skew:
        out = np.concatenate([np.zeros(skew, dtype=np.float32), out])

    n = synth.n_samples(seconds)
    if len(out) >= n:
        out = out[:n]
        # Truncation leaves a step at the end; fade only what was cut into.
        out = synth.fade(out, 0.0005, min(0.012, seconds * 0.25))
    else:
        out = np.concatenate([out, np.zeros(n - len(out), dtype=np.float32)])
    return out.astype(np.float32)


def pick(
    category: str,
    key: str,
    seed: int,
    seconds: float,
    pitch: float = 1.0,
) -> Optional[np.ndarray]:
    """One contact, chosen deterministically and put into register.

    Returns `None` when the bank is missing, which is the caller's cue to build
    the whole thing procedurally.
    """
    bank = load(category, key)
    if not bank:
        return None
    idx = int(synth.rng(seed).integers(0, len(bank)))
    return shape(bank[idx], seconds, pitch=pitch, seed=seed + 1)


def mix_real(body: np.ndarray, real: np.ndarray, amount: float) -> np.ndarray:
    """Adds a recording at `amount` times the synthesised body's **energy**.

    Deliberately not a peak-referenced blend. A recording and a synthesised
    gesture with the same peak do not carry remotely the same energy: the
    synthesised one is exactly as long as its envelope and nothing else, and the
    recording is that plus the floor's real decay plus the room it was in.
    Referenced by peak, the footstep recordings arrived +19 to +28 dB hot, which
    is how eight materials designed to sit 1.4x apart in §12 ended up inside
    1.2x of each other — the recordings were simply louder than the design.

    Energy is also the quantity the callers' `real_amount` should mean: 1.0 is
    "half of this sound is a microphone", which is a sentence someone tuning it
    can act on.
    """
    if amount <= 0.0:
        return body
    n = max(len(body), len(real))
    out = np.zeros(n, dtype=np.float32)
    out[: len(body)] += body
    rb = float(np.sqrt(np.mean(np.square(body.astype(np.float64))))) if len(body) else 0.0
    rr = float(np.sqrt(np.mean(np.square(real.astype(np.float64))))) if len(real) else 0.0
    if rb > 1e-12 and rr > 1e-12:
        out[: len(real)] += real * np.float32(amount * rb / rr)
    return out


def taper_tail(buf: np.ndarray, fraction: float = 0.34) -> np.ndarray:
    """Raised-cosine over the last `fraction` of a buffer.

    A recording does not know how long the clip it is joining is allowed to be —
    that length is a design decision (concrete is over before tile has started
    to ring, and the Listener uses that) while the recording just runs until the
    recordist's floor stopped. Truncating instead reads as the audio being
    switched off rather than decaying, which every generator here asserts
    against.
    """
    n = len(buf)
    if n < 4:
        return buf
    k = max(1, int(n * fraction))
    taper = np.ones(n, dtype=np.float32)
    taper[-k:] = (0.5 * (1.0 + np.cos(np.linspace(0.0, np.pi, k)))).astype(np.float32)
    return (buf * taper).astype(np.float32)


def spectral_tilt(buf: np.ndarray, k: float, f_ref: float = 1000.0) -> np.ndarray:
    """Multiplies the spectrum by (f / f_ref)**k. Negative k darkens.

    A single smooth exponent rather than a filter design, because the job is to
    move a centroid without introducing any feature of its own — a shelf or a
    peak here would be a resonance, and a resonance is how this file spells
    "material".
    """
    if abs(k) < 1e-4 or len(buf) < 8:
        return buf
    X = np.fft.rfft(buf.astype(np.float64))
    f = np.fft.rfftfreq(len(buf), 1.0 / synth.SAMPLE_RATE)
    g = (np.maximum(f, 25.0) / f_ref) ** k
    return np.fft.irfft(X * g, n=len(buf)).astype(np.float32)


def band_centroid(buf: np.ndarray, low: float, high: float) -> float:
    """Spectral centroid measured only inside a surface's own band."""
    if len(buf) < 8:
        return 0.0
    mag = np.abs(np.fft.rfft(buf.astype(np.float64) * np.hanning(len(buf))))
    f = np.fft.rfftfreq(len(buf), 1.0 / synth.SAMPLE_RATE)
    m = (f >= low) & (f <= high)
    tot = float(mag[m].sum())
    return float((f[m] * mag[m]).sum() / tot) if tot > 1e-20 else 0.0


def match_centroid(
    real: np.ndarray, target: float, low: float, high: float, limit: float = 6.0
) -> np.ndarray:
    """Tilts the recording until its in-band centroid matches the material's.

    This is the whole reason a real recording can be used at all here without
    dismantling §12. A microphone in front of a foot records the *room*, the
    shoe, and the recordist's mic placement along with the floor, and eight
    recordings made that way land far closer together in the spectrum than the
    eight designed materials do: dropped in raw at a level where they carry the
    contact, they pulled every surface toward the middle and collapsed earth
    against wood to 1.20x, under §12's 1.4x floor.

    So the recording is not allowed to bring its own spectral balance. It brings
    its envelope, its micro-timing, its inharmonicity and its substrate — all
    the things `modal_impact` cannot produce — and the *material* keeps saying
    where the energy sits, which is the part the design owns and the part the
    Listener reads. Solved by bisection on the tilt exponent because the centroid
    is monotone in it, and clamped so a pathological slice cannot ask for an
    80 dB slope across the band.
    """
    if target <= 0.0:
        return real
    lo, hi = -limit, limit
    for _ in range(14):
        mid = 0.5 * (lo + hi)
        c = band_centroid(spectral_tilt(real, mid), low, high)
        if c <= 0.0:
            return real
        if c < target:
            lo = mid
        else:
            hi = mid
    return spectral_tilt(real, 0.5 * (lo + hi))


def bank_size(category: str, key: str) -> int:
    return len(load(category, key))


def summary(pairs: Sequence[Tuple[str, str]]) -> str:
    """One line for a generator's header saying what it is building on."""
    have = [(c, k) for c, k in pairs if available(c, k)]
    if not have:
        return "sources: none vendored — fully procedural"
    counts = ", ".join(f"{k}×{bank_size(c, k)}" for c, k in have)
    return f"sources: CC0 base layer for {len(have)}/{len(pairs)} keys ({counts})"
