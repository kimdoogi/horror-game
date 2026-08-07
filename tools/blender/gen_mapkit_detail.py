#!/usr/bin/env python3
"""Architectural detail library for §12's map kit.

This module is not a generator. It is the vocabulary ``gen_mapkit.py`` builds its
pieces out of: a dressed wall elevation, a framed ceiling, a cambered floor pan,
service runs, and the wear that makes concrete look poured rather than extruded.

WHY IT EXISTS
-------------
§12 says the map is a system, and the kit already honoured that — every dimension
in ``gen_mapkit.py`` traces to a §06 speed or a §12 rule. What it did not honour
is §05: **first person, dark, one narrow flashlight cone.** A corridor made of six
boxes is dimensionally correct and visually dead, because a flat wall gives the
beam a single flat gradient and nothing else. There is no silhouette to read, no
edge to catch a highlight, no way to tell 2 m of corridor from 8 m of it.

So every element in here earns its triangles against one of three §-backed jobs:

* **Give the §03 flashlight something to carve.** §03 makes light the lock on
  progress; §07 then takes 30% of the beam's radius away at 16 minutes. A beam
  that lands on a flat plane reads as a grey disc. A beam that crosses a skirting,
  a dado rail at 1.05 m and a pilaster every 2.5 m reads as *a corridor going
  somewhere*, and the horizontal bands are what make distance legible when the
  radius shrinks.
* **Give the player depth cues at the top of the screen.** §05 is first person, so
  the ceiling is roughly a third of the view at all times. Joists at a fixed 1.25 m
  pitch turn it into a ruler: the player counts bays without knowing they are
  counting, which is the cheapest possible distance sense in the dark.
* **Make the building continuous across a modular join.** Pipes, cable tray and
  conduit sit at fixed lanes measured from the wall face, identical in every
  corridor piece, so a service run threads from piece to piece instead of stopping
  at a dock. Without a shared constant this is the detail that most obviously
  exposes a kit as a kit.

WHAT IT DELIBERATELY DOES NOT DO
--------------------------------
No element may reduce a §12 clearance. The tightest protrusion here is the
pilaster at 0.060 m, which takes the corridor from 2.20 m to **2.08 m** clear —
the same number the door pintles already produced, and still far above the 1.80 m
§08's two-person w5 궤짝 carry needs. Nothing hangs below ``MIN_SOFFIT`` = 2.45 m
over walkable floor, against the monster's 2.34 m height. ``gen_mapkit.py``
recomputes both from these constants every run rather than trusting this note.
"""

from __future__ import annotations

import math
import os
import random
import sys
from dataclasses import dataclass
from typing import Iterable, Sequence

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402

import blendkit  # noqa: E402
from blendkit import MaterialSpec  # noqa: E402


# ── Bevel groups ────────────────────────────────────────────────────────────
#
# A hard 90° edge is one flat line under a flashlight; a chamfer is a highlight,
# and that is most of what stops primitive geometry looking like primitives. But
# one bevel width cannot serve both a 0.15 m wall and a 0.04 m skirting — a 12 mm
# chamfer on a 40 mm moulding eats the moulding. So objects are sorted into groups
# by a name prefix and each group gets its own pass.

PREFIX_FLOOR = "F_"
"""Walkable surface. Never bevelled: a chamfer at a tile seam opens a light leak."""

PREFIX_TRIM = "T_"
"""Fine trim. 4 mm chamfer — enough for a highlight, small enough to survive."""

PREFIX_RAW = "P_"
"""Already round, or too thin to chamfer at all. Pipes, slats, cables, chips."""

PREFIX_PILLAR = "C_"
"""Free-standing verticals — piers, posts, column shafts. They get a wider,
two-segment chamfer, because a pier is the one shape the beam always rakes
edge-on in a 2.5 m maze: a 12 mm arris is a hairline at half a metre, a 22 mm
double chamfer is a lit facet. This is the whole difference between "3D asset"
and "untextured block" in the near field."""

BEVEL_STRUCTURE = 0.012
BEVEL_TRIM = 0.004
BEVEL_PILLAR = 0.022
BEVEL_PILLAR_SEGMENTS = 2

MIN_BEVEL_THICKNESS = 0.06
"""Below this, a box is auto-routed to the trim group. A 12 mm offset on a 40 mm
box removes 60% of it, and on a 25 mm threshold plate it inverts the geometry."""


# ── Materials ───────────────────────────────────────────────────────────────
#
# Four families, because §12's floor channel and the art pass need different
# things from a slot:
#
#   Floor_*    contractual. FloorMaterial in Core/Map maps onto these five names
#              and MapSceneBuilder.Finish() rebinds any slot whose name starts
#              with "Floor" to the zone's surface. Nothing structural may be
#              named Floor_* or it would silently become walkable-sounding.
#   Wall_*     vertical surfaces. Split in two because §12's building is a
#              basement: the bottom metre of a basement wall does not look like
#              the top two, and a damp line at ~1 m is the single cheapest cue
#              that says "underground" instead of "grey room".
#   Ceiling_*  soffit, beams, joists.
#   Trim_*     everything that is not the building: steel, painted joinery,
#              services, hardware.
#
# Values are chosen for §03's flashlight, not for a lit render. The baseline
# render's failure was albedo near black — a beam hit a wall and nothing came
# back. Every value below sits in 0.28–0.62 so there is something for the cone to
# return, and the *range* between them is what carries shape in the dark.

MAT_SPECS: dict[str, MaterialSpec] = {
    "Floor_Wood": MaterialSpec("Floor_Wood", (0.31, 0.19, 0.10), roughness=0.76),
    "Floor_Tile": MaterialSpec("Floor_Tile", (0.58, 0.59, 0.56), roughness=0.26),
    "Floor_Gravel": MaterialSpec("Floor_Gravel", (0.34, 0.32, 0.29), roughness=0.95),
    "Floor_Concrete": MaterialSpec("Floor_Concrete", (0.42, 0.42, 0.40), roughness=0.80),
    # Metallic is kept low on purpose, here and on Trim_Steel. A fully metallic
    # surface returns almost nothing without a reflection probe, and the measured
    # baseline's headline failure was exactly that: "the beam hits a wall and the
    # wall is almost invisible". §12 needs the stair to be unmistakable, so it is
    # modelled as galvanised and worn rather than as a mirror.
    "Floor_Metal": MaterialSpec("Floor_Metal", (0.47, 0.48, 0.50), roughness=0.44, metallic=0.45),

    # Upper wall: the lightest large surface in the kit on purpose. It is what the
    # beam finds at head height, so it is what has to answer (§03).
    "Wall_Structure": MaterialSpec("Wall_Structure", (0.55, 0.54, 0.50), roughness=0.84),
    # Lower wall: the damp line. Darker and rougher, so the band reads as a value
    # step even before a texture lands on it.
    "Wall_Dado": MaterialSpec("Wall_Dado", (0.33, 0.32, 0.30), roughness=0.93),

    "Ceiling_Structure": MaterialSpec("Ceiling_Structure", (0.38, 0.37, 0.35), roughness=0.88),

    # Painted and galvanised steel, not bare: pipes, tray, gully covers and door
    # hardware have to still be *there* when the beam finds them at 6 m.
    "Trim_Steel": MaterialSpec("Trim_Steel", (0.41, 0.42, 0.44), roughness=0.50, metallic=0.25),
    # Painted joinery: warmer and lighter than the wall, so skirtings, rails and
    # door casings separate from the surface they sit on under a warm beam.
    "Trim_Painted": MaterialSpec("Trim_Painted", (0.50, 0.46, 0.39), roughness=0.62),

    "Door_Leaf": MaterialSpec("Door_Leaf", (0.30, 0.19, 0.11), roughness=0.70),
}


def mat(key: str) -> bpy.types.Material:
    """Creates or reuses a material. Safe to call after every reset_scene()."""
    return blendkit.make_material(MAT_SPECS[key])


# ── Wall elevation profile, all metres ──────────────────────────────────────
#
# One profile, used by every wall in the kit, so a corridor reads as continuous
# architecture across a dock rather than as tiles that happen to abut.

SKIRT_H = 0.20
SKIRT_D = 0.040
"""Skirting. Stops the floor/wall junction being a single black line, and gives
the beam a lit edge at exactly the height it lands when a player looks down."""

DADO_TOP = 1.02
DADO_D = 0.022
"""Top of the tanking / damp band. §12's building is a basement; this is the
water line, and it puts a value change across the middle of every wall."""

RAIL_TOP = 1.11
RAIL_D = 0.055
"""Dado rail at 1.02–1.11 m. Deliberately at chest height: §05 holds the
flashlight from the chest, so this is the one moulding the cone rakes along at a
grazing angle, and a grazing highlight is the strongest depth cue available.
Built as a two-step profile — a deep lower band and a shallower top ledge — so
the cap reads as a chamfered moulding rather than one extruded strip. The total
projection never exceeds RAIL_D, which design_checks() counts."""

PANEL_PITCH = 1.25
"""Sub-bay pitch for wall panelling, wainscot and upper field alike. Half the
2.5 m grid, and the same pitch as the ceiling joists, so vertical reveals arrive
every 1.25 m — near enough that a beam raking a wall at half a metre always has
a shadow slot inside the cone. The measured failure this fixes: at oblique
incidence the old one-panel-per-bay upper field lit as a single featureless
rectangle 2.2 m wide, which is exactly the flat-quad read of an unfinished
level."""

PANEL_D = 0.020
"""Relief of the upper bay panels. Was 0.016 and raw (unbevelled) — a hairline.
20 mm plus the 4 mm trim chamfer is a real facet under grazing light, and still
far inside the pilaster's 60 mm, so no clearance below carry height changes."""

WAINS_GAP = 0.07
"""Width of the vertical reveal slots between wainscot panels. The wall body
shows at the bottom of each slot, 22 mm deeper and a value step lighter — a
repeating hard shadow line the beam finds at any angle."""

CROWN_H = 0.07
CROWN_D = 0.030
"""Crown band at the very top of the wall, in the ceiling's darker material.
Its underside is the shadow gap where wall meets soffit: without it the
junction is a single unbroken crease and the top third of the near field is a
flat quad. 30 mm proud at 2.93 m — far above anything carried (CAP_D = 85 mm
already governs the overhead pinch)."""

FRIEZE_H = 0.10
FRIEZE_D = 0.042
"""Cornice band under the ceiling. The upper twin of the dado rail — two
horizontals converging in perspective is what tells the eye how long a corridor
is when only 4 m of it is lit (§07 shrinks the beam 30% at 16 minutes)."""

PILASTER_W = 0.30
PILASTER_D = 0.060
CAP_W = 0.38
CAP_D = 0.085
CAP_H = 0.16
"""Pilasters on the 2.5 m grid. Vertical rhythm, and the only geometry that
produces a *silhouette* in a corridor: a lit pilaster face against the unlit bay
behind it is the shape a player reads before anything else."""

MIN_SOFFIT = 2.45
"""Nothing may hang below this over walkable floor. The monster is 2.34 m
(gen_monster_model.py) and §06's speeds must never be limited by a pipe."""


# ── Service lanes, measured from the wall face ──────────────────────────────
#
# Fixed so runs thread continuously through every corridor piece. A pipe that
# stops at a dock is the single most obvious tell that a level is a kit.

PIPE_LANE = 0.34
"""Centre of the main pipe, from the wall face it runs beside."""

PIPE_R = 0.075
PIPE_Z = 2.62
"""Bottom of the main pipe = 2.545 m, clear of MIN_SOFFIT."""

TRAY_LANE = 0.30
TRAY_W = 0.22
TRAY_Z = 2.55
"""Cable tray on the opposite wall. Underside 2.55 m, clear of MIN_SOFFIT."""

CONDUIT_R = 0.020
CONDUIT_Z = 1.55
"""Small conduit dropping to junction boxes, run at hand height along the upper
wall field where it will not foul the dado rail."""


# ── Primitives ──────────────────────────────────────────────────────────────


def slab(name: str, x0: float, x1: float, y0: float, y1: float,
         z0: float, z1: float, material: bpy.types.Material,
         floor: bool = False, trim: bool | None = None,
         raw: bool = False, pillar: bool = False) -> bpy.types.Object:
    """A box given as an axis-aligned extent rather than centre+size.

    Every dimension in §12 is expressed as "from here to there", so building from
    extents is the only way to keep the script readable against the design doc.

    The prefix decides which bevel pass the box lands in. ``trim=None`` means
    *decide by thickness*, which is the safe default: a box thinner than
    ``MIN_BEVEL_THICKNESS`` cannot survive the structural chamfer, and a silently
    inverted moulding is far harder to notice in a render than a missing one.
    """
    sx, sy, sz = x1 - x0, y1 - y0, z1 - z0
    if min(sx, sy, sz) <= 1e-6:
        raise ValueError(f"{name}: degenerate box {sx:.4f}x{sy:.4f}x{sz:.4f}")

    if floor:
        prefix = PREFIX_FLOOR
    elif pillar:
        prefix = PREFIX_PILLAR
    elif raw:
        prefix = PREFIX_RAW
    elif trim is True or (trim is None and min(sx, sy, sz) < MIN_BEVEL_THICKNESS):
        prefix = PREFIX_TRIM
    else:
        prefix = ""

    obj = blendkit.add_box(prefix + name, (sx, sy, sz),
                           ((x0 + x1) / 2.0, (y0 + y1) / 2.0, (z0 + z1) / 2.0))
    blendkit.assign_material(obj, material)
    return obj


_AXIS_INDEX = {"X": 0, "Y": 1, "Z": 2}


def pipe(name: str, axis: str, a0: float, a1: float, u: float, v: float,
         radius: float, material: bpy.types.Material, sides: int = 8) -> bpy.types.Object:
    """An axis-aligned tube, built directly in bmesh.

    ``(u, v)`` is the centre on the other two axes in cyclic order — X→(y, z),
    Y→(x, z), Z→(x, y). Eight sides costs 28 triangles and is indistinguishable
    from sixteen in a flashlight cone (§05), which is the whole argument for
    spending the saving on more *runs* instead of rounder ones.
    """
    idx = _AXIS_INDEX[axis]
    other = [(1, 2), (0, 2), (0, 1)][idx]

    mesh = bpy.data.meshes.new(PREFIX_RAW + name + "_M")
    obj = bpy.data.objects.new(PREFIX_RAW + name, mesh)
    bpy.context.collection.objects.link(obj)

    bm = bmesh.new()
    rings = []
    for end in (a0, a1):
        ring = []
        for k in range(sides):
            ang = 2.0 * math.pi * (k + 0.5) / sides
            co = [0.0, 0.0, 0.0]
            co[idx] = end
            co[other[0]] = u + radius * math.cos(ang)
            co[other[1]] = v + radius * math.sin(ang)
            ring.append(bm.verts.new(co))
        rings.append(ring)
    for k in range(sides):
        n = (k + 1) % sides
        bm.faces.new((rings[0][k], rings[0][n], rings[1][n], rings[1][k]))
    bm.faces.new(tuple(reversed(rings[0])))
    bm.faces.new(tuple(rings[1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    blendkit.assign_material(obj, material)
    return obj


# ── UV projection ───────────────────────────────────────────────────────────


def uv_box_project(obj: bpy.types.Object, metres_per_tile: float = 2.0) -> None:
    """World-aligned box projection at a fixed texel density.

    Smart-project was what the kit used, and it is the wrong tool for a modular
    building: it packs islands to minimise waste, so texel density varies per face
    and a tiling concrete texture arrives at a different scale and rotation on
    every wall. Two pieces docked together then show a seam that no texture can
    fix.

    A box projection keyed to world position instead means one metre of wall is
    always the same number of texels, grain runs the same way along a corridor,
    and a texture crosses a dock without registering. It also removes an operator
    and two mode switches from the headless path, which matters because
    ``--background`` reports a failed operator as a silent no-op.

    Runs *after* ``origin_set`` so object space equals world space.
    """
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    uv_layer = bm.loops.layers.uv.verify()
    s = 1.0 / metres_per_tile

    for face in bm.faces:
        n = face.normal
        axis = max(range(3), key=lambda i: abs(n[i]))
        for loop in face.loops:
            co = loop.vert.co
            if axis == 0:
                u, v = (-co.y if n.x > 0.0 else co.y), co.z
            elif axis == 1:
                u, v = (co.x if n.y > 0.0 else -co.x), co.z
            else:
                u, v = co.x, (co.y if n.z > 0.0 else -co.y)
            loop[uv_layer].uv = (u * s, v * s)

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


# ── Wall dressing ───────────────────────────────────────────────────────────


@dataclass(frozen=True)
class Wall:
    """A wall plane, resolved from a box plus which side the room is on."""

    run: str
    """The axis the wall runs along, "X" or "Y"."""
    a0: float
    a1: float
    face: float
    """Coordinate of the finished (room-side) face, on the cross axis."""
    back: float
    """Coordinate of the back of the structural body."""
    inward: int
    """+1 if the room lies at higher cross-axis values, −1 otherwise."""


def wall_of(x0: float, x1: float, y0: float, y1: float, room: str) -> Wall:
    """Resolves a wall box plus a room side into a :class:`Wall`."""
    if room in ("+X", "-X"):
        face, back = (x1, x0) if room == "+X" else (x0, x1)
        return Wall("Y", y0, y1, face, back, 1 if room == "+X" else -1)
    if room in ("+Y", "-Y"):
        face, back = (y1, y0) if room == "+Y" else (y0, y1)
        return Wall("X", x0, x1, face, back, 1 if room == "+Y" else -1)
    raise ValueError(f"room must be one of +X/-X/+Y/-Y, got {room!r}")


def wall_box(wall: Wall, name: str, a0: float, a1: float, d0: float, d1: float,
             z0: float, z1: float, material: bpy.types.Material,
             trim: bool | None = None, raw: bool = False) -> bpy.types.Object:
    """A box positioned in wall-local terms: along the run, out from the face.

    ``d`` is depth measured from the finished face, positive *into the room*. That
    single sign convention is what lets one elevation description serve all four
    wall orientations without a rotation matrix anywhere.

    ``d0 == 0.0`` — a box whose back sits exactly on the wall face — is sunk 6 mm
    into the wall body instead. A back face coincident with the face it mounts on
    exports two coplanar surfaces into one joined mesh, and the round-1 renders of
    this pass showed what that costs: clean-edged black voids down the Doorway
    piers and along the ceiling edge beams, where the renderer resolved the
    coincidence arbitrarily. The 6 mm is invisible (it is inside solid wall) and
    the projection into the room is unchanged.
    """
    if abs(d0) < 1e-9:
        d0 = -0.006
    c0 = wall.face + wall.inward * d0
    c1 = wall.face + wall.inward * d1
    lo, hi = min(c0, c1), max(c0, c1)
    # Callers work in "left of centre / right of centre" terms, which reverses on a
    # wall whose inward normal is negative. Ordering here rather than at every call
    # site is what keeps the elevation code orientation-free.
    a0, a1 = min(a0, a1), max(a0, a1)
    if wall.run == "Y":
        return slab(name, lo, hi, a0, a1, z0, z1, material, trim=trim, raw=raw)
    return slab(name, a0, a1, lo, hi, z0, z1, material, trim=trim, raw=raw)


Opening = tuple[float, float, float, float]
"""``(a0, a1, z0, z1)`` — a hole in a wall, in world coordinates along the run."""


def _clip(a0: float, a1: float, z0: float, z1: float,
          openings: Sequence[Opening]) -> Iterable[tuple[float, float]]:
    """Sub-runs of ``[a0, a1]`` left after removing openings that reach into
    ``[z0, z1]``."""
    blockers = sorted((o[0], o[1]) for o in openings
                      if o[3] > z0 + 1e-6 and o[2] < z1 - 1e-6)
    cursor = a0
    for b0, b1 in blockers:
        if b0 > cursor + 1e-6:
            yield (cursor, min(b0, a1))
        cursor = max(cursor, b1)
    if cursor < a1 - 1e-6:
        yield (cursor, a1)


def _blocked(a: float, half: float, openings: Sequence[Opening]) -> bool:
    """Whether a pilaster of half-width ``half`` centred at ``a`` hits an opening."""
    return any(o[0] - half < a < o[1] + half for o in openings)


def dressed_wall(
    name: str,
    x0: float, x1: float, y0: float, y1: float,
    room: str,
    z_top: float,
    openings: Sequence[Opening] = (),
    z_base: float = 0.0,
    pilaster_pitch: float = 0.0,
    pilaster_phase: float = 1.25,
    rail: bool = True,
    frieze: bool = True,
    dado: bool = True,
    wear_seed: int = 0,
) -> list:
    """A wall with an elevation instead of a face.

    Emits, from the bottom: the structural body (with openings cut as sill and
    head), a skirting, the damp/tanking band, the dado rail at chest height, a
    cornice band under the ceiling, and pilasters on the 2.5 m grid.

    Why each band is where it is, is in this module's constant docstrings — the
    short version is that §05 puts a narrow cone in the player's hand and §07
    shrinks it, so a wall has to be legible from a 3 m disc of light. Horizontal
    bands give the disc something to cross; pilasters give it something to end on.

    ``wear_seed`` is a fixed seed, never a clock: §12 fixes 맵 구조 so the map is
    learnable, and a rebuild that moved the chips would make two runs of the same
    level look like different buildings.
    """
    wall = wall_of(x0, x1, y0, y1, room)
    body_d = -abs(wall.face - wall.back)
    objs: list = []
    w = mat("Wall_Structure")

    # ── Structural body, openings cut as sill and head ──
    for i, (s0, s1) in enumerate(_clip(wall.a0, wall.a1, z_base, z_top, openings)):
        objs.append(wall_box(wall, f"{name}_body{i}", s0, s1, body_d, 0.0,
                             z_base, z_top, w, trim=False))
    for i, (oa0, oa1, oz0, oz1) in enumerate(openings):
        if oz0 > z_base + 1e-6:
            objs.append(wall_box(wall, f"{name}_sill{i}", oa0, oa1, body_d, 0.0,
                                 z_base, oz0, w, trim=False))
        if oz1 < z_top - 1e-6:
            objs.append(wall_box(wall, f"{name}_head{i}", oa0, oa1, body_d, 0.0,
                                 oz1, z_top, w, trim=False))

    dado_top = min(DADO_TOP, z_top - 0.20)
    rail_top = min(RAIL_TOP, z_top - 0.12)
    frieze_z0 = z_top - 0.60
    frieze_z1 = frieze_z0 + FRIEZE_H

    # ── Skirting ──
    for i, (s0, s1) in enumerate(_clip(wall.a0, wall.a1, z_base, z_base + SKIRT_H, openings)):
        objs.append(wall_box(wall, f"{name}_skirt{i}", s0, s1, 0.0, SKIRT_D,
                             z_base, z_base + SKIRT_H, mat("Trim_Painted")))

    # ── Wainscot: the damp band, panelled ──
    #
    # It used to be one flat strip 0.022 proud, and the round-1 beam test showed
    # what that is worth up close: nothing. The band is now emitted as panels at
    # PANEL_PITCH with WAINS_GAP reveal slots between them, so the wall body
    # shows at the bottom of each slot — a 22 mm-deep, 70 mm-wide vertical
    # shadow line every 1.25 m at exactly the height a lowered beam rakes.
    # Projection unchanged at DADO_D, so no clearance moves.
    if dado and dado_top > z_base + SKIRT_H + 0.05:
        for i, (s0, s1) in enumerate(_clip(wall.a0, wall.a1, z_base + SKIRT_H,
                                           dado_top, openings)):
            n = max(1, int(round((s1 - s0) / PANEL_PITCH)))
            for k in range(n):
                p0 = s0 + k * (s1 - s0) / n + (WAINS_GAP / 2 if k else 0.0)
                p1 = s0 + (k + 1) * (s1 - s0) / n - (WAINS_GAP / 2 if k < n - 1 else 0.0)
                if p1 - p0 < 0.15:
                    continue
                objs.append(wall_box(wall, f"{name}_dado{i}_{k}", p0, p1, 0.0,
                                     DADO_D, z_base + SKIRT_H, dado_top,
                                     mat("Wall_Dado")))

    # ── Wainscot cap rail, two-step profile ──
    if rail and rail_top > dado_top + 0.02:
        step_z = dado_top + (rail_top - dado_top) * 0.6
        for i, (s0, s1) in enumerate(_clip(wall.a0, wall.a1, dado_top, rail_top, openings)):
            objs.append(wall_box(wall, f"{name}_rail{i}", s0, s1, 0.0, RAIL_D,
                                 dado_top, step_z, mat("Trim_Painted")))
            objs.append(wall_box(wall, f"{name}_railcap{i}", s0, s1, 0.0,
                                 RAIL_D - 0.018, step_z, rail_top,
                                 mat("Trim_Painted")))

    # ── Cornice band ──
    if frieze and frieze_z0 > rail_top + 0.30:
        for i, (s0, s1) in enumerate(_clip(wall.a0, wall.a1, frieze_z0, frieze_z1, openings)):
            objs.append(wall_box(wall, f"{name}_frieze{i}", s0, s1, 0.0, FRIEZE_D,
                                 frieze_z0, frieze_z1, mat("Trim_Painted")))

    # ── Bay panels ──
    #
    # Raised fields between the rail and the cornice, at PANEL_PITCH. The first
    # version was one raw 16 mm panel per 2.5 m bay, and the round-1 beam test
    # measured its worth in the near field: a raked 2.2 m rectangle of one value,
    # indistinguishable from a bare quad. Split at 1.25 m with 90 mm reveals and
    # a 4 mm chamfer on every edge (trim group, not raw), the same wall gives the
    # beam a lit facet and a shadow slot inside every cone-width.
    panel_z0 = rail_top + 0.10
    # Cap the field height: on a 6 m hall wall a single 4 m panel is a
    # skyscraper, and it would run through the gallery string course.
    panel_z1 = min(frieze_z0 - 0.09, z_base + 2.9)
    if panel_z1 > panel_z0 + 0.25:
        inset = PILASTER_W / 2.0 + 0.13
        stations = []
        if pilaster_pitch > 0.0:
            a = wall.a0 + pilaster_phase
            while a < wall.a1 - 1e-6:
                stations.append(a)
                a += pilaster_pitch
        # A wall with no pilasters still gets panels across its whole run. Those
        # are the closing walls at corners and dead ends — the surface a player is
        # looking straight at when the corridor stops, and the last place in the
        # kit that can afford to be a blank rectangle.
        edges = [wall.a0] + stations + [wall.a1]
        for k in range(len(edges) - 1):
            b0 = edges[k] + (inset if k > 0 else 0.13)
            b1 = edges[k + 1] - (inset if k < len(edges) - 2 else 0.13)
            if b1 - b0 < 0.35:
                continue
            n = max(1, int(round((b1 - b0) / PANEL_PITCH)))
            for j in range(n):
                p0 = b0 + j * (b1 - b0) / n + (0.045 if j else 0.0)
                p1 = b0 + (j + 1) * (b1 - b0) / n - (0.045 if j < n - 1 else 0.0)
                if p1 - p0 < 0.30 or _blocked((p0 + p1) / 2, (p1 - p0) / 2, openings):
                    continue
                objs.append(wall_box(wall, f"{name}_panel{k}_{j}", p0, p1, 0.0,
                                     PANEL_D, panel_z0, panel_z1, w, trim=True))

    # ── Crown shadow gap ──
    #
    # The top of the wall, in the ceiling's darker material, with its underside
    # throwing a shadow line across the wall head. The 0.5 m above the cornice
    # band was the last strip of bare quad on a dressed wall, and it is the strip
    # the beam crosses every time a player checks the ceiling.
    if frieze and z_top - CROWN_H > frieze_z1 + 0.15:
        for i, (s0, s1) in enumerate(_clip(wall.a0, wall.a1, z_top - CROWN_H,
                                           z_top, openings)):
            objs.append(wall_box(wall, f"{name}_crown{i}", s0, s1, 0.0, CROWN_D,
                                 z_top - CROWN_H, z_top, mat("Ceiling_Structure")))

    # ── Pilasters ──
    if pilaster_pitch > 0.0:
        half = PILASTER_W / 2.0
        a = wall.a0 + pilaster_phase
        k = 0
        while a < wall.a1 - 1e-6:
            if (a - half >= wall.a0 - 1e-6 and a + half <= wall.a1 + 1e-6
                    and not _blocked(a, half + 0.05, openings)):
                objs.append(wall_box(wall, f"{name}_pil{k}", a - half, a + half,
                                     0.0, PILASTER_D, z_base, frieze_z1, w, trim=False))
                objs.append(wall_box(wall, f"{name}_pilcap{k}", a - CAP_W / 2.0,
                                     a + CAP_W / 2.0, 0.0, CAP_D,
                                     frieze_z1, frieze_z1 + CAP_H, mat("Trim_Painted")))
                # A pilaster base reads as load being carried into the floor. It
                # is also the first thing a beam finds when a player looks down.
                objs.append(wall_box(wall, f"{name}_pilbase{k}", a - CAP_W / 2.0,
                                     a + CAP_W / 2.0, 0.0, CAP_D - 0.01,
                                     z_base, z_base + SKIRT_H, mat("Trim_Painted")))
            a += pilaster_pitch
            k += 1

    if wear_seed:
        objs += wall_wear(f"{name}_wear", wall, z_base, min(z_top, dado_top + 1.4),
                          wear_seed, openings)
    return objs


def wall_wear(name: str, wall: Wall, z_base: float, z_top: float,
              seed: int, openings: Sequence[Opening] = ()) -> list:
    """Patched render, spalled corners and a run of drip staining.

    §07 turns the night against the player and §03 makes reading a surface the
    objective itself. Both want the building to look *used* — a wall that is
    uniformly perfect gives the eye nothing to fix on, so a beam sweeping it
    produces no sense of movement. These are the cheapest possible marks that
    break uniformity: proud rectangles of patch mortar, small angled shards where
    a corner has spalled, and vertical drip lines under the cornice.

    Deterministic by seed, for the same reason ``dressed_wall`` is (§12: 맵 구조 고정).
    """
    rng = random.Random(seed)
    objs: list = []
    span = wall.a1 - wall.a0
    if span < 1.0:
        return objs

    def free(a: float, half: float) -> bool:
        return not _blocked(a, half, openings)

    # Patches of newer mortar over the damp band: a value change, no silhouette.
    for k in range(max(1, int(span // 5.0))):
        a = wall.a0 + rng.uniform(0.4, max(0.5, span - 0.4))
        w_ = rng.uniform(0.45, 1.05)
        if not free(a, w_ / 2.0 + 0.05) or a - w_ / 2 < wall.a0 or a + w_ / 2 > wall.a1:
            continue
        z0 = rng.uniform(z_base + 0.24, min(z_base + 0.75, z_top - 0.4))
        objs.append(wall_box(wall, f"{name}_patch{k}", a - w_ / 2, a + w_ / 2,
                             DADO_D, DADO_D + 0.012, z0, z0 + rng.uniform(0.30, 0.55),
                             mat("Wall_Structure"), raw=True))

    # Spalled corners: small shards proud of the skirting, where a trolley or a
    # carried 궤짝 (§08) would actually have hit it.
    for k in range(max(2, int(span // 4.0))):
        a = wall.a0 + rng.uniform(0.3, max(0.4, span - 0.3))
        w_ = rng.uniform(0.10, 0.26)
        if not free(a, w_) or a - w_ < wall.a0 or a + w_ > wall.a1:
            continue
        z0 = z_base + rng.uniform(0.02, 0.16)
        objs.append(wall_box(wall, f"{name}_chip{k}", a - w_ / 2, a + w_ / 2,
                             SKIRT_D - rng.uniform(0.010, 0.028), SKIRT_D + 0.004,
                             z0, z0 + rng.uniform(0.03, 0.09),
                             mat("Wall_Dado"), raw=True))

    # Drip lines below the cornice, where water tracks down a basement wall.
    for k in range(max(1, int(span // 6.0))):
        a = wall.a0 + rng.uniform(0.5, max(0.6, span - 0.5))
        if not free(a, 0.1):
            continue
        objs.append(wall_box(wall, f"{name}_drip{k}", a - 0.035, a + 0.035,
                             0.0, 0.009, z_top - rng.uniform(0.9, 1.5), z_top,
                             mat("Wall_Dado"), raw=True))
    return objs


# ── Ceiling ─────────────────────────────────────────────────────────────────


def framed_ceiling(name: str, x0: float, x1: float, y0: float, y1: float,
                   ceiling: float, span: str, slab_t: float = 0.15,
                   joist_pitch: float = 1.25, joist_w: float = 0.11,
                   joist_d: float = 0.19, edge_d: float = 0.15,
                   edge_w: float = 0.20) -> list:
    """Soffit slab, edge beams over the wall heads, and joists at a fixed pitch.

    §05 is first person, so the ceiling is about a third of the screen the whole
    match and right now it is a plane. Joists at a **fixed** 1.25 m pitch make it a
    ruler: a player walking a corridor watches bays pass overhead and gets a
    distance sense that costs nothing to learn and works with the flashlight
    pointed at the floor. That is worth far more here than any amount of ceiling
    texture, because §03 keeps the light low and forward.

    ``span`` is the axis the joists span across — the short way, into the walls,
    which is also how the building would actually be framed. Joist soffits sit at
    ``ceiling - joist_d``; the caller is responsible for keeping that above
    ``MIN_SOFFIT``, and ``gen_mapkit.py`` asserts it.
    """
    objs: list = []
    c = mat("Ceiling_Structure")
    objs.append(slab(f"{name}_slab", x0, x1, y0, y1, ceiling, ceiling + slab_t, c,
                     trim=False))

    run = "Y" if span == "X" else "X"
    r0, r1 = (y0, y1) if run == "Y" else (x0, x1)
    s0, s1 = (x0, x1) if span == "X" else (y0, y1)

    # Edge beams: the joists have to land on something, and the line where the
    # ceiling meets the wall is otherwise a single unbroken crease. Their outer
    # face is inset 20 mm from the piece extent — it lands buried inside the
    # 150 mm wall body either way, and flush with the extent it exported a face
    # coplanar with the wall's own back, which round 1 rendered as a black band.
    for tag, e0, e1 in (("A", s0 + 0.02, s0 + edge_w), ("B", s1 - edge_w, s1 - 0.02)):
        if span == "X":
            objs.append(slab(f"{name}_edge{tag}", e0, e1, r0, r1,
                             ceiling - edge_d, ceiling, c, trim=False))
        else:
            objs.append(slab(f"{name}_edge{tag}", r0, r1, e0, e1,
                             ceiling - edge_d, ceiling, c, trim=False))

    # Joist ends stop 20 mm inside the piece extent, for the same reason as the
    # edge beams: they die into 150 mm wall bodies either way, and run to the
    # extent they exported end faces on the boundary plane — round 2's studio
    # render showed them as black slots punched through the wall head.
    n = max(1, int(round((r1 - r0) / joist_pitch)))
    pitch = (r1 - r0) / n
    for k in range(n):
        cpos = r0 + (k + 0.5) * pitch
        if span == "X":
            objs.append(slab(f"{name}_joist{k}", s0 + 0.02, s1 - 0.02,
                             cpos - joist_w / 2, cpos + joist_w / 2,
                             ceiling - joist_d, ceiling, c))
        else:
            objs.append(slab(f"{name}_joist{k}", cpos - joist_w / 2,
                             cpos + joist_w / 2, s0 + 0.02, s1 - 0.02,
                             ceiling - joist_d, ceiling, c))
    return objs


# ── Services ────────────────────────────────────────────────────────────────


def soffit_conduit(name: str, axis: str, a0: float, a1: float, cross: float,
                   ceiling: float, count: int = 2,
                   radius: float = 0.018, clip_pitch: float = 1.25) -> list:
    """A pair of small conduits clipped to the ceiling soffit, run the length of
    a corridor as if drilled through the joists.

    §05 makes the ceiling a third of the screen, and the up-glance between the
    joists was bare slab — the round-1 beam_up render is two blank trapezoids.
    Two parallel runs with saddle clips at the joist pitch give the glance the
    same scrolling distance cue the wall services give the forward view. Bottom
    of the run sits at ~ceiling − 0.06, far above MIN_SOFFIT.
    """
    objs: list = []
    st = mat("Trim_Steel")
    for k in range(count):
        c = cross + k * (2 * radius + 0.030)
        z = ceiling - radius - 0.024
        objs.append(pipe(f"{name}_c{k}", axis, a0, a1, c, z, radius, st, sides=6))
    span_c = (count - 1) * (2 * radius + 0.030)
    n = max(1, int((a1 - a0) / clip_pitch))
    for k in range(n):
        a = a0 + (k + 0.5) * (a1 - a0) / n
        lo = cross - radius - 0.016
        hi = cross + span_c + radius + 0.016
        z0 = ceiling - 2 * radius - 0.036
        if axis == "X":
            objs.append(slab(f"{name}_clip{k}", a - 0.014, a + 0.014, lo, hi,
                             z0, ceiling + 0.01, st, raw=True))
        else:
            objs.append(slab(f"{name}_clip{k}", lo, hi, a - 0.014, a + 0.014,
                             z0, ceiling + 0.01, st, raw=True))
    return objs


def pipe_run(name: str, axis: str, a0: float, a1: float, cross: float,
             z: float = PIPE_Z, radius: float = PIPE_R,
             flange_pitch: float = 2.5) -> list:
    """A pipe with couplings, hung off the soffit.

    The couplings are the point. A bare cylinder reads as a cylinder; a cylinder
    interrupted every 2.5 m by a slightly fatter collar reads as *plumbing*, and
    it gives the beam a repeating highlight that scrolls past as the player walks
    — the same distance cue as the joists, on a different axis.
    """
    objs: list = []
    st = mat("Trim_Steel")
    u, v = (cross, z)
    objs.append(pipe(f"{name}_tube", axis, a0, a1, u, v, radius, st))

    n = max(1, int((a1 - a0) / flange_pitch))
    for k in range(n):
        a = a0 + (k + 0.5) * (a1 - a0) / n
        objs.append(pipe(f"{name}_collar{k}", axis, a - 0.045, a + 0.045, u, v,
                         radius + 0.022, st, sides=8))
        # Hanger: a strap up to the soffit. Thin, so it never chamfers.
        if axis == "X":
            objs.append(slab(f"{name}_hang{k}", a - 0.018, a + 0.018,
                             cross - 0.018, cross + 0.018, z, z + 0.44, st, raw=True))
        else:
            objs.append(slab(f"{name}_hang{k}", cross - 0.018, cross + 0.018,
                             a - 0.018, a + 0.018, z, z + 0.44, st, raw=True))
    return objs


def cable_tray(name: str, axis: str, a0: float, a1: float, cross: float,
               z: float = TRAY_Z, width: float = TRAY_W,
               bracket_pitch: float = 2.5, cables: int = 3,
               wall_face: float | None = None) -> list:
    """A U-section tray with cables in it, bracketed off the wall.

    §03 hangs the whole objective on light, and §04's Engineer restores it from a
    panel. Cable threading the corridors is the visible half of that system: it
    tells the player, without a line of dialogue, that the lights in this building
    are wired to somewhere and that somewhere can be found.
    """
    objs: list = []
    st = mat("Trim_Steel")
    half = width / 2.0
    depth = 0.075

    def box(tag: str, c0: float, c1: float, z0: float, z1: float, raw: bool = False):
        if axis == "X":
            objs.append(slab(f"{name}_{tag}", a0, a1, cross + c0, cross + c1,
                             z0, z1, st, raw=raw))
        else:
            objs.append(slab(f"{name}_{tag}", cross + c0, cross + c1, a0, a1,
                             z0, z1, st, raw=raw))

    box("base", -half, half, z, z + 0.012, raw=True)
    box("lipA", -half, -half + 0.010, z, z + depth, raw=True)
    box("lipB", half - 0.010, half, z, z + depth, raw=True)

    for k in range(cables):
        off = -half + 0.045 + k * (width - 0.09) / max(1, cables - 1)
        u, v = (cross + off, z + 0.030)
        objs.append(pipe(f"{name}_cable{k}", axis, a0, a1, u, v, 0.018, st, sides=6))

    # Brackets reach back to the wall the tray is carried on. A tray floating in
    # mid-air is the classic modular-kit tell; a cantilever bracket says the wall
    # is holding it up, which is the whole reason the run reads as a building.
    # They overreach the wall face by 8 mm — buried in solid wall — because a
    # bracket ending exactly on the face is a coplanar pair (see wall_box).
    b_lo = min(cross - half,
               wall_face - 0.008 if wall_face is not None else cross - half)
    b_hi = max(cross + half,
               wall_face + 0.008 if wall_face is not None else cross + half)
    n = max(1, int((a1 - a0) / bracket_pitch))
    for k in range(n):
        a = a0 + (k + 0.5) * (a1 - a0) / n
        if axis == "X":
            objs.append(slab(f"{name}_brk{k}", a - 0.02, a + 0.02,
                             b_lo, b_hi, z - 0.05, z, st, raw=True))
        else:
            objs.append(slab(f"{name}_brk{k}", b_lo, b_hi,
                             a - 0.02, a + 0.02, z - 0.05, z, st, raw=True))
    return objs


def wall_vent(name: str, wall: Wall, a_centre: float, z_centre: float,
              width: float = 0.46, height: float = 0.32, slats: int = 5) -> list:
    """A louvred vent set into a wall.

    Vents are placed high, where the beam catches them when a player sweeps up to
    check a corridor's length. The louvres are what matter: five thin slats at an
    offset produce five hard shadow lines from any angle, which is a lot of
    apparent detail for 60 triangles.
    """
    objs: list = []
    st = mat("Trim_Steel")
    a0, a1 = a_centre - width / 2, a_centre + width / 2
    z0, z1 = z_centre - height / 2, z_centre + height / 2

    # Recessed box, then a proud frame around it.
    objs.append(wall_box(wall, f"{name}_back", a0, a1, -0.05, -0.03, z0, z1,
                         mat("Wall_Dado"), raw=True))
    for tag, ba0, ba1, bz0, bz1 in (
        ("L", a0 - 0.035, a0, z0 - 0.035, z1 + 0.035),
        ("R", a1, a1 + 0.035, z0 - 0.035, z1 + 0.035),
        ("B", a0, a1, z0 - 0.035, z0),
        ("T", a0, a1, z1, z1 + 0.035),
    ):
        objs.append(wall_box(wall, f"{name}_frame{tag}", ba0, ba1, -0.03, 0.030,
                             bz0, bz1, st))
    for k in range(slats):
        sz = z0 + (k + 0.5) * height / slats
        objs.append(wall_box(wall, f"{name}_slat{k}", a0, a1, -0.030, 0.014,
                             sz - 0.020, sz + 0.020, st, raw=True))
    return objs


def junction_box(name: str, wall: Wall, a_centre: float, z_centre: float = 1.55,
                 drop_to: float | None = None) -> list:
    """A steel junction box with conduit stubs, optionally dropping to the floor.

    §12 puts 전기 패널 구역당 1개; this is the small change between panels, and it
    is what makes the conduit runs look like they are going somewhere rather than
    decorating the wall.
    """
    objs: list = []
    st = mat("Trim_Steel")
    objs.append(wall_box(wall, f"{name}_box", a_centre - 0.11, a_centre + 0.11,
                         0.0, 0.085, z_centre - 0.14, z_centre + 0.14, st))
    objs.append(wall_box(wall, f"{name}_lid", a_centre - 0.095, a_centre + 0.095,
                         0.085, 0.100, z_centre - 0.125, z_centre + 0.125, st, raw=True))
    for k, side in enumerate((-1, 1)):
        objs.append(wall_box(wall, f"{name}_gland{k}",
                             a_centre + side * 0.11, a_centre + side * 0.15,
                             0.018, 0.058, z_centre - 0.022, z_centre + 0.022,
                             st, raw=True))
    if drop_to is not None:
        objs.append(wall_box(wall, f"{name}_drop", a_centre - 0.020, a_centre + 0.020,
                             0.014, 0.054, drop_to, z_centre - 0.14, st, raw=True))
    return objs


def conduit_along(name: str, wall: Wall, a0: float, a1: float,
                  z: float = CONDUIT_Z, radius: float = CONDUIT_R,
                  count: int = 2, clip_pitch: float = 1.25) -> list:
    """Small conduits clipped to a wall face, with saddle clips.

    Runs at ``CONDUIT_Z`` in the upper wall field so it never fouls the dado rail
    and never narrows the corridor by more than its own radius.
    """
    objs: list = []
    st = mat("Trim_Steel")
    for k in range(count):
        # Stack the conduits vertically rather than side by side: a vertical pair
        # throws two separate shadow lines down the wall, a horizontal pair reads
        # as one thick bar once the beam is more than a couple of metres away.
        cross = wall.face + wall.inward * (radius + 0.014)
        zc = z + k * (2 * radius + 0.018)
        # pipe() takes the two non-run axes in cyclic order: Y→(x, z), X→(y, z).
        objs.append(pipe(f"{name}_c{k}", wall.run, a0, a1, cross, zc, radius,
                         st, sides=6))

    span_z = (count - 1) * (2 * radius + 0.018)
    n = max(1, int((a1 - a0) / clip_pitch))
    for k in range(n):
        a = a0 + (k + 0.5) * (a1 - a0) / n
        objs.append(wall_box(wall, f"{name}_clip{k}", a - 0.014, a + 0.014,
                             0.0, radius * 2 + 0.050,
                             z - radius - 0.026, z + span_z + radius + 0.026,
                             st, raw=True))
    return objs


# ── Floor ───────────────────────────────────────────────────────────────────


CAMBER = 0.012
"""Transverse fall of a basement slab towards its channel, over ~1.1 m.

Deliberately small. A big camber makes a dramatic dish but it fights everything
laid on the floor — a control joint or a drain cover has to be either buried at
the walls or floating at the centre. 12 mm is what a real fall actually is, it
still catches a grazing highlight along the crown, and it leaves the slab flat
enough that flush detail stays flush."""


def floor_joints(name: str, x0: float, x1: float, y0: float, y1: float,
                 run_axis: str = "Y", pitch: float = 2.5,
                 width: float = 0.024) -> list:
    """Saw-cut control joints: one down the channel line, the rest across it.

    The single highest-value-per-triangle detail in the kit. A corridor floor lit
    by a torch is otherwise one smooth gradient with no information in it; a joint
    running away from the player converges to the vanishing point and instantly
    says how long the corridor is, and the transverse cuts at the same 2.5 m pitch
    as the grid give a step count. §07 shrinks the flashlight radius 30% at
    16 minutes — this is exactly the cue that survives that.

    Cut into the slab, top at z = 0, so nothing stands proud and the joints stay
    flush across a dock.
    """
    objs: list = []
    m = mat("Wall_Dado")
    run_lo, run_hi = (y0, y1) if run_axis == "Y" else (x0, x1)
    cross_lo, cross_hi = (x0, x1) if run_axis == "Y" else (y0, y1)
    centre = (cross_lo + cross_hi) / 2.0

    def cut(tag: str, a0: float, a1: float, c0: float, c1: float) -> None:
        if run_axis == "Y":
            objs.append(slab(f"{name}_{tag}", c0, c1, a0, a1, -0.022, -0.0005,
                             m, floor=True))
        else:
            objs.append(slab(f"{name}_{tag}", a0, a1, c0, c1, -0.022, -0.0005,
                             m, floor=True))

    cut("spine", run_lo, run_hi, centre - width / 2, centre + width / 2)
    n = max(1, int(round((run_hi - run_lo) / pitch)))
    for k in range(1, n):
        a = run_lo + k * (run_hi - run_lo) / n
        cut(f"x{k}", a - width / 2, a + width / 2, cross_lo, cross_hi)
    return objs


def floor_pan(name: str, x0: float, x1: float, y0: float, y1: float,
              material: bpy.types.Material, depth: float = 0.15,
              camber: float = CAMBER, channel_axis: str = "Y",
              channel_at: float | None = None, taper: float = 0.35,
              cross_divisions: int = 8, run_step: float = 1.25) -> list:
    """A floor slab with a transverse camber falling to a drainage channel.

    §12's building is a basement, and a basement floor is never flat — it falls to
    a channel so water goes somewhere. That is the in-fiction reason. The reason
    it is worth the triangles is §05: a dead-flat plane lit by a torch is a single
    smooth gradient with no information in it, whereas a dished floor puts a soft
    highlight along the crown either side of the channel that moves as the player
    moves. It is the difference between a floor and a texture on nothing.

    **The camber tapers back to flat over the last ``taper`` metres at each end of
    the run**, so every dock edge is exactly z = 0 across its full width. Without
    that, a cambered corridor butting a flat hall would show a 30 mm step at the
    threshold, and §12's grid promises pieces that simply abut.
    """
    if channel_at is None:
        channel_at = (x0 + x1) / 2.0 if channel_axis == "Y" else (y0 + y1) / 2.0

    run_lo, run_hi = (y0, y1) if channel_axis == "Y" else (x0, x1)
    cross_lo, cross_hi = (x0, x1) if channel_axis == "Y" else (y0, y1)

    cross = [cross_lo + (cross_hi - cross_lo) * k / cross_divisions
             for k in range(cross_divisions + 1)]
    # Force stations either side of the channel so the dish has a defined bottom.
    for extra in (channel_at - 0.16, channel_at, channel_at + 0.16):
        if cross_lo + 1e-4 < extra < cross_hi - 1e-4:
            cross.append(extra)
    cross = sorted(set(round(v, 5) for v in cross))

    run = [run_lo, run_lo + taper]
    n = max(1, int(round((run_hi - taper - (run_lo + taper)) / run_step)))
    for k in range(1, n):
        run.append(run_lo + taper + k * (run_hi - taper - (run_lo + taper)) / n)
    run += [run_hi - taper, run_hi]
    run = sorted(set(round(v, 5) for v in run if run_lo - 1e-4 <= v <= run_hi + 1e-4))

    half_lo = max(1e-6, channel_at - cross_lo)
    half_hi = max(1e-6, cross_hi - channel_at)

    def height(c: float, r: float) -> float:
        t = (c - cross_lo) / half_lo if c <= channel_at else (cross_hi - c) / half_hi
        end = min(1.0, (r - run_lo) / taper, (run_hi - r) / taper) if taper > 1e-6 else 1.0
        return -camber * max(0.0, min(1.0, t)) * max(0.0, end)

    mesh = bpy.data.meshes.new(PREFIX_FLOOR + name + "_M")
    obj = bpy.data.objects.new(PREFIX_FLOOR + name, mesh)
    bpy.context.collection.objects.link(obj)

    bm = bmesh.new()
    grid = []
    for ci, c in enumerate(cross):
        col = []
        for r in run:
            z = height(c, r)
            co = (c, r, z) if channel_axis == "Y" else (r, c, z)
            col.append(bm.verts.new(co))
        grid.append(col)
    for ci in range(len(cross) - 1):
        for ri in range(len(run) - 1):
            quad = (grid[ci][ri], grid[ci + 1][ri], grid[ci + 1][ri + 1], grid[ci][ri + 1])
            bm.faces.new(quad if channel_axis == "Y" else tuple(reversed(quad)))

    ring: list = []
    last_c, last_r = len(cross) - 1, len(run) - 1
    for ci in range(last_c):
        ring.append(grid[ci][0])
    for ri in range(last_r):
        ring.append(grid[last_c][ri])
    for ci in range(last_c, 0, -1):
        ring.append(grid[ci][last_r])
    for ri in range(last_r, 0, -1):
        ring.append(grid[0][ri])
    bottom = [bm.verts.new((v.co.x, v.co.y, -depth)) for v in ring]
    for k in range(len(ring)):
        nk = (k + 1) % len(ring)
        bm.faces.new((ring[k], ring[nk], bottom[nk], bottom[k]))
    bm.faces.new(tuple(bottom))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    blendkit.assign_material(obj, material)
    return [obj]


def floor_drain(name: str, cx: float, cy: float, size: float = 0.30,
                at_z: float = -0.030, bars: int = 4) -> list:
    """A cast gully cover sitting in ``floor_pan``'s channel.

    The cover sits *on* the channel rather than being cut into it — no boolean, no
    hole, and at 34 mm of relief it still throws a hard shadow ring in a beam. It
    is what makes the camber legible: a fall with nothing at the bottom of it just
    looks like a modelling error, and a fall that ends in a grating reads as
    drainage, which is one of the few details that says "basement" without a
    single texture.

    ``at_z`` is the finished floor level where it lands. The cover stands **12 mm**
    proud of it and no more: a bolted-down inspection cover is a real thing, it is
    not a trip at that height, and it is the only profile available without cutting
    a hole — a recessed cover modelled on a solid slab is simply buried inside it
    and invisible. An earlier version stood 34 mm proud, which put a lip across the
    middle of every corridor and grew the concrete floor tile's bounding box.

    Everything stays above −FLOOR_T, so the piece's ``bounds_min.z`` is untouched
    and §12's grid contract holds.
    """
    objs: list = []
    st = mat("Trim_Steel")
    half = size / 2.0
    lip = 0.012

    # Sump plate under the bars: dark, so the gaps read as a void rather than as
    # the concrete they actually are.
    objs.append(slab(f"{name}_sump", cx - half + 0.026, cx + half - 0.026,
                     cy - half + 0.026, cy + half - 0.026,
                     at_z - 0.002, at_z + 0.005, mat("Wall_Dado"), floor=True))
    for tag, x0, x1, y0, y1 in (
        ("L", cx - half, cx - half + 0.030, cy - half, cy + half),
        ("R", cx + half - 0.030, cx + half, cy - half, cy + half),
        ("B", cx - half + 0.030, cx + half - 0.030, cy - half, cy - half + 0.030),
        ("T", cx - half + 0.030, cx + half - 0.030, cy + half - 0.030, cy + half),
    ):
        objs.append(slab(f"{name}_frame{tag}", x0, x1, y0, y1,
                         at_z - 0.010, at_z + lip, st, floor=True))
    inner = size - 0.060
    for k in range(bars):
        c = cy - inner / 2 + (k + 0.5) * inner / bars
        objs.append(slab(f"{name}_bar{k}", cx - half + 0.030, cx + half - 0.030,
                         c - 0.018, c + 0.018, at_z + 0.005, at_z + lip,
                         st, floor=True))
    return objs


# ── Doorways ────────────────────────────────────────────────────────────────


def door_lining(name: str, x0: float, x1: float, y0: float, y1: float,
                head_z: float, reveal: float = 0.030,
                architrave_w: float = 0.10, architrave_d: float = 0.028,
                threshold: bool = True, side_architraves: bool = True) -> list:
    """Jamb linings, a head lining, architraves on both faces and a threshold.

    §12 puts a lockable door at the neck of a 순환로 — "잠그면 순환이 끊김" — so the
    doorway is a decision point the player has to recognise *at a distance, in the
    dark*. A hole in a wall does not read as a door. A cased opening with a lit
    architrave and a steel threshold does, and it does it from the far end of a
    corridor where only the frame is catching the beam.

    ``(x0, x1)`` is the *structural* opening and ``(y0, y1)`` the wall's thickness.
    The linings are set inside the opening, so the finished passage is
    ``2 × reveal`` = 60 mm narrower — 2.14 m at the kit's 2.20 m opening, still
    well above §08's 1.80 m two-person 궤짝 carry.

    The leaves are **face-hung**, not set in the reveal, which is why the linings
    do not have to be subtracted from ``Door_Panel_Lockable``'s width: two 1.10 m
    leaves still total the full 2.20 m structural opening and lap the lining by
    30 mm each side. That is the correct construction for a basement bulkhead door
    with three exposed pintles per leaf, and it is the only version that keeps
    §12's stated opening and §08's carry both true at once.
    """
    objs: list = []
    tp, st = mat("Trim_Painted"), mat("Trim_Steel")

    objs.append(slab(f"{name}_jambL", x0, x0 + reveal, y0, y1, 0.0, head_z, tp))
    objs.append(slab(f"{name}_jambR", x1 - reveal, x1, y0, y1, 0.0, head_z, tp))
    objs.append(slab(f"{name}_head", x0, x1, y0, y1, head_z - reveal, head_z, tp))

    # Stop bead on the face the leaves shut against. Without it a closed door has
    # no line where it meets the frame, and a closed door is a §12 state the team
    # has to be able to read at a glance ("잠그면 순환이 끊김").
    objs.append(slab(f"{name}_stopL", x0 + reveal, x0 + reveal + 0.020,
                     y1, y1 + 0.012, 0.0, head_z, tp, raw=True))
    objs.append(slab(f"{name}_stopR", x1 - reveal - 0.020, x1 - reveal,
                     y1, y1 + 0.012, 0.0, head_z, tp, raw=True))
    objs.append(slab(f"{name}_stopH", x0 + reveal, x1 - reveal,
                     y1, y1 + 0.012, head_z - 0.020, head_z, tp, raw=True))

    # Architraves sit on the wall face *beside* the opening. When the opening is the
    # full width of the corridor there is no such face — the side walls are the
    # jambs — so the vertical casings would be modelled entirely inside them:
    # invisible geometry paying full triangle price. ``side_architraves=False`` is
    # the honest answer for that case, and the reveal lining is what frames the
    # opening instead.
    for tag, fy0, fy1 in (("A", y0 - architrave_d, y0), ("B", y1, y1 + architrave_d)):
        if side_architraves:
            objs.append(slab(f"{name}_archL{tag}", x0 - architrave_w, x0 + 0.012,
                             fy0, fy1, 0.0, head_z + architrave_w, tp))
            objs.append(slab(f"{name}_archR{tag}", x1 - 0.012, x1 + architrave_w,
                             fy0, fy1, 0.0, head_z + architrave_w, tp))
        span0 = x0 - architrave_w if side_architraves else x0
        span1 = x1 + architrave_w if side_architraves else x1
        objs.append(slab(f"{name}_archH{tag}", span0, span1,
                         fy0, fy1, head_z, head_z + architrave_w, tp))

    if threshold:
        # Below z = 0 so it is flush: no trip, but a hard material seam the beam
        # catches, which is what marks the doorway on the floor.
        objs.append(slab(f"{name}_threshold", x0 - 0.03, x1 + 0.03,
                         y0 - architrave_d, y1 + architrave_d, -0.026, 0.0,
                         st, floor=True))
    return objs


def opening_frame(name: str, wall: Wall, a0: float, a1: float, head_z: float,
                  depth: float = 0.026, width: float = 0.09) -> list:
    """A light cased surround for a wall opening that has no door.

    Every dock in the kit is a hole; a hole with a surround reads as a doorway and
    a hole without one reads as a missing wall. Cheap enough (three trim boxes) to
    put on every opening in a 20 m hall.
    """
    objs: list = []
    tp = mat("Trim_Painted")
    objs.append(wall_box(wall, f"{name}_L", a0 - width, a0, 0.0, depth, 0.0,
                         head_z + width, tp))
    objs.append(wall_box(wall, f"{name}_R", a1, a1 + width, 0.0, depth, 0.0,
                         head_z + width, tp))
    objs.append(wall_box(wall, f"{name}_H", a0 - width, a1 + width, 0.0, depth,
                         head_z, head_z + width, tp))
    return objs


# ── Columns ─────────────────────────────────────────────────────────────────


def column(name: str, cx: float, cy: float, height: float, size: float = 0.50,
           base_h: float = 0.24, cap_h: float = 0.30, corbel: bool = True) -> list:
    """A square column with a base, a capital and four corbel brackets.

    A plain extruded column is the same failure as a plain wall: one shape, one
    gradient. The base and capital give it three horizontal breaks, and the
    corbels give the top a silhouette that a beam can find from across a 20 m hall
    — which matters because §12's 개방 공간 exists so the Runner can take aggro from
    10 m or more, and to do that they have to be able to see the room.
    """
    objs: list = []
    w, tp = mat("Wall_Structure"), mat("Trim_Painted")
    h = size / 2.0
    bh = size / 2.0 + 0.07

    objs.append(slab(f"{name}_base", cx - bh, cx + bh, cy - bh, cy + bh,
                     0.0, base_h, tp))
    # The shaft rides the pillar bevel group: a 22 mm two-segment chamfer on
    # every arris, which is what a raking beam actually reports as "a column"
    # rather than "a box" from half a metre away.
    objs.append(slab(f"{name}_shaft", cx - h, cx + h, cy - h, cy + h,
                     0.0, height - cap_h, w, pillar=True))
    objs.append(slab(f"{name}_cap", cx - bh, cx + bh, cy - bh, cy + bh,
                     height - cap_h, height, tp))
    # A shallow flute on each face: one recessed strip that reads as a chamfered
    # corner pair without the triangle cost of an actual chamfer profile.
    objs.append(slab(f"{name}_flute", cx - h + 0.10, cx + h - 0.10,
                     cy - h - 0.012, cy + h + 0.012, base_h, height - cap_h,
                     w, raw=True))
    if corbel:
        out = 0.11   # how far the bracket projects past the shaft face
        thick = 0.12  # its width across the face
        for k, (dx, dy) in enumerate(((1, 0), (-1, 0), (0, 1), (0, -1))):
            if dx:
                x0, x1 = (cx + h, cx + h + out) if dx > 0 else (cx - h - out, cx - h)
                y0, y1 = cy - thick, cy + thick
            else:
                x0, x1 = cx - thick, cx + thick
                y0, y1 = (cy + h, cy + h + out) if dy > 0 else (cy - h - out, cy - h)
            objs.append(slab(f"{name}_corb{k}", x0, x1, y0, y1,
                             height - cap_h - 0.34, height - cap_h, tp))
    return objs
