#!/usr/bin/env python3
"""Generates 주자 — the one body all twenty racers wear.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_runner.py

Outputs ``Assets/Models/Player/Runner.fbx``. Three switches, after a bare ``--``:
``--glb`` writes a GLB beside the FBX for eyeballing without opening Unity, ``--out
PATH`` sends the FBX somewhere that is not the Unity project, and ``--no-export``
builds and runs every check and writes nothing.

WHY THIS ASSET EXISTS AND WHY IT IS ANONYMOUS
---------------------------------------------
DESCENT-PIVOT §5 is one line long and settles the whole brief: **「모델 하나. 20명이
똑같이 생긴다.」** §04's five roles are gone, so role colour has nothing left to encode,
and the document argues the sameness is worth more than the distinction was —
**「똑같이 생긴 스무 명이 어두운 복도에서 같은 방향으로 움직이는 것이 각자 다르게 생긴
스무 명보다 무섭다. 누가 사람이고 누가 아닌지도 한순간 헷갈린다」** — which is also the
trap §10's 그늘 is built to spring.

So this figure is authored for **silhouette only**. §03 keeps the maze dark; at ten
metres down a B-storey corridor a racer is an outline crossing a doorway and nothing
else. That is why it carries no face, no hands with fingers, no costume, and one flat
near-white material (0.86, 0.87, 0.89) at roughness 0.88: a pale matte body is the
shape that survives being lit by one moving flashlight from an unpredictable angle,
and every detail finer than the outline is budget spent where nobody is looking.

WHY PRIMITIVES AND NOT METABALLS — seven passes, one failure, seven times
------------------------------------------------------------------------
This figure was attempted with metaballs first, and every attempt failed the same way,
which is the only reason worth writing down.

A metaball does not blend with the ball you want it to blend with. It blends with
**every ball in its family**, by distance, all the time. So an arm placed close enough
to the torso for the shoulder to fuse also drags the whole upper arm into the torso's
field, and the result is not an arm — it is a hunched slab of trapezius running from the
ear to the elbow. Push the arm outward until the upper arm survives as its own limb and
the shoulder stops fusing, so the figure reads T-shaped with a gap of air under each
armpit. Both are wrong, and they are wrong in opposite directions from the *same*
parameter, so between them there is no separation distance that is right. Seven passes
were spent looking for one. There is not one.

The influence radius does not rescue it either: shrinking it to keep the arm free
shrinks the shoulder blend that was the only reason to use metaballs, and growing it to
recover the shoulder swallows the arm again. The parameter that fixes one end breaks the
other end at exactly the same rate.

Primitives do not have this property. **A primitive goes where it is put and stops at
its own surface**, and the blending is moved to a step that happens *after* placement
and cannot reach across the figure: a voxel remesh, which unions overlapping solids and
knows nothing about how near an arm is to a chest. The shoulder is a sphere that
deliberately overlaps the torso by 44 mm and fuses; the upper arm is 228 mm out and does
not, because nothing in a level-set union propagates by proximity. That is the whole
argument, and it is why the geometry table below is metres rather than field strengths.

THE TWO THINGS THIS SCRIPT ASSERTS, AND WHAT EACH ONE COST
-----------------------------------------------------------
**1. One shell.** ``verify_one_shell`` walks the welded mesh with bmesh and counts
connected components. Parts must **overlap**, not touch. The failure this is written
against was a 6 cm gap between the top of the torso and the bottom of the neck: both
primitives were correct, both were where the table said, and the weld had nothing to
weld — the head and neck came out of the remesh as a separate closed shell floating
above the shoulders, which in Unity is a head that does not move with the body. Nothing
in a mesh export complains about that. A component count does, and
``verify_parts_interpenetrate`` catches it 20 s earlier and names the offending part.

**2. 1.75 m.** ``AssetImportPolicy.PlayerHeightMetres`` is 1.750, the CharacterController
capsule is 1.75 m (``ViewMotionTuning.RigHeightMetres``), and §12's corridor section is
sized against a person of that height. A model shorter than its own capsule floats,
because the capsule is what touches the floor. The height is therefore not authored —
the figure is built at whatever height the primitives and the smoothing produce, then
scaled to exactly 1.75 and dropped until its lowest vertex is z = 0. Both are measured
back off the mesh afterwards and both are printed.

PROVENANCE — this file was written against the live model, and checked against it
------------------------------------------------------------------------------------
Until this script existed the figure was a mesh in one interactive Blender session and
nothing else, which is one crash from being an asset nobody can rebuild. The
regeneration was measured against that session before it was trusted:

======================  ==========================  ==========================
measure                 live session                this script
======================  ==========================  ==========================
height                  1.750000 m                  1.750000 m
width (hand to hand)    0.778444 m                  0.778 m
depth (heel to toe)     0.392557 m                  0.394 m
material                ``Runner_Plaster.011``      ``Runner_Plaster``
mesh                    1765 verts / 3307 faces     1748 verts / 3496 tris
======================  ==========================  ==========================

Under 2 mm on every axis. The vertex counts differ because the interactive decimation
left n-gons where this one triangulates, and the material name differs because ``.011``
is Blender's duplicate-name suffix from a session that had built the figure twelve times
— it is a name Unity would have imported and kept, and a generated asset does not have
one. Both meshes come out genus 1 rather than genus 0, which is not a defect: the hands
hang at the hips and fuse to the thighs (``Hand``/``Leg`` overlap 48.6 mm), closing one
loop of material between arm and body. It is still a single shell, which is what the
check is about.
"""

from __future__ import annotations

import math
import os
import sys
import traceback
from dataclasses import dataclass

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import blendkit  # noqa: E402
from blendkit import MaterialSpec  # noqa: E402

# ── The contract with Unity ─────────────────────────────────────────────────

TARGET_HEIGHT = 1.750
"""Metres, exact. ``AssetImportPolicy.PlayerHeightMetres`` and the CharacterController
capsule (``ViewMotionTuning.RigHeightMetres``) are both this number, and the capsule is
what stands on the floor — a body shorter than it hovers, a body taller than it wades."""

HEIGHT_TOLERANCE = 0.0005
"""0.5 mm. The scale is solved arithmetically so the only error left is float rounding;
anything larger means the measurement and the scale disagree, which is a bug, not slack."""

MESH_NAME = "Runner"
"""The object name, and therefore the FBX Model node Unity binds a prefab to."""

MATERIAL_NAME = "Runner_Plaster"
"""One slot, one name. The live-session export carried ``Runner_Plaster.011`` — Blender's
duplicate-name suffix, which becomes a real material name in Unity and a second material
in the project the first time anyone edits it. A generated asset has no ``.011``."""

BASE_COLOUR = (0.86, 0.87, 0.89)
"""Slightly cool near-white. §03 is dark and §05's flashlight is the only reliable light,
so the body has to give back most of what little lands on it or it is not a silhouette,
it is a hole. Faintly blue rather than neutral so it separates from the warm concrete of
the map kit under the same beam."""

ROUGHNESS = 0.88
"""Matte to the point of chalk. A glossy body would throw a specular hotspot that tracks
the viewer's own flashlight, and that hotspot reads as a *light source in the corridor* —
the exact misread §10's 그늘 already exploits. Plaster does not do that."""

# ── The weld pipeline ───────────────────────────────────────────────────────

VOXEL_SIZE = 0.012
"""12 mm, and this number is doing two jobs. It has to be **smaller than the thinnest
overlap in the table** or the union drops a join — the tightest is the wrist, where the
arm's 62 mm end-ball sits inside the hand ellipsoid, and 12 mm resolves it with room
over. It also has to be big enough that a 1.4 m figure stays a few tens of thousands of
faces, because everything downstream of it is per-face work."""

SMOOTH_FACTOR = 1.0
SMOOTH_ITERATIONS = 8
"""Full-strength Laplacian, eight passes. The remesh output is a voxel staircase — every
surface is made of 12 mm axis-aligned steps, and the steps catch the flashlight as a
grid. Smoothing is what turns the union back into a body. It is topology-preserving, so
it cannot break the single shell; what it does cost is volume, which is why the height is
measured *after* this and not before."""

DECIMATE_RATIO = 0.10
"""Keep a tenth. The remesh spends its faces uniformly, which is the wrong distribution
for a shape that is mostly smooth: after eight smoothing passes nine faces in ten are
describing a curve their neighbours already describe. Collapse mode, so the budget goes
to the silhouette — §05's answer to a dark game is that the outline is the only thing a
player ever resolves, and DESCENT-PIVOT §5 doubles down on it."""

SAMPLE_CHORD = VOXEL_SIZE * 0.5
"""Target edge length on the input primitives: half a voxel. Any coarser and the remesh
digitises the *faceting* of the source sphere instead of the sphere, and a 15 cm head
arrives with a visible tessellation ridge that eight smoothing passes then preserve."""

PREFLIGHT_SAMPLES = 512
"""Surface points per part for the interpenetration graph. Enough that a real overlap
cannot slip between samples at these radii, few enough that the whole graph is instant."""

CORRIDOR_CLEAR_WIDTH = 2.20
"""§12's corridor clear width, in metres — ``gen_mapkit.CLEAR_W``. Restated rather than
imported: this generator has no other reason to pull in a 120 KB module, and the number
is used here only as a **cross-check**, never as a source of truth. What it answers is
the one question this figure's width decides: DESCENT-PIVOT §2's last gate is a single
opening every racer on the storey has to pass, so how many bodies fit abreast is a race
mechanic. If the kit ever narrows and this stops printing 2, the figure is the problem."""


# ── The figure, in metres ───────────────────────────────────────────────────
#
# Z-up, +X to the figure's left, +Y forward, origin under the feet after the drop.
# Every number below is a placement, not a proportion — see the module docstring on
# metaballs for why placements are the thing this file is allowed to have.


@dataclass(frozen=True)
class Ellipsoid:
    """One triaxial blob: torso, belly, neck, head, shoulder, hand, foot.

    Carries its own ``contains``/``depth`` because the preflight overlap graph tests the
    *solids*, not the tessellation — a check that depended on where the vertices happened
    to land would pass or fail with the sphere resolution.
    """

    name: str
    centre: tuple[float, float, float]
    radii: tuple[float, float, float]

    def quadric(self, p: Vector) -> float:
        """<1 inside, 1 on the surface, >1 outside. The ellipsoid's own metric."""
        cx, cy, cz = self.centre
        rx, ry, rz = self.radii
        return ((p.x - cx) / rx) ** 2 + ((p.y - cy) / ry) ** 2 + ((p.z - cz) / rz) ** 2

    def contains(self, p: Vector) -> bool:
        """Strictly inside. Strictly, because a point *on* the surface is a touch, and a
        touch is the failure this whole preflight exists to reject."""
        return self.quadric(p) < 1.0

    def depth(self, p: Vector) -> float:
        """Metres from ``p`` to the surface, measured radially from the centre.

        Exact along that ray and an upper bound off it, which is the right bias: the
        preflight is looking for joins that are *too shallow*, and an optimistic depth
        that still passes the minimum is a join that is genuinely deep enough."""
        q = self.quadric(p)
        if q >= 1.0:
            return 0.0
        if q <= 1e-12:
            return min(self.radii)
        reach = (p - Vector(self.centre)).length
        return reach * (1.0 / math.sqrt(q) - 1.0)

    def aabb(self) -> tuple[Vector, Vector]:
        """World bounds. Only used to skip pairs that cannot possibly overlap, which is
        most of the 91 pairs and all of the cost."""
        c, r = Vector(self.centre), Vector(self.radii)
        return c - r, c + r

    def samples(self) -> list[Vector]:
        """Surface points for the overlap graph."""
        return [Vector(self.centre) + Vector((u.x * self.radii[0],
                                              u.y * self.radii[1],
                                              u.z * self.radii[2]))
                for u in _fibonacci_sphere(PREFLIGHT_SAMPLES)]

    def build(self) -> list[bpy.types.Object]:
        """The Blender objects this part contributes to the join."""
        return [_uv_ellipsoid(self.name, self.centre, self.radii)]


@dataclass(frozen=True)
class Shaft:
    """A tapered limb segment with a ball of the matching radius at each end.

    The balls are not decoration. A bare cone ends in a flat disc, and a flat disc
    against the next part's surface makes a **tangential** contact — the two solids share
    a plane and almost no volume, which is the contact the remesh is least able to weld.
    A ball ends the segment in a solid the next part can be given a real interior overlap
    with, and at a joint it is also the only shape that stays round when the limb bends.
    Modelled as one part, because the three primitives are concentric by construction and
    the overlap graph should reason about a limb, not about its pieces.
    """

    name: str
    x: float
    y: float
    z_top: float
    r_top: float
    z_bottom: float
    r_bottom: float

    def radius_at(self, z: float) -> float:
        """The taper, linear between the two authored ends."""
        t = (z - self.z_bottom) / (self.z_top - self.z_bottom)
        return self.r_bottom + (self.r_top - self.r_bottom) * t

    def _ends(self) -> tuple[Ellipsoid, Ellipsoid]:
        top = Ellipsoid(f"{self.name}_BallTop", (self.x, self.y, self.z_top),
                        (self.r_top,) * 3)
        bottom = Ellipsoid(f"{self.name}_BallBottom", (self.x, self.y, self.z_bottom),
                           (self.r_bottom,) * 3)
        return top, bottom

    def contains(self, p: Vector) -> bool:
        """Inside the cone or inside either end ball — the segment is their union."""
        top, bottom = self._ends()
        if top.contains(p) or bottom.contains(p):
            return True
        if not (self.z_bottom <= p.z <= self.z_top):
            return False
        return math.hypot(p.x - self.x, p.y - self.y) < self.radius_at(p.z)

    def depth(self, p: Vector) -> float:
        """Metres to the nearest surface of the union, taking the deepest of the three."""
        top, bottom = self._ends()
        best = max(top.depth(p), bottom.depth(p))
        if self.z_bottom <= p.z <= self.z_top:
            radial = self.radius_at(p.z) - math.hypot(p.x - self.x, p.y - self.y)
            if radial > 0.0:
                best = max(best, min(radial, p.z - self.z_bottom, self.z_top - p.z))
        return best

    def aabb(self) -> tuple[Vector, Vector]:
        """World bounds, widened by the end balls — they stick out past the cone."""
        r = max(self.r_top, self.r_bottom)
        return (Vector((self.x - r, self.y - r, self.z_bottom - self.r_bottom)),
                Vector((self.x + r, self.y + r, self.z_top + self.r_top)))

    def samples(self) -> list[Vector]:
        """Surface points: both end balls, plus rings up the lateral cone."""
        pts: list[Vector] = []
        top, bottom = self._ends()
        for end in (top, bottom):
            pts += [Vector(end.centre) + Vector((u.x * end.radii[0],
                                                 u.y * end.radii[1],
                                                 u.z * end.radii[2]))
                    for u in _fibonacci_sphere(PREFLIGHT_SAMPLES // 4)]
        rings = 24
        per_ring = max(8, PREFLIGHT_SAMPLES // (2 * rings))
        for i in range(rings + 1):
            z = self.z_bottom + (self.z_top - self.z_bottom) * i / rings
            r = self.radius_at(z)
            for j in range(per_ring):
                a = 2.0 * math.pi * j / per_ring
                pts.append(Vector((self.x + r * math.cos(a), self.y + r * math.sin(a), z)))
        return pts

    def build(self) -> list[bpy.types.Object]:
        """Three objects — cone plus both balls — that the join fuses into one limb."""
        top, bottom = self._ends()
        depth = self.z_top - self.z_bottom
        cone = _cone(self.name, self.x, self.y,
                     mid_z=0.5 * (self.z_top + self.z_bottom), depth=depth,
                     r_bottom=self.r_bottom, r_top=self.r_top)
        return [cone] + top.build() + bottom.build()


Part = Ellipsoid | Shaft


def build_parts() -> list[Part]:
    """The figure's placement table.

    The spine is four blobs rather than one because a single ellipsoid tall enough to
    reach from the hips to the collarbone is also, at that aspect ratio, a bin liner:
    ``Belly`` is wider than ``Torso`` at the same height, which is what puts a waist in
    the outline, and DESCENT-PIVOT §5 says the outline is all this model has.

    Every part overlaps at least one other by design; ``verify_parts_interpenetrate``
    proves that rather than trusting it.
    """
    parts: list[Part] = [
        Ellipsoid("Torso", (0.0, 0.0, 0.86), (0.190, 0.140, 0.300)),
        Ellipsoid("Belly", (0.0, 0.0, 0.68), (0.175, 0.135, 0.180)),
        Ellipsoid("Neck", (0.0, 0.0, 1.17), (0.072, 0.072, 0.080)),
        Ellipsoid("Head", (0.0, 0.0, 1.30), (0.150, 0.145, 0.150)),
    ]

    # Mirrored, not modelled twice. s = -1 is the figure's right.
    for s in (-1.0, +1.0):
        tag = "L" if s > 0 else "R"
        parts += [
            # 8.5 cm ball, centred 20.5 cm out — far enough to be a shoulder in the
            # outline, near enough that it eats 44 mm into the torso and welds.
            Ellipsoid(f"Shoulder_{tag}", (s * 0.205, 0.0, 1.02), (0.085,) * 3),
            # Straight down from the shoulder to just above the wrist. A-pose, not
            # T-pose: the arms hang, so the silhouette is a person walking a corridor
            # rather than a mannequin, and the elbows stay inside §12's 2.20 m clear
            # width even with two racers abreast.
            Shaft(f"Arm_{tag}", s * 0.228, 0.0,
                  z_top=1.02, r_top=0.076, z_bottom=0.66, r_bottom=0.062),
            # A mitten, deliberately. Fingers are ~15 mm features seen at ten metres in
            # the dark; they cost geometry and survive as noise.
            Ellipsoid(f"Hand_{tag}", (s * 0.232, 0.0, 0.62), (0.082, 0.078, 0.070)),
            # Legs at 10 cm from centreline — a 20 cm stance, narrow enough that the two
            # thighs share the belly's volume and the pelvis welds as one mass.
            Shaft(f"Leg_{tag}", s * 0.100, 0.0,
                  z_top=0.66, r_top=0.100, z_bottom=0.17, r_bottom=0.084),
            # Pushed 4.8 cm forward, because a foot is the one part of a person that is
            # not symmetric front-to-back and the toes are what tell a viewer at a
            # glance which way a distant racer is facing.
            Ellipsoid(f"Foot_{tag}", (s * 0.102, 0.048, 0.11), (0.094, 0.130, 0.066)),
        ]
    return parts


# ── Primitive construction ──────────────────────────────────────────────────


def _fibonacci_sphere(count: int) -> list[Vector]:
    """Near-uniform points on the unit sphere. No polar clustering, unlike a UV grid,
    so the preflight samples a shoulder as densely as it samples an equator."""
    pts = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for i in range(count):
        z = 1.0 - 2.0 * (i + 0.5) / count
        r = math.sqrt(max(0.0, 1.0 - z * z))
        a = golden * i
        pts.append(Vector((r * math.cos(a), r * math.sin(a), z)))
    return pts


def _rings_for(radius: float) -> tuple[int, int]:
    """Segments and rings that put roughly ``SAMPLE_CHORD`` between neighbouring verts.

    Capped at 128 segments: past that the input is finer than the voxel grid can record
    and the extra vertices are paid for and then discarded by the remesh."""
    segments = int(math.ceil(2.0 * math.pi * radius / SAMPLE_CHORD))
    segments = max(24, min(128, segments))
    return segments, max(12, segments // 2)


def _bake_transform(obj: bpy.types.Object) -> None:
    """Pushes location/rotation/scale into the mesh data.

    Every primitive is created at its world position, so leaving the offset on the object
    means ``join`` inherits the active object's transform and every other part arrives
    displaced by it. Baking first makes the join a pure vertex concatenation."""
    blendkit.apply_transforms(obj, location=True, rotation=True, scale=True)


def _uv_ellipsoid(name: str, centre: tuple[float, float, float],
                  radii: tuple[float, float, float]) -> bpy.types.Object:
    segments, rings = _rings_for(max(radii))
    bpy.ops.mesh.primitive_uv_sphere_add(radius=1.0, location=centre,
                                         segments=segments, ring_count=rings)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = Vector(radii)
    _bake_transform(obj)
    return obj


def _cone(name: str, x: float, y: float, mid_z: float, depth: float,
          r_bottom: float, r_top: float) -> bpy.types.Object:
    """A capped truncated cone. The caps matter: an open tube has no interior for the
    level set to fill, and the remesh turns it into a 12 mm-thick pipe wall."""
    segments, _ = _rings_for(max(r_bottom, r_top))
    bpy.ops.mesh.primitive_cone_add(radius1=r_bottom, radius2=r_top, depth=depth,
                                    location=(x, y, mid_z), vertices=segments,
                                    end_fill_type="NGON")
    obj = bpy.context.active_object
    obj.name = name
    _bake_transform(obj)
    return obj


# ── The weld ────────────────────────────────────────────────────────────────


def _apply_modifier(obj: bpy.types.Object, mod) -> None:
    """Bakes one modifier into the mesh via the evaluated depsgraph.

    ``bpy.ops.object.modifier_apply`` needs a context this script has no reason to keep
    correct in ``--background``; evaluating the object and stealing its mesh does not."""
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    baked = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    old = obj.data
    obj.data = baked
    obj.modifiers.remove(mod)
    bpy.data.meshes.remove(old)


def _tris(obj: bpy.types.Object) -> int:
    return sum(max(0, len(p.vertices) - 2) for p in obj.data.polygons)


def _stage(label: str, obj: bpy.types.Object) -> None:
    print(f"MESH_STAGE {label:<16} verts={len(obj.data.vertices):>7} tris={_tris(obj):>7}")


def weld(objects: list[bpy.types.Object]) -> bpy.types.Object:
    """Union of the primitives, in the order the pipeline has to run in.

    join → remesh → smooth → decimate. The remesh is what actually welds; the join only
    puts the parts in one mesh so the level set sees them as one field. Reordering any
    two of these produces a different asset: decimating before smoothing, for instance,
    removes the vertices the smoothing needed to average across and leaves the voxel
    staircase in place at a tenth of the cost.
    """
    body = blendkit.join(objects, MESH_NAME)
    _stage("raw", body)

    remesh = body.modifiers.new("Weld", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = VOXEL_SIZE
    remesh.adaptivity = 0.0
    _apply_modifier(body, remesh)
    _stage(f"remesh_{VOXEL_SIZE}", body)

    smooth = body.modifiers.new("Relax", "SMOOTH")
    smooth.factor = SMOOTH_FACTOR
    smooth.iterations = SMOOTH_ITERATIONS
    _apply_modifier(body, smooth)
    _stage(f"smooth_{SMOOTH_FACTOR}x{SMOOTH_ITERATIONS}", body)

    decimate = body.modifiers.new("Budget", "DECIMATE")
    decimate.decimate_type = "COLLAPSE"
    decimate.ratio = DECIMATE_RATIO
    decimate.use_collapse_triangulate = True
    _apply_modifier(body, decimate)
    _stage(f"decimate_{DECIMATE_RATIO}", body)

    # 180° so nothing is split. The figure has no hard edge anywhere on it — after eight
    # smoothing passes every crease is a curve, and an auto-smooth angle low enough to
    # find one would be finding decimation noise.
    blendkit.shade_smooth(body, angle_degrees=180.0)
    return body


def world_bounds(obj: bpy.types.Object) -> tuple[Vector, Vector]:
    """Bounds from the vertices, not from ``obj.bound_box``.

    ``bound_box`` is cached against the mesh and is stale immediately after the vertex
    edits ``fit_height_and_ground`` makes — which is exactly when the height is measured,
    so trusting it would mean checking the size the model used to be.
    """
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for v in obj.data.vertices:
        w = obj.matrix_world @ v.co
        for i in range(3):
            lo[i] = min(lo[i], w[i])
            hi[i] = max(hi[i], w[i])
    return lo, hi


def fit_height_and_ground(obj: bpy.types.Object, target: float) -> float:
    """Scales to ``target`` metres tall, then drops the lowest vertex onto z = 0.

    Both operations are written into the vertices rather than onto the object transform.
    A non-unit object scale exports as a scaled node, and then Unity's collider and the
    NavMesh bake disagree with the renderer about how big the thing is — the same reason
    ``blendkit.add_box`` bakes its size. Returns the scale factor used.
    """
    lo, hi = world_bounds(obj)
    height = hi.z - lo.z
    if height <= 1e-6:
        blendkit.fail("the welded body has no height — the remesh produced nothing.")

    k = target / height
    for v in obj.data.vertices:
        v.co *= k

    lo, _ = world_bounds(obj)
    for v in obj.data.vertices:
        v.co.z -= lo.z

    obj.data.update()
    return k


# ── The two checks that cost hours ──────────────────────────────────────────


def verify_parts_interpenetrate(parts: list[Part]) -> None:
    """Preflight: the placement table must describe one connected solid.

    This runs on the **table**, before a single vertex is generated, and it is the check
    that would have caught the failure the module docstring records — a 6 cm gap between
    the torso top and the neck bottom. Both primitives were individually correct, so
    nothing complained until the remesh had already run and produced a head floating in
    its own shell.

    Two parts are linked when one's surface samples land strictly *inside* the other. A
    touch does not count and must not: two spheres that meet at a point share a point,
    and a level set built on a 12 mm grid will not find it.
    """
    n = len(parts)
    boxes = [p.aabb() for p in parts]
    samples = [p.samples() for p in parts]

    links: dict[int, list[tuple[int, float]]] = {i: [] for i in range(n)}
    for i in range(n):
        for j in range(i + 1, n):
            lo_i, hi_i = boxes[i]
            lo_j, hi_j = boxes[j]
            if any(hi_i[k] < lo_j[k] or hi_j[k] < lo_i[k] for k in range(3)):
                continue  # bounding boxes miss — no solid overlap is possible
            depth = 0.0
            for p in samples[i]:
                if parts[j].contains(p):
                    depth = max(depth, parts[j].depth(p))
            for p in samples[j]:
                if parts[i].contains(p):
                    depth = max(depth, parts[i].depth(p))
            if depth > 0.0:
                links[i].append((j, depth))
                links[j].append((i, depth))

    for i, part in enumerate(parts):
        neighbours = sorted(links[i], key=lambda t: -t[1])
        print(f"OVERLAP {part.name:<14} " + (
            " ".join(f"{parts[j].name}={d * 1000.0:.1f}mm" for j, d in neighbours)
            or "NONE"))

    # Connectivity, and then the number that actually matters. The thinnest overlap
    # *anywhere* is not it: Head/Torso graze each other by 9 mm because a 15 cm head
    # centred at 1.30 dips a hair below a torso that tops out at 1.16, and welding that
    # graze is nobody's plan — the head is attached through the neck at 100 mm. What has
    # to be thick is every join the figure's connectivity **depends on**.
    #
    # So: build the maximum-bottleneck spanning tree (Prim's, taking the deepest edge
    # each time) and report its weakest link. That is the coarsest voxel at which this
    # table still unions into one solid, and it is the only overlap number worth
    # comparing against VOXEL_SIZE.
    reached = {0}
    tree: list[tuple[float, int, int]] = []
    while len(reached) < n:
        best: tuple[float, int, int] | None = None
        for i in reached:
            for j, d in links[i]:
                if j not in reached and (best is None or d > best[0]):
                    best = (d, i, j)
        if best is None:
            break
        reached.add(best[2])
        tree.append(best)

    if len(reached) != n:
        loose = [parts[i].name for i in range(n) if i not in reached]
        detail = ", ".join(
            f"{parts[i].name} (overlaps nothing)" if not links[i]
            else f"{parts[i].name} (only reaches {parts[links[i][0][0]].name})"
            for i in range(n) if i not in reached)
        blendkit.fail(
            f"the placement table is not one connected solid — {len(loose)} part(s) are "
            f"not reachable from Torso: {detail}. Parts must OVERLAP, not touch: a "
            "primitive stops at its own surface, so a gap between two of them leaves the "
            "weld nothing to weld and the remesh returns two shells. Move the part in, "
            "or widen it.")

    bottleneck = min(tree)
    print(f"WELD_PREFLIGHT parts={n} links={sum(len(v) for v in links.values()) // 2} "
          f"connected=yes bottleneck={bottleneck[0] * 1000.0:.1f}mm "
          f"({parts[bottleneck[1]].name}/{parts[bottleneck[2]].name}) "
          f"voxel={VOXEL_SIZE * 1000.0:.0f}mm "
          f"margin={bottleneck[0] / VOXEL_SIZE:.1f}x")

    if bottleneck[0] <= VOXEL_SIZE:
        blendkit.fail(
            f"the figure's connectivity bottleneck is {bottleneck[0] * 1000.0:.1f} mm "
            f"({parts[bottleneck[1]].name}/{parts[bottleneck[2]].name}), at or under the "
            f"{VOXEL_SIZE * 1000.0:.0f} mm voxel. The union may resolve that join and may "
            "not, depending on where the grid happens to land — an intermittent extra "
            "shell is worse than a reliable one. Deepen the overlap or shrink the voxel.")


def verify_one_shell(obj: bpy.types.Object) -> None:
    """The welded mesh must be exactly one connected component.

    Walks the mesh with bmesh rather than trusting the pipeline, because every way this
    goes wrong is silent: the export succeeds, the file opens, the model looks right in a
    thumbnail, and in the game a head hangs in the air above a running body.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    seen: set[int] = set()
    shells: list[int] = []
    for vert in bm.verts:
        if vert.index in seen:
            continue
        seen.add(vert.index)
        stack = [vert]
        size = 0
        while stack:
            v = stack.pop()
            size += 1
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        shells.append(size)
    shells.sort(reverse=True)

    boundary = sum(1 for e in bm.edges if len(e.link_faces) < 2)
    nonmanifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.free()

    print(f"SHELL_COUNT shells={len(shells)} verts={','.join(str(s) for s in shells[:6])}"
          f"{'...' if len(shells) > 6 else ''} boundary_edges={boundary} "
          f"nonmanifold_edges={nonmanifold}")

    if len(shells) != 1:
        blendkit.fail(
            f"the welded body is {len(shells)} shells, not 1 "
            f"(vertex counts {shells[:8]}). Parts must OVERLAP, not merely touch — a "
            "primitive stops at its own surface, so a gap of a few centimetres between "
            "two of them leaves a limb or a head as its own closed surface and the weld "
            "had nothing to weld. Deepen the overlap in build_parts().")

    if boundary:
        blendkit.fail(
            f"{boundary} boundary edges — the body is not watertight. A voxel remesh "
            "returns a closed surface, so holes mean a primitive was open before the "
            "join (a cone exported without caps is the usual cause).")


def verify_height(obj: bpy.types.Object) -> tuple[float, float, float]:
    """1.750 m tall, feet on z = 0. Returns the measured size."""
    lo, hi = world_bounds(obj)
    size = (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z)

    print(f"RUNNER_HEIGHT height={size[2]:.4f}m target={TARGET_HEIGHT:.4f}m "
          f"error={(size[2] - TARGET_HEIGHT) * 1000.0:+.3f}mm "
          f"floor_z={lo.z * 1000.0:+.3f}mm")

    if abs(size[2] - TARGET_HEIGHT) > HEIGHT_TOLERANCE:
        blendkit.fail(
            f"the body is {size[2]:.4f} m, not {TARGET_HEIGHT:.3f} m. The "
            "CharacterController capsule is 1.75 m and the capsule is what touches the "
            "floor — a shorter model floats above it, a taller one sinks into it.")

    if abs(lo.z) > HEIGHT_TOLERANCE:
        blendkit.fail(
            f"the lowest vertex is at z = {lo.z * 1000.0:+.2f} mm, not 0. Unity places "
            "this model at the capsule's base, so any offset here is a body standing in "
            "or above the floor everywhere it is ever spawned.")

    return size


def report_breadth(obj: bpy.types.Object, size: tuple[float, float, float]) -> None:
    """Says *where* the figure is at its widest, and whether two of them fit abreast.

    The bounding box alone is misleading here: the widest cross-section is not the
    shoulders, it is the hands, which hang beside the hips. Printing the height the width
    occurs at is what stops a reader concluding the shoulders are 78 cm across — and the
    shoulder band is printed beside it so the two cannot be confused.
    """
    widest = max(((abs((obj.matrix_world @ v.co).x), (obj.matrix_world @ v.co).z)
                  for v in obj.data.vertices), key=lambda t: t[0])
    shoulder_z = widest[1]
    # The shoulder band, taken at the height of the widest point *above* mid-chest, which
    # on this figure is the deltoid rather than the hand.
    upper = [(abs((obj.matrix_world @ v.co).x), (obj.matrix_world @ v.co).z)
             for v in obj.data.vertices
             if (obj.matrix_world @ v.co).z > 0.55 * size[2]]
    shoulder = max(upper, key=lambda t: t[0]) if upper else widest

    abreast = int(CORRIDOR_CLEAR_WIDTH // size[0])
    print(f"RUNNER_BREADTH widest={size[0]:.3f}m at z={shoulder_z:.3f}m "
          f"shoulder_band={shoulder[0] * 2.0:.3f}m at z={shoulder[1]:.3f}m "
          f"depth={size[1]:.3f}m")
    print(f"CORRIDOR_FIT span={size[0]:.3f}m corridor_clear={CORRIDOR_CLEAR_WIDTH:.2f}m "
          f"abreast={abreast} (§12)")
    if abreast < 2:
        blendkit.fail(
            f"only {abreast} of this body fits across §12's {CORRIDOR_CLEAR_WIDTH:.2f} m "
            f"corridor at {size[0]:.3f} m wide. DESCENT-PIVOT §2 funnels a whole storey "
            "through one gate — a figure that cannot be passed turns every corridor into "
            "a queue and the race into a traffic jam.")


# ── Export ──────────────────────────────────────────────────────────────────


def export_fbx(obj: bpy.types.Object, path: str) -> str:
    """Writes the FBX with the settings Unity needs, which are not blendkit's.

    ``blendkit.export_fbx`` uses ``FBX_SCALE_NONE``, which parks the unit conversion on
    the root node as ``Lcl Scaling 100`` — right for the rigged characters, whose bone
    lengths Unity's Humanoid mapper reads off that node. This mesh has no rig, and
    ``FBX_SCALE_UNITS`` puts the conversion in the file's ``UnitScaleFactor`` instead, so
    the exported node scale is a clean 1 and nothing downstream has to cancel a ×100 on a
    transform. Either is correct **only** with the importer's Convert Units left on;
    ``AssetImportPolicy.RequiredUseFileScale`` is the thing that keeps it on.

    ``bake_space_transform=True`` finishes the Z-up → Y-up conversion in the *vertices*
    rather than leaving a −90° X rotation on the object, which is what makes the model
    arrive in Unity with an identity transform instead of one every prefab has to undo.
    """
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        bake_space_transform=True,
        use_tspace=False,
        bake_anim=False,
        path_mode="COPY",
        embed_textures=False,
    )
    return path


def verify_roundtrip(path: str) -> None:
    """Reads the FBX back and re-measures it.

    The height check above measures the *scene*. This measures the **file**, which is the
    only thing Unity ever sees, and it is the one place a unit-scale mistake in the
    export settings can still be caught — a body written at 175 m or 0.0175 m passes
    every check in this script except this one.
    """
    before = {o.name for o in bpy.context.scene.objects}
    try:
        bpy.ops.import_scene.fbx(filepath=path, global_scale=1.0)
    except RuntimeError as exc:
        blendkit.fail(f"the FBX just written cannot be read back: {exc}")

    fresh = [o for o in bpy.context.scene.objects
             if o.name not in before and o.type == "MESH"]
    if not fresh:
        blendkit.fail("the FBX just written contains no mesh.")

    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for o in fresh:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])

    height = hi.z - lo.z
    print(f"FBX_ROUNDTRIP meshes={len(fresh)} height={height:.4f}m "
          f"error={(height - TARGET_HEIGHT) * 1000.0:+.3f}mm "
          f"scale_mode=FBX_SCALE_UNITS")

    if abs(height - TARGET_HEIGHT) > 0.002:
        blendkit.fail(
            f"the exported file reads back at {height:.4f} m, not {TARGET_HEIGHT:.3f}. "
            "The scene was the right size, so this is the export's unit handling — check "
            "apply_scale_options / apply_unit_scale before touching the geometry.")

    for o in fresh:
        bpy.data.objects.remove(o, do_unlink=True)


# ── Entry point ─────────────────────────────────────────────────────────────


def main() -> None:
    """Builds the figure, proves the two things that have to be true, and exports it.

    The order is not arrangeable. The preflight runs before any geometry so a bad
    placement table costs a second instead of a remesh; the height is fitted after the
    smoothing because the smoothing is what changes it; and both verifications run before
    the export, so a failure never leaves a wrong ``Runner.fbx`` in the Unity project for
    somebody else's scene to pick up.
    """
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = blendkit.out_path("Player", "Runner.fbx")
    if "--out" in argv:
        i = argv.index("--out") + 1
        if i >= len(argv):
            blendkit.fail("--out needs a path after it.")
        out = os.path.abspath(argv[i])
        os.makedirs(os.path.dirname(out), exist_ok=True)

    blendkit.reset_scene()

    parts = build_parts()
    print(f"RUNNER_PARTS count={len(parts)} "
          f"names={','.join(p.name for p in parts)}")

    # Before any geometry: the table itself has to describe one solid. Cheap, and it
    # fails by name instead of by symptom.
    verify_parts_interpenetrate(parts)

    primitives: list[bpy.types.Object] = []
    for part in parts:
        primitives += part.build()
    print(f"RUNNER_PRIMITIVES count={len(primitives)} "
          f"(ellipsoids and capped cones; {SAMPLE_CHORD * 1000.0:.0f}mm target chord)")

    body = weld(primitives)

    blendkit.assign_material(body, blendkit.make_material(MaterialSpec(
        name=MATERIAL_NAME, color=BASE_COLOUR, roughness=ROUGHNESS, metallic=0.0)))
    print(f"MATERIAL name={MATERIAL_NAME} "
          f"base=({BASE_COLOUR[0]:.2f},{BASE_COLOUR[1]:.2f},{BASE_COLOUR[2]:.2f}) "
          f"roughness={ROUGHNESS:.2f} metallic=0.00 slots={len(body.data.materials)}")

    scale = fit_height_and_ground(body, TARGET_HEIGHT)
    print(f"RUNNER_FIT scale={scale:.5f}x "
          f"(the smoothing shrinks the union, so the height is solved after it, "
          f"never authored into the table)")

    verify_one_shell(body)
    size = verify_height(body)
    print(f"RUNNER_SHAPE height={size[2]:.3f}m span={size[0]:.3f}m depth={size[1]:.3f}m "
          f"tris={_tris(body)} verts={len(body.data.vertices)}")
    report_breadth(body, size)

    if "--no-export" in argv:
        print("NO_EXPORT built and checked; nothing written")
        return

    export_fbx(body, out)
    report = blendkit.describe(out)
    blendkit.assert_asset(report, min_vertices=200, max_triangles=8000,
                          max_dimension=3.0)
    blendkit.print_report(report)

    if "--glb" in argv:
        glb = os.path.splitext(out)[0] + ".glb"
        blendkit.export_gltf(glb)
        print(f"PREVIEW {glb}")

    verify_roundtrip(out)

    print(f"FILES {out}")
    print(f"BYTES fbx={os.path.getsize(out)}")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001 — Blender exits 0 after a Python exception, so a
        # broken generator otherwise looks like a successful one. Everything routes
        # through blendkit.fail() to make a failure exit non-zero.
        traceback.print_exc()
        blendkit.fail("gen_runner.py raised:\n" + traceback.format_exc())
