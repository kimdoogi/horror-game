"""Procedural PBR texture generation for the horror game.

Every surface the game ships is authored by code in this file. Most are wholly
synthetic, for the same reason the audio toolkit in `tools/audio/synth.py`
synthesises rather than samples. Six of them — the walls and floors that a player
stares at all match, and that read "너무 허접" when they were pure noise — now sit
on a **CC0 photo-scan base** (PolyHaven / ambientCG, vendored under
`tools/textures/cc0`), with this file's grime, §3.8c wet and §3.9 grain layered on
top. CC0 is public-domain-equivalent: commercial use is fine and no attribution is
required, so the "no texture that turns out to be someone else's" guarantee holds —
the credits ride along in the manifest and docs/ASSETS.md anyway. See `PhotoSpec`
for the why and the how; the scans change the *source* of the base maps and touch
nothing else — packing, names, resolution, manifest and every `verify` gate are
unchanged, and a checkout without the scans falls back to the procedural build.

WHY THIS EXISTS AT ALL
----------------------
The measured baseline was a black screen. The flashlight beam landed on a wall
and the wall stayed invisible, because every surface was a flat colour with an
albedo so low there was nothing for the beam to bounce off. §03 makes darkness
the lock on progress and the flashlight the key, so a beam that visibly does
nothing is not atmosphere — it is a broken mechanic.

The fix is stated once, here, and enforced by assertion below:

    **Darkness comes from LIGHTING, never from painting the albedo black.**

A real basement wall reflects 20–40% of the light that hits it. Painted black
it reflects 2%, and no amount of lamp intensity recovers the shape of the room.
So every albedo this file produces is calibrated to a mean in
[`ALBEDO_MIN_LINEAR`, `ALBEDO_MAX_LINEAR`] *linear* and the run fails if one
drifts out.

§12 IS A GAMEPLAY REQUIREMENT, NOT AN ART REQUEST
--------------------------------------------------
"구역별로 바닥 재질이 달라야 청음사가 위치를 판별할 수 있다 — 아트 결정이 아니라
시스템 결정이다." The five floors are a channel the Listener reads. The audio
side already makes them distinguishable by ear; this file has to make them
distinguishable by eye, under a narrow beam, at a glance. That is why each floor
gets a different *structure* (planks / grid / loose stones / stains / plate)
rather than a different tint — a tint dies the moment the beam is off-centre,
structure survives because the normal map keeps working at grazing angles.

CONVENTIONS, AND THE REASONS FOR THEM
-------------------------------------
* **Everything is periodic by construction.** A seam every 2 m reads as amateur
  work. No filter here is allowed to be non-periodic: blurs use `mode="wrap"`,
  frequency-domain work is inherently circular, structure counts divide the
  resolution exactly, and domain warps index with integer wraparound. The seam
  check at the end measures it rather than trusting it.
* **Height fields are in metres, and carry `world_size`.** The normal map is a
  real slope, `dh/dx`, not a fudge factor — which is why gravel actually reads
  as loose stone instead of as a bumpy photograph of stone.
* **Linear float internally, sRGB only at the PNG boundary.** Albedo is authored
  and calibrated in linear because that is the space the band is specified in;
  roughness / AO / normal are data and never get an sRGB curve.
* **Buffers are float32 in [0, 1] until written.**
"""

from __future__ import annotations

import argparse
import math
import os
import struct
import sys
import zlib
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Sequence, Tuple

import numpy as np
from scipy import ndimage

# ── Calibration constants ───────────────────────────────────────────────────

ALBEDO_MIN_LINEAR = 0.15
"""Floor of the albedo band, linear.

Below this a surface returns so little of the beam that the flashlight stops
reading as a light source. §03 needs the beam to visibly carve — a wall that
eats it is indistinguishable from a wall that is not there.
"""

ALBEDO_MAX_LINEAR = 0.45
"""Ceiling of the albedo band, linear.

Above this the room stops being dark even before the lighting rig is tuned, and
§05's "어둡다" turns into an evenly lit grey box. It is also roughly the top of
the range real building materials occupy: dry concrete sits near 0.35, white
plaster near 0.45, and nothing indoors goes higher without being a light source.
"""

ALBEDO_CLAMP_LINEAR = (0.012, 0.90)
"""Hard per-texel bounds, linear — charcoal to fresh snow.

Individual texels may sit outside the *mean* band (grout is darker than tile,
that is the point), but a texel outside these bounds is not a material any more,
it is a hole in the render.
"""

MAX_CALIBRATION_GAIN = 2.6
"""How far the mean calibration in `finish_albedo` may move a texture.

The gain exists to land an intentional target exactly, not to rescue bad
authoring. If a material needs more than this, its paint values are wrong and
the run should fail loudly rather than quietly ship a washed-out texture.
"""

SEAM_RATIO_LIMIT = 1.10
"""Wrap-edge difference over the *largest* interior difference.

See `seam_ratio` for the derivation. The materials here score 0.0–0.95 and a
genuine discontinuity scores 6–12, so this sits in the gap with a factor of five
of margin on the failing side.
"""

MIN_ROUGHNESS = 0.16
"""Floor on roughness for every surface in the set.

Not a physical limit — polished tile really does go below this — but a limit set
by the light rig. §03 puts a bright spot light within a metre or two of a wall
and there is no exposure control in front of it, so a surface at roughness 0.06
returns a specular lobe several hundred times over white and renders as a hole
punched in the frame. The first textured render showed exactly that: a blown
disc where the beam met smooth trim, with the material underneath unreadable.

Clamping roughness costs a little of the wet-tile look and buys back the whole
highlight. It sits here rather than in each builder so that the reason is stated
once and no surface can quietly opt out of it.
"""

NORMAL_UNIT_TOLERANCE = 0.02
"""Allowed deviation from unit length after 8-bit quantisation.

An 8-bit channel steps by 1/255 ≈ 0.0078 in [0,1] and twice that once remapped
to [-1,1], so a correctly generated normal cannot be exact. Anything beyond this
means the vector was never normalised, not that it was rounded.
"""

DEFAULT_RESOLUTION = 1024
"""Texels per side.

1024 over a 2 m tile is ~512 px/m. A 1920-wide frame at §05's 90° FOV resolves
about 21 px per degree; one centimetre two metres ahead subtends 0.29°, so the
eye can separate roughly 600 px/m at the distance a player actually reads a
floor. 512 sits just under that — sharp enough that nothing is visibly soft,
small enough that the whole set is a few seconds of generation and ~70 MB of
compressed VRAM.
"""


KIT_UV_UNITS_PER_METRE = 0.5
"""UV units the MapKit's meshes span per metre of world surface. **Measured.**

`gen_mapkit.py` sets `UV_METRES_PER_TILE = 2.0` and `uv_box_project` multiplies
every world coordinate by `1 / metres_per_tile`, so one metre of wall, floor or
soffit carries half a UV unit. Confirmed against the shipped FBXs rather than
against the source: importing `Corridor_Straight_5m`, `Hall_Open_20x20` and
`FloorTile_Concrete` and dividing every UV edge length by its world edge length
gives 0.5000 on all 19 632 edges, on faces of every orientation.

WHY THIS CONSTANT IS HERE AND NOT IN THE ENGINE
-----------------------------------------------
`ProceduralTextureMaterials.ApplyTiling` computes the material's UV scale as
`KitUvUnitsPerMetre / world_size_metres` with `KitUvUnitsPerMetre = 1f`, on the
strength of a comment claiming the kit was unwrapped in metres. It was not, and
the consequence is not subtle: **every surface in the game was rendering at
exactly twice the size it was authored at.** A brick drawn 21.5 cm wide arrived
45 cm wide, a floor tile at 25 cm arrived at 50 cm, and the crack network drawn
across a 2.5 m concrete slab spanned five metres of floor. That single factor is
most of what made the basement read as a model of a basement — nothing else
communicates "this is a game asset" as loudly as masonry at double scale.

That file belongs to another area, so the correction is applied on this side, in
one place, by emitting the manifest's `world_size_metres` in the units that
field is actually consumed in — UV units per tile, not metres per tile. The
truthful figure travels alongside it as `authored_metres_per_tile`.

If `KitUvUnitsPerMetre` is ever corrected to 0.5 in the engine, set this to 1.0
in the same commit. The two numbers multiply, and getting both right doubles the
error rather than fixing it.
"""


# ── PNG output ──────────────────────────────────────────────────────────────
#
# Written by hand rather than through Pillow so the toolkit runs in the same
# venv the audio generators use (numpy + scipy, nothing else). PNG's stored
# format is exactly what we need anyway: lossless, 8-bit, deflate.


def _png_chunk(tag: bytes, data: bytes) -> bytes:
    """One length-tag-payload-CRC PNG chunk."""
    return (
        struct.pack(">I", len(data))
        + tag
        + data
        + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    )


def write_png(path: str, image: np.ndarray) -> None:
    """Writes an 8-bit PNG from a uint8 array shaped (H, W), (H, W, 3) or (H, W, 4)."""
    if image.dtype != np.uint8:
        raise TypeError("write_png wants uint8; convert deliberately so rounding is visible")

    if image.ndim == 2:
        colour_type, channels = 0, 1
    elif image.ndim == 3 and image.shape[2] == 3:
        colour_type, channels = 2, 3
    elif image.ndim == 3 and image.shape[2] == 4:
        colour_type, channels = 6, 4
    else:
        raise ValueError("write_png supports grey, RGB and RGBA only, got shape %r" % (image.shape,))

    height, width = image.shape[0], image.shape[1]

    # Filter byte 0 (None) per scanline. The images here are noise-dominated, so
    # the adaptive filters buy little and cost a pass over every row.
    raw = np.zeros((height, width * channels + 1), dtype=np.uint8)
    raw[:, 1:] = image.reshape(height, width * channels)

    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(_png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, colour_type, 0, 0, 0)))
        handle.write(_png_chunk(b"IDAT", zlib.compress(raw.tobytes(), 9)))
        handle.write(_png_chunk(b"IEND", b""))


# ── Colour space ────────────────────────────────────────────────────────────


def linear_to_srgb(x: np.ndarray) -> np.ndarray:
    """Linear light to sRGB-encoded, the exact IEC 61966-2-1 curve.

    Albedo PNGs are read back through the inverse of this by the GPU, so using
    the real curve rather than a 1/2.2 approximation is what makes the asserted
    linear mean the mean the shader actually sees.
    """
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1.0 / 2.4) - 0.055)


def srgb_to_linear(x: np.ndarray) -> np.ndarray:
    """sRGB-encoded to linear light."""
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


def to_u8(x: np.ndarray) -> np.ndarray:
    """Rounds a [0,1] float array to uint8 without the truncation bias of a cast."""
    return np.clip(np.rint(np.asarray(x, dtype=np.float64) * 255.0), 0, 255).astype(np.uint8)


# ── Periodic noise ──────────────────────────────────────────────────────────
#
# Every generator below is periodic over the full texture. That is the property
# that makes tiling free rather than something to paper over afterwards.


def rng(seed: int) -> np.random.Generator:
    """Seeded generator. Every texture is reproducible from its seed alone."""
    return np.random.default_rng(seed)


def _radial_frequency(res: int) -> np.ndarray:
    """Radial frequency magnitude for an `res × res` FFT grid, in cycles/tile."""
    fy = np.fft.fftfreq(res)[:, None] * res
    fx = np.fft.fftfreq(res)[None, :] * res
    return np.sqrt(fy * fy + fx * fx)


def fbm(
    res: int,
    seed: int,
    beta: float = 1.9,
    low_cycles: float = 2.0,
    high_cycles: float | None = None,
) -> np.ndarray:
    """Fractal noise by spectral synthesis, normalised to [0, 1].

    Built in the frequency domain because a circular FFT is periodic *by
    definition* — there is no wrap-around error to correct, unlike a lattice
    noise that has to be told its period. `beta` is the power-law falloff:
    around 2 gives the self-similar mottling of concrete and plaster, lower
    values keep more fine grain.
    """
    freq = _radial_frequency(res)
    spectrum = np.fft.fft2(rng(seed).standard_normal((res, res)))

    amplitude = np.zeros_like(freq)
    np.divide(1.0, np.power(freq, beta), out=amplitude, where=freq > 0)
    amplitude[freq < low_cycles] = 0.0
    if high_cycles is not None:
        amplitude[freq > high_cycles] = 0.0

    field = np.real(np.fft.ifft2(spectrum * amplitude))
    return normalise01(field)


def normalise01(field: np.ndarray) -> np.ndarray:
    """Rescales to exactly [0, 1]. Flat input becomes 0.5 rather than NaN."""
    low, high = float(field.min()), float(field.max())
    if high - low < 1e-12:
        return np.full_like(field, 0.5, dtype=np.float32)
    return ((field - low) / (high - low)).astype(np.float32)


def blur(field: np.ndarray, sigma: float) -> np.ndarray:
    """Gaussian blur that wraps.

    `mode="wrap"` is the whole point: the default reflecting boundary would make
    every blurred feature stop dead at the texture edge, which is precisely the
    seam this file exists to avoid.
    """
    if sigma <= 0.0:
        return field
    return ndimage.gaussian_filter(field, sigma=sigma, mode="wrap").astype(np.float32)


def worley(
    res: int,
    cells: int,
    seed: int,
    jitter: float = 1.0,
) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Periodic cellular noise. Returns (F1, F2, cell id) with distances in tiles.

    One feature point per cell of a `cells × cells` grid, searched over the 3×3
    neighbourhood with wrapped cell indices, so the pattern is periodic and no
    point near an edge loses its neighbours. Used two different ways here:
    F1 plus the cell id builds gravel (a dome and a tone per stone), and F2 − F1
    builds crack networks, because that difference goes to zero exactly on the
    boundary between two cells — which is what a fracture is.
    """
    generator = rng(seed)
    points = (np.stack(np.meshgrid(np.arange(cells), np.arange(cells), indexing="ij"), axis=-1)
              + 0.5 + (generator.random((cells, cells, 2)) - 0.5) * jitter) / cells

    axis = (np.arange(res) + 0.5) / res
    py = axis[:, None]
    px = axis[None, :]

    cell_y = np.minimum((py * cells).astype(np.int32), cells - 1)
    cell_x = np.minimum((px * cells).astype(np.int32), cells - 1)

    best1 = np.full((res, res), 4.0, dtype=np.float32)
    best2 = np.full((res, res), 4.0, dtype=np.float32)
    owner = np.zeros((res, res), dtype=np.int32)

    for offset_y in (-1, 0, 1):
        for offset_x in (-1, 0, 1):
            ny = (cell_y + offset_y) % cells
            nx = (cell_x + offset_x) % cells
            feature = points[ny, nx]

            # Wrapped difference: the shorter way round the torus.
            dy = feature[..., 0] - py
            dx = feature[..., 1] - px
            dy -= np.rint(dy)
            dx -= np.rint(dx)
            distance = np.sqrt(dy * dy + dx * dx).astype(np.float32)

            closer = distance < best1
            best2 = np.where(closer, best1, np.minimum(best2, distance))
            owner = np.where(closer, ny * cells + nx, owner)
            best1 = np.where(closer, distance, best1)

    return best1, best2, owner


def per_cell(owner: np.ndarray, cells: int, seed: int, low: float = 0.0, high: float = 1.0) -> np.ndarray:
    """A random value per Worley cell, painted back over the pixels it owns.

    This is what turns a distance field into a pile of individual stones: each
    one gets its own tone, height and roughness instead of the whole surface
    sharing one.
    """
    table = rng(seed).uniform(low, high, size=cells * cells).astype(np.float32)
    return table[owner]


def stripes(res: int, count: int, axis: int, phase: np.ndarray | None = None) -> np.ndarray:
    """Sawtooth position within a stripe, in [0, 1). Periodic when `count` divides `res`.

    The building block for planks, tile rows, brick courses and form boards.
    `phase` offsets each stripe, which is how brick courses get their stagger.
    """
    coordinate = (np.arange(res, dtype=np.float32) + 0.5) / res
    grid = coordinate[:, None] if axis == 0 else coordinate[None, :]
    value = grid * count
    if phase is not None:
        value = value + phase
    return np.mod(value, 1.0).astype(np.float32)


def index_of(res: int, count: int, axis: int, phase: np.ndarray | None = None) -> np.ndarray:
    """Which stripe each texel falls in, wrapped to [0, count). Pairs with `stripes`."""
    coordinate = (np.arange(res, dtype=np.float32) + 0.5) / res
    grid = coordinate[:, None] if axis == 0 else coordinate[None, :]
    value = grid * count
    if phase is not None:
        value = value + phase
    return np.mod(np.floor(value), count).astype(np.int32)


def cell_random(index: np.ndarray, count: int, seed: int, low: float = 0.0, high: float = 1.0) -> np.ndarray:
    """A random value per stripe / brick / tile index."""
    table = rng(seed).uniform(low, high, size=count).astype(np.float32)
    return table[np.clip(index, 0, count - 1)]


def upward(res: int) -> np.ndarray:
    """Height within the tile as the *renderer* sees it: 0 at V=0, 1 at V=1.

    Needed because the array and the texture disagree about which way is up, and
    getting it backwards is invisible in an image viewer and wrong in the game.
    `write_png` emits row 0 as the first PNG scanline, Unity puts the first
    scanline at the top of the texture, and UV (0,0) samples the bottom-left —
    so **array row 0 is V = 1**.

    It matters here because `gen_mapkit.py` unwraps with a world-aligned box
    projection whose V is world Z on every vertical face, and every kit piece has
    its origin on its own floor. So V = 0 is not an arbitrary edge of a tile: it
    is *the floor line*, on every wall of every piece on every storey. Anything
    keyed to this lands where a real building would put it.

    `build_trim_skirting` had it inverted — its mop damage, its scuffing and its
    "shadow where the board meets the floor" were all authored against a
    `stripes(..., axis=0)` ramp that runs from the top down, so every one of them
    rendered at the top edge of the board.
    """
    rows = (np.arange(res, dtype=np.float32) + 0.5) / res
    return np.repeat((1.0 - rows)[:, None], res, axis=1).astype(np.float32)


def groove(position: np.ndarray, width: float, softness: float = 0.35) -> np.ndarray:
    """1 inside a gap of fractional `width` centred on a stripe boundary, 0 on the face.

    Drives every joint in the set — plank gaps, tile grout, brick mortar, plate
    seams — so they all get the same chamfer treatment and read as the same
    building.
    """
    distance = np.minimum(position, 1.0 - position)
    edge = width * 0.5
    return np.clip(1.0 - (distance - edge * (1.0 - softness)) / (edge * softness + 1e-6), 0.0, 1.0).astype(np.float32)


def smoothstep(low: float, high: float, x: np.ndarray) -> np.ndarray:
    """Hermite fade between two thresholds."""
    t = np.clip((x - low) / (high - low + 1e-9), 0.0, 1.0)
    return (t * t * (3.0 - 2.0 * t)).astype(np.float32)


def warp(field: np.ndarray, offset_y: np.ndarray, offset_x: np.ndarray) -> np.ndarray:
    """Domain-warps by integer texel offsets, wrapping at the edges.

    Integer offsets with `np.take(mode="wrap")` rather than a resampling warp:
    it costs nothing, and it cannot introduce the edge artefacts that a
    interpolating warp would need a periodic-aware sampler to avoid.
    """
    res = field.shape[0]
    grid_y, grid_x = np.meshgrid(np.arange(res), np.arange(res), indexing="ij")
    sample_y = np.mod(grid_y + np.rint(offset_y).astype(np.int32), res)
    sample_x = np.mod(grid_x + np.rint(offset_x).astype(np.int32), res)
    return field[sample_y, sample_x].astype(np.float32)


def warped_fbm(res: int, seed: int, beta: float, strength: float, low_cycles: float = 2.0) -> np.ndarray:
    """fBm pushed around by more fBm — breaks up the machine regularity of pure noise.

    Straight fBm reads as television static at close range. Warping it gives the
    directional streaking that stains, rust and water damage actually have.
    """
    base = fbm(res, seed, beta=beta, low_cycles=low_cycles)
    push_y = (fbm(res, seed + 991, beta=2.2, low_cycles=1.0) - 0.5) * strength
    push_x = (fbm(res, seed + 997, beta=2.2, low_cycles=1.0) - 0.5) * strength
    return normalise01(warp(base, push_y, push_x))


def micro_grain(res: int, world_size: float, seed: int, feature_metres: float,
                anisotropy: float = 0.0, beta: float = 1.15) -> np.ndarray:
    """Zero-mean fine relief at a stated *physical* size, in units of its own σ.

    The scale correction (`KIT_UV_UNITS_PER_METRE`) halved the world size of
    every tile, which doubled texel density and left the whole set with nothing
    up there to resolve: measured, the albedo of concrete carried 7.9 % of its
    contrast above 25 cycles/metre and metal 9.7 %. A surface with no energy in
    that band has nothing left to show once the player is closer than about four
    metres, and in a §12 corridor 2.2 m wide that is the entire game.

    Authored in metres rather than in texels so the same call gives the same
    physical grain on a 1.5 m gravel tile and a 2.5 m concrete slab. Above the
    map's Nyquist there is nothing to synthesise, so the band is clamped and the
    caller gets whatever the resolution can actually carry — that clamp is the
    reason the very fine stuff lives in the detail normal instead (`DETAILS`).

    `anisotropy` stretches the grain along U, which is what separates timber
    fibre and rolled steel from sand: both are the same noise, drawn out.
    """
    cycles = world_size / max(feature_metres, 1e-4)
    nyquist = res / 2.0
    field = fbm(res, seed, beta=beta, low_cycles=min(cycles, nyquist * 0.6))

    if anisotropy > 0.0:
        # Blurring across U leaves the along-U detail intact, so the features
        # come out drawn in one direction rather than merely softened.
        sigma = anisotropy * res / max(cycles, 1.0) * 2.0
        field = ndimage.gaussian_filter1d(field, sigma=sigma, axis=1, mode="wrap").astype(np.float32)

    field = field - float(field.mean())
    deviation = float(field.std())
    if deviation < 1e-9:
        return np.zeros_like(field)

    # ±3σ: one freak texel from the tail must not set the amplitude of a term
    # that is then scaled in millimetres.
    return np.clip(field / deviation, -3.0, 3.0).astype(np.float32)


@dataclass(frozen=True)
class Grain:
    """The sub-centimetre layer every real surface has and none of these had.

    Applied once, in `generate`, rather than inside twelve builders — it is the
    same physics every time (fine relief that also scatters light and holds
    dirt), and stating it as data means the whole set can be re-judged against
    the corrected texel density by reading one table.
    """

    feature_mm: float
    """Characteristic size of one grain, in millimetres of world."""

    relief_mm: float
    """Peak-to-peak height it adds, in millimetres. Feeds the normal and the AO."""

    albedo: float = 0.0
    """Fraction the grain modulates albedo by. Dirt collects in the low bits."""

    roughness: float = 0.0
    """How much the grain breaks up the specular lobe. Small numbers do a lot."""

    anisotropy: float = 0.0
    """0 = sand, 1 = drawn out along U. Timber fibre and rolled plate want this."""


def apply_grain(maps: "MapSet", grain: "Grain", seed: int) -> None:
    """Adds `grain` to a finished map set, in place, preserving physical scale.

    The height field is converted back to metres, the grain is added there, and
    `height_scale` is re-derived from the result — so the relief the normal map
    encodes stays a true slope and the reported `relief_mm` stays honest. Doing
    it by clipping into [0, 1] instead would have quietly flattened the peaks of
    every deep surface in the set, gravel worst of all.
    """
    res = maps.height.shape[0]
    field = micro_grain(res, maps.world_size, seed, grain.feature_mm / 1000.0,
                        anisotropy=grain.anisotropy)

    metres = maps.height * maps.height_scale + field * (grain.relief_mm / 1000.0 / 6.0)
    low, high = float(metres.min()), float(metres.max())
    maps.height = ((metres - low) / max(high - low, 1e-9)).astype(np.float32)
    maps.height_scale = max(high - low, 1e-9)

    if grain.albedo > 0.0:
        # Value only, never hue: grain changes how much light comes back, not
        # what colour the material is.
        maps.albedo = (maps.albedo * (1.0 + field[..., None] * (grain.albedo / 3.0))).astype(np.float32)

    if grain.roughness > 0.0:
        maps.roughness = np.clip(
            maps.roughness + field * (grain.roughness / 3.0), 0.0, 1.0).astype(np.float32)


def stain(
    res: int,
    seed: int,
    threshold: float = 0.5,
    softness: float = 0.06,
    detail: float = 0.40,
    low_cycles: float = 1.5,
    beta: float = 2.4,
) -> np.ndarray:
    """A stain-shaped mask with a *ragged* edge, in [0, 1].

    This exists because the obvious construction is wrong in a way that is only
    obvious once rendered. Thresholding a smooth low-frequency field —
    `smoothstep(a, b, warped_fbm(...))` — produces enormous smooth-edged bubbles:
    the field varies slowly, so its contour lines are slow curves, and the result
    reads as clouds painted onto the wall rather than as damp.

    The fix is to perturb the field with mid- and high-frequency noise *before*
    thresholding. The contour then has to detour around every small wiggle, so
    the boundary is broken at every scale at once, which is exactly what a real
    water mark, rust bloom or paint failure looks like: a large-scale shape with
    a fractal edge.
    """
    large = warped_fbm(res, seed, beta=beta, strength=res * 0.05, low_cycles=low_cycles)
    mid = fbm(res, seed + 13, beta=1.8, low_cycles=6.0)
    fine = fbm(res, seed + 17, beta=1.4, low_cycles=20.0)

    field = large + (mid - 0.5) * detail + (fine - 0.5) * detail * 0.45
    return smoothstep(threshold - softness, threshold + softness, field)


def runs(source: np.ndarray, length: int, falloff: float = 1.6, axis: int = 0) -> np.ndarray:
    """Drags a mask along one axis with decay — water running down a surface.

    Gravity is the strongest tell that a stain is real: rust, seepage and grime
    all originate somewhere and travel *one* way. Radiating blobs read as paint.
    `np.roll` keeps it periodic, so a streak that leaves the bottom re-enters the
    top and the tiling survives.
    """
    out = np.zeros_like(source)
    for step in range(1, max(2, length)):
        out = np.maximum(out, np.roll(source, step, axis=axis) * (1.0 - step / length) ** falloff)
    return out.astype(np.float32)


def scratches(res: int, seed: int, count: int, length: float, width: float) -> np.ndarray:
    """Thin wrapped line marks — scuffs, drag marks, tool scars.

    Drawn by walking a point across the torus and stamping it, then blurring.
    Wrapping the walk with a modulo is what keeps them continuous across the
    seam instead of stopping at the edge like a scratch on a photograph.
    """
    generator = rng(seed)
    canvas = np.zeros((res, res), dtype=np.float32)
    steps = max(2, int(length * res))

    for _ in range(count):
        angle = generator.uniform(0.0, math.pi * 2.0)
        wobble = generator.uniform(-0.6, 0.6)
        y = generator.uniform(0.0, res)
        x = generator.uniform(0.0, res)
        strength = generator.uniform(0.35, 1.0)

        for step in range(steps):
            drift = angle + wobble * math.sin(step / max(1.0, steps * 0.25))
            y = (y + math.sin(drift)) % res
            x = (x + math.cos(drift)) % res
            canvas[int(y), int(x)] = max(canvas[int(y), int(x)], strength)

    return normalise01(blur(canvas, max(0.6, width * res * 0.5)))


# ── Repetition and water ────────────────────────────────────────────────────
#
# The two things that separate a tiling texture from a surface. Both are
# operators applied at the end of a builder rather than options inside it, so
# every material gets the same treatment and the reasoning is written once.


def detile(colour: np.ndarray, strength: float = 0.75, cycles: float = 2.2,
           axes: Tuple[int, ...] = (0, 1)) -> np.ndarray:
    """Flattens the low-frequency luminance a tile repeats as a visible template.

    A repeat is not seen texel by texel. What the eye locks onto is the *blob* —
    the one large damp patch, the one bright corner — reappearing on a lattice
    every few metres. Local contrast is not the problem; the tile's low-frequency
    envelope is, and it is also the cheapest thing to remove.

    So: blur the luminance down to features longer than `1 / cycles` of a tile,
    and divide that envelope back out. Dividing rather than subtracting is what
    keeps this from being a fade — every ratio inside the tile survives exactly
    (grout stays as dark relative to tile, rust as dark relative to plate), only
    the slow drift across the tile goes. Hue is untouched for the same reason:
    colour variation tiles far less visibly than value variation does, so it is
    the variation worth keeping.

    Not 1.0. A completely flat-fielded texture reads as printed wallpaper — real
    walls do have slow variation, they just do not have *the same* slow variation
    every 1.8 m. 0.75 removes the lattice and leaves the surface alive.

    `axes` restricts which directions are flattened, and the reference level is
    the mean *over those axes only*. A material whose vertical structure is
    registered to the floor — `build_wall_plaster`'s rising damp — passes
    `axes=(1,)`: each row is normalised to its own mean, so drift along the wall
    goes and the damp front survives untouched. Flattening both axes there would
    delete the feature the material exists for.

    The correct fix is stochastic or triplanar blending in the shader. The
    materials here are bound to URP's stock Lit shader by an editor script in
    another area, so there is no shader to put it in, and this is what can be
    done from the texture side alone.
    """
    if strength <= 0.0:
        return colour

    res = colour.shape[0]
    luminance = np.clip(colour @ np.asarray([0.2126, 0.7152, 0.0722], dtype=np.float32), 1e-4, None)

    # sigma from the requested cut-off: a Gaussian's half-power point sits near
    # 1 / (2 pi sigma) cycles per texel. Filtered one axis at a time so a
    # restricted `axes` really does leave the other direction alone; `mode="wrap"`
    # for the same reason `blur` uses it — a non-periodic filter would author the
    # very seam this whole file is built to avoid.
    sigma = res / (2.0 * math.pi * max(0.2, cycles))
    envelope = luminance
    for axis in axes:
        envelope = ndimage.gaussian_filter1d(envelope, sigma=sigma, axis=axis, mode="wrap")

    reference = np.mean(luminance, axis=axes, keepdims=True)
    gain = (reference / np.clip(envelope, 1e-4, None)) ** strength

    return (colour * gain[..., None]).astype(np.float32)


def water_line(height: np.ndarray, fraction: float) -> float:
    """The height a given fraction of the surface sits below.

    Water levels have to be quantiles, not constants. Every builder normalises
    its height field differently — packed gravel is a max over three layers of
    domes, so its 5th percentile sits at 0.538 and *nothing* is near zero, while
    a concrete slab is a flat 0.86 with cracks cut to 0.16. A level of 0.34 read
    like "the bottom third" and flooded 0.3 % of the gravel and none of the
    concrete: both floors measured wet, neither looked it, and the mistake is
    invisible until something is rendered.

    Asking for a fraction instead states the intent — "the lowest third of this
    surface holds water" — and stays true when a builder's relief is re-tuned.
    """
    return float(np.quantile(height, np.clip(fraction, 0.0, 1.0)))


def standing_water(height: np.ndarray, level: float, softness: float = 0.04) -> np.ndarray:
    """How deeply flooded each texel is, given a water level in height units.

    Water is the one surface feature that is *purely* a function of the height
    field: it fills from the bottom up and its top is flat. Deriving it rather
    than painting it is what makes a puddle sit in the hollows between gravel
    stones and along a crack instead of floating over them as a decal.
    """
    return smoothstep(level + softness, level - softness, height)


def wet(colour: np.ndarray, roughness: np.ndarray, height: np.ndarray,
        film: np.ndarray, pool: np.ndarray, level: float,
        darkening: float = 0.55, film_roughness: float = 0.30,
        pool_roughness: float = 0.09) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Applies §03's 「물이 있는 층」 to a surface: damp film, then standing water.

    §03's first worked example of a clue is a floor with water on it, which makes
    wet a gameplay-readable surface state rather than decoration — a player has
    to be able to say "this is the flooded one" from a beam sweep.

    Three separable physical effects, applied in the order they happen:

    * **Darker.** Water on a rough surface removes the air/solid interface that
      was scattering light straight back out, so more of it enters the substrate
      and fewer photons return. A damp patch is genuinely darker, not tinted.
    * **Smoother.** A film fills the micro-roughness, which is what turns the
      flashlight's diffuse pool into a streak. This is the whole read: at a
      grazing angle from 1.63 m the beam skips off water and does not off stone.
    * **Flat.** Standing water has a level top. Pulling the height field up to
      the water line where it is pooled is what makes a puddle mirror the room
      instead of mirroring the gravel underneath it.

    `film` is the damp area, `pool` the subset deep enough to hold water.
    """
    film = np.clip(film, 0.0, 1.0)
    pool = np.clip(pool, 0.0, 1.0)
    both = np.clip(film + pool, 0.0, 1.0)

    colour = (colour * (1.0 - darkening * both)[..., None]).astype(np.float32)

    roughness = np.where(film > 0.0, roughness * (1.0 - film) + film_roughness * film, roughness)
    roughness = np.where(pool > 0.0, roughness * (1.0 - pool) + pool_roughness * pool, roughness)

    height = (height * (1.0 - pool) + max(level, 0.0) * pool).astype(np.float32)

    return colour.astype(np.float32), roughness.astype(np.float32), height


# ── Height → normal, AO ─────────────────────────────────────────────────────


def height_to_normal(height: np.ndarray, world_size: float, height_scale: float) -> np.ndarray:
    """Tangent-space normal from the real slope of the height field.

    `height` is unitless [0,1]; `height_scale` is the metres between its 0 and
    its 1, and `world_size` is the metres the tile spans. So `dh/dx` below is a
    true gradient in metres per metre and the relief strength of every material
    is set by a number with a physical meaning, not by a taste slider. That is
    why gravel and tile can share this function and still read as different
    depths of surface.

    Differences are taken with `np.roll`, which is periodic — the normal map
    tiles because the height field does, with no special case at the border.
    OpenGL convention (green = +Y), which is what Unity samples.
    """
    res = height.shape[0]
    metres_per_texel = world_size / res

    dz_dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * height_scale / (2.0 * metres_per_texel)
    dz_dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * height_scale / (2.0 * metres_per_texel)

    normal = np.stack([-dz_dx, -dz_dy, np.ones_like(height)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return normal.astype(np.float32)


def ambient_occlusion(height: np.ndarray, world_size: float, height_scale: float, strength: float = 1.0) -> np.ndarray:
    """Cavity-style AO: how far below its own neighbourhood a texel sits.

    Compared against several blur radii rather than one, so a deep narrow grout
    line and a wide shallow dish both darken. Cheap, and for surfaces lit almost
    entirely by a moving flashlight it is indistinguishable from a ray-traced
    bake — the occlusion that matters here is contact shadowing in the joints.
    """
    relief = np.zeros_like(height)
    res = height.shape[0]
    scales = 0

    for radius_metres in (0.008, 0.02, 0.05, 0.12):
        sigma = radius_metres / world_size * res
        if sigma < 0.7:
            continue

        # Depth below the local mean, in metres, over the radius it was measured
        # at — a dimensionless "how enclosed is this texel". Dividing by the
        # radius is the part that has to be right: the first version divided by
        # its square root and multiplied by `height_scale` separately, which left
        # the whole term four orders of magnitude too small. Every AO map came out
        # at a mean of 0.99, imported fine, and did nothing at all.
        drop = np.clip(blur(height, sigma) - height, 0.0, None) * height_scale
        relief += drop / radius_metres
        scales += 1

    if scales == 0:
        return np.ones_like(height)

    return np.clip(1.0 - (relief / scales) * strength * 3.2, 0.0, 1.0).astype(np.float32)


# ── Material description ────────────────────────────────────────────────────


@dataclass
class MapSet:
    """The four maps a material is made of, plus what they mean physically."""

    albedo: np.ndarray
    """Linear RGB, shaped (res, res, 3). Calibrated by `finish_albedo`."""

    roughness: np.ndarray
    """GGX roughness in [0,1]. Unity is handed 1 − this as smoothness."""

    height: np.ndarray
    """Unitless [0,1]; multiply by `height_scale` for metres."""

    metallic: np.ndarray
    """0 for dielectrics. Only the steel surfaces put anything here."""

    world_size: float
    """Metres one tile spans. Sets the material's UV tiling in Unity."""

    height_scale: float
    """Metres between height 0 and height 1. Sets normal-map strength."""


@dataclass(frozen=True)
class PhotoSpec:
    """A CC0 photo-scan base layer laid under a material's procedural detail.

    WHY THIS EXISTS, when the file header says nothing is photographed
    -----------------------------------------------------------------
    It said that, and the walls read "너무 허접" — flat, synthetic, obviously
    generated — because procedural noise approximates the *statistics* of a real
    surface without its *history*: a photograph of a basement wall carries fifty
    years of one-off events (a spill here, a repair there, a brick fired darker)
    that no noise field reproduces, and the eye reads exactly that history as
    "real". So a curated set of CC0 scans (PolyHaven / ambientCG, all
    commercial-OK, no attribution required) is the BASE albedo/normal/rough for
    the surfaces that have a good match, and the existing procedural passes —
    grime, §3.8c wet, §3.9 grain — layer ON TOP. Photo for the history,
    procedure for the game-specific state. Surfaces with no honest match
    (`Floor_Wood`, `Floor_Gravel`, the soffit, the trims) keep their `build`.

    HOW IT STAYS INSIDE EVERY EXISTING CONTRACT
    -------------------------------------------
    The photo's tangent normal is *integrated* into a height field (periodic
    Poisson, `integrate_normal_to_height`) rather than shipped raw, so the whole
    downstream pipeline is unchanged: `generate` still derives the normal and the
    AO from that one height with the same functions every procedural material
    uses, `apply_grain` still adds §3.9 grain to it, `finish_albedo` still lands
    the albedo mean in the §03 band, and `verify` still measures the result. The
    photo swaps the *source* of the base maps; it does not touch the packing, the
    file names, the resolution or the manifest.
    """

    set_dir: str
    """Directory under the CC0 root (`tools/textures/cc0`), e.g. 'polyhaven/brick_wall_10'."""

    provenance: str
    """Human-readable credit, for docs/ASSETS.md and the manifest. All CC0."""

    height_scale: float
    """Metres the integrated relief is scaled to — sets normal/AO strength.

    Kept at the matching procedural builder's value so the beam meets the same
    depth of surface it was tuned against, not a photograph's arbitrary one.
    """

    tiles: int = 1
    """Whole photo repeats across one material tile. 1 unless the scan's real
    features come out the wrong physical size at the material's `world`."""

    offset: Tuple[float, float] = (0.0, 0.0)
    """Fractional (V, U) roll, to decorrelate a scan shared between two materials
    (the concrete wall and the concrete floor) without breaking its tiling."""

    has_metal: bool = False
    metallic_ceiling: float = 0.0
    """Metals stay *partial* on purpose — see `build_metal_floor`: the scene's
    ambient is near black, so a fully metallic surface with nothing to reflect is
    a black hole. The photo's metalness is scaled by this ceiling, never used raw."""

    grime: float = 0.16
    """Broad soot/dirt darkening laid over the scan. Small — the scan already
    carries most of its own dirt; this is the room's, not the material's."""

    detile_strength: float = 0.5
    detile_axes: Tuple[int, ...] = (0, 1)
    """The scan is one tile repeated, so its low-frequency envelope repeats on the
    lattice `detile` attacks. Axes match the material's `tiling_axes`."""

    recess_from_albedo: float = 0.0
    """Sinks the scan's darker (grimy, stained, jointed) texels into the relief.
    Zero for surfaces the integrated normal already gives depth; raised for
    near-flat stock (formwork concrete) so its AO has something to occlude."""

    kind: str = "wall"
    """'wall' (generic damp streaks), 'floor' (traffic lane), or 'damp_band'
    (plaster's rising damp, keyed to the floor line — see `build_wall_plaster`)."""

    wet_fraction: float = 0.0
    """Quantile of the height flooded, per §3.8c. 0 = dry. Water pools in the
    scan's own hollows because it is keyed to the integrated height."""

    wet_darken: float = 0.5
    wet_film_rough: float = 0.24
    wet_pool_rough: float = 0.09
    traffic: float = 0.0
    """Floor only: a worn central lane, lighter and smoother, drawn broad so it
    adds no repeatable shape."""

    tint_mul: Tuple[float, float, float] = (1.0, 1.0, 1.0)
    """A gentle per-channel multiply on the scan's albedo, before overlays. The
    concrete scans run warm sepia; cooling them a little seats them in the dank
    basement register and — for the floor — pulls it off the warm gravel it would
    otherwise share a palette with. `finish_albedo` re-lands the mean after."""

    rust: float = 0.0
    """Floor metal: iron-oxide bloom in the plate's hollows and a few patches,
    with a local roughen. §12 asks the stair for 'rust streaks'; kept light so the
    plate stays the glossiest floor with the brightest specular."""

    ao_floor: float = 0.0
    """Lifts the baked-AO floor toward 1.0 for a scan whose recessed texels crush
    to black under the dim flashlight. Applied in `generate` after the AO is baked
    (so `verify` measures the floored map): occ' = ao_floor + (1-ao_floor)*occ.

    Exists because the CC0 swap measured +6–8 pts more crushed (pixels < 2/255) in
    every zone view than the procedural fallback on the identical map/seed, at an
    unchanged mean luminance — a shadow-floor problem, not a brightness one. This is
    the safe half of the recovery: it lifts recessed pixels without touching albedo,
    so no midtone detail moves. It scales the AO contrast by (1-ao_floor), so it is
    kept small enough that `verify`'s crevice-vs-peak contrast gate still holds.
    Zero (the default) leaves the map untouched — every non-mapped surface keeps it."""

    shadow_lift: float = 0.0
    shadow_knee: float = 0.22
    """A gentle low-mid lift of the scan albedo's darkest texels, the second half of
    the same recovery. albedo *= 1 + shadow_lift * clip((shadow_knee - luma)/shadow_knee, 0, 1),
    applied after grain and before `finish_albedo`, which re-lands the mean — so the
    mean does not move and nothing at or above `shadow_knee` (linear luma) is touched;
    only the darkest mortar/grout/brick-face texels rise enough to clear the crush
    floor. The relief lives in the normal/height maps, which are left alone, so the
    photoreal 3D detail survives; this softens albedo micro-contrast only, and only
    at the bottom. Zero (the default) leaves the albedo untouched."""


@dataclass(frozen=True)
class MaterialSpec:
    """One shippable surface: how to build it, and where it plugs into the kit."""

    name: str
    build: Callable[[int, int], MapSet]
    seed: int
    target_albedo: float
    """Intended mean linear albedo. An art decision, and it must sit in the band."""

    note: str
    """Why this surface looks the way it does. Ends up in the generated manifest."""

    slots: Tuple[str, ...] = ()
    """MapKit material slot names this binds to. Empty = generated but unbound."""

    ao_strength: float = 1.0
    """How much of the baked cavity occlusion to keep, 0–1.

    Exists for exactly one reason and is 1.0 everywhere else. The engine runs
    screen-space AO as well (`AtmosphereSetup.TuneAmbientOcclusion`), and the two
    occlude the *same* ambient term — so a material whose relief is deep enough
    for the baked map to be dark on its own has its shadows counted twice.

    That is only a real problem for gravel: 45 mm of relief against 5–13 mm for
    everything else in the set, and a measured AO contrast of 0.68 against 0.07
    for the concrete slab. Zone C sat at 25.5 % of the frame legible against
    ART.md's 30 % floor with every other zone inside the band, and the excess is
    the double count rather than the paint. 1.0 → 0.72 bought 2.6 points of it
    and 0.72 → 0.55 was needed for the rest; the stones still shade themselves,
    the engine is simply no longer shading them a second time.

    Turning the SSAO down globally to fix one zone would have been the wrong
    lever — it would drag the four zones that are in band out of it — and so
    would flattening gravel's relief, which is §12's whole reason for choosing
    it: 자갈 is the floor you cannot mistake for another because every stone
    throws its own shadow.
    """

    grain: "Grain | None" = None
    """The sub-centimetre layer, or None for a surface that genuinely has none.

    Every material in the shipped set carries one. It is declared here rather
    than built into each builder so that the whole set's near-field detail can
    be read, compared and re-tuned from a single table — which is exactly what
    the scale correction made necessary, since it doubled the texel density
    available to all of them at once.
    """

    detail: str = ""
    """Which shared micro-normal from `DETAILS` this surface wears, or "" for none.

    Below about 4 mm there is no room left in a 410–683 px/m base map, and that
    is the band a player standing on a floor is actually looking at. A second,
    much finer normal tiled at its own rate is the only way to put anything
    there; see `DETAILS`.
    """

    tiling_axes: Tuple[int, ...] = (0, 1)
    """Which axes must tile. 0 is V (down the texture), 1 is U (across).

    Almost everything tiles both ways. Trim is the exception: a skirting board is
    a *cross-section* — a profile that repeats along its length and does not
    repeat across its height. Demanding it tile vertically would mean forcing the
    top edge of the board to match the floor-scuffed bottom edge, which is not a
    seam fix, it is a wrong board. Declaring the axis is the honest move; silently
    loosening the threshold for everything would have hidden real seams elsewhere.
    """

    photo: "PhotoSpec | None" = None
    """A CC0 photo-scan base, or None to build purely from `build` (the default).

    When present, `generate` builds the base maps from the scan
    (`build_photo_surface`) and layers this material's grime/wet/grain on top;
    `build` becomes the fallback for a checkout without the scans. When None the
    material is fully procedural, which is the honest state for the four surfaces
    with no good match downloaded.
    """


def finish_albedo(colour: np.ndarray, target_mean: float) -> Tuple[np.ndarray, float]:
    """Lands a linear albedo on its intended mean and clamps it to real materials.

    The gain is a deliberate, reported calibration step, not a rescue: it is
    bounded by `MAX_CALIBRATION_GAIN`, so a surface painted far from its target
    fails the run instead of being silently normalised into looking fine. Scaling
    happens in linear space, which preserves the ratios between grout and tile,
    rust and plate — the contrast survives, only the overall level moves.
    """
    if not ALBEDO_MIN_LINEAR <= target_mean <= ALBEDO_MAX_LINEAR:
        raise AssertionError(
            "target mean %.3f is outside the [%.2f, %.2f] band; §03 needs the beam to "
            "land on something that reflects it" % (target_mean, ALBEDO_MIN_LINEAR, ALBEDO_MAX_LINEAR)
        )

    colour = np.clip(colour, 1e-4, None)
    gain = target_mean / float(colour.mean())
    if not (1.0 / MAX_CALIBRATION_GAIN) <= gain <= MAX_CALIBRATION_GAIN:
        raise AssertionError(
            "albedo needs a %.2fx correction to reach %.3f — the painted values are wrong, "
            "fix them rather than widening the gain" % (gain, target_mean)
        )

    return np.clip(colour * gain, *ALBEDO_CLAMP_LINEAR).astype(np.float32), gain


def lift_shadow_albedo(albedo: np.ndarray, lift: float, knee: float) -> np.ndarray:
    """Raise a linear albedo's darkest texels toward `knee`, leaving mid/high alone.

    `weight` is 1 at black and tapers linearly to 0 at `knee` (linear luma), so the
    multiply only touches the shadow end. The mean rises, but `finish_albedo` re-lands
    it on the material's target afterwards, so the shipped mean is unchanged and this
    reads purely as a shadow black-point lift — the measured cure for the CC0 crush
    regression that does not move the midtones or disturb the relief maps.
    """
    if lift <= 0.0:
        return albedo
    luma = albedo @ np.asarray([0.2126, 0.7152, 0.0722], np.float32)
    weight = np.clip((knee - luma) / max(knee, 1e-6), 0.0, 1.0)
    return (albedo * (1.0 + lift * weight)[..., None]).astype(np.float32)


def _as_rgb(colour: Sequence[float] | np.ndarray) -> np.ndarray:
    """Accepts either a single linear RGB triple or a full per-texel colour field."""
    array = np.asarray(colour, dtype=np.float32)
    return array if array.ndim == 3 else array.reshape(1, 1, 3)


def tint(base: Sequence[float] | np.ndarray, variation: np.ndarray,
         toward: Sequence[float] | np.ndarray, amount: np.ndarray) -> np.ndarray:
    """Blends a base colour toward another, per texel, in linear space.

    Takes a flat triple or an already-built colour field on either side so the
    material builders can chain it — brick, then paint over the brick, then damp
    over the paint — which is the order the real surface acquired them in.
    """
    mix = np.clip(amount, 0.0, 1.0)[..., None]
    return (_as_rgb(base) * variation[..., None] * (1.0 - mix) + _as_rgb(toward) * mix).astype(np.float32)


# ── §12 floors ──────────────────────────────────────────────────────────────
#
# Five surfaces that have to be told apart at a glance under a 44° beam
# (`GameConstants.FlashlightHalfAngle` × 2). Each one is given a different
# *structure*, because structure is what survives when the beam is off-axis.


def build_wood(res: int, seed: int) -> MapSet:
    """§12 구역 A — 나무. Boards, grain, gaps, and wear where feet go.

    The recognisable feature at a glance is the long parallel gap lines: they
    stay legible at grazing angles where a wood *colour* would just read as
    "brown floor". Boards are broken into staggered lengths so a beam sweeping
    along the corridor crosses butt joints, which is the cue that says "planks"
    faster than grain does.
    """
    world = 2.0
    planks = 10
    segments = 4

    plank_index = index_of(res, planks, axis=1)
    plank_position = stripes(res, planks, axis=1)

    # Each board is cut into lengths at a different offset, so butt joints never
    # line up across the floor — the give-away of a tiling texture.
    stagger = cell_random(plank_index, planks, seed + 1, 0.0, 1.0)
    board_position = stripes(res, segments, axis=0, phase=stagger)
    board_index = plank_index * segments + index_of(res, segments, axis=0, phase=stagger)

    gap = np.maximum(groove(plank_position, 0.055), groove(board_position, 0.030) * 0.75)

    # Grain: noise stretched hard along the board, plus a slow ring structure.
    grain = fbm(res, seed + 2, beta=1.35, low_cycles=3.0)
    grain = blur(grain, res * 0.010)
    grain_stretch = warp(grain, np.zeros((res, res)), (fbm(res, seed + 3, beta=2.4) - 0.5) * res * 0.02)
    rings = normalise01(np.abs(np.sin(grain_stretch * math.pi * 9.0 + cell_random(board_index, planks * segments, seed + 4, 0.0, 6.0))))

    board_tone = cell_random(board_index, planks * segments, seed + 5, 0.72, 1.28)

    # A soft traffic lane, deliberately low contrast and broken up by noise: a
    # hard stripe would tile into a painted line down the middle of the corridor.
    lane = np.exp(-((stripes(res, 1, axis=0) - 0.5) ** 2) / 0.055)
    lane = np.clip(lane * (0.55 + 0.45 * fbm(res, seed + 6, beta=2.0, low_cycles=1.0)), 0.0, 1.0)

    scuff = scratches(res, seed + 7, count=40, length=0.35, width=0.0015)
    dirt = warped_fbm(res, seed + 8, beta=2.1, strength=res * 0.03)

    # Old boards cup: the top face dries faster than the underside, so each
    # plank curls its edges up a couple of millimetres. Under a beam sweeping
    # along the corridor that puts a soft highlight-shadow pair on *every*
    # plank edge — the long parallel read §12 wants from this floor, doubled,
    # where the gap lines alone gave one thin dark line per board.
    cup = (np.abs(plank_position - 0.5) * 2.0) ** 2 * (0.5 + 0.5 * board_tone)

    height = np.clip(
        0.78
        + board_tone * 0.06
        + rings * 0.05
        + grain_stretch * 0.06
        + cup * 0.10
        - gap * 0.85
        - lane * 0.03,
        0.0, 1.0,
    )

    shade = board_tone * (0.80 + 0.20 * rings) * (0.85 + 0.30 * grain_stretch)
    shade *= 1.0 - 0.28 * dirt
    shade *= 1.0 - 0.35 * gap

    colour = tint((0.300, 0.170, 0.086), shade, (0.055, 0.036, 0.026), gap * 0.85)
    # Worn wood goes grey and pale, not darker — it is the finish that is missing,
    # not the timber. Darkening the lane would have been the intuitive move and
    # the wrong one: it reads as a stain rather than as traffic.
    colour = tint(colour, np.ones((res, res), np.float32), (0.250, 0.200, 0.148), lane * 0.30)
    colour *= 1.0 - 0.20 * scuff[..., None]

    # Lane polish deepened (−0.16 → −0.26): the traffic path is the *smooth*
    # population on this floor and it was close enough to the base to read as
    # the same finish. Feet burnish; the beam should streak down the walk line
    # and go matte at the walls.
    roughness = np.clip(
        0.82 - 0.26 * lane - 0.05 * board_tone + 0.10 * grain_stretch + 0.12 * gap + 0.08 * scuff,
        0.05, 1.0,
    )

    colour = detile(colour, strength=0.60)

    # Wood is the one floor that should not hold water — it drains through the
    # gaps and swells rather than pooling. So: damp wicking up out of the joints
    # and nothing standing on the boards. Getting this wrong in the other
    # direction would cost the zone its identity, because a lake on a timber
    # floor reads as the 자갈 zone's flooding in the wrong room.
    seepage = stain(res, seed + 31, threshold=0.52, softness=0.10, detail=0.5, low_cycles=1.7)
    film = np.clip(blur(gap, res * 0.008) * 1.5, 0.0, 1.0) * seepage
    colour, roughness, height = wet(
        colour, roughness, height, film, np.zeros_like(film), 0.0,
        darkening=0.38, film_roughness=0.40)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.013)


def build_tile(res: int, seed: int) -> MapSet:
    """§12 구역 B — 타일. Grid, grout, chips, and the gloss that catches the beam.

    This is the surface that most needs to *shine*: §12 gives zone B the open
    hall and the observation post, and a glossy floor there gives the flashlight
    a specular streak that reads as distance. The grid is the strongest
    at-a-glance cue in the set, so the grout is cut deep and kept dark.
    """
    world = 2.0
    count = 8

    position_y = stripes(res, count, axis=0)
    position_x = stripes(res, count, axis=1)
    tile_index = index_of(res, count, axis=0) * count + index_of(res, count, axis=1)

    grout = np.maximum(groove(position_y, 0.055, softness=0.5), groove(position_x, 0.055, softness=0.5))

    # Chips: the corners take the damage, so the chip mask is biased to them.
    corner = np.minimum(np.minimum(position_y, 1.0 - position_y), np.minimum(position_x, 1.0 - position_x))
    chip_noise = fbm(res, seed + 1, beta=1.4, low_cycles=14.0)
    chips = smoothstep(0.62, 0.80, chip_noise) * smoothstep(0.22, 0.02, corner)

    # Wide tone spread per tile: a tiled floor laid over decades is never one
    # batch, and it is the mismatch between neighbouring tiles that makes the
    # grid legible from across a room rather than only underfoot.
    tile_tone = cell_random(tile_index, count * count, seed + 2, 0.74, 1.26)
    mottle = blur(fbm(res, seed + 3, beta=2.3, low_cycles=3.0), res * 0.004)

    f1_craze, f2_craze, _ = worley(res, 26, seed + 4, jitter=0.9)
    crazing = 1.0 - smoothstep(0.0, 0.012, f2_craze - f1_craze)

    grime = np.clip(
        stain(res, seed + 5, threshold=0.52, softness=0.10, detail=0.5, low_cycles=2.0) * 0.7
        + stain(res, seed + 9, threshold=0.72, softness=0.04, detail=0.6, low_cycles=5.0) * 0.5,
        0.0, 1.0)

    # Grime collects in the grout — the single detail that stops a tiled floor
    # looking like a bathroom showroom.
    grout_grime = grout * (0.45 + 0.55 * grime)

    height = np.clip(
        0.88
        + tile_tone * 0.02
        - grout * 0.80
        - chips * 0.45
        - crazing * 0.03,
        0.0, 1.0,
    )

    shade = tile_tone * (0.90 + 0.20 * mottle)
    shade *= 1.0 - 0.22 * grime
    colour = tint((0.360, 0.352, 0.330), shade, (0.105, 0.100, 0.090), np.clip(grout * 0.9 + grout_grime * 0.4, 0, 1))
    colour = tint(colour, np.ones((res, res), np.float32), (0.185, 0.172, 0.155), chips * 0.85)
    colour *= 1.0 - 0.10 * crazing[..., None]

    # A minority of tiles have lost their glaze entirely — worn through by
    # decades of feet, abruptly matte against their neighbours. Per tile, not
    # noise: glaze fails by the piece, and a checker of gloss answers a moving
    # beam tile by tile, which is the most legible §12 cue this floor has
    # after the grid itself.
    worn_tile = smoothstep(0.72, 0.88, cell_random(tile_index, count * count, seed + 6, 0.0, 1.0))
    roughness = np.clip(
        0.22 + 0.60 * grout + 0.45 * chips + 0.14 * grime - 0.10 * tile_tone
        + 0.34 * worn_tile * (1.0 - grout),
        0.06, 1.0,
    )

    colour = detile(colour, strength=0.75)

    # Water on a tiled floor sits in the grout lines first — the grid fills and
    # the field stays merely damp. That is the most legible wet read of the five
    # floors, because the beam then finds a lit *lattice* rather than a lit
    # patch, and a lattice tells a player which way the room runs.
    seepage = stain(res, seed + 31, threshold=0.44, softness=0.10, detail=0.5, low_cycles=1.6)
    water_level = water_line(height, 0.14)
    pool = standing_water(height, water_level, softness=0.04) * np.clip(seepage * 1.3, 0.0, 1.0)
    film = np.clip(seepage - pool, 0.0, 1.0) * 0.8

    colour, roughness, height = wet(
        colour, roughness, height, film, pool, water_level,
        darkening=0.45, film_roughness=0.16, pool_roughness=0.08)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.010)


def build_gravel(res: int, seed: int) -> MapSet:
    """§12 구역 C — 자갈. Loose stones, and the deepest relief in the set.

    Gravel is the one floor with no flat plane at all, so it is unmistakable the
    instant a beam crosses it — every stone throws its own shadow. Three sizes of
    stone are layered because a single Worley scale reads as a golf ball; real
    gravel has fines packed between the big pieces.
    """
    world = 1.5

    # Layers combine with `maximum`, not by adding. Gravel is packed stone: the
    # surface at any point is the top of whichever stone is there, so the height
    # is a max over the layers. Summing them — the obvious thing, and what this
    # did first — makes regions where several domes overlap pile up into broad
    # mounds, and the render came back looking like rippled sand dunes with the
    # individual stones washed out.
    height = np.zeros((res, res), dtype=np.float32)
    shade = np.full((res, res), 0.9, dtype=np.float32)

    for level, (cells, lift) in enumerate(((16, 0.00), (27, 0.10), (45, 0.18))):
        distance, _, owner = worley(res, cells, seed + level * 37, jitter=1.0)

        # A dome per stone: high at the centre, falling to the gap between stones.
        radius = 0.66 / cells
        dome = np.sqrt(np.clip(1.0 - (distance / radius) ** 1.7, 0.0, 1.0))
        dome = dome * per_cell(owner, cells, seed + level * 37 + 11, 0.60, 1.0)

        # Smaller stones sit slightly proud, filling between the larger ones.
        candidate = dome * (1.0 - lift) + lift * (dome > 0.02)
        winner = candidate > height
        shade = np.where(winner, per_cell(owner, cells, seed + level * 37 + 13, 0.45, 1.45), shade)
        height = np.maximum(height, candidate)

    height = normalise01(height + fbm(res, seed + 5, beta=1.4, low_cycles=26.0) * 0.06)

    grit = fbm(res, seed + 6, beta=1.2, low_cycles=30.0)
    dust = blur(fbm(res, seed + 7, beta=2.4, low_cycles=2.0), res * 0.01)

    # Depth between stones is the read. Darken the gaps, but nowhere near to
    # black: this floor was measured at 43.8% of the frame below 2/255, out the
    # bottom of ART.md's 10–40% band and out the bottom of its legible band too,
    # and the gaps between stones are where all of that black lives. §03 gates
    # the objective with darkness; a floor the beam cannot resolve gates nothing,
    # it just deletes the zone.
    pit = smoothstep(0.42, 0.0, height)

    surface = shade * (0.85 + 0.30 * grit) * (0.88 + 0.24 * dust)
    colour = tint((0.300, 0.282, 0.252), surface, (0.152, 0.144, 0.133), pit * 0.52)

    # A minority of stones are a different rock. Cheap, and it stops the pile
    # reading as one extruded material.
    _, _, owner_big = worley(res, 16, seed, jitter=1.0)
    reddish = smoothstep(0.78, 0.92, per_cell(owner_big, 16, seed + 21, 0.0, 1.0))
    colour = tint(colour, np.ones((res, res), np.float32), (0.235, 0.150, 0.108), reddish * 0.55)

    roughness = np.clip(0.86 - 0.10 * shade + 0.10 * grit - 0.06 * dust, 0.35, 1.0)

    # Before the water, never after. `detile` removes low-frequency luminance and
    # a puddle *is* low-frequency luminance — running it last erased every pool
    # in the first generated set and left a floor that measured wet and did not
    # look it. Everything below this line is meant to survive to the render.
    colour = detile(colour, strength=0.70)

    # §03's worked example of a clue is 물이 있는 층, and ART.md §3.6 already
    # calls zone C "the flooded 자갈 end" — the lighting says flooded and the
    # floor did not. Ballast is where standing water is most obvious in a real
    # basement, because the voids between stones hold it and the water line cuts
    # straight across a surface that has no straight line anywhere else in it.
    #
    # The pools are patchy rather than a sheet: a floor uniformly under water
    # reads as a swimming pool, and §12 needs a player to still recognise the
    # 자갈 underneath.
    flooded = stain(res, seed + 31, threshold=0.48, softness=0.07, detail=0.55, low_cycles=1.9)
    water_level = water_line(height, 0.38)
    pool = standing_water(height, water_level, softness=0.06) * flooded
    film = np.clip(blur(pool, res * 0.012) * 1.4 - pool, 0.0, 1.0) * 0.7

    colour, roughness, height = wet(
        colour, roughness, height, film, pool, water_level,
        darkening=0.55, film_roughness=0.42, pool_roughness=0.12)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.045)


def build_concrete_floor(res: int, seed: int) -> MapSet:
    """§12 구역 D — 콘크리트. Dull, stained, cracked, hairline fractures.

    Zone D holds the exit, so this is the surface players see most under stress.
    It has the least structure of the five on purpose — the contrast against
    gravel and tile is what makes those two legible. Its identity comes from
    stains and a crack network instead.
    """
    world = 2.5

    base = warped_fbm(res, seed + 1, beta=2.1, strength=res * 0.05)
    fine = fbm(res, seed + 2, beta=1.5, low_cycles=18.0)

    # Cracks: the boundary between Worley cells is where a slab actually fails.
    # The raw boundaries are dead straight, though, and a floor of clean polygons
    # reads as dried mud, not concrete — so the distance field is domain-warped
    # before thresholding, which bends each crack into an irregular path. Masked
    # hard as well, because "hairline fracture" means occasional, not a network.
    crack = np.zeros((res, res), dtype=np.float32)
    for cells, width, seed_offset in ((6, 0.0032, 31), (11, 0.0020, 47)):
        f1, f2, _ = worley(res, cells, seed + seed_offset, jitter=0.95)
        wander_y = (fbm(res, seed + seed_offset + 71, beta=2.0, low_cycles=4.0) - 0.5) * res * 0.045
        wander_x = (fbm(res, seed + seed_offset + 73, beta=2.0, low_cycles=4.0) - 0.5) * res * 0.045
        vein = 1.0 - smoothstep(0.0, width, warp(f2 - f1, wander_y, wander_x))

        # Branch decay: a crack fades along its length instead of running the
        # full width of the slab at constant strength.
        #
        # Masked at 0.56–0.80, not 0.34–0.66. The looser window let most of the
        # Worley boundaries through, so the slab arrived as a *complete* polygon
        # network — every cell outlined, edge to edge. That is dried mud or
        # crazed glaze; a concrete floor has a handful of cracks wandering across
        # an otherwise unbroken surface, and a full network was the loudest
        # generated-looking thing left in the frame once the tiling was fixed.
        # `Floor_Concrete` is bound to 13 of the kit's pieces, so it is also the
        # floor this costs the most on.
        mask = smoothstep(0.56, 0.80, fbm(res, seed + seed_offset + 5, beta=2.4, low_cycles=2.5))
        crack = np.maximum(crack, vein * mask)
    crack = blur(crack, 0.6)

    # Aggregate showing through where the surface has worn.
    aggregate_distance, _, aggregate_owner = worley(res, 46, seed + 9, jitter=1.0)
    exposed = smoothstep(0.55, 0.80, base) * smoothstep(0.020, 0.006, aggregate_distance)
    # More pockmarks, and deeper. With the crack network thinned to occasional,
    # these are the only relief a flat slab has left, and the AO guard passes on
    # them by 0.002 without this. Old floated concrete really is pitted — the
    # laitance spalls off in coin-sized flakes — so this is the honest place to
    # find the contrast rather than putting the crack network back.
    pits = smoothstep(0.72, 0.91, fbm(res, seed + 10, beta=1.3, low_cycles=26.0))

    # Kept deliberately weak. Zone D holds the exit, so this floor is read under
    # stress and its job is to be the *plain* one — heavy staining here would
    # compete with the crack network that gives it its identity.
    damp = np.clip(
        stain(res, seed + 11, threshold=0.66, softness=0.09, detail=0.5, low_cycles=2.0) * 0.6
        + stain(res, seed + 14, threshold=0.76, softness=0.03, detail=0.6, low_cycles=4.5) * 0.45,
        0.0, 1.0)
    efflorescence = stain(res, seed + 12, threshold=0.70, softness=0.05, detail=0.55, low_cycles=3.5)

    # Cracks cut deeper (0.70 → 0.82) and pits with them, because thinning the
    # crack network out to "occasional" removed most of what this floor's AO map
    # had to occlude: the run's own guard caught it at an AO contrast of 0.025
    # against a 0.03 floor, which is a map that imports, looks plausible in a
    # viewer, and does nothing. Fewer cracks is right; shallower cracks was not.
    # A real crack in a slab is a few millimetres of shadow, and it is the only
    # shadow a flat floor has.
    height = np.clip(
        0.86
        + base * 0.06
        + fine * 0.03
        + exposed * 0.04
        - crack * 0.82
        - pits * 0.52,
        0.0, 1.0,
    )

    shade = (0.86 + 0.28 * base) * (0.92 + 0.16 * fine)
    shade *= 1.0 - 0.30 * damp
    # Crack interiors lifted from 0.075 to 0.115 linear. A crack in concrete is a
    # shadow a few millimetres deep, not a hole: at 0.075 the network read as ink
    # drawn on the slab, which is the other half of why it looked printed.
    colour = tint((0.318, 0.312, 0.300), shade, (0.115, 0.111, 0.108), np.clip(crack * 0.85 + pits * 0.6, 0, 1))
    colour = tint(colour, np.ones((res, res), np.float32), (0.400, 0.392, 0.365), efflorescence * 0.35)
    colour = tint(colour, np.ones((res, res), np.float32), (0.250, 0.235, 0.205),
                  per_cell(aggregate_owner, 46, seed + 13, 0.0, 1.0) * exposed * 0.6)

    # Burnished traffic patches: forty years of feet polish a floated slab to
    # a dull sheen in the places people actually walk, and the polish darkens
    # a touch as it compacts. Broken by slow noise rather than drawn as a lane
    # so it cannot tile into a painted stripe. This is zone D's roughness
    # story: the beam streaks on the polish and dies on the dust, where tile
    # answers as a gloss *lattice* and metal as one hard lozenge — three
    # different specular signatures on the three hard floors (§12).
    burnish = smoothstep(0.62, 0.86, warped_fbm(res, seed + 15, beta=2.4,
                                                strength=res * 0.04, low_cycles=1.6)) \
        * (1.0 - np.clip(crack * 3.0, 0.0, 1.0))
    roughness = np.clip(0.74 + 0.16 * fine - 0.10 * damp + 0.14 * crack + 0.10 * pits
                        - 0.30 * burnish, 0.26, 1.0)
    colour *= (1.0 - 0.07 * burnish)[..., None]

    colour = detile(colour, strength=0.80)

    # Water on a slab does not pool in the open — it runs into the crack network
    # and stays there, and the surface around a wet crack stays damp for days.
    # Deriving the wet mask from the cracks rather than painting a second stain
    # is what ties the two features together; a damp patch that ignores the crack
    # running through it is the giveaway that both were airbrushed on.
    seepage = stain(res, seed + 33, threshold=0.46, softness=0.09, detail=0.5, low_cycles=1.7)
    # A ninth of the surface, not a sixth. `wet` flattens the height to the water
    # line where it pools, and at 0.16 the line sat at the *rim* of the crack
    # network — so the water filled every crack flush and erased the relief the
    # AO map is baked from. Water in a crack sits in the bottom of it.
    water_level = water_line(height, 0.09)
    pool = standing_water(height, water_level, softness=0.04) * np.clip(seepage + crack * 0.6, 0.0, 1.0)
    film = np.clip(blur(np.maximum(pool, crack * seepage), res * 0.016) * 2.2 - pool, 0.0, 1.0) * 0.85

    colour, roughness, height = wet(
        colour, roughness, height, film, pool, water_level,
        darkening=0.52, film_roughness=0.34, pool_roughness=0.10)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.012)


def build_metal_floor(res: int, seed: int) -> MapSet:
    """§12 계단 — 금속. Plate, rivets, rust streaks, the brightest specular of the five.

    The stair is where §04's Listener hears 울림, and it should be the surface
    that most obviously *reflects*: a low-roughness plate throws a hard specular
    lozenge back at a flashlight, which no other floor here does. Rust is the
    counterweight — it breaks the plate into panels and keeps the albedo from
    reading as one flat sheet.

    Metallic is left partial rather than 1.0 deliberately. The scene's ambient is
    near black by design (§03), and a fully metallic surface with nothing to
    reflect renders as a black hole with a highlight in it. Partial metallic
    keeps a diffuse term so the plate is still *visible* off-beam.
    """
    world = 2.0
    plates = 2

    plate_y = stripes(res, plates, axis=0)
    plate_x = stripes(res, plates, axis=1)
    plate_index = index_of(res, plates, axis=0) * plates + index_of(res, plates, axis=1)
    seam = np.maximum(groove(plate_y, 0.020, softness=0.55), groove(plate_x, 0.020, softness=0.55))

    # Rivets march along the seams — the detail that says "fabricated" instantly.
    rivet_pitch = 16
    along_y = stripes(res, rivet_pitch, axis=1)
    along_x = stripes(res, rivet_pitch, axis=0)
    near_seam_y = groove(plate_y, 0.075, softness=0.9)
    near_seam_x = groove(plate_x, 0.075, softness=0.9)
    rivet = np.maximum(
        smoothstep(0.34, 0.16, np.abs(along_y - 0.5)) * near_seam_y,
        smoothstep(0.34, 0.16, np.abs(along_x - 0.5)) * near_seam_x,
    )
    rivet = smoothstep(0.55, 0.95, rivet)

    # Tread pattern: a raised diamond grid, the reason a steel floor is walkable.
    tread_count = 26
    diamond = np.abs(stripes(res, tread_count, axis=0) - 0.5) + np.abs(stripes(res, tread_count, axis=1) - 0.5)
    tread = smoothstep(0.42, 0.24, diamond) * (1.0 - seam)

    brushed = fbm(res, seed + 1, beta=1.1, low_cycles=40.0)
    brushed = blur(brushed, 0.4) * 0.5 + 0.5 * warp(brushed, np.zeros((res, res)), np.zeros((res, res)))

    # Rust starts where water sits — the seams and the rivet heads — and runs
    # downhill from there. Sourcing it from the geometry rather than from free
    # noise is what makes it read as corrosion instead of as camouflage paint:
    # every streak has a visible origin the eye can trace back to.
    # Higher `low_cycles` than the walls on purpose: a handful of large rust
    # blooms per tile makes the repeat obvious the moment two tiles are visible
    # at once. Many small ones read as corrosion and hide the period.
    streak_source = np.clip(seam * 0.75 + rivet, 0.0, 1.0) * \
        stain(res, seed + 2, threshold=0.50, softness=0.10, detail=0.6, low_cycles=6.0)
    streak = runs(streak_source, length=int(res * 0.18), falloff=1.5)

    bloom = stain(res, seed + 3, threshold=0.62, softness=0.05, detail=0.6, low_cycles=7.0)
    rust = np.clip(bloom * 0.75 + streak * 0.95, 0.0, 1.0)

    # Corrosion is granular at close range; without this it is a smooth wash.
    rust = np.clip(rust * (0.55 + 0.45 * fbm(res, seed + 4, beta=1.5, low_cycles=18.0)) * 1.25, 0.0, 1.0)

    plate_tone = cell_random(plate_index, plates * plates, seed + 5, 0.92, 1.08)
    scuff = scratches(res, seed + 6, count=55, length=0.30, width=0.0012)

    height = np.clip(
        0.72
        + tread * 0.16
        + rivet * 0.22
        - seam * 0.60
        + brushed * 0.02
        + rust * 0.05,
        0.0, 1.0,
    )

    shade = plate_tone * (0.88 + 0.24 * brushed) * (1.0 - 0.18 * scuff)
    colour = tint((0.345, 0.352, 0.368), shade, (0.090, 0.088, 0.092), seam * 0.85)

    # Two rust tones, dark iron oxide under a lighter powdery bloom, rather than
    # one flat brown — a single tint over grey plate blends to salmon.
    colour = tint(colour, np.ones((res, res), np.float32), (0.145, 0.062, 0.028), rust * 0.92)
    colour = tint(colour, np.ones((res, res), np.float32), (0.255, 0.125, 0.055),
                  np.clip(rust - 0.35, 0.0, 1.0) * 0.75)

    # The tread and the rivet heads take the polish, so they are the brightest
    # thing on the plate — that is what the beam catches first.
    colour *= (1.0 + 0.22 * tread * (1.0 - rust))[..., None]
    colour *= (1.0 + 0.30 * rivet * (1.0 - rust))[..., None]

    roughness = np.clip(0.26 + 0.52 * rust + 0.30 * seam + 0.16 * scuff - 0.10 * tread - 0.08 * rivet, 0.08, 1.0)
    metallic = np.clip(0.50 * (1.0 - rust) * (1.0 - seam * 0.8), 0.0, 1.0)

    colour = detile(colour, strength=0.65)

    # Damp, not puddles. This is stair and walkway plate: it drains, and anywhere
    # it does not drain is exactly where the rust already is — so the wet mask is
    # the rust mask, softened. Causality both ways round, which is what stops two
    # separately painted masks from reading as two separate stickers.
    film = np.clip(blur(rust, res * 0.010) * 0.9, 0.0, 1.0) * \
        stain(res, seed + 31, threshold=0.52, softness=0.10, detail=0.5, low_cycles=2.2)
    metal_water = water_line(height, 0.18)
    pool = np.clip(standing_water(height, metal_water, softness=0.04) * film * 1.4, 0.0, 1.0)

    colour, roughness, height = wet(
        colour, roughness, height, film, pool, metal_water,
        darkening=0.42, film_roughness=0.24, pool_roughness=0.10)

    return MapSet(colour, roughness, height, metallic, world, 0.012)


# ── §12's three deep floors ─────────────────────────────────────────────────
#
# `Core/Map/FloorMaterial.cs` has named **eight** surfaces since 하강 stacked
# eight storeys, and this file shipped five. B6 병동 (카펫), B7 수몰층 (물) and
# B8 굴착층 (흙) therefore rendered on `MapSceneBuilder`'s placeholder colour —
# no albedo map, no normal, no roughness, no occlusion — for as long as those
# storeys have existed. Counted in the shipped `.mat` bytes it was six texture
# bindings each on the five that worked and **zero** on the three that did not.
#
# The guard that exists to catch exactly this,
# `ProceduralTextureMaterials.ContractualFloors`, enumerated only the five that
# already worked, so it could never fire. The three names go into that constant
# in the same change that makes these three surfaces exist and not before: a gate
# asserting something that is not yet true is a gate people learn to ignore.
#
# They are authored to the same rule as the five — a different *structure* each,
# never a different tint, because structure is what survives an off-axis beam.
# And they widen §12's distinctness requirement from a five-surface space to an
# eight-surface one. That is the part most likely to go wrong: a new floor that
# collides with an old one is a worse defect than the missing texture was,
# because the Listener channel it breaks is one a player was already using.
# Where the eight land on (albedo, roughness) is in `docs/ART.md`; the short
# version is that the three below take the two corners the five had left empty —
# dark-and-matte (흙), dark-and-mirror (물) — and the one place a mid albedo can
# still sit without touching 나무, which is directly above it in roughness (카펫).


def build_carpet(res: int, seed: int) -> MapSet:
    """§12 B6 병동 — 카펫. Contract tile, crushed lanes, ward stains.

    The ward is "the one storey finished for people rather than plant"
    (`ZoneIdentity`'s 병동 row), and what a hospital corridor is actually finished
    in is **contract carpet tile**: 50 cm squares, low loop pile, laid
    quarter-turned so the pile of every other tile runs the other way.

    That quarter-turn is this floor's at-a-glance cue and no other surface in the
    set makes it. Pile is directional — it is brighter looked into and darker
    looked along — so a beam raking the room lights a soft checkerboard that
    *appears and vanishes as the player turns*. A tint would die the moment the
    beam went off-centre; a checker that answers to viewing angle cannot, because
    it is carried by the normal map like every other structure here.

    Its second cue is the opposite of every other floor's wear. Worn timber goes
    pale and worn concrete burnishes brighter, so on both of them the walk line
    is the light one. Crushed pile lies flat: the light that used to scatter back
    out of a standing tuft now travels along it and does not come back. **On this
    floor the traffic lane is the dark one**, and where forty years of feet have
    taken the pile off entirely the pale jute backing shows through — dark lane,
    bright scar inside it, which is a two-tone read nothing else here has.
    """
    world = 2.0
    tiles = 4            # 50 cm contract tiles across a 2 m tile
    tufts = 152          # ~13 mm per tuft cluster — the finest thing 512 px/m holds

    row = index_of(res, tiles, axis=0)
    col = index_of(res, tiles, axis=1)
    tile_index = row * tiles + col
    turned = ((row + col) % 2).astype(np.float32)

    # One dome per tuft, each with its own height: loop pile is a field of
    # individual loops, and it is the *unevenness* between them that keeps a
    # carpet from rendering as felt.
    distance, _, owner = worley(res, tufts, seed + 1, jitter=1.0)
    radius = 0.70 / tufts
    tuft = np.sqrt(np.clip(1.0 - (distance / radius) ** 2, 0.0, 1.0))
    tuft = tuft * per_cell(owner, tufts, seed + 2, 0.50, 1.0)

    # The lay. Blurring *across* the run leaves the along-run detail intact, so
    # the pile comes out drawn in a direction rather than merely softened — the
    # same trick `micro_grain`'s anisotropy uses on timber fibre and rolled
    # steel. Two versions, mixed by a blurred checkerboard rather than a hard
    # one: tiles butt, they do not gap, and the loops at a joint interlock over
    # about a centimetre. Blurring the selector also means the wrap edge is
    # treated exactly like the three interior joints, so the tile still tiles.
    #
    # The smear is deliberately *short* — about a third of a tuft. The first
    # version drew it a whole tuft long and the normal map came back as a comb:
    # rendered, that is brushed steel, which is another floor in this set. Pile
    # is a dense field of loops that happens to lean, not a set of grooves, so
    # the lean has to stay smaller than the loop that carries it.
    lay = res / tufts * 0.34
    along_u = ndimage.gaussian_filter1d(tuft, sigma=lay, axis=1, mode="wrap").astype(np.float32)
    along_v = ndimage.gaussian_filter1d(tuft, sigma=lay, axis=0, mode="wrap").astype(np.float32)
    lay_mix = blur(turned, res * 0.005)
    pile = normalise01(along_u * (1.0 - lay_mix) + along_v * lay_mix)

    seam = np.maximum(groove(stripes(res, tiles, axis=0), 0.016, softness=0.70),
                      groove(stripes(res, tiles, axis=1), 0.016, softness=0.70))

    # A heather yarn, never one colour. Contract carpet is spun from two or three
    # shades precisely so it hides dirt, and that speckle is what a beam finds
    # first at a metre. Per tuft, so it survives every filter that would average
    # a noise field away — including `detile` and the mip chain.
    yarn = per_cell(owner, tufts, seed + 3, 0.48, 1.54)
    fleck = smoothstep(0.72, 0.92, per_cell(owner, tufts, seed + 4, 0.0, 1.0))
    tile_tone = cell_random(tile_index, tiles * tiles, seed + 5, 0.90, 1.10)

    # Broad, noise-broken, and deliberately not a stripe: a hard lane would tile
    # into a painted line down the middle of every ward corridor in the building.
    # Broken *less* than the first version, which mixed it 45/55 with noise and
    # left nothing legible at all — a lane the eye cannot find is not a lane, and
    # this one carries half of what tells 카펫 from 나무.
    lane = np.exp(-((stripes(res, 1, axis=0) - 0.5) ** 2) / 0.105)
    lane = np.clip(lane * (0.66 + 0.34 * warped_fbm(
        res, seed + 6, beta=2.2, strength=res * 0.05, low_cycles=1.4)), 0.0, 1.0)
    crush = smoothstep(0.18, 0.82, lane)
    # Rare. Carpet wears *through* at a doorway or a bed foot, not along a whole
    # corridor: thresholded at 0.62 it took the entire centre of the tile and the
    # lane rendered as a pale streak, which inverts the read this floor is built
    # on — the walk line is the dark one and the bald scars are the small bright
    # things inside it.
    bald = smoothstep(0.80, 0.97, lane * (0.55 + 0.45 * fbm(res, seed + 7, beta=2.0, low_cycles=5.0)))
    backing = fbm(res, seed + 8, beta=1.2, low_cycles=90.0)

    # Spills, and the rim is the point. A stain dries from its edge inward, so
    # the solids end up concentrated in a ring — the coffee-ring effect. Without
    # it a stain reads as an airbrush, which is what every painted-on stain in
    # every generated texture looks like.
    spill = np.clip(
        stain(res, seed + 9, threshold=0.70, softness=0.05, detail=0.60, low_cycles=3.2) * 0.9
        + stain(res, seed + 10, threshold=0.79, softness=0.03, detail=0.70, low_cycles=6.5) * 0.7,
        0.0, 1.0)
    ring = np.clip(blur(spill, res * 0.006) - spill, 0.0, 1.0) * 2.4
    soil = warped_fbm(res, seed + 11, beta=2.2, strength=res * 0.04, low_cycles=1.8)

    height = np.clip(
        0.52
        + pile * 0.34
        + tile_tone * 0.02
        + backing * bald * 0.03
        - crush * 0.15
        - bald * 0.32
        - seam * 0.62,
        0.0, 1.0,
    )

    shade = yarn * tile_tone * (0.84 + 0.32 * pile)
    shade *= 1.0 - 0.14 * soil
    colour = tint((0.232, 0.250, 0.208), shade, (0.086, 0.090, 0.078), np.clip(seam * 0.85, 0.0, 1.0))
    colour = tint(colour, np.ones((res, res), np.float32), (0.094, 0.092, 0.076), fleck * 0.62)

    # The quarter-turn read, and the one place this file bakes a directional
    # effect into a *tone*. It is not the tint the header warns about. Pile
    # scatters more light back the way it leans, so two tiles laid at right
    # angles genuinely differ in luminance under any one light — every real
    # contract-carpet install shows this checkerboard, and installers have a name
    # for it. The normal map carries the anisotropy that produces it, but at
    # 512 px/m a 13 mm tuft is six texels wide and the shading cancels across a
    # mip; this term is the *mean* of what the normal is already doing, so the
    # checker survives to the distance a player actually reads a room at.
    # Deliberately small — nine per cent — because it is a real material's
    # difference, not a decal of one.
    colour *= (1.0 - 0.09 * blur(turned, res * 0.006))[..., None]

    colour *= (1.0 - 0.34 * crush)[..., None]
    colour = tint(colour, np.ones((res, res), np.float32), (0.330, 0.310, 0.268), bald * 0.70)
    colour = tint(colour, np.ones((res, res), np.float32), (0.084, 0.068, 0.050),
                  np.clip(spill * 0.62 + ring * 0.55, 0.0, 1.0))

    # The matte end of the whole set, by a distance. Standing pile is as close to
    # a Lambertian surface as a real material gets — the fibres point every way
    # at once, so there is no lobe left to specular with. Everything that lowers
    # this number is fibre being flattened or removed.
    # Every term that lowers this is fibre being flattened or removed, and none of
    # them may lower it far. Crushed pile is still *fibre* — it picks up a sheen,
    # not a gloss — and a first pass that spent 0.24 on the lane and 0.42 on the
    # bald patches pulled the mean to 0.85, which is 나무's neighbourhood. Two
    # floors that answer a beam the same way are two floors the Listener cannot
    # separate, and 카펫 is the one surface here whose entire identity is that it
    # answers a beam with nothing at all.
    roughness = np.clip(
        0.98 + 0.03 * pile + 0.02 * seam
        - 0.14 * crush - 0.30 * bald - 0.08 * spill,
        0.44, 1.0,
    )

    colour = detile(colour, strength=0.70)

    # Carpet wicks; it does not pool. Damp spreads out of the wall junction and
    # travels through the backing, so the mark is broad, darker, and *not*
    # smoother — wet wool mats. Standing water on this floor would read as
    # 수몰층 one storey down, which is the surface this one has to be told apart
    # from, so the pool term is deliberately zero.
    seepage = stain(res, seed + 31, threshold=0.60, softness=0.11, detail=0.50, low_cycles=1.5)
    film = np.clip(seepage * (0.55 + 0.45 * soil), 0.0, 1.0) * 0.80
    colour, roughness, height = wet(
        colour, roughness, height, film, np.zeros_like(film), 0.0,
        darkening=0.38, film_roughness=0.90)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.0085)


def build_water(res: int, seed: int) -> MapSet:
    """§12 B7 수몰층 — 물. Standing water over a silted floor, and its shoreline.

    ART.md §3.8c: 물 is one of §12's eight floor materials in its own right, B7 is
    수몰층, and standing water is *the loudest surface in the game* — 「you cannot
    cross it unheard」. So this floor has to be recognisable **before** a player
    steps in it, from outside the beam, because stepping in it tells everyone
    within 40 m where they are.

    This is a flooded storey, not a swimming pool. The building's own screed is
    still under there, silt has settled into its hollows, and about three
    quarters of it is drowned — which leaves a **shoreline**, and the shoreline
    is the whole read. It is the only genuinely level line anywhere in the game:
    every other straight edge here is a joint, a course or a plank, and all of
    them follow the building. Water follows gravity, so its edge cuts across the
    building's geometry at whatever angle the floor happens to fall at, and a
    contour that ignores the architecture is unmistakable at a glance.

    The wet itself is `wet()` — §3.8c's three separable effects, not a fourth
    private implementation of them — driven off `water_line`, which is a quantile
    of this surface's own height field for the reason that function records. What
    is added on top of it is what a *basement* flood has and a clean model of
    water does not: dried silt cracking into polygons on the banks, a pale tide
    mark where the level has dropped, and a scum line of dust and oil held at the
    water's edge by surface tension.
    """
    world = 2.0

    # The bed: a screed floor that has settled. Slow, shallow, and it is what
    # decides where the water goes — the quantile does the rest.
    bed = warped_fbm(res, seed + 1, beta=2.2, strength=res * 0.06, low_cycles=1.4)
    fines = blur(fbm(res, seed + 2, beta=1.9, low_cycles=6.0), res * 0.004)
    silt_grain = fbm(res, seed + 13, beta=1.30, low_cycles=64.0)

    # Banks and islands. `stain` rather than a smoothed threshold, because a
    # shoreline is ragged at every scale at once and that is exactly the shape
    # `stain` exists to make — see its docstring for why the obvious
    # construction gives smooth-edged bubbles instead.
    banks = stain(res, seed + 3, threshold=0.44, softness=0.05, detail=0.65, low_cycles=1.8)
    banks = np.clip(banks + stain(res, seed + 4, threshold=0.74, softness=0.04,
                                  detail=0.70, low_cycles=5.5) * 0.7, 0.0, 1.0)

    # Rubble: flat angular pieces of the building lying in the silt. They break
    # the surface, which is what stops one big lake from reading as a lake.
    #
    # The *interior* of a Worley cell rather than a radius from its centre. A
    # radius gives perfect discs, and a field of discs on water reads as bubbles
    # or as rivets — the first version did exactly that. Broken slab is angular,
    # so the shape has to come from the cell polygon, warped so the polygon is
    # not obviously a polygon.
    chip_near, chip_far, chip_owner = worley(res, 18, seed + 5, jitter=1.0)
    chip_y = (fbm(res, seed + 14, beta=2.0, low_cycles=5.0) - 0.5) * res * 0.022
    chip_x = (fbm(res, seed + 15, beta=2.0, low_cycles=5.0) - 0.5) * res * 0.022
    rubble = smoothstep(0.004, 0.019, warp(chip_far - chip_near, chip_y, chip_x)) * \
        smoothstep(0.78, 0.94, per_cell(chip_owner, 18, seed + 6, 0.0, 1.0))

    # The drowned part is a screed floor, and screed is flat — so the bed takes
    # very little of the range and the banks take most of it. That is not a
    # shaping preference. `wet` levels the height wherever it pools, so whatever
    # share of the range sits *below* the water line is deleted from the shipped
    # relief: the first version spent three quarters of its height on a floor
    # nobody can see and shipped 8.6 mm of normal for a flooded storey.
    height = np.clip(
        0.10
        + bed * 0.11
        + fines * 0.06
        + banks * 0.62
        + rubble * 0.15,
        0.0, 1.0,
    )

    # Dried silt cracks into polygons as it shrinks — the one structure that says
    # "this was under water and is not now" without a single wet texel in it.
    # Cut from the Worley boundary for the same reason `build_concrete_floor`
    # cuts its cracks there: that difference goes to zero exactly on the boundary
    # between two cells, which is what a shrinkage fracture is.
    f1, f2, _ = worley(res, 26, seed + 7, jitter=0.95)
    wander_y = (fbm(res, seed + 8, beta=2.0, low_cycles=4.0) - 0.5) * res * 0.03
    wander_x = (fbm(res, seed + 9, beta=2.0, low_cycles=4.0) - 0.5) * res * 0.03
    craze = 1.0 - smoothstep(0.0, 0.0045, warp(f2 - f1, wander_y, wander_x))

    water_level = water_line(height, 0.74)
    pool = standing_water(height, water_level, softness=0.022)
    film = np.clip(blur(pool, res * 0.014) * 1.7 - pool, 0.0, 1.0) * 0.9

    # Cracks only where it is genuinely dry, and the AO sees them: silt under
    # water has not shrunk.
    dry = np.clip(1.0 - pool - film * 0.7, 0.0, 1.0)
    craze = craze * dry
    height = np.clip(height - craze * 0.10, 0.0, 1.0)

    # Where the level has dropped it has left a band of pale dried silt — the
    # tide mark. Measured off the height rather than painted, so it stays on the
    # contour when the bed is re-tuned.
    shore = np.exp(-((height - water_level) / 0.030) ** 2)
    tide = shore * dry * (0.55 + 0.45 * fbm(res, seed + 10, beta=2.1, low_cycles=4.0))

    # `silt_grain` is here for a measured reason. The first version's albedo put
    # 4.1 % of its energy above 25 cycles per metre — the lowest in the set by a
    # factor of three, and ART.md §3.9's definition of a surface with nothing left
    # to show once a player is closer than four metres. Three quarters of this
    # floor is under water and `wet` scales that region's contrast by a third, so
    # whatever fine structure the silt carries has to be authored *strong* to
    # survive being drowned.
    silt = 0.84 + 0.24 * fines + 0.20 * silt_grain
    shade = silt * (0.90 + 0.20 * bed) * (1.0 - 0.16 * craze)
    colour = tint((0.300, 0.292, 0.262), shade, (0.118, 0.108, 0.092), craze * 0.65)
    colour = tint(colour, np.ones((res, res), np.float32), (0.395, 0.380, 0.336), tide * 0.45)
    colour = tint(colour, np.ones((res, res), np.float32), (0.245, 0.238, 0.222),
                  per_cell(chip_owner, 18, seed + 11, 0.0, 1.0) * rubble * 0.55)

    roughness = np.clip(0.82 - 0.10 * bed + 0.08 * craze + 0.06 * tide - 0.14 * rubble, 0.30, 1.0)

    colour = detile(colour, strength=0.70)

    # §3.8c, and it is the whole material rather than a treatment on top of one.
    # `darkening` is the highest in the set: this is not a damp film, it is
    # centimetres of standing water, and what comes back off it is a reflection
    # of a room that is almost black.
    colour, roughness, height = wet(
        colour, roughness, height, film, pool, water_level,
        darkening=0.68, film_roughness=0.30, pool_roughness=0.07)

    # Surface tension holds every loose thing in the building at the edge of the
    # water: dust, soot, a skin of oil. So the shoreline is a *line* and not a
    # gradient — darker, and rough where the water beside it is a mirror. It goes
    # on after `wet` deliberately, because `wet` is the physics and this is the
    # dirt floating on it.
    scum = np.clip(shore * np.clip(pool + film, 0.0, 1.0) * 1.6, 0.0, 1.0)
    colour = (colour * (1.0 - 0.30 * scum)[..., None]).astype(np.float32)
    roughness = np.clip(roughness + 0.34 * scum, 0.0, 1.0)

    # Capillary ripple. A dead-flat mirror over a 2 m tile reads as glass, not as
    # water: standing water is never still in a building with air moving through
    # it. Tiny — a fifth of a millimetre — but it is what turns the beam's
    # reflection from a hard disc into a shimmer, and it is added to the height
    # *after* `wet` has levelled the pools, because it is a disturbance of the
    # level surface rather than of the floor underneath.
    ripple = micro_grain(res, world, seed + 12, 0.055, beta=1.6)
    height = np.clip(height + pool * ripple * 0.006, 0.0, 1.0).astype(np.float32)

    # Grit and dust on the water, silt still in suspension under it. Confined to
    # the wet, and the one thing in the pools with any contrast in it at all —
    # water itself has none, which is exactly why a rendered pool with nothing
    # floating on it reads as a hole in the floor rather than as a surface.
    motes = smoothstep(0.60, 0.92, fbm(res, seed + 16, beta=1.15, low_cycles=76.0))
    wetness = np.clip(pool + film, 0.0, 1.0)
    colour = (colour * (1.0 + (motes * 0.62 - 0.17) * wetness)[..., None]).astype(np.float32)

    return MapSet(colour.astype(np.float32), roughness.astype(np.float32), height,
                  np.zeros((res, res), np.float32), world, 0.055)


def build_earth(res: int, seed: int) -> MapSet:
    """§12 B8 굴착층 — 흙. A dig face: cut clay, spoil, tool marks, buried timber.

    The bottom of the race, and §02 puts the finish light on it, so this is the
    last surface in the descent and the one a player is standing on when the
    match is decided. §07 has been promising for seven storeys that the night
    deepens all the way down, which is why this is deliberately **the darkest
    floor in the set** — 0.17 linear against 자갈's 0.44 and 콘크리트's 0.23. Damp
    turned loam genuinely sits at 0.08–0.12; 0.17 is already a concession to
    `ALBEDO_MIN_LINEAR`, not a dark-painting of the kind this file's header
    forbids, and the darkness still comes from the paint being *this material's*
    paint rather than from the lighting being cheated.

    A dig is not ground. Ground is weathered, settled and level; a dig is a
    *worked* surface, cut this week, and the three things that say so are:

    * **Blocky clods, not stones.** Soil fails on planes, so turned earth breaks
      into flat-topped lumps separated by open fissures — where 자갈 is a pile of
      domes. The height field is built from the Worley cell interior (one flat
      level per clod) and its boundary (the fissure), which is the opposite way
      round from `build_gravel`, and it is why the two do not read alike even
      though both are loose material.
    * **Tool marks, and they are the specular story.** A spade in wet clay
      polishes what it cuts. So the gouges are the *smooth* population on an
      otherwise matte floor and the beam finds short bright slicks lying at every
      angle — a signature no other surface here produces, because every other
      floor's smooth places are flat and continuous (a plate, a glaze, a
      burnished slab) and these are short, curved and scattered.
    * **Buried timber.** Shoring boards laid on the mud to walk on, mostly
      trodden under. They give long straight lines like 나무's plank gaps, but
      *interrupted* — a board surfaces for 30 cm and goes back under — which is
      the difference between a floor made of boards and a floor with boards in it.

    And it is wet, because it is directly under 수몰층. Water stands in the
    deepest gouges rather than lying in sheets, which is what a working dig looks
    like and also what keeps this floor away from B7's on every axis at once.
    """
    world = 2.0

    # Spoil heaped over the cut floor: the slow, metre-scale undulation a dig has
    # and a laid floor never does. It goes in first because it is what the eye
    # reads from across a room, before any clod is resolvable.
    # `low_cycles` 3.2, not 2.6, and the difference is one measured number.
    # `BLOB_CYCLES_PER_TILE` is 2.0: anything slower than that is a feature the
    # size of the tile itself, so every copy of it lands on the lattice and the
    # eye locks onto the repeat instead of the material. At 2.6 the heap's skirt
    # reached into that band and this floor measured 17.0 % blob — the worst of
    # the eight after 금속's plate. At 3.2 the undulation is 62 cm across, which
    # is still a heap and no longer the tile.
    heap = warped_fbm(res, seed + 1, beta=2.3, strength=res * 0.05, low_cycles=3.2)

    height = np.zeros((res, res), dtype=np.float32)
    shade = np.full((res, res), 0.9, dtype=np.float32)
    fissure = np.zeros((res, res), dtype=np.float32)

    # Two clod sizes, 29 cm and 15 cm. A third and finer scale was built, rendered
    # and removed: three scales of Worley boundary is a *complete* polygon network
    # at every size at once, and a complete polygon network is dried mud — which
    # is precisely what `build_water`'s silt banks are, one storey up. Two floors
    # sharing a structure is the §12 collision this whole section exists to avoid,
    # and it is worse than the missing texture it would have replaced.
    for level, (cells, lift) in enumerate(((7, 0.00), (13, 0.16))):
        f1, f2, owner = worley(res, cells, seed + 40 + level * 41, jitter=1.0)

        # One wander, applied to every term drawn from this layer, so the clod and
        # the fissure that outlines it stay registered to each other. Unwarped,
        # Worley cells are dead-straight polygons and no amount of tinting hides it.
        wander_y = (fbm(res, seed + 40 + level * 41 + 5, beta=2.1, low_cycles=3.0) - 0.5) * res * 0.055
        wander_x = (fbm(res, seed + 40 + level * 41 + 7, beta=2.1, low_cycles=3.0) - 0.5) * res * 0.055
        boundary = warp(f2 - f1, wander_y, wander_x)

        # A plateau, not a dome, and this is the whole difference from 자갈. Stone
        # is rounded by transport and its surface is the top of a sphere; soil
        # fails on planes, so a clod is a flat-ish face with a steep broken skirt.
        # `build_gravel` takes a max over domes; this takes a max over tables.
        #
        # And the tables must NOT tessellate. `smoothstep(0, w, boundary)` fills
        # the whole Worley cell, so every clod shares an edge with its neighbours
        # and the floor comes out as a space-filling polygon tiling — which is
        # cobblestone, or a turtle's shell, and it was the last thing wrong with
        # this surface after three rounds. Shifting the zero crossing inward by
        # `0.12 / cells` shrinks each clod off its own cell boundary and opens a
        # real gap between it and the next one. The gaps are not empty: nothing
        # wins the `maximum` there, so they fill with `heap` and `grit` — the
        # loose fines that sit between lumps of turned earth, which is what a
        # tessellation has nowhere to put.
        plateau = smoothstep(0.0, 0.30 / cells, boundary - 0.12 / cells)
        top = warp(per_cell(owner, cells, seed + 40 + level * 41 + 11, 0.42, 1.0), wander_y, wander_x)

        candidate = (top * (1.0 - lift) + lift) * plateau
        winner = candidate > height
        shade = np.where(winner, warp(per_cell(owner, cells, seed + 40 + level * 41 + 13, 0.48, 1.46),
                                      wander_y, wander_x), shade)

        # Occasional, never a network. A fissure needs somewhere for the soil to
        # have moved to, and most clod joints are simply packed shut.
        opening = smoothstep(0.58, 0.86, fbm(res, seed + 40 + level * 41 + 17, beta=2.3, low_cycles=2.2))
        fissure = np.maximum(fissure, (1.0 - smoothstep(0.0, 0.10 / cells, boundary)) * opening)
        height = np.maximum(height, candidate)

    grit = fbm(res, seed + 3, beta=1.25, low_cycles=26.0)
    # Above `GRAIN_CYCLES_PER_METRE * world` = 50 cycles per tile, which is the
    # band ART.md §3.9 measures and the band a plateau-based height field has none
    # of by construction: flat tops carry no fine relief, so the crumb sitting on
    # them has to be authored rather than inherited. The first pass scored 7.8 %
    # grain — the lowest of the eight floors — for exactly that reason.
    crumb = fbm(res, seed + 11, beta=1.15, low_cycles=62.0)
    # Fissures pulled back from 0.26 to 0.16 now that the clods no longer
    # tessellate: the gaps between them are doing the work a drawn fissure was
    # standing in for, and two overlapping ways of saying "there is a hole here"
    # is what made the first three rounds read as a crack network.
    height = normalise01(height * 0.72 + heap * 0.34 - fissure * 0.16
                         + grit * 0.06 + crumb * 0.03)

    # Tool marks, and they have to be *big*. Drawn by `scratches`, which walks a
    # point across the torus so a mark that leaves one edge arrives at the other
    # instead of stopping like a scratch on a photograph. The first version drew
    # them at a centimetre wide and they were invisible at every distance: a spade
    # is 20 cm across and takes a scoop that size, so 4 cm of blur on an 11-stroke
    # pass is the honest figure, not a timid one.
    spade = smoothstep(0.46, 0.90, scratches(res, seed + 4, count=11, length=0.20, width=0.022))
    pick = smoothstep(0.62, 0.96, scratches(res, seed + 5, count=26, length=0.08, width=0.007))
    tool = np.clip(spade + pick * 0.65, 0.0, 1.0)

    # Shoring boards laid on the mud to walk on: 22 cm of board in a 67 cm band,
    # butted at a per-board stagger, and mostly trodden under. The exposure mask
    # is slow noise along the board's length, so a board surfaces for a third of
    # a metre and goes back under. That interruption is the point — uninterrupted
    # it is 나무's staggered planking, which is a different floor in this set.
    band = stripes(res, 3, axis=0)
    board_face = smoothstep(0.050, 0.075, band) * smoothstep(0.360, 0.335, band)
    butt = groove(stripes(res, 2, axis=1,
                          phase=cell_random(index_of(res, 3, axis=0), 3, seed + 6, 0.0, 1.0)), 0.020)
    surfaced = smoothstep(0.44, 0.70, warped_fbm(res, seed + 7, beta=2.3, strength=res * 0.055, low_cycles=2.4))
    timber = np.clip(board_face * surfaced - butt * 0.85, 0.0, 1.0)
    fibre = normalise01(ndimage.gaussian_filter1d(
        fbm(res, seed + 8, beta=1.25, low_cycles=14.0), sigma=res * 0.010, axis=0, mode="wrap"))

    damp = np.clip(
        stain(res, seed + 9, threshold=0.52, softness=0.10, detail=0.55, low_cycles=1.7) * 0.8
        + stain(res, seed + 10, threshold=0.72, softness=0.05, detail=0.60, low_cycles=4.0) * 0.5,
        0.0, 1.0)

    # A board is *flat*, and the height field has to be lerped toward its level
    # rather than added to. The first version added `timber * 0.20` on top of the
    # clods and got a ridge of mud shaped like a plank: every lump underneath came
    # straight through the board, so the one structure that was supposed to give
    # this floor a straight line did not read at any distance. A plank lying on
    # mud replaces the mud it lies on.
    board_level = 0.80 + fibre * 0.05
    height = np.clip(
        (height - tool * 0.22) * (1.0 - timber) + board_level * timber,
        0.0, 1.0,
    )

    # `crumb` carries a third of the paint variation on purpose. It is the only
    # term in this material above 25 cycles per metre — `grit` sits at 13 and the
    # clod plateaux are flat by construction — so it is the whole of what this
    # floor has to show a player standing on it. Measured as absolute RMS rather
    # than as a share: at 0.20 the albedo carried 0.0184 in that band, the lowest
    # of the eight floors and a fifth under 콘크리트, which was the previous worst.
    surface = shade * (0.88 + 0.22 * grit) * (0.86 + 0.30 * crumb)
    # Wetter than a dig above ground would be, and it has to be: this storey sits
    # directly under 수몰층. Dry, the clay reads as sand under a beam, which is a
    # desert and not the bottom of a flooded building.
    surface *= 1.0 - 0.40 * damp
    # Fissures lifted from 0.85 to 0.62. At 0.85 every clod was outlined in near
    # black and the floor read as crazed dried mud — which is a real surface, but
    # it is `build_water`'s silt bank one storey up, and it is not what a dig
    # face looks like. The gap between two clods is a shadow a centimetre deep,
    # not an ink line, and the AO map is what should be drawing it.
    colour = tint((0.176, 0.142, 0.108), surface, (0.058, 0.046, 0.036), np.clip(fissure * 0.62, 0.0, 1.0))

    # Dry fines on the clod tops — the pale crumb that sits on turned earth, and
    # the only thing on this floor brighter than the base. Kept off the timber:
    # a board that has been walked on is swept clean by feet.
    dust = smoothstep(0.64, 0.94, height) * (0.40 + 0.60 * grit) * (1.0 - timber)
    colour = tint(colour, np.ones((res, res), np.float32), (0.228, 0.202, 0.166), dust * 0.30)

    # A cut face is compacted and wet, so it is darker than the crumb beside it.
    # That is the read which makes the slicks legible as *cuts* rather than as a
    # gloss map somebody painted: the dark places are the shiny ones, which is
    # backwards for every other floor here and forwards for a spade in clay.
    colour = tint(colour, np.ones((res, res), np.float32), (0.112, 0.084, 0.058), tool * 0.60)
    colour = tint(colour, np.ones((res, res), np.float32), (0.104, 0.074, 0.046),
                  timber * (0.55 + 0.45 * fibre) * 0.88)

    roughness = np.clip(
        0.93 + 0.07 * fissure - 0.05 * dust
        - 0.40 * tool - 0.13 * damp - 0.18 * timber,
        0.30, 1.0,
    )

    colour = detile(colour, strength=0.72)

    # Water stands in the gouges, not in sheets. This is the storey directly
    # under 수몰층 and it is being dug in the wet, so the causality runs one way:
    # every pool here is in something a tool made. A ninth of the surface, for
    # the same reason `build_concrete_floor` uses a ninth — `wet` levels the
    # height where it pools, and a level any higher fills the tool marks flush
    # and erases the relief the AO is baked from.
    seepage = stain(res, seed + 33, threshold=0.48, softness=0.09, detail=0.55, low_cycles=1.8)
    water_level = water_line(height, 0.11)
    pool = standing_water(height, water_level, softness=0.035) * np.clip(seepage + tool * 0.5, 0.0, 1.0)
    film = np.clip(blur(np.maximum(pool, damp * seepage), res * 0.018) * 2.2 - pool, 0.0, 1.0) * 0.85

    colour, roughness, height = wet(
        colour, roughness, height, film, pool, water_level,
        darkening=0.58, film_roughness=0.38, pool_roughness=0.10)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.028)


# ── Walls ───────────────────────────────────────────────────────────────────


def build_wall_brick(res: int, seed: int) -> MapSet:
    """Painted brick. The default basement wall.

    Brick is chosen as the bound wall because its courses are horizontal lines at
    a fixed spacing: when a flashlight sweeps across it, the mortar lines give a
    continuous readout of distance and angle. A flat wall gives none, which is
    exactly why the baseline read as a void. The paint is old and failing, so
    patches of bare brick and plaster show through — that variation is what stops
    a 2 m tile from announcing itself.
    """
    world = 1.8
    courses = 24
    per_course = 8

    course_index = index_of(res, courses, axis=0)
    # Alternate courses shift half a brick — the stretcher bond every wall uses.
    stagger = (course_index % 2).astype(np.float32) * 0.5
    course_position = stripes(res, courses, axis=0)
    brick_position = stripes(res, per_course, axis=1, phase=stagger)
    brick_index = course_index * per_course + index_of(res, per_course, axis=1, phase=stagger)

    mortar = np.maximum(groove(course_position, 0.13, softness=0.55), groove(brick_position, 0.10, softness=0.55))

    # Widened from 0.78–1.22. At half a metre per brick — which is what this
    # texture was rendering at before KIT_UV_UNITS_PER_METRE was measured — a
    # narrow tone spread was survivable because the courses were too big to read
    # as brick anyway. At 21.5 cm it is not: a wall of identically-toned bricks
    # is the single clearest tell that a surface was generated, because a real
    # wall is built from several firings and half a century of weather.
    brick_tone = cell_random(brick_index, courses * per_course, seed + 1, 0.62, 1.34)
    brick_lean = cell_random(brick_index, courses * per_course, seed + 2, -1.0, 1.0)
    face = fbm(res, seed + 3, beta=1.6, low_cycles=12.0)
    pocks = smoothstep(0.72, 0.92, fbm(res, seed + 4, beta=1.2, low_cycles=30.0))

    # ── The relief the wall was missing, measured rather than felt. ──
    #
    # The first shipped set carried all of its normal energy in the mortar
    # grooves: a raking-light render of the normal map alone (flat grey albedo,
    # spot at 75° incidence) showed a lattice of joints around brick faces that
    # were *perfectly flat* — mean normal tilt 13.7° with the 95th percentile at
    # 67°, i.e. joints and nothing else. Under the game's oblique beam that
    # renders as wallpaper. Three things a hand-laid wall actually has:
    #
    # * **Every brick sits at its own depth.** A mason works to a string line,
    #   ±2 mm; half a century of settlement doubles that. Per-brick, not noise.
    # * **A fired brick face is bowed.** Clay shrinks in the kiln; stretchers
    #   crown 1–2 mm proud at the centre. The bow is what turns each brick into
    #   its own soft highlight under a moving light, which is the per-brick
    #   read the beam needs at 2 m.
    # * **Some faces have spalled.** Frost pops the fired skin off a weak brick
    #   and leaves a ragged crater a few millimetres deep with the soft core
    #   showing — rougher, redder, and the strongest single "old wall" cue.
    proud = cell_random(brick_index, courses * per_course, seed + 9, -1.0, 1.0)
    bow = (4.0 * course_position * (1.0 - course_position)) * \
        (4.0 * brick_position * (1.0 - brick_position))
    bow = np.sqrt(np.clip(bow, 0.0, 1.0)).astype(np.float32) * (0.55 + 0.45 * face) \
        * cell_random(brick_index, courses * per_course, seed + 12, 0.35, 1.0)

    spall_pick = smoothstep(0.80, 0.90,
                            cell_random(brick_index, courses * per_course, seed + 10, 0.0, 1.0))
    spall = spall_pick * smoothstep(0.38, 0.72, fbm(res, seed + 11, beta=1.7, low_cycles=9.0)) \
        * (1.0 - mortar)

    # Mortar is not one grey. It was repointed in patches, it carries the same
    # damp as the brick, and where it is old it is darker than the brick rather
    # than brighter — the bright uniform lattice the first render showed is what
    # made this wall read as a texture sample rather than as masonry.
    mortar_tone = np.clip(
        0.62
        + 0.55 * stain(res, seed + 41, threshold=0.55, softness=0.12, detail=0.45, low_cycles=2.4)
        + 0.22 * fbm(res, seed + 42, beta=1.5, low_cycles=16.0),
        0.28, 1.25)

    # Paint fails around damp and along the mortar, which holds water. Modulating
    # the mask by the brick structure itself is what keeps it from floating over
    # the wall as an unrelated shape — real paint failure follows the substrate.
    paint = stain(res, seed + 5, threshold=0.42, softness=0.09, detail=0.50, low_cycles=1.8)
    peel = stain(res, seed + 6, threshold=0.58, softness=0.04, detail=0.60, low_cycles=6.0)
    paint = np.clip(paint * (0.72 + 0.28 * (1.0 - mortar)) - peel * 0.75 - pocks * 0.4, 0.0, 1.0)

    damp = np.clip(
        stain(res, seed + 7, threshold=0.60, softness=0.08, detail=0.45, low_cycles=1.2) * 0.75
        + runs(stain(res, seed + 8, threshold=0.76, softness=0.03, detail=0.5, low_cycles=2.5),
               length=int(res * 0.14)) * 0.5,
        0.0, 1.0)

    # Weights re-derived for the deeper height_scale below: at 0.024 m full
    # range the mortar rakes ~14 mm deep, a brick leans ±2.4 mm off the string
    # line and a face crowns ~1.7 mm — all real dimensions, and between them
    # they put slope on every texel of the wall instead of only on the joints.
    height = np.clip(
        0.72
        + brick_tone * 0.04
        + proud * 0.13
        + bow * 0.11
        + face * 0.04
        - mortar * 0.58
        - pocks * 0.24
        - spall * 0.30
        + paint * 0.02,
        0.0, 1.0,
    )

    shade = brick_tone * (0.86 + 0.28 * face)
    brick_colour = tint((0.270, 0.150, 0.115), shade, (0.330, 0.205, 0.150), np.clip(brick_lean * 0.5 + 0.5, 0, 1) * 0.5)
    mortar_colour = (np.asarray([0.300, 0.292, 0.272], dtype=np.float32)[None, None, :]
                     * mortar_tone[..., None])

    colour = brick_colour * (1.0 - mortar[..., None] * 0.85) + mortar_colour * mortar[..., None] * 0.85
    colour = tint(colour, np.ones((res, res), np.float32), (0.330, 0.325, 0.290), paint * 0.80)
    # The spalled core: the soft inside of the brick, redder and unpainted.
    colour = tint(colour, np.ones((res, res), np.float32), (0.225, 0.108, 0.078), spall * 0.80)
    colour *= 1.0 - 0.25 * pocks[..., None]

    # Efflorescence: the salt bloom damp masonry pushes out to its own surface as
    # it dries. It is the most recognisable thing about a wet basement wall, it
    # is always *on* the damp rather than next to it, and it is nearly white —
    # the one bright feature in a room whose whole palette is dark, which makes
    # it worth its cost off-beam as well as under it.
    bloom = np.clip(
        stain(res, seed + 43, threshold=0.62, softness=0.05, detail=0.6, low_cycles=5.0)
        * damp * (0.55 + 0.45 * mortar), 0.0, 1.0)
    colour = tint(colour, np.ones((res, res), np.float32), (0.520, 0.512, 0.495), bloom * 0.70)

    # Soot and hand-height dirt, drawn as slow broad noise rather than as blobs
    # so it darkens without adding another shape to repeat.
    soot = blur(fbm(res, seed + 44, beta=2.4, low_cycles=2.2), res * 0.02)
    colour *= (1.0 - 0.22 * soot)[..., None]

    # ── Four roughness populations, because one is vinyl. ──
    # Base paint (mid), bare face and mortar (rough), soot/grease sheen where
    # hands and coats have burnished the paint (smooth — grime on paint is not
    # matte, it is greasy, and it is what a beam skates across at a grazing
    # angle), and the damp handled below by `wet`. The sheen mask is broad slow
    # noise so it adds no repeatable shape of its own.
    sheen = smoothstep(0.60, 0.82, warped_fbm(res, seed + 45, beta=2.3,
                                              strength=res * 0.04, low_cycles=1.6))
    roughness = np.clip(
        0.86 - 0.30 * paint + 0.10 * face + 0.06 * mortar + 0.14 * bloom
        + 0.12 * spall - 0.30 * sheen * (1.0 - mortar), 0.18, 1.0)

    colour = detile(colour, strength=0.80)

    # Damp masonry, by §3.8c's rule: genuinely darker AND smoother, in one
    # operator, after detile so the patch survives the flat-fielding. This wall
    # used to darken by 0.32 with a token −0.12 roughness — tinted, not wet.
    colour, roughness, height = wet(
        colour, roughness, height, np.clip(damp * 0.85, 0.0, 1.0),
        np.zeros_like(damp), 0.0, darkening=0.34, film_roughness=0.30)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.024)


def build_wall_concrete(res: int, seed: int) -> MapSet:
    """Bare poured concrete wall — board-formed, tie holes, streaked.

    The utilitarian counterpart to brick. Its identity is the horizontal board
    lines from the formwork and the regular grid of snap-tie holes, both of which
    are strong horizontal cues under a beam.
    """
    world = 2.5
    boards = 10

    board_position = stripes(res, boards, axis=0)
    board_index = index_of(res, boards, axis=0)
    board_seam = groove(board_position, 0.045, softness=0.6)
    # 0.90–1.10 → 0.82–1.18: under the beam the strongest per-board read is
    # tonal — each board of the shuttering sealed the pour differently, and a
    # narrow spread left the wall reading as one grey pour with lines on it.
    board_tone = cell_random(board_index, boards, seed + 1, 0.82, 1.18)

    # Snap-tie holes: a regular grid, plugged and slightly proud of the wall.
    tie_y = stripes(res, 4, axis=0)
    tie_x = stripes(res, 5, axis=1)
    tie = smoothstep(0.055, 0.020,
                     np.sqrt((tie_y - 0.5) ** 2 + (tie_x - 0.5) ** 2).astype(np.float32))

    base = warped_fbm(res, seed + 2, beta=2.2, strength=res * 0.04)
    fine = fbm(res, seed + 3, beta=1.5, low_cycles=20.0)

    # ── What board-formed actually means, which this wall did not have. ──
    #
    # The first shipped set was a plane with lines on it: mean normal tilt 3.7°,
    # every texel between the seams dead flat. In the B1 shots that renders as
    # a featureless void the beam slides over, because a seam every 25 cm is
    # all the raking light had to find. Three things real shuttering leaves:
    #
    # * **Boards are not coplanar.** Each one deflects under the pour and is
    #   re-used at a slightly different packing, so adjacent boards step 1–3 mm
    #   at every seam. The step is the strongest oblique-light feature a formed
    #   wall has — a continuous shading change across the whole board, not a
    #   line at its edge.
    # * **The timber's grain prints.** Concrete is a cast: every fibre of the
    #   board face comes out in negative. Warped per board so the print breaks
    #   at each seam the way different boards do.
    # * **Honeycombing.** Where the pour was badly vibrated the fines never
    #   reached the form and the wall face is a patch of exposed aggregate
    #   voids — deeper, darker, and abruptly rough against the cast face.
    board_offset = cell_random(board_index, boards, seed + 11, -1.0, 1.0)
    form_grain = ndimage.gaussian_filter1d(
        fbm(res, seed + 12, beta=1.35, low_cycles=6.0), sigma=res * 0.006,
        axis=1, mode="wrap").astype(np.float32)
    form_grain = normalise01(warp(
        form_grain,
        cell_random(board_index, boards, seed + 13, 0.0, float(res)),
        (fbm(res, seed + 14, beta=2.4) - 0.5) * res * 0.02))

    # A step between two *flat* boards is one texel of edge and no shading —
    # measured in the round-2 raking render, where the offsets were invisible.
    # What makes a formed wall shade board by board is *deflection*: each board
    # bowed into the pour, so the cast face crowns out between the seams and
    # every face carries a continuous normal gradient. Per-board amount,
    # because a board that has been re-used ten times bows more than a new one.
    board_bow = (4.0 * board_position * (1.0 - board_position)).astype(np.float32) \
        * cell_random(board_index, boards, seed + 18, 0.25, 1.0)

    honey = smoothstep(0.76, 0.90, warped_fbm(res, seed + 15, beta=2.3,
                                              strength=res * 0.03, low_cycles=1.8)) \
        * smoothstep(0.40, 0.75, fbm(res, seed + 16, beta=1.2, low_cycles=34.0))

    # Rust bleed from the ties and lime leaching from the board joints both run
    # downward, so the streaking is sourced from those features and dragged.
    bleed = np.clip(
        runs(np.clip(tie * 0.9 + board_seam * 0.35, 0.0, 1.0) *
             stain(res, seed + 4, threshold=0.44, softness=0.10, detail=0.6, low_cycles=2.0),
             length=int(res * 0.20), falloff=1.4) * 0.9
        + stain(res, seed + 6, threshold=0.66, softness=0.06, detail=0.5, low_cycles=1.5) * 0.45,
        0.0, 1.0)
    pits = smoothstep(0.82, 0.95, fbm(res, seed + 5, beta=1.3, low_cycles=28.0))

    # At height_scale 0.024 a board steps ±1.9 mm against its neighbour, the
    # grain prints ~1.2 mm and a honeycombed patch sinks ~7 mm. The seams and
    # tie holes keep their absolute depth (weights halved as the scale doubled).
    height = np.clip(
        0.78
        + base * 0.05
        + fine * 0.035
        + board_offset * 0.08
        + board_bow * 0.15
        + form_grain * 0.08
        - board_seam * 0.30
        - tie * 0.22
        - pits * 0.16
        - honey * 0.28,
        0.0, 1.0)

    shade = board_tone * (0.88 + 0.24 * base) * (0.94 + 0.12 * fine) * (0.92 + 0.16 * form_grain)
    shade *= 1.0 - 0.12 * bleed
    colour = tint((0.335, 0.330, 0.318), shade, (0.100, 0.098, 0.095), np.clip(board_seam * 0.7 + pits * 0.6, 0, 1))
    colour = tint(colour, np.ones((res, res), np.float32), (0.215, 0.200, 0.175), tie * 0.55)
    colour = tint(colour, np.ones((res, res), np.float32), (0.148, 0.138, 0.124), honey * 0.65)

    # Cast concrete takes the form's finish: each board face has its own sheen
    # (ply casts near-gloss, sawn timber matte), the honeycombing is abruptly
    # rough, and the damp streaks below go through `wet`. The old wall ran
    # 0.757–0.863 roughness across 90 % of its texels — one material, one
    # answer to the beam, which is the definition of reading as vinyl.
    board_sheen = cell_random(board_index, boards, seed + 17, -1.0, 1.0)
    roughness = np.clip(
        0.62
        + board_sheen * 0.11
        + 0.10 * fine
        + 0.06 * form_grain
        + 0.26 * honey
        + 0.12 * pits
        + 0.10 * board_seam,
        0.28, 1.0)

    colour = detile(colour, strength=0.75)

    # The tie and seam streaks are water: darker and smoother where they run,
    # per §3.8c, not just a grey tint in the shade term.
    colour, roughness, height = wet(
        colour, roughness, height, np.clip(bleed * 0.9, 0.0, 1.0),
        np.zeros_like(bleed), 0.0, darkening=0.30, film_roughness=0.34)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.024)


def build_wall_plaster(res: int, seed: int) -> MapSet:
    """Water-stained plaster — the brightest wall in the set, and the damp band.

    Kept pale on purpose: §03's flashlight needs at least one surface class that
    throws light back generously, or every room reads at the same brightness.
    Plaster is that surface. The staining is what keeps it from looking clean —
    tide marks where water ran and dried, and blown patches where it has fallen
    away to the substrate.

    REGISTERED TO THE FLOOR, WHICH IS WHY IT TILES ONE WAY ONLY
    -----------------------------------------------------------
    This material is bound to `Wall_Dado`, and `gen_mapkit_detail.dressed_wall`
    calls that band by its real name: the *damp / tanking band*, `SKIRT_H` (0.20 m)
    up to `DADO_TOP` (1.02 m). The kit's box projection puts V = world Z with the
    piece origin on its own floor, so at a 2 m tile that band lands on
    V = 0.10 … 0.51 — the same slice of this texture, on every wall, on all three
    storeys.

    So the damp can be drawn where damp actually is. Rising damp is not a stain
    that happens to be low down; it is a front that climbs out of the ground to
    the height the wall can wick to and stops in a hard tide line, with salt
    blooming below it and nothing above. Drawn as a `stain` mask floating
    anywhere on the wall — which is what this did — it reads as an airbrushed
    smudge. Drawn against the floor it reads as a building with a water problem,
    and it puts grime along the wall/floor junction of every corridor in the map
    without a single decal.

    The price is that a vertical gradient cannot wrap, so `tiling_axes` declares
    U only. That is honest rather than a loosened threshold: the band is 0.82 m
    of a 2 m tile and never repeats within itself.
    """
    world = 2.0

    trowel = blur(fbm(res, seed + 1, beta=2.2, low_cycles=3.0), res * 0.004)
    swirl = warp(trowel, (fbm(res, seed + 2, beta=2.2) - 0.5) * res * 0.05,
                 (fbm(res, seed + 3, beta=2.2) - 0.5) * res * 0.05)
    fine = fbm(res, seed + 4, beta=1.4, low_cycles=22.0)

    # Water damage in three layers, because one mask reads as a cloud. A broad
    # damp area, a hard tide line at its edge where the water stopped and dried
    # its dissolved load, and vertical runs above it. All ragged-edged.
    damp = stain(res, seed + 5, threshold=0.56, softness=0.10, detail=0.45, low_cycles=1.2)
    tide_edge = stain(res, seed + 5, threshold=0.52, softness=0.02, detail=0.45, low_cycles=1.2) - \
        stain(res, seed + 5, threshold=0.58, softness=0.03, detail=0.45, low_cycles=1.2)
    trickle = runs(stain(res, seed + 21, threshold=0.74, softness=0.03, detail=0.5, low_cycles=2.5),
                   length=int(res * 0.16)) * 0.55

    # Weighted down from what looks right in an image viewer. Plaster is the
    # brightest surface in the building and its value range is doing structural
    # work — heavy blotching turns it into camouflage and costs the room the one
    # wall that reliably throws light back.
    marks = np.clip(damp * 0.40 + np.clip(tide_edge, 0.0, 1.0) * 0.60 + trickle * 0.8, 0.0, 1.0)

    # ── Rising damp, keyed to the floor line. See the docstring. ──
    #
    # The front is not level: masonry wicks further where the mortar is worse, so
    # the height of the tide line is modulated by slow noise along the wall. A
    # dead-straight line reads as a painted dado.
    above_floor = upward(res)
    front = 0.40 + 0.13 * (fbm(res, seed + 31, beta=2.4, low_cycles=2.5) - 0.5) * 2.0
    rise = smoothstep(0.10, -0.06, above_floor - front)

    # The tide line itself: where the water stopped and left everything it was
    # carrying. It is the single most recognisable feature of a damp wall.
    tide = np.exp(-((above_floor - front) / 0.028) ** 2) * (0.55 + 0.45 * damp)

    # Salt bloom below the front, crusty and near-white, strongest in the middle
    # of the band rather than at the very bottom where the skirting covers it.
    # low_cycles 14, not 4.5: at 4.5 over a 2 m tile the blooms came out 45 cm
    # across and read as mould rather than as salt. Efflorescence is a crust of
    # centimetre-scale crystals; the size is most of what identifies it.
    salt = np.clip(
        stain(res, seed + 32, threshold=0.66, softness=0.05, detail=0.65, low_cycles=14.0)
        * rise * smoothstep(0.04, 0.16, above_floor) * 1.3, 0.0, 1.0)

    # Grime along the wall/floor junction: dust, mop water and boot dirt, which
    # accumulate in the corner and nowhere else. This is the contact shading a
    # decal would otherwise have to provide.
    junction = smoothstep(0.16, 0.03, above_floor) * (0.55 + 0.45 * warped_fbm(
        res, seed + 33, beta=2.2, strength=res * 0.02, low_cycles=3.0))

    marks = np.clip(marks + rise * 0.55 + tide * 0.9, 0.0, 1.0)

    blown = stain(res, seed + 6, threshold=0.70, softness=0.05, detail=0.55, low_cycles=4.0)

    # 9 and 17 cells over a 2 m tile drew 22 cm and 12 cm polygons, which is
    # crazy paving rather than crazing. Real hairline cracking in a lime render
    # closes at two to ten centimetres, and at 22 cm the pattern reads as a
    # decorative motif — it was the loudest generated-looking thing left on the
    # wall once the brick came back to size.
    # Masked harder than it was (0.45–0.72 → 0.58–0.82): the raking-light test
    # showed the crazing running edge to edge as an even net, which reads as a
    # decorative motif — the same failure the concrete floor's crack network
    # had. Real crazing lives in patches where the render was over-troweled.
    hairline = np.zeros((res, res), dtype=np.float32)
    for cells, width, offset in ((20, 0.0022, 61), (38, 0.0014, 71)):
        f1, f2, _ = worley(res, cells, seed + offset, jitter=0.95)
        vein = 1.0 - smoothstep(0.0, width, f2 - f1)
        hairline = np.maximum(hairline, vein * smoothstep(0.58, 0.82, fbm(res, seed + offset + 3, beta=2.2, low_cycles=2.0)))

    mould = stain(res, seed + 7, threshold=0.76, softness=0.04, detail=0.5, low_cycles=3.0) * marks

    # The blown patches concentrate in the damp band, because that is what damp
    # does to plaster — it pushes it off the wall. Multiplying an existing mask
    # by the front rather than adding a second one keeps the count of independent
    # shapes down, and every shape is one more thing that can repeat visibly.
    blown = np.clip(blown * (0.45 + 0.85 * rise), 0.0, 1.0)

    # Swirl doubled and the scale below doubled with it: the trowel's broad
    # undulation is what a raking beam actually shades on a plaster wall — the
    # old 0.5 mm of it at scale 0.008 measured a mean normal tilt of 6.2°, and
    # the wall answered oblique light with nothing between its blown patches.
    # A blown patch is a step, not a dish: plaster leaves the wall as a plate,
    # so its edge is hardened before it cuts.
    blown_step = smoothstep(0.25, 0.60, blown)
    height = np.clip(0.86 + swirl * 0.12 + fine * 0.02 - blown_step * 0.52 - hairline * 0.34
                     + salt * 0.05 - junction * 0.03, 0.0, 1.0)

    shade = (0.86 + 0.28 * swirl) * (0.95 + 0.10 * fine)
    colour = tint((0.430, 0.418, 0.388), shade, (0.250, 0.212, 0.158), marks * 0.60)
    colour = tint(colour, np.ones((res, res), np.float32), (0.135, 0.140, 0.115), mould * 0.55)
    colour = tint(colour, np.ones((res, res), np.float32), (0.290, 0.268, 0.238), blown * 0.70)
    colour = tint(colour, np.ones((res, res), np.float32), (0.545, 0.535, 0.512), salt * 0.65)
    colour *= 1.0 - 0.30 * hairline[..., None]
    colour *= 1.0 - 0.52 * junction[..., None]

    # Floor raised 0.30 → 0.40. Plaster has no gloss at any age, and this is the
    # palest surface in the game standing where §03's beam lands on it from one
    # metre: at 0.30 the specular lobe put 0.93 % of the spawn3 frame over 250,
    # against ART.md's 0.5 % ceiling, and a clipped highlight throws away the
    # texture exactly where the player is looking.
    roughness = np.clip(
        0.80 + 0.10 * fine - 0.10 * marks + 0.12 * blown + 0.14 * salt + 0.10 * junction, 0.40, 1.0)

    # A damp wall is damp: below the front the plaster is wet enough to darken
    # and to take a sheen at a grazing angle, which is how a player reads "this
    # room floods" from the wall rather than from the floor.
    # U only: the vertical structure above is registered to the floor and must
    # not be flattened away, so detile runs across the wall and not up it. And
    # before the damp, so the damp survives it.
    colour = detile(colour, strength=0.55, cycles=2.6, axes=(1,))

    # film_roughness 0.52 → 0.36. At 0.52 against a 0.80 base the "sheen" was a
    # rumour; §3.8c's read is the beam turning to a streak on the damp band at
    # a grazing angle, and that needs the film to be *smooth*, not less rough.
    film = np.clip(rise * (0.35 + 0.65 * damp) - salt * 0.6, 0.0, 1.0)
    colour, roughness, _ = wet(
        colour, roughness, height, film, np.zeros_like(film), 0.0,
        darkening=0.34, film_roughness=0.36)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.015)


# ── Ceiling ─────────────────────────────────────────────────────────────────


def build_ceiling_concrete(res: int, seed: int) -> MapSet:
    """Concrete soffit with form marks — the plank impressions the shuttering left.

    A ceiling is mostly seen at a grazing angle in flashlight spill, so its whole
    job is to have *direction*. Board impressions running one way give the eye
    something to follow down a corridor. Kept a little darker than the walls
    because it is the surface furthest from the beam and closest to the shadow
    the room needs.
    """
    world = 2.5
    boards = 12

    board_position = stripes(res, boards, axis=0)
    board_index = index_of(res, boards, axis=0)
    board_seam = groove(board_position, 0.055, softness=0.5)
    board_tone = cell_random(board_index, boards, seed + 1, 0.84, 1.16)

    # Grain of the shuttering timber, printed into the concrete. Broken per
    # board (each plank of the shuttering is a different piece of timber) and
    # drawn out along the board, for the same reason as `build_wall_concrete`.
    grain = ndimage.gaussian_filter1d(
        fbm(res, seed + 2, beta=1.3, low_cycles=6.0), sigma=res * 0.006,
        axis=1, mode="wrap").astype(np.float32)
    grain = normalise01(warp(
        grain,
        cell_random(board_index, boards, seed + 9, 0.0, float(res)),
        (fbm(res, seed + 3, beta=2.4) - 0.5) * res * 0.015))

    # Shuttering boards deflect and re-seat between pours exactly as the wall's
    # do; a soffit at 3 m still shows the steps because grazing spill is the
    # only light it ever gets — the *more* oblique the light, the more a step
    # reads. Held to ±1.4 mm against the wall's ±1.9: nothing is ever near it.
    board_offset = cell_random(board_index, boards, seed + 10, -1.0, 1.0)

    base = warped_fbm(res, seed + 4, beta=2.2, strength=res * 0.04)

    # Water tracks along a board joint before it drips, so seepage is seeded on
    # the joints. Soot is the opposite — it settles evenly and only collects in
    # the low corners — so it stays a broad ragged field.
    seep = np.clip(
        board_seam * stain(res, seed + 5, threshold=0.42, softness=0.10, detail=0.6, low_cycles=2.0) * 0.9
        + stain(res, seed + 8, threshold=0.70, softness=0.05, detail=0.55, low_cycles=2.5) * 0.55,
        0.0, 1.0)
    soot = stain(res, seed + 6, threshold=0.60, softness=0.10, detail=0.5, low_cycles=1.5)
    pits = smoothstep(0.80, 0.94, fbm(res, seed + 7, beta=1.3, low_cycles=24.0))

    # Deflection bow across each board, same physics and same round-2 lesson
    # as `build_wall_concrete`: the offset step alone rendered as a one-texel
    # line and the soffit stayed a sheet of lined paper under grazing spill.
    board_bow = (4.0 * board_position * (1.0 - board_position)).astype(np.float32) \
        * cell_random(board_index, boards, seed + 12, 0.25, 1.0)

    height = np.clip(0.80 + base * 0.04 + grain * 0.08 + board_offset * 0.07
                     + board_bow * 0.14 - board_seam * 0.36 - pits * 0.16, 0.0, 1.0)

    shade = board_tone * (0.88 + 0.22 * base) * (0.90 + 0.20 * grain)
    shade *= 1.0 - 0.30 * soot
    colour = tint((0.300, 0.296, 0.288), shade, (0.090, 0.088, 0.086), np.clip(board_seam * 0.8 + pits * 0.5, 0, 1))
    colour = tint(colour, np.ones((res, res), np.float32), (0.185, 0.165, 0.130), seep * 0.55)

    # Per-board sheen for the same reason as the wall: the form's finish is the
    # concrete's finish, and a soffit of one roughness answers the beam's spill
    # as one grey card. Soot is dead matte; the seep tracks go through `wet`.
    board_sheen = cell_random(board_index, boards, seed + 11, -1.0, 1.0)
    roughness = np.clip(
        0.72 + board_sheen * 0.10 + 0.08 * grain + 0.10 * soot + 0.10 * pits, 0.30, 1.0)

    colour = detile(colour, strength=0.75)

    colour, roughness, height = wet(
        colour, roughness, height, np.clip(seep * 0.8, 0.0, 1.0),
        np.zeros_like(seep), 0.0, darkening=0.28, film_roughness=0.34)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.020)


# ── Trim ────────────────────────────────────────────────────────────────────


def build_trim_steel(res: int, seed: int) -> MapSet:
    """Rusted steel trim — the kit's `Trim_Steel` slot.

    Trim runs along corridor edges, so it is almost always *inside* the beam
    while the wall behind it is not. Making it rusted and rough rather than clean
    steel means it reads as a band of warm colour rather than a blown-out
    highlight line.
    """
    world = 1.0

    ribs = stripes(res, 6, axis=0)
    rib = smoothstep(0.44, 0.28, np.abs(ribs - 0.5))
    edge = groove(ribs, 0.10, softness=0.6)

    pit_distance, _, pit_owner = worley(res, 40, seed + 1, jitter=1.0)
    pitting = smoothstep(0.014, 0.004, pit_distance) * smoothstep(0.55, 0.80, per_cell(pit_owner, 40, seed + 2, 0.0, 1.0))

    rust = np.clip(
        stain(res, seed + 3, threshold=0.48, softness=0.07, detail=0.6, low_cycles=5.0) + pitting * 0.8,
        0.0, 1.0)
    flake = smoothstep(0.62, 0.84, fbm(res, seed + 4, beta=1.6, low_cycles=14.0)) * rust
    brushed = fbm(res, seed + 5, beta=1.1, low_cycles=45.0)
    scuff = scratches(res, seed + 6, count=45, length=0.28, width=0.0012)

    height = np.clip(0.78 + rib * 0.12 - edge * 0.45 - pitting * 0.35 + rust * 0.06 + brushed * 0.02, 0.0, 1.0)

    shade = (0.88 + 0.24 * brushed) * (1.0 - 0.18 * scuff)
    colour = tint((0.310, 0.318, 0.330), shade, (0.255, 0.118, 0.052), rust * 0.92)
    colour = tint(colour, np.ones((res, res), np.float32), (0.160, 0.082, 0.045), flake * 0.60)
    colour *= 1.0 - 0.22 * edge[..., None]

    roughness = np.clip(0.34 + 0.55 * rust + 0.20 * pitting + 0.14 * scuff, 0.20, 1.0)

    # Held well below 1.0. A fully metallic surface has no diffuse term at all,
    # so with this scene's near-black ambient it renders as a black cutout with a
    # highlight sitting in it — the trim disappears the instant the beam moves off
    # it, which is the opposite of what trim is for.
    metallic = np.clip(0.45 * (1.0 - rust), 0.0, 1.0)

    return MapSet(colour, roughness, height, metallic, world, 0.010)


def build_trim_door(res: int, seed: int) -> MapSet:
    """Painted door leaf and frame — the kit's `Door_Leaf` slot.

    §04's Mechanic locks doors at the chokepoints §12 puts them in, so a door is
    a thing players *look for*. It is the palest surface in the set so that a
    beam finds it first, with the paint chipped back to primer at the edges where
    hands and trolleys hit it.
    """
    world = 1.2

    panel_y = stripes(res, 2, axis=0)
    panel_x = stripes(res, 1, axis=1)
    inset = np.maximum(
        smoothstep(0.16, 0.24, np.abs(panel_y - 0.5) * 2.0),
        smoothstep(0.55, 0.72, np.abs(panel_x - 0.5) * 2.0),
    )
    rail = groove(panel_y, 0.10, softness=0.6)

    brush = blur(fbm(res, seed + 1, beta=1.9, low_cycles=6.0), res * 0.004)
    brush = warp(brush, (fbm(res, seed + 2, beta=2.4) - 0.5) * res * 0.01, np.zeros((res, res)))

    chip = stain(res, seed + 3, threshold=0.62, softness=0.04, detail=0.6, low_cycles=8.0)
    chip = np.clip(chip * (0.30 + 0.70 * np.maximum(rail, 1.0 - inset)), 0.0, 1.0)
    grime = np.clip(
        stain(res, seed + 4, threshold=0.54, softness=0.09, detail=0.5, low_cycles=2.0) * 0.7
        + runs(stain(res, seed + 7, threshold=0.76, softness=0.03, detail=0.6, low_cycles=4.0),
               length=int(res * 0.12)) * 0.5,
        0.0, 1.0)
    scuff = scratches(res, seed + 5, count=30, length=0.20, width=0.0014)

    height = np.clip(0.84 + inset * 0.08 - rail * 0.40 + brush * 0.03 - chip * 0.25, 0.0, 1.0)

    shade = (0.90 + 0.20 * brush) * (1.0 - 0.14 * scuff)
    colour = tint((0.352, 0.340, 0.300), shade, (0.230, 0.216, 0.188), np.clip(grime * 0.55 + rail * 0.4, 0, 1))
    colour = tint(colour, np.ones((res, res), np.float32), (0.245, 0.140, 0.092), chip * 0.75)

    roughness = np.clip(0.42 + 0.34 * chip + 0.16 * grime + 0.10 * scuff, 0.14, 1.0)

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.009)


def build_trim_skirting(res: int, seed: int) -> MapSet:
    """Painted timber skirting — the base board where wall meets floor.

    Bound to `Trim_Painted`, which `gen_mapkit_detail.dressed_wall` puts on 13 of
    the kit's pieces: the skirting itself, world Z 0 → `SKIRT_H` = 0.20 m, and
    the dado rail at 1.02 → 1.11 m. It is therefore the surface that draws the
    wall/floor junction in almost every corridor in the game, which is why the
    dirt line at its foot is worth more than anything else in this file per texel
    spent — objects and rooms read as *placed* until something darkens where they
    meet, and this is the one contact shadow a tiling texture is allowed to draw.

    TWO THINGS WERE WRONG HERE AND BOTH WERE INVISIBLE IN AN IMAGE VIEWER
    ---------------------------------------------------------------------
    The tile was 1 m and the board is 0.20 m, so only the lowest fifth of the
    authored profile ever reached a wall — the bead, the body and the top shadow
    were drawn and never rendered. And the profile ramp ran the wrong way (see
    `upward`), so the mop damage, the scuffing and the "shadow where the board
    meets the floor" were all sitting at the top edge. The board rendered as a
    flat painted stripe, which is exactly what it looked like.

    Now the tile is the board: `world` is `SKIRT_H` plus the millimetre of margin
    that keeps the top shadow off the wrap, and every feature is placed by its
    real height above the floor.
    """
    world = 0.22

    # Height above the floor within the tile, in metres — so the numbers below
    # are the dimensions of an actual skirting board rather than fractions.
    metres = upward(res) * world
    bead = (smoothstep(0.020, 0.008, np.abs(metres - 0.155))
            + smoothstep(0.014, 0.004, np.abs(metres - 0.130)))
    body = smoothstep(0.004, 0.020, metres) * smoothstep(0.205, 0.185, metres)

    # A skirting is nailed over finished wall and never sits flush, so there is a
    # dark gap where it meets the plaster and another where it meets the floor.
    # Without them the profile is all smooth ramps, the AO map has nothing to
    # occlude, and the board reads as a painted stripe rather than as a thing
    # standing proud of the wall.
    shadow_top = smoothstep(0.008, 0.001, np.abs(metres - 0.196))
    shadow_floor = smoothstep(0.010, 0.001, np.abs(metres - 0.004))
    shadow = np.clip(shadow_top + shadow_floor, 0.0, 1.0)

    grain = blur(fbm(res, seed + 1, beta=1.3, low_cycles=4.0), res * 0.010)
    grain = warp(grain, np.zeros((res, res)), (fbm(res, seed + 2, beta=2.4) - 0.5) * res * 0.02)

    # Skirting takes damage at the bottom, from mops, boots and damp.
    low = smoothstep(0.090, 0.012, metres)
    chip = smoothstep(0.66, 0.85, fbm(res, seed + 3, beta=1.5, low_cycles=16.0)) * (0.3 + 0.7 * low)
    scuffing = np.clip(low * smoothstep(0.35, 0.75, fbm(res, seed + 4, beta=2.0, low_cycles=3.0)), 0.0, 1.0)
    dust = warped_fbm(res, seed + 5, beta=2.4, strength=res * 0.04, low_cycles=1.5)

    # The dirt line. Everything a floor collects ends up in the 2 cm against the
    # skirting, because that is the one strip a mop cannot reach and a boot
    # pushes into. It is nearly black, it is ragged, and it is the single detail
    # that makes a wall and a floor look like they have been in the same room for
    # forty years rather than having been placed in the same scene.
    junction = smoothstep(0.030, 0.002, metres) * (0.5 + 0.5 * warped_fbm(
        res, seed + 6, beta=2.1, strength=res * 0.02, low_cycles=4.0))
    junction = np.clip(junction * 1.2, 0.0, 1.0)

    height = np.clip(0.70 + body * 0.14 + bead * 0.14 + grain * 0.03 - chip * 0.25
                     - shadow * 0.55, 0.0, 1.0)

    shade = (0.90 + 0.20 * grain)
    colour = tint((0.340, 0.330, 0.298), shade, (0.215, 0.196, 0.168), np.clip(scuffing * 0.7 + dust * 0.3, 0, 1))
    colour = tint(colour, np.ones((res, res), np.float32), (0.230, 0.148, 0.090), chip * 0.70)
    colour = tint(colour, np.ones((res, res), np.float32), (0.055, 0.052, 0.048), junction * 0.85)
    colour *= 1.0 - 0.45 * shadow[..., None]

    roughness = np.clip(
        0.48 + 0.30 * chip + 0.14 * scuffing + 0.08 * grain + 0.15 * shadow + 0.22 * junction, 0.16, 1.0)

    # Along the board only: the profile above is the material's whole point.
    colour = detile(colour, strength=0.55, cycles=2.6, axes=(1,))

    return MapSet(colour, roughness, height, np.zeros((res, res), np.float32), world, 0.012)


# ── The set ─────────────────────────────────────────────────────────────────
#
# `slots` are MapKit material slot names read out of
# `Assets/Models/MapKit/MapKit.manifest.json`. The Unity side binds by these, so
# the contractual Floor_* names survive: §04's Listener reads the surface off the
# collider's physics material and the footstep audio matches on the same string.


# ── CC0 photo bases ─────────────────────────────────────────────────────────
#
# The loader, the normal→height integrator, and the one generic builder that
# lays a scan under a material's procedural detail. See `PhotoSpec` for why.


def cc0_root() -> str:
    """Where the vendored CC0 scans live. Override with $HORROR_TEXTURE_CC0.

    Defaults to `tools/textures/cc0`, beside this generator, so a clean checkout
    reproduces the shipped set with no external download — the same contract the
    procedural half already keeps ("the generators are the source of truth").
    """
    env = os.environ.get("HORROR_TEXTURE_CC0")
    if env:
        return env
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), "cc0")


def _load_photo_channel(path: str, res: int, gray: bool, tiles: int,
                        offset: Tuple[float, float]) -> np.ndarray:
    """One map of a scan, resized (and optionally tiled) to `res`, in [0, 1].

    Pillow is imported here rather than at module top so the procedural-only path
    keeps the header's numpy+scipy-only promise: a checkout that never touches a
    PhotoSpec never imports it.
    """
    from PIL import Image

    image = Image.open(path).convert("L" if gray else "RGB")
    if tiles > 1:
        step = max(1, res // tiles)
        cell = np.asarray(image.resize((step, step), Image.LANCZOS), np.float32) / 255.0
        reps = -(-res // step)
        pattern = (reps, reps) if gray else (reps, reps, 1)
        field = np.tile(cell, pattern)[:res, :res]
    else:
        field = np.asarray(image.resize((res, res), Image.LANCZOS), np.float32) / 255.0

    if offset != (0.0, 0.0):
        shift = (int(round(offset[0] * res)), int(round(offset[1] * res)))
        field = np.roll(field, shift, axis=(0, 1))
    return field


def load_photo_maps(photo: "PhotoSpec", res: int) -> Dict[str, np.ndarray]:
    """Loads a scan's albedo (sRGB), tangent normal (unit), roughness, AO and
    optional metalness, each tiled to the material's resolution.

    A missing directory raises, and `generate` catches it and falls back to the
    procedural `build`, so the generator never hard-fails on a checkout without
    the scans — it just ships the older look and says so.
    """
    folder = os.path.join(cc0_root(), photo.set_dir)
    if not os.path.isdir(folder):
        raise FileNotFoundError(folder)

    tiles, off = photo.tiles, photo.offset
    maps: Dict[str, np.ndarray] = {
        "albedo": _load_photo_channel(os.path.join(folder, "albedo.jpg"), res, False, tiles, off),
        "rough": _load_photo_channel(os.path.join(folder, "rough.jpg"), res, True, tiles, off),
        "ao": _load_photo_channel(os.path.join(folder, "ao.jpg"), res, True, tiles, off),
    }
    normal = _load_photo_channel(os.path.join(folder, "normal.png"), res, False, tiles, off) * 2.0 - 1.0
    normal[..., 2] = np.abs(normal[..., 2])
    normal /= np.clip(np.linalg.norm(normal, axis=-1, keepdims=True), 1e-6, None)
    maps["normal"] = normal.astype(np.float32)

    if photo.has_metal:
        maps["metal"] = _load_photo_channel(os.path.join(folder, "metal.jpg"), res, True, tiles, off)
    return maps


def integrate_normal_to_height(normal: np.ndarray) -> np.ndarray:
    """Periodic Poisson integration of a tangent normal into a [0, 1] height.

    Shipping the scan's normal *raw* would leave the game with a normal the AO
    and the height disagree with, and the two are measured together (`verify`'s
    AO-contrast gate compares occlusion against height). Integrating instead
    gives one height the whole pipeline agrees on: `height_to_normal` reproduces
    the scan's normal from it to within a cosine of 0.99, `ambient_occlusion`
    darkens exactly its hollows, and `apply_grain` adds §3.9 grain to the same
    field — so the shipped normal is the scan's relief *combined with* the
    procedural micro-grain, which is what the task asked for.

    The frequency kernel is the transfer of the *same* central difference
    `height_to_normal` uses (i·sin ω, not i·ω), so re-differencing the integral
    returns the input rather than a smoothed cousin of it. FFT-based, hence
    exactly periodic — the scan tiles, so its integral tiles, with no seam.
    """
    nz = np.clip(np.abs(normal[..., 2]), 1e-3, None)
    gx = (-normal[..., 0] / nz).astype(np.float64)   # dz/dx, arbitrary units per texel
    gy = (-normal[..., 1] / nz).astype(np.float64)   # dz/dy
    res = normal.shape[0]
    k = np.fft.fftfreq(res) * res
    s = np.sin(2.0 * np.pi * k / res)
    sx, sy = s[None, :], s[:, None]
    denom = sx ** 2 + sy ** 2
    denom[0, 0] = 1.0
    num = (-1j * sx) * np.fft.fft2(gx) + (-1j * sy) * np.fft.fft2(gy)
    height = np.real(np.fft.ifft2(num / denom)).astype(np.float32)

    # Robust range: a single spike must not flatten the wall by owning [0,1].
    low, high = np.percentile(height, [0.3, 99.7])
    if high - low < 1e-9:
        return np.full_like(height, 0.5)
    return np.clip((height - low) / (high - low), 0.0, 1.0).astype(np.float32)


def build_photo_surface(spec: "MaterialSpec", res: int, seed: int) -> MapSet:
    """A CC0 scan as the base layer, this material's procedural detail on top.

    Order matters and follows the physics: the scan is the surface, grime is the
    room's dirt over it, `detile` removes the *repeat's* signature without
    touching the scan's own variation, and §3.8c wet is applied last so its
    darkening and its flat pools survive to the render. §3.9 grain is added after
    this returns, by `generate`, exactly as for a procedural material.
    """
    photo = spec.photo
    maps = load_photo_maps(photo, res)
    world = photo_world_size(spec)

    # ── Base albedo: linear, pre-authored to the §03 band. ──
    # The scans run dark (dry brick sits near 0.05 linear); pre-scaling to the
    # target here keeps `finish_albedo`'s later correction inside its 2.6× guard
    # and is the same "author to the band, don't paint black" rule the whole file
    # enforces — a real basement wall reflects 20–40%, and a scan of one lit in a
    # studio has to be brought to that before the beam ever meets it.
    albedo = srgb_to_linear(maps["albedo"])
    if photo.tint_mul != (1.0, 1.0, 1.0):
        albedo = albedo * np.asarray(photo.tint_mul, np.float32)[None, None, :]
    albedo *= spec.target_albedo / max(float(albedo.mean()), 1e-6)

    # ── Base relief: the scan's normal, integrated. ──
    height = integrate_normal_to_height(maps["normal"])
    if photo.recess_from_albedo > 0.0:
        # Sink the scan's darker texels — stains, joints, formwork lines all sit
        # where the surface is worn low. For near-flat stock this is what gives
        # the AO something to occlude; it also ties the dirt to the geometry.
        luma = albedo @ np.asarray([0.2126, 0.7152, 0.0722], np.float32)
        dark = np.clip(1.0 - luma / max(float(luma.mean()), 1e-6), 0.0, 1.5)
        height = normalise01(height - photo.recess_from_albedo * dark)

    roughness = np.clip(maps["rough"], 0.0, 1.0).astype(np.float32)

    metallic = np.zeros((res, res), np.float32)
    if photo.has_metal:
        metallic = np.clip(maps["metal"] * photo.metallic_ceiling, 0.0, 1.0).astype(np.float32)

    # ── Grime: the room's dirt, not the material's. Broad and slow. ──
    if photo.grime > 0.0:
        soot = blur(fbm(res, seed + 44, beta=2.4, low_cycles=2.2), res * 0.02)
        patches = stain(res, seed + 51, threshold=0.60, softness=0.10, detail=0.5, low_cycles=1.7)
        grime = np.clip(0.6 * soot + 0.5 * patches, 0.0, 1.0)
        albedo = albedo * (1.0 - photo.grime * grime)[..., None]
        roughness = np.clip(roughness + 0.12 * photo.grime * grime, 0.0, 1.0)

    if photo.kind == "damp_band":
        albedo, roughness, height = _photo_damp_band(albedo, roughness, height, res, seed, photo)
    elif photo.kind == "floor":
        albedo, roughness = _photo_floor_wear(albedo, roughness, res, seed, photo)
    else:
        albedo, roughness = _photo_wall_streaks(albedo, roughness, res, seed, photo)

    if photo.rust > 0.0:
        albedo, roughness, metallic = _photo_rust(albedo, roughness, metallic, height, res, seed, photo)

    # ── Kill the repeat's low-frequency envelope, keep the scan's variation. ──
    albedo = detile(albedo, strength=photo.detile_strength, axes=photo.detile_axes)

    # ── §3.8c wet, keyed to the scan's own hollows. ──
    if photo.wet_fraction > 0.0 and photo.kind != "damp_band":
        seep = stain(res, seed + 31, threshold=0.46, softness=0.09, detail=0.55, low_cycles=1.9)
        level = water_line(height, photo.wet_fraction)
        pool = standing_water(height, level, softness=0.05) * np.clip(seep * 1.3, 0.0, 1.0)
        film = np.clip(seep - pool, 0.0, 1.0) * 0.8
        albedo, roughness, height = wet(
            albedo, roughness, height, film, pool, level,
            darkening=photo.wet_darken, film_roughness=photo.wet_film_rough,
            pool_roughness=photo.wet_pool_rough)

    return MapSet(albedo.astype(np.float32), roughness.astype(np.float32),
                  height.astype(np.float32), metallic, world, photo.height_scale)


def _photo_wall_streaks(albedo, roughness, res, seed, photo):
    """A few vertical damp runs down a wall — dirt that started somewhere and
    travelled one way, which free noise never does."""
    damp = np.clip(
        runs(stain(res, seed + 61, threshold=0.72, softness=0.04, detail=0.5, low_cycles=2.4),
             length=int(res * 0.16)) * 0.7
        + stain(res, seed + 63, threshold=0.66, softness=0.09, detail=0.45, low_cycles=1.3) * 0.5,
        0.0, 1.0)
    albedo = albedo * (1.0 - 0.16 * damp)[..., None]
    roughness = np.clip(roughness - 0.06 * damp, 0.0, 1.0)
    return albedo.astype(np.float32), roughness.astype(np.float32)


def _photo_floor_wear(albedo, roughness, res, seed, photo):
    """A worn central traffic lane, lighter and a touch smoother — feet polish a
    path down the middle of a corridor and leave the edges dirty."""
    if photo.traffic <= 0.0:
        return albedo.astype(np.float32), roughness.astype(np.float32)
    across = np.abs(np.linspace(-1.0, 1.0, res, dtype=np.float32))[None, :]
    lane = smoothstep(0.75, 0.15, across) * (0.6 + 0.4 * blur(
        fbm(res, seed + 71, beta=2.2, low_cycles=2.0), res * 0.02))
    lane = lane * np.ones((res, 1), np.float32)
    albedo = albedo * (1.0 + 0.10 * photo.traffic * lane)[..., None]
    roughness = np.clip(roughness - 0.10 * photo.traffic * lane, 0.0, 1.0)
    return albedo.astype(np.float32), roughness.astype(np.float32)


def _photo_rust(albedo, roughness, metallic, height, res, seed, photo):
    """Iron-oxide bloom for the steel plate, sourced from the geometry so it reads
    as corrosion rather than paint: it settles where water sits — the plate's
    hollows — and blooms in a few patches, granular at close range."""
    hollow = smoothstep(0.45, 0.05, height)
    bloom = stain(res, seed + 81, threshold=0.62, softness=0.06, detail=0.6, low_cycles=6.0)
    granular = 0.55 + 0.45 * fbm(res, seed + 83, beta=1.5, low_cycles=18.0)
    rust = np.clip((0.55 * hollow + 0.9 * bloom) * granular * photo.rust, 0.0, 1.0)

    # Dark iron oxide under a lighter powdery edge, never one flat brown.
    albedo = albedo + (np.asarray([0.150, 0.064, 0.030], np.float32)[None, None, :] - albedo) \
        * (0.85 * rust)[..., None]
    albedo = albedo + (np.asarray([0.255, 0.125, 0.055], np.float32)[None, None, :] - albedo) \
        * (0.6 * np.clip(rust - 0.35, 0.0, 1.0))[..., None]
    roughness = np.clip(roughness + 0.5 * rust, 0.0, 1.0)
    metallic = np.clip(metallic * (1.0 - rust), 0.0, 1.0)   # rust is not a conductor
    return albedo.astype(np.float32), roughness.astype(np.float32), metallic.astype(np.float32)


def _photo_damp_band(albedo, roughness, height, res, seed, photo):
    """Plaster's rising damp, keyed to the floor line — the material's whole
    reason to exist. Mirrors `build_wall_plaster`: a tide front that climbs out
    of the ground, salt bloom below it, junction grime in the corner, and the
    §3.8c darker-and-smoother band under the front. Tiles along the wall only."""
    above_floor = upward(res)
    front = 0.40 + 0.13 * (fbm(res, seed + 31, beta=2.4, low_cycles=2.5) - 0.5) * 2.0
    rise = smoothstep(0.10, -0.06, above_floor - front)
    tide = np.exp(-((above_floor - front) / 0.028) ** 2) * (
        0.55 + 0.45 * stain(res, seed + 5, threshold=0.56, softness=0.10, detail=0.45, low_cycles=1.2))
    salt = np.clip(
        stain(res, seed + 32, threshold=0.66, softness=0.05, detail=0.65, low_cycles=14.0)
        * rise * smoothstep(0.04, 0.16, above_floor) * 1.3, 0.0, 1.0)
    junction = smoothstep(0.16, 0.03, above_floor) * (0.55 + 0.45 * warped_fbm(
        res, seed + 33, beta=2.2, strength=res * 0.02, low_cycles=3.0))

    # Darken toward damp, lift toward the salt bloom (near-white), sink in the corner.
    albedo = albedo * (1.0 - 0.30 * rise[..., None] - 0.10 * tide[..., None]
                       - 0.42 * junction[..., None])
    albedo = albedo + (np.asarray([0.52, 0.51, 0.49], np.float32)[None, None, :]
                       - albedo) * (0.55 * salt)[..., None]
    roughness = np.clip(roughness + 0.14 * salt + 0.10 * junction, 0.0, 1.0)

    # §3.8c: the band below the front is genuinely wet — darker and smoother.
    film = np.clip(rise * (0.35 + 0.65 * stain(
        res, seed + 5, threshold=0.56, softness=0.10, detail=0.45, low_cycles=1.2)) - salt * 0.6, 0.0, 1.0)
    albedo, roughness, _ = wet(
        albedo, roughness, height, film, np.zeros_like(film), 0.0,
        darkening=photo.wet_darken, film_roughness=photo.wet_film_rough)
    return albedo.astype(np.float32), roughness.astype(np.float32), height.astype(np.float32)


def photo_world_size(spec: "MaterialSpec") -> float:
    """The world size a photo material tiles at — the same metres the procedural
    builder returns, so the kit's UV scale and the manifest are untouched by the
    swap. Read once from the fallback build's MapSet and cached on the spec name."""
    return _PHOTO_WORLD[spec.name]


# world_size per mapped material, kept identical to its procedural builder so the
# manifest's `world_size_metres` (and therefore the in-game UV tiling) does not move.
_PHOTO_WORLD: Dict[str, float] = {
    "Wall_Brick_Painted": 1.8,
    "Wall_Concrete_Bare": 2.5,
    "Wall_Plaster_Stained": 2.0,
    "Floor_Tile": 2.0,
    "Floor_Concrete": 2.5,
    "Floor_Metal": 2.0,
}


MATERIALS: Tuple[MaterialSpec, ...] = (
    # 0.20 → 0.23 → 0.25. Zone A sat at 37.8 % crushed with the textures alone and
    # went to 45.3 % once the fittings stopped lighting through walls — the light
    # it was reading by was light that should never have reached it. Worn timber
    # goes grey and pale rather than dark, so paying for that here is in
    # character; `build_wood` already lifts the traffic lane the same way.
    MaterialSpec(
        "Floor_Wood", build_wood, seed=1201, target_albedo=0.25,
        slots=("Floor_Wood",),
        grain=Grain(feature_mm=11.0, relief_mm=0.45, albedo=0.10, roughness=0.05, anisotropy=0.85),
        detail="Detail_Timber",
        note="§12 구역 A 나무 — staggered boards, deep gaps, soft traffic lane.",
    ),
    MaterialSpec(
        "Floor_Tile", build_tile, seed=1301, target_albedo=0.32,
        slots=("Floor_Tile",),
        grain=Grain(feature_mm=9.0, relief_mm=0.20, albedo=0.05, roughness=0.04),
        detail="Detail_Ceramic",
        photo=PhotoSpec(
            set_dir="ambientcg/Tiles133B", provenance="ambientCG Tiles133B (CC0)",
            height_scale=0.010, kind="floor", grime=0.14, detile_strength=0.45,
            wet_fraction=0.14, wet_darken=0.45, wet_film_rough=0.16, wet_pool_rough=0.08,
            traffic=0.45),
        note="§12 구역 B 타일 — dirty white mosaic, dark grout, glossiest floor so the "
             "beam streaks. CC0 base: ambientCG Tiles133B.",
    ),
    # 0.24 → 0.31 → 0.35 → 0.40, in three measured steps, and it is the highest
    # value in the set by a wide margin for a reason that is not taste. Zone C
    # started at 43.8% of the frame crushed to black and 29.2% legible, out of
    # ART.md's 10–40% and 30–75% bands in the same direction: too dark to read.
    # Gravel has 45 mm of relief, the deepest in the set, so its own baked AO
    # takes most of its albedo back in self-shadow before a single light is
    # placed — it needs the most paint on it to arrive where a flat concrete
    # slab arrives with 0.23. Pale limestone ballast really does sit here, and
    # 0.44 is the last step before ALBEDO_MAX_LINEAR stops the run.
    MaterialSpec(
        "Floor_Gravel", build_gravel, seed=1401, target_albedo=0.44,
        slots=("Floor_Gravel",),
        ao_strength=0.55,
        grain=Grain(feature_mm=13.0, relief_mm=1.1, albedo=0.09, roughness=0.05),
        detail="Detail_Aggregate",
        note="§12 구역 C 자갈 — three stone scales, 45 mm relief, standing water in the voids.",
    ),
    # 0.28 → 0.23. Zone D measured 8.8% crushed, below ART.md's 10% floor: not
    # dark enough for §03's lock to be a lock, which is the mechanic this zone
    # holds the exit for.
    MaterialSpec(
        "Floor_Concrete", build_concrete_floor, seed=1501, target_albedo=0.23,
        slots=("Floor_Concrete",),
        grain=Grain(feature_mm=17.0, relief_mm=2.0, albedo=0.15, roughness=0.07),
        detail="Detail_Aggregate",
        # Same scan as the concrete wall, but rolled off it (offset), darker
        # (0.23 vs 0.27), floored (traffic lane) and flooded (§3.8c) — so the two
        # never read as the same surface even where a corridor shows both.
        photo=PhotoSpec(
            set_dir="polyhaven/concrete_wall_007",
            provenance="PolyHaven concrete_wall_007 (CC0, floor variant)",
            height_scale=0.016, offset=(0.37, 0.21), kind="floor", grime=0.18,
            detile_strength=0.55, recess_from_albedo=0.35, wet_fraction=0.12,
            wet_darken=0.50, wet_film_rough=0.22, wet_pool_rough=0.09, traffic=0.4,
            tint_mul=(0.90, 0.95, 1.04),
            ao_floor=0.22, shadow_lift=0.28, shadow_knee=0.22),
        note="§12 구역 D 콘크리트 — stains, exposed aggregate, cracks that hold water. "
             "CC0 base: PolyHaven concrete_wall_007 (floor variant).",
    ),
    MaterialSpec(
        "Floor_Metal", build_metal_floor, seed=1601, target_albedo=0.26,
        slots=("Floor_Metal",),
        grain=Grain(feature_mm=8.0, relief_mm=0.25, albedo=0.07, roughness=0.06, anisotropy=0.9),
        detail="Detail_Brushed",
        photo=PhotoSpec(
            set_dir="ambientcg/DiamondPlate008A",
            provenance="ambientCG DiamondPlate008A (CC0)",
            height_scale=0.012, has_metal=True, metallic_ceiling=0.5, kind="floor",
            grime=0.12, detile_strength=0.50, wet_fraction=0.16, wet_darken=0.42,
            wet_film_rough=0.24, wet_pool_rough=0.10, traffic=0.3, rust=0.48),
        note="§12 계단 금속 — diamond tread, lowest roughness, brightest specular. "
             "CC0 base: ambientCG DiamondPlate008A. Metalness stays partial (§ambient).",
    ),
    # ── The three storeys that had no texture at all. ──
    #
    # In `FloorMaterial` order of *depth* rather than of enum value, because that
    # is the order a player meets them in and the order ART.md §3.12 and
    # `ZoneIdentity` both list them in: B6 병동, B7 수몰층, B8 굴착층.
    #
    # All three are **fully procedural**, and that is a decision rather than an
    # omission. `PhotoSpec` exists because a scan carries a surface's *history*,
    # which noise does not — and none of the five scans vendored under
    # `tools/textures/cc0` is any of these materials, so using one would be
    # dressing a floor in another floor's history, which is worse than no history
    # at all. Nothing new was downloaded (see docs/ASSETS.md). Beyond that, two of
    # the three have an argument against a photograph on their own terms: water is
    # not a static surface to scan — §3.8c derives it from the height field, which
    # is why `wet()` exists — and a dig face is a *worked* surface whose tool
    # marks and shoring have to be keyed to the geometry that carries them.
    # `Floor_Wood` and `Floor_Gravel`, the two floors this set is judged best on,
    # are procedural for the same reason.
    #
    # 0.22 linear, and it is the only one of the three not at the band's floor.
    # A ward is the one storey in the building finished for people, and contract
    # carpet is a mid-tone material; painting it down to 0.16 to make B6 measure
    # darker would be exactly the "darkness from the albedo" this file forbids.
    # It sits one point *under* `Floor_Wood` and 0.72 of roughness above it, which
    # is where the eight-surface space had room left.
    MaterialSpec(
        "Floor_Carpet", build_carpet, seed=1701, target_albedo=0.22,
        slots=("Floor_Carpet",),
        grain=Grain(feature_mm=7.0, relief_mm=0.60, albedo=0.14, roughness=0.03),
        detail="Detail_Fibre",
        note="§12 B6 병동 카펫 — 50 cm contract tile laid quarter-turned, so the pile "
             "lights as a checkerboard that answers to viewing angle. Crushed lanes are "
             "the DARK ones, and the bald scars inside them the bright. Matte end of the set.",
    ),
    # 0.16 — the darkest surface in the game, and the least arbitrary number here.
    # Water darkens what it covers by removing the air/solid interface that was
    # scattering light straight back out (§3.8c); three quarters of this floor is
    # under centimetres of it, and what comes back is a reflection of a room that
    # is almost black. It is one point off `ALBEDO_MIN_LINEAR` because the band's
    # floor is where a genuinely drowned surface belongs.
    MaterialSpec(
        "Floor_Water", build_water, seed=1801, target_albedo=0.16,
        slots=("Floor_Water",),
        grain=Grain(feature_mm=9.0, relief_mm=0.16, albedo=0.06, roughness=0.03),
        detail="Detail_Ceramic",
        note="§12 B7 수몰층 물 — standing water over the building's own silted screed, "
             "74 % drowned. The shoreline is the read: the only level line in the game, "
             "cutting across the architecture instead of following it. §3.8c throughout.",
    ),
    # 0.17, aimed at the dark end on purpose and 0.27 below 자갈. B8 is the last
    # storey, §07 promises the night deepens all the way down, and
    # `MapKitCatalogue.FloorTileFor` aliases Earth onto the GRAVEL tile mesh — so
    # the one thing that must not happen is this material landing anywhere near
    # gravel's 0.44, which is what an untextured floor on that mesh was already
    # being read as. Damp turned loam is 0.08–0.12 in life; 0.17 is the band floor
    # talking, not the art.
    MaterialSpec(
        "Floor_Earth", build_earth, seed=1901, target_albedo=0.17,
        slots=("Floor_Earth",),
        # No `ao_strength` discount, and that is deliberate rather than an
        # oversight. 자갈 is still the only surface in the set that needs one: it
        # carries 45 mm of relief and measured an AO contrast of 0.74 undiscounted,
        # so the engine's SSAO was shading it a second time. 흙 measures 0.32 at
        # full strength with 28 mm — between 콘크리트 and 자갈, which is where a
        # surface of its depth belongs — so there is nothing here to give back.
        grain=Grain(feature_mm=15.0, relief_mm=2.2, albedo=0.16, roughness=0.06),
        detail="Detail_Aggregate",
        note="§12 B8 굴착층 흙 — blocky clods over open fissures, spade cuts that read as "
             "the smooth population, half-buried shoring boards, water standing in the "
             "gouges. Darkest floor in the set.",
    ),
    MaterialSpec(
        "Wall_Brick_Painted", build_wall_brick, seed=2101, target_albedo=0.22,
        slots=("Wall_Structure",),
        grain=Grain(feature_mm=9.0, relief_mm=1.0, albedo=0.12, roughness=0.06),
        detail="Detail_Sand",
        photo=PhotoSpec(
            set_dir="polyhaven/brick_wall_10", provenance="PolyHaven brick_wall_10 (CC0)",
            height_scale=0.024, kind="wall", grime=0.16, detile_strength=0.55,
            ao_floor=0.28, shadow_lift=0.60, shadow_knee=0.27),
        note="Painted brick, stretcher bond. Bound to the kit's single wall slot. "
             "CC0 base: PolyHaven brick_wall_10.",
    ),
    MaterialSpec(
        "Wall_Concrete_Bare", build_wall_concrete, seed=2201, target_albedo=0.27,
        grain=Grain(feature_mm=15.0, relief_mm=1.5, albedo=0.11, roughness=0.07),
        detail="Detail_Sand",
        # recess_from_albedo sinks the formwork lines and stains into the relief:
        # a scan of flat board-formed concrete carries almost no normal, so without
        # it the AO has nothing to occlude and the run's own gate catches it.
        photo=PhotoSpec(
            set_dir="polyhaven/concrete_wall_007",
            provenance="PolyHaven concrete_wall_007 (CC0)",
            height_scale=0.030, kind="wall", grime=0.18, detile_strength=0.55,
            recess_from_albedo=0.35, tint_mul=(0.94, 0.97, 1.03)),
        note="Board-formed concrete with snap-tie holes. Generated, awaiting a per-zone "
             "wall slot. CC0 base: PolyHaven concrete_wall_007.",
    ),
    # 0.34 → 0.30. Still the brightest wall in the set and still the surface that
    # throws the beam back, but 0.34 was chosen before the tiling correction
    # halved the area each stain covers, and it now clips at close range.
    MaterialSpec(
        "Wall_Plaster_Stained", build_wall_plaster, seed=2301, target_albedo=0.30,
        slots=("Wall_Dado",),
        grain=Grain(feature_mm=8.0, relief_mm=0.45, albedo=0.08, roughness=0.05),
        detail="Detail_Sand",
        tiling_axes=(1,),
        # The standout scan — broken white plaster over brick, the 병동 look. The
        # rising-damp band stays keyed to the floor line (kind="damp_band"), and
        # detile/tiling run across the wall only, so the vertical structure the
        # material exists for survives the swap.
        photo=PhotoSpec(
            set_dir="ambientcg/PaintedPlaster016",
            provenance="ambientCG PaintedPlaster016 (CC0)",
            height_scale=0.015, kind="damp_band", grime=0.12, detile_strength=0.55,
            detile_axes=(1,), wet_darken=0.34, wet_film_rough=0.36,
            ao_floor=0.18, shadow_lift=0.40, shadow_knee=0.24),
        note="Water-stained plaster on the 0.20–1.02 m tanking band. Rising damp, its "
             "tide line, salt bloom and the junction dirt are all keyed to the floor, "
             "so this tiles along the wall only. CC0 base: ambientCG PaintedPlaster016.",
    ),
    MaterialSpec(
        "Ceiling_Concrete_Formed", build_ceiling_concrete, seed=3101, target_albedo=0.21,
        slots=("Ceiling_Structure",),
        grain=Grain(feature_mm=15.0, relief_mm=0.9, albedo=0.10, roughness=0.05),
        # No detail normal, and it is the only surface in the set without one.
        # A detail map exists to serve the near field, and nothing is ever near a
        # soffit: §05 puts the eye at 1.63 m under a 3 m ceiling, so the closest a
        # player gets to it is 1.4 m and that is straight up. It is also a large
        # share of the screen in every corridor, so it was the most expensive place
        # in the game to sample two extra textures for detail nobody can reach.
        note="Board-marked soffit — directional grain for corridors seen at grazing angles.",
    ),
    MaterialSpec(
        "Trim_Steel_Rusted", build_trim_steel, seed=4101, target_albedo=0.19,
        slots=("Trim_Steel",),
        grain=Grain(feature_mm=7.0, relief_mm=0.55, albedo=0.12, roughness=0.07),
        detail="Detail_Brushed",
        note="Rusted ribbed steel. Sits inside the beam more often than the wall behind it.",
    ),
    MaterialSpec(
        "Trim_Door_Painted", build_trim_door, seed=4201, target_albedo=0.31,
        slots=("Door_Leaf",),
        grain=Grain(feature_mm=6.0, relief_mm=0.22, albedo=0.05, roughness=0.04),
        detail="Detail_Ceramic",
        note="Painted door leaf — palest surface, so a beam finds §04's 정비공 chokepoints first.",
    ),
    MaterialSpec(
        "Trim_Skirting_Painted", build_trim_skirting, seed=4301, target_albedo=0.29,
        slots=("Trim_Painted",),
        grain=Grain(feature_mm=5.0, relief_mm=0.22, albedo=0.05, roughness=0.04),
        detail="Detail_Ceramic",
        tiling_axes=(1,),
        note="Painted skirting, a cross-section profile: tiles along its length only.",
    ),
)


# ── Detail normals ──────────────────────────────────────────────────────────
#
# ART.md §7.9 listed these as unreachable, because the engine-side binder sets
# four maps and `_DetailNormalMap` is not one of them. It is reachable: the
# binder is one file and the *materials* are a generated artefact, so a second
# pass can enrich them (`Assets/Scripts/Editor/Rendering/MaterialDetailPass.cs`).
#
# What they are for, stated once. The base maps run 410–683 px/m, so their
# finest honest feature is 3–5 mm. §05 puts the camera 1.63 m above a floor it
# is looking at from about a metre away, and at one metre a 1920-wide frame at
# 90° resolves roughly 1220 px/m — twice what the base map carries. Everything
# in the 0.3–4 mm band is therefore missing from the exact surface the player
# spends the match staring at. One 512² normal tiled every 30 cm supplies
# 1700 px/m for 0.3 MB, which is the cheapest legible detail in the project.
#
# They are pure grain on purpose: grain has no landmark, so a 30 cm repeat of it
# is invisible where a 30 cm repeat of anything recognisable would be unbearable.


@dataclass(frozen=True)
class DetailSpec:
    """One shared micro-normal: what it is made of and how big it is in the world."""

    name: str
    seed: int
    tile_metres: float
    """Metres one detail tile spans. Small — this is grain, not a pattern."""

    feature_mm: float
    relief_mm: float
    anisotropy: float = 0.0
    resolution: int = 512
    note: str = ""


DETAILS: Tuple[DetailSpec, ...] = (
    DetailSpec(
        "Detail_Sand", seed=9101, tile_metres=0.30, feature_mm=1.6, relief_mm=0.30,
        note="Cement, plaster and brick: fine isotropic tooth, the sand in the mix.",
    ),
    DetailSpec(
        "Detail_Aggregate", seed=9201, tile_metres=0.45, feature_mm=3.2, relief_mm=0.55,
        note="Floated concrete and packed ballast fines: coarser, still sub-5 mm.",
    ),
    DetailSpec(
        "Detail_Timber", seed=9301, tile_metres=0.30, feature_mm=1.1, relief_mm=0.22,
        anisotropy=0.9,
        note="Timber fibre — the same noise drawn along the board.",
    ),
    DetailSpec(
        "Detail_Brushed", seed=9401, tile_metres=0.25, feature_mm=0.7, relief_mm=0.12,
        anisotropy=0.95,
        note="Rolled and brushed steel. Tiny relief, but it is what breaks the beam's "
             "specular lobe into a streak instead of a disc.",
    ),
    DetailSpec(
        "Detail_Ceramic", seed=9501, tile_metres=0.22, feature_mm=0.9, relief_mm=0.08,
        note="Glaze and gloss paint: almost flat, just enough to stop a mirror.",
    ),
    DetailSpec(
        "Detail_Fibre", seed=9601, tile_metres=0.20, feature_mm=0.8, relief_mm=0.20,
        note="Carpet pile: individual loops at sub-millimetre scale, the one band a "
             "512 px/m base map cannot hold and the one a player kneeling on a ward "
             "floor is looking straight at.",
    ),
)


def generate_detail(spec: DetailSpec, out_dir: str) -> Tuple[str, float]:
    """Writes one shared micro-normal. Returns its relative path and measured slope."""
    field = micro_grain(spec.resolution, spec.tile_metres, spec.seed,
                        spec.feature_mm / 1000.0, anisotropy=spec.anisotropy)
    height = normalise01(field)
    normal = height_to_normal(height, spec.tile_metres, spec.relief_mm / 1000.0)

    relative = "Detail/" + spec.name + "_normal.png"
    write_png(os.path.join(out_dir, relative), to_u8(normal * 0.5 + 0.5))

    # The mean tilt off vertical, in degrees. A detail normal that measures 0°
    # is a flat blue image that imports, binds, renders and does nothing — the
    # same silent failure the AO map had.
    tilt = float(np.degrees(np.arccos(np.clip(normal[..., 2], -1.0, 1.0))).mean())
    return relative, tilt


# ── Decals ──────────────────────────────────────────────────────────────────
#
# The tiling set above can say what a surface is made of and cannot say anything
# about what has happened to it *here*. Everything in a real basement that tells
# you a room has been used is a one-off registered to a position: the dirt that
# collects where a crate has stood for years, the stain under the joint that
# drips, the pale patch where the slab was cut and made good, the polish worn
# through a doorway. Baking any of that into a tiling material puts it
# everywhere at once, which is worse than not having it — it turns into pattern.
#
# ART.md §7.9 called decals unreachable because "a decal needs a projector placed
# where a prop meets a floor, and prop placement is the dressing pass". The
# placement is done here instead, at the end of the pipeline, from the geometry
# the dressing pass left behind — see
# `Assets/Scripts/Editor/Rendering/ContactDecals.cs`.
#
# ONE RULE ABOUT THE COLOUR CHANNELS
# ----------------------------------
# Every decal's RGB is painted over the *whole* texture and only its alpha is
# shaped. A decal drawn as "colour where it is opaque, black where it is not"
# bleeds that black into the colour channels at every mip level, and the decal
# then wears a dark halo that gets worse with distance — which is the one place
# a decal must not draw attention to itself. Colour everywhere, shape in alpha.


@dataclass(frozen=True)
class DecalSpec:
    """One placed surface event: its maps, its physical size, and what it is for."""

    name: str
    build: Callable[[int, int], Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]]
    """Returns (linear RGB, alpha, roughness, height), all (res, res[, 3])."""

    seed: int
    size_metres: float
    """Metres the decal spans at scale 1. The placer varies this per instance."""

    metallic: float = 0.0
    note: str = ""


def _radial(res: int) -> np.ndarray:
    """Distance from the centre in units of the half-width. 1.0 at the edge midpoint."""
    axis = (np.arange(res, dtype=np.float32) + 0.5) / res * 2.0 - 1.0
    return np.sqrt(axis[:, None] ** 2 + axis[None, :] ** 2)


def _fade_border(alpha: np.ndarray, width: float = 0.06) -> np.ndarray:
    """Forces alpha to zero at the texture border.

    A decal is clamped, not tiled, so whatever sits on its last texel is
    stretched along the whole edge of the quad. Anything but zero there draws a
    hard rectangle on the floor, which is the single most recognisable way a
    decal system announces itself.
    """
    res = alpha.shape[0]
    axis = np.minimum(np.arange(res), np.arange(res)[::-1]).astype(np.float32) / res
    edge = np.minimum(axis[:, None], axis[None, :])
    return (alpha * smoothstep(0.0, width, edge)).astype(np.float32)


def build_decal_contact(res: int, seed: int):
    """Ground-in dirt where something has stood on a floor for years.

    Densest against the object's own footprint and thinning outwards, because
    that is where the sweeping never reached. The edge is broken by noise at two
    scales so it does not read as an airbrushed oval — the give-away of every
    contact decal that was drawn with a gradient tool.
    """
    r = _radial(res)
    noise = fbm(res, seed, beta=2.0, low_cycles=3.0)
    fine = fbm(res, seed + 1, beta=1.4, low_cycles=14.0)

    edge = 0.62 + (noise - 0.5) * 0.42
    alpha = smoothstep(1.0, 0.0, (r - 0.10) / np.maximum(edge, 0.05))
    alpha = np.clip(alpha * (0.55 + 0.75 * (1.0 - r)) * (0.65 + 0.7 * fine), 0.0, 1.0)

    colour = tint((0.055, 0.049, 0.041), np.ones_like(noise),
                  (0.030, 0.026, 0.022), noise * 0.8)
    roughness = 0.86 + fine * 0.10
    height = np.clip(alpha * 0.35 + fine * 0.25, 0.0, 1.0)
    return colour, _fade_border(alpha), roughness, height


def build_decal_water_stain(res: int, seed: int):
    """The dark tongue under a joint that has been dripping since it was built.

    Runs *down* the texture, which is what registers it to gravity: the placer
    puts +V downhill on a wall and along the run of the floor under a pipe.
    Damp, so it is darker and smoother than what it sits on rather than tinted —
    §3.8c of ART.md derives that from where the light actually goes.
    """
    seeds = fbm(res, seed, beta=1.2, low_cycles=6.0, high_cycles=40.0)

    # The source is a soft patch at the top centre, not a band across the whole
    # width: the streaks have to read as coming out of one joint, and a full-width
    # source draws a solid bar along the top edge of the quad instead.
    down = np.linspace(0.0, 1.0, res, dtype=np.float32)
    across = np.abs(np.linspace(-1.0, 1.0, res, dtype=np.float32))
    from_above = (smoothstep(0.42, 0.02, down)[:, None]
                  * smoothstep(0.95, 0.10, across)[None, :]).astype(np.float32)
    streak = runs(np.clip((seeds - 0.55) * 6.0, 0.0, 1.0) * from_above,
                  length=res // 2, falloff=1.35, axis=0)

    body = blur(streak, res * 0.012)
    salt = fbm(res, seed + 3, beta=1.7, low_cycles=9.0)

    alpha = np.clip(body * 2.2 + from_above * 0.55, 0.0, 0.92)
    colour = tint((0.038, 0.040, 0.043), np.ones_like(salt),
                  (0.086, 0.082, 0.070), np.clip(salt * 1.4 - 0.55, 0.0, 1.0))
    roughness = np.clip(0.55 - body * 0.28, 0.18, 1.0)
    height = np.clip(body * 0.4, 0.0, 1.0)
    return colour, _fade_border(alpha), roughness, height


def build_decal_scuff(res: int, seed: int):
    """Traffic wear: a drawn-out smear along the line people walk.

    Anisotropic on purpose. A round wear patch reads as a spill; a long one
    reads as a route, and §12's corridors are routes. Bright rather than dark —
    a floor that is walked on gets *polished*, and the polish is what catches
    §03's beam at a grazing angle.
    """
    grain = fbm(res, seed, beta=1.5, low_cycles=8.0)
    drawn = ndimage.gaussian_filter1d(grain, sigma=res * 0.045, axis=1, mode="wrap")
    drawn = normalise01(drawn)

    across = smoothstep(1.0, 0.15, np.abs(np.linspace(-1.0, 1.0, res, dtype=np.float32))[:, None])
    along = smoothstep(1.0, 0.05, np.abs(np.linspace(-1.0, 1.0, res, dtype=np.float32))[None, :])
    lines = scratches(res, seed + 5, count=42, length=0.55, width=0.0015)

    alpha = np.clip((0.30 + 0.55 * drawn) * across * along + lines * 0.35 * across, 0.0, 0.75)
    colour = tint((0.115, 0.112, 0.106), np.ones_like(drawn),
                  (0.052, 0.050, 0.047), lines)
    roughness = np.clip(0.42 + (1.0 - drawn) * 0.22, 0.18, 1.0)
    height = np.clip(lines * 0.5, 0.0, 1.0)
    return colour, _fade_border(alpha), roughness, height


def build_decal_patch(res: int, seed: int):
    """A cut-and-made-good repair: a slab of newer, paler, flatter screed.

    The most legible piece of history a concrete floor can carry, and the one a
    tiling material can never contain — its whole meaning is that it is a
    rectangle *here* and nowhere else. The edge is a trowelled joint, so it is
    hard on three sides and feathered where the screed was floated out.
    """
    axis = np.linspace(-1.0, 1.0, res, dtype=np.float32)
    box = np.minimum(smoothstep(0.92, 0.72, np.abs(axis))[:, None],
                     smoothstep(0.88, 0.66, np.abs(axis))[None, :])
    inner = np.minimum(smoothstep(0.80, 0.60, np.abs(axis))[:, None],
                       smoothstep(0.76, 0.54, np.abs(axis))[None, :])
    wobble = fbm(res, seed, beta=2.1, low_cycles=3.0)
    float_marks = ndimage.gaussian_filter1d(fbm(res, seed + 2, beta=1.6, low_cycles=10.0),
                                            sigma=res * 0.02, axis=1, mode="wrap")

    # The joint between old slab and new screed is the part that reads. Without
    # it the patch is a pale rectangle that could be a lighting artefact; with
    # it, it is obviously a repair.
    joint = np.clip(box - inner, 0.0, 1.0)
    alpha = np.clip(box * (0.34 + 0.26 * wobble) + joint * 0.55, 0.0, 0.92)
    colour = tint((0.128, 0.127, 0.124), np.ones_like(wobble),
                  (0.088, 0.086, 0.083), float_marks)
    roughness = np.clip(0.72 + float_marks * 0.14, 0.18, 1.0)
    height = np.clip(box * 0.7 + float_marks * 0.2, 0.0, 1.0)
    return colour, _fade_border(alpha, width=0.03), roughness, height


def build_decal_puddle(res: int, seed: int):
    """Standing water. §03's first worked example of a clue is 물이 있는 층.

    Three things make water read as water and none of them is a blue tint:
    it is *darker* (the film lets light into the substrate instead of scattering
    it back), it is *smooth* (roughness 0.05–0.12, which turns the beam's diffuse
    pool into a streak), and its edge is *level* — a puddle boundary follows a
    contour, so it is irregular in plan and sharp in section.
    """
    shape = fbm(res, seed, beta=2.3, low_cycles=2.5)
    wobble = fbm(res, seed + 2, beta=2.8, low_cycles=1.5)

    # The outline is a *contour*, so it is pushed around by the low-frequency
    # shape of the ground rather than being a circle with a rough edge. Warping
    # the radius is what turns one blob into a puddle with lobes and a neck.
    warped = _radial(res) * (0.72 + 0.62 * wobble)
    depth = np.clip(smoothstep(1.0, 0.30, warped) * (0.55 + 1.05 * shape) - 0.44, 0.0, 1.0)

    # Sharp in section: the water line is a threshold on depth, not a gradient.
    alpha = smoothstep(0.0, 0.055, depth)
    ripple = fbm(res, seed + 4, beta=1.1, low_cycles=26.0)

    colour = tint((0.021, 0.022, 0.024), np.ones_like(shape),
                  (0.012, 0.013, 0.015), np.clip(depth * 2.2, 0.0, 1.0))
    roughness = np.clip(0.055 + (1.0 - alpha) * 0.35 + ripple * 0.045, 0.02, 1.0)
    height = np.clip(1.0 - depth * 0.3, 0.0, 1.0)
    return colour, _fade_border(alpha), roughness, height


def build_decal_drip(res: int, seed: int):
    """Spatter under a leak: a tight cluster of small dark marks.

    Small and cheap, and it does something no large decal does — it puts a
    feature at a size the player can only see by being close, which is what
    makes a room reward walking into it.
    """
    generator = rng(seed)
    alpha = np.zeros((res, res), dtype=np.float32)
    grid = np.stack(np.meshgrid(np.arange(res), np.arange(res), indexing="ij"), axis=-1)

    for _ in range(26):
        centre = generator.normal(res * 0.5, res * 0.13, size=2)
        radius = generator.uniform(res * 0.008, res * 0.035)
        distance = np.linalg.norm(grid - centre, axis=-1)
        alpha = np.maximum(alpha, smoothstep(radius, radius * 0.4, distance)
                           * float(generator.uniform(0.45, 0.95)))

    speckle = fbm(res, seed + 6, beta=1.3, low_cycles=18.0)
    colour = tint((0.030, 0.029, 0.028), np.ones_like(speckle),
                  (0.016, 0.016, 0.017), speckle)
    roughness = np.clip(0.30 + speckle * 0.25, 0.18, 1.0)
    return colour, _fade_border(alpha), roughness, np.clip(alpha * 0.4, 0.0, 1.0)


def build_decal_soot(res: int, seed: int):
    """The blackened halo a bare bulb leaves on the soffit above it.

    §03 makes every light switchable and therefore worth looking at, and this is
    the cheapest thing that says a fitting has been *burning for years* rather
    than being switched on for the render. It also solves a framing problem: the
    ceiling above a practical is otherwise the flattest, emptiest, best-lit
    surface in the room.
    """
    r = _radial(res)
    plume = fbm(res, seed, beta=2.4, low_cycles=2.5)
    body = smoothstep(1.0, 0.0, r / np.maximum(0.55 + (plume - 0.5) * 0.5, 0.1))

    alpha = np.clip(body ** 1.6 * 0.78, 0.0, 0.78)
    colour = tint((0.020, 0.019, 0.018), np.ones_like(plume),
                  (0.045, 0.041, 0.036), plume * 0.5)
    roughness = np.full_like(plume, 0.93)
    return colour, _fade_border(alpha), roughness, np.zeros_like(plume)


def build_decal_rust(res: int, seed: int):
    """Rust bleeding out of a fixing and running down what it is bolted to.

    The only warm decal in the set, and it is the one that keeps the building
    from being a single colour (ART.md §7.8): every steel fixing in the map is a
    small orange source of contrast on a wall that is otherwise cold grey.
    """
    axis = (np.arange(res, dtype=np.float32) + 0.5) / res * 2.0 - 1.0

    # The fixing sits near the top of the decal and the bleed runs down away
    # from it, so the placer only has to know which way is down.
    offset = np.sqrt((axis[:, None] + 0.74) ** 2 + axis[None, :] ** 2)
    head = smoothstep(0.26, 0.0, offset)

    seeds = fbm(res, seed, beta=1.1, low_cycles=8.0, high_cycles=48.0)
    streak = runs(np.clip((seeds - 0.60) * 7.0, 0.0, 1.0) * smoothstep(0.55, 0.1, offset),
                  length=int(res * 0.75), falloff=1.5, axis=0)
    body = blur(streak, res * 0.008)

    alpha = np.clip(body * 1.8 + head, 0.0, 0.94)
    grit = fbm(res, seed + 8, beta=1.5, low_cycles=12.0)

    colour = tint((0.135, 0.052, 0.021), np.ones_like(grit),
                  (0.062, 0.030, 0.016), grit * 0.9)
    roughness = np.clip(0.80 + grit * 0.15, 0.18, 1.0)
    height = np.clip(body * 0.3 + head * 0.5, 0.0, 1.0)
    return colour, _fade_border(alpha), roughness, height


DECALS: Tuple[DecalSpec, ...] = (
    DecalSpec("Decal_Contact", build_decal_contact, seed=7101, size_metres=1.30,
              note="Dirt where a prop has stood. Placed at every prop's footprint."),
    DecalSpec("Decal_WaterStain", build_decal_water_stain, seed=7201, size_metres=1.60,
              note="Damp tongue under a dripping joint. Runs downhill in +V."),
    DecalSpec("Decal_Scuff", build_decal_scuff, seed=7301, size_metres=2.60,
              note="Polished traffic line. Aligned along the corridor it sits in."),
    DecalSpec("Decal_Patch", build_decal_patch, seed=7401, size_metres=1.80,
              note="Cut-and-made-good screed repair. Concrete and tile floors only."),
    DecalSpec("Decal_Puddle", build_decal_puddle, seed=7501, size_metres=1.70,
              note="Standing water — §03's 물이 있는 층. Smooth enough to mirror the room."),
    DecalSpec("Decal_Drip", build_decal_drip, seed=7601, size_metres=0.70,
              note="Spatter under a leak. Near-field detail that repays walking in."),
    DecalSpec("Decal_Soot", build_decal_soot, seed=7701, size_metres=1.10,
              note="Burn halo on the soffit above a bare bulb."),
    DecalSpec("Decal_Rust", build_decal_rust, seed=7801, size_metres=0.85,
              note="Rust bleeding from a fixing. The only warm mark in the building."),
)


def generate_decal(spec: DecalSpec, out_dir: str, res: int) -> Dict[str, object]:
    """Writes one decal's three maps and returns its manifest entry."""
    colour, alpha, roughness, height = spec.build(res, spec.seed)

    if float(alpha.max()) < 0.25:
        raise AssertionError("%s is effectively invisible (max alpha %.3f)"
                             % (spec.name, float(alpha.max())))

    border = max(float(np.abs(alpha[0]).max()), float(np.abs(alpha[-1]).max()),
                 float(np.abs(alpha[:, 0]).max()), float(np.abs(alpha[:, -1]).max()))
    if border > 1e-3:
        raise AssertionError(
            "%s has alpha %.4f on its border; a clamped decal stretches that along the "
            "whole edge of the quad and draws a rectangle on the floor" % (spec.name, border))

    folder = os.path.join(out_dir, "Decals")
    rgba = np.concatenate([linear_to_srgb(np.clip(colour, 0.0, 1.0)),
                           alpha[..., None]], axis=-1)
    write_png(os.path.join(folder, spec.name + "_albedo.png"), to_u8(rgba))

    roughness = np.maximum(roughness, 0.02).astype(np.float32)
    mask = np.zeros((res, res, 4), dtype=np.float32)
    mask[..., :3] = spec.metallic
    mask[..., 3] = 1.0 - roughness
    write_png(os.path.join(folder, spec.name + "_ms.png"), to_u8(mask))

    normal = height_to_normal(height, spec.size_metres, 0.004)
    write_png(os.path.join(folder, spec.name + "_normal.png"), to_u8(normal * 0.5 + 0.5))

    return {
        "name": spec.name,
        "size_metres": spec.size_metres,
        "coverage": round(float((alpha > 0.05).mean()), 4),
        "mean_alpha": round(float(alpha.mean()), 4),
        "roughness_mean": round(float(roughness.mean()), 4),
        "metallic": spec.metallic,
        "note": spec.note,
        "maps": {
            "albedo": "Decals/" + spec.name + "_albedo.png",
            "normal": "Decals/" + spec.name + "_normal.png",
            "metallic_smoothness": "Decals/" + spec.name + "_ms.png",
        },
    }


# ── Light sprites ───────────────────────────────────────────────────────────
#
# §03 makes light the mechanic — 어둠 = 목표의 잠금장치 — which means every light
# in the building has to look like a thing that could be switched off. A point
# light on its own is not that: it puts a disc on the floor and the *source* is
# invisible, so the room reads as lit by nothing.
#
# URP 17 has no volumetric fog, so the alternative to geometry is nothing at all
# (ART.md §7.10). These two sprites are what the geometry is made of, and they
# are drawn **additively**, which is the reason the falloff lives in RGB rather
# than in alpha: an additive blend is `src + dst` and never reads the alpha
# channel, so a sprite that carries its shape in alpha renders as a solid white
# rectangle. That mistake is invisible in a texture viewer and unmissable in a
# frame.


def build_glow_point(res: int, seed: int) -> np.ndarray:
    """The halo around a filament: a hot core with a long, quiet tail.

    Two lobes rather than one Gaussian. A single falloff either has a core too
    small to read as a source or a tail so wide it fogs the room; a real bare
    bulb behind a dirty glass does both at once — a small intense centre and a
    faint reach that picks out the dust.
    """
    r = _radial(res)
    core = np.exp(-(r / 0.10) ** 2)
    halo = np.exp(-(r / 0.42) ** 1.6) * 0.30
    field = np.clip(core + halo, 0.0, 1.0)

    # Zero at the border for the same reason the decals are: the sprite is
    # clamped, and anything left on the last texel becomes a bright frame.
    return (field * smoothstep(1.0, 0.86, r)).astype(np.float32)


def build_glow_shaft(res: int, seed: int) -> np.ndarray:
    """A cone of light hanging below a fitting, seen through the dust in the air.

    V runs down the shaft. It is brightest at the top where the source is, fades
    to nothing before it lands — a shaft that reaches the floor draws a hard line
    where it stops — and is soft across its width so the two crossed quads it is
    drawn on never show their own edges.

    The mottling is what stops it reading as a cardboard wedge: real shafts are
    picked out by dust that is not evenly distributed, and it is the unevenness
    the eye recognises rather than the cone.
    """
    across = np.abs(np.linspace(-1.0, 1.0, res, dtype=np.float32))[None, :]
    down = np.linspace(0.0, 1.0, res, dtype=np.float32)[:, None]

    # The cone widens as it descends, so the usable half-width at a given depth
    # grows — that is the whole shape, and it is one line.
    half_width = 0.18 + down * 0.80
    body = smoothstep(1.0, 0.0, np.clip(across / half_width, 0.0, 2.0))

    fade = smoothstep(1.0, 0.0, down) ** 1.35
    dust = 0.55 + 0.45 * fbm(res, seed, beta=1.9, low_cycles=3.0)

    return np.clip(body ** 1.7 * fade * dust, 0.0, 1.0).astype(np.float32)


GLOWS: Tuple[Tuple[str, Callable[[int, int], np.ndarray], int, bool, str], ...] = (
    ("Glow_Point", build_glow_point, 8801, False,
     "Halo around a bare filament. Drawn on three crossed quads at each fitting."),
    ("Glow_Shaft", build_glow_shaft, 8901, True,
     "Dust-lit cone below a fitting. V runs down the shaft."),
)


def generate_glow(name: str, build: Callable[[int, int], np.ndarray], seed: int,
                  top_open: bool, out_dir: str, res: int = 256) -> Dict[str, object]:
    """Writes one additive light sprite and returns its manifest entry.

    `top_open` exempts the V=0 edge from the border check. A shaft *starts* at
    its source and the source is at the top of the quad, so demanding it fade
    there would be demanding a beam that begins in mid-air. Every other edge
    still has to reach zero — a clamped additive sprite stretches whatever is on
    its last texel along the whole side of the quad, which draws a lit frame.
    """
    field = build(res, seed)
    edges = [float(field[-1].max()), float(field[:, 0].max()), float(field[:, -1].max())]
    if not top_open:
        edges.append(float(field[0].max()))

    border = max(edges)
    if border > 2e-3:
        raise AssertionError("%s is %.4f bright on its border; additive sprites clamp, so "
                             "that paints a lit frame around the quad" % (name, border))

    rgba = np.repeat(field[..., None], 4, axis=-1)
    write_png(os.path.join(out_dir, "Glow", name + "_albedo.png"),
              to_u8(linear_to_srgb(rgba)))

    return {
        "name": name,
        "map": "Glow/" + name + "_albedo.png",
        "mean": round(float(field.mean()), 4),
        "peak": round(float(field.max()), 4),
    }


# ── Verification ────────────────────────────────────────────────────────────
#
# The three properties that, if wrong, make the whole set worthless. Measured,
# never eyeballed.


@dataclass
class TextureReport:
    """What one generated material measured. Printed as a table at the end."""

    name: str
    resolution: int
    world_size: float
    albedo_mean_linear: float
    albedo_min_linear: float
    albedo_max_linear: float
    calibration_gain: float
    roughness_mean: float
    roughness_span: float
    normal_unit_error: float
    normal_relief_mm: float
    ao_mean: float
    ao_contrast: float = 0.0
    seam_ratio: float = 0.0
    texel_px_per_m: float = 0.0
    blob_share: float = 0.0
    grain_share: float = 0.0
    height_blob_share: float = 0.0
    failures: List[str] = field(default_factory=list)


BLOB_CYCLES_PER_TILE = 2.0
"""Where the "this is a repeating tile" band ends, in cycles per tile.

Anything slower than two cycles across the tile is a shape the size of the tile
itself, so every copy of it lands on the same grid and the eye locks onto the
lattice rather than onto the material. This is the frequency `detile` attacks,
and it is measured in cycles *per tile* rather than per metre on purpose: the
repeat is a property of the tiling, not of how big the tile is in the world.
"""

GRAIN_CYCLES_PER_METRE = 25.0
"""Where near-field grain begins, in cycles per metre — 4 cm features and finer.

A 1920-wide frame at §05's 90° FOV resolves ~21 px per degree, so at one metre
the eye separates about 610 cycles per metre and at three metres about 200. A
surface with no energy above 25 cyc/m therefore has nothing left to show once
the player is closer than about four metres — which, in a §12 corridor with a
2.2 m clear width, is *all the time*. This share is the number that says whether
the texel density the scale correction bought is being used or wasted.
"""


def spectral_shares(field_2d: np.ndarray, world_size: float) -> Tuple[float, float]:
    """Fraction of a map's contrast energy that is tile-sized blob, and that is grain.

    Both are measured from the radially binned power spectrum of the zero-mean
    field, so they are shift- and rotation-independent and do not care what the
    feature happens to *be* — a paint cloud, a stain and a pool of rust all count
    as blob if they are the size of the tile.

    Why this is measured at all: correcting the kit's UV scale (see
    `KIT_UV_UNITS_PER_METRE`) halved the world size of every tile without
    changing a single texture, so the repeat became twice as frequent and every
    texture's largest features became the repeat's signature. The same correction
    doubled texel density, which is only worth anything if there is detail up
    there to resolve. One number for each half of that.
    """
    data = np.asarray(field_2d, dtype=np.float64)
    if data.ndim == 3:
        # Rec.709 luma: chroma variation is not what the eye reads a surface by.
        data = data[..., 0] * 0.2126 + data[..., 1] * 0.7152 + data[..., 2] * 0.0722

    res = data.shape[0]
    spectrum = np.abs(np.fft.fft2(data - data.mean())) ** 2

    # Cycles per tile, signed frequencies folded to their magnitude.
    axis = np.fft.fftfreq(res) * res
    radius = np.sqrt(axis[:, None] ** 2 + axis[None, :] ** 2)

    total = float(spectrum.sum())
    if total <= 0.0:
        return 0.0, 0.0

    grain_cycles_per_tile = GRAIN_CYCLES_PER_METRE * world_size
    blob = float(spectrum[(radius > 0.0) & (radius <= BLOB_CYCLES_PER_TILE)].sum())
    grain = float(spectrum[radius >= grain_cycles_per_tile].sum())
    return blob / total, grain / total


def seam_ratio(image: np.ndarray, axes: Tuple[int, ...] = (0, 1)) -> float:
    """How far the wrap-around edge is an outlier among ordinary interior edges.

    Testing edge rows for *equality* would fail every detailed texture — two
    neighbouring rows of gravel are not equal either. So the test is whether the
    wrap pair is a *discontinuity*: does it differ more than adjacent pairs
    elsewhere in the same image?

    The comparison is against the *largest* interior difference, and arriving at
    that took two wrong answers worth recording, because both are the obvious
    thing to do.

    Comparing against the **mean** flags every texture that has structure in it.
    A board-formed wall has ten hard board boundaries among a thousand smooth row
    pairs, so the mean is dominated by the smooth ones; the wrap lands on a board
    boundary — it must, since the boundaries are periodic too — and the ratio
    explodes to 18 on a texture that is provably periodic.

    Comparing against a high **percentile** fixes that case and then fails when a
    texture has only a *few* structural edges. This wall's snap-tie holes sit on
    four rows, so the 99th percentile of a thousand row pairs still lands among
    the smooth ones and a legitimate streak start reads as a seam. The percentile
    that works depends on how many edges the texture happens to contain, which
    means it is not a criterion, it is a fudge factor.

    The **maximum** has neither problem, and states something meaningful on its
    own: a seam is a discontinuity *bigger than any that legitimately occurs
    inside the image*. It needs no tuning per material and no assumption about
    feature counts. Measured this way the whole set scores 0.0–0.95 and a real
    discontinuity scores 6–12.

    `axes` exists for profile textures, where tiling in one direction is the
    wrong thing to ask for — see `MaterialSpec.tiling_axes`.
    """
    data = np.asarray(image, dtype=np.float64)
    worst = 0.0

    for axis in axes:
        rolled = np.moveaxis(data, axis, 0).reshape(data.shape[axis], -1)
        wrap = float(np.abs(rolled[0] - rolled[-1]).mean())
        interior = np.abs(np.diff(rolled, axis=0)).mean(axis=1)
        worst = max(worst, wrap / max(float(interior.max()), 1e-9))

    return float(worst)


def verify(spec: MaterialSpec, maps: MapSet, normal: np.ndarray, occlusion: np.ndarray,
           gain: float) -> TextureReport:
    """Measures a finished material and records every way it fails the contract."""
    res = maps.albedo.shape[0]
    failures: List[str] = []

    albedo_mean = float(maps.albedo.mean())
    if not ALBEDO_MIN_LINEAR <= albedo_mean <= ALBEDO_MAX_LINEAR:
        failures.append(
            "albedo mean %.4f outside [%.2f, %.2f] linear — the beam would land on it invisibly"
            % (albedo_mean, ALBEDO_MIN_LINEAR, ALBEDO_MAX_LINEAR)
        )

    # Normals must be unit length or the lighting maths is simply wrong: a short
    # normal darkens the surface, which is the exact failure this file exists to
    # undo. Measured after the 8-bit round trip, because that is what ships.
    decoded = np.asarray(to_u8(normal * 0.5 + 0.5), dtype=np.float32) / 255.0 * 2.0 - 1.0
    lengths = np.linalg.norm(decoded, axis=-1)
    unit_error = float(np.abs(lengths - 1.0).max())
    if unit_error > NORMAL_UNIT_TOLERANCE:
        failures.append("normal max unit-length error %.4f > %.4f — not normalised"
                        % (unit_error, NORMAL_UNIT_TOLERANCE))

    # A flat normal map is a silent failure: it imports, it renders, and the
    # surface just stays flat. Require it to actually vary.
    if float(np.abs(decoded[..., 2] - 1.0).mean()) < 1e-4:
        failures.append("normal map is flat — no relief was generated from the height field")

    ratios: List[float] = []
    for label, image in (("albedo", maps.albedo), ("normal", normal), ("roughness", maps.roughness),
                         ("occlusion", occlusion), ("height", maps.height)):
        ratio = seam_ratio(image, spec.tiling_axes)
        ratios.append(ratio)
        if ratio > SEAM_RATIO_LIMIT:
            failures.append("%s seam ratio %.2f > %.2f — a visible join every %.1f m"
                            % (label, ratio, SEAM_RATIO_LIMIT, maps.world_size))

    # Roughness that never varies is the "flat colour" problem wearing a hat: the
    # whole surface answers the beam identically and nothing reads as material.
    roughness_span = float(maps.roughness.max() - maps.roughness.min())
    if roughness_span < 0.12:
        failures.append("roughness span %.3f is too flat to read as a real surface" % roughness_span)

    # An all-white AO map is the quietest failure in the set: it imports, it
    # binds, it renders, and the joints simply never darken. One shipped already
    # because nothing measured it, so it is measured now.
    #
    # The test is deliberately not "is the mean low enough". A smooth plaster
    # wall genuinely has almost no occlusion, and a cracked concrete floor has
    # deep occlusion over one percent of its area — both have a mean near 1.0,
    # and both are correct. What has to hold is the *relationship*: the crevices
    # must come out darker than the high points. That is scale-free, it is what
    # AO means, and a no-op map fails it outright.
    low_mark, high_mark = np.percentile(maps.height, [10.0, 90.0])
    crevice = occlusion[maps.height <= low_mark]
    peak = occlusion[maps.height >= high_mark]
    contrast = float(peak.mean() - crevice.mean()) if crevice.size and peak.size else 0.0

    if contrast < 0.03:
        failures.append(
            "AO contrast %.4f between crevice and peak — the map is not occluding anything"
            % contrast)

    # Measured, not judged: how much of this surface is tile-sized blob (the
    # repeat's signature) and how much is grain the near field can resolve.
    #
    # Both come from the albedo. The normal map is deliberately *not* averaged
    # in: it is a spatial derivative of the height field, and a derivative is a
    # high-pass filter, so every normal map in the set scores 70–100 % grain
    # whatever its height field actually contains. Its blob share is still
    # meaningful and is reported beside it.
    albedo_blob, albedo_grain = spectral_shares(maps.albedo, maps.world_size)
    normal_blob, _ = spectral_shares(maps.height, maps.world_size)

    return TextureReport(
        name=spec.name,
        resolution=res,
        world_size=maps.world_size,
        albedo_mean_linear=albedo_mean,
        albedo_min_linear=float(maps.albedo.min()),
        albedo_max_linear=float(maps.albedo.max()),
        calibration_gain=gain,
        roughness_mean=float(maps.roughness.mean()),
        roughness_span=roughness_span,
        normal_unit_error=unit_error,
        normal_relief_mm=float(maps.height.max() - maps.height.min()) * maps.height_scale * 1000.0,
        ao_mean=float(occlusion.mean()),
        ao_contrast=contrast,
        seam_ratio=max(ratios),
        texel_px_per_m=res / maps.world_size,
        blob_share=albedo_blob,
        grain_share=albedo_grain,
        height_blob_share=normal_blob,
        failures=failures,
    )


# ── Generation ──────────────────────────────────────────────────────────────


def generate(spec: MaterialSpec, out_dir: str, res: int) -> TextureReport:
    """Builds, calibrates, writes and measures one material."""
    # A CC0 scan is the base when the material declares one and the scans are
    # present; the procedural `build` is the fallback for a checkout without
    # them, so the generator degrades to the older look rather than hard-failing.
    if spec.photo is not None:
        try:
            maps = build_photo_surface(spec, res, spec.seed)
        except (FileNotFoundError, OSError) as exc:
            print(" [cc0 %s missing → procedural]" % spec.photo.set_dir, end="", flush=True)
            maps = spec.build(res, spec.seed)
    else:
        maps = spec.build(res, spec.seed)

    # Before calibration, so the grain's albedo modulation is inside what gets
    # landed on the target mean rather than knocking it off afterwards.
    if spec.grain is not None:
        apply_grain(maps, spec.grain, spec.seed + 7717)

    # Shadow black-point lift for a mapped scan that crushes deeper than its
    # procedural fallback. After grain (so it lands on the final texture detail)
    # and before finish_albedo (which re-lands the mean, keeping this a shadow-only
    # lift). No-op for every material that does not declare it.
    if spec.photo is not None and spec.photo.shadow_lift > 0.0:
        maps.albedo = lift_shadow_albedo(maps.albedo, spec.photo.shadow_lift, spec.photo.shadow_knee)

    maps.albedo, gain = finish_albedo(maps.albedo, spec.target_albedo)
    maps.roughness = np.maximum(maps.roughness, MIN_ROUGHNESS).astype(np.float32)

    normal = height_to_normal(maps.height, maps.world_size, maps.height_scale)
    occlusion = ambient_occlusion(maps.height, maps.world_size, maps.height_scale, spec.ao_strength)

    # AO floor lift, the albedo-free half of the same crush recovery. After the bake
    # so verify measures the shipped map; scales AO contrast by (1-ao_floor), kept
    # small enough that the crevice-vs-peak gate still holds. No-op unless declared.
    if spec.photo is not None and spec.photo.ao_floor > 0.0:
        occlusion = (spec.photo.ao_floor + (1.0 - spec.photo.ao_floor) * occlusion).astype(np.float32)

    folder = os.path.join(out_dir, spec.name)
    write_png(os.path.join(folder, spec.name + "_albedo.png"),
              to_u8(linear_to_srgb(maps.albedo)))
    write_png(os.path.join(folder, spec.name + "_normal.png"),
              to_u8(normal * 0.5 + 0.5))
    write_png(os.path.join(folder, spec.name + "_rough.png"),
              to_u8(maps.roughness))
    write_png(os.path.join(folder, spec.name + "_ao.png"),
              to_u8(occlusion))

    # URP's Lit shader reads metallic from _MetallicGlossMap.r and smoothness
    # from its alpha, so the two have to travel in one texture. The separate
    # roughness map above stays the authored, inspectable deliverable; this is
    # the packing the shader happens to want.
    mask = np.zeros((res, res, 4), dtype=np.float32)
    mask[..., 0] = maps.metallic
    mask[..., 1] = maps.metallic
    mask[..., 2] = maps.metallic
    mask[..., 3] = 1.0 - maps.roughness
    write_png(os.path.join(folder, spec.name + "_ms.png"), to_u8(mask))

    return verify(spec, maps, normal, occlusion, gain)


def report_table(reports: Sequence[TextureReport]) -> str:
    """The measurement table. Every number here is a thing that can be wrong."""
    header = ("%-26s %6s %6s %6s %7s %6s %6s %7s %6s %6s %6s %7s"
              % ("material", "px", "size", "px/m", "albedo", "rough", "seam", "relief",
                 "AOcon", "blob%", "hblob%", "grain%"))
    lines = [header, "-" * len(header)]

    for r in reports:
        lines.append(
            "%-26s %6d %5.1fm %6.0f %7.4f %6.2f %6.2f %5.1fmm %6.3f %5.1f%% %5.1f%% %6.1f%%%s"
            % (r.name, r.resolution, r.world_size, r.texel_px_per_m, r.albedo_mean_linear,
               r.roughness_mean, r.seam_ratio, r.normal_relief_mm,
               r.ao_contrast, r.blob_share * 100.0, r.height_blob_share * 100.0,
               r.grain_share * 100.0,
               "" if not r.failures else "  FAIL")
        )

    return "\n".join(lines)


def detail_payload(spec: MaterialSpec) -> Dict[str, object]:
    """The detail-normal half of a manifest entry, in the units the binder consumes.

    `size_metres` carries the same deliberate lie `world_size_metres` does and
    for the same reason — see `KIT_UV_UNITS_PER_METRE`. The engine divides its
    own `KitUvUnitsPerMetre = 1` by this field, so what has to be written here
    is UV units per detail tile, not metres. `authored_metres_per_tile` beside
    it is the truth. Correct both together or not at all.
    """
    if not spec.detail:
        return {}

    by_name = {d.name: d for d in DETAILS}
    if spec.detail not in by_name:
        raise AssertionError(
            "%s asks for detail %r, which is not in DETAILS" % (spec.name, spec.detail))

    detail = by_name[spec.detail]
    return {
        "name": detail.name,
        "normal": "Detail/" + detail.name + "_normal.png",
        "size_metres": round(detail.tile_metres * KIT_UV_UNITS_PER_METRE, 6),
        "authored_metres_per_tile": detail.tile_metres,
        "note": detail.note,
    }


def write_decal_manifest(path: str, entries: Sequence[Dict[str, object]],
                         glows: Sequence[Dict[str, object]] = ()) -> None:
    """A manifest of its own, for the same reason the material one exists.

    Kept separate from `Textures.manifest.json` because the two are consumed by
    different passes at different points in the pipeline — the material binder
    runs once per texture rebuild, and the decal placer runs once per map
    regeneration — and a single file would make each of them fail on the other's
    absence.
    """
    import json

    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump({"generated_by": "tools/textures/gen_textures.py",
                   "decals": list(entries), "glows": list(glows)},
                  handle, indent=2, ensure_ascii=False)
        handle.write("\n")


def write_manifest(path: str, reports: Sequence[TextureReport], res: int) -> None:
    """A manifest the Unity side reads, so slot binding is data rather than a second list.

    Two copies of "which texture goes on which kit slot" would drift the first
    time a material was renamed, and the failure would be a silently untextured
    wall. One copy, written by the generator, imported by the editor script.
    """
    import json

    by_name = {r.name: r for r in reports}
    payload = {
        "generated_by": "tools/textures/gen_textures.py",
        "resolution": res,
        "albedo_band_linear": [ALBEDO_MIN_LINEAR, ALBEDO_MAX_LINEAR],
        "materials": [
            {
                "name": spec.name,
                "slots": list(spec.slots),
                "note": spec.note,
                # Provenance for the Steam content survey (docs/ASSETS.md). Null
                # for the fully-procedural surfaces. Unity's JsonUtility ignores
                # a field it has no member for, so this rides along for free.
                "cc0_base": spec.photo.provenance if spec.photo is not None else None,
                # UV units per tile, which is what ApplyTiling divides into its
                # own (wrong) uv-per-metre constant. See KIT_UV_UNITS_PER_METRE:
                # the field is named for metres and consumed as UV units, and
                # emitting metres here rendered the whole game at double scale.
                "world_size_metres": round(
                    by_name[spec.name].world_size * KIT_UV_UNITS_PER_METRE, 6),
                # The truth, for anyone reading the manifest rather than the
                # binder. Unity's JsonUtility ignores fields it has no member
                # for, so this is free to carry.
                "authored_metres_per_tile": by_name[spec.name].world_size,
                "kit_uv_units_per_metre": KIT_UV_UNITS_PER_METRE,
                "albedo_mean_linear": round(by_name[spec.name].albedo_mean_linear, 4),
                "roughness_mean": round(by_name[spec.name].roughness_mean, 4),
                "relief_mm": round(by_name[spec.name].normal_relief_mm, 2),
                "texel_px_per_metre": round(by_name[spec.name].texel_px_per_m, 1),
                "detail": detail_payload(spec),
                "maps": {
                    "albedo": spec.name + "/" + spec.name + "_albedo.png",
                    "normal": spec.name + "/" + spec.name + "_normal.png",
                    "roughness": spec.name + "/" + spec.name + "_rough.png",
                    "occlusion": spec.name + "/" + spec.name + "_ao.png",
                    "metallic_smoothness": spec.name + "/" + spec.name + "_ms.png",
                },
            }
            for spec in MATERIALS
            if spec.name in by_name
        ],
    }

    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


DEFAULT_OUT = os.path.normpath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)),
                 "..", "..", "unity", "HorrorGame", "Assets", "Textures")
)


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default=DEFAULT_OUT, help="Output directory (Assets/Textures).")
    parser.add_argument("--res", type=int, default=DEFAULT_RESOLUTION, help="Texels per side.")
    parser.add_argument("--only", default=None, help="Substring filter on material name.")
    args = parser.parse_args(argv)

    if args.res & (args.res - 1):
        print("resolution must be a power of two — GPU block compression needs it", file=sys.stderr)
        return 2

    selected = [m for m in MATERIALS if args.only is None or args.only.lower() in m.name.lower()]
    if not selected:
        print("no material matched %r" % args.only, file=sys.stderr)
        return 2

    reports: List[TextureReport] = []
    for spec in selected:
        print("  generating %-26s ..." % spec.name, end="", flush=True)
        report = generate(spec, args.out, args.res)
        reports.append(report)
        print(" ok" if not report.failures else " FAIL")

    # Always written, even under --only. They are shared, they are ~0.3 MB each,
    # and a material rebuilt without its detail map is a material that silently
    # loses its near field.
    detail_lines: List[str] = []
    for detail in DETAILS:
        relative, tilt = generate_detail(detail, args.out)
        detail_lines.append("  %-20s %4d px  %.2f m tile  %5.0f px/m  mean tilt %.2f°"
                            % (detail.name, detail.resolution, detail.tile_metres,
                               detail.resolution / detail.tile_metres, tilt))
        if tilt < 0.25:
            print("FAIL  %s detail normal is effectively flat (%.3f°)" % (detail.name, tilt),
                  file=sys.stderr)
            return 1

    # Decals are placed, not tiled, so 512 is plenty: the largest of them spans
    # 2.6 m, which is 197 px/m — but a decal is a soft mark over a base material
    # that already carries the grain, so it never has to hold an edge on its own.
    decal_entries = [generate_decal(spec, args.out, 512) for spec in DECALS]
    glow_entries = [generate_glow(name, build, seed, top_open, args.out)
                    for name, build, seed, top_open, _ in GLOWS]
    write_decal_manifest(os.path.join(args.out, "Decals.manifest.json"), decal_entries,
                         glow_entries)

    print()
    print("detail normals")
    print("\n".join(detail_lines))
    print()
    print("light sprites")
    for entry in glow_entries:
        print("  %-14s mean %.3f  peak %.3f" % (entry["name"], entry["mean"], entry["peak"]))
    print()
    print("decals")
    for entry in decal_entries:
        print("  %-20s %.2f m  coverage %5.1f%%  mean alpha %.3f  rough %.2f"
              % (entry["name"], entry["size_metres"], float(entry["coverage"]) * 100.0,
                 entry["mean_alpha"], entry["roughness_mean"]))
    print()
    print(report_table(reports))
    print()

    failed = [r for r in reports if r.failures]
    for r in failed:
        for message in r.failures:
            print("FAIL  %-26s %s" % (r.name, message), file=sys.stderr)

    if failed:
        print("\n%d of %d materials failed." % (len(failed), len(reports)), file=sys.stderr)
        return 1

    if len(selected) == len(MATERIALS):
        write_manifest(os.path.join(args.out, "Textures.manifest.json"), reports, args.res)

    means = [r.albedo_mean_linear for r in reports]
    print("all %d materials pass: albedo means %.3f-%.3f (band %.2f-%.2f linear), "
          "worst seam ratio %.2f, worst normal error %.4f"
          % (len(reports), min(means), max(means), ALBEDO_MIN_LINEAR, ALBEDO_MAX_LINEAR,
             max(r.seam_ratio for r in reports), max(r.normal_unit_error for r in reports)))

    # The same contract line every asset generator in this repo ends on
    # (tools/ci/run_blender_generators.sh trusts the marker, not the exit
    # code). This generator is run through the audio venv rather than through
    # Blender, but whoever drives it deserves the same "it actually wrote the
    # set" signal, printed only after every verification above has passed.
    print("ASSET_REPORT textures materials=%d/%d res=%d albedo=%.3f-%.3f "
          "worst_seam=%.2f worst_ao_contrast=%.3f details=%d decals=%d glows=%d manifest=%s"
          % (len(reports), len(MATERIALS), args.res, min(means), max(means),
             max(r.seam_ratio for r in reports),
             min(r.ao_contrast for r in reports),
             len(DETAILS), len(decal_entries), len(glow_entries),
             "written" if len(selected) == len(MATERIALS) else "kept (--only run)"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
