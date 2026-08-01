#!/usr/bin/env python3
"""Generates the player character: mesh, Humanoid rig, mounts and nine animations.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_player_model.py

Outputs ``Assets/Models/Characters/Player.fbx`` (+ ``Player.glb`` for eyeballing).

WHY THIS ASSET EXISTS
---------------------
§05 corrects an earlier cost estimate in one line: **"1인칭이어도 캐릭터 모델이 필요하다.
협동 게임에서는 다른 3명이 보여야 한다."** The player never sees their own body, but three
teammates see it constantly, and §16-1 lists this model together with the monster as the
one pair of assets that **cannot be worked around**.

§05 then scopes it exactly: *플레이어 모델 1종* (per-role colour and prop differences only)
+ *애니메이션 4종* (서기 / 걷기 / 달리기 / 운반) + a flashlight attachment. This generator
produces that, plus the three states the rest of the document implies but §05's list does
not name (§08's two-person carry, §12's concealment crouch, §09's death → ghost).

WHAT THE DESIGN DICTATES ABOUT ITS SHAPE
----------------------------------------
* **Ordinary on purpose.** The monster (gen_monster_model.py) owns the memorable
  silhouette: 2.34 m, four-segment arms, digitigrade. This is a 1.75 m person with real
  anthropometry (0.344 m shoulder breadth, 0.336 m hip breadth, 1.635 m eye height) and
  1252 of its 4000 triangles spent on nothing but readable shape.
* **§05 — the flashlight is a pointing device.** *"손전등이 포인터가 된다"*, and §13
  networks camera **yaw and pitch** because *"남의 손전등 방향이 정보다"*. So in every
  non-carry clip the right arm is posed **holding the light out in front**, not swinging
  freely: a beam that flails with an arm swing would destroy the signal it is supposed
  to carry. ``FlashlightMount`` is a bone on the right hand and the script measures
  where it ends up and which way it faces.
* **§03 — the objective takes both hands.** *"양손을 쓴다 → 손전등을 들 수 없다"* and
  *"운반자는 앞을 보지 못한다"*. `Carry` therefore reads as occupied hands at a glance:
  both forearms in, hands meeting 0.3 m in front of the chest, torso tipped **back** to
  counter the load, head pitched down over it. The script asserts the hand separation,
  the backward lean, and that the hands actually reach ``ObjectiveMount``.
* **§05 speed table is the animation spec.** 걷기 2.0 / 달리기 4.5 m/s are the authored
  cadences, measured from the posed feet rather than asserted from a comment: step length
  comes from evaluating the rig at the contact frame and reading the real distance between
  the two ball-of-foot positions. `Carry` is authored at
  ``WalkSpeed × ObjectiveCarrySpeedMultiplier`` = 2.0 × 0.80 = 1.6 m/s to match
  GameConstants. 주자's 5.6 m/s sprint is `Run` played at 1.24× by the Animator — a
  separate clip would only be a second copy of the same curves.
* **§04 관측자 — "이동 정지 3초".** `Idle` is a 3.07 s loop that moves the spine, chest and
  head and **nothing below the hips**: the Observer's ability keys off standing still, so
  a decorative foot shuffle would be the animation lying about a gameplay state. Measured
  at exactly 0.0000° of leg motion, the same way §06's Standstill is asserted on the
  monster. The pelvis is pinned for the same reason — the legs are aimed in world space
  but exported as LOCAL curves, so a drifting pelvis writes motion into all eight of them.

HOW THE GAITS ARE VERIFIED
--------------------------
Nothing about the motion is asserted from a comment. Each key is **ground-locked**: the
pose is applied, the six sole contact points (heel, ball, toe on each foot) are measured,
and the hips drop until the lower foot rests on z = 0. Residual sole error comes out at
0.00 mm on every key of every clip, and the vertical bob is then a *consequence* of the
leg angles rather than a second curve to keep in sync with them.

Speed is measured the same way, from the stance phase rather than from the whole cycle:
the planted foot's travel divided by the time it is planted. That distinction is what
makes §05's 4.5 m/s run authorable at all — whole-cycle arithmetic implies a 1.2 m stride
from a 0.93 m leg, which is impossible, because a run spends a quarter of its stride
airborne. `Run` therefore has eight keys with two flight frames; `Walk` has four and no
flight, because one foot is always down and that is what walking is.
* **§12 — 은폐 지점.** *"출입구 근처에 은폐 지점이 있다"* for §07's 새벽 stage. A crouch is
  only worth animating if it actually lowers the player behind cover, so the check is on
  the measured ``HeadCameraAnchor`` height, not on a knee angle.
* **§09 — death is a state, not an exit.** The ghost keeps playing, and §08 drops the
  dead player's loot where the body is, so teammates have to come back for it. `Death`
  does not loop and ends flat on the floor: the corpse is a map marker.

THREE MESHES — THE FIRST-PERSON CONTRACT
----------------------------------------
§05 has a second half that used to be ignored: *"자기 몸은 안 보이므로 손만 있으면 된다"*
says the body is not visible **and that the hands are**. One mesh cannot deliver both,
because ``shadowCastingMode`` is a property of a whole Renderer — hiding the chest that
sits across the near plane in first person takes the arms with it. So the file exports:

    Player_Body    head, neck, torso, legs, gear.  Owner: ShadowsOnly. Others: drawn.
    Player_Arms    shoulders → fingertips.         Owner: drawn. §05's 손.
    Player_Torch   the flashlight in the fist.     Owner: drawn while it is in hand.

The Unity layer (``PlayerRigParts``) recognises them by MATERIAL SLOTS, not by these
names and not by bone weights — a Humanoid import binds all 26 bones to all three meshes,
measured, so the binding stops saying what a mesh is made of. ``verify_mesh_split``
asserts the composition the classifier depends on, and ``verify_first_person`` asserts
that the hands are actually inside the frame §05's FOV gives the player.

MATERIALS — THE ROLE CONTRACT
-----------------------------
Eight slots on the body, three on the arms, one on the torch, all in SLOTS order so slot
0 is ``Role_Listener`` on every mesh that has a role-tinted face. Slot 0 carries every
role-tinted face — a painted band of the torso shell
from z 1.10 to 1.41 (chest AND upper back), the painted deltoid caps, the raised collar,
and a ring around each bicep. The Unity layer swaps that slot per ``RoleId``:

    0 Role_Listener   1 Role_Observer   2 Role_Runner   3 Role_Engineer   4 Role_Flasher
    5 Player_Skin     6 Player_Coverall 7 Player_Gear

Slots 1–4 are not left empty. Each is attached to a 1.6 cm cube **inside the pelvis**,
fully enclosed by the torso shell and invisible from outside. It costs 48 triangles and
buys certainty: a material slot with no geometry survives Blender's FBX exporter (probed)
but there is no guarantee Unity's importer extracts a material whose submesh has zero
triangles, and four of the five contractual names silently failing to appear is exactly
the kind of breakage this pipeline exists to prevent. Called out here rather than left as
a surprise in the mesh.

Role colours are picked for **value** separation, not hue: §05 says the game is dark and
hue discrimination collapses at low light, so the script asserts a minimum pairwise
luminance gap as well as a minimum RGB distance.

BONE NAMES
----------
Unity Humanoid names throughout — Hips, Spine, Chest, UpperChest, Neck, Head, and
Left/Right Shoulder, UpperArm, LowerArm, Hand, UpperLeg, LowerLeg, Foot, Toes — so the
importer's Humanoid auto-mapping resolves without hand-editing an avatar. The rest pose is
a **true T-pose** (1.69 m span, 1.75 m tall), which is what auto-mapping wants, and the
script asserts the rig is actually at rest at export time rather than assuming it.

Four extra leaf bones are sockets, not deformers (``use_deform = False``, no vertex
groups, ignored by Humanoid):

    FlashlightMount     child of RightHand.  Bone +Y = beam direction. §05/§13.
    HeadCameraAnchor    child of Head at 1.635 m eye height. Bone +Y = look direction.
    ObjectiveMount      child of Chest, 0.34 m in front. §03's two-handed objective.
    BackpackMount       child of Chest, behind. §08's 가방 and per-role props.

They are bones rather than empties because ``export_fbx(with_animation=True)`` exports
only ARMATURE and MESH object types — an empty would be dropped silently.

NO FINGER BONES — stated plainly rather than omitted: each hand has four modelled
fingers and a thumb, curled 30° below the palm, but they are geometry on a single ``Hand``
bone and do not articulate. Nothing in the design needs them to (the flashlight and the
objective are both parented to sockets), and Humanoid treats finger bones as optional.
The curl is not decoration: §05 puts these hands at the bottom of the owner's screen seen
from above, and a flat palm at that angle photographs as a pale plank. Articulating them
later means extending ``bone_specs()``; nothing else here would change.
"""

from __future__ import annotations

import json
import math
import os
import re
import struct
import sys
import traceback
from dataclasses import dataclass, field

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Euler, Matrix, Vector  # noqa: E402

import blendkit  # noqa: E402
from blendkit import BoneSpec, MaterialSpec, Pose  # noqa: E402


# ── Proportions, in metres ──────────────────────────────────────────────────
# 1 Blender unit = 1 metre = 1 Unity unit. The model faces -Y in Blender, which
# export_fbx's axis_forward='-Z' turns into +Z forward in Unity. Do not add a
# rotation to compensate. Facing -Y with +Z up makes the character's LEFT +X.

FPS = 30

HEIGHT = 1.75
ANKLE_Z = 0.095
KNEE_Z = 0.50
HIP_Z = 0.93
CHEST_Z = 1.31
SHOULDER_Z = 1.44          # acromion / arm root height
EYE_Z = 1.635              # HeadCameraAnchor. 0.934 × height, a real eye height.
CROWN_Z = 1.75

LEG_X = 0.090              # leg centre line
SHOULDER_X = 0.155         # shoulder joint
ELBOW_X = 0.475
WRIST_X = 0.7000          # see gen_player_ai.HAND_LENGTH — the span is capped at 1.785 m
HAND_TIP_X = 0.845

THIGH_LEN = HIP_Z - KNEE_Z          # 0.430
SHANK_LEN = KNEE_Z - ANKLE_Z        # 0.405
BALL_Y = -0.115                     # ball of the foot, forward of the ankle
GROUND_CONTACT_Z = 0.030            # ball height when the sole is flat

MOUNTS = ("FlashlightMount", "HeadCameraAnchor", "ObjectiveMount", "BackpackMount")

# ── Ring resolution ─────────────────────────────────────────────────────────
# The triangle budget is 4000 and this model shipped using 1252 of them, so the
# octagonal limbs were never a cost decision — they were an unspent one. An 8-sided
# tube has a 45° facet: under §03's hard moving spot that is a band of flat shading
# that slides along the limb as the beam sweeps, and it reads as faceted plastic on
# the one asset three teammates look at all game.
#
# Raised on 2026-08-02, after the owner said the model was 허접 for the third time
# and a studio render of it agreed: at twelve sides a skull is a faceted lump and a
# thigh is a hexagonal prism, and no texture fixes a silhouette. The argument these
# numbers used to carry — "past twelve the silhouette change is under a pixel at the
# distances §12's corridors allow" — was measuring the wrong thing. §13 puts three
# teammates in frame at arm's length in the van, §09's ghost flies right up to a
# body, and the store screenshots are 1920×1080 of a character standing still.
#
# The budget they were husbanding was 4,000 triangles on a PC-only Steam release.
# It is now 60,000 (gen_player_ai.TRI_BUDGET), which is a fifteenth of what one
# rock in a modern game costs. Four players and a monster on screen at these
# numbers is under 300k triangles; the GPU that cannot do that has not shipped
# since 2010.
#
# Changing these is safe for the §04 role contract. `shell` assigns materials per
# *segment* — that is, per pair of rings — so the vest band and deltoid caps are
# indexed by ring number and are untouched by how many sides each ring has.
SIDES_BODY = 32
SIDES_NECK = 24
SIDES_LIMB = 24
SIDES_BOOT = 20

# ── Materials ───────────────────────────────────────────────────────────────

ROLE_MATERIALS = (
    "Role_Listener", "Role_Observer", "Role_Runner", "Role_Engineer", "Role_Flasher",
)
SKIN, COVERALL, GEAR = "Player_Skin", "Player_Coverall", "Player_Gear"
SLOTS = ROLE_MATERIALS + (SKIN, COVERALL, GEAR)

M_ROLE = 0      # every role-tinted face. Unity swaps this slot per RoleId.
M_SKIN = SLOTS.index(SKIN)
M_COVERALL = SLOTS.index(COVERALL)
M_GEAR = SLOTS.index(GEAR)

# ── The three meshes ────────────────────────────────────────────────────────
# §05: *"자기 몸은 안 보이므로 손만 있으면 된다"* — the player never sees their own body,
# but they must see their own hands. One mesh cannot deliver both, because
# `shadowCastingMode` is a property of a whole Renderer: hiding the chest that sits
# across the near plane in first person takes the arms with it, which is exactly the
# defect this split fixes. So the geometry is exported as three objects and the Unity
# layer decides per object what the owner sees:
#
#   Player_Body    head, neck, torso, legs, gear.  Owner: ShadowsOnly — it occludes.
#   Player_Arms    shoulders → fingertips.         Owner: drawn. §05's 손.
#   Player_Torch   the flashlight in the fist.     Owner: drawn while it is in hand.
#
# The seam is buried: the arm tube's two innermost rings sit INSIDE the torso shell, so
# hiding the torso does not open a hole at the shoulder.
#
# The Unity side classifies them by the MATERIAL SLOTS each mesh carries, never by these
# names — see PlayerRigParts, and `verify_mesh_split` below for the contract. Mesh names
# are what a regeneration is free to change; material names are read by the importer, by
# §04's role swap and by the texture manifest, so a rename breaks something loudly first.
ARM_SLOTS = (ROLE_MATERIALS[0], SKIN, COVERALL)
TORCH_SLOTS = (GEAR,)

MATERIAL_SPECS = (
    # §04 order. Values spread deliberately — see the module docstring.
    MaterialSpec("Role_Listener", (0.10, 0.15, 0.34), roughness=0.80),
    MaterialSpec("Role_Engineer", (0.18, 0.26, 0.11), roughness=0.80),
    MaterialSpec("Role_Runner", (0.66, 0.21, 0.10), roughness=0.78),
    MaterialSpec("Role_Observer", (0.72, 0.55, 0.14), roughness=0.76),
    MaterialSpec("Role_Flasher", (0.78, 0.78, 0.74), roughness=0.70),
    MaterialSpec(SKIN, (0.60, 0.46, 0.39), roughness=0.62),
    MaterialSpec(COVERALL, (0.255, 0.245, 0.235), roughness=0.88),
    MaterialSpec(GEAR, (0.075, 0.072, 0.070), roughness=0.50),
)


def luma(rgb) -> float:
    """Rec.709 relative luminance. §05's darkness makes this matter more than hue."""
    r, g, b = rgb
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


# ── Mesh accumulation ───────────────────────────────────────────────────────
# The whole body is built into ONE bmesh with explicit per-vertex bone weights and
# per-face material indices. Two reasons over building parts and joining them:
#
# * Slot order is contractual. Role_Listener MUST be slot 0 because that is what the
#   Unity layer swaps. Joining objects merges material lists in join order, which is
#   an implicit dependency on the order geometry happens to be created in.
# * Weights are authored, not guessed. Every ring of every tube names the bones it
#   belongs to and in what proportion, so joints bend smoothly with no automatic
#   heat-map weighting to go wrong on a mesh whose limbs interpenetrate the torso.


class Body:
    """Accumulates vertices, faces, material indices and bone weights for one mesh.

    `slots` is the subset of SLOTS this mesh carries, in SLOTS order. Faces are still
    authored against the GLOBAL indices (``M_ROLE``, ``M_SKIN``, …) and remapped here,
    so a body part can be moved from one mesh to another without renumbering a single
    face. Keeping SLOTS order is what preserves the §04 contract on every mesh that has
    a role-tinted face: ``Role_Listener`` is index 0 in SLOTS, therefore index 0 in any
    subset of it, therefore ``renderer.materials[0]`` on every renderer Unity builds.
    """

    def __init__(self, slots) -> None:
        self.slots = tuple(slots)
        self._local = {SLOTS.index(name): i for i, name in enumerate(self.slots)}
        self.bm = bmesh.new()
        self.bverts: list[bmesh.types.BMVert] = []
        self.positions: list[Vector] = []
        self.weights: list[dict[str, float]] = []

    def vert(self, p, w: dict[str, float]) -> int:
        v = self.bm.verts.new(Vector(p))
        self.bverts.append(v)
        self.positions.append(Vector(p))
        total = sum(w.values())
        if abs(total - 1.0) > 1e-4:
            raise ValueError(f"vertex weights sum to {total:.4f}, not 1.0: {w}")
        self.weights.append(dict(w))
        return len(self.bverts) - 1

    def face(self, idxs, mat: int) -> None:
        local = self._local.get(mat)
        if local is None:
            raise ValueError(
                f"{SLOTS[mat]} is not one of this mesh's slots {self.slots} — a body part "
                "was moved to a mesh that does not carry the material it paints with.")
        f = self.bm.faces.new([self.bverts[i] for i in idxs])
        f.material_index = local

    # -- shells ------------------------------------------------------------
    def shell(self, rings, mat, cap_start: bool = True, cap_end: bool = True) -> None:
        """Stitches a list of equal-length vertex rings into a tube.

        `rings` is a list of (list_of_points, weights_dict). `mat` is one material index
        for the whole tube, or one per segment — which is how the §04 role colour is
        applied: as a band of the body's OWN faces rather than as a panel floating in
        front of it. The torso is an ellipse and a flat panel laid over it either sinks in
        at the sternum or lifts off at the ribs; painting the shell cannot do either, and
        costs no triangles.

        Winding is fixed afterwards by recalc_face_normals, so ring order does not matter.
        """
        idx_rings = [[self.vert(p, w) for p in points] for points, w in rings]
        n = len(idx_rings[0])
        mats = [mat] * (len(idx_rings) - 1) if isinstance(mat, int) else list(mat)
        if len(mats) != len(idx_rings) - 1:
            raise ValueError(f"{len(mats)} segment materials for {len(idx_rings) - 1} segments")
        for seg, (r0, r1) in enumerate(zip(idx_rings, idx_rings[1:])):
            for i in range(n):
                j = (i + 1) % n
                self.face((r0[i], r0[j], r1[j], r1[i]), mats[seg])
        if cap_start:
            self.face(tuple(reversed(idx_rings[0])), mats[0])
        if cap_end:
            self.face(tuple(idx_rings[-1]), mats[-1])

    def finish(self, name: str) -> bpy.types.Object:
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        for slot in self.slots:
            mesh.materials.append(bpy.data.materials[slot])

        # bmesh writes vertices in creation order. Verifying it rather than trusting
        # it: a silent reorder would scramble every vertex weight below.
        if len(mesh.vertices) != len(self.positions):
            raise ValueError(f"{len(mesh.vertices)} mesh verts vs {len(self.positions)} recorded")
        for i, mv in enumerate(mesh.vertices):
            if (Vector(mv.co) - self.positions[i]).length > 1e-5:
                raise ValueError(f"vertex {i} moved during to_mesh — weight mapping is invalid")

        groups: dict[str, bpy.types.VertexGroup] = {}
        for i, w in enumerate(self.weights):
            for bone, value in w.items():
                if value <= 0.0:
                    continue
                g = groups.get(bone)
                if g is None:
                    g = groups[bone] = obj.vertex_groups.new(name=bone)
                g.add([i], value, "REPLACE")
        return obj


# ── Ring generators ─────────────────────────────────────────────────────────
# Every ring lies in the plane spanned by (u, v) around a centre. `u`/`v` are chosen
# per body part so the radii read as anatomy: for the torso and legs u = X (width)
# and v = Y (depth); for the arms u = Y and v = Z.

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def ellipse(c, u, v, ru: float, rv: float, sides: int, phase: float = 0.5):
    """An elliptical ring. `phase` offsets the vertices so a silhouette edge lands
    on the widest point instead of a flat facet."""
    out = []
    for i in range(sides):
        a = 2.0 * math.pi * (i + phase) / sides
        out.append(Vector(c) + u * (ru * math.cos(a)) + v * (rv * math.sin(a)))
    return out


def rect(c, u, v, hu: float, hv: float):
    """A rectangular ring — panels and anything that genuinely has a hard edge."""
    c = Vector(c)
    return [c + u * hu + v * hv, c - u * hu + v * hv, c - u * hu - v * hv, c + u * hu - v * hv]


def flat_bottom(c, u, v, hu: float, hv: float, sides: int = SIDES_BOOT):
    """A ring with a flat base and a rounded top — a boot cross-section.

    The sole has to stay flat and exactly on the ring's lower edge: every gait key in
    this file is ground-locked by dropping the hips until a sole contact reaches z = 0,
    and a curved bottom would leave the boot's corners hanging in the air on every
    frame. So the base stays a single straight span between the two corners and only
    the upper half is swept, which is also what a boot actually looks like.

    Returned in ring order: the +u sole corner, over the top to the -u corner, and the
    closing edge between them is the sole.
    """
    c = Vector(c)
    arc = max(2, sides - 2)
    out = [c + u * hu - v * hv]
    for i in range(1, arc + 1):
        a = math.pi * i / (arc + 1)
        out.append(c + u * (hu * math.cos(a)) + v * (hv * math.sin(a)))
    out.append(c - u * hu - v * hv)
    return out


def mirror_x(rings):
    """Mirrors ring geometry across X and swaps Left/Right in the weight names."""
    out = []
    for points, w in rings:
        mw = {}
        for bone, value in w.items():
            if bone.startswith("Left"):
                bone = "Right" + bone[4:]
            elif bone.startswith("Right"):
                bone = "Left" + bone[5:]
            mw[bone] = value
        out.append(([Vector((-p.x, p.y, p.z)) for p in points], mw))
    return out


# ── Geometry ────────────────────────────────────────────────────────────────


def build_torso(b: Body) -> None:
    """Pelvis → waist → ribcage, weighted across Hips / Spine / Chest / UpperChest.

    Hip breadth 0.336 m and shoulder breadth 0.344 m at the ribcage: real numbers for a
    1.75 m person, which is the whole point of this model. The waist is the narrowest
    ring so the silhouette has a readable taper in a dark corridor.
    """
    rings = [
        (0.82, 0.130, 0.098, {"Hips": 1.0}),
        (0.88, 0.160, 0.108, {"Hips": 1.0}),
        (0.95, 0.168, 0.110, {"Hips": 0.70, "Spine": 0.30}),
        (1.03, 0.142, 0.100, {"Hips": 0.25, "Spine": 0.75}),
        (1.10, 0.128, 0.096, {"Spine": 1.0}),
        (1.20, 0.145, 0.104, {"Spine": 0.40, "Chest": 0.60}),
        (1.31, 0.165, 0.116, {"Chest": 1.0}),
        (1.41, 0.172, 0.112, {"Chest": 0.30, "UpperChest": 0.70}),
        (1.50, 0.115, 0.092, {"UpperChest": 1.0}),
    ]
    # Segments 4-6 (z 1.10 → 1.41) are the §04 vest: the chest and the whole upper back,
    # so a role is readable from behind and from the side, not only head-on. Segment 7
    # stays coverall, which gives the vest a shoulder line to end at.
    seg_mats = [M_COVERALL] * (len(rings) - 1)
    for seg in (4, 5, 6):
        seg_mats[seg] = M_ROLE
    b.shell([(ellipse((0, 0, z), X, Y, rx, ry, SIDES_BODY), w)
             for z, rx, ry, w in rings], seg_mats)


def build_neck_head(b: Body) -> None:
    """Neck, skull, nose and ears. The nose is 12 triangles that tell three teammates
    which way a head is facing — cheaper than any other cue at that job."""
    neck = [
        (1.44, 0.060, 0.056, {"UpperChest": 1.0}),          # tucked inside the torso
        (1.50, 0.055, 0.052, {"UpperChest": 0.40, "Neck": 0.60}),
        (1.545, 0.050, 0.048, {"Neck": 1.0}),
        (1.585, 0.055, 0.052, {"Neck": 0.45, "Head": 0.55}),
    ]
    b.shell([(ellipse((0, -0.004 * i, z), X, Y, rx, ry, SIDES_NECK), w)
             for i, (z, rx, ry, w) in enumerate(neck)], M_SKIN)

    head = [
        (1.575, -0.020, 0.058, 0.062),
        (1.615, -0.015, 0.072, 0.082),
        (1.655, -0.012, 0.079, 0.090),
        (1.700, -0.008, 0.077, 0.088),
        (1.735, -0.005, 0.055, 0.062),
        (1.750, -0.005, 0.026, 0.030),
    ]
    b.shell([(ellipse((0, y, z), X, Y, rx, ry, SIDES_BODY), {"Head": 1.0})
             for z, y, rx, ry in head], M_SKIN)

    # Jaw: a short wedge under the front of the skull. Without it the head is an egg,
    # and an egg reads as a mannequin's head no matter how many sides it has — the
    # eye finds a jawline long before it finds a nose 12 mm across.
    b.shell([(rect((0, -0.030, 1.622), X, Y, 0.052, 0.036), {"Head": 1.0}),
             (rect((0, -0.040, 1.592), X, Y, 0.042, 0.030), {"Head": 1.0}),
             (rect((0, -0.044, 1.572), X, Y, 0.028, 0.020), {"Head": 1.0})], M_SKIN)

    # Brow: a shelf over the eye line, so the beam catches a hard edge above the nose.
    b.shell([(rect((0, -0.052, 1.664), X, Y, 0.062, 0.020), {"Head": 1.0}),
             (rect((0, -0.058, 1.650), X, Y, 0.058, 0.014), {"Head": 1.0})], M_SKIN)

    # Nose: a wedge off the eye line, pointing -Y (forward).
    b.shell([(rect((0, -0.074, 1.652), X, Z, 0.015, 0.017), {"Head": 1.0}),
             (rect((0, -0.104, 1.634), X, Z, 0.010, 0.010), {"Head": 1.0})], M_SKIN)

    for side in (1, -1):
        b.shell([(rect((side * 0.078, 0.004, 1.662), Y, Z, 0.012, 0.022), {"Head": 1.0}),
                 (rect((side * 0.092, 0.004, 1.660), Y, Z, 0.009, 0.018), {"Head": 1.0})],
                M_SKIN)


def arm_rings(side: int):
    s = "Left" if side > 0 else "Right"
    # (x, half-depth along Y, half-height along Z, weights)
    return [
        (0.075, 0.075, 0.058, {"UpperChest": 1.0}),         # inside the torso
        (0.150, 0.072, 0.076, {"UpperChest": 0.45, f"{s}Shoulder": 0.55}),
        (0.215, 0.062, 0.068, {f"{s}Shoulder": 0.35, f"{s}UpperArm": 0.65}),
        (0.330, 0.052, 0.056, {f"{s}UpperArm": 1.0}),
        (0.455, 0.046, 0.049, {f"{s}UpperArm": 0.70, f"{s}LowerArm": 0.30}),
        (0.500, 0.044, 0.047, {f"{s}UpperArm": 0.20, f"{s}LowerArm": 0.80}),
        (0.610, 0.041, 0.044, {f"{s}LowerArm": 1.0}),
        (0.700, 0.034, 0.037, {f"{s}LowerArm": 1.0}),
        (0.755, 0.030, 0.026, {f"{s}LowerArm": 0.35, f"{s}Hand": 0.65}),
    ]


def build_arm(b: Body, side: int) -> None:
    """Shoulder → hand as one continuous tube, plus a palm slab and a thumb nub.

    The elbow gets two rings 45 mm apart so the blend from UpperArm to LowerArm happens
    across a short band instead of one hard ring — that is what stops a rigid crease
    when §03's carry pose folds the arm past 70°.
    """
    s = "Left" if side > 0 else "Right"
    rings = arm_rings(side)
    arm_mats = [M_COVERALL] * (len(rings) - 1)
    arm_mats[1] = M_ROLE            # the deltoid cap, above the shoulder line
    b.shell([(ellipse((side * x, 0, SHOULDER_Z), Y, Z, ru, rv, SIDES_LIMB), w)
             for x, ru, rv, w in rings], arm_mats)

    # Palm: flat slab, thickness in Z (T-pose palms face down).
    b.shell([(rect((side * 0.750, 0, SHOULDER_Z), Y, Z, 0.040, 0.021), {f"{s}Hand": 1.0}),
             (rect((side * 0.800, 0, SHOULDER_Z), Y, Z, 0.043, 0.019), {f"{s}Hand": 1.0}),
             (rect((side * 0.828, 0, SHOULDER_Z), Y, Z, 0.040, 0.016),
              {f"{s}Hand": 1.0})], M_SKIN)

    # Four fingers off the knuckle line. A single tapered slab reads as a mitten from
    # any distance the beam reaches, and this model's whole job is to be read by three
    # other players — the hand holding §05's pointing device should look like a hand.
    # They also bring the T-pose span to 1.75 m, which is where a real 1.75 m person's
    # arm span sits; the slab alone left it 60 mm short.
    # The fingers are cut to slightly different lengths and curl 30° below the palm
    # plane. Straight and coplanar they read as a comb — four flat prongs with slots
    # between them, which is worse than the plain slab this replaced. A relaxed hand
    # curls, and the curl is what makes four separate pieces read as one.
    #
    # The 30° is a first-person number, not a modelling preference. §05 puts these hands
    # on the owner's screen at the bottom of the frame, seen from above and behind, and a
    # flat palm seen from that angle is a rectangle: the first working version of this
    # change photographed as a pale plank. The curl is the only thing at this triangle
    # count that turns the silhouette back into a hand. It also lands the fingertips on
    # the torch barrel (`build_torch` puts its axis 29 mm under the palm), so the grip is
    # a grip rather than a cylinder passing through a slab.
    for offset, length, half_w, droop in ((-0.030, 0.044, 0.0095, 0.024),
                                          (-0.010, 0.049, 0.0100, 0.027),
                                          (0.010, 0.046, 0.0095, 0.026),
                                          (0.029, 0.038, 0.0085, 0.022)):
        b.shell([(rect((side * 0.830, offset, SHOULDER_Z - 0.001), Y, Z, half_w, 0.014),
                  {f"{s}Hand": 1.0}),
                 (rect((side * (0.830 + length * 0.58), offset,
                        SHOULDER_Z - droop * 0.30), Y, Z, half_w * 0.92, 0.012),
                  {f"{s}Hand": 1.0}),
                 (rect((side * (0.830 + length * 0.94), offset, SHOULDER_Z - droop), Y, Z,
                       half_w * 0.70, 0.009), {f"{s}Hand": 1.0})], M_SKIN)

    # Thumb: forward off the palm, so a fist reads as a grip on the flashlight.
    b.shell([(rect((side * 0.762, -0.036, SHOULDER_Z), X, Z, 0.014, 0.016),
              {f"{s}Hand": 1.0}),
             (rect((side * 0.790, -0.068, SHOULDER_Z - 0.004), X, Z, 0.011, 0.012),
              {f"{s}Hand": 1.0})], M_SKIN)


def leg_rings(side: int):
    s = "Left" if side > 0 else "Right"
    # (z, y, rx, ry, weights)
    return [
        (0.950, 0.000, 0.078, 0.090, {"Hips": 0.60, f"{s}UpperLeg": 0.40}),
        (0.860, -0.004, 0.085, 0.092, {f"{s}UpperLeg": 1.0}),
        (0.690, -0.008, 0.074, 0.081, {f"{s}UpperLeg": 1.0}),
        (0.560, -0.012, 0.066, 0.072, {f"{s}UpperLeg": 0.75, f"{s}LowerLeg": 0.25}),
        (0.500, -0.014, 0.062, 0.068, {f"{s}UpperLeg": 0.22, f"{s}LowerLeg": 0.78}),
        (0.420, -0.004, 0.060, 0.072, {f"{s}LowerLeg": 1.0}),
        (0.250, 0.006, 0.050, 0.058, {f"{s}LowerLeg": 1.0}),
        (0.140, 0.013, 0.041, 0.046, {f"{s}LowerLeg": 0.85, f"{s}Foot": 0.15}),
        (0.098, 0.015, 0.038, 0.043, {f"{s}LowerLeg": 0.50, f"{s}Foot": 0.50}),
    ]


def build_leg(b: Body, side: int) -> None:
    """Hip → ankle, then a boxy foot with the sole flat on z = 0.

    The knee is 12 mm forward of the hip-to-ankle line and the ankle 15 mm behind it, so
    knee flexion has a direction to bend in without the animator specifying one.
    """
    s = "Left" if side > 0 else "Right"
    b.shell([(ellipse((side * LEG_X, y, z), X, Y, rx, ry, SIDES_LIMB), w)
             for z, y, rx, ry, w in leg_rings(side)], M_COVERALL)

    # One continuous boot instead of two stacked slabs. Each ring's centre sits at its
    # own half-height, which puts the flat base of every ring on z = 0 exactly, so the
    # sole stays a plane for the ground-lock to rest on.
    boot = [
        (0.088, 0.040, 0.054, {f"{s}Foot": 1.0}),      # heel, narrow and tall
        (0.040, 0.047, 0.050, {f"{s}Foot": 1.0}),
        (-0.025, 0.050, 0.043, {f"{s}Foot": 1.0}),     # instep
        (BALL_Y, 0.048, 0.032, {f"{s}Foot": 0.55, f"{s}Toes": 0.45}),
        (-0.163, 0.042, 0.025, {f"{s}Toes": 1.0}),
        (-0.200, 0.028, 0.017, {f"{s}Toes": 1.0}),     # rounded toe cap
    ]
    b.shell([(flat_bottom((side * LEG_X, y, hz), X, Z, hx, hz), w)
             for y, hx, hz, w in boot], M_GEAR)


def build_gear(b: Body) -> None:
    """Belt, boot cuffs, knee pads and a shoulder radio. Neutral on every role.

    The radio is not decoration: §13 gives every player proximity voice, and a visible
    set on the shoulder is the only thing on the model that says so.
    """
    # Every one of these is deliberately a little LARGER than the limb it rings. A belt
    # modelled flush with the waist half-vanishes into it and the visible half z-fights.
    b.shell([(ellipse((0, 0, 1.010), X, Y, 0.152, 0.113, SIDES_BODY),
              {"Hips": 0.5, "Spine": 0.5}),
             (ellipse((0, 0, 1.080), X, Y, 0.148, 0.111, SIDES_BODY),
              {"Spine": 1.0})], M_GEAR)

    for side in (1, -1):
        s = "Left" if side > 0 else "Right"
        b.shell([(ellipse((side * LEG_X, 0.008, 0.120), X, Y, 0.062, 0.069, SIDES_LIMB),
                  {f"{s}LowerLeg": 1.0}),
                 (ellipse((side * LEG_X, 0.004, 0.240), X, Y, 0.059, 0.066, SIDES_LIMB),
                  {f"{s}LowerLeg": 1.0})], M_GEAR)
        b.shell([(rect((side * LEG_X, -0.070, 0.535), X, Z, 0.048, 0.040),
                  {f"{s}UpperLeg": 0.6, f"{s}LowerLeg": 0.4}),
                 (rect((side * LEG_X, -0.086, 0.520), X, Z, 0.040, 0.032),
                  {f"{s}UpperLeg": 0.5, f"{s}LowerLeg": 0.5})], M_GEAR)

    # Radio on the chest strap, not floating off the shoulder: it has to sit ON a surface,
    # and the front of the ribcage is the only place at that height that has one.
    b.shell([(rect((0.088, -0.098, 1.372), X, Z, 0.030, 0.034), {"Chest": 1.0}),
             (rect((0.088, -0.132, 1.368), X, Z, 0.024, 0.028), {"Chest": 1.0})], M_GEAR)


def build_collar(b: Body) -> None:
    """The role-coloured ring around the neck — its own geometry, not a painted face.

    It wraps the neck so it cannot float, and it stands above the shoulder line, which
    puts the role colour into the head-height silhouette. Together with the painted vest,
    the deltoid caps and the bicep bands it covers the front, the back, both sides and the
    silhouette — §05 gives the flashlight a narrow beam, so a role has to be identifiable
    from whatever fragment of a teammate is lit.

    Lives on the body mesh, so a first-person owner never sees their own collar under
    their chin. The bicep bands are the arms' half of the same job — `build_arm_band`.
    """
    b.shell([(ellipse((0, -0.006, 1.492), X, Y, 0.072, 0.068, 8), {"UpperChest": 1.0}),
             (ellipse((0, -0.010, 1.548), X, Y, 0.068, 0.064, 8),
              {"UpperChest": 0.4, "Neck": 0.6})], M_ROLE)


def build_arm_band(b: Body, side: int) -> None:
    """The role-coloured ring around one bicep. On the arms mesh — see `build_collar`."""
    s = "Left" if side > 0 else "Right"
    b.shell([(ellipse((side * 0.285, 0, SHOULDER_Z), Y, Z, 0.057, 0.061, 8),
              {f"{s}UpperArm": 1.0}),
             (ellipse((side * 0.345, 0, SHOULDER_Z), Y, Z, 0.055, 0.059, 8),
              {f"{s}UpperArm": 1.0})], M_ROLE)


def build_role_swatches(b: Body) -> None:
    """One 1.6 cm cube per non-default role material, sealed inside the pelvis.

    Explained at length in the module docstring. Twelve triangles each, invisible from
    outside the body, and the only way to guarantee the importer on the far end extracts
    all five names §04 depends on.
    """
    for i in range(1, len(ROLE_MATERIALS)):
        cx = -0.048 + 0.032 * i
        b.shell([(rect((cx, -0.008, 0.885), X, Y, 0.008, 0.008), {"Hips": 1.0}),
                 (rect((cx, 0.008, 0.885), X, Y, 0.008, 0.008), {"Hips": 1.0})], i)


def build_torch(b: Body) -> None:
    """§05's 손전등 as geometry in the right fist, weighted rigidly to RightHand.

    Why it exists at all: §05 hands every player a flashlight and §10 makes switching it
    on the game's most-repeated trade — *"손전등을 켠다 → 괴물이 본다 · 배터리를 쓴다"*.
    In first person the beam alone cannot say which of §03's states you are in, because a
    beam is the one thing you see when you are NOT looking at your hands. A torch you can
    see arriving in and leaving your fist makes that trade physical: the Unity layer draws
    this mesh only while the light is in hand, so 목표물 운반 (both hands, no torch) and an
    empty pair of hands are told apart without a HUD.

    Its own object so it can be switched off without touching the arms, and skinned to
    exactly one bone so the Unity classifier can recognise it as a held item rather than
    as part of the person. Modelled along the rest-pose arm axis (-X is distal on the
    right side) with the lens just short of `FlashlightMount`, so the spot light sits in
    FRONT of the last surface and cannot light the barrel from inside it.
    """
    # Proportions chosen for one job: being recognisable end-on. In first person the
    # torch is nearly parallel to the line of sight, so a plain barrel gripped inside the
    # fist reads as a lump on the hand — measured, in the preview render, before this.
    # Half its length therefore projects past the fingertips (which reach x = -0.879) and
    # the head is 1.7× the barrel, so what the owner sees past their own knuckles is a
    # disc that is obviously an object and obviously not a finger.
    axis_z = 1.408          # under the palm, in the curl of the fingers
    rings = [
        (-0.690, 0.016),    # butt, proud of the wrist side of the fist
        (-0.706, 0.021),
        (-0.858, 0.021),    # barrel
        (-0.874, 0.036),    # bezel shoulder, clear of the fingertips
        (-0.925, 0.033),    # lens rim; FlashlightMount sits 10 mm ahead of this
    ]
    b.shell([(ellipse((x, 0.0, axis_z), Y, Z, r, r, SIDES_BOOT), {"RightHand": 1.0})
             for x, r in rings], M_GEAR)


def build_body() -> tuple[bpy.types.Object, bpy.types.Object, bpy.types.Object]:
    """The three meshes of the section note: body, arms, torch."""
    body = Body(SLOTS)
    build_torso(body)
    build_neck_head(body)
    for side in (1, -1):
        build_leg(body, side)
    build_gear(body)
    build_collar(body)
    build_role_swatches(body)

    arms = Body(ARM_SLOTS)
    for side in (1, -1):
        build_arm(arms, side)
        build_arm_band(arms, side)

    torch = Body(TORCH_SLOTS)
    build_torch(torch)

    return (body.finish("Player_Body"), arms.finish("Player_Arms"),
            torch.finish("Player_Torch"))


# ── Rig ─────────────────────────────────────────────────────────────────────


def bone_specs() -> list[BoneSpec]:
    """26 bones: 22 Unity Humanoid deformers plus 4 sockets.

    Every name that maps to a Humanoid slot uses Unity's own spelling, so the importer
    resolves the avatar with no hand mapping. The sockets are leaves, which is why they
    cannot confuse auto-mapping — Hand is still the direct child of LowerArm.
    """
    specs = [
        BoneSpec("Hips", (0.0, 0.0, HIP_Z), (0.0, 0.0, 1.02)),
        BoneSpec("Spine", (0.0, 0.0, 1.02), (0.0, 0.0, 1.16), "Hips", True),
        BoneSpec("Chest", (0.0, 0.0, 1.16), (0.0, 0.0, CHEST_Z), "Spine", True),
        BoneSpec("UpperChest", (0.0, 0.0, CHEST_Z), (0.0, 0.0, SHOULDER_Z), "Chest", True),
        BoneSpec("Neck", (0.0, -0.005, 1.450), (0.0, -0.015, 1.565), "UpperChest"),
        BoneSpec("Head", (0.0, -0.015, 1.565), (0.0, -0.015, 1.730), "Neck", True),

        # §05/§13: the first-person camera. +Y = look direction, so the Unity layer can
        # drive yaw/pitch on this transform and the beam and the eyes agree.
        BoneSpec("HeadCameraAnchor", (0.0, -0.055, EYE_Z), (0.0, -0.175, EYE_Z), "Head"),
        # §03: where the two-handed objective attaches. Sits where both palms meet in
        # the Carry pose, which the script measures rather than assumes — so this moved
        # up with the carry pose when the objective was raised into the owner's own view.
        BoneSpec("ObjectiveMount", (0.0, -0.405, 1.455), (0.0, -0.525, 1.455), "Chest"),
        # §08's 가방 (+5 loot, −10% speed) and any per-role back prop.
        BoneSpec("BackpackMount", (0.0, 0.115, CHEST_Z), (0.0, 0.255, CHEST_Z), "Chest"),
    ]

    for side, s in ((1, "Left"), (-1, "Right")):
        x = float(side)
        specs += [
            BoneSpec(f"{s}Shoulder", (x * 0.035, 0.0, 1.415), (x * SHOULDER_X, 0.0, SHOULDER_Z),
                     "UpperChest"),
            BoneSpec(f"{s}UpperArm", (x * SHOULDER_X, 0.0, SHOULDER_Z),
                     (x * ELBOW_X, 0.0, SHOULDER_Z), f"{s}Shoulder", True),
            BoneSpec(f"{s}LowerArm", (x * ELBOW_X, 0.0, SHOULDER_Z),
                     (x * WRIST_X, 0.0, SHOULDER_Z), f"{s}UpperArm", True),
            BoneSpec(f"{s}Hand", (x * WRIST_X, 0.0, SHOULDER_Z),
                     (x * HAND_TIP_X, 0.0, SHOULDER_Z), f"{s}LowerArm", True),

            BoneSpec(f"{s}UpperLeg", (x * LEG_X, -0.005, HIP_Z), (x * LEG_X, -0.015, KNEE_Z),
                     "Hips"),
            BoneSpec(f"{s}LowerLeg", (x * LEG_X, -0.015, KNEE_Z), (x * LEG_X, 0.015, ANKLE_Z),
                     f"{s}UpperLeg", True),
            BoneSpec(f"{s}Foot", (x * LEG_X, 0.015, ANKLE_Z), (x * LEG_X, BALL_Y, 0.032),
                     f"{s}LowerLeg", True),
            BoneSpec(f"{s}Toes", (x * LEG_X, BALL_Y, 0.032), (x * LEG_X, -0.185, 0.026),
                     f"{s}Foot", True),
        ]

    # §05: 손전등이 포인터가 된다. Bone +Y runs distally out of the fist, so once the
    # forearm is posed forward the beam axis points forward with no extra offset.
    # Placed 10 mm ahead of the torch's lens rim (`build_torch`) rather than in the
    # middle of the fist: the Unity spot light is positioned on this bone, and a light
    # source inside the barrel would light the torch from within and cast the player's
    # own fist down the corridor.
    specs.append(BoneSpec("FlashlightMount", (-0.935, 0.0, 1.408), (-1.055, 0.0, 1.408),
                          "RightHand"))
    return specs


DEFORM_BONES = tuple(
    s.name for s in bone_specs() if s.name not in MOUNTS
)


# ── Posing by absolute aim ──────────────────────────────────────────────────
# blendkit.Pose takes bone-LOCAL euler degrees, and a bone's local axes depend on its
# rest direction. Authoring in local degrees means a different convention per bone, and
# on a T-pose rig it is worse than on the monster: once the upper arm swings forward, the
# forearm's "elbow bend" is no longer a rotation about any world axis.
#
# So every bone is authored as the ABSOLUTE world direction it should point in, and the
# local basis is derived:
#
#     R_abs(bone)  = rotation taking the bone's rest direction to the aimed direction
#     R_rel        = R_abs(parent)⁻¹ · R_abs(bone)
#     matrix_basis = A⁻¹ · R_rel · A          (A = bone rest orientation, armature space)
#
# Absolute aiming is what makes gait authoring tractable: "thigh 26° forward of vertical,
# shank 21° forward of vertical" is how gait is described in the first place, and knee
# flexion falls out as the difference instead of having to be composed by hand.
#
# A bone with no aim inherits its parent's absolute rotation, i.e. follows rigidly.

REST: dict[str, Matrix] = {}
PARENT: dict[str, str | None] = {}
ORDER: list[str] = []


@dataclass(frozen=True)
class Aim:
    """A world direction for a bone, plus an optional twist about that direction."""

    dir: tuple
    roll: float = 0.0


def cache_rig(rig: bpy.types.Object) -> None:
    REST.clear()
    PARENT.clear()
    ORDER.clear()

    def walk(bone):
        REST[bone.name] = bone.matrix_local.to_3x3()
        PARENT[bone.name] = bone.parent.name if bone.parent else None
        ORDER.append(bone.name)
        for child in bone.children:
            walk(child)

    for bone in rig.data.bones:
        if bone.parent is None:
            walk(bone)


def rest_dir(bone: str) -> Vector:
    return REST[bone].col[1].normalized()


def abs_rot(bone: str, aim: Aim) -> Matrix:
    target = Vector(aim.dir).normalized()
    rot = rest_dir(bone).rotation_difference(target).to_matrix()
    if aim.roll:
        rot = Matrix.Rotation(math.radians(aim.roll), 3, target) @ rot
    return rot


def solve(spec: dict[str, Aim]) -> dict[str, Matrix]:
    unknown = [b for b in spec if b not in REST]
    if unknown:
        raise ValueError("pose names bones that do not exist: " + ", ".join(unknown))
    out: dict[str, Matrix] = {}
    for bone in ORDER:
        parent = PARENT[bone]
        parent_abs = out[parent] if parent else Matrix.Identity(3)
        out[bone] = abs_rot(bone, spec[bone]) if bone in spec else parent_abs
    return out


def to_local_vec(bone: str, world_offset) -> tuple[float, float, float]:
    """A world-space translation expressed in the bone's local space.

    matrix_basis is T(location) @ R, so the location is mapped to armature space by the
    bone's rest orientation alone — independent of how the bone is rotated.
    """
    return tuple(REST[bone].inverted() @ Vector(world_offset))


def make_pose(frame: int, spec: dict[str, Aim], hips_world=None,
              prev: dict[str, Euler] | None = None) -> Pose:
    """Expands an aim spec into a keyframe for EVERY bone.

    Keying all 26 bones in all 9 actions is deliberate. The FBX exporter bakes every bone
    for every clip, so a bone left uncurved in clip B keeps whatever pose clip A happened
    to leave on it — which is how a walk cycle ends up with the death pose's arms.

    `prev` carries the previous frame's euler per bone so `to_euler` picks the compatible
    branch. Without it a curve can jump 179° → −179° between keys and the interpolation
    takes the long way round, which reads as a limb snapping.
    """
    absrots = solve(spec)
    rotations: dict[str, tuple[float, float, float]] = {}
    for bone in ORDER:
        parent = PARENT[bone]
        rel = (absrots[parent].inverted() if parent else Matrix.Identity(3)) @ absrots[bone]
        rest = REST[bone]
        basis = rest.inverted() @ rel @ rest
        if prev is not None and bone in prev:
            euler = basis.to_euler("XYZ", prev[bone])
        else:
            euler = basis.to_euler("XYZ")
        if prev is not None:
            prev[bone] = euler
        rotations[bone] = (math.degrees(euler.x), math.degrees(euler.y), math.degrees(euler.z))

    locations = {"Hips": to_local_vec("Hips", hips_world or (0.0, 0.0, 0.0))}
    return Pose(frame=frame, rotations=rotations, locations=locations)


# ── Direction helpers ───────────────────────────────────────────────────────


def up_dir(lean: float = 0.0, tilt: float = 0.0) -> tuple:
    """A spine/neck/head direction. lean + = forward (-Y), tilt + = to the body's right."""
    v = (Matrix.Rotation(math.radians(-tilt), 3, "Y")
         @ Matrix.Rotation(math.radians(lean), 3, "X") @ Vector((0.0, 0.0, 1.0)))
    return tuple(v)


def arm_dir(side: int, down: float = 0.0, swing: float = 0.0) -> tuple:
    """An arm direction in spherical terms from the T-pose.

    down  = degrees below horizontal (90 = straight down, and non-degenerate there).
    swing = degrees from straight out sideways toward straight forward; past 90 the
            forearm crosses in front of the chest, which is what §03's carry needs.
    """
    d, s = math.radians(down), math.radians(swing)
    return (side * math.cos(d) * math.cos(s), -math.cos(d) * math.sin(s), -math.sin(d))


def leg_dir(side: int, swing: float = 0.0, out: float = 0.0) -> tuple:
    """A thigh/shank direction. swing + = forward of vertical, out + = away from the body."""
    sw, o = math.radians(swing), math.radians(out)
    return (side * math.sin(o), -math.sin(sw) * math.cos(o), -math.cos(sw) * math.cos(o))


FOOT_REST = Vector((0.0, BALL_Y - 0.015, 0.032 - ANKLE_Z)).normalized()
TOES_REST = Vector((0.0, -0.185 - BALL_Y, 0.026 - 0.032)).normalized()


# ── Ground contact ──────────────────────────────────────────────────────────
# Sole contact points in rest space, as offsets from the bone they belong to. The mesh
# models the sole flat on z = 0, so at rest all three sit exactly on the floor.

CONTACTS = (
    ("Foot", (0.0, 0.040, -0.095)),      # heel, back-bottom corner of the boot
    ("Toes", (0.0, 0.000, -0.032)),      # ball
    ("Toes", (0.0, -0.070, -0.032)),     # toe tip
)


def apply_pose(rig: bpy.types.Object, pose: Pose) -> None:
    """Pushes a Pose onto the rig WITHOUT keyframing it, so it can be measured."""
    for name, rot in pose.rotations.items():
        pb = rig.pose.bones[name]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = Euler([math.radians(a) for a in rot], "XYZ")
    for name, loc in pose.locations.items():
        rig.pose.bones[name].location = Vector(loc)
    bpy.context.view_layer.update()


def clear_pose(rig: bpy.types.Object) -> None:
    """Returns every bone to rest.

    Required, not tidiness. apply_pose() writes rotations straight onto the pose bones so
    a frame can be measured, and those values PERSIST with no action attached — NLA strips
    set to NOTHING contribute nothing, they do not reset anything. Exporting in that state
    ships a glTF whose default node transforms are whatever pose was measured last (in
    practice Death, the final clip built), so the model lies on the floor until an
    animation is played. The FBX bind pose comes from the rest matrices and survives it,
    but there is no reason to leave a live rig mid-pose either.
    """
    for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = Euler((0.0, 0.0, 0.0), "XYZ")
        pb.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
        pb.location = Vector((0.0, 0.0, 0.0))
        pb.scale = Vector((1.0, 1.0, 1.0))
    bpy.context.view_layer.update()


def bone_point(rig: bpy.types.Object, bone: str, rest_offset) -> Vector:
    """A rest-space offset from a bone's head, carried through that bone's current pose."""
    pb = rig.pose.bones[bone]
    local = REST[bone].inverted() @ Vector(rest_offset)
    return (rig.matrix_world @ pb.matrix) @ local


def foot_contacts(rig: bpy.types.Object) -> dict[str, Vector]:
    """The lowest sole point of each foot in the currently applied pose."""
    out = {}
    for side in ("Left", "Right"):
        pts = [bone_point(rig, side + bone, off) for bone, off in CONTACTS]
        out[side] = min(pts, key=lambda p: p.z)
    return out


def ground_locked(rig: bpy.types.Object, frame: int, spec: dict, hips_xy,
                  prev: dict[str, Euler] | None = None) -> tuple[Pose, float, float]:
    """Builds a keyframe and drops the hips until the lower foot rests on the floor.

    Authoring a hip-height curve by hand is how a walk cycle ends up skating or sinking
    ankle-deep, and neither is forgivable when §04's 청음사 identifies the monster BY the
    surface its footsteps land on (§12: 바닥 재질이 지도다). A player whose feet do not
    meet the floor is the same lie told about the team's own footsteps.

    The correction is exact in one pass: translating the hips translates every descendant
    rigidly, so the measured penetration is the offset needed. Returns the pose, the hip
    height it settled at, and the residual sole error.
    """
    pose = make_pose(frame, spec, hips_world=(hips_xy[0], hips_xy[1], 0.0), prev=prev)
    apply_pose(rig, pose)
    # The euler values are unaffected by the hip translation below, so a caller solving
    # only for the hip height may pass prev=None and rebuild the pose itself afterwards.
    low = min(p.z for p in foot_contacts(rig).values())
    pose.locations["Hips"] = to_local_vec("Hips", (hips_xy[0], hips_xy[1], -low))
    apply_pose(rig, pose)
    settled = foot_contacts(rig)
    hips = rig.pose.bones["Hips"]
    hip_z = ((rig.matrix_world @ hips.matrix).translation).z
    return pose, hip_z, min(p.z for p in settled.values())


def step_length(rig: bpy.types.Object) -> float:
    """Heel-to-toe distance between the two feet's ground contacts, in the applied pose.

    Measured between the CONTACT points rather than between the ankles: a real stride
    borrows a lot of its length from the trailing foot's plantarflexion, and measuring
    the ankles would under-report it and make §05's cadence arithmetic wrong.
    """
    c = foot_contacts(rig)
    return abs(c["Left"].y - c["Right"].y)


def foot_dir(side: int, pitch: float = 0.0, out: float = 0.0, rest=None) -> tuple:
    """A foot direction relative to the flat-on-the-floor rest pose.

    pitch + = toe up (dorsiflexion), out + = toe pointing away from the body. Measuring
    from rest rather than from horizontal matters: the mesh sole is modelled flat on
    z = 0, so pitch = 0 is genuinely flat and a heel strike is a positive number.
    """
    base = Vector(rest if rest is not None else FOOT_REST)
    v = (Matrix.Rotation(math.radians(side * out), 3, "Z")
         @ Matrix.Rotation(math.radians(-pitch), 3, "X") @ base)
    return tuple(v)


# ── Pose library ────────────────────────────────────────────────────────────


def merge(*specs) -> dict:
    out: dict = {}
    for s in specs:
        out.update(s)
    return out


def torso(lean: float = 0.0, twist: float = 0.0, tilt: float = 0.0,
          hips_yaw: float = 0.0, hips_tilt: float = 0.0,
          hips_lean: float | None = None) -> dict:
    """Distributes a total lean/twist over the spine. Values are ABSOLUTE per bone, so
    `lean` is the angle the upper chest ends up at and the lower bones take a share.

    `hips_lean` pins the pelvis independently. The still clips pin it: the legs are aimed
    in world space, but their exported curves are LOCAL, so a pelvis that drifts by half a
    degree writes motion into eight leg curves that §04's stillness check would then have
    to forgive.
    """
    return {
        "Hips": Aim(up_dir(lean * 0.10 if hips_lean is None else hips_lean, hips_tilt),
                    roll=hips_yaw),
        "Spine": Aim(up_dir(lean * 0.40, tilt * 0.4), roll=twist * 0.35),
        "Chest": Aim(up_dir(lean * 0.75, tilt * 0.8), roll=twist * 0.75),
        "UpperChest": Aim(up_dir(lean, tilt), roll=twist),
    }


def head(lean: float = 0.0, yaw: float = 0.0, tilt: float = 0.0, neck: float = 0.0) -> dict:
    """Absolute head aim. `lean` + = looking down, `neck` + = the neck craning forward."""
    return {"Neck": Aim(up_dir(neck, tilt * 0.3), roll=yaw * 0.35),
            "Head": Aim(up_dir(lean, tilt), roll=yaw)}


def arm(side: int, up_down: float, up_swing: float, lo_down: float, lo_swing: float,
        hand_down: float | None = None, hand_swing: float | None = None,
        shoulder_down: float = 8.0, shoulder_swing: float = 5.0) -> dict:
    s = "Left" if side > 0 else "Right"
    return {
        f"{s}Shoulder": Aim(arm_dir(side, shoulder_down, shoulder_swing)),
        f"{s}UpperArm": Aim(arm_dir(side, up_down, up_swing)),
        f"{s}LowerArm": Aim(arm_dir(side, lo_down, lo_swing)),
        f"{s}Hand": Aim(arm_dir(side, lo_down if hand_down is None else hand_down,
                                lo_swing if hand_swing is None else hand_swing)),
    }


def leg(side: int, thigh: float, shank: float, foot: float = 0.0, toe: float = 0.0,
        out: float = 1.5) -> dict:
    """thigh/shank in degrees forward of vertical (absolute). foot/toe + = toe up."""
    s = "Left" if side > 0 else "Right"
    return {
        f"{s}UpperLeg": Aim(leg_dir(side, thigh, out)),
        f"{s}LowerLeg": Aim(leg_dir(side, shank, out * 0.5)),
        f"{s}Foot": Aim(foot_dir(side, foot, out * 1.5)),
        f"{s}Toes": Aim(foot_dir(side, toe, out * 1.5, rest=TOES_REST)),
    }


# The right arm in every clip where the flashlight is in hand: elbow folded ~110°, upper
# arm forward and the forearm angled up, so the torch is carried in front of the lower
# chest with the lens ~0.6 m ahead of the hips, pointing where the player faces.
#
# §05 asks two things of this pose and they pull the same way. The beam is a pointing
# device — *"손전등이 포인터가 된다"* — so it is HELD, not swung. And the owner has to be
# able to see it: *"자기 몸은 안 보이므로 손만 있으면 된다"* is a promise that the hands
# are on screen, and at 80° of vertical FOV (§05) an arm hanging at hip height puts the
# fist 22° below the bottom edge. That was the whole defect: the hands were authored
# correctly for the three teammates who look at this model and off-screen for the one
# person inside it. `verify_first_person` now checks the second reading too.
# Measured from the inside, not judged: `FirstPersonHandsShot` reports the viewport
# coordinate of each hand, and at up_swing 59 the torch hand landed at **x = 0.79** — a
# fist in the right-hand margin, half of it past the edge, with the same in the left. §05
# promises 「손만 있으면 된다」 and two lumps in the corners is not that. Swinging the
# upper arm forward and folding the elbow harder brings it to x ≈ 0.68 without moving the
# lens, which `verify_first_person` and POSE_MEASURE both still check.
FLASHLIGHT_ARM = dict(up_down=26.0, up_swing=76.0, lo_down=-46.0, lo_swing=106.0,
                      hand_down=-10.0, hand_swing=106.0)

# The free arm: raised to the same lower-chest height and slightly wider, so an empty
# hand is a thing the owner can SEE rather than an absence they have to infer. §10 makes
# switching the light off the most repeated trade in the game, and the state you switch
# into has to look like something.
FREE_ARM = dict(up_down=28.0, up_swing=72.0, lo_down=-46.0, lo_swing=102.0,
                hand_down=-26.0, hand_swing=102.0)

# Crouched, the eye drops 0.38 m and the arm does not, so the same pose puts the lens
# 0.11 m under the eye instead of the 0.15 m `verify_motion` requires — a torch held level
# with your own face lights the inside of your view. Dropping the elbow is what a person
# does when they take cover anyway: the light comes down and in.
CROUCH_FLASHLIGHT_ARM = dict(FLASHLIGHT_ARM)
CROUCH_FLASHLIGHT_ARM.update(up_down=FLASHLIGHT_ARM["up_down"] + 6.0,
                             lo_down=FLASHLIGHT_ARM["lo_down"] + 6.0,
                             hand_down=FLASHLIGHT_ARM["hand_down"] + 4.0)


def stand_spec(**over) -> dict:
    """The reference standing pose. Every clip is a departure from this one."""
    spec = merge(
        torso(lean=4.0),
        head(lean=2.0, neck=10.0),
        arm(1, **FREE_ARM),
        arm(-1, **FLASHLIGHT_ARM),
        leg(1, 0.0, -2.0, 0.0, 0.0),
        leg(-1, 0.0, -2.0, 0.0, 0.0),
    )
    spec.update(over)
    return spec


# ── Gait tables ─────────────────────────────────────────────────────────────
# Four phases per cycle, angles absolute from vertical (+ = forward), feet relative to
# flat. Separate tables per clip rather than one table scaled by an amplitude: a loaded
# shuffle is not a small run, and §08's "욕심이 곧 속도 저하" is about posture as much as
# speed. Cadence is set by the key spacing in each action builder; step length comes out
# of the geometry and is MEASURED from the posed feet afterwards.

WALK_GAIT = {
    "contact": dict(thigh=27.0, shank=22.0, foot=13.0, toe=0.0),
    "pass":    dict(thigh=-2.0, shank=-18.0, foot=-3.0, toe=0.0),
    "toeoff":  dict(thigh=-21.0, shank=-47.0, foot=-38.0, toe=22.0),
    "swing":   dict(thigh=15.0, shank=-34.0, foot=6.0, toe=0.0),
}

# Knee flexion is thigh − shank, so the table reads as a stride: 14° at the strike,
# 90° folded in flight, unfolding again to reach. §06's 4.5 m/s run is 0.3 m/s slower
# than the monster, and a runner who does not look committed makes that gap read as a
# choice rather than as the whole game's tension.
RUN_GAIT = {
    "contact":  dict(thigh=25.0, shank=12.0, foot=4.0, toe=0.0),
    "stance":   dict(thigh=4.0, shank=-14.0, foot=-6.0, toe=0.0),
    "toeoff":   dict(thigh=-21.0, shank=-42.0, foot=-40.0, toe=24.0),
    "fold":     dict(thigh=-6.0, shank=-96.0, foot=-10.0, toe=0.0),
    "swingup":  dict(thigh=22.0, shank=-74.0, foot=8.0, toe=0.0),
    "swingfwd": dict(thigh=40.0, shank=-32.0, foot=12.0, toe=0.0),
    "reach":    dict(thigh=38.0, shank=4.0, foot=10.0, toe=0.0),
    "predrop":  dict(thigh=31.0, shank=14.0, foot=6.0, toe=0.0),
}

CARRY_GAIT = {
    "contact": dict(thigh=22.0, shank=17.0, foot=9.0, toe=0.0),
    "pass":    dict(thigh=-1.0, shank=-9.0, foot=-2.0, toe=0.0),
    "toeoff":  dict(thigh=-17.0, shank=-40.0, foot=-24.0, toe=14.0),
    "swing":   dict(thigh=13.0, shank=-27.0, foot=5.0, toe=0.0),
}

HEAVY_GAIT = {
    "contact": dict(thigh=17.0, shank=8.0, foot=4.0, toe=0.0),
    "pass":    dict(thigh=2.0, shank=-14.0, foot=-2.0, toe=0.0),
    "toeoff":  dict(thigh=-12.0, shank=-34.0, foot=-16.0, toe=10.0),
    "swing":   dict(thigh=11.0, shank=-30.0, foot=4.0, toe=0.0),
}

# The crouch cycle swings around a deeply flexed base rather than around vertical.
CROUCH_GAIT = {
    "contact": dict(thigh=80.0, shank=-30.0, foot=-4.0, toe=0.0),
    "pass":    dict(thigh=58.0, shank=-51.0, foot=0.0, toe=0.0),
    "toeoff":  dict(thigh=36.0, shank=-64.0, foot=-18.0, toe=14.0),
    "swing":   dict(thigh=66.0, shank=-78.0, foot=8.0, toe=0.0),
}

# Key orders. Each entry is (left leg phase, right leg phase, flight lift in metres).
# A walk has no flight: one foot is always down, which is the definition of walking.
WALK_ORDER = (("contact", "toeoff", 0.0), ("pass", "swing", 0.0),
              ("toeoff", "contact", 0.0), ("swing", "pass", 0.0))

# A run does. Eight keys per stride, two of them airborne, so stance is a quarter of the
# cycle — which is what lets §05's 4.5 m/s come out of a stride a 0.93 m leg can actually
# make. Reading down the left column gives one full leg cycle; the right leg is the same
# list rotated by four.
RUN_ORDER = (("contact", "swingup", 0.0), ("stance", "swingfwd", 0.0),
             ("toeoff", "reach", 0.0), ("fold", "predrop", 0.042),
             ("swingup", "contact", 0.0), ("swingfwd", "stance", 0.0),
             ("reach", "toeoff", 0.0), ("predrop", "fold", 0.042))


def gait_legs(gait: dict, left_phase: str, right_phase: str, out: float = 1.5) -> dict:
    return merge(leg(1, out=out, **gait[left_phase]),
                 leg(-1, out=out, **gait[right_phase]))


# ── Actions ─────────────────────────────────────────────────────────────────
# Every cycle uses evenly spaced keys, because blendkit.make_action closes a loop by
# copying the first pose to `last + (last - second_last)`. Even spacing therefore makes the
# cycle exactly `spacing × len(order)` frames long, which is what the cadence arithmetic
# below depends on.


@dataclass
class Clip:
    """One authored action plus what has to be measured about it."""

    name: str
    poses: list
    loop: bool = True
    cycle_frames: int = 0
    measure_frame: int = 1
    note: str = ""
    target_speed: float | None = None
    speed_tolerance: float = 0.0
    stance_travel: float = 0.0
    stance_seconds: float = 0.0
    speed: float = 0.0
    hip_lo: float = 0.0
    hip_hi: float = 0.0
    sole_error: float = 0.0
    action: object = field(default=None, repr=False)


def cycle_frames_for(spacing: int, count: int = 4) -> tuple[list[int], int]:
    keys = [1 + spacing * i for i in range(count)]
    return keys, spacing * count


def build_cycle(rig, name: str, spacing: int, gait: dict, body_fn, order,
                out: float = 1.5, target_speed: float | None = None,
                speed_tolerance: float = 0.0, note: str = "",
                stance_keys: tuple[int, int] = (0, 2)) -> Clip:
    """A locomotion cycle, ground-locked key by key.

    `order` is one (left_phase, right_phase, flight_lift) per key. `body_fn(i, left,
    right) -> (spec, (x, y))` supplies the torso, head, arms and the horizontal hip sway.

    The hip HEIGHT is never authored. Grounded keys get it from ground_locked(), so the
    vertical bob is a consequence of the leg angles instead of a second curve that has to
    be kept in agreement with them. Keys with a non-zero `flight_lift` are airborne — both
    feet are clear — so those are placed relative to the highest grounded key instead;
    ground-locking a flight frame would weld a foot to the floor mid-stride.

    `stance_keys` names the key where the left foot lands and the key where it leaves.
    """
    keys = [1 + spacing * i for i in range(len(order))]
    specs, sways = [], []
    for i, (left, right, _lift) in enumerate(order):
        spec, hips_xy = body_fn(i, left, right)
        specs.append(merge(spec, gait_legs(gait, left, right, out)))
        sways.append(hips_xy)

    # Pass 1 — hip heights only. Grounded keys are solved first because the flight keys are
    # placed relative to them. The throwaway poses this builds are discarded: they cannot
    # be the exported ones, because solving out of key order would thread the euler
    # continuity chain out of order too (see pass 2).
    hip_zs: dict[int, float] = {}
    residuals = []
    for i, (_l, _r, lift) in enumerate(order):
        if lift:
            continue
        _pose, hip_z, residual = ground_locked(rig, keys[i], specs[i], sways[i])
        hip_zs[i] = hip_z
        residuals.append(residual)
    airborne_base = max(hip_zs.values())
    for i, (_l, _r, lift) in enumerate(order):
        if lift:
            hip_zs[i] = airborne_base + lift

    # Pass 2 — the exported poses, built in KEY ORDER so each frame's euler is chosen
    # compatible with the frame before it. Out of order, a 179° → -179° flip survives into
    # the curve and the limb takes the long way round on playback.
    poses, left_contacts = [], {}
    prev: dict[str, Euler] = {}
    for i, frame in enumerate(keys):
        pose = make_pose(frame, specs[i],
                         hips_world=(sways[i][0], sways[i][1], hip_zs[i] - HIP_Z),
                         prev=prev)
        apply_pose(rig, pose)
        poses.append(pose)
        left_contacts[i] = foot_contacts(rig)["Left"]

    # §05's m/s is a STANCE relation, not a whole-cycle one: the planted foot has to
    # travel backward at exactly the ground speed or it slides. Whole-cycle arithmetic
    # (2 × step / cycle) is only correct when stance is half the cycle, which is true of a
    # walk and false of a run — a run spends a quarter of its stride in flight, and
    # pretending otherwise is what forces an impossible stride length onto the legs.
    a, b = stance_keys
    travel = abs(left_contacts[b].y - left_contacts[a].y)
    stance_seconds = (keys[b] - keys[a]) / FPS
    speed = travel / stance_seconds if stance_seconds else 0.0

    return Clip(name=name, poses=poses,
                cycle_frames=spacing * len(order), measure_frame=keys[a],
                target_speed=target_speed,
                speed_tolerance=speed_tolerance, note=note,
                stance_travel=travel, stance_seconds=stance_seconds, speed=speed,
                hip_lo=min(hip_zs.values()), hip_hi=max(hip_zs.values()),
                sole_error=max(abs(r) for r in residuals))


def clip_idle(rig) -> Clip:
    """§04 관측자 — the ability needs 이동 정지 3초, so this loop is 3.07 s and moves
    nothing below the hips. Breathing lives in the spine, chest, neck and the free arm.

    §04 청음사 also depends on it: *"자기가 소리를 내면 못 듣는다"*. Standing still is a
    real gameplay state for two of the five roles, and the animation must not fake motion
    the systems have declared absent.
    """
    keys, _ = cycle_frames_for(23)
    # `up` is a DELTA on FREE_ARM's shoulder angle, not an absolute: the free arm's rest
    # height is a first-person framing decision now (`verify_first_person`), and an
    # absolute here would silently unhook the breathing from it the next time it moves.
    breath = [
        dict(lean=4.0, neck=10.0, hl=2.0, yaw=0.0, up=0.0),
        dict(lean=2.4, neck=8.5, hl=1.0, yaw=-6.0, up=-1.5),
        dict(lean=4.6, neck=10.6, hl=2.6, yaw=0.0, up=0.6),
        dict(lean=3.0, neck=9.0, hl=1.4, yaw=7.0, up=-1.0),
    ]
    poses, hip_zs, residuals = [], [], []
    prev: dict[str, Euler] = {}
    for frame, b in zip(keys, breath):
        spec = merge(
            torso(lean=b["lean"], hips_lean=0.4),
            head(lean=b["hl"], yaw=b["yaw"], neck=b["neck"]),
            arm(1, **{**FREE_ARM, "up_down": FREE_ARM["up_down"] + b["up"]}),
            arm(-1, **FLASHLIGHT_ARM),
            leg(1, 0.0, -2.0, 0.0, 0.0),
            leg(-1, 0.0, -2.0, 0.0, 0.0),
        )
        pose, hip_z, residual = ground_locked(rig, frame, spec, (0.0, 0.0), prev)
        poses.append(pose)
        hip_zs.append(hip_z)
        residuals.append(residual)
    return Clip(name="Idle", poses=poses, cycle_frames=92, measure_frame=keys[0],
                note="feet welded, §04 stillness", hip_lo=min(hip_zs), hip_hi=max(hip_zs),
                sole_error=max(abs(r) for r in residuals))


def clip_walk(rig) -> Clip:
    """§05 걷기 2.0 m/s. 24-frame cycle = 0.8 s for two steps.

    The right arm holds the flashlight and only bobs; the left arm swings. That asymmetry
    is the design showing: §13 networks the beam direction because it is information, and
    an arm that swings 60° would smear it across the corridor twice a second.
    """
    sway = (0.014, 0.0, -0.014, 0.0)
    twist = (-7.0, 0.0, 7.0, 0.0)

    def body(i, left, right):
        spec = merge(
            torso(lean=6.0, twist=twist[i], hips_yaw=-twist[i] * 0.8, hips_tilt=sway[i] * 90),
            head(lean=2.0, neck=11.0, yaw=-twist[i] * 0.25),
            # Deltas on FREE_ARM, so the swing rides whatever height the rest pose is
            # authored at and cannot drift away from the framing check.
            arm(1, **{**FREE_ARM,
                      "up_down": FREE_ARM["up_down"] + 14.0 * math.sin(math.pi * i / 2),
                      "up_swing": FREE_ARM["up_swing"] - 20.0 * math.sin(math.pi * i / 2),
                      "lo_down": FREE_ARM["lo_down"] + 10.0 * math.sin(math.pi * i / 2),
                      "lo_swing": FREE_ARM["lo_swing"] - 18.0 * math.sin(math.pi * i / 2)}),
            arm(-1, **{**FLASHLIGHT_ARM,
                       "up_down": FLASHLIGHT_ARM["up_down"] + 3.0 * math.cos(math.pi * i / 2),
                       "lo_swing": FLASHLIGHT_ARM["lo_swing"] - 3.0 * math.cos(math.pi * i / 2)}),
        )
        return spec, (sway[i], 0.0)

    return build_cycle(rig, "Walk", 5, WALK_GAIT, body, WALK_ORDER,
                       target_speed=2.0, speed_tolerance=0.25, note="§05 걷기 2.0 m/s")


def clip_run(rig) -> Clip:
    """§05 달리기 4.5 m/s. 16-frame cycle = 0.533 s, torso pitched 14° forward.

    주자's 질주 (§04, 5.6 m/s) is this clip at 1.24× Animator speed. §06's whole tension
    lives in 4.5 < 4.8 < 5.6, and a second hand-authored clip for the sprint would only
    duplicate these curves while inviting the two to drift apart.
    """
    def body(i, left, right):
        # One full stride over eight keys, so everything above the hips runs at half the
        # leg frequency: the torso counter-rotates once per stride, not once per step.
        phase = math.sin(math.pi * i / 4.0)          # +1 at key 2, -1 at key 6
        beat = math.cos(math.pi * i / 2.0)           # per-step vertical accent
        twist = -12.0 * phase
        sway = 0.018 * phase
        spec = merge(
            torso(lean=14.0, twist=twist, hips_yaw=-twist * 0.9, hips_tilt=sway * 90),
            head(lean=-4.0, neck=20.0, yaw=-twist * 0.2),
            # A wider swing than the walk's, still expressed as a delta on FREE_ARM.
            arm(1, **{**FREE_ARM,
                      "up_down": FREE_ARM["up_down"] + 20.0 * phase,
                      "up_swing": FREE_ARM["up_swing"] - 26.0 * phase,
                      "lo_down": FREE_ARM["lo_down"] + 16.0 * phase,
                      "lo_swing": FREE_ARM["lo_swing"] - 18.0 * phase}),
            arm(-1, **{**FLASHLIGHT_ARM, "up_down": FLASHLIGHT_ARM["up_down"] + 4.0 * beat,
                       "lo_down": FLASHLIGHT_ARM["lo_down"] + 2.0 * beat,
                       "lo_swing": FLASHLIGHT_ARM["lo_swing"] - 6.0 * beat}),
        )
        return spec, (sway, 0.0)

    return build_cycle(rig, "Run", 2, RUN_GAIT, body, RUN_ORDER,
                       target_speed=4.5, speed_tolerance=0.55,
                       note="§05 달리기 4.5 m/s; 질주 = ×1.24")


# §03: both hands on the objective. Arms in, forearms crossing past 90° of swing so the
# palms face each other, and the load carried HIGH — up against the collarbones rather
# than at the navel. That height is not decoration: §03 says the carrier 앞을 보지
# 못한다, and an object held where the owner can see it is the only version of that
# sentence the owner experiences. From the inside the frame fills with two hands and the
# objective between them, no beam anywhere, which is the state read without a HUD.
CARRY_ARMS = dict(up_down=16.0, up_swing=78.0, lo_down=-53.0, lo_swing=104.0,
                  hand_down=-55.0, hand_swing=116.0, shoulder_down=2.0, shoulder_swing=16.0)

# §08 w5, two carriers: WIDE on a crate edge and a clear step lower than the objective,
# so the two two-handed states are never mistaken for each other — from the outside by
# the grip, and from the inside by where the hands sit in the frame.
# Two rules hold this pose in place and they pull against each other, which is why the
# numbers are written down rather than left as taste. The hands must sit at least 0.12 m
# below Carry's, so §08's two-person crate never reads as §03's objective; and they must
# stay inside §05's 80° frame with 3° to spare, or the owner is carrying something they
# cannot see. The band between the two is narrow — shortening the forearm to 0.2315 m for
# the scale anchor (gen_player_ai.HAND_LENGTH) closed it by about 2° — and 34°/−32° is
# where both hold: hand_z **1.314 m against Carry's 1.455**, worst key **36.6° below**
# centre against a 37° limit.
HEAVY_ARMS = dict(up_down=34.0, up_swing=70.0, lo_down=-32.0, lo_swing=86.0,
                  hand_down=-28.0, hand_swing=90.0, shoulder_down=6.0, shoulder_swing=10.0)


def carry_torso(i: int, twist_amp: float, sway_amp: float):
    return (-twist_amp, 0.0, twist_amp, 0.0)[i], (sway_amp, 0.0, -sway_amp, 0.0)[i]


def clip_carry(rig) -> Clip:
    """§03 목표물 운반 — *"양손을 쓴다 → 손전등을 들 수 없다"*, *"운반자는 앞을 보지 못한다"*.

    Authored at GameConstants' WalkSpeed × ObjectiveCarrySpeedMultiplier = 2.0 × 0.80 =
    1.6 m/s over a 28-frame cycle: short braced steps at a slightly quicker cadence than
    the free walk, which is what a load in front of the chest actually does to a stride.

    The legibility requirement is the point — three teammates have to see "that one is
    carrying it" instantly: arms in and occupied, torso tipped BACK against the weight,
    head down over the object, knees soft.
    """
    def body(i, left, right):
        twist, sway = carry_torso(i, 4.0, 0.010)
        spec = merge(
            torso(lean=-9.0, twist=twist, hips_yaw=-twist * 0.7, hips_tilt=sway * 80),
            head(lean=16.0, neck=20.0, yaw=-twist * 0.2),
            arm(1, **CARRY_ARMS),
            arm(-1, **CARRY_ARMS),
        )
        return spec, (sway, 0.0)

    return build_cycle(rig, "Carry", 5, CARRY_GAIT, body, WALK_ORDER, out=3.0,
                       target_speed=1.6, speed_tolerance=0.30,
                       note="§03 both hands; 2.0 × 0.80 ObjectiveCarrySpeedMultiplier")


def clip_carry_idle(rig) -> Clip:
    """The carry pose standing still — the escort's *"잠깐, 기다려"* moment (§03: the last
    trip is 목표물 1명 + 호송 3명, and the carrier stops whenever the escort clears ahead).

    Without this clip a Unity blend tree at zero speed falls back to Idle and the carrier's
    arms drop through the objective they are supposed to be holding.
    """
    keys, _ = cycle_frames_for(16)
    poses, hip_zs, residuals = [], [], []
    prev: dict[str, Euler] = {}
    strain = (0.0, 1.6, -0.8, 1.2)
    for i, frame in enumerate(keys):
        spec = merge(
            torso(lean=-9.0 + strain[i] * 0.5, tilt=strain[i] * 0.8, hips_lean=-0.9),
            head(lean=16.0 + strain[i], neck=20.0, yaw=strain[i] * 2.0),
            arm(1, **{**CARRY_ARMS, "up_down": CARRY_ARMS["up_down"] + strain[i]}),
            arm(-1, **{**CARRY_ARMS, "up_down": CARRY_ARMS["up_down"] + strain[i]}),
            leg(1, 3.0, -4.0, 1.0, 0.0, out=3.0),
            leg(-1, 3.0, -4.0, 1.0, 0.0, out=3.0),
        )
        pose, hip_z, residual = ground_locked(rig, frame, spec, (0.0, 0.0), prev)
        poses.append(pose)
        hip_zs.append(hip_z)
        residuals.append(residual)
    return Clip(name="CarryIdle", poses=poses, cycle_frames=64, measure_frame=keys[0],
                note="§03 carrier halted", hip_lo=min(hip_zs), hip_hi=max(hip_zs),
                sole_error=max(abs(r) for r in residuals))


def clip_carry_heavy(rig) -> Clip:
    """§08 대형 초상화 · 궤짝 (weight 5) — *"2인 운반 or 극심한 감속"*, and the document
    says outright these exist to produce one scene: *"두 명이 궤짝을 들고 좁은 통로를
    지나는데 괴물이 온다."*

    So this reads as half of a two-person carry: hands low and wide on a crate edge, hips
    5 cm down, deeper lean, 36-frame shuffle. §08 fixes no m/s for a two-person carry, so
    the clip is authored slower than Carry and the only assertion made is the ORDERING
    CarryHeavy < Carry < Walk < Run — which is what "욕심이 곧 속도 저하" actually claims.
    The host positions the crate between two ObjectiveMounts; it cannot be parented to both.
    """
    def body(i, left, right):
        twist, sway = carry_torso(i, 3.0, 0.014)
        spec = merge(
            torso(lean=-13.0, twist=twist, hips_yaw=-twist * 0.6, hips_tilt=sway * 70),
            head(lean=10.0, neck=16.0, yaw=-twist * 0.15),
            arm(1, **HEAVY_ARMS),
            arm(-1, **HEAVY_ARMS),
        )
        return spec, (sway, 0.0)

    return build_cycle(rig, "CarryHeavy", 6, HEAVY_GAIT, body, WALK_ORDER, out=5.0,
                       note="§08 w5, two carriers")


def clip_crouch(rig) -> Clip:
    """§12 은폐 지점 — *"출입구 근처에 은폐 지점이 있다"*, required for §07's 새벽 stage
    where the monster knows the exit.

    A crouch earns its clip only if it actually puts the player behind cover, so the check
    is on the measured HeadCameraAnchor height: 1.635 m standing → ~1.22 m here, a 0.41 m
    drop. Thigh 58° forward, shank 51° back = 109° of knee flexion, feet flat.
    """
    keys, _ = cycle_frames_for(20)
    poses, hip_zs, residuals = [], [], []
    prev: dict[str, Euler] = {}
    breath = (0.0, 1.4, -0.6, 1.0)
    for i, frame in enumerate(keys):
        spec = merge(
            torso(lean=24.0 + breath[i] * 0.6, hips_lean=2.4),
            head(lean=4.0 - breath[i], neck=26.0, yaw=breath[i] * 4.0),
            # Both arms pulled in a little against the crouched torso, as deltas on the
            # standing pair. The eye drops 0.39 m in this clip, so the framing check has
            # to keep holding here and not only when standing up straight.
            arm(1, **{**FREE_ARM, "up_down": FREE_ARM["up_down"] + 6.0,
                      "up_swing": FREE_ARM["up_swing"] - 4.0,
                      "lo_down": FREE_ARM["lo_down"] + 14.0}),
            arm(-1, **CROUCH_FLASHLIGHT_ARM),
            leg(1, 58.0, -50.6, 0.0, 0.0, out=6.0),
            leg(-1, 58.0, -50.6, 0.0, 0.0, out=6.0),
        )
        # Hips 0.10 m BACK, not just down. Dropping straight down puts the knees 0.37 m
        # ahead of the pelvis and the pose reads as sitting on an invisible chair; shifting
        # the weight back over the heels is what makes it read as taking cover.
        pose, hip_z, residual = ground_locked(rig, frame, spec, (0.0, 0.100), prev)
        poses.append(pose)
        hip_zs.append(hip_z)
        residuals.append(residual)
    return Clip(name="Crouch", poses=poses, cycle_frames=80, measure_frame=keys[0],
                note="§12 concealment", hip_lo=min(hip_zs), hip_hi=max(hip_zs),
                sole_error=max(abs(r) for r in residuals))


def clip_crouch_walk(rig) -> Clip:
    """Moving while concealed. §04 청음사's constraint makes this a real state rather than
    a convenience: *"자기가 소리를 내면 못 듣는다"* — the Listener has to travel without
    generating the footsteps that would mask the monster's own.

    No design number exists for a crouched speed, so this one is reported and not asserted.
    """
    def body(i, left, right):
        breath = (0.0, 1.0, -0.5, 0.8)[i]
        sway = (0.012, 0.0, -0.012, 0.0)[i]
        spec = merge(
            torso(lean=26.0 + breath, twist=(-5.0, 0.0, 5.0, 0.0)[i], hips_tilt=sway * 60),
            head(lean=2.0, neck=28.0, yaw=(-2.0, 0.0, 2.0, 0.0)[i]),
            arm(1, **{**FREE_ARM, "up_down": FREE_ARM["up_down"] + 5.0,
                      "lo_down": FREE_ARM["lo_down"] + 14.0}),
            # Same drop as Crouch and for the same measured reason: the eye is 0.32 m
            # lower here than standing, so the standing arm would hold the lens inside
            # the crouched view.
            arm(-1, **CROUCH_FLASHLIGHT_ARM),
        )
        return spec, (sway, 0.080)

    return build_cycle(rig, "CrouchWalk", 6, CROUCH_GAIT, body, WALK_ORDER, out=6.0,
                       note="§04 quiet travel; no design speed to match")


def clip_death(rig) -> Clip:
    """§09 사망 처리 — death is a transition into the ghost state, not an exit.

    Does NOT loop, and ends flat and settled rather than in a heap: §08 says
    *"사망자의 전리품은 떨어진다. 회수하려면 시체가 있는 곳으로 돌아가야 한다"*, so the
    body is a map marker that teammates have to navigate back to, and §09's ghost is
    *"자기 물건이 어디 있는지 보이는데 말할 수 없다"* — the corpse's pose is the thing the
    ghost is screaming about and cannot point at.

    Frame 1 is the standing pose so any state can cross-fade in. The last two keys are
    nearly identical: the settle has to stop, or a corpse breathes.
    """
    steps = [
        # frame, lean, neck, headlean, yaw, tilt, hips(world), legs, arms
        (1, 4.0, 10.0, 2.0, 0.0, 0.0, (0.0, 0.0, 0.0), (0.0, -2.0, 0.0), "stand"),
        (6, -16.0, -6.0, -22.0, -14.0, 8.0, (0.0, 0.04, -0.02), (-6.0, -14.0, -8.0), "jolt"),
        (14, 22.0, 24.0, 16.0, 10.0, -6.0, (0.0, -0.05, -0.26), (34.0, -46.0, 6.0), "buckle"),
        (23, 52.0, 40.0, 30.0, 18.0, -10.0, (0.0, -0.24, -0.55), (-14.0, -40.0, -12.0), "fall"),
        (32, 80.0, 66.0, 54.0, 26.0, -14.0, (0.0, -0.46, -0.76), (-62.0, -56.0, -22.0), "impact"),
        (41, 86.0, 78.0, 70.0, 34.0, -8.0, (0.0, -0.55, -0.810), (-80.0, -70.0, -20.0), "settle"),
        (48, 85.0, 80.0, 72.0, 35.0, -7.0, (0.0, -0.555, -0.812), (-81.0, -71.0, -20.0), "still"),
    ]
    fling = [
        (78.0, 6.0, 72.0, 18.0),
        (44.0, -28.0, 20.0, -34.0),
        (58.0, 26.0, 34.0, 40.0),
        (40.0, 62.0, 26.0, 78.0),
        (16.0, 88.0, 8.0, 104.0),
        (8.0, 96.0, 2.0, 112.0),
        (8.0, 97.0, 2.0, 113.0),
    ]
    poses = []
    prev: dict[str, Euler] = {}
    for (frame, lean, neck, hlean, yaw, tilt, hips, legs, _label), fl in zip(steps, fling):
        thigh, shank, foot = legs
        spec = merge(
            torso(lean=lean, tilt=tilt, twist=yaw * 0.5),
            head(lean=hlean, yaw=yaw, tilt=tilt, neck=neck),
            arm(1, up_down=fl[0], up_swing=fl[1], lo_down=fl[0] * 0.7, lo_swing=fl[1] + 14.0),
            arm(-1, up_down=fl[2], up_swing=fl[3], lo_down=fl[2] * 0.7, lo_swing=fl[3] + 10.0),
            leg(1, thigh, shank, foot, 0.0, out=7.0),
            leg(-1, thigh - 8.0, shank + 6.0, foot - 6.0, 0.0, out=-3.0),
        )
        poses.append(make_pose(frame, spec, hips_world=hips, prev=prev))
    return Clip(name="Death", poses=poses, loop=False, measure_frame=48,
                note="§09 → ghost; corpse marks dropped loot (§08)")


CLIP_BUILDERS = (clip_idle, clip_walk, clip_run, clip_crouch, clip_crouch_walk,
                 clip_carry, clip_carry_idle, clip_carry_heavy, clip_death)


# ── Measurement ─────────────────────────────────────────────────────────────

_BONE_RE = re.compile(r'pose\.bones\["([^"]+)"\]\.(\w+)')


def measure_action(action) -> dict:
    """Per-bone peak-to-peak excursion in degrees, read back off the keyframes.

    A generator can run clean and still emit an Idle that shuffles its feet or a Run whose
    legs barely move. These are the numbers that make that visible instead of leaving it
    to be noticed in Unity.
    """
    per_bone: dict[str, float] = {}
    loc_span = 0.0
    max_step = 0.0
    keys = curves = 0
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
            per_bone[bone] = max(per_bone.get(bone, 0.0),
                                 math.degrees(max(vals) - min(vals)))
            # Largest jump between CONSECUTIVE keys. An euler curve that wraps (179° to
            # -179°) has a tiny peak-to-peak span and a 358° step, and on playback the
            # limb spins the long way round. This is the number that catches it.
            for a, b in zip(vals, vals[1:]):
                max_step = max(max_step, abs(math.degrees(b - a)))
        elif prop == "location":
            loc_span = max(loc_span, max(vals) - min(vals))

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
        "max_deg": max(per_bone.values()) if per_bone else 0.0,
        "loc_span": loc_span,
        "max_step_deg": max_step,
    }


def eval_bone(rig: bpy.types.Object, frame: int, bone: str) -> tuple[Vector, Vector]:
    """(head position, bone direction) in world space at `frame`.

    Read off the depsgraph-evaluated copy, not the original object: the original's pose
    matrices can be stale right after frame_set, and a stale measurement is worse than
    none because it looks like a measurement.
    """
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    ev = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
    pb = ev.pose.bones[bone]
    m = ev.matrix_world @ pb.matrix
    return m.translation.copy(), m.col[1].to_3d().normalized()


REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
GAME_CONSTANTS = os.path.join(
    REPO_ROOT, "unity", "HorrorGame", "Assets", "Scripts", "Core", "GameConstants.cs")

# The narrowest frame the game is played in. §05 fixes the VERTICAL field of view, so a
# wider monitor only ever shows more; checking the hands against 16:9 is therefore the
# conservative case, and it is the shape every review render in this repo is taken at.
VIEW_ASPECT = 16.0 / 9.0


def fov_default_degrees() -> float:
    """§05's default vertical FOV, parsed from GameConstants rather than repeated here.

    §05 calls FOV a balance item — *"기본 80, 조절 범위 70~90"* — and the whole point of
    the checks below is that the hands stay inside the frame the player actually gets.
    A number typed into this file would go stale the day that one is retuned, and the
    failure would be silent: hands that had quietly left the screen again.
    """
    try:
        with open(GAME_CONSTANTS, "r", encoding="utf-8") as handle:
            source = handle.read()
    except OSError as exc:
        blendkit.fail(f"cannot read {GAME_CONSTANTS}: {exc}. The first-person framing of the "
                      "hands is checked against §05's FOV, and guessing it is not a check.")
        raise

    match = re.search(r"FovDefault\s*=\s*([0-9.]+)f", source)
    if match is None:
        blendkit.fail("GameConstants.FovDefault is no longer a plain assignment — the hands "
                      "can no longer be checked against the frame §05 gives the player.")
    return float(match.group(1))


def view_angles(eye: Vector, point: Vector) -> tuple[float, float, float]:
    """Where a point sits in the owner's view: (degrees below the axis, degrees off
    centre, metres in front).

    The camera's POSITION is the HeadCameraAnchor bone and its ROTATION is the player's
    (PlayerCameraRig takes only the position — an animation that aimed the view would be
    an animation aiming the flashlight, §05). So the axis here is world forward from
    wherever the animation has put the eye, which is exactly what the player looks along
    when they are not pitching the mouse. Negative "below" means above the axis.
    """
    forward = eye.y - point.y                  # the model faces -Y
    if forward <= 1e-4:
        return 180.0, 180.0, forward
    return (math.degrees(math.atan2(eye.z - point.z, forward)),
            math.degrees(math.atan2(abs(point.x - eye.x), forward)),
            forward)


def pose_metrics(rig: bpy.types.Object, frame: int) -> dict:
    """The handful of world-space facts the design's requirements are actually about."""
    hips, _ = eval_bone(rig, frame, "Hips")
    chest, _ = eval_bone(rig, frame, "UpperChest")
    lhand, _ = eval_bone(rig, frame, "LeftHand")
    rhand, _ = eval_bone(rig, frame, "RightHand")
    eye, look = eval_bone(rig, frame, "HeadCameraAnchor")
    mount, beam = eval_bone(rig, frame, "FlashlightMount")
    obj_mount, _ = eval_bone(rig, frame, "ObjectiveMount")
    head, _ = eval_bone(rig, frame, "Head")
    lball, _ = eval_bone(rig, frame, "LeftToes")
    rball, _ = eval_bone(rig, frame, "RightToes")
    hands_mid = (lhand + rhand) * 0.5
    return {
        "hips": hips, "chest": chest, "head": head,
        "lhand": lhand, "rhand": rhand, "hands_mid": hands_mid,
        "hand_gap": (lhand - rhand).length,
        "hand_z": hands_mid.z,
        "eye": eye, "eye_z": eye.z, "look": look,
        # Where each hand and the torch land in the owner's own frame (§05).
        "view_left": view_angles(eye, lhand),
        "view_right": view_angles(eye, rhand),
        "view_mount": view_angles(eye, mount),
        "mount": mount, "beam": beam,
        "mount_fwd": hips.y - mount.y,          # metres ahead of the hips (-Y is forward)
        "objective_reach": (hands_mid - obj_mount).length,
        "lean_back": chest.y - hips.y,          # + = upper body behind the hips
        "lowest_foot_z": min(lball.z, rball.z),
    }


# ── File read-back ──────────────────────────────────────────────────────────


def fbx_objects(path: str, class_token: bytes) -> list[str]:
    """Names of FBX object records of one class, read back out of the written file.

    Not decoration. `bake_anim_use_all_bones` silently drops every non-active action when
    the actions are not stashed, and an export that lost four of five materials or the
    mount bones looks identical from the outside. Binary FBX stores an object's name as
    ``<name>\\x00\\x01<Class>``, so walking back from each class token recovers it.
    """
    with open(path, "rb") as fh:
        data = fh.read()
    found = []
    for match in re.finditer(re.escape(class_token), data):
        i = match.start()
        if data[i - 2:i] != b"\x00\x01":
            continue                      # the ObjectType definition carries no name
        j = k = i - 2
        while k > 0 and 32 <= data[k - 1] < 127:
            k -= 1
        found.append(data[k:j].decode("ascii", "replace"))
    return found


def glb_animations(path: str) -> list[dict]:
    """Animation names AND measured durations from the GLB.

    The duration is the real cross-check on clip length: it comes from the max of each
    sampler's input accessor, i.e. the keyframe times actually written to the file, not
    from what the action claimed in Blender.
    """
    with open(path, "rb") as fh:
        buf = fh.read()
    if buf[:4] != b"glTF":
        return []
    off, doc = 12, None
    while off + 8 <= len(buf):
        clen, ctype = struct.unpack_from("<I4s", buf, off)
        off += 8
        if ctype == b"JSON":
            doc = json.loads(buf[off:off + clen].decode("utf-8"))
            break
        off += clen
    if doc is None:
        return []
    accessors = doc.get("accessors", [])
    out = []
    for anim in doc.get("animations", []):
        lo, hi = math.inf, -math.inf
        for sampler in anim.get("samplers", []):
            acc = accessors[sampler["input"]]
            if "min" in acc and "max" in acc:
                lo = min(lo, acc["min"][0])
                hi = max(hi, acc["max"][0])
        out.append({"name": anim.get("name", "<unnamed>"),
                    "seconds": (hi - lo) if math.isfinite(lo) else 0.0,
                    "channels": len(anim.get("channels", []))})
    return out


# ── Optional preview render ─────────────────────────────────────────────────


def render_previews(rig, body, out_dir: str, shots) -> list[str]:
    """Solid-shaded PNGs of specific frames, so the poses can be LOOKED at.

    Env-gated (HORROR_PLAYER_PREVIEW_DIR) and never part of the shipped output: a
    production run should not spend seconds rendering, but a headless generator that
    cannot be looked at gets its poses wrong silently.
    """
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = 52.0
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    sun_data = bpy.data.lights.new("PreviewSun", type="SUN")
    sun_data.energy = 4.0
    sun = bpy.data.objects.new("PreviewSun", sun_data)
    sun.rotation_euler = Euler((math.radians(55.0), 0.0, math.radians(35.0)), "XYZ")
    bpy.context.collection.objects.link(sun)

    scene.render.resolution_x = 520
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    written = []

    for engine in ("BLENDER_WORKBENCH", "CYCLES"):
        scene.render.engine = engine
        if engine == "CYCLES":
            scene.cycles.samples = 12
            scene.cycles.use_denoising = False
        else:
            shading = scene.display.shading
            shading.light = "STUDIO"
            shading.color_type = "MATERIAL"
        written = []
        for name, frame, loc, target in shots:
            scene.frame_set(frame)
            cam.location = Vector(loc)
            direction = Vector(target) - Vector(loc)
            cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
            path = os.path.join(out_dir, f"{name}.png")
            scene.render.filepath = path
            try:
                bpy.ops.render.render(write_still=True)
            except Exception as exc:                       # noqa: BLE001
                print(f"PREVIEW_ENGINE_FAILED {engine}: {exc}")
                written = []
                break
            if os.path.exists(path) and os.path.getsize(path) > 1024:
                written.append(path)
        if written:
            print(f"PREVIEW_ENGINE {engine}")
            break

    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.objects.remove(sun, do_unlink=True)
    scene.camera = None
    return written


def render_first_person(rig, out_dir: str, shots) -> list[str]:
    """The owner's own view: camera on HeadCameraAnchor, §05's FOV, 16:9.

    Deliberately not the same rig as `render_previews`. That one photographs the model
    for the three teammates who look at it; this one photographs what the person inside
    it sees, and the two disagree — a pose can be perfect in one and empty in the other,
    which is exactly the defect this pass exists to close. It is Blender's answer to the
    question, in seconds, before Unity is asked the same question properly.

    The camera takes the anchor's POSITION and world forward for its rotation, matching
    PlayerCameraRig: the head animation moves the eye but never aims it, because §05
    makes where the player is pointing their own responsibility.
    """
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene

    cam_data = bpy.data.cameras.new("FirstPersonCam")
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle_y = math.radians(fov_default_degrees())
    cam_data.clip_start = 0.05
    cam = bpy.data.objects.new("FirstPersonCam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    # A torch, roughly. The real beam is a Unity spot on FlashlightMount; this is only
    # here so the preview is lit from where the light will be rather than from nowhere.
    lamp_data = bpy.data.lights.new("FirstPersonBeam", type="SPOT")
    lamp_data.energy = 60.0
    lamp_data.spot_size = math.radians(70.0)
    lamp_data.spot_blend = 0.6
    lamp = bpy.data.objects.new("FirstPersonBeam", lamp_data)
    bpy.context.collection.objects.link(lamp)

    fill_data = bpy.data.lights.new("FirstPersonFill", type="SUN")
    fill_data.energy = 1.2
    fill = bpy.data.objects.new("FirstPersonFill", fill_data)
    fill.rotation_euler = Euler((math.radians(50.0), 0.0, math.radians(200.0)), "XYZ")
    bpy.context.collection.objects.link(fill)

    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"

    written = []
    for name, frame in shots:
        eye, _ = eval_bone(rig, frame, "HeadCameraAnchor")
        mount, beam = eval_bone(rig, frame, "FlashlightMount")
        cam.location = eye
        cam.rotation_euler = Vector((0.0, -1.0, 0.0)).to_track_quat("-Z", "Y").to_euler()
        lamp.location = mount
        lamp.rotation_euler = Vector(beam).to_track_quat("-Z", "Y").to_euler()

        path = os.path.join(out_dir, f"fp_{name}.png")
        scene.render.filepath = path
        try:
            bpy.ops.render.render(write_still=True)
        except Exception as exc:                       # noqa: BLE001
            print(f"FP_PREVIEW_FAILED {name}: {exc}")
            break
        if os.path.exists(path) and os.path.getsize(path) > 1024:
            written.append(path)

    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.objects.remove(lamp, do_unlink=True)
    bpy.data.objects.remove(fill, do_unlink=True)
    scene.camera = None
    return written


# ── Design-linked verification ──────────────────────────────────────────────


def verify_materials() -> None:
    """§04's five role colours must be tellable apart under §05's darkness."""
    roles = [(s.name, s.color) for s in MATERIAL_SPECS if s.name in ROLE_MATERIALS]
    lumas = sorted((luma(c), n) for n, c in roles)
    gaps = [(lumas[i + 1][0] - lumas[i][0], lumas[i][1], lumas[i + 1][1])
            for i in range(len(lumas) - 1)]
    worst_gap = min(gaps)
    if worst_gap[0] < 0.05:
        blendkit.fail(
            f"role colours {worst_gap[1]} and {worst_gap[2]} differ by only "
            f"{worst_gap[0]:.3f} in luminance. §05's game is dark, where hue "
            "discrimination collapses and value is all that is left to read a role by."
        )
    worst_rgb = min(
        (math.dist(a[1], b[1]), a[0], b[0])
        for i, a in enumerate(roles) for b in roles[i + 1:]
    )
    if worst_rgb[0] < 0.20:
        blendkit.fail(f"role colours {worst_rgb[1]} and {worst_rgb[2]} are only "
                      f"{worst_rgb[0]:.3f} apart in RGB — §04 needs five distinct roles.")
    print("ROLE_COLOURS " + " ".join(
        f"{n}=rgb({c[0]:.2f},{c[1]:.2f},{c[2]:.2f})/luma{luma(c):.3f}" for n, c in roles))
    print(f"ROLE_COLOUR_SEPARATION min_luma_gap={worst_gap[0]:.3f} "
          f"min_rgb_distance={worst_rgb[0]:.3f}")


def verify_shape(report, metrics: dict) -> None:
    span, depth, height = report.size
    if not (1.70 <= height <= 1.80):
        blendkit.fail(f"height {height:.3f} m is not the ~1.75 m the brief asks for.")
    if span > 2.0:
        blendkit.fail(f"T-pose span {span:.3f} m — check unit scale or arm length.")
    stand = metrics["Idle"]
    if abs(stand["eye_z"] - EYE_Z) > 0.03:
        blendkit.fail(f"HeadCameraAnchor sits at {stand['eye_z']:.3f} m standing, not the "
                      f"{EYE_Z:.3f} m eye height the first-person camera is specified at.")
    if stand["lowest_foot_z"] > 0.10:
        blendkit.fail(f"the model floats: lowest ball-of-foot is {stand['lowest_foot_z']:.3f} m.")


def verify_motion(clips, stats: dict, metrics: dict, speeds: dict, worst: dict) -> None:
    leg_bones = [f"{s}{p}" for s in ("Left", "Right")
                 for p in ("UpperLeg", "LowerLeg", "Foot", "Toes")]

    for clip in clips:
        jump = stats[clip.name]["max_step_deg"]
        if jump > 175.0:
            blendkit.fail(
                f"{clip.name} has a {jump:.1f}° jump between consecutive keys — an euler "
                "curve wrapped and the bone will rotate the long way round. make_pose() "
                "passes the previous frame's euler to to_euler() to prevent exactly this."
            )
        if clip.sole_error > 0.001:
            blendkit.fail(f"{clip.name}: a foot misses the floor by "
                          f"{clip.sole_error * 1000:.1f} mm after ground-locking.")
        if clip.speed > 0.0 and clip.hip_hi - clip.hip_lo > 0.13:
            blendkit.fail(f"{clip.name} bobs {clip.hip_hi - clip.hip_lo:.3f} m vertically. "
                          "The stride is asking for more leg than the model has.")

    idle = stats["Idle"]
    leg_motion = max(idle["per_bone"].get(b, 0.0) for b in leg_bones)
    if leg_motion > 0.001:
        blendkit.fail(
            f"Idle moves the legs {leg_motion:.4f}°. §04's 관측자 ability requires "
            "이동 정지 3초 and 청음사 cannot hear over their own steps — a shuffling "
            "Idle is the animation contradicting a gameplay state."
        )
    if idle["loc_span"] > 0.005:
        blendkit.fail(f"Idle translates the hips {idle['loc_span']:.4f} m — see §04 stillness.")
    if idle["max_deg"] > 12.0:
        blendkit.fail(f"Idle peaks at {idle['max_deg']:.1f}° — it should read as breathing.")

    for clip in clips:
        if clip.target_speed is None:
            continue
        if abs(clip.speed - clip.target_speed) > clip.speed_tolerance:
            blendkit.fail(
                f"{clip.name} is authored at {clip.speed:.2f} m/s but §05/GameConstants "
                f"says {clip.target_speed:.2f} m/s (±{clip.speed_tolerance}). At 1.0× "
                "Animator speed the planted foot would slide across §12's floor."
            )

    if not (speeds["CarryHeavy"] < speeds["Carry"] < speeds["Walk"] < speeds["Run"]):
        blendkit.fail(
            "the gait speeds are not ordered CarryHeavy < Carry < Walk < Run "
            f"({speeds['CarryHeavy']:.2f}, {speeds['Carry']:.2f}, {speeds['Walk']:.2f}, "
            f"{speeds['Run']:.2f}). §08: 욕심이 곧 속도 저하."
        )
    if stats["Run"]["per_bone"]["LeftUpperLeg"] <= stats["Walk"]["per_bone"]["LeftUpperLeg"]:
        blendkit.fail("Run does not swing its hip further than Walk — §05's 2.0 vs 4.5 m/s "
                      "must be two visibly different gaits, not one played faster.")

    # §05 — the flashlight is a pointing device, so it has to be out in front and aimed
    # where the player faces in every clip where it is in hand.
    for name in ("Idle", "Walk", "Run", "Crouch", "CrouchWalk"):
        m = metrics[name]
        if m["mount_fwd"] < 0.18:
            blendkit.fail(f"{name}: FlashlightMount is only {m['mount_fwd']:.3f} m ahead of "
                          "the hips. §05 makes the beam a pointer — it must read as held out.")
        if -m["beam"].y < 0.80:
            blendkit.fail(f"{name}: the beam axis points {tuple(round(v, 2) for v in m['beam'])}, "
                          "barely forward. §13 networks pitch and yaw because the direction "
                          "is information; it has to start out pointing where the player looks.")
        # Between hip and eye height, wherever the hips happen to be — the same invariant
        # has to hold standing and crouched, so an absolute band would only be a band for
        # standing up straight.
        if not (m["hips"].z - 0.05 <= m["mount"].z <= m["eye_z"] - 0.15):
            blendkit.fail(f"{name}: FlashlightMount at {m['mount'].z:.2f} m is outside "
                          f"hip ({m['hips'].z:.2f} m) to eye ({m['eye_z']:.2f} m) height — "
                          "a light held there is not aimed where the player is looking.")

    # §03 — both hands on the objective, posture compensating for the weight.
    for name in ("Carry", "CarryIdle"):
        m = metrics[name]
        if m["hand_gap"] > 0.45:
            blendkit.fail(f"{name}: the hands are {m['hand_gap']:.3f} m apart. §03's objective "
                          "takes both hands and it has to be legible from a corridor away.")
        if m["lean_back"] < 0.02:
            blendkit.fail(f"{name}: the upper chest is only {m['lean_back']:.3f} m behind the "
                          "hips. §03's carrier is compensating for a load in front.")
        if m["objective_reach"] > 0.15:
            blendkit.fail(f"{name}: the palms are {m['objective_reach']:.3f} m from "
                          "ObjectiveMount — the socket is not where the hands are.")
        if m["head"].y > m["chest"].y:
            blendkit.fail(f"{name}: the head is not over the load. §03 — 앞을 보지 못한다.")

    heavy, carry = metrics["CarryHeavy"], metrics["Carry"]
    if heavy["hand_z"] > carry["hand_z"] - 0.12:
        blendkit.fail(f"CarryHeavy hands at {heavy['hand_z']:.3f} m vs Carry's "
                      f"{carry['hand_z']:.3f} m — §08's two-person crate must not look like "
                      "the one-person objective.")
    if heavy["hand_gap"] < carry["hand_gap"] + 0.15:
        blendkit.fail(f"CarryHeavy grip is {heavy['hand_gap']:.3f} m wide vs Carry's "
                      f"{carry['hand_gap']:.3f} m — a crate edge, not a hug.")

    # §12 — a crouch that does not put the player behind cover is decoration.
    crouch_eye = metrics["Crouch"]["eye_z"]
    if crouch_eye > 1.28:
        blendkit.fail(f"Crouch leaves the camera at {crouch_eye:.3f} m; §12's 은폐 지점 need a "
                      "real drop from the 1.635 m standing eye height.")

    # §05 from the inside. Third-person legibility and first-person framing are two
    # different requirements on the same eight arm angles, and until this ran only the
    # first one was checked — which is how a model whose Carry pose passed every §03
    # assertion still showed its owner nothing at all.
    verify_first_person(clips, worst)

    # §09 — the corpse is a marker teammates come back to (§08 dropped loot).
    dead = metrics["Death"]
    if dead["head"].z > 0.45 or dead["hips"].z > 0.35:
        blendkit.fail(f"Death ends with the head at {dead['head'].z:.3f} m and the hips at "
                      f"{dead['hips'].z:.3f} m — it has to end on the floor.")
    if (metrics["DeathSettle"]["head"] - dead["head"]).length > 0.03:
        blendkit.fail("Death is still moving on its last frames — a corpse does not breathe.")


# ── First person ────────────────────────────────────────────────────────────
# Clips whose right hand holds the torch, and clips where §03 has taken it away and put
# something two-handed there instead. Nothing else in this file needs to know the
# difference, so the two lists live next to the check that uses them.
TORCH_CLIPS = ("Idle", "Walk", "Run", "Crouch", "CrouchWalk")
TWO_HANDED_CLIPS = ("Carry", "CarryIdle", "CarryHeavy")

# Degrees of frame the hands must be inside the edge by. A hand exactly on the edge is
# a hand that leaves the screen on the next retune, and 3° is about 3 % of the frame.
FRAME_MARGIN_DEGREES = 3.0

# Nothing may sit closer to the eye than this. The camera's near plane is 0.05 m
# (PlayerCameraRig), so a hand nearer than that is clipped through; the margin over it
# is because a hand filling the frame is not a hand you can see either.
MIN_HAND_DISTANCE = 0.18


def frame_half_angles() -> tuple[float, float]:
    """§05's frame as half-angles: (vertical, horizontal) degrees from the view axis."""
    half_v = fov_default_degrees() * 0.5
    half_h = math.degrees(math.atan(math.tan(math.radians(half_v)) * VIEW_ASPECT))
    return half_v, half_h


def frame_deficit(view: tuple[float, float, float], half_v: float, half_h: float) -> float:
    """How far outside the owner's frame a measured point is. ≤ 0 means it is on screen.

    One signed number rather than three booleans, so "the worst frame of this clip" is a
    thing that can be picked rather than argued about.
    """
    below, off_centre, forward = view
    return max(abs(below) - (half_v - FRAME_MARGIN_DEGREES),
               off_centre - (half_h - FRAME_MARGIN_DEGREES),
               MIN_HAND_DISTANCE - forward)


def in_frame(view: tuple[float, float, float], half_v: float, half_h: float) -> bool:
    """Whether a measured (below, off-centre, forward) triple is on the owner's screen."""
    return frame_deficit(view, half_v, half_h) <= 0.0


def first_person_worst(rig: bpy.types.Object, clip) -> dict:
    """The least favourable authored key of a clip, per hand and for the torch.

    One frame per clip is not a check. A walk swings the free arm 30° and a run 50°, and
    it is the bottom of that swing — not the key the gait speed happens to be measured on
    — that decides whether the owner can see a hand. So every key is measured and the
    worst kept, per target and for "either hand", which is the one that has to be
    evaluated frame by frame rather than by combining two separate worst cases.
    """
    half_v, half_h = frame_half_angles()
    worst: dict = {"view_left": None, "view_right": None, "view_mount": None,
                   "any_hand": None, "any_hand_deficit": -math.inf}

    for pose in clip.poses:
        m = pose_metrics(rig, pose.frame)
        for key in ("view_left", "view_right", "view_mount"):
            view = m[key]
            if (worst[key] is None
                    or frame_deficit(view, half_v, half_h)
                    > frame_deficit(worst[key], half_v, half_h)):
                worst[key] = view

        # The better of the two hands on THIS frame; the worst such frame is the answer
        # to "is there ever a moment with no hand on screen".
        best = min((m["view_left"], m["view_right"]),
                   key=lambda v: frame_deficit(v, half_v, half_h))
        deficit = frame_deficit(best, half_v, half_h)
        if deficit > worst["any_hand_deficit"]:
            worst["any_hand_deficit"] = deficit
            worst["any_hand"] = best

    return worst


def verify_first_person(clips, worst: dict) -> None:
    """§05 — *"자기 몸은 안 보이므로 손만 있으면 된다."*

    The sentence is usually read as permission to skip modelling a body. Its other half
    is a requirement: if the body is not drawn and the hands are not on screen either,
    the player is a floating camera. So every clip a player is alive in has to put at
    least one hand in the frame §05 gives them, the clips that carry the torch have to
    show the torch, and §03's two-handed states have to show BOTH hands — that is what
    makes 목표물 운반 (both hands, no light) readable from the inside with no HUD.

    Measured from HeadCameraAnchor along world forward, because that is the camera:
    PlayerCameraRig takes the bone's position and leaves the rotation to the player.
    Every authored key is measured, not one per clip — see `first_person_worst`.
    """
    half_v, half_h = frame_half_angles()

    for clip in clips:
        if clip.name == "Death":
            # §09 hands the camera to the ghost. Nothing is asserted about a corpse's
            # framing because nobody is behind that eye any more.
            continue

        view = worst[clip.name]["any_hand"]
        if not in_frame(view, half_v, half_h):
            blendkit.fail(
                f"{clip.name}: at its worst key neither hand is inside the "
                f"{half_v * 2:.0f}° frame — the better of the two is {view[0]:.0f}° below "
                f"and {view[1]:.0f}° off centre at {view[2]:.2f} m. §05 gives the player no "
                "body and no third-person view; with no hands either there is nothing on "
                "screen that belongs to them.")

    for name in TORCH_CLIPS:
        w = worst[name]
        if not in_frame(w["view_mount"], half_v, half_h):
            v = w["view_mount"]
            blendkit.fail(
                f"{name}: the torch reaches {v[0]:.0f}° below and {v[1]:.0f}° off centre at "
                f"{v[2]:.2f} m, outside the frame. §10 makes turning the light on the most "
                "repeated trade in the game; the thing being traded has to be visible to "
                "the person making the trade.")
        if not in_frame(w["view_right"], half_v, half_h):
            v = w["view_right"]
            blendkit.fail(f"{name}: the torch hand reaches {v[0]:.0f}° below / {v[1]:.0f}° off "
                          f"centre — a beam with no hand under it reads as a floating light.")

    for name in TWO_HANDED_CLIPS:
        w = worst[name]
        for label, view in (("left", w["view_left"]), ("right", w["view_right"])):
            if not in_frame(view, half_v, half_h):
                blendkit.fail(
                    f"{name}: the {label} hand reaches {view[0]:.0f}° below and "
                    f"{view[1]:.0f}° off centre at {view[2]:.2f} m. §03 makes 「양손을 쓴다」 "
                    "the rule that costs the carrier their flashlight — one hand on screen "
                    "looks like the state where they still have it.")

    # §03 again: the two two-handed states must not photograph the same. The outside
    # already has to tell them apart (`verify_motion`); this is the inside.
    carry_h = worst["Carry"]["view_left"][0]
    heavy_h = worst["CarryHeavy"]["view_left"][0]
    if heavy_h - carry_h < 6.0:
        blendkit.fail(
            f"CarryHeavy sits {heavy_h:.0f}° below the axis and Carry {carry_h:.0f}° — under "
            "6° apart they are the same picture, and §08's 궤짝 and §03's 목표물 are two "
            "different decisions with two different costs.")


# ── Surfaces ────────────────────────────────────────────────────────────────
#
# The three neutral materials get procedural PBR maps. The five role materials do not
# and must not: Unity swaps slot 0 per RoleId, so anything painted into it would have
# to exist five times over, and §05 picks those colours for **value separation read at
# distance in the dark** — a texture is the one thing that would blur that separation.
#
# The noise generators come from gen_monster_model rather than being copied. They are
# general-purpose field synthesis with no monster in them, and a second copy would drift
# from the first the moment either is tuned. (They would sit better in blendkit or a
# texkit module; moving them means editing a generator that currently ships a shipping
# asset, so the import is the smaller risk today and the refactor is worth doing later.)
#
# WHY THIS DOES NOT REUSE gen_monster_model.write_skin
# ----------------------------------------------------
# That function asserts that **no texel may be brighter than the darkest wall in a §12
# corridor**. For the creature that rule is the whole point — §06 needs it to resolve
# out of the dark too late. For a teammate it is exactly backwards: three other players
# have to find this model, tell it from the other two, and read its role colour, and a
# character forced under the corridor's floor value is a silhouette nobody can identify.
# So the calibration here keeps gen_textures.py's albedo band and the roughness floor,
# and deliberately drops the corridor ceiling.

SURFACE_RES = 1024

# Targets inside gen_textures.py's [0.15, 0.45] band, spread for value separation so
# gear reads dark against coverall and skin reads light against both — the same
# reasoning §05 applies to the role colours, applied to the neutral ones.
SURFACE_TARGETS = {SKIN: 0.34, COVERALL: 0.25, GEAR: 0.165}


def build_coverall_surface(res: int) -> dict:
    """Heavy cotton work fabric: a woven grid, soft folds, and wear on the high points."""
    import gen_monster_model as gmm  # noqa: PLC0415

    # The weave: two perpendicular high-frequency bands. Stretching one field along each
    # axis is what makes it read as woven rather than as noise — a fabric has a grain
    # direction and a texture without one reads as concrete.
    warp = gmm._fbm(res, 8101, beta=0.9, low_cycles=150.0, stretch=14.0)
    weft = gmm._fbm(res, 8102, beta=0.9, low_cycles=150.0, stretch=1.0 / 14.0)
    weave = (warp + weft) * 0.5

    folds = gmm._normalise01(gmm._warp(
        gmm._fbm(res, 8103, beta=2.5, low_cycles=3.5),
        (gmm._fbm(res, 8104, beta=2.2, low_cycles=2.0) - 0.5) * 0.05,
        (gmm._fbm(res, 8105, beta=2.2, low_cycles=2.0) - 0.5) * 0.05))
    crease = gmm._smoothstep(0.34, 0.06, folds)
    soil = gmm._fbm(res, 8106, beta=2.3, low_cycles=5.0)

    height = np.clip(0.5 + (weave - 0.5) * 0.42 + (folds - 0.5) * 0.30
                     - crease * 0.30, 0.0, 1.0).astype(np.float32)

    base = np.array([0.252, 0.246, 0.232], dtype=np.float32)
    dirt = np.array([0.150, 0.138, 0.120], dtype=np.float32)
    colour = base.reshape(1, 1, 3) * (0.90 + 0.20 * weave)[..., None]
    grime = (crease * 0.55 + gmm._smoothstep(0.62, 1.0, soil) * 0.30)[..., None]
    colour = colour * (1.0 - grime) + dirt.reshape(1, 1, 3) * grime

    # Cotton is matte everywhere and slightly less matte where it has been rubbed
    # smooth — knees, seat, elbows. The fold bottoms stay dull because that is where
    # the dust sits.
    roughness = (0.92 - 0.10 * gmm._smoothstep(0.55, 1.0, weave)
                 + 0.04 * crease).astype(np.float32)

    return dict(albedo=colour, roughness=roughness, height=height,
                metallic=np.zeros((res, res), dtype=np.float32),
                world_size=0.55, height_scale=0.0035)


def build_skin_surface(res: int) -> dict:
    """Face and hands. Small area on screen, so pores over pattern."""
    import gen_monster_model as gmm  # noqa: PLC0415

    pore_f1, _, pore_owner = gmm._worley(res, 96, 8201, jitter=0.95)
    pore = gmm._smoothstep(0.0075, 0.0, pore_f1)
    tone = gmm._per_cell(pore_owner, 96, 8202, 0.90, 1.10)
    mottle = gmm._fbm(res, 8203, beta=2.0, low_cycles=7.0)
    grain = gmm._fbm(res, 8204, beta=1.1, low_cycles=30.0)

    height = np.clip(0.5 + (mottle - 0.5) * 0.30 + (grain - 0.5) * 0.16
                     - pore * 0.34, 0.0, 1.0).astype(np.float32)

    base = np.array([0.352, 0.268, 0.226], dtype=np.float32)
    flush = np.array([0.392, 0.246, 0.212], dtype=np.float32)
    colour = base.reshape(1, 1, 3) * (tone * (0.92 + 0.16 * mottle))[..., None]
    blush = gmm._smoothstep(0.58, 1.0, mottle)[..., None] * 0.22
    colour = colour * (1.0 - blush) + flush.reshape(1, 1, 3) * blush

    # Skin is the shiniest thing on the model and still not shiny. The pores are the
    # matte part: they hold what light does not bounce straight back.
    roughness = (0.56 + 0.22 * pore + 0.05 * (grain - 0.5)).astype(np.float32)

    return dict(albedo=colour, roughness=roughness, height=height,
                metallic=np.zeros((res, res), dtype=np.float32),
                world_size=0.28, height_scale=0.0015)


def build_gear_surface(res: int) -> dict:
    """Belt, boots, pads, radio: moulded rubber and webbing, scuffed on the edges."""
    import gen_monster_model as gmm  # noqa: PLC0415

    pebble_f1, _, pebble_owner = gmm._worley(res, 40, 8301, jitter=1.0)
    pebble = gmm._smoothstep(0.020, 0.0, pebble_f1)
    facet = gmm._per_cell(pebble_owner, 40, 8302, 0.86, 1.14)
    scuff = gmm._fbm(res, 8303, beta=1.7, low_cycles=9.0)
    grain = gmm._fbm(res, 8304, beta=1.0, low_cycles=40.0)

    height = np.clip(0.5 + (facet - 1.0) * 0.9 + (grain - 0.5) * 0.12
                     - pebble * 0.40, 0.0, 1.0).astype(np.float32)

    base = np.array([0.168, 0.163, 0.158], dtype=np.float32)
    worn = np.array([0.232, 0.226, 0.214], dtype=np.float32)
    colour = base.reshape(1, 1, 3) * (0.88 + 0.24 * facet)[..., None]
    rub = gmm._smoothstep(0.66, 1.0, scuff)[..., None] * 0.34
    colour = colour * (1.0 - rub) + worn.reshape(1, 1, 3) * rub

    # Moulded rubber is dull; the scuffed high points are where it has been polished,
    # and that is the only specular this model carries below the waist. Under §03's
    # moving spot it is what stops the boots from being two dark blobs.
    roughness = (0.72 - 0.30 * gmm._smoothstep(0.66, 1.0, scuff)
                 + 0.10 * pebble).astype(np.float32)

    return dict(albedo=colour, roughness=roughness, height=height,
                metallic=np.zeros((res, res), dtype=np.float32),
                world_size=0.32, height_scale=0.0028)


SURFACES = (
    (COVERALL, build_coverall_surface,
     "Work coverall. The largest surface on the model, so its weave sets how the whole "
     "figure reads under a beam. Matte, dirtied in the folds."),
    (SKIN, build_skin_surface,
     "Face and hands. The lightest of the three, which is what makes a head read as a "
     "head at the range §13 puts teammates at."),
    (GEAR, build_gear_surface,
     "Belt, boots, pads, radio. Darkest of the three and the only one with a specular "
     "worth the name, on the scuffed high points."),
)


def write_surface(name: str, build, target: float, note: str, res: int,
                  limits: dict) -> dict:
    """Calibrates and writes one player surface. See the section note on the ceiling."""
    import gen_monster_model as gmm  # noqa: PLC0415

    maps = build(res)
    colour = np.clip(maps["albedo"], 1e-4, None)
    gain = target / float(colour.mean())
    if not (1.0 / 2.6) <= gain <= 2.6:
        blendkit.fail(f"{name}: albedo needs a {gain:.2f}x correction to reach {target:.3f} — "
                      "the painted values are wrong, fix them rather than widening the gain.")
    if not limits["ALBEDO_MIN_LINEAR"] <= target <= limits["ALBEDO_MAX_LINEAR"]:
        blendkit.fail(f"{name}: target albedo {target:.3f} is outside gen_textures.py's "
                      f"[{limits['ALBEDO_MIN_LINEAR']}, {limits['ALBEDO_MAX_LINEAR']}] band.")

    albedo = np.clip(colour * gain, 0.010, 0.90).astype(np.float32)
    roughness = np.clip(maps["roughness"], limits["MIN_ROUGHNESS"], 1.0).astype(np.float32)
    metallic = np.clip(maps["metallic"], 0.0, 1.0).astype(np.float32)
    height = maps["height"]

    normal = gmm._height_to_normal(height, maps["world_size"], maps["height_scale"])
    occlusion = gmm._ambient_occlusion(height, maps["world_size"], maps["height_scale"])

    folder = os.path.join(gmm.TEXTURE_DIR, name)
    written = 0
    written += gmm._write_png(os.path.join(folder, name + "_albedo.png"),
                              gmm._to_u8(gmm._linear_to_srgb(albedo)))
    written += gmm._write_png(os.path.join(folder, name + "_normal.png"),
                              gmm._to_u8(normal * 0.5 + 0.5))
    written += gmm._write_png(os.path.join(folder, name + "_rough.png"),
                              gmm._to_u8(np.repeat(roughness[..., None], 3, axis=2)))
    written += gmm._write_png(os.path.join(folder, name + "_ao.png"),
                              gmm._to_u8(np.repeat(occlusion[..., None], 3, axis=2)))

    mask = np.zeros((res, res, 4), dtype=np.float32)
    mask[..., 0] = mask[..., 1] = mask[..., 2] = metallic
    mask[..., 3] = 1.0 - roughness
    written += gmm._write_png(os.path.join(folder, name + "_ms.png"), gmm._to_u8(mask))

    # Measured on the 8-bit sRGB bytes the shader will actually sample, not on the
    # float buffer: quantisation is the last thing that can push a map out of band,
    # and the quantised mean is the only one that exists by the time it is rendered.
    written_linear = gmm._srgb_to_linear(gmm._to_u8(gmm._linear_to_srgb(albedo)) / 255.0)
    quantised = float(np.mean(written_linear))
    if not limits["ALBEDO_MIN_LINEAR"] <= quantised <= limits["ALBEDO_MAX_LINEAR"]:
        blendkit.fail(f"{name}: albedo means {quantised:.4f} linear after 8-bit encoding, "
                      f"outside [{limits['ALBEDO_MIN_LINEAR']}, {limits['ALBEDO_MAX_LINEAR']}].")

    relief_mm = float(height.max() - height.min()) * maps["height_scale"] * 1000.0
    return dict(name=name, note=note, bytes=written,
                albedo_mean_linear=round(quantised, 4),
                albedo_gain=round(gain, 3),
                world_size_metres=maps["world_size"],
                roughness_mean=round(float(roughness.mean()), 4),
                metallic_mean=round(float(metallic.mean()), 4),
                relief_mm=round(relief_mm, 2),
                ao_mean=round(float(occlusion.mean()), 4),
                maps={
                    "albedo": f"{name}/{name}_albedo.png",
                    "normal": f"{name}/{name}_normal.png",
                    "roughness": f"{name}/{name}_rough.png",
                    "occlusion": f"{name}/{name}_ao.png",
                    "metallic_smoothness": f"{name}/{name}_ms.png",
                })


def uv_units_per_metre(body: bpy.types.Object) -> float:
    """UV density, so Unity can set a tiling that makes the world_size above real metres."""
    import gen_monster_model as gmm  # noqa: PLC0415
    return gmm.uv_units_per_metre(body)


def normalise_uv_density(obj: bpy.types.Object, target: float) -> float:
    """Scales one mesh's UVs so it samples the shared maps at the same metres-per-texel
    as the body does. Returns the achieved density.

    Smart-project packs each object into its own 0–1 square, so density is a function of
    how much surface an object happens to have. The manifest carries ONE tiling number
    per material, so three objects unwrapped independently would render the same coverall
    at three different weave scales.
    """
    density = uv_units_per_metre(obj)
    if density <= 0.0:
        blendkit.fail(f"{obj.name}: degenerate unwrap, cannot match the body's UV density.")

    factor = target / density
    uv_layer = obj.data.uv_layers.active
    for datum in uv_layer.data:
        datum.uv = (datum.uv[0] * factor, datum.uv[1] * factor)

    achieved = uv_units_per_metre(obj)
    if abs(achieved - target) > target * 0.01:
        blendkit.fail(f"{obj.name}: UV density is {achieved:.4f} after rescaling, not the "
                      f"body's {target:.4f}.")
    return achieved


# Bones that, if a mesh is skinned to them, put that mesh across the owner's near plane
# in first person. §05: 자기 몸은 안 보이므로. Anything weighted to one of these belongs
# on Player_Body and is hidden from its owner.
OCCLUDING_BONES = ("Head", "Neck", "Hips", "Spine", "Chest",
                   "LeftUpperLeg", "RightUpperLeg")


def verify_mesh_split(body, arms, torch) -> None:
    """The two properties the Unity classifier depends on, asserted where they are authored.

    PlayerRigParts decides what the owner sees from the MATERIAL SLOTS a renderer carries,
    never from a mesh name — and not from bone weights either, because a Humanoid import
    binds all 26 bones to all three meshes and the binding stops saying anything about what
    a mesh is made of. So the material composition is a contract:

        Player_Body    equipment AND skin      belt, boots, radio, and a face
        Player_Arms    skin, cloth, NO gear    the one mesh with no equipment on it
        Player_Torch   gear and nothing else   a held object

    The weights are checked as well, for a different reason: they are what makes the seam
    at the shoulder invisible and the torch rigid in the fist. Both checks are here so a
    regeneration that breaks either one fails in Blender rather than arriving in Unity as
    a player who cannot see their own hands.
    """
    body_mats = {m.name for m in body.data.materials}
    arm_mats = {m.name for m in arms.data.materials}
    torch_mats = {m.name for m in torch.data.materials}

    if not ({GEAR, SKIN} <= body_mats):
        blendkit.fail(f"Player_Body carries {sorted(body_mats)}; the Unity classifier finds "
                      f"the body by it having BOTH {GEAR} and {SKIN} — equipment and a face.")
    if GEAR in arm_mats or SKIN not in arm_mats:
        blendkit.fail(
            f"Player_Arms carries {sorted(arm_mats)}. It is recognised as the owner's own "
            f"hands by having {SKIN} and NO {GEAR}: every piece of equipment on this model "
            "is on the body or in the hand, and putting a strap or a watch on the sleeve "
            "would make the arms read as a torso and stop being drawn.")
    if torch_mats != {GEAR}:
        blendkit.fail(
            f"Player_Torch carries {sorted(torch_mats)}, not {GEAR} alone. A held item is "
            "recognised by being one slot of equipment; a second material on it makes it "
            "part of the person and it stops being switchable.")
    if arm_mats and list(arms.data.materials)[0].name != ROLE_MATERIALS[0]:
        blendkit.fail("Player_Arms slot 0 is not " + ROLE_MATERIALS[0] + " — §04 swaps slot 0 "
                      "per RoleId on every renderer, and the bicep bands are on this one.")

    body_groups = set(body.vertex_groups.keys())
    arm_groups = set(arms.vertex_groups.keys())
    torch_groups = set(torch.vertex_groups.keys())

    leaked = sorted(arm_groups & set(OCCLUDING_BONES))
    if leaked:
        blendkit.fail(
            "Player_Arms is skinned to " + ", ".join(leaked) + ". The arms mesh is the one "
            "the owner sees; a vertex on the head or the pelvis rides that bone into the "
            "middle of their view (§05).")
    for hand in ("LeftHand", "RightHand"):
        if hand not in arm_groups:
            blendkit.fail(f"Player_Arms has no {hand} weights — §05 needs both hands.")
    if "Head" not in body_groups:
        blendkit.fail("Player_Body is not skinned to Head; the classifier finds the body by "
                      "the bone that occludes, so it would be drawn into its owner's face.")
    if torch_groups != {"RightHand"}:
        blendkit.fail(
            "Player_Torch is skinned to " + ", ".join(sorted(torch_groups)) + ", not to "
            "RightHand alone. A held item is recognised by being rigid on one hand; skinned "
            "to two it reads as part of the person and stops being switchable.")

    print("MESH_SPLIT " + " ".join(
        f"{m.name}(tris={sum(max(0, len(p.vertices) - 2) for p in m.data.polygons)},"
        f"slots={'|'.join(mat.name for mat in m.data.materials)},"
        f"weighted_bones={len(m.vertex_groups)})"
        for m in (body, arms, torch)))


def write_surface_manifest(reports: list, uv_per_metre: float, res: int,
                           generated_by: str = "tools/blender/gen_player_model.py") -> None:
    """Emits ``Player.textures.json`` — the contract ``PlayerMaterials`` builds from.

    **The role colours are in here too, and they were not before.** FBX cannot carry a PBR
    material — the format has a Lambert slot — so every one of this model's eight materials
    imports as flat white, which is exactly the defect ART.md §7.11 recorded for the props
    and nobody had recorded for the player. `PlayerMaterials` rebuilds all eight as URP Lit
    assets and remaps the importer onto them, and to do that it needs the five role RGBs as
    well as the three texture sets. Retyping them on the C# side would put §04's colour
    contract in two places and let it drift; emitting them here means the values a
    Principled BSDF was actually built with are the values Unity gets.
    """
    import gen_monster_model as gmm  # noqa: PLC0415
    path = os.path.join(gmm.TEXTURE_DIR, "Player.textures.json")
    with open(path, "w", encoding="utf-8") as handle:
        json.dump({
            "generated_by": generated_by,
            "resolution": res,
            "uv_units_per_metre": round(uv_per_metre, 6),
            "fbx": "Assets/Models/Characters/Player.fbx",
            "note": "The five Role_* slots are flat colour by design — Unity swaps slot 0 "
                    "per RoleId and §05 picks those values for separation in the dark. "
                    "Only the three neutral surfaces carry maps.",
            "roles": [
                {
                    "name": spec.name,
                    "base_color_linear": [round(c, 5) for c in spec.color],
                    "roughness": round(spec.roughness, 4),
                    "metallic": 0.0,
                    "luma": round(luma(spec.color), 4),
                }
                for spec in MATERIAL_SPECS if spec.name in ROLE_MATERIALS
            ],
            "materials": reports,
        }, handle, indent=2)
        handle.write("\n")


def surface_tiling(world_size: float, uv_per_metre: float) -> float:
    """How many times a map must repeat across the unwrap to land at its authored scale.

    A UV unit is ``1 / uv_per_metre`` metres of surface, and a map is painted to cover
    ``world_size`` metres, so one repeat has to span ``world_size × uv_per_metre`` UV
    units — the tiling is the reciprocal. On this model the unwrap runs 0.52 UV units
    per metre and the coverall is painted at 0.55 m, so it repeats 3.5×; left at 1 the
    weave stretches over 1.9 m of cloth and the fabric reads as bare plastic with a few
    smears on it.

    Unity derives the same number from ``Player.textures.json``. It is applied here as
    well so the .glb docs/ASSETS.md keeps for eyeballing shows what the game will.
    """
    return 1.0 / max(world_size * uv_per_metre, 1e-6)


def hook_surface_maps(reports: list, uv_per_metre: float) -> None:
    """Wires the written maps onto the three neutral materials, for the .glb preview."""
    import gen_monster_model as gmm  # noqa: PLC0415

    for report in reports:
        name = report["name"]
        mat = bpy.data.materials.get(name)
        if mat is None:
            continue
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue
        folder = os.path.join(gmm.TEXTURE_DIR, name)

        tiling = surface_tiling(report["world_size_metres"], uv_per_metre)
        coords = nt.nodes.new("ShaderNodeTexCoord")
        mapping = nt.nodes.new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (tiling, tiling, 1.0)
        nt.links.new(coords.outputs["UV"], mapping.inputs["Vector"])
        report["tiling"] = round(tiling, 4)

        def plug(suffix, socket, non_color):
            path = os.path.join(folder, f"{name}_{suffix}.png")
            if not os.path.exists(path):
                return
            node = nt.nodes.new("ShaderNodeTexImage")
            node.image = bpy.data.images.load(path, check_existing=True)
            if non_color:
                node.image.colorspace_settings.name = "Non-Color"
            nt.links.new(mapping.outputs["Vector"], node.inputs["Vector"])
            if socket == "Normal":
                nm = nt.nodes.new("ShaderNodeNormalMap")
                nt.links.new(node.outputs["Color"], nm.inputs["Color"])
                nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
            else:
                nt.links.new(node.outputs["Color"], bsdf.inputs[socket])

        plug("albedo", "Base Color", False)
        plug("rough", "Roughness", True)
        plug("normal", "Normal", True)


def verify_surface_separation(reports: list) -> None:
    """The three neutral surfaces must stay apart in value, in the order §05 implies."""
    by_name = {r["name"]: r["albedo_mean_linear"] for r in reports}
    order = (GEAR, COVERALL, SKIN)
    for dark, light in zip(order, order[1:]):
        if by_name[light] - by_name[dark] < 0.04:
            blendkit.fail(
                f"{dark} means {by_name[dark]:.3f} linear and {light} means "
                f"{by_name[light]:.3f} — under 0.04 apart they are one grey mass at the "
                "range §13 puts teammates at, and the model stops reading as a person "
                "wearing equipment.")


# ── Main ────────────────────────────────────────────────────────────────────


def main() -> None:
    """Delegates to ``gen_player_ai.py``, which is what writes the player now.

    **This file is a library.** Its pose solver, its nine clip authors, its gait tables,
    its procedural surfaces and its ``verify_*`` gates are all still the ones the shipping
    asset is built and checked with — ``gen_player_ai`` imports every one of them. What it
    no longer owns is the *geometry*: the hands are cut out of a sculpt, because a hand is
    made of the gaps between its fingers and a lofted tube has none, and §05 puts that
    assembly 35 cm from the camera for the whole match.

    ``main`` delegates rather than failing for one reason: ``tools/ci/run_blender_generators.sh``
    names **this** file in its generator list, and that list belongs to the CI area rather
    than to this one. Delegating keeps CI building the right asset with no edit outside
    this area, and keeps the promise that matters — there is exactly one Player.fbx and
    exactly one thing that writes it. Pointing that list at ``gen_player_ai`` directly is a
    one-line change and would make this shim unnecessary.

    ``-- --shell`` builds the old hull-and-tube model instead, into ``artifacts/`` and
    never into ``Assets/``. It is kept the way ``gen_monster_model``'s hull creature is
    kept: so the thing that was replaced can still be looked at, and so a reader who finds
    it cannot mistake it for the player the game loads.
    """
    tail = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--shell" not in tail:
        print("DELEGATE gen_player_model.py is a library; gen_player_ai.py writes "
              "Assets/Models/Characters/Player.fbx. Pass -- --shell for the old "
              "hull-and-tube model, which goes to artifacts/ and never to Assets/.")
        import gen_player_ai  # noqa: PLC0415 — deferred, or the two modules import in a cycle

        gen_player_ai.main()
        return

    build_shell_variant()


def build_shell_variant() -> None:
    """The pre-sculpt model, written to ``artifacts/`` for comparison only."""
    blendkit.reset_scene()
    blendkit.set_frame_range(1, 93)

    for spec in MATERIAL_SPECS:
        blendkit.make_material(spec)
    verify_materials()

    body, arms, torch = build_body()
    meshes = (body, arms, torch)
    for mesh in meshes:
        blendkit.triangulate(mesh)
        blendkit.shade_smooth(mesh, angle_degrees=38.0)
        blendkit.uv_smart_project(mesh)

    # The surfaces, before the rig: the unwrap has to exist to measure the UV density
    # the tiling is derived from, and nothing after this point touches UVs.
    import gen_monster_model as gmm  # noqa: PLC0415
    limits = gmm.pipeline_constants()

    # One tiling number in the manifest, three meshes. Smart-projecting each into its
    # own 0–1 square makes the small ones far denser than the large one, so the arms
    # are rescaled to the body's density: without this the same tiling paints a
    # coverall weave at 1:1 on the chest and at 1:4 on the sleeve, and the seam is a
    # visible change of fabric halfway down a limb.
    uv_per_metre = uv_units_per_metre(body)
    for mesh in (arms, torch):
        normalise_uv_density(mesh, uv_per_metre)
    surfaces = [write_surface(name, build, SURFACE_TARGETS[name], note, SURFACE_RES, limits)
                for name, build, note in SURFACES]
    write_surface_manifest(surfaces, uv_per_metre, SURFACE_RES)
    verify_surface_separation(surfaces)
    print(f"SKIN_UV uv_units_per_metre={uv_per_metre:.4f} "
          f"(1 tile covers {1.0 / uv_per_metre:.2f} m of surface at tiling 1)")
    for s in surfaces:
        print(f"SKIN_REPORT {s['name']:18s} albedo={s['albedo_mean_linear']:.4f}lin "
              f"gain={s['albedo_gain']:.2f} rough={s['roughness_mean']:.3f} "
              f"metal={s['metallic_mean']:.3f} relief={s['relief_mm']:5.2f}mm "
              f"ao={s['ao_mean']:.3f} tile={s['world_size_metres']:.2f}m "
              f"bytes={s['bytes']}")

    rig = blendkit.build_armature("Player_Rig", bone_specs())
    for name in MOUNTS:
        rig.data.bones[name].use_deform = False
    cache_rig(rig)
    for mesh in meshes:
        blendkit.bind_skin(mesh, rig, auto_weights=False)

    weighted = {b for mesh in meshes for b in mesh.vertex_groups.keys()}
    missing = [b for b in DEFORM_BONES if b not in weighted]
    if missing:
        blendkit.fail("Humanoid bones with no weighted geometry: " + ", ".join(missing))
    stray = [n for n in MOUNTS if n in weighted]
    if stray:
        blendkit.fail("socket bones must not deform the mesh: " + ", ".join(stray))

    verify_mesh_split(body, arms, torch)

    print(f"RIG_BONES count={len(rig.data.bones)} deform={len(DEFORM_BONES)} "
          f"sockets={len(MOUNTS)}")
    for bone in rig.data.bones:
        h, t = bone.head_local, bone.tail_local
        print(f"BONE {bone.name:17s} parent={(bone.parent.name if bone.parent else '-'):14s} "
              f"deform={int(bone.use_deform)} "
              f"head=({h.x:+.3f},{h.y:+.3f},{h.z:+.3f}) tail=({t.x:+.3f},{t.y:+.3f},{t.z:+.3f})")

    # Build → measure → stash. The measurement needs the action active on the rig, and
    # stash_action clears it.
    clips: list[Clip] = []
    stats: dict[str, dict] = {}
    metrics: dict[str, dict] = {}
    worst: dict[str, dict] = {}
    speeds: dict[str, float] = {}

    for build in CLIP_BUILDERS:
        clip = build(rig)
        action = blendkit.make_action(rig, clip.name, clip.poses, loop=clip.loop)
        clip.action = action
        stats[clip.name] = measure_action(action)
        metrics[clip.name] = pose_metrics(rig, clip.measure_frame)
        worst[clip.name] = first_person_worst(rig, clip)
        if clip.name == "Death":
            metrics["DeathSettle"] = pose_metrics(rig, 41)
        if clip.speed > 0.0:
            speeds[clip.name] = clip.speed
        clips.append(clip)

    preview_dir = os.environ.get("HORROR_PLAYER_PREVIEW_DIR")
    if preview_dir:
        # Rendered while the actions are still live, before they are stashed. Shot list is
        # per clip so a "Carry" render cannot pick up CarryHeavy's pose by name prefix.
        # The bind pose with no action attached — this is what Unity's Humanoid mapper sees,
        # so it is the one render that has to be a clean T-pose.
        rig.animation_data.action = None
        clear_pose(rig)
        for p in render_previews(rig, body, preview_dir, [
                ("00_bind_front", 0, (0.0, -4.6, 1.05), (0.0, 0.0, 1.00)),
                ("00_bind_side", 0, (4.0, -1.4, 1.10), (0.0, 0.0, 1.00))]):
            print(f"PREVIEW {p}")
        for clip in clips:
            rig.animation_data.action = clip.action
            f = clip.measure_frame
            for p in render_previews(rig, body, preview_dir, [
                    (f"{clip.name}_q", f, (2.3, -3.1, 1.35), (0.0, -0.05, 0.92)),
                    (f"{clip.name}_side", f, (3.4, -0.5, 1.05), (0.0, -0.15, 0.85)),
                    (f"{clip.name}_front", f, (0.0, -3.4, 1.20), (0.0, -0.10, 0.95))]):
                print(f"PREVIEW {p}")
            for p in render_first_person(rig, preview_dir, [(clip.name, f)]):
                print(f"PREVIEW {p}")
        rig.animation_data.action = None

    for clip in clips:
        blendkit.stash_action(rig, clip.action)

    # Each clip must stand alone in Unity, and NOTHING extrapolation means frame 0 is the
    # unposed T-pose — so the mesh cannot be exported mid-pose.
    for track in rig.animation_data.nla_tracks:
        for strip in track.strips:
            strip.extrapolation = "NOTHING"
            strip.blend_in = 0.0
            strip.blend_out = 0.0
    rig.animation_data.action = None
    clear_pose(rig)
    bpy.context.scene.frame_set(0)

    bind_span = max(abs(bone_point(rig, s + "Hand", (0.0, 0.0, 0.0)).x)
                    for s in ("Left", "Right"))
    if abs(bind_span - WRIST_X) > 0.002:
        blendkit.fail(f"the rig is not at rest before export: the wrist sits at "
                      f"{bind_span:.3f} m instead of {WRIST_X:.3f} m. Unity's Humanoid "
                      "auto-mapping reads the bind pose, and it has to be the T-pose.")
    print(f"BIND_POSE t_pose_confirmed wrist_x={bind_span:.3f}m "
          f"eye_z={bone_point(rig, 'HeadCameraAnchor', (0.0, 0.0, 0.0)).z:.3f}m")

    artifacts = os.path.abspath(os.path.join(
        os.path.dirname(os.path.abspath(__file__)), "..", "..", "artifacts", "player_variants"))
    os.makedirs(artifacts, exist_ok=True)
    fbx_path = os.path.join(artifacts, "Player_Shell.fbx")
    glb_path = os.path.join(artifacts, "Player_Shell.glb")
    blendkit.export_fbx(fbx_path, objects=[rig, body, arms, torch], with_animation=True)

    # Maps go on only now. Unity finds them under Assets/Textures/<material>/ by name,
    # the way the monster's do; hooking image nodes up before the FBX export instead
    # makes Blender's exporter copy every PNG into a .fbm sidecar next to the model,
    # which is a second committed copy of 15 MB of textures. The .glb that
    # docs/ASSETS.md keeps for eyeballing does want them, so it is exported after.
    hook_surface_maps(surfaces, uv_per_metre)
    write_surface_manifest(surfaces, uv_per_metre, SURFACE_RES)   # re-emit, now with tiling
    blendkit.export_gltf(glb_path, with_animation=True)

    report = blendkit.assert_asset(
        blendkit.describe(fbx_path),
        max_triangles=4000,
        expect_bones=len(bone_specs()),
        expect_actions=len(CLIP_BUILDERS),
        max_dimension=2.5,
    )
    blendkit.print_report(report)

    span, depth, height = report.size
    print(f"PLAYER_SHAPE height={height:.3f}m tpose_span={span:.3f}m depth={depth:.3f}m "
          f"eye_height={metrics['Idle']['eye_z']:.3f}m hip_height={HIP_Z:.3f}m "
          f"shoulder_breadth={2 * 0.172:.3f}m hip_breadth={2 * 0.168:.3f}m")
    verify_shape(report, metrics)

    for clip in clips:
        s = stats[clip.name]
        m = metrics[clip.name]
        extra = ""
        if clip.name in speeds:
            extra = (f" stance_travel={clip.stance_travel:.3f}m/{clip.stance_seconds:.3f}s"
                     f" speed={speeds[clip.name]:.2f}m/s cycle={clip.cycle_frames}f")
        if clip.hip_hi:
            extra += (f" hip_z={clip.hip_lo:.3f}-{clip.hip_hi:.3f}m"
                      f" bob={clip.hip_hi - clip.hip_lo:.3f}m"
                      f" sole_err={clip.sole_error * 1000:.2f}mm")
        print(f"ANIM_REPORT {clip.name:11s} frames={s['start']}-{s['end']} "
              f"({s['frames']:3d}f, {s['seconds']:.2f}s) loop={int(clip.loop)} "
              f"curves={s['curves']} keys={s['keys']:4d} "
              f"max_bone_motion={s['max_deg']:6.2f}deg "
              f"max_key_step={s['max_step_deg']:6.2f}deg "
              f"hipswing={s['per_bone'].get('LeftUpperLeg', 0.0):6.2f}deg{extra}  # {clip.note}")

    for clip in clips:
        m = metrics[clip.name]
        print(f"POSE_MEASURE {clip.name:11s} "
              f"flashlight_mount=({m['mount'].x:+.3f},{m['mount'].y:+.3f},{m['mount'].z:+.3f}) "
              f"fwd_of_hips={m['mount_fwd']:+.3f}m "
              f"beam=({m['beam'].x:+.2f},{m['beam'].y:+.2f},{m['beam'].z:+.2f}) "
              f"eye_z={m['eye_z']:.3f}m hand_gap={m['hand_gap']:.3f}m "
              f"hand_z={m['hand_z']:.3f}m lean_back={m['lean_back']:+.3f}m "
              f"objective_reach={m['objective_reach']:.3f}m")

    half_v, half_h = frame_half_angles()
    print(f"FIRST_PERSON_FRAME fov={fov_default_degrees():.0f}deg aspect={VIEW_ASPECT:.3f} "
          f"half_v={half_v:.1f}deg half_h={half_h:.1f}deg margin={FRAME_MARGIN_DEGREES:.0f}deg "
          f"min_distance={MIN_HAND_DISTANCE:.2f}m")
    for clip in clips:
        w = worst[clip.name]
        print("FP_WORST_KEY {:11s} {}".format(clip.name, " ".join(
            "{}=({:+.0f}deg below,{:.0f}deg off,{:.2f}m,{})".format(
                label, v[0], v[1], v[2], "IN " if in_frame(v, half_v, half_h) else "OUT")
            for label, v in (("left_hand", w["view_left"]),
                             ("right_hand", w["view_right"]),
                             ("torch", w["view_mount"]),
                             ("best_hand", w["any_hand"])))))

    verify_motion(clips, stats, metrics, speeds, worst)

    takes = fbx_objects(fbx_path, b"AnimStack")
    clean = [t for t in takes if "|" not in t]
    print(f"FBX_TAKES count={len(takes)} unprefixed={len(clean)} names={','.join(sorted(clean))}")
    dropped = [c.name for c in clips if c.name not in takes]
    if dropped:
        blendkit.fail("actions missing from the FBX: " + ", ".join(dropped) +
                      " — they were not stashed into NLA tracks.")

    # blendkit.export_fbx passes bake_anim_use_nla_strips=True AND
    # bake_anim_use_all_actions=True, and Blender's exporter treats those as two
    # independent passes, so every stashed action is written twice — once with a clean
    # name and once as "<object>|<action>". Harmless (identical curves) but Unity lists 18
    # clips. Surfaced here rather than discovered in the importer. The fix belongs in
    # blendkit.export_fbx (bake_anim_use_all_actions=False; stash_action already makes the
    # NLA pass cover every action) and this generator does not own that file.
    duplicates = [t for t in takes if "|" in t]
    if duplicates:
        print(f"FBX_NOTE {len(duplicates)} duplicate takes from the all-actions pass "
              f"({','.join(sorted(duplicates))}). Use the {len(clean)} unprefixed clips.")

    fbx_models = fbx_objects(fbx_path, b"Model")
    for socket in MOUNTS:
        if socket not in fbx_models:
            blendkit.fail(f"{socket} is not in the exported hierarchy — §05/§13 need it.")
    for mesh in meshes:
        if mesh.name not in fbx_models:
            blendkit.fail(
                f"{mesh.name} is not in the exported hierarchy. The three meshes are how "
                "§05's 「자기 몸은 안 보이므로 손만 있으면 된다」 is delivered — one mesh "
                "means hiding the chest hides the hands with it.")
    print(f"FBX_HIERARCHY models={len(fbx_models)} "
          f"sockets_present={','.join(s for s in MOUNTS if s in fbx_models)}")
    humanoid = [b.name for b in rig.data.bones if b.name not in MOUNTS]
    absent = [b for b in humanoid if b not in fbx_models]
    if absent:
        blendkit.fail("bones missing from the FBX: " + ", ".join(absent))

    fbx_mats = fbx_objects(fbx_path, b"Material")
    missing_mats = [m for m in SLOTS if m not in fbx_mats]
    if missing_mats:
        blendkit.fail("materials missing from the FBX: " + ", ".join(missing_mats))
    print(f"FBX_MATERIALS count={len(fbx_mats)} names={','.join(sorted(set(fbx_mats)))}")
    print("ROLE_SLOT_CONTRACT slot0=Role_Listener carries every role-tinted face; "
          "Unity sets renderer.materials[0] per RoleId. slots1-4 = concealed swatches.")

    glb_anims = glb_animations(glb_path)
    print(f"GLB_ANIMATIONS count={len(glb_anims)} " + " ".join(
        f"{a['name']}={a['seconds']:.2f}s/{a['channels']}ch" for a in glb_anims))
    glb_names = {a["name"] for a in glb_anims}
    glb_dropped = [c.name for c in clips if c.name not in glb_names]
    if glb_dropped:
        blendkit.fail("actions missing from the GLB: " + ", ".join(glb_dropped))

    print(f"FILES {fbx_path} {glb_path}")
    print(f"BYTES fbx={os.path.getsize(fbx_path)} glb={os.path.getsize(glb_path)}")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_player_model.py raised:\n" + traceback.format_exc())
