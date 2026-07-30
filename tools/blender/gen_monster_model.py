#!/usr/bin/env python3
"""Generates the monster: mesh, rig and one animation per §06 state.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_monster_model.py

Outputs ``Assets/Models/Characters/Monster.fbx`` (+ ``Monster.glb`` for eyeballing).

WHY THIS ASSET EXISTS
---------------------
§16-1 lists the monster model and animations as the project's hidden bottleneck and
says of it and the player model: **"이 둘은 우회가 안 된다"** — everything else can be
worked around with darkness, first person and modular kits; these two cannot. This
generator is the answer: the monster is produced by script, so it is reproducible,
reviewable as a diff, and cheap to retune when §06/§07 numbers move.

WHAT THE DESIGN DICTATES ABOUT ITS SHAPE
----------------------------------------
* §01 — **there is no way to kill it.** Every counter is temporary (flash, doors,
  traps). A creature that cannot lose has no reason to hurry, and the silhouette
  says so: limbs long enough to cross a corridor in two strides, a stoop that never
  straightens, and a resting pose that is already mid-step.
* §06 — it is only **0.3 m/s faster than a running player** (4.8 vs 4.5). It must
  read as *unbothered*, not frantic — the horror is that it is barely faster and
  still uncatchable. Patrol is therefore slower than a walking player (§05: 2.0 m/s).
* §06 — **정지 상태가 이 게임의 무기.** When it stops it makes no sound, the Listener
  (§04 청음사) loses it, and the team walks into it. `Standstill` is authored as
  *measured* stillness, not as an idle: no breathing, no weight shift, feet welded,
  one 4.5° head roll in three seconds. The script asserts that number.
* §05/§12 — the game is dark and first person, so triangles buy nothing. The budget
  here is 6000 and the mesh uses a fraction of it. Silhouette does all the work.
* §12 — corridors are the arena, so shoulder width is a hard constraint, not taste.
  The check below keeps the span at 0.93 m while the creature stands 2.34 m tall.

HOW IT IS DELIBERATELY WRONG
----------------------------
Not a person with a monster texture. The distortions are structural:

* **An extra elbow.** Each arm is UpperArm → LowerArm → ForearmExtra → Hand. Four
  segments, 1.78 m of reach from a 0.93 m shoulder span, so the fingertips hang
  6 cm off the floor in the rest pose.
* **Digitigrade legs.** Femur forward, shin swept *back*, then a 0.51 m metatarsal —
  so the knee appears to bend the wrong way and the ankle rides at 0.48 m.
* **A head that is not a head.** Two eyeless blade halves separated by a vertical
  slot, of slightly different size, plus a mandible that hangs to mid-chest and
  swings *forward* to gape. Nothing to make eye contact with.
* **Asymmetry.** Left shoulder hiked with a scapular spur; right shoulder dropped
  with three exposed rib shards. The skeleton stays mirror-symmetric so a Unity
  Humanoid avatar can still be built; only the mesh is lopsided.
* **A dorsal crest** that lies folded on Patrol and flares on Alert — the only
  visible tell that it has heard something, which is what §06's Alert state is.

BONE NAMES
----------
Unity's Humanoid names are used wherever a bone corresponds (Hips, Spine, Chest,
UpperChest, Neck, Head, Left/Right UpperArm, LowerArm, Hand, UpperLeg, LowerLeg,
Foot, Toes, Shoulder). The wrongness lives in extras that Humanoid ignores:
ForearmExtra, Jaw, Crest1-3, LeftScapulaSpur. Extra links inside a chain are legal
for Humanoid mapping — Hand is still a descendant of LowerArm.

Note: the rest pose is an **arms-down** pose, not a T-pose. It has to be — arms
1.78 m long held out sideways would make the asset 4.4 m wide and blow the
`max_dimension` unit-scale check. Unity's "Enforce T-Pose" handles it, or use a
Generic rig, which is all this monster needs since it ships its own clips.

MEASURED EXPORT CHARACTERISTICS
-------------------------------
Two properties of ``blendkit.export_fbx`` were measured by re-importing the exported
file and diffing it against the source scene. Both are blendkit-wide (they will hit
the player model too), both are cosmetic, and neither is patched here because this
generator does not own that file.

1. **Every clip is exported twice.** blendkit passes ``bake_anim_use_nla_strips=True``
   *and* ``bake_anim_use_all_actions=True``. In Blender's exporter those are two
   independent ``if`` blocks (``export_fbx_bin.py`` :2479 and :2522), so a stashed
   action is written once by the NLA pass under its own name and again by the
   all-actions pass as ``Monster_Rig|<name>`` — 14 takes for 7 actions, verified by
   parsing the FBX. The curves are identical; use the unprefixed clips. One-line fix:
   ``bake_anim_use_all_actions=False``, since stash_action already guarantees the NLA
   pass covers every action. (blendkit's docstring says an unstashed action is dropped
   — that is true of Blender's default settings, not of blendkit's, which would still
   export it via the all-actions pass under the prefixed name.)

2. **Baked curves are lossily simplified.** ``bake_anim_simplify_factor`` is left at
   the operator default of 1.0, which decimates baked keys with up to a 10-frame step.
   Sampling every probe bone on every frame of every clip, the worst round-trip
   positional error was **11.9 mm** (a toe tip) before this generator keyed its gait
   cycles per-frame, and **3.9 mm** (a hand tip) after. Re-exporting the identical
   scene with ``bake_anim_simplify_factor=0.0`` gives **0.00 mm** on every frame, which
   isolates the cause beyond doubt. Standstill measures 0.00 mm either way — flat
   curves simplify losslessly — so §06's silent state is unaffected regardless.
"""

from __future__ import annotations

import json
import math
import os
import re
import struct
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import blendkit  # noqa: E402
from blendkit import BoneSpec, MaterialSpec, Pose  # noqa: E402


# ── Proportions, in metres ──────────────────────────────────────────────────
# 1 Blender unit = 1 metre = 1 Unity unit. The model faces -Y (Blender), which
# export_fbx's axis_forward='-Z' turns into +Z forward in Unity. Do not add a
# rotation to compensate.

FPS = 30

HIP_Z = 1.36          # hip pivot height — 58% of total height (a person is ~52%)
KNEE_Z = 0.82
ANKLE_Z = 0.48        # digitigrade: the "heel" never touches the floor
BALL_Z = 0.07
TOE_Z = 0.03
LEG_X = 0.195

SHOULDER_Z = 1.96
YOKE_X = 0.30         # half-width of the fixed yoke; the clavicles carry the span out


def _lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


# ── Mesh helpers ────────────────────────────────────────────────────────────

PARTS: list[tuple[bpy.types.Object, str, str]] = []
"""(object, bone it is rigidly weighted to, material name)."""


def part(obj: bpy.types.Object, bone: str, mat: str) -> bpy.types.Object:
    """Registers a piece so it gets a vertex group, a material and gets joined.

    Weighting is rigid — one bone per piece, weight 1.0. Auto weights would be a
    coin toss on a mesh with floating rib shards and crest blades, and a doll-like
    rigid skin suits something that is not supposed to move like flesh.
    """
    PARTS.append((obj, bone, mat))
    return obj


def _mesh_object(name: str, bm: bmesh.types.BMesh) -> bpy.types.Object:
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def _ring_frame(axis: Vector) -> tuple[Vector, Vector]:
    axis = axis.normalized()
    ref = Vector((0.0, 0.0, 1.0)) if abs(axis.z) < 0.95 else Vector((1.0, 0.0, 0.0))
    u = axis.cross(ref).normalized()
    return u, axis.cross(u).normalized()


def tube(name, p0, p1, r0, r1, sides=8) -> bpy.types.Object:
    """A tapered tube between two points. Every limb segment is one of these.

    Segments overlap their joints and a sphere sits at each major joint, so rigid
    weighting cannot open a gap when the bone rotates.
    """
    p0v, p1v = Vector(p0), Vector(p1)
    u, v = _ring_frame(p1v - p0v)
    bm = bmesh.new()
    ring0, ring1 = [], []
    for i in range(sides):
        a = 2.0 * math.pi * i / sides
        d = u * math.cos(a) + v * math.sin(a)
        ring0.append(bm.verts.new(p0v + d * r0))
        ring1.append(bm.verts.new(p1v + d * r1))
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new((ring0[i], ring0[j], ring1[j], ring1[i]))
    bm.faces.new(ring0)
    bm.faces.new(ring1)
    return _mesh_object(name, bm)


def hull(name, points) -> bpy.types.Object:
    """Convex hull of a point cloud. Used for every faceted, bladed piece."""
    bm = bmesh.new()
    for p in points:
        bm.verts.new(Vector(p))
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    # The two result lists overlap, and bmesh.ops.delete rejects duplicates outright.
    seen: set[int] = set()
    junk = []
    for el in res.get("geom_unused", []) + res.get("geom_interior", []):
        if id(el) not in seen:
            seen.add(id(el))
            junk.append(el)
    if junk:
        bmesh.ops.delete(bm, geom=junk, context="VERTS")
    return _mesh_object(name, bm)


def mirrored(points, y_scale=1.0, z_shift=0.0):
    """Mirrors a point list in X, optionally distorting it.

    The two head halves are built from the same cloud with a different `y_scale`
    and `z_shift`, which is what stops the face from being a face.
    """
    return [(-x, y * y_scale, z + z_shift) for (x, y, z) in points]


# ── Geometry ────────────────────────────────────────────────────────────────

FLESH = "Monster_Flesh"
CARAPACE = "Monster_Carapace"
MAW = "Monster_Maw"


def build_torso() -> None:
    """Pelvis, three spine segments and the shoulder yoke.

    The torso is long and narrow so the shoulders sit at 1.96 m — above the head of
    any player standing in front of it. §12's corridors are the stage, so it gains its
    presence vertically, never in width.
    """
    part(hull("Pelvis", [
        (0.21, 0.13, 1.30), (-0.21, 0.13, 1.30), (0.21, -0.12, 1.32), (-0.21, -0.12, 1.32),
        (0.17, 0.11, 1.46), (-0.17, 0.11, 1.46), (0.17, -0.11, 1.46), (-0.17, -0.11, 1.46),
        (0.13, 0.02, 1.19), (-0.13, 0.02, 1.19),
    ]), "Hips", FLESH)

    part(tube("Spine_Seg", (0.0, 0.02, 1.38), (0.0, -0.01, 1.66), 0.165, 0.150, 10), "Spine", FLESH)
    part(tube("Chest_Seg", (0.0, -0.01, 1.63), (0.0, 0.0, 1.88), 0.150, 0.185, 10), "Chest", FLESH)
    part(tube("UpperChest_Seg", (0.0, 0.0, 1.85), (0.0, 0.01, 2.00), 0.185, 0.140, 10),
         "UpperChest", FLESH)

    # Shoulder yoke — a fixed bar of bone across the top of the chest, weighted to
    # UpperChest. The clavicles (build_arm) carry the span out from here, so the
    # Shoulder bones have geometry of their own to move.
    part(hull("Yoke", [
        (YOKE_X, 0.07, SHOULDER_Z - 0.02), (-YOKE_X, 0.07, SHOULDER_Z - 0.02),
        (YOKE_X, -0.06, SHOULDER_Z + 0.01), (-YOKE_X, -0.06, SHOULDER_Z + 0.01),
        (0.10, 0.09, SHOULDER_Z + 0.10), (-0.10, 0.09, SHOULDER_Z + 0.10),
        (0.10, -0.08, SHOULDER_Z + 0.08), (-0.10, -0.08, SHOULDER_Z + 0.08),
        (YOKE_X - 0.06, 0.0, SHOULDER_Z - 0.11), (-(YOKE_X - 0.06), 0.0, SHOULDER_Z - 0.11),
    ]), "UpperChest", FLESH)

    # Right side only: three rib shards pushing out through the skin. The left side
    # gets the scapular spur instead. Asymmetry without touching the skeleton.
    for i, z in enumerate((1.62, 1.73, 1.83)):
        part(hull(f"RibShard_{i}", [
            (-0.13, 0.04, z), (-0.13, -0.05, z), (-0.13, 0.0, z + 0.055),
            (-0.245 - 0.012 * i, 0.01, z + 0.02), (-0.235, -0.02, z - 0.015),
        ]), "Chest", CARAPACE)


def build_head() -> None:
    """Neck, the two head blades, the hanging mandible and the crest.

    "A head that is not quite a head": two eyeless wedges of *different* size with a
    3.6 cm slot between them, and a mandible reaching to mid-chest. There is no face
    to read and nothing to make eye contact with — which is the point, because §06's
    Alert state is the only moment it shows intent, and it shows it with the crest.
    """
    part(tube("Neck_Seg", (0.0, 0.0, 1.97), (0.0, -0.04, 2.15), 0.080, 0.068, 8), "Neck", FLESH)

    blade = [
        (0.018, 0.03, 2.04), (0.105, 0.04, 2.07),
        (0.018, 0.00, 2.30), (0.092, 0.01, 2.27),
        (0.018, -0.21, 2.36), (0.062, -0.20, 2.33),
        (0.018, -0.31, 2.13), (0.048, -0.30, 2.14),
        (0.015, -0.375, 2.24),
    ]
    left = hull("HeadBlade_L", blade)
    blendkit.bevel(left, width=0.006, segments=1)
    part(left, "Head", CARAPACE)

    # Same cloud, 7% deeper and 2 cm lower: the halves do not match.
    right = hull("HeadBlade_R", mirrored(blade, y_scale=1.07, z_shift=-0.02))
    blendkit.bevel(right, width=0.006, segments=1)
    part(right, "Head", CARAPACE)

    # The mandible hangs to mid-chest and gapes by swinging forward, so opening it
    # pulls the whole head apart vertically instead of dropping a chin.
    jaw = hull("Mandible", [
        (0.072, -0.04, 2.06), (-0.072, -0.04, 2.06),
        (0.066, -0.27, 2.06), (-0.066, -0.27, 2.06),
        (0.034, -0.31, 1.88), (-0.034, -0.31, 1.88),
        (0.040, -0.01, 1.90), (-0.040, -0.01, 1.90),
        (0.013, -0.19, 1.70), (-0.013, -0.19, 1.70),
    ])
    blendkit.bevel(jaw, width=0.006, segments=1)
    part(jaw, "Jaw", MAW)

    # Dorsal crest: three chained blades behind the shoulders. Folded on Patrol,
    # flared on Alert — the creature's only visible "I heard that".
    crest_pts = [
        ((0.0, 0.16, 1.90), (0.0, 0.245, 2.06), 0.055, "Crest1"),
        ((0.0, 0.245, 2.04), (0.0, 0.305, 2.19), 0.045, "Crest2"),
        ((0.0, 0.305, 2.17), (0.0, 0.325, 2.30), 0.032, "Crest3"),
    ]
    for i, (p0, p1, half, bone) in enumerate(crest_pts):
        b = hull(f"CrestBlade_{i}", [
            (0.016, p0[1] - half, p0[2]), (-0.016, p0[1] - half, p0[2]),
            (0.016, p0[1] + half, p0[2]), (-0.016, p0[1] + half, p0[2]),
            (0.008, p1[1] - half * 0.5, p1[2]), (-0.008, p1[1] - half * 0.5, p1[2]),
            (0.008, p1[1] + half * 0.4, p1[2]), (-0.008, p1[1] + half * 0.4, p1[2]),
        ])
        blendkit.bevel(b, width=0.005, segments=1)
        part(b, bone, CARAPACE)
        # A shorter blade to each side of the first two, so the crest has volume.
        if i < 2:
            for s in (1, -1):
                part(hull(f"CrestSide_{i}_{s}", [
                    (s * 0.075, p0[1] - half * 0.8, p0[2] - 0.02),
                    (s * 0.100, p0[1] - half * 0.6, p0[2] - 0.01),
                    (s * 0.075, p0[1] + half * 0.8, p0[2] - 0.02),
                    (s * 0.070, _lerp(p0[1], p1[1], 0.7), _lerp(p0[2], p1[2], 0.7)),
                    (s * 0.095, _lerp(p0[1], p1[1], 0.5), _lerp(p0[2], p1[2], 0.4)),
                ]), bone, CARAPACE)

    # Left scapular spur — a hooked blade sweeping up and back off one shoulder.
    spur = hull("ScapulaSpur", [
        (0.185, 0.04, 1.91), (0.305, 0.05, 1.95), (0.195, 0.15, 1.99),
        (0.265, 0.19, 2.09), (0.320, 0.16, 2.05),
        (0.250, 0.265, 2.17), (0.300, 0.245, 2.14),
    ])
    blendkit.bevel(spur, width=0.005, segments=1)
    part(spur, "LeftScapulaSpur", CARAPACE)


def build_arm(side: int) -> None:
    """One four-segment arm. §01: it cannot be killed, so it never needs to lunge —
    it simply reaches. 1.78 m of arm on a 0.93 m frame puts the fingertips 6 cm off
    the floor in the rest pose — it does not have to bend down to take you."""
    s = "Left" if side > 0 else "Right"
    x = side

    sh = (x * 0.32, 0.0, SHOULDER_Z)
    elbow = (x * 0.375, 0.03, 1.38)
    elbow2 = (x * 0.395, -0.03, 0.86)
    wrist = (x * 0.405, 0.03, 0.44)
    tip = (x * 0.405, -0.02, 0.20)

    # Clavicle: the only geometry the Shoulder bone owns. Without it the shrug in
    # every pose (left hiked, right collapsed) would deform nothing.
    part(tube(f"{s}Clavicle", (x * 0.10, 0.0, 1.945), (x * 0.315, 0.0, SHOULDER_Z),
              0.108, 0.100, 8), f"{s}Shoulder", FLESH)
    part(blendkit.add_sphere(f"{s}ShoulderBall", 0.115, sh, segments=8, rings=5),
         f"{s}Shoulder", FLESH)
    part(tube(f"{s}UpperArm_Seg", sh, elbow, 0.098, 0.076, 8), f"{s}UpperArm", FLESH)

    part(blendkit.add_sphere(f"{s}ElbowBall", 0.086, elbow, segments=8, rings=5),
         f"{s}LowerArm", FLESH)
    part(tube(f"{s}LowerArm_Seg", elbow, elbow2, 0.076, 0.062, 8), f"{s}LowerArm", FLESH)

    # The second elbow. There is no anatomical reason for it; that is the reason.
    part(blendkit.add_sphere(f"{s}ElbowBall2", 0.070, elbow2, segments=8, rings=5),
         f"{s}ForearmExtra", CARAPACE)
    part(tube(f"{s}Forearm_Seg", elbow2, wrist, 0.062, 0.048, 8), f"{s}ForearmExtra", FLESH)

    part(blendkit.add_sphere(f"{s}WristBall", 0.054, wrist, segments=8, rings=5),
         f"{s}Hand", FLESH)
    part(tube(f"{s}Palm", wrist, tip, 0.048, 0.036, 8), f"{s}Hand", FLESH)

    # Three long fingers, no thumb. They reach past the ankle to z ≈ 0.06.
    for i, dx in enumerate((-0.030, 0.0, 0.030)):
        base = (tip[0] + x * dx, tip[1] + 0.012, tip[2] + 0.01)
        knuckle = (tip[0] + x * dx * 1.5, tip[1] - 0.035, tip[2] - 0.075)
        end = (tip[0] + x * dx * 1.7, tip[1] - 0.075, 0.062)
        part(tube(f"{s}Finger{i}a", base, knuckle, 0.020, 0.014, 6), f"{s}Hand", FLESH)
        part(tube(f"{s}Finger{i}b", knuckle, end, 0.014, 0.005, 6), f"{s}Hand", CARAPACE)


def build_leg(side: int) -> None:
    """One digitigrade leg.

    Femur forward, shin swept back, then a 0.51 m metatarsal — the knee reads as
    bending backwards. Hip-to-toe strut length is 1.38 m, and that number is what
    the walk and run cycles are sized against (see `stride_report`).
    """
    s = "Left" if side > 0 else "Right"
    x = side

    hip = (x * 0.17, 0.0, HIP_Z)
    knee = (x * LEG_X, -0.11, KNEE_Z)
    ankle = (x * LEG_X, 0.15, ANKLE_Z)
    ball = (x * LEG_X, -0.15, BALL_Z)
    toe = (x * LEG_X, -0.36, TOE_Z)

    part(blendkit.add_sphere(f"{s}HipBall", 0.130, hip, segments=8, rings=5),
         f"{s}UpperLeg", FLESH)
    part(tube(f"{s}UpperLeg_Seg", hip, knee, 0.118, 0.088, 10), f"{s}UpperLeg", FLESH)

    part(blendkit.add_sphere(f"{s}KneeBall", 0.096, knee, segments=8, rings=5),
         f"{s}LowerLeg", CARAPACE)
    part(tube(f"{s}LowerLeg_Seg", knee, ankle, 0.088, 0.062, 10), f"{s}LowerLeg", FLESH)

    part(blendkit.add_sphere(f"{s}AnkleBall", 0.064, ankle, segments=8, rings=5),
         f"{s}Foot", CARAPACE)
    part(tube(f"{s}Foot_Seg", ankle, ball, 0.062, 0.046, 8), f"{s}Foot", FLESH)

    # Toe pad plus three claws. §12 makes footstep material a gameplay channel, so
    # the contact surface is a single flat pad — one clean impact per step.
    part(hull(f"{s}ToePad", [
        (x * (LEG_X - 0.055), -0.09, BALL_Z + 0.03), (x * (LEG_X + 0.055), -0.09, BALL_Z + 0.03),
        (x * (LEG_X - 0.060), -0.20, BALL_Z + 0.02), (x * (LEG_X + 0.060), -0.20, BALL_Z + 0.02),
        (x * (LEG_X - 0.050), -0.15, TOE_Z - 0.02), (x * (LEG_X + 0.050), -0.15, TOE_Z - 0.02),
    ]), f"{s}Toes", FLESH)
    for i, dx in enumerate((-0.055, 0.0, 0.055)):
        base = (x * (LEG_X + dx), -0.18, BALL_Z)
        part(tube(f"{s}Claw{i}", base, (base[0], toe[1] - 0.02 * abs(i - 1), TOE_Z - 0.005),
                  0.024, 0.006, 6), f"{s}Toes", CARAPACE)


# ── Rig ─────────────────────────────────────────────────────────────────────

def bone_specs() -> list[BoneSpec]:
    """The 29-bone skeleton. Unity Humanoid names wherever a bone corresponds."""
    specs = [
        BoneSpec("Hips", (0.0, 0.0, HIP_Z), (0.0, 0.0, HIP_Z + 0.09)),
        BoneSpec("Spine", (0.0, 0.01, 1.40), (0.0, 0.0, 1.66), "Hips"),
        BoneSpec("Chest", (0.0, 0.0, 1.66), (0.0, 0.0, 1.88), "Spine", True),
        BoneSpec("UpperChest", (0.0, 0.0, 1.88), (0.0, 0.0, 2.00), "Chest", True),
        # A long neck that cranes the head out ahead of the body.
        BoneSpec("Neck", (0.0, 0.0, 1.99), (0.0, -0.03, 2.14), "UpperChest"),
        BoneSpec("Head", (0.0, -0.03, 2.14), (0.0, -0.03, 2.32), "Neck", True),
        # Points down, so swinging it forward gapes the maw open.
        BoneSpec("Jaw", (0.0, -0.06, 2.05), (0.0, -0.10, 1.86), "Head"),
    ]

    crest = [
        ("Crest1", (0.0, 0.16, 1.90), (0.0, 0.245, 2.06), "UpperChest"),
        ("Crest2", (0.0, 0.245, 2.06), (0.0, 0.305, 2.19), "Crest1"),
        ("Crest3", (0.0, 0.305, 2.19), (0.0, 0.325, 2.30), "Crest2"),
    ]
    for name, head, tail, parent in crest:
        specs.append(BoneSpec(name, head, tail, parent, name != "Crest1"))

    specs.append(BoneSpec("LeftScapulaSpur", (0.20, 0.07, 1.94), (0.29, 0.30, 2.22), "UpperChest"))

    for side in (1, -1):
        s = "Left" if side > 0 else "Right"
        x = side
        specs += [
            BoneSpec(f"{s}Shoulder", (x * 0.09, 0.0, 1.94), (x * 0.32, 0.0, SHOULDER_Z),
                     "UpperChest"),
            BoneSpec(f"{s}UpperArm", (x * 0.32, 0.0, SHOULDER_Z), (x * 0.375, 0.03, 1.38),
                     f"{s}Shoulder", True),
            BoneSpec(f"{s}LowerArm", (x * 0.375, 0.03, 1.38), (x * 0.395, -0.03, 0.86),
                     f"{s}UpperArm", True),
            BoneSpec(f"{s}ForearmExtra", (x * 0.395, -0.03, 0.86), (x * 0.405, 0.03, 0.44),
                     f"{s}LowerArm", True),
            BoneSpec(f"{s}Hand", (x * 0.405, 0.03, 0.44), (x * 0.405, -0.02, 0.20),
                     f"{s}ForearmExtra", True),
            BoneSpec(f"{s}UpperLeg", (x * 0.17, 0.0, HIP_Z), (x * LEG_X, -0.11, KNEE_Z), "Hips"),
            BoneSpec(f"{s}LowerLeg", (x * LEG_X, -0.11, KNEE_Z), (x * LEG_X, 0.15, ANKLE_Z),
                     f"{s}UpperLeg", True),
            BoneSpec(f"{s}Foot", (x * LEG_X, 0.15, ANKLE_Z), (x * LEG_X, -0.15, BALL_Z),
                     f"{s}LowerLeg", True),
            BoneSpec(f"{s}Toes", (x * LEG_X, -0.15, BALL_Z), (x * LEG_X, -0.36, TOE_Z),
                     f"{s}Foot", True),
        ]
    return specs


# ── Posing in world terms ───────────────────────────────────────────────────
# blendkit.Pose takes bone-LOCAL XYZ euler degrees, and a bone's local axes depend
# on its rest direction and roll. Authoring in local degrees would mean guessing a
# different convention per bone. Instead every pose is written as a rotation about
# world axes and converted here:
#
#     matrix_basis = M⁻¹ · R_world · M      (M = the bone's rest matrix)
#
# Semantics, with the monster facing -Y:
#   swing/pitch +  a downward bone rotates toward -Y  → forward
#   yaw       +  the bone turns toward +X            → the monster's left
#   roll      +  a downward bone rotates toward -X   → the monster's right


def world_rot(pitch: float = 0.0, yaw: float = 0.0, roll: float = 0.0) -> Matrix:
    return (Matrix.Rotation(math.radians(yaw), 3, "Z")
            @ Matrix.Rotation(math.radians(roll), 3, "Y")
            @ Matrix.Rotation(math.radians(-pitch), 3, "X"))


def to_local_deg(arm_obj, bone_name: str, rot: Matrix) -> tuple[float, float, float]:
    m = arm_obj.data.bones[bone_name].matrix_local.to_3x3()
    local = m.inverted() @ rot @ m
    e = local.to_euler("XYZ")
    return (math.degrees(e.x), math.degrees(e.y), math.degrees(e.z))


def to_local_vec(arm_obj, bone_name: str, world_vec) -> tuple[float, float, float]:
    m = arm_obj.data.bones[bone_name].matrix_local.to_3x3()
    return tuple(m.inverted() @ Vector(world_vec))


def limb(prefix: str, side: int, **segments) -> dict:
    """Side-aware limb spec. Each value is (swing, out, twist) in degrees.

    swing + = forward, out + = away from the body, twist + = inward, on both sides.
    """
    s = "Left" if side > 0 else "Right"
    order = (("UpperArm", "up"), ("LowerArm", "lo"), ("ForearmExtra", "ex"), ("Hand", "hd")) \
        if prefix == "arm" else \
        (("UpperLeg", "hip"), ("LowerLeg", "knee"), ("Foot", "ankle"), ("Toes", "toe"))
    out = {}
    for suffix, key in order:
        swing, away, twist = segments.get(key, (0.0, 0.0, 0.0))
        out[s + suffix] = (swing, side * twist, -side * away)
    return out


def shoulder(side: int, fwd: float = 0.0, drop: float = 0.0, twist: float = 0.0) -> dict:
    s = "Left" if side > 0 else "Right"
    return {s + "Shoulder": (side * twist, -side * fwd, side * drop)}


def merge(*dicts) -> dict:
    out: dict = {}
    for d in dicts:
        out.update(d)
    return out


# The pose every action starts from: a stoop that never straightens, a head craned
# out ahead of the body, one shoulder hiked and one dropped. §01 — it has never had
# to hurry, and the resting shape says so.
BASE = merge(
    {
        "Hips": (0.0, 0.0, 3.0),
        "Spine": (11.0, -3.0, 0.0),
        "Chest": (8.0, 2.0, 0.0),
        "UpperChest": (5.0, 0.0, -2.0),
        "Neck": (17.0, 0.0, 0.0),
        "Head": (-25.0, 0.0, 5.0),
        "Jaw": (4.0, 0.0, 0.0),
        "Crest1": (-16.0, 0.0, 0.0),
        "Crest2": (-11.0, 0.0, 0.0),
        "Crest3": (-8.0, 0.0, 0.0),
        "LeftScapulaSpur": (0.0, 0.0, 0.0),
    },
    shoulder(1, fwd=9.0, drop=-8.0),    # left hiked, carrying the spur
    shoulder(-1, fwd=13.0, drop=12.0),  # right collapsed
    # The double elbow zigzags: back, forward, back again.
    limb("arm", 1, up=(-7.0, 5.0, 0.0), lo=(13.0, 2.0, 0.0), ex=(-19.0, 0.0, 0.0), hd=(9.0, 0.0, 0.0)),
    limb("arm", -1, up=(-4.0, 8.0, 0.0), lo=(16.0, 3.0, 0.0), ex=(-23.0, 0.0, 0.0), hd=(12.0, 0.0, 0.0)),
    limb("leg", 1),
    limb("leg", -1),
)


def spec_pose(arm_obj, frame: int, spec: dict, hips_world=None) -> Pose:
    """Expands a spec into a full keyframe for EVERY bone.

    This matters: the FBX exporter bakes all bones for every action, and a bone with
    no curve in action B keeps whatever pose action A left on it. Keying all 29 bones
    in all 7 actions is the only way to stop poses leaking between clips.
    """
    rotations = {}
    for b in arm_obj.pose.bones:
        pitch, yaw, roll = spec.get(b.name, (0.0, 0.0, 0.0))
        rotations[b.name] = to_local_deg(arm_obj, b.name, world_rot(pitch, yaw, roll))
    locations = {"Hips": to_local_vec(arm_obj, "Hips", hips_world or (0.0, 0.0, 0.0))}
    return Pose(frame=frame, rotations=rotations, locations=locations)


# ── Walk / run cycle keys ───────────────────────────────────────────────────
# A four-phase gait. `HIP_TO_TOE` is the strut the stride is measured against:
# hip (0.17, 0, 1.36) → toe contact (0.195, -0.36, 0.03).

HIP_TO_TOE = (Vector((0.195, -0.36, TOE_Z)) - Vector((0.17, 0.0, HIP_Z))).length


# The gait as a continuous function of cycle phase t ∈ [0,1) rather than four poses.
# Sampling it densely is what keeps the foot on the floor: ground_action() can only
# correct the frames it has keys on, and with four keys per cycle the Bézier segments
# between them still drove the foot 5 cm under the floor. Ten keys on the run cycle
# and eight on the walk cut that to millimetres.
#
# Knee angles are relative to the digitigrade rest pose, where the shin already
# sweeps backward — so a positive knee value extends the leg and a negative one folds it.

# Toe-off extension is what sets the vertical bob: that is where the hip peaks, up on
# the toe with the leg straight. Measured across a sweep of extension values, the bob
# ran 148 mm at a full plantar push down to 89 mm with none. The values below land at
# 94 mm (Patrol) / 113 mm (Chase) — about 7-8% of the 1.36 m hip height, the same ratio
# a running human holds — while the toes still curl hard enough to read as a push-off.
# Flexing the *stance* knee, the intuitive fix, made it worse: it only lowers the
# trough and leaves the peak alone.
GAIT_PHASES = [
    (0.00, (36.0, 6.0, -5.0, 14.0)),      # contact — lands on a bent digitigrade leg
    (0.25, (4.0, 0.0, 8.0, 0.0)),         # mid-stance — leg under the body
    (0.50, (-32.0, 8.0, -12.0, 30.0)),    # toe-off — toes curl and push
    (0.75, (9.0, -38.0, 30.0, -16.0)),    # swing — knee folded high, foot tucked
    (1.00, (36.0, 6.0, -5.0, 14.0)),
]

ARM_PHASES = [
    (0.00, (-26.0, 20.0, -12.0)),
    (0.25, (-6.0, 12.0, -22.0)),
    (0.50, (30.0, -6.0, -30.0)),
    (0.75, (10.0, 6.0, -26.0)),
    (1.00, (-26.0, 20.0, -12.0)),
]


def _sample(phases, t: float) -> tuple:
    t = t % 1.0
    for i in range(len(phases) - 1):
        t0, v0 = phases[i]
        t1, v1 = phases[i + 1]
        if t0 <= t <= t1:
            f = (t - t0) / (t1 - t0)
            return tuple(_lerp(a, b, f) for a, b in zip(v0, v1))
    return phases[0][1]


def gait_leg(side: int, t: float, amp: float) -> dict:
    hip, knee, ankle, toe = _sample(GAIT_PHASES, t)
    return limb("leg", side, hip=(hip * amp, 0.0, 0.0), knee=(knee * amp, 0.0, 0.0),
                ankle=(ankle * amp, 0.0, 0.0), toe=(toe * amp, 0.0, 0.0))


def gait_arm(side: int, t: float, amp: float) -> dict:
    """Patrol arms barely swing — they hang and trail, which is most of what reads as
    "does not need to hurry". Chase amplifies the same curve into a hard pump. The
    ForearmExtra lags the LowerArm at half amplitude: a dead segment on a whip."""
    up, lo, ex = _sample(ARM_PHASES, t)
    base = BASE[("Left" if side > 0 else "Right") + "UpperArm"]
    return limb("arm", side,
                up=(base[0] + up * amp, 5.0 + 3.0 * amp, 0.0),
                lo=(13.0 + lo * amp, 2.0, 0.0),
                ex=(-19.0 + ex * amp * 0.5, 0.0, 0.0),
                hd=(9.0 + 6.0 * amp, 0.0, 0.0))


def locomotion_poses(arm_obj, cycle_frames: int, key_step: int, amp: float,
                     torso_base: dict, sway: dict) -> list:
    """Samples one full gait cycle into keyframes.

    The right leg runs half a cycle out of phase with the left, and each arm counters
    the opposite leg. Torso sway is sinusoidal in the cycle phase — a lateral weight
    shift plus a counter-rotating spine and a lazy head scan — so the whole cycle is
    one continuous function with no hand-placed poses to fall out of sync.
    """
    poses = []
    spine = torso_base.get("Spine", BASE["Spine"])
    head = torso_base.get("Head", BASE["Head"])
    for i in range(cycle_frames // key_step):
        frame = 1 + i * key_step
        t = (i * key_step) / cycle_frames
        ph = 2.0 * math.pi * t
        overlay = {
            "Hips": (0.0, -sway["yaw"] * math.cos(ph),
                     BASE["Hips"][2] + sway["roll"] * math.sin(ph)),
            "Spine": (spine[0], sway["spine_yaw"] * math.cos(ph), spine[2]),
            "Head": (head[0], -sway["head_yaw"] * math.sin(ph), head[2]),
        }
        spec = merge(BASE, torso_base, overlay,
                     gait_leg(1, t, amp), gait_leg(-1, t + 0.5, amp),
                     gait_arm(1, t + 0.5, amp), gait_arm(-1, t, amp))
        # Lateral weight shift only. Height is computed by ground_action().
        poses.append(spec_pose(arm_obj, frame, spec,
                               (sway["shift"] * math.sin(ph), 0.0, 0.0)))
    return poses


# ── Actions ─────────────────────────────────────────────────────────────────

def action_patrol(arm_obj):
    """§06 순찰 — an unhurried walk, footsteps audible, the Listener's main signal.

    48-frame cycle at 30 fps = 1.6 s, two steps. Hip swing 0.72 × the run amplitude
    gives a 1.28 m step → 1.60 m/s. Slower than a walking player (§05: 2.0 m/s),
    which is the point: it is not chasing anyone yet and it knows it does not have to.
    """
    amp = 0.72
    poses = locomotion_poses(arm_obj, cycle_frames=48, key_step=1, amp=amp,
                             torso_base={},
                             sway=dict(yaw=4.0, roll=5.5, spine_yaw=3.0,
                                       head_yaw=6.5, shift=0.030))
    return blendkit.make_action(arm_obj, "Patrol", poses, loop=True), amp


def action_chase(arm_obj):
    """§06 추격 — direct, committed, footsteps + a roar.

    20-frame cycle = 0.667 s, two strides → 3.0 strides/s. At full amplitude the hip
    swings ±36°, giving a 1.62 m stride → **4.86 m/s**, which brackets §06's 4.8 and
    §07's 4.4-5.2 tiers at playback speeds 0.90-1.07. The stride is therefore
    plausible at speed instead of a fast-forwarded walk.
    """
    amp = 1.0
    # The crest lies HARD BACK here, not flared. Two reasons: a 48deg torso lean
    # rotates a flared crest until it juts out horizontally and reads as geometry
    # sticking through the neck; and reserving the flare for Alert turns it into a
    # state tell the Observer (§04 관측자) can actually read instead of decoration.
    # LeftScapulaSpur is counter-pitched for the same reason -- inherited lean would
    # swing the shoulder hook out level with the ground.
    lean = {"Spine": (26.0, 0.0, 0.0), "Chest": (14.0, 0.0, 0.0), "UpperChest": (8.0, 0.0, 0.0),
            "Neck": (22.0, 0.0, 0.0), "Head": (-38.0, 0.0, 3.0), "Jaw": (24.0, 0.0, 0.0),
            "Crest1": (-26.0, 0.0, 0.0), "Crest2": (-18.0, 0.0, 0.0), "Crest3": (-12.0, 0.0, 0.0),
            "LeftScapulaSpur": (-30.0, 0.0, 0.0)}
    poses = locomotion_poses(arm_obj, cycle_frames=20, key_step=1, amp=amp,
                             torso_base=lean,
                             sway=dict(yaw=6.0, roll=6.0, spine_yaw=3.0,
                                       head_yaw=7.5, shift=0.020))
    return blendkit.make_action(arm_obj, "Chase", poses, loop=True), amp


def action_alert(arm_obj):
    """§06 경계 — it heard something and is turning its head toward it.

    Frozen mid-step: the left leg planted, the right still trailing from the stride
    it abandoned. The body does almost nothing; the crest flares and the head snaps
    and holds, tilting to bring one side of the not-a-head toward the sound. Snaps
    are authored as key pairs 5-6 frames apart with long identical holds between, so
    the motion is discrete — it looks like it is deciding, not idling.
    """
    listening = merge(
        BASE,
        {"Spine": (16.0, 0.0, 0.0), "Chest": (6.0, 0.0, 0.0), "Neck": (23.0, 0.0, 0.0),
         "Jaw": (0.0, 0.0, 0.0),
         "Crest1": (27.0, 0.0, 0.0), "Crest2": (22.0, 0.0, 0.0), "Crest3": (18.0, 0.0, 0.0)},
        # Abandoned mid-stride: weight forward-left, right leg still behind.
        limb("leg", 1, hip=(7.0, 0.0, 0.0), knee=(2.0, 0.0, 0.0), ankle=(3.0, 0.0, 0.0)),
        limb("leg", -1, hip=(-15.0, 0.0, 0.0), knee=(13.0, 0.0, 0.0), ankle=(-9.0, 0.0, 0.0)),
    )

    def look(yaw, roll, neck_yaw=0.0):
        return merge(listening, {"Head": (-30.0, yaw, roll),
                                 "Neck": (23.0, neck_yaw, 0.0)})

    keys = [
        (1, look(0.0, 5.0)),
        (7, look(31.0, 17.0, 10.0)),     # snap left, ear-tilt into the sound
        (26, look(31.0, 17.0, 10.0)),    # hold — the unnerving part is the holding
        (32, look(-27.0, -15.0, -9.0)),  # snap right
        (52, look(-27.0, -15.0, -9.0)),
        (58, look(4.0, 5.0, 0.0)),
        (73, look(0.0, 5.0)),            # closes the cycle by hand; loop=False
    ]
    poses = [spec_pose(arm_obj, f, s, (0.0, 0.0, 0.0)) for f, s in keys]
    return blendkit.make_action(arm_obj, "Alert", poses, loop=False), None


def action_search(arm_obj):
    """§06 수색 — sweeping the radius around the last known position.

    §06: aggro release sends it to where it last saw you, then it searches for 15 s
    (GameConstants.SearchGiveUpSeconds) over a 12 m radius. So this reads as *area
    coverage*, not pursuit: the torso yaws ±24°, the head leads the turn by another
    ~15°, and the arms — which already reach the floor — drag alternately across the
    front. A 90-frame cycle (3 s) tiles five times into the 15 s window.
    """
    def sweep(torso_yaw, head_yaw, head_pitch, l_arm, r_arm, l_leg, r_leg):
        return merge(
            BASE,
            {"Spine": (13.0, torso_yaw * 0.45, 0.0),
             "Chest": (8.0, torso_yaw * 0.35, 0.0),
             "UpperChest": (5.0, torso_yaw * 0.2, -2.0),
             "Neck": (18.0, head_yaw * 0.3, 0.0),
             "Head": (head_pitch, head_yaw, 5.0),
             "Crest1": (10.0, 0.0, 0.0), "Crest2": (7.0, 0.0, 0.0), "Crest3": (5.0, 0.0, 0.0),
             "Hips": (0.0, torso_yaw * 0.3, 3.0)},
            limb("arm", 1, up=l_arm[0], lo=l_arm[1], ex=(-19.0, 0.0, 0.0), hd=(9.0, 0.0, 0.0)),
            limb("arm", -1, up=r_arm[0], lo=r_arm[1], ex=(-23.0, 0.0, 0.0), hd=(12.0, 0.0, 0.0)),
            limb("leg", 1, hip=(l_leg, 0.0, 0.0), knee=(-l_leg * 0.5, 0.0, 0.0)),
            limb("leg", -1, hip=(r_leg, 0.0, 0.0), knee=(-r_leg * 0.5, 0.0, 0.0)),
        )

    # (frame, torso yaw, head yaw, head pitch, left arm, right arm, left hip, right hip)
    keys = [
        (1, 22.0, 34.0, -25.0, ((16.0, -24.0, 0.0), (18.0, 0.0, 0.0)),
         ((-8.0, 27.0, 0.0), (14.0, 4.0, 0.0)), 9.0, -9.0),
        (16, 5.0, -7.0, -22.0, ((2.0, 6.0, 0.0), (13.0, 2.0, 0.0)),
         ((-2.0, 10.0, 0.0), (16.0, 3.0, 0.0)), 2.0, -2.0),
        (31, -23.0, -37.0, -25.0, ((-6.0, 29.0, 0.0), (12.0, 4.0, 0.0)),
         ((17.0, -26.0, 0.0), (20.0, 0.0, 0.0)), -11.0, 11.0),
        (46, -5.0, 9.0, 6.0, ((4.0, 8.0, 0.0), (15.0, 2.0, 0.0)),
         ((0.0, 11.0, 0.0), (17.0, 3.0, 0.0)), -3.0, 3.0),
        (61, 17.0, 27.0, -37.0, ((-9.0, 12.0, 0.0), (9.0, 2.0, 0.0)),
         ((-6.0, 14.0, 0.0), (11.0, 3.0, 0.0)), 6.0, -6.0),
        (76, 2.0, -15.0, -18.0, ((10.0, -14.0, 0.0), (17.0, 2.0, 0.0)),
         ((6.0, 16.0, 0.0), (19.0, 3.0, 0.0)), -1.0, 1.0),
    ]
    poses = []
    for f, ty, hy, hp, la, ra, ll, rl in keys:
        # X only: it drifts sideways as it casts about. Height is grounded later.
        poses.append(spec_pose(arm_obj, f, sweep(ty, hy, hp, la, ra, ll, rl),
                               (ty * 0.0012, 0.0, 0.0)))
    return blendkit.make_action(arm_obj, "Search", poses, loop=True), None


def action_standstill(arm_obj):
    """§06 정지 — the state the design calls the game's weapon.

    > 괴물이 멈추면 소리를 내지 않는다. … **침묵이 가장 무서운 소리다.**

    Silence is *designed*, not missing, so this clip is authored as measured
    stillness rather than an idle. What is deliberately absent: no breathing, no
    weight shift, no hip translation, zero motion below the neck. Feet are welded —
    §06 gives this state no footstep sound and a shifting foot would be a lie the
    Listener could see.

    The single event is one 4.5° head roll and 3° yaw over four frames at 1.6 s in,
    held for a second, unwound before the loop closes. Every other span is two
    identical keys, which makes those F-curve segments exactly flat. `verify_motion`
    asserts the total excursion stays under 6° and that the legs and hips move by 0.
    90 frames = 3.0 s; §06 holds this state for 5 s (GameConstants.StandstillSeconds).
    """
    still = merge(BASE, {"Jaw": (2.0, 0.0, 0.0)})
    tilt = merge(still, {"Head": (-25.0, 3.0, 9.5), "Neck": (17.0, 1.2, 0.0)})
    keys = [(1, still), (46, still), (50, tilt), (52, tilt), (84, tilt), (88, still), (91, still)]
    poses = [spec_pose(arm_obj, f, s, (0.0, 0.0, 0.0)) for f, s in keys]
    return blendkit.make_action(arm_obj, "Standstill", poses, loop=False), None


def action_stunned(arm_obj):
    """§04 섬광수 — the Flasher's stun. Weak, reusable, and obviously temporary.

    Length is not a guess: 75 frames at 30 fps = 2.5 s = GameConstants.FlashStunSeconds
    (§16-3, provisional). §04 says the effect is *weak* in exchange for being
    reusable, so this must never read as a kill or a knockdown — it recoils hard for
    a third of a second, sags, and then visibly recovers, ending on the exact BASE
    pose so it blends straight back into Chase with no pop.

    The recoil throws the head back and slams the crest flat: the flash hurts the
    thing it uses instead of eyes, and the crest going down is the readable "it lost
    you" beat that tells the Flasher their window is open.
    """
    def hit(spine, head, jaw, crest, arm_up, arm_lo, l_leg, r_leg, drop):
        l_hip, l_knee = l_leg
        r_hip, r_knee = r_leg
        return merge(
            BASE,
            {"Spine": (spine, 0.0, 0.0), "Chest": (spine * 0.4, 0.0, 0.0),
             "Neck": (17.0 + spine * 0.3, 0.0, 0.0), "Head": (head, 0.0, 5.0),
             "Jaw": (jaw, 0.0, 0.0),
             "Crest1": (crest, 0.0, 0.0), "Crest2": (crest * 0.8, 0.0, 0.0),
             "Crest3": (crest * 0.6, 0.0, 0.0),
             "LeftScapulaSpur": (-spine * 0.5, 0.0, 0.0)},
            limb("arm", 1, up=(arm_up, 24.0, 0.0), lo=(arm_lo, 3.0, 0.0),
                 ex=(-19.0 - arm_lo * 0.3, 0.0, 0.0), hd=(9.0, 0.0, 0.0)),
            limb("arm", -1, up=(arm_up * 0.9, 30.0, 0.0), lo=(arm_lo * 1.1, 4.0, 0.0),
                 ex=(-23.0 - arm_lo * 0.3, 0.0, 0.0), hd=(12.0, 0.0, 0.0)),
            # The stagger has to reach the legs — a violent recoil above welded hips
            # reads as a mannequin tipping, not as something absorbing a blow.
            limb("leg", 1, hip=(l_hip, 0.0, 0.0), knee=(l_knee, 0.0, 0.0),
                 ankle=(-l_knee * 0.4, 0.0, 0.0)),
            limb("leg", -1, hip=(r_hip, 0.0, 0.0), knee=(r_knee, 0.0, 0.0),
                 ankle=(-r_knee * 0.4, 0.0, 0.0)),
        # The recoil shifts it back, proportional to how deep the stagger is. The
        # sink comes from the buckling knee via ground_action(), not from a guess.
        ), (0.0, abs(drop) * 0.25, 0.0)

    keys = [
        # frame, spine, head, jaw, crest, armUp, armLo, (Lhip,Lknee), (Rhip,Rknee), drop
        (1, 11.0, -25.0, 4.0, -16.0, -7.0, 13.0, (0.0, 0.0), (0.0, 0.0), 0.0),
        # the flash lands — head thrown back, crest slammed flat, front knee braces
        (4, -14.0, -52.0, 34.0, -44.0, 46.0, -58.0, (14.0, -8.0), (-9.0, -4.0), -0.04),
        # deepest recoil: the braced knee buckles and the rear leg takes the weight
        (10, -19.0, -58.0, 30.0, -48.0, 52.0, -66.0, (21.0, -30.0), (-15.0, -12.0), -0.13),
        (22, -10.0, -46.0, 22.0, -40.0, 38.0, -50.0, (16.0, -23.0), (-11.0, -9.0), -0.10),
        # coming back — §04's stun is weak, so recovery starts well before it ends
        (38, 2.0, -34.0, 14.0, -30.0, 20.0, -26.0, (9.0, -13.0), (-6.0, -5.0), -0.06),
        (56, 9.0, -27.0, 7.0, -21.0, 4.0, -2.0, (3.0, -5.0), (-2.0, -2.0), -0.02),
        (70, 11.0, -25.0, 4.0, -16.0, -6.0, 11.0, (1.0, -1.0), (0.0, 0.0), 0.0),
        # frame 76 = exactly BASE, so it blends back into Chase with no pop.
        # 1→76 spans 75 frame intervals = 2.500 s = GameConstants.FlashStunSeconds.
        (76, 11.0, -25.0, 4.0, -16.0, -7.0, 13.0, (0.0, 0.0), (0.0, 0.0), 0.0),
    ]
    poses = []
    for f, sp, hd, jw, cr, au, al, ll, rl, dz in keys:
        spec, hips = hit(sp, hd, jw, cr, au, al, ll, rl, dz)
        poses.append(spec_pose(arm_obj, f, spec, hips))
    return blendkit.make_action(arm_obj, "Stunned", poses, loop=False), None


def action_grab(arm_obj):
    """Catching a player. §01: there is no killing it, so this is the payoff pose.

    Four beats over 40 frames (1.33 s): coil the arms back, throw both of them
    forward, close them across the front, then fold the catch to the chest while the
    mandible comes down over it. The arms cross inward at the clamp — with 1.78 m of
    reach there is no gap to slip through, which is the read §01 needs: every counter
    in this game is temporary, and being caught is not one of them.

    The arm angles look small for a lunge because shoulder swing COMPOUNDS with the
    torso lean: the spine, chest and upper chest contribute about 1.7x the spine value
    before the arm rotates at all. At the throw that is ~61deg of inherited lean, so
    the 18deg shoulder swing here puts the arm near horizontal. Authoring the 74deg
    that "a lunge" suggests threw both arms vertically over the head instead.
    """
    def beat(spine, head, jaw, crest, l_arm, r_arm, l_leg, r_leg, hips):
        return merge(
            BASE,
            {"Spine": (spine, 0.0, 0.0), "Chest": (spine * 0.45, 0.0, 0.0),
             "UpperChest": (spine * 0.25, 0.0, -2.0),
             "Neck": (20.0, 0.0, 0.0), "Head": (head, 0.0, 4.0), "Jaw": (jaw, 0.0, 0.0),
             "Crest1": (crest, 0.0, 0.0), "Crest2": (crest * 0.8, 0.0, 0.0),
             "Crest3": (crest * 0.6, 0.0, 0.0),
             # Cancel most of the inherited lean so the shoulder hook stays diagonal.
             "LeftScapulaSpur": (-spine * 0.7, 0.0, 0.0)},
            limb("arm", 1, up=l_arm[0], lo=l_arm[1], ex=l_arm[2], hd=l_arm[3]),
            limb("arm", -1, up=r_arm[0], lo=r_arm[1], ex=r_arm[2], hd=r_arm[3]),
            limb("leg", 1, hip=(l_leg, 0.0, 0.0), knee=(-l_leg * 0.6, 0.0, 0.0),
                 ankle=(l_leg * 0.3, 0.0, 0.0)),
            limb("leg", -1, hip=(r_leg, 0.0, 0.0), knee=(-r_leg * 0.6, 0.0, 0.0),
                 ankle=(r_leg * 0.3, 0.0, 0.0)),
        ), hips

    keys = [
        # 1 — arriving, mid-chase
        (1, 24.0, -36.0, 20.0, 18.0,
         ((-20.0, 10.0, 0.0), (16.0, 2.0, 0.0), (-19.0, 0.0, 0.0), (9.0, 0.0, 0.0)),
         ((22.0, 12.0, 0.0), (10.0, 3.0, 0.0), (-23.0, 0.0, 0.0), (12.0, 0.0, 0.0)),
         26.0, -22.0, (0.0, 0.0, 0.0)),
        # 6 — coil: arms drawn back and out, torso pulled up, maw opening
        (6, 8.0, -30.0, 34.0, 28.0,
         ((-38.0, 34.0, 0.0), (24.0, 6.0, 0.0), (-30.0, 0.0, 0.0), (4.0, 0.0, 0.0)),
         ((-36.0, 36.0, 0.0), (26.0, 6.0, 0.0), (-32.0, 0.0, 0.0), (6.0, 0.0, 0.0)),
         8.0, -6.0, (0.0, 0.0, 0.0)),
        # 14 — the throw: both arms out front, everything committed forward
        (14, 36.0, -44.0, 42.0, 22.0,
         ((18.0, 16.0, 0.0), (-18.0, 2.0, 0.0), (-6.0, 0.0, 0.0), (34.0, 0.0, 0.0)),
         ((16.0, 18.0, 0.0), (-16.0, 2.0, 0.0), (-8.0, 0.0, 0.0), (36.0, 0.0, 0.0)),
         34.0, -14.0, (0.0, -0.05, 0.0)),
        # 20 — clamp: arms cross inward, hands close, maw comes down
        (20, 33.0, -30.0, 12.0, 6.0,
         ((16.0, -26.0, 14.0), (-44.0, -10.0, 0.0), (-14.0, 0.0, 0.0), (48.0, 0.0, 0.0)),
         ((14.0, -28.0, 14.0), (-42.0, -12.0, 0.0), (-16.0, 0.0, 0.0), (50.0, 0.0, 0.0)),
         20.0, -10.0, (0.0, -0.03, 0.0)),
        # 28 — fold the catch to the chest, head down over it
        (28, 20.0, 26.0, 16.0, -10.0,
         ((22.0, -22.0, 18.0), (-64.0, -12.0, 0.0), (-22.0, 0.0, 0.0), (44.0, 0.0, 0.0)),
         ((20.0, -24.0, 18.0), (-62.0, -14.0, 0.0), (-24.0, 0.0, 0.0), (46.0, 0.0, 0.0)),
         6.0, -4.0, (0.0, 0.0, 0.0)),
        # 41 — settled, holding. Loops or blends out from here. 40 intervals = 1.333 s.
        (41, 15.0, 20.0, 9.0, -14.0,
         ((25.0, -20.0, 16.0), (-60.0, -10.0, 0.0), (-20.0, 0.0, 0.0), (40.0, 0.0, 0.0)),
         ((23.0, -22.0, 16.0), (-58.0, -12.0, 0.0), (-22.0, 0.0, 0.0), (42.0, 0.0, 0.0)),
         2.0, -2.0, (0.0, 0.0, 0.0)),
    ]
    poses = []
    for f, sp, hd, jw, cr, la, ra, ll, rl, hips in keys:
        spec, h = beat(sp, hd, jw, cr, la, ra, ll, rl, hips)
        poses.append(spec_pose(arm_obj, f, spec, h))
    return blendkit.make_action(arm_obj, "Grab", poses, loop=False), None


# ── Measurement ─────────────────────────────────────────────────────────────

_BONE_RE = re.compile(r'pose\.bones\["([^"]+)"\]\.(\w+)')


def lowest_point(body: bpy.types.Object) -> float:
    """World Z of the lowest deformed vertex at the current frame."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = body.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        return min((body.matrix_world @ v.co).z for v in mesh.vertices)
    finally:
        evaluated.to_mesh_clear()


def ground_action(rig: bpy.types.Object, body: bpy.types.Object, action) -> float:
    """Re-keys the hips' vertical translation so the lowest vertex sits on the floor.

    Hand-authored hip heights cannot be right: rotating a hip, knee and 0.51 m
    metatarsal changes the leg's effective length every frame, so a guessed offset
    buries the foot on some keys and floats it on others. Measured here it sank up to
    12 cm through the floor on Patrol and Chase.

    Every key of the four-phase gait has at least one foot weight-bearing (there is no
    flight key), so grounding every key is correct, and the vertical bob then *emerges*
    from the leg geometry — hips lowest at contact where the leg is swung out and
    longest, highest at mid-stance where it is under the body. That is the real
    relationship, and it is the opposite of what the hand-authored offsets had.

    Translating the hips moves the whole body rigidly, so the correction is exact in
    one pass with no iteration. It runs on every frame rather than only the authored
    keys, because correcting the keys alone still left the sparsely-keyed clips
    (Grab, Search, Stunned) a few millimetres under the floor between them.
    """
    muted = [(t, t.mute) for t in rig.animation_data.nla_tracks]
    for track, _ in muted:
        track.mute = True
    rig.animation_data.action = action

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")

    hips = rig.pose.bones["Hips"]
    basis = rig.data.bones["Hips"].matrix_local.to_3x3()
    lo, hi = action.frame_range
    worst = 0.0
    for frame in range(int(lo), int(hi) + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        delta = -lowest_point(body)
        worst = max(worst, abs(delta))
        world = basis @ Vector(hips.location) + Vector((0.0, 0.0, delta))
        hips.location = basis.inverted() @ world
        hips.keyframe_insert(data_path="location", frame=frame)

    bpy.ops.object.mode_set(mode="OBJECT")
    for track, was in muted:
        track.mute = was
    return worst


def grounding_error(rig: bpy.types.Object, body: bpy.types.Object,
                    action) -> tuple[float, float, float]:
    """Floor error and hip travel across EVERY frame, not just the keys.

    Returns (worst penetration, worst float, hip vertical travel). Bézier segments
    between grounded keys can still dip, so the keys being right is not the same as
    the clip being right — that is what this measures. The hip travel is the actual
    vertical bob of the body, which the grounded gait produces rather than receives.
    """
    muted = [(t, t.mute) for t in rig.animation_data.nla_tracks]
    for track, _ in muted:
        track.mute = True
    rig.animation_data.action = action

    lo, hi = action.frame_range
    lows, hips = [], []
    for frame in range(int(lo), int(hi) + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        lows.append(lowest_point(body))
        hips.append((rig.matrix_world @ rig.pose.bones["Hips"].head).z)

    rig.animation_data.action = None
    for track, was in muted:
        track.mute = was
    return min(lows), max(lows), max(hips) - min(hips)


def measure_action(action) -> dict:
    """Per-bone peak-to-peak excursion, in degrees, from the keyframes themselves.

    A generator that "ran fine" can still emit a Standstill that breathes or a Chase
    whose legs barely move. These are the numbers that make that visible.
    """
    per_bone: dict[str, float] = {}
    per_bone_loc: dict[str, float] = {}
    keys = 0
    curves = 0
    for fc in blendkit.iter_fcurves(action):
        m = _BONE_RE.match(fc.data_path)
        if m is None:
            continue
        bone, prop = m.group(1), m.group(2)
        vals = [kp.co[1] for kp in fc.keyframe_points]
        keys += len(vals)
        curves += 1
        if not vals:
            continue
        if prop == "rotation_euler":
            span = math.degrees(max(vals) - min(vals))
            per_bone[bone] = max(per_bone.get(bone, 0.0), span)
        elif prop == "location":
            span = max(vals) - min(vals)
            per_bone_loc[bone] = max(per_bone_loc.get(bone, 0.0), span)

    lo, hi = action.frame_range
    return {
        "name": action.name,
        "start": int(round(lo)),
        "end": int(round(hi)),
        "frames": int(round(hi - lo)) + 1,
        "seconds": (hi - lo) / FPS,
        "curves": curves,
        "keys": keys,
        "per_bone": per_bone,
        "per_bone_loc": per_bone_loc,
        "max_deg": max(per_bone.values()) if per_bone else 0.0,
        "max_loc": max(per_bone_loc.values()) if per_bone_loc else 0.0,
    }


def stride_report(amp: float, cycle_frames: int) -> tuple[float, float]:
    """Step length and resulting ground speed for a gait amplitude.

    step = 2 · L · sin(θ), L = hip-to-toe strut (1.38 m), θ = peak hip swing.
    speed = 2 steps / cycle duration.
    """
    theta = math.radians(36.0 * amp)
    step = 2.0 * HIP_TO_TOE * math.sin(theta)
    speed = 2.0 * step / (cycle_frames / FPS)
    return step, speed


def fbx_takes(path: str) -> list[str]:
    """Reads back the AnimationStack names actually written to the FBX.

    Not decoration. `bake_anim_use_all_bones` silently drops every non-active action
    when the actions are not stashed, and there is no other way to tell from a
    successful-looking export that seven clips became one. FBX binary object records
    store their name as ``<name>\\x00\\x01<Class>``, so the names are recoverable by
    walking back from each ``AnimStack`` token.
    """
    with open(path, "rb") as fh:
        data = fh.read()
    found = []
    for match in re.finditer(rb"AnimStack", data):
        i = match.start()
        if data[i - 2:i] != b"\x00\x01":
            continue  # the ObjectType definition, which carries no name
        j = k = i - 2
        while k > 0 and 32 <= data[k - 1] < 127:
            k -= 1
        found.append(data[k:j].decode("ascii", "replace"))
    return found


def glb_animations(path: str) -> list:
    with open(path, "rb") as fh:
        buf = fh.read()
    if buf[:4] != b"glTF":
        return []
    off = 12
    while off + 8 <= len(buf):
        clen, ctype = struct.unpack_from("<I4s", buf, off)
        off += 8
        if ctype == b"JSON":
            doc = json.loads(buf[off:off + clen].decode("utf-8"))
            return [a.get("name", "<unnamed>") for a in doc.get("animations", [])]
        off += clen
    return []


def verify_motion(stats: dict) -> None:
    """Design-linked assertions on the animation itself, not just on the file.

    Each one fails when the design's reasoning breaks, not when a number changes.
    """
    leg_bones = [f"{s}{p}" for s in ("Left", "Right")
                 for p in ("UpperLeg", "LowerLeg", "Foot", "Toes")]

    still = stats["Standstill"]
    if still["max_deg"] > 6.0:
        blendkit.fail(
            f"Standstill moves {still['max_deg']:.2f}° — §06 makes this the silent state "
            "and the game's weapon. It must not read as an idle."
        )
    leg_motion = max(still["per_bone"].get(b, 0.0) for b in leg_bones)
    hip_motion = still["per_bone"].get("Hips", 0.0)
    hip_travel = still["max_loc"]
    if max(leg_motion, hip_motion) > 0.001 or hip_travel > 0.0005:
        blendkit.fail(
            f"Standstill shifts its weight (legs {leg_motion:.3f}°, hips {hip_motion:.3f}°, "
            f"hip travel {hip_travel * 1000:.2f} mm). §06 gives this state no footstep "
            "sound — a moving foot is a visible lie the Listener can see."
        )

    chase_hip = stats["Chase"]["per_bone"].get("LeftUpperLeg", 0.0)
    if chase_hip < 60.0:
        blendkit.fail(
            f"Chase hip swing is only {chase_hip:.1f}° peak-to-peak; the stride cannot "
            "carry §06's 4.8 m/s and will look fast-forwarded."
        )

    patrol_hip = stats["Patrol"]["per_bone"].get("LeftUpperLeg", 0.0)
    if patrol_hip >= chase_hip:
        blendkit.fail(
            f"Patrol swings its hip {patrol_hip:.1f}° vs Chase's {chase_hip:.1f}° — §06's "
            "unhurried patrol must be visibly a different gait from the committed run."
        )

    stunned = stats["Stunned"]
    if stunned["max_deg"] < 40.0:
        blendkit.fail(
            f"Stunned peaks at {stunned['max_deg']:.1f}° — §04's flash has to read as a "
            "real recoil even though it is weak."
        )

    alert_head = stats["Alert"]["per_bone"].get("Head", 0.0)
    if alert_head < 30.0:
        blendkit.fail(
            f"Alert turns its head only {alert_head:.1f}° — §06's 경계 state is defined by "
            "moving toward the sound it heard."
        )


# ── Main ────────────────────────────────────────────────────────────────────

def main() -> None:
    blendkit.reset_scene()
    blendkit.set_frame_range(1, 91)
    PARTS.clear()

    for spec in (
        MaterialSpec(FLESH, (0.255, 0.232, 0.212), roughness=0.88),
        MaterialSpec(CARAPACE, (0.072, 0.068, 0.066), roughness=0.52),
        MaterialSpec(MAW, (0.185, 0.052, 0.048), roughness=0.38),
    ):
        blendkit.make_material(spec)

    build_torso()
    build_head()
    for side in (1, -1):
        build_arm(side)
        build_leg(side)

    # Rigid weights: one group per bone, weight 1.0, assigned before the join so the
    # groups merge by name.
    for obj, bone, mat_name in PARTS:
        blendkit.assign_material(obj, bpy.data.materials[mat_name])
        group = obj.vertex_groups.new(name=bone)
        group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")

    body = blendkit.join([o for o, _, _ in PARTS], "Monster_Body")
    blendkit.triangulate(body)
    blendkit.shade_smooth(body, angle_degrees=34.0)
    blendkit.uv_smart_project(body)

    rig = blendkit.build_armature("Monster_Rig", bone_specs())
    blendkit.bind_skin(body, rig, auto_weights=False)

    missing = [b.name for b in rig.pose.bones if b.name not in body.vertex_groups]
    if missing:
        blendkit.fail("bones with no weighted geometry: " + ", ".join(missing))

    builders = [action_patrol, action_alert, action_chase, action_search,
                action_standstill, action_stunned, action_grab]
    stats: dict[str, dict] = {}
    gaits: dict[str, float] = {}
    ground: dict[str, tuple[float, float]] = {}
    for build in builders:
        action, amp = build(rig)
        ground_action(rig, body, action)
        ground[action.name] = grounding_error(rig, body, action)
        stats[action.name] = measure_action(action)
        if amp is not None:
            gaits[action.name] = amp
        blendkit.stash_action(rig, action)

    # Each clip must stand alone in Unity, and NOTHING extrapolation means frame 0 is
    # the unposed rest pose — so the mesh cannot be exported mid-pose.
    for track in rig.animation_data.nla_tracks:
        for strip in track.strips:
            strip.extrapolation = "NOTHING"
            strip.blend_in = 0.0
            strip.blend_out = 0.0
    rig.animation_data.action = None
    bpy.context.scene.frame_set(0)

    fbx_path = blendkit.out_path("Characters", "Monster.fbx")
    glb_path = os.path.join(os.path.dirname(fbx_path), "Monster.glb")
    blendkit.export_fbx(fbx_path, objects=[rig, body], with_animation=True)
    blendkit.export_gltf(glb_path, with_animation=True)

    report = blendkit.assert_asset(
        blendkit.describe(fbx_path),
        max_triangles=6000,
        expect_bones=29,
        expect_actions=7,
        max_dimension=3.0,
    )
    blendkit.print_report(report)

    width, depth, height = report.size
    if not (2.2 <= height <= 2.5):
        blendkit.fail(f"height {height:.2f} m is outside the 2.2-2.5 m the brief calls for "
                      "(oversized in a §12 corridor, but still fitting one).")
    if width > 1.2:
        blendkit.fail(f"shoulder span {width:.2f} m exceeds 1.2 m — §12's corridors are the "
                      "arena and it has to fit them.")
    print(f"MONSTER_SHAPE height={height:.3f}m width={width:.3f}m depth={depth:.3f}m "
          f"hip_to_toe={HIP_TO_TOE:.3f}m shoulder_span={2 * 0.46:.2f}m")

    verify_motion(stats)

    order = ["Patrol", "Alert", "Chase", "Search", "Standstill", "Stunned", "Grab"]
    for name in order:
        s = stats[name]
        extra = ""
        if name in gaits:
            step, speed = stride_report(gaits[name], s["frames"] - 1)
            extra = f" amp={gaits[name]:.2f} step={step:.2f}m speed={speed:.2f}m/s"
        print(f"ANIM_REPORT {name:11s} frames={s['start']}-{s['end']} "
              f"({s['frames']:3d}f, {s['seconds']:.2f}s) curves={s['curves']} "
              f"keys={s['keys']:4d} max_bone_motion={s['max_deg']:6.2f}deg"
              f" head={s['per_bone'].get('Head', 0.0):5.1f}deg"
              f" hipswing={s['per_bone'].get('LeftUpperLeg', 0.0):5.1f}deg{extra}")

    # Feet on the floor. Penetration is the failure that reads as broken; a little
    # float during a swing phase does not.
    worst_sink = 0.0
    for name in order:
        lo_z, hi_z, bob = ground[name]
        worst_sink = min(worst_sink, lo_z)
        print(f"GROUND_REPORT {name:11s} floor_error sink={lo_z * 1000:+7.2f}mm "
              f"float={hi_z * 1000:+7.2f}mm   hip_bob={bob * 1000:6.1f}mm")
    if worst_sink < -0.005:
        blendkit.fail(f"a foot sinks {abs(worst_sink) * 1000:.1f} mm through the floor — "
                      "the grounding pass did not converge.")

    takes = fbx_takes(fbx_path)
    print(f"FBX_TAKES count={len(takes)} names={','.join(takes)}")
    dropped = [n for n in order if n not in takes]
    if dropped:
        blendkit.fail("actions missing from the FBX: " + ", ".join(dropped) +
                      " — they were not stashed into NLA tracks.")

    # blendkit.export_fbx passes bake_anim_use_nla_strips=True AND
    # bake_anim_use_all_actions=True. In Blender's exporter those are two independent
    # `if` blocks (export_fbx_bin.py:2479 and :2522), so a stashed action is written
    # once by the NLA pass (clean name) and again by the all-actions pass (prefixed
    # "<object>|<action>"). Harmless — the curves are identical — but Unity will list
    # 14 clips. Surfaced here rather than left to be discovered in the importer.
    duplicates = [t for t in takes if "|" in t]
    if duplicates:
        print(f"FBX_NOTE {len(duplicates)} duplicate takes from the all-actions pass: "
              f"{','.join(duplicates)}. Use the {len(order)} unprefixed clips. Fix belongs "
              "in blendkit.export_fbx (bake_anim_use_all_actions=False — stash_action "
              "already makes the NLA pass cover every action); not patched here because "
              "this generator does not own that file.")

    glb_anims = glb_animations(glb_path)
    print(f"GLB_ANIMATIONS count={len(glb_anims)} names={','.join(glb_anims)}")
    print(f"FILES {fbx_path} {glb_path}")
    print(f"GLB_BYTES {os.path.getsize(glb_path)}")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_monster_model.py raised:\n" + traceback.format_exc())
