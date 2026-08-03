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
  it at the correct height with no offset to remember.
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

MATERIALS: dict[str, MaterialSpec] = {
    WOOD: MaterialSpec(WOOD, (0.196, 0.126, 0.072), roughness=0.85),
    IRON: MaterialSpec(IRON, (0.112, 0.116, 0.122), roughness=0.55, metallic=0.90),
    RUST: MaterialSpec(RUST, (0.201, 0.092, 0.046), roughness=0.92, metallic=0.30),
    STONE: MaterialSpec(STONE, (0.302, 0.292, 0.271), roughness=0.90),
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
    # ── Set dressing ───────────────────────────────────────────────────────
    # All that is left. The §08 loot block, the Interactable block and the
    # 궤짝 were deleted with the systems that placed them — see the tombstone
    # above the builders.
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
