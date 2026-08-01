#!/usr/bin/env python3
"""Builds the player from a sculpt's hands: mesh, Humanoid rig, mounts, nine clips.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_player_ai.py

Writes ``Assets/Models/Characters/Player.fbx`` — **the player the game loads** — with
``Player.glb`` beside it and ``Assets/Textures/Player.textures.json``. Those are the
same three names ``gen_player_model.py`` used to write, and they are still the only
ones: ``PlayerFeelHarnessMenu``, ``AssetImportPolicy.ExpectedAnimationClipCount`` and
``PlayerRigParts`` all address that one file. ``gen_player_model.py`` is now a library
(its clip authors, its pose solver, its surfaces and its verification are all imported
here) and no longer writes a shipping asset.

WHY THIS EXISTS ALONGSIDE gen_player_model.py
---------------------------------------------
The same argument ``gen_monster_ai.py`` makes, applied to the one part of a person a
first-person game shows constantly. That file's header says a hull-and-tube assembly
has "no sunken flank, no hollow between two ribs". A hand is worse: the whole of what
makes a hand read as a hand is **the gaps** — between two fingers, under the arch of
the palm, in the web of the thumb — and every one of those is concave. The previous
model made each finger a tapered box off a slab, and §05 puts that assembly 35 cm from
the camera for the entire match. It photographed as a plank with prongs.

So the hands here are not built, they are **cut out of a sculpt**:

    vessel sculpt → isolate the +X hand → derive its own frame → orient into the
    T-pose → rescale to a human hand → decimate → segment the five digits →
    fit finger bones to them → weld onto a generated arm

WHAT THE SCULPT IS, AND WHAT IT IS NOT — MEASURED
--------------------------------------------------
``tools/blender/source/monster_vessel_base.glb`` is the human-shaped Rodin variant
``gen_monster_ai.py`` describes as *"something that used to be a person"*. Scaled to
1.75 m and measured with ``monster_fit.measure`` it splits cleanly in two:

* **Its hands are human and they are the reason this file exists.** Wrist to fingertip
  measures **0.184 m** against 0.189 m for a 1.75 m adult — 97 %. Slicing the hand
  perpendicular to its own axis finds **five separated masses 4–19 mm across** at the
  fingertips and five again at the knuckles: fingers, an opposed thumb, and knuckles,
  none of which anything in this repo could have built.
* **Its body is not a person and cannot be made into one cheaply.** Acromion to wrist
  measures **0.910 m** on a 1.75 m frame where a human is ~0.49 m — the arm is
  **1.9×** too long, and its fingertips hang at z = 0.337 m, *below its own knee at
  0.416 m*. The torso carries flayed skin flaps at the collarbone, ribs, waist and
  thigh, and the face below the brow is torn open. §05 needs three teammates to tell a
  player from a 2.336 m monster down a 12 m beam; a naked flayed humanoid is on the
  wrong side of that line, and the arm length alone puts it there.

So the sculpt is taken for the hands and rejected for the body, and the body is built
— because what a body has to do at the 3–15 m §12 corridors allow is carry a
**silhouette and a role colour**, not a sculpted flank. ART.md §4.1 measured that
directly on the creature: *"the sculpt detail the art pass added is invisible past
about 5 m"*. Nobody is ever more than a hand's length from these hands.

THE TRIANGLE BUDGET, AND WHERE IT GOES
---------------------------------------
The monster ships 5,704 against a 6,000 cap. This model is allowed the same order and
spends it very differently, because the two are looked at from different distances:

    Player_Arms    shoulders → fingertips, and 2 × HAND_TRIS of that is the hands
    Player_Body    head, torso, legs, helmet, harness, pack, boots
    Player_Torch   the flashlight in the fist

The hands take the largest single share of any part of this model on purpose. They are
the only geometry in the game a camera is ever 0.35 m from; the body is never closer
than about 1.5 m to anybody, and at §04's 관측 range it is sixty pixels tall.

WHAT IS LOAD-BEARING AND NOT NEGOTIABLE
----------------------------------------
* ``AssetImportPolicy.PlayerHeightMetres`` pins 1.750 m and ``ScaleTolerance`` is 2 %.
* ``AssetImportPolicy.PlayerMountBones`` — HeadCameraAnchor, FlashlightMount,
  ObjectiveMount, BackpackMount. They are not humanoid bones, so nothing but this
  generator and ``AssetImportValidator`` protects them.
* ``ExpectedAnimationClipCount`` says 9 clips, and ``LoopingAnimationClips`` /
  ``OneShotAnimationClips`` say which loop.
* Unity Humanoid bone names throughout, including the fingers — see FINGERS below.
* ``PlayerStance`` sizes the crouched capsule from ``GameConstants``; the Crouch clip
  must keep ``HeadCameraAnchor`` at or below 1.28 m and this file fails its own build
  otherwise (``gen_player_model.verify_motion``).

FINGERS — WHY THEY ARE UNITY'S OWN NAMES
-----------------------------------------
A Humanoid import converts clips to muscle space and **drops every curve on a bone the
avatar does not map**. Finger animation therefore only survives if the bones are named
something Unity's auto-mapper recognises, so they use the ``HumanBodyBones`` spelling
verbatim — ``LeftThumbProximal``, ``LeftIndexIntermediate`` and so on. Two joints per
digit, not three: the distal phalanx is optional in Humanoid, it is 12 mm long at this
scale, and its own bone would cost 10 more bones to move something no camera resolves.

Even so the rest pose is a **relaxed half-curl rather than a flat splay**, and that is
insurance rather than styling: if the mapping ever fails the hands freeze at rest, and
a resting hand that is already shaped like a hand is a far cheaper failure than five
flat spars.
"""

from __future__ import annotations

import math
import os
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import blendkit  # noqa: E402
import monster_fit  # noqa: E402
import gen_player_model as gpm  # noqa: E402
from blendkit import BoneSpec  # noqa: E402


SOURCE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "source")
VESSEL = "monster_vessel_base.glb"
MANIFEST_SOURCE = "tools/blender/gen_player_ai.py"

# ── Hand geometry ───────────────────────────────────────────────────────────

HAND_LENGTH = 0.181
"""Wrist crease to middle fingertip, metres. 0.103 x height, inside the human band.

The previous model's hand was 0.110 m — a child's — and it got away with it because
nothing had ever looked at it from the inside.

**What it costs is the wrist, and the reason is a rule this file does not own.**
``AssetImportValidator`` compares a character's largest extent to
``AssetImportPolicy.PlayerHeightMetres`` within ``ScaleTolerance``, 2 %, and on a T-posed
figure the largest extent is the **span**, not the height. So the span may not exceed
1.785 m, and a real hand on the old 0.735 m wrist gives 1.832 — which the validator
rejected, correctly, the first time this asset was imported with one. ``gpm.WRIST_X`` came
in to 0.7065 to pay for it: span 1.775 m, a ratio of **1.014** against the human 1.00–1.06,
and a forearm of 0.2315 m against an adult's 0.245. The alternative was a 0.157 m hand,
which is the child's hand again with better topology.
"""

HAND_TIP_X = gpm.WRIST_X + HAND_LENGTH

HAND_TRIS = 1400
"""Triangles per hand, after decimation and before the wrist stump is closed.

Chosen by looking: at 700 the sculpt's knuckles flatten and the gap between the ring
and little fingers closes, which is the exact detail the hand is being harvested for.
Two hands is therefore about 2,500 of this model's budget, and it is the right place
for it — see the section note on distance.
"""

DIGIT_NAMES = ("Thumb", "Index", "Middle", "Ring", "Little")
"""Unity ``HumanBodyBones`` spelling, in thumb-to-little order. The segmentation sorts
the four fingers by their position across the palm and assigns these in order, so a
re-sculpt with differently proportioned fingers still lands index next to the thumb."""


def digit_bones(side: str) -> list[str]:
    """The ten finger bone names on one hand, in ``bone_specs`` order."""
    return [f"{side}{d}{j}" for d in DIGIT_NAMES
            for j in ("Proximal", "Intermediate")]


# ── Loading and cutting the sculpt ──────────────────────────────────────────


def load_sculpt(path: str) -> bpy.types.Object:
    """Imports the glb, drops the importer's debris, stands it up, scales to 1.75 m.

    Same contract as ``gen_monster_ai.load_base`` plus the normalise: what comes back is
    one mesh at the origin, Z-up, facing -Y, feet on z = 0, at the player's own height.
    Working on mesh data rather than the object transform is deliberate and is
    ``gen_monster_ai.normalise``'s hard-won note — ``transform_apply`` is an operator and
    operators silently do nothing under ``--background``.
    """
    if not os.path.exists(path):
        blendkit.fail(f"missing base sculpt {path}. §05's hands are cut out of it; there is "
                      "nothing to fall back to that would not be the mitten this replaced.")

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    added = [o for o in bpy.data.objects if o not in before]

    meshes = [o for o in added if o.type == "MESH" and not o.name.startswith("Icosphere")]
    if not meshes:
        blendkit.fail(f"{os.path.basename(path)} contains no mesh")
    obj = max(meshes, key=lambda o: len(o.data.vertices))
    for junk in added:
        if junk is not obj:
            bpy.data.objects.remove(junk, do_unlink=True)

    obj.matrix_world = Matrix.Identity(4)
    obj.parent = None
    weld(obj)

    me = obj.data

    def bounds():
        lo = Vector((1e9,) * 3)
        hi = Vector((-1e9,) * 3)
        for v in me.vertices:
            for i in range(3):
                lo[i] = min(lo[i], v.co[i])
                hi[i] = max(hi[i], v.co[i])
        return lo, hi

    lo, hi = bounds()
    if (hi.z - lo.z) < max(hi.x - lo.x, hi.y - lo.y):
        me.transform(Matrix.Rotation(math.radians(90.0), 4, "X"))   # Rodin returns Y-up
        lo, hi = bounds()
    me.transform(Matrix.Scale(gpm.HEIGHT / (hi.z - lo.z), 4))
    lo, hi = bounds()
    me.transform(Matrix.Translation(Vector((-(lo.x + hi.x) * 0.5,
                                            -(lo.y + hi.y) * 0.5, -lo.z))))
    me.update()
    return obj


def weld(obj: bpy.types.Object, distance: float = 1e-5) -> tuple[int, int]:
    """Merges coincident vertices. Returns (before, after).

    **Not tidying — the sculpt arrives cut into 3,505 pieces without it.** glTF stores one
    vertex per (position, normal, UV) tuple, so every UV seam and every hard edge on
    Rodin's unwrap is a duplicated position, and Blender's importer keeps them apart. The
    measured file: 25,670 vertices and 45,616 edges over 23,332 triangles, where a closed
    triangle mesh would have 34,998 edges. Everything downstream of here reads the hand as
    *the connected component that is a hand* — the cut, the digit segmentation, the check
    that a mitten has been rejected — and on unwelded geometry the largest component in the
    whole hand region is 55 vertices, which is one finger pad.
    """
    before = len(obj.data.vertices)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=distance)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    after = len(obj.data.vertices)
    print(f"WELD {obj.name} verts {before}->{after} (glTF splits every UV seam)")
    return before, after


WRIST_CUT = -0.006
"""Where the hand is cut off, as metres distal of the measured wrist crease.

Just proximal of it, which is as far back as the cut can go without keeping the vessel's
torn wrist skin and as far forward as it can go without moving the palm centroid every
other measurement is taken from. Two alternatives were tried and both cost more than they
bought: cutting 11 mm *into* the palm to clear the flaps moved the centring and the digit
segmentation came back with a ring finger longer than a middle finger, and eroding the
flaps by boundary took the fingertips with them and squared off the index and the little
finger — which is exactly the geometry this file exists to harvest.

What is left of the torn skin sits inside the sleeve: ``cuff_rings`` runs the forearm 26
mm past this plane and into the palm.
"""

FOREARM_STUB = 0.006
"""Metres of forearm kept above the wrist on the harvested hand.

Almost nothing, and deliberately: a longer stub was tried and abandoned. The cut plane is
oblique to a hanging hand and the thumb's base sits on it, so the proximal face measures
**98 x 50 mm** whatever is kept — it is never a tidy forearm section, and a sleeve sized
to swallow it is a bell. What closes the joint instead is a **glove cuff** built onto the
hand's own measured cross-sections a centimetre further out, where the geometry is a palm
and behaves. See ``cuff_rings``."""


def _largest_island(bm: bmesh.types.BMesh) -> set:
    """The biggest edge-connected group of vertices. Returns a set of BMVert."""
    bm.verts.ensure_lookup_table()
    seen: set = set()
    best: set = set()
    for start in bm.verts:
        if start in seen:
            continue
        stack = [start]
        island = set()
        seen.add(start)
        while stack:
            v = stack.pop()
            island.add(v)
            for edge in v.link_edges:
                other = edge.other_vert(v)
                if other not in seen:
                    seen.add(other)
                    stack.append(other)
        if len(island) > len(best):
            best = island
    return best


def cut_out_hand(obj: bpy.types.Object, m: dict) -> bpy.types.Object:
    """Separates the sculpt's +X hand into an object of its own.

    Two planes and then connectivity, in that order, because each alone is wrong. The
    horizontal plane at the measured wrist keeps the hands, the feet and the shins; the
    vertical one at ``x_gate`` drops everything inboard of the arm, which on this sculpt
    means the legs — its ankles sit at x = 0.148 m and its wrists at x = 0.318 m, so the
    gate has 17 cm of daylight either side and is not a tuned number. What is left on the
    +X side is the hand plus whatever shards the flayed thigh contributed, and the
    largest island is the hand.
    """
    x_gate = (m["arm_x_wrist"] + m["leg_x_ankle"]) * 0.5
    ceiling = m["wrist"] + FOREARM_STUB
    if x_gate <= m["leg_x_ankle"] * 1.15:
        blendkit.fail(f"the sculpt's wrist (x={m['arm_x_wrist']:.3f}) and ankle "
                      f"(x={m['leg_x_ankle']:.3f}) are too close to separate the hand from "
                      "the leg with a plane. The arms are not hanging clear of the body.")

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    doomed = [v for v in bm.verts if v.co.z > ceiling or v.co.x < x_gate]
    bmesh.ops.delete(bm, geom=doomed, context="VERTS")

    keep = _largest_island(bm)
    if len(keep) < 200:
        blendkit.fail(f"the largest island below the wrist and outboard of x={x_gate:.3f} has "
                      f"{len(keep)} vertices. That is not a hand — the sculpt's landmarks and "
                      "its geometry disagree.")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v not in keep], context="VERTS")

    mesh = bpy.data.meshes.new("HarvestedHand")
    bm.to_mesh(mesh)
    bm.free()
    hand = bpy.data.objects.new("HarvestedHand", mesh)
    bpy.context.collection.objects.link(hand)
    return hand


# ── Orienting the hand ──────────────────────────────────────────────────────


def hand_frame(hand: bpy.types.Object, wrist_z: float) -> tuple[Vector, Vector, Vector, Vector]:
    """The hand's own axes, read off its geometry. Returns (wrist, distal, palm_n, across).

    **Derived, never typed.** ``gen_monster_ai.add_eyes`` makes this argument at length
    and it applies here for the same reason: a hand-picked rotation is correct for one
    sculpt and silently wrong for the next, and the symptom — a hand rolled 20° — is
    exactly the kind of thing that reads as "cheap" without reading as "broken".

    * **distal** is wrist-slab centroid → tip-slab centroid. The hand hangs, so those are
      the top and bottom of its own z range.
    * **palm normal** is the direction of least variance in the plane perpendicular to
      distal. A hand is a flattened thing; that is its defining shape and it is what the
      covariance finds. Taking the bounding box instead reads the *thumb* as the thin
      axis on a relaxed hand, which rolls the result 90°.
    * The sign of the palm normal is settled by the thumb, in ``orient_hand`` — the two
      candidate frames are palm-down-thumb-forward and palm-up-thumb-back, and only one
      of those is a left hand in a T-pose.
    """
    everything = [hand.matrix_world @ v.co for v in hand.data.vertices]
    # The hand proper. The piece also carries FOREARM_STUB of arm above the wrist, and
    # every axis below has to be the hand's — a forearm is round, so including it drops
    # the measured flatness and tilts the long axis toward the arm.
    pts = [p for p in everything if p.z <= wrist_z]
    if len(pts) < 100:
        blendkit.fail(f"only {len(pts)} vertices lie below the measured wrist; the cut kept "
                      "an arm and no hand.")
    z_hi = max(p.z for p in pts)
    z_lo = min(p.z for p in pts)
    span = z_hi - z_lo
    if span < 0.05:
        blendkit.fail(f"the harvested hand spans {span * 100:.1f} cm along its own axis; "
                      "it is a shard, not a hand.")

    def slab_centroid(lo, hi):
        band = [p for p in pts if lo <= p.z <= hi]
        return sum(band, Vector((0, 0, 0))) / len(band)

    wrist = slab_centroid(z_hi - span * 0.10, z_hi)
    tip = slab_centroid(z_lo, z_lo + span * 0.12)
    distal = (tip - wrist).normalized()

    # Covariance of the cloud projected into the plane ⊥ distal, in a 2-D basis of that
    # plane. The smaller eigenvector, lifted back to 3-D, is the palm normal.
    ref = Vector((0.0, 0.0, 1.0))
    if abs(distal.dot(ref)) > 0.9:
        ref = Vector((1.0, 0.0, 0.0))
    e1 = (ref - distal * ref.dot(distal)).normalized()
    e2 = distal.cross(e1)

    sxx = sxy = syy = 0.0
    centre = sum(pts, Vector((0, 0, 0))) / len(pts)
    for p in pts:
        d = p - centre
        a, b = d.dot(e1), d.dot(e2)
        sxx += a * a
        sxy += a * b
        syy += b * b
    n = float(len(pts))
    sxx, sxy, syy = sxx / n, sxy / n, syy / n

    # Smaller eigenvalue of the symmetric 2×2 [[sxx, sxy], [sxy, syy]].
    tr, det = sxx + syy, sxx * syy - sxy * sxy
    disc = max(0.0, tr * tr * 0.25 - det) ** 0.5
    lam_small = tr * 0.5 - disc
    lam_big = tr * 0.5 + disc
    if abs(sxy) > 1e-12:
        vec = Vector((lam_small - syy, sxy)).normalized()
    else:
        vec = Vector((1.0, 0.0)) if sxx < syy else Vector((0.0, 1.0))
    palm_n = (e1 * vec.x + e2 * vec.y).normalized()

    flatness = (lam_big / max(lam_small, 1e-12)) ** 0.5
    if flatness < 1.35:
        blendkit.fail(f"the hand's cross-section is {flatness:.2f}:1 — too round for the "
                      "covariance to say which way the palm faces. A hand is a flattened "
                      "thing; this one is a tube, so it is a mitten and harvesting it buys "
                      "nothing.")
    print(f"HAND_FRAME span={span:.4f}m flatness={flatness:.2f}:1 "
          f"distal=({distal.x:+.3f},{distal.y:+.3f},{distal.z:+.3f}) "
          f"palm_n=({palm_n.x:+.3f},{palm_n.y:+.3f},{palm_n.z:+.3f})")
    return wrist, distal, palm_n, distal.cross(palm_n)


def _apply_frame(hand: bpy.types.Object, wrist: Vector, distal: Vector,
                 palm_n: Vector) -> None:
    """Rotates and translates the hand so distal → +X, palm normal → −Z, and scales it.

    The target frame is the T-pose left hand: the arm runs out along +X, the palm faces
    the floor, and the thumb therefore points forward, which in this project is −Y
    (``gen_player_model``'s axis note: the model faces −Y and its LEFT is +X).
    """
    across = distal.cross(palm_n).normalized()
    src = Matrix((distal, palm_n, across)).transposed()      # columns = the source axes
    dst = Matrix(((1.0, 0.0, 0.0), (0.0, 0.0, 1.0), (0.0, -1.0, 0.0)))
    # dst's columns are (+X, −Z, +Y): distal → +X, palm normal → −Z, across → +Y.
    rot = dst @ src.transposed()
    me = hand.data
    me.transform(Matrix.Translation(-wrist))
    me.transform(rot.to_4x4())
    me.update()


def trim_wrist(hand: bpy.types.Object) -> None:
    """Cuts the hand off at the wrist plane, drops the debris and closes the stump.

    The sculpt is flayed — ``gen_monster_ai`` calls the vessel *"something that used to
    be a person"* and models torn skin at every joint — so the harvested hand arrives
    with loose flaps hanging off its wrist that render as shards floating beside the
    forearm. They also break the size measurements: they reach further back than the
    wrist does, so the hand measures longer than it is and is then scaled down to
    compensate.

    Cut at x = 0, which after ``_apply_frame`` is the centroid of the wrist-end slab and
    therefore the wrist by construction; keep the largest island, which is the hand;
    fill the boundary, because an open mesh is a hole in the player the first time the
    sleeve rides up.
    """
    bm = bmesh.new()
    bm.from_mesh(hand.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                           plane_co=Vector((WRIST_CUT, 0.0, 0.0)),
                           plane_no=Vector((1.0, 0.0, 0.0)),
                           clear_inner=True)
    keep = _largest_island(bm)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v not in keep], context="VERTS")
    # Filled repeatedly rather than once: `holes_fill` closes a simple loop and leaves
    # anything self-touching open, and the sculpt has a few of those where a torn skin
    # flap meets the wrist. Each pass simplifies what is left.
    for _ in range(3):
        holes = [e for e in bm.edges if len(e.link_faces) < 2]
        if not holes:
            break
        bmesh.ops.holes_fill(bm, edges=holes, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    remaining = [e for e in bm.edges if len(e.link_faces) < 2]
    bm.to_mesh(hand.data)
    bm.free()
    hand.data.update()
    if remaining:
        blendkit.fail(f"the harvested hand still has {len(remaining)} boundary edge(s) after "
                      "the stump was filled. An open mesh renders as a hole into the inside "
                      "of the player's own arm, 35 cm from the camera.")
    print(f"HAND_TRIM verts={len(hand.data.vertices)} closed=yes")


WRIST_RADIUS = 0.030
"""Radius the harvested hand is squeezed into at the wrist, before scaling, in metres.

**The vessel's wrist is torn open and the flaps are geometry.** Measured, the hand's
cross-section 8 mm past the wrist is 86 mm across where a wrist is about 56, and the
excess is hanging skin: in first person it read as a crumpled paper collar round both
wrists, which was the single most obviously wrong thing in the frame.

Pulling those vertices *in* rather than deleting them is what makes this safe. Deleting
opens holes and eroding by boundary takes fingertips with it — both were tried. Clamping
the radius changes no topology at all: the flaps land on the wrist's surface and become
tendons, which is what they should have been.
"""

TAPER_LENGTH = 0.016
"""Metres over which the wrist clamp releases back to the hand's own shape.

Short, and the limit is the **thumb**, not the knuckles: its base sits 20 mm past the
wrist, and a taper that reached it squeezed the thenar eminence into the wrist and took
2 cm off the thumb's measured length. The flaps are all inside the first centimetre
anyway.
"""


def taper_wrist(hand: bpy.types.Object) -> None:
    """Squeezes the wrist end of the harvested hand into a cylinder. See ``WRIST_RADIUS``."""
    palm = [v.co for v in hand.data.vertices
            if TAPER_LENGTH <= v.co.x <= TAPER_LENGTH + 0.014]
    if not palm:
        blendkit.fail("no geometry at the far end of the wrist taper, so there is nothing "
                      "to release it into.")
    cy = (max(p.y for p in palm) + min(p.y for p in palm)) * 0.5
    cz = (max(p.z for p in palm) + min(p.z for p in palm)) * 0.5
    palm_r = max(math.hypot(p.y - cy, p.z - cz) for p in palm)

    moved = 0
    for vertex in hand.data.vertices:
        p = vertex.co
        if p.x > TAPER_LENGTH:
            continue
        t = _smoothstep(0.0, TAPER_LENGTH, max(0.0, p.x))
        limit = WRIST_RADIUS + (palm_r - WRIST_RADIUS) * t
        dy, dz = p.y - cy, p.z - cz
        radius = math.hypot(dy, dz)
        if radius <= limit or radius < 1e-9:
            continue
        scale = limit / radius
        vertex.co = Vector((p.x, cy + dy * scale, cz + dz * scale))
        moved += 1
    hand.data.update()
    print(f"WRIST_TAPER pulled {moved} vertices onto a {WRIST_RADIUS * 2000:.0f} mm wrist "
          f"releasing to {palm_r * 2000:.0f} mm over {TAPER_LENGTH * 1000:.0f} mm")


def orient_hand(hand: bpy.types.Object, wrist_z: float) -> dict:
    """Puts the harvested hand in the T-pose, at human scale, wrist at the origin.

    Returns the measurements the rest of the file needs: the hand's length, the palm
    plane, and which way the thumb came out — which is also the test that settles the
    palm normal's sign.
    """
    wrist, distal, palm_n, _ = hand_frame(hand, wrist_z)
    _apply_frame(hand, wrist, distal, palm_n)
    trim_wrist(hand)
    taper_wrist(hand)

    # Scale to a human hand. Measured along the hand's own axis after orientation, so a
    # sculpt whose fingers are curled is still scaled by its real reach rather than by a
    # bounding box that happens to be short.
    pts = [v.co for v in hand.data.vertices]
    length = max(p.x for p in pts) - min(p.x for p in pts)
    hand.data.transform(Matrix.Scale(HAND_LENGTH / length, 4))
    hand.data.update()

    thumb_y = _thumb_side(hand)
    if thumb_y > 0.0:
        # The other candidate frame: palm up, thumb back. Rotating 180° about the hand's
        # own axis is the only difference between them and it preserves handedness, which
        # mirroring would not — a mirrored left hand is a right hand and no amount of
        # posing recovers from that.
        hand.data.transform(Matrix.Rotation(math.pi, 4, "X"))
        hand.data.update()
        thumb_y = _thumb_side(hand)
        if thumb_y > 0.0:
            blendkit.fail("neither orientation of the harvested hand puts the thumb forward. "
                          "The thumb was not found where the palm normal says it should be, so "
                          "the frame is wrong and the hand would ship rolled.")

    # X is left alone: `_apply_frame` already put the wrist crease at the origin and the
    # forearm stub behind it, and every station downstream — the cuff, the digit sweep,
    # `_hand_world` — is measured from that plane.
    pts = [v.co for v in hand.data.vertices]
    palm = [p for p in pts if 0.0 <= p.x < HAND_LENGTH * 0.30]
    centre = sum(palm, Vector((0, 0, 0))) / max(1, len(palm))
    hand.data.transform(Matrix.Translation(Vector((0.0, -centre.y, -centre.z))))
    hand.data.update()

    pts = [v.co for v in hand.data.vertices]
    out = {
        "length": max(p.x for p in pts),
        "width": max(p.y for p in pts) - min(p.y for p in pts),
        "thickness": max(p.z for p in pts) - min(p.z for p in pts),
        "thumb_y": thumb_y,
    }
    print(f"HAND_ORIENTED length={out['length']:.4f}m width={out['width']:.4f}m "
          f"thickness={out['thickness']:.4f}m thumb_y={out['thumb_y']:+.4f}m")
    return out


def _thumb_side(hand: bpy.types.Object) -> float:
    """Signed Y of the lobe sticking out of the palm nearest the wrist — the thumb.

    Taken over the proximal half of the hand only, and that restriction is the whole
    method. Distally the four fingers occupy both sides of the palm and their spread
    swamps everything; proximally the hand is a single block with exactly one lobe on
    it, and that lobe is the thenar eminence. Returns the signed distance from the
    median Y to whichever tail reaches further, so the sign is the side the thumb is on.
    """
    pts = [v.co for v in hand.data.vertices]
    x0, x1 = min(p.x for p in pts), max(p.x for p in pts)
    band = [p for p in pts if x0 + (x1 - x0) * 0.10 <= p.x <= x0 + (x1 - x0) * 0.45]
    if not band:
        return 0.0
    ys = sorted(p.y for p in band)
    mid = ys[len(ys) // 2]
    above, below = ys[-1] - mid, mid - ys[0]
    return above if above > below else -below


def subdivide_smooth(obj: bpy.types.Object, cuts: int = 1) -> int:
    """One smoothed subdivision. Returns the new triangle count.

    The sculpt only spends **745 triangles** on a whole hand, measured after welding, and
    that is not enough for the one surface §05 puts 0.35 m from the camera: at that range
    a 1280-wide frame at 80° FOV resolves 0.43 mm per pixel, so a 6 mm facet across a
    finger is fourteen pixels of flat shading and the knuckles read as chamfers.

    Subdividing with ``smooth=1.0`` and then collapsing back to the budget is not a way of
    spending more triangles — the count afterwards is set by ``HAND_TRIS`` either way. It
    is a way of spending them on a *rounder surface*: the subdivision moves every new
    vertex onto the limit surface of the original, and collapse decimation then keeps the
    silhouette of the rounded form rather than the silhouette of the faceted one.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts,
                              use_grid_fill=True, smooth=1.0,
                              smooth_falloff="SMOOTH")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def decimate_to(obj: bpy.types.Object, target: int) -> int:
    """Collapses to a triangle budget, baking the result. Returns the triangle count."""
    obj.data.calc_loop_triangles()
    have = len(obj.data.loop_triangles)
    if have > target:
        mod = obj.modifiers.new("Budget", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = target / have
        mod.use_collapse_triangulate = True
        depsgraph = bpy.context.evaluated_depsgraph_get()
        baked = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
        old = obj.data
        obj.data = baked
        obj.modifiers.remove(mod)
        bpy.data.meshes.remove(old)
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


# ── Segmenting the digits ───────────────────────────────────────────────────


def _islands_beyond(mesh: bpy.types.Mesh, keep) -> list[list[int]]:
    """Edge-connected groups of the vertices ``keep`` accepts, as lists of mesh indices.

    Connectivity, not proximity, and that distinction is the whole method. The first
    attempt clustered the vertices past a cut plane by distance in the (y, z) projection
    and it could not be made to work at any tolerance: the sweep found 1, 2, 3, 5 and 6
    groups but never a stable 4, because a relaxed hand's fingers *overlap in projection*
    — the little finger curls under the ring finger and the two share a column. Two
    fingers are two separate volumes whatever they do in projection, so the mesh's own
    edges answer the question exactly and with nothing to tune.
    """
    chosen = [i for i, v in enumerate(mesh.vertices) if keep(v.co)]
    inside = set(chosen)
    adjacency: dict[int, list[int]] = {i: [] for i in chosen}
    for edge in mesh.edges:
        a, b = edge.vertices
        if a in inside and b in inside:
            adjacency[a].append(b)
            adjacency[b].append(a)

    seen: set[int] = set()
    groups: list[list[int]] = []
    for start in chosen:
        if start in seen:
            continue
        stack = [start]
        seen.add(start)
        group: list[int] = []
        while stack:
            v = stack.pop()
            group.append(v)
            for other in adjacency[v]:
                if other not in seen:
                    seen.add(other)
                    stack.append(other)
        groups.append(group)
    return groups


def segment_digits(hand: bpy.types.Object, size: dict) -> list[dict]:
    """Finds the five digits and returns a base/tip axis for each.

    Two different cuts, because the thumb is not a fifth finger. The four fingers leave
    the palm together at the knuckle line and are separate volumes past it, so a plane
    across the hand isolates them by connectivity alone. The thumb leaves the palm 40 %
    further back and on the other axis, so it is found on a plane along the hand instead.

    Everything is expressed as fractions of the hand's own measured length and width, so
    the numbers survive a different sculpt; the counts are asserted rather than assumed,
    which is the whole reason to do it by connectivity instead of by hand-written
    coordinates.
    """
    length = size["length"]
    co = [v.co.copy() for v in hand.data.vertices]

    # ── four fingers ────────────────────────────────────────────────────────
    # The cut plane is swept rather than fixed. Where the fingers separate depends on how
    # curled the sculpt's hand is, and the right plane is simply the one that leaves four
    # islands — asserted, not assumed, which is what makes this a measurement of the
    # sculpt rather than a set of coordinates that happen to suit one. The lowest such
    # plane is taken, so each finger keeps as much of its own knuckle as the separation
    # allows.
    best = None
    tried = []
    steps = 34
    for step in range(steps + 1):
        cut = length * (0.40 + 0.42 * step / steps)
        islands = [g for g in _islands_beyond(hand.data, lambda p, c=cut: p.x >= c)
                   if len(g) >= 6]
        tried.append((cut / length, len(islands)))
        if not 1 <= len(islands) <= 4 or len(islands) == 1:
            continue
        groups = _split_touching(islands, co)
        if len(groups) == 4 and best is None:
            best = (cut, groups)

    if best is None:
        blendkit.fail(
            "no plane across the harvested hand yields four fingers, before or after "
            "separating the ones that touch. Islands per cut: "
            + " ".join(f"{f:.2f}:{n}" for f, n in tried) + ". That sweep is the "
            "measurement that decides whether this sculpt is worth harvesting at all: a "
            "hand whose digits are not distinguishable volumes is a mitten, and building "
            "one of those was already possible without importing anything.")

    cut_x, groups = best
    print(f"DIGIT_CUT at {cut_x / length:.2f} of the hand's length; islands per cut "
          + " ".join(f"{f:.2f}:{n}" for f, n in tried))

    # ── the thumb ───────────────────────────────────────────────────────────
    # The thumb does not leave the hand on the finger cut's axis — it branches off the
    # palm 40 % further back and across it — so it is cut on Y instead and then taken by
    # connectivity for the same reason as above. The gate sits halfway between the palm's
    # own front face and the thumb's reach, both read off this hand's Y distribution.
    prox = [i for i in range(len(co)) if co[i].x < cut_x]
    ys = sorted(co[i].y for i in prox)
    gate = (ys[int(len(ys) * 0.04)] + ys[int(len(ys) * 0.32)]) * 0.5
    lobes = _islands_beyond(hand.data, lambda p: p.x < cut_x and p.y <= gate)
    thumb = max(lobes, key=len) if lobes else []
    if len(thumb) < 12:
        blendkit.fail(f"only {len(thumb)} vertices lie forward of the palm on the proximal "
                      "half of the hand, so there is no thumb to oppose. §03's grip on the "
                      "torch and §08's grip on a 전리품 are both a thumb closing on four "
                      "fingers; without one the hand cannot hold anything.")

    digits = [_digit_axis(g, co, cut_x, length) for g in groups]
    digits.sort(key=lambda d: d["tip"].y)          # thumb side (−Y) first
    digits.insert(0, _digit_axis(thumb, co, None, length, thumb=True))
    for digit in digits:
        reach = digit["tip"] - digit["base"]
        span = reach.length_squared
        starts = [(co[i] - digit["base"]).dot(reach) / span for i in digit["indices"]]
        digit["u0"] = max(0.0, min(starts)) if starts else 0.0

    for name, d in zip(DIGIT_NAMES, digits):
        d["name"] = name
        print(f"DIGIT {name:7s} verts={d['n']:4d} base=({d['base'].x:+.3f},"
              f"{d['base'].y:+.3f},{d['base'].z:+.3f}) "
              f"tip=({d['tip'].x:+.3f},{d['tip'].y:+.3f},{d['tip'].z:+.3f}) "
              f"length={(d['tip'] - d['base']).length * 100:5.1f}cm")
    return digits


def _spread(indices, co) -> tuple[float, Vector, Vector]:
    """Extent of a digit island across the hand, plus the axis and centre it was on.

    Measured in the (y, z) plane — across and through the palm — because that is the
    plane fingers are side by side in whatever they are doing along their own length.
    Returns (extent, unit axis in y/z as a 3-vector with x = 0, centroid).
    """
    pts = [co[i] for i in indices]
    centre = _mean(pts)
    syy = szz = syz = 0.0
    for p in pts:
        dy, dz = p.y - centre.y, p.z - centre.z
        syy += dy * dy
        szz += dz * dz
        syz += dy * dz
    n = float(len(pts))
    syy, szz, syz = syy / n, szz / n, syz / n
    tr, det = syy + szz, syy * szz - syz * syz
    disc = max(0.0, tr * tr * 0.25 - det) ** 0.5
    lam = tr * 0.5 + disc
    axis = (Vector((0.0, lam - szz, syz)).normalized() if abs(syz) > 1e-12
            else Vector((0.0, 1.0, 0.0)) if syy >= szz else Vector((0.0, 0.0, 1.0)))
    projected = [(p - centre).dot(axis) for p in pts]
    return max(projected) - min(projected), axis, centre


def _split_touching(islands, co) -> list[list[int]]:
    """Splits any island wide enough to be two fingers touching, into that many.

    **This sculpt's fingers are not all separate volumes and pretending otherwise was
    the first version's mistake.** Swept across its whole length the cut plane never
    leaves more than three islands, because two of the four fingers are modelled in
    contact and welded there. Rejecting the sculpt on that would throw away the only
    hand in the repository with knuckles in it.

    So the width decides, against the narrowest island in the same cut rather than
    against a typed millimetre: one finger is one finger wide, and an island 1.9 times
    that is two fingers. Splitting is 1-D k-means along the island's own principal axis
    across the palm — which is where two touching fingers differ and along which one
    finger has no structure at all.
    """
    measured = [(_spread(g, co), g) for g in islands]
    unit = min(s[0] for s, _ in measured)
    out: list[list[int]] = []
    for (extent, axis, centre), group in measured:
        parts = max(1, min(4, int(round(extent / max(unit, 1e-9)))))
        if parts == 1:
            out.append(group)
            continue
        keyed = sorted(group, key=lambda i: (co[i] - centre).dot(axis))
        # Equal-count slices rather than equal-width: fingers taper, so the outer one
        # of a touching pair covers less width and a width split cuts through it.
        step = len(keyed) / parts
        for k in range(parts):
            out.append(keyed[int(k * step):int((k + 1) * step)])
    return out


def _mean(points) -> Vector:
    total = Vector((0.0, 0.0, 0.0))
    for p in points:
        total = total + p
    return total / max(1, len(points))


def _digit_axis(indices, co, cut_x, length: float, thumb: bool = False) -> dict:
    """A base → tip segment for one digit, from the vertices that belong to it."""
    pts = [co[i] for i in indices]
    if thumb:
        # The thumb's own direction, not a guess: the far end of its point cloud against
        # the near end, ordered along the diagonal a relaxed thumb actually lies on. A
        # thumb pinned to −Y would have its bones outside its own geometry, and skinning
        # by distance to a bone that misses the mesh weights the palm instead.
        along = sorted(pts, key=lambda p: p.x - p.y)
        take = max(3, len(along) // 6)
        base, tip = _mean(along[:take]), _mean(along[-take:])
    else:
        along = sorted(pts, key=lambda p: p.x)
        tip = _mean(along[-max(3, len(along) // 8):])
        base = _mean(along[: max(3, len(along) // 8)])
        # The knuckle sits proximal of the plane that separated the fingers: that plane is
        # chosen for separation, and a bone starting on it would leave the knuckle welded
        # to the palm, so a curl would fold the finger a centimetre too far out.
        base.x = cut_x - length * 0.075
    return {"n": len(indices), "indices": list(indices), "base": base, "tip": tip}


# ── Skinning and relaxing the hand ──────────────────────────────────────────

KNUCKLE_BLEND = 0.20
"""Fraction of a digit's length over which it hands back to the palm. Below this the
metacarpal head — the knuckle you punch with — swings with the finger, and the back of
a closing hand loses the row of four bumps that is most of what says *fist*."""

WRIST_BLEND = 0.030
"""Metres over which the palm hands back to the forearm. A wrist has no crease in the
skin at the joint, so a hard boundary here creases one in."""


def _segment_u(p: Vector, a: Vector, b: Vector) -> tuple[float, float]:
    """Parameter along a→b and distance to it, clamped to the segment."""
    ab = b - a
    denom = ab.length_squared
    if denom < 1e-12:
        return 0.0, (p - a).length
    t = (p - a).dot(ab) / denom
    clamped = max(0.0, min(1.0, t))
    return t, (p - (a + ab * clamped)).length


def _smoothstep(lo: float, hi: float, x: float) -> float:
    if hi <= lo:
        return 0.0 if x < lo else 1.0
    t = max(0.0, min(1.0, (x - lo) / (hi - lo)))
    return t * t * (3.0 - 2.0 * t)


def skin_hand(hand: bpy.types.Object, digits: list[dict], side: str) -> list[dict]:
    """Per-vertex bone weights for the harvested hand, from the digit segmentation.

    **Scoped by the segmentation rather than by distance alone**, which is the difference
    between this and ``gen_monster_ai.skin_by_proximity``. That function has to guess
    which limb a vertex belongs to, and its own note records the failure mode — a thigh
    picking up the opposite leg. Here the digits are already known exactly, so a vertex
    on the index finger is only ever weighted to the index chain and the palm. Two
    adjacent fingertips are 6 mm apart on this hand and inverse-distance weighting cannot
    keep them apart at that spacing: the ring finger would drag the little one halfway
    through every grip.

    The two blends that are not "one bone, weight 1" are the two joints a hand actually
    has skin over — see ``KNUCKLE_BLEND`` and ``WRIST_BLEND``.
    """
    thumb = next(d for d in digits if d["name"] == "Thumb")
    fingers = [d for d in digits if d["name"] != "Thumb"]
    in_thumb = set(thumb["indices"])

    weights: list[dict[str, float]] = []
    for i, vertex in enumerate(hand.data.vertices):
        p = vertex.co
        candidates = [thumb] if i in in_thumb else fingers

        # Inverse distance to each candidate digit's axis, sharply weighted. **Not a hard
        # assignment**, and that is the fix for the worst defect this pass produced: two of
        # this sculpt's fingers are modelled in contact, `_split_touching` divides that
        # shared volume down the middle, and giving each half weight 1 on a different bone
        # shears the surface apart the moment the hand closes. Twenty millimetres apart and
        # at this exponent a separate finger's neighbour contributes about five parts per
        # million, so nothing blends that should not; where two fingers genuinely touch,
        # the blend is exactly what keeps the skin between them whole.
        share = []
        for digit in candidates:
            u, d = _segment_u(p, digit["base"], digit["tip"])
            share.append((1.0 / (d + 0.004) ** 6, max(0.0, min(1.0, u)), digit))
        total = sum(x for x, _, _ in share)
        share = [(x / total, u, digit) for x, u, digit in share]
        lead = max(share)[2]
        u = sum(x * u for x, u, _ in share)

        # The palm ramp starts where the digit's own geometry starts, not at its bone's
        # head. The segmentation cut runs 14 % of the way up a finger, so a ramp from zero
        # gives the first digit vertex 79 % finger weight while the palm vertex sharing an
        # edge with it has none — a 79 % step across one edge, which is a tear.
        start = lead.get("u0", 0.0)
        to_palm = 1.0 - _smoothstep(start, start + KNUCKLE_BLEND, u)
        distal = _smoothstep(DIGIT_SPLIT - 0.14, DIGIT_SPLIT + 0.14, u)

        w: dict[str, float] = {}
        if to_palm > 1e-4:
            # One ramp across the wrist: all forearm at the sleeve's cut, half and half on
            # the crease, all hand a wrist's width past it.
            arm = to_palm * (1.0 - _smoothstep(-0.004, WRIST_BLEND, p.x))
            w[f"{side}Hand"] = to_palm - arm
            if arm > 1e-4:
                w[f"{side}LowerArm"] = arm
        for x, _, digit in share:
            hold = x * (1.0 - to_palm)
            if hold <= 1e-4:
                continue
            name = digit["name"]
            w[f"{side}{name}Proximal"] = w.get(f"{side}{name}Proximal", 0.0) \
                + hold * (1.0 - distal)
            w[f"{side}{name}Intermediate"] = w.get(f"{side}{name}Intermediate", 0.0) \
                + hold * distal
        weights.append({k: v for k, v in w.items() if v > 1e-4})

    for w in weights:
        total = sum(w.values())
        for k in w:
            w[k] /= total
    return smooth_weights(hand.data, weights)


WEIGHT_SMOOTHING = 4
"""Laplacian passes over the hand's weights. **Not polish — without it the hand tears.**

The segmentation assigns every vertex to exactly one digit, which is right, and then
every vertex on a boundary between two digits jumps from weight 1 on one bone to weight 1
on the other across a single edge. Where two of this sculpt's fingers are modelled in
contact (see ``_split_touching``) that boundary runs down the middle of a solid volume,
and the first render showed the result exactly: the closed hand came apart into shards.

Smoothing over the **mesh's own edges** rather than over distance is what makes this
safe. Two separate fingers share no edge, so no amount of smoothing bleeds the index into
the middle; the places it does reach are the webbing, the knuckle line and the seam
between two touching fingers — which are the three places a hand's skin genuinely does
belong to two bones at once.
"""


def smooth_weights(mesh: bpy.types.Mesh, weights: list[dict]) -> list[dict]:
    """Averages each vertex's weights with its edge neighbours', then renormalises."""
    neighbours: list[list[int]] = [[] for _ in range(len(mesh.vertices))]
    for edge in mesh.edges:
        a, b = edge.vertices
        neighbours[a].append(b)
        neighbours[b].append(a)

    for _ in range(WEIGHT_SMOOTHING):
        out = []
        for i, w in enumerate(weights):
            blended = {k: v * 0.45 for k, v in w.items()}
            share = 0.55 / max(1, len(neighbours[i]))
            for j in neighbours[i]:
                for k, v in weights[j].items():
                    blended[k] = blended.get(k, 0.0) + v * share
            total = sum(blended.values())
            out.append({k: v / total for k, v in blended.items() if v > 1e-3})
        weights = out

    for w in weights:
        total = sum(w.values())
        for k in w:
            w[k] /= total
    return weights


DIGIT_SPLIT = 0.52
"""Where a digit's proximal bone hands over to its intermediate one, as a fraction of
the digit's length. A finger's proximal phalanx is about half of it and the middle and
distal together are the other half; with the distal dropped (see FINGERS) the
intermediate bone stands in for both, so the split stays near the middle."""


def relax_hand(hand: bpy.types.Object, digits: list[dict], weights: list[dict],
               side: str) -> None:
    """Brings the sculpt's splayed fingers together and opens the thumb, in the rest mesh.

    The sculpt's hand hangs at the end of a dead arm with its four fingers **fanned 36 mm
    apart at the tips against 26 mm at the knuckles**, and its thumb lying flat alongside
    the index finger rather than opposing it. Measured on the harvest; it is a corpse's
    hand and it reads as one.

    Applied to the rest mesh rather than to every clip because the rest pose is what
    Unity's Humanoid mapper sees, what a dropped finger curve falls back to, and what
    the ``.glb`` shows — three places a splayed hand would leak through. It is applied
    **through the skin weights**, so the knuckles deform instead of shearing, and the
    bones are moved by the same rotation so they stay inside the geometry they drive.
    """
    rotations = {}
    for digit in digits:
        base, tip = digit["base"], digit["tip"]
        reach = tip - base
        if digit["name"] == "Thumb":
            # Abduction and opposition. A thumb that lies along the fingers cannot close
            # on anything, and §03's torch, §08's 전리품 and §03's objective are all a
            # thumb closing on four fingers.
            rot = (Matrix.Rotation(math.radians(-THUMB_ABDUCT), 3, "Z")
                   @ Matrix.Rotation(math.radians(THUMB_OPPOSE), 3, "X"))
        else:
            # Rotate the fan out of the finger: the tip lands at the same Y as its own
            # knuckle, so the four run parallel. Then a shared flexion, because a
            # relaxed hand is not flat.
            yaw = math.atan2(reach.y, max(reach.x, 1e-6))
            rot = (Matrix.Rotation(-yaw, 3, "Z")
                   @ Matrix.Rotation(math.radians(FINGER_FLEX), 3, "Y"))
        rotations[digit["name"]] = (rot, base.copy())
        digit["tip"] = base + rot @ reach

    for i, vertex in enumerate(hand.data.vertices):
        p = vertex.co.copy()
        moved = Vector((0.0, 0.0, 0.0))
        for bone, w in weights[i].items():
            name = _digit_of(bone, side)
            if name is None:
                moved += p * w
            else:
                rot, base = rotations[name]
                moved += (base + rot @ (p - base)) * w
        vertex.co = moved
    hand.data.update()


THUMB_ABDUCT = 26.0
"""Degrees the thumb swings away from the index finger in the palm's plane."""

THUMB_OPPOSE = 34.0
"""Degrees the thumb rolls so its pad faces the fingers rather than the floor. Without
it the thumb is a fifth finger and a grip has nothing on the far side of the barrel."""

FINGER_FLEX = 9.0
"""Degrees of flexion in the rest pose. A live hand at rest is never flat; a flat one
photographs as a plank, which is the defect this whole file exists to fix."""


def _digit_of(bone: str, side: str) -> str | None:
    """The digit a bone belongs to, or None for the palm and the forearm."""
    if not bone.startswith(side):
        return None
    tail = bone[len(side):]
    for name in DIGIT_NAMES:
        if tail.startswith(name):
            return name
    return None


def harvest_hand(path: str) -> dict:
    """The whole harvest: sculpt in, an oriented human hand and five digit axes out."""
    sculpt = load_sculpt(path)
    landmarks = monster_fit.measure(sculpt)
    print(f"SCULPT_FIT height={landmarks['height']:.3f} shoulder={landmarks['shoulder']:.3f} "
          f"wrist={landmarks['wrist']:.3f} fingertip={landmarks['fingertip']:.3f} "
          f"knee={landmarks['knee']:.3f} arm_drop="
          f"{landmarks['shoulder'] - landmarks['wrist']:.3f}m")

    hand = cut_out_hand(sculpt, landmarks)
    bpy.data.objects.remove(sculpt, do_unlink=True)

    raw = decimate_to(hand, 10 ** 9)
    size = orient_hand(hand, landmarks["wrist"])
    dense = subdivide_smooth(hand)
    tris = decimate_to(hand, HAND_TRIS)
    print(f"HAND_BUDGET tris={raw}->{dense}(subdivided)->{tris} "
          f"verts={len(hand.data.vertices)}")

    digits = segment_digits(hand, size)
    weights = skin_hand(hand, digits, "Left")
    relax_hand(hand, digits, weights, "Left")

    # Re-scaled after relaxing, not before. The subdivision pulls the surface onto its own
    # limit shape and the relax swings four fingers, so the reach measured on the raw cut
    # is not the reach that ships — and the T-pose span, which ``verify_span`` holds to a
    # human ratio, is the wrist plus exactly this number.
    reach = max(v.co.x for v in hand.data.vertices)
    hand.data.transform(Matrix.Scale(HAND_LENGTH / reach, 4))
    hand.data.update()
    for digit in digits:
        digit["base"] *= HAND_LENGTH / reach
        digit["tip"] *= HAND_LENGTH / reach
    size["length"] = HAND_LENGTH
    for name, digit in zip(DIGIT_NAMES, digits):
        print(f"DIGIT_RELAXED {name:7s} tip=({digit['tip'].x:+.3f},{digit['tip'].y:+.3f},"
              f"{digit['tip'].z:+.3f})")
    thumb = next(d["indices"] for d in digits if d["name"] == "Thumb")
    size["sections"] = hand_sections(hand, exclude=set(thumb))
    for at, sec in sorted(size["sections"].items()):
        print(f"HAND_SECTION x={at * 1000:5.1f}mm  y={sec['y0'] * 1000:+6.1f}..{sec['y1'] * 1000:+6.1f} "
              f"z={sec['z0'] * 1000:+6.1f}..{sec['z1'] * 1000:+6.1f}")
    return {"object": hand, "size": size, "digits": digits, "tris": tris,
            "weights": weights, "landmarks": landmarks}


def stump_section(hand: bpy.types.Object) -> dict:
    """The wrist cut's own cross-section, taken from the geometry within 2 mm of it.

    Not a slab and not a guess: ``trim_wrist`` cut the hand on the plane x = 0 and this
    is the material sitting on that plane, so a cuff sized from it meets the hand's cap
    exactly. Sizing the cuff from an 8 mm slab instead measured 85 × 57 mm — the palm on
    an oblique section, not the wrist — and produced a flared collar that read as a torn
    cone in every first-person frame.
    """
    edge = min(v.co.x for v in hand.data.vertices)
    band = [v.co for v in hand.data.vertices if v.co.x <= edge + 0.009]
    if len(band) < 6:
        blendkit.fail(f"only {len(band)} vertices lie on the wrist cut, so the sleeve has "
                      "nothing to be joined to.")
    return {"y0": min(p.y for p in band), "y1": max(p.y for p in band),
            "z0": min(p.z for p in band), "z1": max(p.z for p in band)}


def hand_sections(hand: bpy.types.Object, exclude: set,
                  stations=(0.008, 0.026)) -> dict:
    """The hand's cross-section at a few distances out from the wrist, thumb excluded.

    The sleeve is built onto these rather than onto guessed radii. Guessing cost two
    rounds: a cuff sized by eye was wider than the palm it was supposed to disappear
    into, so the tube's open end showed as a black cavity at the wrist in every
    first-person frame, and a cuff *centred* on z = 0 poked through the back of a palm
    that arches 8 mm off that plane.

    **The thumb is excluded and that is the whole reason this takes an argument.** On
    this sculpt the thumb's base sits 3 mm from the wrist plane, so a section measured
    over every vertex reports the wrist as **106 mm wide** — the hand plus a thumb, not a
    wrist — and a cuff sized to swallow that is a bell. A thumb is supposed to come out of
    a sleeve.
    """
    out = {}
    for at in stations:
        band = [v.co for i, v in enumerate(hand.data.vertices)
                if i not in exclude and abs(v.co.x - at) <= 0.007]
        if not band:
            continue
        out[at] = {"y0": min(p.y for p in band), "y1": max(p.y for p in band),
                   "z0": min(p.z for p in band), "z1": max(p.z for p in band)}
    return out


# ── The rig ─────────────────────────────────────────────────────────────────


def bone_specs(harvest: dict) -> list[BoneSpec]:
    """``gen_player_model``'s 26 bones plus twenty finger bones fitted to the sculpt.

    The 26 are taken verbatim and that is deliberate rather than lazy: every arm and leg
    aim in ``gen_player_model``'s pose library was tuned against those exact joint
    positions, ``verify_motion`` measures §05's speeds and §12's crouch height against
    them, and moving one would invalidate nine clips to gain nothing. What changes here
    is what hangs off ``LeftHand`` and ``RightHand``.

    The finger bones are **placed by the harvest**, not typed: each digit's base and tip
    come out of ``segment_digits``, so a different sculpt puts them somewhere else and
    they are still inside their own geometry.
    """
    specs = list(gpm.bone_specs())
    for side in ("Left", "Right"):
        sign = 1.0 if side == "Left" else -1.0
        for digit in harvest["digits"]:
            base, tip = digit["base"], digit["tip"]
            mid = base + (tip - base) * DIGIT_SPLIT
            name = digit["name"]
            specs += [
                BoneSpec(f"{side}{name}Proximal", _hand_world(base, sign),
                         _hand_world(mid, sign), f"{side}Hand"),
                BoneSpec(f"{side}{name}Intermediate", _hand_world(mid, sign),
                         _hand_world(tip, sign), f"{side}{name}Proximal", True),
            ]
    return specs


def _hand_world(p: Vector, sign: float) -> tuple:
    """Hand-local (wrist at origin, distal +X) → armature space, mirroring for the right.

    A pure mirror in X, never a rotation: mirroring the left hand's *geometry* across X
    is what makes it a right hand, and the bones have to travel by the same map or they
    end up outside the mesh they drive.
    """
    return (sign * (gpm.WRIST_X + p.x), p.y, gpm.SHOULDER_Z + p.z)


FINGER_BONES = tuple(f"{side}{d}{j}" for side in ("Left", "Right")
                     for d in DIGIT_NAMES for j in ("Proximal", "Intermediate"))


# ── Geometry: the arms ──────────────────────────────────────────────────────


def cuff_rings(side: int, sections: dict):
    """The last three rings of the sleeve: a wrist that tapers and ends *inside* the palm.

    **Four rounds of renders went into this being three ordinary rings.** Every attempt to
    make the sleeve *meet* the hand failed for the same reason: a hanging hand's wrist cut
    is oblique and the thumb's base lies on it, so the hand's section at the wrist measures
    around 92 x 57 mm however it is taken. A cuff wide enough to enclose that is a flared
    collar; a cuff narrower than it leaves a slot into the inside of the arm; and a cuff
    that tries to do both spikes through the skin.

    Two closed surfaces that simply **interpenetrate** have none of those problems. The
    sleeve tapers to a real wrist, carries on 30 mm into the palm and is capped there, and
    the hand is closed in its own right. There is no seam to open because there is no
    seam: what a camera sees is the curve where two solids cross, which is what a wrist
    looks like. The only thing that has to be true is that the last ring is genuinely
    inside the hand, and that is why it is sized from the hand's own 26 mm section rather
    than chosen.
    """
    s = "Left" if side > 0 else "Right"
    palm = sections[0.026]
    py = (palm["y1"] - palm["y0"]) * 0.5
    pz = (palm["z1"] - palm["z0"]) * 0.5
    pcy = (palm["y1"] + palm["y0"]) * 0.5
    pcz = (palm["z1"] + palm["z0"]) * 0.5
    return [
        (gpm.WRIST_X - 0.056, 0.0, 0.0, 0.036, 0.033, {f"{s}LowerArm": 1.0}),
        # Sized off WRIST_RADIUS with 15 % of slack, not by eye: the taper leaves the
        # hand's wrist a cylinder of exactly that radius, and a sleeve narrower than it
        # lets the skin ring stand proud all the way round — which photographed as a
        # crumpled paper cuff on both wrists in first person.
        (gpm.WRIST_X - 0.008, 0.0, 0.0, WRIST_RADIUS * 1.15, WRIST_RADIUS * 1.10,
         {f"{s}LowerArm": 0.86, f"{s}Hand": 0.14}),
        (gpm.WRIST_X + 0.026, pcy * 0.55, pcz * 0.55, py * 0.44, pz * 0.52,
         {f"{s}Hand": 1.0}),                              # capped, inside the palm
    ]


def arm_rings(side: int):
    """Shoulder → wrist, as (x, half-depth in Y, half-height in Z, weights).

    Reshaped from the tube ``gen_player_model`` had. That one ran 0.075 → 0.030 in eight
    even steps, which is a cone: no deltoid, no bicep, no elbow, and a forearm that
    tapers the wrong way. This one has the four things an arm's silhouette is actually
    made of — the deltoid cap standing proud of the shoulder, the bicep swelling and
    falling to the elbow, the forearm swelling *below* the elbow and then narrowing hard
    into the wrist. §03 sweeps a hard spot along this limb and a cone shows every one of
    those absences as a smooth gradient where there should be a change of direction.
    """
    s = "Left" if side > 0 else "Right"
    return [
        (0.070, 0.078, 0.062, {"UpperChest": 1.0}),                       # inside the torso
        (0.150, 0.076, 0.082, {"UpperChest": 0.45, f"{s}Shoulder": 0.55}),
        (0.196, 0.068, 0.078, {f"{s}Shoulder": 0.40, f"{s}UpperArm": 0.60}),  # deltoid
        (0.262, 0.058, 0.062, {f"{s}UpperArm": 1.0}),                     # bicep
        (0.352, 0.052, 0.055, {f"{s}UpperArm": 1.0}),
        (0.442, 0.045, 0.048, {f"{s}UpperArm": 0.78, f"{s}LowerArm": 0.22}),
        (0.492, 0.043, 0.047, {f"{s}UpperArm": 0.22, f"{s}LowerArm": 0.78}),  # elbow
        (0.556, 0.047, 0.050, {f"{s}LowerArm": 1.0}),                     # forearm belly
        (0.640, 0.038, 0.041, {f"{s}LowerArm": 1.0}),
    ]


def build_arm(b: gpm.Body, side: int, harvest: dict) -> None:
    """One sleeve, and the harvested hand welded into its cuff."""
    rings = [(x, 0.0, 0.0, ru, rv, w) for x, ru, rv, w in arm_rings(side)]
    rings += cuff_rings(side, harvest["size"]["sections"])
    mats = [gpm.M_COVERALL] * (len(rings) - 1)
    mats[1] = gpm.M_ROLE            # the deltoid cap, above the shoulder line
    # Capped at both ends. The far cap sits inside the palm where nothing can see it, and
    # it is there because an *open* tube whose mouth strays a millimetre outside the hand
    # renders as a black hole at the wrist — which is what the first two rounds showed.
    b.shell([(gpm.ellipse((side * x, cy, gpm.SHOULDER_Z + cz), gpm.Y, gpm.Z, ru, rv,
                          gpm.SIDES_LIMB), w)
             for x, cy, cz, ru, rv, w in rings], mats)
    weld_hand(b, side, harvest)


def weld_hand(b: gpm.Body, side: int, harvest: dict) -> None:
    """Copies the harvested hand into the arms mesh at the wrist, weights and all.

    Fed through ``Body.vert``/``Body.face`` rather than joined as an object, because the
    material slot ORDER is a contract — §04 swaps ``renderer.materials[0]`` per role, and
    Blender's join merges slot lists in join order. The accumulator keeps SLOTS order by
    construction, so the hand cannot be the thing that shuffles it.

    The cuff overlaps the stump by design: the sleeve's last ring sits 6 mm past the
    wrist plane and the hand's own stump is closed, so there is a lap joint rather than a
    butt joint and no amount of wrist flexion opens a seam.
    """
    s = "Left" if side > 0 else "Right"
    mesh = harvest["object"].data
    weights = harvest["weights"]
    sign = 1.0 if side > 0 else -1.0

    remap: dict[int, int] = {}
    for i, vertex in enumerate(mesh.vertices):
        world = _hand_world(vertex.co, sign)
        w = {(name if side > 0 else _swap_side(name)): value
             for name, value in weights[i].items()}
        remap[i] = b.vert(Vector(world), w)

    for poly in mesh.polygons:
        idx = [remap[v] for v in poly.vertices]
        if side < 0:
            idx.reverse()       # the X mirror flips winding; the recalc would too, but
                                # doing it here keeps the mesh sane before it is welded
        b.face(tuple(idx), gpm.M_SKIN)


def _swap_side(bone: str) -> str:
    if bone.startswith("Left"):
        return "Right" + bone[4:]
    if bone.startswith("Right"):
        return "Left" + bone[5:]
    return bone


def build_arm_band(b: gpm.Body, side: int) -> None:
    """The role-coloured ring around one bicep. §04's colour has to survive a beam that
    only finds part of a teammate, so it is repeated on the helmet, the collar, the vest,
    the deltoid caps and here."""
    s = "Left" if side > 0 else "Right"
    b.shell([(gpm.ellipse((side * 0.288, 0, gpm.SHOULDER_Z), gpm.Y, gpm.Z, 0.056, 0.060, 8),
              {f"{s}UpperArm": 1.0}),
             (gpm.ellipse((side * 0.346, 0, gpm.SHOULDER_Z), gpm.Y, gpm.Z, 0.054, 0.057, 8),
              {f"{s}UpperArm": 1.0})], gpm.M_ROLE)


# ── Geometry: the body ──────────────────────────────────────────────────────


def torso_ring(z: float, rx: float, ry: float, sides: int, groove: float = 0.0,
               shoulder: float = 0.0):
    """One cross-section of a clothed torso — not an ellipse.

    Two departures, and both are shape a lofted cross-section can have and a convex hull
    cannot, which is the argument ``gen_monster_ai`` makes about the creature applied
    here at a scale that matters:

    * **groove** pulls the centre of the back in, so the spine is a valley between two
      erector ridges instead of the apex of an arc. Under §03's moving spot that valley
      is the only thing on a retreating teammate's back that changes with the beam.
    * **shoulder** squares the top rings off toward the deltoid line, because a clothed
      shoulder is a corner and an ellipse rounds it away — which is most of why the
      previous model read as a mannequin from behind.
    """
    out = []
    for i in range(sides):
        a = 2.0 * math.pi * (i + 0.5) / sides
        ca, sa = math.cos(a), math.sin(a)
        depth = ry
        if sa > 0.0:                       # +Y is behind the player
            depth *= 1.0 - groove * math.exp(-(ca / 0.42) ** 2)
        width = rx
        if shoulder > 0.0:
            width *= 1.0 + shoulder * (abs(ca) ** 3) * (1.0 - abs(sa) * 0.4)
        out.append(Vector((width * ca, depth * sa, z)))
    return out


def build_torso(b: gpm.Body) -> None:
    """Pelvis → waist → ribcage → shoulder yoke, with §04's colour on chest and back.

    Anthropometry for a 1.75 m person: 0.344 m shoulder breadth, 0.336 m hip breadth,
    and the waist the narrowest ring, which is what gives the silhouette a taper to read
    at the distance §12's corridors allow.
    """
    rings = [
        (0.815, 0.128, 0.098, 0.00, 0.00, {"Hips": 1.0}),
        (0.880, 0.161, 0.110, 0.05, 0.00, {"Hips": 1.0}),
        (0.950, 0.168, 0.112, 0.08, 0.00, {"Hips": 0.70, "Spine": 0.30}),
        (1.030, 0.143, 0.101, 0.10, 0.00, {"Hips": 0.25, "Spine": 0.75}),
        (1.100, 0.129, 0.097, 0.11, 0.00, {"Spine": 1.0}),
        (1.200, 0.147, 0.106, 0.10, 0.00, {"Spine": 0.40, "Chest": 0.60}),
        (1.310, 0.167, 0.118, 0.08, 0.06, {"Chest": 1.0}),
        (1.400, 0.174, 0.114, 0.06, 0.14, {"Chest": 0.30, "UpperChest": 0.70}),
        (1.452, 0.156, 0.104, 0.04, 0.10, {"UpperChest": 1.0}),
        (1.500, 0.101, 0.086, 0.02, 0.00, {"UpperChest": 1.0}),
    ]
    # Segments 4–6 (z 1.10 → 1.40) are §04's vest: chest AND the whole upper back, so a
    # role is readable from behind and from the side, not only head-on.
    seg = [gpm.M_COVERALL] * (len(rings) - 1)
    for i in (4, 5, 6):
        seg[i] = gpm.M_ROLE
    b.shell([(torso_ring(z, rx, ry, gpm.SIDES_BODY, groove, sh), w)
             for z, rx, ry, groove, sh, w in rings], seg)


HELMET_CROWN = 1.750
"""The top of the model, and therefore ``AssetImportPolicy.PlayerHeightMetres``. It is
the helmet rather than the skull: a worker underground wears one, and it puts §04's role
colour on the highest, largest, most-often-lit surface the model has."""


def build_neck_head(b: gpm.Body) -> None:
    """Neck, skull, brow, nose, a half-mask and the helmet over all of it.

    The previous model's head was a bald egg with a 12 mm nose, and at §04's 관측 range
    that is a pale blob. Three things change what a head does at distance and none of
    them is facial detail: **its outline**, **its value against the wall**, and **whether
    anything on it is the role colour**. So the skull loses 34 mm to make room for a
    helmet that supplies all three, and the face below the brow — which is the part that
    would need real modelling to look like anything — is covered by a respirator instead.
    A dust mask in a 요양원 basement is what the fiction wants anyway, and it costs 96
    triangles against the several hundred a face would.
    """
    neck = [
        (1.440, 0.060, 0.056, {"UpperChest": 1.0}),          # tucked inside the torso
        (1.498, 0.055, 0.052, {"UpperChest": 0.40, "Neck": 0.60}),
        (1.542, 0.050, 0.048, {"Neck": 1.0}),
        (1.582, 0.055, 0.052, {"Neck": 0.45, "Head": 0.55}),
    ]
    b.shell([(gpm.ellipse((0, -0.004 * i, z), gpm.X, gpm.Y, rx, ry, gpm.SIDES_NECK), w)
             for i, (z, rx, ry, w) in enumerate(neck)], gpm.M_SKIN)

    skull = [
        (1.572, -0.020, 0.057, 0.061),
        (1.610, -0.015, 0.070, 0.080),
        (1.648, -0.012, 0.076, 0.087),
        (1.686, -0.008, 0.073, 0.083),
        (1.712, -0.005, 0.050, 0.056),
        (1.722, -0.005, 0.022, 0.026),
    ]
    b.shell([(gpm.ellipse((0, y, z), gpm.X, gpm.Y, rx, ry, gpm.SIDES_BODY), {"Head": 1.0})
             for z, y, rx, ry in skull], gpm.M_SKIN)

    # Jaw: a wedge under the front of the skull. Without it the head is an egg, and an
    # egg reads as a mannequin however many sides it has.
    b.shell([(gpm.rect((0, -0.030, 1.618), gpm.X, gpm.Y, 0.050, 0.035), {"Head": 1.0}),
             (gpm.rect((0, -0.040, 1.590), gpm.X, gpm.Y, 0.041, 0.029), {"Head": 1.0}),
             (gpm.rect((0, -0.044, 1.572), gpm.X, gpm.Y, 0.027, 0.019), {"Head": 1.0})],
            gpm.M_SKIN)

    # Brow: a shelf over the eye line, so the beam catches a hard edge and the eyes sit
    # in shadow. The one piece of face left uncovered, and the one that carries facing.
    b.shell([(gpm.rect((0, -0.050, 1.658), gpm.X, gpm.Y, 0.060, 0.019), {"Head": 1.0}),
             (gpm.rect((0, -0.057, 1.645), gpm.X, gpm.Y, 0.056, 0.013), {"Head": 1.0})],
            gpm.M_SKIN)

    # Respirator: a half-mask over nose and mouth with a filter each side. Its outline is
    # what makes a head at 15 m read as a person in equipment rather than as a pale oval.
    b.shell([(gpm.rect((0, -0.048, 1.618), gpm.X, gpm.Z, 0.052, 0.030), {"Head": 1.0}),
             (gpm.rect((0, -0.078, 1.616), gpm.X, gpm.Z, 0.048, 0.028), {"Head": 1.0}),
             (gpm.rect((0, -0.096, 1.612), gpm.X, gpm.Z, 0.034, 0.020), {"Head": 1.0})],
            gpm.M_GEAR)
    for side in (1, -1):
        b.shell([(gpm.rect((side * 0.052, -0.062, 1.612), gpm.Y, gpm.Z, 0.022, 0.021),
                  {"Head": 1.0}),
                 (gpm.rect((side * 0.078, -0.062, 1.610), gpm.Y, gpm.Z, 0.019, 0.018),
                  {"Head": 1.0})], gpm.M_GEAR)

    # Helmet: role-coloured, brimmed, with a lamp bracket over the brow.
    shell = [
        (1.652, -0.006, 0.092, 0.103),
        (1.678, -0.006, 0.096, 0.107),
        (1.706, -0.005, 0.090, 0.100),
        (1.732, -0.004, 0.068, 0.076),
        (HELMET_CROWN, -0.004, 0.030, 0.034),
    ]
    b.shell([(gpm.ellipse((0, y, z), gpm.X, gpm.Y, rx, ry, gpm.SIDES_BODY), {"Head": 1.0})
             for z, y, rx, ry in shell], gpm.M_ROLE, cap_start=False)
    # Brim, forward only: a full circular brim reads as a hat and eats the silhouette.
    b.shell([(gpm.rect((0, -0.086, 1.660), gpm.X, gpm.Y, 0.086, 0.024), {"Head": 1.0}),
             (gpm.rect((0, -0.126, 1.664), gpm.X, gpm.Y, 0.062, 0.020), {"Head": 1.0})],
            gpm.M_ROLE)
    # Lamp bracket. §03 hands every player a torch and §05 makes it the pointing device;
    # the bracket says the helmet is equipment rather than a hat. Not a light source —
    # the beam lives on FlashlightMount, in the hand, where §05 puts it.
    b.shell([(gpm.rect((0, -0.090, 1.686), gpm.X, gpm.Z, 0.030, 0.016), {"Head": 1.0}),
             (gpm.rect((0, -0.112, 1.684), gpm.X, gpm.Z, 0.024, 0.013), {"Head": 1.0})],
            gpm.M_GEAR)


def leg_rings(side: int):
    s = "Left" if side > 0 else "Right"
    return [
        (0.950, 0.000, 0.079, 0.091, {"Hips": 0.60, f"{s}UpperLeg": 0.40}),
        (0.862, -0.004, 0.086, 0.093, {f"{s}UpperLeg": 1.0}),
        (0.700, -0.008, 0.075, 0.082, {f"{s}UpperLeg": 1.0}),
        (0.566, -0.012, 0.066, 0.072, {f"{s}UpperLeg": 0.78, f"{s}LowerLeg": 0.22}),
        (0.500, -0.014, 0.061, 0.067, {f"{s}UpperLeg": 0.22, f"{s}LowerLeg": 0.78}),
        (0.432, -0.004, 0.062, 0.074, {f"{s}LowerLeg": 1.0}),      # calf
        (0.330, 0.002, 0.056, 0.066, {f"{s}LowerLeg": 1.0}),
        (0.232, 0.008, 0.045, 0.052, {f"{s}LowerLeg": 1.0}),
        (0.150, 0.013, 0.041, 0.046, {f"{s}LowerLeg": 0.86, f"{s}Foot": 0.14}),
        (0.104, 0.015, 0.039, 0.044, {f"{s}LowerLeg": 0.50, f"{s}Foot": 0.50}),
    ]


def build_leg(b: gpm.Body, side: int) -> None:
    """Hip → ankle, then a work boot with a heel block and a toe cap.

    Every boot ring's centre sits at its own half-height, which puts the flat base of
    every ring on z = 0 exactly. That is load-bearing rather than tidy: every gait key in
    ``gen_player_model`` is ground-locked by dropping the hips until a sole contact
    reaches z = 0, and a curved sole leaves the boot's corners hanging on every frame.
    """
    s = "Left" if side > 0 else "Right"
    b.shell([(gpm.ellipse((side * gpm.LEG_X, y, z), gpm.X, gpm.Y, rx, ry, gpm.SIDES_LIMB), w)
             for z, y, rx, ry, w in leg_rings(side)], gpm.M_COVERALL)

    boot = [
        (0.092, 0.041, 0.058, {f"{s}Foot": 1.0}),      # heel counter, tall and narrow
        (0.058, 0.047, 0.055, {f"{s}Foot": 1.0}),
        (0.006, 0.051, 0.046, {f"{s}Foot": 1.0}),      # instep
        (-0.056, 0.050, 0.038, {f"{s}Foot": 1.0}),
        (gpm.BALL_Y, 0.048, 0.033, {f"{s}Foot": 0.55, f"{s}Toes": 0.45}),
        (-0.168, 0.043, 0.026, {f"{s}Toes": 1.0}),
        (-0.203, 0.028, 0.017, {f"{s}Toes": 1.0}),     # rounded toe cap
    ]
    b.shell([(gpm.flat_bottom((side * gpm.LEG_X, y, hz), gpm.X, gpm.Z, hx, hz), w)
             for y, hx, hz, w in boot], gpm.M_GEAR)


def build_gear(b: gpm.Body) -> None:
    """Belt, harness, backpack, boot cuffs, knee pads, thigh pouch and the radio.

    Every one of these is a little LARGER than the thing it rings: a strap modelled flush
    with a torso half-vanishes into it and the visible half z-fights. And every one is
    silhouette — §12 puts teammates 3–15 m away in a 22° cone, where a person is between
    sixty and two hundred pixels tall and the only thing that survives is the outline.
    """
    # Belt.
    b.shell([(gpm.ellipse((0, 0, 1.008), gpm.X, gpm.Y, 0.153, 0.114, gpm.SIDES_BODY),
              {"Hips": 0.5, "Spine": 0.5}),
             (gpm.ellipse((0, 0, 1.078), gpm.X, gpm.Y, 0.149, 0.112, gpm.SIDES_BODY),
              {"Spine": 1.0})], gpm.M_GEAR)

    # Harness: two shoulder straps over the vest and a chest strap between them. The
    # straps are what make the pack read as carried rather than as a lump on the back.
    for side in (1, -1):
        b.shell([(gpm.rect((side * 0.082, 0.098, 1.400), gpm.X, gpm.Y, 0.028, 0.011),
                  {"Chest": 0.5, "UpperChest": 0.5}),
                 (gpm.rect((side * 0.086, 0.010, 1.437), gpm.X, gpm.Y, 0.028, 0.010),
                  {"UpperChest": 1.0}),
                 (gpm.rect((side * 0.080, -0.096, 1.392), gpm.X, gpm.Y, 0.027, 0.011),
                  {"Chest": 0.6, "UpperChest": 0.4}),
                 (gpm.rect((side * 0.070, -0.112, 1.286), gpm.X, gpm.Y, 0.026, 0.010),
                  {"Chest": 1.0})], gpm.M_GEAR)
    b.shell([(gpm.rect((0, -0.116, 1.318), gpm.X, gpm.Z, 0.086, 0.014), {"Chest": 1.0}),
             (gpm.rect((0, -0.126, 1.316), gpm.X, gpm.Z, 0.082, 0.012), {"Chest": 1.0})],
            gpm.M_GEAR)

    # §08's 가방. It sits on BackpackMount's line and it is why §08 can sell a bag at all:
    # a +5 loot upgrade the other three cannot see is an upgrade nobody talks about.
    b.shell([(gpm.rect((0, 0.106, 1.240), gpm.X, gpm.Z, 0.132, 0.106), {"Chest": 1.0}),
             (gpm.rect((0, 0.196, 1.246), gpm.X, gpm.Z, 0.140, 0.112), {"Chest": 1.0}),
             (gpm.rect((0, 0.252, 1.244), gpm.X, gpm.Z, 0.118, 0.092), {"Chest": 1.0})],
            gpm.M_GEAR)
    # The pack's top flap takes the role colour, which is the only one of §04's five
    # marks visible from directly behind a walking teammate.
    b.shell([(gpm.rect((0, 0.112, 1.346), gpm.X, gpm.Y, 0.126, 0.006), {"Chest": 1.0}),
             (gpm.rect((0, 0.242, 1.352), gpm.X, gpm.Y, 0.112, 0.006), {"Chest": 1.0})],
            gpm.M_ROLE)

    for side in (1, -1):
        s = "Left" if side > 0 else "Right"
        b.shell([(gpm.ellipse((side * gpm.LEG_X, 0.008, 0.128), gpm.X, gpm.Y,
                              0.064, 0.071, gpm.SIDES_LIMB), {f"{s}LowerLeg": 1.0}),
                 (gpm.ellipse((side * gpm.LEG_X, 0.004, 0.248), gpm.X, gpm.Y,
                              0.060, 0.067, gpm.SIDES_LIMB), {f"{s}LowerLeg": 1.0})],
                gpm.M_GEAR)
        b.shell([(gpm.rect((side * gpm.LEG_X, -0.072, 0.540), gpm.X, gpm.Z, 0.049, 0.041),
                  {f"{s}UpperLeg": 0.6, f"{s}LowerLeg": 0.4}),
                 (gpm.rect((side * gpm.LEG_X, -0.088, 0.522), gpm.X, gpm.Z, 0.041, 0.033),
                  {f"{s}UpperLeg": 0.5, f"{s}LowerLeg": 0.5})], gpm.M_GEAR)

    # Thigh pouch, on the left so it never fouls §05's torch hand.
    b.shell([(gpm.rect((0.148, -0.026, 0.782), gpm.Y, gpm.Z, 0.052, 0.062),
              {"LeftUpperLeg": 1.0}),
             (gpm.rect((0.176, -0.026, 0.778), gpm.Y, gpm.Z, 0.046, 0.056),
              {"LeftUpperLeg": 1.0})], gpm.M_GEAR)

    # §13 gives every player proximity voice, and a set on the chest is the only thing on
    # the model that says so.
    b.shell([(gpm.rect((0.090, -0.106, 1.372), gpm.X, gpm.Z, 0.030, 0.034), {"Chest": 1.0}),
             (gpm.rect((0.090, -0.140, 1.368), gpm.X, gpm.Z, 0.024, 0.028), {"Chest": 1.0})],
            gpm.M_GEAR)


def build_collar(b: gpm.Body) -> None:
    """The role-coloured ring around the neck, standing above the shoulder line."""
    b.shell([(gpm.ellipse((0, -0.006, 1.486), gpm.X, gpm.Y, 0.075, 0.070, 8),
              {"UpperChest": 1.0}),
             (gpm.ellipse((0, -0.010, 1.542), gpm.X, gpm.Y, 0.070, 0.066, 8),
              {"UpperChest": 0.4, "Neck": 0.6})], gpm.M_ROLE)


# ── The grip, solved rather than typed ──────────────────────────────────────

GRIP_MCP = 70.0
GRIP_PIP = 88.0
"""Degrees of flexion at the two modelled finger joints when the fist is closed.

Anatomy, not taste: a metacarpophalangeal joint reaches about 90° and a proximal
interphalangeal about 110°, and a power grip on anything hand-sized uses most of both.
They are **inputs** here — the fist closes to these and the torch's handle is then
inscribed in the cavity that leaves (``inscribe_handle``), rather than the handle being
chosen and the fingers bent until they reach it. Solving it the other way round asks the
sculpt's 55 mm proximal phalanx to put its far end on a 24 mm circle, which has no
solution at any angle, and the generator said so.

**The torch had to become an angle-head lamp for any of this to exist, and the reason is
anatomy rather than taste.** Fingers flex about one axis: the knuckle line, across the
palm. Whatever a fist encircles therefore has its axis across the palm too. The previous
model held a straight barrel running *along* the arm, from the heel of the hand out past
the fingertips, and that is a shape no fist can close on. It survived review only because
the hand it passed through was a flat slab with four prongs, where nothing could be seen
to intersect anything.

A right-angle work lamp settles §05 as well: 「손전등이 포인터가 된다」 wants the beam
along the arm, and on this shape the beam leaves perpendicular to the grip — i.e. exactly
along the arm — with no wrist contortion in any of the nine clips."""

HEAD_RADIUS = 0.0262
"""Half the lamp head's diameter. Larger than the handle on purpose: in first person the
head points almost straight down the line of sight, so what the owner sees past their own
knuckles is its end-on disc, and that disc is the whole cue that says *the torch is in
your hand* (§03's four states, §10's most-repeated decision)."""


def flex(reach: Vector, sign: float) -> Vector:
    """The axis a digit bends about: across the digit, in the plane of the palm.

    Derived per digit rather than shared, because the thumb does not bend about the same
    axis as the fingers — ``relax_hand`` has already rolled it 34° into opposition, and a
    thumb flexed about the fingers' axis swings sideways out of the grip instead of onto
    the barrel.
    """
    mirrored = Vector((sign * reach.x, reach.y, reach.z))
    return mirrored.cross(Vector((0.0, 0.0, -1.0))).normalized()


def _curled(digit: dict, proximal: float, intermediate: float) -> tuple[Vector, Vector]:
    """Where a digit's middle joint and tip land after a two-joint curl, hand-local."""
    base, tip = digit["base"], digit["tip"]
    reach = tip - base
    axis = flex(reach, 1.0)
    r1 = Matrix.Rotation(math.radians(proximal), 3, axis)
    r2 = Matrix.Rotation(math.radians(proximal + intermediate), 3, axis)
    mid = base + r1 @ (reach * DIGIT_SPLIT)
    return mid, mid + r2 @ (reach * (1.0 - DIGIT_SPLIT))


def _curled_tip(digit: dict, proximal: float, intermediate: float) -> Vector:
    return _curled(digit, proximal, intermediate)[1]


def _wrap_angle(radius_of, target: float) -> float | None:
    """The angle at which a joint leaves a circle of radius ``target``, coming back out.

    A joint swinging toward a barrel gets closer, passes it and gets further away again,
    so the distance is **not monotonic** and a plain bisection over the whole range finds
    nothing — that was the first version's bug and it reported the sculpt as unable to
    hold a torch. The minimum is found by a coarse scan first, and the root taken on the
    far side of it, which is the one that means *wrapped around* rather than
    *not there yet*.
    """
    scan = [(radius_of(a), a) for a in [i * 2.0 for i in range(0, 91)]]
    _, at_min = min(scan)
    lo, hi = at_min, 180.0
    if radius_of(lo) > target or radius_of(hi) < target:
        return None
    for _ in range(48):
        mid = (lo + hi) * 0.5
        if radius_of(mid) < target:
            lo = mid
        else:
            hi = mid
    return (lo + hi) * 0.5


def _point_segment(px: float, pz: float, ax: float, az: float,
                   bx: float, bz: float) -> float:
    dx, dz = bx - ax, bz - az
    denom = dx * dx + dz * dz
    t = 0.0 if denom < 1e-12 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / denom))
    return math.hypot(px - (ax + dx * t), pz - (az + dz * t))


def solve_grip(harvest: dict) -> dict:
    """Closes the fist to anatomical limits and **inscribes the torch's handle in it**.

    This is the inverse of the obvious thing and it is the only formulation that works.
    Choosing a handle first and bending the fingers until they reach it has no solution
    at any angle: the sculpt's proximal phalanx is 55 mm and the grip cavity's radius is
    around 24 mm, so the far end of that segment can never lie on the circle — it is
    always further out. The generator found that itself, and the fix is to stop asking.

    So the fingers close to ``GRIP_MCP``/``GRIP_PIP``, and the handle becomes **the
    largest cylinder that fits inside the closed hand**: a grid search for the centre
    that maximises the smallest clearance to the two phalange segments and to the palm
    it is pressed against. The torch is then sized by the hand rather than the other way
    round, which is also why it comes out at a plausible 31–37 mm — the diameter of a
    hand's own grip cavity is what every tool handle in the world is sized to.
    """
    digits = {d["name"]: d for d in harvest["digits"]}
    middle = digits["Middle"]
    knuckle = middle["base"]

    palm = [v.co for v in harvest["object"].data.vertices
            if knuckle.x * 0.40 <= v.co.x <= knuckle.x * 1.00]
    underside = min(p.z for p in palm)

    # Every finger, not just the middle one. The four are 8.5–10.6 cm long on this sculpt,
    # so at the same joint angles their tips land in four different places, and a cavity
    # inscribed in the longest alone puts the index finger 14 mm inside the handle —
    # measured, on the first version that used the middle finger by itself.
    segments = []
    for digit in harvest["digits"]:
        if digit["name"] == "Thumb":
            continue          # it closes over the fingers, not around the handle
        base = digit["base"]
        joint, end = _curled(digit, GRIP_MCP, GRIP_PIP)
        segments.append((base.x, base.z, joint.x, joint.z))
        segments.append((joint.x, joint.z, end.x, end.z))
    mid, tip = _curled(middle, GRIP_MCP, GRIP_PIP)

    def clearance(cx: float, cz: float) -> float:
        """Largest handle radius centred here: limited by the fingers or by the palm."""
        if cz >= underside:
            return -1.0
        skin = min(_point_segment(cx, cz, *s) for s in segments) - FINGER_HALF_THICKNESS
        return min(skin, underside - cz)

    # The search box IS the cavity, and bounding it is load-bearing rather than tidy:
    # clearance to a finger grows without limit as the circle walks away from the hand,
    # so an unbounded search "inscribes" a 165 mm handle floating below the fist. The
    # box is the closed fist's own corners — proximal of the fingertip, distal of the
    # knuckle, under the palm and above the closed fingers.
    box_x = (min(tip.x, mid.x) - 0.006, max(knuckle.x, mid.x))
    box_z = (min(mid.z, tip.z) + 0.002, underside - 0.004)
    if box_x[1] <= box_x[0] or box_z[1] <= box_z[0]:
        blendkit.fail(f"the closed fist encloses no cavity at all: x {box_x[0]:.3f}..{box_x[1]:.3f}, "
                      f"z {box_z[0]:.3f}..{box_z[1]:.3f}. The fingers are not curling toward the "
                      "palm, which means the flexion axis derived from the digit is wrong.")

    lo_x, hi_x = box_x
    lo_z, hi_z = box_z
    best = (-1.0, (lo_x + hi_x) * 0.5, (lo_z + hi_z) * 0.5)
    for _ in range(4):
        for i in range(41):
            for j in range(41):
                cx = lo_x + (hi_x - lo_x) * i / 40.0
                cz = lo_z + (hi_z - lo_z) * j / 40.0
                d = clearance(cx, cz)
                if d > best[0]:
                    best = (d, cx, cz)
        span_x, span_z = (hi_x - lo_x) * 0.25, (hi_z - lo_z) * 0.25
        lo_x = max(box_x[0], best[1] - span_x)
        hi_x = min(box_x[1], best[1] + span_x)
        lo_z = max(box_z[0], best[2] - span_z)
        hi_z = min(box_z[1], best[2] + span_z)

    radius, centre_x, centre_z = best
    if not 0.011 <= radius <= 0.028:
        blendkit.fail(
            f"the closed fist's cavity inscribes a {radius * 2000:.0f} mm handle. Under 22 mm "
            "is a pen and over 56 mm is a fence post; either says the digit segmentation or "
            "the hand's scale is wrong, and §05 hands every player this object for the whole "
            "match.")

    # Measured on the CURLED fingertips, not the relaxed ones. The lamp is only ever
    # drawn while the fist is closed on it, so clearing the open hand's reach put 26 mm of
    # head past fingers that are not there — and the T-pose span that produced is the
    # largest extent on the whole model, which is the number AssetImportValidator anchors
    # to PlayerHeightMetres.
    reach = max(_curled_tip(d, GRIP_MCP, GRIP_PIP).x
                for d in harvest["digits"] if d["name"] != "Thumb")
    grip = {
        "proximal": GRIP_MCP, "intermediate": GRIP_PIP,
        "centre_x": centre_x, "centre_z": centre_z,
        "radius": radius, "half_width": HANDLE_HALF_WIDTH,
        "head_z": centre_z + radius + HEAD_RADIUS * 0.62,
        "lens_x": reach + 0.086,
    }
    print(f"GRIP mcp={GRIP_MCP:.0f}deg pip={GRIP_PIP:.0f}deg inscribed_handle_r="
          f"{radius * 1000:.1f}mm at ({centre_x:+.4f},{centre_z:+.4f}) "
          f"knuckle_z={knuckle.z * 1000:+.1f}mm palm_underside={underside * 1000:+.1f}mm "
          f"head_z={grip['head_z'] * 1000:+.1f}mm lens_x={grip['lens_x']:.4f}")
    return grip


FINGER_HALF_THICKNESS = 0.008
"""Half a finger's depth, metres. The bone chain is solved onto a circle this much larger
than the handle, so the *skin* touches it rather than the *bone* — the difference is 8 mm
and at 0.35 m from the camera 8 mm is twenty pixels of finger inside a torch."""

THUMB_GRIP_SCALE = 1.0
"""How much of the fingers' curl the thumb takes when the fist closes. Less than one,
because a thumb on a tool handle lies **over the fingers** rather than round the handle —
it is shorter than they are and it comes at the grip from the other side."""

HANDLE_HALF_WIDTH = 0.046
"""Half the handle's length across the palm. It has to run past the little finger and
short of the thumb's web, or the fist closes on air at one end."""


def build_torch(b: gpm.Body, grip: dict) -> None:
    """§05's 손전등, on the solved grip axis, weighted rigidly to RightHand.

    Its own mesh so the Unity layer can switch it off without touching the arms, and
    skinned to exactly one bone so ``PlayerRigParts`` recognises it as a held item rather
    than as part of the person. In first person the torch is nearly parallel to the line
    of sight, so a plain barrel inside a fist reads as a lump: the head is 1.8× the barrel
    and it clears the fingertips, which makes what the owner sees past their own knuckles
    a disc that is obviously an object.
    """
    r = grip["radius"]
    half = grip["half_width"]
    gx = -(gpm.WRIST_X + grip["centre_x"])
    gz = gpm.SHOULDER_Z + grip["centre_z"]
    hz = gpm.SHOULDER_Z + grip["head_z"]
    lens = -(gpm.WRIST_X + grip["lens_x"])

    # Handle: a cylinder along the knuckle line, which is the axis a fist closes about.
    b.shell([(gpm.ellipse((gx, y, gz), gpm.X, gpm.Z, r * s, r * s, gpm.SIDES_BOOT),
              {"RightHand": 1.0})
             for y, s in ((-half, 0.80), (-half * 0.78, 1.0),
                          (half * 0.78, 1.0), (half, 0.80))], gpm.M_GEAR)

    # Head: forward off the top of the handle, along the arm, so §05's beam leaves in the
    # direction the arm is pointing without a wrist bend anywhere in the nine clips.
    head = [
        (gx + 0.026, HEAD_RADIUS * 0.62),
        (gx - 0.010, HEAD_RADIUS * 0.86),
        (lens + 0.040, HEAD_RADIUS * 0.86),
        (lens + 0.014, HEAD_RADIUS),          # bezel, clear of the fingertips
        (lens, HEAD_RADIUS * 0.90),
    ]
    b.shell([(gpm.ellipse((x, 0.0, hz), gpm.Y, gpm.Z, rad, rad, gpm.SIDES_LIMB),
              {"RightHand": 1.0}) for x, rad in head], gpm.M_GEAR)


# ── Finger poses: §03's four states, from the inside ────────────────────────

GRIP_CLIPS = ("Idle", "Walk", "Run", "Crouch", "CrouchWalk")
"""Clips where the right hand is closed on the torch and the left is free. §05 poses the
right arm holding the light out in every one of them, because a beam that swings with an
arm destroys the signal §13 networks camera pitch to carry."""

CARRY_CLIPS = ("Carry", "CarryIdle")
"""§03's 목표물: both hands, no torch. 「양손을 쓴다 → 손전등을 들 수 없다」."""

HEAVY_CLIPS = ("CarryHeavy",)
"""§08's 대형 전리품: hooked under, not gripped."""


def finger_pose(clip: str, grip: dict) -> dict:
    """Degrees of (proximal, intermediate) curl per digit for one clip, per hand.

    This is priority two of the brief and it is the only thing that delivers it: §03 asks
    a player to tell **empty · torch · loot · objective** apart with no HUD, from a view
    that contains their hands and nothing else of themselves. Four hand shapes is the
    whole answer, and it needs fingers to have any shapes to be in.

    * **empty / free** — the open half-curl a hand rests in. Nothing in it, and it reads
      as nothing in it because the fingers are apart and the thumb is out.
    * **torch** — the solved grip, on the right only. The left stays open, which is what
      makes the difference between the two hands legible rather than symmetrical.
    * **objective** — both hands, fingers spread and *flat*, thumbs wide, as though under
      a weight. The torch mesh is not drawn, and §03's reason for that is on screen: the
      hands are full.
    * **heavy** — both hands hooked, fingers curled hard and thumbs alongside rather than
      opposed, which is how something is carried underneath rather than held.
    * **death** — §09's ghost is a state, not an exit, and a corpse's hands fall open.
    """
    free = {d: (24.0, 30.0) for d in DIGIT_NAMES}
    free["Thumb"] = (12.0, 16.0)

    if clip in GRIP_CLIPS:
        closed = {d: (grip["proximal"], grip["intermediate"]) for d in DIGIT_NAMES}
        closed["Thumb"] = (grip["proximal"] * THUMB_GRIP_SCALE,
                           grip["intermediate"] * THUMB_GRIP_SCALE)
        return {"Left": free, "Right": closed}
    if clip in CARRY_CLIPS:
        flat = {d: (14.0, 6.0) for d in DIGIT_NAMES}
        flat["Thumb"] = (4.0, 4.0)
        return {"Left": flat, "Right": flat}
    if clip in HEAVY_CLIPS:
        hook = {d: (78.0, 66.0) for d in DIGIT_NAMES}
        hook["Thumb"] = (40.0, 30.0)
        return {"Left": hook, "Right": hook}
    if clip == "Death":
        limp = {d: (34.0, 40.0) for d in DIGIT_NAMES}
        limp["Thumb"] = (16.0, 18.0)
        return {"Left": limp, "Right": limp}
    return {"Left": free, "Right": free}


def finger_basis(harvest: dict, bone: str) -> tuple[Vector, float]:
    """The world flexion axis for a finger bone and the sign its side needs."""
    side = "Left" if bone.startswith("Left") else "Right"
    sign = 1.0 if side == "Left" else -1.0
    name = _digit_of(bone, side)
    digit = next(d for d in harvest["digits"] if d["name"] == name)
    return flex(digit["tip"] - digit["base"], sign), sign


def apply_finger_curl(clip, harvest: dict, grip: dict) -> None:
    """Writes the clip's finger curl into every one of its keyframes.

    ``gen_player_model.make_pose`` keys all bones on every frame — its own note explains
    why, and it is the reason a bone left uncurved in one clip keeps the previous clip's
    pose. Finger bones get the parent's absolute rotation from that pass, i.e. identity
    relative to the hand, and this overwrites those entries.

    Composed in the bone's rest frame rather than in world space, which is what makes one
    number work in nine clips: ``basis = REST⁻¹ · R(θ, world axis) · REST`` is a rotation
    of θ about that axis **whatever the hand is doing**, so the same 62° closes the fist
    in Idle, in a sprint and lying on the floor.
    """
    pose = finger_pose(clip.name, grip)
    for bone in FINGER_BONES:
        side = "Left" if bone.startswith("Left") else "Right"
        name = _digit_of(bone, side)
        proximal, intermediate = pose[side][name]
        degrees = proximal if bone.endswith("Proximal") else intermediate
        axis, _ = finger_basis(harvest, bone)
        local = gpm.REST[bone].inverted() @ axis
        basis = Matrix.Rotation(math.radians(degrees), 3, local)
        euler = basis.to_euler("XYZ")
        value = (math.degrees(euler.x), math.degrees(euler.y), math.degrees(euler.z))
        for key in clip.poses:
            key.rotations[bone] = value


# ── Build ───────────────────────────────────────────────────────────────────


def build_meshes(harvest: dict, grip: dict) -> tuple:
    """The three meshes of §05's split: body, arms (with the hands in them), torch."""
    body = gpm.Body(gpm.SLOTS)
    build_torso(body)
    build_neck_head(body)
    for side in (1, -1):
        build_leg(body, side)
    build_gear(body)
    build_collar(body)
    gpm.build_role_swatches(body)

    arms = gpm.Body(gpm.ARM_SLOTS)
    for side in (1, -1):
        build_arm(arms, side, harvest)
        build_arm_band(arms, side)

    torch = gpm.Body(gpm.TORCH_SLOTS)
    build_torch(torch, grip)

    return (body.finish("Player_Body"), arms.finish("Player_Arms"),
            torch.finish("Player_Torch"))


def mount_specs(harvest: dict, grip: dict) -> list[BoneSpec]:
    """The rig, with ``FlashlightMount`` moved onto the solved barrel axis.

    §05 makes the torch a **pointing device** — 「손전등이 포인터가 된다」 — and §13
    networks camera pitch because 「남의 손전등 방향이 정보다」. The Unity spot light is
    placed on this bone, so it has to sit on the barrel's own axis and in front of the
    lens: inside the barrel it lights the torch from within and throws the player's own
    fist down the corridor.
    """
    specs = bone_specs(harvest)
    lens_x = -(gpm.WRIST_X + grip["lens_x"] + 0.012)
    z = gpm.SHOULDER_Z + grip["head_z"]
    out = []
    for spec in specs:
        if spec.name == "FlashlightMount":
            out.append(BoneSpec("FlashlightMount", (lens_x, 0.0, z),
                                (lens_x - 0.120, 0.0, z), "RightHand"))
        else:
            out.append(spec)
    return out


def main() -> None:
    blendkit.reset_scene()
    blendkit.set_frame_range(1, 93)

    for spec in gpm.MATERIAL_SPECS:
        blendkit.make_material(spec)
    gpm.verify_materials()

    harvest = harvest_hand(os.path.join(SOURCE_DIR, VESSEL))
    grip = solve_grip(harvest)
    grip_report = measure_grip(harvest, grip)
    body, arms, torch = build_meshes(harvest, grip)
    bpy.data.objects.remove(harvest["object"], do_unlink=True)
    meshes = (body, arms, torch)
    for mesh in meshes:
        blendkit.triangulate(mesh)
        blendkit.shade_smooth(mesh, angle_degrees=44.0)
        blendkit.uv_smart_project(mesh)

    import gen_monster_model as gmm  # noqa: PLC0415
    limits = gmm.pipeline_constants()

    uv_per_metre = gpm.uv_units_per_metre(body)
    for mesh in (arms, torch):
        gpm.normalise_uv_density(mesh, uv_per_metre)
    surfaces = [gpm.write_surface(name, build, gpm.SURFACE_TARGETS[name], note,
                                  gpm.SURFACE_RES, limits)
                for name, build, note in gpm.SURFACES]
    gpm.write_surface_manifest(surfaces, uv_per_metre, gpm.SURFACE_RES, MANIFEST_SOURCE)
    gpm.verify_surface_separation(surfaces)
    print(f"SKIN_UV uv_units_per_metre={uv_per_metre:.4f} "
          f"(1 tile covers {1.0 / uv_per_metre:.2f} m of surface at tiling 1)")
    for s in surfaces:
        print(f"SKIN_REPORT {s['name']:18s} albedo={s['albedo_mean_linear']:.4f}lin "
              f"rough={s['roughness_mean']:.3f} relief={s['relief_mm']:5.2f}mm "
              f"tile={s['world_size_metres']:.2f}m bytes={s['bytes']}")

    specs = mount_specs(harvest, grip)
    rig = blendkit.build_armature("Player_Rig", specs)
    for name in gpm.MOUNTS:
        rig.data.bones[name].use_deform = False
    gpm.cache_rig(rig)
    for mesh in meshes:
        blendkit.bind_skin(mesh, rig, auto_weights=False)

    verify_rig(rig, meshes)
    gpm.verify_mesh_split(body, arms, torch)
    print(f"RIG_BONES count={len(rig.data.bones)} deform="
          f"{len([b for b in rig.data.bones if b.use_deform])} "
          f"sockets={len(gpm.MOUNTS)} fingers={len(FINGER_BONES)}")

    clips = []
    stats: dict[str, dict] = {}
    metrics: dict[str, dict] = {}
    worst: dict[str, dict] = {}
    speeds: dict[str, float] = {}

    for build in gpm.CLIP_BUILDERS:
        clip = build(rig)
        apply_finger_curl(clip, harvest, grip)
        action = blendkit.make_action(rig, clip.name, clip.poses, loop=clip.loop)
        clip.action = action
        stats[clip.name] = gpm.measure_action(action)
        metrics[clip.name] = gpm.pose_metrics(rig, clip.measure_frame)
        worst[clip.name] = gpm.first_person_worst(rig, clip)
        if clip.name == "Death":
            metrics["DeathSettle"] = gpm.pose_metrics(rig, 41)
        if clip.speed > 0.0:
            speeds[clip.name] = clip.speed
        clips.append(clip)


    preview_dir = os.environ.get("HORROR_PLAYER_PREVIEW_DIR")
    if preview_dir:
        rig.animation_data.action = None
        gpm.clear_pose(rig)
        for p in gpm.render_previews(rig, body, preview_dir, [
                ("00_bind_front", 0, (0.0, -4.6, 1.05), (0.0, 0.0, 1.00)),
                ("00_bind_side", 0, (4.0, -1.4, 1.10), (0.0, 0.0, 1.00))]):
            print(f"PREVIEW {p}")
        for clip in clips:
            rig.animation_data.action = clip.action
            f = clip.measure_frame
            for p in gpm.render_previews(rig, body, preview_dir, [
                    (f"{clip.name}_q", f, (2.3, -3.1, 1.35), (0.0, -0.05, 0.92)),
                    (f"{clip.name}_front", f, (0.0, -3.4, 1.20), (0.0, -0.10, 0.95))]):
                print(f"PREVIEW {p}")
            for p in gpm.render_first_person(rig, preview_dir, [(clip.name, f)]):
                print(f"PREVIEW {p}")
        rig.animation_data.action = None

    for clip in clips:
        blendkit.stash_action(rig, clip.action)
    for track in rig.animation_data.nla_tracks:
        for strip in track.strips:
            strip.extrapolation = "NOTHING"
            strip.blend_in = 0.0
            strip.blend_out = 0.0
    rig.animation_data.action = None
    gpm.clear_pose(rig)
    bpy.context.scene.frame_set(0)

    bind_span = max(abs(gpm.bone_point(rig, s + "Hand", (0.0, 0.0, 0.0)).x)
                    for s in ("Left", "Right"))
    if abs(bind_span - gpm.WRIST_X) > 0.002:
        blendkit.fail(f"the rig is not at rest before export: the wrist sits at "
                      f"{bind_span:.3f} m instead of {gpm.WRIST_X:.3f} m. Unity's Humanoid "
                      "auto-mapping reads the bind pose, and it has to be the T-pose.")

    fbx_path = blendkit.out_path("Characters", "Player.fbx")
    glb_path = os.path.join(os.path.dirname(fbx_path), "Player.glb")
    blendkit.export_fbx(fbx_path, objects=[rig, body, arms, torch], with_animation=True)
    gpm.hook_surface_maps(surfaces, uv_per_metre)
    gpm.write_surface_manifest(surfaces, uv_per_metre, gpm.SURFACE_RES, MANIFEST_SOURCE)
    blendkit.export_gltf(glb_path, with_animation=True)

    report = blendkit.assert_asset(
        blendkit.describe(fbx_path),
        max_triangles=TRI_BUDGET,
        expect_bones=len(specs),
        expect_actions=len(gpm.CLIP_BUILDERS),
        max_dimension=2.5,
    )
    blendkit.print_report(report)

    span, depth, height = report.size
    ratio = verify_span(arms, height)
    print(f"PLAYER_SHAPE height={height:.3f}m file_span={span:.3f}m depth={depth:.3f}m "
          f"arm_span_over_height={ratio:.3f} "
          f"eye_height={metrics['Idle']['eye_z']:.3f}m hand_length={HAND_LENGTH:.3f}m")
    gpm.verify_shape(report, metrics)

    for clip in clips:
        s = stats[clip.name]
        extra = ""
        if clip.name in speeds:
            extra = (f" speed={speeds[clip.name]:.2f}m/s cycle={clip.cycle_frames}f")
        if clip.hip_hi:
            extra += (f" bob={clip.hip_hi - clip.hip_lo:.3f}m"
                      f" sole_err={clip.sole_error * 1000:.2f}mm")
        print(f"ANIM_REPORT {clip.name:11s} frames={s['start']}-{s['end']} "
              f"({s['frames']:3d}f, {s['seconds']:.2f}s) loop={int(clip.loop)} "
              f"curves={s['curves']} keys={s['keys']:4d} "
              f"max_bone_motion={s['max_deg']:6.2f}deg{extra}  # {clip.note}")

    for clip in clips:
        m = metrics[clip.name]
        print(f"POSE_MEASURE {clip.name:11s} "
              f"fwd_of_hips={m['mount_fwd']:+.3f}m eye_z={m['eye_z']:.3f}m "
              f"hand_gap={m['hand_gap']:.3f}m hand_z={m['hand_z']:.3f}m "
              f"objective_reach={m['objective_reach']:.3f}m")

    half_v, half_h = gpm.frame_half_angles()
    print(f"FIRST_PERSON_FRAME fov={gpm.fov_default_degrees():.0f}deg "
          f"half_v={half_v:.1f}deg half_h={half_h:.1f}deg")
    for clip in clips:
        w = worst[clip.name]
        print("FP_WORST_KEY {:11s} {}".format(clip.name, " ".join(
            "{}=({:+.0f}deg below,{:.0f}deg off,{:.2f}m,{})".format(
                label, v[0], v[1], v[2], "IN " if gpm.in_frame(v, half_v, half_h) else "OUT")
            for label, v in (("left_hand", w["view_left"]),
                             ("right_hand", w["view_right"]),
                             ("torch", w["view_mount"]),
                             ("best_hand", w["any_hand"])))))

    gpm.verify_motion(clips, stats, metrics, speeds, worst)

    takes = gpm.fbx_objects(fbx_path, b"AnimStack")
    dropped = [c.name for c in clips if c.name not in takes]
    if dropped:
        blendkit.fail("actions missing from the FBX: " + ", ".join(dropped))
    fbx_models = gpm.fbx_objects(fbx_path, b"Model")
    for socket in gpm.MOUNTS:
        if socket not in fbx_models:
            blendkit.fail(f"{socket} is not in the exported hierarchy — §05/§13 need it.")
    for mesh in meshes:
        if mesh.name not in fbx_models:
            blendkit.fail(f"{mesh.name} is not in the exported hierarchy. The three meshes "
                          "are how §05's 「자기 몸은 안 보이므로 손만 있으면 된다」 is "
                          "delivered — one mesh means hiding the chest hides the hands.")
    missing_fingers = [b for b in FINGER_BONES if b not in fbx_models]
    if missing_fingers:
        blendkit.fail("finger bones missing from the FBX: " + ", ".join(missing_fingers))
    fbx_mats = gpm.fbx_objects(fbx_path, b"Material")
    missing_mats = [m for m in gpm.SLOTS if m not in fbx_mats]
    if missing_mats:
        blendkit.fail("materials missing from the FBX: " + ", ".join(missing_mats))

    glb_names = {a["name"] for a in gpm.glb_animations(glb_path)}
    glb_dropped = [c.name for c in clips if c.name not in glb_names]
    if glb_dropped:
        blendkit.fail("actions missing from the GLB: " + ", ".join(glb_dropped))

    print(f"FILES {fbx_path} {glb_path}")
    print(f"ASSET_REPORT Player tris={report.triangles} height={height:.3f} "
          f"bones={len(specs)} clips={len(clips)} hands={grip_report}")


TRI_BUDGET = 7200
"""The cap this generator fails on. The monster ships 5,704 against 6,000 and the brief
asks for the same order; this one lands higher because 2 × HAND_TRIS of it is two hands
a camera sits 0.35 m from, and because §05 draws them on screen every frame of the match
while the monster is off screen for most of it."""


def verify_rig(rig: bpy.types.Object, meshes) -> None:
    """Every deforming bone must own geometry and no socket may own any."""
    weighted = {b for mesh in meshes for b in mesh.vertex_groups.keys()}
    deform = [b.name for b in rig.data.bones if b.use_deform]
    missing = [b for b in deform if b not in weighted]
    if missing:
        blendkit.fail("bones with no weighted geometry: " + ", ".join(missing)
                      + ". A finger bone with nothing on it animates a hand that does not "
                      "move, which is the failure §03's four carry states cannot survive.")
    stray = [n for n in gpm.MOUNTS if n in weighted]
    if stray:
        blendkit.fail("socket bones must not deform the mesh: " + ", ".join(stray))


def verify_span(arms: bpy.types.Object, height: float) -> float:
    """Arm span against height. A human's is 1.00–1.06 × their height.

    Measured on the arms mesh rather than on the whole file, because the whole file
    includes the torch head projecting past the fingertips and a torch is not part of a
    person's span. Returns the ratio so the caller can print it.
    """
    xs = [v.co.x for v in arms.data.vertices]
    span = max(xs) - min(xs)
    ratio = span / height
    if not 1.00 <= ratio <= 1.06:
        blendkit.fail(f"the T-pose span is {span:.3f} m on a {height:.3f} m body, a ratio of "
                      f"{ratio:.3f}. A person's is 1.00–1.06. The vessel sculpt was rejected "
                      f"for the body on exactly this measurement — its own arms run 1.9× a "
                      f"human's — so shipping the same error rebuilt would be worse than "
                      f"importing it.")
    return ratio


def measure_grip(harvest: dict, grip: dict) -> str:
    """How far each closed fingertip sits from the handle's surface, in millimetres.

    The one thing about this model a picture cannot settle at the size it is looked at,
    and the one that would look cheapest if it were wrong: a finger through the handle is
    invisible in a 200-pixel thumbnail and unmissable at 35 cm. Reported per digit rather
    than asserted with one threshold, because a thumb resting 4 mm off the handle is a
    hand holding a torch and a thumb 4 mm inside it is not — the sign is the whole story,
    and a single worst-case number would hide which of the two it is.

    Computed from the solved angles rather than from a posed rig on purpose: the curl is
    applied in each bone's own rest frame, so it is the same in all nine clips, and a
    measurement taken through one of them would only be re-measuring the pose solver.
    """
    lay = grip["radius"] + FINGER_HALF_THICKNESS
    parts = []
    worst = 0.0
    for digit in harvest["digits"]:
        thumb = digit["name"] == "Thumb"
        _, tip = _curled(digit, grip["proximal"] * (THUMB_GRIP_SCALE if thumb else 1.0),
                         grip["intermediate"] * (THUMB_GRIP_SCALE if thumb else 1.0))
        gap = (math.hypot(tip.x - grip["centre_x"], tip.z - grip["centre_z"]) - lay) * 1000.0
        parts.append(f"{digit['name']}={gap:+.1f}")
        worst = gap if abs(gap) > abs(worst) else worst
    print("GRIP_CLEARANCE mm from the handle's surface, + is clear: " + " ".join(parts))
    if worst < -6.0:
        blendkit.fail(f"a closed fingertip sits {worst:.1f} mm inside the torch handle. At "
                      "§05's 0.35 m that is a finger visibly through metal, which is the "
                      "exact class of defect this whole pass exists to remove.")
    return f"worst_fingertip_to_handle={worst:+.1f}mm"


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_player_ai.py raised:\n" + traceback.format_exc())
