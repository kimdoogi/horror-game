#!/usr/bin/env python3
"""Generates every carryable, interactable and set-dressing prop in the basement.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_props.py

Optional filter while iterating (everything after ``--`` is a name substring)::

    ... --python tools/blender/gen_props.py -- Chest Safe

Outputs one FBX per prop into ``Assets/Models/Props/``.


WHY THESE ASSETS EXIST
======================

**§08's loot table is an economy, not decoration.** Weight and value *are* the
gameplay: 5 weight units is exactly ``WeightFreeMax``, so one 궤짝 ends the free
speed band by itself, and §05's directional multipliers then multiply the loss.
That means a player has to be able to price a piece **before** picking it up, in
the dark, at the edge of a flashlight beam — so the only channel left is
**physical size**. Every dimension below is chosen to make weight legible at a
glance, and `main()` asserts the size ordering against §08's 무게 column rather
than trusting it.

The two weight-5 pieces exist for one scene, which §08 calls the game's best
moment: *"두 명이 궤짝을 들고 좁은 통로를 지나는데 괴물이 온다."* They are therefore
built to a different rule from everything else — **two grab points more than a
metre apart**, so a single carrier is physically implausible, and a horizontal
span that eats most of a corridor. `LootDefinition.AllowsSharedCarry` is true for
exactly these two rows, and the geometry has to earn it.

The interactables are the map's verbs. §12 requires an electrical panel per zone
for the Engineer's zone light; §03 requires clue surfaces that can only be read in
place; §12's checklist requires concealment near the exit for §07's 새벽 stage,
where the monster knows where the exit is and the standstill state is gone.


WHAT THE DESIGN DICTATES ABOUT THEIR SHAPE
==========================================

* **§05 — first person, dark, headphones.** Detail buys nothing; silhouette and
  specular response buy everything. Budget is 1500 triangles per prop and most
  props spend under half of it. The vehicle is the single exception.
* **§08 — 은수저·잡동사니 are "눈에 잘 보임 (유혹)".** They are the *cheapest* loot
  and must look like the *best*. Handled two ways, both measured below: the
  largest floor footprint of any small piece (a beam sweeping the floor cannot
  miss them), and mirror-grade materials — metallic 1.0, roughness ≤ 0.16. The
  actually-efficient pieces (회중시계·반지, the "효율 최고" row) get the opposite
  treatment: 2-10 cm across, and a *tarnished* case at roughness 0.55 so they
  never throw a highlight. The trap is built out of geometry and material, not
  out of a spawn table.
* **§03 — clues cannot be carried out; they are read in place and spoken aloud.**
  So a clue prop's only real feature is a flat face the host can stamp a glyph
  onto (§13: the glyph is rendered host-side and sent for that clue only). Each
  clue surface carries a single-quad face with material ``Clue_Face`` and UVs
  mapped exactly 0..1, and this script reports that face's size in metres so the
  glyph texel density can be chosen deliberately.
* **§03's 혼동쌍 are a geometry requirement, not a texture requirement.** A glyph
  is only misread under a viewing condition, and the three clue props supply
  three different conditions:

  ===================  ==================================================  =========
  Prop                 Viewing condition it creates                        Pair
  ===================  ==================================================  =========
  Clue_EngravedPlate   0.16 m off the floor, 180°-rotationally symmetric    6 ↔ 9
                       with no up-cue, so it is legible from any side —
                       approach from the far side and 6 *is* 9. The
                       symmetry is asserted numerically, not eyeballed.
  Clue_WallBoard       A polished pane in the same frame beside the         좌 ↔ 우
                       glyph. The reflection is readable and reversed.
  Clue_LedgerStand     A page held at 32°, read at a glancing angle in a    1 ↔ 7
                       narrow beam — the condition §03 files under 손글씨체.
  ===================  ==================================================  =========

  ㅁ ↔ ㅇ is listed as "흐릿할 때", which is a beam-and-distance condition rather
  than a shape; all three faces are matte (roughness 0.75) so a beam at an angle
  washes them out instead of resolving them.
* **§03 — darkness is the lock on progress**, so light sources carry weight.
  Every lamp lens, indicator and flare tip uses an emissive material so it reads
  at distance in an unlit corridor. The vehicle is deliberately the brightest
  object in the game: §08 makes it safe zone, shop and supply point at once, and
  players need to be able to run *toward* it.
* **§06 — silence is designed.** Nothing here animates or hums on its own. The
  noise trap is the one prop whose entire purpose is to make sound, and it looks
  like it: an exposed spring, a struck bell, a tripwire on a stake.


CONVENTIONS THIS FILE GUARANTEES
================================

* **1 Blender unit = 1 metre = 1 Unity unit.** Every prop is checked against a
  declared intended size, so a scale mistake fails the build instead of shipping.
* **Pivot.** Floor props: origin at floor level (min Z = 0), centred on the
  footprint. Wall props: origin at the wall plane (max Y = 0), centred in X, with
  the real mounting height left in Z — so dropping one at the foot of a wall puts
  it at the correct height with no offset to remember.
* **Facing.** Props face **-Y** in Blender, matching the monster generator, which
  `export_fbx`'s ``axis_forward='-Z'`` turns into +Z forward in Unity. No
  compensating rotation is applied anywhere in this file.
* **One mesh, one object, identity transform** per FBX. Multi-material by design;
  material names are the seam Unity binds against — in particular ``Clue_Face``
  is where the host's rendered glyph goes.

Loot FBX names embed their ``LootId`` (``Loot_Trinket_*``, ``Loot_Timepiece_*``,
``Loot_SafeDocument``, ``Loot_LargePiece_*``) so a prefab cannot be wired to the
wrong economy row: the weight and value in the report below are read straight off
`GameConstants` values quoted in `LOOT_ROWS`.
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

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Euler, Vector  # noqa: E402

import blendkit  # noqa: E402
from blendkit import MaterialSpec  # noqa: E402


# ── §08 economy rows, quoted from Core so the report can be checked ─────────
# Values are GameConstants' (LootWeight*, LootValue*). They are quoted, never
# redefined: if the balance sweep in §16-2 moves them, this table is wrong and
# the mismatch is supposed to be noticed here.

LOOT_ROWS: dict[str, tuple[str, int, int, str]] = {
    # prop name              -> (LootId, weight, value, §08 value word)
    "Loot_Trinket_SilverSpoons": ("Trinket", 1, 10, "낮음"),
    "Loot_Trinket_Junk": ("Trinket", 1, 10, "낮음"),
    "Loot_Timepiece_PocketWatch": ("Timepiece", 1, 25, "중간"),
    "Loot_Timepiece_Ring": ("Timepiece", 1, 25, "중간"),
    "Loot_SafeDocument": ("SafeDocument", 2, 40, "높음"),
    "Loot_LargePiece_Portrait": ("LargePiece", 5, 100, "매우 높음"),
    "Loot_LargePiece_Chest": ("LargePiece", 5, 100, "매우 높음"),
}

CONSPICUOUS = ("Loot_Trinket_SilverSpoons", "Loot_Trinket_Junk")
"""§08's "눈에 잘 보임 (유혹)" row — cheap loot that must look expensive."""

EFFICIENT = ("Loot_Timepiece_PocketWatch", "Loot_Timepiece_Ring")
"""§08's "효율 최고" row — the pieces worth taking, and the easiest to walk past."""


# ── Assumptions NOT taken from the design document ──────────────────────────
# Flagged loudly because the checks below depend on them. §12 fixes corridor
# *length* (MaxStraightCorridor = 20 m), S-leg length (10 m) and zone diagonals
# (30-40 m) but never states a corridor **width**, and §05 never states player
# body size. These are this generator's working figures, not design numbers, and
# they are printed with the report so a real value can replace them.

CORRIDOR_WIDTH_ASSUMED = 1.60
"""Working width of §12's 좁은 통로, metres. NOT a design number."""

PLAYER_SHOULDER_ASSUMED = 0.50
"""Shoulder span used to reason about squeezing past a carried piece. NOT a design number."""

PLAYER_HEIGHT_ASSUMED = 1.80
"""Standing height the hiding spot has to swallow. NOT a design number."""

ONE_HAND_CARRY_SPAN = 0.60
"""Widest object one player can plausibly grip alone. Grab points further apart
than this read as a two-person carry, which is what §08 requires of weight 5."""

MEASURE_ONLY = os.environ.get("PROPS_MEASURE_ONLY") == "1"
"""Survey mode: report size mismatches instead of failing on the first one.

Only for retuning a prop's proportions, where failing on prop 1 of 24 hides the
other 23. It downgrades **one** check — the declared-size guard — and nothing
else, and the default path is strict, so a build run without the variable still
refuses to ship a mis-scaled asset."""


# ── Materials ───────────────────────────────────────────────────────────────
# Created on demand so each prop's ASSET_REPORT material count means something.
# Names avoid the contractual Floor_* namespace blendkit reserves for §12's
# footstep surfaces.

WOOD = "Prop_Wood"
WOOD_DARK = "Prop_WoodDark"
IRON = "Prop_Iron"
RUST = "Prop_Rust"
SILVER = "Prop_Silver"
BRASS = "Prop_Brass"
BRASS_DULL = "Prop_BrassTarnished"
GOLD = "Prop_Gold"
GEM = "Prop_Gem"
PAPER = "Prop_Paper"
WAX = "Prop_Wax"
CLOTH = "Prop_Cloth"
LEATHER = "Prop_Leather"
GLASS = "Prop_Glass"
MIRROR = "Prop_Mirror"
LAMP = "Prop_Lamp"
FLARE = "Prop_FlareBurn"
STONE = "Prop_Stone"
CANVAS = "Prop_Canvas"
PAINT = "Prop_Paint"
VAN_BODY = "Prop_VanBody"
VAN_LOWER = "Prop_VanLower"
PAINTED_STEEL = "Prop_PaintedSteel"
CLUE_FACE = "Clue_Face"

VEHICLE_PAINT = (VAN_BODY, VAN_LOWER)
"""The 차량's two livery coats. `check_the_vehicle_is_painted` reads this."""

MATERIALS: dict[str, MaterialSpec] = {
    WOOD: MaterialSpec(WOOD, (0.196, 0.126, 0.072), roughness=0.85),
    WOOD_DARK: MaterialSpec(WOOD_DARK, (0.098, 0.062, 0.038), roughness=0.80),
    IRON: MaterialSpec(IRON, (0.112, 0.116, 0.122), roughness=0.55, metallic=0.90),
    RUST: MaterialSpec(RUST, (0.201, 0.092, 0.046), roughness=0.92, metallic=0.30),
    # §08's temptation. Mirror-grade on purpose: a narrow beam has to bounce off
    # these from across a room even though they are the worst loot in the game.
    SILVER: MaterialSpec(SILVER, (0.862, 0.871, 0.882), roughness=0.12, metallic=1.0),
    BRASS: MaterialSpec(BRASS, (0.712, 0.552, 0.203), roughness=0.16, metallic=1.0),
    # The efficient row, deliberately dull — §08 makes it "중간" value and easy to
    # miss, and a tarnished case is how that happens without shrinking it further.
    BRASS_DULL: MaterialSpec(BRASS_DULL, (0.302, 0.241, 0.112), roughness=0.55, metallic=1.0),
    GOLD: MaterialSpec(GOLD, (0.831, 0.662, 0.241), roughness=0.14, metallic=1.0),
    GEM: MaterialSpec(GEM, (0.352, 0.451, 0.622), roughness=0.05),
    PAPER: MaterialSpec(PAPER, (0.762, 0.721, 0.621), roughness=0.90),
    WAX: MaterialSpec(WAX, (0.421, 0.072, 0.072), roughness=0.50),
    CLOTH: MaterialSpec(CLOTH, (0.161, 0.132, 0.151), roughness=0.95),
    LEATHER: MaterialSpec(LEATHER, (0.132, 0.091, 0.062), roughness=0.72),
    GLASS: MaterialSpec(GLASS, (0.621, 0.662, 0.702), roughness=0.06, metallic=0.40),
    # §03's 좌↔우 pair needs a real reflector next to the glyph.
    MIRROR: MaterialSpec(MIRROR, (0.892, 0.902, 0.912), roughness=0.03, metallic=1.0),
    # §03: darkness is the lock. Emissive so a lens reads at distance unlit.
    LAMP: MaterialSpec(LAMP, (1.0, 0.932, 0.721), roughness=0.30, emission=6.0),
    FLARE: MaterialSpec(FLARE, (1.0, 0.421, 0.122), roughness=0.40, emission=18.0),
    STONE: MaterialSpec(STONE, (0.302, 0.292, 0.271), roughness=0.90),
    CANVAS: MaterialSpec(CANVAS, (0.552, 0.512, 0.432), roughness=0.88),
    PAINT: MaterialSpec(PAINT, (0.221, 0.172, 0.132), roughness=0.62),
    # ── Paint is a dielectric ──────────────────────────────────────────────
    # ART.md §7.12. A painted panel returns light *diffusely*; its gloss comes
    # from roughness and not from metallic, and putting metallic on it deletes
    # the diffuse term and leaves a surface that renders whatever it reflects —
    # which, in a basement with no reflection probe and a 12 m torch, is nothing.
    # These three exist so the props that are painted stop borrowing `Prop_Iron`,
    # which stays exactly as it is for the things that really are forged.
    #
    # The livery is a faded works green because the 하역 베이 is warm brick over
    # grey concrete and §07 grades the building cold: green is the one hue in
    # reach that separates from both, and §12's only other green is 저수조's
    # glazed tile five storeys down. Luminance 0.276, deliberately *over* ART.md's
    # 0.21 darkest corridor wall: §01 makes the apron the lit 안전 지대 rather than
    # part of §03's dark building, and §08 wants a team able to run *toward* this
    # from across a 20 m bay. At 0.221 it measured 0.61x the wall behind it at 12 m
    # and read as a shape; at 0.276 it reads as a vehicle.
    VAN_BODY: MaterialSpec(VAN_BODY, (0.222, 0.297, 0.231), roughness=0.45),
    # The skirt. Dark, because road dirt collects at the bottom of every real
    # panel and because a single flat colour over 3.4 m² is what makes a box
    # read as a box; not black, because that is the defect being fixed.
    VAN_LOWER: MaterialSpec(VAN_LOWER, (0.128, 0.152, 0.136), roughness=0.58),
    # Institutional grey-green enamel for the things in the building that are
    # sheet steel with a coat of paint on them rather than forged iron: the 금고's
    # shell, the 전기 패널's breaker bank, the 발전기's engine block. Luminance 0.207
    # — level with ART.md's darkest §12 wall and no brighter, because unlike the
    # 차량 these three stand *inside* the building where §03 makes darkness the lock
    # on progress. The change that matters to them is metallic 0.90 -> 0, which is
    # the difference between a surface with no diffuse response and one with a
    # diffuse response; the albedo lift is secondary.
    PAINTED_STEEL: MaterialSpec(PAINTED_STEEL, (0.196, 0.212, 0.194), roughness=0.52),
    # The seam §13 binds to: the host renders one clue's glyph and stamps it here.
    CLUE_FACE: MaterialSpec(CLUE_FACE, (0.682, 0.641, 0.552), roughness=0.75),
}


def mat(name: str) -> bpy.types.Material:
    """Returns the named material, creating it the first time it is asked for."""
    spec = MATERIALS.get(name)
    if spec is None:
        blendkit.fail(f"unknown material '{name}'")
    return blendkit.make_material(spec)  # type: ignore[arg-type]


def register_materials(specs: dict[str, MaterialSpec]) -> None:
    """Adds material specs to the shared table so `PropBuild` can resolve them.

    `PropBuild` looks materials up through `mat()`, which reads `MATERIALS`. A
    sibling generator that reuses the build scaffolding — `gen_dressing.py` does —
    therefore has to put its own specs here before it builds anything, and this is
    the seam it does that through rather than reaching into the dict. Re-defining a
    name already present is refused: `Clue_Face` is §13's contractual seam and the
    `Prop_*` names carry §08's temptation/efficiency contrast, and silently
    re-colouring either from another file is the kind of change nobody would think
    to look for.
    """
    for name, spec in specs.items():
        existing = MATERIALS.get(name)
        if existing is not None and existing != spec:
            blendkit.fail(f"material '{name}' is already defined with different values; "
                          "pick another name rather than redefining a shared one")
        MATERIALS[name] = spec


def rads(degrees: tuple[float, float, float]) -> tuple[float, float, float]:
    return (math.radians(degrees[0]), math.radians(degrees[1]), math.radians(degrees[2]))


# ── Build scaffolding ───────────────────────────────────────────────────────


class PropBuild:
    """Collects the parts of one prop plus the measurements worth asserting.

    Rotations are taken in **degrees** because every angle in this file was
    reasoned about in degrees (a door at 100°, a lectern face at 32°).
    """

    def __init__(self, name: str) -> None:
        self.name = name
        self.parts: list[bpy.types.Object] = []
        self.nobevel: set[str] = set()
        self.named: dict[str, bpy.types.Object] = {}
        self.pivot_part: bpy.types.Object | None = None
        self.meta: dict[str, object] = {}
        self._n = 0

    # -- naming ------------------------------------------------------------

    def _tag(self, tag: str) -> str:
        self._n += 1
        return f"{self.name}_{tag}" if tag else f"{self.name}_{self._n:03d}"

    def _register(self, obj: bpy.types.Object, material: str, role: str,
                  nobevel: bool) -> bpy.types.Object:
        blendkit.assign_material(obj, mat(material))
        self.parts.append(obj)
        if nobevel:
            self.nobevel.add(obj.name)
        if role:
            self.named[role] = obj
        return obj

    # -- primitives --------------------------------------------------------

    def box(self, size, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), mat=WOOD,
            tag: str = "", role: str = "", nobevel: bool = False):
        obj = blendkit.add_box(self._tag(tag or role), tuple(size), tuple(loc), rads(tuple(rot)))
        return self._register(obj, mat, role, nobevel)

    def cyl(self, radius, depth, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), verts=16,
            mat=IRON, tag: str = "", role: str = "", nobevel: bool = False):
        obj = blendkit.add_cylinder(self._tag(tag or role), radius, depth, tuple(loc),
                                    verts, rads(tuple(rot)))
        return self._register(obj, mat, role, nobevel)

    def sph(self, radius, loc=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0), rot=(0.0, 0.0, 0.0),
            segs=12, rings=6, mat=IRON, tag: str = "", role: str = "", nobevel: bool = False):
        obj = blendkit.add_sphere(self._tag(tag or role), radius, tuple(loc), segs, rings)
        obj.rotation_euler = Euler(rads(tuple(rot)), "XYZ")
        if scale != (1.0, 1.0, 1.0):
            obj.scale = Vector(scale)
            blendkit.apply_transforms(obj, scale=True)
        return self._register(obj, mat, role, nobevel)

    def cone(self, radius1, radius2, depth, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0),
             verts=12, mat=IRON, tag: str = "", role: str = "", nobevel: bool = False):
        bpy.ops.mesh.primitive_cone_add(radius1=radius1, radius2=radius2, depth=depth,
                                        location=tuple(loc), vertices=verts)
        obj = bpy.context.active_object
        obj.name = self._tag(tag or role)
        obj.rotation_euler = Euler(rads(tuple(rot)), "XYZ")
        return self._register(obj, mat, role, nobevel)

    def torus(self, major, minor, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0),
              mseg=16, nseg=6, mat=GOLD, tag: str = "", role: str = "",
              nobevel: bool = True):
        bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor,
                                         major_segments=mseg, minor_segments=nseg,
                                         location=tuple(loc))
        obj = bpy.context.active_object
        obj.name = self._tag(tag or role)
        obj.rotation_euler = Euler(rads(tuple(rot)), "XYZ")
        return self._register(obj, mat, role, nobevel)

    def quad(self, w, h, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), mat=CLUE_FACE,
             tag: str = "", role: str = ""):
        """A single-quad readable face. Never bevelled — bevelling would eat it."""
        obj = blendkit.add_plane(self._tag(tag or role), w, h, tuple(loc))
        obj.rotation_euler = Euler(rads(tuple(rot)), "XYZ")
        return self._register(obj, mat, role, nobevel=True)

    # -- grouping ----------------------------------------------------------

    def hinge_group(self, name: str, parts: list[bpy.types.Object],
                    pivot_world, angle_deg: float, axis: str = "Z"):
        """Joins `parts` into one object that pivots about `pivot_world`.

        Used for the safe door and the locker door. The trick is that the parts
        are authored in hinge-local coordinates and the first member is a pin at
        the local origin, so the joined object's origin *is* the hinge and a
        plain object rotation swings the door.
        """
        pin = blendkit.add_cylinder(f"{self.name}_{name}_pin", 0.012, 0.02, (0.0, 0.0, 0.0), 8)
        blendkit.assign_material(pin, mat(IRON))
        # Deregister the members *before* joining. join() deletes the merged
        # objects, and touching a deleted one afterwards raises
        # "StructRNA of type Object has been removed" — including from `in`.
        for p in parts:
            if p in self.parts:
                self.parts.remove(p)
            self.nobevel.discard(p.name)
            for role, obj in list(self.named.items()):
                if obj == p:
                    del self.named[role]
        group = blendkit.join([pin] + parts, f"{self.name}_{name}")
        group.location = Vector(pivot_world)
        rot = {"X": (angle_deg, 0.0, 0.0), "Y": (0.0, angle_deg, 0.0),
               "Z": (0.0, 0.0, angle_deg)}[axis]
        group.rotation_euler = Euler(rads(rot), "XYZ")
        self.parts.append(group)
        self.nobevel.add(group.name)
        return group

    def frame(self, origin=(0.0, 0.0, 0.0), yaw=0.0) -> "Frame":
        return Frame(self, origin, yaw)


class Frame:
    """A yaw-rotated, translated local coordinate frame.

    Blender's XYZ Euler composes as Rz·Ry·Rx, so adding the frame's yaw to a
    part's own Z rotation is exactly "tilt locally, then yaw" — which is what a
    spoon lying at an angle on the floor needs.
    """

    def __init__(self, build: PropBuild, origin, yaw: float) -> None:
        self.b = build
        self.origin = Vector(origin)
        self.yaw = yaw

    def _loc(self, loc) -> tuple[float, float, float]:
        c, s = math.cos(math.radians(self.yaw)), math.sin(math.radians(self.yaw))
        x, y, z = loc
        return (self.origin.x + x * c - y * s, self.origin.y + x * s + y * c, self.origin.z + z)

    def _rot(self, rot) -> tuple[float, float, float]:
        return (rot[0], rot[1], rot[2] + self.yaw)

    def box(self, size, loc=(0, 0, 0), rot=(0, 0, 0), **kw):
        return self.b.box(size, self._loc(loc), self._rot(rot), **kw)

    def cyl(self, radius, depth, loc=(0, 0, 0), rot=(0, 0, 0), **kw):
        return self.b.cyl(radius, depth, self._loc(loc), self._rot(rot), **kw)

    def sph(self, radius, loc=(0, 0, 0), rot=(0, 0, 0), **kw):
        return self.b.sph(radius, self._loc(loc), rot=self._rot(rot), **kw)

    def cone(self, r1, r2, depth, loc=(0, 0, 0), rot=(0, 0, 0), **kw):
        return self.b.cone(r1, r2, depth, self._loc(loc), self._rot(rot), **kw)


# ── Measurement helpers ─────────────────────────────────────────────────────


def squash_lid_z(obj: bpy.types.Object, factor: float) -> None:
    """Flattens a cylinder that was laid along X into an elliptical dome.

    `add_cylinder` leaves its rotation on the object, so the cylinder's local Z is
    still its axis and local X has become world -Z. Scaling local **X** is
    therefore what squashes the dome's height; scaling local Z would shorten the
    chest instead. Applied to the mesh so the export carries no object scale.
    """
    obj.scale = Vector((factor, 1.0, 1.0))
    blendkit.apply_transforms(obj, scale=True)


def world_bbox(objs) -> tuple[Vector, Vector]:
    """Union bounding box of objects, in world space."""
    bpy.context.view_layer.update()
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for obj in objs:
        for corner in obj.bound_box:
            w = obj.matrix_world @ Vector(corner)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    return lo, hi


def bbox_size(objs) -> Vector:
    lo, hi = world_bbox(objs)
    return hi - lo


def mesh_volume(obj: bpy.types.Object) -> float:
    """Signed volume of the mesh, m³. A rough mass proxy; hollow props overstate."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    vol = bm.calc_volume(signed=False)
    bm.free()
    return vol


def material_areas(obj: bpy.types.Object) -> dict[str, float]:
    """Surface area in m² carried by each of the prop's materials.

    This is the measurement ART.md §7.12 was missing. "Which material is this prop
    *made of*" cannot be answered from the slot list — the 차량 carries seven
    materials and two of them are a headlamp lens — and it is exactly the question
    that decides whether a dark metal renders the object or renders a hole. Face
    area answers it, and it costs one pass over the polygons.
    """
    out: dict[str, float] = {}
    names = [m.name if m is not None else "" for m in obj.data.materials]
    for poly in obj.data.polygons:
        if 0 <= poly.material_index < len(names):
            key = names[poly.material_index]
            out[key] = out.get(key, 0.0) + poly.area
    return out


def largest_visible_panel(obj: bpy.types.Object) -> dict[str, tuple[float, float]]:
    """Per material, its biggest visible flat slab as ``(area m², narrow span m)``.

    Area alone does not separate a forged thing from a painted one: the 파이프 run
    and the 차량's flank can carry the same number of square metres of the same
    material and only one of them is a defect. **Flatness** is what separates them,
    and it is physics rather than taste. A metal has no diffuse term, so all it can
    return is a specular highlight — and a highlight needs the light to be in the
    mirror direction. Curvature guarantees that: somewhere on a pipe or a bolt head
    there is always a normal pointing at the lamp, which is why a dark pipe still
    reads as a dark pipe. A flat slab has one normal, so it is lit at exactly one
    viewing angle and is a hole from everywhere else — which is the frame ART.md
    §7.12 photographed.

    Two faces are dropped before anything is measured, and the first version of this
    function had neither, which is why it reported the 차량's biggest iron panel as
    its 10.47 m² **chassis plate** — a part no player has ever seen:

    * **Downward-facing.** §05 puts the eye at 1.63 m and every prop here stands on
      a floor, so a face pointing at the ground is not a surface the game has.
    * **Occluded by the prop's own body.** A ray along the face's own normal that
      lands back on the same mesh means something of this prop is in front of it —
      the chassis top under the cargo floor, the inside of a sealed box.

    What survives is then gathered by (material, normal, plane offset) rounded to
    1 mm **and split into connected components**, so a bevelled face still measures
    as the one panel it looks like — and the 선반's front does not. Merely coplanar
    is not a slab: four shelf-edge rails and four legs all flush at the same y added
    up to 0.57 m² of "panel" on a unit whose front is mostly timber and air. Sharing
    an edge is the test, and after joining, separately-built boxes share none.

    The **narrow span** comes back beside the area because area alone still cannot
    tell a face from a strip, and the kit contains a decisive pair: the 금고's door is
    0.55 m² and the 차량's front bumper is 0.50 m², and only one of them is a defect.
    A 2.2 m × 0.24 m bumper is trim — it is read by its own long edge highlight and
    by the shapes either side of it, which is what a dark metal can still do. A
    0.72 m × 0.78 m door is a face, and a face made of dark metal has nothing.
    """
    names = [m.name if m is not None else "" for m in obj.data.materials]
    planes: dict[tuple, list[int]] = {}
    for poly in obj.data.polygons:
        if not 0 <= poly.material_index < len(names):
            continue
        n = poly.normal
        if n.z < -0.5:
            continue
        if obj.ray_cast(poly.center + n * 0.002, n, distance=8.0)[0]:
            continue
        key = (names[poly.material_index], round(n.x, 2), round(n.y, 2), round(n.z, 2),
               round(n.dot(poly.center), 3))
        planes.setdefault(key, []).append(poly.index)

    out: dict[str, tuple[float, float]] = {}
    polygons = obj.data.polygons
    vertices = obj.data.vertices
    for key, indices in planes.items():
        axis_u, axis_v = _face_basis(Vector(key[1:4]))
        by_vertex: dict[int, list[int]] = {}
        for index in indices:
            for vertex in polygons[index].vertices:
                by_vertex.setdefault(vertex, []).append(index)
        seen: set[int] = set()
        for start in indices:
            if start in seen:
                continue
            seen.add(start)
            stack, area, corners = [start], 0.0, []
            while stack:
                index = stack.pop()
                area += polygons[index].area
                for vertex in polygons[index].vertices:
                    corners.append(vertices[vertex].co)
                    for neighbour in by_vertex.get(vertex, ()):
                        if neighbour not in seen:
                            seen.add(neighbour)
                            stack.append(neighbour)
            us = [c.dot(axis_u) for c in corners]
            vs = [c.dot(axis_v) for c in corners]
            span = min(max(us) - min(us), max(vs) - min(vs))
            if area > out.get(key[0], (0.0, 0.0))[0]:
                out[key[0]] = (area, span)
    return out


def albedo_luminance(name: str) -> float:
    """Rec.709 luminance of a material's authored base colour, 0..1 linear.

    The same weighting `tools/render/frame_stats.py` reads a rendered frame with,
    so a material's number here and a measured frame's number are comparable.
    """
    r, g, b = MATERIALS[name].color
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def material_index(obj: bpy.types.Object, name: str) -> int | None:
    for i, m in enumerate(obj.data.materials):
        if m is not None and m.name == name:
            return i
    return None


def _face_basis(normal: Vector) -> tuple[Vector, Vector]:
    """Screen-right and screen-up for a viewer looking at `normal`.

    Getting this backwards mirrors every glyph, which for §03 is not a cosmetic
    bug: the whole point is that 좌↔우 is confused *only* in the reflection, not
    on the clue itself.
    """
    up = Vector((0.0, 0.0, 1.0))
    if abs(normal.z) > 0.95:
        u = Vector((1.0, 0.0, 0.0))
    else:
        u = up.cross(normal)
        u.normalize()
    v = normal.cross(u)
    v.normalize()
    return u, v


def map_face_uv_unit(obj: bpy.types.Object, mat_name: str) -> dict[str, float] | None:
    """Maps the polygons carrying `mat_name` to exactly 0..1 in UV and measures them.

    §13 sends the host-rendered glyph for one clue only; that image has to land
    on the face with no guesswork about which corner is which, so the readable
    face gets its own unit mapping after smart-project has done the rest.
    """
    idx = material_index(obj, mat_name)
    if idx is None:
        return None
    me = obj.data
    polys = [p for p in me.polygons if p.material_index == idx]
    if not polys:
        return None

    n = Vector((0.0, 0.0, 0.0))
    for p in polys:
        n += p.normal
    n.normalize()
    u, v = _face_basis(n)

    coords: list[tuple[int, float, float]] = []
    for p in polys:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            coords.append((li, co.dot(u), co.dot(v)))
    umin = min(c[1] for c in coords)
    umax = max(c[1] for c in coords)
    vmin = min(c[2] for c in coords)
    vmax = max(c[2] for c in coords)
    du = max(umax - umin, 1e-6)
    dv = max(vmax - vmin, 1e-6)

    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    layer = me.uv_layers.active
    for li, cu, cv in coords:
        layer.data[li].uv = ((cu - umin) / du, (cv - vmin) / dv)

    flat = max(abs((me.vertices[me.loops[li].vertex_index].co - polys[0].center).dot(n))
               for p in polys for li in p.loop_indices)
    return {"width": du, "height": dv, "polys": float(len(polys)), "flatness": flat}


def rotational_symmetry_error(obj: bpy.types.Object, cx: float = 0.0, cy: float = 0.0) -> float:
    """Worst distance from a 180°-rotated vertex to the nearest real vertex, metres.

    §03 puts 6↔9 behind "뒤집힌 각도에서". A clue that can only be approached from
    one side never produces the misread, so the plate has to be genuinely
    symmetric under a half-turn — and "genuinely" means measured.
    """
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    worst = 0.0
    for p in pts:
        qx, qy, qz = 2.0 * cx - p.x, 2.0 * cy - p.y, p.z
        best = math.inf
        for r in pts:
            d = (r.x - qx) ** 2 + (r.y - qy) ** 2 + (r.z - qz) ** 2
            if d < best:
                best = d
                if best < 1e-12:
                    break
        worst = max(worst, math.sqrt(best))
    return worst


def record_handles(b: PropBuild) -> None:
    """Measures the two tagged grab points of a weight-5 piece.

    §08 grants 2인 운반 to 대형 초상화 and 궤짝 only, and
    `LootDefinition.AllowsSharedCarry` is true for exactly that row. The geometry
    has to earn the flag, so the distance between the two grab points is measured
    off the placed parts and asserted against what one player could hold. Stored
    as distances, which are unaffected by the later pivot shift.
    """
    a_lo, a_hi = world_bbox([b.named["handle_a"]])
    c_lo, c_hi = world_bbox([b.named["handle_b"]])
    a = (a_lo + a_hi) / 2
    c = (c_lo + c_hi) / 2
    body_lo, body_hi = world_bbox(b.parts)
    mid = (body_lo + body_hi) / 2
    span = a - c
    b.meta["handle_span"] = span.length
    b.meta["handle_height"] = (a.z + c.z) / 2 - body_lo.z
    # Imbalance is measured **along the span**, not in 3D. A painting's cleats sit
    # behind its face by half the frame depth and always will; that offset is
    # perpendicular to the span and costs neither carrier anything. What would put
    # the load on one player is the grab points sitting off-centre along the line
    # between them, so that is the number.
    b.meta["handle_imbalance"] = abs(((a + c) / 2 - mid).dot(span.normalized()))
    b.meta["handle_standoff"] = (((a + c) / 2 - mid) - span.normalized()
                                 * ((a + c) / 2 - mid).dot(span.normalized())).length


def fbx_missing_materials(path: str, names: list[str]) -> list[str]:
    """Names that did not make it into the exported FBX bytes.

    Material names are the seam Unity binds against — ``Clue_Face`` is where §13's
    host-rendered glyph goes, and the emissive lamp materials are what make the
    vehicle read as safety. A slot that silently failed to export turns into a
    grey prop nobody can explain, so the written file is searched for each name
    rather than assumed. FBX binary stores object names as ASCII runs, so a plain
    byte search is enough to prove presence.
    """
    with open(path, "rb") as fh:
        data = fh.read()
    return [n for n in names if n.encode("ascii") not in data]


def emissive_material_count(obj: bpy.types.Object) -> int:
    n = 0
    for m in obj.data.materials:
        if m is None:
            continue
        spec = MATERIALS.get(m.name)
        if spec is not None and spec.emission > 0.0:
            n += 1
    return n


# ══════════════════════════════════════════════════════════════════════════
#  LOOT — §08's table. Size is the price tag.
# ══════════════════════════════════════════════════════════════════════════


def _one_spoon(f: Frame, mat_name: str = SILVER) -> None:
    """One piece of flatware, bowl convex-up so a beam gets a specular blob."""
    f.sph(0.021, (0.0, 0.058, 0.009), scale=(1.0, 1.55, 0.42), segs=12, rings=6,
          mat=mat_name, nobevel=True)
    f.box((0.013, 0.030, 0.005), (0.0, 0.034, 0.008), mat=mat_name)
    f.box((0.010, 0.072, 0.0045), (0.0, -0.004, 0.008), mat=mat_name)
    f.box((0.019, 0.026, 0.005), (0.0, -0.052, 0.008), mat=mat_name)


def build_trinket_silver_spoons() -> PropBuild:
    """은수저 — §08 weight 1, value 낮음, and the row flagged "눈에 잘 보임 (유혹)".

    Three pieces, not one. A single 19 cm spoon is a dot on a basement floor; a
    scattered set covers a footprint no floor-sweeping beam misses, which is the
    entire mechanical job of this prop — it has to be the first loot a player
    sees and the worst loot in the game.
    """
    b = PropBuild("Loot_Trinket_SilverSpoons")
    _one_spoon(b.frame((-0.062, 0.000, 0.0), yaw=8.0))
    _one_spoon(b.frame((0.060, -0.030, 0.0), yaw=-118.0))
    _one_spoon(b.frame((-0.005, 0.070, 0.0), yaw=58.0))
    return b


def build_trinket_junk() -> PropBuild:
    """잡동사니 — §08 weight 1, value 낮음, the other half of the temptation row.

    A spread of household silver: a tray that acts as a mirror lying on the
    floor, a candlestick tall enough to catch a beam aimed at head height, a
    bottle, a buckle, loose coins. Volume of actual metal is small (the weight is
    1); *footprint* is the largest of any small piece, which is the trap.
    """
    b = PropBuild("Loot_Trinket_Junk")
    # Tray — a horizontal mirror. Thin, so the metal volume stays honest at weight 1.
    b.cyl(0.082, 0.004, (-0.012, 0.014, 0.002), verts=16, mat=SILVER, nobevel=True)
    b.cyl(0.086, 0.006, (-0.012, 0.014, 0.004), verts=16, mat=SILVER, nobevel=True)
    # Candlestick, fallen over — reads at beam height rather than floor height.
    cf = b.frame((0.118, -0.062, 0.0), yaw=-24.0)
    cf.cyl(0.034, 0.011, (0.0, 0.0, 0.006), verts=12, mat=BRASS)
    cf.cyl(0.010, 0.112, (0.0, 0.052, 0.030), rot=(-62.0, 0.0, 0.0), verts=10, mat=BRASS)
    cf.cyl(0.017, 0.020, (0.0, 0.101, 0.060), rot=(-62.0, 0.0, 0.0), verts=12, mat=BRASS)
    cf.cyl(0.010, 0.032, (0.0, 0.112, 0.070), rot=(-62.0, 0.0, 0.0), verts=8, mat=PAPER)
    # Bottle.
    b.cyl(0.029, 0.098, (-0.108, 0.086, 0.049), verts=12, mat=GLASS)
    b.cyl(0.012, 0.040, (-0.108, 0.086, 0.116), verts=10, mat=GLASS)
    # Buckle — four bars, so it is a frame rather than a lump.
    bf = b.frame((0.028, 0.104, 0.0), yaw=32.0)
    for (sx, sy, lx, ly) in ((0.048, 0.006, 0.0, 0.019), (0.048, 0.006, 0.0, -0.019),
                             (0.006, 0.044, 0.021, 0.0), (0.006, 0.044, -0.021, 0.0)):
        bf.box((sx, sy, 0.005), (lx, ly, 0.003), mat=BRASS)
    # Loose coins.
    for (x, y) in ((-0.062, -0.048), (-0.038, -0.062), (-0.074, -0.070)):
        b.cyl(0.013, 0.0026, (x, y, 0.0013), verts=10, mat=SILVER, nobevel=True)
    return b


def build_timepiece_pocket_watch() -> PropBuild:
    """회중시계 — §08 weight 1, value 중간, "효율 최고".

    The best piece in the game per unit of weight, and it has to be easy to walk
    past: 10 cm across including a coiled chain, and a *tarnished* case so it
    never throws the highlight the silver does. §07 also makes it the only way to
    know the time from inside, so a player who finds one has found the clock.
    """
    b = PropBuild("Loot_Timepiece_PocketWatch")
    b.cyl(0.026, 0.009, (0.0, 0.0, 0.0045), verts=16, mat=BRASS_DULL, nobevel=True)
    b.cyl(0.0275, 0.004, (0.0, 0.0, 0.0105), verts=16, mat=BRASS_DULL, nobevel=True)
    b.cyl(0.0225, 0.0012, (0.0, 0.0, 0.0125), verts=16, mat=GLASS, nobevel=True)  # one small glint
    b.cyl(0.0052, 0.010, (0.0, 0.0295, 0.006), verts=8, mat=BRASS_DULL, nobevel=True)  # crown
    b.torus(0.0072, 0.0016, (0.0, 0.0395, 0.006), rot=(90.0, 0.0, 0.0), mseg=10, nseg=5,
            mat=BRASS_DULL)  # bow
    # Chain, coiled so the footprint stays small — the point is that it is missable.
    for i, (x, y) in enumerate(((0.017, 0.041), (0.032, 0.033), (0.040, 0.017),
                                (0.038, -0.002), (0.026, -0.016), (0.009, -0.021),
                                (-0.008, -0.017))):
        b.cyl(0.0055, 0.0022, (x, y, 0.0011), rot=(0.0, 0.0, i * 26.0), verts=8,
              mat=BRASS_DULL, nobevel=True)
    return b


def build_timepiece_ring() -> PropBuild:
    """반지 — §08 weight 1, value 중간. The same row as the watch, half its size.

    2.2 cm across. §08's efficiency crown sits on an object a player will step
    over, which is the design's own joke: the cheap loot is impossible to miss
    and the good loot is impossible to see.
    """
    b = PropBuild("Loot_Timepiece_Ring")
    b.torus(0.0092, 0.0016, (0.0, 0.0, 0.0092), rot=(0.0, 0.0, 0.0), mseg=16, nseg=6, mat=GOLD)
    b.cyl(0.0042, 0.0035, (0.0, 0.0, 0.0188), verts=8, mat=GOLD, nobevel=True)
    b.sph(0.0038, (0.0, 0.0, 0.0212), scale=(1.0, 1.0, 0.82), segs=10, rings=5,
          mat=GEM, nobevel=True)
    return b


def build_safe_document() -> PropBuild:
    """금고 속 문서 — §08 weight 2, value 높음, gated by the Engineer (8 s on the safe).

    Weight 2 has to look like weight 2 from a beam's distance: a folded bundle
    30 cm across, visibly bigger than any weight-1 piece and visibly smaller than
    the two-person pieces. `main()` asserts it fits the safe's measured cavity,
    because a document that cannot physically sit inside the prop it comes out of
    is the kind of thing nobody notices until level dressing.
    """
    b = PropBuild("Loot_SafeDocument")
    # Leather folder.
    b.box((0.300, 0.216, 0.008), (0.0, 0.0, 0.004), mat=LEATHER, nobevel=True)
    b.box((0.300, 0.216, 0.008), (0.0, 0.0, 0.050), rot=(0.0, 0.0, -2.5), mat=LEATHER,
          nobevel=True)
    b.box((0.014, 0.216, 0.046), (-0.143, 0.0, 0.027), mat=LEATHER, nobevel=True)
    # Paper block, sheets fanned so the stack reads as loose documents.
    for i, yaw in enumerate((-1.6, 0.9, 3.1, -3.4, 1.8)):
        b.box((0.288, 0.204, 0.0055), (0.004, 0.0, 0.012 + i * 0.0075), rot=(0.0, 0.0, yaw),
              mat=PAPER, nobevel=True)
    # Ribbon and wax seal — reads as "sealed", i.e. not something lying around.
    b.box((0.310, 0.026, 0.056), (0.0, 0.040, 0.027), mat=CLOTH, nobevel=True)
    b.cyl(0.017, 0.006, (0.0, 0.040, 0.057), verts=12, mat=WAX, nobevel=True)
    return b


def build_large_portrait() -> PropBuild:
    """대형 초상화 — §08 weight 5, value 매우 높음, 2인 운반.

    1.14 m wide and 1.52 m tall. Carried broadside it leaves
    ``1.60 - 1.14 = 0.46 m`` of an assumed corridor — less than a player's
    shoulder — so while two people move it, the corridor is *closed*. That is the
    §08 scene: the monster arrives and there is no way past your own loot.

    The two carry cleats on the back are the load-bearing detail: 1.06 m apart,
    which is past anything one player can grip, so the silhouette says "two
    people" before any UI does.
    """
    b = PropBuild("Loot_LargePiece_Portrait")
    W, H, D = 1.140, 1.520, 0.120
    stile, rail = 0.088, 0.088
    # Canvas and its stretcher.
    b.box((W - 2 * stile, 0.014, H - 2 * rail), (0.0, 0.036, H / 2), mat=PAINT, nobevel=True)
    b.box((W - 2 * stile + 0.02, 0.026, H - 2 * rail + 0.02), (0.0, 0.050, H / 2),
          mat=CANVAS, nobevel=True)
    # Frame: outer members, then an inner lip so the profile is not one flat slab.
    b.box((W, D, rail), (0.0, 0.0, rail / 2), mat=WOOD_DARK)
    b.box((W, D, rail), (0.0, 0.0, H - rail / 2), mat=WOOD_DARK)
    b.box((stile, D, H), (-(W - stile) / 2, 0.0, H / 2), mat=WOOD_DARK)
    b.box((stile, D, H), ((W - stile) / 2, 0.0, H / 2), mat=WOOD_DARK)
    b.box((W - 2 * stile, 0.036, 0.030), (0.0, -0.030, rail + 0.015), mat=GOLD)
    b.box((W - 2 * stile, 0.036, 0.030), (0.0, -0.030, H - rail - 0.015), mat=GOLD)
    b.box((0.030, 0.036, H - 2 * rail), (-(W - 2 * stile) / 2 - 0.015, -0.030, H / 2), mat=GOLD)
    b.box((0.030, 0.036, H - 2 * rail), ((W - 2 * stile) / 2 + 0.015, -0.030, H / 2), mat=GOLD)
    # Crest — a gilt top ornament. Asymmetric by design; this is not a clue.
    b.box((0.230, 0.070, 0.070), (0.0, 0.0, H + 0.020), mat=GOLD)
    b.sph(0.052, (0.0, 0.0, H + 0.070), scale=(1.0, 0.6, 0.9), segs=12, rings=6, mat=GOLD)
    # Carry cleats — the two-person tell. Tagged so the span can be measured.
    for role, sign in (("handle_a", -1.0), ("handle_b", 1.0)):
        b.box((0.070, 0.058, 0.170), (sign * 0.530, 0.086, H * 0.52), mat=WOOD_DARK, role=role)
    record_handles(b)
    return b


def build_large_chest() -> PropBuild:
    """궤짝 — §08 weight 5, value 매우 높음, and the reason the row exists.

    1.25 m long with an iron handle at each end, 1.26 m apart. Two players stand
    fore and aft, which puts a rigid 1.25 m object plus two bodies into roughly
    2 m of a corridor that §12 caps at 20 m of straight run — and neither carrier
    can see past it. §08: *"두 명이 궤짝을 들고 좁은 통로를 지나는데 괴물이 온다."*

    The lid is a squashed cylinder rather than a flat plank so the silhouette is
    unmistakable at the edge of a beam: nothing else in the kit is a dome.
    """
    b = PropBuild("Loot_LargePiece_Chest")
    L, W, BODY_H = 1.220, 0.680, 0.480
    body = b.box((L, W, BODY_H), (0.0, 0.0, BODY_H / 2), mat=WOOD, role="body")
    b.pivot_part = body
    # Domed lid: a cylinder along X, squashed in Z. Its lower half hides inside the
    # body, which costs a few triangles and buys the profile.
    lid = b.cyl(0.340, L, (0.0, 0.0, BODY_H), rot=(0.0, 90.0, 0.0), verts=16, mat=WOOD,
                role="lid", nobevel=True)
    squash_lid_z(lid, 0.240 / 0.340)
    # Iron bands and corner brackets. The two that cross the lid have to take the
    # same squash, or they hoop above the dome and the chest grows 11 cm.
    for x in (-0.420, 0.0, 0.420):
        b.box((0.052, W + 0.014, BODY_H + 0.008), (x, 0.0, BODY_H / 2), mat=IRON)
    for x in (-0.420, 0.420):
        band = b.cyl(0.348, 0.052, (x, 0.0, BODY_H), rot=(0.0, 90.0, 0.0), verts=16,
                     mat=IRON, nobevel=True)
        squash_lid_z(band, 0.240 / 0.340)
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.box((0.070, 0.070, 0.070), (sx * (L / 2 - 0.030), sy * (W / 2 - 0.030), 0.030),
                  mat=IRON)
    # Hasp and lock plate.
    b.box((0.120, 0.030, 0.140), (0.0, -W / 2 - 0.006, BODY_H - 0.020), mat=IRON)
    b.box((0.062, 0.036, 0.070), (0.0, -W / 2 - 0.010, BODY_H - 0.075), mat=BRASS_DULL)
    # End handles — 1.26 m apart, which is what makes a solo carry unreadable.
    for role, sign in (("handle_a", -1.0), ("handle_b", 1.0)):
        x = sign * (L / 2 + 0.014)
        b.box((0.028, 0.200, 0.026), (x, 0.0, 0.400), mat=IRON, role=role)
        b.box((0.028, 0.026, 0.104), (x, -0.087, 0.352), mat=IRON)
        b.box((0.028, 0.026, 0.104), (x, 0.087, 0.352), mat=IRON)
    record_handles(b)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  INTERACTABLES
# ══════════════════════════════════════════════════════════════════════════


def build_objective() -> PropBuild:
    """§03의 목표물 — the thing the match is for. Until now a 0.45 m Unity capsule.

    The design fixes its rules and says nothing about its shape, so the shape is
    derived from the rules and from what else is in the building:

    * **양손을 쓴다.** One grab handle on each end, 0.62 m apart — outside a
      comfortable one-hand reach and inside a two-hand one, so 양손 is legible from
      the silhouette rather than only from a UI string. It is a *one*-person
      carry, unlike §08's chest and portrait, so the handles are deliberately close
      enough that a single player spans them.
    * **운반자는 앞을 보지 못한다.** 0.54 m wide and 0.40 m tall, held at chest
      height: that is a body-width of solid object between the carrier's eyes and
      the corridor. A capsule 0.45 m across on the floor never sold that.
    * **The rest of §08 is an estate being stripped** — portraits, ledgers, pocket
      watches, silver. So this is not a sci-fi container. Oak, iron strapping, a
      brass plate, and a wax seal over the hasp.
    * **Sealed, not openable.** The seal is the whole characterisation available to
      a prop with no animation: whatever this is, the crew are being paid to carry
      it out without looking inside, and the one thing a player can read off it is
      that it has not been opened.

    Deliberately NOT emissive. §03 makes darkness the lock on the objective
    (*"어둠 = 목표의 잠금장치"*), so the brass plate and the strapping are the only
    things that answer a beam, and they answer it by being specular rather than by
    glowing on their own.
    """
    b = PropBuild("Objective")
    L, W, H = 0.540, 0.360, 0.300      # body; the lid adds 10 cm
    BAND = 0.030

    body = b.box((L, W, H), (0.0, 0.0, H / 2), mat=WOOD_DARK, role="body")
    b.pivot_part = body

    # Lid: a shallow slab with a raised rim, so the top catches a beam as two
    # distinct planes rather than one flat face.
    b.box((L, W, 0.070), (0.0, 0.0, H + 0.035), mat=WOOD_DARK, role="lid")
    b.box((L - 0.070, W - 0.070, 0.028), (0.0, 0.0, H + 0.084), mat=WOOD_DARK)

    # Iron strapping: two bands over the lid and down the sides, plus a waist band.
    for x in (-L / 4, L / 4):
        b.box((BAND, W + 0.012, H + 0.076), (x, 0.0, (H + 0.070) / 2), mat=IRON)
    b.box((L + 0.012, W + 0.012, BAND), (0.0, 0.0, H * 0.55), mat=RUST)

    # Corner brackets on the eight verticals of the body.
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.box((0.058, 0.058, 0.058),
                  (sx * (L / 2 - 0.024), sy * (W / 2 - 0.024), 0.029), mat=IRON)

    # Hasp and the wax seal across it. The seal spans the lid joint, which is what
    # makes it read as a seal and not as a decoration.
    b.box((0.110, 0.026, 0.150), (0.0, -W / 2 - 0.010, H - 0.012), mat=IRON, role="hasp")
    b.cyl(0.046, 0.022, (0.0, -W / 2 - 0.026, H - 0.030), rot=(90.0, 0.0, 0.0),
          verts=12, mat=WAX, role="seal")

    # Engraved plate on the lid — the same brass the §12 clue plates are cut from,
    # so a player who has read a clue recognises the metal on the objective.
    b.box((0.180, 0.110, 0.008), (0.0, 0.040, H + 0.100), mat=BRASS_DULL, role="plate")

    # End handles. Drop handles, not fixed loops: a bail lying against the end is
    # what a chest of this period has, and it keeps the footprint honest.
    for role, sign in (("handle_a", -1.0), ("handle_b", 1.0)):
        x = sign * (L / 2 + 0.016)
        b.box((0.026, 0.170, 0.024), (x, 0.0, H * 0.72), mat=IRON, role=role)
        for sy in (-1.0, 1.0):
            b.box((0.026, 0.024, 0.076), (x, sy * 0.073, H * 0.72 - 0.050), mat=IRON)
    record_handles(b)
    return b


SAFE_W, SAFE_D, SAFE_H = 0.720, 0.620, 0.840
SAFE_WALL = 0.060
SAFE_DOOR_T = 0.080


def _safe(name: str, door_angle: float) -> PropBuild:
    """Shared body for both §08 safe variants — 8 s of Engineer work (§04, §08).

    The two variants must be drop-in swappable in Unity, so the body is built
    from one set of constants and `main()` compares the closed and open bodies'
    footprints rather than trusting that.
    """
    b = PropBuild(name)
    hw, hd, hh = SAFE_W / 2, SAFE_D / 2, SAFE_H
    inner_h = hh - 2 * SAFE_WALL
    # Japanned sheet steel, not bare iron. A safe is a flat-panelled box, and
    # `check_metal_is_not_a_panel` measured its door at 0.65 m² of `Prop_Iron` —
    # the 차량's defect at a twelfth of the size, on the object §04 has the 정비공
    # stand over for 8 s with a torch. Photographed at 2 m under the game's own beam
    # it was a black cut-out on a lit tile floor with nothing on it but the brass
    # dial. The dial, the handle and the hinges stay iron: they are the parts that
    # really are bare, they are small and round, and a highlight is what reads them.
    body_parts = [
        b.box((SAFE_W, SAFE_D, SAFE_WALL), (0.0, 0.0, SAFE_WALL / 2), mat=PAINTED_STEEL,
              role="floor"),
        b.box((SAFE_W, SAFE_D, SAFE_WALL), (0.0, 0.0, hh - SAFE_WALL / 2), mat=PAINTED_STEEL,
              role="ceil"),
        b.box((SAFE_W, SAFE_WALL, inner_h), (0.0, hd - SAFE_WALL / 2, hh / 2), mat=PAINTED_STEEL,
              role="back"),
        b.box((SAFE_WALL, SAFE_D, inner_h), (-hw + SAFE_WALL / 2, 0.0, hh / 2), mat=PAINTED_STEEL,
              role="left"),
        b.box((SAFE_WALL, SAFE_D, inner_h), (hw - SAFE_WALL / 2, 0.0, hh / 2), mat=PAINTED_STEEL,
              role="right"),
    ]
    b.pivot_part = body_parts[0]
    b.named["body_ref"] = body_parts[0]
    # A shelf, so the cavity reads as a place things are kept rather than a void.
    b.box((SAFE_W - 2 * SAFE_WALL - 0.02, SAFE_D - SAFE_WALL - SAFE_DOOR_T - 0.02, 0.026),
          (0.0, 0.010, hh / 2), mat=IRON, role="shelf")

    # Door authored in hinge-local coordinates: hinge on the left front corner.
    hinge = (-hw, -hd + SAFE_DOOR_T / 2, hh / 2)
    dl: list[bpy.types.Object] = []
    dl.append(b.box((SAFE_W, SAFE_DOOR_T, hh - 2 * SAFE_WALL + 0.04),
                    (hw, 0.0, 0.0), mat=PAINTED_STEEL, role="door_panel"))
    dl.append(b.box((SAFE_W - 0.06, 0.014, hh - 2 * SAFE_WALL - 0.02),
                    (hw, -SAFE_DOOR_T / 2 - 0.007, 0.0), mat=PAINTED_STEEL))
    # Combination dial.
    dl.append(b.cyl(0.088, 0.030, (hw, -SAFE_DOOR_T / 2 - 0.015, 0.110), rot=(90.0, 0.0, 0.0),
                    verts=16, mat=BRASS_DULL, nobevel=True))
    dl.append(b.cyl(0.030, 0.052, (hw, -SAFE_DOOR_T / 2 - 0.026, 0.110), rot=(90.0, 0.0, 0.0),
                    verts=12, mat=BRASS, nobevel=True))
    for i in range(4):
        dl.append(b.box((0.150, 0.014, 0.012), (hw, -SAFE_DOOR_T / 2 - 0.020, 0.110),
                        rot=(0.0, i * 45.0, 0.0), mat=BRASS))
    # Three-spoke handle.
    dl.append(b.cyl(0.034, 0.060, (hw, -SAFE_DOOR_T / 2 - 0.030, -0.150), rot=(90.0, 0.0, 0.0),
                    verts=12, mat=IRON, nobevel=True))
    for i in range(3):
        dl.append(b.box((0.024, 0.024, 0.220), (hw, -SAFE_DOOR_T / 2 - 0.040, -0.150),
                        rot=(0.0, i * 60.0, 0.0), mat=IRON))
    for z in (-0.290, 0.290):
        dl.append(b.cyl(0.024, 0.090, (0.010, 0.0, z), verts=10, mat=IRON, nobevel=True))
    door = b.hinge_group("door", dl, hinge, door_angle, axis="Z")

    # Cavity, measured from the placed shell rather than restated from constants.
    left = b.named["left"]
    right = b.named["right"]
    back = b.named["back"]
    floor = b.named["floor"]
    ceil = b.named["ceil"]
    shelf = b.named["shelf"]
    cav_x = world_bbox([right])[0].x - world_bbox([left])[1].x
    cav_y = world_bbox([back])[0].y - (-SAFE_D / 2 + SAFE_DOOR_T)
    cav_z = world_bbox([ceil])[0].z - world_bbox([floor])[1].z
    shelf_z = world_bbox([shelf])[0].z - world_bbox([floor])[1].z
    b.meta["cavity"] = (cav_x, cav_y, cav_z)
    b.meta["cavity_lower"] = (cav_x, cav_y, shelf_z)
    b.meta["body_footprint"] = tuple(bbox_size(body_parts))
    b.meta["door_angle"] = door_angle
    return b


def build_safe_closed() -> PropBuild:
    """금고, shut — what §08's high-value document looks like before the Engineer."""
    return _safe("Safe_Closed", 0.0)


def build_safe_open() -> PropBuild:
    """금고, open — the 8 s payoff, with a shelf the weight-2 document sits on.

    The door swings 100° toward -Y, i.e. into the room, so an open safe eats
    corridor space and a player has to walk around their own success.
    """
    return _safe("Safe_Open", -100.0)


def build_electrical_panel() -> PropBuild:
    """전기 패널 — §12 requires exactly one per zone for the Engineer.

    §03 puts the whole objective behind light ("어둠 = 목표의 잠금장치") and gives the
    Engineer the zone-light switch, whose cost is that "괴물도 그쪽으로 온다". This
    is that switch. Wall-mounted, so the pivot sits at the wall's foot and the
    enclosure lands at 1.01-1.69 m with no offset to remember.

    The big lever is deliberately readable from across a dark room, and the
    indicator lens is emissive: a player needs to see from the corridor whether
    the zone is live before deciding to walk into a lit room.
    """
    b = PropBuild("ElectricalPanel")
    W, D, H = 0.520, 0.150, 0.680
    z0 = 1.010
    zc = z0 + H / 2
    back = b.box((W, 0.030, H), (0.0, -0.015, zc), mat=RUST, role="back")
    b.pivot_part = back
    # Enclosure lip: two rails and two stiles standing off the backplate.
    lip_y = -0.030 - (D - 0.030) / 2
    for lz in (H / 2 - 0.015, -H / 2 + 0.015):
        b.box((W, D - 0.030, 0.030), (0.0, lip_y, zc + lz), mat=RUST)
    for lx in (-W / 2 + 0.015, W / 2 - 0.015):
        b.box((0.030, D - 0.030, H), (lx, lip_y, zc), mat=RUST)
    # Breaker bank. Painted sheet, like every consumer unit ever made — and the
    # 정비공 has to find this in a dark corridor, which a 90 % metal cannot help with.
    b.box((W - 0.090, 0.040, H - 0.120), (0.0, -0.052, zc), mat=PAINTED_STEEL)
    for i in range(6):
        x = -0.170 + i * 0.068
        b.box((0.046, 0.024, 0.108), (x, -0.082, zc + 0.130), mat=CLOTH)
        b.box((0.030, 0.036, 0.040), (x, -0.096, zc + 0.150 - (0.052 if i % 2 else 0.0)),
              mat=BRASS_DULL)
    # Main lever — the Engineer's zone light, big enough to read from a corridor.
    b.box((0.070, 0.050, 0.240), (-0.150, -0.086, zc - 0.150), rot=(-24.0, 0.0, 0.0), mat=IRON)
    b.cyl(0.034, 0.048, (-0.150, -0.130, zc - 0.252), rot=(90.0, 0.0, 0.0), verts=12, mat=RUST)
    b.box((0.190, 0.030, 0.070), (0.060, -0.076, zc - 0.230), mat=CLOTH)
    # Live indicator, emissive: §03's light is information as much as illumination.
    b.cyl(0.026, 0.026, (0.180, -0.086, zc - 0.230), rot=(90.0, 0.0, 0.0), verts=12,
          mat=LAMP, nobevel=True)
    # Conduit leaving the top, so the panel belongs to a building.
    for x in (-0.140, 0.140):
        b.cyl(0.026, 0.360, (x, -0.048, z0 + H + 0.160), verts=10, mat=RUST, nobevel=True)
        b.cyl(0.034, 0.040, (x, -0.048, z0 + H + 0.020), verts=10, mat=IRON, nobevel=True)
    b.meta["mount_z"] = (z0, z0 + H)
    return b


def build_surface_generator() -> PropBuild:
    """지상 발전기 — §03's battery source, and therefore the reason to come back out.

    §03's round-trip table makes flashlight battery the resource that forces the
    trip: *"배터리가 떨어지면 단서를 읽을 수 없다."* This is where that trip ends. Four
    charging cradles, one per player (§11 sizes the team at four), each with an
    emissive lens so a returning team can see from a distance whether their
    batteries are done — §07 prices the wait in threat, so the answer has to be
    readable without walking over.
    """
    b = PropBuild("SurfaceGenerator")
    L, W = 1.020, 0.620
    frame_z, top_z = 0.120, 0.740
    base = b.box((L, W, 0.060), (0.0, 0.0, frame_z + 0.030), mat=IRON, role="base")
    b.pivot_part = base
    # Roll cage. Tubes are left unbevelled: an 8-sided tube already reads as round
    # in a beam and bevelling twelve of them costs ~700 triangles for nothing.
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.cyl(0.022, top_z - frame_z, (sx * (L / 2 - 0.040), sy * (W / 2 - 0.040),
                                           (frame_z + top_z) / 2), verts=8, mat=RUST,
                  nobevel=True)
    for sy in (-1.0, 1.0):
        b.cyl(0.020, L - 0.080, (0.0, sy * (W / 2 - 0.040), top_z), rot=(0.0, 90.0, 0.0),
              verts=8, mat=RUST, nobevel=True)
    for sx in (-1.0, 1.0):
        b.cyl(0.020, W - 0.080, (sx * (L / 2 - 0.040), 0.0, top_z), rot=(90.0, 0.0, 0.0),
              verts=8, mat=RUST, nobevel=True)
    # Fuel tank and engine.
    b.box((0.460, 0.420, 0.200), (-0.200, 0.0, 0.640), mat=RUST)
    b.cyl(0.040, 0.070, (-0.200, 0.0, 0.760), verts=12, mat=IRON)
    # Painted block. A field generator's engine wears enamel; the frame it is
    # bolted to, its exhaust and its flywheel housing do not, and keep their iron.
    b.box((0.360, 0.400, 0.300), (0.180, 0.0, 0.330), mat=PAINTED_STEEL)
    b.cyl(0.140, 0.160, (0.180, -0.240, 0.330), rot=(90.0, 0.0, 0.0), verts=16, mat=IRON)
    b.cyl(0.052, 0.030, (0.180, -0.330, 0.330), rot=(90.0, 0.0, 0.0), verts=12, mat=RUST)
    # Exhaust.
    b.cyl(0.034, 0.280, (0.330, 0.180, 0.400), verts=10, mat=RUST)
    b.cyl(0.038, 0.120, (0.330, 0.180, 0.560), rot=(90.0, 0.0, 0.0), verts=10, mat=IRON)
    # Pull start.
    b.box((0.130, 0.030, 0.030), (0.180, -0.330, 0.470), mat=WOOD)
    # Charging cradles — one per player (§11), each with a lens that reads at distance.
    for i in range(4):
        y = -0.195 + i * 0.130
        b.box((0.120, 0.098, 0.070), (-0.340, y, 0.510), mat=CLOTH)
        b.box((0.020, 0.060, 0.016), (-0.404, y, 0.545), mat=LAMP, nobevel=True)
    b.box((0.030, 0.520, 0.220), (-0.290, 0.0, 0.480), mat=PAINTED_STEEL)
    # Wheels, so it reads as a thing that was dragged here.
    for sy in (-1.0, 1.0):
        b.cyl(0.088, 0.048, (-0.420, sy * 0.250, 0.088), rot=(0.0, 90.0, 0.0), verts=12,
              mat=WOOD_DARK, nobevel=True)
    b.meta["cradles"] = 4
    return b


def build_vehicle() -> PropBuild:
    """지상 차량 — §08's safe zone, shop and supply point in one object.

    *"지상 차량 = 안전 지대 + 상점 + 보급소."* Players run toward this, so it has to
    read as safety from further away than anything else in the game reads as
    anything. That is done with light, because §03 makes darkness the threat:
    a roof floodbar, a beacon, headlamps, a lit shop hatch and a lit battery rack
    inside the bay — all emissive, all visible before the silhouette resolves.

    The rear ramp is down and the bay is open, which is the supply read (§03's
    round trip ends by walking *in*). The side counter is the shop, where §08's
    shared wallet gets argued over. The only prop in the kit exempt from the 1500
    triangle ceiling, because it is the one object seen from 40 m.

    **The bodywork is painted, and that is a material decision with a measurement
    behind it** (ART.md §7.12). It used to be `Prop_Iron` — metallic 0.90 over an
    albedo of 0.11 — and a 90 % metal has no diffuse response at all: it renders
    what it reflects, and in a basement with no reflection probe and a 12 m torch
    there is nothing to reflect. The owner's own frames are the evidence: with
    *five* warm sources of its own the van still photographed as a black cut-out,
    and adding a sixth could not have worked. A works van is painted steel, so the
    panels are dielectric — metallic 0, a real albedo, gloss carried by roughness —
    and the parts that really are bare metal keep `Prop_Iron`: chassis, bumper,
    grille slats, wheel hubs, rubbing rails, ladder, ramp edges.

    Three things beyond the colour, because a single flat coat over 3.4 m² is still
    a slab: the flank is **two-tone** with a rubbing rail on the seam, so the box
    has a waistline; there are **vertical body ribs**, which is what a real box body
    is built from and what gives an untextured panel its own shading; and there is a
    **grille**, so the face a player walks up to has something in it besides two
    lamps. All of it is geometry the beam can find, which is the only kind of
    surface detail this kit has — the props carry no textures.
    """
    b = PropBuild("Vehicle")
    body_w, wall = 2.200, 0.080
    bay_y0, bay_y1 = -2.900, 1.050          # cargo bay, open at -Y
    floor_z, roof_z = 0.750, 2.570
    bay_mid, bay_len = (bay_y0 + bay_y1) / 2, bay_y1 - bay_y0
    waist_z = floor_z + 0.620               # where the livery breaks to the skirt
    chassis = b.box((1.900, 5.560, 0.180), (0.0, -0.100, 0.620), mat=IRON, role="chassis")
    b.pivot_part = chassis
    # Cargo bay: floor, roof, two flanks and the bulkhead behind the cab.
    b.box((body_w, bay_len, wall), (0.0, bay_mid, floor_z), mat=WOOD)
    b.box((body_w + 0.040, bay_len, wall), (0.0, bay_mid, roof_z), mat=VAN_BODY)
    for sx in (-1.0, 1.0):
        x = sx * (body_w / 2 - wall / 2)
        # Flank in two coats. The seam is a real waistline rather than a texture
        # boundary, so it survives being lit from any direction.
        b.box((wall, bay_len, waist_z - floor_z), (x, bay_mid, (floor_z + waist_z) / 2),
              mat=VAN_LOWER)
        b.box((wall, bay_len, roof_z - waist_z), (x, bay_mid, (waist_z + roof_z) / 2),
              mat=VAN_BODY)
        # Rubbing rail on the seam, and vertical body ribs above it. Both stand
        # proud of the flank, so each one is a highlight and a shadow of its own.
        b.box((0.034, bay_len, 0.070), (sx * (body_w / 2 + 0.006), bay_mid, waist_z),
              mat=IRON, nobevel=True)
        for i in range(5):
            y = bay_y0 + bay_len * (i + 0.5) / 5.0
            # The +X flank carries the shop hatch and its frame; a rib behind the
            # frame would be two coincident faces fighting for the same pixels.
            if sx > 0.0 and -1.80 < y < -0.20:
                continue
            b.box((0.026, 0.070, roof_z - waist_z - 0.090),
                  (sx * (body_w / 2 + 0.004), y, (waist_z + roof_z) / 2 + 0.045),
                  mat=VAN_BODY, nobevel=True)
    b.box((body_w, wall, roof_z - floor_z), (0.0, bay_y1 - wall / 2, (floor_z + roof_z) / 2),
          mat=VAN_LOWER)
    # Cab.
    b.box((2.100, 1.600, 1.400), (0.0, 1.900, 1.410), mat=VAN_BODY)
    b.box((1.900, 0.060, 0.720), (0.0, 1.135, 1.760), rot=(-9.0, 0.0, 0.0), mat=GLASS)
    for sx in (-1.0, 1.0):
        b.box((0.060, 0.680, 0.560), (sx * 1.030, 1.900, 1.760), mat=GLASS)
    # Cab skirt, carrying the flank's waistline forward so the two volumes read as
    # one vehicle rather than as a box sitting on a box.
    b.box((2.108, 1.604, 0.360), (0.0, 1.900, 0.890), mat=VAN_LOWER)
    b.box((2.200, 0.140, 0.240), (0.0, 2.740, 0.560), mat=IRON)
    # Grille: a painted surround with bare steel slats, set between the headlamps
    # and kept inside the bumper line so the van's own footprint does not grow.
    b.box((1.320, 0.060, 0.600), (0.0, 2.715, 1.060), mat=VAN_BODY)
    for i in range(5):
        b.box((1.180, 0.056, 0.046), (0.0, 2.732, 0.850 + i * 0.105), mat=IRON, nobevel=True)
    # Headlamps — the first thing visible down a surface track.
    for sx in (-1.0, 1.0):
        b.cyl(0.110, 0.060, (sx * 0.740, 2.740, 0.920), rot=(90.0, 0.0, 0.0), verts=12,
              mat=LAMP, nobevel=True)
    # Mudguards over each axle. The skirt above them is a flat 2.4 m² band and the
    # wheels hang under it with daylight between; a guard closes that gap and is what
    # stops the profile reading as a box that happens to have wheels near it. Painted,
    # like the panel they hang off, and standing 4 cm proud so each one is its own
    # highlight and its own shadow across the skirt.
    for sx in (-1.0, 1.0):
        for (y, span) in ((1.850, 1.240), (-1.465, 1.320)):
            b.box((0.320, span, 0.052), (sx * 0.980, y, 0.880), mat=VAN_LOWER)
    # Wheels.
    for (x, y) in ((-0.980, 1.850), (0.980, 1.850), (-0.980, -1.150), (0.980, -1.150),
                   (-0.980, -1.780), (0.980, -1.780)):
        b.cyl(0.420, 0.280, (x, y, 0.420), rot=(0.0, 90.0, 0.0), verts=12, mat=CLOTH,
              nobevel=True)
        b.cyl(0.180, 0.300, (x, y, 0.420), rot=(0.0, 90.0, 0.0), verts=10, mat=IRON,
              nobevel=True)
    # Rear ramp, folded down to the ground: walk-in supply point.
    ramp: list[bpy.types.Object] = []
    ramp.append(b.box((2.100, 1.200, 0.070), (0.0, -0.600, 0.0), mat=WOOD, role="ramp_deck"))
    for sx in (-1.0, 1.0):
        ramp.append(b.box((0.060, 1.200, 0.100), (sx * 1.020, -0.600, 0.060), mat=IRON))
    b.hinge_group("ramp", ramp, (0.0, bay_y0, floor_z + 0.040), 41.0, axis="X")
    # Inside: shelving, battery rack, crates, cans.
    for sx in (-1.0, 1.0):
        for z in (1.280, 1.800):
            b.box((0.380, 2.600, 0.050), (sx * 0.860, -1.400, z), mat=WOOD)
    b.box((0.640, 0.240, 0.500), (-0.680, 0.700, 1.060), mat=IRON)
    for i in range(6):
        b.box((0.074, 0.020, 0.100), (-0.930 + i * 0.100, 0.578, 1.060), mat=LAMP, nobevel=True)
    for (x, y) in ((0.560, 0.560), (0.560, 0.060), (-0.020, 0.640)):
        b.box((0.420, 0.420, 0.380), (x, y, floor_z + 0.230), mat=WOOD)
    for x in (0.700, 0.880):
        b.box((0.160, 0.320, 0.400), (x, -0.700, floor_z + 0.240), mat=RUST)
    # Roof floodbar aimed back over the bay, and the beacon.
    b.box((1.700, 0.180, 0.110), (0.0, 0.760, roof_z + 0.095), mat=IRON)
    for i in range(4):
        b.box((0.320, 0.060, 0.100), (-0.585 + i * 0.390, 0.665, roof_z + 0.095), mat=LAMP,
              nobevel=True)
    b.cyl(0.032, 0.150, (0.780, 0.400, roof_z + 0.115), verts=8, mat=IRON, nobevel=True)
    b.sph(0.092, (0.780, 0.400, roof_z + 0.265), scale=(1.0, 1.0, 0.85), segs=12, rings=6,
          mat=LAMP, nobevel=True)
    # Shop: fold-out counter on +X with a hatch frame and a lantern over it.
    b.box((0.480, 1.400, 0.060), (1.320, -1.000, 1.180), mat=WOOD)
    for y in (-1.620, -0.380):
        b.box((0.050, 0.050, 0.420), (1.520, y, 0.970), mat=IRON)
    b.box((0.600, 1.500, 0.050), (1.380, -1.000, 1.900), rot=(0.0, 8.0, 0.0), mat=CLOTH)
    for (sy, sz, ly, lz) in ((0.060, 0.760, -0.720, 0.0), (0.060, 0.760, 0.720, 0.0),
                             (1.500, 0.060, 0.0, -0.380), (1.500, 0.060, 0.0, 0.380)):
        b.box((0.040, sy, sz), (1.140, -1.000 + ly, 1.560 + lz), mat=WOOD_DARK)
    b.sph(0.070, (1.240, -1.000, 1.780), segs=10, rings=5, mat=LAMP, nobevel=True)
    # Rear ladder.
    for sx in (-1.0, 1.0):
        b.box((0.050, 0.050, 1.700), (sx * 0.500, bay_y0 - 0.070, 1.620), mat=IRON)
    for i in range(4):
        b.box((1.000, 0.040, 0.040), (0.0, bay_y0 - 0.070, 0.980 + i * 0.400), mat=IRON)
    b.meta["lamp_parts"] = sum(1 for o in b.parts if o.data.materials
                               and o.data.materials[0].name == LAMP)
    return b


def build_clue_wall_board() -> PropBuild:
    """벽 게시판 — a §03 clue read in place, with §03's 좌↔우 condition built in.

    §03 forbids carrying a clue out: *"그 자리에서 보고, 기억해서, 말로 전달해야 한다."*
    So the prop is a face and a viewing condition, nothing else. The face is a
    single quad with material ``Clue_Face`` mapped 0..1, which is where §13's
    host-rendered glyph lands.

    The condition here is the **polished pane in the same frame**, one panel over
    from the glyph. §03 lists 좌↔우 as happening at "거울 · 반사면"; a player who
    reads the reflection instead of the board — likely, in a beam, at an angle,
    in a hurry — swaps left and right and sends the team the wrong way. Mounted
    at 1.09-1.81 m, i.e. across standing eye height, so the reflection is at eye
    level too.
    """
    b = PropBuild("Clue_WallBoard")
    W, D, H = 0.960, 0.100, 0.720
    z0 = 1.090
    zc = z0 + H / 2
    back = b.box((W, 0.026, H), (0.0, -0.013, zc), mat=WOOD_DARK, role="back")
    b.pivot_part = back
    b.box((W, D - 0.026, 0.048), (0.0, -0.026 - (D - 0.026) / 2, zc + H / 2 - 0.024),
          mat=WOOD_DARK)
    b.box((W, D - 0.026, 0.048), (0.0, -0.026 - (D - 0.026) / 2, zc - H / 2 + 0.024),
          mat=WOOD_DARK)
    for sx in (-1.0, 1.0):
        b.box((0.048, D - 0.026, H), (sx * (W / 2 - 0.024), -0.026 - (D - 0.026) / 2, zc),
              mat=WOOD_DARK)
    b.box((0.036, D - 0.026, H - 0.096), (0.0, -0.026 - (D - 0.026) / 2, zc), mat=WOOD_DARK)
    # Left panel: the clue itself, recessed behind the frame so a beam has to be
    # aimed rather than swept.
    b.box((0.400, 0.014, 0.560), (-0.234, -0.040, zc), mat=PAPER, nobevel=True)
    b.quad(0.380, 0.540, (-0.234, -0.049, zc), rot=(90.0, 0.0, 0.0), mat=CLUE_FACE,
           role="clue_face")
    # Right panel: the reflector. §03's 좌↔우 lives here.
    b.box((0.400, 0.014, 0.560), (0.234, -0.040, zc), mat=IRON, nobevel=True)
    b.quad(0.386, 0.546, (0.234, -0.048, zc), rot=(90.0, 0.0, 0.0), mat=MIRROR,
           role="mirror_face")
    b.meta["mount_z"] = (z0, z0 + H)
    b.meta["confusion_pair"] = "좌 ↔ 우 (거울 · 반사면)"
    return b


def build_clue_ledger_stand() -> PropBuild:
    """장부 받침대 — a §03 clue on a lectern, tilted for §03's 1↔7 condition.

    The face sits at 32° and 1.06 m: a standing player reads it at a glancing
    angle with a narrow beam raking across it, which is the condition §03 files
    under 손글씨체 for 1↔7 — an ascender and a serif are the same smudge at that
    angle. Matte, so a beam held too close washes it out instead of resolving it
    (§03's ㅁ↔ㅇ, "흐릿할 때").

    Two escape routes are a §12 placement rule, not a prop rule, but the stand is
    freestanding and knee-height-narrow so it never blocks one.
    """
    b = PropBuild("Clue_LedgerStand")
    base = b.box((0.440, 0.400, 0.052), (0.0, 0.0, 0.026), mat=WOOD_DARK, role="base")
    b.pivot_part = base
    b.box((0.300, 0.280, 0.030), (0.0, 0.0, 0.066), mat=WOOD_DARK)
    b.box((0.110, 0.110, 0.880), (0.0, 0.0, 0.520), mat=WOOD_DARK)
    b.cyl(0.078, 0.060, (0.0, 0.0, 0.500), verts=12, mat=WOOD_DARK)
    b.box((0.540, 0.400, 0.040), (0.0, -0.020, 1.010), rot=(32.0, 0.0, 0.0), mat=WOOD_DARK,
          role="desk")
    b.box((0.540, 0.044, 0.052), (0.0, -0.182, 0.930), mat=WOOD_DARK)
    # The ledger: covers plus a page block, opened flat on the slope.
    b.box((0.420, 0.320, 0.026), (0.0, -0.016, 1.036), rot=(32.0, 0.0, 0.0), mat=LEATHER,
          nobevel=True)
    b.box((0.400, 0.300, 0.022), (0.0, -0.010, 1.058), rot=(32.0, 0.0, 0.0), mat=PAPER,
          nobevel=True)
    b.quad(0.360, 0.260, (0.0, -0.017, 1.074), rot=(32.0, 0.0, 0.0), mat=CLUE_FACE,
           role="clue_face")
    # A quill and a spent candle: the reading light that is not there any more.
    b.box((0.010, 0.180, 0.010), (0.160, 0.040, 1.070), rot=(32.0, 0.0, -14.0), mat=PAPER,
          nobevel=True)
    b.cyl(0.030, 0.024, (-0.210, 0.120, 0.960), verts=12, mat=BRASS_DULL)
    b.cyl(0.017, 0.060, (-0.210, 0.120, 1.000), verts=10, mat=PAPER, nobevel=True)
    b.meta["face_tilt_deg"] = 32.0
    b.meta["confusion_pair"] = "1 ↔ 7 (손글씨체), ㅁ ↔ ㅇ (흐릿할 때)"
    return b


def build_clue_engraved_plate() -> PropBuild:
    """각자판 — a §03 clue at floor level, built so 6↔9 is unavoidable.

    §03 makes 6↔9 happen "뒤집힌 각도에서". A clue you can only stand in front of
    never produces it, so this one has **no front**: an octagonal pedestal 0.16 m
    tall with a square engraved face on top, symmetric under a half-turn, no
    border, no plinth cue, no arrow. Read it from the far side of the room and 6
    *is* 9 — and two players who walked in from different doors will disagree,
    out loud, which is exactly §03's stated goal for the confusion pairs.

    `main()` asserts the symmetry to 1 mm rather than trusting the construction;
    a single asymmetric stud would quietly restore the up-cue and kill the pair.
    """
    b = PropBuild("Clue_EngravedPlate")
    ped = b.cyl(0.300, 0.120, (0.0, 0.0, 0.060), verts=16, mat=STONE, role="pedestal")
    b.pivot_part = ped
    b.cyl(0.318, 0.028, (0.0, 0.0, 0.106), verts=16, mat=STONE, nobevel=True)
    b.cyl(0.250, 0.020, (0.0, 0.0, 0.130), verts=16, mat=BRASS_DULL, nobevel=True)
    # Four studs at 90° — still symmetric under a half-turn, so no up-cue is added.
    for i in range(4):
        a = math.radians(45.0 + i * 90.0)
        b.cyl(0.018, 0.014, (0.272 * math.cos(a), 0.272 * math.sin(a), 0.147), verts=8,
              mat=BRASS, nobevel=True)
    b.quad(0.300, 0.300, (0.0, 0.0, 0.141), rot=(0.0, 0.0, 0.0), mat=CLUE_FACE,
           role="clue_face")
    b.meta["confusion_pair"] = "6 ↔ 9 (뒤집힌 각도에서)"
    b.meta["symmetry_required"] = True
    return b


def build_barricade() -> PropBuild:
    """차단물 — the Engineer's 자재 spent on geometry (§04, §08's 정비 자재).

    §06 is explicit that aggro release is not about distance: *"어그로 해제는 거리가
    아니라 맵을 쓰는 것이다."* A barricade is the Engineer editing the map mid-match.
    Sized to plug a doorway: 1.26 m wide, 1.12 m tall — high enough to stop a
    sight line at crouch height, low enough that a player can see what is on the
    other side before committing, because §04 warns the Engineer's mistakes kill
    teammates and a solid wall would hide one.
    """
    b = PropBuild("Barricade")
    b.box((0.090, 0.090, 1.120), (-0.560, 0.0, 0.560), mat=WOOD, role="post_l")
    b.box((0.090, 0.090, 1.120), (0.560, 0.0, 0.560), mat=WOOD, role="post_r")
    for i, (z, tilt) in enumerate(((0.180, 1.6), (0.450, -2.4), (0.720, 1.1), (0.990, -1.8))):
        b.box((1.220, 0.044, 0.180), (0.0, -0.070, z), rot=(0.0, tilt, 0.0), mat=WOOD)
    b.box((1.360, 0.040, 0.140), (0.0, 0.062, 0.560), rot=(0.0, -39.0, 0.0), mat=WOOD)
    for sx in (-1.0, 1.0):
        b.box((0.070, 0.320, 0.070), (sx * 0.560, 0.110, 0.035), mat=WOOD)
        b.box((0.060, 0.060, 0.420), (sx * 0.560, 0.230, 0.260), rot=(38.0, 0.0, 0.0), mat=WOOD)
    return b


def build_noise_trap() -> PropBuild:
    """소음 함정 — the Engineer's noise trap (§04), and §06's silence turned around.

    §06 makes the monster's standstill the game's weapon because it produces
    silence and the Listener loses it. This is the counter-weapon, and it works on
    the same channel: a struck bell puts a fix on the monster's position in the
    one state where it gives nothing away. It is also §04's accident generator —
    the tripwire does not care who crosses it, and the Runner will.

    Built so it is legible as a *sound* device, not a damage device: exposed
    spring, hanging clapper, wire on a stake.
    """
    b = PropBuild("NoiseTrap")
    plate = b.box((0.240, 0.200, 0.024), (0.0, 0.0, 0.012), mat=IRON, role="plate")
    b.pivot_part = plate
    b.box((0.036, 0.036, 0.080), (-0.090, -0.070, 0.064), mat=IRON)
    b.cyl(0.020, 0.070, (-0.090, -0.070, 0.140), verts=10, mat=RUST)  # spring housing
    for i in range(4):
        b.cyl(0.026, 0.008, (-0.090, -0.070, 0.112 + i * 0.019), verts=10, mat=IRON,
              nobevel=True)
    # Striker arm, cocked.
    b.box((0.020, 0.190, 0.014), (-0.090, 0.014, 0.176), rot=(-22.0, 0.0, 0.0), mat=IRON,
          nobevel=True)
    b.box((0.030, 0.030, 0.030), (-0.090, 0.100, 0.212), mat=IRON)
    # Bell and clapper.
    b.cyl(0.026, 0.100, (0.078, 0.010, 0.074), verts=8, mat=RUST)
    b.sph(0.058, (0.078, 0.010, 0.150), scale=(1.0, 1.0, 0.86), segs=12, rings=6,
          mat=BRASS, nobevel=True)
    b.cyl(0.014, 0.030, (0.078, 0.010, 0.196), verts=8, mat=BRASS, nobevel=True)
    b.sph(0.014, (0.078, 0.010, 0.116), segs=8, rings=4, mat=IRON, nobevel=True)
    # Tripwire on a stake — the part that has to be visible to a friend and not to
    # something moving at 4.8 m/s.
    b.cyl(0.008, 0.240, (0.130, -0.150, 0.120), verts=8, mat=IRON, nobevel=True)
    b.box((0.004, 0.230, 0.004), (0.020, -0.080, 0.216), rot=(0.0, 0.0, 27.0), mat=IRON,
          nobevel=True)
    return b


FLARE_AXIS_Z = 0.019
"""Height of the flare tube's axis: the end caps are the widest part, so this is
their radius and the tube rests on the floor instead of hovering over it."""


def _flare_body(b: PropBuild) -> None:
    """The shared 조명탄 housing (§03, §08): one-use, lights a zone, makes noise."""
    z = FLARE_AXIS_Z
    b.cyl(0.017, 0.200, (0.0, 0.0, z), rot=(90.0, 0.0, 0.0), verts=12, mat=CLOTH,
          role="tube", nobevel=True)
    b.cyl(0.019, 0.026, (0.0, -0.086, z), rot=(90.0, 0.0, 0.0), verts=12, mat=RUST,
          nobevel=True)
    b.cyl(0.019, 0.026, (0.0, 0.086, z), rot=(90.0, 0.0, 0.0), verts=12, mat=BRASS_DULL,
          nobevel=True)
    b.box((0.030, 0.070, 0.004), (0.0, 0.010, z + 0.017), mat=PAPER, nobevel=True)


def build_flare_unlit() -> PropBuild:
    """조명탄, unlit — §08's purchase: lights a zone without the Engineer.

    §03's table gives it "1회용 · 소리를 낸다" and that trade is the whole item: it
    solves darkness and buys a threat escalation. Lying down, 20 cm, so it reads
    as inventory rather than as a light source. Nothing emissive — asserted,
    because an unlit flare that glows would give away §03's whole cost structure.
    """
    b = PropBuild("Flare_Unlit")
    _flare_body(b)
    b.pivot_part = b.named["tube"]
    return b


def build_flare_lit() -> PropBuild:
    """조명탄, burning — the zone is lit, and everything now knows where you are.

    Same housing, so a Unity swap at ignition does not jump. §03: 구역 조명 means
    "여러 명이 동시에 읽는다" *and* "괴물도 그쪽으로 온다"; the flame is oversized and
    emissive at strength 18 so the prop itself communicates that it is a beacon
    for both sides.
    """
    b = PropBuild("Flare_Lit")
    _flare_body(b)
    b.pivot_part = b.named["tube"]
    # The flame sits *above* the tube axis rather than centred on it, so the burning
    # flare rests on the floor like the unlit one. A cone centred on the axis would
    # dip below the floor and lift the whole prop 13 mm into the air.
    z = FLARE_AXIS_Z
    b.cone(0.026, 0.004, 0.110, (0.0, 0.145, z + 0.011), rot=(-90.0, 0.0, 0.0), verts=12,
           mat=FLARE, nobevel=True)
    b.sph(0.032, (0.0, 0.104, z + 0.007), scale=(1.0, 1.2, 0.66), segs=12, rings=6,
          mat=FLARE, nobevel=True)
    for (x, y, dz, s) in ((0.026, 0.176, 0.023, 0.010), (-0.030, 0.166, 0.009, 0.008),
                          (0.008, 0.208, 0.037, 0.007), (-0.014, 0.192, 0.043, 0.006)):
        b.box((s, s, s), (x, y, z + dz), rot=(0.0, 0.0, 30.0), mat=FLARE, nobevel=True)
    return b


def build_hiding_spot() -> PropBuild:
    """은폐 지점 — §12's checklist: concealment near the exit, for §07's 새벽.

    §07's 24-32분 band takes the standstill state away, patrols the whole map and
    hands the monster the exit: *"괴물이 출입구를 안다."* At that point the last
    approach to the exit has to be survivable by waiting, not by running, because
    §06 says only the Runner can outrun it. This is the wardrobe you wait in.

    Two features do the work. The cavity is measured, not assumed, and asserted
    against a standing player. And the closed door is **louvred** — the slats let
    a hider watch the monster walk past, which is the difference between hiding
    and guessing. §04's Observer needs a sightline to be useful and this is one of
    the few safe ones.
    """
    b = PropBuild("HidingSpot_Locker")
    W, D, H = 1.060, 0.660, 2.040
    t = 0.040
    plinth = 0.100
    base = b.box((W, D, plinth), (0.0, 0.0, plinth / 2), mat=WOOD_DARK, role="plinth")
    b.pivot_part = base
    b.box((W, D, t), (0.0, 0.0, H - t / 2), mat=WOOD_DARK, role="top")
    b.box((W + 0.060, D + 0.050, 0.056), (0.0, -0.010, H + 0.028), mat=WOOD_DARK)  # cornice
    b.box((W, t, H - plinth - t), (0.0, D / 2 - t / 2, (H + plinth - t) / 2), mat=WOOD_DARK,
          role="back")
    for sx, role in ((-1.0, "left"), (1.0, "right")):
        b.box((t, D, H - plinth - t), (sx * (W / 2 - t / 2), 0.0, (H + plinth - t) / 2),
              mat=WOOD_DARK, role=role)
    b.box((W - 2 * t, D - t, 0.030), (0.0, -0.020, 1.180), mat=WOOD_DARK, role="rail")
    # Closed door on the left, louvred so the hider can see out.
    b.box((W / 2 - t, 0.034, H - plinth - t - 0.020), (-W / 4, -D / 2 + 0.017,
                                                       (H + plinth - t) / 2), mat=WOOD_DARK,
          role="door_closed")
    for i in range(6):
        b.box((W / 2 - 0.120, 0.030, 0.052), (-W / 4, -D / 2 - 0.006, 1.320 + i * 0.096),
              rot=(28.0, 0.0, 0.0), mat=WOOD, nobevel=True)
    b.box((0.030, 0.036, 0.130), (-0.060, -D / 2 - 0.010, 1.120), mat=BRASS_DULL)
    # Right door, standing open — the invitation.
    dl: list[bpy.types.Object] = []
    dl.append(b.box((W / 2 - t, 0.034, H - plinth - t - 0.020), ((W / 2 - t) / 2, 0.0, 0.0),
                    mat=WOOD_DARK, role="door_open"))
    dl.append(b.box((W / 2 - 0.140, 0.024, 0.700), ((W / 2 - t) / 2, -0.028, 0.0), mat=WOOD))
    dl.append(b.box((0.030, 0.036, 0.130), ((W / 2 - t) - 0.056, -0.030, -0.060),
                    mat=BRASS_DULL))
    b.hinge_group("door_r", dl, (W / 2 - t, -D / 2 + 0.017, (H + plinth - t) / 2), -68.0,
                  axis="Z")

    left, right = b.named["left"], b.named["right"]
    back, top, floorp = b.named["back"], b.named["top"], b.named["plinth"]
    cav_x = world_bbox([right])[0].x - world_bbox([left])[1].x
    cav_y = world_bbox([back])[0].y - (-D / 2 + 0.034)
    cav_z = world_bbox([top])[0].z - world_bbox([floorp])[1].z
    b.meta["cavity"] = (cav_x, cav_y, cav_z)
    b.meta["louvres"] = 6
    return b


# ══════════════════════════════════════════════════════════════════════════
#  SET DRESSING — minimal and reusable. §12 gives it two jobs beyond looking
#  like a basement: blocking sight lines (aggro release is a map property, §06)
#  and rewarding the 20-25% of dead ends (§12's 막힌 길 보상).
# ══════════════════════════════════════════════════════════════════════════


def build_crate() -> PropBuild:
    """Stackable crate. §12's dead ends need a reason to be entered and something
    to hide loot behind; a 0.62 m cube stacks into both. Flat top on purpose."""
    b = PropBuild("Crate")
    S, H, t = 0.620, 0.580, 0.026
    body = b.box((S - 0.02, S - 0.02, H - 0.02), (0.0, 0.0, H / 2), mat=WOOD_DARK, role="body")
    b.pivot_part = body
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.box((0.052, 0.052, H), (sx * (S / 2 - 0.026), sy * (S / 2 - 0.026), H / 2),
                  mat=WOOD)
    for z in (0.070, H - 0.070):
        b.box((S, 0.030, 0.090), (0.0, -S / 2 + 0.015, z), mat=WOOD)
        b.box((S, 0.030, 0.090), (0.0, S / 2 - 0.015, z), mat=WOOD)
        b.box((0.030, S, 0.090), (-S / 2 + 0.015, 0.0, z), mat=WOOD)
        b.box((0.030, S, 0.090), (S / 2 - 0.015, 0.0, z), mat=WOOD)
    b.box((S, S, t), (0.0, 0.0, H - t / 2), mat=WOOD)
    return b


def build_pipes() -> PropBuild:
    """Ceiling pipe run. Wall-mounted, 2.4 m, with a valve.

    §12 wants zone boundaries legible, and the Listener (§04) works off floor
    material; overhead pipes are a way to differentiate a corridor's *look*
    without touching the floor and without narrowing the walkable width, which
    §12's 20 m straight-run rule already constrains.
    """
    b = PropBuild("Pipes")
    L = 2.400
    ref = b.cyl(0.052, L, (0.0, -0.100, 2.120), rot=(0.0, 90.0, 0.0), verts=10, mat=RUST,
                role="main", nobevel=True)
    b.pivot_part = ref
    b.cyl(0.034, L, (0.0, -0.190, 2.050), rot=(0.0, 90.0, 0.0), verts=10, mat=IRON,
          nobevel=True)
    b.cyl(0.022, L, (0.0, -0.096, 1.960), rot=(0.0, 90.0, 0.0), verts=8, mat=RUST,
          nobevel=True)
    for x in (-0.900, 0.0, 0.900):
        b.box((0.060, 0.230, 0.030), (x, -0.115, 2.190), mat=IRON)
        b.box((0.060, 0.030, 0.150), (x, -0.014, 2.120), mat=IRON)
        b.cyl(0.062, 0.036, (x, -0.100, 2.120), rot=(0.0, 90.0, 0.0), verts=10, mat=IRON,
              nobevel=True)
    # A valve and a drop leg, so the run reads as plumbing rather than as a stripe.
    b.cyl(0.052, 0.240, (0.560, -0.100, 2.000), verts=10, mat=RUST, nobevel=True)
    b.cyl(0.070, 0.070, (0.560, -0.100, 1.880), verts=12, mat=IRON, nobevel=True)
    b.torus(0.086, 0.014, (0.560, -0.100, 1.830), rot=(0.0, 0.0, 0.0), mseg=14, nseg=5,
            mat=RUST)
    for i in range(3):
        b.box((0.170, 0.016, 0.016), (0.560, -0.100, 1.830), rot=(0.0, 0.0, i * 60.0),
              mat=RUST, nobevel=True)
    return b


def build_shelving() -> PropBuild:
    """Shelving unit. Four decks, 1.84 x 0.46 x 1.94 m.

    Doubles as a sight blocker: §06 makes aggro release a matter of breaking line
    of sight for 3 s, and §12 asks for blockers every 15-25 m. A 1.94 m unit
    breaks a sightline standing up without closing a route, which a wall would.
    """
    b = PropBuild("Shelving")
    W, D, H, t = 1.840, 0.460, 1.940, 0.034
    b.box((0.070, 0.070, H), (-W / 2 + 0.035, -D / 2 + 0.035, H / 2), mat=IRON, role="leg")
    for (sx, sy) in ((1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.box((0.070, 0.070, H), (sx * (W / 2 - 0.035), sy * (D / 2 - 0.035), H / 2), mat=IRON)
    for z in (0.120, 0.640, 1.180, 1.720):
        b.box((W, D, t), (0.0, 0.0, z), mat=WOOD)
        b.box((W, 0.030, 0.056), (0.0, -D / 2 + 0.015, z + 0.030), mat=IRON)
    # Back cross-braces.
    b.box((W - 0.10, 0.024, 0.060), (0.0, D / 2 - 0.030, 1.000), rot=(0.0, 26.0, 0.0),
          mat=IRON)
    b.box((W - 0.10, 0.024, 0.060), (0.0, D / 2 - 0.030, 1.000), rot=(0.0, -26.0, 0.0),
          mat=IRON)
    return b


def build_debris() -> PropBuild:
    """Rubble and broken planks. Low, angular, 1.24 x 1.05 x 0.30 m.

    Kept under knee height on purpose: §05 makes backward movement 65% speed and
    §06 gives the monster 4.8 m/s, so anything a fleeing player can trip over is a
    death sentence written by set dressing rather than by design. Debris marks a
    dead end (§12: 20-25% of the map) without acting as an obstacle — one plank
    leans up to 0.30 m so the pile is findable in a beam, and nothing goes higher.
    """
    b = PropBuild("Debris")
    # Deterministic layout — no RNG, so a rebuild is a byte-for-byte rebuild.
    planks = (
        (0.980, 0.150, 0.036, -0.120, 0.060, 0.020, (0.0, 2.0, 12.0)),
        (0.860, 0.140, 0.034, 0.140, -0.170, 0.056, (0.0, -4.0, -28.0)),
        (0.720, 0.130, 0.032, -0.220, 0.240, 0.090, (7.0, 3.0, 62.0)),
        (0.640, 0.120, 0.030, 0.300, 0.190, 0.036, (0.0, 0.0, -66.0)),
        (0.540, 0.110, 0.028, -0.020, -0.250, 0.120, (-5.0, 9.0, 34.0)),
    )
    for (sx, sy, sz, x, y, z, rot) in planks:
        b.box((sx, sy, sz), (x, y, z), rot=rot, mat=WOOD)
    chunks = (
        (0.190, 0.160, 0.130, -0.420, -0.110, 0.065, 18.0),
        (0.150, 0.140, 0.110, 0.420, -0.060, 0.055, -34.0),
        (0.130, 0.120, 0.095, 0.060, 0.330, 0.048, 52.0),
        (0.110, 0.100, 0.085, -0.300, -0.300, 0.042, -12.0),
        (0.095, 0.090, 0.070, 0.240, -0.320, 0.035, 41.0),
        (0.170, 0.130, 0.100, 0.520, 0.230, 0.050, -58.0),
        (0.120, 0.110, 0.080, -0.520, 0.180, 0.040, 24.0),
    )
    for (sx, sy, sz, x, y, z, yaw) in chunks:
        b.box((sx, sy, sz), (x, y, z), rot=(6.0, -4.0, yaw), mat=STONE)
    for (x, y, r) in ((-0.100, -0.400, 0.070), (0.360, 0.360, 0.060)):
        b.sph(r, (x, y, r * 0.55), scale=(1.0, 0.9, 0.55), segs=10, rings=5, mat=STONE,
              nobevel=True)
    b.box((1.100, 0.700, 0.020), (0.0, 0.0, 0.010), mat=STONE, nobevel=True)
    # One plank propped on the mound. Its only job is to break the flat silhouette
    # so a beam finds the pile; at 0.30 m it is still well under knee height.
    b.box((0.760, 0.130, 0.030), (0.050, -0.050, 0.152), rot=(0.0, -20.0, 20.0), mat=WOOD)
    return b


# ── Prop table ──────────────────────────────────────────────────────────────


@dataclass
class Spec:
    name: str
    build: Callable[[], PropBuild]
    category: str
    expect: tuple[float, float, float]
    """Intended metre dimensions. Checked, so a unit-scale slip fails the build."""
    mount: str = "FLOOR"
    bevel: float = 0.005
    max_tris: int = 1500
    max_dim: float = 3.0
    note: str = ""
    checks: tuple[str, ...] = ()


SPECS: list[Spec] = [
    # ── §08 loot ───────────────────────────────────────────────────────────
    Spec("Loot_Trinket_SilverSpoons", build_trinket_silver_spoons, "Loot",
         (0.232, 0.206, 0.018), bevel=0.0015, note="conspicuous, mirror-grade"),
    Spec("Loot_Trinket_Junk", build_trinket_junk, "Loot",
         (0.314, 0.231, 0.140), bevel=0.0015, note="conspicuous, largest small footprint"),
    Spec("Loot_Timepiece_PocketWatch", build_timepiece_pocket_watch, "Loot",
         (0.073, 0.074, 0.017), bevel=0.0008, note="efficient, tarnished so it stays missable"),
    Spec("Loot_Timepiece_Ring", build_timepiece_ring, "Loot",
         (0.022, 0.022, 0.017), bevel=0.0004, note="efficient, 2 cm across"),
    Spec("Loot_SafeDocument", build_safe_document, "Loot",
         (0.310, 0.229, 0.061), bevel=0.0010, note="fits the measured safe cavity",
         checks=("fits_safe",)),
    Spec("Loot_LargePiece_Portrait", build_large_portrait, "Loot",
         (1.140, 0.175, 1.632), bevel=0.005, max_tris=1500,
         note="two-person carry, closes a corridor", checks=("two_person",)),
    Spec("Loot_LargePiece_Chest", build_large_chest, "Loot",
         (1.276, 0.716, 0.731), bevel=0.006, note="two-person carry, §08's best moment",
         checks=("two_person",)),
    # ── Interactables ──────────────────────────────────────────────────────
    Spec("Objective", build_objective, "Interactable", (0.598, 0.403, 0.407), bevel=0.004,
         note="§03 목표물; one-person two-handed carry, sealed"),
    Spec("Safe_Closed", build_safe_closed, "Interactable", (0.733, 0.680, 0.840), bevel=0.005,
         note="handle stands 6 cm off the door; hinge barrel 1 cm off the side"),
    Spec("Safe_Open", build_safe_open, "Interactable", (0.893, 1.296, 0.840), bevel=0.005,
         max_dim=3.0, note="door swings 100° into the room"),
    Spec("ElectricalPanel", build_electrical_panel, "Interactable", (0.520, 0.156, 1.020),
         mount="WALL", bevel=0.004, note="§12: one per zone; box at 1.01-1.69 m"),
    Spec("SurfaceGenerator", build_surface_generator, "Interactable",
         (1.020, 0.683, 0.795), bevel=0.004, note="4 charging cradles, one per player"),
    Spec("Vehicle", build_vehicle, "Interactable", (2.810, 6.688, 2.937), bevel=0.006,
         max_tris=6000, max_dim=8.0, note="safe zone + shop + supply; the exception"),
    Spec("Clue_WallBoard", build_clue_wall_board, "Interactable", (0.960, 0.100, 0.720),
         mount="WALL", bevel=0.004, note="좌↔우 via the pane beside the glyph",
         checks=("clue_face",)),
    Spec("Clue_LedgerStand", build_clue_ledger_stand, "Interactable", (0.540, 0.404, 1.147),
         bevel=0.004, note="1↔7 via a 32° glancing read", checks=("clue_face",)),
    Spec("Clue_EngravedPlate", build_clue_engraved_plate, "Interactable",
         (0.636, 0.636, 0.154), bevel=0.004, note="6↔9 via 180° symmetry",
         checks=("clue_face", "symmetry")),
    Spec("Barricade", build_barricade, "Interactable", (1.226, 0.472, 1.120), bevel=0.005),
    Spec("NoiseTrap", build_noise_trap, "Interactable", (0.258, 0.298, 0.240), bevel=0.003),
    Spec("Flare_Unlit", build_flare_unlit, "Interactable", (0.038, 0.200, 0.038),
         bevel=0.0008, note="no emissive material — asserted", checks=("dark",)),
    Spec("Flare_Lit", build_flare_lit, "Interactable", (0.068, 0.313, 0.065),
         bevel=0.0008, note="emissive at strength 18", checks=("bright",)),
    Spec("HidingSpot_Locker", build_hiding_spot, "Interactable", (1.249, 1.119, 2.096),
         bevel=0.005, note="§12 checklist: concealment near the exit",
         checks=("player_fits",)),
    # ── Set dressing ───────────────────────────────────────────────────────
    Spec("Crate", build_crate, "Dressing", (0.620, 0.620, 0.580), bevel=0.006),
    Spec("Pipes", build_pipes, "Dressing", (2.400, 0.231, 0.388), mount="WALL", bevel=0.004),
    Spec("Shelving", build_shelving, "Dressing", (1.840, 0.460, 1.940), bevel=0.005),
    Spec("Debris", build_debris, "Dressing", (1.237, 1.046, 0.299), bevel=0.004),
]


# ── Emit one prop ───────────────────────────────────────────────────────────

ROWS: list[dict] = []
META: dict[str, dict] = {}


def pivot_shift(b: PropBuild, mount: str) -> Vector:
    """Offset that puts the prop's origin where the placement convention says.

    FLOOR: origin on the floor under the footprint centre. WALL: origin on the
    wall plane under the fitting, height preserved. Both mean a level designer
    drops the prop at a point on a surface and it is correct.
    """
    ref = [b.pivot_part] if b.pivot_part is not None else b.parts
    rlo, rhi = world_bbox(ref)
    alo, ahi = world_bbox(b.parts)
    cx = (rlo.x + rhi.x) / 2
    if mount == "WALL":
        return Vector((-cx, -ahi.y, 0.0))
    cy = (rlo.y + rhi.y) / 2
    return Vector((-cx, -cy, -alo.z))


def apply_smooth(obj: bpy.types.Object, angle: float) -> int:
    """Smooth by angle, and report how many edges stayed sharp.

    A silent failure here is expensive: if the angle never gets applied, every
    box edge is smoothed, FBX exports face-smoothing for the lot, and every prop
    arrives in Unity looking melted. So the count is measured and a zero falls
    back to flat shading, which for low-poly primitives is correct anyway.
    """
    blendkit.shade_smooth(obj, angle_degrees=angle)
    sharp = 0
    attr = obj.data.attributes.get("sharp_edge")
    if attr is not None:
        sharp = sum(1 for d in attr.data if d.value)
    if sharp == 0 and any(m.type == "NODES" for m in obj.modifiers):
        sharp = -1  # handled by a modifier; export applies it
    if sharp == 0:
        for poly in obj.data.polygons:
            poly.use_smooth = False
    return sharp


def emit(spec: Spec) -> None:
    blendkit.reset_scene()
    b = spec.build()
    if not b.parts:
        blendkit.fail(f"{spec.name}: builder produced no geometry")

    shift = pivot_shift(b, spec.mount)
    for obj in b.parts:
        obj.location = obj.location + shift
    bpy.context.view_layer.update()

    bevel_skipped = 0
    for obj in b.parts:
        blendkit.apply_transforms(obj, location=True, rotation=True, scale=True)
        if spec.bevel <= 0.0 or obj.name in b.nobevel:
            bevel_skipped += 1
            continue
        # A bevel wider than a quarter of the thinnest dimension folds the part
        # inside out. Cheaper to detect than to eyeball 24 props.
        dims = [d for d in obj.dimensions]
        if min(dims) < spec.bevel * 4.0:
            bevel_skipped += 1
            continue
        blendkit.bevel(obj, width=spec.bevel, segments=1)

    obj = blendkit.join(b.parts, spec.name)
    blendkit.triangulate(obj)
    sharp = apply_smooth(obj, 30.0)
    blendkit.uv_smart_project(obj)

    face = map_face_uv_unit(obj, CLUE_FACE)
    sym = None
    if "symmetry" in spec.checks:
        sym = rotational_symmetry_error(obj, 0.0, 0.0)

    path = blendkit.out_path("Props", spec.name + ".fbx")
    blendkit.export_fbx(path, objects=[obj], with_animation=False)

    report = blendkit.describe(path)
    blendkit.assert_asset(report, min_vertices=8, max_triangles=spec.max_tris,
                          max_dimension=spec.max_dim)

    used = [m.name for m in obj.data.materials if m is not None]
    lost = fbx_missing_materials(path, used)
    if lost:
        blendkit.fail(f"{spec.name}: materials missing from the exported FBX: {', '.join(lost)}")

    size = report.size
    for i, axis in enumerate("XYZ"):
        want = spec.expect[i]
        tol = max(0.006, want * 0.08)
        if abs(size[i] - want) > tol:
            msg = (f"{spec.name}: {axis} is {size[i]:.3f} m, intended {want:.3f} m "
                   f"(tolerance {tol:.3f}). 1 unit must be 1 metre — either the geometry "
                   f"drifted from the design size or the scale is wrong.")
            if MEASURE_ONLY:
                print("SIZE_MISMATCH " + msg)
            else:
                blendkit.fail(msg)
    if min(size) < 0.010:
        blendkit.fail(f"{spec.name}: smallest dimension {min(size) * 1000:.1f} mm — "
                      "too small to be lit by a flashlight beam, let alone found")

    lo, hi = report.bounds_min, report.bounds_max
    if spec.mount == "WALL":
        if abs(hi[1]) > 0.003:
            blendkit.fail(f"{spec.name}: wall prop's mounting face is at y={hi[1]:.4f}, "
                          "must be 0 so it drops onto a wall plane")
        if lo[2] < -0.003:
            blendkit.fail(f"{spec.name}: wall prop dips below the floor (min z={lo[2]:.4f})")
    else:
        if abs(lo[2]) > 0.003:
            blendkit.fail(f"{spec.name}: floor prop's base is at z={lo[2]:.4f}, must be 0")
    if "clue_face" in spec.checks:
        if face is None:
            blendkit.fail(f"{spec.name}: no '{CLUE_FACE}' polygons — §03's glyph has nowhere to go")
        if face["flatness"] > 0.001:
            blendkit.fail(f"{spec.name}: clue face is not flat ({face['flatness'] * 1000:.2f} mm "
                          "out of plane) — a glyph would distort")
        uv = obj.data.uv_layers.active
        idx = material_index(obj, CLUE_FACE)
        us = [uv.data[li].uv for p in obj.data.polygons if p.material_index == idx
              for li in p.loop_indices]
        if min(u.x for u in us) > 1e-6 or max(u.x for u in us) < 1.0 - 1e-6:
            blendkit.fail(f"{spec.name}: clue face UVs do not span 0..1")

    ROWS.append({
        "name": spec.name, "category": spec.category, "size": size, "tris": report.triangles,
        "verts": report.vertices, "mats": report.materials, "bytes": report.bytes,
        "note": spec.note, "mount": spec.mount, "path": path,
        "volume": mesh_volume(obj), "sharp": sharp, "bevel_skipped": bevel_skipped,
        "face": face, "sym": sym, "emissive": emissive_material_count(obj),
        "materials": [m.name for m in obj.data.materials if m is not None],
        "areas": material_areas(obj), "panels": largest_visible_panel(obj),
    })
    META[spec.name] = dict(b.meta)
    META[spec.name]["checks"] = spec.checks

    blendkit.print_report(report)
    extra = [f"prop={spec.name}", f"category={spec.category}", f"mount={spec.mount}",
             f"size={size[0]:.3f}x{size[1]:.3f}x{size[2]:.3f}m",
             f"sharp_edges={sharp}", f"bevel_skipped={bevel_skipped}",
             f"solid_volume_m3={mesh_volume(obj):.6f}", f"emissive_materials={emissive_material_count(obj)}"]
    if face is not None:
        extra.append(f"clue_face={face['width']:.3f}x{face['height']:.3f}m")
    if sym is not None:
        extra.append(f"symmetry_error_mm={sym * 1000:.3f}")
    print("PROP_DETAIL " + " ".join(extra))


# ── Cross-prop checks: the §08 economy has to be legible as geometry ────────


def row(name: str) -> dict:
    for r in ROWS:
        if r["name"] == name:
            return r
    blendkit.fail(f"no report row for {name}")
    raise AssertionError  # unreachable; keeps type checkers quiet


def footprint(r: dict) -> float:
    return r["size"][0] * r["size"][1]


def longest_horizontal(r: dict) -> float:
    return max(r["size"][0], r["size"][1])


def check_economy_is_legible(names: list[str]) -> list[str]:
    """Asserts §08's weight column is readable off the geometry alone.

    Not decoration: a player prices a piece from its silhouette in a beam before
    committing to the weight, and §08's speed bands punish a wrong guess
    immediately. If the sizes stop ordering with the weights, the economy stops
    being playable in the dark and no amount of UI fixes it.
    """
    lines: list[str] = []
    present = [n for n in names if n in LOOT_ROWS]
    if len(present) < len(LOOT_ROWS):
        lines.append("SKIP economy checks — not every loot prop was built this run")
        return lines

    by_weight: dict[int, list[dict]] = {}
    for n in present:
        by_weight.setdefault(LOOT_ROWS[n][1], []).append(row(n))

    w5 = min(longest_horizontal(r) for r in by_weight[5])
    light = max(longest_horizontal(r) for r in by_weight[1] + by_weight[2])
    if w5 < light * 3.0:
        blendkit.fail(f"§08 weight 5 pieces are only {w5:.2f} m across against {light:.2f} m "
                      f"for weight ≤2 — 5 weight units must look like 5 weight units")
    lines.append(f"weight-5 span {w5:.2f} m ≥ 3x the largest weight-≤2 span {light:.2f} m  OK")

    w2 = min(longest_horizontal(r) for r in by_weight[2])
    eff = max(longest_horizontal(row(n)) for n in EFFICIENT)
    if w2 < eff * 2.0:
        blendkit.fail(f"§08 weight 2 ({w2:.2f} m) does not read as heavier than the weight-1 "
                      f"efficient row ({eff:.2f} m)")
    lines.append(f"weight-2 span {w2:.2f} m ≥ 2x the efficient weight-1 span {eff:.2f} m  OK")

    # Honest about a real collision: 잡동사니 (weight 1) is spread out on purpose and
    # ends up with almost the same footprint as the weight-2 document. Span cannot
    # separate them, so the discriminator is bulk — and §08 keeps them from ever
    # being seen side by side by putting the document inside a safe.
    doc_v = min(r["volume"] for r in by_weight[2])
    light_v = max(r["volume"] for r in by_weight[1])
    if doc_v < light_v * 4.0:
        blendkit.fail(f"§08's weight-2 document has {doc_v:.5f} m³ of bulk against {light_v:.5f} m³ "
                      "for weight-1 loot — with their footprints this close, nothing tells them "
                      "apart in a beam")
    lines.append(f"weight-2 bulk {doc_v:.5f} m³ ≥ 4x the largest weight-1 bulk {light_v:.5f} m³  OK "
                 f"(footprints are {footprint(by_weight[2][0]):.4f} vs "
                 f"{max(footprint(r) for r in by_weight[1]):.4f} m² — span alone cannot separate "
                 "these two; §08 gates the document behind the safe so they never compete)")

    con = min(footprint(row(n)) for n in CONSPICUOUS)
    effa = max(footprint(row(n)) for n in EFFICIENT)
    if con < effa * 3.0:
        blendkit.fail(f"§08's 유혹 row has a {con:.4f} m² footprint against {effa:.4f} m² for the "
                      f"efficient row — the cheap loot must be the loot a beam finds first")
    lines.append(f"conspicuous footprint {con:.4f} m² ≥ 3x efficient {effa:.4f} m²  OK")

    for n in CONSPICUOUS:
        for m in row(n)["materials"]:
            spec = MATERIALS[m]
            if spec.metallic > 0.5 and spec.roughness > 0.20:
                blendkit.fail(f"{n}: '{m}' is metallic at roughness {spec.roughness} — §08's "
                              "temptation has to throw a highlight")
    watch_mats = row("Loot_Timepiece_PocketWatch")["materials"]
    if BRASS_DULL not in watch_mats:
        blendkit.fail("the pocket watch lost its tarnished case; it would start out-shining "
                      "§08's 유혹 row and the trap would invert")
    lines.append("conspicuous materials mirror-grade (roughness ≤ 0.20); watch case tarnished "
                 f"(roughness {MATERIALS[BRASS_DULL].roughness})  OK")
    return lines


DARK_METAL_LUMINANCE = 0.20
"""Above this, a metal's own base colour carries enough to survive having nothing
to reflect. ART.md quotes 0.21 as the linear albedo of the darkest §12 corridor
wall; a metal below that is darker than the wall it stands against before any light
is applied, and metals have no diffuse term with which to catch up."""

DARK_METAL_PANEL_AREA = 0.35
DARK_METAL_PANEL_SPAN = 0.35
"""When a flat slab of dark metal stops being trim and becomes a face: m² of area
**and** metres across its narrow side, both.

Measured against the kit rather than chosen. The two numbers exist because the kit
contains a decisive pair that area alone cannot separate — the 금고's door is
0.55 m² and the 차량's front bumper is 0.50 m². The door is 0.72 m across its narrow
side and is a *face*; the bumper is 0.23 m and is a *strip*, read by its own long
edge highlight and by the wheels and grille either side of it. With the 차량 and the
금고 repainted, that bumper is the largest bare-iron slab left anywhere in the kit —
0.50 m² over the area gate, 0.23 m under the span gate — and everything else is
under both: the 발전기's flywheel housing at 0.05 m², the 노이즈 트랩 at 0.02 m²."""


def check_metal_is_not_a_panel(names: list[str]) -> list[str]:
    """Refuses to ship a large flat slab of dark metal. ART.md §7.12.

    A metallic surface has no diffuse term: it renders what it reflects. There is no
    reflection probe in this building and §03 lights it with a 12 m torch, so what a
    90 % metal reflects is the 0.25-intensity night skybox — nothing. The 차량's
    bodywork was `Prop_Iron` (albedo 0.112/0.116/0.122, metallic 0.90) and rendered
    as a black cut-out from every angle and under every one of the five lamps
    `SurfaceApron` gave it. Measured at 5 m with the torch on it, its flank came back
    at **0.42×** the luminance of the brick behind it; at 2 m the panel's median was
    **7/255** against the wall's 33, with 29 % of it crushed to black.

    This is not "avoid metal". Bare iron is *correct* on a hinge, a hasp, a bumper, a
    pipe run — see `largest_visible_panel` for why curvature is what rescues them.
    It is wrong on a flat panel, and flatness is the thing measured here.
    """
    lines: list[str] = []
    offenders: list[str] = []
    worst_ok = ("", 0.0, 0.0)
    for name in names:
        for material, (area, span) in row(name)["panels"].items():
            spec = MATERIALS.get(material)
            if spec is None or spec.metallic <= 0.5:
                continue
            lum = albedo_luminance(material)
            if lum >= DARK_METAL_LUMINANCE:
                continue
            if area > DARK_METAL_PANEL_AREA and span > DARK_METAL_PANEL_SPAN:
                offenders.append(
                    f"{name}: a {area:.2f} m² face of '{material}', {span:.2f} m across its "
                    f"narrow side — metallic {spec.metallic:.2f} at luminance {lum:.3f}. One "
                    "viewing angle lights it and it is a hole from every other")
            elif area > worst_ok[1]:
                worst_ok = (f"{name}/{material}", area, span)
    if offenders:
        blendkit.fail("these props are flat faces of dark metal and render as holes "
                      "(ART.md §7.12). A painted panel is a dielectric — metallic 0 with a "
                      "real albedo, gloss carried by roughness:\n  " + "\n  ".join(offenders))
    lines.append(f"no dark-metal face exceeds {DARK_METAL_PANEL_AREA} m² AND "
                 f"{DARK_METAL_PANEL_SPAN} m across; the largest left is {worst_ok[0]} at "
                 f"{worst_ok[1]:.2f} m², {worst_ok[2]:.2f} m across — trim, not a face  OK")
    return lines


def check_the_vehicle_is_painted(name: str) -> list[str]:
    """§08's 차량 is the one object §01 sends the team back to; it has to read.

    Two halves, and both are the point. Its bodywork must be a **dielectric** —
    §08 calls it 안전 지대 · 상점 · 보급소 and §01 sends the team to it 2.94 times a
    match, so it is the one prop that must read as a surface from across a 20 m bay
    with a torch on it. And it must **keep bare metal where the metal is bare** —
    bumper, grille, wheel hubs, ladder, chassis — because the fix for a black van is
    paint, not "make everything a dielectric", and a van with no metal on it at all
    reads as a toy.
    """
    r = row(name)
    areas = r["areas"]
    total = sum(areas.values())
    painted = sum(a for m, a in areas.items() if m in VEHICLE_PAINT)
    if painted / total < 0.30:
        blendkit.fail(f"{name}: only {painted / total * 100:.0f}% of the 차량's surface is painted "
                      "bodywork. §08 makes it the 안전 지대 · 상점 · 보급소 and it has to read as a "
                      "vehicle you could walk up to, not as a silhouette")
    for paint in VEHICLE_PAINT:
        spec = MATERIALS[paint]
        if spec.metallic != 0.0:
            blendkit.fail(f"'{paint}' is metallic {spec.metallic}. Paint is a dielectric — "
                          "its gloss is roughness, not metallic (ART.md §7.12)")
        if albedo_luminance(paint) < 0.08:
            blendkit.fail(f"'{paint}' has a luminance of {albedo_luminance(paint):.3f}. Fixing a "
                          "black van with black paint is the same frame")
    metal = sum(a for m, a in areas.items()
                if m in MATERIALS and MATERIALS[m].metallic > 0.5)
    if metal <= 0.0:
        blendkit.fail(f"{name}: nothing on the 차량 is bare metal any more. The bumper, the grille, "
                      "the hubs and the ladder are steel and should answer a beam like steel")
    return [f"{name}: {painted / total * 100:.0f}% painted dielectric bodywork "
            f"({' · '.join(f'{p} L={albedo_luminance(p):.3f} rough={MATERIALS[p].roughness}' for p in VEHICLE_PAINT)}), "
            f"{metal / total * 100:.0f}% bare metal  OK"]


def check_two_person(name: str) -> list[str]:
    """§08 grants 2인 운반 to exactly these two rows; the geometry has to say so."""
    r = row(name)
    span = longest_horizontal(r)
    grab = float(META[name]["handle_span"])  # type: ignore[arg-type]
    off = float(META[name]["handle_imbalance"])  # type: ignore[arg-type]
    if grab <= ONE_HAND_CARRY_SPAN:
        blendkit.fail(f"{name}: grab points are {grab:.2f} m apart, inside one player's "
                      f"{ONE_HAND_CARRY_SPAN} m grip — nothing requires a second carrier")
    if off > 0.010:
        blendkit.fail(f"{name}: the grab points sit {off * 100:.1f} cm off centre along their own "
                      "span — the rear carrier would take most of the load")
    frac = span / CORRIDOR_WIDTH_ASSUMED
    margin = CORRIDOR_WIDTH_ASSUMED - span
    if frac < 0.60:
        blendkit.fail(f"{name}: occupies only {frac * 100:.0f}% of an assumed "
                      f"{CORRIDOR_WIDTH_ASSUMED} m corridor — §08's scene needs it to be awkward")
    passable = "another player CANNOT squeeze past" if margin < PLAYER_SHOULDER_ASSUMED \
        else "another player can squeeze past"
    return [f"{name}: grab points {grab:.2f} m apart (> {ONE_HAND_CARRY_SPAN} m one-hand span), "
            f"{META[name]['handle_height']:.2f} m up, balanced to {off * 1000:.1f} mm along the "
            f"span ({META[name]['handle_standoff'] * 1000:.0f} mm stand-off for fingers)  OK",
            f"{name}: {span:.2f} m span = {frac * 100:.0f}% of the assumed corridor, "
            f"{margin:.2f} m margin — {passable}"]


def write_manifest() -> str:
    """Writes ``Assets/Models/Props/Props.manifest.json`` — the Unity-side contract.

    **FBX cannot carry a PBR material.** It has a Lambert/Phong slot, so Unity's
    importer sees a diffuse colour and a shininess and nothing else. Measured on
    the shipped files: every embedded material arrives at ``metallic 0``, which
    turns §08's mirror-grade 은수저 into grey plastic and the 반지's gold into
    beige paint. The values below are the ones the Principled BSDF was actually
    built with, so the Unity binder can rebuild a URP Lit material that matches
    what Blender rendered instead of guessing.

    It carries the material table and the prop roster only. Sizes are deliberately
    absent: Unity can measure the imported mesh, and a number restated in two
    places is a number that drifts. Same reasoning as
    ``Dressing.manifest.json``, which this mirrors.
    """
    manifest = {
        "generator": "tools/blender/gen_props.py",
        "note": ("Material values as authored on the Principled BSDF. FBX loses "
                 "metallic entirely; the Unity binder rebuilds URP Lit from these."),
        "materials": [
            {
                "name": spec.name,
                "color": [round(c, 6) for c in spec.color],
                "roughness": spec.roughness,
                "metallic": spec.metallic,
                "emission": spec.emission,
            }
            for spec in sorted(MATERIALS.values(), key=lambda s: s.name)
        ],
        "props": [
            {
                "name": spec.name,
                "file": spec.name + ".fbx",
                "category": spec.category,
                "mount": spec.mount,
                "note": spec.note,
            }
            for spec in SPECS
        ],
    }

    path = blendkit.out_path("Props", "Props.manifest.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    print(f"MANIFEST {path} ({len(manifest['materials'])} materials, "
          f"{len(manifest['props'])} props)")
    return path


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    # The manifest is pure data — the two module tables above — so it can be
    # rewritten without rebuilding 24 meshes. Used when only the Unity binding
    # changed, which is the common case once the geometry has settled.
    if "--manifest-only" in argv:
        write_manifest()
        return

    todo = [s for s in SPECS if not argv or any(a.lower() in s.name.lower() for a in argv)]
    if not todo:
        blendkit.fail(f"no prop matches {argv}")

    print("PROP_ASSUMPTIONS " + " ".join([
        f"corridor_width={CORRIDOR_WIDTH_ASSUMED}m",
        f"player_shoulder={PLAYER_SHOULDER_ASSUMED}m",
        f"player_height={PLAYER_HEIGHT_ASSUMED}m",
        f"one_hand_span={ONE_HAND_CARRY_SPAN}m",
        "source=NOT_IN_DESIGN_DOC",
    ]))

    write_manifest()

    for spec in todo:
        emit(spec)

    names = [s.name for s in todo]
    lines: list[str] = []
    lines += check_economy_is_legible(names)
    lines += check_metal_is_not_a_panel(names)
    if "Vehicle" in names:
        lines += check_the_vehicle_is_painted("Vehicle")

    for spec in todo:
        if "two_person" in spec.checks:
            lines += check_two_person(spec.name)

    # §08's document has to fit the safe it comes out of.
    if "Loot_SafeDocument" in names and "Safe_Open" in names:
        doc = row("Loot_SafeDocument")["size"]
        cav = META["Safe_Open"]["cavity_lower"]  # type: ignore[index]
        fits = doc[0] <= cav[0] and doc[1] <= cav[1] and doc[2] <= cav[2]
        if not fits:
            blendkit.fail(f"§08's 금고 속 문서 ({doc[0]:.3f}x{doc[1]:.3f}x{doc[2]:.3f} m) does not "
                          f"fit the safe compartment ({cav[0]:.3f}x{cav[1]:.3f}x{cav[2]:.3f} m)")
        lines.append(f"document {doc[0]:.2f}x{doc[1]:.2f}x{doc[2]:.2f} m fits the safe's measured "
                     f"lower compartment {cav[0]:.2f}x{cav[1]:.2f}x{cav[2]:.2f} m  OK")

    # The two safe variants must be a drop-in swap.
    if "Safe_Closed" in names and "Safe_Open" in names:
        a = META["Safe_Closed"]["body_footprint"]  # type: ignore[index]
        c = META["Safe_Open"]["body_footprint"]  # type: ignore[index]
        if max(abs(a[i] - c[i]) for i in range(3)) > 0.001:
            blendkit.fail(f"safe bodies differ: closed {a} vs open {c} — swapping the variants "
                          "at the moment the Engineer opens it would make the safe jump")
        lines.append(f"safe body identical in both variants ({a[0]:.3f}x{a[1]:.3f}x{a[2]:.3f} m)  OK")

    # §12's concealment must actually swallow a player.
    if "HidingSpot_Locker" in names:
        cav = META["HidingSpot_Locker"]["cavity"]  # type: ignore[index]
        need = (0.55, 0.45, PLAYER_HEIGHT_ASSUMED)
        if cav[0] < need[0] or cav[1] < need[1] or cav[2] < need[2]:
            blendkit.fail(f"hiding spot cavity {cav} is smaller than a "
                          f"{PLAYER_HEIGHT_ASSUMED} m player needs {need} — §12's checklist item "
                          "would be cosmetic")
        lines.append(f"hiding cavity {cav[0]:.2f}x{cav[1]:.2f}x{cav[2]:.2f} m swallows a "
                     f"{PLAYER_HEIGHT_ASSUMED} m player, with "
                     f"{META['HidingSpot_Locker']['louvres']} louvres to watch through  OK")

    # §03's clue faces.
    for spec in todo:
        if "clue_face" not in spec.checks:
            continue
        f = row(spec.name)["face"]
        lines.append(f"{spec.name}: readable face {f['width'] * 100:.1f}x{f['height'] * 100:.1f} cm, "
                     f"UV 0..1, material '{CLUE_FACE}'  OK")
    if "Clue_EngravedPlate" in names:
        err = row("Clue_EngravedPlate")["sym"]
        if err > 0.0015:
            blendkit.fail(f"the engraved plate is {err * 1000:.2f} mm off 180° symmetry — an "
                          "up-cue that large restores the orientation and kills §03's 6↔9")
        lines.append(f"engraved plate is 180°-symmetric to {err * 1000:.3f} mm — §03's 6↔9 stands  OK")

    # §03's flare pair: the unlit one must be dark.
    if "Flare_Unlit" in names:
        if row("Flare_Unlit")["emissive"] != 0:
            blendkit.fail("the unlit flare carries an emissive material")
        lines.append("unlit flare has 0 emissive materials  OK")
    if "Flare_Lit" in names:
        if row("Flare_Lit")["emissive"] < 1:
            blendkit.fail("the lit flare has nothing emissive — §03's 조명탄 must light a zone")
        lines.append(f"lit flare has {row('Flare_Lit')['emissive']} emissive material "
                     f"(strength {MATERIALS[FLARE].emission})  OK")

    # ── Report ─────────────────────────────────────────────────────────────
    print()
    print("=" * 118)
    print("PROPS — measured, not intended.  §08 weight/value quoted from GameConstants.")
    print("=" * 118)
    head = (f"{'prop':<28}{'category':<14}{'dimensions (m)':<22}{'tris':>6}"
            f"{'verts':>7}{'W':>3}{'value':>7}{'§08':>9}")
    print(head)
    print("-" * 118)
    for r in ROWS:
        sx, sy, sz = r["size"]
        loot = LOOT_ROWS.get(r["name"])
        w = str(loot[1]) if loot else "-"
        val = str(loot[2]) if loot else "-"
        word = loot[3] if loot else "-"
        print(f"{r['name']:<28}{r['category']:<14}"
              f"{sx:.3f} x {sy:.3f} x {sz:.3f}  {r['tris']:>6}{r['verts']:>7}"
              f"{w:>3}{val:>7}{word:>9}")
    print("-" * 118)
    print(f"{len(ROWS)} props   total tris {sum(r['tris'] for r in ROWS)}   "
          f"max tris {max(r['tris'] for r in ROWS)} ({max(ROWS, key=lambda r: r['tris'])['name']})")
    print()
    print("§08 loot — mechanical weight against physical size")
    print(f"{'prop':<28}{'LootId':<14}{'W':>2}{'value':>7}{'cr/W':>7}"
          f"{'longest':>9}{'footprint':>11}{'solid m³':>10}")
    for r in ROWS:
        loot = LOOT_ROWS.get(r["name"])
        if not loot:
            continue
        lid, w, val, _ = loot
        print(f"{r['name']:<28}{lid:<14}{w:>2}{val:>7}{val / w:>7.1f}"
              f"{longest_horizontal(r):>9.3f}{footprint(r):>11.4f}{r['volume']:>10.5f}")
    # What each prop is actually *made of*, by surface area rather than by slot
    # count. ART.md §7.12's defect is invisible in every other table in this
    # report: the 차량 lists seven materials and two of them are a lens.
    print()
    print("what each prop's surface is made of, and its biggest flat slab (ART.md §7.12)")
    print(f"{'prop':<28}{'dominant material':<22}{'share':>7}{'metal':>7}{'lum':>7}"
          f"{'face m²':>9}{'narrow m':>10}  verdict")
    for r in ROWS:
        areas = r["areas"]
        total = sum(areas.values())
        if total <= 0.0:
            continue
        top, area = max(areas.items(), key=lambda kv: kv[1])
        spec = MATERIALS.get(top)
        if spec is None:
            continue
        lum = albedo_luminance(top)
        face, span = r["panels"].get(top, (0.0, 0.0))
        if spec.metallic > 0.5 and lum < DARK_METAL_LUMINANCE:
            verdict = ("HOLE"
                       if face > DARK_METAL_PANEL_AREA and span > DARK_METAL_PANEL_SPAN
                       else "dark metal, but curved or trim")
        else:
            verdict = "metal, bright" if spec.metallic > 0.5 else "dielectric"
        print(f"{r['name']:<28}{top:<22}{area / total * 100:6.0f}%{spec.metallic:7.2f}"
              f"{lum:7.3f}{face:9.2f}{span:10.2f}  {verdict}")

    print()
    for line in lines:
        print("CHECK " + line)
    print()
    for r in ROWS:
        print(f"FILE {r['path']} ({r['bytes']} bytes)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_props.py raised:\n" + traceback.format_exc())
