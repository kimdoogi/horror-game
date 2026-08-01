#!/usr/bin/env python3
"""Builds the player: authored hands, a body, a Humanoid rig, mounts, nine clips.

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

THE HANDS WERE HARVESTED FROM A SCULPT AND ARE NOW BUILT — WHY IT CHANGED
-------------------------------------------------------------------------
Until this pass the hands were **cut out of** ``tools/blender/source/monster_vessel_base
.glb``, the flayed Rodin variant ``gen_monster_ai.py`` calls *"something that used to be
a person"*, with *"torn skin at every joint"*. The argument was ``gen_monster_ai``'s own
— a hull-and-tube assembly can only make bulges, and a hand is all gaps: between two
fingers, under the arch of the palm, in the web of the thumb.

**The argument was right about hulls and wrong about what it was rejecting.** The gaps
between fingers are not concavities in one surface; they are the space between five
separate surfaces, and five separate lofted solids have them for free. What genuinely
defeated the creature was its flank, which really is one surface with a hollow in it.
A hand is not one surface.

What the harvest shipped, measured on ``Shots/land_guide_van.png`` and recorded in
ART.md §7.13: fingers fused into a paddle with no knuckles, no nails and no creases; a
stippled displacement that reads as raw meat; bare untextured forearms; and **a hole at
the right wrist you could see through**. Its own guard — *"the hand is a tube, so it is
a mitten"* — passed the whole time, because the guard measured span and the defect was
topology. The sculpt's five separated masses are real, and they are 4 mm across at the
fingertips *only*; over the rest of their length the digits are modelled in contact, and
decimating to a triangle budget welds what is left.

So the hands are authored now:

    anthropometry → a lofted palm with a knuckle arch → five separate digits on their
    own curved centre lines → nails → the three pads → weights written down, not fitted

A hand is one of the best-documented shapes there is. Every number ``build_hand`` uses
is a measurement somebody already took, and what that buys, in the order it shows up at
the 0.35 m §05 puts it at: daylight between five digits, four knuckles, five nails,
three visible segments per finger, a thumb that opposes, and a wrist that is closed
because it is a capped solid rather than a cut through somebody else's arm.

The **body** is still built and always was, for the reason ART.md §4.1 measured on the
creature: *"the sculpt detail the art pass added is invisible past about 5 m"*, and what
a teammate has to carry at the 3–15 m §12 corridors allow is a silhouette and a role
colour. Nobody is ever more than a hand's length from these hands.

THE TRIANGLE BUDGET, AND WHERE IT GOES
---------------------------------------
The monster ships 5,704 against a 6,000 cap. This model is allowed the same order and
spends it very differently, because the two are looked at from different distances:

    Player_Arms    shoulders → fingertips, and two built hands of that
    Player_Body    head, torso, legs, helmet, harness, pack, boots
    Player_Torch   the flashlight in the fist

The hands take the largest single share of any part of this model on purpose. They are
the only geometry in the game a camera is ever 0.35 m from; the body is never closer
than about 1.5 m to anybody, and at §04's 관측 range it is sixty pixels tall.

THE SLEEVE IS PART OF THE HAND'S JOB
-------------------------------------
§05 shows the owner their own forearms and shows three teammates a coverall, and until
this pass those were different garments — first person had bare skin tubes, third person
had cloth. They are the same coverall now, with a **role-coloured cuff** at the wrist:
§04's colour is the one thing about a teammate that has to survive a beam finding only
part of them, and putting it where the owner also sees it costs eight triangles.

WHAT IS LOAD-BEARING AND NOT NEGOTIABLE
----------------------------------------
* ``AssetImportPolicy.PlayerHeightMetres`` pins 1.750 m and ``ScaleTolerance`` is 2 %.
  On a T-posed figure the largest extent is the **span**, not the height — see
  ``HAND_LENGTH``.
* ``AssetImportPolicy.PlayerMountBones`` — HeadCameraAnchor, FlashlightMount,
  ObjectiveMount, BackpackMount. They are not humanoid bones, so nothing but this
  generator and ``AssetImportValidator`` protects them.
* ``ExpectedAnimationClipCount`` says 9 clips, and ``LoopingAnimationClips`` /
  ``OneShotAnimationClips`` say which loop.
* ``PlayerRigParts`` classifies the three meshes by their MATERIAL SLOTS, never by
  name — ``gen_player_model.verify_mesh_split`` is where that contract is asserted.
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
The distal segment is still *modelled*, with a nail on it; only the bone is dropped.

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
import gen_player_model as gpm  # noqa: E402
from blendkit import BoneSpec  # noqa: E402


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


# ── The shared blends ───────────────────────────────────────────────────────

KNUCKLE_BLEND = 0.20
"""Fraction of a digit's length over which it hands back to the palm. Below this the
metacarpal head — the knuckle you punch with — swings with the finger, and the back of
a closing hand loses the row of four bumps that is most of what says *fist*."""

WRIST_BLEND = 0.030
"""Metres over which the palm hands back to the forearm. A wrist has no crease in the
skin at the joint, so a hard boundary here creases one in."""

DIGIT_SPLIT = 0.52
"""Where a digit's proximal bone hands over to its intermediate one, as a fraction of
the digit's length. A finger's proximal phalanx is about half of it and the middle and
distal together are the other half; with the distal dropped (see FINGERS) the
intermediate bone stands in for both, so the split stays near the middle."""


def _smoothstep(lo: float, hi: float, x: float) -> float:
    if hi <= lo:
        return 0.0 if x < lo else 1.0
    t = max(0.0, min(1.0, (x - lo) / (hi - lo)))
    return t * t * (3.0 - 2.0 * t)


def _digit_of(bone: str, side: str) -> str | None:
    """The digit a bone belongs to, or None for the palm and the forearm."""
    if not bone.startswith(side):
        return None
    tail = bone[len(side):]
    for name in DIGIT_NAMES:
        if tail.startswith(name):
            return name
    return None


WRIST_RADIUS = 0.030
"""Radius the sleeve's cuff is sized off, in metres.

A wrist is about 56 mm across on a hand this size and the cuff has to enclose it with
slack rather than meet it — see ``cuff_rings``, where the 15 % is spent."""


# ── Building the hand ───────────────────────────────────────────────────────
#
# THE FRAME. Everything below is in hand-local coordinates and every consumer in this
# file agrees on them, so they are stated once:
#
#     +X   distal. The wrist crease is x = 0 and the middle fingertip is x = HAND_LENGTH.
#     -Y   the thumb side (anterior in the T-pose).
#     -Z   palmar. `flex()` curls a digit toward -Z and `solve_grip` inscribes the
#          handle under the palm, so the sign is load-bearing, not a convention.
#
# `_hand_world` maps this into armature space and mirrors X for the right hand.


def _spow(x: float, power: float) -> float:
    """Signed |x|^(2/power) — the superellipse exponent, written so power=2 is a circle.

    A palm is not an ellipse. It is flat on the back, flat across the front and rounded
    only at the two edges, and an elliptical cross-section reads as a sausage from the
    one distance §05 puts this geometry at. Raising the exponent pushes the section
    toward a rounded rectangle at no triangle cost at all.
    """
    if x == 0.0:
        return 0.0
    return math.copysign(abs(x) ** (2.0 / power), x)


def _section(centre: Vector, hy: float, z_up: float, z_down: float,
             sides: int, power: float, frame=None):
    """One cross-section: half-width in Y, and separate rises above and below the axis.

    Two half-depths rather than one radius because every part of a hand is asymmetric
    through its thickness — the dorsum is flat and close to the bone, the palmar side
    carries pads. A single radius makes a finger a cylinder, and a cylinder with a
    fingernail on it is a cylinder with a fingernail on it.

    ``z_up`` and ``z_down`` are both **magnitudes**; the sign comes from where round the
    section a vertex is. Passing a signed z_down negates twice and folds the lower half
    of every ring onto the upper one, which is a solid that looks plausible in a vertex
    count and is a crumpled sheet in a render.

    ``frame`` is (along, across, up) for geometry that does not run along +X — the
    digits, which are built on their own curved centre lines.
    """
    if frame is None:
        across, up = Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0))
    else:
        _, across, up = frame
    out = []
    for i in range(sides):
        a = 2.0 * math.pi * i / sides
        cy, sz = math.cos(a), math.sin(a)
        t = _spow(sz, power)
        out.append(centre + across * (hy * _spow(cy, power))
                   + up * ((z_up if t >= 0.0 else z_down) * t))
    return out


def _centroid(hand, ring: list[int]) -> Vector:
    total = Vector((0.0, 0.0, 0.0))
    for i in ring:
        total = total + hand.co[i]
    return total / float(len(ring))


class _Hand:
    """Vertices, per-vertex bone weights and quads for one hand, built ring by ring.

    The same accumulator idea as ``gen_player_model.Body`` and for the same reason —
    **the weights are authored, not inferred.** The version this replaces harvested a
    sculpt and then had to *guess* which bone every vertex belonged to by inverse
    distance, which needed a k-means split for two fingers modelled in contact, four
    passes of Laplacian smoothing to stop the guess tearing the mesh, and still shipped
    a hand whose fingers were one paddle. Here every ring is placed on a known digit at
    a known parameter along it, so its weights are simply written down and there is
    nothing left to go wrong at a knuckle.
    """

    def __init__(self) -> None:
        self.co: list[Vector] = []
        self.w: list[dict[str, float]] = []
        self.faces: list[tuple] = []
        self.thumb: set[int] = set()

    def ring(self, points, weights: dict[str, float], thumb: bool = False) -> list[int]:
        first = len(self.co)
        for p in points:
            self.co.append(Vector(p))
            self.w.append(dict(weights))
            if thumb:
                self.thumb.add(len(self.co) - 1)
        return list(range(first, len(self.co)))

    def bridge(self, a: list[int], b: list[int]) -> None:
        n = len(a)
        for i in range(n):
            j = (i + 1) % n
            self.faces.append((a[i], a[j], b[j], b[i]))

    def cap(self, ring: list[int], weights: dict[str, float], thumb: bool = False,
            dome: Vector = None) -> None:
        """Closes a ring with a fan from its own centroid.

        A fan and not an n-gon: an n-gon at a fingertip shades as one flat disc and
        catches the beam as a highlight with a straight edge, which is the single most
        obvious way to say *this is a cylinder*. The centre vertex is pushed a little
        along the ring's own normal so the tip is domed rather than cut off.
        """
        centre = Vector((0.0, 0.0, 0.0))
        for i in ring:
            centre = centre + self.co[i]
        centre /= float(len(ring))
        if dome is not None:
            radius = sum((self.co[i] - centre).length for i in ring) / float(len(ring))
            centre = centre + dome.normalized() * (radius * 0.62)
        idx = self.ring([centre], weights, thumb)[0]
        n = len(ring)
        for i in range(n):
            self.faces.append((ring[i], ring[(i + 1) % n], idx))

    def loft(self, rings, cap_start: bool = True, cap_end: bool = True,
             thumb: bool = False) -> list[list[int]]:
        """`rings` is a list of (points, weights). Returns the index rings."""
        idx = [self.ring(points, weights, thumb) for points, weights in rings]
        for a, b in zip(idx, idx[1:]):
            self.bridge(a, b)
        # The dome direction is taken from the loft's own run rather than from a face
        # normal, because a ring's winding is not fixed until `recalc_face_normals` and a
        # cap pushed the wrong way is a dimple in a fingertip at 0.35 m.
        if cap_start:
            self.cap(list(reversed(idx[0])), rings[0][1], thumb,
                     _centroid(self, idx[0]) - _centroid(self, idx[1]))
        if cap_end:
            self.cap(idx[-1], rings[-1][1], thumb,
                     _centroid(self, idx[-1]) - _centroid(self, idx[-2]))
        return idx

    def bulge(self, centre_x: float, centre_y: float, sigma_x: float, sigma_y: float,
              amount: float, dorsal: bool, only=None) -> float:
        """Raises one soft mound out of the surface. Returns the largest rise applied.

        The knuckles, the thenar and the hypothenar are all this: a local swelling of an
        otherwise smooth solid, on one side of it only. Applying it as a displacement
        after the loft rather than as a shaped ring is what keeps the ring list readable
        — a knuckle is not a cross-section of anything, it is a lump on one.

        Scaled by how far a vertex already is from the mid-plane, so the silhouette
        edge, which sits *on* that plane, does not balloon: a bump that moved the outline
        as much as the surface would read as a swollen hand rather than a knuckled one.
        """
        peak = 0.0
        for i, p in enumerate(self.co):
            if only is not None and i not in only:
                continue
            side = (p.z / 0.010) if dorsal else (-p.z / 0.010)
            if side <= 0.0:
                continue
            fall = math.exp(-((p.x - centre_x) / sigma_x) ** 2
                            - ((p.y - centre_y) / sigma_y) ** 2)
            rise = amount * fall * min(1.0, side)
            if rise <= 1e-6:
                continue
            p.z += rise if dorsal else -rise
            peak = max(peak, rise)
        return peak

    def finish(self, name: str) -> bpy.types.Object:
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([tuple(p) for p in self.co], [], [list(f) for f in self.faces])
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)

        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        return obj


# ── The measurements ────────────────────────────────────────────────────────
# Anthropometry, scaled to this hand. A 1.75 m adult's hand is 0.189 m wrist crease to
# middle fingertip; HAND_LENGTH is 0.181 because the T-pose span is capped (see its own
# note), so every dimension below is the human figure times 181/189 = 0.958. They are
# written as absolute metres rather than as ratios because that is how they were
# checked — against a ruler and a hand.

PALM_LENGTH = 0.0985
"""Wrist crease to the middle finger's knuckle. 54 % of the hand, which is the ratio
that decides at a glance whether a hand reads as a hand or as a glove: too short and the
fingers are spider legs, too long and it is a paddle with notches."""

PALM_WIDTH = 0.0855
"""Across the four metacarpal heads. The four fingers are laid out to fill exactly this,
so a finger's width is not a free parameter — it is this divided among them."""

WRIST_WIDTH, WRIST_DEPTH = 0.0555, 0.0395
"""The wrist's own section. Narrower than the palm in Y and *deeper* in Z, which is the
change of direction that says wrist; a hand tapering evenly into the sleeve reads as a
mitten however good the fingers are."""

KNUCKLE_RISE = 0.0046
"""Metres the four metacarpal heads stand out of the back of the hand.

The row of four bumps is most of what a knuckled hand has that a mitten does not, and
at §05's 0.35 m it is about ten pixels of relief under a light that moves with the
camera. Set by rendering: at 3.4 mm the back of the hand photographed flat under a light
coming from the camera's own direction, which — §05 holding the torch at the eye — is
the only direction this geometry is ever lit from. It is weighted to ``Hand`` rather than to the fingers on purpose — see
``KNUCKLE_BLEND`` — so it stays put when the fist closes, which is when it matters."""

THENAR_RISE = 0.0052
HYPOTHENAR_RISE = 0.0034
"""The two pads of the palm. Without them the palmar surface is flat, and a flat palm
cannot hold anything: §03's torch is inscribed in the cavity *between* these two."""

FINGERS = (
    # name,   mcp_x,  mcp_y,   length, width,  yaw,  curl
    ("Index",  0.0958, -0.0315, 0.0726, 0.0202, -1.8, 0.90),
    ("Middle", 0.0985, -0.0103, 0.0845, 0.0204, 0.0, 1.00),
    ("Ring",   0.0942, 0.0107, 0.0778, 0.0192, 1.4, 1.08),
    ("Little", 0.0855, 0.0301, 0.0602, 0.0170, 3.2, 1.18),
)
"""The four fingers: knuckle position, length, width at the proximal phalanx, the yaw
that fans them, and how much of the rest curl each takes.

``mcp_x`` is not the same for all four and that is the point — the knuckle line is an
**arch**, running from the middle finger forward of the index and 13 mm forward of the
little. A straight knuckle line is the second thing after fused fingers that makes a
built hand read as a mitten, and it costs nothing to avoid.

``curl`` rises across the hand because a relaxed hand's little finger is the most
flexed and its index the least; four fingers at identical flexion read as a salute.

``yaw`` fans them **apart**, not together. The four widths sum to 0.0768 against a
0.0855 palm, so they touch at the knuckles and the fan opens daylight between them from
there out — which is what a relaxed hand does and what ``verify_hand`` measures. An
earlier revision converged them by 2° and closed the index-to-middle clearance to 0.3 mm
at the middle joint, which is the fused paddle this whole file exists to stop shipping,
rebuilt out of separate solids."""

THUMB_BASE = Vector((0.0215, -0.0205, -0.0055))
THUMB_TIP = Vector((0.0932, -0.0602, -0.0318))
"""The thumb's bone chain, carpometacarpal to tip.

Unity's ``ThumbProximal`` **is the first metacarpal**, not a phalanx, so the chain that
maps onto the Humanoid avatar starts in the middle of the palm — which is also where a
thumb genuinely hinges. ``DIGIT_SPLIT`` then lands the second joint at (0.062, -0.043,
-0.019), within 3 mm of a real MCP.

The tip is set where an opposed thumb's is: forward of the palm, well below its plane,
and reaching to about the index knuckle. That last one is the check anybody can make on
their own hand, and it is what makes the thumb read as opposed rather than as a fifth
finger lying alongside the others."""

THUMB_WIDTH = 0.0224
"""Widest across the proximal phalanx. A thumb is thicker than any finger and reads
wrong if it is not."""

PALM_SIDES = 16
DIGIT_SIDES = 10
"""Twelve is enough for a limb at 1.5 m (``gen_player_model.SIDES_LIMB``) and is not
enough here. At 0.35 m a ten-sided finger is about four degrees per facet across a
20 mm cylinder, which is under a pixel of silhouette break; eight showed as a visible
hexagon on the index finger's near edge in the first render of this pass."""

DIGIT_POWER = 2.05
"""Superellipse exponent for a digit's cross-section. Just off a circle.

A finger is very slightly flattened on its pad and almost round everywhere else. At 2.4
— the value the palm wants — a ten-sided section squares up enough to put a visible
ridge down the back of every finger, which under a light at the camera reads as a bevel
and makes the hand look moulded."""

REST_MCP, REST_PIP, REST_DIP = 9.0, 17.0, 12.0
"""Degrees of flexion at the three joints in the **rest mesh**, before any clip.

A live hand at rest is never flat. This is also insurance and not styling: if Unity's
Humanoid mapping ever drops the finger curves the hands freeze here, and a resting hand
already shaped like a hand is a far cheaper failure than five flat spars. Kept modest so
the two-bone chain, which is straight base→tip, stays inside geometry that is not."""

PHALANX = (0.44, 0.29, 0.27)
"""Proximal / intermediate / distal, as fractions of a finger's length. Real ratios are
about 45/28/27; the creases between them are where the joint bulges go, and those are
what make a finger read as three segments rather than as a taper."""

NAIL_LENGTH = 0.46
NAIL_WIDTH = 0.62
NAIL_PROUD = 0.0009
"""The nail plate: fraction of the distal phalanx it covers, fraction of the finger's
half-width it spans, and how far it stands off the surface.

0.9 mm is small enough to be honest and large enough that ``shade_smooth``'s 44° crease
angle breaks the shading at the nail fold, which is the whole trick — what reads as a
nail at 0.35 m is not the plate, it is the hard line round it."""


def _digit_frame(direction: Vector, yaw: float) -> tuple:
    """(along, across, up) for a digit pointing `direction`, yawed `yaw` degrees.

    ``up`` comes out dorsal (+Z-ish) by construction, which is what the nails and the
    joint bulges are placed against."""
    across = Matrix.Rotation(math.radians(yaw), 3, "Z") @ Vector((0.0, 1.0, 0.0))
    along = direction.normalized()
    up = along.cross(across).normalized()
    return along, across.normalized(), up


def _digit_path(base: Vector, length: float, yaw: float, curl: float,
                joints=(REST_MCP, REST_PIP, REST_DIP)) -> list:
    """The centre line of one digit: four points and the frame at each.

    Built as a chain rather than as a straight axis because a straight finger is the
    other half of the mitten problem — fused *and* flat. Each segment turns by its
    joint's angle about the digit's own across-axis, which is the axis a real finger
    bends about, so the curl stays in the plane the yaw put it in.
    """
    mcp, pip, dip = (a * curl for a in joints)
    out = []
    here = Vector(base)
    for i, share in enumerate(PHALANX):
        pitch = (mcp, mcp + pip, mcp + pip + dip)[i]
        # +pitch about Y leans +X toward -Z, and -Z is palmar. Getting this sign wrong
        # curls the fingers over the BACK of the hand, which renders as a claw and
        # leaves `solve_grip` inscribing the torch handle in a cavity that is not there.
        direction = (Matrix.Rotation(math.radians(yaw), 3, "Z")
                     @ Matrix.Rotation(math.radians(pitch), 3, "Y")
                     @ Vector((1.0, 0.0, 0.0)))
        out.append((here.copy(), _digit_frame(direction, yaw), length * share))
        here = here + direction * (length * share)
    out.append((here.copy(), out[-1][1], 0.0))
    return out


def _along_path(path: list, s: float) -> tuple:
    """Point and frame at fraction `s` of a digit's total length. `s` may be negative,
    which runs back down the first segment's direction and into the palm — where a
    finger's first ring belongs, so that the join is a lap and not a butt."""
    total = sum(seg[2] for seg in path)
    want = s * total
    if want < 0.0:
        origin, frame, _ = path[0]
        return origin + frame[0] * want, frame
    walked = 0.0
    for origin, frame, seg_length in path:
        if seg_length <= 0.0:
            continue
        if want <= walked + seg_length:
            return origin + frame[0] * (want - walked), frame
        walked += seg_length
    origin, frame, _ = path[-1]
    return origin.copy(), frame


DIGIT_STATIONS = (
    # s along the digit, half-width scale, dorsal rise scale, palmar rise scale
    (-0.17, 1.00, 0.96, 1.02),      # buried in the palm
    (0.02, 0.99, 0.94, 1.02),       # knuckle
    (0.22, 0.90, 0.84, 0.94),       # proximal shaft
    (0.42, 0.96, 0.90, 0.99),       # PIP — the joint stands proud
    (0.55, 0.85, 0.80, 0.90),
    (0.72, 0.88, 0.84, 0.90),       # DIP
    (0.86, 0.80, 0.76, 0.80),
    (0.94, 0.68, 0.64, 0.66),
    (1.00, 0.30, 0.26, 0.26),       # the pad; the cap domes it
)
"""Nine sections down a finger. The two swells at 0.42 and 0.72 are the interphalangeal
joints and they are the reason a finger reads as segmented: a monotonic taper is a cone,
and a cone with a nail on it is a claw."""


def _digit_weights(name: str, s: float, side: str = "Left") -> dict[str, float]:
    """Bone weights at fraction `s` along a digit — authored, not fitted.

    Two ramps and nothing else. Proximal hands over to intermediate across
    ``DIGIT_SPLIT``, and the whole digit hands back to the palm below the knuckle over
    ``KNUCKLE_BLEND`` — which is what leaves the metacarpal head on the hand, so the
    four dorsal bumps stay where they are when the fist closes.
    """
    to_palm = 1.0 - _smoothstep(-KNUCKLE_BLEND * 0.5, KNUCKLE_BLEND, s)
    distal = _smoothstep(DIGIT_SPLIT - 0.16, DIGIT_SPLIT + 0.16, s)
    out: dict[str, float] = {}
    if to_palm > 1e-4:
        out[side + "Hand"] = to_palm
    hold = 1.0 - to_palm
    if hold > 1e-4:
        out[side + name + "Proximal"] = hold * (1.0 - distal)
        out[side + name + "Intermediate"] = hold * distal
    return {k: v for k, v in out.items() if v > 1e-4}


def _build_digit(hand: _Hand, name: str, base: Vector, length: float, width: float,
                 yaw: float, curl: float, thumb: bool = False) -> dict:
    """One digit: nine lofted sections down its own curved centre line, plus a nail."""
    path = _digit_path(base, length, yaw, curl)
    half = width * 0.5
    rings = []
    for s, w_scale, up_scale, down_scale in DIGIT_STATIONS:
        centre, frame = _along_path(path, s)
        rings.append((_section(centre, half * w_scale, half * 0.95 * up_scale,
                               half * 1.02 * down_scale, DIGIT_SIDES, DIGIT_POWER, frame),
                      _digit_weights(name, s)))
    hand.loft(rings, cap_start=True, cap_end=True, thumb=thumb)

    # The nail. A closed lens of its own resting on the finger rather than a
    # displacement of the finger's vertices, because what has to exist is the FOLD — a
    # crease all the way round the plate — and a displaced ring shares its neighbours'
    # normals and produces a bump. Closed, and it overlaps the finger by the same
    # interpenetration argument the cuff makes; an open band is 8 boundary edges per
    # digit and 40 holes in a hand is worse than the one this pass set out to close.
    at = 0.845
    centre, frame = _along_path(path, at)
    along, across, up = frame
    plate = half * 0.95 * 0.78
    outer, inner = [], []
    for i in range(8):
        a = 2.0 * math.pi * i / 8.0
        du = length * PHALANX[2] * NAIL_LENGTH * 0.5 * _spow(math.cos(a), 3.4)
        dv = half * NAIL_WIDTH * _spow(math.sin(a), 3.4)
        foot = centre + along * du + across * dv
        outer.append(foot + up * (plate * 0.98))
        inner.append(centre + along * (du * 0.86) + across * (dv * 0.86)
                     + up * (plate + NAIL_PROUD))
    weights = _digit_weights(name, at)
    hand.loft([(outer, weights), (inner, weights)], thumb=thumb)

    tip, _ = _along_path(path, 1.0)
    return {"name": name, "base": Vector(base), "tip": tip, "path": path, "half": half,
            "n": len(DIGIT_STATIONS) * DIGIT_SIDES, "indices": [], "u0": 0.0}


PALM_STATIONS = (
    # x, half-width, dorsal rise, palmar rise, how much of the knuckle arch, dorsal pull
    # The three middle columns are fractions of PALM_WIDTH, so half-width 0.500 is
    # exactly half the palm and the whole hand scales from one number.
    (-0.0125, 0.325, 0.232, 0.232, 0.0, 0.0),      # the wrist, capped inside the sleeve
    (0.0080, 0.335, 0.211, 0.222, 0.0, 0.0),
    (0.0290, 0.390, 0.197, 0.216, 0.10, 0.0),
    (0.0520, 0.455, 0.178, 0.206, 0.30, 0.0),
    (0.0740, 0.492, 0.165, 0.190, 0.62, 0.0),
    (0.0910, 0.502, 0.157, 0.175, 0.90, 0.0),
    (0.0995, 0.500, 0.146, 0.166, 1.00, 0.0),      # the knuckle line
    (0.1085, 0.470, 0.099, 0.149, 1.00, 0.0055),   # the web starts, dorsal side first
    (0.1155, 0.428, 0.041, 0.117, 1.00, 0.0115),   # the commissure, capped
)
"""Nine sections from the wrist to the interdigital web. Widths are fractions of
``PALM_WIDTH`` and rises are fractions of it too, so the whole palm scales together.

The last two rows are the thing a built hand usually gets wrong. **Fingers separate at
the knuckle on the back of the hand and about 15 mm further out on the palm**, so the
web is not a plane: the dorsal side is pulled back (``dorsal pull``) while the palmar
side runs on. Cut square instead and the back of the hand has a shelf across it."""


def _knuckle_x(y: float) -> float:
    """The knuckle line's own X at a given Y — the arch, interpolated across the four
    metacarpal heads and held flat past the outer two."""
    points = [(f[2], f[1]) for f in FINGERS]
    if y <= points[0][0]:
        return points[0][1]
    if y >= points[-1][0]:
        return points[-1][1]
    for (y0, x0), (y1, x1) in zip(points, points[1:]):
        if y0 <= y <= y1:
            t = (y - y0) / max(1e-9, y1 - y0)
            return x0 + (x1 - x0) * t
    return points[-1][1]


def _palm_weights(x: float, side: str = "Left") -> dict[str, float]:
    """Palm weights: all hand, ramping to the forearm across the wrist.

    One ramp, and it is the reason a wrist has no crease: all forearm at the sleeve's
    cut, half and half on the crease, all hand a wrist's width past it."""
    arm = 1.0 - _smoothstep(-0.006, WRIST_BLEND, x)
    out = {side + "Hand": 1.0 - arm}
    if arm > 1e-4:
        out[side + "LowerArm"] = arm
    return {k: v for k, v in out.items() if v > 1e-4}


def _build_palm(hand: _Hand) -> list[int]:
    """The palm, wrist cap to interdigital web. Returns its vertex indices."""
    first = len(hand.co)
    rings = []
    for x, w, up, down, arch, pull in PALM_STATIONS:
        half = PALM_WIDTH * w
        # The wrist is its own section and the palm's is a rounded slab; blending
        # between the two by x is what puts the change of direction at the crease
        # instead of smearing it over the whole hand.
        to_wrist = 1.0 - _smoothstep(-0.012, 0.030, x)
        half = half * (1.0 - to_wrist) + (WRIST_WIDTH * 0.5) * to_wrist
        rise_up = PALM_WIDTH * up * (1.0 - to_wrist) + (WRIST_DEPTH * 0.5) * to_wrist
        rise_dn = PALM_WIDTH * down * (1.0 - to_wrist) + (WRIST_DEPTH * 0.5) * to_wrist
        power = 2.2 + 1.1 * (1.0 - to_wrist)

        points = []
        for i in range(PALM_SIDES):
            a = 2.0 * math.pi * i / PALM_SIDES
            cy, sz = math.cos(a), math.sin(a)
            y = half * _spow(cy, power)
            t = _spow(sz, power)
            z = (rise_up if t >= 0.0 else rise_dn) * t
            at = x + arch * (_knuckle_x(y) - FINGERS[1][1])
            if pull > 0.0 and sz > 0.0:
                at -= pull * abs(_spow(sz, power))
            points.append(Vector((at, y, z)))
        rings.append((points, _palm_weights(x)))
    hand.loft(rings, cap_start=True, cap_end=True)
    return list(range(first, len(hand.co)))


def build_hand() -> dict:
    """Builds one left hand and returns what the rest of this file consumes.

    WHY THIS IS BUILT AND NOT HARVESTED
    -----------------------------------
    It used to be cut out of ``monster_vessel_base.glb`` — the flayed vessel
    ``gen_monster_ai.py`` calls *"something that used to be a person"*, with *"torn skin
    at every joint"* — on the argument that a hand is all concavity and a hull-and-tube
    assembly can only make bulges. The argument was right about hulls and wrong about
    what was being proposed. **The gaps between fingers are not concavities in one
    surface; they are the space between five separate surfaces**, and five separate
    lofted solids have them for free. What defeated the creature was its flank, which
    genuinely is one surface with a hollow in it. A hand is not.

    What the harvest actually shipped, measured on ``land_guide_van.png``: fingers fused
    into a paddle, no knuckles, no nails, no creases, a stippled displacement that reads
    as raw meat, and a hole at the right wrist you could see through. Its own guard —
    *"the hand is a tube, so it is a mitten"* — passed, because the guard measured span
    and the defect was topology. The sculpt's five separated masses are real and they are
    4 mm across at the fingertips only; over the other 80 % of their length the digits
    are welded, and decimating to a budget welds the rest.

    So the hand is authored. A hand is one of the best-documented shapes in
    anthropometry and every number this file needs is a measurement somebody has already
    taken. What that buys, in the order it shows up at 0.35 m: five digits with daylight
    between them, four knuckles, five nails, three visible segments per finger, a thumb
    that opposes, and a wrist that is closed because it is a capped solid rather than a
    cut through somebody else's arm.

    Returns the same dictionary the harvest did — ``object``, ``size``, ``digits``,
    ``tris``, ``weights`` — so the rig, the grip solver, the sleeve and the nine clips
    are all unchanged. That seam is deliberate: this pass is allowed to change what the
    hand *is* and not what anything downstream believes about it.
    """
    hand = _Hand()
    palm = _build_palm(hand)

    # Thumb first, so it owns the low vertex indices `hand.thumb` records and
    # `hand_sections` excludes. DIGIT_NAMES order is thumb-to-little and `bone_specs`
    # reads this list in order, so it is also the order the rig is built in.
    digits = [_build_thumb(hand, THUMB_TIP - THUMB_BASE)]
    for name, mcp_x, mcp_y, length, width, yaw, curl in FINGERS:
        digits.append(_build_digit(hand, name, Vector((mcp_x, mcp_y, 0.0008)),
                                   length, width, yaw, curl))

    # The three mounds. Applied to the palm only — a bulge that reached the fingers
    # would swell the proximal phalanx, and a swollen finger is a sausage.
    on_palm = set(palm)
    knuckles = max(hand.bulge(f[1] - 0.0045, f[2], 0.0125, 0.0108, KNUCKLE_RISE,
                              dorsal=True, only=on_palm) for f in FINGERS)
    thenar = hand.bulge(0.0455, -0.0255, 0.0265, 0.0175, THENAR_RISE,
                        dorsal=False, only=on_palm)
    hypo = hand.bulge(0.0545, 0.0295, 0.0250, 0.0150, HYPOTHENAR_RISE,
                      dorsal=False, only=on_palm)
    print(f"HAND_RELIEF knuckles={knuckles * 1000:.2f}mm thenar={thenar * 1000:.2f}mm "
          f"hypothenar={hypo * 1000:.2f}mm")

    obj = hand.finish("Hand_Left")

    # Scale so the middle fingertip lands exactly on HAND_LENGTH. The rest curl shortens
    # the reach by about 4 mm and the correction is under 3 %, but it is applied rather
    # than absorbed because the T-pose span this decides is the number
    # AssetImportValidator anchors to PlayerHeightMetres.
    span = max(v.co.x for v in obj.data.vertices)
    gain = HAND_LENGTH / span
    obj.data.transform(Matrix.Scale(gain, 4))
    obj.data.update()
    for digit in digits:
        digit["base"] = digit["base"] * gain
        digit["tip"] = digit["tip"] * gain
        digit["half"] *= gain
        digit["path"] = [(origin * gain, frame, length * gain)
                         for origin, frame, length in digit["path"]]

    points = [v.co for v in obj.data.vertices]
    size = {
        "length": max(p.x for p in points),
        "width": max(p.y for p in points) - min(p.y for p in points),
        "thickness": max(p.z for p in points) - min(p.z for p in points),
        "thumb_y": min(p.y for p in points),
    }
    size["sections"] = hand_sections(obj, exclude=hand.thumb)
    verify_hand(obj, digits, size, hand.thumb)

    tris = sum(len(f) - 2 for f in hand.faces)
    print(f"HAND_BUILT tris={tris} verts={len(obj.data.vertices)} scale_gain={gain:.4f}")
    print(f"HAND_SIZE length={size['length']:.4f}m width={size['width']:.4f}m "
          f"thickness={size['thickness']:.4f}m")
    for digit in digits:
        reach = digit["tip"] - digit["base"]
        print(f"DIGIT {digit['name']:7s} base=({digit['base'].x:+.3f},{digit['base'].y:+.3f},"
              f"{digit['base'].z:+.3f}) tip=({digit['tip'].x:+.3f},{digit['tip'].y:+.3f},"
              f"{digit['tip'].z:+.3f}) length={reach.length * 100:5.1f}cm")
    for at, sec in sorted(size["sections"].items()):
        print(f"HAND_SECTION x={at * 1000:5.1f}mm  y={sec['y0'] * 1000:+6.1f}..{sec['y1'] * 1000:+6.1f} "
              f"z={sec['z0'] * 1000:+6.1f}..{sec['z1'] * 1000:+6.1f}")

    return {"object": obj, "size": size, "digits": digits, "tris": tris,
            "weights": hand.w, "thumb": hand.thumb}


def _build_thumb(hand: _Hand, reach: Vector) -> dict:
    """The thumb, on the line THUMB_BASE → THUMB_TIP, already opposed.

    Opposition is built into the rest mesh rather than applied as a rotation afterwards.
    The version this replaces had to swing a harvested thumb 26° of abduction and 34° of
    opposition through the skin weights to get it off the index finger, because the
    sculpt's thumb lay alongside the fingers like a corpse's. Stating where the thumb is
    is both shorter and the only version in which ``DIGIT_SPLIT`` lands the middle joint
    on an anatomical MCP.
    """
    yaw = math.degrees(math.atan2(reach.y, reach.x))
    pitch = math.degrees(math.asin(max(-1.0, min(1.0, reach.z / reach.length))))
    length = reach.length
    # The thumb's own rest curl is small: the metacarpal is straight and the one joint
    # that shows is the interphalangeal.
    # The metacarpal is straight — the first "joint" is just the aim onto the base→tip
    # line — and the two that follow are the MCP and the IP. Both were 6°/10° and the
    # thumb photographed as a spar: a straight thumb is the second thing after fused
    # fingers that makes a built hand read as a mannequin's.
    path = _digit_path(THUMB_BASE, length, yaw, 1.0,
                       joints=(-pitch, 15.0, 21.0))
    half = THUMB_WIDTH * 0.5
    stations = (
        (-0.10, 1.26, 1.55, 2.05),      # inside the palm, and thick: this is the thenar
        (0.06, 1.18, 1.30, 1.75),
        (0.30, 1.00, 0.98, 1.22),       # the metacarpal, narrowing
        (0.50, 0.98, 0.94, 1.02),       # MCP
        (0.66, 0.88, 0.82, 0.88),
        (0.80, 0.90, 0.84, 0.86),       # IP
        (0.92, 0.80, 0.74, 0.74),
        (1.00, 0.42, 0.38, 0.38),
    )
    rings = []
    for s, w_scale, up_scale, down_scale in stations:
        centre, frame = _along_path(path, s)
        rings.append((_section(centre, half * w_scale, half * 0.95 * up_scale,
                               half * 1.02 * down_scale, DIGIT_SIDES, DIGIT_POWER, frame),
                      _digit_weights("Thumb", s)))
    hand.loft(rings, cap_start=True, cap_end=True, thumb=True)

    at = 0.86
    centre, frame = _along_path(path, at)
    along, across, up = frame
    plate = half * 0.95 * 0.80
    outer, inner = [], []
    for i in range(8):
        a = 2.0 * math.pi * i / 8.0
        du = length * 0.20 * NAIL_LENGTH * 0.5 * _spow(math.cos(a), 3.4)
        dv = half * NAIL_WIDTH * _spow(math.sin(a), 3.4)
        foot = centre + along * du + across * dv
        outer.append(foot + up * (plate * 0.98))
        inner.append(centre + along * (du * 0.86) + across * (dv * 0.86)
                     + up * (plate + NAIL_PROUD))
    weights = _digit_weights("Thumb", at)
    hand.loft([(outer, weights), (inner, weights)], thumb=True)

    tip, _ = _along_path(path, 1.0)
    return {"name": "Thumb", "base": Vector(THUMB_BASE), "tip": tip, "path": path,
            "half": half, "n": len(stations) * DIGIT_SIDES, "indices": [], "u0": 0.0}


# ── What the pictures could not settle ──────────────────────────────────────

MIN_DIGIT_GAP = 0.0030
"""Metres of daylight required between two adjacent fingers, at their widest separation.

**This is the check the harvest's own guard should have been.** That one measured the
hand's span, concluded the sculpt's fingers were separate volumes, and passed on a hand
that shipped as one paddle — because span is not separation. So separation is what is
measured here, and on the **surfaces**: the distance between two digits' centre lines
minus the two radii, which is the daylight a camera actually sees.

Taken as the *maximum* over the distal two thirds rather than the minimum, because
fingers are supposed to touch at the knuckles. What has to be true is that somewhere
along their length there is air between them; 3 mm at 0.35 m is about seven pixels, and
below that the two shade as one mass whatever the topology says."""


def _digit_radius(digit: dict, s: float) -> float:
    """The digit's own half-width at fraction `s`, interpolated between its stations."""
    stations = DIGIT_STATIONS
    if s <= stations[0][0]:
        return digit["half"] * stations[0][1]
    for (u0, w0, _, _), (u1, w1, _, _) in zip(stations, stations[1:]):
        if u0 <= s <= u1:
            t = (s - u0) / max(1e-9, u1 - u0)
            return digit["half"] * (w0 + (w1 - w0) * t)
    return digit["half"] * stations[-1][1]


def digit_clearance(a: dict, b: dict, from_s: float = 0.30) -> float:
    """Closest approach between two digits' **surfaces**, distal of `from_s`, in metres.

    Every point on one against every point on the other, because two fingers of
    different lengths are not closest at the same fraction along themselves — comparing
    matched fractions reports the index and middle fingers 21 mm apart when the gap
    between their surfaces is two.
    """
    worst = 1e9
    for i in range(25):
        sa = from_s + (1.0 - from_s) * i / 24.0
        pa, _ = _along_path(a["path"], sa)
        ra = _digit_radius(a, sa)
        for j in range(25):
            sb = from_s + (1.0 - from_s) * j / 24.0
            pb, _ = _along_path(b["path"], sb)
            worst = min(worst, (pa - pb).length - ra - _digit_radius(b, sb))
    return worst


def verify_hand(obj: bpy.types.Object, digits: list[dict], size: dict,
                thumb: set[int]) -> None:
    """Everything about this hand a render would only show after twenty minutes."""
    if abs(size["length"] - HAND_LENGTH) > 1e-4:
        blendkit.fail(f"the hand measures {size['length']:.4f} m, not {HAND_LENGTH:.4f}. "
                      "The span this sets is what AssetImportValidator anchors to "
                      "PlayerHeightMetres.")

    ratio = PALM_LENGTH / HAND_LENGTH
    if not 0.50 <= ratio <= 0.58:
        blendkit.fail(f"the palm is {ratio:.3f} of the hand; a human's is 0.52-0.56. "
                      "Outside that it reads as a glove or as a paddle.")

    by_name = {d["name"]: d for d in digits}
    gaps = []
    for a, b in (("Index", "Middle"), ("Middle", "Ring"), ("Ring", "Little")):
        gap = digit_clearance(by_name[a], by_name[b])
        gaps.append(f"{a[0]}{b[0]}={gap * 1000:.1f}")
        if gap < MIN_DIGIT_GAP:
            blendkit.fail(f"the {a} and {b} fingers approach to {gap * 1000:.1f} mm "
                          "of one another distal of their knuckles. Under "
                          f"{MIN_DIGIT_GAP * 1000:.0f} mm they shade as one mass, which is "
                          "the exact defect this file was rewritten to remove.")
    print("DIGIT_CLEARANCE mm, surface to surface, distal of the knuckles: "
          + " ".join(gaps))

    # The thumb has to be on the other side of the hand, not alongside the index — the
    # difference between a hand and a mitten with a spare finger. Measured as the angle
    # between the two digits, because a thumb that is merely offset in Y but parallel is
    # still not opposed.
    thumb_reach = (by_name["Thumb"]["tip"] - by_name["Thumb"]["base"]).normalized()
    index_reach = (by_name["Index"]["tip"] - by_name["Index"]["base"]).normalized()
    degrees = math.degrees(math.acos(max(-1.0, min(1.0, thumb_reach.dot(index_reach)))))
    if degrees < 22.0:
        blendkit.fail(f"the thumb lies {degrees:.1f}deg off the index finger. Under 22deg it "
                      "is a fifth finger and §03's grip on the torch, §08's on a 전리품 and "
                      "§03's on the objective all have nothing on the far side of them.")

    # A closed solid. A hole in the mesh is not a style: the shipped hand had one at the
    # right wrist and it was the first thing anybody saw.
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    boundary = [e for e in bm.edges if len(e.link_faces) != 2]
    count = len(boundary)
    bm.free()
    if count:
        blendkit.fail(f"the hand has {count} edge(s) with other than two faces on them, so "
                      "it is not a closed surface. That is the hole at the wrist, and the "
                      "sleeve cannot hide one it does not know about.")

    if not thumb:
        blendkit.fail("no vertices were recorded as belonging to the thumb, so "
                      "`hand_sections` would size the sleeve's cuff over the thenar "
                      "eminence and the cuff would come out a bell.")

    print(f"HAND_VERIFY palm={ratio:.3f}of_hand thumb_opposition={degrees:.1f}deg "
          f"closed=yes thumb_verts={len(thumb)}")


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


CUFF_SEGMENTS = 2
"""How many of the sleeve's segments are the role-coloured cuff band.

§04's colour is repeated on the helmet, the collar, the vest, the deltoid caps and the
bicep bands because a beam finds *part* of a teammate, never all of one. The cuff is the
sixth place and the only one the owner also sees — in first person it is the widest
thing in the lower third of the frame for the whole match, and until this pass what was
there instead was an untextured tube that read as a bare arm."""


def cuff_rings(side: int, sections: dict):
    """The sleeve's wrist: a cuff band, a mouth that tapers, and an end *inside* the palm.

    **Four rounds of renders went into the join being an interpenetration rather than a
    weld.** Every attempt to make the sleeve *meet* the hand failed the same way: a
    hand's section at the wrist is oblique and the thumb's base lies on it, so a cuff
    wide enough to enclose it is a flared collar, a cuff narrower than it leaves a slot
    into the inside of the arm, and a cuff that tries to do both spikes through the skin.

    Two closed surfaces that simply overlap have none of those problems. The sleeve
    tapers to a real wrist, carries on 26 mm into the palm and is capped there, and the
    hand is closed in its own right (``verify_hand`` asserts it, which is the check the
    shipped hole at the right wrist did not have). There is no seam to open because there
    is no seam: what a camera sees is the curve where two solids cross, which is what a
    wrist looks like. The only thing that has to be true is that the last ring is
    genuinely inside the hand, and that is why it is sized from the hand's own 26 mm
    section rather than chosen.

    The **cuff band** is the two rings before that, and it is a band rather than a taper
    for the same reason the vest is: a cuff on a work coverall is a doubled-over hem with
    a hard edge at each end, and a hard edge is what makes a sleeve read as clothing
    instead of as a tube the arm was extruded into.
    """
    s = "Left" if side > 0 else "Right"
    palm = sections[0.026]
    py = (palm["y1"] - palm["y0"]) * 0.5
    pz = (palm["z1"] - palm["z0"]) * 0.5
    pcy = (palm["y1"] + palm["y0"]) * 0.5
    pcz = (palm["z1"] + palm["z0"]) * 0.5
    return [
        (gpm.WRIST_X - 0.078, 0.0, 0.0, 0.0345, 0.0330, {f"{s}LowerArm": 1.0}),
        # The cuff proper: 26 mm of hem standing about 2 mm proud of the sleeve, then a
        # hard step back down at its mouth. Both edges are creases at shade_smooth's 44°.
        (gpm.WRIST_X - 0.070, 0.0, 0.0, 0.0375, 0.0358, {f"{s}LowerArm": 1.0}),
        (gpm.WRIST_X - 0.044, 0.0, 0.0, 0.0368, 0.0352, {f"{s}LowerArm": 1.0}),
        # Sized off WRIST_RADIUS with 15 % of slack, not by eye: a sleeve narrower than
        # the wrist lets the skin ring stand proud all the way round, which photographed
        # as a crumpled paper cuff on both wrists in first person.
        (gpm.WRIST_X - 0.010, 0.0, 0.0, WRIST_RADIUS * 1.15, WRIST_RADIUS * 1.10,
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


def build_arm(b: gpm.Body, side: int, hand: dict) -> None:
    """One coverall sleeve, its role-coloured cuff, and the built hand inside it."""
    rings = [(x, 0.0, 0.0, ru, rv, w) for x, ru, rv, w in arm_rings(side)]
    cuff = cuff_rings(side, hand["size"]["sections"])
    first_cuff = len(rings)
    rings += cuff
    mats = [gpm.M_COVERALL] * (len(rings) - 1)
    mats[1] = gpm.M_ROLE            # the deltoid cap, above the shoulder line
    for seg in range(first_cuff, first_cuff + CUFF_SEGMENTS):
        mats[seg] = gpm.M_ROLE      # §04's colour where the owner can see it too
    # Capped at both ends. The far cap sits inside the palm where nothing can see it, and
    # it is there because an *open* tube whose mouth strays a millimetre outside the hand
    # renders as a black hole at the wrist — which is what the first two rounds showed.
    b.shell([(gpm.ellipse((side * x, cy, gpm.SHOULDER_Z + cz), gpm.Y, gpm.Z, ru, rv,
                          gpm.SIDES_LIMB), w)
             for x, cy, cz, ru, rv, w in rings], mats)
    weld_hand(b, side, hand)


def weld_hand(b: gpm.Body, side: int, harvest: dict) -> None:
    """Copies the built hand into the arms mesh at the wrist, weights and all.

    Fed through ``Body.vert``/``Body.face`` rather than joined as an object, because the
    material slot ORDER is a contract — §04 swaps ``renderer.materials[0]`` per role, and
    Blender's join merges slot lists in join order. The accumulator keeps SLOTS order by
    construction, so the hand cannot be the thing that shuffles it.

    The cuff overlaps the wrist by design: the sleeve's last ring sits 26 mm past the
    wrist plane and the hand is a closed solid, so there is a lap joint rather than a
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

    # Eleven rings instead of six, through gpm._skull_ring rather than a plain
    # ellipse, so the brow, the cheekbones and the jaw are part of the same surface.
    # They used to be three rects stuck on the front of an egg, and rendered close —
    # which §13 does in the van and §09's ghost does at any range it likes — that is
    # what they looked like: loose dark chunks with shadowed gaps behind them. The
    # nose band in _skull_ring still fires and is still worth its triangles: the
    # respirator sits ON the face, so the shape under it decides the mask's outline.
    skull = [
        (1.5720, -0.020, 0.048, 0.052),
        (1.5870, -0.019, 0.058, 0.063),
        (1.6020, -0.018, 0.066, 0.072),
        (1.6170, -0.017, 0.071, 0.078),
        (1.6320, -0.016, 0.074, 0.082),
        (1.6480, -0.014, 0.076, 0.086),
        (1.6630, -0.013, 0.076, 0.086),
        (1.6780, -0.012, 0.074, 0.083),
        (1.6950, -0.010, 0.067, 0.075),
        (1.7110, -0.007, 0.049, 0.055),
        (1.7220, -0.005, 0.021, 0.025),
    ]
    b.shell([(gpm._skull_ring(z, rx, ry, y, chin=1.5720, crown=1.7220), {"Head": 1.0})
             for z, y, rx, ry in skull], gpm.M_SKIN)

    # Respirator: a half-mask over nose and mouth with a filter each side. Its outline is
    # what makes a head at 15 m read as a person in equipment rather than as a pale oval.
    #
    # Moved forward 30 mm on 2026-08-02, when the skull under it stopped being an
    # ellipse. _skull_ring projects a brow 8.5 mm and a nose 18.5 mm past where the
    # old egg's surface was, so a mask authored against the egg ended up buried in
    # the face with only its tip out — three dark fragments floating on a cheek,
    # which is worse than the three boxes it replaced. The back plate is also wider
    # now (60 mm against 52) so it meets the cheekbones instead of hovering inboard
    # of them.
    b.shell([(gpm.rect((0, -0.078, 1.618), gpm.X, gpm.Z, 0.060, 0.032), {"Head": 1.0}),
             (gpm.rect((0, -0.108, 1.616), gpm.X, gpm.Z, 0.050, 0.029), {"Head": 1.0}),
             (gpm.rect((0, -0.126, 1.612), gpm.X, gpm.Z, 0.035, 0.021), {"Head": 1.0})],
            gpm.M_GEAR)
    for side in (1, -1):
        b.shell([(gpm.rect((side * 0.056, -0.092, 1.612), gpm.Y, gpm.Z, 0.024, 0.022),
                  {"Head": 1.0}),
                 (gpm.rect((side * 0.082, -0.092, 1.610), gpm.Y, gpm.Z, 0.020, 0.019),
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

MCP_LIMIT, PIP_LIMIT = 95.0, 110.0
"""Anatomical ceilings on the two modelled finger joints, in degrees. A
metacarpophalangeal joint reaches about 90-100 and a proximal interphalangeal about
110. They are limits the solve is clamped to, not the pose it aims for."""

PIP_OVER_MCP = 1.18
"""How much more the middle joint flexes than the knuckle, closing on a handle.

Fingers do not curl one joint at a time — the two flex together in a roughly fixed
ratio, which is why a relaxed hand and a fist are the same shape at different scales.
Fixing the ratio leaves **one unknown per digit**, and one unknown is what makes the
grip solvable in closed form instead of by search."""

HANDLE_OVER_HAND = 0.190
"""Handle diameter as a fraction of hand length: 34.4 mm on this 181 mm hand.

Every tool handle in the world is sized to the cavity a hand makes, and the ratio is
stable across hand sizes because the cavity is. 22 mm is a pen and 56 mm is a fence
post; a work lamp is in the middle of the band and so is this."""

HANDLE_SEAT_X = 0.415
"""Where along the palm the handle lies, as a fraction of the hand's length.

Under the metacarpal heads and along the distal transverse crease, which is where a
hand puts anything it means to keep hold of. Further out and the fingers cannot get
round it; further back and it sits in the hollow of the palm where nothing grips."""

SKIN_COMPRESSION = 0.0020
"""Metres the palm's skin gives under a held object. Small, and it is the difference
between a handle resting *on* the palm and one floating a visible millimetre off it."""

HEAD_RADIUS = 0.0262
"""Half the lamp head's diameter. Larger than the handle on purpose: in first person the
head points almost straight down the line of sight, so what the owner sees past their own
knuckles is its end-on disc, and that disc is the whole cue that says *the torch is in
your hand* (§03's four states, §10's most-repeated decision).

**The torch is an angle-head lamp and that is anatomy, not taste.** Fingers flex about
one axis: the knuckle line, across the palm. Whatever a fist encircles therefore has its
axis across the palm too. The model before this one held a straight barrel running
*along* the arm, from the heel of the hand out past the fingertips, and that is a shape
no fist can close on. It survived review only because the hand it passed through was a
flat slab with four prongs, where nothing could be seen to intersect anything.

A right-angle work lamp settles §05 as well: 「손전등이 포인터가 된다」 wants the beam
along the arm, and on this shape the beam leaves perpendicular to the grip — i.e.
exactly along the arm — with no wrist contortion in any of the nine clips."""


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


def _wrap_angle(radius_of, target: float, limit: float) -> float | None:
    """The angle at which a joint leaves a circle of radius ``target``, coming back out.

    A joint swinging toward a barrel gets closer, passes it and gets further away again,
    so the distance is **not monotonic** and a plain bisection over the whole range finds
    nothing — that was the first version's bug and it reported the sculpt as unable to
    hold a torch. The minimum is found by a coarse scan first, and the root taken on the
    far side of it, which is the one that means *wrapped around* rather than
    *not there yet*.

    ``limit`` is the joint's own anatomical ceiling, and it bounds the search rather
    than decorating it. Scanning to 180deg finds the tip coming back round to the handle
    from the other side, reports the distance as still under target at the far end, and
    concludes there is no root — which is how a hand that grips perfectly well at 80deg
    came back as unable to hold anything at all.
    """
    scan = [(radius_of(a), a) for a in [limit * i / 90.0 for i in range(0, 91)]]
    _, at_min = min(scan)
    lo, hi = at_min, limit
    if radius_of(lo) > target or radius_of(hi) < target:
        return None
    for _ in range(48):
        mid = (lo + hi) * 0.5
        if radius_of(mid) < target:
            lo = mid
        else:
            hi = mid
    return (lo + hi) * 0.5


def _closest_angle(radius_of, target: float, limit: float) -> float:
    """The flexion inside `limit` whose fingertip comes nearest `target`.

    The fallback when there is no root at all — a digit too short to reach round the
    handle, or the thumb, which is not trying to. Returning the closest approach is
    better than clamping to the anatomical limit: a finger held at its limit because the
    solve gave up is a finger visibly curled past the thing it is holding.
    """
    best, at = 1e9, 0.0
    for i in range(361):
        theta = limit * i / 360.0
        error = abs(radius_of(theta) - target)
        if error < best:
            best, at = error, theta
    return at


def _point_segment(px: float, pz: float, ax: float, az: float,
                   bx: float, bz: float) -> float:
    dx, dz = bx - ax, bz - az
    denom = dx * dx + dz * dz
    t = 0.0 if denom < 1e-12 else max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / denom))
    return math.hypot(px - (ax + dx * t), pz - (az + dz * t))


def solve_grip(hand: dict) -> dict:
    """Seats a handle on the palm and closes each finger onto it. Returns the grip.

    **This is the inverse of what it used to be, and the reason is that the hands are
    now anatomically proportioned.** The previous version closed the fist to anatomical
    *limits* and then inscribed the largest cylinder that fitted in whatever cavity was
    left. That worked on a harvested sculpt whose proximal phalanx was 55 mm — half
    again what a 181 mm hand has — because an over-long finger curls into a wide arc and
    leaves a hole in the middle of it. On a correctly proportioned hand a fist closed to
    the limit is a **fist**: the fingertips reach the palm, the cavity is a few
    millimetres, and the solver reported a handle 4 mm across the wrong side of zero.
    Which is true. You cannot hold a torch in a clenched fist.

    So the handle is placed where a hand puts one — on the palm, under the metacarpal
    heads, sized by the hand itself (``HANDLE_OVER_HAND``) — and each digit's flexion is
    solved so that **its fingertip lands on the handle's surface**. One unknown per digit
    because ``PIP_OVER_MCP`` fixes the ratio between the two joints, and ``_wrap_angle``
    finds it on the far side of the closest approach, which is the root that means
    *wrapped around* rather than *not there yet*.

    Solved per digit rather than once, and that is most of what it buys: the four fingers
    are 60-85 mm long, so at a shared angle their tips land in four different places and
    the short ones never reach. Each one now closes exactly as far as it has to, which is
    also what a hand does and what a picture at 0.35 m shows.
    """
    digits = {d["name"]: d for d in hand["digits"]}
    length = hand["size"]["length"]

    # The thumb is excluded for the same reason `hand_sections` excludes it: an opposed
    # thumb hangs 40 mm below the palm, so a palmar surface read over every vertex
    # reports the thumb's own pad as the roof of the grip cavity. Measured: -44 mm
    # against the palm's real -22.
    thumb_verts = hand["thumb"]
    knuckle = digits["Middle"]["base"]
    palm = [v.co for i, v in enumerate(hand["object"].data.vertices)
            if i not in thumb_verts and knuckle.x * 0.40 <= v.co.x <= knuckle.x * 1.05]
    underside = min(p.z for p in palm)

    radius = length * HANDLE_OVER_HAND * 0.5
    centre_x = length * HANDLE_SEAT_X
    centre_z = underside + SKIN_COMPRESSION - radius
    target = radius + FINGER_HALF_THICKNESS

    angles: dict[str, tuple] = {}
    reached: dict[str, float] = {}
    for digit in hand["digits"]:
        if digit["name"] == "Thumb":
            continue

        def radius_of(theta: float, d=digit) -> float:
            tip = _curled_tip(d, theta, theta * PIP_OVER_MCP)
            return math.hypot(tip.x - centre_x, tip.z - centre_z)

        theta = _wrap_angle(radius_of, target, MCP_LIMIT)
        if theta is None:
            theta = _closest_angle(radius_of, target, MCP_LIMIT)
        theta = max(0.0, min(MCP_LIMIT, theta))
        angles[digit["name"]] = (theta, min(PIP_LIMIT, theta * PIP_OVER_MCP))
        reached[digit["name"]] = radius_of(theta) - target

    # The thumb closes OVER the fingers rather than round the handle — it is shorter
    # than they are and it comes at the grip from the other side — so its target is the
    # handle plus a wrapped finger's thickness, and it is solved for the *closest*
    # approach rather than for wrapping past. A share of the index finger's angle was
    # tried first and left the thumb 66 mm clear of the torch, pointing at nothing.
    thumb = digits["Thumb"]

    def thumb_radius(theta: float) -> float:
        tip = _curled_tip(thumb, theta, theta * PIP_OVER_MCP * THUMB_GRIP_SCALE)
        return math.hypot(tip.x - centre_x, tip.z - centre_z)

    over = radius + FINGER_HALF_THICKNESS * 3.0
    theta = _closest_angle(thumb_radius, over, MCP_LIMIT)
    angles["Thumb"] = (theta, min(PIP_LIMIT, theta * PIP_OVER_MCP * THUMB_GRIP_SCALE))
    reached["Thumb"] = thumb_radius(theta) - over

    # Measured on the CURLED fingertips, not the relaxed ones. The lamp is only ever
    # drawn while the fist is closed on it, so clearing the open hand's reach put 26 mm
    # of head past fingers that are not there — and the T-pose span that produced is the
    # largest extent on the whole model, which is the number AssetImportValidator
    # anchors to PlayerHeightMetres.
    reach = max(_curled_tip(d, *angles[d["name"]]).x
                for d in hand["digits"] if d["name"] != "Thumb")
    grip = {
        "angles": angles,
        "proximal": angles["Middle"][0], "intermediate": angles["Middle"][1],
        "centre_x": centre_x, "centre_z": centre_z,
        "radius": radius, "half_width": HANDLE_HALF_WIDTH,
        "head_z": centre_z + radius + HEAD_RADIUS * 0.62,
        "lens_x": reach + 0.086,
    }
    print(f"GRIP handle_r={radius * 1000:.1f}mm at ({centre_x:+.4f},{centre_z:+.4f}) "
          f"palm_underside={underside * 1000:+.1f}mm head_z={grip['head_z'] * 1000:+.1f}mm "
          f"lens_x={grip['lens_x']:.4f}")
    print("GRIP_ANGLES deg mcp/pip: " + " ".join(
        f"{n}={angles[n][0]:.0f}/{angles[n][1]:.0f}" for n in DIGIT_NAMES))

    strays = {n: v for n, v in reached.items() if abs(v) > 0.006}
    if strays:
        blendkit.fail(
            "these fingertips do not close onto the handle: "
            + " ".join(f"{n}={v * 1000:+.1f}mm" for n, v in sorted(strays.items()))
            + f". The handle is {radius * 2000:.0f} mm and every finger has to reach it "
            "at some flexion inside its own anatomical limit; one that cannot is a digit "
            "whose length or whose knuckle position is wrong.")
    return grip


FINGER_HALF_THICKNESS = 0.008
"""Half a finger's depth, metres. The bone chain is solved onto a circle this much larger
than the handle, so the *skin* touches it rather than the *bone* — the difference is 8 mm
and at 0.35 m from the camera 8 mm is twenty pixels of finger inside a torch."""

THUMB_GRIP_SCALE = 0.72
"""How much of the index finger's solved curl the thumb takes when the fist closes.

Less than one, because a thumb on a tool handle lies **over the fingers** rather than
round the handle — it is shorter than they are and it comes at the grip from the other
side. At 1.0 the thumb wrapped past the barrel and its tip finished inside the index
finger's middle phalanx, which `measure_grip` reports and a render at 0.35 m shows."""

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
        # Per digit, because `solve_grip` closes each finger exactly as far as its own
        # length needs to reach the handle. One shared angle put the little finger 9 mm
        # short of the barrel and the middle finger 5 mm inside it.
        return {"Left": free, "Right": dict(grip["angles"])}
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

    harvest = build_hand()
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


TRI_BUDGET = 60000
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
        _, tip = _curled(digit, *grip["angles"][digit["name"]])
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
