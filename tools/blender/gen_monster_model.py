#!/usr/bin/env python3
"""Generates the monster: mesh, rig and one animation per §06 state.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_monster_model.py

**This no longer writes the monster the game loads.** ``gen_monster_ai.py`` does, and
``Monster.fbx``, ``Monster.clips.json`` and ``Monster.textures.json`` are its outputs
now. Reason in one line: everything here is an assembly of convex hulls and tubes, and a
hull cannot make a concave form — no sunken flank, no hollow between two ribs — so past
a few metres the creature read as a bag of bulges. The owner's sculpt reads as a body.

What this file still *is*, and why it is not deleted:

* **The seven §06 clips.** ``action_patrol`` … ``action_grab``, plus ``ground_action``,
  ``lock_stance_slide``, ``measure_ground_speed`` and ``grounding_error``, are imported
  and run verbatim by ``gen_monster_ai.py``. They transfer because they are authored as
  rotations about *world* axes and converted per bone through ``spec_pose``, so they
  follow whatever rest pose a rig happens to have. Deleting this file deletes the
  animation.
* **The procedural texture pipeline.** ``write_skin`` and ``build_eyes`` still paint the
  creature's lenses, through the same ``gen_textures.py`` calibration and the same
  corridor-albedo ceiling every other surface in the game is held to.

Running it directly writes the hull monster over the shipped asset, which is only ever
wanted for a before/after comparison, so it asks::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_monster_model.py -- --hull

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
  here is 6000 and the mesh uses 3,202 of them. Silhouette does all the work, which
  is why the ones that were added went into READABLE MASS — a plated shoulder shelf,
  a five-shard rib rack, a fan of crest blades, a keel down the chest — rather than
  into smoothing anything.
* §12 — corridors are the arena, so shoulder width is a hard constraint, not taste.
  The check below keeps the span at 0.98 m while the creature stands 2.34 m tall.

HOW IT IS DELIBERATELY WRONG
----------------------------
Not a person with a monster texture. The distortions are structural:

* **An extra elbow.** Each arm is UpperArm → LowerArm → ForearmExtra → Hand. Four
  segments, 1.78 m of reach from a 0.98 m frame, so the fingertips hang 6 cm off the
  floor in the rest pose.
* **Digitigrade legs.** Femur forward, shin swept *back*, then a 0.51 m metatarsal —
  so the knee appears to bend the wrong way and the ankle rides at 0.48 m.
* **A head that is not a head.** Two eyeless blade halves separated by a vertical
  slot, of slightly different size, plus a mandible that hangs to mid-chest and
  swings *forward* to gape. Nothing to make eye contact with.
* **Asymmetry.** Left shoulder hiked, carrying a scapular spur and the larger of the
  two shoulder plates; right shoulder dropped, with five exposed rib shards. The
  skeleton stays mirror-symmetric so a Unity Humanoid avatar can still be built; only
  the mesh is lopsided.
* **A dorsal crest** that lies folded on Patrol and flares on Alert — the only
  visible tell that it has heard something, which is what §06's Alert state is.
* **Pale hide over black plate.** Set by measurement, not taste — see the albedo
  discussion above `build_flesh`. The creature carries its own contrast, so it does
  not depend on the beam happening to land behind it.

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
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
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


def tube(name, p0, p1, r0, r1, sides=8, lobes=0, lobe_depth=0.0,
         twist=0.0, waist=None) -> bpy.types.Object:
    """A tapered tube between two points. Every limb segment is one of these.

    Segments overlap their joints and a knuckle sits at each major joint, so rigid
    weighting cannot open a gap when the bone rotates.

    `lobes`, `twist` and `waist` exist because the shot review's verdict was "clean
    cylindrical limb segments", and a circular cross-section swept linearly between
    two radii is the definition of a manufactured part:

    * **lobes / lobe_depth** flute the cross-section — `lobes=3, lobe_depth=0.2` makes
      a rounded triangle instead of a circle. Free: same vertex count, and the flutes
      catch §03's beam as three separate highlights running down the limb instead of
      one, which is what reads as bundled sinew rather than as pipe.
    * **twist** spirals those flutes from one end to the other, so no two silhouettes
      of the same limb are the same shape.
    * **waist** inserts a mid ring at its own radius factor, so a limb can swell into
      a muscle belly and neck down again. A cone cannot, and a cone is what a
      mannequin is made of.
    """
    p0v, p1v = Vector(p0), Vector(p1)
    u, v = _ring_frame(p1v - p0v)

    sections = [(0.0, r0), (1.0, r1)]
    if waist is not None:
        at, factor = waist
        sections.insert(1, (at, _lerp(r0, r1, at) * factor))

    bm = bmesh.new()
    rings = []
    for t, radius in sections:
        centre = p0v + (p1v - p0v) * t
        spin = math.radians(twist) * t
        ring = []
        for i in range(sides):
            a = 2.0 * math.pi * i / sides + spin
            r = radius * (1.0 + lobe_depth * math.cos(lobes * a)) if lobes else radius
            d = u * math.cos(a) + v * math.sin(a)
            ring.append(bm.verts.new(centre + d * r))
        rings.append(ring)

    for k in range(len(rings) - 1):
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    bm.faces.new(rings[0])
    bm.faces.new(rings[-1])
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


def _hash01(*ints) -> float:
    """Deterministic 0–1 hash. Jitter that survives a rebuild, with no RNG to seed."""
    x = 0.0
    for i, n in enumerate(ints):
        x += (n + 1) * (12.9898 + 7.233 * i)
    return (math.sin(x) * 43758.5453) % 1.0


# ── Joints ──────────────────────────────────────────────────────────────────
# The shot review's most damning line: "visible ball joints at shoulders, elbows,
# hips and knees". It was literally true — every joint was a UV sphere between two
# cylinders, which is the construction of an artist's mannequin and reads as one from
# any distance the beam reaches.
#
# A sphere is wrong for a reason worth stating, because "make it lumpier" is not the
# fix. A ball joint reads as manufactured because it is SEPARABLE: the eye can see
# where the limb ends and the joint begins, so it parses the creature as parts that
# were assembled. Organic joints hide that seam under something continuous — plates
# that overlap ACROSS the gap, tendon that spans it, skin that wrinkles over it.
#
# So each joint is now three things and none of them is a ball:
#   1. a `knuckle` — faceted, lopsided, tapered along the limb, never symmetric;
#   2. two or three `lame` plates lying ACROSS the joint line, overlapping like
#      armour, so there is no visible boundary for the eye to find;
#   3. a `collar` where the segment enters, flared and deeply fluted, so the limb
#      appears to be swallowed by the joint rather than plugged into it.


def knuckle(name, centre, radius, axis, seed, span=1.0, taper=1.0):
    """The lump where a ball joint would be. Faceted and deliberately lopsided.

    Four rings of five points along the limb axis, each ring phase-shifted and
    radius-jittered by `_hash01`, hulled into big flat facets. Five points per ring
    rather than sixteen is the whole point: a low facet count catches §03's beam as
    a few hard planes, and hard planes are what a sphere cannot produce at any size.
    """
    c = Vector(centre)
    a = Vector(axis).normalized()
    u, v = _ring_frame(a)
    pts = []
    for k, (along, scale) in enumerate(((-0.70, 0.58), (-0.20, 1.00),
                                        (0.30, 0.88), (0.76, 0.46))):
        phase = _hash01(seed, k) * math.tau
        for i in range(5):
            ang = math.tau * i / 5.0 + phase
            r = radius * scale * (0.82 + 0.40 * _hash01(seed, k, i))
            pts.append(c + a * (along * radius * span * taper)
                       + (u * math.cos(ang) + v * math.sin(ang)) * r)
    return hull(name, pts)


def lame(name, centre, radius, axis, around, along=0.0, reach=1.15,
         width=0.85, thick=0.26, lift=1.0):
    """One armour plate lying across a joint, overlapping the segments either side.

    Named for the overlapping strips of a gauntlet, which is exactly the read: the
    plate crosses the joint line, so there is no seam where a limb becomes a ball.

    It is **wrapped, not flat**. The first attempt made each plate a flat tangent
    slab and the result was worse than the sphere it replaced — a joint wearing a
    dozen paper flags, which reads as shattered rather than as armoured. A plate that
    follows the limb's curvature over a ±`width` arc, sitting `lift` proud of the
    knuckle underneath, is the thing that actually hides a joint: from every angle
    there is plate over the gap, and the eye never finds the boundary.
    """
    c = Vector(centre)
    a = Vector(axis).normalized()
    u, v = _ring_frame(a)

    def at(angle, r, slide):
        return (c + a * slide + (u * math.cos(angle) + v * math.sin(angle)) * r)

    arc = math.radians(46.0 * width)
    base = along * radius
    pts = []
    for slide, spread, out in ((base - radius * reach * 0.58, 1.0, 1.0),
                               (base + radius * reach * 0.66, 0.62, 0.93)):
        for k in (-1.0, -0.35, 0.35, 1.0):
            angle = around + arc * spread * k
            pts.append(at(angle, radius * lift * out, slide))
            pts.append(at(angle * 1.0, radius * (lift * out - thick), slide))
    return hull(name, pts)


def joint(prefix, bone, mat, centre, radius, axis, seed, plates=(0.0, 2.2, 4.3),
          span=1.0, lift=1.0):
    """A whole joint: one knuckle plus its overlapping plates. Registers every piece.

    The plate angles default to three roughly-thirds around the limb but deliberately
    not exactly — 0, 126°, 246° rather than 0, 120, 240, because three-fold symmetry
    is itself a manufactured cue.
    """
    part(knuckle(f"{prefix}Knuckle", centre, radius, axis, seed, span=span), bone, mat)
    for i, around in enumerate(plates):
        part(lame(f"{prefix}Lame{i}", centre, radius, axis, around,
                  along=(_hash01(seed, 90 + i) - 0.5) * 0.55,
                  reach=1.05 + 0.35 * _hash01(seed, 70 + i),
                  width=0.72 + 0.30 * _hash01(seed, 50 + i),
                  lift=lift),
             bone, CARAPACE)


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
EYES = "Monster_Eyes"


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

    part(tube("Spine_Seg", (0.0, 0.02, 1.38), (0.0, -0.01, 1.66), 0.190, 0.185, 12,
              lobes=5, lobe_depth=0.13, twist=14.0, waist=(0.45, 0.93)), "Spine", FLESH)
    part(tube("Chest_Seg", (0.0, -0.01, 1.63), (0.0, 0.0, 1.88), 0.185, 0.235, 12,
              lobes=5, lobe_depth=0.11, twist=-10.0, waist=(0.5, 1.05)), "Chest", FLESH)
    part(tube("UpperChest_Seg", (0.0, 0.0, 1.85), (0.0, 0.01, 2.00), 0.235, 0.165, 12,
              lobes=5, lobe_depth=0.10, twist=8.0), "UpperChest", FLESH)

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

    # Right side only: five rib shards pushing out through the skin, longest at the
    # middle of the arc. The left side gets the scapular spur and the shoulder shelf
    # instead. Asymmetry without touching the skeleton.
    #
    # Five rather than the original three, and 6 cm longer: at 15 m in a 44° beam the
    # individual shards are below a pixel, but the RACK is not — it turns one side of
    # the ribcage into a serrated outline while the other stays smooth, and an
    # asymmetric outline is the cheapest "that is not a person" a silhouette can carry.
    for i, z in enumerate((1.56, 1.655, 1.75, 1.845, 1.94)):
        reach = 0.245 + 0.055 * math.sin(math.pi * i / 4.0)
        part(hull(f"RibShard_{i}", [
            (-0.13, 0.05, z), (-0.13, -0.06, z), (-0.13, 0.0, z + 0.058),
            (-reach, 0.015, z + 0.028), (-reach + 0.02, -0.035, z - 0.018),
            (-reach * 0.6, 0.01, z + 0.05),
        ]), "Chest", CARAPACE)

    # Sternal keel — a blade down the front of the chest. The creature is seen head-on
    # more than from any other angle (§06 has it coming at you), and head-on the torso
    # was a plain cylinder. A keel gives the front a bright vertical highlight under
    # the beam and splits the chest into two shaded halves.
    part(hull("SternalKeel", [
        (0.055, -0.155, 1.90), (-0.055, -0.155, 1.90),
        (0.070, -0.130, 1.72), (-0.070, -0.130, 1.72),
        (0.045, -0.105, 1.52), (-0.045, -0.105, 1.52),
        (0.0, -0.235, 1.83), (0.0, -0.215, 1.66), (0.0, -0.150, 1.50),
    ]), "Chest", CARAPACE)


def build_head() -> None:
    """Neck, the two head blades, the hanging mandible, the crest and the two lenses.

    "A head that is not quite a head": two wedges of *different* size with a 3.6 cm
    slot between them, and a mandible reaching to mid-chest.

    This was authored eyeless, and the reasoning was that §06's Alert state is the
    only moment the creature shows intent and it shows it with the crest. That was a
    good argument about intent and the wrong answer to a different question, which the
    shot rig then asked: at 15 m the crest is four pixels of a forty-pixel figure and
    the creature is a smudge. §04 gives the 관측자 exactly one ability — 괴물의 시야를
    본다 → 누가 표적인지 — and §12 obliges every zone to contain 1~2 관측 지점 that
    make it usable at 15 m, adding that without them 관측자는 죽으러 가야 한다. A gaze
    is the thing the role reads, and a creature with no eyes has no gaze; the crest
    says *that* it noticed, never *whom*.

    So it has two, and they are the smallest thing on it that can carry that. Two
    separated points are what a person recognises a face and a facing from at a range
    where nothing else survives — and they stay a pair of pinpricks rather than lamps,
    because §03 makes darkness the lock and a creature that lights the room it walks
    into has opened it. See MonsterSkin.EyeGlow for the brightness and why it is
    bounded from both ends.
    """
    part(tube("Neck_Seg", (0.0, 0.0, 1.97), (0.0, -0.04, 2.15), 0.098, 0.082, 10,
              lobes=4, lobe_depth=0.20, twist=18.0, waist=(0.5, 0.90)), "Neck", FLESH)

    # Splayed 3 cm wider than the original wedge and given a backswept horn each.
    # The head is the top of the silhouette and therefore the first thing a beam
    # finds at range; a narrow wedge read as "a head" at 15 m, and a forked one that
    # is wider than the neck it sits on does not. 2.36 m stays the apex — it is what
    # sets the asset's height, which AssetImportPolicy.MonsterHeightMetres pins.
    blade = [
        (0.020, 0.05, 1.99), (0.168, 0.06, 2.02),
        (0.020, 0.00, 2.30), (0.148, 0.01, 2.23),
        (0.020, -0.27, 2.36), (0.090, -0.26, 2.30),
        (0.020, -0.40, 2.09), (0.064, -0.39, 2.09),
        (0.016, -0.475, 2.22),
        # the horn: sweeps back and out past the neck line
        (0.185, 0.185, 2.17), (0.132, 0.190, 2.09), (0.156, 0.135, 2.23),
    ]
    left = hull("HeadBlade_L", blade)
    blendkit.bevel(left, width=0.006, segments=1)
    part(left, "Head", CARAPACE)

    # Same cloud, 7% deeper and 2 cm lower: the halves do not match.
    right = hull("HeadBlade_R", mirrored(blade, y_scale=1.07, z_shift=-0.02))
    blendkit.bevel(right, width=0.006, segments=1)
    part(right, "Head", CARAPACE)

    build_eyes_on(left, 1)
    build_eyes_on(right, -1)

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
    #
    # Widened from a 0.15 m fan to 0.29 m and given a third pair of side blades. The
    # flare is the state tell §04's 관측자 is meant to read, and a tell has to be
    # legible from further than the 15 m the Observer's ability works at
    # (GameConstants.ObserverRange) or it is not information, it is decoration.
    crest_pts = [
        ((0.0, 0.15, 1.84), (0.0, 0.275, 2.06), 0.105, "Crest1"),
        ((0.0, 0.275, 2.04), (0.0, 0.370, 2.22), 0.080, "Crest2"),
        ((0.0, 0.370, 2.20), (0.0, 0.415, 2.35), 0.055, "Crest3"),
    ]
    for i, (p0, p1, half, bone) in enumerate(crest_pts):
        b = hull(f"CrestBlade_{i}", [
            (0.032, p0[1] - half, p0[2]), (-0.032, p0[1] - half, p0[2]),
            (0.032, p0[1] + half, p0[2]), (-0.032, p0[1] + half, p0[2]),
            (0.016, p1[1] - half * 0.5, p1[2]), (-0.016, p1[1] - half * 0.5, p1[2]),
            (0.016, p1[1] + half * 0.4, p1[2]), (-0.016, p1[1] + half * 0.4, p1[2]),
        ])
        blendkit.bevel(b, width=0.005, segments=1)
        part(b, bone, CARAPACE)
        # Two shorter blades to each side of every segment, so the crest is a fan
        # rather than a fin: a fan keeps a recognisable outline at any yaw, and the
        # player almost never sees the creature exactly side-on.
        #
        # Doubled in every dimension after the first render pass. At 0.15 m across it
        # was invisible from the front at 3 m, which makes it useless as the Alert tell
        # §04's 관측자 is supposed to read — a tell nobody can see is a decoration.
        for s in (1, -1):
            for j, (spread, drop, lean) in enumerate(((0.135, 0.02, 0.62), (0.205, 0.07, 0.38))):
                if i == 2 and j == 1:
                    continue  # the tip stays narrow, or the head disappears behind it
                part(hull(f"CrestSide_{i}_{j}_{s}", [
                    (s * spread * 0.62, p0[1] - half * 0.8, p0[2] - drop),
                    (s * spread, p0[1] - half * 0.5, p0[2] - drop * 0.5),
                    (s * spread * 0.62, p0[1] + half * 0.8, p0[2] - drop),
                    (s * spread * 0.55, _lerp(p0[1], p1[1], lean + 0.16),
                     _lerp(p0[2], p1[2], lean + 0.16)),
                    (s * spread * 0.90, _lerp(p0[1], p1[1], lean), _lerp(p0[2], p1[2], lean * 0.6)),
                ]), bone, CARAPACE)

    # Left scapular spur — a hooked blade sweeping up and back off one shoulder.
    spur = hull("ScapulaSpur", [
        (0.185, 0.04, 1.91), (0.305, 0.05, 1.95), (0.195, 0.15, 1.99),
        (0.265, 0.19, 2.09), (0.320, 0.16, 2.05),
        (0.250, 0.265, 2.17), (0.300, 0.245, 2.14),
    ])
    blendkit.bevel(spur, width=0.005, segments=1)
    part(spur, "LeftScapulaSpur", CARAPACE)


EYE_RADIUS = 0.036
"""Half-width of a lens, metres.

Sized off the measurement rather than off the model. At GameConstants.ObserverRange
the creature is ~40 px tall in a 1280-wide 90° frame, which is 14.2 px per degree; a
7.2 cm lens subtends 0.27° and lands on just under 4 px, and the pair about 12 cm
apart lands 7 px apart. Below roughly 5 cm the two merge into a single point and the
*facing* goes with them — and the facing is the half of §04's ability that the crest
was never able to carry.
"""

EYE_PROUD = 0.013
"""How far a lens stands out of the blade it sits on, metres.

Proud rather than socketed. A socket is a shadow at every range past a few metres,
and at 15 m a shadow inside a 40-pixel figure is not a feature, it is noise.
"""


def build_eyes_on(blade: bpy.types.Object, side: int) -> None:
    """Puts one lens on the front face of one head blade.

    The placement is *found*, not typed, and that is load-bearing. The first attempt
    put both lenses at a hand-written coordinate near where the front of the head looked
    like it was, and the render answered plainly: the blades are 0.66 m deep in Y and
    the hand-picked point was 15 cm inside one of them, so what shipped was two slivers
    of glass peeping out of a solid wedge — the eyes were there, the emission was there,
    and sweeping their brightness from 1.5 to 12 moved the measured frame by 0.0003.

    So a ray is cast straight down the axis the player is on. The outermost x that
    still hits the blade from the front is the widest a forward-facing lens can sit,
    which maximises the separation §04 reads the facing from; the lens then sits
    EYE_PROUD in front of whatever that ray hit. Re-shape the head and the eyes move
    with it instead of sinking into it.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = blade.evaluated_get(depsgraph)

    # Forward is -Y (the creature faces -Y), so the ray travels +Y from in front of it.
    forward = Vector((0.0, -1.0, 0.0))
    height = 2.215 + (0.0 if side > 0 else -0.02)   # the blades do not match; nor do the eyes

    found = None
    x = 0.086
    while x >= 0.020:
        origin = Vector((side * x, -0.95, height))
        hit, location, normal, _ = evaluated.ray_cast(origin, -forward)
        if hit and normal.dot(forward) > 0.15:
            found = location
            break

        x -= 0.004

    if found is None:
        blendkit.fail(
            "no forward-facing surface on the head blade to sit an eye on. §04's 관측자 reads "
            "the creature's facing off the pair of lenses and §12 obliges every zone to hold "
            "관측 지점 that make that readable at 15 m; a head with nowhere to put them is a "
            "head that has to be re-shaped, not shipped.")
        return

    centre = found + forward * EYE_PROUD

    # A lens rather than a ball: a ring in the X–Z plane, domed forward. Flattened along
    # the view axis so it reads as glass set into a face instead of a bead glued on.
    points = []
    for i in range(8):
        angle = i * math.pi / 4.0
        points.append((centre.x + EYE_RADIUS * math.cos(angle),
                       centre.y + EYE_PROUD * 0.35,
                       centre.z + EYE_RADIUS * 0.86 * math.sin(angle)))
        points.append((centre.x + EYE_RADIUS * 0.62 * math.cos(angle),
                       centre.y - EYE_PROUD * 0.55,
                       centre.z + EYE_RADIUS * 0.53 * math.sin(angle)))

    points.append((centre.x, centre.y - EYE_PROUD * 0.95, centre.z))
    points.append((centre.x, centre.y + EYE_PROUD * 0.9, centre.z))

    lens = hull(f"Eye_{'L' if side > 0 else 'R'}", points)
    blendkit.bevel(lens, width=0.003, segments=1)
    part(lens, "Head", EYES)


def build_arm(side: int) -> None:
    """One four-segment arm. §01: it cannot be killed, so it never needs to lunge —
    it simply reaches. 1.78 m of arm on a 0.98 m frame puts the fingertips 6 cm off
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
              0.130, 0.122, 10), f"{s}Shoulder", FLESH)
    joint(f"{s}Shoulder", f"{s}Shoulder", FLESH, sh, 0.138,
          Vector(elbow) - Vector(sh), seed=11 + side, span=1.15, lift=1.02)

    # Shoulder shelf — a swept plate carrying the span out to ±0.50 m.
    #
    # This is the single biggest silhouette change and the reasoning is the brief's:
    # what the player acts on is the OUTLINE in a narrow beam at range. The original
    # creature was a column with sticks on it, and a column reads as a pipe. A wide
    # plated top over a narrow waist over long dangling arms reads as a body, and as
    # the wrong kind of one, in one glance.
    #
    # Width is a hard §12 constraint, not taste, and it is also where the creature's
    # SCALE actually reads. The kit builds corridors 2.2 m wide and CLEAR_H = 3.00 m
    # tall (gen_mapkit.py) — §12's "2.2 × 3.0 m" is width × height — so a 2.34 m
    # creature has 0.66 m of headroom and cannot be made to scrape a ceiling without
    # breaking AssetImportPolicy.MonsterHeightMetres. What it CAN do is fill the
    # corridor sideways, and at 0.99 m it was taking up under half of one.
    #
    # The span below lands the asset at 1.18 m against the 1.2 m the corridors allow:
    # 54% of the clear width, so a player cannot pass it in a corridor and can see
    # that at a glance. That is where "too big for this space" comes from here.
    # Weighted to the Shoulder bones, so the left/right shrug asymmetry in BASE tilts
    # the two plates differently.
    shelf_out = 0.573 if side > 0 else 0.545   # the hiked side carries the bigger plate
    shelf_top = 2.035 if side > 0 else 1.995
    part(hull(f"{s}ShoulderShelf", [
        (x * 0.235, 0.075, SHOULDER_Z - 0.055), (x * 0.235, -0.075, SHOULDER_Z - 0.030),
        (x * 0.245, 0.020, shelf_top),
        (x * (shelf_out - 0.09), 0.130, shelf_top - 0.055),
        (x * (shelf_out - 0.06), -0.105, shelf_top - 0.085),
        (x * shelf_out, 0.060, shelf_top - 0.150),
        (x * (shelf_out - 0.02), 0.115, shelf_top - 0.235),
        (x * (shelf_out - 0.10), -0.060, shelf_top - 0.255),
        (x * 0.300, 0.045, SHOULDER_Z - 0.185),
    ]), f"{s}Shoulder", CARAPACE)

    part(tube(f"{s}UpperArm_Seg", sh, elbow, 0.122, 0.096, 10,
              lobes=3, lobe_depth=0.20, twist=34.0 * side, waist=(0.42, 1.13)),
         f"{s}UpperArm", FLESH)

    joint(f"{s}Elbow", f"{s}LowerArm", FLESH, elbow, 0.104,
          Vector(elbow2) - Vector(elbow), seed=23 + side, span=1.25)
    part(tube(f"{s}LowerArm_Seg", elbow, elbow2, 0.096, 0.078, 10,
              lobes=3, lobe_depth=0.22, twist=-28.0 * side, waist=(0.38, 1.10)),
         f"{s}LowerArm", FLESH)

    # A blade running the length of the forearm. Straight edges are what a spot light
    # turns into a continuous highlight, and a continuous highlight is how a limb
    # stays visible at 15 m once the diffuse term has fallen off — a plain tube gives
    # one moving dot instead of a line, and reads as nothing.
    part(hull(f"{s}LowerArmFin", [
        (x * 0.372, 0.075, 1.36), (x * 0.398, 0.055, 1.36),
        (x * 0.386, 0.185, 1.20), (x * 0.404, 0.165, 1.20),
        (x * 0.392, 0.130, 1.02), (x * 0.408, 0.115, 1.02),
        (x * 0.396, 0.020, 0.88), (x * 0.408, 0.010, 0.88),
    ]), f"{s}LowerArm", CARAPACE)

    # The second elbow. There is no anatomical reason for it; that is the reason.
    joint(f"{s}Elbow2", f"{s}ForearmExtra", CARAPACE, elbow2, 0.090,
          Vector(wrist) - Vector(elbow2), seed=37 + side, span=1.30, lift=1.06)
    part(tube(f"{s}Forearm_Seg", elbow2, wrist, 0.078, 0.060, 10,
              lobes=4, lobe_depth=0.24, twist=40.0 * side, waist=(0.45, 1.08)),
         f"{s}ForearmExtra", FLESH)

    joint(f"{s}Wrist", f"{s}Hand", FLESH, wrist, 0.068,
          Vector(tip) - Vector(wrist), seed=53 + side, span=1.10,
          plates=(0.8, 3.6))
    part(tube(f"{s}Palm", wrist, tip, 0.060, 0.044, 8,
              lobes=3, lobe_depth=0.18), f"{s}Hand", FLESH)

    # Three long fingers, no thumb. They reach past the ankle to z ≈ 0.06.
    for i, dx in enumerate((-0.030, 0.0, 0.030)):
        base = (tip[0] + x * dx, tip[1] + 0.012, tip[2] + 0.01)
        mid = (tip[0] + x * dx * 1.5, tip[1] - 0.035, tip[2] - 0.075)
        end = (tip[0] + x * dx * 1.7, tip[1] - 0.075, 0.062)
        part(tube(f"{s}Finger{i}a", base, mid, 0.026, 0.018, 6,
                  lobes=3, lobe_depth=0.22), f"{s}Hand", FLESH)
        part(tube(f"{s}Finger{i}b", mid, end, 0.018, 0.006, 6), f"{s}Hand", CARAPACE)


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

    joint(f"{s}Hip", f"{s}UpperLeg", FLESH, hip, 0.156,
          Vector(knee) - Vector(hip), seed=71 + side, span=1.20, lift=0.98)
    part(tube(f"{s}UpperLeg_Seg", hip, knee, 0.148, 0.110, 12,
              lobes=3, lobe_depth=0.17, twist=26.0 * side, waist=(0.40, 1.12)),
         f"{s}UpperLeg", FLESH)

    # Hip blade — a flat plate over the outside of the thigh. §06's gait swings the
    # hip ±36°, so this is the piece that makes the STRIDE readable at range: a flat
    # face rotating through the beam flashes, a smooth tube does not.
    part(hull(f"{s}HipBlade", [
        (x * 0.245, 0.085, 1.34), (x * 0.285, 0.055, 1.30),
        (x * 0.250, -0.075, 1.31), (x * 0.278, -0.045, 1.27),
        (x * 0.232, 0.030, 1.10), (x * 0.262, 0.010, 1.07),
        (x * 0.215, -0.060, 0.95), (x * 0.238, -0.040, 0.93),
    ]), f"{s}UpperLeg", CARAPACE)

    joint(f"{s}Knee", f"{s}LowerLeg", CARAPACE, knee, 0.120,
          Vector(ankle) - Vector(knee), seed=89 + side, span=1.30, lift=1.05)
    part(tube(f"{s}LowerLeg_Seg", knee, ankle, 0.110, 0.078, 12,
              lobes=4, lobe_depth=0.19, twist=-22.0 * side, waist=(0.34, 1.14)),
         f"{s}LowerLeg", FLESH)

    # Hock fin — a blade along the BACK edge of the reversed shin (the monster faces
    # −Y, so +Y is behind it). The digitigrade leg is the loudest "not a person" the
    # silhouette has, and a hard edge tracing the wrong-way bend is what makes that
    # bend visible at range instead of only in close-up.
    part(hull(f"{s}HockFin", [
        (x * (LEG_X - 0.014), -0.055, 0.815), (x * (LEG_X + 0.014), -0.055, 0.815),
        (x * (LEG_X - 0.012), 0.055, 0.760), (x * (LEG_X + 0.012), 0.055, 0.760),
        (x * (LEG_X - 0.010), 0.155, 0.615), (x * (LEG_X + 0.010), 0.155, 0.615),
        (x * (LEG_X - 0.010), 0.190, 0.500), (x * (LEG_X + 0.010), 0.190, 0.500),
        (x * (LEG_X - 0.012), 0.120, 0.490), (x * (LEG_X + 0.012), 0.120, 0.490),
    ]), f"{s}LowerLeg", CARAPACE)

    joint(f"{s}Ankle", f"{s}Foot", CARAPACE, ankle, 0.082,
          Vector(ball) - Vector(ankle), seed=101 + side, span=1.15,
          plates=(1.1, 4.0))
    part(tube(f"{s}Foot_Seg", ankle, ball, 0.078, 0.056, 8,
              lobes=3, lobe_depth=0.20, waist=(0.5, 1.06)), f"{s}Foot", FLESH)

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
#
# THE TRAP, and it cost this asset its whole silhouette
# ----------------------------------------------------
# `pitch` is one rigid rotation about world X. It therefore reads OPPOSITE on a bone
# that points up and a bone that points down: R tips a downward bone's tip toward −Y
# (in front of the creature) and an upward bone's tip toward +Y (behind it).
#
# The limbs all hang downward, so "+swing = forward" is true for every value `limb()`
# takes. The torso chain — Spine, Chest, UpperChest, Neck, Head, the crest, the
# scapular spur — points UP, and for those the same positive number leans the
# creature BACKWARDS. Every torso value in this file was authored as a positive
# "lean", and the creature was therefore reclining: measured on the built rig, the
# Chase pose put the head 0.469 m BEHIND the hips at −38.1° of lean, and the shot
# review read that as "upright and neutral with no forward lean" because a backward
# lean foreshortens to nothing when the creature is coming straight at you, which is
# the one angle §06 guarantees.
#
# Rather than flip `world_rot` — which would silently invert ~200 correct limb values
# and the measured gait with them — the up-pointing bones are authored through
# `stoop()`, which takes the angle a human means by "bend forward".


def world_rot(pitch: float = 0.0, yaw: float = 0.0, roll: float = 0.0) -> Matrix:
    return (Matrix.Rotation(math.radians(yaw), 3, "Z")
            @ Matrix.Rotation(math.radians(roll), 3, "Y")
            @ Matrix.Rotation(math.radians(-pitch), 3, "X"))


def stoop(forward: float, yaw: float = 0.0, roll: float = 0.0) -> tuple[float, float, float]:
    """A pose triple for an UP-pointing bone, where `forward` bends it toward the front.

    The one place the sign of the torso chain is decided. `stoop(26)` leans the spine
    26° over its own feet; `stoop(-26)` arches it back. Read the note above for why a
    bare positive number does the opposite.
    """
    return (-forward, yaw, roll)


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


# ── The dorsal crest ────────────────────────────────────────────────────────
# §06 gives the creature no eyes and no face, so the crest is the only channel it has
# for showing state, and §04's 관측자 is the player whose whole job is reading it at
# ObserverRange. It is therefore authored as one number — degrees of RISE off the
# folded position — rather than as three loose columns per clip that drift apart.
#
# The fold is deep because the creature is stooped: a crest lies along the BACK, and
# once the back is pitched 30° forward a crest that only folds a little stands
# straight up. Measured, that was the tallest point on the whole creature at 2.46 m,
# 0.26 m through §12's 2.2 m ceiling — the crest, not the head, was what stopped the
# hunch from reading.
CREST_FOLD = (-44.0, -17.0, -9.0)
"""Per-segment fold that lays the crest flat along a stooped back."""


def crest(rise: float = 0.0) -> dict:
    """The crest, as degrees of rise off folded. 0 = flat, 60 = fully flared (Alert)."""
    share = (0.62, 0.26, 0.12)
    return {f"Crest{i + 1}": stoop(CREST_FOLD[i] + rise * share[i]) for i in range(3)}


# ── The hunch ───────────────────────────────────────────────────────────────
# The pose every action starts from. It is a permanent stoop, and the argument for it
# had to survive a measurement that killed the obvious version.
#
# The obvious version: §12's corridor clear section is "2.2 × 3.0 m", so hunch the
# 2.34 m creature until its head is at the ceiling. That is wrong — those are WIDTH ×
# HEIGHT, and gen_mapkit.py builds CLEAR_H = 3.00 m. The creature has 0.66 m of
# headroom standing straight and no hunch can put its head near a 3 m ceiling;
# hunching only lowers it further. The dimension that is actually tight is the width,
# and that is answered in `build_arm` by carrying the shoulder span out to 1.12 m of
# the 2.2 m available.
#
# What the stoop is for, then, is not headroom. It is that the creature reads as
# having ADAPTED to a space it does not fit: a thing whose neck has learned to carry
# its head forward at the player's eye height (§05: 1.63 m) rather than a metre above
# it, where nothing it hunts ever is. §01 says nothing in this game can make it
# straighten, because every counter is temporary.
#
# The stoop is therefore geometry, not mood:
# a deep bend at the waist, shoulders rolled up and forward around a neck that cranes
# the head out ahead of the chest, and the head itself levelled so the eyeless blades
# aim at a standing player instead of at the floor. It never straightens, because
# §01 says nothing in this game can make it — every counter is temporary.
#
# `STOOP_TOTAL` is the sum of the four torso bends and is what actually decides the
# head height: the head sits 0.78 m up the spine from the hips, so each degree of it
# costs roughly 13 mm of standing height. It is measured after posing, not trusted.
STOOP_SPINE, STOOP_CHEST, STOOP_UPPER, STOOP_NECK = 15.0, 9.0, 6.0, 13.0
STOOP_TOTAL = STOOP_SPINE + STOOP_CHEST + STOOP_UPPER + STOOP_NECK

SHOULDER_BEND = STOOP_SPINE + STOOP_CHEST + STOOP_UPPER
"""Forward bend inherited by anything hanging off the shoulders."""

ALERT_BEND = 20.0
"""§06 경계 straightens it partway. The only state that does, which is what makes the
rise itself a tell §04's 관측자 can read at ObserverRange before the head angle is
legible at all."""


def arm_hang(bend: float) -> float:
    """Shoulder swing that re-hangs an arm vertically under `bend` degrees of stoop.

    Counter-intuitive and worth stating, because getting it backwards folded the
    creature's arms up over its own back on the first attempt: a rigid arm bolted to a
    chest that pitches FORWARD sweeps BACKWARD in world terms, the way a diver's arms
    trail behind them in a tuck. So the correction runs the same way as the bend, not
    against it. `limb()`'s +swing is forward; the stoop is worth −bend of it.
    """
    return bend


def spur_pitch(bend: float) -> tuple[float, float, float]:
    """Counter-arch for the scapular spur, which points UP and therefore tips the
    other way from the arms. Left alone it swings out level with the ground."""
    return stoop(-bend + 8.0)

BASE = merge(
    {
        "Hips": (0.0, 0.0, 3.0),
        "Spine": stoop(STOOP_SPINE, yaw=-3.0),
        "Chest": stoop(STOOP_CHEST, yaw=2.0),
        "UpperChest": stoop(STOOP_UPPER, roll=-2.0),
        "Neck": stoop(STOOP_NECK),
        # Arched back against the neck, so the not-a-face is aimed forward rather than
        # down. Net head tilt is STOOP_TOTAL - 24 ≈ 19° below horizontal, which is where
        # a player standing 3 m away at §05's 1.63 m eye height actually is.
        "Head": stoop(-24.0, roll=5.0),
        "Jaw": (4.0, 0.0, 0.0),
        # Counter-pitched, or the inherited stoop swings the shoulder hook forward
        # until it juts out of the creature's chest.
        "LeftScapulaSpur": spur_pitch(SHOULDER_BEND),
    },
    shoulder(1, fwd=9.0, drop=-8.0),    # left hiked, carrying the spur
    shoulder(-1, fwd=13.0, drop=12.0),  # right collapsed
    # The double elbow zigzags: back, forward, back again.
    limb("arm", 1, up=(arm_hang(SHOULDER_BEND) - 7.0, 5.0, 0.0), lo=(13.0, 2.0, 0.0),
         ex=(-19.0, 0.0, 0.0), hd=(9.0, 0.0, 0.0)),
    limb("arm", -1, up=(arm_hang(SHOULDER_BEND) - 4.0, 8.0, 0.0), lo=(16.0, 3.0, 0.0),
         ex=(-23.0, 0.0, 0.0), hd=(12.0, 0.0, 0.0)),
    crest(0.0),
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
#
# The hip angles below were opened from ±36/−32 to ±44/−39 and the Chase cycle
# lengthened from 20 frames to 22. Both changes buy the same thing — a LONGER stride
# rather than a faster one — and the brief asks for it in as many words. §06's
# creature is 0.3 m/s faster than a sprinting human and must read as *unbothered*
# while it does that, and the difference between a predator and a jogger is entirely
# whether the same ground speed arrives as few long strides or many short ones. The
# ground speed itself is not set here: `lock_stance_slide` solves it from the cycle,
# and Unity divides §07's tier speed by whatever it solved for.
GAIT_PHASES = [
    (0.00, (44.0, 6.0, -5.0, 14.0)),      # contact — lands on a bent digitigrade leg
    (0.25, (4.0, 0.0, 8.0, 0.0)),         # mid-stance — leg under the body
    (0.50, (-39.0, 8.0, -12.0, 30.0)),    # toe-off — toes curl and push
    (0.75, (11.0, -44.0, 34.0, -16.0)),   # swing — knee folded high, foot tucked
    (1.00, (44.0, 6.0, -5.0, 14.0)),
]

PATROL_LIMP = 0.70
"""Right-leg amplitude as a fraction of the left, on Patrol only. §06 순찰 has to be
unhurried AND wrong, and the same leg dragging every cycle is the cheapest wrong
there is. Chase is symmetric — a limp that vanishes the moment it commits is a much
better tell than one it never had."""

PEAK_HIP_SWING = max(abs(v[0]) for _, v in GAIT_PHASES)
"""Peak hip angle at amplitude 1.0. `stride_report` measures the stride against it,
so the two cannot drift apart the way a hand-copied 36.0 did."""

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


def gait_arm(side: int, t: float, amp: float, hang: float) -> dict:
    """Patrol arms barely swing — they hang and trail, which is most of what reads as
    "does not need to hurry". Chase amplifies the same curve into a hard pump. The
    ForearmExtra lags the LowerArm at half amplitude: a dead segment on a whip.

    `hang` is the shoulder swing that leaves the arm vertical under whatever stoop
    the clip is using; the swing curve rides on top of it. Without it a deeper lean
    carries the arms forward with the chest and the creature runs pointing at the
    floor.
    """
    up, lo, ex = _sample(ARM_PHASES, t)
    trail = -7.0 if side > 0 else -4.0
    return limb("arm", side,
                up=(hang + trail + up * amp, 5.0 + 3.0 * amp, 0.0),
                lo=(13.0 + lo * amp, 2.0, 0.0),
                ex=(-19.0 + ex * amp * 0.5, 0.0, 0.0),
                hd=(9.0 + 6.0 * amp, 0.0, 0.0))


def torso_hang(torso_base: dict) -> float:
    """The shoulder counter-swing that hangs an arm vertically under a given stoop.

    The arms inherit Spine + Chest + UpperChest, so the angle to cancel is whatever
    those three add up to in *this* clip — read back out of the spec rather than
    assumed, because Chase leans further than BASE and Alert leans less.
    """
    total = 0.0
    for bone in ("Spine", "Chest", "UpperChest"):
        total += -torso_base.get(bone, BASE[bone])[0]     # stoop() stores it negated
    return arm_hang(total)


def locomotion_poses(arm_obj, cycle_frames: int, key_step: int, amp: float,
                     torso_base: dict, sway: dict, limp: float = 1.0) -> list:
    """Samples one full gait cycle into keyframes.

    The right leg runs half a cycle out of phase with the left, and each arm counters
    the opposite leg. Torso sway is sinusoidal in the cycle phase — a lateral weight
    shift plus a counter-rotating spine and a lazy head scan — so the whole cycle is
    one continuous function with no hand-placed poses to fall out of sync.

    `limp` scales the RIGHT leg's amplitude only. §06's 순찰 is supposed to be
    unhurried *and wrong*, and an asymmetric gait is the cheapest wrong there is: the
    same leg drags every cycle, so it reads as a broken thing rather than as noise.
    It is 1.0 — perfectly symmetric — on Chase, because a limp that vanishes the
    moment it commits is a much better tell than one it never had.
    """
    poses = []
    spine = torso_base.get("Spine", BASE["Spine"])
    head = torso_base.get("Head", BASE["Head"])
    hang = torso_hang(torso_base)
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
                     gait_leg(1, t, amp), gait_leg(-1, t + 0.5, amp * limp),
                     gait_arm(1, t + 0.5, amp, hang), gait_arm(-1, t, amp, hang))
        # Lateral weight shift only. Height is computed by ground_action().
        poses.append(spec_pose(arm_obj, frame, spec,
                               (sway["shift"] * math.sin(ph), 0.0, 0.0)))
    return poses


# ── Actions ─────────────────────────────────────────────────────────────────

def action_patrol(arm_obj):
    """§06 순찰 — an unhurried walk, footsteps audible, the Listener's main signal.

    56-frame cycle at 30 fps = 1.87 s for two steps, against Chase's 0.73 s. Slower
    than a walking player (§05: 2.0 m/s), which is the point: it is not chasing anyone
    yet and it knows it does not have to.

    And *wrong*, which the brief asks for and the previous version did not deliver.
    Two things carry it, both structural rather than decorative:

    * A **limp** — the right leg swings at 0.70 of the left's amplitude, so the same
      side drags on every cycle. A symmetric slow walk reads as a person taking their
      time; an asymmetric one reads as something with a broken part that has not
      slowed it down at all.
    * A **head that lolls off the axis of travel** — the sway yaw is nearly doubled
      and given a roll, so the not-a-face is rarely pointed where the feet are going.
      It is not looking for anyone. It will find them anyway.
    """
    amp = 0.72
    poses = locomotion_poses(arm_obj, cycle_frames=56, key_step=1, amp=amp,
                             torso_base={}, limp=PATROL_LIMP,
                             sway=dict(yaw=5.0, roll=6.5, spine_yaw=4.5,
                                       head_yaw=12.0, shift=0.038))
    return blendkit.make_action(arm_obj, "Patrol", poses, loop=True), amp


def action_chase(arm_obj):
    """§06 추격 — direct, committed, footsteps + a roar.

    20-frame cycle = 0.667 s, two strides → 3.0 strides/s. At full amplitude the hip
    swings ±36°, giving a 1.62 m stride → **4.86 m/s**, which brackets §06's 4.8 and
    §07's 4.4-5.2 tiers at playback speeds 0.90-1.07. The stride is therefore
    plausible at speed instead of a fast-forwarded walk.
    """
    amp = 1.0
    # 65° of cumulative forward bend against BASE's 43°: the chest is thrown out over
    # the leading foot and the head leads the body into the corridor. This is the pose the
    # shot review said did not read as running, and it did not because every one of
    # these numbers used to be POSITIVE — see the note above `world_rot`. It was
    # leaning 38° BACKWARDS while its legs ran.
    #
    # The crest lies HARD BACK here, not flared. Two reasons: reserving the flare for
    # Alert turns it into a state tell the Observer (§04 관측자) can read instead of
    # decoration; and on a back this close to horizontal a flared crest juts straight
    # up and turns the silhouette into a fin. LeftScapulaSpur is counter-pitched for
    # the same reason — inherited lean would swing the shoulder hook out level with
    # the ground.
    lean_spine, lean_chest, lean_upper = 25.0, 14.0, 9.0
    lean = {"Spine": stoop(lean_spine), "Chest": stoop(lean_chest),
            "UpperChest": stoop(lean_upper), "Neck": stoop(17.0),
            "Head": stoop(-40.0, roll=3.0), "Jaw": (24.0, 0.0, 0.0),
            "LeftScapulaSpur": spur_pitch(lean_spine + lean_chest + lean_upper)}
    lean.update(crest(0.0))
    poses = locomotion_poses(arm_obj, cycle_frames=22, key_step=1, amp=amp,
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
    # The one state that partly UNCOILS: hearing something is the only thing that
    # makes it lift its head, so the stoop eases from BASE's 43° to 29° and the crest
    # comes up. That rise is itself the tell — at 15 m (§04 관측자's ObserverRange) the
    # creature getting taller is legible long before the head angle is.
    listening = merge(
        BASE,
        {"Spine": stoop(ALERT_BEND * 0.5), "Chest": stoop(ALERT_BEND * 0.3),
         "UpperChest": stoop(ALERT_BEND * 0.2),
         "Neck": stoop(9.0), "Jaw": (0.0, 0.0, 0.0)},
        crest(rise=62.0),
        # Abandoned mid-stride: weight forward-left, right leg still behind.
        limb("leg", 1, hip=(7.0, 0.0, 0.0), knee=(2.0, 0.0, 0.0), ankle=(3.0, 0.0, 0.0)),
        limb("leg", -1, hip=(-15.0, 0.0, 0.0), knee=(13.0, 0.0, 0.0), ankle=(-9.0, 0.0, 0.0)),
        limb("arm", 1, up=(arm_hang(ALERT_BEND) - 7.0, 5.0, 0.0), lo=(13.0, 2.0, 0.0),
             ex=(-19.0, 0.0, 0.0), hd=(9.0, 0.0, 0.0)),
        limb("arm", -1, up=(arm_hang(ALERT_BEND) - 4.0, 8.0, 0.0), lo=(16.0, 3.0, 0.0),
             ex=(-23.0, 0.0, 0.0), hd=(12.0, 0.0, 0.0)),
    )

    def look(yaw, roll, neck_yaw=0.0):
        return merge(listening, {"Head": stoop(-16.0, yaw, roll),
                                 "Neck": stoop(9.0, neck_yaw, 0.0)})

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
    # Deeper than BASE, not shallower: it is searching the FLOOR of a corridor it
    # cannot stand up in, and 59° of bend puts the head down at the height of the
    # cover a player is actually behind.
    sweep_spine, sweep_chest, sweep_upper = 21.0, 12.0, 8.0
    bend = sweep_spine + sweep_chest + sweep_upper
    hang = arm_hang(bend)

    def sweep(torso_yaw, head_yaw, head_pitch, l_arm, r_arm, l_leg, r_leg):
        return merge(
            BASE,
            {"Spine": stoop(sweep_spine, yaw=torso_yaw * 0.45),
             "Chest": stoop(sweep_chest, yaw=torso_yaw * 0.35),
             "UpperChest": stoop(sweep_upper, yaw=torso_yaw * 0.2, roll=-2.0),
             "Neck": stoop(18.0, yaw=head_yaw * 0.3),
             "Head": stoop(head_pitch, yaw=head_yaw, roll=5.0),
             "LeftScapulaSpur": spur_pitch(bend),
             "Hips": (0.0, torso_yaw * 0.3, 3.0)},
            crest(rise=14.0),
            limb("arm", 1, up=(hang + l_arm[0][0],) + l_arm[0][1:], lo=l_arm[1],
                 ex=(-19.0, 0.0, 0.0), hd=(9.0, 0.0, 0.0)),
            limb("arm", -1, up=(hang + r_arm[0][0],) + r_arm[0][1:], lo=r_arm[1],
                 ex=(-23.0, 0.0, 0.0), hd=(12.0, 0.0, 0.0)),
            limb("leg", 1, hip=(l_leg, 0.0, 0.0), knee=(-l_leg * 0.5, 0.0, 0.0)),
            limb("leg", -1, hip=(r_leg, 0.0, 0.0), knee=(-r_leg * 0.5, 0.0, 0.0)),
        )

    # (frame, torso yaw, head yaw, head lift, left arm, right arm, left hip, right hip)
    #
    # Head lift is a `stoop` value against 59° of inherited bend, so it is negative
    # throughout: -22 leaves the head 37° below horizontal, casting over the floor,
    # and -50 lifts it to 9° to look down the corridor. The range is the search.
    keys = [
        (1, 22.0, 34.0, -22.0, ((16.0, -24.0, 0.0), (18.0, 0.0, 0.0)),
         ((-8.0, 27.0, 0.0), (14.0, 4.0, 0.0)), 9.0, -9.0),
        (16, 5.0, -7.0, -28.0, ((2.0, 6.0, 0.0), (13.0, 2.0, 0.0)),
         ((-2.0, 10.0, 0.0), (16.0, 3.0, 0.0)), 2.0, -2.0),
        (31, -23.0, -37.0, -20.0, ((-6.0, 29.0, 0.0), (12.0, 4.0, 0.0)),
         ((17.0, -26.0, 0.0), (20.0, 0.0, 0.0)), -11.0, 11.0),
        (46, -5.0, 9.0, -50.0, ((4.0, 8.0, 0.0), (15.0, 2.0, 0.0)),
         ((0.0, 11.0, 0.0), (17.0, 3.0, 0.0)), -3.0, 3.0),
        (61, 17.0, 27.0, -14.0, ((-9.0, 12.0, 0.0), (9.0, 2.0, 0.0)),
         ((-6.0, 14.0, 0.0), (11.0, 3.0, 0.0)), 6.0, -6.0),
        (76, 2.0, -15.0, -34.0, ((10.0, -14.0, 0.0), (17.0, 2.0, 0.0)),
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

    The single event is one 2.2° head roll and 1.4° yaw over SIX frames at 1.6 s in,
    held for a second, unwound before the loop closes. Every other span is two
    identical keys, which makes those F-curve segments exactly flat.

    Halved from the 4.5° it used to carry, and the brief's reasoning is the reason:
    the line it has to produce is *"어디 갔어? 방금 여기 있었는데"*, and that line comes
    from a player who cannot decide whether the shape at the end of the corridor moved
    or whether they blinked. 4.5° over four frames is visible, and anything visible
    answers the question. 2.2° over six is under the threshold at 8 m and over it at
    3 m, so the doubt survives exactly as far as the beam does.

    `verify_motion` asserts the total excursion stays under 3° and that the legs and
    hips move by 0. 90 frames = 3.0 s; §06 holds this state for 5 s
    (GameConstants.StandstillSeconds).
    """
    still = merge(BASE, {"Jaw": (2.0, 0.0, 0.0)})
    tilt = merge(still, {"Head": stoop(-24.0, yaw=1.4, roll=7.2),
                         "Neck": stoop(STOOP_NECK, yaw=0.6)})
    keys = [(1, still), (44, still), (50, tilt), (52, tilt), (82, tilt), (88, still), (91, still)]
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
    def hit(spine, head, jaw, crest_rise, arm_up, arm_lo, l_leg, r_leg, drop):
        l_hip, l_knee = l_leg
        r_hip, r_knee = r_leg
        # Chest and upper chest hold BASE's proportions of the spine, so one number
        # drives the whole recoil and the stoop cannot come apart mid-clip.
        chest, upper = spine * 0.60, spine * 0.40
        hang = arm_hang(spine + chest + upper)
        return merge(
            BASE,
            {"Spine": stoop(spine), "Chest": stoop(chest), "UpperChest": stoop(upper),
             "Neck": stoop(STOOP_NECK + (spine - STOOP_SPINE) * 0.4),
             "Head": stoop(head, roll=5.0), "Jaw": (jaw, 0.0, 0.0),
             "LeftScapulaSpur": spur_pitch(spine + chest + upper)},
            crest(rise=crest_rise),
            limb("arm", 1, up=(hang + arm_up, 24.0, 0.0), lo=(arm_lo, 3.0, 0.0),
                 ex=(-19.0 - arm_lo * 0.3, 0.0, 0.0), hd=(9.0, 0.0, 0.0)),
            limb("arm", -1, up=(hang + arm_up * 0.9, 30.0, 0.0), lo=(arm_lo * 1.1, 4.0, 0.0),
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
        #
        # `spine` is forward bend (BASE = 15) and `head` is arch against the neck
        # (BASE = -24). The recoil therefore runs spine DOWNWARD from 15 to -8 — the
        # creature is thrown up out of its own stoop and briefly arches backwards,
        # which is the shape a blow to the sensory organ makes and the shape these
        # keys were always meant to describe. They previously read 11 → -19, which
        # under the sign trap above folded it 19° FORWARD instead.
        (1, 15.0, -24.0, 4.0, 0.0, -7.0, 13.0, (0.0, 0.0), (0.0, 0.0), 0.0),
        # the flash lands — head thrown back, crest slammed flat, front knee braces
        (4, -2.0, -50.0, 34.0, -26.0, 46.0, -58.0, (14.0, -8.0), (-9.0, -4.0), -0.04),
        # deepest recoil: the braced knee buckles and the rear leg takes the weight
        (10, -8.0, -56.0, 30.0, -30.0, 52.0, -66.0, (21.0, -30.0), (-15.0, -12.0), -0.13),
        (22, 0.0, -46.0, 22.0, -22.0, 38.0, -50.0, (16.0, -23.0), (-11.0, -9.0), -0.10),
        # coming back — §04's stun is weak, so recovery starts well before it ends
        (38, 6.0, -38.0, 14.0, -14.0, 20.0, -26.0, (9.0, -13.0), (-6.0, -5.0), -0.06),
        (56, 12.0, -29.0, 7.0, -6.0, 4.0, -2.0, (3.0, -5.0), (-2.0, -2.0), -0.02),
        (70, 14.0, -25.0, 4.0, -1.0, -6.0, 11.0, (1.0, -1.0), (0.0, 0.0), 0.0),
        # frame 76 = exactly BASE, so it blends back into Chase with no pop.
        # 1→76 spans 75 frame intervals = 2.500 s = GameConstants.FlashStunSeconds.
        (76, 15.0, -24.0, 4.0, 0.0, -7.0, 13.0, (0.0, 0.0), (0.0, 0.0), 0.0),
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
    def beat(spine, head, jaw, crest_rise, l_arm, r_arm, l_leg, r_leg, hips):
        chest, upper = spine * 0.60, spine * 0.40
        hang = arm_hang(spine + chest + upper)
        return merge(
            BASE,
            {"Spine": stoop(spine), "Chest": stoop(chest),
             "UpperChest": stoop(upper, roll=-2.0),
             "Neck": stoop(16.0), "Head": stoop(head, roll=4.0), "Jaw": (jaw, 0.0, 0.0),
             # Cancel most of the inherited lean so the shoulder hook stays diagonal.
             "LeftScapulaSpur": spur_pitch(spine + chest + upper)},
            crest(rise=crest_rise),
            limb("arm", 1, up=(hang + l_arm[0][0],) + l_arm[0][1:],
                 lo=l_arm[1], ex=l_arm[2], hd=l_arm[3]),
            limb("arm", -1, up=(hang + r_arm[0][0],) + r_arm[0][1:],
                 lo=r_arm[1], ex=r_arm[2], hd=r_arm[3]),
            limb("leg", 1, hip=(l_leg, 0.0, 0.0), knee=(-l_leg * 0.6, 0.0, 0.0),
                 ankle=(l_leg * 0.3, 0.0, 0.0)),
            limb("leg", -1, hip=(r_leg, 0.0, 0.0), knee=(-r_leg * 0.6, 0.0, 0.0),
                 ankle=(r_leg * 0.3, 0.0, 0.0)),
        ), hips

    # `spine` is forward bend and `head` is arch against the neck, per `stoop`. Beat 14
    # runs the spine to 38° — past Chase's 25 — because the lunge is the one moment
    # this creature is not conserving anything.
    keys = [
        # 1 — arriving, mid-chase
        (1, 25.0, -40.0, 20.0, 0.0,
         ((-20.0, 10.0, 0.0), (16.0, 2.0, 0.0), (-19.0, 0.0, 0.0), (9.0, 0.0, 0.0)),
         ((22.0, 12.0, 0.0), (10.0, 3.0, 0.0), (-23.0, 0.0, 0.0), (12.0, 0.0, 0.0)),
         26.0, -22.0, (0.0, 0.0, 0.0)),
        # 6 — coil: arms drawn back and out, torso pulled up, maw opening. The crest
        # flares here and nowhere else in this clip — the last thing you see.
        (6, 8.0, -30.0, 34.0, 46.0,
         ((-38.0, 34.0, 0.0), (24.0, 6.0, 0.0), (-30.0, 0.0, 0.0), (4.0, 0.0, 0.0)),
         ((-36.0, 36.0, 0.0), (26.0, 6.0, 0.0), (-32.0, 0.0, 0.0), (6.0, 0.0, 0.0)),
         8.0, -6.0, (0.0, 0.0, 0.0)),
        # 14 — the throw: both arms out front, everything committed forward
        (14, 38.0, -52.0, 42.0, 26.0,
         ((18.0, 16.0, 0.0), (-18.0, 2.0, 0.0), (-6.0, 0.0, 0.0), (34.0, 0.0, 0.0)),
         ((16.0, 18.0, 0.0), (-16.0, 2.0, 0.0), (-8.0, 0.0, 0.0), (36.0, 0.0, 0.0)),
         34.0, -14.0, (0.0, -0.05, 0.0)),
        # 20 — clamp: arms cross inward, hands close, maw comes down
        (20, 34.0, -40.0, 12.0, 6.0,
         ((16.0, -26.0, 14.0), (-44.0, -10.0, 0.0), (-14.0, 0.0, 0.0), (48.0, 0.0, 0.0)),
         ((14.0, -28.0, 14.0), (-42.0, -12.0, 0.0), (-16.0, 0.0, 0.0), (50.0, 0.0, 0.0)),
         20.0, -10.0, (0.0, -0.03, 0.0)),
        # 28 — fold the catch to the chest, head down over it
        (28, 25.0, -4.0, 16.0, -14.0,
         ((22.0, -22.0, 18.0), (-64.0, -12.0, 0.0), (-22.0, 0.0, 0.0), (44.0, 0.0, 0.0)),
         ((20.0, -24.0, 18.0), (-62.0, -14.0, 0.0), (-24.0, 0.0, 0.0), (46.0, 0.0, 0.0)),
         6.0, -4.0, (0.0, 0.0, 0.0)),
        # 41 — settled, holding. Loops or blends out from here. 40 intervals = 1.333 s.
        (41, 22.0, -8.0, 9.0, -18.0,
         ((25.0, -20.0, 16.0), (-60.0, -10.0, 0.0), (-20.0, 0.0, 0.0), (40.0, 0.0, 0.0)),
         ((23.0, -22.0, 16.0), (-58.0, -12.0, 0.0), (-22.0, 0.0, 0.0), (42.0, 0.0, 0.0)),
         2.0, -2.0, (0.0, 0.0, 0.0)),
    ]
    poses = []
    for f, sp, hd, jw, cr, la, ra, ll, rl, hips in keys:
        spec, h = beat(sp, hd, jw, cr, la, ra, ll, rl, hips)
        poses.append(spec_pose(arm_obj, f, spec, h))
    return blendkit.make_action(arm_obj, "Grab", poses, loop=False), None


# ── Skin: procedural PBR, on the world's own terms ──────────────────────────
#
# The monster shipped with three flat colours while every wall, floor and prop in
# the game got a full PBR set out of tools/textures/gen_textures.py. Under §03's
# beam that is not a stylistic difference, it is a lighting failure: a flat
# surface has no normal to catch the spot's grazing edge, so the creature renders
# as a matte cut-out pasted over a textured room and stops reading as an object
# that is *in* the room.
#
# The maps below are written into Assets/Textures/ under gen_textures.py's own
# file-name convention, which is what makes the existing importer
# (Editor/TextureImport/ProceduralTextureImport.cs) configure them correctly with
# no new import rule: sRGB on the albedo only, NormalMap type on the normal,
# smoothness surviving in the mask's alpha.
#
# gen_textures.py itself cannot be imported here — it needs scipy and Blender's
# bundled Python has numpy only — so its calibration constants are READ OUT OF
# ITS SOURCE rather than copied. Duplicating 0.15 by hand would work until the
# day someone moves the band, and then the monster would be the one surface in
# the game outside it, silently.

TEXTURE_PIPELINE = os.path.join(blendkit.REPO_ROOT, "tools", "textures", "gen_textures.py")

TEXTURE_DIR = os.path.join(blendkit.UNITY_ASSETS, "Textures")
"""Where gen_textures.py writes, and therefore where the importer is watching."""

SKIN_MANIFEST = os.path.join(TEXTURE_DIR, "Monster.textures.json")
"""Read by the Unity side so tiling and slot binding are data, not a second list."""


def pipeline_constants() -> dict:
    """The calibration numbers gen_textures.py enforces, parsed from its source.

    Only the four that constrain a *material* rather than a floor: the albedo band
    §03 needs the beam to land inside, the roughness floor that stops a wet
    surface blowing out under a spot a metre away, and the resolution the rest of
    the set is authored at.
    """
    try:
        with open(TEXTURE_PIPELINE, "r", encoding="utf-8") as handle:
            source = handle.read()
    except OSError as exc:
        blendkit.fail(f"cannot read {TEXTURE_PIPELINE}: {exc}. The monster's skin is calibrated "
                      "against that pipeline; guessing its band would put the creature outside it.")
        raise

    wanted = ("ALBEDO_MIN_LINEAR", "ALBEDO_MAX_LINEAR", "MIN_ROUGHNESS", "DEFAULT_RESOLUTION")
    found = {}
    for name in wanted:
        m = re.search(r"^%s\s*=\s*([0-9.]+)\s*$" % name, source, re.MULTILINE)
        if m is None:
            blendkit.fail(f"{name} is no longer a plain assignment in gen_textures.py — the monster's "
                          "skin reads its calibration from there and can no longer find it.")
        found[name] = float(m.group(1))
    found["DEFAULT_RESOLUTION"] = int(found["DEFAULT_RESOLUTION"])
    found["CORRIDOR_DARKEST"] = corridor_darkest_surface()
    return found


WORLD_MANIFEST = os.path.join(TEXTURE_DIR, "Textures.manifest.json")
"""What gen_textures.py actually put on the walls the creature stands against."""


def corridor_darkest_surface() -> float:
    """Linear albedo of the darkest wall or ceiling in a §12 corridor.

    The brief's requirement is relational — "albedo must be DARKER than the corridor
    walls so the beam reveals it rather than it glowing" — so it is encoded as a
    relation and not as a number somebody typed. `write_skin` uses this as a ceiling
    the creature's brightest texel may not cross, which means re-grading the basement
    darker automatically re-grades the creature darker with it, and re-grading it
    lighter never silently leaves the monster as the pale thing in the room.

    Walls and ceiling only. Floors are excluded on purpose: the creature is almost
    never seen against one — §06 has it coming down a corridor at eye height — and
    Floor_Tile at 0.32 would raise the ceiling by half for a surface it is standing
    on rather than in front of.
    """
    try:
        with open(WORLD_MANIFEST, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
    except (OSError, ValueError) as exc:
        blendkit.fail(f"cannot read {WORLD_MANIFEST}: {exc}. The creature's albedo ceiling is "
                      "defined against the corridor it stands in; without the corridor there is "
                      "nothing to be darker than. Run tools/textures/gen_textures.py first.")
        raise

    surfaces = [m for m in payload.get("materials", [])
                if str(m.get("name", "")).startswith(("Wall_", "Ceiling_"))]
    if not surfaces:
        blendkit.fail(f"{WORLD_MANIFEST} lists no Wall_/Ceiling_ materials, so there is no "
                      "corridor albedo to calibrate the creature against.")
    return min(float(m["albedo_mean_linear"]) for m in surfaces)


def _soft_ceiling(x: np.ndarray, ceiling: float, knee: float = 0.88) -> np.ndarray:
    """Rolls the bright tail off toward `ceiling` asymptotically instead of clipping.

    A hard clip would be simpler and is wrong: it flattens every highlight into one
    plateau at exactly the ceiling value, and a plateau on a creature lit by a moving
    spot reads as a blown-out patch — which is the artefact this exists to remove. An
    exponential knee is monotone, leaves the bottom `knee` of the range untouched, and
    never reaches the ceiling, so ordering between texels is preserved everywhere.

    `knee` has to be HIGH. At 0.62 the knee sat at 0.13 linear, below the hide's own
    median, so nearly every texel was being compressed toward the ceiling and the map
    came out almost flat: measured p5 to p95 spanned 0.157–0.202 linear, a total range
    of 0.045, and the creature photographed as one uniform grey mass with no structure
    for §03's beam to find. At 0.88 only the genuine outliers — the top few per cent
    that were brighter than the wall — are touched, and everything below is left
    exactly as painted.
    """
    knee_at = ceiling * knee
    headroom = max(ceiling - knee_at, 1e-6)
    over = np.maximum(x - knee_at, 0.0)
    return np.where(x <= knee_at, x,
                    knee_at + headroom * (1.0 - np.exp(-over / headroom))).astype(np.float32)


# ── numpy-only reimplementations of the pipeline's field operators ───────────
# Same definitions as gen_textures.py, minus scipy. `_blur` is the only one that
# differs in kind: an exact Gaussian multiply in the frequency domain instead of
# ndimage's truncated kernel, which is both periodic by construction and closer
# to the intent than `mode="wrap"` on a clipped kernel.


def _rng(seed: int) -> np.random.Generator:
    return np.random.default_rng(seed)


def _normalise01(field: np.ndarray) -> np.ndarray:
    low, high = float(field.min()), float(field.max())
    if high - low < 1e-12:
        return np.full_like(field, 0.5, dtype=np.float32)
    return ((field - low) / (high - low)).astype(np.float32)


def _blur(field: np.ndarray, sigma: float) -> np.ndarray:
    """Periodic Gaussian blur. Exact, because a Gaussian is a Gaussian in either domain."""
    if sigma <= 0.0:
        return field.astype(np.float32)
    res = field.shape[0]
    fy = np.fft.fftfreq(res)[:, None]
    fx = np.fft.fftfreq(res)[None, :]
    kernel = np.exp(-2.0 * (math.pi ** 2) * (sigma ** 2) * (fy * fy + fx * fx))
    return np.real(np.fft.ifft2(np.fft.fft2(field) * kernel)).astype(np.float32)


def _fbm(res: int, seed: int, beta: float = 1.9, low_cycles: float = 2.0,
         high_cycles: float | None = None, stretch: float = 1.0) -> np.ndarray:
    """Spectral fractal noise, normalised to [0,1].

    `stretch` is the one addition over gen_textures.fbm: dividing the vertical
    frequency by it before the power law smears the noise along one axis, which is
    how the hide gets sinew running *down* the limb instead of isotropic mottle.
    Anisotropy is most of what makes a creature surface read as tensioned rather
    than as rock.
    """
    fy = np.fft.fftfreq(res)[:, None] * res
    fx = np.fft.fftfreq(res)[None, :] * res
    freq = np.sqrt((fy / stretch) ** 2 + fx * fx)
    radial = np.sqrt(fy * fy + fx * fx)

    amplitude = np.zeros_like(freq)
    np.divide(1.0, np.power(freq, beta), out=amplitude, where=freq > 0)

    # Gate on the STRETCHED frequency as well as the radial one. Only gating on
    # `radial` — which is what this did, following gen_textures.py, where `stretch` is
    # always 1 and the two are the same thing — lets a mode with a large radial
    # frequency but a tiny stretched one through carrying amplitude (stretch/f)^beta.
    # At stretch 3.6 that is a factor of ~8 over everything else, and a handful of
    # such modes dominated the field: the hide came out as horizontal banding with
    # clipped-white blobs where the bands crossed, which photographed at 3 m as
    # patches of disease. Bounding the amplitude at (1/low_cycles)^beta is the fix,
    # and it is the same gate the isotropic case already applies.
    amplitude[freq < low_cycles] = 0.0
    amplitude[radial < low_cycles] = 0.0
    if high_cycles is not None:
        amplitude[radial > high_cycles] = 0.0

    field = np.real(np.fft.ifft2(np.fft.fft2(_rng(seed).standard_normal((res, res))) * amplitude))

    # Robust rescale rather than min/max. `_normalise01` divides by the extremes, so a
    # single outlying texel compresses the whole field into a narrow band and then the
    # smoothstep bands downstream land in the wrong place. Clipping to the 0.5–99.5
    # percentiles first costs those texels and nothing else.
    lo, hi = np.percentile(field, (0.5, 99.5))
    if hi - lo < 1e-12:
        return np.full_like(field, 0.5, dtype=np.float32)
    return np.clip((field - lo) / (hi - lo), 0.0, 1.0).astype(np.float32)


def _worley(res: int, cells: int, seed: int, jitter: float = 1.0):
    """Periodic cellular noise. Returns (F1, F2, owner) exactly as gen_textures does."""
    generator = _rng(seed)
    points = (np.stack(np.meshgrid(np.arange(cells), np.arange(cells), indexing="ij"), axis=-1)
              + 0.5 + (generator.random((cells, cells, 2)) - 0.5) * jitter) / cells

    axis = (np.arange(res) + 0.5) / res
    py, px = axis[:, None], axis[None, :]
    cell_y = np.minimum((py * cells).astype(np.int32), cells - 1)
    cell_x = np.minimum((px * cells).astype(np.int32), cells - 1)

    best1 = np.full((res, res), 4.0, dtype=np.float32)
    best2 = np.full((res, res), 4.0, dtype=np.float32)
    owner = np.zeros((res, res), dtype=np.int32)

    for oy in (-1, 0, 1):
        for ox in (-1, 0, 1):
            ny, nx = (cell_y + oy) % cells, (cell_x + ox) % cells
            feature = points[ny, nx]
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


def _per_cell(owner: np.ndarray, cells: int, seed: int, low: float, high: float) -> np.ndarray:
    return _rng(seed).uniform(low, high, size=cells * cells).astype(np.float32)[owner]


def _smoothstep(low: float, high: float, x: np.ndarray) -> np.ndarray:
    """Hermite fade between two thresholds. `high` below `low` inverts the ramp.

    The inversion is the whole point and this copy used to destroy it. It divided by
    ``max(high - low, 1e-6)``, so a descending range — ``_smoothstep(0.30, 0.05, f)``,
    meaning "1 in the deep creases, fading out by 0.30" — got a denominator of 1e-6
    instead of −0.25 and collapsed into a HARD BINARY STEP, inverted: 1 everywhere
    above 0.30 rather than a smooth ramp below 0.05.

    Four call sites here are descending (creases, pores, carapace seams, maw beads),
    so four of the creature's features were binary masks covering most of the map with
    aliased edges. That is what the shot review was looking at when it called the
    surface blotchy: the hard-edged pale islands were not a paint choice, they were
    this. gen_textures.py's own smoothstep divides by ``high - low + 1e-9``, which
    keeps the sign; this now matches it, which is what it always claimed to do.
    """
    span = high - low
    t = np.clip((x - low) / (span + math.copysign(1e-9, span or 1.0)), 0.0, 1.0)
    return (t * t * (3.0 - 2.0 * t)).astype(np.float32)


def _warp(field: np.ndarray, dy: np.ndarray, dx: np.ndarray) -> np.ndarray:
    """Integer domain warp with wraparound — periodic, so it cannot open a seam."""
    res = field.shape[0]
    iy, ix = np.meshgrid(np.arange(res), np.arange(res), indexing="ij")
    return field[(iy + np.rint(dy * res).astype(np.int32)) % res,
                 (ix + np.rint(dx * res).astype(np.int32)) % res]


def _height_to_normal(height: np.ndarray, world_size: float, height_scale: float) -> np.ndarray:
    """Tangent-space normal from the true slope, OpenGL convention. gen_textures.py's."""
    res = height.shape[0]
    metres_per_texel = world_size / res
    dz_dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * height_scale / (2.0 * metres_per_texel)
    dz_dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * height_scale / (2.0 * metres_per_texel)
    normal = np.stack([-dz_dx, -dz_dy, np.ones_like(height)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    return normal.astype(np.float32)


def _ambient_occlusion(height: np.ndarray, world_size: float, height_scale: float,
                       strength: float = 1.0) -> np.ndarray:
    """Cavity AO over four radii. gen_textures.py's, with the periodic blur above."""
    res = height.shape[0]
    relief = np.zeros_like(height)
    scales = 0
    for radius_metres in (0.004, 0.010, 0.025, 0.060):
        sigma = radius_metres / world_size * res
        if sigma < 0.7:
            continue
        drop = np.clip(_blur(height, sigma) - height, 0.0, None) * height_scale
        relief += drop / radius_metres
        scales += 1
    if scales == 0:
        return np.ones_like(height)
    return np.clip(1.0 - (relief / scales) * strength * 3.2, 0.0, 1.0).astype(np.float32)


def _linear_to_srgb(x: np.ndarray) -> np.ndarray:
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1.0 / 2.4) - 0.055)


def _to_u8(x: np.ndarray) -> np.ndarray:
    return np.clip(np.rint(np.asarray(x, dtype=np.float64) * 255.0), 0, 255).astype(np.uint8)


def _png_chunk(tag: bytes, data: bytes) -> bytes:
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def _write_png(path: str, image: np.ndarray) -> int:
    """Writes an 8-bit PNG. Same hand-rolled writer as the texture pipeline, and for
    the same reason: it keeps the dependency list at numpy."""
    if image.dtype != np.uint8:
        raise TypeError("write_png wants uint8; convert deliberately so rounding is visible")
    if image.ndim == 3 and image.shape[2] == 3:
        colour_type, channels = 2, 3
    elif image.ndim == 3 and image.shape[2] == 4:
        colour_type, channels = 6, 4
    else:
        raise ValueError("monster maps are RGB or RGBA, got shape %r" % (image.shape,))

    height, width = image.shape[0], image.shape[1]
    raw = np.zeros((height, width * channels + 1), dtype=np.uint8)
    raw[:, 1:] = image.reshape(height, width * channels)

    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(_png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, colour_type, 0, 0, 0)))
        handle.write(_png_chunk(b"IDAT", zlib.compress(raw.tobytes(), 9)))
        handle.write(_png_chunk(b"IEND", b""))
    return os.path.getsize(path)


# ── The three surfaces ──────────────────────────────────────────────────────
#
# The albedo targets are the single most important art decision in this file, and
# they have now been wrong in both directions. What follows is the measurement trail,
# because the argument is genuinely two-sided and the next person will re-litigate it.
#
# **Too dark (0.15–0.17).** Rendered in a §12 corridor, a creature at 8 m measured
# 0.1106 luminance against a 0.1137 background: 0.003 of contrast, invisible.
#
# **Too pale (0.24 hide / 0.20 plate).** The correction argued that §03's beam is
# mounted on the player (§05: 손전등이 포인터가 된다) and falls off with distance, so
# the near creature out-lights the far wall and albedo multiplies that advantage.
# True, and it overshot: the shot review measured the creature rendering **0.0964
# brighter** than the corridor. It stopped being a thing the beam finds and became a
# pale object that announces itself — the exact opposite of §06, where the whole
# design is that you notice it too late.
#
# **What was actually wrong both times** is that contrast was being bought with the
# MEAN. The mean is the one number that has to lose: at 8 m it fights the wall, at
# 3 m it blows out. So the mean now goes decisively UNDER the corridor and the
# contrast comes from somewhere else:
#
#   corridor, measured out of Textures.manifest.json (linear albedo means)
#     Wall_Brick_Painted   0.22    Ceiling_Concrete_Formed  0.21
#     Wall_Concrete_Bare   0.27    Floor_Concrete           0.28
#     Wall_Plaster_Stained 0.34    Floor_Tile               0.32
#
# The hide is 0.170 — 0.05 under the darkest wall in the corridor and 0.17 under the
# brightest — and the plates are 0.152, at gen_textures.py's floor. Against that, the
# contrast is carried by the three channels that do not move the mean:
#
#   * **Range, not level.** The hide's creases go to 0.07 and its stretched sinew to
#     0.36 around a 0.17 mean, so the beam crossing it finds structure to reveal.
#     A wall has no such range; that is the difference the eye reads.
#   * **Specular.** The plates are the smoothest, most metallic surface in the game
#     (roughness 0.21, metallic 0.28). A dark gloss under a hard-shadowed spot is a
#     moving highlight on a shape you cannot otherwise see, which is exactly the
#     "something moved" §06 wants and no diffuse wall produces.
#   * **Occlusion.** Relief of 15 mm on the hide with real cavity AO means the
#     creature carries its own shadowing, so it separates from the wall even when the
#     beam lights both equally.
#
# Net: it is darker than everything around it, and it resolves out of the darkness
# when the beam lands on it instead of glowing before the beam arrives.


def build_flesh(res: int) -> dict:
    """Hide stretched over structure. The bulk of the silhouette.

    Anisotropic sinew running along the limb, pocked with a fine cell pattern and
    creased by a coarse warp. The creases are where the roughness drops — a body
    is wet in its folds and dry on its stretched faces, and that contrast is what
    makes the beam travel across it instead of flattening it.
    """
    # low_cycles 11, not 3. At 3 the bright "stretched" regions were 30 cm across on a
    # 0.85 m tile, and photographed in the corridor they read as MOULD — pale continents
    # drifting over the body, which is the single loudest "this is a texture" cue a
    # creature can carry. At 11 the same field becomes 8 cm cords running along the
    # limb, which is what sinew is, and `stretch` 3.6 makes them run one way instead of
    # pooling.
    sinew = _fbm(res, 5101, beta=1.55, low_cycles=11.0, stretch=3.6)
    mottle = _fbm(res, 5102, beta=2.1, low_cycles=6.0)
    grain = _fbm(res, 5103, beta=1.2, low_cycles=24.0)

    # Creases: warp a coarse field by a finer one so the folds meander.
    warp_y = (_fbm(res, 5104, beta=2.2, low_cycles=2.0) - 0.5) * 0.06
    warp_x = (_fbm(res, 5105, beta=2.2, low_cycles=2.0) - 0.5) * 0.06
    crease = _normalise01(_warp(_fbm(res, 5106, beta=2.4, low_cycles=4.0, stretch=1.6), warp_y, warp_x))
    crease = _smoothstep(0.30, 0.05, crease)          # deep, narrow, dark

    pore_f1, _, pore_owner = _worley(res, 64, 5107, jitter=0.95)
    pore = _smoothstep(0.0090, 0.0, pore_f1)
    pore_tone = _per_cell(pore_owner, 64, 5108, 0.84, 1.16)

    height = np.clip(
        0.50 + (sinew - 0.5) * 0.55 + (mottle - 0.5) * 0.34
        + (grain - 0.5) * 0.10 - crease * 0.46 - pore * 0.20, 0.0, 1.0).astype(np.float32)

    # Grey-brown, barely chromatic. §12's basement is graded cold; a creature with
    # any real saturation reads as a prop lit by a different scene.
    base = np.array([0.171, 0.157, 0.142], dtype=np.float32)
    bruise = np.array([0.038, 0.030, 0.032], dtype=np.float32)   # creases go nearly black
    pallor = np.array([0.296, 0.280, 0.257], dtype=np.float32)   # stretched sinew, barely lighter

    variation = (0.88 + 0.24 * mottle) * pore_tone
    colour = base.reshape(1, 1, 3) * variation[..., None]
    colour = colour * (1.0 - crease[..., None] * 0.88) + bruise.reshape(1, 1, 3) * crease[..., None] * 0.88
    # Photographed at 3 m, a strong pallor mix over a narrow smoothstep band came out
    # as hard-edged WHITE PATCHES — the creature looked diseased rather than sinewy,
    # and they were the brightest pixels on it, which is what was still reading as
    # "pale object" after the mean had already gone under the corridor.
    #
    # A tendon under skin is not a different COLOUR, it is a different SURFACE: it
    # stands proud and it is wetter. So the ridge signal moves almost entirely into
    # roughness and height below, and what stays in albedo is a wide, soft, 26% lift
    # that never resolves into a patch with an edge.
    ridge = _smoothstep(0.62, 0.99, sinew)
    stretched = ridge[..., None]
    colour = colour * (1.0 - stretched * 0.26) + pallor.reshape(1, 1, 3) * stretched * 0.26

    # Dry, and drier than it looks like it should be. A §12 corridor lamp hangs at
    # 2.5 m and the creature's shoulders reach 2.0, so at close range it is lit from
    # half a metre away — measured at 3 m its lit side reached 0.31 mean with the
    # highlights clipped, and a clipped shoulder throws away both the hide's structure
    # and the maw's tell underneath it. Rolling the specular off is the half of that
    # which does not cost the 8 m read that the albedo buys.
    # The ridges are the wet part. 0.24 of roughness swing across a cord is what makes
    # §03's beam travel along the limb instead of sitting on it as one hotspot, and it
    # is the half of the sinew read that survives the albedo going flat.
    roughness = (0.87 - 0.24 * ridge - 0.30 * crease
                 + 0.08 * (grain - 0.5)).astype(np.float32)

    return dict(albedo=colour, roughness=roughness, height=height,
                metallic=np.zeros((res, res), dtype=np.float32),
                world_size=0.85, height_scale=0.020)


def build_carapace(res: int) -> dict:
    """The plates: head blades, crest, knee and elbow caps, rib shards, claws.

    Near-black and the smoothest surface on the creature, on purpose. §03's beam is
    a narrow hard-shadowed spot, and a smooth dark plate turns it into a moving
    specular streak along the plate's edge — which is the one cue that survives at
    15 m through fog, where albedo detail is long gone. Darkness plus a highlight
    is a silhouette with an edge on it; darkness alone is a hole.
    """
    _, plate_f2, plate_owner = _worley(res, 11, 5201, jitter=0.85)
    plate_f1, _, _ = _worley(res, 11, 5201, jitter=0.85)
    seam = _smoothstep(0.030, 0.0, plate_f2 - plate_f1)
    dome = _smoothstep(0.0, 0.055, plate_f1)
    plate_tone = _per_cell(plate_owner, 11, 5202, 0.80, 1.22)

    # Growth ridges: a warped stripe field, so each plate looks accreted rather
    # than moulded.
    warp = (_fbm(res, 5203, beta=2.0, low_cycles=2.0) - 0.5) * 0.10
    lines = np.sin((np.arange(res)[:, None] / res + warp) * 2.0 * math.pi * 34.0)
    ridge = ((lines * 0.5 + 0.5) ** 2).astype(np.float32)

    chip_f1, _, chip_owner = _worley(res, 58, 5204, jitter=1.0)
    chipped = (_smoothstep(0.009, 0.0, chip_f1)
               * _smoothstep(0.55, 0.85, _per_cell(chip_owner, 58, 5205, 0.0, 1.0)))

    height = np.clip(0.42 + dome * 0.40 + ridge * 0.12 - seam * 0.55 - chipped * 0.30,
                     0.0, 1.0).astype(np.float32)

    base = np.array([0.146, 0.134, 0.128], dtype=np.float32)
    dust = np.array([0.232, 0.214, 0.189], dtype=np.float32)   # grit settled in the seams
    colour = base.reshape(1, 1, 3) * (plate_tone * (0.86 + 0.28 * dome))[..., None]
    grit = np.clip(seam * 0.8 + chipped * 0.5, 0.0, 1.0)[..., None]
    colour = colour * (1.0 - grit) + dust.reshape(1, 1, 3) * grit

    roughness = (0.21 + 0.36 * seam + 0.32 * chipped + 0.06 * ridge).astype(np.float32)

    # A shell's sheen is a thin-film effect, not a metal, but a little metallic is
    # how a dielectric that glossy is faked without a custom shader.
    metallic = (0.28 * (1.0 - seam) * (1.0 - chipped)).astype(np.float32)

    return dict(albedo=colour, roughness=roughness, height=height, metallic=metallic,
                world_size=0.32, height_scale=0.009)


def build_maw(res: int) -> dict:
    """The inside of the mandible — the only warm surface, and only just.

    §06 gives the creature no eyes to read, so the maw is the only part of it that
    can signal anything, and MonsterAcquireTell drives its emission when the chase
    starts. That means this map is doing double duty: albedo when it is closed, and
    the shape of the glow when it opens. Hence the ridged, radial folds — lit from
    within they read as a throat rather than as a lamp.
    """
    folds = _fbm(res, 5301, beta=2.0, low_cycles=3.0, stretch=4.0)
    ridged = 1.0 - np.abs(folds * 2.0 - 1.0)
    fine = _fbm(res, 5302, beta=1.5, low_cycles=18.0)
    wet_f1, _, wet_owner = _worley(res, 40, 5303, jitter=1.0)
    beads = _smoothstep(0.011, 0.0, wet_f1) * _per_cell(wet_owner, 40, 5304, 0.4, 1.0)

    height = np.clip(0.45 + (ridged - 0.5) * 0.62 + (fine - 0.5) * 0.16 + beads * 0.22,
                     0.0, 1.0).astype(np.float32)

    deep = np.array([0.074, 0.030, 0.027], dtype=np.float32)
    raw = np.array([0.226, 0.106, 0.091], dtype=np.float32)
    mix = _smoothstep(0.25, 0.85, ridged)[..., None]
    colour = (deep.reshape(1, 1, 3) * (1.0 - mix) + raw.reshape(1, 1, 3) * mix) * (0.85 + 0.30 * fine)[..., None]

    roughness = (0.42 - 0.18 * _smoothstep(0.3, 0.9, ridged) - 0.16 * beads).astype(np.float32)

    return dict(albedo=colour, roughness=roughness, height=height,
                metallic=np.zeros((res, res), dtype=np.float32),
                world_size=0.22, height_scale=0.010)


def build_eyes(res: int) -> dict:
    """The lenses: a dying lamp behind fouled glass, not a lit pair of irises.

    This map's real job is not albedo, it is the *shape of the light*: MonsterSkin
    binds each material's albedo as its own emission map, so whatever is painted here
    is what the glow looks like. The previous paint was a nearly flat field, and the
    shot review named the result precisely — two even glowing discs on a smooth skull,
    which is a cartoon robot. An even disc is what a manufactured lamp looks like;
    nothing organic emits evenly.

    So the field is broken three ways, all of them multiplicative into EyeGlow:

    * **dead cells** — whole Worley cells crushed to near-black, like soot baked onto
      glass. Roughly a third of the lens does not glow at all.
    * **membrane drift** — a broad low-frequency occlusion, so one side of a lens is
      always dimmer, and because the two lenses sit at different points of the tiled
      map, the left and right eye come out *unequal* without either being authored.
    * **pin flecks** — a few texels near the albedo ceiling. At 3 m they read as
      points of heat behind the glass; at 15 m they average away.

    The MEAN is untouched policy: write_skin still lands it on its target, so at
    ObserverRange the pair carries exactly the luminance §04 was calibrated against —
    the redistribution only exists inside the lens, at the range where a player is
    close enough to see what the light is coming out of.
    """
    crazing = _fbm(res, 5401, beta=2.4, low_cycles=26.0)
    bloom = _fbm(res, 5402, beta=1.7, low_cycles=2.6)
    _, _, soot_owner = _worley(res, 9, 5403, jitter=1.0)
    dead = _smoothstep(0.42, 0.70, _per_cell(soot_owner, 9, 5404, 0.0, 1.0))
    membrane = _smoothstep(0.20, 0.85, bloom)
    smoulder = 0.30 + 0.70 * _smoothstep(0.40, 0.92, bloom)
    flecks = _smoothstep(0.905, 0.985, crazing) * 1.8

    # The three occlusions multiply, so their floors are generous — the first cut of
    # this stacked them at ~0.02 mean and write_skin's gain guard rightly refused the
    # 9.7x correction. The mean is write_skin's job; this field only owns the RATIO
    # between the dead glass and the live glass (about 8:1).
    level = np.clip(smoulder * (1.0 - dead * 0.78) * (0.45 + 0.55 * (1.0 - membrane))
                    + flecks, 0.03, 3.0)

    height = np.clip(0.55 + (crazing - 0.5) * 0.34 + (bloom - 0.5) * 0.12
                     - dead * 0.10, 0.0, 1.0).astype(np.float32)

    # Faintly warm, still nearly neutral. The hue is authored in MonsterSkin and a
    # strong tint here would multiply into it; 5% of warmth only keeps the glass from
    # reading as an LED when the beam itself lands on it.
    base = np.array([0.330, 0.315, 0.285], dtype=np.float32)
    colour = base.reshape(1, 1, 3) * level[..., None]

    # Sooted patches are dry; live glass stays wet.
    roughness = (0.14 + 0.10 * crazing + 0.42 * dead).astype(np.float32)

    return dict(albedo=colour, roughness=roughness, height=height,
                metallic=np.zeros((res, res), dtype=np.float32),
                world_size=0.06, height_scale=0.0016)


SKINS = (
    (FLESH, build_flesh, 0.170,
     "Hide. 0.05 under the darkest wall in a §12 corridor (Wall_Brick_Painted, 0.22) "
     "and 0.17 under the brightest (Wall_Plaster_Stained, 0.34). At 0.240 the shot "
     "review measured it rendering 0.0964 BRIGHTER than the corridor, which made it a "
     "pale object announcing itself instead of something §06 lets you notice too "
     "late. Contrast is bought with range and specular now, not with the mean."),
    (CARAPACE, build_carapace, 0.152,
     "Plates. At gen_textures.py's floor — the darkest surface in the game. Their job "
     "is specular, not albedo: roughness 0.21 and metallic 0.28 under §03's hard spot "
     "give a moving highlight along an edge you cannot otherwise see."),
    (MAW, build_maw, 0.155,
     "Mandible interior. A dark hole until MonsterAcquireTell lights it, and the "
     "emission map is this albedo so the glow takes the shape of the folds."),
    (EYES, build_eyes, 0.152,
     "The two lenses. At the pipeline's albedo floor because they are unlit black "
     "glass; everything anybody ever sees of them is MonsterSkin.EyeGlow multiplied "
     "through this map. §04's 관측자 reads the creature's facing off the pair, which "
     "is the only thing on it that carries that information at ObserverRange."),
)


def write_skin(name: str, build, target_albedo: float, note: str, res: int,
               limits: dict) -> dict:
    """Builds, calibrates, writes and measures one of the creature's three surfaces.

    Calibration is gen_textures.finish_albedo's, restated rather than imported:
    scale the linear albedo onto its intended mean, and fail if the paint was so far
    off that the gain is doing the authoring.
    """
    maps = build(res)
    colour = np.clip(maps["albedo"], 1e-4, None)
    gain = target_albedo / float(colour.mean())
    if not (1.0 / 2.6) <= gain <= 2.6:
        blendkit.fail(f"{name}: albedo needs a {gain:.2f}x correction to reach {target_albedo:.3f} — "
                      "the painted values are wrong, fix them rather than widening the gain.")
    if not limits["ALBEDO_MIN_LINEAR"] <= target_albedo <= limits["ALBEDO_MAX_LINEAR"]:
        blendkit.fail(f"{name}: target albedo {target_albedo:.3f} is outside gen_textures.py's "
                      f"[{limits['ALBEDO_MIN_LINEAR']}, {limits['ALBEDO_MAX_LINEAR']}] band. §03 needs the "
                      "beam to land on something that reflects it.")

    # The corridor ceiling. Calibrating the MEAN under the walls is not enough and the
    # shot review proved it: the hide's mean sat at 0.170 while its top 5% of texels
    # ran to 0.35 linear — brighter than Wall_Plaster_Stained — and those texels were
    # exactly the pale patches that still read as a glowing object at 3 m. A mean is
    # not what an eye finds in a dark frame; the brightest thing in it is.
    #
    # So the bright tail is rolled off under the darkest wall or ceiling in the room
    # and the whole map re-scaled to land back on its intended mean. Iterated, because
    # the roll-off moves the mean it was scaled to. Two passes used to be enough; the
    # eye map is now deliberately heavy-tailed (a third of it is dead soot, part of it
    # rides the ceiling) and each clip claws back what the rescale added, so this
    # loops until the mean settles — six passes bounds it for any sane paint.
    ceiling = limits["CORRIDOR_DARKEST"]
    albedo = np.clip(colour * gain, 0.012, 0.90).astype(np.float32)
    for _ in range(6):
        albedo = _soft_ceiling(albedo, ceiling)
        albedo = np.clip(albedo * (target_albedo / max(float(albedo.mean()), 1e-6)),
                         0.012, ceiling).astype(np.float32)
        if abs(float(albedo.mean()) - target_albedo) < 2e-4:
            break

    roughness = np.clip(maps["roughness"], limits["MIN_ROUGHNESS"], 1.0).astype(np.float32)
    metallic = np.clip(maps["metallic"], 0.0, 1.0).astype(np.float32)
    height = maps["height"]

    normal = _height_to_normal(height, maps["world_size"], maps["height_scale"])
    occlusion = _ambient_occlusion(height, maps["world_size"], maps["height_scale"])

    folder = os.path.join(TEXTURE_DIR, name)
    written = 0
    written += _write_png(os.path.join(folder, name + "_albedo.png"),
                          _to_u8(_linear_to_srgb(albedo)))
    written += _write_png(os.path.join(folder, name + "_normal.png"),
                          _to_u8(normal * 0.5 + 0.5))
    written += _write_png(os.path.join(folder, name + "_rough.png"),
                          _to_u8(np.repeat(roughness[..., None], 3, axis=2)))
    written += _write_png(os.path.join(folder, name + "_ao.png"),
                          _to_u8(np.repeat(occlusion[..., None], 3, axis=2)))

    mask = np.zeros((res, res, 4), dtype=np.float32)
    mask[..., 0] = metallic
    mask[..., 1] = metallic
    mask[..., 2] = metallic
    mask[..., 3] = 1.0 - roughness
    written += _write_png(os.path.join(folder, name + "_ms.png"), _to_u8(mask))

    # Round-trip the albedo through the 8-bit sRGB PNG the shader will actually
    # read, and measure THAT. Asserting the float mean would pass a texture whose
    # quantised mean has drifted out of the band, which is the only mean that exists
    # by the time the beam hits it.
    written_linear = _srgb_to_linear(_to_u8(_linear_to_srgb(albedo)) / 255.0)
    quantised = float(np.mean(written_linear))
    if not limits["ALBEDO_MIN_LINEAR"] <= quantised <= limits["ALBEDO_MAX_LINEAR"]:
        blendkit.fail(f"{name}: albedo means {quantised:.4f} linear after 8-bit encoding, outside "
                      f"[{limits['ALBEDO_MIN_LINEAR']}, {limits['ALBEDO_MAX_LINEAR']}].")

    # The brief's actual requirement, asserted on the bytes the shader will sample: NO
    # texel of the creature may be brighter than the darkest surface of the corridor
    # it is standing in. §06 makes it something you notice too late, and a single
    # patch above the wall behind it is enough to notice early.
    brightest = float(np.max(written_linear @ np.array([0.2126, 0.7152, 0.0722],
                                                       dtype=np.float32)))
    if brightest > ceiling + 0.005:
        blendkit.fail(f"{name}: its brightest texel is {brightest:.4f} linear against a corridor "
                      f"whose darkest wall is {ceiling:.4f}. The creature would out-reflect the "
                      "room and read as a pale object rather than resolving out of the dark.")

    relief_mm = float(height.max() - height.min()) * maps["height_scale"] * 1000.0
    return dict(name=name, note=note, bytes=written,
                albedo_max_linear=round(brightest, 4),
                corridor_darkest_linear=round(ceiling, 4),
                world_size_metres=maps["world_size"],
                albedo_mean_linear=round(quantised, 4),
                albedo_gain=round(gain, 3),
                roughness_mean=round(float(roughness.mean()), 4),
                metallic_mean=round(float(metallic.mean()), 4),
                relief_mm=round(relief_mm, 2),
                normal_unit_error=round(float(np.abs(np.linalg.norm(normal, axis=-1) - 1.0).max()), 5),
                ao_mean=round(float(occlusion.mean()), 4),
                maps={
                    "albedo": name + "/" + name + "_albedo.png",
                    "normal": name + "/" + name + "_normal.png",
                    "roughness": name + "/" + name + "_rough.png",
                    "occlusion": name + "/" + name + "_ao.png",
                    "metallic_smoothness": name + "/" + name + "_ms.png",
                })


def _srgb_to_linear(x: np.ndarray) -> np.ndarray:
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


def uv_units_per_metre(body: bpy.types.Object) -> float:
    """UV units the unwrap spans per metre of the creature's surface.

    Smart-projecting into the 0–1 square makes the UV scale depend on how the
    islands happened to pack, so the tiling that puts one texture tile on the
    intended `world_size` metres cannot be a guessed constant. Measured as
    √(ΣUV area / Σworld area) over every triangle, written into the manifest, and
    turned into a texture scale on the Unity side.
    """
    mesh = body.data
    uv_layer = mesh.uv_layers.active
    if uv_layer is None:
        blendkit.fail("the joined body has no UV layer — the PBR maps would sample garbage.")

    world_area = 0.0
    uv_area = 0.0
    for poly in mesh.polygons:
        loops = list(poly.loop_indices)
        verts = [mesh.vertices[mesh.loops[i].vertex_index].co for i in loops]
        uvs = [uv_layer.data[i].uv for i in loops]
        for i in range(1, len(loops) - 1):
            a, b, c = verts[0], verts[i], verts[i + 1]
            world_area += (b - a).cross(c - a).length * 0.5
            ua, ub, uc = uvs[0], uvs[i], uvs[i + 1]
            uv_area += abs((ub - ua).x * (uc - ua).y - (ub - ua).y * (uc - ua).x) * 0.5

    if world_area <= 1e-9 or uv_area <= 1e-12:
        blendkit.fail(f"degenerate unwrap: world area {world_area:.6f} m², uv area {uv_area:.6f}.")
    return math.sqrt(uv_area / world_area)


def write_skin_manifest(reports: list, uv_per_metre: float, res: int) -> None:
    """One copy of "which map goes on which slot, at what tiling", written here.

    The same argument gen_textures.write_manifest makes: two copies drift the first
    time a material is renamed, and the failure mode is a silently untextured
    monster in a game where the monster is the only thing worth looking at.
    """
    payload = {
        "generated_by": "tools/blender/gen_monster_model.py",
        "resolution": res,
        "uv_units_per_metre": round(uv_per_metre, 6),
        "fbx": "Assets/Models/Characters/Monster.fbx",
        "materials": reports,
    }
    os.makedirs(os.path.dirname(SKIN_MANIFEST), exist_ok=True)
    with open(SKIN_MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


# ── Dressing the adopted sculpt ─────────────────────────────────────────────
#
# Everything below is imported by gen_monster_ai.py, which owns the shipped
# Monster.fbx. It lives here for the same reason the clip authors do: this module is
# the library of monster-specific art decisions, and gen_monster_ai is the pipeline
# that applies them to whatever sculpt has been adopted.
#
# WHY THE SCULPT NEEDS DRESSING AT ALL — the 2026-08 shot review, three findings:
#
# 1. Rodin's graded diffuse is one material: a single grey with baked turntable
#    shading and pink blush stains at the groin and knuckles. Under §03's beam it
#    reads as moulded plastic, because optically that is what a uniform-albedo
#    uniform-roughness surface is.
# 2. The pelvis is a smooth closed panel. At 3 m it reads as a nappy; in silhouette
#    the thigh gap above two thin legs reads as a naked person, not a creature.
# 3. The body is mirror-symmetric to the millimetre, and symmetry is the single
#    strongest "manufactured object" cue a shape can carry.


def cavity_from_normal(normal_rgb: np.ndarray, res: int, blur_px: float = 2.5) -> np.ndarray:
    """Concavity, measured off the sculpt's own baked tangent-space normal map.

    Rodin bakes no occlusion (the ORM channel arrives constant), but the normal map
    knows where the hollows are: across a crease the normals *converge*, so the
    negative divergence of the tangent-space XY is a cavity signal — the rib hollows,
    the throat pleats, the pit of the pelvis all light up. That is exactly the mask
    the wet-crevice roughness and the craquelure need, and deriving it beats painting
    it because it follows any future re-sculpt with no coordinates typed here.
    """
    nx = normal_rgb[..., 0] * 2.0 - 1.0
    ny = normal_rgb[..., 1] * 2.0 - 1.0
    div = ((np.roll(nx, -1, axis=1) - np.roll(nx, 1, axis=1)) * 0.5
           + (np.roll(ny, -1, axis=0) - np.roll(ny, 1, axis=0)) * 0.5)
    cav = _blur(np.clip(-div, 0.0, None), blur_px * res / 1024.0)
    lo, hi = np.percentile(cav, (2.0, 99.0))
    if hi - lo < 1e-9:
        return np.zeros_like(cav, dtype=np.float32)
    return np.clip((cav - lo) / (hi - lo), 0.0, 1.0).astype(np.float32)


def _slopes(normal_xyz: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    z = np.maximum(normal_xyz[..., 2], 0.35)
    return normal_xyz[..., 0] / z, normal_xyz[..., 1] / z


# ── Micro-surface ───────────────────────────────────────────────────────────

# Amplitudes for the grain octave, tuned against measured map statistics rather than
# by eye — see `micro_surface`. Height is metres of relief across the full 0-1 field;
# the rest are fractions of the channel they modulate.
#
# They are this large for one measured reason: **the maps ship as 8-bit PNGs, and
# 8-bit quantisation is what flattens a normal map.** Writing the identical float
# array and reading the file back:
#
#     in memory (float32)          flat<2°  0.1345   tilt_mean  4.76°
#     after PNG round trip         flat<2°  0.5962   tilt_mean  3.17°
#     8-bit quantised in memory    flat<2°  0.5961   tilt_mean  3.17°
#
# — the file and the bare quantisation agree to four decimal places, so the loss is
# entirely the byte buffer. One code of 255 is 0.45° of tilt, and a micro-relief
# gentle enough to look right as floats lands under half a code and is stored as
# exactly flat. Anything that tunes this by looking at the array instead of the PNG
# will under-shoot by ~5x; an earlier pass here reported 0.058 flat while the file on
# disk held 0.297. Raising `write_png` to 16-bit would let these come down, and that
# is `gen_monster_ai.py`'s call, not this file's.
MICRO_HEIGHT_M = 0.0045
MICRO_ALBEDO = 0.20
MICRO_ROUGH = 0.16
MICRO_AO = 0.12


def micro_surface(res: int, world_size: float, cav: np.ndarray,
                  armw: np.ndarray | None = None, handw: np.ndarray | None = None,
                  finreg: np.ndarray | None = None) -> dict:
    """The grain octave the hide was missing: skin at texel scale, not sculpt scale.

    **Why this exists (2026-08-09).** Every other surface in the frame had just been
    rebuilt on CC0 photo scans, and the creature had not, so the maps were measured
    side by side with the same band analysis. The hide was the outlier on every axis:

    ======================  ==========  ================  ==========  =============
    map set                 mm/texel    normal flat <2°   tilt mean   albedo 1px sd
    ======================  ==========  ================  ==========  =============
    Wall_Plaster_Stained          0.98            0.241       9.48°         0.01899
    Wall_Concrete_Bare            1.22            0.190      11.45°         0.01750
    Player_Skin                   0.27            0.077      14.61°         0.00461
    **Monster_Hide**              1.74        **0.485**    **6.42°**    **0.00524**
    ======================  ==========  ================  ==========  =============

    Read that as: **half the creature's normal map was dead flat**, twice the flat
    fraction of the worst wall it stands against, at the lowest mean tilt of anything
    in the frame, and spread over the coarsest texels. Everything on it was relief the
    *sculpt* carries — ribs, tendons, the pelvic pleats — which is why it photographs
    well at 12 m and under a raking beam at 3 m, and photographs as a wax cast at
    arm's length, where §06's grab happens. There was no octave between "1.7 mm texel"
    and "a rib".

    (The albedo column is the standard deviation of the finest spatial band, a 1-texel
    Gaussian difference. It is quoted rather than a fine/coarse ratio because the ratio
    flatters a map whose coarse band is weak; the walls carry 3.3-3.6x this hide's
    finest-band energy outright. The runner's skin reads low here only because its
    atlas is 0.27 mm/texel, so its finest band is a quarter the physical size.)

    It is also spread thinner than anything else: one 2048 atlas over 3.56 m of body,
    1.74 mm per texel against the walls' ~1 mm and the runner's 0.27 mm. Raising
    `TEX_RES` is the other half of the fix and is not this file's to make, so the
    amplitudes below are chosen to put as much energy as possible into the 2-4 texel
    band — the finest this atlas can carry without aliasing under a mip.

    What it paints:

    * **Papillary grain.** Worley cells about three texels across, edges *and* cell
      interiors, so the surface reads pebbled rather than veined. This is the octave
      a beam at 0.6 m actually resolves.
    * **Pores**, sparser and deeper, gated away from the cavity mask — pores read on
      the stretched convex skin, and the creases already have craquelure doing that
      job. Without the gate the crevices double-darken and go to mud.
    * **Stretch.** On the arms the grain is drawn out along the limb (`stretch=3.2`),
      because skin under tension grains along the tension and isotropic pebbling on a
      1.78 m arm reads as gravel.
    * **Carapace scratch** on the crest fan: a much finer, harder, directional field
      instead of pebbling, so the blades read as keratin beside flesh rather than as
      the same material bent into a different shape.

    Everything returned is zero-mean-ish and *relative*; the caller's grade still owns
    the mean, the corridor ceiling and the roughness target, so this cannot move the
    creature outside §03's albedo band no matter how it is tuned.
    """
    zero = np.zeros((res, res), dtype=np.float32)
    armw = zero if armw is None else armw
    handw = zero if handw is None else handw
    finreg = zero if finreg is None else finreg

    # ~3 texels per cell at any resolution, so the grain is the same physical size
    # whatever TEX_RES becomes.
    cells = max(24, int(round(res / 3.0)))
    f1, f2, owner = _worley(cells=cells, res=res, seed=7501, jitter=0.85)
    interior = _smoothstep(0.0, 1.4 / cells, f2 - f1)          # 0 on the cell seam
    dome = 1.0 - _smoothstep(0.0, 0.85 / cells, f1)            # a bump per cell
    tint = _per_cell(owner, cells, 7502, 0.42, 1.0)
    pebble = (0.55 * interior + 0.45 * dome * tint).astype(np.float32)

    # An isotropic fine field mixed in stops the Worley lattice reading as a lattice.
    speck = _fbm(res, 7503, beta=1.35, low_cycles=res / 6.0)
    grain = (0.70 * pebble + 0.30 * speck).astype(np.float32)

    # Along-limb stretch, blended in only where the arm mask says so.
    if float(armw.max()) > 0.0:
        drawn = _fbm(res, 7504, beta=1.4, low_cycles=res / 7.0, stretch=3.2)
        w = _smoothstep(0.25, 0.85, armw)[..., None][..., 0]
        grain = (grain * (1.0 - w) + (0.45 * grain + 0.55 * drawn) * w).astype(np.float32)

    # Pores: sparse, deep, off the creases. Only a fraction of the cells get one, so
    # they scatter instead of tiling — a regular pore lattice is worse than none.
    pcells = max(12, int(round(res / 9.0)))
    pf1, _pf2, powner = _worley(cells=pcells, res=res, seed=7505, jitter=1.0)
    keep = _per_cell(powner, pcells, 7506, 0.0, 1.0) > 0.55
    pore = ((1.0 - _smoothstep(0.0, 0.38 / pcells, pf1))
            * keep.astype(np.float32)
            * (1.0 - _smoothstep(0.25, 0.70, cav))).astype(np.float32)

    # Hands work: the grain coarsens and hardens where it grips.
    grain = np.clip(grain + _smoothstep(0.30, 0.90, handw) * (pebble - 0.5) * 0.55,
                    0.0, 1.0).astype(np.float32)

    # Amplitude varies on a slow field, ~20-40 cm across. Without it the grain is the
    # same everywhere and reads as a stucco overlay rather than as a body: real hide
    # is coarse over the shoulders and stretched fine over the ribs, and a constant
    # amplitude is the tell that a procedural layer was dropped on top of a scan.
    swell = 0.55 + 0.90 * _fbm(res, 7508, beta=2.2, low_cycles=world_size / 0.30)
    height = ((grain - float(grain.mean())) * swell - pore * 0.55).astype(np.float32)

    # The crest is keratin, not hide: swap pebbling for a fine hard scratch.
    if float(finreg.max()) > 0.0:
        scratch = _fbm(res, 7507, beta=1.25, low_cycles=res / 4.5, stretch=5.0)
        w = _smoothstep(0.20, 0.80, finreg)
        height = (height * (1.0 - w) + (scratch - float(scratch.mean())) * 0.75 * w
                  ).astype(np.float32)
        grain = (grain * (1.0 - w) + scratch * w).astype(np.float32)

    centred = (grain - float(grain.mean())).astype(np.float32)
    return {
        "height": height,
        "normal": _height_to_normal(height * 0.5 + 0.5, world_size, MICRO_HEIGHT_M),
        "albedo": np.clip(1.0 + centred * MICRO_ALBEDO - pore * 0.22, 0.0, 2.0
                          ).astype(np.float32),
        "rough": (centred * MICRO_ROUGH + pore * 0.06).astype(np.float32),
        "ao": np.clip(1.0 - np.clip(-centred, 0.0, None) * MICRO_AO - pore * 0.14,
                      0.0, 1.0).astype(np.float32),
        "stats": {
            "micro_cells_per_texel": round(3.0, 3),
            "micro_height_mm": round(MICRO_HEIGHT_M * 1000.0, 3),
            "micro_pore_cover": round(float((pore > 0.35).mean()), 4),
        },
    }


def dress_sculpt_maps(diffuse: np.ndarray, normal: np.ndarray | None,
                      rough: np.ndarray | None, res: int,
                      world_size: float, fields: dict | None = None,
                      landmarks: dict | None = None) -> dict:
    """Turns Rodin's turntable maps into dead flesh. Shape only — no albedo policy.

    ``fields``/``landmarks`` (2026-08) make the dressing anatomical: with a per-texel
    world position and arm/hand masks from ``rasterize_fields``, the craquelure keeps
    going down the forearms instead of stopping at the biceps, split-skin lines run
    over the tendons, grime builds toward the claws, the maw channel goes dark and
    wet ONLY inside its own torn margins, and the crest blades get cartilage-rib
    versus membrane shading. Without them (the hull monster path) everything below
    still works exactly as before.

    The caller still runs its mean/ceiling grade afterwards, so everything here is
    *relative*: mottle, craquelure and staining move texels against each other and
    the grade then lands the whole field back on §03's numbers. That split is what
    lets this function paint freely without being able to break the corridor policy.

    What it paints, and why each piece is there:

    * **Desaturation first.** The sculpt's pink blush patches (groin, knuckles) read
      as skin-care, not horror. Chroma is halved before anything else so the marbling
      painted below is the dominant colour event.
    * **Cadaveric mottle** — two scales of fbm multiplied in, ±30%, with a slow
      hue drift between a cold bruised grey and a pallid warm grey. This is the
      "dead flesh" read: livor mortis is patchy, and patchiness at 10–30 cm scale is
      what a beam crossing the torso picks out.
    * **Craquelure, gated by cavity.** A fine Worley edge net, warped so it meanders,
      weighted 3× inside the cavity mask — the dark cracking concentrates around the
      ribs and pleats exactly where drying skin actually splits, and the flat outer
      shells stay comparatively clean.
    * **Roughness rebuilt: dry shells, wet crevices.** The base is pulled to a
      bone-dry 0.88 matte (keeping a quarter of Rodin's variation), then the cavity
      and crack masks cut it down toward wet. Under a 12 m beam this is the whole
      material read: the hotspot dies on the dry hide and *glints out of the seams*,
      which is the difference between plastic and something that seeps.
    * **Real AO** from the same masks, because Unity's _OcclusionMap is sampled by
      the rim light §3.14 added, and a rim over creviced skin without AO flattens it
      back into a paper cut-out.
    * **Crack relief folded into the normal map**, so the seams are not just dark
      paint but catch the beam's grazing angle.
    """
    lin = _srgb_to_linear(np.clip(diffuse[..., :3], 0.0, 1.0)).astype(np.float32)
    lum = (lin @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32))[..., None]
    lin = lin * 0.48 + lum * 0.52

    cav = (cavity_from_normal(normal[..., :3], res) if normal is not None
           else np.zeros((res, res), dtype=np.float32))

    # The anatomical fields, unpacked early because the craquelure gate below wants
    # the arm mask. All zeros when the caller has none — every gate degrades to the
    # pre-2026-08 behaviour.
    armw = np.zeros((res, res), dtype=np.float32)
    handw = np.zeros((res, res), dtype=np.float32)
    wx = wy = wz = None
    if fields is not None and landmarks is not None and "pos" in fields:
        coverf = fields["cover"].astype(np.float32)
        armw = (fields["arm"] * coverf).astype(np.float32)
        handw = (fields["hand"] * coverf).astype(np.float32)
        wx = fields["pos"][..., 0]
        wy = fields["pos"][..., 1]
        wz = fields["pos"][..., 2]

    mottle = _fbm(res, 7301, beta=2.0, low_cycles=5.0)
    blotch = _fbm(res, 7302, beta=2.3, low_cycles=2.4)
    tone = (0.76 + 0.48 * mottle) * (0.86 + 0.28 * blotch)
    lin = lin * tone[..., None]

    cold = np.array([0.94, 1.00, 1.07], dtype=np.float32).reshape(1, 1, 3)
    pale = np.array([1.06, 1.00, 0.93], dtype=np.float32).reshape(1, 1, 3)
    w = _smoothstep(0.35, 0.75, blotch)[..., None]
    lin = lin * (cold * (1.0 - w) + pale * w)

    warp_y = (_fbm(res, 7303, beta=2.2, low_cycles=3.0) - 0.5) * 0.05
    warp_x = (_fbm(res, 7304, beta=2.2, low_cycles=3.0) - 0.5) * 0.05
    fine_f1, fine_f2, _ = _worley(res, 96, 7305, jitter=1.0)
    coarse_f1, coarse_f2, _ = _worley(res, 34, 7306, jitter=1.0)
    fine_edge = _warp(fine_f2 - fine_f1, warp_y, warp_x)
    coarse_edge = _warp(coarse_f2 - coarse_f1, warp_y, warp_x)
    # The fine-crack gate takes whichever is stronger: concavity (drying skin splits
    # in its folds) or arm-ness (2026-08 — the review's F: torso craquelure stopped
    # at the biceps and the forearms photographed as moulded plastic beside it).
    fine_gate = np.maximum(0.30 + 0.70 * _smoothstep(0.12, 0.55, cav),
                           0.25 + 0.75 * _smoothstep(0.20, 0.75, armw))
    coarse_gate = 0.45 + 0.30 * _smoothstep(0.30, 0.90, armw)
    crack = np.clip(
        _smoothstep(0.014, 0.0, fine_edge) * fine_gate
        + _smoothstep(0.010, 0.0, coarse_edge) * coarse_gate, 0.0, 1.0).astype(np.float32)

    lin = lin * (1.0 - crack[..., None] * 0.66)

    ao = np.clip(1.0 - 0.52 * cav - 0.38 * crack, 0.30, 1.0).astype(np.float32)

    base_rough = rough if rough is not None else np.full((res, res), 0.60, dtype=np.float32)
    dry = 0.88 + (base_rough - float(base_rough.mean())) * 0.25
    wet = np.clip(cav * 1.15 + crack * 0.85, 0.0, 1.0)
    rough_out = np.clip(dry - wet * 0.55, 0.10, 1.0).astype(np.float32)

    # ── 2026-08: the arms, the claws, the channel, the fins — 3D-gated paint ──
    extra_relief = np.zeros((res, res), dtype=np.float32)
    fin_mask = np.zeros((res, res), dtype=np.float32)
    plate_found = 0.0
    if wz is not None:
        elbow, tipz = landmarks["elbow"], landmarks["fingertip"]
        along = np.clip((elbow + 0.05 - wz) / max(elbow - tipz, 1e-4),
                        0.0, 1.0).astype(np.float32)

        # Split-skin lines over the tendons: thin dark stripes running down the
        # forearm, meandering on a noise phase, gone before the claw tips (the
        # tips get the grime instead).
        meander = _fbm(res, 7411, beta=2.0, low_cycles=3.0)
        stripe = (0.5 + 0.5 * np.sin(wx * 235.0 + (meander - 0.5) * 6.5
                                     + wz * 9.0)).astype(np.float32)
        tendon = (_smoothstep(0.86, 0.99, stripe) * armw
                  * _smoothstep(0.06, 0.30, along)
                  * (1.0 - _smoothstep(0.80, 1.0, along))).astype(np.float32)
        lin = lin * (1.0 - tendon[..., None] * 0.50)
        rough_out = np.clip(rough_out - tendon * 0.26, 0.10, 1.0)
        extra_relief += tendon * 0.55

        # Knuckle wear: pale dry abrasion where the hands are convex — it rakes
        # walls, and the knuckles are what touch first.
        if normal is not None:
            nx2 = normal[..., 0] * 2.0 - 1.0
            ny2 = normal[..., 1] * 2.0 - 1.0
            div = ((np.roll(nx2, -1, axis=1) - np.roll(nx2, 1, axis=1)) * 0.5
                   + (np.roll(ny2, -1, axis=0) - np.roll(ny2, 1, axis=0)) * 0.5)
            convex = _blur(np.clip(div, 0.0, None), 2.5 * res / 1024.0)
            c_lo, c_hi = np.percentile(convex, (2.0, 99.0))
            convex = np.clip((convex - c_lo) / max(c_hi - c_lo, 1e-9),
                             0.0, 1.0).astype(np.float32)
            wear = (handw * _smoothstep(0.55, 0.90, convex)).astype(np.float32)
            bone = np.array([0.305, 0.285, 0.252], dtype=np.float32).reshape(1, 1, 3)
            lin = lin * (1.0 - wear[..., None] * 0.40) + bone * wear[..., None] * 0.40
            rough_out = np.clip(rough_out - wear * 0.20, 0.10, 1.0)

        # Grime gradient toward the claws.
        dirt = (np.maximum(armw * _smoothstep(0.20, 0.95, along), handw * 0.9)
                * (0.62 + 0.38 * mottle)).astype(np.float32)
        grime = np.array([0.72, 0.66, 0.58], dtype=np.float32).reshape(1, 1, 3)
        lin = lin * (1.0 - dirt[..., None] * 0.45) + lin * grime * dirt[..., None] * 0.45
        rough_out = np.clip(rough_out + dirt * 0.08, 0.10, 1.0)
        ao = np.clip(ao - dirt * 0.08, 0.25, 1.0)

        # The maw channel: pooling dark and wet gloss ONLY inside the torn slot.
        # The interior test is depth behind the local front shell, measured off the
        # position map itself, so it follows tear_maw_channel's displaced geometry.
        neck, shoulder, crotch = landmarks["neck"], landmarks["shoulder"], landmarks["crotch"]
        topz = landmarks["top"]
        ch_hi = neck + (topz - neck) * 0.10
        ch_lo = shoulder - (shoulder - crotch) * 0.55
        band = ((np.abs(wx) < 0.062) & (wz > ch_lo) & (wz < ch_hi)
                & (wy < 0.05) & fields["cover"])
        wideband = ((np.abs(wx) < 0.10) & (wz > ch_lo) & (wz < ch_hi)
                    & (wy < 0.05) & fields["cover"])
        if band.any() and wideband.any():
            nb = 36
            zb = np.clip(((wz - ch_lo) / max(ch_hi - ch_lo, 1e-6) * nb).astype(np.int32),
                         0, nb - 1)
            front_ref = np.full(nb, np.nan, dtype=np.float32)
            for b in range(nb):
                sel = wideband & (zb == b)
                if sel.any():
                    front_ref[b] = np.percentile(wy[sel], 3.0)
            finite = front_ref[np.isfinite(front_ref)]
            if finite.size:
                fill = finite[0]
                for b in range(nb):
                    if np.isfinite(front_ref[b]):
                        fill = front_ref[b]
                    else:
                        front_ref[b] = fill
                depth = np.where(band, wy - front_ref[zb], 0.0).astype(np.float32)
                # The lateral gate is load-bearing: depth alone also fires on the
                # chest as it curves back beside the slot, and round 3 rendered
                # that as a straight-edged glossy column to the right of the
                # channel. Wet is only wet within the slot's own torn half-width.
                lateral = _smoothstep(0.042, 0.022, np.abs(wx).astype(np.float32))
                inside = (_smoothstep(0.006, 0.020, depth) * lateral
                          * band.astype(np.float32)).astype(np.float32)
                pool = (_smoothstep(0.015, 0.038, depth) * lateral
                        * band.astype(np.float32)).astype(np.float32)
                lin = lin * (1.0 - inside[..., None] * 0.48 - pool[..., None] * 0.22)
                gloss = np.clip(0.30 - 0.10 * pool, 0.10, 1.0)
                rough_out = np.where(inside > 0.3,
                                     np.minimum(rough_out, gloss), rough_out)
                ao = np.clip(ao - inside * 0.22, 0.22, 1.0)

        # The crest blades: cartilage ribs against waxy membrane. Only geometry
        # BEHIND the torso's own back can be a blade, which is exactly the fan.
        finreg = ((wy > landmarks["back_y"] + 0.012)
                  & fields["cover"]).astype(np.float32)
        fin_mask = finreg
        if finreg.any():
            rib = (0.5 + 0.5 * np.sin(wz * 240.0 + wy * 60.0)).astype(np.float32)
            rib = (_smoothstep(0.72, 0.98, rib) * finreg).astype(np.float32)
            memb = finreg * (1.0 - rib)
            lin = lin * (1.0 - rib[..., None] * 0.22)
            rough_out = np.clip(rough_out - memb * 0.18 + rib * 0.06, 0.10, 1.0)
            extra_relief += rib * 0.40

        # ── The skull and crown, as keratin plate ────────────────────────────
        # §06's head is "two eyeless blade halves separated by a vertical slot" under
        # "a crown that is one mass" — it is carapace, not skin, and it was the one
        # region carrying no surface at all. Renders at 0.65 m photographed the crest
        # and the crown as flat grey cardboard with visible polygon edges beside a
        # torso that had just gained a hide, which made the head look like a separate,
        # cheaper asset welded on.
        #
        # Polygons cannot fix it here: the head already took +4158 faces from
        # `refine_head` and the mesh sits at 30,306 of a 32,000 budget. What a flat
        # plate needs is relief an order coarser than skin grain — a 5 mm pebble on a
        # plane facing the beam shades almost identically to no pebble at all, which
        # is exactly what the round-2 A/B showed. So this paints growth banding and
        # pitting at 10-30 mm into `extra_relief`, which is carried at the 10 mm
        # height scale the crack normal already uses.
        #
        # The mask is two things unioned. Height above the neck catches the skull and
        # the crown. The second half catches the rest, and is found rather than typed:
        # every plate this pipeline *invents* — the crest fan, the scapular spur, the
        # shoulder shelves, the teeth — is welded on as flat polygons and then planar-
        # unwrapped, so its baked normal is locally CONSTANT, where a scanned surface
        # is never quite constant anywhere. Thresholding the local variance of the
        # sculpt's own normal map therefore separates "geometry a script added" from
        # "geometry Rodin scanned", with no coordinates in this file to go stale the
        # next time `add_crest` moves a blade.
        plate = (_smoothstep(neck + 0.02, neck + 0.11, wz)
                 * fields["cover"]).astype(np.float32)
        if normal is not None:
            n3 = normal[..., :3].astype(np.float32) * 2.0 - 1.0
            sig = 3.0 * res / 1024.0
            var = np.zeros((res, res), dtype=np.float32)
            for c in range(3):
                var += _blur(n3[..., c] ** 2, sig) - _blur(n3[..., c], sig) ** 2
            flatpanel = (_smoothstep(2.0e-4, 1.0e-5, np.clip(var, 0.0, None))
                         * fields["cover"]).astype(np.float32)
            plate = np.maximum(plate, flatpanel)
            plate_found = float((flatpanel > 0.5).mean())
        else:
            plate_found = 0.0
        if plate.any():
            # Growth bands: concentric, slightly wandering, hard-edged. Keyed to
            # height above the neck so they ring the skull rather than stripe it.
            phase = (wz - neck) * 46.0 + (_fbm(res, 7411, beta=2.4, low_cycles=2.0)
                                          - 0.5) * 2.2
            band = 1.0 - np.abs(2.0 * (phase - np.floor(phase)) - 1.0)
            band = _smoothstep(0.30, 0.92, band.astype(np.float32))

            # Pitting: shallow craters, sparse, the wear a plate takes rather than
            # the splitting skin takes. Deliberately not the craquelure — a plate
            # that cracks like drying flesh stops reading as a different material.
            pcells = max(16, int(round(res / 26.0)))
            pf1, _pf2, pown = _worley(cells=pcells, res=res, seed=7412, jitter=1.0)
            pit = ((1.0 - _smoothstep(0.0, 0.62 / pcells, pf1))
                   * (_per_cell(pown, pcells, 7413, 0.0, 1.0) > 0.62)).astype(np.float32)

            relief = (band * 0.55 + pit * 0.85) * plate
            extra_relief += relief
            lin = lin * (1.0 - (pit * plate)[..., None] * 0.30
                         - (band * plate)[..., None] * 0.10)
            # Keratin is smoother than hide and drier than the crevices, and the
            # pits hold what little damp there is.
            rough_out = np.clip(rough_out - plate * 0.10 + pit * plate * 0.14,
                                0.10, 1.0)
            ao = np.clip(ao - (pit * plate) * 0.30 - (band * plate) * 0.08, 0.20, 1.0)
            fin_mask = np.maximum(fin_mask, plate)

    # ── The grain octave, last, so every mask above can gate it ──────────────
    # Applied to albedo/roughness/AO here and folded into the normal below. It is the
    # only thing on this surface finer than the sculpt, and without it the creature
    # renders as a wax cast at the one distance §06 cares most about — see
    # `micro_surface` for the measurement that says so.
    micro = micro_surface(res, world_size, cav, armw=armw, handw=handw,
                          finreg=fin_mask)
    lin = lin * micro["albedo"][..., None]
    rough_out = np.clip(rough_out + micro["rough"], 0.10, 1.0).astype(np.float32)
    ao = np.clip(ao * micro["ao"], 0.20, 1.0).astype(np.float32)

    if normal is not None:
        crack_n = _height_to_normal(np.clip(0.5 - crack * 0.5 - cav * 0.10
                                            - extra_relief * 0.30, 0.0, 1.0),
                                    world_size, 0.010)
        sx, sy = _slopes(normal[..., :3] * 2.0 - 1.0)
        cx, cy = _slopes(crack_n)
        mx, my = _slopes(micro["normal"])
        merged = np.stack([sx + cx + mx, sy + cy + my, np.ones_like(sx)], axis=-1)
        merged /= np.linalg.norm(merged, axis=-1, keepdims=True)
        normal_out = np.ones((res, res, 4), dtype=np.float32)
        normal_out[..., :3] = merged * 0.5 + 0.5
        # Measured on the BYTE buffer write_png will store, not on the float array:
        # the two differ by 5x and only one of them is the file Unity samples. Z is
        # read straight out of the quantised byte rather than rebuilt from X/Y, which
        # is the same convention the reference numbers in the note below were taken
        # with — the comparison is only worth anything if both sides are measured the
        # same way.
        stored = np.rint(np.clip(normal_out[..., :3], 0.0, 1.0) * 255.0) / 255.0 * 2.0 - 1.0
        tilt = np.degrees(np.arccos(np.clip(stored[..., 2], -1.0, 1.0)))
    else:
        normal_out = None
        tilt = None

    albedo_out = np.ones((res, res, 4), dtype=np.float32)
    albedo_out[..., :3] = _linear_to_srgb(np.clip(lin, 0.0, 1.0))

    stats = {
        "cavity_mean": round(float(cav.mean()), 4),
        "crack_cover": round(float((crack > 0.25).mean()), 4),
        "dressed": True,
    }
    stats.update(micro["stats"])
    stats["plate_cover"] = round(float((fin_mask > 0.5).mean()), 4)
    stats["plate_found_by_planarity"] = round(plate_found, 4)
    if tilt is not None:
        # The two numbers that caught the 2026-08-09 regression, kept in the manifest
        # so the next person can see at a glance whether the hide still has a surface.
        # Reference points measured the same way: CC0 plaster wall 0.241 / 9.48°, the
        # runner's skin 0.077 / 14.61°, this hide before the grain octave 0.485 / 6.42°.
        stats["normal_flat_frac_under_2deg"] = round(float((tilt < 2.0).mean()), 4)
        stats["normal_tilt_mean_deg"] = round(float(tilt.mean()), 3)
    return {
        "albedo": albedo_out,
        "rough": rough_out,
        "normal": normal_out,
        "ao": ao,
        "stats": stats,
    }


def sculpt_asymmetry(obj: bpy.types.Object, m: dict) -> None:
    """Breaks the sculpt's mirror symmetry, in place, before the rig is fitted.

    Three deliberate injuries, all displacement-only (no topology change, so the
    baked UVs ride along and the maps keep sampling correctly):

    * **The left leg bulks out** ~16% radially below the knee, ~fading to nothing at
      the crotch. One heavy leg and one wasted one is the strongest cheap "wrong"
      cue a biped silhouette can carry, and it survives at 8 m where texture is gone.
    * **The right forefoot drags into a claw** — toes stretched forward and pinched.
      Z is never touched, so the grounding solver and the height pin see the same
      floor contact they always did.
    * **The right temple is staved in** — a 3 cm elliptical dent. The skull stops
      being a bulb exactly where the eye pair used to sit, and the asymmetric
      socket the eye pass builds lands beside a wound instead of on clean bone.
      Verts within 3 cm of the very top are excluded so the 2.336 m height pin
      cannot move.
    """
    me = obj.data
    crotch, knee, ankle = m["crotch"], m["knee"], m["ankle"]
    neck, top = m["neck"], m["top"]
    lx_hip, lx_knee, lx_ank = m["leg_x_hip"], m["leg_x_knee"], m["leg_x_ankle"]

    dent_centre = Vector((-0.085, m["head_y"] - 0.03, neck + (top - neck) * 0.66))

    for v in me.vertices:
        c = v.co

        # ── legs: radial bulk, asymmetric ──
        if ankle + 0.04 < c.z < crotch + 0.04 and abs(c.x) > 0.015:
            side = 1.0 if c.x > 0.0 else -1.0
            if c.z >= knee:
                t = (c.z - knee) / max(crotch - knee, 1e-4)
                axis_x = side * _lerp(lx_knee, lx_hip, t)
            else:
                t = (c.z - ankle) / max(knee - ankle, 1e-4)
                axis_x = side * _lerp(lx_ank, lx_knee, t)
            dx, dy = c.x - axis_x, c.y
            if math.hypot(dx, dy) < 0.30:
                gain = 0.16 if side > 0 else 0.035
                fade = min(1.0, max(0.0, (crotch + 0.04 - c.z) / 0.22))
                below_knee = 1.0 if c.z < knee + 0.08 else 0.72
                s = 1.0 + gain * fade * below_knee
                v.co.x = axis_x + dx * s
                v.co.y = dy * s

        # ── right forefoot: dragged claw ──
        if c.z < ankle * 0.9 and c.x < -0.02 and c.y < 0.02:
            fade = (ankle * 0.9 - c.z) / max(ankle * 0.9, 1e-4)
            axis_x = -lx_ank
            v.co.y = c.y * (1.0 + 0.50 * fade) - 0.030 * fade
            v.co.x = axis_x + (c.x - axis_x) * (1.0 - 0.14 * fade)

        # ── right temple: staved in ──
        if neck + (top - neck) * 0.40 < c.z < top - 0.03 and c.x < 0.0:
            d = Vector((c.x, c.y, c.z)) - dent_centre
            d.x *= 1.7
            fall = max(0.0, 1.0 - d.length / 0.18)
            if fall > 0.0:
                v.co.x += 0.048 * fall * fall

    me.update()


def grow_pelvis_shroud(obj: bpy.types.Object, m: dict) -> list[tuple]:
    """Hangs a ragged hide skirt off the pelvis and welds it on. Bridges the hip gap.

    The sculpt's pelvis is a smooth closed panel with a thigh gap under it; at 3 m it
    reads as underwear and in silhouette as a person. Thirteen-odd tattered strips
    rooted in a band just above the hip pivot fix both: the silhouette becomes one
    continuous mass from ribs to mid-thigh, and what hangs there reads as torn hide.

    Anchored to the sculpt's *measured* radius per angle, the same argument
    add_crest makes: a skirt at a typed radius floats off one sculpt and buries
    itself in another. Strips vary by hashed length/width/skew, one position in ten
    is torn out entirely, and the front strips hang longest — they are what covers
    the pelvis panel from the §06 head-on view.

    Returns ``(start, end, "Hips")`` ranges: the caller weights them rigidly to Hips
    so the skirt swings with the pelvis. Rigid to Hips and not proximity-blended into
    the thighs, deliberately — a tatter that half-follows a femur stretches into a
    membrane between the legs, which is a worse artefact than a stiff tatter.
    """
    me = obj.data
    crotch, shoulder, knee = m["crotch"], m["shoulder"], m["knee"]
    z_root = crotch + (shoulder - crotch) * 0.155

    band = [v.co.copy() for v in me.vertices if abs(v.co.z - z_root) < 0.06]
    if not band:
        return []

    def radius_at(theta: float) -> float:
        dx, dy = math.cos(theta), math.sin(theta)
        best = 0.0
        for c in band:
            along = c.x * dx + c.y * dy
            perp = abs(-c.x * dy + c.y * dx)
            if along > best and perp < 0.09:
                best = along
        return best if best > 0.02 else 0.12

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()

    ranges = []
    count = 15
    for i in range(count):
        if _hash01(9100, i) > 0.88:      # a torn-out gap; the raggedness is the point
            continue
        theta = (i + 0.5) / count * 2.0 * math.pi
        cx, cy = math.cos(theta), math.sin(theta)
        tx, ty = -math.sin(theta), math.cos(theta)

        r0 = radius_at(theta) - 0.006
        length = 0.16 + 0.22 * _hash01(9101, i)   # wider spread than the first pass:
                                                  # equal-length strips read as a fringe
        if cy < -0.3:                    # front (-Y): longest, covers the pelvis panel
            length *= 1.15
        if abs(cx) > 0.72:               # the flanks: the thigh is well inboard of the
            length *= 0.80               # iliac shelf, so a long strip here floats
        tip_z = max(z_root - length, knee + 0.05)
        width = 0.038 + 0.062 * _hash01(9102, i)
        skew = (0.5 - _hash01(9105, i)) * width * 1.2

        start = len(bm.verts)
        # The tip tucks INBOARD, not out — round 2's hula-skirt lesson still holds:
        # hide with weight hangs down the thigh it covers. The round-7 adversarial
        # pass refuted the 2026-08 ribbon rebuild at beam_head range: the strips
        # still read as uniform planks (edge-line residuals under 5 px over 100
        # rows) and the two flank strips rendered as paper-flat hip cards. What
        # answers it, verbatim from the fix list:
        #
        # * **Per-strip hashed taper** on top of the widened width spread — no two
        #   strips share a width profile any more.
        # * **Two to three midpoint kinks**: the centreline dog-legs 10-26 mm
        #   sideways at hashed interior stations, so a straight line fitted to
        #   either edge misses by well over 5 px at beam_head range.
        # * **A creased cross-section**: six verts per ring with a radially proud
        #   ridge whose apex slides across the width — the strip folds along its
        #   length and catches the beam on two planes, never one. The ridge is
        #   also the Solidify pass: root thickness 14-22 mm tapering to 4 mm.
        # * **A V-cut tip on every strip**: the two fray fingers are longer and
        #   more unequal, and the notch vertex between them is pulled UP into the
        #   strip, so every tip ends torn (the beam_3m V-cuts, applied to all).
        tip_r = r0 * (0.72 + 0.12 * _hash01(9103, i))
        SEG = 7
        curl = (_hash01(9106, i) - 0.5) * 0.11
        twist = (_hash01(9107, i) - 0.5) * 1.4
        taper = 0.30 + 0.28 * _hash01(9110, i)
        crease = 0.8 + 1.4 * _hash01(9113, i)
        apex_slide = (_hash01(9114, i) - 0.5) * 0.5
        th_root = 0.014 + 0.008 * _hash01(9115, i)

        # the kinks: 2-3 hashed dog-legs, each a lateral jump that HOLDS from its
        # station down, so the centreline is piecewise, not smoothed back straight
        kink_off = [0.0] * (SEG + 1)
        n_kinks = 2 + (1 if _hash01(9111, i) > 0.45 else 0)
        for j in range(n_kinks):
            ks = 1 + int(_hash01(9112, i, j) * (SEG - 1))
            mag = 0.010 + 0.016 * _hash01(9116, i, j)
            sign = 1.0 if _hash01(9117, i, j) > 0.5 else -1.0
            for s in range(ks, SEG + 1):
                kink_off[s] += sign * mag

        radial = Vector((cx, cy, 0.0))
        rings = []
        for s in range(SEG + 1):
            t = s / SEG
            if s == 0:
                # the root ring, buried deep into the body so the strip never floats
                centre = Vector((cx * (r0 - 0.032), cy * (r0 - 0.032), z_root + 0.032))
                ww, th = width, th_root
                cd = Vector((tx, ty, 0.0))
            else:
                rr = _lerp(r0 + 0.004, tip_r, t) - math.sin(math.pi * t) * curl
                zz = z_root - (z_root - tip_z) * (0.06 + 0.94 * t)
                ww = max(width * (1.0 - taper * t - 0.22 * t * t), 0.016)
                th = max(th_root * (1.0 - 0.68 * t), 0.004)
                ang = twist * t
                ca, sa = math.cos(ang), math.sin(ang)
                cd = Vector((tx * ca, ty * ca, sa))
                lat = skew * t + kink_off[s]
                centre = Vector((cx * rr + tx * lat, cy * rr + ty * lat, zz))
            rings.append((centre, cd, ww, th))

        strip_faces = []
        ring_verts = []
        for si, (centre, cd, ww, th) in enumerate(rings):
            half = cd * (ww * 0.5)
            off = radial * (th * 0.5)
            # the crease apex wanders per station, so the fold line itself is bent
            wander = (_hash01(9118, i, si) - 0.5) * 0.3
            apex = cd * (ww * (apex_slide + wander) * 0.5)
            ridge = radial * (th * crease)
            ring_verts.append((
                bm.verts.new(centre - half + off),
                bm.verts.new(centre + apex + off + ridge),
                bm.verts.new(centre + half + off),
                bm.verts.new(centre + half - off),
                bm.verts.new(centre + apex - off + ridge * 0.35),
                bm.verts.new(centre - half - off),
            ))
        for a, b_ in zip(ring_verts, ring_verts[1:]):
            for k in range(6):
                k2 = (k + 1) % 6
                try:
                    strip_faces.append(bm.faces.new((a[k], a[k2], b_[k2], b_[k])))
                except ValueError:
                    pass
        try:
            strip_faces.append(bm.faces.new(tuple(reversed(ring_verts[0]))))
        except ValueError:
            pass

        # the V-cut: two fingers of unequal hashed length past the last ring, and
        # the notch vertex between them pulled UP into the strip so the tip is torn
        centre, cd, ww, th = rings[-1]
        half = cd * (ww * 0.5)
        lv = ring_verts[-1]
        notch = 0.012 + 0.018 * _hash01(9119, i)
        m_out = bm.verts.new(centre + radial * (th * 0.5) + Vector((0, 0, notch)))
        m_in = bm.verts.new(centre - radial * (th * 0.5) + Vector((0, 0, notch)))
        fa = 0.035 + 0.055 * _hash01(9108, i)
        fb = 0.025 + 0.050 * _hash01(9109, i)
        tip_a = bm.verts.new(centre - half * 0.55 + Vector((0.0, 0.0, -fa)))
        tip_b = bm.verts.new(centre + half * 0.60 + Vector((0.0, 0.0, -fb)))
        for quad, tipv in (((lv[0], m_out, m_in, lv[5]), tip_a),
                           ((m_out, lv[2], lv[3], m_in), tip_b)):
            for k in range(4):
                k2 = (k + 1) % 4
                try:
                    strip_faces.append(bm.faces.new((quad[k], quad[k2], tipv)))
                except ValueError:
                    pass
        for f in strip_faces:
            f.smooth = True
        # Every wall quad, the root cap and all eight fray faces, or the strip is
        # a partial shell — round 7's "hip cards" were exactly that read.
        expect = SEG * 6 + 1 + 8
        if len(strip_faces) != expect:
            blendkit.fail(f"skirt strip {i}: built {len(strip_faces)} of {expect} "
                          f"faces — a partial shell ships floating cards")
        bm.verts.ensure_lookup_table()
        ranges.append((start, len(bm.verts), "Hips"))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    me.update()
    print(f"SHROUD {obj.name} {len(ranges)} strip(s) rooted at z={z_root:.3f}")
    return ranges


def tear_maw_channel(obj: bpy.types.Object, m: dict) -> list[tuple]:
    """Breaks the chin-to-chest channel out of its machined straightness.

    The sculpt carves the maw as a groove running from the chin down the sternum,
    and it arrives exactly as a tool would leave it: straight, even-width, hard
    parallel margins. The 2026-08 review called it a slot, not a wound. Three
    treatments, all seeded and deterministic:

    * **The margin faces are subdivided once** (the sculpt's ~2 cm sampling cannot
      hold a torn edge) and then **displaced**: the slot's width breathes along its
      length (±40%, low-frequency), every channel vertex gets a small hashed
      jitter, and the floor of the slot is pushed deeper in irregular pools. The
      dark pooling and the wet-only-inside roughness ride the same geometry through
      ``dress_sculpt_maps``' position fields, so the maps follow the torn shape by
      construction.
    * **Gristle teeth** grow in the channel's upper third — short pinched spurs off
      the two walls, alternating sides, none of them symmetric. They are returned as
      ``(start, end, "Jaw")`` ranges: the caller weights them rigidly to the Jaw,
      which both makes them gape with the mandible and routes their faces onto
      ``Monster_Maw`` (``maw_polygons`` counts Jaw-majority faces), so §06's
      acquisition flare lights them from inside the throat.

    Displacements move X and Y only — never Z — so every landmark height measured
    before this holds, and the 2.336 m pin cannot move.
    """
    me = obj.data
    neck, top, shoulder, crotch = m["neck"], m["top"], m["shoulder"], m["crotch"]
    z_hi = neck + (top - neck) * 0.10        # the chin
    z_lo = shoulder - (shoulder - crotch) * 0.55   # mid-sternum

    def n1(z):     # slot width, low frequency
        return math.sin(z * 23.1 + 1.7) * 0.6 + math.sin(z * 47.3 + 4.0) * 0.4

    def n2(z):     # pool depth
        return 0.5 + 0.5 * math.sin(z * 31.7 + 2.9) * math.sin(z * 12.3 + 0.6)

    # The front reference per height: the frontmost surface beside the slot. The
    # slot's own floor sits BEHIND it (larger y — the sculpt faces -Y).
    bins = 36
    front = [None] * bins

    def bin_of(z):
        return min(bins - 1, max(0, int((z - z_lo) / max(z_hi - z_lo, 1e-6) * bins)))

    for v in me.vertices:
        c = v.co
        if z_lo < c.z < z_hi and abs(c.x) < 0.10 and c.y < 0.05:
            b = bin_of(c.z)
            if front[b] is None or c.y < front[b]:
                front[b] = c.y
    if all(f is None for f in front):
        print(f"MAWTEAR {obj.name} found no front surface in the channel band")
        return []
    last = next(f for f in front if f is not None)
    for b in range(bins):
        if front[b] is None:
            front[b] = last
        last = front[b]

    def in_channel(c):
        return (z_lo < c.z < z_hi and abs(c.x) < 0.055 and c.y < 0.05
                and c.y < front[bin_of(c.z)] + 0.045)

    # ── subdivide the channel faces so the tear has resolution to exist on ──
    # Below the neck only: gen_monster_ai.refine_head has already quadrupled
    # everything above it, and subdividing that band twice put 16x density into
    # exactly the Jaw's proximity capture — measured 8.4% of all faces on the maw,
    # through the 6% lantern ceiling.
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    sub_top = neck - 0.005
    channel_edges = [e for e in bm.edges
                     if in_channel(e.verts[0].co) and in_channel(e.verts[1].co)
                     and e.verts[0].co.z < sub_top and e.verts[1].co.z < sub_top]
    if channel_edges:
        bmesh.ops.subdivide_edges(bm, edges=channel_edges, cuts=1,
                                  use_grid_fill=True, smooth=0.0)
    bm.to_mesh(me)
    bm.free()
    me.update()

    # ── displace: breathing width, hashed jitter, pooled floor ──
    moved = 0
    for v in me.vertices:
        c = v.co
        if not in_channel(c):
            continue
        b = bin_of(c.z)
        depth = c.y - front[b]
        # the whole front band moves a little; the slot's own walls move most
        wall = max(0.0, 1.0 - abs(depth) / 0.045)
        widen = 1.0 + 0.40 * n1(c.z) * wall
        jx = (_hash01(9401, v.index) - 0.5) * 0.006
        jy = (_hash01(9402, v.index) - 0.5) * 0.005
        v.co.x = c.x * widen + jx * wall
        v.co.y = c.y + jy * wall
        if depth > 0.010:                       # the floor: pool it deeper
            v.co.y += 0.011 * n2(c.z) * min(1.0, (depth - 0.010) / 0.02)
        moved += 1
    me.update()

    # ── gristle teeth in the upper third ──
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    ranges = []
    teeth_hi = z_hi - 0.015
    teeth_lo = z_hi - (z_hi - z_lo) * 0.33
    count = 6
    for i in range(count):
        t = (i + 0.5) / count
        z = teeth_hi + (teeth_lo - teeth_hi) * t
        side = 1.0 if i % 2 == 0 else -1.0
        b = bin_of(z)
        wall_x = side * (0.012 + 0.014 * _hash01(9403, i))
        base_y = front[b] + 0.016 + 0.012 * _hash01(9404, i)
        length = 0.018 + 0.017 * _hash01(9405, i)
        radius = 0.0045 + 0.0035 * _hash01(9406, i)
        # tip leans toward the centreline and downward — a snag, not a peg
        tip = Vector((wall_x - side * length * 0.7,
                      base_y + length * 0.25,
                      z - length * 0.55))
        start = len(bm.verts)
        base = []
        for k in range(4):
            ang = k * math.pi / 2.0 + _hash01(9407, i, k) * 0.6
            base.append(bm.verts.new(Vector((
                wall_x + math.cos(ang) * radius * 0.6,
                base_y + math.sin(ang) * radius,
                z + math.cos(ang + 1.1) * radius))))
        tv = bm.verts.new(tip)
        for k in range(4):
            try:
                bm.faces.new((base[k], base[(k + 1) % 4], tv))
            except ValueError:
                pass
        try:
            bm.faces.new(tuple(reversed(base)))
        except ValueError:
            pass
        bm.verts.ensure_lookup_table()
        ranges.append((start, len(bm.verts), "Jaw"))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    me.update()
    print(f"MAWTEAR {obj.name} displaced {moved} vert(s) "
          f"z[{z_lo:.3f},{z_hi:.3f}], grew {len(ranges)} tooth/teeth")
    return ranges


def rasterize_fields(obj: bpy.types.Object, res: int) -> dict:
    """World position and limb masks per texel, rasterised from the skinned mesh.

    This is what turns the UV-space dressing anatomical: ``dress_sculpt_maps`` gets,
    for every texel the mesh actually covers, WHERE that texel sits on the body
    (object space, which is world space here — the transform is identity) and how
    arm-like / hand-like it is. The masks come from the deformation weights, not
    from a guessed bounding box, so "the forearms" means exactly the vertices the
    LowerArm/ForearmExtra/Hand bones own and nothing else — a claw that curls
    inboard of the thigh stays a claw.

    Triangles whose UVs straddle a wrap seam (span > 0.5 after centring) are
    skipped; on this atlas that is a handful of slivers. Overlaps — the planar-
    projected crest, skirt and teeth share UV space with body islands — resolve
    last-writer-wins in polygon order, i.e. the added geometry wins its own texels,
    which is the useful direction: the fin ribs land on fins. The residue is that a
    few body texels shared with added-geometry UVs take the added geometry's paint;
    at 2048² that is noise-level and reads as speckle.
    """
    me = obj.data
    me.calc_loop_triangles()
    uv_layer = me.uv_layers.active
    if uv_layer is None:
        return {"cover": np.zeros((res, res), bool)}
    uvdata = uv_layer.data

    gi = {g.name: g.index for g in obj.vertex_groups}
    arm_groups = {idx for name, idx in gi.items()
                  if ("LowerArm" in name or "ForearmExtra" in name
                      or name.endswith("Hand"))}
    hand_groups = {idx for name, idx in gi.items() if name.endswith("Hand")}

    n = len(me.vertices)
    vpos = np.zeros((n, 3), np.float32)
    varm = np.zeros(n, np.float32)
    vhand = np.zeros(n, np.float32)
    for v in me.vertices:
        vpos[v.index] = v.co[:]
        a = h = 0.0
        for g in v.groups:
            if g.group in arm_groups:
                a += g.weight
            if g.group in hand_groups:
                h += g.weight
        varm[v.index] = min(a, 1.0)
        vhand[v.index] = min(h, 1.0)

    pos = np.zeros((res, res, 3), np.float32)
    arm = np.zeros((res, res), np.float32)
    hand = np.zeros((res, res), np.float32)
    cover = np.zeros((res, res), bool)

    for tri in me.loop_triangles:
        li = tri.loops
        uv = np.array([uvdata[li[0]].uv, uvdata[li[1]].uv, uvdata[li[2]].uv],
                      dtype=np.float64)
        uv -= np.floor(uv.mean(axis=0))
        span = uv.max(axis=0) - uv.min(axis=0)
        if span[0] > 0.5 or span[1] > 0.5:
            continue
        t = uv * res - 0.5
        x0, x1 = int(math.floor(t[:, 0].min())), int(math.ceil(t[:, 0].max()))
        y0, y1 = int(math.floor(t[:, 1].min())), int(math.ceil(t[:, 1].max()))
        gy, gx = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        d = ((t[1, 1] - t[2, 1]) * (t[0, 0] - t[2, 0])
             + (t[2, 0] - t[1, 0]) * (t[0, 1] - t[2, 1]))
        if abs(d) < 1e-12:
            continue
        w0 = ((t[1, 1] - t[2, 1]) * (gx - t[2, 0])
              + (t[2, 0] - t[1, 0]) * (gy - t[2, 1])) / d
        w1 = ((t[2, 1] - t[0, 1]) * (gx - t[2, 0])
              + (t[0, 0] - t[2, 0]) * (gy - t[2, 1])) / d
        w2 = 1.0 - w0 - w1
        inside = (w0 >= -1e-3) & (w1 >= -1e-3) & (w2 >= -1e-3)
        if not inside.any():
            continue
        py = gy[inside] % res
        px = gx[inside] % res
        vi = tri.vertices
        wa, wb, wc = w0[inside], w1[inside], w2[inside]
        pos[py, px] = (np.outer(wa, vpos[vi[0]]) + np.outer(wb, vpos[vi[1]])
                       + np.outer(wc, vpos[vi[2]])).astype(np.float32)
        arm[py, px] = (wa * varm[vi[0]] + wb * varm[vi[1]] + wc * varm[vi[2]])
        hand[py, px] = (wa * vhand[vi[0]] + wb * vhand[vi[1]] + wc * vhand[vi[2]])
        cover[py, px] = True

    print(f"FIELDS {obj.name} cover={float(cover.mean()):.3f} "
          f"arm={float((arm > 0.5).mean()):.4f} hand={float((hand > 0.5).mean()):.4f}")
    return {"pos": pos, "arm": arm, "hand": hand, "cover": cover}


# ── Measurement ─────────────────────────────────────────────────────────────

_BONE_RE = re.compile(r'pose\.bones\["([^"]+)"\]\.(\w+)')


def lowest_point(body: bpy.types.Object) -> float:
    """World Z of the lowest deformed vertex at the current frame."""
    return lowest_vertex(body).z


def lowest_vertex(body: bpy.types.Object) -> Vector:
    """World position of the lowest deformed vertex — the actual contact point.

    Not a bone: which part of the creature is touching the floor changes through the
    cycle (pad, then claw tip), and the bone that owns it moves relative to the
    contact. The lowest vertex is the contact by definition, which is what a
    foot-lock has to hold still.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = body.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        best = None
        for v in mesh.vertices:
            world = body.matrix_world @ v.co
            if best is None or world.z < best.z:
                best = world
        return best.copy() if best is not None else Vector((0.0, 0.0, 0.0))
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


def lock_stance_slide(rig: bpy.types.Object, action) -> tuple[float, float, float]:
    """Re-keys the hips' fore-aft translation so the contact point stops skating.

    The vertical twin of this is `ground_action`, and the argument is the same one:
    a gait authored as hip/knee/ankle/toe angle curves has no constraint that keeps
    the foot still while it bears weight, so it slides, and no playback rate can
    remove a slide that varies *within* the cycle. Measured before this pass, the
    contact point moved at 1.52 m/s ± 2.76 on Patrol and 5.27 ± 9.27 on Chase — the
    ripple being larger than the mean means the foot was going backwards and
    forwards under the creature, which is exactly what reads as skating.

    The correction is exact in one pass because translating the hips moves the whole
    rigid-weighted body. Writing `d[i]` for the weight-bearing foot's backward travel
    over interval i (see `stance_rates`) and `o[i]` for the hips offset:

        o[i+1] = o[i] + (v/FPS − d[i])

    and the one free parameter, v, is then *forced* by the requirement that the clip
    loop — the offset has to return to where it started after one cycle, which pins

        v = (Σ d) · FPS / (number of intervals)

    So the clip's true ground speed is not chosen here, it is solved for. That is the
    number written into Monster.clips.json and the number Unity divides §07's tier
    speed by. Returns (solved speed, worst residual slide, peak hip correction).
    """
    muted = [(t, t.mute) for t in rig.animation_data.nla_tracks]
    for track, _ in muted:
        track.mute = True

    rates = stance_rates(rig, action)
    if not rates:
        for track, was in muted:
            track.mute = was
        return 0.0, 0.0, 0.0

    speed = sum(rates) * FPS / len(rates)
    offsets = [0.0]
    for d in rates:
        offsets.append(offsets[-1] + speed / FPS - d)

    rig.animation_data.action = action
    lo, hi = (int(v) for v in action.frame_range)

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")

    hips = rig.pose.bones["Hips"]
    basis = rig.data.bones["Hips"].matrix_local.to_3x3()
    for i, frame in enumerate(range(lo, hi + 1)):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        world = basis @ Vector(hips.location) + Vector((0.0, offsets[i], 0.0))
        hips.location = basis.inverted() @ world
        hips.keyframe_insert(data_path="location", frame=frame)
    bpy.ops.object.mode_set(mode="OBJECT")

    rig.animation_data.action = None
    for track, was in muted:
        track.mute = was

    # Re-measure against the corrected clip rather than trusting the arithmetic. The
    # keys were written through the pose channel and read back through the evaluated
    # armature, so this catches a basis or sign mistake that the algebra cannot.
    after = stance_rates(rig, action)
    residual_slide = max(abs(r * FPS - speed) for r in after) if after else 0.0
    return speed, residual_slide, max(abs(o) for o in offsets)


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


def sample_contacts(rig: bpy.types.Object, action) -> list:
    """Per-frame world position of both toe-pad pivots (the Toes bone heads).

    The pivot rather than the toe TIP, because the tip swings through 30° of curl at
    toe-off (GAIT_PHASES) and a point that rotates is not a point that is planted.
    """
    muted = [(t, t.mute) for t in rig.animation_data.nla_tracks]
    for track, _ in muted:
        track.mute = True
    rig.animation_data.action = action

    lo, hi = (int(v) for v in action.frame_range)
    frames = []
    for frame in range(lo, hi + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        frames.append({side: (rig.matrix_world @ rig.pose.bones[side + "Toes"].head).copy()
                       for side in ("Left", "Right")})

    rig.animation_data.action = None
    for track, was in muted:
        track.mute = was
    return frames


def stance_rates(rig: bpy.types.Object, action) -> list:
    """Per-interval backward travel of the weight-bearing foot, in metres per frame.

    <b>Stance is identified by direction of travel, not by height.</b> That is not the
    obvious choice and it was arrived at by measurement: probing the Chase cycle frame
    by frame showed the *swing* foot skimming within 2 cm of the floor for four frames
    after toe-off, low enough that the grounding pass anchored the body to it while it
    was sweeping forward at 4 m/s. Any height-based test calls that frame a contact
    and reads the gait's speed as negative.

    The physical definition has no such failure mode. With no root motion the planted
    foot is the one moving backwards through rig space, and it is moving backwards at
    exactly the speed the creature is travelling forwards; a swinging foot moves the
    other way. So: whichever foot has the greater +Y velocity is bearing weight, and
    that velocity is the answer. (The monster faces −Y.)
    """
    frames = sample_contacts(rig, action)
    rates = []
    for i in range(len(frames) - 1):
        rates.append(max(frames[i + 1][side].y - frames[i][side].y for side in ("Left", "Right")))
    return rates


def measure_ground_speed(rig: bpy.types.Object, action) -> tuple[float, float]:
    """The ground speed the clip's own stride produces, m/s, measured not derived.

    Unity plays these clips against a transform driven by §07's tier speed, so the
    only way the feet stay planted is if playback runs at `tierSpeed ÷ this`.

    `stride_report` computes the same quantity from 2·L·sin θ, but that is an
    idealised strut and the real cycle has an ankle, a 0.51 m metatarsal and a
    grounding pass moving the hips every frame. Both are printed so a divergence
    between the model of the gait and the gait is visible rather than assumed away.

    Returns (mean, worst single-frame departure). The second number is the honest one:
    it is the foot slide *inside* the clip, which no playback rate can remove and
    which `lock_stance_slide` exists to take out at the source.
    """
    rates = stance_rates(rig, action)
    if not rates:
        return 0.0, 0.0
    mean = sum(rates) / len(rates) * FPS
    return mean, max(abs(r * FPS - mean) for r in rates)


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


def stride_report(amp: float, cycle_frames: int, limp: float = 1.0) -> tuple[float, float]:
    """Step length and resulting ground speed for a gait amplitude.

    step = 2 · L · sin(θ), L = hip-to-toe strut (1.38 m), θ = peak hip swing.
    speed = 2 steps / cycle duration.

    `limp` matters because this number is the independent check on `lock_stance_slide`
    — the two must agree or the contact detection is misreading which foot bears
    weight. Patrol's right leg swings at 0.70 of the left's on purpose (§06 순찰 is
    unhurried *and wrong*), so its two steps are not the same length and a symmetric
    model of the gait reports a stride the clip never takes. Averaging the two
    amplitudes keeps the check honest about a gait that is deliberately not.
    """
    theta = math.radians(PEAK_HIP_SWING * amp * (1.0 + limp) * 0.5)
    step = 2.0 * HIP_TO_TOE * math.sin(theta)
    speed = 2.0 * step / (cycle_frames / FPS)
    return step, speed


CLIP_MANIFEST = os.path.join(blendkit.MODELS_DIR, "Characters", "Monster.clips.json")
"""What each clip is worth, measured. Read by the Unity animator builder."""


def write_clip_manifest(order: list, stats: dict, travel: dict, locks: dict) -> None:
    """Publishes each clip's length and its own ground speed.

    This exists so the Unity side never guesses. `MonsterAnimationDriver` used to
    scale playback by `speed ÷ GameConstants.MonsterBaseSpeed`, which is right only
    for Chase — it assumed every clip was authored at 4.8 m/s, so a Patrol authored
    at a fifth of that ran at a fifth of the correct cadence and the feet skated
    through §12's floors. Numbers measured here, consumed there, one copy.
    """
    payload = {
        "generated_by": "tools/blender/gen_monster_model.py",
        "fps": FPS,
        "note": "ground_speed_mps is measured from the weight-bearing foot, not derived. "
                "Playback rate for a §07 tier speed is tierSpeed / ground_speed_mps.",
        "clips": [
            {
                "name": name,
                "frames": stats[name]["frames"],
                "seconds": round(stats[name]["seconds"], 4),
                "loops": name in ("Patrol", "Alert", "Chase", "Search", "Standstill"),
                "locomotion": name in locks,
                # The solved speed when the clip was stance-locked, else the measured
                # median. Locked clips are the ones Unity speed-matches against.
                "ground_speed_mps": round(locks[name][0] if name in locks else travel[name][0], 4),
                "residual_slide_mps": round(locks[name][1], 4) if name in locks else None,
                "stance_speed_spread_mps": round(travel[name][1], 4),
            }
            for name in order
        ],
    }
    os.makedirs(os.path.dirname(CLIP_MANIFEST), exist_ok=True)
    with open(CLIP_MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


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
    if still["max_deg"] > 3.0:
        blendkit.fail(
            f"Standstill moves {still['max_deg']:.2f}° — §06 makes this the silent state "
            "and the game's weapon. The player is supposed to be unable to tell whether "
            "it moved at all; anything they can be sure they saw answers the question."
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
    # The hull monster is no longer what ships, and every path below writes to the names
    # that do — Monster.fbx, Monster.clips.json, Monster.textures.json. Running this by
    # habit would silently replace the sculpt with the asset it replaced, and nothing
    # downstream would notice: the file names match, the rig matches, the clip count
    # matches. So it has to be asked for.
    tail = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--hull" not in tail:
        print("SKIP gen_monster_model no longer writes the shipped monster — "
              "tools/blender/gen_monster_ai.py does. This file is kept for the seven §06 "
              "clip authors and the procedural skin pipeline, both of which gen_monster_ai "
              "imports. Pass `-- --hull` to rebuild the hull creature over Monster.fbx for "
              "a before/after comparison.")
        return None

    blendkit.reset_scene()
    blendkit.set_frame_range(1, 91)
    PARTS.clear()

    for spec in (
        MaterialSpec(FLESH, (0.255, 0.232, 0.212), roughness=0.88),
        MaterialSpec(CARAPACE, (0.072, 0.068, 0.066), roughness=0.52),
        MaterialSpec(MAW, (0.185, 0.052, 0.048), roughness=0.38),
        MaterialSpec(EYES, (0.150, 0.156, 0.146), roughness=0.18),
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

    # The skin, before the rig: the unwrap has to exist to measure the UV density
    # the tiling is derived from, and nothing after this point touches UVs.
    limits = pipeline_constants()
    res = limits["DEFAULT_RESOLUTION"]
    uv_per_metre = uv_units_per_metre(body)
    skins = [write_skin(name, build, target, note, res, limits)
             for name, build, target, note in SKINS]
    write_skin_manifest(skins, uv_per_metre, res)
    print(f"SKIN_UV uv_units_per_metre={uv_per_metre:.4f} "
          f"(1 tile of a {SKINS[0][0]} map covers "
          f"{1.0 / uv_per_metre:.2f} m of surface at tiling 1)")
    for s in skins:
        print(f"SKIN_REPORT {s['name']:18s} albedo={s['albedo_mean_linear']:.4f}lin "
              f"max={s['albedo_max_linear']:.4f} (corridor {s['corridor_darkest_linear']:.4f}) "
              f"gain={s['albedo_gain']:.2f} rough={s['roughness_mean']:.3f} "
              f"metal={s['metallic_mean']:.3f} relief={s['relief_mm']:6.2f}mm "
              f"ao={s['ao_mean']:.3f} n_err={s['normal_unit_error']:.4f} "
              f"tile={s['world_size_metres']:.2f}m bytes={s['bytes']}")

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
    travel: dict[str, tuple[float, float]] = {}
    locks: dict[str, tuple[float, float, float]] = {}
    for build in builders:
        action, amp = build(rig)
        ground_action(rig, body, action)
        if amp is not None:
            # Locomotion only. Search, Grab and Stunned are *meant* to drift — a
            # creature casting about or recoiling is not holding a stride — and
            # locking them would freeze motion the design asked for.
            locks[action.name] = lock_stance_slide(rig, action)
            gaits[action.name] = amp
        ground[action.name] = grounding_error(rig, body, action)
        stats[action.name] = measure_action(action)
        # After both correction passes, because they move the hips every frame and
        # therefore move the foot this measures.
        travel[action.name] = measure_ground_speed(rig, action)
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
    # The gait asymmetry each locomotion clip was authored with, so `stride_report`
    # models the clip rather than an idealised symmetric one.
    LIMPS = {"Patrol": PATROL_LIMP, "Chase": 1.0}
    for name in order:
        s = stats[name]
        extra = ""
        if name in gaits:
            step, speed = stride_report(gaits[name], s["frames"] - 1, LIMPS[name])
            extra = f" amp={gaits[name]:.2f} step={step:.2f}m derived={speed:.2f}m/s"
        measured, ripple = travel[name]
        print(f"ANIM_REPORT {name:11s} frames={s['start']}-{s['end']} "
              f"({s['frames']:3d}f, {s['seconds']:.2f}s) curves={s['curves']} "
              f"keys={s['keys']:4d} max_bone_motion={s['max_deg']:6.2f}deg"
              f" head={s['per_bone'].get('Head', 0.0):5.1f}deg"
              f" hipswing={s['per_bone'].get('LeftUpperLeg', 0.0):5.1f}deg"
              f" ground={measured:5.2f}m/s ripple=±{ripple:.2f}{extra}")

    # Foot slide is the failure the brief names, so it gets its own measured line.
    # `solved` is the speed the loop constraint forced; `residual` is what still
    # skates after the lock, and it is the number that decides whether the feet
    # read as planted.
    for name in ("Patrol", "Chase"):
        solved, residual, correction = locks[name]
        _, derived = stride_report(gaits[name], stats[name]["frames"] - 1, LIMPS[name])
        print(f"LOCK_REPORT {name:11s} solved={solved:.3f}m/s derived={derived:.3f}m/s "
              f"residual_slide={residual * 1000:6.1f}mm/s peak_hip_correction={correction * 1000:.1f}mm")
        # The residual is zero by construction, so it proves nothing on its own. What
        # can go wrong is the SOLVE: if the stance detection misreads which foot is
        # bearing weight, it converges beautifully on a nonsense speed. The
        # independent check is the leg geometry — 2·L·sin θ knows nothing about the
        # contact test, so agreement between the two means both are right.
        if not 0.75 <= solved / max(derived, 1e-6) <= 1.35:
            blendkit.fail(f"{name}'s stance lock solved {solved:.3f} m/s against the {derived:.3f} m/s "
                          "the leg geometry gives. The contact detection is misreading which foot "
                          "bears weight, so the gait speed Unity divides by would be fiction.")
        if correction > 0.30:
            blendkit.fail(f"{name}'s stance lock had to move the hips {correction * 1000:.0f} mm "
                          "fore-aft. That is a lurch, not a correction — the authored cycle is not "
                          "covering its own stride.")

    write_clip_manifest(order, stats, travel, locks)

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
