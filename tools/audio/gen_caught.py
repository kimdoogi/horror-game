"""잡혔다 — the one sting for being sent back to the start line.

Run:
    /Users/doogi/horror-game/tools/audio/.venv/bin/python \
        /Users/doogi/horror-game/tools/audio/gen_caught.py

Writes exactly one file:
    unity/HorrorGame/Assets/Audio/UI/caught_sent_home.wav

WHAT THIS IS FOR IN GAMEPLAY TERMS
──────────────────────────────────
§06's creature catching you is the second most important thing that can happen
in this game, and until this clip it was **silent**. `RaceState.ReportCaught`
puts a runner back on the rim of B1 with `Storey` reset to 0 and `TimesCaught`
incremented; `MatchDirector.SendBackToTheStartLine` writes the transform. Both
happen inside one fixed step. The grab clip that plays is §06's — it is the
creature's voice, it fires on the *lunge*, and it plays identically on a lunge
that MISSES. So the only thing distinguishing "it nearly had me" from "I have
just lost eight storeys" was the scenery changing.

A player who cannot tell those two apart reads the second one as a bug, and
this project has already had a player report exactly that class of thing.

WHY ONE STING AND WHY IT IS SHORT
─────────────────────────────────
This is a race. Every frame of a cutscene is a frame somebody else is running,
and — this is the part that decides the length — **the runner is not frozen**.
`SendBackToTheStartLine` is a position write, not a sequence: by the time this
clip's first sample plays, the player is already standing on B1's rim and can
already move. So the sting is not covering dead time; it is competing with a
live race the player has already re-entered.

It is therefore built as a *cut*, not as a swell:

    0.000  붙잡힘      broadband contact, 8 ms attack. The hand landing.
    0.005  무너짐      210 → 44 Hz collapse over 190 ms. Eight storeys, gone.
    0.010  방          the room you were in, low-passing 5.2 kHz → 300 Hz …
    0.235              … and gated OFF. Severance, the same device §09's
                       death_transition used, because the fact is the same one:
                       where you were is no longer where you are.
    0.300  출발선      a thin thread and open air, and nothing warm in it.
                       B1's rim is bright and safe (§01 출발: 밝고, 넓고,
                       안전하다) and it is the *worst* place a runner can be
                       twelve minutes in. The tail is an empty room, not relief.

That is 1.05 s, with its meaning delivered in the first fifth of it. Compare
`death_transition_01` — 3.45 s, of which 2.4 s is a ghost drone for a spectator
state that has been deleted. Three and a half seconds is affordable when the
player's race has ended. It is not affordable when they are running again
before the clip finishes.

THE TWO MEASUREMENTS THIS FILE EXISTS TO MAKE
─────────────────────────────────────────────
[1] **It must not be confusable with `descend_basement`.** Both cues mean "you
    have been moved between storeys", they are the only two that do, and they
    are separated by ONE fact: one of them cost you a floor and the other cost
    you seven. If a player has to look at the HUD to tell them apart then the
    sound has failed and the HUD is doing the work alone. The separation is
    measured, not asserted by ear — see `onset_seconds` and
    `energy_before`. descend is a swell (300 ms attack, energy in the back);
    caught is a cut (energy in the front). Section [3] prints both, read off
    the shipped WAVs.

[2] **The meaning must land inside the curtain.** `CaughtScreen.cs` holds the
    frame black from 0.10 s to 0.20 s and has lifted it by 0.50 s. If the
    sting's energy arrives after the picture is back, the sound is scoring a
    corridor rather than naming an event. `CURTAIN_*` below mirror that file's
    constants and section [4] asserts against them — so moving the curtain and
    leaving the sound behind fails here rather than in review.

Determinism: one seed, no `hash()`, so a rebuild is byte-identical.
"""

from __future__ import annotations

import os
import re
import sys
from typing import Tuple

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import synth  # noqa: E402

# ── Paths ───────────────────────────────────────────────────────────────────

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
UNITY = os.path.join(REPO, "unity", "HorrorGame")
OUT_DIR = os.path.join(UNITY, "Assets", "Audio", "UI")

NAME = "caught_sent_home"
"""The stem. `AudioClipCatalog` derives every wiring decision from filenames, so
this string is the wiring — see the CueTable row it belongs in."""

CATALOG = os.path.join(
    UNITY, "Assets", "Scripts", "Audio", "AudioClipCatalog.cs")
LIBRARY = os.path.join(
    UNITY, "Assets", "Audio", "Resources", "MatchAudioLibrary.asset")

SEED = 0xCA0B7  # "caught"
"""Bumping this reshuffles the clip. Do not, casually."""

# ── Level ───────────────────────────────────────────────────────────────────

LEVEL_DB = synth.UI_HEADROOM_DB
"""−6 dBFS, which is the loudest anything on the Interface bus is allowed to be
and is what `death_transition` used. Being caught keeps that budget: it is the
loudest thing that can happen to a runner who is still in the race, and §06
makes the creature's silence the weapon — a restrained mix is what gives the
one loud event somewhere to be loud *from*."""

LEAD_IN = 0.003
"""Silence before the transient. `synth.write_wav` fades the first 2 ms of
everything it writes, which is right — a buffer starting on a non-zero sample
clicks — but this clip's peak IS inside those 2 ms, so without a lead-in the
fade lands on the attack and shaves 2~3 dB off the one moment that has to read
as a cut. gen_footsteps.py found this the hard way; the same fix applies."""

STEREO = True
"""Interface cues ship stereo and that is deliberate, not an oversight.
`AssetImportPolicy.AudioRole.InterfaceCue` covers everything under
`Assets/Audio/UI`, and `AudioCues.IsPositional` is false for this bus: the sound
did not happen anywhere in the building, it happened to the player. Nothing
spatialises it, so nothing is lost to stereo — and the width is what keeps it
from sitting in the same mono point as §06's grab, which is a world sound and
IS positioned."""

# ── The curtain this has to fit inside. Mirrors CaughtScreen.cs ─────────────
#
# Duplicated rather than imported because they live in a C# file this script
# cannot read as code — so section [4] asserts against them, and a curtain that
# is retimed without retiming the sound fails the build of this clip.

CURTAIN_FALL = 0.10
"""Seconds to full black. `CaughtScreen.FallSeconds`."""

CURTAIN_HOLD = 0.10
"""Seconds held at full black. `CaughtScreen.HoldSeconds`."""

CURTAIN_LIFT = 0.30
"""Seconds back to clear. `CaughtScreen.LiftSeconds`."""

BLIND_SECONDS = CURTAIN_FALL + CURTAIN_HOLD
"""0.20 s — when the picture starts coming back. The sting's job is done by
here or it is not doing it."""

# ── What the clip must measure ──────────────────────────────────────────────

MAX_SECONDS = 1.50
"""A hard ceiling on the whole clip. Being caught costs the runner eight
storeys; it must not also cost them a second and a half of a mix that is telling
them something they already know."""

ONSET_MAX = 0.030
"""Seconds from the first sample to 90 % of peak. Above this it is a swell, and
a swell is what `descend_basement` is."""

ONSET_RATIO_MIN = 6.0
"""How many times faster this clip's onset must be than `descend_basement`'s.
Not a tuning knob — it is the number that says the two cues cannot be mistaken
for each other by a player who is looking at a corridor rather than at a HUD."""

ENERGY_IN_BLIND_MIN = 0.50
"""Fraction of the clip's total energy that must arrive before the curtain
starts lifting. See [2] in the header."""

TAIL_CEILING_DB = -26.0
"""How far under the peak the tail has to sit, measured over the last 40 % of
the clip. The tail is an empty room and an empty room is quiet; a loud one
would be scoring the player's next thirty seconds of running."""


# ── Small helpers on synth ──────────────────────────────────────────────────


def _env(seconds: float, attack: float, tau: float) -> np.ndarray:
    """Raised-cosine attack into an exponential decay."""
    n = synth.n_samples(seconds)
    out = synth.exp_decay(seconds, tau)
    a = max(1, synth.n_samples(attack))
    a = min(a, n)
    ramp = 0.5 - 0.5 * np.cos(np.linspace(0.0, np.pi, a, dtype=np.float32))
    out[:a] *= ramp
    return out.astype(np.float32)


def _tone(freq: float, seconds: float, attack: float = 0.008,
          tau: float = 0.4) -> np.ndarray:
    return (synth.sine(freq, seconds) * _env(seconds, attack, tau)).astype(np.float32)


def _glide(f0: float, f1: float, seconds: float) -> np.ndarray:
    """A pitch glide as a phase integral, so it does not step."""
    n = synth.n_samples(seconds)
    f = np.geomspace(max(f0, 1e-3), max(f1, 1e-3), n).astype(np.float64)
    phase = np.cumsum(2.0 * np.pi * f / synth.SAMPLE_RATE)
    return np.sin(phase).astype(np.float32)


def _moving_lowpass(buf: np.ndarray, f_start: float, f_end: float,
                    stages: int = 14) -> np.ndarray:
    """Crossfades between `stages` fixed low-passes to fake a sweeping filter.

    A real time-varying biquad is the right tool and numpy is not the place to
    write one; the artefact of this approach is a slight softening at each
    boundary, which for a filter that is closing anyway is inaudible."""
    n = len(buf)
    cuts = np.geomspace(f_start, f_end, stages)
    out = np.zeros(n, dtype=np.float32)
    edges = np.linspace(0, n, stages + 1).astype(int)
    for i, c in enumerate(cuts):
        band = synth.lowpass(buf, float(c))
        lo, hi = edges[i], edges[i + 1]
        w = np.zeros(n, dtype=np.float32)
        w[lo:hi] = 1.0
        if lo > 0:
            fade_n = min(lo, max(1, (hi - lo) // 2))
            w[lo - fade_n:lo] = np.linspace(0.0, 1.0, fade_n, dtype=np.float32)
        out += band * w
    return out.astype(np.float32)


def _moving_highpass(buf: np.ndarray, f_start: float, f_end: float,
                     stages: int = 10) -> np.ndarray:
    """The opening counterpart of `_moving_lowpass`."""
    n = len(buf)
    cuts = np.geomspace(max(f_start, 1e-3), max(f_end, 1e-3), stages)
    out = np.zeros(n, dtype=np.float32)
    edges = np.linspace(0, n, stages + 1).astype(int)
    for i, c in enumerate(cuts):
        band = synth.highpass(buf, float(c))
        lo, hi = edges[i], edges[i + 1]
        w = np.zeros(n, dtype=np.float32)
        w[lo:hi] = 1.0
        if lo > 0:
            fade_n = min(lo, max(1, (hi - lo) // 2))
            w[lo - fade_n:lo] = np.linspace(0.0, 1.0, fade_n, dtype=np.float32)
        out += band * w
    return out.astype(np.float32)


def _gate_off(buf: np.ndarray, at: float, fall: float = 0.012) -> np.ndarray:
    """Cuts everything after `at` with a short fall. The severance."""
    out = buf.copy()
    i = synth.n_samples(at)
    if i >= len(out):
        return out
    f = min(max(1, synth.n_samples(fall)), len(out) - i)
    out[i:i + f] *= np.linspace(1.0, 0.0, f, dtype=np.float32)
    out[i + f:] = 0.0
    return out


def _rough(seconds: float, rate: float, seed: int, depth: float = 0.8) -> np.ndarray:
    """Grain modulation — contact texture rather than a clean impact."""
    n = synth.n_samples(seconds)
    r = synth.rng(seed)
    steps = max(2, int(seconds * rate))
    coarse = r.random(steps).astype(np.float32)
    fine = np.interp(np.linspace(0.0, steps - 1, n),
                     np.arange(steps), coarse).astype(np.float32)
    return (1.0 - depth + depth * fine).astype(np.float32)


def _polish(buf: np.ndarray, hp_hz: float = 22.0) -> np.ndarray:
    """DC-blocks and trims the sub-audible. `assert_usable` is strict about DC
    and the rumble under 20 Hz is headroom spent on something no speaker in the
    world reproduces."""
    return synth.highpass(buf, hp_hz).astype(np.float32)


# ── The sting ───────────────────────────────────────────────────────────────


def build_caught() -> np.ndarray:
    """One sting: seized, collapsed, severed, and put down somewhere empty."""
    sec = 1.05
    s = SEED

    # 붙잡힘 — the contact. Low modes with a lot of noise in them, so it reads as
    # a body being taken hold of rather than as a drum. Short: the hand is not
    # the event, what follows it is.
    grab = synth.modal_impact(
        [synth.Mode(96.0, 0.070, 1.00),
         synth.Mode(152.0, 0.045, 0.55),
         synth.Mode(233.0, 0.028, 0.30)],
        0.26, seed=s + 3, noise_amount=0.42, noise_tau=0.014)
    grab = (grab * _rough(0.26, 120.0, s + 4, depth=0.35)).astype(np.float32)

    # 무너짐 — the collapse. A glide DOWN, and it is the one gesture in the clip
    # a player could hum back. §01's whole loop is downward, so a downward
    # gesture would normally mean progress; this one is far too fast to be a
    # descent and it ends under the floor of the mix, which is the difference.
    fall = (_glide(210.0, 44.0, 0.19) * _env(0.19, 0.005, 0.075)).astype(np.float32)

    # 방 — the room being taken away. Broadband, closing.
    room = synth.bandpass(synth.pink(0.24, s + 7), 180.0, 5200.0)
    room = _moving_lowpass(room, 5200.0, 300.0, stages=14)
    room = (room * _env(0.24, 0.012, 0.16)).astype(np.float32)

    # 출발선 — where they are now. Air with nothing in it: a thread that does not
    # resolve and a band that opens upward and stops. B1's rim is bright, and
    # after B6 that brightness is the punishment.
    tail_len = sec - 0.30
    air = synth.pink(tail_len, s + 11)
    air = _moving_highpass(air, 200.0, 900.0, stages=10)
    air = (air * _env(tail_len, 0.22, 0.62)).astype(np.float32)
    thread = _tone(1180.0, tail_len, attack=0.18, tau=0.50)
    breath = synth.lowpass(synth.brown(tail_len, s + 17), 140.0)
    breath = (breath * _env(tail_len, 0.30, 0.55)).astype(np.float32)

    canvas = synth.silence(sec)
    synth.place(canvas, grab, 0.000, 0.90)
    synth.place(canvas, fall, 0.005, 0.80)
    synth.place(canvas, room, 0.010, 0.38)

    # Severance, at 0.235 s — inside the curtain's blackout, so the picture and
    # the mix cut at the same time rather than a frame apart.
    canvas = _gate_off(canvas, 0.235, fall=0.010)

    # Placed by measurement, not by ear: TAIL_CEILING_DB is the bar and these
    # three gains are what puts the tail under it. First pass ran 0.085 / 0.030
    # / 0.070 and measured −25.4 dB, which failed by 0.6 dB — the clip moved,
    # not the bar.
    synth.place(canvas, air, 0.300, 0.068)
    synth.place(canvas, thread, 0.320, 0.026)
    synth.place(canvas, breath.astype(np.float32), 0.300, 0.056)

    lead = synth.silence(LEAD_IN)
    return _polish(synth.concat(lead, canvas))


# ── Measurement ─────────────────────────────────────────────────────────────


def onset_seconds(path: str, fraction: float = 0.90) -> float:
    """Seconds from the first sample until the signal first reaches `fraction`
    of its peak. The one number that separates a cut from a swell."""
    data, sr = synth.read_wav(path)
    if not len(data):
        return 0.0
    peak = float(np.max(np.abs(data)))
    if peak <= 0.0:
        return 0.0
    hit = np.argmax(np.abs(data) >= peak * fraction)
    return float(hit) / float(sr)


def energy_before(path: str, seconds: float) -> float:
    """Fraction of the clip's total energy delivered before `seconds`."""
    data, sr = synth.read_wav(path)
    e = np.square(data.astype(np.float64))
    total = float(np.sum(e))
    if total <= 0.0:
        return 0.0
    n = min(len(e), int(round(seconds * sr)))
    return float(np.sum(e[:n]) / total)


SIGNAL_FLOOR_DB = -45.0
"""How far under the loudest bin a bin still counts toward `signal_centroid`.

`ClipReport.spectral_centroid` sums every bin, and there are vastly more high
bins than low ones — so 0.75 s of quiet high-passed air can drag the figure
above 7 kHz on a clip whose audible content is a 100 Hz impact. gen_footsteps.py
hit the same artefact on its darkest floors and answered it the same way."""


def signal_centroid(path: str, floor_db: float = SIGNAL_FLOOR_DB) -> float:
    """Spectral centroid over the bins that actually carry the clip."""
    data, sr = synth.read_wav(path)
    if len(data) < 16:
        return 0.0
    mag = np.abs(np.fft.rfft(data.astype(np.float64) * np.hanning(len(data))))
    freqs = np.fft.rfftfreq(len(data), 1.0 / sr)
    if not np.any(mag > 0):
        return 0.0
    keep = mag >= np.max(mag) * synth.db_to_gain(floor_db)
    total = float(np.sum(mag[keep]))
    return float(np.sum(freqs[keep] * mag[keep]) / total) if total > 0 else 0.0


def tail_db(path: str, last_fraction: float = 0.40) -> float:
    """Peak of the final `last_fraction` of the clip, in dB under the clip's own
    peak. Negative and large is quiet."""
    data, _ = synth.read_wav(path)
    if not len(data):
        return -120.0
    peak = float(np.max(np.abs(data)))
    if peak <= 0.0:
        return -120.0
    start = int(len(data) * (1.0 - last_fraction))
    tail = float(np.max(np.abs(data[start:]))) if start < len(data) else 0.0
    return synth.gain_to_db(max(tail, 1e-9) / peak)


def wiring_state() -> Tuple[bool, bool, str]:
    """Is this clip actually IN the game?

    Answers two separate questions by reading the artefacts rather than
    trusting that writing the WAV was enough:

      * does `AudioClipCatalog.cs` name the stem, and
      * does `MatchAudioLibrary.asset` contain the GUID Unity gave the file?

    A .wav under Assets/Audio that no cue resolves to is exactly what
    `AudioCueId`'s own remarks call out — "a .wav that ships in the build for
    nobody" — and the whole reason the shipped DLLs were once found full of
    them. So the generator says so on every run instead of leaving it to be
    noticed."""
    in_catalog = False
    if os.path.exists(CATALOG):
        with open(CATALOG, "r", encoding="utf-8") as fh:
            in_catalog = NAME in fh.read()

    meta = os.path.join(OUT_DIR, NAME + ".wav.meta")
    if not os.path.exists(meta):
        return in_catalog, False, "no .meta — Unity has not imported this clip yet"

    with open(meta, "r", encoding="utf-8") as fh:
        m = re.search(r"^guid:\s*([0-9a-f]{32})", fh.read(), re.MULTILINE)
    if m is None:
        return in_catalog, False, "meta has no guid"

    guid = m.group(1)
    if not os.path.exists(LIBRARY):
        return in_catalog, False, f"guid {guid}, but no MatchAudioLibrary.asset"

    with open(LIBRARY, "r", encoding="utf-8") as fh:
        in_library = guid in fh.read()
    return in_catalog, in_library, f"guid {guid}"


# ── Entry point ─────────────────────────────────────────────────────────────


def main() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, NAME + ".wav")

    synth.write_wav(path, build_caught(), headroom_db=LEVEL_DB, stereo=STEREO)
    r = synth.assert_usable(path, min_seconds=0.20, max_seconds=MAX_SECONDS)

    # The two clips this one is measured against, read off disk rather than
    # rebuilt: the comparison is only worth anything against what actually
    # ships.
    descend_path = os.path.join(OUT_DIR, "descend_basement.wav")
    death_path = os.path.join(OUT_DIR, "death_transition_01.wav")
    for other in (descend_path, death_path):
        if not os.path.exists(other):
            raise SystemExit(
                f"{os.path.basename(other)} is not on disk. This clip is defined by "
                f"how it differs from it, so the assertions below would pass by "
                f"measuring nothing. Run gen_ui.py first.")

    descend = synth.analyse(descend_path)
    death = synth.analyse(death_path)

    onset = onset_seconds(path)
    onset_descend = onset_seconds(descend_path)
    blind = energy_before(path, BLIND_SECONDS)
    blind_descend = energy_before(descend_path, BLIND_SECONDS)
    tail = tail_db(path)

    print(f"1 clip written to {OUT_DIR}")
    print(f"{synth.SAMPLE_RATE} Hz, 16-bit PCM, "
          f"{'stereo' if STEREO else 'mono'}; passed synth.assert_usable()")
    print()
    print(synth.report_table([r, descend, death]))

    print()
    print("=" * 78)
    print("[3] §01 — can a runner tell 'I dropped a floor' from 'I lost seven'?")
    print("=" * 78)
    print("  centroids are signal_centroid (bins within "
          f"{SIGNAL_FLOOR_DB:.0f} dB of the loudest), not")
    print("  the raw full-band figure in the table above — see SIGNAL_FLOOR_DB.")
    print(f"  {'':<22} {'onset':>9} {'E before 0.20s':>16} {'seconds':>9} {'centroid':>10}")
    print(f"  {NAME:<22} {onset * 1000:>7.1f}ms {blind * 100:>15.1f}% "
          f"{r.seconds:>8.3f}s {signal_centroid(path):>9.0f}Hz")
    print(f"  {'descend_basement':<22} {onset_descend * 1000:>7.1f}ms "
          f"{blind_descend * 100:>15.1f}% {descend.seconds:>8.3f}s "
          f"{signal_centroid(descend_path):>9.0f}Hz")
    ratio = onset_descend / max(onset, 1e-6)
    print(f"  onset ratio {ratio:.0f}x — caught is a cut, descend is a swell.")

    print()
    print("=" * 78)
    print("[4] the curtain — does the sound land while the frame is still black?")
    print("=" * 78)
    print(f"  CaughtScreen: black by {CURTAIN_FALL:.2f}s, held to "
          f"{BLIND_SECONDS:.2f}s, clear by "
          f"{CURTAIN_FALL + CURTAIN_HOLD + CURTAIN_LIFT:.2f}s")
    print(f"  energy delivered before the lift starts: {blind * 100:.1f}%")
    print(f"  tail over the last 40 %: {tail:+.1f} dB under peak")

    print()
    print("=" * 78)
    print("[5] IS IT IN THE GAME?")
    print("=" * 78)
    in_catalog, in_library, note = wiring_state()
    print(f"  AudioClipCatalog.cs names '{NAME}':      {'yes' if in_catalog else 'NO'}")
    print(f"  MatchAudioLibrary.asset holds its guid:  {'yes' if in_library else 'NO'}")
    print(f"  ({note})")
    if not (in_catalog and in_library):
        print("  → the clip is written but NOT WIRED. A cue that cannot resolve is")
        print("    silent, and a .wav nothing resolves to ships for nobody. Add the")
        print("    CueTable row and re-run the audio wiring step in the editor.")

    # ── Assertions ─────────────────────────────────────────────────────────
    #
    # Everything above is printed; everything below is a condition this clip is
    # not allowed to ship without.

    assert r.channels == (2 if STEREO else 1), (
        f"{NAME}: {r.channels} channels. Interface cues ship stereo — see STEREO.")

    assert r.seconds <= MAX_SECONDS, (
        f"{NAME}: {r.seconds:.3f}s. The runner is already moving again by the time "
        f"this plays; a sting longer than {MAX_SECONDS}s is scoring a race in "
        f"progress. death_transition_01 is {death.seconds:.2f}s and that is what "
        f"this replaces.")

    assert r.seconds < death.seconds, (
        f"{NAME} ({r.seconds:.3f}s) is not shorter than death_transition_01 "
        f"({death.seconds:.3f}s). The whole reason for a new clip is that being "
        f"caught no longer ends the race.")

    assert onset <= ONSET_MAX, (
        f"{NAME}: onset {onset * 1000:.1f}ms exceeds {ONSET_MAX * 1000:.0f}ms. "
        f"This has to read as a cut. Anything slower is a swell, and the swell "
        f"is what descend_basement already means.")

    assert ratio >= ONSET_RATIO_MIN, (
        f"{NAME}: onset is only {ratio:.1f}x faster than descend_basement's "
        f"({onset_descend * 1000:.0f}ms). These are the only two cues in the game "
        f"that mean 'you have been moved between storeys' and they must not be "
        f"confusable — one of them cost a floor and the other cost seven.")

    assert blind >= ENERGY_IN_BLIND_MIN, (
        f"{NAME}: only {blind * 100:.1f}% of its energy arrives before the curtain "
        f"starts lifting at {BLIND_SECONDS:.2f}s (floor {ENERGY_IN_BLIND_MIN * 100:.0f}%). "
        f"The sting names the moment the picture is hiding; energy after the lift "
        f"is scoring the corridor the player is already running down.")

    assert blind > blind_descend, (
        f"{NAME} front-loads {blind * 100:.1f}% against descend_basement's "
        f"{blind_descend * 100:.1f}%. It has to be the more front-loaded of the two "
        f"or the shapes have converged.")

    assert tail <= TAIL_CEILING_DB, (
        f"{NAME}: tail sits {tail:+.1f} dB under peak, wanted {TAIL_CEILING_DB:+.1f} dB "
        f"or quieter. B1's rim is an empty room and §06 makes silence the weapon.")

    assert r.peak <= synth.db_to_gain(LEVEL_DB) + 1e-3, (
        f"{NAME}: peaks at {r.peak_db:.1f} dBFS, over the "
        f"{LEVEL_DB:.1f} dBFS Interface budget.")

    print()
    print(f"ok — {NAME}.wav: {r.seconds:.3f}s, onset {onset * 1000:.1f}ms, "
          f"{blind * 100:.0f}% of its energy inside the curtain, tail {tail:+.0f} dB.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
