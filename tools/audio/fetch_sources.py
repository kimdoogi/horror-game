"""Vendors the CC0 field recordings the generators use as a base layer.

Every clip the game ships is still *built* by `gen_*.py`. What changed on
2026-08-09 is that the generators no longer start from silence for the families
where a microphone knows something synthesis does not: a real foot on a real
floor, a real hinge, a real drip. Those recordings are vendored here, curated and
trimmed, exactly the way `tools/textures/cc0/` vendors its scans — so a clean
checkout builds without a network and the licence of every byte is recorded.

**Why a base layer rather than a sample library.** `synth.modal_impact` models a
struck object as a sum of decaying sinusoids. That is a very good model of *one*
resonant body and a very poor model of the things a footstep actually is: a
heel arriving slightly before a sole, several hundred stones re-seating against
each other, the floor under the floor. §12 makes floor material a gameplay
channel, so what the Listener needs is not "a plausible tile sound" but the
specific irregular envelope real contact has. The recording supplies that
envelope; `gen_footsteps.py` still supplies the per-surface EQ, the per-actor
register, the variation and the loudness landing, because those are the parts
that are *designed* rather than observed.

**Provenance.** `docs/ASSETS.md` states the rule: §13 ships this on Steam and an
asset of unclear provenance is a legal problem rather than a mixing problem. So:

* Everything here is **CC0 1.0** — public-domain-equivalent, commercial use,
  no attribution required.
* Everything here comes from **one** collection whose release is documented by
  an institution rather than asserted by an anonymous uploader: the USC Optical
  Sound Effects Library, donated to the USC HMH Foundation Moving Image Archive,
  digitised by Craig Smith (CalArts) and published to the Internet Archive under
  CC0 by Archive.org staff.
* Archive.org's `licenseurl` field is set by whoever uploaded the item, not
  verified by Archive.org. That makes most "CC0" audio there worthless as a
  licence: the search that found this collection also returned a Skywalker Sound
  pack tagged CC0 whose description is the two words "I Own Nothing." Nothing is
  taken from an item unless the uploader is the archive's own staff and the
  collection carries a written donation history. See `PROVENANCE.json`.

Run it only when the source list below changes; the vendored output is committed:

    tools/audio/.venv/bin/python tools/audio/fetch_sources.py
    tools/audio/.venv/bin/python tools/audio/fetch_sources.py --only gravel ambience
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import urllib.parse
import wave
from dataclasses import dataclass, field
from typing import Dict, List, Sequence, Tuple

import numpy as np
from scipy import signal as sg

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE_DIR = os.path.join(HERE, "source")
CACHE_DIR = os.environ.get(
    "HORROR_AUDIO_SOURCE_CACHE",
    os.path.join(tempfile.gettempdir(), "horror-audio-source-cache"),
)
"""Where the untrimmed originals land. Deliberately outside the tree: they are
~90 MB against ~2 MB of vendored extract, and nothing in the build needs them
once `source/` exists."""

SAMPLE_RATE = 48000

LICENSE = "CC0 1.0 (public-domain-equivalent, commercial use, no attribution required)"
COLLECTION = "USC Optical Sound Effects Library (archive.org/details/usc-sound-effect-archive)"
COLLECTION_NOTE = (
    "Red Library: first-generation copies of 1930s-40s nitrate optical sound effects "
    "collected by a Hollywood sound editor, donated to USC, transferred to tape by USC "
    "Cinema students in the early 1970s, digitised by Craig Smith (Academic Sound "
    "Coordinator, CalArts School of Film/Video) and uploaded to the Internet Archive by "
    "jscott@archive.org under CC0 1.0. Bandwidth is optical-era (99% of energy under "
    "~5-8 kHz) and the transfers are unrestored — which is why these are a base layer "
    "under the generators' own high band, not a drop-in replacement."
)


# ── What we take, and what it feeds ─────────────────────────────────────────


@dataclass(frozen=True)
class Take:
    """One source recording, and the slot in the game it supplies."""

    category: str
    """Vendored subdirectory — mirrors the Assets/Audio family it feeds."""

    key: str
    """Output stem. For footsteps this is the §12 surface key."""

    item: str
    """archive.org item identifier."""

    name: str
    """File path inside the item."""

    feeds: str
    """Which generated clips this ends up inside. Recorded in PROVENANCE.json."""

    keep: int = 6
    """How many individual contacts to vendor. More variants than the generator
    has (4) so the seeded picker has something to choose between per actor."""

    window: float = 0.55
    """Seconds captured after the onset. Long enough for the surface's own ring;
    the extractor trims the actual tail per slice."""

    min_gap: float = 0.16
    """Onset detector's refractory period, in seconds."""

    hp: float = 32.0
    """A high-pass before anything else. Optical transfers carry rumble well
    below anything a footstep contains, and it would otherwise dominate the
    slice-scoring energy measurements."""

    denoise: float = 1.6
    """Over-subtraction factor for the spectral gate. These transfers run 15-40 dB
    SNR; the game's quietest footstep is -44 dBFS RMS, so untreated hiss would
    arrive louder than a carpeted step."""

    max_tonality: float = 0.42
    """Reject a contact whose strongest third-octave band holds more than this
    fraction of its energy. See `step_likeness` — the number that this exists to
    exclude measured 0.66."""

    max_attack: float = 0.030
    """Reject a contact that takes longer than this to reach its peak. A foot
    landing is fast; a door, a voice or a passing car is not."""

    min_seconds: float = 0.06
    """Reject a slice shorter than this after trimming.

    A footstep can legitimately be 70 ms. A *creak* cannot: it is a sustained
    stick-slip, and the first pass here vendored a 65 ms one because the score
    rewards a quiet tail and a short click has nothing but tail. Pitched into a
    1.75 s clip and levelled by energy, that click became a single transient
    loud enough to take the whole clip's headroom — `sfx_creak_distant_04`
    measured 18 dB down in RMS against its own siblings."""


TAKES: Tuple[Take, ...] = (
    # ── Footsteps: §12's eight surfaces. One recording each, chosen on measured
    #    noise floor rather than on the description, because every slice is
    #    denoised and a quiet source is the only thing that survives that well.
    Take("footsteps", "concrete", "Red_Library_Footsteps_4",
         "R27-45-Footsteps on Hard Surface.wav",
         "step_concrete_{player_walk,player_run,monster_step}_01..04 — contact body"),
    Take("footsteps", "wood", "Red_Library_Footsteps_4",
         "R19-07-Footsteps on wood with Leather.wav",
         "step_wood_* — contact body under the synthesised 삐걱 stick-slip"),
    Take("footsteps", "metal", "Red_Library_Footsteps_4",
         "R11-41-Walking Up Metal Stairs.wav",
         "step_metal_* — contact body under the synthesised comb 울림"),
    Take("footsteps", "tile", "Red_Library_Footsteps_1",
         "R10-22-Shoes on Tile Floor.wav",
         "step_tile_* — contact body under the synthesised early reflections"),
    Take("footsteps", "gravel", "Red_Library_Footsteps_3",
         "R11-03-Walking on Gravel.wav",
         "step_gravel_* — the aggregate re-seating, which grain synthesis renders "
         "as a scatter with no substrate under it"),
    Take("footsteps", "earth", "Red_Library_Footsteps_4",
         "R19-19-Slow Footsteps Deep Dirt.wav",
         "step_earth_* — the compression of a mass that answers as one body"),
    Take("footsteps", "carpet", "Red_Library_Footsteps_2",
         "R10-42-Muffled Steps on Floor or Stairs.wav",
         "step_carpet_* — a muffled contact, the hardest thing in the set to "
         "synthesise because almost nothing is left after the damping"),

    # ── Items: the three door gestures. §04 makes opening a door the Listener's
    #    own blindness, so these are heard constantly and heard on purpose.
    Take("items", "hinge", "Red_Library_Doors",
         "R18-51-Door Hinge Squeaks.wav", keep=6, window=1.6, min_gap=0.5,
         feeds="door_open_01..02 — the binding creak. `hinge_creak` models it as a "
               "modulated sweep, which is the mechanism and not the sound: a hinge "
               "squeals because it is repeatedly seizing, and the pitch jumps rather "
               "than glides",
         denoise=1.4, max_tonality=0.75, max_attack=1.20, min_seconds=0.35),
    Take("items", "doorbody", "Red_Library_Doors",
         "R09-24-Doors Being Opened and Shut.wav", keep=6, window=1.1, min_gap=0.35,
         feeds="door_close_01..02 — the leaf, the strike plate and the frame "
               "answering as one object rather than as three placed impacts",
         denoise=1.5, max_tonality=0.55, max_attack=0.090, min_seconds=0.15),
    Take("items", "bolt", "Red_Library_Doors",
         "R09-44-Unlocking a Door.wav", keep=6, window=0.8, min_gap=0.25,
         feeds="door_lock_01..02 — §04's Engineer closing a §12 순환로. The "
               "mechanism noise before the bolt is a dozen small metal parts, which "
               "is a texture rather than a set of clicks",
         denoise=1.6, max_tonality=0.55, max_attack=0.120, min_seconds=0.10),

    # ── Ambience one-shots: the two positional families §03 and §06 lean on.
    Take("ambience", "creak", "GOLD_TAPE_27_Creaks",
         "G27-12-Quiet Wooden Creak.wav", keep=8, window=2.4, min_gap=0.9,
         feeds="sfx_creak_distant_01..05 — the structural creak §06's 정지 needs "
               "so silence reads as ominous rather than as dropped audio. Stick-slip "
               "is the one thing here synthesis cannot fake: its pitch wanders because "
               "the wood is failing to slide, and no envelope reproduces that",
         denoise=1.4, max_tonality=0.80, max_attack=0.90, min_seconds=0.55),
    Take("ambience", "drip", "GOLD_TAPE_53_54_Water",
         "G53-16-Water Drip.wav", keep=8, window=0.9, min_gap=0.30,
         feeds="sfx_water_drip_01..04 — §03's worked clue is literally "
               "'그것은 물이 있는 층에 있다', so the drip is information and its "
               "cavity resonance has to sound like a real room, not a sine burst",
         denoise=1.5, max_tonality=0.70, max_attack=0.020, min_seconds=0.10),
)


# ── Reading and writing ─────────────────────────────────────────────────────


def _read_wav_any(path: str) -> Tuple[np.ndarray, int]:
    """Reads a WAV of any common bit depth as float64 mono.

    `synth.read_wav` deliberately refuses anything but 16-bit, because that is
    what the game ships. The sources are 24-bit, so this reader exists only here.
    """
    with wave.open(path, "rb") as w:
        sr, ch, sw, n = w.getframerate(), w.getnchannels(), w.getsampwidth(), w.getnframes()
        raw = w.readframes(n)

    if sw == 1:
        d = (np.frombuffer(raw, dtype=np.uint8).astype(np.float64) - 128.0) / 128.0
    elif sw == 2:
        d = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    elif sw == 3:
        a = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = a[:, 0] | (a[:, 1] << 8) | (a[:, 2] << 16)
        d = np.where(v & 0x800000, v - (1 << 24), v).astype(np.float64) / 8388608.0
    elif sw == 4:
        d = np.frombuffer(raw, dtype="<i4").astype(np.float64) / 2147483648.0
    else:
        raise ValueError(f"{path}: unsupported sample width {sw * 8}-bit")

    if ch > 1:
        d = d.reshape(-1, ch).mean(axis=1)
    return d, sr


def _write_wav16(path: str, buf: np.ndarray, sr: int = SAMPLE_RATE) -> int:
    """Writes 48 kHz 16-bit mono — the one format the generators can read back."""
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    pcm = np.clip(np.rint(buf * 32767.0), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    return os.path.getsize(path)


def sha1(path: str) -> str:
    h = hashlib.sha1()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def download(item: str, name: str) -> str:
    """Fetches a source file into the cache. The cache is *not* committed."""
    os.makedirs(CACHE_DIR, exist_ok=True)
    dest = os.path.join(CACHE_DIR, f"{item}__{name.replace('/', '_')}")
    if os.path.exists(dest) and os.path.getsize(dest) > 4096:
        return dest
    url = f"https://archive.org/download/{item}/{urllib.parse.quote(name)}"
    print(f"    fetching {url}")
    r = subprocess.run(["curl", "-sL", "--max-time", "600", "-o", dest, url],
                       capture_output=True)
    if r.returncode != 0 or not os.path.exists(dest) or os.path.getsize(dest) < 4096:
        raise RuntimeError(f"download failed: {url}")
    return dest


# ── Extraction ──────────────────────────────────────────────────────────────


def onsets(d: np.ndarray, sr: int, min_gap: float) -> np.ndarray:
    """Onset times (in samples) by half-wave-rectified spectral flux.

    Flux rather than a level threshold: a level gate on a noisy optical transfer
    fires on the hiss swelling, while flux only fires when the *spectrum*
    changes, which is what an impact is.
    """
    hop, win = 256, 1024
    n = (len(d) - win) // hop
    if n < 4:
        return np.zeros(0, dtype=int)
    frames = np.lib.stride_tricks.as_strided(
        d, shape=(n, win), strides=(d.strides[0] * hop, d.strides[0])
    ) * np.hanning(win)
    mag = np.abs(np.fft.rfft(frames, axis=1))
    flux = np.maximum(0.0, np.diff(mag, axis=0)).sum(axis=1)
    if not len(flux):
        return np.zeros(0, dtype=int)

    iqr = np.percentile(flux, 75) - np.percentile(flux, 25)
    thr = np.median(flux) + 2.2 * (iqr + 1e-12)
    gap = min_gap * sr / hop
    picks: List[int] = []
    last = -1e9
    for i in range(1, len(flux) - 1):
        if flux[i] > thr and flux[i] >= flux[i - 1] and flux[i] >= flux[i + 1] and i - last > gap:
            picks.append(i)
            last = i
    return (np.asarray(picks, dtype=int) * hop).astype(int)


def noise_profile(d: np.ndarray, sr: int) -> np.ndarray:
    """Magnitude spectrum of the recording's own hiss.

    Taken as the 20th percentile per bin across the whole file rather than from a
    hand-picked "silent" region: these transfers have no silent region, and the
    per-bin low percentile is exactly the stationary part.
    """
    f, t, Z = sg.stft(d, fs=sr, nperseg=1024, noverlap=768)
    return np.percentile(np.abs(Z), 20, axis=1)


def denoise(d: np.ndarray, sr: int, profile: np.ndarray, over: float) -> np.ndarray:
    """Spectral-subtraction gate with a soft floor.

    A hard subtraction leaves "musical noise" — isolated bins surviving at random
    and warbling. The floor (`-24 dB` of the estimate) plus the two-frame
    smoothing below keeps the residual as a quiet, stationary hiss, which is what
    the ear ignores.
    """
    f, t, Z = sg.stft(d, fs=sr, nperseg=1024, noverlap=768)
    mag, phase = np.abs(Z), np.angle(Z)
    est = over * profile[:, None]
    gain = np.clip((mag - est) / np.maximum(mag, 1e-12), 10 ** (-24 / 20), 1.0)
    # Smooth the gain in time so the gate does not chatter on the transient.
    gain = sg.lfilter([0.45, 0.35, 0.20], [1.0], gain, axis=1)
    _, out = sg.istft(gain * mag * np.exp(1j * phase), fs=sr, nperseg=1024, noverlap=768)
    return np.asarray(out[: len(d)], dtype=np.float64)


def envelope(s: np.ndarray, sr: int, block_s: float = 0.0025) -> Tuple[np.ndarray, int]:
    """Block RMS envelope, and the block size in samples."""
    block = max(1, int(block_s * sr))
    n = (len(s) // block) * block
    if n < block * 2:
        return np.abs(s[:1]), block
    return np.sqrt((s[:n].reshape(-1, block) ** 2).mean(axis=1)), block


def trim_tail(s: np.ndarray, sr: int, floor_db: float = -42.0) -> np.ndarray:
    """Cuts the slice where its decay reaches its own residual floor.

    The first version of this measured only against the slice's peak, and on a
    denoised optical transfer the decay never gets 42 dB down — the spectral
    gate's own floor holds it up. So every slice came back the full window long,
    carrying a second person's footstep in its tail, and the tail-quietness score
    below then preferred whichever slice happened to be a resonant clang rather
    than a contact. Both halves of that are fixed by measuring the residual and
    cutting at whichever target is *higher*.
    """
    env, block = envelope(s, sr)
    if len(env) < 4:
        return s
    pk = float(env.max())
    if pk <= 1e-9:
        return s
    resid = float(np.percentile(env, 12))
    target = max(pk * (10 ** (floor_db / 20)), resid * 2.2)
    hold = max(1, int(0.025 * sr / block))
    start = int(np.argmax(env))
    for i in range(start + 1, len(env) - hold):
        if np.all(env[i: i + hold] < target):
            return s[: (i + hold) * block]
    return s


def step_likeness(s: np.ndarray, sr: int) -> Dict[str, float]:
    """How much this slice behaves like a foot landing, rather than like anything
    else the microphone caught between two footsteps.

    Three independent measurements, because tail quietness alone is a trap: the
    quietest-tailed slice in the metal recording was a 362 Hz handrail ring with
    the next spectral peak 13 dB down, which scores beautifully and is not a
    footstep.

    * `attack` — seconds from 10% to peak envelope. Contact is fast.
    * `front` — fraction of the energy in the first 40%. A step front-loads.
    * `tonality` — energy fraction in the single strongest third-octave band. A
      struck floor spreads; a ringing object does not.
    """
    env, block = envelope(s, sr)
    if len(env) < 4:
        return dict(attack=1.0, front=0.0, tonality=1.0)
    ipk = int(np.argmax(env))
    pk = float(env[ipk])
    lo = np.where(env[: ipk + 1] >= pk * 0.1)[0]
    attack = (ipk - int(lo[0])) * block / sr if len(lo) else ipk * block / sr

    e = env ** 2
    cut = max(1, int(len(e) * 0.4))
    front = float(e[:cut].sum() / max(e.sum(), 1e-30))

    n = len(s)
    mag = np.abs(np.fft.rfft(s * np.hanning(n)))
    frq = np.fft.rfftfreq(n, 1.0 / sr)
    p = mag ** 2
    tot = max(p.sum(), 1e-30)
    best = 0.0
    f = 40.0
    while f < sr * 0.45:
        hi = f * 2 ** (1 / 3)
        best = max(best, float(p[(frq >= f) & (frq < hi)].sum() / tot))
        f = hi
    return dict(attack=float(attack), front=front, tonality=best)


def extract(take: Take, path: str) -> Tuple[List[np.ndarray], Dict[str, float]]:
    """Slices, denoises and ranks the individual contacts in one recording."""
    d, sr = _read_wav_any(path)
    if sr != SAMPLE_RATE:
        d = sg.resample(d, int(round(len(d) * SAMPLE_RATE / sr)))
        sr = SAMPLE_RATE

    b, a = sg.butter(2, take.hp / (sr / 2), "high")
    d = sg.filtfilt(b, a, d)

    prof = noise_profile(d, sr)
    clean = denoise(d, sr, prof, take.denoise)

    marks = onsets(d, sr, take.min_gap)
    pre = int(0.006 * sr)
    span = int(take.window * sr)

    scored: List[Tuple[float, float, np.ndarray]] = []
    rejected = {"short": 0, "crowded": 0, "quiet": 0, "tonal": 0, "slow": 0}
    for k, m in enumerate(marks):
        start, end = max(0, m - pre), min(len(clean), m + span)
        if end - start < int(0.05 * sr):
            rejected["short"] += 1
            continue
        # Isolation: nothing else may land inside this window after the first 110 ms.
        nxt = marks[k + 1] if k + 1 < len(marks) else len(clean)
        if nxt - m < int(0.11 * sr):
            rejected["crowded"] += 1
            continue
        s = trim_tail(clean[start:end], sr)
        if len(s) < int(take.min_seconds * sr):
            rejected["short"] += 1
            continue
        pk = float(np.max(np.abs(s)))
        if pk < 1e-4:
            rejected["quiet"] += 1
            continue

        shape = step_likeness(s, sr)
        if shape["tonality"] > take.max_tonality:
            rejected["tonal"] += 1
            continue
        if shape["attack"] > take.max_attack:
            rejected["slow"] += 1
            continue

        tail = s[int(len(s) * 0.85):]
        floor = float(np.sqrt((tail ** 2).mean())) if len(tail) else 1e-9
        snr = 20 * np.log10(pk / max(floor, 1e-9))
        # Rank on cleanliness first, but pay for being front-loaded and against
        # being tonal, so the picker cannot buy a high score with a ring.
        score = snr + 24.0 * shape["front"] - 40.0 * shape["tonality"]
        scored.append((score, snr, s / pk * 0.97))

    scored.sort(key=lambda x: -x[0])
    picks = [s for _, _, s in scored[: take.keep]]
    kept_snr = [snr for _, snr, _ in scored[: take.keep]]
    stats = dict(
        onsets=float(len(marks)),
        usable=float(len(scored)),
        best_snr=max(kept_snr) if kept_snr else 0.0,
        worst_kept_snr=min(kept_snr) if kept_snr else 0.0,
        source_seconds=len(d) / sr,
        rejected=rejected,
    )
    return picks, stats


# ── Driver ──────────────────────────────────────────────────────────────────


def run(only: Sequence[str] | None = None) -> int:
    takes = [t for t in TAKES if not only or t.key in only or t.category in only]
    if not takes:
        print("nothing selected", file=sys.stderr)
        return 1

    by_category: Dict[str, List[dict]] = {}
    total = 0

    for t in takes:
        print(f"  {t.category}/{t.key}: {t.item}/{t.name}")
        src = download(t.item, t.name)
        src_sha = sha1(src)
        picks, stats = extract(t, src)
        if len(picks) < 2:
            raise RuntimeError(f"{t.key}: only {len(picks)} usable contacts extracted")

        out_dir = os.path.join(SOURCE_DIR, t.category)
        files = []
        for i, s in enumerate(picks, start=1):
            # A short fade at both ends: the slice boundary is arbitrary and a
            # non-zero first sample is a click the generator would then band-shape
            # into something that sounds like the material.
            s = s.copy()
            fi = min(int(0.0015 * SAMPLE_RATE), len(s) // 4)
            fo = min(int(0.010 * SAMPLE_RATE), len(s) // 4)
            s[:fi] *= np.linspace(0.0, 1.0, fi)
            s[-fo:] *= np.linspace(1.0, 0.0, fo)
            p = os.path.join(out_dir, f"{t.key}_{i:02d}.wav")
            n = _write_wav16(p, s)
            total += n
            files.append({
                "file": f"{t.key}_{i:02d}.wav",
                "bytes": n,
                "seconds": round(len(s) / SAMPLE_RATE, 4),
                "sha1": sha1(p),
            })

        by_category.setdefault(t.category, []).append({
            "key": t.key,
            "source_item": t.item,
            "source_file": t.name,
            "source_url": f"https://archive.org/download/{t.item}/{urllib.parse.quote(t.name)}",
            "source_item_url": f"https://archive.org/details/{t.item}",
            "source_sha1": src_sha,
            "source_seconds": round(stats["source_seconds"], 2),
            "license": LICENSE,
            "collection": COLLECTION,
            "feeds": t.feeds,
            "extraction": {
                "onsets_detected": int(stats["onsets"]),
                "contacts_usable": int(stats["usable"]),
                "kept": len(files),
                "best_snr_db": round(stats["best_snr"], 1),
                "worst_kept_snr_db": round(stats["worst_kept_snr"], 1),
                "rejected": stats["rejected"],
                "denoise_oversubtraction": t.denoise,
                "format": "48 kHz 16-bit PCM mono",
            },
            "files": files,
        })
        print(f"    kept {len(files)}/{int(stats['usable'])} contacts "
              f"(snr {stats['best_snr']:.1f}..{stats['worst_kept_snr']:.1f} dB)")

    for category, entries in by_category.items():
        doc = {
            "license": LICENSE,
            "source": COLLECTION,
            "source_note": COLLECTION_NOTE,
            "verified": "2026-08-09 — licenseurl CC0 1.0 on every item, uploader "
                        "jscott@archive.org (Internet Archive staff), collection "
                        "usc-sound-effect-archive carries a written donation history",
            "note": "Vendored so the gen_*.py generators rebuild without a network. "
                    "These are trimmed single contacts, denoised and levelled; the "
                    "untrimmed originals are at the source_url of each entry. Each "
                    "generator falls back to fully-procedural if this directory is "
                    "missing, so a clean checkout still builds.",
            "rebuild": "tools/audio/.venv/bin/python tools/audio/fetch_sources.py",
            "assets": sorted(entries, key=lambda e: e["key"]),
        }
        p = os.path.join(SOURCE_DIR, category, "PROVENANCE.json")
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")
        total += os.path.getsize(p)
        print(f"  wrote {p}")

    print(f"\n  vendored {total} bytes under tools/audio/source/")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--only", nargs="*", help="surface keys or categories to refresh")
    args = ap.parse_args()
    return run(args.only)


if __name__ == "__main__":
    raise SystemExit(main())
