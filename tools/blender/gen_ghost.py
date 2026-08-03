#!/usr/bin/env python3
"""Builds §09's 유령: the dead player, hollow, self-lit, and unable to leave.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_ghost.py

Writes ``Assets/Models/Characters/Ghost.fbx`` with ``Ghost.glb`` beside it and
``Assets/Textures/Ghost.textures.json`` — the manifest ``GhostMaterials.cs`` builds the
six URP materials from, because **FBX cannot carry emission** and emission is what this
model is made of (ART.md §7.11's lesson, applied before it becomes a defect).

WHAT §09 SAYS, AND WHY IT IS A DESIGN AND NOT A CONSTRAINT LIST
---------------------------------------------------------------
| 시야 | 맵 전체를 자유롭게 본다 (벽 통과) |
| 말하기 | **불가능** |
| 신호 | 근처 물건을 아주 약하게 흔든다 (쿨타임 45초) |
| 탈출 | **불가능** |

Every one of those is a restriction, and together they are a character rather than a
penalty: *a person who knows the answer and cannot say it.* §09 names the scene the
whole state exists for — 「방금 뭔가 흔들렸어?」 / 「바람이겠지」 / (유령의 절규) — so
the model has to carry two readings at once. Up close it is somebody's colleague. At the
edge of a beam it is the reason the corridor felt wrong.

So the four restrictions are built, one by one, into geometry:

* **cannot speak** → there is no face. Inside the helmet is a cavity, and the only
  feature in it is ``build_maw``: a mouth open in a shout, and it is the one part of
  this model that does not glow. Everything else is faintly lit from within; the mouth
  is a hole in that. What the eye reads is a shape trying to make a sound.
* **cannot escape** → there is nothing to walk with. The coverall ends in a torn hem
  below the knee (``HEM_Z``) and what continues to the floor is two thin trailing
  wisps, not legs. It is the right height and it cannot take a step.
* **can reach and touch nothing** → the hands are the brightest part of the model by a
  factor of three, and they are the **same built hands the living player has**
  (``gen_player_ai.build_hand``). They were bright because §09 left exactly one channel
  open — 흔들기 — and it was worked with hands. §11 closed that channel, so the hands
  now say the opposite thing and say it better: this figure is all reach and no grip,
  and the eye is still sent to the part that cannot do anything.
* **sees the whole map** → ``HeadCameraAnchor`` is on the rig, in the same place and
  with the same forward axis as the player's, so the free-flying view §09 describes
  attaches to the same bone the living use and needs nothing new.

WHY IT IS NOT A TRANSLUCENT COPY OF THE PLAYER
-----------------------------------------------
A 50 %-alpha duplicate is the cheap answer and it fails this game specifically. §03
builds everything on 「어둠 = 목표의 잠금장치」: the beam is how a player learns anything.
A translucent player is *dimmer where the light is*, so it is read exactly the way every
other object in the game is read — point the torch at it and find out. That is a monster,
not a ghost.

**This one does not answer to the beam.** Its albedo is 0.02–0.04 linear, an order of
magnitude under the darkest §12 wall, and it carries a constant emission instead. Shine
the torch on a wall and the wall brightens; shine it on this and nothing happens. In a
game where light is the entire information channel, a thing that ignores light is the
most unsettling object that can be put in it — and it costs one material property.

The emission is **banded down the figure** rather than uniform, which is the other half
of the look and costs nothing at all: ``Body.shell`` already assigns materials per
segment, so the crown and shoulders are lit at 0.62, the torso and sleeves at 0.34, the
legs and wisps at 0.12. The figure fades downward into the dark it is standing in. A
single emission value reads as a lamp in the shape of a man.

WHAT MAKES IT *THIS* PERSON — the part the brief turns on
----------------------------------------------------------
§04's role colour survives, drained toward the shell but still separated in luma, on the
chest, the helmet band and both upper arms. That is the whole of "the person who just
died": in this game a teammate **is** their role colour, and three living players who
see a drained 주자 red hanging in a doorway know who it is and therefore what has gone
wrong. The silhouette does the rest — the same coverall, the same helmet, the same
shoulder line, at the same 1.75 m.

WHAT IS LOAD-BEARING
--------------------
* ``Assets/Models/Characters/`` is graded as **CharacterHumanoid** by
  ``AssetImportPolicy.ResolveModelCategory`` — everything in that folder except
  ``Monster`` is. So this must import Humanoid with a valid Avatar, must carry all four
  ``PlayerMountBones``, and its largest extent must be within 2 % of 1.750 m. All three
  are the right answer anyway: it is a dead player.
* Unity auto-maps a Humanoid avatar from the **bind pose**, so the rig ships T-posed and
  the hang comes from the clips. ``verify_bind`` asserts it.
* Clip names must be in ``AssetImportPolicy.LoopingAnimationClips`` or
  ``OneShotAnimationClips`` or the validator warns that their loop flag is a guess —
  ``Drift`` is in the first and ``Wail`` in the second. ``Rattle`` was in the second
  and is deleted; its entry in that set is now unused and is listed in this round's
  report as a line to remove.
* Slot 0 is ``Ghost_Role``, matching the player's contract, so anything that swaps
  ``renderer.materials[0]`` per ``RoleId`` works on this model unchanged.
"""

from __future__ import annotations

import json
import math
import os
import sys
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import blendkit  # noqa: E402
import gen_player_ai as gpa  # noqa: E402
import gen_player_model as gpm  # noqa: E402
from blendkit import BoneSpec, MaterialSpec  # noqa: E402


FPS = 30
TRI_BUDGET = 30000
"""The cap this generator fails on. The monster ships 5,704 against 6,000 and the player
5,254 against 7,200; a ghost is seen at §04's 관측 ranges and through walls, never at the
0.35 m the player's own hands are, so it has no reason to be the most expensive."""

MANIFEST_SOURCE = "tools/blender/gen_ghost.py"
MANIFEST_PATH = os.path.join(blendkit.UNITY_ASSETS, "Textures", "Ghost.textures.json")


# ── The look, as numbers ────────────────────────────────────────────────────

ROLE, CROWN, SHELL, HEM, HANDS, MAW = (
    "Ghost_Role", "Ghost_Crown", "Ghost_Shell", "Ghost_Hem", "Ghost_Hands", "Ghost_Maw")
SLOTS = (ROLE, CROWN, SHELL, HEM, HANDS, MAW)

M_ROLE, M_CROWN, M_SHELL, M_HEM, M_HANDS, M_MAW = range(6)

GHOST_ALBEDO = 0.030
"""Linear albedo of every part of this model except the maw.

**This is the number the whole design rests on and it is deliberately absurd.** The
darkest wall in a §12 corridor is 0.21 linear and ``gen_monster_ai`` holds the creature's
hide *under* that, at 0.17, so it does not announce itself. This is an order of magnitude
below the creature: a 2.6-intensity torch at 3 m returns about 3 % of what it returns off
the same wall. The beam does not find this thing, which is the point — §03 makes light
the only way to learn anything and this is the one object that does not answer."""

MAW_ALBEDO = 0.004
"""The mouth. As close to a hole as an opaque surface gets, and no emission at all —
every other face on this model is lit from within, so the maw reads as an absence in
something that is itself barely present."""

EMISSION_SCALE = 0.26
"""What the whole table below is multiplied by before it reaches a material.

**Set by rendering, and the first value was wrong by about four times.** The bands were
authored against each other — a vertical gradient from the crown down to the hem — and
then shipped straight into a pipeline that applies +1.28 EV of post-exposure and a bloom
with a 0.55 threshold on top. Every one of them clipped: `g3_03m_dark.png` came back a
featureless white cut-out of a person, which is the exact failure this file's header
warns about in as many words — *a figure lit evenly is a lamp shaped like a man*. It was
lit unevenly and it was still a lamp.

One scale rather than five new numbers, for the same reason `NightAtmosphere.AmbientGain`
is one number: the gradient between the bands is the design and it is right; only its
absolute level was measured against nothing."""

EMISSION = {
    name: value * EMISSION_SCALE for name, value in {
        CROWN: 0.62,
        ROLE: 0.55,
        SHELL: 0.34,
        HEM: 0.12,
        HANDS: 0.95,
        MAW: 0.0,
    }.items()
}
"""Emission per band, linear, before the colour tint.

Banded rather than uniform, and the band is a **vertical gradient**: 0.62 at the helmet
and shoulders, 0.34 across the torso and sleeves, 0.12 at the legs and the wisps. A
figure lit evenly is a lamp shaped like a man; a figure that fades downward is one that
the floor has already taken half of. Costs nothing — ``Body.shell`` assigns materials per
segment, so this is the same mechanism §04's vest band uses.

The hands are the outlier and they are the design: §09 leaves exactly one channel open
and it is 「근처 물건을 아주 약하게 흔든다」. Three times the crown puts the eye on the
only part of a ghost that can still do anything."""

COLD = (0.62, 0.72, 0.86)
"""The tint every emission is multiplied by. Blue-white, and cold on purpose: §07's night
is warm — the practicals are 0.85/0.88/1.00 and the torch is 1.00/0.96/0.87 — so a cold
source is the only light in the building that did not come from a fitting or a battery."""

HAND_TINT = (0.78, 0.84, 0.92)
"""The hands are nearer white than the shell. They are what the ghost signals with, and
the eye finds the least saturated thing in a dark frame first."""

ROLE_DRAIN = 0.62
"""How much of §04's own colour survives, mixed toward the shell's cold.

**Not zero, and that is the load-bearing part of this file.** In this game a teammate
*is* their role colour, so a colourless ghost is anonymous and the whole §09 scene — the
living going the wrong way while somebody who knows watches — loses the fact that they
know *who* is watching. Not one either: a ghost at full §04 saturation is a living
player, and the first thing anyone would do is call out to it.

It started at 0.34 and ``verify_look`` rejected it, which is the check earning its keep.
Draining toward a constant preserves hue and **compresses value**, and §04's five are
separated by value on purpose because §05 makes the game dark and hue discrimination is
the first thing to go. At 0.34 the two closest roles came out 0.012 of luma apart — one
code value at the brightness these are emitted at, which is two teammates nobody can
tell apart after they die."""


def role_gain(colours) -> float:
    """The scale that puts the *average* drained role at the shell's own brightness.

    Applied after the drain and before the emission, and it is what stops the role band
    being the dimmest thing on a figure whose whole job is to say who it was. A flat
    normalisation per colour would have done the same for brightness and destroyed the
    value separation the drain is already threatening; one gain across all five moves
    them together and leaves the gaps between them intact.
    """
    mean = sum(gpm.luma(c) for c in colours) / max(1, len(colours))
    return gpm.luma(COLD) / max(1e-6, mean)


# ── Geometry ────────────────────────────────────────────────────────────────

HEM_Z = 0.455
"""Where the coverall's legs end, in metres. Just below the knee (``gpm.KNEE_Z`` 0.50).

Below this there is no leg — §09's 「탈출 불가능」 is not a rule the model is told about,
it is a thing the model cannot do. A ghost with boots on could walk out of the basement
and the picture would be arguing with the design."""

WISP_SIDES = 6
"""The trailing wisps are hexagonal. They are 12–90 mm across and never closer than about
2 m to anybody, and six sides is where a taper stops reading as a cylinder."""

HELMET_CROWN = 1.750
SHOULDER_DROP = 0.028
"""Metres the shoulder line is dropped against the living player's.

The bind pose has to stay a T-pose for Unity's auto-mapper, so the slump that makes this
read as a body hanging rather than standing lives in the clips. This is the part of it
that can be built in: a hollow suit has nothing holding its shoulders up."""


class GhostBody(gpm.Body):
    """``gen_player_model.Body`` with the ghost's own slot table.

    The base class maps a face's material index through ``gen_player_model.SLOTS``,
    because on the player the slot ORDER is §04's contract and the indices are named
    constants. This model has six materials of its own and none of those names, so the
    map is the identity — but everything else about the accumulator is wanted verbatim:
    authored per-vertex weights, faces that refuse a material the mesh does not carry,
    and the assertion that ``to_mesh`` did not reorder a single vertex.
    """

    def __init__(self, slots) -> None:
        self.slots = tuple(slots)
        self._local = {i: i for i in range(len(self.slots))}
        self.bm = bmesh.new()
        self.bverts = []
        self.positions = []
        self.weights = []


def torso_rings():
    """Pelvis → collar. The living player's silhouette, emptied.

    Narrower than ``gen_player_model.build_torso`` at every station and narrowest at the
    chest, which is the opposite of a person: a coverall with nobody in it collapses
    where the ribcage was and keeps its width only at the seams. The hips stay wide
    because a garment hangs from the shoulders and pools at the belt.
    """
    z = gpm.SHOULDER_Z - SHOULDER_DROP
    return [
        (0.830, 0.118, 0.088, {"Hips": 1.0}),
        (0.895, 0.146, 0.099, {"Hips": 1.0}),
        (0.960, 0.152, 0.101, {"Hips": 0.70, "Spine": 0.30}),
        (1.040, 0.126, 0.089, {"Hips": 0.25, "Spine": 0.75}),
        (1.115, 0.111, 0.083, {"Spine": 1.0}),            # the collapse
        (1.205, 0.126, 0.092, {"Spine": 0.40, "Chest": 0.60}),
        (1.310, 0.148, 0.104, {"Chest": 1.0}),
        (1.400, 0.156, 0.101, {"Chest": 0.30, "UpperChest": 0.70}),
        (z, 0.104, 0.082, {"UpperChest": 1.0}),
    ]


def build_torso(b: gpm.Body) -> None:
    """The coverall. §04's colour on the chest and upper back, as on the living."""
    rings = torso_rings()
    mats = [M_SHELL] * (len(rings) - 1)
    for seg in (4, 5, 6):
        mats[seg] = M_ROLE          # the vest band, in the same place §04 puts it
    mats[7] = M_CROWN               # the shoulder line, where the figure starts to glow
    b.shell([(gpm.ellipse((0, 0, z), gpm.X, gpm.Y, rx, ry, gpm.SIDES_BODY), w)
             for z, rx, ry, w in rings], mats)


def build_head(b: gpm.Body) -> None:
    """Neck, helmet, and the cavity where a face was.

    The helmet is the living player's — same crown height, same band — because that is
    what makes this read as one of the four rather than as a ghost. What is different is
    underneath it: no jaw, no brow, no eyes. ``build_maw`` puts the only feature there is
    into the hollow.
    """
    shoulder = gpm.SHOULDER_Z - SHOULDER_DROP
    b.shell([(gpm.ellipse((0, -0.004, shoulder - 0.010), gpm.X, gpm.Y, 0.062, 0.058,
                          gpm.SIDES_NECK), {"UpperChest": 0.6, "Neck": 0.4}),
             (gpm.ellipse((0, -0.010, 1.520), gpm.X, gpm.Y, 0.052, 0.050, gpm.SIDES_NECK),
              {"Neck": 1.0}),
             (gpm.ellipse((0, -0.014, 1.566), gpm.X, gpm.Y, 0.057, 0.056, gpm.SIDES_NECK),
              {"Neck": 0.35, "Head": 0.65})], M_SHELL)

    # The helmet: a dome over a hollow. Its lowest ring is wider than the neck it sits
    # over, so from below there is a rim and a shadow under it rather than a join.
    dome = [
        (1.588, 0.098, 0.108, {"Head": 1.0}),
        (1.628, 0.104, 0.116, {"Head": 1.0}),
        (1.676, 0.100, 0.110, {"Head": 1.0}),
        (1.718, 0.079, 0.086, {"Head": 1.0}),
        (HELMET_CROWN, 0.041, 0.045, {"Head": 1.0}),
    ]
    mats = [M_ROLE, M_CROWN, M_CROWN, M_CROWN]
    b.shell([(gpm.ellipse((0, -0.014, z), gpm.X, gpm.Y, rx, ry, gpm.SIDES_NECK), w)
             for z, rx, ry, w in dome], mats)

    # The cavity. A second, smaller dome inside the first, and dark: the helmet's front
    # is open and what a beam finds inside it is this.
    b.shell([(gpm.ellipse((0, -0.010, 1.592), gpm.X, gpm.Y, 0.070, 0.076, gpm.SIDES_NECK),
              {"Head": 1.0}),
             (gpm.ellipse((0, -0.012, 1.660), gpm.X, gpm.Y, 0.074, 0.080, gpm.SIDES_NECK),
              {"Head": 1.0}),
             (gpm.ellipse((0, -0.012, 1.712), gpm.X, gpm.Y, 0.052, 0.056, gpm.SIDES_NECK),
              {"Head": 1.0})], M_MAW)
    build_maw(b)


MAW_HALF_HEIGHT = 0.043
MAW_HALF_WIDTH = 0.026
MAW_DEPTH = 0.052
"""The mouth, in metres: 86 mm tall, 52 mm across and 52 mm deep.

Deliberately too big for a face. A mouth at anatomical scale on a head with no other
feature reads as damage; one this size reads as **a shout**, and §09's whole content is
that the shout does not arrive. Depth matters as much as the opening — a shallow dimple
catches the beam on its far wall and becomes a light-coloured patch, which is the exact
opposite of the read.
"""


def build_maw(b: gpm.Body) -> None:
    """The one feature in the face, and the only unlit surface on the model.

    A tapering shaft driven back into the helmet's cavity rather than a hole cut in a
    surface, because there is no surface to cut: the head is two nested domes and what
    is wanted is something that is unmistakably **deep**. The far end is capped and
    carries the same material, so at any angle a beam finds it the answer is the same —
    nothing comes back.
    """
    front, back = -0.082, -0.082 + MAW_DEPTH
    rings = [
        (front, MAW_HALF_WIDTH, MAW_HALF_HEIGHT),
        (front + MAW_DEPTH * 0.45, MAW_HALF_WIDTH * 0.72, MAW_HALF_HEIGHT * 0.78),
        (back, MAW_HALF_WIDTH * 0.30, MAW_HALF_HEIGHT * 0.34),
    ]
    b.shell([(gpm.ellipse((0.0, y, 1.646), gpm.X, gpm.Z, rx, rz, 10), {"Head": 1.0})
             for y, rx, rz in rings], M_MAW)


def sleeve_rings(side: int):
    """Shoulder → wrist. ``gen_player_ai``'s arm, thinner and without the deltoid cap.

    Kept as a real arm silhouette — deltoid, bicep, elbow, forearm belly — because that
    is what says *there is a person's shape in this sleeve*, and a cone says nothing at
    all. Every radius is 8 % under the living player's: an empty garment does not fill.
    """
    s = "Left" if side > 0 else "Right"
    z = gpm.SHOULDER_Z - SHOULDER_DROP
    return [
        (0.062, 0.072, 0.058, {"UpperChest": 1.0}),
        (0.148, 0.070, 0.076, {"UpperChest": 0.45, f"{s}Shoulder": 0.55}),
        (0.196, 0.062, 0.072, {f"{s}Shoulder": 0.40, f"{s}UpperArm": 0.60}),
        (0.262, 0.053, 0.057, {f"{s}UpperArm": 1.0}),
        (0.352, 0.047, 0.050, {f"{s}UpperArm": 1.0}),
        (0.442, 0.041, 0.044, {f"{s}UpperArm": 0.78, f"{s}LowerArm": 0.22}),
        (0.492, 0.039, 0.043, {f"{s}UpperArm": 0.22, f"{s}LowerArm": 0.78}),
        (0.556, 0.043, 0.046, {f"{s}LowerArm": 1.0}),
        (0.640, 0.035, 0.038, {f"{s}LowerArm": 1.0}),
    ], z


def build_arm(b: gpm.Body, side: int, hand: dict) -> None:
    """One sleeve, its role-coloured cuff, and the ghost's hand inside it."""
    rings, z = sleeve_rings(side)
    rings = [(x, ru, rv, w) for x, ru, rv, w in rings]
    cuff = gpa.cuff_rings(side, hand["size"]["sections"])
    first_cuff = len(rings)
    every = [(x, 0.0, 0.0, ru, rv, w) for x, ru, rv, w in rings]
    every += cuff

    mats = [M_SHELL] * (len(every) - 1)
    mats[0] = M_CROWN               # inside the torso, at the shoulder
    mats[1] = M_ROLE                # the deltoid cap, §04's colour where a beam clips it
    for seg in range(first_cuff, first_cuff + gpa.CUFF_SEGMENTS):
        mats[seg] = M_ROLE
    b.shell([(gpm.ellipse((side * x, cy, z + cz), gpm.Y, gpm.Z, ru, rv, gpm.SIDES_LIMB), w)
             for x, cy, cz, ru, rv, w in every], mats)
    weld_hand(b, side, hand, z)


def weld_hand(b: gpm.Body, side: int, hand: dict, shoulder_z: float) -> None:
    """The living player's hand, copied in and painted with the ghost's brightest slot.

    **The same geometry, on purpose.** It is the one part of this model a player is meant
    to look at — §09 gives a ghost exactly one thing it can do and it does it with its
    hands — and building a second, worse hand for it would be building the defect this
    pass exists to remove, twice.
    """
    mesh = hand["object"].data
    weights = hand["weights"]
    sign = 1.0 if side > 0 else -1.0

    remap: dict[int, int] = {}
    for i, vertex in enumerate(mesh.vertices):
        p = vertex.co
        world = (sign * (gpm.WRIST_X + p.x), p.y, shoulder_z + p.z)
        w = {(name if side > 0 else gpa._swap_side(name)): value
             for name, value in weights[i].items()}
        remap[i] = b.vert(Vector(world), w)

    for poly in mesh.polygons:
        idx = [remap[v] for v in poly.vertices]
        if side < 0:
            idx.reverse()
        b.face(tuple(idx), M_HANDS)


HEM_TEETH = 7
HEM_BITE = 0.019
"""The torn hem: how many notches round each leg and how deep, in metres.

A cylinder cut square across reads as a cut pipe. The notches are what make it read as
cloth that has come apart, and they are the last hard silhouette on the figure before
the wisps — everything below is soft, so this edge does the work of saying where the
body stopped."""


def build_leg(b: gpm.Body, side: int) -> None:
    """Hip to a torn hem below the knee, then a wisp that reaches the floor.

    §09's 「탈출 불가능」, built. There is no foot, no ankle and no boot: the coverall
    frays out at ``HEM_Z`` and what continues is not a leg. The wisp still reaches z = 0
    because ``AssetImportPolicy`` anchors this model's largest extent to 1.750 m and
    because a figure that stops in mid-air is a figure somebody forgot to finish — the
    reading wanted is *dissolving into the floor*, not *floating above it*.
    """
    s = "Left" if side > 0 else "Right"
    x = side * gpm.LEG_X
    rings = [
        (0.980, -0.006, 0.088, 0.094, {"Hips": 1.0}),
        (0.860, -0.008, 0.079, 0.086, {"Hips": 0.35, f"{s}UpperLeg": 0.65}),
        (0.700, -0.008, 0.070, 0.076, {f"{s}UpperLeg": 1.0}),
        (0.566, -0.012, 0.061, 0.066, {f"{s}UpperLeg": 0.78, f"{s}LowerLeg": 0.22}),
        (0.500, -0.014, 0.056, 0.061, {f"{s}UpperLeg": 0.22, f"{s}LowerLeg": 0.78}),
    ]
    mats = [M_SHELL, M_SHELL, M_HEM, M_HEM]
    b.shell([(gpm.ellipse((x, y, z), gpm.X, gpm.Y, rx, ry, gpm.SIDES_LIMB), w)
             for z, y, rx, ry, w in rings], mats, cap_end=False)

    # The torn hem. Alternate vertices are pulled down and in, so the last edge of the
    # garment is a ring of teeth rather than a rim.
    hem = []
    for i in range(gpm.SIDES_LIMB):
        a = 2.0 * math.pi * (i + 0.5) / gpm.SIDES_LIMB
        bite = HEM_BITE * (0.30 + 0.70 * (i * HEM_TEETH % gpm.SIDES_LIMB)
                           / float(gpm.SIDES_LIMB))
        hem.append(Vector((x + 0.052 * math.cos(a), -0.014 + 0.058 * math.sin(a),
                           HEM_Z - bite)))
    b.shell([[gpm.ellipse((x, -0.014, 0.500), gpm.X, gpm.Y, 0.056, 0.061, gpm.SIDES_LIMB),
              {f"{s}UpperLeg": 0.22, f"{s}LowerLeg": 0.78}],
             [hem, {f"{s}LowerLeg": 1.0}]], M_HEM, cap_start=False, cap_end=False)

    build_wisp(b, side)


def build_wisp(b: gpm.Body, side: int) -> None:
    """What hangs below the hem: a tapering thread, drifting back and inward.

    Not a sheet and not a tail. It is 90 mm across where the leg left off and 12 mm at
    the floor, and it curves toward the model's centre line so the two read as one
    trailing mass rather than as two amputated limbs — which is the difference between
    *dissolving* and *injured*, and only one of those is §09.
    """
    s = "Left" if side > 0 else "Right"
    x = side * gpm.LEG_X
    # Weighted down the ankle chain rather than all to LowerLeg, and that is not
    # bookkeeping: `Foot` and `Toes` still exist on a Humanoid rig whether or not there
    # is a boot on them, `verify` requires every deforming bone to own geometry, and
    # hanging the wisp off them gives it the trailing lag a leg's own aim already
    # produces in every clip. The thing that used to be a foot is what still drags.
    rings = [
        (0.470, x, -0.014, 0.045, 0.048, {f"{s}LowerLeg": 1.0}),
        (0.360, x * 0.86, -0.002, 0.036, 0.038, {f"{s}LowerLeg": 0.72, f"{s}Foot": 0.28}),
        (0.250, x * 0.66, 0.018, 0.026, 0.027, {f"{s}LowerLeg": 0.25, f"{s}Foot": 0.75}),
        (0.140, x * 0.44, 0.042, 0.016, 0.017, {f"{s}Foot": 0.62, f"{s}Toes": 0.38}),
        (0.040, x * 0.24, 0.062, 0.008, 0.008, {f"{s}Foot": 0.18, f"{s}Toes": 0.82}),
        (0.000, x * 0.16, 0.072, 0.004, 0.004, {f"{s}Toes": 1.0}),
    ]
    b.shell([(gpm.ellipse((cx, cy, z), gpm.X, gpm.Y, rx, ry, WISP_SIDES), w)
             for z, cx, cy, rx, ry, w in rings], M_HEM)


def build_ghost(hand: dict) -> bpy.types.Object:
    """The whole figure, as one mesh."""
    b = GhostBody(SLOTS)
    build_torso(b)
    build_head(b)
    for side in (1, -1):
        build_arm(b, side, hand)
        build_leg(b, side)
    return b.finish("Ghost")


# ── The rig ─────────────────────────────────────────────────────────────────


def bone_specs(hand: dict) -> list[BoneSpec]:
    """The player's own rig, verbatim, plus the twenty finger bones.

    Verbatim is the point. ``AssetImportPolicy`` grades this folder as Humanoid, the four
    mount bones are asserted by ``AssetImportValidator``, and — the reason that is a
    feature rather than a hoop — a ghost on the player's skeleton retargets the player's
    own clips. If §09's wiring ever wants a dead player to keep moving the way they moved
    while alive, the rig is already the same rig.
    """
    return gpa.bone_specs(hand)


# ── Clips ───────────────────────────────────────────────────────────────────


def hang(**over) -> dict:
    """The pose everything else is a variation on: a body hanging from its own shoulders.

    The bind pose is a T-pose because Unity's Humanoid auto-mapper reads the bind pose,
    so the slump has to live here. Arms down and slightly forward, head tilted back and
    over, spine curved — a figure suspended rather than standing, which is also the only
    pose that makes sense for something with nothing to stand on.
    """
    spec = gpm.merge(
        gpm.torso(lean=-7.0, tilt=0.0),
        gpm.head(lean=-16.0, tilt=3.0, neck=-9.0),
        gpm.arm(1, up_down=64.0, up_swing=14.0, lo_down=22.0, lo_swing=10.0),
        gpm.arm(-1, up_down=66.0, up_swing=-12.0, lo_down=24.0, lo_swing=-8.0),
        gpm.leg(1, thigh=6.0, shank=-16.0),
        gpm.leg(-1, thigh=-4.0, shank=-20.0),
    )
    spec.update(over)
    return spec


DRIFT_FRAMES = 121
"""Four seconds at 30 fps. Slow, and slower than anything else in the game: §06's 순찰 is
a walk and §05's 걷기 is 2 m/s. A ghost that moves at a living pace is a player."""

DRIFT_RISE = 0.055
"""Metres the whole figure rises and falls over the drift cycle. Small — the read wanted
is *not quite still*, which is far more unsettling than motion."""


def drift_clip(rig: bpy.types.Object):
    """The idle. A slow vertical breath and a sway the figure does not drive."""
    poses = []
    for step in range(5):
        frame = 1 + step * (DRIFT_FRAMES - 1) // 4
        phase = 2.0 * math.pi * step / 4.0
        rise = DRIFT_RISE * 0.5 * (1.0 - math.cos(phase))
        poses.append(gpm.make_pose(frame, hang(
            **gpm.merge(gpm.torso(lean=-7.0 + 2.4 * math.sin(phase),
                                  tilt=1.8 * math.sin(phase + 1.0)),
                        gpm.head(lean=-16.0 - 3.0 * math.sin(phase + 0.7),
                                 yaw=5.0 * math.sin(phase + 2.0), neck=-9.0))),
            hips_world=(0.0, 0.02 * math.sin(phase), gpm.HIP_Z + rise)))
    return blendkit.make_action(rig, "Drift", poses, loop=True)


# DELETED with §09's 신호: `rattle_clip`. It was 「근처 물건을 아주 약하게 흔든다」
# on a 45 s cooldown, built as a reach that costs everything the figure has —
# gather, throw, recoil — because the design's own note is 「아주 약하게」 and one
# attempt used it all up. §11's 탈락자 rule deleted the channel: 「살아 있는
# 사람에게 개입할 수 없다」, and §12 uses sound as a map, so a placed noise is a
# forged footstep from somebody who can see the whole floor. GhostState's
# TryRattle / CanRattle / RattleCooldownRemaining, GameConstants'
# GhostRattleCooldownSeconds and GhostRattleRange, GhostOverlay's RattleBar and
# the four ghost_rattle_*.wav clips all went in the round that made the ghost
# watch-only. This clip on the rig was the last trace of it.

def wail_clip(rig: bpy.types.Object):
    """The thing §09 says cannot happen: 「말하기 불가능」, attempted.

    The design's own best moment is 「방금 뭔가 흔들렸어?」 / 「바람이겠지」 / (유령의
    절규), and this is the third line. Head back, shoulders heaving, the whole figure
    convulsing — and no sound, because there is no sound to make. It is the only clip in
    the game whose entire content is a failure.
    """
    poses = [
        gpm.make_pose(1, hang(), hips_world=(0.0, 0.0, gpm.HIP_Z)),
        gpm.make_pose(14, hang(**gpm.merge(
            gpm.torso(lean=-18.0),
            gpm.head(lean=-46.0, neck=-24.0),
            gpm.arm(1, up_down=52.0, up_swing=-26.0, lo_down=30.0, lo_swing=-16.0),
            gpm.arm(-1, up_down=54.0, up_swing=26.0, lo_down=32.0, lo_swing=16.0))),
            hips_world=(0.0, 0.0, gpm.HIP_Z + 0.06)),
        gpm.make_pose(24, hang(**gpm.merge(
            gpm.torso(lean=-24.0, tilt=4.0),
            gpm.head(lean=-52.0, neck=-28.0))),
            hips_world=(0.0, -0.02, gpm.HIP_Z + 0.09)),
        gpm.make_pose(33, hang(**gpm.merge(
            gpm.torso(lean=-19.0, tilt=-4.0),
            gpm.head(lean=-48.0, neck=-25.0))),
            hips_world=(0.0, 0.01, gpm.HIP_Z + 0.05)),
        gpm.make_pose(62, hang(), hips_world=(0.0, 0.0, gpm.HIP_Z)),
    ]
    return blendkit.make_action(rig, "Wail", poses, loop=False)


CLIPS = (("Drift", drift_clip, True), ("Wail", wail_clip, False))
"""Two clips, and the second is a failure.

`Drift` is the hover the ghost is always in — 탈출 불가능, and no way to travel of
its own. `Wail` is 「말하기 불가능」 attempted: head back, shoulders heaving, no
sound, because there is no sound to make. Neither reaches a living runner, which
after §11 is the whole specification for a 탈락자.
"""


# ── Materials and the manifest ──────────────────────────────────────────────


def role_colour(base) -> tuple:
    """§04's colour, drained toward the shell's cold. See ``ROLE_DRAIN``."""
    return tuple(base[i] * ROLE_DRAIN + COLD[i] * (1.0 - ROLE_DRAIN) * 0.30
                 for i in range(3))


def _entry(name: str, colour, emission: float, albedo: float = GHOST_ALBEDO) -> dict:
    return {
        "name": name,
        "base_color_linear": [round(albedo * c, 6) for c in colour],
        "emission_linear": [round(emission * c, 6) for c in colour],
        "emission_strength": round(emission, 4),
        "roughness": 0.86,
        "metallic": 0.0,
    }


def material_entries() -> list[dict]:
    """Every material this model needs, as the numbers ``GhostMaterials.cs`` will apply.

    Eleven, not six: the six the mesh carries, plus **one drained §04 colour per role**.
    Slot 0 is ``Ghost_Role`` and the player's contract is that slot 0 is swapped per
    ``RoleId`` on every renderer, so the five variants are built as assets for that swap
    to land on. Without them a ghost is anonymous, and an anonymous ghost throws away the
    only thing that makes §09's best moment work — the living do not just see somebody
    watching them go the wrong way, they see **who**.
    """
    tint = {HANDS: HAND_TINT}
    out = [_entry(name, tint.get(name, COLD), EMISSION[name],
                  MAW_ALBEDO if name == MAW else GHOST_ALBEDO)
           for name in SLOTS]

    drained = [role_colour(spec.color) for spec in gpm.MATERIAL_SPECS
               if spec.name in gpm.ROLE_MATERIALS]
    gain = role_gain(drained)
    names = [spec.name for spec in gpm.MATERIAL_SPECS if spec.name in gpm.ROLE_MATERIALS]
    for name, colour in zip(names, drained):
        out.append(_entry("Ghost_" + name, tuple(c * gain for c in colour), EMISSION[ROLE]))
    return out


def write_manifest(entries: list[dict], tris: int) -> None:
    os.makedirs(os.path.dirname(MANIFEST_PATH), exist_ok=True)
    payload = {
        "generated_by": MANIFEST_SOURCE,
        "model": "Assets/Models/Characters/Ghost.fbx",
        "note": "§09's 유령. Albedo is an order of magnitude under the darkest §12 wall "
                "and the read is carried by emission, so a torch finds nothing — see the "
                "generator's header. FBX carries no emission, which is why this file "
                "exists (ART.md §7.11).",
        "slot_order": list(SLOTS),
        "role_drain": ROLE_DRAIN,
        "triangles": tris,
        "materials": entries,
    }
    with open(MANIFEST_PATH, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print(f"MANIFEST {MANIFEST_PATH}")


# ── Verification ────────────────────────────────────────────────────────────


def verify_bind(rig: bpy.types.Object) -> None:
    """The bind pose must be the T-pose, or Unity's Humanoid mapper cannot read it."""
    for side in ("Left", "Right"):
        at = gpm.bone_point(rig, side + "Hand", (0.0, 0.0, 0.0))
        if abs(abs(at.x) - gpm.WRIST_X) > 0.002:
            blendkit.fail(
                f"{side}Hand sits at x={at.x:.3f} m in the bind pose, not "
                f"±{gpm.WRIST_X:.3f}. Unity auto-maps a Humanoid avatar from the bind "
                "pose, and this folder's import policy requires a valid human Avatar — "
                "so a rig that ships hanging cannot be mapped at all.")


def verify_look(obj: bpy.types.Object, entries: list[dict]) -> None:
    """The two claims this model's whole design rests on, asserted where they are made."""
    roles = [e for e in entries if e["name"].startswith("Ghost_Role_")]
    if len(roles) != len(gpm.ROLE_MATERIALS):
        blendkit.fail(f"{len(roles)} drained role colours against §04's "
                      f"{len(gpm.ROLE_MATERIALS)}. Slot 0 is swapped per RoleId and a "
                      "missing variant is a teammate nobody can identify after they die.")
    # Measured on the colours rather than on the emitted values, and the same 0.05
    # `PlayerMaterials.MinimumRoleLumaGap` holds the living five to. The five are scaled
    # by one shared EMISSION_SCALE, so the emitted gap moves with the exposure of the
    # whole model while the thing that has to be true — that these five are separated in
    # VALUE, because §05 makes the game dark and hue goes first — is a property of the
    # colours. Checking the product instead made this fail every time the model was
    # dimmed, which is measuring the wrong thing loudly.
    lumas = sorted(gpm.luma(e["emission_linear"]) / max(1e-6, EMISSION[ROLE]) for e in roles)
    gap = min(b - a for a, b in zip(lumas, lumas[1:]))
    if gap < 0.05:
        blendkit.fail(f"two drained role colours are {gap:.3f} apart in luma, under 0.05. "
                      "§05 makes the game dark, hue goes first, and the drain is what puts "
                      "them at risk — value separation is the whole reason §04's five are "
                      "spread.")
    print(f"GHOST_ROLES drained x{ROLE_DRAIN:.2f}, min luma gap {gap:.3f}: "
          + " ".join(f"{e['name'][11:]}="
                     f"{gpm.luma(e['emission_linear']) / max(1e-6, EMISSION[ROLE]):.3f}"
                     for e in roles))

    lit = [e for e in entries if e["emission_strength"] > 0.0]
    if len(lit) < 4:
        blendkit.fail("fewer than four of the ghost's materials emit. The figure's "
                      "vertical gradient is what stops it reading as a lamp shaped like "
                      "a man, and it needs bands to be a gradient.")

    darkest = min(e["emission_strength"] for e in lit)
    if EMISSION[HANDS] < darkest * 3.0:
        blendkit.fail(f"the hands emit {EMISSION[HANDS]:.2f} against a dimmest band of "
                      f"{darkest:.2f}. §09 leaves exactly one channel open and it is "
                      "worked with hands; if they are not the brightest thing on the "
                      "model by a wide margin, the eye is being sent somewhere else.")

    if GHOST_ALBEDO > 0.05:
        blendkit.fail(f"the shell's albedo is {GHOST_ALBEDO:.3f} linear. Over about 0.05 "
                      "a §03 torch starts to find it, and a ghost that answers to the "
                      "beam is read the way every other object in the game is read.")

    if any(e["emission_strength"] > 0.0 for e in entries if e["name"] == MAW):
        blendkit.fail("the maw emits. It is the only unlit surface on the model and that "
                      "contrast is the whole face.")

    low = min(v.co.z for v in obj.data.vertices)
    if low > 0.02:
        blendkit.fail(f"the model's lowest point is z={low:.3f} m. The wisps have to "
                      "reach the floor: a figure that stops in mid-air reads as unfinished "
                      "rather than as dissolving, and the import policy anchors the "
                      "largest extent to 1.750 m.")


def main() -> None:
    blendkit.reset_scene()
    blendkit.set_frame_range(1, DRIFT_FRAMES)

    entries = material_entries()
    # Only the six the mesh actually paints with. The five drained §04 variants go in the
    # manifest and become assets on the Unity side: they are what `renderer.materials[0]`
    # is swapped to per RoleId, and a Blender material with no faces on it still exports
    # as a slot, which would put five dead slots on every ghost renderer in the game.
    by_name = {e["name"]: e for e in entries}
    for name in SLOTS:
        entry = by_name[name]
        blendkit.make_material(MaterialSpec(
            entry["name"], tuple(entry["base_color_linear"]),
            roughness=entry["roughness"], metallic=entry["metallic"]))

    # The living player's own hand, built by the file that owns hands. The ghost is a
    # dead player and building it a second, worse hand would be shipping the defect this
    # whole pass exists to remove, twice.
    hand = gpa.build_hand()

    ghost = build_ghost(hand)
    bpy.data.objects.remove(hand["object"], do_unlink=True)
    blendkit.triangulate(ghost)
    blendkit.shade_smooth(ghost, angle_degrees=44.0)
    blendkit.uv_smart_project(ghost)

    specs = bone_specs(hand)
    rig = blendkit.build_armature("Ghost_Rig", specs)
    for name in gpm.MOUNTS:
        rig.data.bones[name].use_deform = False
    gpm.cache_rig(rig)
    blendkit.bind_skin(ghost, rig, auto_weights=False)
    verify_bind(rig)

    weighted = set(ghost.vertex_groups.keys())
    orphans = [b.name for b in rig.data.bones if b.use_deform and b.name not in weighted]
    if orphans:
        blendkit.fail("bones with no geometry on them: " + ", ".join(orphans)
                      + ". A deforming bone the mesh does not use animates nothing, and "
                      "the ghost's clips key every bone on every frame.")

    actions = []
    for name, build, loop in CLIPS:
        action = build(rig)
        blendkit.stash_action(rig, action)
        actions.append((name, action, loop))
        stats = gpm.measure_action(action)
        print(f"GHOST_CLIP {name:7s} frames={stats['start']}-{stats['end']} "
              f"({stats['frames']:3d}f, {stats['seconds']:.2f}s) loop={int(loop)} "
              f"curves={stats['curves']} keys={stats['keys']} "
              f"max_bone_motion={stats['max_deg']:.1f}deg")
    for track in rig.animation_data.nla_tracks:
        for strip in track.strips:
            strip.extrapolation = "NOTHING"
            strip.blend_in = 0.0
            strip.blend_out = 0.0
    rig.animation_data.action = None
    gpm.clear_pose(rig)
    bpy.context.scene.frame_set(0)

    verify_look(ghost, entries)

    fbx_path = blendkit.out_path("Characters", "Ghost.fbx")
    glb_path = os.path.join(os.path.dirname(fbx_path), "Ghost.glb")
    blendkit.export_fbx(fbx_path, objects=[rig, ghost], with_animation=True)
    blendkit.export_gltf(glb_path, with_animation=True)

    report = blendkit.assert_asset(
        blendkit.describe(fbx_path),
        max_triangles=TRI_BUDGET,
        expect_bones=len(specs),
        expect_actions=len(CLIPS),
        max_dimension=2.0,
    )
    blendkit.print_report(report)
    write_manifest(entries, report.triangles)

    span, depth, height = report.size
    if abs(height - HELMET_CROWN) > HELMET_CROWN * 0.02:
        blendkit.fail(f"the ghost stands {height:.3f} m against the player's "
                      f"{HELMET_CROWN:.3f}. AssetImportPolicy anchors everything in "
                      "Assets/Models/Characters to PlayerHeightMetres within 2 %, and a "
                      "ghost that is not the size of the person who died is not them.")

    takes = gpm.fbx_objects(fbx_path, b"AnimStack")
    missing = [n for n, _, _ in CLIPS if n not in takes]
    if missing:
        blendkit.fail("clips missing from the FBX: " + ", ".join(missing))
    models = gpm.fbx_objects(fbx_path, b"Model")
    for socket in gpm.MOUNTS:
        if socket not in models:
            blendkit.fail(f"{socket} is not in the exported hierarchy. "
                          "AssetImportValidator fails every model in this folder without "
                          "all four, and §09's free camera hangs off HeadCameraAnchor.")
    fbx_mats = gpm.fbx_objects(fbx_path, b"Material")
    absent = [m for m in SLOTS if m not in fbx_mats]
    if absent:
        blendkit.fail("materials missing from the FBX: " + ", ".join(absent))

    print(f"GHOST_LOOK albedo={GHOST_ALBEDO:.3f}lin (corridor wall 0.21, monster hide "
          f"0.17) role_drain={ROLE_DRAIN:.2f} bands="
          + " ".join(f"{n.split('_')[1]}={EMISSION[n]:.2f}" for n in SLOTS))
    print(f"FILES {fbx_path} {glb_path}")
    print(f"ASSET_REPORT Ghost tris={report.triangles} height={height:.3f} "
          f"span={span:.3f} bones={len(specs)} clips={len(CLIPS)} "
          f"materials={len(SLOTS)}")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_ghost.py raised:\n" + traceback.format_exc())
