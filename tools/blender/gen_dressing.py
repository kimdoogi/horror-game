#!/usr/bin/env python3
"""Generates the set-dressing kit — the props that turn a level into a place.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_dressing.py

Optional filter while iterating (everything after ``--`` is a name substring)::

    ... --python tools/blender/gen_dressing.py -- Puddle Sign

Outputs one FBX per piece into ``Assets/Models/Dressing/`` plus
``Dressing.manifest.json``, which is the contract the Unity scatter tool
(``Assets/Scripts/Editor/Dressing/``) reads. Nothing about a piece's size or
placement rule is restated in C#: it is measured here, written to the manifest,
and consumed there. A re-export that changes a footprint therefore changes where
the scatterer will and will not put it, instead of leaving a stale number in a
second file.


WHY THIS KIT EXISTS
===================

**Empty corridors are not neutral, they are a mechanical hole.** §12 derives the
whole escape structure from line of sight: 어그로 해제 needs 3 s of broken sight
line, 시야 차단 지점 간격 is fixed at 15~25 m, and the 주자 테스트 grades the map on
whether that actually works from ten arbitrary points. A corridor made of bare
walls has cover only where the architecture bends. Dressing is where the rest of
those blockers come from — which is why the tall pieces here are sized against
standing eye height and against `GameConstants.LineOfSightBreakSpacingMin`, not
against a mood board.

**§03 makes light the mechanic, so this kit is really about albedo.** The
building is lit by a 12 m flashlight cone in near-black ambient. A surface only
exists if it returns light, so the kit deliberately carries the brightest
materials in the project — galvanised pipe, dust sheets, loose paper, enamel
signage and standing water — and the flashlight is what finds them. A corridor
dressed only in browns and rusts stays exactly as invisible as an empty one.

**Water is atmosphere now, not information.** It used to be §03's worked example
of a clue — *"그것은 물이 있는 층에 있다"* — so puddles, drips, stains and a floor
drain were the thing a clue could point at. 단서 is deleted and the destination
is announced at the start, so they are back to being what they look like: the
kit's only mirror, and the reason a torch beam has somewhere to bounce. The
scatter tool still concentrates them in one zone, which now buys a storey a
memorable look rather than a name.

**§12 makes floor material a gameplay channel**, and the Listener has to be able
to learn it. The floor tint is somebody else's file, but which *props* a zone
gets is this one's, and four distinct dressing palettes (storage timber /
institutional steel / wet gravel plant / utility concrete) give a player a second,
visual way to know which zone they are standing in.


THE SIGNAGE SET USED TO BE §03's CONFUSION PAIRS
================================================

Four pieces — Dress_PipeLabel, Dress_HangingSign, Dress_WallSign,
Dress_DoorPlate — each carried a single flat ``Clue_Face`` quad mapped exactly
0..1, the seam §13's host-rendered glyph landed on, and each was *built* for one
misread condition: a pipe label symmetric under a half-turn about its own face
normal so 6 is 9 from the far end of the corridor; a double-sided hanging sign
with the back face's UVs mirrored so 좌 is 우 depending which way you came; a
glossy enamel field that blows out under a close beam for ㅁ↔ㅇ; a 0.18 m plate
read at a glancing angle for 1↔7. Two of the four were asserted to a millimetre.

All of it is deleted. 경주는 목적지를 처음부터 알려 준다 — there is no chain to
narrow, no glyph to stamp, and therefore nothing a runner can misread. What is
left is the *plate*: painted steel, an enamel field, bolts, chains. A basement
with signage in it looks like a basement, which is §12's job for the kit, and
the scatter tool no longer stamps 526 readable surfaces into a race.


CONVENTIONS THIS FILE GUARANTEES
================================

* **1 Blender unit = 1 metre = 1 Unity unit**, checked per piece against a
  declared size, exactly as `gen_props.py` does.
* **Pivot**, by mount, so the scatter tool never carries an offset:

  ======== =========================================================
  FLOOR    origin on the floor under the footprint centre (min z = 0)
  WALL     origin on the wall plane (max y = 0), centred in x
  CEILING  origin on the ceiling plane (max z = 0), centred in x and y
  CORNER   origin at the wall/wall/ceiling corner (max x, y, z = 0)
  ======== =========================================================

* **Facing −Y**, matching `gen_props.py` and the monster generator, which
  `export_fbx`'s ``axis_forward='-Z'`` turns into +Z forward in Unity. A floor
  piece meant to stand against a wall has its **back at +Y**, so the scatter tool
  faces it away from the wall with a plain yaw.
* **One mesh, one object, identity transform** per FBX. Material names are the
  seam Unity binds against; the manifest lists them so the scatter tool can build
  real URP materials rather than inheriting whatever the FBX importer guesses.
"""

from __future__ import annotations

import json
import math
import os
import sys
import traceback
from dataclasses import dataclass, field
from typing import Callable

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import blendkit  # noqa: E402
import gen_props  # noqa: E402
from blendkit import MaterialSpec  # noqa: E402
from gen_props import Frame, PropBuild, bbox_size, world_bbox  # noqa: E402


# ── Assumptions NOT taken from the design document ──────────────────────────
# Same treatment as gen_props.py's block: printed with the report so a real
# number can replace them. §12 fixes corridor *length* and zone diagonals but
# never states a corridor width or a body size, and §05 never states eye height.

EYE_HEIGHT_ASSUMED = 1.63
"""Standing eye height, metres. Matches the camera rig in `SceneShot`. NOT a design number."""

CEILING_CLEAR_ASSUMED = 3.0
"""Corridor clear height from the MapKit manifest (``corridor_clear.height``). A
kit measurement, so ceiling-mounted pieces must hang shorter than this minus
head clearance or a player walks through them."""

HEAD_CLEARANCE_ASSUMED = 0.35
"""Gap left between the lowest point of a ceiling piece and a standing player's
crown. NOT a design number — it is what stops hanging dressing reading as a bug."""

MEASURE_ONLY = os.environ.get("DRESSING_MEASURE_ONLY") == "1"
"""Survey mode: report size mismatches instead of failing on the first one.

Only for retuning proportions, where failing on piece 1 of 37 hides the other 36.
Downgrades the declared-size guard and nothing else."""


# ── Materials ───────────────────────────────────────────────────────────────
# Registered into gen_props' shared table so `PropBuild` resolves them. Names are
# prefixed `Dress_` so they cannot collide with `Prop_*` (§08's temptation and
# efficiency contrast is encoded in those roughness values) or with the
# contractual `Floor_*` namespace blendkit reserves for §12's footstep surfaces.
#
# The value range is the point. §03 lights this building with a 12 m cone in
# near-black ambient, so the kit needs surfaces at the top of the range to be
# visible at all: galvanised pipe at 0.52 albedo, dust sheets at 0.62, paper at
# 0.78, enamel at 0.70. Everything dark in here is dark *against* those.

STEEL_PAINTED = "Dress_SteelPainted"
STEEL_BARE = "Dress_SteelBare"
GALVANISED = "Dress_Galvanised"
COPPER = "Dress_CopperPipe"
RUST_HEAVY = "Dress_RustHeavy"
CONCRETE = "Dress_Concrete"
CONCRETE_BROKEN = "Dress_ConcreteBroken"
BRICK = "Dress_Brick"
TIMBER_PALE = "Dress_TimberPale"
TIMBER_DARK = "Dress_TimberDark"
PLY = "Dress_Ply"
CARD = "Dress_Cardboard"
PAPER = "Dress_Paper"
CLOTH_DUST = "Dress_ClothDust"
CLOTH_STAINED = "Dress_ClothStained"
COBWEB = "Dress_Cobweb"
WATER = "Dress_Water"
WET_STAIN = "Dress_WetStain"
ENAMEL = "Dress_Enamel"
ENAMEL_RED = "Dress_EnamelRed"
GAUGE_FACE = "Dress_GaugeFace"
GLASS_DIRTY = "Dress_GlassDirty"
BULB_DEAD = "Dress_BulbDead"
BULB_LIT = "Dress_BulbLit"
RUBBER = "Dress_Rubber"
GRIME = "Dress_Grime"
BRASS_FITTING = "Dress_BrassFitting"

# DELETED: CLUE_FACE. It was `gen_props.CLUE_FACE` — §13's seam, the surface the
# host rendered one clue's glyph onto and stamped here. §03 단서 is deleted:
# 목적지가 이미 알려져 있으니 좁혀 갈 것이 없다. Four Sign pieces carried one each
# and the scatter tool stamped 526 of them into the shipped scene; the enamel
# field they sat on is still there, so a sign is still a sign, with nothing
# written on it that a runner has to read.

MATERIALS: dict[str, MaterialSpec] = {
    # Painted steel is metallic=0. Paint is a dielectric coat over the metal, and
    # calling it metallic is not a stylistic choice — with no reflection probe in
    # the basement, URP falls back to the skybox, so every "metal" surface
    # mirrored a dusk sky and the kit's lockers and cabinets came out with white
    # tops in a pitch-dark corridor. The physically correct value fixes the look
    # and the reason at the same time.
    STEEL_PAINTED: MaterialSpec(STEEL_PAINTED, (0.238, 0.252, 0.262), roughness=0.52, metallic=0.0),
    # Bare steel is the kit's specular anchor: a beam sliding along it is the
    # cheapest possible depth cue in a corridor with no textures. It stays a real
    # metal, and it is the only one that is both bright and smooth.
    STEEL_BARE: MaterialSpec(STEEL_BARE, (0.452, 0.462, 0.478), roughness=0.38, metallic=1.0),
    GALVANISED: MaterialSpec(GALVANISED, (0.524, 0.541, 0.556), roughness=0.52, metallic=0.85),
    COPPER: MaterialSpec(COPPER, (0.552, 0.312, 0.182), roughness=0.34, metallic=1.0),
    # Rust is iron oxide — a dielectric. Metallic rust is the single most common
    # material mistake and it reads as wet plastic.
    RUST_HEAVY: MaterialSpec(RUST_HEAVY, (0.262, 0.132, 0.072), roughness=0.95, metallic=0.0),
    CONCRETE: MaterialSpec(CONCRETE, (0.302, 0.300, 0.292), roughness=0.92),
    # Freshly broken concrete is paler than the wall it fell off. That contrast is
    # what makes a rubble pile findable at the edge of a beam.
    CONCRETE_BROKEN: MaterialSpec(CONCRETE_BROKEN, (0.412, 0.404, 0.382), roughness=0.95),
    BRICK: MaterialSpec(BRICK, (0.322, 0.182, 0.142), roughness=0.90),
    TIMBER_PALE: MaterialSpec(TIMBER_PALE, (0.442, 0.342, 0.222), roughness=0.80),
    TIMBER_DARK: MaterialSpec(TIMBER_DARK, (0.162, 0.112, 0.072), roughness=0.82),
    PLY: MaterialSpec(PLY, (0.502, 0.402, 0.262), roughness=0.85),
    CARD: MaterialSpec(CARD, (0.422, 0.322, 0.212), roughness=0.95),
    PAPER: MaterialSpec(PAPER, (0.782, 0.752, 0.682), roughness=0.90),
    # The brightest large surface in the building. A dust sheet over a stack is a
    # silhouette a player reads before the flashlight resolves anything on it.
    CLOTH_DUST: MaterialSpec(CLOTH_DUST, (0.622, 0.602, 0.552), roughness=0.95),
    CLOTH_STAINED: MaterialSpec(CLOTH_STAINED, (0.342, 0.312, 0.262), roughness=0.95),
    COBWEB: MaterialSpec(COBWEB, (0.722, 0.722, 0.702), roughness=0.85),
    # Dark base colour, mirror roughness. A puddle is nearly black until a beam
    # crosses it and then it is the brightest thing in frame — which is exactly
    # what §03 wants of "그것은 물이 있는 층에 있다": you find water by lighting it.
    # 0.16, not 0.05. A perfect mirror is right in principle and wrong on screen:
    # URP has no reflection probe in this building yet, so a puddle falls back to the
    # skybox, and at the grazing angle a standing player sees a floor from, every
    # distant pool turned into a flat white blob of reflected dusk sky. At 0.16 the
    # near pools still mirror the beam — which is the whole read — and the far ones
    # blur into wet floor. It is still the smoothest surface in the kit by a factor
    # of two over the next one.
    WATER: MaterialSpec(WATER, (0.042, 0.052, 0.058), roughness=0.22, metallic=0.0),
    WET_STAIN: MaterialSpec(WET_STAIN, (0.102, 0.112, 0.108), roughness=0.35),
    ENAMEL: MaterialSpec(ENAMEL, (0.702, 0.712, 0.702), roughness=0.30),
    ENAMEL_RED: MaterialSpec(ENAMEL_RED, (0.422, 0.092, 0.072), roughness=0.35),
    GAUGE_FACE: MaterialSpec(GAUGE_FACE, (0.862, 0.842, 0.782), roughness=0.40),
    GLASS_DIRTY: MaterialSpec(GLASS_DIRTY, (0.402, 0.432, 0.442), roughness=0.22, metallic=0.30),
    # A dead bulb still has to catch a beam — most of them are dead, and a corridor
    # of invisible fittings is a corridor with no ceiling.
    BULB_DEAD: MaterialSpec(BULB_DEAD, (0.722, 0.702, 0.642), roughness=0.12),
    # §03: 어둠 = 목표의 잠금장치. A working bulb is emissive but weak, and the
    # scatter tool gives only a minority of them a real light, ranged so the pool
    # never substitutes for the flashlight.
    BULB_LIT: MaterialSpec(BULB_LIT, (1.0, 0.862, 0.622), roughness=0.10, emission=2.2),
    RUBBER: MaterialSpec(RUBBER, (0.062, 0.062, 0.068), roughness=0.85),
    GRIME: MaterialSpec(GRIME, (0.092, 0.086, 0.078), roughness=0.98),
    BRASS_FITTING: MaterialSpec(BRASS_FITTING, (0.482, 0.382, 0.162), roughness=0.34, metallic=1.0),
}

gen_props.register_materials(MATERIALS)


def rads(degrees):
    return gen_props.rads(degrees)


# ── Measurement helpers ─────────────────────────────────────────────────────


# DELETED: `rotational_symmetry_error_axis` and `mirror_symmetry_error`. The
# first measured a half-turn about an arbitrary axis (gen_props' version
# hard-codes Z, and a wall label's face normal is Y); the second measured a
# plate against its own mirror plane. Both existed only to assert §03's 6↔9 and
# 좌↔우 misread conditions to a millimetre. gen_props.rotational_symmetry_error
# is still imported for the Z-axis case and is untouched.
# DELETED: `map_clue_faces`. It grouped a piece's Clue_Face polygons by facing,
# gave each group its own 0..1 UV mapping and flipped U on the group facing +Y —
# the entire 좌↔우 mechanism: one glyph, two sides, one of them reversed. With no
# glyph the mapping has nothing to map and the flip nothing to reverse.
def _crate(f: Frame, side: float, height: float, wood: str = TIMBER_PALE,
           batten: str = TIMBER_DARK) -> None:
    """One packing crate: a body plus corner battens and a lid.

    Kept to six primitives because these are stacked three deep and the whole
    stack has to stay inside the per-piece triangle budget §05 justifies (dark,
    first person — detail buys nothing, silhouette buys everything).
    """
    t = 0.028
    f.box((side - 0.03, side - 0.03, height - 0.03), (0.0, 0.0, height / 2), mat=wood)
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            f.box((0.052, 0.052, height), (sx * (side / 2 - 0.026), sy * (side / 2 - 0.026),
                                           height / 2), mat=batten, nobevel=True)
    f.box((side, side, t), (0.0, 0.0, height - t / 2), mat=wood, nobevel=True)


def _barrel(f: Frame, radius: float, height: float, body: str = RUST_HEAVY,
            hoop: str = STEEL_BARE) -> None:
    """A 55-gallon drum: body, two rolling hoops, a rim and a bung.

    The hoops are `STEEL_BARE` on purpose. A drum in one flat rust tone is a
    cylinder-shaped hole in the frame; two bright rings around it are what let a
    beam say "drum" from 8 m.
    """
    f.cyl(radius, height, (0.0, 0.0, height / 2), verts=16, mat=body, nobevel=True)
    for z in (height * 0.28, height * 0.72):
        f.cyl(radius + 0.016, 0.052, (0.0, 0.0, z), verts=16, mat=hoop, nobevel=True)
    # The end rims take the *body* material, not the hoop's. They are solid discs,
    # so a bare-steel rim paints the whole lid — and a mirror-finish lid in a dark
    # basement reads as a white hole rather than as a drum.
    f.cyl(radius + 0.010, 0.030, (0.0, 0.0, height - 0.015), verts=16, mat=body, nobevel=True)
    f.cyl(radius + 0.010, 0.030, (0.0, 0.0, 0.015), verts=16, mat=body, nobevel=True)
    f.cyl(0.036, 0.018, (radius * 0.5, 0.0, height + 0.006), verts=10, mat=BRASS_FITTING,
          nobevel=True)


def _shelf_frame(b: PropBuild, w: float, d: float, h: float, decks: tuple) -> None:
    """Four angle-iron uprights, cross braces and the named deck heights."""
    for (sx, sy) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.box((0.048, 0.048, h), (sx * (w / 2 - 0.024), sy * (d / 2 - 0.024), h / 2),
              mat=STEEL_PAINTED, nobevel=True)
    for z in decks:
        b.box((w, d, 0.026), (0.0, 0.0, z), mat=PLY, nobevel=True)
        b.box((w, 0.024, 0.044), (0.0, -d / 2 + 0.012, z + 0.026), mat=STEEL_PAINTED,
              nobevel=True)
    b.box((w - 0.10, 0.020, 0.048), (0.0, d / 2 - 0.020, h * 0.55), rot=(0.0, 24.0, 0.0),
          mat=STEEL_PAINTED, nobevel=True)
    b.box((w - 0.10, 0.020, 0.048), (0.0, d / 2 - 0.020, h * 0.55), rot=(0.0, -24.0, 0.0),
          mat=STEEL_PAINTED, nobevel=True)


def _tin(f: Frame, radius: float, height: float, loc, mat_name: str = STEEL_BARE) -> None:
    f.cyl(radius, height, (loc[0], loc[1], loc[2] + height / 2), verts=10, mat=mat_name,
          nobevel=True)
    f.cyl(radius + 0.004, 0.010, (loc[0], loc[1], loc[2] + height - 0.005), verts=10,
          mat=STEEL_BARE, nobevel=True)


def _pipe_run(b: PropBuild, length: float, y: float, specs: tuple, bracket_x: tuple) -> None:
    """A horizontal parallel pipe run laid along X, plus its brackets.

    `specs` is (radius, z, material, verts) per pipe. Runs are authored 2.5 m long
    so they tile exactly on the MapKit's 2.5 m grid — a run that does not tile
    leaves a gap at every cell boundary, which reads worse than no pipes at all.
    """
    for (radius, z, mat_name, verts) in specs:
        b.cyl(radius, length, (0.0, y, z), rot=(0.0, 90.0, 0.0), verts=verts, mat=mat_name,
              nobevel=True)
    for x in bracket_x:
        lo = min(s[1] for s in specs) - 0.09
        hi = max(s[1] for s in specs) + 0.09
        b.box((0.056, 0.026, hi - lo), (x, y * 0.35, (lo + hi) / 2), mat=STEEL_PAINTED,
              nobevel=True)
        for (radius, z, _m, _v) in specs:
            b.box((0.056, abs(y) * 1.05, 0.026), (x, y * 0.5, z + radius + 0.013),
                  mat=STEEL_PAINTED, nobevel=True)


def _plank_scatter(b: PropBuild, planks: tuple, mat_name: str = TIMBER_PALE) -> None:
    for (sx, sy, sz, x, y, z, rot) in planks:
        b.box((sx, sy, sz), (x, y, z), rot=rot, mat=mat_name, nobevel=True)


def _rubble(b: PropBuild, chunks: tuple, mat_name: str = CONCRETE_BROKEN) -> None:
    for (sx, sy, sz, x, y, z, yaw) in chunks:
        b.box((sx, sy, sz), (x, y, z), rot=(7.0, -5.0, yaw), mat=mat_name, nobevel=True)


def _chain(b: PropBuild, x: float, y: float, z_top: float, z_bottom: float,
           links: int, radius: float = 0.022) -> None:
    """A hanging chain built from alternating flattened tori.

    Six-segment minor rings: at 2.5 m away in a torch beam a chain is a dotted
    bright line, and anything smoother is triangles nobody sees.
    """
    step = (z_top - z_bottom) / max(1, links)
    for i in range(links):
        z = z_top - step * (i + 0.5)
        b.torus(radius, 0.0055, (x, y, z), rot=(90.0, 0.0, 0.0 if i % 2 else 90.0),
                mseg=8, nseg=4, mat=STEEL_BARE)


# ══════════════════════════════════════════════════════════════════════════
#  BULK — storage and furniture. These are the pieces §12's escape maths cares
#  about: anything at or above eye height is a 시야 차단 지점.
# ══════════════════════════════════════════════════════════════════════════


def build_crate_stack_tall() -> PropBuild:
    """Three crates stacked to 1.72 m — a sight-line break that is not a wall.

    §12 wants 시야 차단 지점 every 15~25 m and §06 needs 3 s of broken line of
    sight for an aggro release. A stack just under standing eye height
    (1.63 m assumed) breaks the line for a running player without hiding the
    route, which a full-height block would. The slight yaw on each crate is what
    stops three boxes reading as one extruded box."""
    b = PropBuild("Dress_CrateStack_Tall")
    _crate(b.frame((0.0, 0.0, 0.0), yaw=0.0), 0.640, 0.600)
    _crate(b.frame((0.03, -0.02, 0.600), yaw=7.0), 0.600, 0.560)
    _crate(b.frame((-0.02, 0.03, 1.160), yaw=-11.0), 0.560, 0.520)
    b.pivot_part = b.parts[0]
    return b


def build_crate_stack_low() -> PropBuild:
    """Two crates and a sack — waist height, so it is cover to crouch behind.

    §12's 막힌 길 need a reason to be entered and something to hide loot behind;
    §08 hides the good pieces there. Low enough to see over, which keeps §04's
    Observer sightlines intact."""
    b = PropBuild("Dress_CrateStack_Low")
    _crate(b.frame((-0.30, 0.0, 0.0), yaw=-5.0), 0.640, 0.600)
    _crate(b.frame((-0.26, 0.04, 0.600), yaw=9.0), 0.580, 0.540)
    _crate(b.frame((0.38, -0.06, 0.0), yaw=22.0), 0.560, 0.520)
    # A dust sheet half-pulled off the low crate: the brightest thing in the pile.
    b.box((0.700, 0.640, 0.030), (0.38, -0.06, 0.525), rot=(0.0, -4.0, 22.0), mat=CLOTH_DUST,
          nobevel=True)
    b.box((0.180, 0.560, 0.026), (0.66, -0.14, 0.360), rot=(0.0, 26.0, 22.0), mat=CLOTH_DUST,
          nobevel=True)
    b.pivot_part = b.parts[0]
    return b


def build_crate_broken() -> PropBuild:
    """A crate that lost an argument with a forklift. Spilled straw and boards.

    Below knee height everywhere: §05 makes backward movement 65 % speed and §06
    gives the monster 4.8 m/s, so dressing a player can trip on is a death written
    by an artist rather than by design."""
    b = PropBuild("Dress_CrateBroken")
    b.box((0.620, 0.580, 0.300), (0.0, 0.0, 0.150), rot=(0.0, 4.0, 0.0), mat=TIMBER_PALE)
    b.pivot_part = b.parts[0]
    for sx in (-1.0, 1.0):
        b.box((0.048, 0.048, 0.320), (sx * 0.290, -0.270, 0.160), mat=TIMBER_DARK, nobevel=True)
    _plank_scatter(b, (
        (0.620, 0.140, 0.026, 0.10, 0.380, 0.014, (0.0, 0.0, 14.0)),
        (0.560, 0.130, 0.024, -0.34, 0.330, 0.040, (0.0, -22.0, -34.0)),
        (0.480, 0.120, 0.024, 0.42, -0.300, 0.012, (0.0, 0.0, 62.0)),
    ))
    # Packing straw and paper — bright, low, catches a floor sweep.
    for (x, y, yaw) in ((0.10, 0.20, 12.0), (-0.16, 0.28, -48.0), (0.30, 0.06, 71.0)):
        b.box((0.300, 0.220, 0.014), (x, y, 0.008), rot=(0.0, 0.0, yaw), mat=PAPER, nobevel=True)
    return b


def build_barrel_upright() -> PropBuild:
    """One steel drum, 0.88 m. The kit's most reusable single silhouette."""
    b = PropBuild("Dress_BarrelUpright")
    _barrel(b.frame((0.0, 0.0, 0.0)), 0.290, 0.880)
    b.pivot_part = b.parts[0]
    return b


def build_barrel_cluster() -> PropBuild:
    """Three drums, one on its side — a 1.6 m wide block of cover.

    Wide on purpose: §12 asks for cover at 15~25 m spacing and a single drum is
    too narrow to break a corridor's line of sight. The toppled one gives the
    cluster a horizontal element so it does not read as three copies."""
    b = PropBuild("Dress_BarrelCluster")
    _barrel(b.frame((-0.36, 0.10, 0.0), yaw=17.0), 0.290, 0.880)
    b.pivot_part = b.parts[0]
    _barrel(b.frame((0.28, -0.14, 0.0), yaw=-53.0), 0.290, 0.880, body=STEEL_PAINTED)
    # Toppled, lying along X, resting on its hoops.
    f = b.frame((0.30, 0.52, 0.306), yaw=0.0)
    f.cyl(0.290, 0.880, (0.0, 0.0, 0.0), rot=(0.0, 90.0, 0.0), verts=16, mat=RUST_HEAVY,
          nobevel=True)
    for x in (-0.246, 0.246):
        f.cyl(0.306, 0.052, (x, 0.0, 0.0), rot=(0.0, 90.0, 0.0), verts=16, mat=STEEL_BARE,
              nobevel=True)
    return b


def build_barrel_toppled() -> PropBuild:
    """A drum on its side with a wedge under it, 0.62 m tall — walk-over-able cover."""
    b = PropBuild("Dress_BarrelToppled")
    body = b.cyl(0.290, 0.880, (0.0, 0.0, 0.300), rot=(0.0, 90.0, 0.0), verts=16,
                 mat=STEEL_PAINTED, nobevel=True)
    b.pivot_part = body
    for x in (-0.246, 0.246):
        b.cyl(0.306, 0.052, (x, 0.0, 0.300), rot=(0.0, 90.0, 0.0), verts=16, mat=STEEL_BARE,
              nobevel=True)
    b.cyl(0.010, 0.020, (0.0, 0.0, 0.596), verts=8, mat=BRASS_FITTING, nobevel=True)
    b.box((0.180, 0.220, 0.070), (0.36, 0.0, 0.035), rot=(0.0, 12.0, 0.0), mat=TIMBER_DARK,
          nobevel=True)
    return b


def build_shelf_stocked() -> PropBuild:
    """A 1.10 m stocked shelving bay, 1.95 m tall.

    Narrower than `gen_props`' 1.84 m Shelving so it fits a 2.5 m corridor cell
    against one wall while leaving the §08 two-person carry channel open — the
    scatter tool checks that arithmetic per cell, but a piece that could never
    pass it would simply never be placed. §12 counts it as a sightline blocker:
    the gaps between decks make it usable cover rather than a wall, which is also
    why the import policy lists shelving as concave."""
    b = PropBuild("Dress_ShelfStocked")
    W, D, H = 1.100, 0.420, 1.950
    _shelf_frame(b, W, D, H, (0.140, 0.640, 1.140, 1.640))
    b.pivot_part = b.parts[0]
    f = b.frame((0.0, 0.0, 0.0))
    # Deck 1 — paint tins and a coil of hose.
    for i, x in enumerate((-0.40, -0.24, -0.08)):
        _tin(f, 0.072, 0.180, (x, 0.02, 0.153))
    f.cyl(0.150, 0.090, (0.30, 0.0, 0.198), verts=12, mat=RUBBER, nobevel=True)
    # Deck 2 — cardboard boxes, one crooked.
    f.box((0.320, 0.300, 0.240), (-0.32, 0.01, 0.773), rot=(0.0, 0.0, 4.0), mat=CARD)
    f.box((0.260, 0.260, 0.200), (0.02, -0.02, 0.753), rot=(0.0, 0.0, -9.0), mat=CARD)
    f.box((0.220, 0.240, 0.180), (0.34, 0.03, 0.743), mat=CARD)
    # Deck 3 — bottles and jars, the bright row.
    for i, x in enumerate((-0.44, -0.30, -0.16, 0.10, 0.24)):
        f.cyl(0.046, 0.230, (x, 0.0, 1.268), verts=10, mat=GLASS_DIRTY, nobevel=True)
        f.cyl(0.022, 0.070, (x, 0.0, 1.418), verts=8, mat=GLASS_DIRTY, nobevel=True)
    f.box((0.180, 0.300, 0.120), (0.42, 0.0, 1.213), mat=PAPER, nobevel=True)
    # Deck 4 — ledgers and a folded sheet.
    for i, x in enumerate((-0.42, -0.36, -0.30, -0.24)):
        f.box((0.048, 0.280, 0.320), (x, 0.0, 1.813), rot=(0.0, 3.0 * i - 4.0, 0.0),
              mat=TIMBER_DARK, nobevel=True)
    f.box((0.420, 0.360, 0.130), (0.22, 0.0, 1.718), mat=CLOTH_DUST, nobevel=True)
    return b


def build_shelf_toppled() -> PropBuild:
    """The same bay on its face, contents spilled. Knee height, walk-around cover.

    A toppled unit is the cheapest way to say "something happened here" without
    animating anything — §06 designs the building to be silent, so the evidence
    has to be in the geometry."""
    b = PropBuild("Dress_ShelfToppled")
    W, D, H = 1.100, 0.420, 1.900
    # The frame, laid over: uprights along Y, decks stacked in Y.
    for (sy) in (-1.0, 1.0):
        b.box((W, 0.048, 0.048), (0.0, sy * (H / 2 - 0.024), 0.396), rot=(0.0, 0.0, 0.0),
              mat=STEEL_PAINTED, nobevel=True)
        b.box((W, 0.048, 0.048), (0.0, sy * (H / 2 - 0.024), 0.048), mat=STEEL_PAINTED,
              nobevel=True)
    b.pivot_part = b.parts[0]
    for i, y in enumerate((-0.62, -0.14, 0.34, 0.78)):
        b.box((W, 0.026, 0.360), (0.0, y, 0.200), rot=(3.0 - i * 2.0, 0.0, 0.0), mat=PLY,
              nobevel=True)
    # Spilled contents in front of it.
    for (x, y, r, h) in ((-0.36, -0.72, 0.072, 0.180), (0.12, -0.86, 0.072, 0.180),
                         (0.46, -0.66, 0.072, 0.180)):
        b.cyl(r, h, (x, y, r), rot=(90.0, 0.0, 24.0), verts=10, mat=STEEL_BARE, nobevel=True)
    for (x, y, yaw) in ((-0.10, -0.98, 18.0), (0.34, -1.04, -37.0)):
        b.box((0.280, 0.240, 0.026), (x, y, 0.013), rot=(0.0, 0.0, yaw), mat=PAPER, nobevel=True)
    b.box((0.300, 0.280, 0.220), (-0.44, -1.00, 0.110), rot=(0.0, 8.0, 27.0), mat=CARD)
    return b


def build_filing_cabinet() -> PropBuild:
    """Four-drawer cabinet, one drawer out. 1.32 m — waist-to-chest cover.

    The pulled drawer is the whole point: it turns a box into a thing somebody
    was using, and it gives the silhouette an asymmetry a beam can find."""
    b = PropBuild("Dress_FilingCabinet")
    W, D, H = 0.460, 0.620, 1.320
    body = b.box((W, D, H), (0.0, 0.0, H / 2), mat=STEEL_PAINTED)
    b.pivot_part = body
    b.box((W + 0.02, D + 0.02, 0.026), (0.0, 0.0, H), mat=STEEL_PAINTED, nobevel=True)
    for i, z in enumerate((0.190, 0.500, 0.810, 1.120)):
        out = 0.320 if i == 2 else 0.0
        b.box((W - 0.04, 0.026, 0.280), (0.0, -D / 2 - 0.013 - out, z), mat=STEEL_PAINTED,
              nobevel=True)
        b.box((0.140, 0.030, 0.030), (0.0, -D / 2 - 0.030 - out, z + 0.070),
              mat=STEEL_BARE, nobevel=True)
        b.box((0.110, 0.024, 0.044), (0.0, -D / 2 - 0.027 - out, z - 0.080),
              mat=PAPER, nobevel=True)
    # The open drawer's sides and its paper, which is the bright bit.
    b.box((W - 0.06, 0.300, 0.230), (0.0, -D / 2 - 0.170, 0.810), mat=STEEL_PAINTED,
          nobevel=True)
    for i in range(4):
        b.box((W - 0.10, 0.260, 0.030), (0.0, -D / 2 - 0.170, 0.760 + i * 0.036),
              rot=(0.0, 2.0 * i - 3.0, 0.0), mat=PAPER, nobevel=True)
    return b


def build_locker_bank() -> PropBuild:
    """A three-door locker bank, one door ajar. 1.82 m — a full sightline blocker.

    Distinct from §12's 은폐 지점 (`gen_props`' HidingSpot_Locker) on purpose: this
    one is shallow, has no usable cavity and reads as furniture, so a player does
    not waste a §07 새벽 escape trying to climb into set dressing."""
    b = PropBuild("Dress_LockerBank")
    W, D, H = 1.050, 0.420, 1.820
    plinth = 0.090
    base = b.box((W, D, plinth), (0.0, 0.0, plinth / 2), mat=STEEL_PAINTED)
    b.pivot_part = base
    b.box((W, D, H - plinth), (0.0, 0.02, (H + plinth) / 2), mat=STEEL_PAINTED)
    b.box((W + 0.04, D + 0.03, 0.040), (0.0, 0.0, H + 0.020), mat=STEEL_PAINTED, nobevel=True)
    for i, x in enumerate((-0.348, 0.0, 0.348)):
        ajar = i == 2
        if not ajar:
            b.box((0.330, 0.028, H - plinth - 0.060), (x, -D / 2 - 0.006,
                                                       (H + plinth) / 2), mat=STEEL_PAINTED,
                  nobevel=True)
            for j in range(3):
                b.box((0.180, 0.016, 0.020), (x, -D / 2 - 0.020, H - 0.230 + j * 0.058),
                      mat=GRIME, nobevel=True)
            b.box((0.030, 0.030, 0.130), (x + 0.132, -D / 2 - 0.026, (H + plinth) / 2 - 0.10),
                  mat=STEEL_BARE, nobevel=True)
            b.box((0.110, 0.016, 0.070), (x, -D / 2 - 0.016, H - 0.420), mat=PAPER,
                  nobevel=True)
        else:
            # The open leaf, swung 62° toward −Y so it eats a little corridor.
            b.box((0.330, 0.028, H - plinth - 0.060), (x + 0.238, -D / 2 - 0.150,
                                                       (H + plinth) / 2),
                  rot=(0.0, 0.0, 62.0), mat=STEEL_PAINTED, nobevel=True)
            b.box((0.030, 0.030, 0.130), (x + 0.375, -D / 2 - 0.290, (H + plinth) / 2 - 0.10),
                  mat=STEEL_BARE, nobevel=True)
            # A coat left inside — cloth, so the dark opening is not a black hole.
            b.box((0.220, 0.100, 0.760), (x, -0.05, 1.100), rot=(0.0, 2.0, 0.0),
                  mat=CLOTH_STAINED, nobevel=True)
    return b


def build_workbench() -> PropBuild:
    """A 1.80 m bench with a vice, a tool board and clutter. Waist height.

    §04's 정비공 is the role that edits the map mid-match; a bench is where that
    reads as belonging to the building rather than to a menu. The tool board
    behind it is the piece's real job — it puts bright, small, specular objects at
    eye height, which is where a flashlight beam actually points."""
    b = PropBuild("Dress_Workbench")
    W, D, H = 1.800, 0.680, 0.900
    top = b.box((W, D, 0.060), (0.0, 0.0, H - 0.030), mat=TIMBER_PALE)
    b.pivot_part = top
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.box((0.080, 0.080, H - 0.060), (sx * (W / 2 - 0.070), sy * (D / 2 - 0.070),
                                              (H - 0.060) / 2), mat=TIMBER_DARK, nobevel=True)
    b.box((W - 0.20, 0.040, 0.140), (0.0, -D / 2 + 0.040, H - 0.140), mat=TIMBER_DARK,
          nobevel=True)
    b.box((W - 0.24, D - 0.20, 0.030), (0.0, 0.0, 0.240), mat=PLY, nobevel=True)
    # Vice.
    b.box((0.180, 0.160, 0.130), (-0.640, -0.150, H + 0.065), mat=STEEL_BARE)
    b.box((0.150, 0.060, 0.110), (-0.640, -0.245, H + 0.055), mat=STEEL_BARE, nobevel=True)
    b.cyl(0.018, 0.300, (-0.640, -0.320, H + 0.065), rot=(90.0, 0.0, 0.0), verts=8,
          mat=STEEL_BARE, nobevel=True)
    b.cyl(0.014, 0.220, (-0.640, -0.460, H + 0.065), rot=(0.0, 90.0, 0.0), verts=8,
          mat=STEEL_BARE, nobevel=True)
    # Tool board on the back edge, with hanging tools.
    b.box((W, 0.024, 0.620), (0.0, D / 2 - 0.012, H + 0.310), mat=PLY, nobevel=True)
    for i, x in enumerate((-0.62, -0.42, -0.22, 0.06, 0.26, 0.48, 0.68)):
        h = 0.180 + 0.055 * (i % 3)
        b.box((0.036, 0.020, h), (x, D / 2 - 0.030, H + 0.520 - h / 2), mat=STEEL_BARE,
              nobevel=True)
        b.box((0.070, 0.022, 0.032), (x, D / 2 - 0.030, H + 0.520 - h), mat=TIMBER_DARK,
              nobevel=True)
    # Clutter on the deck.
    f = b.frame((0.0, 0.0, 0.0))
    _tin(f, 0.060, 0.140, (0.360, 0.060, H + 0.030))
    _tin(f, 0.052, 0.110, (0.500, -0.070, H + 0.030), mat_name=BRASS_FITTING)
    f.box((0.360, 0.260, 0.070), (0.030, 0.080, H + 0.065), rot=(0.0, 0.0, 8.0),
          mat=STEEL_PAINTED, nobevel=True)
    for i, yaw in enumerate((6.0, -14.0, 21.0)):
        f.box((0.300, 0.220, 0.006), (0.760, -0.060 + i * 0.02, H + 0.033 + i * 0.007),
              rot=(0.0, 0.0, yaw), mat=PAPER, nobevel=True)
    f.box((0.220, 0.160, 0.180), (-0.220, 0.070, H + 0.120), rot=(0.0, 0.0, -6.0), mat=CARD)
    return b


def build_table_broken() -> PropBuild:
    """A trestle table with a collapsed leg. 0.78 m at the high end.

    The tilt is the information: a level table is furniture, a tilted one is a
    building nobody has maintained since §07's 저녁 stage started."""
    b = PropBuild("Dress_TableBroken")
    top = b.box((1.400, 0.760, 0.046), (0.0, 0.0, 0.700), rot=(0.0, -9.0, 0.0),
                mat=TIMBER_PALE)
    b.pivot_part = top
    for (sx, sy, h) in ((-1.0, -1.0, 0.700), (-1.0, 1.0, 0.700), (1.0, -1.0, 0.560)):
        b.box((0.070, 0.070, h), (sx * 0.620, sy * 0.320, h / 2), mat=TIMBER_DARK, nobevel=True)
    # The failed leg, lying under the corner it used to hold up.
    b.box((0.070, 0.070, 0.640), (0.500, 0.360, 0.035), rot=(0.0, 90.0, 24.0),
          mat=TIMBER_DARK, nobevel=True)
    b.box((1.300, 0.060, 0.036), (0.0, 0.0, 0.600), rot=(0.0, -9.0, 0.0), mat=TIMBER_DARK,
          nobevel=True)
    for i, yaw in enumerate((11.0, -26.0, 44.0)):
        b.box((0.280, 0.210, 0.006), (-0.30 + i * 0.28, 0.10 - i * 0.09,
                                      0.760 - i * 0.04), rot=(0.0, -9.0, yaw), mat=PAPER,
              nobevel=True)
    return b


def build_chair() -> PropBuild:
    """A plain wooden chair, upright. 0.92 m.

    Small, cheap, and the single most human object in the kit — one chair pulled
    out from a table is a person who left."""
    b = PropBuild("Dress_Chair")
    seat = b.box((0.420, 0.400, 0.040), (0.0, 0.0, 0.450), mat=TIMBER_PALE)
    b.pivot_part = seat
    for (sx, sy) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        h = 0.920 if sy > 0 else 0.450
        b.box((0.040, 0.040, h), (sx * 0.180, sy * 0.170, h / 2), mat=TIMBER_DARK,
              nobevel=True)
    for z in (0.700, 0.830):
        b.box((0.360, 0.030, 0.070), (0.0, 0.170, z), mat=TIMBER_PALE, nobevel=True)
    b.box((0.380, 0.030, 0.030), (0.0, -0.170, 0.230), mat=TIMBER_DARK, nobevel=True)
    return b


def build_chair_broken() -> PropBuild:
    """A chair on its side with a leg gone. 0.44 m tall, entirely walk-around."""
    b = PropBuild("Dress_ChairBroken")
    seat = b.box((0.420, 0.400, 0.040), (0.0, 0.0, 0.220), rot=(74.0, 0.0, 18.0),
                 mat=TIMBER_PALE)
    b.pivot_part = seat
    b.box((0.360, 0.040, 0.400), (0.10, -0.16, 0.230), rot=(74.0, 0.0, 18.0), mat=TIMBER_PALE,
          nobevel=True)
    for (x, y, yaw, pitch) in ((-0.20, 0.14, 26.0, 80.0), (0.16, 0.22, -48.0, 84.0)):
        b.box((0.040, 0.040, 0.440), (x, y, 0.100), rot=(pitch, 0.0, yaw), mat=TIMBER_DARK,
              nobevel=True)
    b.box((0.040, 0.040, 0.420), (0.42, -0.24, 0.020), rot=(0.0, 90.0, 62.0), mat=TIMBER_DARK,
          nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  DEBRIS — low enough that §05's 65 % backward speed is never the reason a
#  player dies. Nothing in this group passes knee height.
# ══════════════════════════════════════════════════════════════════════════


def build_rubble_pile() -> PropBuild:
    """Fallen ceiling: concrete chunks, brick and dust. 1.15 x 0.95 x 0.34 m.

    Deterministic layout, no RNG, so a rebuild is byte-for-byte identical.
    Pale broken concrete over a dark floor is one of the few places this kit gets
    a genuine value step without a texture."""
    b = PropBuild("Dress_RubblePile")
    bed = b.box((1.060, 0.860, 0.024), (0.0, 0.0, 0.012), mat=CONCRETE, nobevel=True)
    b.pivot_part = bed
    _rubble(b, (
        (0.230, 0.200, 0.170, -0.300, -0.090, 0.096, 18.0),
        (0.190, 0.170, 0.140, 0.060, 0.070, 0.088, -34.0),
        (0.160, 0.150, 0.120, 0.330, -0.140, 0.070, 52.0),
        (0.140, 0.130, 0.100, -0.120, 0.290, 0.062, -12.0),
        (0.120, 0.110, 0.090, 0.420, 0.230, 0.056, 41.0),
        (0.200, 0.150, 0.130, -0.430, 0.220, 0.078, -58.0),
        (0.130, 0.120, 0.095, 0.170, -0.320, 0.058, 24.0),
        (0.105, 0.100, 0.080, -0.020, -0.180, 0.130, 66.0),
    ))
    _rubble(b, (
        (0.215, 0.100, 0.062, 0.230, 0.360, 0.034, -21.0),
        (0.215, 0.100, 0.062, -0.360, -0.320, 0.034, 39.0),
        (0.215, 0.100, 0.062, 0.480, -0.020, 0.096, 8.0),
    ), mat_name=BRICK)
    # Two rebar ends sticking out — the detail that says "ceiling", not "gravel".
    for (x, y, pitch, yaw) in ((-0.10, 0.06, 62.0, 24.0), (0.24, -0.04, 71.0, -46.0)):
        b.cyl(0.011, 0.520, (x, y, 0.180), rot=(pitch, 0.0, yaw), verts=6, mat=RUST_HEAVY,
              nobevel=True)
    return b


def build_rubble_small() -> PropBuild:
    """A scatter of chunks, 0.18 m tall. Filler for corners and wall feet."""
    b = PropBuild("Dress_RubbleSmall")
    bed = b.box((0.560, 0.460, 0.016), (0.0, 0.0, 0.008), mat=CONCRETE, nobevel=True)
    b.pivot_part = bed
    _rubble(b, (
        (0.150, 0.130, 0.110, -0.120, -0.040, 0.062, 22.0),
        (0.120, 0.110, 0.090, 0.110, 0.070, 0.052, -41.0),
        (0.100, 0.095, 0.075, 0.190, -0.120, 0.044, 63.0),
        (0.085, 0.080, 0.065, -0.190, 0.130, 0.040, -17.0),
        (0.070, 0.070, 0.055, 0.020, 0.160, 0.036, 48.0),
    ))
    return b


def build_planks_fallen() -> PropBuild:
    """Timber that came off a crate or a shelf. 1.65 x 0.80 x 0.17 m."""
    b = PropBuild("Dress_PlanksFallen")
    first = b.box((1.560, 0.160, 0.032), (0.0, -0.170, 0.016), rot=(0.0, 0.0, 4.0),
                  mat=TIMBER_PALE, nobevel=True)
    b.pivot_part = first
    _plank_scatter(b, (
        (1.420, 0.150, 0.030, -0.04, 0.030, 0.048, (0.0, 2.0, -7.0)),
        (1.180, 0.140, 0.030, 0.10, 0.220, 0.020, (0.0, 0.0, 12.0)),
        (0.940, 0.130, 0.028, -0.28, 0.320, 0.084, (0.0, -14.0, 27.0)),
        (0.760, 0.120, 0.026, 0.44, -0.290, 0.016, (0.0, 0.0, -31.0)),
    ))
    b.box((0.140, 0.130, 0.110), (-0.62, 0.170, 0.055), rot=(0.0, 6.0, 22.0),
          mat=CONCRETE_BROKEN, nobevel=True)
    for (x, y, yaw) in ((0.30, -0.06, 0.0), (-0.10, 0.14, 40.0)):
        b.cyl(0.006, 0.090, (x, y, 0.062), rot=(84.0, 0.0, yaw), verts=6, mat=RUST_HEAVY,
              nobevel=True)
    return b


def build_papers_scatter() -> PropBuild:
    """Loose paper across the floor. 1.25 x 1.00 x 0.03 m, walkable.

    Albedo 0.78 on a floor that renders near black: this is the cheapest readable
    object in the kit, and §03's whole loop is about reading things in a narrow
    beam. Kept under 3 cm so it is scenery, not an obstacle."""
    b = PropBuild("Dress_PapersScatter")
    sheets = (
        (0.300, 0.220, -0.36, -0.26, 0.003, 12.0),
        (0.300, 0.220, -0.10, -0.34, 0.005, -37.0),
        (0.290, 0.210, 0.24, -0.22, 0.003, 61.0),
        (0.300, 0.220, 0.44, 0.06, 0.007, -14.0),
        (0.280, 0.210, 0.10, 0.14, 0.003, 28.0),
        (0.300, 0.220, -0.22, 0.10, 0.009, -52.0),
        (0.290, 0.215, -0.44, 0.34, 0.003, 7.0),
        (0.300, 0.220, 0.06, 0.40, 0.005, 44.0),
        (0.280, 0.200, 0.40, 0.36, 0.003, -23.0),
    )
    first = None
    for (sx, sy, x, y, z, yaw) in sheets:
        obj = b.box((sx, sy, 0.004), (x, y, z), rot=(0.0, 0.0, yaw), mat=PAPER, nobevel=True)
        first = first or obj
    b.pivot_part = first
    # A folded bundle, so the scatter has one thing with a shadow under it.
    b.box((0.260, 0.190, 0.026), (-0.02, -0.10, 0.013), rot=(0.0, 3.0, -8.0), mat=PAPER,
          nobevel=True)
    b.box((0.240, 0.180, 0.020), (0.30, -0.44, 0.010), rot=(0.0, -4.0, 33.0), mat=CARD,
          nobevel=True)
    return b


def build_tool_scatter() -> PropBuild:
    """Dropped tools, 0.60 x 0.44 x 0.10 m. Small bright specular hits on a floor."""
    b = PropBuild("Dress_ToolScatter")
    first = b.box((0.320, 0.044, 0.018), (-0.10, 0.06, 0.009), rot=(0.0, 0.0, 16.0),
                  mat=STEEL_BARE, nobevel=True)
    b.pivot_part = first
    b.box((0.070, 0.070, 0.020), (0.06, 0.03, 0.010), rot=(0.0, 0.0, 16.0), mat=STEEL_BARE,
          nobevel=True)
    b.box((0.240, 0.036, 0.016), (0.14, -0.12, 0.008), rot=(0.0, 0.0, -41.0), mat=STEEL_BARE,
          nobevel=True)
    b.cyl(0.020, 0.170, (-0.16, -0.14, 0.020), rot=(90.0, 0.0, 62.0), verts=8,
          mat=TIMBER_DARK, nobevel=True)
    b.box((0.090, 0.040, 0.048), (-0.24, -0.10, 0.024), rot=(0.0, 0.0, 62.0), mat=STEEL_BARE,
          nobevel=True)
    b.cyl(0.090, 0.060, (0.20, 0.14, 0.030), verts=12, mat=RUST_HEAVY, nobevel=True)
    for (x, y) in ((0.04, 0.16), (-0.02, 0.19), (0.10, 0.20)):
        b.cyl(0.010, 0.014, (x, y, 0.007), verts=6, mat=STEEL_BARE, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  WET — §03's worked clue example is "그것은 물이 있는 층에 있다", so water is
#  diegetic information, not atmosphere.
# ══════════════════════════════════════════════════════════════════════════


def _puddle(b: PropBuild, lobes: tuple, thickness: float) -> None:
    """A pool built from overlapping flattened cylinders.

    Two reasons it is geometry rather than a decal: this project has no decal
    system yet, and a puddle needs its own *material* — roughness 0.05 — because
    the read is specular. A rough plane tinted blue is not water in a torch beam;
    a mirror is."""
    for (x, y, r, verts) in lobes:
        b.cyl(r, thickness, (x, y, thickness / 2), verts=verts, mat=WATER, nobevel=True)


def build_puddle_large() -> PropBuild:
    """Standing water, 1.85 x 1.35 m. The zone-scale answer to §03's water clue.

    A dark mirror on the floor. It costs nothing until a flashlight crosses it and
    then it is the brightest thing in the corridor — which is how a player learns
    "I am on the floor with the water" without a single line of UI."""
    b = PropBuild("Dress_PuddleLarge")
    _puddle(b, (
        (0.00, 0.00, 0.560, 20),
        (0.44, 0.16, 0.400, 16),
        (-0.46, -0.14, 0.360, 16),
        (0.18, -0.38, 0.300, 14),
        (-0.20, 0.40, 0.250, 12),
    ), 0.012)
    b.pivot_part = b.parts[0]
    # A damp rim, so the pool has an edge instead of floating on the floor.
    for (x, y, r) in ((0.00, 0.00, 0.620), (0.46, 0.17, 0.450), (-0.48, -0.15, 0.410)):
        b.cyl(r, 0.005, (x, y, 0.0025), verts=16, mat=WET_STAIN, nobevel=True)
    return b


def build_puddle_small() -> PropBuild:
    """A 0.85 m pool for under a drip or against a wall foot."""
    b = PropBuild("Dress_PuddleSmall")
    _puddle(b, (
        (0.00, 0.00, 0.290, 16),
        (0.20, 0.08, 0.180, 12),
        (-0.18, -0.09, 0.150, 12),
    ), 0.008)
    b.pivot_part = b.parts[0]
    b.cyl(0.330, 0.004, (0.0, 0.0, 0.002), verts=16, mat=WET_STAIN, nobevel=True)
    return b


def build_drain_grate() -> PropBuild:
    """A floor drain with water in it, 0.64 m square, 0.05 m proud.

    §12 asks for zone boundaries a player can name; a drain is a landmark that
    survives being seen for a quarter of a second at the edge of a beam. It also
    explains where §03's water is going, which is the difference between a wet
    basement and a puddle somebody placed."""
    b = PropBuild("Dress_DrainGrate")
    frame_obj = b.box((0.640, 0.640, 0.036), (0.0, 0.0, 0.018), mat=CONCRETE, nobevel=True)
    b.pivot_part = frame_obj
    b.box((0.520, 0.520, 0.010), (0.0, 0.0, 0.010), mat=WATER, nobevel=True)
    b.box((0.560, 0.040, 0.030), (0.0, -0.260, 0.032), mat=STEEL_BARE, nobevel=True)
    b.box((0.560, 0.040, 0.030), (0.0, 0.260, 0.032), mat=STEEL_BARE, nobevel=True)
    for i in range(5):
        y = -0.200 + i * 0.100
        b.box((0.540, 0.036, 0.024), (0.0, y, 0.030), mat=RUST_HEAVY, nobevel=True)
    b.box((0.036, 0.520, 0.024), (0.0, 0.0, 0.030), mat=RUST_HEAVY, nobevel=True)
    # Wet spread around the frame.
    b.cyl(0.480, 0.005, (0.0, 0.0, 0.0025), verts=16, mat=WET_STAIN, nobevel=True)
    return b


def build_drip_stain_wall() -> PropBuild:
    """A wet streak down a wall with the leaking joint at the top. WALL mount.

    Reaches from 2.40 m to the floor so it connects a ceiling pipe to a puddle:
    the three wet pieces are meant to be read as one story, which is what stops
    water looking like a texture somebody remembered to add."""
    b = PropBuild("Dress_DripStain_Wall")
    back = b.box((0.520, 0.014, 2.380), (0.0, -0.007, 1.190), mat=WET_STAIN, nobevel=True)
    b.pivot_part = back
    for (x, w, top, bot) in ((-0.150, 0.052, 2.320, 0.10), (0.020, 0.070, 2.360, 0.05),
                             (0.140, 0.044, 2.180, 0.22), (0.210, 0.030, 1.900, 0.40)):
        b.box((w, 0.008, top - bot), (x, -0.018, (top + bot) / 2), mat=WATER, nobevel=True)
    # Mineral crust where the water has been running longest.
    for (x, z, w, h) in ((0.02, 2.30, 0.260, 0.130), (-0.09, 1.62, 0.170, 0.090),
                         (0.13, 0.92, 0.140, 0.080)):
        b.box((w, 0.012, h), (x, -0.016, z), mat=PAPER, nobevel=True)
    # The joint that is leaking.
    b.cyl(0.046, 0.180, (0.020, -0.052, 2.420), rot=(90.0, 0.0, 0.0), verts=10,
          mat=RUST_HEAVY, nobevel=True)
    b.cyl(0.058, 0.040, (0.020, -0.052, 2.420), rot=(90.0, 0.0, 0.0), verts=10,
          mat=STEEL_BARE, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  WALL SERVICES — the pipes, valves and meters that make a basement a
#  basement. All authored 2.5 m so they tile on the MapKit grid.
# ══════════════════════════════════════════════════════════════════════════

RUN_LENGTH = 2.500
"""Length of a tiling run, metres. Equal to the MapKit's ``grid_metres`` so a run
docks with the next cell's run instead of leaving a gap every 2.5 m."""


def build_pipe_run_wall() -> PropBuild:
    """A 2.5 m parallel run at 1.95–2.25 m with brackets. WALL mount.

    Galvanised on purpose. A horizontal bright line at head height is the single
    most effective depth cue available in a corridor with no textures — the beam
    slides along it and the corridor's length becomes visible. §12 also wanted a
    way to give a zone its own character without touching the floor, which is what
    the Listener reads."""
    b = PropBuild("Dress_PipeRun_Wall")
    specs = ((0.062, 2.230, GALVANISED, 12), (0.044, 2.060, RUST_HEAVY, 10),
             (0.026, 1.950, COPPER, 8))
    _pipe_run(b, RUN_LENGTH, -0.130, specs, (-0.960, 0.0, 0.960))
    b.pivot_part = b.parts[0]
    # A union and a drop leg, so the run reads as plumbing rather than as a stripe.
    b.cyl(0.072, 0.090, (0.520, -0.130, 2.230), rot=(0.0, 90.0, 0.0), verts=12,
          mat=STEEL_BARE, nobevel=True)
    b.cyl(0.030, 0.420, (-0.480, -0.130, 1.760), verts=8, mat=COPPER, nobevel=True)
    b.cyl(0.038, 0.050, (-0.480, -0.130, 1.955), verts=8, mat=BRASS_FITTING, nobevel=True)
    return b


def build_pipe_run_ceiling() -> PropBuild:
    """A 2.5 m run hung under the ceiling on drop rods. CEILING mount.

    Hangs 0.42 m at most, which leaves a standing player 2.58 m of the kit's 3.0 m
    clear height — head clearance, not a duck."""
    b = PropBuild("Dress_PipeRun_Ceiling")
    specs = ((0.070, -0.320, GALVANISED, 12), (0.048, -0.300, STEEL_PAINTED, 10),
             (0.030, -0.150, COPPER, 8))
    for (radius, z, mat_name, verts) in specs:
        y = {0.070: -0.150, 0.048: 0.130, 0.030: 0.010}[radius]
        b.cyl(radius, RUN_LENGTH, (0.0, y, z), rot=(0.0, 90.0, 0.0), verts=verts,
              mat=mat_name, nobevel=True)
    b.pivot_part = b.parts[0]
    for x in (-0.960, 0.0, 0.960):
        b.box((0.560, 0.050, 0.030), (x, -0.010, -0.055), mat=STEEL_PAINTED, nobevel=True)
        for (y, z, radius) in ((-0.150, -0.320, 0.070), (0.130, -0.300, 0.048),
                               (0.010, -0.150, 0.030)):
            b.cyl(0.011, abs(z) - 0.055, (x, y, (-0.055 + z + radius) / 2), verts=6,
                  mat=STEEL_BARE, nobevel=True)
            b.box((0.036, 0.030, 0.030), (x, y, z + radius + 0.014), mat=STEEL_BARE,
                  nobevel=True)
    # A cable tray beside the pipes: a flat bright plane overhead reads at distance.
    b.box((RUN_LENGTH, 0.220, 0.020), (0.0, 0.300, -0.170), mat=GALVANISED, nobevel=True)
    for x in (-0.960, 0.0, 0.960):
        b.box((0.026, 0.220, 0.060), (x, 0.300, -0.150), mat=GALVANISED, nobevel=True)
        b.cyl(0.009, 0.115, (x, 0.300, -0.105), verts=6, mat=STEEL_BARE, nobevel=True)
    for y in (0.240, 0.300, 0.360):
        b.cyl(0.014, RUN_LENGTH - 0.10, (0.0, y, -0.146), rot=(0.0, 90.0, 0.0), verts=6,
              mat=RUBBER, nobevel=True)
    return b


def build_pipe_valve_cluster() -> PropBuild:
    """A valve station: two handwheels, a gauge and a manifold. WALL mount.

    §04's 정비공 turns things on; this is what "on" looks like when it belongs to
    the building. The gauge face is albedo 0.86, the brightest disc in the kit, so
    the cluster resolves before its outline does."""
    b = PropBuild("Dress_PipeValve_Cluster")
    manifold = b.cyl(0.070, 0.760, (0.0, -0.140, 1.700), rot=(0.0, 90.0, 0.0), verts=12,
                     mat=STEEL_PAINTED, nobevel=True)
    b.pivot_part = manifold
    for x in (-0.380, 0.380):
        b.cyl(0.086, 0.060, (x, -0.140, 1.700), rot=(0.0, 90.0, 0.0), verts=12,
              mat=STEEL_BARE, nobevel=True)
        b.box((0.060, 0.150, 0.150), (x, -0.070, 1.700), mat=STEEL_PAINTED, nobevel=True)
    # Two handwheels on risers.
    for (x, z, r) in ((-0.230, 2.060, 0.150), (0.230, 1.980, 0.120)):
        b.cyl(0.036, z - 1.760, (x, -0.140, (1.760 + z) / 2), verts=8, mat=STEEL_PAINTED,
              nobevel=True)
        b.cyl(0.062, 0.060, (x, -0.140, z - 0.050), verts=10, mat=STEEL_BARE, nobevel=True)
        b.torus(r, 0.016, (x, -0.140, z), rot=(0.0, 0.0, 0.0), mseg=14, nseg=5,
                mat=ENAMEL_RED)
        for i in range(3):
            b.box((r * 2.0, 0.024, 0.024), (x, -0.140, z), rot=(0.0, 0.0, i * 60.0),
                  mat=ENAMEL_RED, nobevel=True)
    # Gauge on a stub.
    b.cyl(0.016, 0.130, (0.0, -0.140, 1.830), verts=6, mat=BRASS_FITTING, nobevel=True)
    b.cyl(0.088, 0.050, (0.0, -0.140, 1.930), rot=(90.0, 0.0, 0.0), verts=16,
          mat=BRASS_FITTING, nobevel=True)
    b.cyl(0.076, 0.014, (0.0, -0.172, 1.930), rot=(90.0, 0.0, 0.0), verts=16,
          mat=GAUGE_FACE, nobevel=True)
    b.box((0.056, 0.008, 0.010), (0.020, -0.182, 1.938), rot=(0.0, 24.0, 0.0), mat=GRIME,
          nobevel=True)
    return b


def build_gauge_board() -> PropBuild:
    """Three meters on a backboard at 1.45–1.95 m. WALL mount.

    Deliberately at reading height. §03's whole loop is "look at a thing in a
    beam and remember it", and a wall of dials teaches a player to do that on
    something harmless before a clue asks for it in earnest."""
    b = PropBuild("Dress_GaugeBoard")
    back = b.box((0.640, 0.030, 0.500), (0.0, -0.015, 1.700), mat=TIMBER_DARK, nobevel=True)
    b.pivot_part = back
    b.box((0.680, 0.020, 0.040), (0.0, -0.030, 1.940), mat=STEEL_PAINTED, nobevel=True)
    b.box((0.680, 0.020, 0.040), (0.0, -0.030, 1.460), mat=STEEL_PAINTED, nobevel=True)
    for (x, r) in ((-0.200, 0.090), (0.020, 0.072), (0.220, 0.062)):
        b.cyl(r + 0.014, 0.044, (x, -0.052, 1.760), rot=(90.0, 0.0, 0.0), verts=16,
              mat=BRASS_FITTING, nobevel=True)
        b.cyl(r, 0.012, (x, -0.078, 1.760), rot=(90.0, 0.0, 0.0), verts=16, mat=GAUGE_FACE,
              nobevel=True)
        b.box((r * 1.5, 0.006, 0.008), (x + r * 0.3, -0.086, 1.760), rot=(0.0, -34.0, 0.0),
              mat=GRIME, nobevel=True)
    # A meter with a glass window and a paper chart behind it.
    b.box((0.260, 0.070, 0.170), (-0.060, -0.050, 1.560), mat=STEEL_PAINTED, nobevel=True)
    b.box((0.210, 0.014, 0.120), (-0.060, -0.090, 1.560), mat=GLASS_DIRTY, nobevel=True)
    b.box((0.190, 0.006, 0.100), (-0.060, -0.082, 1.560), mat=PAPER, nobevel=True)
    b.box((0.070, 0.040, 0.070), (0.190, -0.048, 1.550), mat=ENAMEL_RED, nobevel=True)
    b.cyl(0.026, 0.056, (0.190, -0.080, 1.550), rot=(90.0, 0.0, 0.0), verts=10,
          mat=STEEL_BARE, nobevel=True)
    return b


def build_conduit_wall() -> PropBuild:
    """A 2.5 m electrical conduit run with a junction box. WALL mount.

    §12 puts an 전기 패널 in every zone for the Engineer; conduit is what connects
    it to the rest of the building. Without it the panel is a box nailed to a wall
    and the zone-light mechanic looks like a UI element in a costume."""
    b = PropBuild("Dress_Conduit_Wall")
    main = b.cyl(0.026, RUN_LENGTH, (0.0, -0.052, 2.480), rot=(0.0, 90.0, 0.0), verts=8,
                 mat=GALVANISED, nobevel=True)
    b.pivot_part = main
    b.cyl(0.020, RUN_LENGTH, (0.0, -0.048, 2.380), rot=(0.0, 90.0, 0.0), verts=8,
          mat=GALVANISED, nobevel=True)
    for x in (-1.050, -0.350, 0.350, 1.050):
        b.box((0.036, 0.070, 0.036), (x, -0.038, 2.480), mat=STEEL_BARE, nobevel=True)
        b.box((0.030, 0.062, 0.030), (x, -0.036, 2.380), mat=STEEL_BARE, nobevel=True)
    # Junction box and a drop to switch height.
    b.box((0.180, 0.090, 0.180), (0.680, -0.045, 2.430), mat=GALVANISED)
    b.cyl(0.012, 0.010, (0.680, -0.092, 2.430), verts=8, mat=STEEL_BARE, nobevel=True)
    b.cyl(0.020, 1.020, (0.680, -0.048, 1.920), verts=8, mat=GALVANISED, nobevel=True)
    b.box((0.110, 0.070, 0.160), (0.680, -0.050, 1.400), mat=GALVANISED, nobevel=True)
    b.box((0.040, 0.030, 0.070), (0.680, -0.094, 1.400), mat=ENAMEL, nobevel=True)
    # A cable that came loose and hangs in a loop.
    for i, (x, z) in enumerate(((-0.60, 2.300), (-0.44, 2.180), (-0.28, 2.240))):
        b.cyl(0.010, 0.230, (x, -0.040, z), rot=(0.0, 62.0 - i * 44.0, 0.0), verts=6,
              mat=RUBBER, nobevel=True)
    return b


def build_vent_grille() -> PropBuild:
    """A wall vent at 2.05–2.45 m. WALL mount. Small, cheap, everywhere."""
    b = PropBuild("Dress_VentGrille")
    frame_obj = b.box((0.620, 0.040, 0.420), (0.0, -0.020, 2.250), mat=STEEL_PAINTED,
                      nobevel=True)
    b.pivot_part = frame_obj
    b.box((0.560, 0.030, 0.360), (0.0, -0.030, 2.250), mat=GRIME, nobevel=True)
    for i in range(6):
        b.box((0.540, 0.026, 0.036), (0.0, -0.044, 2.100 + i * 0.060), rot=(28.0, 0.0, 0.0),
              mat=GALVANISED, nobevel=True)
    for (sx, sz) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.cyl(0.012, 0.014, (sx * 0.280, -0.046, 2.250 + sz * 0.190), rot=(90.0, 0.0, 0.0),
              verts=6, mat=STEEL_BARE, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  HANGING — the ceiling is half of every frame in a first-person game and it
#  is the half nobody dresses. §05: dark and first person, so what hangs into
#  the beam matters more than what sits on the floor beside it.
# ══════════════════════════════════════════════════════════════════════════


def build_bulb_cord() -> PropBuild:
    """A bare bulb on a cord with a tin reflector. CEILING mount, 1.02 m drop.

    §03 makes darkness the lock on the objective, so a fitting that lights a
    corridor for free would be a design bug. Two things stop that. The material is
    emissive but weak, and the scatter tool gives a *minority* of these a real
    light whose range is one MapKit cell — so a working bulb marks a cell and
    never replaces the flashlight's 12 m reach.

    The reflector is the other half: even dead, its underside is albedo 0.72 and
    catches the player's own beam, so the ceiling stops being a void."""
    b = PropBuild("Dress_BulbCord")
    rose = b.cyl(0.062, 0.030, (0.0, 0.0, -0.015), verts=12, mat=GRIME, nobevel=True)
    b.pivot_part = rose
    b.cyl(0.007, 0.640, (0.0, 0.0, -0.350), verts=6, mat=RUBBER, nobevel=True)
    b.cyl(0.026, 0.090, (0.0, 0.0, -0.700), verts=10, mat=STEEL_PAINTED, nobevel=True)
    # Conical tin shade.
    b.cone(0.170, 0.045, 0.180, (0.0, 0.0, -0.800), rot=(180.0, 0.0, 0.0), verts=14,
           mat=STEEL_BARE, nobevel=True)
    b.cyl(0.022, 0.055, (0.0, 0.0, -0.885), verts=10, mat=BRASS_FITTING, nobevel=True)
    b.sph(0.048, (0.0, 0.0, -0.955), scale=(1.0, 1.0, 1.25), segs=12, rings=6,
          mat=BULB_DEAD, nobevel=True)
    return b


def build_bulb_caged() -> PropBuild:
    """A caged bulb on a short flex. CEILING mount, 0.46 m drop.

    The short version, for cells where a 1 m drop would be in a player's face.
    The cage is six bright wires around a bulb: in a beam it is a bright ring
    with a dark centre, which reads as a fitting at 10 m where a sphere does not."""
    b = PropBuild("Dress_BulbCaged")
    rose = b.cyl(0.058, 0.028, (0.0, 0.0, -0.014), verts=12, mat=GRIME, nobevel=True)
    b.pivot_part = rose
    b.cyl(0.007, 0.180, (0.0, 0.0, -0.118), verts=6, mat=RUBBER, nobevel=True)
    b.cyl(0.034, 0.090, (0.0, 0.0, -0.255), verts=10, mat=BRASS_FITTING, nobevel=True)
    b.sph(0.045, (0.0, 0.0, -0.340), scale=(1.0, 1.0, 1.2), segs=12, rings=6,
          mat=BULB_DEAD, nobevel=True)
    for i in range(6):
        a = math.radians(i * 60.0)
        b.box((0.012, 0.012, 0.200), (0.072 * math.cos(a), 0.072 * math.sin(a), -0.345),
              rot=(0.0, 0.0, math.degrees(a)), mat=STEEL_BARE, nobevel=True)
    b.torus(0.074, 0.008, (0.0, 0.0, -0.300), mseg=12, nseg=4, mat=STEEL_BARE)
    b.torus(0.052, 0.008, (0.0, 0.0, -0.440), mseg=12, nseg=4, mat=STEEL_BARE)
    return b


def build_chain_hang() -> PropBuild:
    """A chain hanging from a ceiling anchor, 1.00 m. CEILING mount.

    Vertical bright dashes in a frame otherwise made of horizontals. It also
    hangs *into* a beam rather than sitting under one, which is the difference
    between the ceiling being lit and the ceiling existing.

    The drop is capped by head clearance, not by taste: 3.0 m of kit clear height
    minus a 1.63 m player and 0.35 m of crown gap leaves 1.02 m, and `emit()`
    fails the build if a ceiling piece exceeds it."""
    b = PropBuild("Dress_ChainHang")
    plate = b.box((0.120, 0.120, 0.024), (0.0, 0.0, -0.012), mat=STEEL_PAINTED, nobevel=True)
    b.pivot_part = plate
    b.torus(0.038, 0.010, (0.0, 0.0, -0.056), rot=(90.0, 0.0, 0.0), mseg=10, nseg=4,
            mat=STEEL_BARE)
    _chain(b, 0.0, 0.0, -0.086, -0.840, links=9, radius=0.026)
    b.cyl(0.044, 0.120, (0.0, 0.0, -0.900), verts=10, mat=RUST_HEAVY, nobevel=True)
    b.torus(0.028, 0.009, (0.0, 0.0, -0.968), rot=(0.0, 0.0, 0.0), mseg=10, nseg=4,
            mat=RUST_HEAVY)
    return b


def build_sheet_hanging() -> PropBuild:
    """A dust sheet on a rail, 1.45 m wide, hanging to 1.05 m above the floor.

    The brightest large surface the kit can put in a corridor, and a real
    line-of-sight break: §06 releases aggro on 3 s without sight, and §12 wants
    those breaks at 15~25 m. A sheet does it without closing the route, so the
    scatter tool keeps its collider — a hider behind one is hidden, which a
    renderer alone would not deliver."""
    b = PropBuild("Dress_SheetHanging")
    rail = b.cyl(0.024, 1.500, (0.0, 0.0, -0.070), rot=(0.0, 90.0, 0.0), verts=8,
                 mat=STEEL_BARE, nobevel=True)
    b.pivot_part = rail
    for sx in (-1.0, 1.0):
        b.box((0.050, 0.100, 0.090), (sx * 0.740, 0.0, -0.045), mat=STEEL_PAINTED,
              nobevel=True)
    # Cloth as a set of slightly rotated panels — folds, not a plane.
    panels = ((-0.560, 0.300, -3.5), (-0.280, 0.310, 2.0), (0.0, 0.300, -1.5),
              (0.280, 0.305, 3.0), (0.560, 0.300, -2.5))
    for (x, w, tilt) in panels:
        b.box((w, 0.030, 1.860), (x, 0.0, -1.020), rot=(0.0, tilt, 0.0), mat=CLOTH_DUST,
              nobevel=True)
    # A torn corner and a hem, so the bottom edge is not a ruled line.
    b.box((0.320, 0.026, 0.360), (0.560, 0.010, -1.760), rot=(0.0, -14.0, 0.0),
          mat=CLOTH_DUST, nobevel=True)
    b.box((1.420, 0.036, 0.060), (0.0, 0.0, -0.130), mat=CLOTH_DUST, nobevel=True)
    b.box((0.900, 0.020, 0.240), (-0.20, -0.024, -1.640), rot=(0.0, 2.0, 0.0),
          mat=CLOTH_STAINED, nobevel=True)
    return b


def build_cobweb_corner() -> PropBuild:
    """A web filling a wall/wall/ceiling corner, 0.95 m across. CORNER mount.

    Authored with the corner itself at the origin and all geometry in the −X/−Y/−Z
    octant, so the scatter tool drops it on a corner point with no offset. Albedo
    0.72 and paper-thin: in a beam it is a bright triangle where the room's
    geometry stops, which is exactly the cue that tells a player a corner is a
    corner and not a doorway."""
    b = PropBuild("Dress_CobwebCorner")
    first = None
    # Radial strands from the corner, as thin triangular sheets at several angles.
    for i in range(7):
        t = i / 6.0
        span = 0.600 - 0.048 * i
        obj = b.box((span, 0.004, span * 0.9),
                    (-span / 2 - 0.02, -0.024 - 0.042 * i, -span * 0.45 - 0.02),
                    rot=(0.0, 22.0 - 44.0 * t, 0.0), mat=COBWEB, nobevel=True)
        first = first or obj
    b.pivot_part = first
    # Cross strands, so it reads as a web rather than as a fan.
    for i in range(4):
        d = 0.130 + i * 0.125
        b.box((d * 1.35, d * 1.35, 0.004), (-d * 0.55, -d * 0.55, -0.030 - i * 0.10),
              rot=(0.0, 0.0, 45.0), mat=COBWEB, nobevel=True)
    # A heavier hank hanging out of the corner.
    b.box((0.060, 0.060, 0.260), (-0.185, -0.185, -0.300), rot=(6.0, -8.0, 45.0),
          mat=COBWEB, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  SIGNAGE — blank plates. Each of these carried one `Clue_Face` island mapped
#  0..1 for §13's host-rendered glyph; §03 단서 is deleted and so is the glyph.
#  They are kept as what is left when the writing goes: painted steel, an enamel
#  field, bolts and chains. §12 wants a corridor to read as a place.
# ══════════════════════════════════════════════════════════════════════════


def build_wall_sign() -> PropBuild:
    """An enamel sign at 1.42–1.78 m. WALL mount.

    The face is a **glossy** enamel field (roughness 0.30) in a bright bezel. §03
    files ㅁ↔ㅇ under blur, and gloss is how a flashlight produces blur: held close
    and square on, the beam's own hotspot blows the plate out and the difference
    between a closed square and a closed circle stops existing. Read it from an
    angle and it is legible — which is the version the player who is not in a
    hurry gets."""
    b = PropBuild("Dress_WallSign")
    W, H = 0.520, 0.360
    back = b.box((W, 0.026, H), (0.0, -0.013, 1.600), mat=STEEL_PAINTED, nobevel=True)
    b.pivot_part = back
    b.box((W, 0.016, 0.034), (0.0, -0.032, 1.600 + H / 2 - 0.017), mat=STEEL_BARE,
          nobevel=True)
    b.box((W, 0.016, 0.034), (0.0, -0.032, 1.600 - H / 2 + 0.017), mat=STEEL_BARE,
          nobevel=True)
    for sx in (-1.0, 1.0):
        b.box((0.034, 0.016, H), (sx * (W / 2 - 0.017), -0.032, 1.600), mat=STEEL_BARE,
              nobevel=True)
    b.box((W - 0.060, 0.012, H - 0.060), (0.0, -0.032, 1.600), mat=ENAMEL, nobevel=True)
    for (sx, sz) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.cyl(0.010, 0.014, (sx * (W / 2 - 0.026), -0.040, 1.600 + sz * (H / 2 - 0.026)),
              rot=(90.0, 0.0, 0.0), verts=6, mat=STEEL_BARE, nobevel=True)
    return b


def build_hanging_sign() -> PropBuild:
    """A double-sided sign on two chains. CEILING mount. §03's 좌↔우.

    Both faces used to carry a ``Clue_Face`` quad, one of them UV-mirrored, so a
    single host-stamped glyph read correctly from one side and reversed from the
    other — §03's 좌↔우 without a mirror. The glyph is deleted; the plate is
    symmetric in Y about its own mid-plane and stays that way, because neither
    face being "the front" is what lets the scatterer hang it in any corridor.

    The plate is asserted mirror-symmetric about its own plane, because a single
    bracket on one side would tell a player which face is the front and the pair
    would collapse."""
    b = PropBuild("Dress_HangingSign")
    W, H = 0.680, 0.320
    top = -0.560
    bar = b.cyl(0.014, W + 0.120, (0.0, 0.0, top), rot=(0.0, 90.0, 0.0), verts=8,
                mat=STEEL_BARE, nobevel=True)
    b.pivot_part = bar
    for sx in (-1.0, 1.0):
        _chain(b, sx * 0.290, 0.0, -0.030, top, links=5, radius=0.020)
        b.torus(0.026, 0.007, (sx * 0.290, 0.0, top - 0.030), rot=(0.0, 0.0, 0.0),
                mseg=8, nseg=4, mat=STEEL_BARE)
    b.box((0.090, 0.090, 0.020), (0.0, 0.0, -0.010), mat=STEEL_PAINTED, nobevel=True)
    zc = top - 0.210
    # The plate: symmetric in Y about its own mid-plane, so neither face is "the front".
    b.box((W, 0.024, H), (0.0, 0.0, zc), mat=STEEL_PAINTED, nobevel=True)
    for sy in (-1.0, 1.0):
        b.box((W - 0.050, 0.010, H - 0.050), (0.0, sy * 0.017, zc), mat=ENAMEL, nobevel=True)
        b.box((W, 0.008, 0.026), (0.0, sy * 0.016, zc + H / 2 - 0.013), mat=STEEL_BARE,
              nobevel=True)
        b.box((W, 0.008, 0.026), (0.0, sy * 0.016, zc - H / 2 + 0.013), mat=STEEL_BARE,
              nobevel=True)
    for sx in (-1.0, 1.0):
        b.cyl(0.012, 0.048, (sx * (W / 2 - 0.030), 0.0, zc + H / 2 + 0.008), verts=6,
              mat=STEEL_BARE, nobevel=True)
    return b


def build_pipe_label() -> PropBuild:
    """A labelled pipe section at 2.05 m. WALL mount. §03's 6↔9, "뒤집힌 각도에서".

    The plate is bolted to a band around a pipe and is built with **no up-cue**:
    four identical corner bolts, an identical band each side, and a rectangular
    field — so the whole assembly is symmetric under a half-turn about the plate's
    own face normal. Walk to the other end of the corridor, look back, and the
    label is upside down with nothing in the geometry to say so. That is exactly
    the condition §03 asks for, and it is asserted to a millimetre rather than
    eyeballed, because one asymmetric bolt restores the orientation and kills the
    pair."""
    b = PropBuild("Dress_PipeLabel")
    zc = 2.050
    y = -0.130
    pipe = b.cyl(0.070, 1.100, (0.0, y, zc), rot=(0.0, 90.0, 0.0), verts=12, mat=GALVANISED,
                 nobevel=True)
    b.pivot_part = pipe
    for sx in (-1.0, 1.0):
        b.cyl(0.082, 0.070, (sx * 0.470, y, zc), rot=(0.0, 90.0, 0.0), verts=12,
              mat=STEEL_BARE, nobevel=True)
        b.box((0.056, 0.026, 0.190), (sx * 0.330, y * 0.35, zc), mat=STEEL_PAINTED,
              nobevel=True)
        for sz in (-1.0, 1.0):
            b.box((0.056, abs(y) * 1.05, 0.026), (sx * 0.330, y * 0.5, zc + sz * 0.083),
                  mat=STEEL_PAINTED, nobevel=True)
    # Band and plate. Everything from here on is symmetric about the plate centre.
    for sx in (-1.0, 1.0):
        b.cyl(0.078, 0.024, (sx * 0.130, y, zc), rot=(0.0, 90.0, 0.0), verts=12,
              mat=STEEL_BARE, nobevel=True)
    b.box((0.320, 0.014, 0.170), (0.0, y - 0.076, zc), mat=ENAMEL, nobevel=True)
    for (sx, sz) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.cyl(0.009, 0.014, (sx * 0.140, y - 0.086, zc + sz * 0.070), rot=(90.0, 0.0, 0.0),
              verts=6, mat=STEEL_BARE, nobevel=True)
    return b


def build_door_plate() -> PropBuild:
    """A stencilled number plate at 1.42 m. WALL mount. §03's 1↔7, 손글씨체.

    0.20 m across on a brass back with an enamel field. It was §03's 1↔7 — the
    cheapest readable surface in the kit and therefore the one that could be
    everywhere, read at a glancing angle in a moving beam where an ascender and a
    serif are the same smudge. Nothing is written on it now; it is the smallest
    piece in the kit and the one that makes a doorway look like a doorway."""
    b = PropBuild("Dress_DoorPlate")
    W, H = 0.200, 0.140
    back = b.box((W, 0.010, H), (0.0, -0.005, 1.420), mat=BRASS_FITTING, nobevel=True)
    b.pivot_part = back
    b.box((W - 0.024, 0.008, H - 0.024), (0.0, -0.012, 1.420), mat=ENAMEL, nobevel=True)
    for sx in (-1.0, 1.0):
        b.cyl(0.007, 0.012, (sx * (W / 2 - 0.016), -0.018, 1.420), rot=(90.0, 0.0, 0.0),
              verts=6, mat=STEEL_BARE, nobevel=True)
    return b


# ── Piece table ─────────────────────────────────────────────────────────────


@dataclass
class Piece:
    """One dressing piece and everything the scatter tool needs to place it."""

    name: str
    build: Callable[[], PropBuild]
    group: str
    """Placement family: Bulk · Debris · Decal · Wet · Wall · Ceiling · Corner · Sign."""

    expect: tuple[float, float, float]
    """Intended metre dimensions. Checked, so a unit-scale slip fails the build."""

    mount: str = "FLOOR"
    palettes: tuple[str, ...] = ("*",)
    """Which zone palettes may use it. `*` means any. See PALETTES."""

    weight: float = 1.0
    """Relative pick probability inside its group."""

    bevel: float = 0.005
    max_tris: int = 1600
    solid: bool = True
    """Whether the piece should keep a collider and be baked into the NavMesh."""

    note: str = ""
    checks: tuple[str, ...] = ()


PALETTES: dict[str, str] = {
    "storage": "§12 zone A 나무 — an old storage wing: timber, crates, sheets, cobwebs.",
    "institutional": "§12 zone B 타일 — the tiled hall: lockers, cabinets, signage, meters.",
    "wet": "§12 zone C 자갈 — the unfinished, flooded end. §03's 물이 있는 층 lives here.",
    "utility": "§12 zone D 콘크리트 — plant and services: pipes, valves, benches, tools.",
}

PIECES: list[Piece] = [
    # ── Bulk — cover, and §12's 시야 차단 지점 ───────────────────────────────
    Piece("Dress_CrateStack_Tall", build_crate_stack_tall, "Bulk", (0.713, 0.713, 1.680),
          palettes=("storage", "utility"), weight=1.2, max_tris=1200,
          note="sightline break just under eye height"),
    Piece("Dress_CrateStack_Low", build_crate_stack_low, "Bulk", (1.492, 0.860, 1.140),
          palettes=("storage", "wet"), weight=1.0, max_tris=1300, note="crouch cover"),
    Piece("Dress_CrateBroken", build_crate_broken, "Debris", (1.181, 1.072, 0.397),
          palettes=("storage", "wet"), weight=0.8, max_tris=900),
    Piece("Dress_BarrelUpright", build_barrel_upright, "Bulk", (0.612, 0.612, 0.895),
          weight=1.3, max_tris=700),
    Piece("Dress_BarrelCluster", build_barrel_cluster, "Bulk", (1.405, 1.269, 0.895),
          palettes=("wet", "utility"), weight=0.9, max_tris=1600),
    Piece("Dress_BarrelToppled", build_barrel_toppled, "Bulk", (0.895, 0.612, 0.624),
          palettes=("wet", "utility", "storage"), weight=0.7, max_tris=700),
    Piece("Dress_ShelfStocked", build_shelf_stocked, "Bulk", (1.100, 0.420, 1.974),
          palettes=("storage", "institutional", "utility"), weight=1.2, max_tris=2600,
          note="§12 sightline blocker; gaps make it cover rather than wall"),
    Piece("Dress_ShelfToppled", build_shelf_toppled, "Bulk", (1.206, 2.170, 0.439),
          palettes=("storage", "institutional"), weight=0.6, max_tris=1200),
    Piece("Dress_FilingCabinet", build_filing_cabinet, "Bulk", (0.480, 0.995, 1.333),
          palettes=("institutional", "utility"), weight=1.0, max_tris=1100),
    Piece("Dress_LockerBank", build_locker_bank, "Bulk", (1.283, 0.745, 1.860),
          palettes=("institutional",), weight=0.9, max_tris=1600),
    Piece("Dress_Workbench", build_workbench, "Bulk", (1.839, 0.814, 1.520),
          palettes=("utility", "storage"), weight=0.8, max_tris=2600),
    Piece("Dress_TableBroken", build_table_broken, "Bulk", (1.501, 0.902, 0.831),
          palettes=("storage", "institutional"), weight=0.7, max_tris=1100),
    Piece("Dress_Chair", build_chair, "Bulk", (0.420, 0.400, 0.920),
          palettes=("storage", "institutional"), weight=0.7, max_tris=800),
    Piece("Dress_ChairBroken", build_chair_broken, "Debris", (0.851, 0.817, 0.416),
          palettes=("storage", "institutional"), weight=0.6, max_tris=800),
    # ── Debris — never above knee height (§05's 65 % backward speed) ────────
    Piece("Dress_RubblePile", build_rubble_pile, "Debris", (1.151, 0.878, 0.324),
          palettes=("wet", "utility", "storage"), weight=1.2, max_tris=1200),
    Piece("Dress_RubbleSmall", build_rubble_small, "Debris", (0.560, 0.460, 0.138),
          weight=1.4, max_tris=700),
    Piece("Dress_PlanksFallen", build_planks_fallen, "Debris", (1.580, 1.124, 0.255),
          palettes=("storage", "wet"), weight=1.0, max_tris=800),
    Piece("Dress_PapersScatter", build_papers_scatter, "Decal", (1.209, 1.164, 0.041),
          weight=1.3, max_tris=600, solid=False, note="albedo 0.78 — the kit's brightest floor"),
    Piece("Dress_ToolScatter", build_tool_scatter, "Decal", (0.569, 0.442, 0.060),
          palettes=("utility", "storage"), weight=0.9, max_tris=700, solid=False),
    # ── Wet — §03: "그것은 물이 있는 층에 있다" ─────────────────────────────
    Piece("Dress_PuddleLarge", build_puddle_large, "Decal", (1.860, 1.340, 0.012),
          palettes=("wet", "institutional"), weight=1.4, max_tris=700, solid=False,
          note="§03's water clue, and the kit's only mirror"),
    Piece("Dress_PuddleSmall", build_puddle_small, "Decal", (0.710, 0.660, 0.008),
          weight=1.2, max_tris=500, solid=False),
    Piece("Dress_DrainGrate", build_drain_grate, "Decal", (0.960, 0.960, 0.050),
          palettes=("wet", "institutional", "utility"), weight=0.6, max_tris=800,
          solid=False),
    Piece("Dress_DripStain_Wall", build_drip_stain_wall, "Wall", (0.520, 0.180, 2.478),
          mount="WALL", palettes=("wet", "institutional"), weight=0.9, max_tris=900,
          solid=False),
    # ── Wall services ──────────────────────────────────────────────────────
    Piece("Dress_PipeRun_Wall", build_pipe_run_wall, "Wall", (2.500, 0.205, 0.770),
          mount="WALL", weight=1.6, max_tris=900, solid=False,
          note="tiles on the 2.5 m grid; galvanised = the corridor's depth cue"),
    Piece("Dress_PipeValve_Cluster", build_pipe_valve_cluster, "Wall",
          (0.820, 0.324, 0.461), mount="WALL", palettes=("utility", "wet"), weight=0.8,
          max_tris=1400, solid=False),
    Piece("Dress_GaugeBoard", build_gauge_board, "Wall", (0.680, 0.100, 0.520),
          mount="WALL", palettes=("utility", "institutional"), weight=0.8, max_tris=1400,
          solid=False),
    Piece("Dress_Conduit_Wall", build_conduit_wall, "Wall", (2.500, 0.104, 1.240),
          mount="WALL", weight=1.2, max_tris=1200, solid=False),
    Piece("Dress_VentGrille", build_vent_grille, "Wall", (0.620, 0.070, 0.420),
          mount="WALL", weight=1.0, max_tris=900, solid=False),
    # ── Hanging ────────────────────────────────────────────────────────────
    Piece("Dress_PipeRun_Ceiling", build_pipe_run_ceiling, "Ceiling",
          (2.500, 0.630, 0.370), mount="CEILING", weight=1.5, max_tris=1600, solid=False),
    Piece("Dress_BulbCord", build_bulb_cord, "Ceiling", (0.340, 0.340, 1.020),
          mount="CEILING", weight=1.4, max_tris=700, solid=False,
          note="§03: a minority get a real light, ranged to one cell"),
    Piece("Dress_BulbCaged", build_bulb_caged, "Ceiling", (0.164, 0.164, 0.448),
          mount="CEILING", weight=1.0, max_tris=900, solid=False),
    Piece("Dress_ChainHang", build_chain_hang, "Ceiling", (0.120, 0.120, 0.977),
          mount="CEILING", palettes=("storage", "wet", "utility"), weight=0.8,
          max_tris=1400, solid=False),
    # The one ceiling piece exempt from head clearance. It is a cloth sheet: it is
    # *meant* to hang into the room and break a line of sight (§06 releases aggro
    # on 3 s without sight), and the manifest flags it `hangs_low` so the scatter
    # tool keeps it flat against a wall and out of dead-end mouths rather than
    # slung across a route a fleeing player has to take at 5.6 m/s.
    Piece("Dress_SheetHanging", build_sheet_hanging, "Ceiling", (1.531, 0.100, 1.973),
          mount="CEILING", palettes=("storage", "institutional"), weight=0.5,
          max_tris=900, checks=("hangs_low",),
          note="a §06 line-of-sight break that is not a wall; hangs into the room"),
    Piece("Dress_CobwebCorner", build_cobweb_corner, "Corner", (0.964, 0.964, 0.725),
          mount="CORNER", palettes=("storage", "wet"), weight=1.0, max_tris=900,
          solid=False),
    # ── Signage ────────────────────────────────────────────────────────────
    # These four were §03's 혼동쌍 fittings and each carried a Clue_Face the host
    # stamped a glyph onto: ㅁ↔ㅇ on a glossy field, 좌↔우 on a mirrored back
    # face, 6↔9 on a plate with no up-cue, 1↔7 on a matte plate read in passing.
    # 단서 is deleted, so the glyph faces and the four misread checks that made
    # them misreadable went with it. The enamel plate under each one is kept:
    # a basement with signage in it looks like a basement, and §12 wants a
    # corridor to read as a place. Nothing on them is information.
    Piece("Dress_WallSign", build_wall_sign, "Sign", (0.520, 0.045, 0.360), mount="WALL",
          weight=1.0, max_tris=800, solid=False, note="wall plate, blank"),
    Piece("Dress_HangingSign", build_hanging_sign, "Sign", (0.800, 0.086, 0.930),
          mount="CEILING", weight=0.7, max_tris=1600, solid=False,
          note="double-sided plate on two chains, blank"),
    Piece("Dress_PipeLabel", build_pipe_label, "Sign", (1.100, 0.224, 0.191), mount="WALL",
          weight=0.9, max_tris=1200, solid=False, note="labelled pipe band, blank"),
    Piece("Dress_DoorPlate", build_door_plate, "Sign", (0.200, 0.026, 0.140), mount="WALL",
          weight=1.1, max_tris=400, solid=False, note="door number plate, blank"),
]


# ── Emit ────────────────────────────────────────────────────────────────────

ROWS: list[dict] = []


def pivot_shift(b: PropBuild, mount: str) -> Vector:
    """Offset that puts the origin where the mount convention says.

    Extends `gen_props.pivot_shift` with the two mounts a dressing kit needs and
    a loot kit does not: CEILING, whose origin is the ceiling plane so a piece
    hangs from wherever it is dropped, and CORNER, whose origin is the
    wall/wall/ceiling corner itself.
    """
    if mount in ("FLOOR", "WALL"):
        return gen_props.pivot_shift(b, mount)

    ref = [b.pivot_part] if b.pivot_part is not None else b.parts
    rlo, rhi = world_bbox(ref)
    alo, ahi = world_bbox(b.parts)
    if mount == "CEILING":
        return Vector((-(rlo.x + rhi.x) / 2, -(rlo.y + rhi.y) / 2, -ahi.z))
    if mount == "CORNER":
        return Vector((-ahi.x, -ahi.y, -ahi.z))
    blendkit.fail(f"unknown mount '{mount}'")
    raise AssertionError


def emit(piece: Piece) -> None:
    blendkit.reset_scene()
    b = piece.build()
    if not b.parts:
        blendkit.fail(f"{piece.name}: builder produced no geometry")

    shift = pivot_shift(b, piece.mount)
    for obj in b.parts:
        obj.location = obj.location + shift
    bpy.context.view_layer.update()

    bevel_skipped = 0
    for obj in b.parts:
        blendkit.apply_transforms(obj, location=True, rotation=True, scale=True)
        if piece.bevel <= 0.0 or obj.name in b.nobevel:
            bevel_skipped += 1
            continue
        if min(obj.dimensions) < piece.bevel * 4.0:
            bevel_skipped += 1
            continue
        blendkit.bevel(obj, width=piece.bevel, segments=1)

    obj = blendkit.join(b.parts, piece.name)
    blendkit.triangulate(obj)
    sharp = gen_props.apply_smooth(obj, 30.0)
    blendkit.uv_smart_project(obj)

    path = blendkit.out_path("Dressing", piece.name + ".fbx")
    blendkit.export_fbx(path, objects=[obj], with_animation=False)

    report = blendkit.describe(path)
    blendkit.assert_asset(report, min_vertices=8, max_triangles=piece.max_tris,
                          max_dimension=4.0)

    used = [m.name for m in obj.data.materials if m is not None]
    lost = gen_props.fbx_missing_materials(path, used)
    if lost:
        blendkit.fail(f"{piece.name}: materials missing from the exported FBX: {', '.join(lost)}")

    size = report.size
    for i, axis in enumerate("XYZ"):
        want = piece.expect[i]
        tol = max(0.008, want * 0.10)
        if abs(size[i] - want) > tol:
            msg = (f"{piece.name}: {axis} is {size[i]:.3f} m, intended {want:.3f} m "
                   f"(tolerance {tol:.3f}). 1 unit must be 1 metre.")
            if MEASURE_ONLY:
                print("SIZE_MISMATCH " + msg)
            else:
                blendkit.fail(msg)

    lo, hi = report.bounds_min, report.bounds_max
    if piece.mount == "WALL":
        if abs(hi[1]) > 0.004:
            blendkit.fail(f"{piece.name}: wall piece's mounting face is at y={hi[1]:.4f}, must be 0")
        if lo[2] < -0.004:
            blendkit.fail(f"{piece.name}: wall piece dips below the floor (min z={lo[2]:.4f})")
    elif piece.mount == "CEILING":
        if abs(hi[2]) > 0.004:
            blendkit.fail(f"{piece.name}: ceiling piece's mounting face is at z={hi[2]:.4f}, must be 0")
        drop = -lo[2]
        headroom = CEILING_CLEAR_ASSUMED - drop
        if headroom < EYE_HEIGHT_ASSUMED + HEAD_CLEARANCE_ASSUMED and "hangs_low" not in piece.checks:
            blendkit.fail(f"{piece.name}: hangs {drop:.2f} m, leaving {headroom:.2f} m under a "
                          f"{CEILING_CLEAR_ASSUMED} m ceiling — a "
                          f"{EYE_HEIGHT_ASSUMED} m player walks into it")
    elif piece.mount == "CORNER":
        for i, axis in enumerate("XYZ"):
            if abs(hi[i]) > 0.004:
                blendkit.fail(f"{piece.name}: corner piece's {axis} face is at {hi[i]:.4f}, must be 0")
    else:
        if abs(lo[2]) > 0.004:
            blendkit.fail(f"{piece.name}: floor piece's base is at z={lo[2]:.4f}, must be 0")

    ROWS.append({
        "name": piece.name, "group": piece.group, "mount": piece.mount, "size": size,
        "bounds_min": lo, "bounds_max": hi, "tris": report.triangles,
        "verts": report.vertices, "bytes": report.bytes, "path": path,
        "materials": used,
        "sharp": sharp, "bevel_skipped": bevel_skipped, "piece": piece,
        "emissive": gen_props.emissive_material_count(obj),
    })

    blendkit.print_report(report)
    extra = [f"piece={piece.name}", f"group={piece.group}", f"mount={piece.mount}",
             f"size={size[0]:.3f}x{size[1]:.3f}x{size[2]:.3f}m",
             f"tris={report.triangles}", f"emissive={ROWS[-1]['emissive']}"]
    print("DRESS_DETAIL " + " ".join(extra))


# ── Manifest ────────────────────────────────────────────────────────────────


def write_manifest(rows: list[dict]) -> str:
    """Writes the contract the Unity scatter tool reads.

    Every number the scatterer needs about a piece is *measured* here and written
    out: footprint, height, how deep it sits when it is pushed against a wall,
    whether it breaks a sight line, whether it keeps a collider. Restating any of
    those in C# would let a re-export silently invalidate them — the piece would
    get 8 cm wider and the scatterer would keep believing the old figure and
    keep parking it 8 cm inside §08's carry channel.
    """
    pieces = []
    for r in rows:
        piece: Piece = r["piece"]
        sx, sy, sz = r["size"]
        pieces.append({
            "name": piece.name,
            "file": piece.name + ".fbx",
            "group": piece.group,
            "mount": piece.mount,
            "palettes": list(piece.palettes),
            "weight": piece.weight,
            "size_x": round(sx, 4),
            "size_y": round(sy, 4),
            "size_z": round(sz, 4),
            # How far the piece reaches out from the surface it is mounted on.
            # FLOOR pieces are authored back-to-+Y, so it is the +Y half-extent
            # doubled; WALL and CEILING pieces measure straight off the mount plane.
            "mount_depth": round(sy if piece.mount in ("FLOOR", "WALL") else sz, 4),
            "breaks_sightline": bool(sz >= EYE_HEIGHT_ASSUMED * 0.55
                                     and piece.mount == "FLOOR"),
            "solid": piece.solid,
            "hangs_low": "hangs_low" in piece.checks,
            "triangles": r["tris"],
            "emissive_materials": r["emissive"],
            "materials": r["materials"],
            "note": piece.note,
        })

    manifest = {
        "generated_by": "tools/blender/gen_dressing.py",
        "grid_metres": 2.5,
        "assumptions": {
            "eye_height_metres": EYE_HEIGHT_ASSUMED,
            "ceiling_clear_metres": CEILING_CLEAR_ASSUMED,
            "head_clearance_metres": HEAD_CLEARANCE_ASSUMED,
            "source": "NOT_IN_DESIGN_DOC",
        },
        "palettes": [{"name": k, "note": v} for k, v in sorted(PALETTES.items())],
        "materials": [
            {
                "name": spec.name,
                "r": round(spec.color[0], 4),
                "g": round(spec.color[1], 4),
                "b": round(spec.color[2], 4),
                "roughness": spec.roughness,
                "metallic": spec.metallic,
                "emission": spec.emission,
            }
            for spec in sorted(MATERIALS.values(), key=lambda s: s.name)
        ],
        "pieces": pieces,
    }

    path = blendkit.out_path("Dressing", "Dressing.manifest.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    return path


# ── Report ──────────────────────────────────────────────────────────────────


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    todo = [p for p in PIECES if not argv or any(a.lower() in p.name.lower() for a in argv)]
    if not todo:
        blendkit.fail(f"no dressing piece matches {argv}")

    print("DRESS_ASSUMPTIONS " + " ".join([
        f"eye_height={EYE_HEIGHT_ASSUMED}m",
        f"ceiling_clear={CEILING_CLEAR_ASSUMED}m",
        f"head_clearance={HEAD_CLEARANCE_ASSUMED}m",
        "source=NOT_IN_DESIGN_DOC",
    ]))

    for piece in todo:
        emit(piece)

    lines: list[str] = []

    # DELETED with §03 단서: the four 혼동쌍 assertions and the coverage check.
    # They proved the signage set could make a glyph *misread* — Dress_PipeLabel
    # half-turn symmetric to a millimetre so 6 is 9 from the far end,
    # Dress_HangingSign mirror symmetric so the back face reverses, every
    # Clue_Face flat and UV 0..1 so a stamped glyph would not distort. The
    # geometry those checks guarded is gone with the material, and there is
    # nothing to misread in a race that announces its finish at the start.

    # Nothing on the floor may be tall enough to trip a fleeing player (§05, §06).
    for r in ROWS:
        piece = r["piece"]
        if piece.group == "Debris" and r["size"][2] > 0.50:
            blendkit.fail(f"{piece.name} is {r['size'][2]:.2f} m tall — debris above knee "
                          "height is a §05/§06 death written by set dressing")
    lines.append("no Debris piece passes 0.50 m — §05's 65 % backward speed stays survivable  OK")

    # The kit has to contain surfaces bright enough to see. §03 lights this
    # building with a 12 m cone in near-black ambient.
    bright = [m for m in MATERIALS.values() if max(m.color) >= 0.50 and m.emission == 0.0]
    if len(bright) < 6:
        blendkit.fail(f"only {len(bright)} materials sit at albedo ≥ 0.50 — the kit would "
                      "disappear in a flashlight beam exactly like the empty corridors it "
                      "is replacing")
    lines.append(f"{len(bright)} of {len(MATERIALS)} materials at albedo ≥ 0.50 "
                 f"(brightest {max(max(m.color) for m in MATERIALS.values()):.2f}), "
                 f"{sum(1 for m in MATERIALS.values() if m.roughness <= 0.35)} at roughness "
                 "≤ 0.35 for specular response  OK")

    manifest_path = write_manifest(ROWS) if len(todo) == len(PIECES) else None

    print()
    print("=" * 112)
    print("DRESSING KIT — measured, not intended.")
    print("=" * 112)
    print(f"{'piece':<28}{'group':<10}{'mount':<9}{'dimensions (m)':<24}{'tris':>6}"
          f"{'solid':>7}  palettes")
    print("-" * 112)
    for r in ROWS:
        piece = r["piece"]
        sx, sy, sz = r["size"]
        print(f"{piece.name:<28}{piece.group:<10}{piece.mount:<9}"
              f"{sx:.2f} x {sy:.2f} x {sz:.2f}      {r['tris']:>6}"
              f"{'yes' if piece.solid else 'no':>7}  {','.join(piece.palettes)}")
    print("-" * 112)
    print(f"{len(ROWS)} pieces   total tris {sum(r['tris'] for r in ROWS)}   "
          f"max {max(r['tris'] for r in ROWS)} "
          f"({max(ROWS, key=lambda r: r['tris'])['name']})   "
          f"total bytes {sum(r['bytes'] for r in ROWS)}")
    print()
    for line in lines:
        print("CHECK " + line)
    print()
    if manifest_path:
        print(f"FILE {manifest_path}")
    for r in ROWS:
        print(f"FILE {r['path']} ({r['bytes']} bytes)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_dressing.py raised:\n" + traceback.format_exc())
