#!/usr/bin/env python3
"""Generates what is left of the basement's prop kit, and is the shared prop-
building library `gen_dressing.py` is written on top of.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_props.py

Optional filter while iterating (everything after ``--`` is a name substring)::

    ... --python tools/blender/gen_props.py -- Pipes

Outputs one FBX per prop into ``Assets/Models/Props/``, plus
``Props.manifest.json`` — the material contract ``PropMaterials.cs`` rebuilds
URP Lit materials from, because FBX cannot carry a PBR material at all.


WHAT THIS FILE USED TO BE, AND WHY IT IS NOT THAT ANY MORE
==========================================================

It authored 25 props: §08's seven 전리품, the 금고 pair, the 목표물, the 승합차,
the three 단서 fittings, the 조명탄 pair, the 배전반, the 발전기, the 은폐 지점,
the 궤짝, and 정비공's 바리케이드 and 소음 함정. Every one of them was an
*interactable*, and the only thing in the project that ever placed one was
``Assets/Resources/InteractableProps.asset``, whose MonoScript
``HorrorGame.Gameplay.Interaction.InteractablePropLibrary`` was deleted with §08.

하강 is a 선착순 미로탈출게임. 손전등 말고 드는 것이 없고, 팔 곳도 살 곳도
없고, 목적지는 처음부터 알려져 있다 — so there is no piece of the kit a runner
can pick up, read, open, buy, light or carry. The 22 that were mechanism went;
the full ledger is in the tombstone above the builders.

Three pieces are left — Pipes, Shelving, Debris — and they are *scenery*: a
corridor's depth cue, a sightline blocker and a floor break. Nothing places them
either, because the library that did is gone; they are kept because they are not
co-op mechanism and because a generator with no output cannot check its own
scale, materials or export path. See the SET DRESSING banner for the condition
under which they should go too.

One set has been ADDED since the pivot: the four 깜짝 (Startle) set pieces —
see the STARTLE banner above their builders. They are not a return of the
interactable kit: a startle is render-side theatre on the triggering player's
own client, seeded per map, with no network traffic and no channel to the
creature (GameConstants.cs §09 records why a placed noise is a forged
footstep). This file supplies geometry only; triggering, pacing and the Unity
hinge empty live with the integrator.


WHAT THE DESIGN STILL DICTATES ABOUT THEIR SHAPE
================================================

* **§05 — first person, dark, headphones.** Detail buys nothing; silhouette and
  specular response buy everything. Budget is 1500 triangles per prop and both
  survivors spend well under half of it.
* **§12 — a corridor has to read as a place.** Overhead pipes differentiate a
  corridor's *look* without narrowing its walkable width, which §12's 20 m
  straight-run rule already constrains; shelving breaks a sightline while
  staying cover rather than wall (aggro release is a map property, §06); debris
  breaks up a floor a torch is sweeping.
* **§06 — silence is designed.** Nothing here animates or hums on its own.
* **ART.md §7.12 — a dark metal with nothing to reflect renders as a hole.**
  There is no reflection probe in this building and a 12 m torch is the only
  light, so a large flat slab of metallic-and-dark is a shape the player never
  sees. `check_metal_is_not_a_panel` refuses to ship one, and it is the one
  cross-prop check that survived the pivot because it is about how a surface
  behaves under this game's lighting rather than about §08's price list.


CONVENTIONS THIS FILE GUARANTEES
================================

* **1 Blender unit = 1 metre = 1 Unity unit.** Every prop is checked against a
  declared intended size, so a scale mistake fails the build instead of shipping.
* **Pivot.** Floor props: origin at floor level (min Z = 0), centred on the
  footprint. Wall props: origin at the wall plane (max Y = 0), centred in X, with
  the real mounting height left in Z — so dropping one at the foot of a wall puts
  it at the correct height with no offset to remember. Hinge props (the 깜짝
  cabinet leaf): origin ON the hinge edge — min X = 0 is the hinge axis, max
  Y = 0 is the closed-leaf back plane, min Z = 0 is the leaf's own bottom — the
  same contract as the map kit's ``Door_Panel_Lockable``, so the Unity editor
  pass parents the leaf to a hinge empty and a plain rotation swings it.
* **Facing.** Props face **-Y** in Blender, matching the monster generator, which
  `export_fbx`'s ``axis_forward='-Z'`` turns into +Z forward in Unity. No
  compensating rotation is applied anywhere in this file.
* **One mesh, one object, identity transform** per FBX. Multi-material by design;
  material names are the seam Unity binds against.
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


# ── §08's economy table went with §08 ───────────────────────────────────────
# LOOT_ROWS quoted GameConstants' LootWeight*/LootValue* for the seven 전리품
# pieces, and CONSPICUOUS / EFFICIENT were §08's "눈에 잘 보임 (유혹)" and "효율
# 최고" rows. 팔 곳도 살 것도 없으므로 값도 무게도 없다: the props are gone, the
# constants they quoted are tombstones in GameConstants, and the cross-prop
# check that read them (`check_economy_is_legible`) went with the table.

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
IRON = "Prop_Iron"
RUST = "Prop_Rust"
STONE = "Prop_Stone"

# DELETED with the props that wore them. Prop_Silver / Prop_Brass /
# Prop_BrassTarnished / Prop_Gold / Prop_Gem / Prop_Mirror / Prop_Glass /
# Prop_Wax / Prop_Cloth / Prop_Leather / Prop_Canvas / Prop_Paper were §08's
# 전리품 surfaces — mirror-grade for the temptation row, dull for the efficient
# one — and there is no 전리품. Prop_Lamp and Prop_FlareBurn lit §08's 조명탄.
# Prop_VanBody / Prop_VanLower were the 승합차's two livery coats.
# Prop_PaintedSteel coated the 금고, the 배전반 and the 발전기. Prop_Paint and
# Prop_WoodDark were the 초상화 frame and the 궤짝 body. Clue_Face was §13's
# seam — the surface the host stamped a glyph onto — and §03 단서 is deleted,
# so both the material and every fitting that carried one are gone.

RUST_DARK = "Prop_RustDark"
WOOD_WORN = "Prop_WoodWorn"
MORTAR = "Prop_Mortar"

# ── 깜짝 (Startle) set-piece surfaces ───────────────────────────────────────
STEEL_PAINTED = "Prop_SteelPainted"
STEEL_CHIP = "Prop_SteelChip"
CAVITY_DARK = "Prop_CavityDark"
FUR_DARK = "Prop_FurDark"
BORE_DARK = "Prop_BoreDark"

MATERIALS: dict[str, MaterialSpec] = {
    WOOD: MaterialSpec(WOOD, (0.196, 0.126, 0.072), roughness=0.85),
    IRON: MaterialSpec(IRON, (0.112, 0.116, 0.122), roughness=0.55, metallic=0.90),
    RUST: MaterialSpec(RUST, (0.201, 0.092, 0.046), roughness=0.92, metallic=0.30),
    STONE: MaterialSpec(STONE, (0.302, 0.292, 0.271), roughness=0.90),
    # ── Weathering set (production detail pass) ─────────────────────────────
    # RUST is the painted-orange side of oxidation and reads as paint when it
    # covers a whole pipe; RUST_DARK is the stain — wet, near-dielectric, for
    # sleeves on a pipe run and the streak a bolt bleeds down a wall or a leg.
    RUST_DARK: MaterialSpec(RUST_DARK, (0.105, 0.052, 0.030), roughness=0.95, metallic=0.10),
    # Pale worn timber: shelf deck tops and fresh-broken plank faces. The beam
    # grazes horizontal surfaces, and at WOOD's 0.196 albedo a deck top under a
    # torch measured near-black — the worn top is what makes a shelf read as a
    # shelf instead of an empty frame.
    WOOD_WORN: MaterialSpec(WOOD_WORN, (0.295, 0.225, 0.150), roughness=0.80),
    # Mortar/plaster dust for debris: the pale pool that ties a rubble pile to
    # the floor the way §3.10's contact decals tie a crate to it.
    MORTAR: MaterialSpec(MORTAR, (0.415, 0.398, 0.368), roughness=0.95),
    # ── The gun's materials (consumed by gen_gun.py) ────────────────────────
    # Registered HERE because this table is what write_manifest() serialises and
    # PropMaterials.cs rebuilds URP materials from — and PropMaterials.Bind walks
    # every FBX under Assets/Models/Props, Gun_Held.fbx and Gun_Pickup.fbx
    # included. Before this entry the gun's slots were in NO manifest: Bind
    # logged them UNBOUND and the revolver shipped on the importer's guess —
    # metallic 0, no emission keyword, so no crosshair highlight either.
    # Value hierarchy, measured off the round-2 render: the first grip values
    # (0.098 wood, 0.038 wrap) rendered BRIGHTER than the steel and the gun read
    # as a toy with a taped brick for a handle. A gun is steel-first: the wood
    # sits below the worn steel's luminance and the wrap sits below the wood.
    "Gun_Steel": MaterialSpec("Gun_Steel", (0.062, 0.066, 0.074), roughness=0.52, metallic=1.0),
    "Gun_SteelWorn": MaterialSpec("Gun_SteelWorn", (0.330, 0.340, 0.360), roughness=0.30, metallic=1.0),
    "Gun_Bore": MaterialSpec("Gun_Bore", (0.012, 0.012, 0.013), roughness=0.90),
    "Gun_Grip": MaterialSpec("Gun_Grip", (0.055, 0.034, 0.022), roughness=0.55),
    "Gun_GripWrap": MaterialSpec("Gun_GripWrap", (0.020, 0.016, 0.014), roughness=0.88),
    "Gun_Cloth": MaterialSpec("Gun_Cloth", (0.295, 0.272, 0.228), roughness=0.95),
    # ── 깜짝 set-piece surfaces ─────────────────────────────────────────────
    # STEEL_PAINTED is the ART.md §7.12 lesson applied in advance: the cabinet's
    # big flat faces are a dielectric with a real albedo (luminance 0.226, a hair
    # over the 0.21 darkest corridor wall), never forged iron — a slammed-open
    # door the beam cannot find is a startle that never happened. Drab industrial
    # enamel, greener than gen_dressing's Dress_SteelPainted so the two kits do
    # not read as one delivery.
    STEEL_PAINTED: MaterialSpec(STEEL_PAINTED, (0.212, 0.232, 0.208), roughness=0.55),
    # Bare metal where the paint has chipped: knuckles, handle, striker, chips.
    # Metallic and bright — always trim-sized, so §7.12's panel gate never sees it.
    STEEL_CHIP: MaterialSpec(STEEL_CHIP, (0.452, 0.462, 0.478), roughness=0.38, metallic=1.0),
    # The cabinet's interior. Near-black dielectric: the cavity behind the leaf
    # must read as DEPTH the beam does not reach, which is the half of the scare
    # geometry can deliver (the other half is the runtime's slam).
    CAVITY_DARK: MaterialSpec(CAVITY_DARK, (0.028, 0.027, 0.025), roughness=0.97),
    # The skitterer's whole body. Same reasoning as gen_ghost's 0.02-0.04 skin:
    # a thing that does not answer the beam reads as a moving absence, and at
    # 0.35 m crossing a corridor that absence IS the startle. Matte so the one
    # highlight a curved near-black could return is killed too.
    FUR_DARK: MaterialSpec(FUR_DARK, (0.032, 0.028, 0.024), roughness=0.95),
    # The broken pipe's mouth interior, recessed behind the torn rim — the same
    # job Gun_Bore does for the revolver: a hole that stays a hole under a
    # coaxial torch.
    BORE_DARK: MaterialSpec(BORE_DARK, (0.014, 0.013, 0.012), roughness=0.90),
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
              mseg=16, nseg=6, mat=IRON, tag: str = "", role: str = "",
              nobevel: bool = True):
        bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor,
                                         major_segments=mseg, minor_segments=nseg,
                                         location=tuple(loc))
        obj = bpy.context.active_object
        obj.name = self._tag(tag or role)
        obj.rotation_euler = Euler(rads(tuple(rot)), "XYZ")
        return self._register(obj, mat, role, nobevel)

    def quad(self, w, h, loc=(0.0, 0.0, 0.0), rot=(0.0, 0.0, 0.0), mat=IRON,
             tag: str = "", role: str = ""):
        """A single-quad flat face. Never bevelled — bevelling would eat it.

        The default used to be ``Clue_Face``, because every quad in the kit was
        §03's readable glyph surface. 단서 is deleted and so is that material;
        the default is now the kit's plainest metal and every real caller —
        all of them in `gen_dressing.py` — passes ``mat=`` explicitly anyway.
        """
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
#  TOMBSTONE — the 22 props DESCENT-PIVOT §3 deleted, and the one thing that
#  ever placed them.
#
#  Every prop this file used to author was an *interactable* for the co-op
#  game, and the only thing in the project that ever put one in a scene was
#  `Assets/Resources/InteractableProps.asset`, a ScriptableObject whose
#  MonoScript — `HorrorGame.Gameplay.Interaction.InteractablePropLibrary` —
#  was deleted with §08. With the library gone the catalogue is 25 rows of
#  GUIDs nothing reads, so the kit was not "art waiting for a level designer";
#  it was art with no way into the game at all.
#
#  What went, and which section deleted it:
#    §03 단서   Clue_WallBoard, Clue_LedgerStand, Clue_EngravedPlate — 목적지가
#               이미 알려져 있으니 좁혀 갈 것이 없다. The Clue_Face material and
#               its 1↔7 / 6↔9 / 좌↔우 glancing-read geometry went with them.
#    §01 임무   Objective (목표물), Crate (2인 운반용 궤짝), Vehicle (승합차 —
#               출발선은 B1 바깥 테두리고 도착선은 B8 한가운데다), and the
#               Prop_VanBody / Prop_VanLower paint authored for it.
#    §01 임무   ElectricalPanel (배전반 — 불을 밝히는 일이 게임에서 빠졌다;
#               MapNode's ElectricalPanel mark and MapValidator's rule are
#               already tombstoned) and SurfaceGenerator (four charging
#               cradles, one per player — 배터리 경제).
#    §08 경제   the seven Loot_* pieces, Safe_Closed / Safe_Open (금고) — 팔
#               곳도 살 것도 없다. The value/weight table, the mirror-grade
#               은수저 and the safe-cavity fit check went with them.
#    §08 재고   Flare_Lit and Flare_Unlit (조명탄) and Prop_FlareBurn. Only
#               Flare_Lit fired the tombstone guard, because `unlit` is not one
#               of the mates that make `flare` mean 조명탄 rather than the
#               monster's crest; the pair is one deleted item and both went.
#    §04 직업   Barricade and NoiseTrap — 정비공's two placeables. Their
#               constants (EngineerBarricadeSeconds, EngineerTrapMaterialCost)
#               are already tombstones in GameConstants.
#    §12 은폐   HidingSpot_Locker. RuleConcealmentNearExit and MapNodeKind's
#               Concealment flag are tombstoned in MapValidator and MapNode;
#               the dressing kit's Dress_LockerBank is the locker the maze
#               still wants, and it is scenery rather than a hiding place.
#
#  WHAT THIS FILE STILL IS. `gen_dressing.py` imports it — Frame, PropBuild,
#  world_bbox, bbox_size, pivot_shift, apply_smooth, material_index,
#  map_face_uv_unit, rotational_symmetry_error, emissive_material_count and the
#  shared MATERIALS table are the machinery all 38 dressing pieces are built
#  with. So the module stays and keeps its three surviving pieces; it is not a
#  library-only file like gen_mapkit_detail.py, and `--manifest-only` still
#  writes the contract PropMaterials.cs binds from.
# ══════════════════════════════════════════════════════════════════════════


# ══════════════════════════════════════════════════════════════════════════
#  SET DRESSING — minimal and reusable. What is left of the kit: three generic
#  industrial pieces with no system attached to them, kept because they are
#  scenery rather than co-op mechanism and because the module needs at least
#  one prop for its own size and material checks to mean anything.
#
#  Nothing places them today — the placement library went with §08 — so if a
#  later round finds the dressing kit's Dress_PipeRun_Wall, Dress_ShelfStocked
#  and Dress_RubblePile cover the same three jobs, these three and this file's
#  main() can go too and gen_props.py becomes a pure library.
# ══════════════════════════════════════════════════════════════════════════



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
        # Two bolt heads where the wall plate meets the brick. A bracket with no
        # fixing reads as a block resting against the wall, which is what the
        # beam render showed.
        for z in (2.075, 2.165):
            b.cyl(0.0060, 0.014, (x + 0.017, -0.033, z), rot=(90.0, 0.0, 0.0),
                  verts=6, mat=IRON, nobevel=True)
            b.cyl(0.0060, 0.014, (x - 0.017, -0.033, z), rot=(90.0, 0.0, 0.0),
                  verts=6, mat=RUST, nobevel=True)
    # A bolted flange pair on the main run. 2.4 m of pipe with no joint reads as
    # an extrusion; one flange is what says "assembled, and by somebody".
    for fx in (-0.531, -0.509):
        b.cyl(0.078, 0.016, (fx, -0.100, 2.120), rot=(0.0, 90.0, 0.0), verts=12,
              mat=IRON, nobevel=True)
    for k in range(6):
        a = math.radians(k * 60.0 + 30.0)
        b.cyl(0.0072, 0.052, (-0.520, -0.100 + 0.060 * math.cos(a),
                              2.120 + 0.060 * math.sin(a)),
              rot=(0.0, 90.0, 0.0), verts=6, mat=RUST, nobevel=True)
    # A coupling sleeve on the mid pipe, and one on the main where the rust has
    # crept over the joint.
    b.cyl(0.040, 0.070, (0.350, -0.190, 2.050), rot=(0.0, 90.0, 0.0), verts=10,
          mat=IRON, nobevel=True)
    b.cyl(0.058, 0.080, (1.050, -0.100, 2.120), rot=(0.0, 90.0, 0.0), verts=10,
          mat=RUST_DARK, nobevel=True)
    # Stain sleeves: RUST is the painted-orange side of oxidation and a whole
    # pipe of it reads as paint (the van lesson, again). Two darker wet-stain
    # bands break the monotone the way real seepage does.
    b.cyl(0.0528, 0.220, (-0.150, -0.100, 2.120), rot=(0.0, 90.0, 0.0), verts=10,
          mat=RUST_DARK, nobevel=True)
    b.cyl(0.0528, 0.160, (0.800, -0.100, 2.120), rot=(0.0, 90.0, 0.0), verts=10,
          mat=RUST_DARK, nobevel=True)
    # Rust bleed down the wall under the outer brackets: the §3.10 rust-bleed
    # decal's cousin, carried by the prop so it arrives wherever the prop does.
    for x in (-0.900, 0.900):
        b.box((0.055, 0.006, 0.420), (x, -0.004, 1.900), mat=RUST_DARK, nobevel=True)
    b.box((0.075, 0.006, 0.300), (0.0, -0.004, 1.950), mat=RUST_DARK, nobevel=True)
    # A valve and a drop leg, so the run reads as plumbing rather than as a stripe.
    b.cyl(0.052, 0.240, (0.560, -0.100, 2.000), verts=10, mat=RUST, nobevel=True)
    b.cyl(0.070, 0.070, (0.560, -0.100, 1.880), verts=12, mat=IRON, nobevel=True)
    b.torus(0.086, 0.014, (0.560, -0.100, 1.830), rot=(0.0, 0.0, 0.0), mseg=14, nseg=5,
            mat=RUST)
    for i in range(3):
        b.box((0.170, 0.016, 0.016), (0.560, -0.100, 1.830), rot=(0.0, 0.0, i * 60.0),
              mat=RUST, nobevel=True)
    # Packing-gland nut on the valve stem — the silhouette between body and wheel.
    b.cyl(0.030, 0.026, (0.560, -0.100, 1.856), verts=6, mat=RUST_DARK, nobevel=True)
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
        # A pale worn top sheet per deck. The beam grazes horizontals, and at
        # WOOD's albedo the decks measured near-black — the unit read as an
        # empty frame in its own beam render. This is the shelf's §7.12 fix:
        # not brighter iron, a lighter dielectric where the light lands.
        b.box((W - 0.060, D - 0.040, 0.005), (0.0, 0.0, z + t / 2 + 0.0025),
              mat=WOOD_WORN, nobevel=True)
        # Bolt heads through the front legs at every deck rail.
        for sx in (-1.0, 1.0):
            b.cyl(0.0068, 0.014, (sx * (W / 2 - 0.035), -0.235, z + 0.030),
                  rot=(90.0, 0.0, 0.0), verts=6, mat=RUST, nobevel=True)
    # Corner gussets under the upper decks — the join detail that says bolted
    # steel rather than glued toy.
    for z in (0.640, 1.180, 1.720):
        for sx in (-1.0, 1.0):
            b.box((0.048, 0.010, 0.048), (sx * (W / 2 - 0.075), -D / 2 + 0.020, z - 0.042),
                  rot=(0.0, 45.0, 0.0), mat=IRON, nobevel=True)
    # Feet: base plates with an anchor bolt each. A leg that meets the slab as a
    # bare 90° edge is what made the unit look dropped-in rather than installed.
    for (sx, sy) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.box((0.095, 0.095, 0.014), (sx * (W / 2 - 0.035), sy * (D / 2 - 0.035), 0.007),
              mat=IRON)
        b.cyl(0.0060, 0.012, (sx * (W / 2 - 0.010), sy * (D / 2 - 0.010), 0.017),
              verts=6, mat=RUST, nobevel=True)
    # Back cross-braces.
    b.box((W - 0.10, 0.024, 0.060), (0.0, D / 2 - 0.030, 1.000), rot=(0.0, 26.0, 0.0),
          mat=IRON)
    b.box((W - 0.10, 0.024, 0.060), (0.0, D / 2 - 0.030, 1.000), rot=(0.0, -26.0, 0.0),
          mat=IRON)
    # Rust bleeding down the front legs from the top and mid rails.
    for sx in (-1.0, 1.0):
        b.box((0.032, 0.005, 0.260), (sx * (W / 2 - 0.035), -0.2325, 1.560),
              mat=RUST_DARK, nobevel=True)
        b.box((0.026, 0.005, 0.180), (sx * (W / 2 - 0.035), -0.2325, 0.500),
              mat=RUST_DARK, nobevel=True)
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
    # Mortar lumps among the stone: broken render is never one mineral. Pale,
    # dielectric, and the tone gap is what stops the chunks reading as grey dice.
    for (sx, sy, sz, x, y, z, yaw) in (
        (0.115, 0.095, 0.075, -0.180, 0.080, 0.038, 71.0),
        (0.095, 0.085, 0.060, 0.150, 0.260, 0.030, -23.0),
        (0.080, 0.070, 0.055, -0.380, 0.300, 0.028, 44.0),
    ):
        b.box((sx, sy, sz), (x, y, z), rot=(9.0, 7.0, yaw), mat=MORTAR)
    for (x, y, r) in ((-0.100, -0.400, 0.070), (0.360, 0.360, 0.060)):
        b.sph(r, (x, y, r * 0.55), scale=(1.0, 0.9, 0.55), segs=10, rings=5, mat=STONE,
              nobevel=True)
    # The base is two offset slabs rather than one — a single crisp rectangle
    # under a rubble pile read as a bathmat in the beam render — plus two pale
    # dust pools that tie the pile to the slab the way §3.10's decals tie a
    # crate to it.
    b.box((1.050, 0.680, 0.020), (0.020, 0.010, 0.010), rot=(0.0, 0.0, 9.0), mat=STONE)
    b.box((0.850, 0.600, 0.018), (-0.060, -0.040, 0.009), rot=(0.0, 0.0, -14.0), mat=STONE)
    b.cyl(0.160, 0.010, (-0.250, -0.150, 0.005), verts=12, mat=MORTAR, nobevel=True)
    b.cyl(0.120, 0.010, (0.300, 0.150, 0.005), verts=12, mat=MORTAR, nobevel=True)
    # Bent rebar out of the rubble — the one silhouette element concrete debris
    # is never without. Kept low: max tip 0.245 m, still under knee height.
    b.cyl(0.0055, 0.620, (-0.150, 0.150, 0.100), rot=(0.0, 78.0, 35.0), verts=8,
          mat=RUST, nobevel=True)
    b.cyl(0.0055, 0.550, (0.250, -0.050, 0.130), rot=(0.0, 82.0, -20.0), verts=8,
          mat=RUST_DARK, nobevel=True)
    b.cyl(0.0055, 0.450, (-0.050, -0.320, 0.160), rot=(0.0, 68.0, 100.0), verts=8,
          mat=RUST, nobevel=True)
    # Splintered ends on two planks: a fresh pale break face, angled off the
    # plank line.
    b.box((0.085, 0.042, 0.018), (0.392, 0.075, 0.022), rot=(0.0, 4.0, 26.0),
          mat=WOOD_WORN, nobevel=True)
    b.box((0.075, 0.038, 0.016), (-0.560, 0.055, 0.024), rot=(0.0, -6.0, 4.0),
          mat=WOOD_WORN, nobevel=True)
    # One plank propped on the mound. Its only job is to break the flat silhouette
    # so a beam finds the pile; at 0.30 m it is still well under knee height. Its
    # top face is the worn pale timber, because it is the one surface the beam
    # actually lands on.
    b.box((0.760, 0.130, 0.030), (0.050, -0.050, 0.152), rot=(0.0, -20.0, 20.0), mat=WOOD)
    b.box((0.740, 0.120, 0.006), (0.050, -0.050, 0.170), rot=(0.0, -20.0, 20.0),
          mat=WOOD_WORN, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  STARTLE — the 깜짝 set pieces. Geometry ONLY.
#
#  What a startle is here: a fitting the triggering player's OWN client animates
#  once — a cabinet leaf slammed open, a low shape darting the corridor, a wall
#  pipe venting a burst. Seeded deterministic placement per map, rendered
#  per-player, zero network traffic, and NO channel to the creature: §12 makes
#  sound the map, so a placed noise is a forged footstep — the exact reason the
#  pivot deleted the last system that put noises in the world (the §09 block in
#  GameConstants.cs records it). Triggering, per-player pacing and the Unity
#  hinge empty are the integrator's; this file ships shapes and their contracts.
#
#  The cabinet mirrors the map kit's proven two-file door: Doorway_Frame.fbx +
#  Door_Panel_Lockable.fbx, where the leaf's origin IS the hinge axis and the
#  editor pass (MapSceneBuilder.BuildDoor is the template) creates the hinge
#  empty and parents the leaf. Same split here: Startle_CabinetShell supplies a
#  leaf-sized opening, Startle_CabinetLeaf is a separate FBX whose origin sits
#  on its hinge edge (mount="HINGE" below enforces it at build time).
#
#  EXPORT PATH. The four pieces ship through the same emit() →
#  blendkit.export_fbx() every other prop uses — no startle-specific export
#  settings exist — so their FBX carry the repo convention exactly as
#  Gun_Pickup.fbx does: Z-up metre vertices, and the Z-up→Y-up conversion
#  parked on the root node as Lcl Rotation (−90, 0, 0) with Lcl Scaling 100
#  (AssetImportModelPostprocessor keeps bakeAxisConversion off and cancels the
#  100 with fileScale). A placement that overrides the imported root's
#  rotation therefore discards that −90° X and must compose it back in —
#  MapSceneBuilder's KitOrientation probe is the measured template.
# ══════════════════════════════════════════════════════════════════════════

# Working figures the startle set is sized against. The first is the module's
# own §05 note (largest_visible_panel: "§05 puts the eye at 1.63 m"); the rest
# derive from it and from the render protocol's trigger distances.
STARTLE_EYE_HEIGHT = 1.63
"""Eye height the set pieces are framed for, metres. Same figure the §7.12
panel check reasons with; NOT a design-doc number, flagged like the others."""

CABINET_W, CABINET_D, CABINET_H = 0.560, 0.200, 0.720
"""Cabinet carcass, metres. Width: hung on a §12 corridor wall
(CORRIDOR_WIDTH_ASSUMED = 1.60 m), 0.560 + face frame stays narrower than the
0.60 one-hand span so it reads as a wall fitting, not furniture. Depth: 0.200
leaves 1.40 m of corridor clear — 2.8× PLAYER_SHOULDER_ASSUMED, so the piece
can never turn a startle into a §05 collision (backward movement is 65% speed;
geometry must not add a death to a scare). Height: 0.720 so the whole leaf
sweep fits a 1.5 m-distance view cone (±0.40 m about the eye line covers
roughly ±15°, the comfortable vertical read at trigger range)."""

CABINET_MOUNT_Z = 1.050
"""Carcass underside, metres above the floor. Puts the cavity centre at
1.410 m — 0.22 m under the 1.63 m eye so the leaf slams across the
lower-centre of the frame at the 1.5 m trigger distance, where the §03 beam
(aimed with the eye) actually is, instead of at the ceiling shadow above it."""

CABINET_LIP = 0.030
"""Face-frame lip, metres. The opening is the carcass minus this lip all
round; 0.030 is the smallest lip that still throws a legible frame-shadow line
around the dark cavity under a 1.5 m beam (thinner read as a seam, not a
frame, in the beam test)."""

CABINET_LEAF_GAP = 0.003
"""Clearance per edge between leaf and opening, metres. 3 mm: visible as a
shadow gap at 1.5 m, invisible at 4 m, and generous enough that the runtime's
slam rotation never intersects the jamb."""

LEAF_T = 0.024
"""Leaf slab thickness. Sheet-steel door language — the dressing kit's locker
doors are 0.028 and this one is a cabinet, not a locker."""

SKITTERER_BODY_LENGTH = 0.335
"""Nose-to-rump target, metres — the "~0.35 m rat-sized body" the set was
asked for; the tail runs the bbox out to ~0.46 m. At this size crossing a
1.60 m corridor the dart lasts under half a second at animal speed — long
enough to be seen, too short to be inspected, which is the whole trick of a
shape with no limbs."""

PIPESTUB_AXIS_Z = 1.250
"""Broken pipe's axis height, metres. Between waist and chest: the vent burst
crosses the frame's lower half at both trigger distances, and the band does
not collide with the cabinet's 1.05-1.77 m so the two set pieces can share a
corridor wall without reading as one object."""


def build_startle_cabinet_shell() -> PropBuild:
    """Wall cabinet carcass with a leaf-sized opening. The leaf is its own FBX.

    Industrial language borrowed from the dressing kit's locker bank (chipped
    painted steel, bare-metal fittings, rust weep below) but built here so the
    startle set rides gen_props' own material manifest. The cavity is DARK on
    purpose — one shelf and a near-black liner: what the beam finds when the
    leaf slams open is depth, not contents. Nothing lives inside; a 선착순 race
    has nothing to put there, and an empty dark box is the scarier one anyway.
    """
    b = PropBuild("Startle_CabinetShell")
    W, D, H = CABINET_W, CABINET_D, CABINET_H
    z0 = CABINET_MOUNT_Z
    zc = z0 + H / 2
    f = CABINET_LIP
    t = 0.020

    # Carcass: back, sides, top, bottom. Painted steel, beveled.
    back = b.box((W, 0.016, H), (0.0, -0.008, zc), mat=STEEL_PAINTED, role="back")
    b.pivot_part = back
    for sx in (-1.0, 1.0):
        b.box((t, D, H), (sx * (W / 2 - t / 2), -D / 2, zc), mat=STEEL_PAINTED)
    b.box((W - 2 * t, D, t), (0.0, -D / 2, z0 + H - t / 2), mat=STEEL_PAINTED)
    b.box((W - 2 * t, D, t), (0.0, -D / 2, z0 + t / 2), mat=STEEL_PAINTED)

    # One interior shelf at cavity mid-height.
    b.box((W - 2 * t - 0.004, D - 0.024, 0.012), (0.0, -D / 2 + 0.004, zc),
          mat=STEEL_PAINTED)

    # Near-black cavity liners: the back and both flanks. This is what makes the
    # opened cabinet read as a hole with a frame instead of a grey box — the
    # beam's answer from inside must be (almost) nothing.
    b.box((W - 2 * t - 0.008, 0.004, H - 2 * t - 0.008),
          (0.0, -0.018, zc), mat=CAVITY_DARK, nobevel=True)
    for sx in (-1.0, 1.0):
        b.box((0.004, D - 0.030, H - 2 * t - 0.008),
              (sx * (W / 2 - t - 0.004), -D / 2 + 0.002, zc), mat=CAVITY_DARK,
              nobevel=True)

    # Face frame, 12 mm proud of the carcass: the opening it leaves is the
    # contract the leaf is cut to. Opening: (W - 2f) x (H - 2f), centred.
    fy = -D - 0.006
    b.box((W, 0.012, f), (0.0, fy, z0 + H - f / 2), mat=STEEL_PAINTED)
    b.box((W, 0.012, f), (0.0, fy, z0 + f / 2), mat=STEEL_PAINTED)
    for sx in (-1.0, 1.0):
        b.box((f, 0.012, H - 2 * f), (sx * (W / 2 - f / 2), fy, zc),
              mat=STEEL_PAINTED)

    # Hinge knuckles on the -X jamb, bare metal — the visual anchor the Unity
    # hinge empty lands on. -X is the player's screen-left when facing the
    # cabinet (props face -Y; a viewer looking +Y has +X as screen-right).
    hinge_x = -(W / 2 - f)
    for dz in (0.165, 0.495):
        b.cyl(0.008, 0.048, (hinge_x, -D - 0.013, z0 + f + dz), verts=6,
              mat=STEEL_CHIP, nobevel=True)
    # Latch striker on the +X jamb.
    b.box((0.012, 0.010, 0.040), (W / 2 - f + 0.002, -D - 0.014, zc),
          mat=STEEL_CHIP, nobevel=True)

    # Mounting tabs and their bolts, on the wall above the carcass.
    for sx in (-1.0, 1.0):
        b.box((0.060, 0.014, 0.040), (sx * 0.180, -0.007, z0 + H + 0.020),
              mat=IRON, nobevel=True)
        b.cyl(0.006, 0.012, (sx * 0.180, -0.016, z0 + H + 0.020),
              rot=(90.0, 0.0, 0.0), verts=6, mat=RUST, nobevel=True)

    # Chipped paint: three bare-metal flecks where hands and the slamming leaf
    # have worn the enamel — front frame corners and the striker edge.
    b.quad(0.020, 0.028, (hinge_x + 0.006, fy - 0.0065, z0 + H - f - 0.020),
           rot=(90.0, 8.0, 0.0), mat=STEEL_CHIP)
    b.quad(0.016, 0.022, (W / 2 - f - 0.010, fy - 0.0065, zc + 0.060),
           rot=(90.0, -12.0, 0.0), mat=STEEL_CHIP)
    b.quad(0.024, 0.014, (0.060, fy - 0.0065, z0 + f + 0.010),
           rot=(90.0, 3.0, 0.0), mat=STEEL_CHIP)

    # Rust weeping down the wall from the carcass underside — the same
    # carried-decal trick Pipes uses, so the fitting arrives installed.
    b.box((0.040, 0.005, 0.300), (-0.140, -0.0035, z0 - 0.155), mat=RUST_DARK,
          nobevel=True)
    b.box((0.024, 0.005, 0.180), (0.095, -0.0035, z0 - 0.095), mat=RUST_DARK,
          nobevel=True)

    b.meta["opening"] = (W - 2 * f, H - 2 * f)
    # The hinge empty is NOT the opening's jamb corner: the leaf is authored
    # CABINET_LEAF_GAP short of the opening on every side, with its origin ON
    # its own hinge edge — so a hinge empty at the bare corner would seat the
    # closed leaf flush at the jamb and sill (0 mm) with a doubled gap at the
    # striker and head. The stated point is the corner inset by one gap in X
    # and Z, which is what puts 3 mm of shadow line on all four edges.
    b.meta["report_facts"] = [
        f"opening={W - 2 * f:.3f}x{H - 2 * f:.3f}m",
        f"hinge_jamb=minus_x opening_corner=({hinge_x:.3f},{fy - 0.006:.3f},{z0 + f:.3f}) "
        f"hinge_empty_at=({hinge_x + CABINET_LEAF_GAP:.3f},{fy - 0.006:.3f},"
        f"{z0 + f + CABINET_LEAF_GAP:.3f}) "
        f"(corner inset {CABINET_LEAF_GAP * 1000:.0f}mm in X and Z: closed leaf then "
        f"clears {CABINET_LEAF_GAP * 1000:.0f}mm per edge)",
        "leaf_file=Startle_CabinetLeaf.fbx",
    ]
    return b


def build_startle_cabinet_leaf() -> PropBuild:
    """The cabinet's door, exported alone. ORIGIN ON ITS HINGE EDGE.

    Contract (mount="HINGE", enforced in emit): min X = 0 is the hinge axis and
    the leaf extends +X; max Y = 0 is the closed-leaf back plane, thickness and
    handle toward -Y; min Z = 0 is the leaf's own bottom. Parent it to a hinge
    empty at the shell's -X jamb (coordinates in the shell's PROP_DETAIL) and a
    negative Z rotation swings it open toward the corridor — exactly how
    MapSceneBuilder.BuildDoor drives Door_Panel_Lockable.
    """
    b = PropBuild("Startle_CabinetLeaf")
    lw = CABINET_W - 2 * CABINET_LIP - 2 * CABINET_LEAF_GAP
    lh = CABINET_H - 2 * CABINET_LIP - 2 * CABINET_LEAF_GAP

    slab = b.box((lw, LEAF_T, lh), (lw / 2, -LEAF_T / 2, lh / 2),
                 mat=STEEL_PAINTED, role="slab")
    b.pivot_part = slab

    # Three louvre slats, tilted 40°, with the dark slot behind each — the
    # locker-bank vent language, and the one detail that says "cabinet with an
    # inside" while the leaf is still shut.
    for i in range(3):
        z = lh * 0.66 + (i - 1) * 0.048
        b.quad(0.180, 0.014, (lw / 2, -LEAF_T - 0.0005, z), rot=(90.0, 0.0, 0.0),
               mat=CAVITY_DARK)
        b.box((0.180, 0.010, 0.016), (lw / 2, -LEAF_T - 0.004, z - 0.004),
              rot=(40.0, 0.0, 0.0), mat=STEEL_PAINTED, nobevel=True)

    # Handle on the free (+X) edge: two stubs and a vertical grip, bare metal.
    hx = lw - 0.045
    for dz in (-0.045, 0.045):
        b.box((0.014, 0.026, 0.012), (hx, -LEAF_T - 0.013, lh / 2 + dz),
              mat=STEEL_CHIP, nobevel=True)
    b.box((0.016, 0.012, 0.110), (hx, -LEAF_T - 0.032, lh / 2), mat=STEEL_CHIP)

    # Hinge knuckles riding the hinge edge, proud of the front face. The real
    # hinge is the Unity empty; these are what the beam sees of it.
    for z in (0.165, 0.495):
        b.cyl(0.009, 0.048, (0.012, -LEAF_T - 0.004, z), verts=6,
              mat=STEEL_CHIP, nobevel=True)

    # Wear: two paint chips at the handle corner and hinge edge, and a rust
    # streak bleeding up from the bottom rail.
    b.quad(0.018, 0.024, (hx - 0.030, -LEAF_T - 0.0005, lh / 2 + 0.080),
           rot=(90.0, 15.0, 0.0), mat=STEEL_CHIP)
    b.quad(0.014, 0.020, (0.035, -LEAF_T - 0.0005, 0.090),
           rot=(90.0, -6.0, 0.0), mat=STEEL_CHIP)
    b.quad(0.028, 0.110, (lw * 0.22, -LEAF_T - 0.0005, 0.075),
           rot=(90.0, 2.0, 0.0), mat=RUST_DARK)

    b.meta["slab"] = (lw, lh)
    b.meta["report_facts"] = [
        "origin=hinge_edge (min X = hinge axis, leaf extends +X; max Y = closed "
        "back plane; min Z = leaf bottom)",
        "swing=negative_Z_rotation_opens_toward_corridor",
        f"slab={lw:.3f}x{lh:.3f}m fits shell opening minus {CABINET_LEAF_GAP * 1000:.0f}mm/edge",
    ]
    return b


def build_startle_skitterer() -> PropBuild:
    """A rat-sized dart-across-the-corridor shape. No rig: the runtime slides
    and bobs the transform, and at 0.35 m in a dark corridor implied legs ARE
    legs. Whole body near-black matte (FUR_DARK, albedo ~0.03): the read is a
    piece of the dark detaching and crossing the beam, silhouetted against the
    floor pool — measured in the beam test, not assumed. Faces -Y like every
    prop, so the runtime darts it along its own forward.
    """
    b = PropBuild("Startle_Skitterer")
    body = b.sph(0.110, (0.0, 0.010, 0.060), scale=(0.60, 1.25, 0.55),
                 segs=10, rings=5, mat=FUR_DARK, nobevel=True, role="body")
    b.pivot_part = body
    # Head: lower and narrower than the body — the dip between the two is what
    # makes the profile an animal instead of a slug. Nose lands at -0.188, so
    # nose-to-rump is 0.335 = SKITTERER_BODY_LENGTH.
    b.sph(0.052, (0.0, -0.128, 0.050), scale=(0.78, 1.15, 0.75),
          segs=8, rings=4, mat=FUR_DARK, nobevel=True)
    # Ears: two clipped flakes, raked back. Round 1 sized them 16 mm and put
    # them at ±0.024: under the steep 1.5 m down-view the FAR ear cleared the
    # skull line and read as a square tab on the neck. Smaller, inboard, swept.
    for sx in (-1.0, 1.0):
        b.box((0.011, 0.005, 0.014), (sx * 0.019, -0.134, 0.088),
              rot=(-24.0, sx * 16.0, sx * 8.0), mat=FUR_DARK, nobevel=True)
    # Tail: one tapering cone laid along +Y, tip a whisker up.
    b.cone(0.0085, 0.0015, 0.130, (0.0, 0.205, 0.055), rot=(-82.0, 0.0, 4.0),
           verts=6, mat=FUR_DARK, nobevel=True)
    # Leg fringe: eight slivers under the flanks. Silhouette only — under the
    # body they are unreadable as shapes but the outline they give the floor
    # line is "many small legs", which is the entire job. Round 1 splayed them
    # 16-23° at x ±0.052; at the 1.5 m trigger view (§05 eye, ~47° down) the
    # far-side pair cleared the flank contour and rode the animal's BACK as
    # square tabs. Now inboard of the flank at every station (body half-width
    # at the rear pair's y is 0.049) with 8° splay: tucked in plan view, still
    # feet in profile.
    for i, y in enumerate((-0.088, -0.026, 0.042, 0.100)):
        for sx in (-1.0, 1.0):
            b.box((0.010, 0.024, 0.046),
                  (sx * 0.044, y, 0.023),
                  rot=(0.0, sx * 8.0, sx * (6.0 - i * 3.0)),
                  mat=FUR_DARK, nobevel=True)
    b.meta["report_facts"] = [
        "no_rig=runtime_slides_and_bobs_transform",
        "forward=minus_Y",
        f"albedo_luminance={albedo_luminance(FUR_DARK):.3f} (does not answer the beam; "
        "read is silhouette against the floor pool)",
    ]
    return b


def build_startle_pipestub() -> PropBuild:
    """A broken wall pipe that vents a burst when triggered. Single mesh, so a
    nozzle-direction child empty is NOT possible in this export — the mesh is
    oriented with its MOUTH ON -Y (the wall-prop facing convention) and the
    runtime vents along the prop's own forward. Stated in the report.

    Rust language matches Pipes.fbx above: RUST for the painted-oxide body,
    RUST_DARK for wet stain and the wall weep, IRON for machined fittings.
    """
    b = PropBuild("Startle_PipeStub")
    z = PIPESTUB_AXIS_Z
    # Wall escutcheon plate, then a stained collar, then the stub itself.
    plate = b.cyl(0.075, 0.018, (0.0, -0.009, z), rot=(90.0, 0.0, 0.0),
                  verts=12, mat=IRON, nobevel=True, role="plate")
    b.pivot_part = plate
    b.cyl(0.055, 0.035, (0.0, -0.036, z), rot=(90.0, 0.0, 0.0), verts=12,
          mat=RUST_DARK, nobevel=True)
    b.cyl(0.045, 0.130, (0.0, -0.105, z), rot=(90.0, 0.0, 0.0), verts=12,
          mat=RUST, nobevel=True)
    # Mouth interior: a near-black core recessed 5 mm behind the rim. Under the
    # coaxial torch the mouth must stay a hole (the Gun_Bore trick) — the burst
    # the runtime spawns has to come FROM darkness, not from a grey disc.
    b.cyl(0.038, 0.140, (0.0, -0.095, z), rot=(90.0, 0.0, 0.0), verts=10,
          mat=BORE_DARK, nobevel=True)
    # The tear: eight ragged petals around the rim — deterministic table, no
    # RNG, same policy as Debris. Round 1 made them 30-44 mm and near-axial and
    # they closed over the mouth: the stub read as a solid brown knob with two
    # twigs, and the dark bore never showed. Now 18-28 mm, bent 45-70° outward,
    # centred ON the rim circle — a ragged crown around a visible hole. Three
    # petals are bare metal: freshly torn steel shows bright at the tear, and
    # that curved highlight is what anchors the piece at the 4 m read where
    # oxide tones sink into the wall.
    petals = (
        (0, 52.0, 0.022, RUST), (47, 64.0, 0.026, STEEL_CHIP),
        (98, 48.0, 0.018, RUST), (141, 70.0, 0.024, RUST_DARK),
        (187, 55.0, 0.028, STEEL_CHIP), (232, 62.0, 0.020, RUST),
        (275, 45.0, 0.026, RUST_DARK), (318, 58.0, 0.022, STEEL_CHIP),
    )
    for ang, bend, length, mtl in petals:
        a = math.radians(ang)
        px, pz = 0.047 * math.cos(a), 0.047 * math.sin(a)
        b.box((0.012, length, 0.006),
              (px, -0.170 - length * 0.22, z + pz),
              rot=(bend * math.sin(a), -bend * math.cos(a), ang * 0.20),
              mat=mtl, nobevel=True)
    # Flange bolts on the plate — three left; a torn pipe lost the fourth.
    for ang in (30.0, 150.0, 270.0):
        a = math.radians(ang)
        b.cyl(0.0065, 0.014, (0.060 * math.cos(a), -0.021, z + 0.060 * math.sin(a)),
              rot=(90.0, 0.0, 0.0), verts=6, mat=IRON if ang != 270.0 else RUST,
              nobevel=True)
    # Wall weep below the plate. Round 1 hung a 50 mm ribbon 15 mm clear of the
    # plate and it read as a plank leaning on the wall; a weep starts AT the
    # fitting that feeds it and narrows as it falls. Two staggered strips, both
    # tops tucked behind the plate's bottom edge, the long one under the mouth.
    b.box((0.026, 0.005, 0.260), (0.004, -0.0035, z - 0.195), mat=RUST_DARK,
          nobevel=True)
    b.box((0.012, 0.005, 0.150), (-0.030, -0.0035, z - 0.140), mat=RUST_DARK,
          nobevel=True)
    b.meta["report_facts"] = [
        "mouth=minus_Y (single-mesh export: no child empty possible; runtime "
        "vents along the prop's forward)",
        "mouth_interior=Prop_BoreDark recessed 5mm behind torn rim",
    ]
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
    # ── Set dressing ───────────────────────────────────────────────────────
    # All that is left. The §08 loot block, the Interactable block and the
    # 궤짝 were deleted with the systems that placed them — see the tombstone
    # above the builders.
    # Z grew 0.388 → 0.515 in the detail pass: the wall streaks under the outer
    # brackets run 0.42 m down the brick, and they are part of the prop so they
    # arrive wherever a level drops it.
    Spec("Pipes", build_pipes, "Dressing", (2.400, 0.231, 0.515), mount="WALL", bevel=0.004),
    Spec("Shelving", build_shelving, "Dressing", (1.840, 0.460, 1.940), bevel=0.005),
    Spec("Debris", build_debris, "Dressing", (1.237, 1.046, 0.299), bevel=0.004),
    # ── 깜짝 set pieces ────────────────────────────────────────────────────
    # Budgets are the integration brief's, not §05's default: these are seen at
    # 1.5 m, once, mid-slam — silhouette pieces, not hero props.
    Spec("Startle_CabinetShell", build_startle_cabinet_shell, "Startle",
         (0.560, 0.221, 1.065), mount="WALL", bevel=0.004, max_tris=900,
         note="깜짝 wall cabinet carcass. Opening 0.500x0.660 m; hinge empty at "
              "the -X jamb (see PROP_FACT). Leaf is Startle_CabinetLeaf.fbx."),
    Spec("Startle_CabinetLeaf", build_startle_cabinet_leaf, "Startle",
         (0.494, 0.062, 0.654), mount="HINGE", bevel=0.004, max_tris=300,
         note="깜짝 cabinet leaf. Origin ON the hinge edge (Door_Panel_Lockable "
              "contract): parent to a hinge empty, negative Z rotation slams it "
              "open toward the corridor."),
    Spec("Startle_Skitterer", build_startle_skitterer, "Startle",
         (0.119, 0.457, 0.121), bevel=0.0, max_tris=350,
         note="깜짝 floor darter. No rig; runtime slides + bobs the transform "
              "along -Y forward. Near-black matte on purpose."),
    Spec("Startle_PipeStub", build_startle_pipestub, "Startle",
         (0.150, 0.190, 0.400), mount="WALL", bevel=0.003, max_tris=400,
         note="깜짝 broken wall pipe. Mouth on -Y (single mesh, no child empty); "
              "runtime vents the burst along the prop's forward."),
]


# ── Emit one prop ───────────────────────────────────────────────────────────

ROWS: list[dict] = []
META: dict[str, dict] = {}


def pivot_shift(b: PropBuild, mount: str) -> Vector:
    """Offset that puts the prop's origin where the placement convention says.

    FLOOR: origin on the floor under the footprint centre. WALL: origin on the
    wall plane under the fitting, height preserved. Both mean a level designer
    drops the prop at a point on a surface and it is correct. HINGE (the 깜짝
    cabinet leaf): origin on the hinge edge — the pivot part's min X becomes 0
    (the hinge axis), max Y becomes 0 (closed-leaf back plane), min Z becomes 0
    (the leaf's own bottom) — so the Unity editor pass parents the FBX to a
    hinge empty and rotates it directly, the Door_Panel_Lockable contract.
    """
    ref = [b.pivot_part] if b.pivot_part is not None else b.parts
    rlo, rhi = world_bbox(ref)
    alo, ahi = world_bbox(b.parts)
    cx = (rlo.x + rhi.x) / 2
    if mount == "WALL":
        return Vector((-cx, -ahi.y, 0.0))
    if mount == "HINGE":
        return Vector((-rlo.x, -ahi.y, -alo.z))
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
    elif spec.mount == "HINGE":
        # The whole point of the two-file cabinet: the leaf's origin IS its
        # hinge. If any of these drift, the Unity editor pass parents the leaf
        # to a hinge empty and the slam orbits the wrong axis.
        if abs(lo[0]) > 0.006:
            blendkit.fail(f"{spec.name}: hinge prop's hinge edge is at x={lo[0]:.4f}, "
                          "must be 0 — the origin is the hinge axis")
        if abs(hi[1]) > 0.003:
            blendkit.fail(f"{spec.name}: hinge prop's closed back plane is at "
                          f"y={hi[1]:.4f}, must be 0")
        if abs(lo[2]) > 0.003:
            blendkit.fail(f"{spec.name}: hinge prop's bottom is at z={lo[2]:.4f}, "
                          "must be 0 (leaf-local, not floor)")
    else:
        if abs(lo[2]) > 0.003:
            blendkit.fail(f"{spec.name}: floor prop's base is at z={lo[2]:.4f}, must be 0")
    ROWS.append({
        "name": spec.name, "category": spec.category, "size": size, "tris": report.triangles,
        "verts": report.vertices, "mats": report.materials, "bytes": report.bytes,
        "note": spec.note, "mount": spec.mount, "path": path,
        "volume": mesh_volume(obj), "sharp": sharp, "bevel_skipped": bevel_skipped,
        "sym": sym, "emissive": emissive_material_count(obj),
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
    if sym is not None:
        extra.append(f"symmetry_error_mm={sym * 1000:.3f}")
    print("PROP_DETAIL " + " ".join(extra))
    # Per-prop contract lines (the 깜짝 pieces state their hinge/forward
    # contracts here so the integrator never has to open Blender to learn them).
    for fact in b.meta.get("report_facts", ()):
        print(f"PROP_FACT {spec.name} {fact}")


# ── Cross-prop checks ───────────────────────────────────────────────────────
#
# DELETED: `check_economy_is_legible`. It asserted that §08's price list was
# readable as geometry — that the 효율 최고 row was physically smaller than the
# 유혹 row, that a weight-5 piece was longer than a corridor is wide, that the
# 회중시계's tarnish kept it missable. Every input to it was a Loot_* prop and
# every threshold quoted a GameConstants value that is now a tombstone.
#
# DELETED: `check_the_vehicle_is_painted`. ART.md §7.12's van check — that the
# 승합차's body is dielectric paint and not forged iron, measured over 20 m.
# 출발선은 B1 바깥 테두리다: there is no van and no bay to see it across.
#
# DELETED: `check_two_person`. It measured the 대형 초상화 and the 궤짝 against a
# corridor's clear width to prove 2인 운반 costs something. Nothing is carried.
#
# KEPT: `check_metal_is_not_a_panel`. It is about how a surface renders under a
# 12 m torch in a black corridor, which is still every surface in this game.

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


def check_startle_leaf_fits_shell(names: list[str]) -> list[str]:
    """The two-file cabinet's one integration risk: a leaf that no longer fits
    the shell's opening. Measured off the exported leaf (bbox X and Z are the
    slab — nothing on the leaf reaches past it) against the shell's authored
    opening, and gated to the 2-12 mm band: under 2 mm the slam scrapes the
    jamb, over 12 mm the shut cabinet shows a black picture-frame gap at 1.5 m.
    Mirrors the Doorway_Frame + Door_Panel_Lockable pairing, where the same
    contract is held by MapKitCatalogue instead.
    """
    if "Startle_CabinetShell" not in names or "Startle_CabinetLeaf" not in names:
        return []
    ow, oh = META["Startle_CabinetShell"]["opening"]
    leaf = row("Startle_CabinetLeaf")
    gw = ow - leaf["size"][0]
    gh = oh - leaf["size"][2]
    for label, gap in (("width", gw), ("height", gh)):
        if not 0.002 <= gap <= 0.012:
            blendkit.fail(
                f"Startle_CabinetLeaf: {label} clearance to the shell opening is "
                f"{gap * 1000:.1f} mm total — outside 2-12 mm. Under 2 the slam "
                "scrapes the jamb; over 12 the shut cabinet wears a black frame.")
    return [f"cabinet leaf fits the shell opening: {gw * 1000:.1f} mm width / "
            f"{gh * 1000:.1f} mm height total clearance (2-12 mm band)  OK"]


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
    lines += check_metal_is_not_a_panel(names)
    lines += check_startle_leaf_fits_shell(names)

    # DELETED with the props they guarded, each of which was a system's model:
    #   * 금고 속 문서 fits the safe cavity            — §08 금고, no safe
    #   * Safe_Closed and Safe_Open share a body       — the swap was §04 정비공's
    #   * the 은폐 지점 cavity swallows a 1.80 m player — §12's Concealment mark is
    #                                                    a tombstone in MapNode
    #   * every clue face is flat and UV 0..1          — §03 단서
    #   * the engraved plate is 180°-symmetric         — §03's 6↔9 misread
    #   * the unlit 조명탄 carries nothing emissive     — §08 재고
    # Each read a `META` entry or a `row()["face"]` that no builder writes now.

    # ── Report ─────────────────────────────────────────────────────────────
    print()
    print("=" * 118)
    print("PROPS — measured, not intended.")
    print("=" * 118)
    head = (f"{'prop':<28}{'category':<14}{'dimensions (m)':<22}{'tris':>6}{'verts':>7}")
    print(head)
    print("-" * 118)
    for r in ROWS:
        sx, sy, sz = r["size"]
        print(f"{r['name']:<28}{r['category']:<14}"
              f"{sx:.3f} x {sy:.3f} x {sz:.3f}  {r['tris']:>6}{r['verts']:>7}")
    print("-" * 118)
    print(f"{len(ROWS)} props   total tris {sum(r['tris'] for r in ROWS)}   "
          f"max tris {max(r['tris'] for r in ROWS)} ({max(ROWS, key=lambda r: r['tris'])['name']})")
    # What each prop is actually *made of*, by surface area rather than by slot
    # count. ART.md §7.12's defect is invisible in every other table in this
    # report — a slot list says a prop has seven materials without saying that
    # one of them is 92% of what a torch actually hits.
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
