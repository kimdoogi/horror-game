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

A SKELETON, AND WHAT RIGGING A STATIC PROP COST THE STATIC PROP
---------------------------------------------------------------
This figure shipped for a while as geometry with **bones=0 actions=0**, so
``PlayerAnimatorDriver`` — which reads the motor's ground speed and fires a Footfall
event off the phase of whatever clip is playing — was handed a mesh with no clips at all,
and twenty racers glided through the maze like furniture. It now carries a 13-bone rig and
the eight clips ``CLIP_NAMES`` lists. The count is argued where the bones are
defined; the short version is that a bone earns its place only if the surface it moves has
somewhere to bend, and after a voxel remesh and eight smoothing passes this body has no
crease anywhere on it.

The gait is not re-derived. ``gen_player_model`` already solved it — a two-bone IK that
places the stance keys by POSITION so the planted foot travels at a constant speed, and a
``FOOT_ROLL`` of 1.18 because the generator levels the ANKLE while it measures the SOLE —
and this file rebinds six of that module's globals to this figure's leg and calls the same
solver (``retarget_gait_solver``). What could not be carried over is the CADENCE: this leg
is 0.61 m against the player's 0.835, and step length is set by the leg, so at §06's shared
2.0 m/s the only free variable left is how often the figure steps. It is solved rather than
copied, against the pendulum scaling a leg actually obeys (``solve_cadence``).

**Three of the four defects the rig exposed were in the geometry, not in the rig**, and
that is the lesson this file is now built around. The figure was authored for a silhouette
that never moves, and a static silhouette hides everything a moving one shows:

* the toes pointed out of the figure's BACK — the table said "+Y forward" where the whole
  toolchain authors −Y, which ``export_fbx``'s ``axis_forward='-Z'`` turns into Unity's +Z;
* the two foot pads were 13.6 mm apart, inside the 12 mm voxel, so the remesh welded them
  and a stride pulled the weld into a bright sail from heel to toe;
* the thighs were one mass down to the KNEE, and a scissoring stride tore that into a
  hanging fringe still legible at ten metres;
* the arms were welded to the torso along their whole length, so §03's carry drew a sheet
  of skin from the forearm to the hip.

None of these is visible in an export, a triangle count, a shell count or a height check,
and two attempts to fix the last two in the skin WEIGHTS made every measurement worse (see
``SKIN_NOTE``). They are caught now by ``verify_limbs_hang_free``, ``verify_skin_stretch``
and ``verify_floor``, which measure the welded, posed, deformed mesh rather than the table
that produced it.

THE THINGS THIS SCRIPT ASSERTS, AND WHAT EACH ONE COST
-------------------------------------------------------
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

**3. bones > 0, actions > 0**, in the ``ASSET_REPORT`` line and asserted against the exact
counts, plus every take read back out of the FBX's own bytes and the rig read back out
of the written file. The pair *bones=0 actions=0* is the whole defect this revision
removes, and a generator that can regress to it silently is the generator that shipped it.

**4. The weld, under motion.** One shell is not enough once the body moves: the LIMBS have
to be joined where the skeleton says they are (``verify_limbs_hang_free``), no pose may put
a vertex through the floor (``verify_floor``), and no pose may pull an edge of the skin far
enough to draw a sheet across a gap (``verify_skin_stretch``).

PROVENANCE — this file was written against the live model, and checked against it
------------------------------------------------------------------------------------
Until this script existed the figure was a mesh in one interactive Blender session and
nothing else, which is one crash from being an asset nobody can rebuild. The
regeneration was measured against that session before it was trusted, and then the rig
moved three of those numbers on purpose:

======================  ====================  ====================  ==================
measure                 live session          this script, static   with the rig
======================  ====================  ====================  ==================
height                  1.750000 m            1.750000 m            1.750 m
width (hand to hand)    0.778444 m            0.778 m               **0.907 m**
depth (heel to toe)     0.392557 m            0.394 m               0.394 m
material                ``Runner_Plaster.011``  ``Runner_Plaster``  ``Runner_Plaster``
mesh                    1765 verts/3307 faces 1748 v / 3496 tris    1879 v / 3754 tris
======================  ====================  ====================  ==================

The static column was under 2 mm on every axis; the vertex counts differ because the
interactive decimation left n-gons where this one triangulates, and the material name
differs because ``.011`` is Blender's duplicate-name suffix from a session that had built
the figure twelve times — a name Unity would have imported and kept, and a generated asset
does not have one.

The **13 cm of span** is the rig's bill and it is itemised at ``LEG_X``: the arms had to
come off the torso and the legs had to come apart at the crotch, and the legs were thinned
and the arms moved out to pay for it. §12's corridor still takes two of these abreast and
its doors are the same 2.20 m. The mesh is also genus 0 now rather than genus 1 — the loop
of material that used to close between the mitten and the thigh is the loop §03's carry was
tearing.
"""

from __future__ import annotations

import math
import os
import re
import sys
import traceback
from dataclasses import dataclass

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

import blendkit  # noqa: E402
import gen_player_model as gpm  # noqa: E402
from blendkit import BoneSpec, MaterialSpec  # noqa: E402
from gen_player_model import Aim  # noqa: E402

REPO_ROOT = blendkit.REPO_ROOT
"""Where the checkout is, so this file can find the two things outside ``tools/`` it has
to reach: ``artifacts/runner/`` for the ``--glb`` preview and ``RunnerGun.cs`` for the
mount cross-check. It was already used by the preview path and never bound, so ``--glb``
raised a ``NameError`` — a switch nobody runs in a production export, which is exactly how
it survived."""

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
# Z-up, +X to the figure's left, **−Y forward**, origin under the feet after the drop.
# Every number below is a placement, not a proportion — see the module docstring on
# metaballs for why placements are the thing this file is allowed to have.
#
# −Y is not a preference, it is the toolchain's one axis convention, stated in the same
# words in ``gen_props``, ``gen_dressing``, ``gen_monster_model`` and ``gen_player_model``:
# *export_fbx's axis_forward='-Z' turns into +Z forward in Unity*. This file said "+Y
# forward" and pushed its feet to +0.048, which put the toes out of the figure's BACK.
# Invisible while the Runner was furniture; the first thing a walk cycle shows.
#
# The placements are named rather than inlined because ``bone_specs`` is derived from
# them. A rig table that repeats the mesh table by hand is two tables that drift, and a
# hip bone 3 cm off the hip is a thigh that swings the belly with it.

TORSO_C, TORSO_R = (0.0, 0.0, 0.86), (0.190, 0.140, 0.300)
BELLY_C, BELLY_R = (0.0, 0.0, 0.68), (0.175, 0.135, 0.180)
NECK_C, NECK_R = (0.0, 0.0, 1.17), (0.072, 0.072, 0.080)
HEAD_C, HEAD_R = (0.0, 0.0, 1.30), (0.150, 0.145, 0.150)

SHOULDER_X, SHOULDER_Z, SHOULDER_R = 0.220, 1.02, 0.100
ARM_X, ARM_R_TOP, ARM_R_BOTTOM = 0.285, 0.076, 0.062
HAND_C, HAND_R = (0.290, 0.0, 0.62), (0.078, 0.074, 0.070)

LEG_PART_FRACTION = 0.33
"""How far down from the hip the two thighs are still allowed to be one mass.

A third, which is to say the pelvis. Below that a leg has to be a leg: the crotch weld is
what a scissoring stride tears into a fringe, and the fringe hangs from wherever the weld
ends. Measured off the mesh by ``verify_limbs_hang_free``, not assumed — the smoothing
fills the notch between the thighs well below where the placement table leaves it open."""

LEG_X = 0.096
LEG_Z_TOP, LEG_R_TOP = 0.66, 0.076
LEG_Z_BOTTOM, LEG_R_BOTTOM = 0.17, 0.068
"""THE ARMS HANG OFF A SHOULDER, THEY ARE NO LONGER PART OF THE TORSO — and this is the
one change to the figure's outline that rigging it forced.

As authored, the arm shaft ran 32 mm inside the torso, the mitten 49 mm inside the thigh
and 17 mm inside the belly, and the module docstring above is right that for a STATIC
silhouette none of that is a defect: it is what makes the shoulder read and it is why this
figure is made of overlapping placements. It becomes a defect the moment the arm moves.
The body is one closed shell, so a weld a pose pulls apart does not open a gap — it draws
a **sheet of skin across the gap**. §03's two-handed carry swings the shoulder 78° forward
and pulled one edge to 190 mm over its rest length; the render shows a flat membrane from
the forearm down to the hip. On a model whose entire job is DESCENT-PIVOT §5's outline,
that membrane IS the outline.

So the arm is separated from the body everywhere except at the shoulder ball, which is
enlarged to 100 mm and is now the only bridge: it eats 41 mm into the torso and 111 mm
into the arm, and it sits AT the pivot, so rotating about it barely stretches anything.
The armpit slot that opens up is 31 mm — three voxels, enough that the remesh cannot
re-close it (``verify_limbs_hang_free``) and far too fine to resolve at §03's ten metres of
dark corridor. This is not the T-pose gap the metaball essay rejected: the arms still
hang, they hang 3 cm clear.

The legs come in and thin down by the same amount the arms went out, so the figure pays
for it in span rather than in bulk: **0.778 m → 0.919 m**, still two abreast in §12's
2.20 m corridor and through its 2.20 m doors. Thinner legs are also the direction a
walking figure wants — the swing is visible against the body instead of merging with it."""

FOOT_C, FOOT_R = (0.112, -0.048, 0.11), (0.086, 0.130, 0.066)
"""The pads sat at 0.102 with a 0.094 half-width, which left their inner faces **13.6 mm
apart** — inside the 12 mm voxel, so the remesh joined them and eight smoothing passes set
the join. Standing, that is invisible: it is buried between two touching feet, the shell
count is 1, the height is exact and the preflight is right that the two SOLIDS do not
overlap. Take a stride and the bridge is drawn 0.7 m into a bright membrane from heel to
toe. Out 10 mm and in 8 mm puts the gap at 65 mm — five voxels — and ``verify_feet_are_separate``
is what will notice if it ever closes again. The pad is also 19 mm narrower than it was,
which is the direction a foot wants: this one was 234 mm across and 324 mm long, and a
foot reads as a foot by being longer than it is wide."""


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
        Ellipsoid("Torso", TORSO_C, TORSO_R),
        Ellipsoid("Belly", BELLY_C, BELLY_R),
        Ellipsoid("Neck", NECK_C, NECK_R),
        Ellipsoid("Head", HEAD_C, HEAD_R),
    ]

    # Mirrored, not modelled twice. s = -1 is the figure's right.
    for s in (-1.0, +1.0):
        tag = "L" if s > 0 else "R"
        parts += [
            # 8.5 cm ball, centred 20.5 cm out — far enough to be a shoulder in the
            # outline, near enough that it eats 44 mm into the torso and welds.
            Ellipsoid(f"Shoulder_{tag}", (s * SHOULDER_X, 0.0, SHOULDER_Z),
                      (SHOULDER_R,) * 3),
            # Straight down from the shoulder to just above the wrist. A-pose, not
            # T-pose: the arms hang, so the silhouette is a person walking a corridor
            # rather than a mannequin, and the elbows stay inside §12's 2.20 m clear
            # width even with two racers abreast.
            Shaft(f"Arm_{tag}", s * ARM_X, 0.0,
                  z_top=SHOULDER_Z, r_top=ARM_R_TOP,
                  z_bottom=LEG_Z_TOP, r_bottom=ARM_R_BOTTOM),
            # A mitten, deliberately. Fingers are ~15 mm features seen at ten metres in
            # the dark; they cost geometry and survive as noise.
            Ellipsoid(f"Hand_{tag}", (s * HAND_C[0], HAND_C[1], HAND_C[2]), HAND_R),
            # Legs at 10 cm from centreline — a 20 cm stance, narrow enough that the two
            # thighs share the belly's volume and the pelvis welds as one mass.
            Shaft(f"Leg_{tag}", s * LEG_X, 0.0,
                  z_top=LEG_Z_TOP, r_top=LEG_R_TOP,
                  z_bottom=LEG_Z_BOTTOM, r_bottom=LEG_R_BOTTOM),
            # Pushed 4.8 cm forward — **−Y**, see the axis note above — because a foot is
            # the one part of a person that is not symmetric front-to-back, and the toes
            # are what tell a viewer at a glance which way a distant racer is facing.
            Ellipsoid(f"Foot_{tag}", (s * FOOT_C[0], FOOT_C[1], FOOT_C[2]), FOOT_R),
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
    # The joined object inherits the mesh DATA name of whichever primitive was active —
    # "Sphere.018" in practice, which is what both exporters then write into the file.
    body.data.name = MESH_NAME
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


@dataclass(frozen=True)
class Fit:
    """The similarity transform ``fit_height_and_ground`` applied to the welded body.

    The rig is authored in the placement table's coordinates and then carried through
    this, rather than being written out in final metres by hand. The scale is not a
    constant anyone can look up: it falls out of how much the eight smoothing passes
    shrank the union, so a bone table in final metres would be a table of numbers whose
    provenance is a previous run of this script.
    """

    scale: float
    drop: float
    """Metres subtracted from every z AFTER the scale, to land the sole on z = 0."""

    def z(self, z_pre: float) -> float:
        """A height from the placement table, in final metres."""
        return z_pre * self.scale - self.drop

    def d(self, metres_pre: float) -> float:
        """A length, radius or lateral offset from the table, in final metres."""
        return metres_pre * self.scale


def fit_height_and_ground(obj: bpy.types.Object, target: float) -> Fit:
    """Scales to ``target`` metres tall, then drops the lowest vertex onto z = 0.

    Both operations are written into the vertices rather than onto the object transform.
    A non-unit object scale exports as a scaled node, and then Unity's collider and the
    NavMesh bake disagree with the renderer about how big the thing is — the same reason
    ``blendkit.add_box`` bakes its size. Returns the transform, because the rig is built
    from the same placement table and has to land in the same place the mesh did.
    """
    lo, hi = world_bounds(obj)
    height = hi.z - lo.z
    if height <= 1e-6:
        blendkit.fail("the welded body has no height — the remesh produced nothing.")

    k = target / height
    for v in obj.data.vertices:
        v.co *= k

    lo, _ = world_bounds(obj)
    drop = lo.z
    for v in obj.data.vertices:
        v.co.z -= drop

    obj.data.update()
    return Fit(scale=k, drop=drop)


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


def verify_limbs_hang_free(obj: bpy.types.Object, ankle_z: float, crotch_z: float,
                           hip_z: float, leg_part_z: float, armpit_z: float,
                           armpit_x: float, armpit_r: float) -> None:
    """A limb may reach its opposite number only through the JOINT it hangs from.

    A third check on the weld, and it is here because a walk cycle found the first half of
    it. ``verify_one_shell`` wants the body to be one connected surface and it is; this
    wants each connection to run where the skeleton says it does — the feet through the
    legs, the arms through the shoulders. Anything else is a bridge, and a bridge between
    two limbs that move apart is not a gap that opens: it is a **sheet of skin drawn across
    the gap**, on a figure whose whole job is DESCENT-PIVOT §5's outline.

    Both halves have caught a real one:

    * the two foot pads were left 13.6 mm apart, inside the 12 mm voxel, so the remesh
      joined them and eight smoothing passes set the join. Standing, it is buried between
      two touching feet; a stride pulled it 0.7 m into a bright sail from heel to toe.
    * the arms ran 32 mm inside the torso for their whole length, so §03's carry drew a
      membrane from the forearm to the hip.

    Nothing else in this file can see either. The shell count is 1, the height is exact,
    the sole error is 0.00 mm, and the preflight is correct that the SOLIDS do not overlap
    — the bridge is made by the grid, not by the table, so only the welded mesh can be
    asked about it.

    The test is a surface walk from one side under a ceiling. Reaching the other side
    without going above the joint means the two are bridged below it.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()

    def walk(seed, allowed) -> set[int]:
        seen, stack = {seed.index}, [seed]
        while stack:
            v = stack.pop()
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in seen and allowed(w):
                    seen.add(w.index)
                    stack.append(w)
        return seen

    # Feet and legs: below the crotch there is nothing but legs, so a ceiling isolates the
    # pair and the only honest way from one side to the other is up over the pelvis. Two
    # ceilings rather than one, because they fail differently and the message should say
    # which: pads welded at the floor, or thighs welded down to the knee.
    for label, ceiling, hint in (
            ("feet", ankle_z,
             "A stride pulls that bridge into a sail from heel to toe. Move the pads apart "
             "in build_parts() until the gap clears the voxel with margin, or narrow them."),
    ):
        pool = [v for v in bm.verts if v.co.z < ceiling]
        if not pool:
            bm.free()
            blendkit.fail(f"no mesh below z = {ceiling:.3f} — the {label} are not where "
                          "the placement table says they are.")
        left = min((v for v in pool if v.co.x > 0.04), key=lambda v: v.co.z)
        right = min((v for v in pool if v.co.x < -0.04), key=lambda v: v.co.z)
        seen = walk(left, lambda w: w.co.z < ceiling)
        gap = 2.0 * min(abs(v.co.x) for v in pool)
        bridged = right.index in seen
        print(f"LIMB_FREE {label:4s} bridged={'YES' if bridged else 'no'} "
              f"ceiling={ceiling:.3f}m reachable={len(seen)}v "
              f"inner_gap={gap * 1000.0:.1f}mm voxel={VOXEL_SIZE * 1000.0:.0f}mm")
        if bridged:
            bm.free()
            blendkit.fail(
                f"the two {label} are welded to each other below z = {ceiling:.3f}: "
                f"{len(seen)} vertices reach across without going over the pelvis, and the "
                f"closest surfaces are {gap * 1000.0:.0f} mm apart against a "
                f"{VOXEL_SIZE * 1000.0:.0f} mm voxel. {hint}")

    # Legs: a boolean is the wrong answer here and the crotch is why. The two thighs MUST
    # be one mass at the top — that mass is the pelvis — so the only real question is how
    # far down the weld reaches. Measured as a profile: the narrowest material at each
    # height, from the ankle to the hip. Where that goes to zero, the legs are still one
    # leg, and every millimetre of it below the crotch is fringe a stride will tear.
    slices = 14
    profile = []
    for i in range(slices):
        lo = ankle_z + (crotch_z - ankle_z) * i / slices
        hi = ankle_z + (crotch_z - ankle_z) * (i + 1) / slices
        band = [abs(v.co.x) for v in bm.verts if lo <= v.co.z < hi]
        profile.append((0.5 * (lo + hi), 2.0 * min(band) if band else math.inf))
    print("LEG_SLOT " + " ".join(f"{z:.2f}m:{gap * 1000.0:.0f}mm" for z, gap in profile))

    welded = [z for z, gap in profile if gap < VOXEL_SIZE * 2.0]
    parted_to = min(welded) if welded else crotch_z
    print(f"LEG_PART legs_are_two_below={parted_to:.3f}m required_below={leg_part_z:.3f}m "
          f"(hip {hip_z:.3f}m, ankle {ankle_z:.3f}m)")
    if parted_to > leg_part_z:
        bm.free()
        blendkit.fail(
            f"the legs are still one mass down to z = {parted_to:.3f}, and they have to be "
            f"two by {leg_part_z:.3f} — the lower two thirds of a leg is a leg. The slot "
            f"between the thighs is under {VOXEL_SIZE * 2000.0:.0f} mm there, so the "
            "remesh fills it and the smoothing sets it. A scissoring stride then tears "
            "that weld into a hanging fringe between the knees, which is still legible "
            "shrunk to the 160 px a 1.75 m figure subtends at §03's ten metres. Widen the "
            "stance or thin the shafts in build_parts().")

    # Arms: a ceiling proves nothing here, because an arm hangs DOWN — below the armpit the
    # torso, the belly and both legs are all in the pool, so the left hand reaches the
    # right hand through the chest and the test passes on a figure with no arms at all.
    # The question is not "can it get across", it is "can it get across WITHOUT USING THE
    # SHOULDER". So the shoulder ball is removed from the graph and the arm must then be
    # unable to reach the body's midline at all.
    ball = Vector((0.0, 0.0, 0.0))
    for sign, side in ((1.0, "left"), (-1.0, "right")):
        ball.x, ball.z = sign * armpit_x, armpit_z + armpit_r
        hand = max((v for v in bm.verts if v.co.z < armpit_z and v.co.x * sign > 0.0),
                   key=lambda v: v.co.x * sign)
        reach = walk(hand, lambda w: (w.co - ball).length > armpit_r * 1.15)
        onto_body = [bm.verts[i].co.x * sign for i in reach if bm.verts[i].co.x * sign < 0.02]
        print(f"LIMB_FREE {side}_arm bridged={'YES' if onto_body else 'no'} "
              f"reachable_without_shoulder={len(reach)}v "
              f"shoulder_r={armpit_r * 1000.0:.0f}mm")
        if onto_body:
            bm.free()
            blendkit.fail(
                f"the {side} arm is welded to the torso somewhere other than the shoulder: "
                f"{len(onto_body)} vertices of the body's far side are reachable from the "
                "hand with the shoulder ball cut out of the graph. §03's carry swings this "
                "arm 78° forward, and every millimetre of that second weld becomes a sheet "
                "of skin from the forearm to the hip. Move the shaft out in build_parts() "
                "until the armpit slot clears the voxel with margin.")
    bm.free()


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


# ── The skeleton ────────────────────────────────────────────────────────────
#
# THIRTEEN BONES, AND WHY THAT IS THE ANSWER RATHER THAN A SHORTCUT
# -----------------------------------------------------------------
# ``Player.fbx`` carries 26. This carries 13, and the count is not a budget — it is a
# reading of the mesh. A bone earns its place here only if the surface it moves has
# somewhere to bend. The Runner is a voxel union smoothed eight times: after that pass
# there is no crease anywhere on it, so a joint put where the geometry is a straight
# taper does not articulate anything, it just gives the skinning solver a second
# influence to smear across a region that has no reason to bend.
#
#   Hips, Spine                the torso and the belly are two ellipsoids fused into one
#                              mass with a waist in the OUTLINE and no joint in the
#                              surface. One spine bone bends it; the player's three
#                              (Spine/Chest/UpperChest) would distribute a lean across a
#                              shape that has no vertebrae to distribute it over.
#   Head                       head + neck as one. The neck ellipsoid is 90 mm tall on a
#                              1.75 m figure. Splitting it buys a nod nobody resolves at
#                              §03's ten metres of dark corridor.
#   Left/RightUpperArm         the WHOLE arm, shoulder to mitten, one pendulum. The arm
#                              is a single tapered shaft with a ball at each end — there
#                              is no elbow to hinge, and the module docstring above is an
#                              essay on why this figure is made of placements rather than
#                              of anatomy.
#   Left/RightUpperLeg         }  the only place the count is NOT minimal, and it is
#   Left/RightLowerLeg         }  deliberate. Two bones per leg is what the player's
#                              two-bone IK needs (``gen_player_model._solve_leg``), and
#                              that solver is the difference between running and sliding:
#                              it places the planted foot by POSITION so the foot travels
#                              at a constant speed, which no amount of angle-authoring
#                              achieves. A one-bone leg cannot be levelled by it. So the
#                              knee exists to serve the gait solver, not the silhouette,
#                              and it sits at the middle of the column because a column
#                              with no knee bends in the middle.
#   Left/RightFoot, …Toes      the foot pad is 324 mm long — a quarter of the figure's
#                              leg. Rigid, it is a plank, and toe-off pivots the whole
#                              body about the tip. The toe joint is also what makes
#                              ``FOOT_ROLL`` mean the same thing here as it does on the
#                              player (see ``retarget_gait_solver``).
#
# Every name is one of Unity's humanoid spellings, so this rig is a strict SUBSET of
# Player.fbx's vocabulary and nothing downstream has to learn a second one. It is still
# imported **Generic**, not Humanoid: Unity's humanoid skeleton requires LowerArm and
# Hand on both sides, and inventing four bones to satisfy a mapper — on a figure whose
# arm has no elbow — is exactly the kind of geometry-free bone this comment argues
# against. ``Monster.fbx`` is Generic for the same class of reason.

RIG_NAME = "Runner_Rig"
"""The armature object, and therefore the FBX's root Model node. Matches
``Player_Rig`` / ``Monster_Rig``. **This renames the FBX root**, which is what Unity
derives a model prefab's root fileID from — see the report in ``main``."""

KNEE_FRACTION = 0.5
"""Where the knee sits between hip and ankle. A tapered column with no knee bends in the
middle; equal segments also make ``|THIGH − SHANK|`` zero, which is the inner limit
``_solve_leg`` clamps against, so the IK has no dead zone near the hip."""

HEEL_T, BALL_T, TOE_T, TIP_T = 0.62, -0.40, -0.78, -0.92
"""Bands along the foot, as a fraction of the foot ellipsoid's half-length (+ = heelward).

The player's sole is modelled flat on z = 0 and its three contacts all sit at z = 0. This
foot is an ellipsoid, so its underside is a ROCKER — at ±0.62 the ideal surface is already
17 mm off the floor — and declaring flat contacts on a rounded pad would float the figure
by that much every time it stood on its heel.

These are bands and not points, because the contact inside each one is **measured off the
welded mesh** (``measure_sole``). The ideal ellipsoid is not what stands on the floor: what
stands on the floor is that ellipsoid after a 12 mm voxel remesh, eight smoothing passes
and a 90% decimation, and the difference is real. Ground-locked against the ideal solid the
figure measured up to 7.8 mm of its RUN inside the concrete while every sole error read
0.00 mm — the stride was levelling a foot that is not the foot the game draws."""

BALL_LIFT = 0.030
"""How far the ball joint sits ABOVE the sole it pivots on. A hinge on the skin creases
the surface it is supposed to roll; a hinge inside the flesh rolls it."""

TOE_DROOP = 0.006
"""How far the toe bone's tip sits BELOW its ball joint — 4° of droop over 84 mm, which is
the player's own toe bone shape (its ``Toes`` runs −0.070 forward and −0.006 down).

Copied rather than solved, and the first attempt is why. Putting the tip on the foot pad's
own surface at 92% of its length looked like the honest choice, but that surface has
already curved 30 mm back up by then, so the toe bone rested **20° ABOVE horizontal**. The
gait tables dorsiflex the toes 22° at toe-off — correct, a real MTP joint extends as the
body rolls over it — and 22° on top of 20° pointed a 90 mm toe 42° at the ceiling. On a
324 mm soft flipper that does not read as a toe-off, it reads as a flap. The toe bone
belongs along the SOLE, inside the flesh; the pad's curve is where the toe ends, not
where it points."""


@dataclass(frozen=True)
class Skeleton:
    """Joint positions in FINAL metres, derived from the placement table and the fit."""

    hip_z: float
    knee_z: float
    ankle_z: float
    leg_x: float
    thigh: float
    shank: float
    spine_z: float
    neck_z: float
    crown_z: float
    shoulder_x: float
    shoulder_z: float
    hand_x: float
    hand_z: float
    ball: tuple[float, float]
    toe_tip: tuple[float, float]
    contacts: tuple[tuple[str, tuple[float, float, float]], ...]

    @property
    def leg(self) -> float:
        return self.thigh + self.shank


def _sole(fit: Fit, t: float) -> tuple[float, float]:
    """(y, z) of the foot pad's underside at fraction ``t`` of its half-length."""
    cy, cz = fit.d(FOOT_C[1]), fit.z(FOOT_C[2])
    ry, rz = fit.d(FOOT_R[1]), fit.d(FOOT_R[2])
    return cy + t * ry, cz - rz * math.sqrt(max(0.0, 1.0 - t * t))


def measure_sole(body: bpy.types.Object, fit: Fit,
                 ankle_z: float) -> tuple[tuple[float, float], ...]:
    """The three lowest points of the LEFT foot's real surface, one per band.

    Sampled off the welded mesh rather than solved off the placement table, for the reason
    in ``HEEL_T``'s note: the remesh, the smoothing and the decimation move the sole by
    several millimetres and the stride is levelled against whichever sole it is given.
    Only the left is measured — the figure is mirrored by construction, and averaging two
    samples of the same shape would only hide a mirror bug rather than find one.
    """
    cy, ry = fit.d(FOOT_C[1]), fit.d(FOOT_R[1])
    cx, rx = fit.d(FOOT_C[0]), fit.d(FOOT_R[0])

    # Three bands split halfway between the three sample points, so the constants above
    # are the single place the foot is divided up and the bands cannot drift from them.
    heel_ball = 0.5 * (HEEL_T + BALL_T)
    ball_toe = 0.5 * (BALL_T + TOE_T)

    out: list[tuple[float, float]] = []
    for lo_t, hi_t in ((heel_ball, 1.0), (ball_toe, heel_ball), (-1.0, ball_toe)):
        lo_y, hi_y = cy + lo_t * ry, cy + hi_t * ry
        if lo_y > hi_y:
            lo_y, hi_y = hi_y, lo_y
        band = [body.matrix_world @ v.co for v in body.data.vertices
                # Below the ankle and on the figure's left, so the leg column and the
                # other foot cannot contribute a "sole" point.
                if (body.matrix_world @ v.co).z < ankle_z
                and abs((body.matrix_world @ v.co).x - cx) < rx
                and lo_y <= (body.matrix_world @ v.co).y <= hi_y]
        if not band:
            blendkit.fail(
                f"no mesh under the foot between y = {lo_y:.3f} and {hi_y:.3f}. The sole "
                "bands are placed off the foot ellipsoid in the placement table, so this "
                "means the table and the mesh have come apart.")
        low = min(band, key=lambda p: p.z)
        out.append((low.y, low.z))
    return tuple(out)


def build_skeleton(body: bpy.types.Object, fit: Fit) -> Skeleton:
    """Reads the joints off the placement table.

    Two of these are measurements rather than choices and both are worth stating.

    The **hip** is the centre of the leg shaft's top ball. Not a nicer, higher number
    inside the pelvis: that ball is the only spherical thing in the leg, and a sphere
    turning about its own centre does not move its own surface, so the thigh swings
    without dragging the belly it is buried in. It lands at 0.44 of the figure's height
    where a person's is at 0.53 — this figure is short-legged by construction, and
    ``solve_cadence`` is what pays for that rather than a longer bone that would tear the
    belly.

    The **ankle** is where the leg column's axis meets the top of the foot pad. Solved
    two ways and they agree to 2 mm: the shaft's bottom ball centre sits at the pad's own
    upper surface at y = 0.
    """
    hip_z = fit.z(LEG_Z_TOP)
    ankle_z = fit.z(LEG_Z_BOTTOM)
    knee_z = hip_z - (hip_z - ankle_z) * KNEE_FRACTION

    ball_y, ball_sole = _sole(fit, BALL_T)
    tip_y, _tip_sole = _sole(fit, TIP_T)

    ball = (ball_y, ball_sole + BALL_LIFT)
    tip = (tip_y, ball[1] - TOE_DROOP)

    (heel_y, heel_z), (mid_y, mid_z), (toe_y, toe_z) = measure_sole(body, fit, ankle_z)
    print(f"SOLE_MEASURED heel=({heel_y:+.4f},{heel_z:+.4f}) "
          f"ball=({mid_y:+.4f},{mid_z:+.4f}) toe=({toe_y:+.4f},{toe_z:+.4f}) "
          f"(off the welded mesh, not off the ideal pad)")

    # Heel behind the ball joint belongs to Foot; everything ahead of it rolls with Toes.
    contacts = (
        ("Foot", (0.0, heel_y, heel_z - ankle_z)),
        ("Toes", (0.0, mid_y - ball[0], mid_z - ball[1])),
        ("Toes", (0.0, toe_y - ball[0], toe_z - ball[1])),
    )

    return Skeleton(
        hip_z=hip_z, knee_z=knee_z, ankle_z=ankle_z, leg_x=fit.d(LEG_X),
        thigh=hip_z - knee_z, shank=knee_z - ankle_z,
        spine_z=fit.z(BELLY_C[2]),
        neck_z=fit.z(NECK_C[2] - NECK_R[2]),
        crown_z=fit.z(HEAD_C[2] + HEAD_R[2] * 0.8),
        shoulder_x=fit.d(SHOULDER_X), shoulder_z=fit.z(SHOULDER_Z),
        hand_x=fit.d(HAND_C[0]), hand_z=fit.z(HAND_C[2]),
        ball=ball, toe_tip=tip, contacts=contacts)


KNEE_BIAS_Y = -0.006
"""The knee is pushed 6 mm forward of the hip–ankle line. Mirrors the player's rig, and
for the same reason: a leg whose three joints are exactly collinear at rest leaves the
bend direction undefined, and the IK is then free to pick a knee that bends backwards."""


def bone_specs(sk: Skeleton) -> list[BoneSpec]:
    """The 13 bones, in final metres. See the essay above for the count."""
    specs = [
        BoneSpec("Hips", (0.0, 0.0, sk.hip_z), (0.0, 0.0, sk.spine_z)),
        BoneSpec("Spine", (0.0, 0.0, sk.spine_z), (0.0, 0.0, sk.neck_z), "Hips", True),
        BoneSpec("Head", (0.0, 0.0, sk.neck_z), (0.0, 0.0, sk.crown_z), "Spine", True),
    ]
    for side, s in ((1, "Left"), (-1, "Right")):
        x = float(side)
        specs += [
            BoneSpec(f"{s}UpperArm", (x * sk.shoulder_x, 0.0, sk.shoulder_z),
                     (x * sk.hand_x, 0.0, sk.hand_z), "Spine"),

            BoneSpec(f"{s}UpperLeg", (x * sk.leg_x, 0.0, sk.hip_z),
                     (x * sk.leg_x, KNEE_BIAS_Y, sk.knee_z), "Hips"),
            BoneSpec(f"{s}LowerLeg", (x * sk.leg_x, KNEE_BIAS_Y, sk.knee_z),
                     (x * sk.leg_x, 0.0, sk.ankle_z), f"{s}UpperLeg", True),
            BoneSpec(f"{s}Foot", (x * sk.leg_x, 0.0, sk.ankle_z),
                     (x * sk.leg_x, sk.ball[0], sk.ball[1]), f"{s}LowerLeg", True),
            BoneSpec(f"{s}Toes", (x * sk.leg_x, sk.ball[0], sk.ball[1]),
                     (x * sk.leg_x, sk.toe_tip[0], sk.toe_tip[1]), f"{s}Foot", True),
        ]
    return specs


def retarget_gait_solver(sk: Skeleton) -> None:
    """Points ``gen_player_model``'s gait solver at THIS figure's leg.

    The brief for this file said the gait numbers are already solved and measured, and
    they are: ``level_stance`` re-places the stance keys by POSITION using a two-bone IK,
    and ``FOOT_ROLL = 1.18`` corrects for the fact that the generator levels the ANKLE
    while it measures the SOLE. Neither is re-derived here. What has to change is the leg
    they are solved for, and that leg reaches the solver as six module globals rather
    than as arguments — ``_ankle_offset``/``_solve_leg`` read ``THIGH_LEN``/``SHANK_LEN``,
    ``build_cycle`` reads ``HIP_Z``, ``foot_contacts`` reads ``CONTACTS``, and ``leg()``
    reads ``FOOT_REST``/``TOES_REST``.

    Rebinding those six is what "reuse the solver" means when the solver is a module and
    not a class. The alternative is a second copy of the IK in this file, parameterised
    by hand, which is precisely the second convention this generator was told not to
    invent — and a copy that would keep passing its own assertions for months after the
    original changed.

    **Does FOOT_ROLL still apply?** It is a property of one measurement: how far ahead of
    the ankle the ball of the foot sits, because that is the lever the contact point
    walks along while the ankle passes over it. The player's ball is 130 mm ahead of its
    ankle. This one solves to within a few mm of the same number off a completely
    different foot — printed as ``ball_ahead`` in RIG_LEG below — so 1.18 is carried over
    rather than re-measured. It is not taken on faith either: ``build_cycle`` measures the
    real sole travel afterwards and ``solve_cadence`` asserts the resulting m/s against
    §06's own 2.0 and 4.5. If the roll had not transferred, that assertion is what fails.
    """
    gpm.HIP_Z = sk.hip_z
    gpm.THIGH_LEN = sk.thigh
    gpm.SHANK_LEN = sk.shank
    gpm.CONTACTS = sk.contacts
    gpm.FOOT_REST = Vector((0.0, sk.ball[0], sk.ball[1] - sk.ankle_z)).normalized()
    gpm.TOES_REST = Vector((0.0, sk.toe_tip[0] - sk.ball[0],
                            sk.toe_tip[1] - sk.ball[1])).normalized()

    ball_ahead = -sk.ball[0]
    print(f"RIG_LEG hip_z={sk.hip_z:.4f}m knee_z={sk.knee_z:.4f}m ankle_z={sk.ankle_z:.4f}m "
          f"thigh={sk.thigh:.4f}m shank={sk.shank:.4f}m leg={sk.leg:.4f}m "
          f"({sk.leg / TARGET_HEIGHT:.3f} of height; player {0.835 / 1.75:.3f}) "
          f"ball_ahead={ball_ahead * 1000.0:.1f}mm (player 130.0mm) "
          f"foot_roll={gpm.FOOT_ROLL:.2f}")
    for name, off in sk.contacts:
        print(f"RIG_CONTACT {name:5s} offset=({off[0]:+.3f},{off[1]:+.3f},{off[2]:+.3f})")


GUN_MOUNT_CONSTANT = "GunMountArmsPerSpine"
"""The name of the constant in ``RunnerGun.cs`` that this file measures for it."""

GUN_MOUNT_SOURCE = os.path.join(
    "unity", "HorrorGame", "Assets", "Scripts", "Gameplay", "Race", "RunnerGun.cs")


def report_arm(sk: Skeleton) -> tuple[float, float]:
    """Measures the arm and the spine, and proves the held pose is a silhouette.

    **Why an arm needs measuring at all.** ``RunnerGun`` hangs ``Gun_Held`` off the end of
    ``RightUpperArm``, and the end of a leaf bone is a thing Unity does not know. Blender
    writes a bone as a node with a head transform; the LENGTH lives in the bone's tail,
    the tail of a leaf is not a node, and ``export_fbx`` sets ``add_leaf_bones=False``
    precisely so that no tip node is invented. So the hand's position is authored here and
    nowhere else, and if it is not carried across it is guessed.

    **It is carried across as a RATIO, not as metres, and that is deliberate.** This kit
    exports through ``FBX_SCALE_NONE``, which parks the unit conversion on the root node
    rather than in the data, so what "1.0" means inside a bone's local transform after
    Unity's importer has had it is a function of import settings this file cannot see.
    A ratio has no units to be wrong about: the runtime reads the SPINE's length off the
    rig it actually loaded — ``Head`` is connected to ``Spine``, so ``Head.localPosition``
    IS the spine's length in whatever units arrived — and multiplies. Both bones come out
    of the same skeleton, so the ratio is fixed by the figure and by nothing else.

    The pose check is the other half. ``GUN_ARM_SWING`` claims the hand leaves the body's
    outline; this measures whether it does, on the body that was actually built rather
    than on the table that was meant to build it.
    """
    arm_len = math.hypot(sk.hand_x - sk.shoulder_x, sk.hand_z - sk.shoulder_z)
    spine_len = sk.neck_z - sk.spine_z
    ratio = arm_len / spine_len
    print(f"RIG_ARM shoulder=({sk.shoulder_x:.4f},{sk.shoulder_z:.4f})m "
          f"hand=({sk.hand_x:.4f},{sk.hand_z:.4f})m arm={arm_len:.4f}m "
          f"spine={spine_len:.4f}m {GUN_MOUNT_CONSTANT}={ratio:.4f}")
    return arm_len, ratio


def verify_gun_pose(sk: Skeleton, arm_len: float, half_depth: float) -> None:
    """The held arm has to be outside the torso, or the pose says nothing at 12 m."""
    reach = arm_len * math.sin(math.radians(GUN_ARM_SWING))
    floor_deg = math.degrees(math.asin(min(1.0, half_depth / arm_len)))
    print(f"GUN_POSE swing={GUN_ARM_SWING:.1f}deg hand_ahead={reach:.4f}m "
          f"half_depth={half_depth:.4f}m clear={reach / half_depth:.2f}x "
          f"(floor {floor_deg:.1f}deg, margin needed {GUN_SILHOUETTE_MARGIN:.1f}x)")
    if reach < half_depth * GUN_SILHOUETTE_MARGIN:
        blendkit.fail(
            f"the held arm reaches {reach * 1000.0:.0f} mm in front of the shoulder against a "
            f"torso that is {half_depth * 1000.0:.0f} mm deep either side of it, which is "
            f"{reach / half_depth:.2f}x and under the {GUN_SILHOUETTE_MARGIN:.1f}x a "
            "silhouette needs. GunIdle and GunWalk then differ from Idle and Walk by "
            "nothing an outline carries, and the whole point of the pose — another runner "
            "reading 「저 사람 총 들었다」 at Gunplay.RangeMetres — is not in the asset. "
            f"Either GUN_ARM_SWING is too small (the floor for this arm is "
            f"{floor_deg:.1f} deg) or the arm got shorter.")


def verify_gun_mount(ratio: float) -> None:
    """Asserts ``RunnerGun.cs`` is still holding the number this file measured.

    A constant copied into another language is a seam, and this repository's standing
    lesson is that seams go on matching after the thing they named has moved. So the
    generator reads the consumer. If the rig's proportions change — a longer arm, a
    different shoulder — this fails in Blender, where the change was made, instead of
    putting the gun through a runner's elbow in a build nobody re-renders.

    Missing file or missing constant is a warning rather than a failure: this script is
    run from a checkout of the tools alone often enough that requiring the Unity project
    to be present would make it unrunnable for the wrong reason.
    """
    path = os.path.join(REPO_ROOT, GUN_MOUNT_SOURCE)
    if not os.path.exists(path):
        print(f"GUN_MOUNT_CHECK skipped: {GUN_MOUNT_SOURCE} is not in this checkout")
        return

    with open(path, encoding="utf-8") as handle:
        source = handle.read()

    found = re.search(GUN_MOUNT_CONSTANT + r"\s*=\s*([0-9]*\.?[0-9]+)f?\s*;", source)
    if found is None:
        print(f"GUN_MOUNT_CHECK skipped: no '{GUN_MOUNT_CONSTANT} = ...' in {GUN_MOUNT_SOURCE}")
        return

    theirs = float(found.group(1))
    print(f"GUN_MOUNT_CHECK {GUN_MOUNT_CONSTANT} measured={ratio:.4f} "
          f"RunnerGun.cs={theirs:.4f} delta={abs(theirs - ratio):.5f}")
    if abs(theirs - ratio) > 0.005:
        blendkit.fail(
            f"{GUN_MOUNT_SOURCE} holds {GUN_MOUNT_CONSTANT} = {theirs:.4f} and this rig "
            f"measures {ratio:.4f}. That constant is where Gun_Held is hung off the arm, "
            "and Unity cannot recover it from the file — a leaf bone's tail is not a node "
            "and add_leaf_bones is off. Left stale, the gun floats off the hand or sits "
            "inside the shoulder, in the third-person view only, which is the view nobody "
            "is looking at while they play.")


SKIN_NOTE = """Bone heat, unedited — and two attempts to edit it are why that line is here.

The crotch and the feet both tore in early renders, and both looked like weighting bugs:
material between the legs owned half by each of them, tracking a midpoint of two limbs
0.6 m apart. Two fixes were written for that reading. One forbade any vertex from being
driven by the opposite side's bones; the other handed the skin between the thighs to the
pelvis, which is where anatomy says it belongs. Both were measured, and both made every
clip WORSE — the walk's worst edge went 33 mm to 64 mm, the crouch-walk's 93 mm to 217 mm.

Neither was a weighting problem. The feet were welded to each other by the voxel grid, and
the thighs were welded down to the knee; the weights were a faithful description of a body
that really was joined there. Once the placement table was fixed the artefacts went with
it, and the weight edits became a solution looking for its problem. Deleted. What is left
is ``verify_limbs_hang_free`` and ``verify_skin_stretch``, which measure the thing itself
rather than a proxy for it."""


def build_rig(sk: Skeleton, body: bpy.types.Object) -> bpy.types.Object:
    """Builds the armature, binds the body to it and proves every bone reached the mesh.

    Automatic (bone-heat) weights, not hand-painted ones. On this mesh that is the honest
    choice rather than the lazy one: the vertices come out of a voxel remesh and a 10%
    decimation, so there is no correspondence at all between a vertex and the primitive it
    used to belong to — any hand weighting would be a second implicit model of where the
    parts are, maintained by nobody. Bone heat solves it off the geometry that actually
    exists, and ``verify_skin`` checks the result instead of trusting it.
    """
    specs = bone_specs(sk)
    rig = blendkit.build_armature(RIG_NAME, specs)
    gpm.cache_rig(rig)

    blendkit.bind_skin(body, rig, auto_weights=True)

    print(f"RIG_BONES count={len(rig.data.bones)} deform="
          f"{sum(1 for b in rig.data.bones if b.use_deform)} sockets=0")
    for bone in rig.data.bones:
        h, t = bone.head_local, bone.tail_local
        print(f"BONE {bone.name:14s} parent={(bone.parent.name if bone.parent else '-'):13s} "
              f"len={bone.length:.4f}m "
              f"head=({h.x:+.3f},{h.y:+.3f},{h.z:+.3f}) tail=({t.x:+.3f},{t.y:+.3f},{t.z:+.3f})")
    return rig


def verify_skin(rig: bpy.types.Object, body: bpy.types.Object) -> None:
    """Every bone must move some of the mesh, and every vertex must be moved by something.

    Both halves have failed silently before on other rigs and neither shows up in an
    export. A bone with no weight is an animated curve driving nothing — the figure plays
    a walk cycle and stands still. A vertex with no weight is worse: an unweighted vertex
    is pinned to the armature's ORIGIN, so it stays at the world origin while the body
    walks away from it, and the mesh grows a spike back to the spawn point.
    """
    groups = {g.name: g.index for g in body.vertex_groups}
    totals = {name: 0.0 for name in groups}
    counts = {name: 0 for name in groups}
    unweighted = 0

    for vert in body.data.vertices:
        total = 0.0
        for g in vert.groups:
            name = body.vertex_groups[g.group].name
            if name in totals and g.weight > 0.0:
                totals[name] += g.weight
                counts[name] += 1
                total += g.weight
        if total <= 1e-6:
            unweighted += 1

    bones = [b.name for b in rig.data.bones]
    missing = [b for b in bones if counts.get(b, 0) == 0]

    for name in bones:
        print(f"SKIN_BONE {name:14s} verts={counts.get(name, 0):5d} "
              f"weight={totals.get(name, 0.0):8.3f}")
    print(f"SKIN_REPORT bones={len(bones)} groups={len(groups)} "
          f"verts={len(body.data.vertices)} unweighted={unweighted}")

    if missing:
        blendkit.fail(
            "these bones move no geometry: " + ", ".join(missing) + ". Bone-heat "
            "weighting only reaches bones that lie INSIDE the mesh volume — a bone that "
            "pokes out of the surface gets no solution and its animation curves then "
            "drive nothing at all, which looks exactly like a missing clip.")
    if unweighted:
        blendkit.fail(
            f"{unweighted} vertices carry no bone weight. Unity pins an unweighted vertex "
            "to the skinned mesh's root, so those vertices stay at the spawn point while "
            "the body runs off, and the model grows a spike across the map.")


# ── Posing this figure ──────────────────────────────────────────────────────
#
# The absolute-aim convention is gen_player_model's, unchanged: a bone is authored as the
# WORLD direction it should point in and the local basis is derived (see that module's
# "Posing by absolute aim" note). Only the fan-out changes, because a 13-bone figure has
# fewer bones to spread a lean over than a 26-bone one. `gpm.leg` is used verbatim — the
# four leg bones it names are the four this rig has.


def torso(lean: float = 0.0, twist: float = 0.0, tilt: float = 0.0,
          hips_yaw: float = 0.0, hips_tilt: float = 0.0,
          hips_lean: float | None = None) -> dict:
    """Pelvis + spine. ``lean`` is the angle the SPINE ends at; the pelvis takes 15% of it
    unless pinned. The player spreads the same lean over four bones and lands its top bone
    at the same absolute angle, so a lean of 6° here is a lean of 6° there."""
    return {
        "Hips": Aim(gpm.up_dir(lean * 0.15 if hips_lean is None else hips_lean, hips_tilt),
                    roll=hips_yaw),
        "Spine": Aim(gpm.up_dir(lean, tilt), roll=twist),
    }


def head(lean: float = 0.0, yaw: float = 0.0, tilt: float = 0.0) -> dict:
    """Absolute head aim. ``lean`` + = looking down."""
    return {"Head": Aim(gpm.up_dir(lean, tilt), roll=yaw)}


def hang_dir(side: int, out: float = 0.0, swing: float = 0.0) -> tuple:
    """A HANGING arm's direction. ``out`` + = away from the body, ``swing`` + = forward.

    ``gen_player_model.arm_dir`` cannot express this pose and it is not a defect there: it
    is spherical about a T-pose, where ``down = 90`` is straight down and ``swing`` then
    multiplies ``cos(90) = 0`` and does nothing. The player's arms are never straight down
    — they are up in front of a first-person camera holding a torch. This figure's arms
    hang, and a hanging arm's whole vocabulary is fore-and-aft swing, so it is measured
    from the down axis instead of from the shoulder line.
    """
    v = (Matrix.Rotation(math.radians(-swing), 3, "X")
         @ Matrix.Rotation(math.radians(-side * out), 3, "Y")
         @ Vector((0.0, 0.0, -1.0)))
    return tuple(v)


def arm(side: int, out: float = 4.0, swing: float = 0.0) -> dict:
    s = "Left" if side > 0 else "Right"
    return {f"{s}UpperArm": Aim(hang_dir(side, out, swing))}


ARM_OUT = 4.0
"""Degrees the arms hang clear of the body. The mitten sits 61 mm INSIDE the thigh in the
placement table — the module docstring calls that fusion "not a defect", and for a static
silhouette it is not. It becomes one the moment the legs move: arm and thigh close a loop
of material, and a walk swings the two ends of that loop in opposite directions. It is
survivable here only because of where the loop is — the mitten sits 50 mm BELOW the hip
joint, so the thigh's displacement at that height is 50·sin(swing) ≈ 17 mm, a twentieth
of what it is at the ankle. The arm is the end that moves, so the arm is the end that is
kept quiet: ±9° of swing, not the player's ±26°."""

WALK_ARM_SWING = 9.0
RUN_ARM_SWING = 15.0

GUN_ARM_SWING = 55.0
"""Degrees the RIGHT arm is held forward while a runner carries §01's 총, and the whole of
what makes GunIdle and GunWalk different from Idle and Walk.

**It is a silhouette threshold, and it is measured off this body.** The pose has one job:
another runner reads *"that one is armed"* at ``Gunplay.RangeMetres`` — 12 m, which is
``GameConstants.FlashlightRange`` and therefore the furthest anything can be read in this
building at all. At that distance nobody sees a revolver; a revolver is 0.26 m and the
figure is 1.75 m, so the gun is a seventh of the outline's height and lost in it. What
carries is the ARM, and an arm only carries when it leaves the body's outline.

So the floor is geometric rather than aesthetic. This figure measures 0.394 m front to
back (``RUNNER_SHAPE depth``), so the torso outline reaches 0.197 m either side of the
shoulder, and an arm of ``RIG_ARM`` puts its hand ``arm × sin(swing)`` in front of the
shoulder. Below ``asin(0.197 / arm)`` — about 30° on this rig — the hand is still inside
the body and there is nothing to see. 55° puts it 1.7× the half-depth clear, which is
unambiguous from the side and still reads from the front as an arm that is not hanging.
``verify_gun_pose`` re-measures that margin off the built skeleton rather than trusting
this paragraph.

It is deliberately not the 78° the deleted two-handed carry used. That was both arms level
in front, which is a person holding a crate; one arm at 55° is a person holding something
small at low ready, and the two must not read alike now that only one of them exists."""

GUN_SILHOUETTE_MARGIN = 1.5
"""How many half-depths clear of the torso the gun hand must sit before the pose counts as
readable. 1.5 rather than 1.0 because 1.0 is the outline itself — a hand exactly on the
silhouette's edge is a hand nobody can see is there — and because the arm swings ±9° under
GunWalk's torso twist, which at this arm length is another 0.06 m of wobble either way."""


# ── The eight clips ─────────────────────────────────────────────────────────
#
# A clip exists here because some state can reach it. `PlayerAnimatorDriver.Resolve` is
# the list of states, and a state whose clip is null has weight nowhere to go, so
# `AdvanceWeights` bails with `total <= 0` and the body FREEZES in whatever pose the last
# clip left it in — which is why this file ships every reachable pose rather than
# Walk/Run/Idle and a shrug.
#
# **Three went and two arrived, and the pair is one export.** Carry, CarryIdle and
# CarryHeavy were §03's 목표물 and §08's 대형 전리품. Both are deleted — nobody carries
# anything, `PlayerAnimationState` has already dropped the three enum members (its own comment
# leaves the numbering sparse so Death stays 8), and a clip no state can reach is an
# asset tombstone: it survives every gate here, ships in the FBX, and is what
# `PivotAssetTombstoneTests` is red about. GunIdle and GunWalk replace them, for §01's
# one-shot 총: another runner has to be able to read *"that one is armed"* off an outline
# at `Gunplay.RangeMetres`, which is the distance a flashlight reaches and therefore the
# furthest anybody can read anything in this building.
#
# EVERY LOCOMOTION CLIP IS AUTHORED AT ITS OWN ReferenceSpeed, which is the whole
# contract with the driver. It plays a clip at `groundSpeed / ReferenceSpeed(state)`, so
# the foot travels at `authored × groundSpeed / reference`, and it only stops skating when
# `authored == reference`. The driver's table references GameConstants.WalkSpeed (2.0) for
# the walking states and RunSpeed (4.5) for Run — so those are the two numbers, regardless
# of what §05 says the player MOVES at while crouched. GunWalk is a walk: the legs are
# Walk's legs and only the right arm differs, so it is authored at the same 2.0 and
# `verify_clip_speeds` asserts the two measure the same.

CLIP_NAMES = ("Idle", "Walk", "Run", "Crouch", "CrouchWalk",
              "GunIdle", "GunWalk", "Death")

DEATH_END_FRAME = 48
"""The last key of the longest clip, and therefore the scene's frame range. Death is the
only one that does not loop, so it is the only one whose length is authored rather than
solved from a cadence."""

WALK_SPEED = 2.0
RUN_SPEED = 4.5


SOLE_PASSES = 2
"""How many times the ankle spread is corrected onto the measured sole travel.

**This is where FOOT_ROLL stops being a constant and starts being a seed, and the reason
is a measurement.** 1.18 is the ratio by which the ANKLE out-travels the SOLE over a
stance, measured off the player's walk and run — two gaits whose leg is within 3% of full
extension the whole time it is planted, so the foot has to roll heel-to-toe to make up the
difference. ``CROUCH_GAIT`` flexes the knee 110°: the ankle then sits well inside the
leg's reach, nothing clamps, the foot stays near flat, and the sole travels with the ankle
instead of behind it. Seeded at 1.18 and left there, CrouchWalk measured **2.43 m/s
against a 2.00 target — the 1.18 applied as pure overshoot** and no cadence in the search
could remove it, because it is a constant factor and cadence is not.

So 1.18 is kept as the first guess for every gait, and then the loop is closed on the
thing that was always the real quantity: ``build_cycle`` already measures the sole. Two
passes of ``step × target / measured`` is enough — the relation is near-linear, so the
first correction lands inside a millimetre-per-second and the second only proves it."""

PLAYER_LEG_METRES = gpm.THIGH_LEN + gpm.SHANK_LEN
"""0.835 m, captured at import — BEFORE ``retarget_gait_solver`` rebinds those globals.
The player's leg is the reference this figure's cadence is scaled from, so it has to be
read while it is still the player's."""

PLAYER_CYCLE_FRAMES = {
    "Walk": 20, "Run": 16, "CrouchWalk": 24, "GunWalk": 20,
}
"""``gen_player_model``'s own authored cycle lengths (spacing × keys, from its locomotion
builders). Not a target — the reference a shorter leg is scaled from.

``GunWalk`` takes Walk's 20 because it IS Walk below the hips: the gait table, the key
order and the stance solve are the walk's, and the only difference is which way the right
arm points. Giving it a reference of its own would let the two clips drift apart in
cadence for no reason anybody could name, and a runner who visibly changes step the
instant they pick something up is telling every other runner more than the silhouette is
supposed to."""


def pendulum_frames(name: str, leg_metres: float) -> float:
    """How long one stride of ``name`` should take on a leg of ``leg_metres``.

    A swinging leg is a pendulum, so its natural period goes as √L and its step frequency
    as 1/√L. That is the whole of it: **a short-legged figure does not stride slowly, it
    steps quickly**, and it is not a stylistic reading — it is why a child scuttles beside
    a walking adult. This leg is 0.73 of the player's, so √0.73 = 0.86 of its stride
    period, and every candidate cadence is scored against that.

    Without it the search picks whatever measures closest to §06's m/s and nothing stops
    that being a 0.267 s stride — 7.5 steps a second, a 1.5% better speed match, and a
    figure that reads as vibrating rather than walking.
    """
    return PLAYER_CYCLE_FRAMES[name] * math.sqrt(leg_metres / PLAYER_LEG_METRES)


def solve_cadence(rig, name: str, gait: dict, phases: tuple[str, ...], body_fn, order,
                  target_speed: float, tolerance: float, candidates: tuple[int, ...],
                  note: str, out: float = 1.5) -> gpm.Clip:
    """Solves this figure's key spacing, then closes the stride onto the measured sole.

    **The cadence cannot be copied from the player and this is the reason.** Step length
    is set by the leg, and this leg is 0.73 of the player's, so at a shared 2.0 m/s the
    only free variable left is how OFTEN the figure steps. The player's own generator says
    as much — *"cadence is set by the key spacing in each action builder; step length comes
    out of the geometry"* — it just never had to solve for it, having only one figure.

    Two things decide the answer and they are ordered, not blended. A candidate must first
    put the clip within ``tolerance`` of §06's m/s, because ``PlayerAnimatorDriver`` plays
    it at ``groundSpeed / ReferenceSpeed`` and any residual is pure skate at the speed the
    game actually moves at — and §12 makes a footstep a positioning channel, so a foot
    that is not where the sound says it is lies to §04's 청음사. Among the candidates that
    pass, the one closest to ``pendulum_frames`` wins. Spacing is searched over whole
    frames because a keyframe is one.

    Every candidate is printed, not only the winner: the runner-up is how a reader sees
    how much of a choice this was.
    """
    target_frames = pendulum_frames(name, gpm.THIGH_LEN + gpm.SHANK_LEN)
    scored: list[tuple[float, gpm.Clip]] = []

    for spacing in candidates:
        step = target_speed * (2.0 * spacing) / gpm.FPS
        clip = None
        for attempt in range(SOLE_PASSES + 1):
            clip = gpm.build_cycle(
                rig, name, spacing, gpm.level_stance(gait, phases, step), body_fn, order,
                out=out, target_speed=target_speed, speed_tolerance=tolerance, note=note)
            print(f"CADENCE {name:11s} spacing={spacing} pass={attempt} "
                  f"cycle={clip.cycle_frames:3d}f ({clip.cycle_frames / gpm.FPS:.3f}s) "
                  f"step_asked={step:.3f}m "
                  f"stance={clip.stance_travel:.3f}m/{clip.stance_seconds:.3f}s "
                  f"speed={clip.speed:.3f}m/s err={clip.speed - target_speed:+.3f}")
            if attempt == SOLE_PASSES or clip.speed <= 0.0:
                break
            step *= target_speed / clip.speed

        assert clip is not None
        if abs(clip.speed - target_speed) <= tolerance:
            scored.append((abs(clip.cycle_frames - target_frames), clip))

    print(f"CADENCE_PICK {name:11s} pendulum_target={target_frames:.1f}f "
          f"(player {PLAYER_CYCLE_FRAMES[name]}f x sqrt("
          f"{(gpm.THIGH_LEN + gpm.SHANK_LEN):.3f}/{PLAYER_LEG_METRES:.3f})) "
          f"in_tolerance={len(scored)}/{len(candidates)}")

    if not scored:
        best = None
        for spacing in candidates:
            step = target_speed * (2.0 * spacing) / gpm.FPS
            clip = gpm.build_cycle(
                rig, name, spacing, gpm.level_stance(gait, phases, step), body_fn, order,
                out=out, target_speed=target_speed, speed_tolerance=tolerance, note=note)
            if best is None or abs(clip.speed - target_speed) < abs(best.speed - target_speed):
                best = clip
        assert best is not None
        blendkit.fail(
            f"{name} measures {best.speed:.3f} m/s against §06's {target_speed:.2f} at "
            f"every cadence tried ({', '.join(str(c) for c in candidates)}); the closest "
            f"is {abs(best.speed - target_speed):.3f} m/s out against a tolerance of "
            f"{tolerance:.2f}. PlayerAnimatorDriver plays this clip at "
            f"groundSpeed/{target_speed:.1f}, so that difference is pure foot skate at "
            f"the speed the game actually moves at. The leg is "
            f"{(gpm.THIGH_LEN + gpm.SHANK_LEN):.3f} m (see RIG_LEG) against the player's "
            f"{PLAYER_LEG_METRES:.3f}; a figure this short-legged cannot reach the "
            "stride, and the honest fixes are a longer leg in the placement table or a "
            "gait table authored for this figure — not a wider tolerance.")

    scored.sort(key=lambda t: t[0])
    winner = scored[0][1]
    print(f"CADENCE_WON {name:11s} cycle={winner.cycle_frames}f "
          f"({winner.cycle_frames / gpm.FPS:.3f}s stride, "
          f"{2.0 * gpm.FPS / winner.cycle_frames:.2f} steps/s) "
          f"speed={winner.speed:.3f}m/s err={winner.speed - target_speed:+.4f} "
          f"sole_err={winner.sole_error * 1000.0:.2f}mm")
    return winner


def clip_idle(rig) -> gpm.Clip:
    """§04 관측자 needs 이동 정지 3초, so nothing below the hips moves in this loop.

    3.07 s at 30 fps, the same length as the player's, because the ability that reads it
    is the same ability. Breathing lives in the spine, the head and the arms.
    """
    keys, _ = gpm.cycle_frames_for(23)
    breath = (
        dict(lean=3.0, hl=2.0, yaw=0.0, sw=0.0),
        dict(lean=1.6, hl=1.0, yaw=-5.0, sw=-1.2),
        dict(lean=3.6, hl=2.6, yaw=0.0, sw=0.8),
        dict(lean=2.2, hl=1.4, yaw=6.0, sw=-0.8),
    )
    return _still_clip(rig, "Idle", keys, 92, "§04 stillness; feet welded",
                       lambda b: gpm.merge(
                           torso(lean=b["lean"], hips_lean=0.4),
                           head(lean=b["hl"], yaw=b["yaw"]),
                           arm(1, ARM_OUT, b["sw"]),
                           arm(-1, ARM_OUT, b["sw"] * 0.8),
                           gpm.leg(1, 0.0, -2.0, 0.0, 0.0),
                           gpm.leg(-1, 0.0, -2.0, 0.0, 0.0)),
                       breath)


def _still_clip(rig, name: str, keys, cycle_frames: int, note: str, spec_fn, table,
                hips_xy=(0.0, 0.0)) -> gpm.Clip:
    """A clip whose feet never leave the floor: ground-locked, keyed, measured."""
    poses, hip_zs, residuals = [], [], []
    prev: dict[str, Euler] = {}
    for frame, row in zip(keys, table):
        pose, hip_z, residual = gpm.ground_locked(rig, frame, spec_fn(row), hips_xy, prev)
        poses.append(pose)
        hip_zs.append(hip_z)
        residuals.append(residual)
    return gpm.Clip(name=name, poses=poses, cycle_frames=cycle_frames,
                    measure_frame=keys[0], note=note,
                    hip_lo=min(hip_zs), hip_hi=max(hip_zs),
                    sole_error=max(abs(r) for r in residuals))


def _cycle_body(lean: float, twist_amp: float, sway_amp: float, swing_amp: float,
                head_lean: float = 2.0, arm_out: float = ARM_OUT, period: int = 4,
                right_held: float | None = None):
    """The shared above-the-hips half of a locomotion cycle.

    One function for every walking clip because the difference between them lives in the
    gait table and the posture, not in how a torso counter-rotates against its own legs.
    ``period`` is the number of keys in one full STRIDE, so the torso and arms run at half
    the leg frequency in the run's eight-key order and at the leg frequency in the
    four-key walk orders — a torso that counter-rotated twice per stride is a shimmy.

    ``right_held`` pins the right arm at a fixed forward angle instead of swinging it,
    which is the whole of GunWalk. Everything below the hips — the gait table, the key
    order, the stance solve, the cadence search — is untouched, so the clip is Walk with
    one arm stopped and ``verify_clip_speeds`` can assert the two measure the same m/s. A
    held arm that also swung would be a runner waving a revolver in time with their steps.
    """
    def body(i, left, right):
        phase = math.sin(2.0 * math.pi * i / period)
        twist = -twist_amp * phase
        sway = sway_amp * phase
        spec = gpm.merge(
            torso(lean=lean, twist=twist, hips_yaw=-twist * 0.8, hips_tilt=sway * 80),
            head(lean=head_lean, yaw=-twist * 0.25),
            arm(1, arm_out, +swing_amp * phase),
            arm(-1, arm_out,
                -swing_amp * phase if right_held is None else right_held),
        )
        return spec, (sway, 0.0)
    return body


def clip_walk(rig) -> gpm.Clip:
    """§06 걷기 2.0 m/s, and the driver's reference speed for this state.

    Both arms swing, unlike the player's — the player holds a torch in its right hand and
    §13 networks the beam direction, so swinging that arm would smear information across
    the corridor twice a second. This figure carries nothing and has no hand to carry it
    with, so the asymmetry has nothing left to protect and a symmetric swing reads better
    in the one thing DESCENT-PIVOT §5 leaves it: 「똑같이 생긴 스무 명」 in outline.
    """
    return solve_cadence(
        rig, "Walk", gpm.WALK_GAIT, ("contact", "pass", "toeoff"),
        _cycle_body(lean=6.0, twist_amp=7.0, sway_amp=0.012, swing_amp=WALK_ARM_SWING),
        gpm.WALK_ORDER, WALK_SPEED, 0.25, (2, 3, 4, 5), "§06 걷기 2.0 m/s")


def clip_run(rig) -> gpm.Clip:
    """§06 달리기 4.5 m/s. 주자's 질주 (5.6 m/s) is this clip at 1.24× — the driver has no
    upper clamp on playback rate precisely so that it can be.

    Torso pitched 14° forward, arms swinging wider, and the flight keys in
    ``gpm.RUN_ORDER`` lift the hips off the highest grounded key instead of being
    ground-locked; welding a foot down mid-flight is how a run becomes a fast walk.
    """
    return solve_cadence(
        rig, "Run", gpm.RUN_GAIT, ("contact", "stance", "toeoff"),
        _cycle_body(lean=14.0, twist_amp=12.0, sway_amp=0.018,
                    swing_amp=RUN_ARM_SWING, head_lean=-4.0, period=8),
        gpm.RUN_ORDER, RUN_SPEED, 0.55, (1, 2, 3),
        "§06 달리기 4.5 m/s; 질주 = ×1.24")


def clip_crouch(rig) -> gpm.Clip:
    """§12 은폐 지점. Thigh 80° forward against a shank 30° back is 110° of knee flexion —
    ``gpm.CROUCH_GAIT``'s own base pose, so the crouch and the crouch-walk are the same
    posture and a player who stops moving does not pop upright.

    The hips go 0.10 m BACK as well as down, for the reason the player's clip records: a
    straight drop puts the knees ahead of the pelvis and reads as sitting on an invisible
    chair rather than taking cover.
    """
    keys, _ = gpm.cycle_frames_for(20)
    breath = (0.0, 1.4, -0.6, 1.0)
    base = gpm.CROUCH_GAIT["pass"]
    return _still_clip(
        rig, "Crouch", keys, 80, "§12 concealment",
        lambda b: gpm.merge(
            torso(lean=24.0 + b * 0.6, hips_lean=2.4),
            head(lean=4.0 - b, yaw=b * 4.0),
            arm(1, ARM_OUT + 2.0, 6.0), arm(-1, ARM_OUT + 2.0, 6.0),
            gpm.leg(1, base["thigh"], base["shank"], 0.0, 0.0, out=6.0),
            gpm.leg(-1, base["thigh"], base["shank"], 0.0, 0.0, out=6.0)),
        breath, hips_xy=(0.0, 0.100))


def clip_crouch_walk(rig) -> gpm.Clip:
    """Moving while concealed. §04 청음사 — *"자기가 소리를 내면 못 듣는다"* — makes this a
    real state rather than a convenience.

    Authored at 2.0 m/s even though §05 crouch-walks at 1.0: the driver's
    ``ReferenceSpeed`` maps CrouchWalk to ``WalkSpeed``, so it plays this clip at half rate
    when the player crouches, and a clip authored at the crouched speed would then deliver
    half of that. The clip's job is to match the reference, not the movement.
    """
    return solve_cadence(
        rig, "CrouchWalk", gpm.CROUCH_GAIT, ("contact", "pass", "toeoff"),
        _cycle_body(lean=26.0, twist_amp=5.0, sway_amp=0.012, swing_amp=6.0,
                    head_lean=2.0, arm_out=ARM_OUT + 2.0),
        gpm.WALK_ORDER, WALK_SPEED, 0.30, (2, 3, 4, 5),
        "§04 quiet travel; driver references WalkSpeed", out=6.0)


def clip_gun_idle(rig) -> gpm.Clip:
    """Standing still with §01's 총 in the right hand.

    Idle's breathing loop with one change: the right arm is held at
    ``GUN_ARM_SWING`` instead of hanging. It exists for the same reason the deleted
    CarryIdle did — ``PlayerAnimatorDriver`` falls back to the unarmed pose at zero ground
    speed, so without it a runner who stops moving drops the arm and the gun goes through
    their own thigh — and for a reason CarryIdle never had: standing still is when another
    runner gets the longest look at you, so it is the pose the *"that one is armed"*
    reading actually has time to happen in.

    The breath table is Idle's, scaled off the held arm rather than replaced. A held arm
    is not a rigid one: 1.2 degrees of drift over three seconds is a hand that is tiring,
    and a limb that is perfectly still next to a torso that is not reads as a prop
    welded on.
    """
    keys, _ = gpm.cycle_frames_for(23)
    breath = (
        dict(lean=3.0, hl=2.0, yaw=0.0, sw=0.0),
        dict(lean=1.6, hl=1.0, yaw=-5.0, sw=-1.2),
        dict(lean=3.6, hl=2.6, yaw=0.0, sw=0.8),
        dict(lean=2.2, hl=1.4, yaw=6.0, sw=-0.8),
    )
    return _still_clip(rig, "GunIdle", keys, 92, "§01 armed, halted",
                       lambda b: gpm.merge(
                           torso(lean=b["lean"], hips_lean=0.4),
                           head(lean=b["hl"], yaw=b["yaw"]),
                           arm(1, ARM_OUT, b["sw"]),
                           arm(-1, ARM_OUT, GUN_ARM_SWING + b["sw"]),
                           gpm.leg(1, 0.0, -2.0, 0.0, 0.0),
                           gpm.leg(-1, 0.0, -2.0, 0.0, 0.0)),
                       breath)


def clip_gun_walk(rig) -> gpm.Clip:
    """Walking with §01's 총, and the clip the whole feature is read off.

    **It is Walk below the hips, exactly.** ``gpm.WALK_GAIT``, ``gpm.WALK_ORDER``, the same
    three phases, the same target speed and the same cadence candidates — the solver runs
    again rather than the number being copied, so the 2.0 m/s is MEASURED for this clip
    instead of inherited, and ``verify_clip_speeds`` asserts the two agree. Everything that
    changed is above the waist: the right arm stops swinging and holds at
    ``GUN_ARM_SWING``, and the torso's counter-twist is halved because a body that is
    carrying something in one hand does not rotate as freely against its own stride.

    Why there is no GunRun. ``PlayerAnimatorDriver`` plays a clip at
    ``groundSpeed / ReferenceSpeed``, so a runner sprinting with a gun would need a third
    armed clip and a fourth for the crouch, and each one is another pose that has to stay
    distinguishable from the other seven at 12 m. Two is what §01 needs: standing and
    moving. An armed runner at 4.5 m/s plays Run and their arm swings — which is a lie
    about their hand for as long as they are sprinting, and it is the cheapest lie
    available, because a sprinting outline at 12 m in the dark is already unreadable.
    """
    return solve_cadence(
        rig, "GunWalk", gpm.WALK_GAIT, ("contact", "pass", "toeoff"),
        _cycle_body(lean=6.0, twist_amp=3.5, sway_amp=0.012, swing_amp=WALK_ARM_SWING,
                    right_held=GUN_ARM_SWING),
        gpm.WALK_ORDER, WALK_SPEED, 0.25, (2, 3, 4, 5), "§01 armed, 2.0 m/s")


def clip_death(rig) -> gpm.Clip:
    """§09 사망 처리 — death is a transition into the ghost state, not an exit.

    Does NOT loop, and settles flat rather than in a heap: §08 makes the corpse a map
    marker teammates have to navigate back to for the dropped loot, and §09's ghost is
    *"자기 물건이 어디 있는지 보이는데 말할 수 없다"* — the pose is the thing the ghost is
    screaming about and cannot point at.

    The player's keyframe table is reused rather than re-authored, with the hip drops
    scaled by the ratio of the two hip heights. A fall is a fall from wherever the hips
    started, and this figure's start 0.166 m lower than the player's; carrying the
    player's −0.810 m over unscaled would drive the pelvis 44 mm through the floor.
    """
    ratio = gpm.HIP_Z / 0.93
    steps = [
        # frame, spine lean, head lean, yaw, tilt, hips(world), (thigh, shank, foot), swing
        (1, 4.0, 2.0, 0.0, 0.0, (0.0, 0.0, 0.0), (0.0, -2.0, 0.0), (2.0, -2.0)),
        (6, -16.0, -22.0, -14.0, 8.0, (0.0, 0.04, -0.02), (-6.0, -14.0, -8.0), (-24.0, -18.0)),
        (14, 22.0, 16.0, 10.0, -6.0, (0.0, -0.05, -0.26), (34.0, -46.0, 6.0), (30.0, 22.0)),
        (23, 52.0, 30.0, 18.0, -10.0, (0.0, -0.24, -0.55), (-14.0, -40.0, -12.0), (58.0, 44.0)),
        (32, 80.0, 54.0, 26.0, -14.0, (0.0, -0.46, -0.76), (-62.0, -56.0, -22.0), (76.0, 66.0)),
        (41, 86.0, 70.0, 34.0, -8.0, (0.0, -0.55, -0.810), (-80.0, -70.0, -20.0), (84.0, 78.0)),
        (48, 85.0, 72.0, 35.0, -7.0, (0.0, -0.555, -0.812), (-81.0, -71.0, -20.0), (85.0, 79.0)),
    ]
    poses = []
    prev: dict[str, Euler] = {}
    for frame, lean, hlean, yaw, tilt, hips, legs, swing in steps:
        thigh, shank, foot = legs
        spec = gpm.merge(
            torso(lean=lean, tilt=tilt, twist=yaw * 0.5),
            head(lean=hlean, yaw=yaw, tilt=tilt),
            arm(1, ARM_OUT + 14.0, swing[0]),
            arm(-1, ARM_OUT + 6.0, swing[1]),
            gpm.leg(1, thigh, shank, foot, 0.0, out=7.0),
            gpm.leg(-1, thigh - 8.0, shank + 6.0, foot - 6.0, 0.0, out=-3.0),
        )
        world = (hips[0], hips[1] * ratio, hips[2] * ratio)
        poses.append(gpm.make_pose(frame, spec, hips_world=world, prev=prev))
    return gpm.Clip(name="Death", poses=poses, loop=False, measure_frame=48,
                    note="§09 → ghost; corpse marks dropped loot (§08)")


CLIP_BUILDERS = (clip_idle, clip_walk, clip_run, clip_crouch, clip_crouch_walk,
                 clip_gun_idle, clip_gun_walk, clip_death)


FLOOR_TOLERANCE = 0.004
"""4 mm of allowed penetration. Not slack — the body is a smoothed blob whose surface
passes between vertices, so a sole resting exactly on z = 0 measures a hair either side of
it. Anything past this is a limb inside the concrete."""


def lowest_vertex(rig: bpy.types.Object, body: bpy.types.Object, pose) -> float:
    """The lowest DEFORMED vertex at one pose, in metres.

    Measured off the evaluated mesh rather than off the sole contacts, because the two
    answer different questions. The contacts are three points on the foot and they are
    what ``ground_locked`` levels a stride against; this is every vertex there is, which
    is the only thing that can catch a shoulder through the floor in a fall.
    """
    gpm.apply_pose(rig, pose)
    evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    low = min((evaluated.matrix_world @ v.co).z for v in mesh.vertices)
    evaluated.to_mesh_clear()
    return low


def settle_on_floor(rig: bpy.types.Object, body: bpy.types.Object, clip: gpm.Clip) -> None:
    """Lifts any key of ``clip`` whose body would be inside the floor. Lift only, never drop.

    §09 makes the corpse a thing other people come back and look at — §08's dropped loot is
    where the body is, and the ghost is *"자기 물건이 어디 있는지 보이는데 말할 수 없다"*.
    A body 50 mm into the concrete is what the ghost is looking at.

    Only lifting matters: a fall is airborne for most of its keys and pinning those to the
    floor would delete the fall. The player's own Death clip authors its hip curve by hand
    and is never checked against the floor at all; the numbers were retargeted onto a
    shorter figure here, so checking them is not optional.
    """
    lifted = 0
    worst = 0.0
    for pose in clip.poses:
        low = lowest_vertex(rig, body, pose)
        worst = min(worst, low)
        if low < -FLOOR_TOLERANCE:
            world = gpm.REST["Hips"] @ Vector(pose.locations["Hips"])
            pose.locations["Hips"] = gpm.to_local_vec(
                "Hips", (world.x, world.y, world.z - low))
            lifted += 1
    print(f"FLOOR_SETTLE {clip.name:11s} keys={len(clip.poses)} lifted={lifted} "
          f"worst_before={worst * 1000.0:+.1f}mm")


def verify_floor(rig: bpy.types.Object, body: bpy.types.Object,
                 clips: list[gpm.Clip]) -> None:
    """No key of any clip may put the body through the floor.

    §12 makes the floor a gameplay surface — *"바닥 재질이 지도다"* — and §04's 청음사 reads
    the monster's position from which surface a foot lands on. A body that intersects the
    floor is the same lie as a body that floats above it, and neither shows up in an export
    or a triangle count.
    """
    worst_name, worst = "", 0.0
    for clip in clips:
        low = min(lowest_vertex(rig, body, pose) for pose in clip.poses)
        print(f"FLOOR_CLEAR {clip.name:11s} lowest_vertex={low * 1000.0:+.2f}mm")
        if low < worst:
            worst_name, worst = clip.name, low
    if worst < -FLOOR_TOLERANCE:
        blendkit.fail(
            f"{worst_name} puts the body {abs(worst) * 1000.0:.1f} mm through the floor. "
            f"The tolerance is {FLOOR_TOLERANCE * 1000.0:.0f} mm, which is the surface's "
            "own sampling error on a decimated blob; past that it is a limb inside the "
            "concrete, and §09 makes the corpse something teammates walk back to and look "
            "at.")


MAX_EDGE_STRETCH = 0.100
"""How much LENGTH one edge of the skin may gain over its rest length, in metres.

**This is the number that decides what this figure's arms are allowed to do**, and it
exists because the placement table welds them to the body along their whole length: the
arm shaft is 32 mm inside the torso, the mitten 49 mm inside the thigh and 17 mm inside
the belly. The module docstring calls that fusion "not a defect", and standing still it is
not — it is what makes the shoulder read. Rotate the arm forward and it is a hinge with a
web across it, the web is made of the same skin, and the only question left is how far it
gets pulled.

Absolute metres and not a ratio, and the first cut of this gate is why. As a ratio the
answer is meaningless on this mesh: a 90% collapse decimation leaves edges under a
millimetre long, so the worst RATIO was 8.7x on a walk that renders perfectly and 1.8x on
an Idle that does not move below the hips. Both were the same sub-millimetre edge doing
nothing visible. What draws a sheet across a corridor is length, not proportion.

100 mm is where the shipped set sits with room and where a regression would not. The nine
clips of the carry-era set measured 4, 33, 56, 53, 93, 45, 37, 27 and 83 mm — the 93 is
CrouchWalk, which strides on a 110°-flexed knee and is still the most deformation this
figure is ever asked for. GunIdle and GunWalk replaced three of those and the current
numbers are printed by ``verify_skin_stretch`` on every run, which is the point: This is a regression guard rather
than a quality bar, and it earns its place by what it caught: at 190 mm the carry drew a
flat sheet from the forearm to the hip that no other check in this file could see.

The limit is also what says NO to the wrong fix. Two attempts to solve the tearing in the
skin WEIGHTS both passed a visual glance and both moved these numbers the wrong way — see
``SKIN_NOTE``. The problem was the placement table twice, and this is the gate that said so.

geometry can absorb, and what will notice when the geometry changes."""


def skin_stretch(rig: bpy.types.Object, body: bpy.types.Object, pose,
                 rest: list[float]) -> tuple[float, float]:
    """The worst edge at one pose: (metres gained, its rest length in metres)."""
    gpm.apply_pose(rig, pose)
    evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    worst, at_rest = 0.0, 0.0
    for i, edge in enumerate(mesh.edges):
        a, b = mesh.vertices[edge.vertices[0]].co, mesh.vertices[edge.vertices[1]].co
        gained = (a - b).length - rest[i]
        if gained > worst:
            worst, at_rest = gained, rest[i]
    evaluated.to_mesh_clear()
    return worst, at_rest


def verify_skin_stretch(rig: bpy.types.Object, body: bpy.types.Object,
                        clips: list[gpm.Clip]) -> None:
    """No pose may add more than ``MAX_EDGE_STRETCH`` to any edge of the skin.

    A stretched edge on this figure is not a subtle artefact. The body is one closed shell
    with the arms welded to the torso, so any weld a pose pulls apart becomes a flat sheet
    of skin spanning the gap — and DESCENT-PIVOT §5 leaves this model exactly one job,
    「똑같이 생긴 스무 명」 read as an outline in the dark. A sheet between the arm and the
    hip IS the outline.
    """
    rest = [(body.data.vertices[e.vertices[0]].co
             - body.data.vertices[e.vertices[1]].co).length for e in body.data.edges]
    worst_name, worst = "", 0.0
    for clip in clips:
        peak, at_rest = max((skin_stretch(rig, body, pose, rest) for pose in clip.poses),
                            key=lambda t: t[0])
        print(f"SKIN_STRETCH {clip.name:11s} worst_edge=+{peak * 1000.0:6.1f}mm "
              f"(rest {at_rest * 1000.0:5.1f}mm) limit={MAX_EDGE_STRETCH * 1000.0:.0f}mm")
        if peak > worst:
            worst_name, worst = clip.name, peak
    if worst > MAX_EDGE_STRETCH:
        blendkit.fail(
            f"{worst_name} adds {worst * 1000.0:.0f} mm to one edge of the skin, past the "
            f"{MAX_EDGE_STRETCH * 1000.0:.0f} mm limit. The arms are welded to the torso "
            "along their whole length in build_parts(), so a pose that swings one away "
            "from the body stretches that weld into a sheet rather than opening a gap. "
            "Author the pose smaller, or move the arms clear of the body — see ARM_OUT.")


def pose_measure(rig, clip: gpm.Clip) -> None:
    """What the figure is actually DOING at a clip's measure frame, in metres.

    A render can be misread — a foot pointing down at the camera looks like a flap, and a
    forward lean looks like a fold. These are the numbers the render is checked against:
    where the hips are, how far the spine is off vertical, and what each foot is doing.
    """
    gpm.apply_pose(rig, clip.poses[[p.frame for p in clip.poses].index(clip.measure_frame)])
    hips = gpm.bone_point(rig, "Hips", (0.0, 0.0, 0.0))
    spine_dir = (gpm.bone_point(rig, "Head", (0.0, 0.0, 0.0)) - hips).normalized()
    lean = math.degrees(math.atan2(-spine_dir.y, spine_dir.z))

    # `Bone.vector` is PARENT-relative; `bone_point` wants a world offset from the head.
    # Feeding one to the other silently rotates the offset by the parent's rest
    # orientation, which is how the first cut of this function reported every toe 51 mm
    # underground on a rig whose sole error measures 0.00 mm.
    def span(name: str) -> tuple[float, float, float]:
        bone = rig.data.bones[name]
        return tuple(bone.tail_local - bone.head_local)

    feet = []
    for side in ("Left", "Right"):
        ankle = gpm.bone_point(rig, side + "Foot", (0.0, 0.0, 0.0))
        ball = gpm.bone_point(rig, side + "Toes", (0.0, 0.0, 0.0))
        tip = gpm.bone_point(rig, side + "Toes", span(side + "Toes"))
        feet.append(f"{side[0]}:ankle=({ankle.y:+.3f},{ankle.z:.3f}) "
                    f"ball=({ball.y:+.3f},{ball.z:.3f}) tip=({tip.y:+.3f},{tip.z:.3f})")

    arm = gpm.bone_point(rig, "LeftUpperArm", span("LeftUpperArm")) \
        - gpm.bone_point(rig, "LeftUpperArm", (0.0, 0.0, 0.0))
    print(f"POSE_MEASURE {clip.name:11s} frame={clip.measure_frame:3d} "
          f"hip=({hips.y:+.3f},{hips.z:.3f}) spine_lean={lean:+.1f}deg "
          f"arm_swing={math.degrees(math.atan2(-arm.y, -arm.z)):+.1f}deg " + "  ".join(feet))


def verify_clip_speeds(clips: list[gpm.Clip]) -> None:
    """The clips have to agree with §06's speed table, not merely be near it.

    Two statements, and both are about a clip lying to another system rather than about
    taste. ``PlayerAnimatorDriver`` plays a clip at ``groundSpeed / ReferenceSpeed``, so a
    clip authored at the wrong m/s is pure foot skate at the speed the game actually
    moves at — and §12 makes a footstep a positioning channel, so a foot that is not where
    the sound says it is lies about where its owner is.

    The CarryHeavy < Walk ordering that used to stand here went with the carry: §08's
    *"욕심이 곧 속도 저하"* has nothing left to burden.
    """
    by_name = {c.name: c for c in clips if c.speed > 0.0}
    walk, run, gun = by_name.get("Walk"), by_name.get("Run"), by_name.get("GunWalk")

    # GunWalk is Walk with one arm stopped, so it must MEASURE as Walk. A millimetre per
    # second is the solver's own residual (CADENCE_WON prints sole_err in mm); anything
    # past 10 mm/s means a change above the waist has reached the legs, and the symptom in
    # the game would be a runner who visibly changes step the instant they pick a gun up.
    if walk and gun and abs(gun.speed - walk.speed) > 0.01:
        blendkit.fail(
            f"GunWalk measures {gun.speed:.3f} m/s against Walk's {walk.speed:.3f}. The two "
            "share a gait table, a key order and a cadence search and differ only in the "
            "right arm, so a gap here means the arm has moved the legs — check that "
            "_cycle_body's right_held path is not being fed into the stance solve.")

    if walk and run and not walk.speed < run.speed:
        blendkit.fail(
            f"Walk measures {walk.speed:.2f} m/s and Run {run.speed:.2f}. §06 requires "
            "walking to be slower than running and GameConstants asserts it; the clips "
            "have to agree or the animation contradicts the movement.")


# ── Export ──────────────────────────────────────────────────────────────────


def export_fbx(rig: bpy.types.Object, body: bpy.types.Object, path: str) -> str:
    """Writes the rigged, animated FBX through ``blendkit.export_fbx``.

    **This used to be a hand-rolled export and the reason it was has expired.** The
    argument written here was: ``blendkit`` uses ``FBX_SCALE_NONE``, which parks the unit
    conversion on the root node as ``Lcl Scaling 100`` and is right for *rigged*
    characters, whereas *this mesh has no rig*, so ``FBX_SCALE_UNITS`` put the conversion
    in ``UnitScaleFactor`` and left a clean node scale of 1. The premise was the rigless
    mesh. It has a rig now, and three of blendkit's settings are not optional once it
    does:

    * ``add_leaf_bones=False`` — Blender otherwise appends a tip bone to every chain, and
      those tips arrive in Unity as real transforms nobody authored.
    * ``primary_bone_axis='Y'`` / ``secondary_bone_axis='X'`` — Unity's convention, and
      the axis the pose solver's rest directions are expressed along.
    * ``bake_anim_use_nla_strips=True`` with ``use_all_actions=False`` — every clip is
      stashed in its own NLA track, and turning both on writes each one twice.

    ``bake_space_transform`` goes with it, and that is a gain rather than a loss: Blender
    documents it as unreliable with armatures and animation, which is exactly the
    combination this file has just acquired. ``Player.fbx`` and ``Monster.fbx`` both ship
    through this path with ``useFileScale: 1`` in their importers, and ``Runner.fbx.meta``
    already carries the same setting — ``verify_roundtrip`` re-measures the written file
    either way.
    """
    return blendkit.export_fbx(path, objects=[rig, body], with_animation=True)


def verify_roundtrip(path: str, expect_bones: int) -> None:
    """Reads the FBX back and re-measures the height, the skeleton and the takes.

    The checks above measure the *scene*. This measures the **file**, which is the only
    thing Unity ever sees. It catches the two failures that survive every other gate here:
    a unit-scale mistake (a body written at 175 m passes every scene check) and a rig or
    an animation stack that was built correctly and then not written — which is the whole
    defect this revision exists to fix, so it is not left to be discovered in an importer.
    """
    before = {o.name for o in bpy.context.scene.objects}
    try:
        bpy.ops.import_scene.fbx(filepath=path, global_scale=1.0)
    except RuntimeError as exc:
        blendkit.fail(f"the FBX just written cannot be read back: {exc}")

    fresh = [o for o in bpy.context.scene.objects if o.name not in before]
    meshes = [o for o in fresh if o.type == "MESH"]
    rigs = [o for o in fresh if o.type == "ARMATURE"]
    if not meshes:
        blendkit.fail("the FBX just written contains no mesh.")
    if not rigs:
        blendkit.fail(
            "the FBX just written contains no armature. The body was skinned in the "
            "scene, so this is the export dropping it — check that the rig was in the "
            "selection and that object_types includes ARMATURE.")

    bones = sum(len(r.data.bones) for r in rigs)

    # A rig and a mesh in the same file is not a skinned mesh. The FBX has to carry the
    # DEFORMER too, and an export that dropped it produces exactly the artefact this
    # revision exists to remove: a body that plays a walk cycle and does not move.
    skinned = [o for o in meshes if o.vertex_groups
               and any(m.type == "ARMATURE" for m in o.modifiers)]
    if not skinned:
        blendkit.fail(
            "the FBX just written has an armature and a mesh but no skin binding them: the "
            "re-imported mesh carries "
            f"{sum(len(o.vertex_groups) for o in meshes)} vertex groups and "
            f"{sum(1 for o in meshes for m in o.modifiers if m.type == 'ARMATURE')} "
            "armature modifiers. Unity would import a rig whose animation drives nothing "
            "and a body that stands still through every clip.")

    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for o in meshes:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])

    height = hi.z - lo.z
    print(f"FBX_ROUNDTRIP meshes={len(meshes)} armatures={len(rigs)} bones={bones} "
          f"skinned={len(skinned)} groups={sum(len(o.vertex_groups) for o in skinned)} "
          f"height={height:.4f}m error={(height - TARGET_HEIGHT) * 1000.0:+.3f}mm "
          f"scale_mode=FBX_SCALE_NONE")

    if abs(height - TARGET_HEIGHT) > 0.002:
        blendkit.fail(
            f"the exported file reads back at {height:.4f} m, not {TARGET_HEIGHT:.3f}. "
            "The scene was the right size, so this is the export's unit handling — check "
            "apply_scale_options / apply_unit_scale before touching the geometry.")
    if bones != expect_bones:
        blendkit.fail(
            f"the exported file reads back with {bones} bones, not {expect_bones}. The "
            "usual cause is add_leaf_bones, which appends a tip bone per chain.")

    for o in fresh:
        bpy.data.objects.remove(o, do_unlink=True)


def verify_takes(path: str, clips: list[gpm.Clip]) -> None:
    """Every clip must be an animation stack IN THE FILE, named the way Unity will list it.

    Read out of the FBX's own bytes rather than out of the Blender session, because the
    session is not what ships. ``PlayerFeelHarnessMenu.AssignClips`` loads every sub-asset
    of ``Runner.fbx`` and looks each one up **by name** — ``Idle``, ``Walk``, ``Run`` … —
    so a take that arrives prefixed, duplicated or missing is a null clip field, and a
    null clip field is a pose the body freezes in.
    """
    takes = gpm.fbx_objects(path, b"AnimStack")
    clean = sorted(t for t in takes if "|" not in t)
    print(f"FBX_TAKES count={len(takes)} unprefixed={len(clean)} names={','.join(clean)}")

    dropped = [c.name for c in clips if c.name not in takes]
    if dropped:
        blendkit.fail(
            "clips missing from the FBX: " + ", ".join(dropped) + " — they were built in "
            "the scene and not stashed into NLA tracks, so the exporter wrote only the "
            "active action.")

    duplicates = [t for t in takes if "|" in t]
    if duplicates:
        blendkit.fail(
            f"{len(duplicates)} takes arrived with a rig prefix ({','.join(sorted(duplicates))}). "
            "Unity imports those as extra clips beside the clean ones, and AssignClips "
            "looks up bare names, so half the animation in the file is unreachable.")

    models = gpm.fbx_objects(path, b"Model")
    absent = [b.name for b in bpy.data.objects[RIG_NAME].data.bones if b.name not in models]
    if absent:
        blendkit.fail("bones missing from the FBX hierarchy: " + ", ".join(absent))
    print(f"FBX_HIERARCHY models={len(models)} root={RIG_NAME} mesh={MESH_NAME}")


# ── Entry point ─────────────────────────────────────────────────────────────


def main() -> None:
    """Builds the figure, rigs it, proves what has to be true, and exports it.

    The order is not arrangeable. The preflight runs before any geometry so a bad
    placement table costs a second instead of a remesh; the height is fitted after the
    smoothing because the smoothing is what changes it; **the rig is built after the fit**,
    because the fit's scale is not a number anyone can look up — it is however much the
    smoothing shrank the union — and a skeleton authored before it would be a skeleton
    inside a body of a different size; and every verification runs before the export, so a
    failure never leaves a wrong ``Runner.fbx`` in the Unity project for somebody else's
    scene to pick up.
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
    blendkit.set_frame_range(0, DEATH_END_FRAME + 1)

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

    fit = fit_height_and_ground(body, TARGET_HEIGHT)
    print(f"RUNNER_FIT scale={fit.scale:.5f}x drop={fit.drop:.5f}m "
          f"(the smoothing shrinks the union, so the height is solved after it, "
          f"never authored into the table)")

    verify_one_shell(body)
    # The ceilings are the joints themselves: below the ankle the only way across is the
    # floor, and below the shoulder ball the only way across is the chest.
    # The crotch ceiling is the belly's own underside: above it the legs are allowed to
    # be one mass, because that mass is the pelvis. Below it they are two legs.
    hip_z, ankle_z = fit.z(LEG_Z_TOP), fit.z(LEG_Z_BOTTOM)
    verify_limbs_hang_free(
        body, ankle_z=ankle_z, crotch_z=fit.z(BELLY_C[2] - BELLY_R[2]), hip_z=hip_z,
        # The lower two thirds of a leg has to be a leg. The top third is inside the
        # pelvis, where being one mass is the point.
        leg_part_z=hip_z - LEG_PART_FRACTION * (hip_z - ankle_z),
        armpit_z=fit.z(SHOULDER_Z - SHOULDER_R),
        armpit_x=fit.d(SHOULDER_X), armpit_r=fit.d(SHOULDER_R))
    size = verify_height(body)
    print(f"RUNNER_SHAPE height={size[2]:.3f}m span={size[0]:.3f}m depth={size[1]:.3f}m "
          f"tris={_tris(body)} verts={len(body.data.vertices)}")
    report_breadth(body, size)

    # ── The rig, and the clips ──────────────────────────────────────────────
    skeleton = build_skeleton(body, fit)
    retarget_gait_solver(skeleton)

    # The arm, and the two things that ride on it: the held pose has to be a silhouette,
    # and RunnerGun.cs has to be holding the ratio this skeleton just produced. Both run
    # before the clips are built so a rig that cannot carry a gun fails in a second rather
    # than after a cadence search.
    arm_len, mount_ratio = report_arm(skeleton)
    verify_gun_pose(skeleton, arm_len, size[1] * 0.5)
    verify_gun_mount(mount_ratio)

    rig = build_rig(skeleton, body)
    verify_skin(rig, body)

    clips: list[gpm.Clip] = []
    for build in CLIP_BUILDERS:
        clip = build(rig)
        # Before the action is keyed, not after: an action is the poses frozen, so a key
        # corrected afterwards is a correction that exists only in this script's memory.
        if not clip.loop:
            settle_on_floor(rig, body, clip)
        clip.action = blendkit.make_action(rig, clip.name, clip.poses, loop=clip.loop)
        clips.append(clip)

    missing = [n for n in CLIP_NAMES if n not in {c.name for c in clips}]
    if missing:
        blendkit.fail(
            "PlayerAnimatorDriver has no clip for " + ", ".join(missing) + ". A null clip "
            "field leaves that state with nowhere to put its weight, so AdvanceWeights "
            "bails and the body freezes in whatever pose it was last in.")
    verify_clip_speeds(clips)
    verify_floor(rig, body, clips)
    verify_skin_stretch(rig, body, clips)
    for clip in clips:
        pose_measure(rig, clip)
    gpm.clear_pose(rig)

    for clip in clips:
        stats = gpm.measure_action(clip.action)
        extra = ""
        if clip.speed > 0.0:
            extra = (f" stance={clip.stance_travel:.3f}m/{clip.stance_seconds:.3f}s"
                     f" speed={clip.speed:.2f}m/s")
        if clip.hip_hi:
            extra += (f" hip_z={clip.hip_lo:.3f}-{clip.hip_hi:.3f}m"
                      f" bob={clip.hip_hi - clip.hip_lo:.3f}m"
                      f" sole_err={clip.sole_error * 1000.0:.2f}mm")
        print(f"ANIM_REPORT {clip.name:11s} frames={stats['start']}-{stats['end']} "
              f"({stats['frames']:3d}f, {stats['seconds']:.2f}s) loop={int(clip.loop)} "
              f"curves={stats['curves']} keys={stats['keys']:4d} "
              f"max_bone_motion={stats['max_deg']:6.2f}deg "
              f"hipswing={stats['per_bone'].get('LeftUpperLeg', 0.0):6.2f}deg"
              f"{extra}  # {clip.note}")

    # Rendered while the actions are still live on the rig, because stashing clears them.
    # Env-gated and never part of a production run, for gen_player_model's reason: a
    # headless generator that cannot be LOOKED at gets its poses wrong silently, and a
    # figure whose whole job is DESCENT-PIVOT §5's silhouette is not a thing numbers alone
    # can sign off. Shot per clip by NAME rather than by prefix, so a "GunIdle" render
    # cannot photograph GunWalk's pose because one name starts with the other.
    preview_dir = os.environ.get("HORROR_RUNNER_PREVIEW_DIR")
    if preview_dir:
        rig.animation_data.action = None
        gpm.clear_pose(rig)
        for p in gpm.render_previews(rig, body, preview_dir, [
                ("00_rest_front", 0, (0.0, -4.4, 1.05), (0.0, 0.0, 0.95)),
                ("00_rest_side", 0, (4.2, -0.2, 1.05), (0.0, 0.0, 0.95))]):
            print(f"PREVIEW {p}")
        for clip in clips:
            rig.animation_data.action = clip.action
            for offset in (0, max(1, clip.cycle_frames // 4)):
                f = clip.measure_frame + offset
                for p in gpm.render_previews(rig, body, preview_dir, [
                        (f"{clip.name}_{offset}_side", f, (4.0, -0.4, 1.0), (0.0, -0.05, 0.85)),
                        (f"{clip.name}_{offset}_front", f, (0.0, -3.8, 1.10), (0.0, -0.05, 0.90))]):
                    print(f"PREVIEW {p}")
        rig.animation_data.action = None

    for clip in clips:
        blendkit.stash_action(rig, clip.action)

    # Each clip must stand alone in Unity, and NOTHING extrapolation is what makes frame 0
    # the unposed rest pose — so the mesh cannot be exported mid-pose.
    for track in rig.animation_data.nla_tracks:
        for strip in track.strips:
            strip.extrapolation = "NOTHING"
            strip.blend_in = 0.0
            strip.blend_out = 0.0
    rig.animation_data.action = None
    gpm.clear_pose(rig)
    bpy.context.scene.frame_set(0)

    rest_hip = gpm.bone_point(rig, "Hips", (0.0, 0.0, 0.0)).z
    if abs(rest_hip - skeleton.hip_z) > 0.001:
        blendkit.fail(
            f"the rig is not at rest before export: the hip sits at {rest_hip:.4f} m "
            f"instead of {skeleton.hip_z:.4f}. Unity reads the bind pose out of the file, "
            "and a bind pose left mid-clip is the pose the model shows before anything "
            "plays it.")
    print(f"BIND_POSE rest_confirmed hip_z={rest_hip:.4f}m frame=0")

    if "--no-export" in argv:
        print("NO_EXPORT built and checked; nothing written")
        return

    export_fbx(rig, body, out)
    report = blendkit.describe(out)
    blendkit.assert_asset(report, min_vertices=200, max_triangles=8000,
                          max_dimension=3.0, expect_bones=len(bone_specs(skeleton)),
                          exact_actions=len(CLIP_NAMES))
    blendkit.print_report(report)
    if report.bones <= 0 or report.actions <= 0:
        blendkit.fail(
            f"the asset reports bones={report.bones} actions={report.actions}. That pair "
            "is the whole defect this revision exists to remove — a mesh with neither is "
            "what PlayerAnimatorDriver has been handed, and it is why the body slides.")

    if "--glb" in argv:
        # Into artifacts/, never beside the FBX. docs/ASSETS.md: *"The .glb copies are for
        # eyeballing in a viewer and should **not** be imported into Unity — importing both
        # formats would give you two Avatars for the same character."* Writing it into
        # Assets/ does exactly that, silently, the next time the editor focuses. The
        # monster and the player both keep theirs under artifacts/ for this reason.
        glb = os.path.join(REPO_ROOT, "artifacts", "runner", "Runner.glb")
        os.makedirs(os.path.dirname(glb), exist_ok=True)
        blendkit.export_gltf(glb, with_animation=True)
        anims = gpm.glb_animations(glb)
        print(f"GLB_ANIMATIONS count={len(anims)} " + " ".join(
            f"{a['name']}={a['seconds']:.2f}s/{a['channels']}ch" for a in anims))
        print(f"PREVIEW {glb}")

    verify_takes(out, clips)
    verify_roundtrip(out, expect_bones=len(bone_specs(skeleton)))

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
