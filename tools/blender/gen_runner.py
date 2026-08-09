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
똑같이 생긴다.」** Twenty identical strangers in a dark maze — **「누가 사람이고 누가
아닌지도 한순간 헷갈린다」** — which is also the trap §10's 그늘 is built to spring.

The figure is an anonymous WORKER: utility jacket with a raised collar and a hood,
work trousers tucked into boots, gloved mitten hands, a small headlamp housing on the
brow (unlit geometry — the game's flashlight component is the only light), and **no
readable face**: the space under the hood's brim is a near-black material so the beam
finds a hollow where a face should be. The anonymity is the design, not a budget cut.

WHY IT IS DARK FABRIC AND NOT WHITE PLASTER — the porcelain problem
-------------------------------------------------------------------
The previous revision shipped one near-white material (0.86, 0.87, 0.89) on the
argument that a pale body survives a moving flashlight. ``Shots/prodbase_03m.png`` is
what that argues into: under the beam the whole figure clips towards white, every
interior contour is gone, and the body reads as a GLOSSY PORCELAIN MANNEQUIN — a
light source in the corridor, the exact misread §10's 그늘 already exploits. A
silhouette does not need to be bright to read; it needs to be *shaped*, and a
near-white albedo under a 12 m beam destroys shape faster than darkness does.

So the clothes are matte mid-dark fabric (albedo 0.13 down to 0.04, roughness 0.85+,
metallic 0): under the beam the jacket gives back enough to read folds and outline,
and it can never bloom. The one bright thing on the body is deliberate and small —
the ``Runner_Accent`` band (armband + hood strap), which is the slot a per-runner
tint targets to keep twenty identical strangers tellable-apart by colour accent.

THE MONSTER TEST. ``Shots/prodbase_Acquire_3m.png``: the monster is thin, ribbed,
hunched, arms to its knees. This figure is its negation in outline — bulky-jacketed,
upright, arms ending at the hip — so a beam's edge at 15 m can tell runner from
monster before it can tell anything else. ``report_breadth`` prints the shoulder
band; the arm/spine ratio the gun mount fixes keeps the arms short.

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

THE FOURTH BODY — a harvested human under the same jacket
---------------------------------------------------------
The primitive body solved the weld and never solved the person: blob head, mitten
hands, cone legs — a plush toy in every close render, and task #81's "better hands"
had nothing to build on. The ANATOMY is now the CC0 Blender Foundation human base
mesh (``HUMAN_SOURCE``, vendored like the Mixamo clips, measured by the vendor
script), and the primitive machinery above keeps the job it was actually good at:
CLOTHING. The trunk loft is the jacket around the measured torso, the shafts are
sleeves and boots around the measured limbs, and the voxel union swallows the body
wherever cloth covers it, so the weld argument is unchanged — placements, overlaps,
one level set. What the body adds is what placements never could: a real neck and
skull under the hood, real knees and calves under the trousers, and real gloved
HANDS — harvested at the wrist, kept at their own quad topology outside the remesh
(a 9 mm voxel would weld the fingers straight back into a mitten), and tucked into
the cuffs as separate rigid shells. Under the flashlight the figure still reads as
the same hooded worker; it just stops reading as a toy the moment it moves or the
camera gets close. ``verify_body_covered`` holds the two halves together: the
garment must dress the body, measured, every run.

A SKELETON, AND WHAT RIGGING A STATIC PROP COST THE STATIC PROP
---------------------------------------------------------------
This figure shipped for a while as geometry with **bones=0 actions=0**, so
``PlayerAnimatorDriver`` — which reads the motor's ground speed and fires a Footfall
event off the phase of whatever clip is playing — was handed a mesh with no clips at all,
and twenty racers glided through the maze like furniture. It now carries a 17-bone rig and
the eight clips ``CLIP_NAMES`` lists. The count is argued where the bones are defined; the
short version is that it started at 13 on a reading of the MESH — a bone earns its place
only if the surface it moves has somewhere to bend — and four more were owed the moment
the clips became mocap, because a recorded skeleton's motion is not attenuated by a bone
you do not have, it is deleted. An elbow each side, a neck, and a second torso bone.

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
**1. The shell census.** ``verify_shells`` walks the mesh with bmesh and counts
connected components: exactly THREE — the welded suit and the two harvested hands,
each watertight — and ``verify_hand_shells`` measures that the hands are tucked,
clear and never coplanar with the suit. Welded parts must **overlap**, not touch. The
failure the census is written against was a 6 cm gap between the top of the torso and
the bottom of the neck: both primitives were correct, both were where the table said,
and the weld had nothing to weld — the head and neck came out of the remesh as a
separate closed shell floating above the shoulders, which in Unity is a head that does
not move with the body. Nothing in a mesh export complains about that. A component
count does, and ``verify_parts_interpenetrate`` catches it 20 s earlier and names the
offending part; ``verify_body_covered`` does the same for skin escaping the garment.

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

TWO CONTRACTS THE UNITY PROJECT PINS FROM OUTSIDE THIS FILE
------------------------------------------------------------
* ``Runner.fbx.meta`` defines every clip as an explicit frame range
  (Walk 0–16, Run 0–16, CrouchWalk 0–20, Idle/GunIdle 0–92, Crouch 0–80, GunWalk
  0–16, Death 0–47). A .meta is never edited by a generator, so a regenerated take
  that lands on a different cycle length is silently TRUNCATED by the importer —
  a walk missing the last quarter of its stride, looping with a pop. The cadence
  search is free to run, but ``EXPECTED_CYCLE_FRAMES`` asserts the winners; the
  pendulum maths says the winners only change if the leg leaves the 0.47–0.67 m
  band, which is why ``LEG_Z_TOP``/``ANKLE_Z`` must stay near where they are.
* ``RunnerGun.GunMountArmsPerSpine = 0.9904`` hangs the gun off the arm as an
  arm/spine ratio. ``HAND_Z`` is SOLVED from that constant (see the placement
  table) rather than eyeballed, and ``verify_gun_mount`` re-reads the C# to prove
  the two still agree.
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
from mathutils.bvhtree import BVHTree  # noqa: E402

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

# ── The five materials, and why five ────────────────────────────────────────
#
# The FBX's embedded materials ARE the in-game materials: Runner.fbx.meta imports them
# in place (materialImportMode 2, no externalObjects remap), so the numbers below are
# the numbers the beam lights. Nothing in the project references the old
# ``Runner_Plaster`` name — the slot vocabulary is owned here, and it is deliberately
# small: five slots is five SRP-batcher buckets across twenty runners, not eighty.
#
# Every roughness is 0.85+ except the accent, and nothing is metallic. A specular
# hotspot on a body tracks the viewer's own flashlight and reads as a light source in
# the corridor — the misread §10's 그늘 already exploits. Matte fabric cannot do it.
#
# The VALUES are separated so the beam reads them as different garments even in a
# grazing hit: jacket 0.13, trousers 0.085, gear 0.045, void 0.012. That ordering —
# bright core, darker legs, darkest extremities, hole for a face — is what makes the
# outline parse as a clothed person instead of one blob.

MAT_JACKET = MaterialSpec("Runner_Jacket", (0.130, 0.135, 0.142), roughness=0.92)
"""Utility jacket and hood. Cool mid-dark grey — desaturated workwear, faintly blue so
it separates from the warm concrete of the map kit under the same beam."""

MAT_TROUSERS = MaterialSpec("Runner_Trousers", (0.085, 0.085, 0.080), roughness=0.92)
"""Work trousers, tucked into the boots. A step darker than the jacket so the waist
reads even when the outline is soft."""

MAT_GEAR = MaterialSpec("Runner_Gear", (0.045, 0.043, 0.040), roughness=0.86)
"""Boots, gloves and the headlamp housing — the worn near-black leather/rubber/plastic
kit. One slot for all three: they are the same value at 12 m and nobody retints boots."""

MAT_VOID = MaterialSpec("Runner_Void", (0.012, 0.012, 0.014), roughness=0.95)
"""The face that is not there. The recess under the hood's brim wears this so the beam
finds a hollow — the anonymity read, done with albedo instead of geometry."""

MAT_ACCENT = MaterialSpec("Runner_Accent", (0.50, 0.33, 0.09), roughness=0.65)
"""THE TINTABLE SLOT. Armband on the left sleeve, plus the REAR arc only of the hood
strap — the strap's front arc is painted gear since the owl-eye fix (see the paint
rules): two warm points at eye height under a 3 m beam read as a glowing face, and
the face must stay a void. A future per-runner tint colours THIS material and nothing
else — twenty identical strangers stay tellable-apart by one accent, which is the
production pattern (an armband / helmet stripe). Default is safety amber."""

MATERIAL_SPECS = (MAT_JACKET, MAT_TROUSERS, MAT_GEAR, MAT_VOID, MAT_ACCENT)
"""Slot order in the mesh. Index 0 is the default (jacket); ``assign_materials``
classifies every polygon into one of these by region."""

# ── The weld pipeline ───────────────────────────────────────────────────────

VOXEL_SIZE = 0.009
"""9 mm, down from the mannequin's 12. The number is doing three jobs now. It has to be
smaller than the thinnest overlap in the table or the union drops a join. It has to be
big enough that a 1.7 m figure stays a few tens of thousands of faces. And it has to
RESOLVE THE CLOTHING: the hem ledge is a 50 mm step, the boot cuff a 34 mm ridge, the
cap brim 26 mm and the headlamp housing a 64×50×52 mm box — features a 12 mm grid
records as two voxels of noise and a 9 mm grid records as shape."""

SMOOTH_FACTOR = 1.0
SMOOTH_ITERATIONS = 3
"""Three passes, not the mannequin's eight. The remesh output is a voxel staircase and
smoothing is what turns the union back into a body — but every pass also erodes the
garment features the table now spends its placements on. Three passes kills the 9 mm
staircase and leaves the collar, hem, cuffs and brim standing; eight passes returns the
Michelin man. Topology-preserving, so it cannot break the single shell."""

DECIMATE_RATIO = 0.115
"""Keep 11.5%, down from the mitten era's 14. The 9 mm remesh spends ~80k triangles
uniformly; after three smoothing passes most describe curves their neighbours already
describe. Collapse mode aims the ~9k that survive at the silhouette and the garment
creases — and since ``sculpt_folds`` runs before this, the creases are real curvature
and the collapse metric spends its keeps on them by itself. The ~2.5 points given up
against the old ratio are the HANDS' budget: the two harvested shells join after this
pass at their own ~2.8k cage triangles (fingers are exactly what a collapse eats
first), and ``assert_asset`` still holds the whole figure's ceiling at 12.5k."""

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
#
# −Y is not a preference, it is the toolchain's one axis convention, stated in the same
# words in ``gen_props``, ``gen_dressing``, ``gen_monster_model`` and ``gen_player_model``:
# *export_fbx's axis_forward='-Z' turns into +Z forward in Unity*.
#
# THE BODY IS HARVESTED NOW, NOT LOFTED — the fourth body under this jacket, and the
# first with anatomy. The synthetic union (ellipsoid head, blob mittens, cone legs)
# photographed as a plush toy in the close renders and could never give task #81 its
# hands. The body is the CC0 Blender Foundation human base mesh (``HUMAN_SOURCE``,
# vendored like the Mixamo clips), measured, re-posed and welded INTO the same
# pipeline: the trunk loft below is no longer the torso — it is the JACKET, a garment
# whose rings are authored as the measured human profile plus a cloth clearance, and
# the voxel union swallows the body wherever cloth covers it. What the beam sees is
# still the garment; what moves under it is a person. Every number below is still a
# placement in the canonical 1.700 m body frame (the vendored mesh's own frame); the
# fit rescales the assembled figure to exactly 1.750 afterwards, as it always did.
#
# The placements are named rather than inlined because ``bone_specs`` is derived from
# them. A rig table that repeats the mesh table by hand is two tables that drift, and a
# hip bone 3 cm off the hip is a thigh that swings the belly with it.

# THE TRUNK LOFT IS THE JACKET. Ring recipe: take the measured torso profile
# (tools/blender/source/human/ measurements, printed by the vendor script) and add
# cloth: ~20-30 mm at the chest and hem, ~15 mm at the pull-in ledge, and a straight
# back line that bridges the lumbar hollow the way hanging fabric actually does
# (torso back: seat +0.109 → lumbar +0.070 → chest +0.112; the jacket back runs
# +0.118..+0.138 and never follows the dip). ``verify_body_covered`` proves the
# body stays inside this garment rather than trusting the arithmetic.
TRUNK_RINGS = (
    #  z      rx     ry     y-centre
    (0.740, 0.206, 0.136, -0.008),  # hem's bottom edge, over the thigh tops — the
                                    # jacket's widest line lives HERE, below the cuff
    (0.780, 0.206, 0.144, -0.002),  # hem (kept off the cuff: at 0.214 the flare sat
                                    # 2.5 mm from the cuff's inner face and welded)
    (0.815, 0.196, 0.146, +0.000),  # (glute corners at x 0.13 ride inside +0.115)
    (0.850, 0.196, 0.152, +0.002),  # pull-in: the ledge that says "jacket ends here"
    (0.980, 0.176, 0.134, -0.020),  # waist (torso front −0.137 inside −0.154)
    (1.120, 0.180, 0.136, -0.018),
    (1.260, 0.187, 0.150, -0.012),  # chest (torso rx 0.165 / front −0.145 / back +0.112)
    (1.350, 0.190, 0.135, +0.000),  # an ellipse pinches at its sides, so the blade
                                    # CORNERS are carried by the BackYoke part, not here
    (1.415, 0.208, 0.120, +0.002),  # deltoid yoke crest — the shoulder's WIDTH lives
                                    # here now, not in the ball (see SHOULDER_Z)
    (1.446, 0.150, 0.096, +0.002),  # trapezius slope
    (1.480, 0.084, 0.090, -0.012),  # neck root, up into the collar (throat −0.099 covered)
)
# THE TRAPEZIUS LINE SURVIVES THE BODY SWAP UNCHANGED IN INTENT: the yoke ring at
# 1.415 is the widest line of the upper body and the outline descends monotonically
# neck → trap → deltoid crest → sleeve, while the shoulder ball sits where its crown
# continues that slope instead of breaking it (ball crown 1.422 vs crest 1.415). The
# human's own deltoids are cut with the arms (X_CUT 0.165) and the stubs ride inside
# the yoke, so the shoulder line is the garment's — bulky by design, DESCENT-PIVOT §5.

HEM_BOTTOM_Z = TRUNK_RINGS[0][0]
"""Where the jacket ends and the trousers show. The material paint and the crotch
ceiling both key off it, so it is named once. 0.740 also settles the inner-thigh
question the harvested body raised: the measured thigh gap is 15 mm — under the 18 mm
weld threshold — only between z 0.72 and 0.78, and that whole band is inside the hem,
where the union of hem and thighs is the pelvis and welding is the point. Below the
hem the measured gap is ≥ 29 mm and the legs stay two legs (``verify_limbs_hang_free``
still proves it on the welded mesh)."""

COLLAR_C, COLLAR_R = (0.0, 0.006, 1.448), (0.126, 0.124, 0.050)
"""Raised jacket collar: a flat disc proud of the trunk's top in depth, wrapping the
human neck and throat (throat front −0.099; collar front reaches −0.118), so the head
sits IN the jacket instead of on a bare stalk."""

HOOD_C, HOOD_R = (0.0, -0.018, 1.600), (0.127, 0.140, 0.125)
HOODSKIRT_C, HOODSKIRT_R = (0.0, 0.048, 1.468), (0.122, 0.130, 0.077)
"""The hood, sized around the HARVESTED head (crown 1.700, half-width 0.092, nose
−0.165 at z 1.554). The shell reaches 25 mm above the crown and 28 mm past the skull
sides; its front wall at nose height sits at −0.147, so of the whole face only the
nose/mouth/chin centre emerges 8–19 mm through the opening while the brow and cheeks
stay inside — a face-shaped sliver in a hole, which ``Runner_Void`` then paints to
nothing. The skirt carries the crown bulge down onto the shoulders and collar."""

BRIM_C, BRIM_R = (0.0, -0.100, 1.638), (0.092, 0.076, 0.020)
"""Cap brim / hood peak: a thin ledge reaching −0.176 — 11 mm past the nose tip — so
the void window sits in its shadow under a high beam hit. The one crisp horizontal in
the head's silhouette."""

LAMP_C, LAMP_HALF = (0.030, -0.128, 1.664), (0.026, 0.030, 0.020)
"""Headlamp housing: a 52×60×40 mm box ABOVE the brim line now, welded ~16 mm into the
hood's brow, and OFF-CENTRE (+30 mm, the figure's left brow). Both moves are the
owl-eye fix (prodship_03m.png): the old housing sat centred UNDER the brim at face
height, so the beam found warm accent band either side of a dark box and the runner
read as two glowing eyes at 3 m. A single asymmetric lens above the brim reads as
equipment; two symmetric bright points at eye height read as a face, and this figure
must not have one. UNLIT geometry — the real light is the game's flashlight component.
Wears ``Runner_Gear``."""

# Shoulders and sleeves. The visible arm is still the GARMENT — a jacket sleeve
# hanging from the yoke — because the human arm hangs too close to its own ribs for a
# 9 mm voxel to keep the armpit open, and because a padded sleeve at ARM_X 0.276 is
# the bulk that makes THE MONSTER TEST work (module docstring). The harvested arms are
# cut at X_CUT and only their HANDS are kept, as separate native-topology shells that
# tuck into the cuffs (``harvest_hands``): the sleeve is cloth, the hand is anatomy,
# and the seam between them is the glove cuff, exactly like the real garment.
#
# The ball's crown (1.340 + 0.082 = 1.422) rides the trapezius slope the yoke ring
# draws — a ball cresting above that line is a pauldron (see TRUNK_RINGS note). The
# ball's job is the sleeve-to-yoke weld and the arm's pivot; it also swallows the
# human's capped deltoid stub so the cut never surfaces.
SHOULDER_X, SHOULDER_Z, SHOULDER_R = 0.212, 1.340, 0.082
ARM_X, ARM_Y = 0.276, -0.022
ARM_Z_BOTTOM = 0.835
ARM_R_TOP, ARM_R_BOTTOM = 0.066, 0.055

CUFF_C, CUFF_R = (0.280, -0.046, 0.812), (0.062, 0.072, 0.058)
"""Sleeve cuff: a ring bump where the glove meets the sleeve — it contains the arm
shaft's bottom ball and is the pocket the harvested hand's capped wrist stub tucks
into (the tuck is measured by ``verify_hand_shells``, not assumed). Rides at the
wrist the gun-mount solve now puts at ~0.82 (HAND_Z's note), and 20 mm further OUT
than the sleeve line: its inner face is what the hem flare welded to in round one's
first build (LIMB_BRIDGE traced the route), so the cuff owns a measured 30+ mm slot
against the hem now."""

GUN_MOUNT_RATIO = 0.9904
"""Arms per TORSO: the arm's length as a multiple of ``SPINE_JOIN_Z``→``NECK_BASE_Z``.
This is what solves ``HAND_Z``, and it is why the arm length is solved rather than
sculpted — the hand is PLACED from the proportion instead of drifting toward it.

**It is no longer the same number as ``RunnerGun.GunMountArmsPerSpine``, and that is the
one thing to read carefully here.** It was, while the arm was a single bone and the torso
a single bone: one length over another, measured the same way on both sides of the
language boundary. The 17-bone rig broke that coincidence twice over — the gun hangs off
``RightLowerArm`` (half an arm) and the C# measures its unit down a three-bone chain
(``Spine``+``Chest``+``Neck``, which runs to ``HEAD_BASE_Z``, not to ``NECK_BASE_Z``). So
the C# constant is DERIVED, by ``report_arm``, and ``verify_gun_mount`` re-reads the C# to
prove the two have not drifted. Do not copy this 0.9904 into RunnerGun.cs."""

SPINE_JOIN_Z = 0.80
"""Where Hips ends and Spine begins. 20 mm above the hip joint so the Hips bone rests
pointing UP — the pose solver aims bones at absolute world directions, and a pelvis bone
resting downward would flip the body the first time ``torso()`` aimed it."""

CHEST_JOIN_Z = 1.191
"""Where the lumbar ``Spine`` ends and the thoracic ``Chest`` begins — **placed by the
mocap, not by a vertebra**, and that is the honest description of what this joint is for.

The eight source clips carry a THREE-segment spine (mixamo ``Spine``/``Spine1``/``Spine2``)
and this rig answers with two. Anchor the source's torso onto this one by its ends — mixamo
``Hips``.head 104.27 ≡ ``SPINE_JOIN_Z``, mixamo ``Neck``.head 150.31 ≡ ``NECK_BASE_Z`` — and
one source unit is 13.34 mm here. Mixamo ``Spine2``.head sits at 133.57, i.e. **1.191**. So a
``Chest`` starting here spans exactly what the source's ``Spine2`` spans, and the ``Spine``
below it spans exactly what the source's ``Spine`` + ``Spine1`` span. The check is the
midpoints, which is what ``MIXAMO_MAP`` actually matches on: this ``Spine``'s midpoint maps
back to source 118.96 against mixamo ``Spine``'s own 119.4, and this ``Chest``'s to 141.94
against mixamo ``Spine2``'s 141.94 — the same number to two decimals.

It reads as a plausible vertebra afterwards (0.70 of height ≈ T8, the mid-thoracic) but that
is a sanity check and not the derivation. The derivation is that the one thing this joint
has to do is carry ``Spine2``'s share of a lean that used to be telescoped onto a single
bone — see ``MIXAMO_MAP``, and the 45° plank in the round-3 renders that is why."""

NECK_BASE_Z = 1.414
"""Where Chest ends and Neck begins — the base of the harvested neck (C7 sits at
~1.42 on the canonical body), just under the collar. Still the top of the TORSO, and
therefore still the landmark the gun mount's length is measured to: spine 0.614 ×
0.9904 = an arm of 0.608, which lands the hand — and, split across an elbow now, the
``LowerArm``'s tail and therefore the gun — in the palm of a human-proportioned
hanging hand instead of at a mitten blob."""

HEAD_BASE_Z = 1.540
"""Where Neck ends and Head begins — MEASURED off the harvested body, as the level where
the neck column flares into the jaw. Slicing the canonical mesh at 5 mm and taking each
slice's half-width (arms excluded) gives a dead-straight neck column at 60–61 mm from
z = 1.455 to z = 1.535, and then 83.3 mm at 1.540: a 37 % step in one slice, which is the
mandible. Cross-checked against the source rig through ``CHEST_JOIN_Z``'s own anchor —
mixamo ``Head``.head 159.93 maps to 1.542 here — so the mesh and the mocap put the skull
base 2 mm apart and this bone is where both of them say it is."""

HAND_X, HAND_Y = 0.272, -0.050
HAND_Z = SHOULDER_Z - math.sqrt(
    (GUN_MOUNT_RATIO * (NECK_BASE_Z - SPINE_JOIN_Z)) ** 2 - (HAND_X - SHOULDER_X) ** 2)
"""SOLVED, not authored: the z that makes arm/spine measure exactly
``GUN_MOUNT_RATIO``. Lands at ~0.735 — the palm centre of a relaxed hanging arm on the
harvested body (wrist ~0.815, fingertips ~0.64, which is the anthropometric 0.38 of
height), still far above the monster's knee-hanging hands. ``harvest_hands`` places
the real hand's palm anchor AT this point, so the mount contract and the visible palm
are the same location by construction."""

HAND_C = (HAND_X, HAND_Y, HAND_Z)
"""Where the harvested hand's palm anchor goes — no longer a mitten ellipsoid's centre,
but the same solved point it always was."""

ARMBAND_C, ARMBAND_R = (0.279, -0.022, 0.960), (0.077, 0.077, 0.045)
"""LEFT sleeve only (not mirrored): a ring ~16 mm proud of the sleeve. Carries
``Runner_Accent`` — the tint band that tells twenty runners apart.

**Moved DOWN onto the forearm when the arm grew an elbow, and the render is the reason.**
It used to sit at 1.100, which is 43 mm above ``ELBOW_Z`` — harmless on a one-bone arm
where nothing between shoulder and cuff ever moved relative to anything else. With a joint
there, a 90 mm band spanning 1.055–1.145 has its lower half inside the bend, and the first
17-bone Run render shows it as a torn yellow smear folded round the crease instead of a
ring. An identifying mark that deforms differently every frame identifies nothing.

Down rather than up, and that was measured too. Mid-upper-arm (1.210) is the other rigid
segment and it FAILS ``verify_limbs_hang_free``: the trunk is widening toward the deltoid
yoke by then, the band's inner face comes within 17 mm of it, and the 9 mm voxel bridges
the armpit slot — 2467 vertices of the far side became reachable from the hand. At 0.960
the band spans 0.915–1.005: 52 mm clear of the elbow, 45 mm above the cuff, on the rigid
forearm, with the same ~23 mm of armpit slot the original position had."""

LEG_PART_FRACTION = 0.33
"""How far down from the hip the two thighs are still allowed to be one mass.

A third, which is to say the pelvis. Below that a leg has to be a leg: a crotch weld is
what a scissoring stride tears into a fringe. Measured off the mesh by
``verify_limbs_hang_free``, not assumed. On the harvested body the measured inner-thigh
gap is ≥ 29 mm everywhere below the hem and only closes inside it (HEM_BOTTOM_Z note),
so the profile still comes back open where it must."""

HIP_X, HIP_Y = 0.092, -0.005
"""The femoral head, off the measured pelvis (seat rx 0.191 → joints at ~±0.09). The
old figure's leg was a vertical column; the harvested leg splays naturally from hip
0.092 to ankle 0.172, and the bones now follow the bone line instead of a plumb line."""

KNEE_X, KNEE_Y = 0.142, +0.002
"""The measured knee centre (LEGYY x-centre 0.137–0.141 at z 0.46–0.52). Its y sits
~22 mm FORWARD of the hip–ankle line, which is the anatomical bend bias — the old
KNEE_BIAS_Y −0.006 nudge is retired because the body now carries a real one."""

LEG_X = 0.172
LEG_Z_TOP = 0.780
ANKLE_Z, ANKLE_Y = 0.115, 0.056
"""Hip joint at the measured groin-crease level (0.78 — this base mesh is deliberately
short-legged: crotch 0.697), ankle at the measured malleolus (0.115, and 56 mm BEHIND
the body line, which is where a real ankle is — the foot reaches forward from it).
hip−ankle = 0.665 pre-fit: still inside the 0.47–0.67 pendulum band, so the cadence
winners the meta pins survive even on the ``--procedural`` fallback. The trouser
SHAFTS are gone — the trouser is the harvested leg itself, inflated ~5 mm by
``_prep_suit_body`` so it reads as cloth over a leg rather than the leg."""

BOOT_TOP_C, BOOT_TOP_R = (0.172, 0.040, 0.372), (0.086, 0.094, 0.032)
BOOT_Z_TOP, BOOT_Z_BOTTOM = 0.360, 0.125
BOOT_R_TOP, BOOT_R_BOTTOM = 0.078, 0.068
"""The boot: a shaft around the harvested shin/calf (calf back +0.111, covered to
+0.120), topped with a flat cuff ring — the tucked-in read is a ridge where trouser
meets boot, kept by the light smoothing. ``Runner_Gear`` colours everything below the
cuff. The human foot welds INSIDE this boot: the boot is what stands on the floor."""

FOOT_C, FOOT_R = (0.180, -0.024, 0.054), (0.080, 0.158, 0.082)
"""Toe forward (−Y — see the axis note above), longer than wide, chunky like a work
boot, and 8 mm DEEPER than the harvested foot's sole so the boot's rubber — never the
skin — is the lowest surface (``verify_body_covered`` proves the containment). Inner
faces sit ~200 mm apart, so the remesh cannot weld the pads into the heel-to-toe sail
the mannequin once grew (``verify_limbs_hang_free``)."""

X_CUT = 0.165
"""Where the harvested arms are severed from the torso (a topology walk from each
fingertip, bounded at |x| = 0.165, then the shoulder holes are capped). Everything
distal feeds ``harvest_hands``; the capped stub stays inside the shoulder ball."""


# ── The folds ───────────────────────────────────────────────────────────────
#
# WHY THE FOLDS ARE A DISPLACEMENT PASS AND NOT PLACEMENTS. The loft cured the quilted
# read by having no intersections, and it overcured: a crease-free surface photographs
# as a plush toy under the beam (runner-rebuild round 5). Folds cannot go back into
# the placement table — a 10 mm crease authored as primitives is exactly the two-voxel
# noise VOXEL_SIZE's note warns about, and the smoothing that kills the remesh
# staircase would kill it too. So the creases are written onto the WELDED, SMOOTHED
# mesh, after the staircase is gone and before the decimation spends the budget: every
# vertex moves along its own normal by a hand-authored field below. The decimator then
# keeps its triangles where the curvature is — which is now the folds — and the
# autosmooth angle in ``weld`` turns each surviving ridge into a hard shading line the
# beam can find at 3 m.
#
# The field is authored per garment, in the placement table's coordinates, and every
# irregularity comes off ``_fold_hash`` — a pure function, so the figure is the same
# figure every run. Depths are 5–14 mm: Lethal-Company-grade compression folds, not
# cloth sim. Nothing below the boot cuff, nothing on the head, nothing on the soles —
# the height fit, the sole measurement and the cadence contract never see this pass.

FOLD_SEED = 82.0
"""The task number, and nothing else. Changing it re-deals every jitter at once."""

FOLD_CLAMP = (-0.016, 0.010)
"""Metres. No valley deeper than 16 mm, no ridge prouder than 10 mm — a field bug
becomes a dented jacket, never a spike through the armpit slot or the thigh gap."""

SLEEVE_FOLD_AXIS = -2.5
"""Radians, in the sleeve's own polar frame (0 = outboard, ±π = inboard, negative =
forward): the inner-front of the elbow, where a hanging arm's sleeve compresses. The
crease fan is deepest there and shallows around the ring."""

ELBOW_Z = 1.0571
"""Metres, placement-table frame: the elbow, and since the rig grew a ``LowerArm`` it is
both the sleeve's crease line AND the joint the bone bends at. One elbow in this file.

**Measured, off the OLECRANON.** The obvious probe — walk the harvested arm from the
fingertip, slab it along its own axis and look for a narrow ring — does not work on this
body: the mean cross-section radius climbs monotonically from the wrist (19.7 mm) to the
deltoid (50 mm) with no elbow notch at all, so a radius minimum would have picked the band
edge. What the mesh does carry is the point of the elbow. Measuring each slab's most
POSTERIOR extent relative to the arm axis gives a clean local maximum at 54.7 mm, 0.369 m
from the fingertip, at **z = 1.0571** — the olecranon, the one elbow landmark that is a
bump rather than a waist.

Three independent numbers agree on it and none of them was used to derive it: the previous
constant here (1.054, authored as ~55 % of the shoulder→wrist drop) is 3 mm away; the
fraction it implies along this rig's shoulder→hand line, 0.4676, sits against the mixamo
source rig's own upper-arm : forearm + half-hand proportion of 0.4618; and the sleeve fold
cluster that has always been drawn at this height does not move."""

ELBOW_FRACTION = (SHOULDER_Z - ELBOW_Z) / (SHOULDER_Z - HAND_Z)
"""How far down the shoulder→hand line the elbow sits — 0.4676, derived from ``ELBOW_Z``
rather than typed, so the joint and the fold cluster cannot drift apart.

The elbow is put ON that line, not beside it, and that is deliberate: the split must not
change the arm's REACH. ``GUN_MOUNT_RATIO`` is solved from shoulder→hand and
``verify_gun_pose`` measures the held pose against the torso's half-depth; a rest elbow
nudged off the line would quietly shorten the arm and move both."""

SLEEVE_CLUSTER_SIGMA = 0.072
"""Metres, gaussian half-width of the elbow-cluster envelope. Round-3 judge: rings at
one even pitch read as quilt channels; compression folds are an accordion at the elbow
that opens into nothing by the shoulder. At this sigma a ring on the elbow line keeps
full depth, the ring under the armband keeps ~27 %, and the sleeve's upper third
(z > 1.19) is under 5 % — nearly smooth, as briefed."""

SLEEVE_RING_OFFSETS = (-0.052, -0.026, -0.004, 0.018, 0.046, 0.082, 0.128)
"""Metres from the elbow line, per ring: the pitch itself is the frequency ramp —
22–28 mm between rings at the crease, opening to 46 mm past the armband. Seven rings
replace the old five because five spread evenly LOOKED like five; seven bunched at the
bend read as one event, which is what a fold cluster is."""

DRAPE_CREASES = ((-2.04, 0.55), (-1.60, -0.30), (-1.14, 0.25))
"""(theta home, drift rad/m) per front drape crease, in the trunk's polar frame where
the zip line is −π/2. Three vertical valleys hanging from the pec line: one on the
figure's right pec, one just right of the zip, one under the pocket column — spacing
and drift deliberately unequal, because the judged defect was PERIODICITY and the fix
must not trade one repeat pattern for another."""

TENSION_CREASE = ((0.098, 1.128), (-0.155, 0.895))
"""(x, z) endpoints of the one diagonal crease: from just under the LEFT pocket flap's
bottom edge (flap ends z 1.145) dragged to above the RIGHT hip. The asymmetric pull a
worn jacket takes from a loaded pocket; it also breaks the vertical rhythm of
``DRAPE_CREASES`` at square-on light."""


def _fold_hash(k: float) -> float:
    """Deterministic noise in [0, 1). sin-hash, seeded by FOLD_SEED only — the classic
    shader one-liner, chosen because it needs no RNG state and cannot drift between
    runs of the same source."""
    return (math.sin(k * 12.9898 + FOLD_SEED * 78.233) * 43758.5453) % 1.0


def _crease(z: float, centre: float, width: float, depth: float) -> float:
    """One fold valley: a gaussian dent of ``depth`` metres, ``width`` metres half-wide.
    Valleys only — the ridge between two valleys is the untouched surface, which is how
    real compression folds work (fabric has nowhere to go but in)."""
    return -depth * math.exp(-(((z - centre) / width) ** 2))


def clothing_displacement(x: float, y: float, z: float,
                          profile) -> float:
    """Metres along the outward normal at one point of the welded body.

    ``profile`` is the trunk loft's ``_profile`` — the same rings the geometry was
    built from, so the trunk features cannot drift off the trunk. Region gates are
    deliberately coarse (half-spaces and radii): a fold field that needed the same
    precision as the placement table would be a second placement table.
    """
    d = 0.0

    # ── sleeves: compression folds clustered at the elbow crease ────────────
    # Round-3 judge's residue #1: even 68 mm pitch up the whole sleeve reads as
    # QUILT CHANNELS. Compression folds bunch where the arm bends — dense, deep,
    # irregular at the elbow; sparse and shallow toward the shoulder. Amplitude
    # AND frequency now both rise toward ``ELBOW_Z``: the ring ladder's pitch
    # tightens into the crease (``SLEEVE_RING_OFFSETS``) and each ring's depth is
    # weighted by a gaussian on its distance from the elbow line. Phase, jitter
    # and the exact elbow line are dealt per arm off the seed — no mirror.
    for s, key in ((1.0, 0.0), (-1.0, 7.0)):
        if x * s <= 0.14:
            continue
        dxa, dya = x - s * ARM_X, y - ARM_Y
        r = math.hypot(dxa, dya)
        if r < 0.115 and 0.88 < z < 1.30:
            a = math.atan2(dya, dxa * s)
            w = 0.30 + 0.70 * max(0.0, math.cos(a - SLEEVE_FOLD_AXIS)) ** 1.3
            env = min(1.0, (1.30 - z) / 0.06, (z - 0.88) / 0.06)
            # The armband is a clean accent ring by contract; creases crossing it
            # would shred the one thing a per-runner tint colours. Damped, not
            # skipped — a band on a creased sleeve still sits on fabric.
            amp = 0.25 if (s > 0 and 1.055 < z < 1.150) else 1.0
            elbow = ELBOW_Z + 0.012 * (_fold_hash(key + 90.0) - 0.5)
            for i, off in enumerate(SLEEVE_RING_OFFSETS):
                g = math.exp(-((off / SLEEVE_CLUSTER_SIGMA) ** 2))
                zc = elbow + off + 0.013 * (2.0 * _fold_hash(key + i) - 1.0)
                zc += 0.020 * (1.0 - math.cos(a - SLEEVE_FOLD_AXIS))   # the fan
                # 8.8–13 mm per ring, down from round 2's 10.5–15.5: adjacent
                # cluster rings OVERLAP (22 mm apart, ~14 mm wide), so the round-2
                # depths summed into the −16 mm clamp and the decimator drew the
                # saturated zone as shard-like flaps on the elbow silhouette.
                depth = ((0.0088 + 0.0042 * _fold_hash(key + i + 40.0))
                         * g * w * env * amp)
                # Tight, deep valleys in the cluster; wider, fainter ghosts of
                # rings as the ladder opens toward the shoulder.
                d += _crease(z, zc, 0.0145 + 0.008 * (1.0 - g), depth)

    # ── trunk features, gated onto the loft's own surface ───────────────────
    if abs(x) < 0.218 and HEM_BOTTOM_Z - 0.012 < z < 1.462:
        rx, ry, yc = profile(z)
        frac = (x / rx) ** 2 + ((y - yc) / ry) ** 2
        if 0.55 < frac < 1.45:
            theta = math.atan2(y - yc, x)

            # Waist compression band, where the hem cinch gathers the fabric.
            g = math.exp(-(((z - 0.868) / 0.030) ** 2))
            if g > 1e-3:
                n = (0.5 + 0.35 * math.sin(3.0 * theta + 6.28 * _fold_hash(1.0))
                     + 0.25 * math.sin(5.0 * theta + 6.28 * _fold_hash(2.0)))
                d += -0.0110 * g * min(1.0, max(0.0, n))

            # Hem ripple, ± — the one feature allowed to push OUT, because a hem
            # swings around its own line. Deepened irregularly per the brief.
            g = math.exp(-(((z - 0.765) / 0.028) ** 2))
            if g > 1e-3:
                n = (math.sin(6.0 * theta + 6.28 * _fold_hash(3.0))
                     + 0.55 * math.sin(11.0 * theta + 6.28 * _fold_hash(4.0)))
                d += 0.0050 * g * n

            # Back yoke: two wandering horizontal wrinkles across the shoulder
            # blades — the fold a jacket takes from its own hanging weight.
            if (y - yc) > 0.35 * ry and 1.27 < z < 1.41:
                for i, zc in enumerate((1.315, 1.362)):
                    wav = 0.008 * math.sin(3.0 * theta + 6.28 * _fold_hash(10.0 + i))
                    d += _crease(z, zc + wav, 0.015, 0.0065)

            # Chest rumple: low-frequency unevenness over the front panel. Not a
            # crease — the broad ~5 mm swell that stops flat fabric reading as
            # injection-moulded plastic in a head-on beam. 0.0035 was invisible in
            # round 2's frontal shot; a head-on light only sees a fold via the
            # shadow its own depth casts, so the front needs more depth than the
            # profile-lit sides do.
            if (y - yc) < 0.0 and 0.90 < z < 1.34:
                d += (0.0050 * math.sin(14.0 * z + 6.28 * _fold_hash(5.0))
                      * math.sin(2.3 * theta + 6.28 * _fold_hash(6.0)))

            # Round-3 judge's residue #2: the front panel. The sides and back
            # have folds; the chest/stomach — the surface another runner's beam
            # hits square-on most often — was still a calm sheet (the rumple
            # above is a swell, not a crease). Two systems, both LOW-amplitude,
            # wide and soft-shouldered, because the front is read at glancing
            # AND head-on light and a tight ridge would sparkle:
            #
            # DRAPE — 2–3 shallow vertical valleys falling from the pec/zip
            # line to the waist band. Fabric hangs off the pecs, so each valley
            # deepens as it falls (``hang``) and dies into the waist band's own
            # compression. Homes, drifts and meanders per ``DRAPE_CREASES``,
            # jittered off the seed — asymmetric on purpose.
            if (y - yc) < 0.0 and 0.885 < z < 1.285:
                # The pocket flap is judged-and-closed geometry: mute both
                # drape systems under its slightly inflated footprint so the
                # one crisp rectangle on the jacket keeps its edges.
                guard = 0.15 if (0.042 < x < 0.145 and 1.132 < z < 1.218) else 1.0
                front = min(1.0, -(y - yc) / (0.55 * ry))
                zenv = min(1.0, (1.285 - z) / 0.085, (z - 0.885) / 0.05)
                hang = 1.0 + 0.35 * min(1.0, max(0.0, (1.16 - z) / 0.22))
                fd = 0.0   # the front panel's own ledger — budgeted below
                for i, (th0, drift) in enumerate(DRAPE_CREASES):
                    th = (th0 + 0.10 * (2.0 * _fold_hash(60.0 + i) - 1.0)
                          + drift * (z - 1.10)
                          + 0.05 * math.sin(9.0 * z + 6.28 * _fold_hash(64.0 + i)))
                    depth = ((0.0046 + 0.0016 * _fold_hash(70.0 + i))
                             * zenv * hang * front * guard)
                    fd += -depth * math.exp(-(((theta - th) / 0.30) ** 2))

                # TENSION — one diagonal crease dragged from the left pocket
                # flap toward the right hip (``TENSION_CREASE``): the pull a
                # worn jacket takes from a loaded pocket, and the line that
                # breaks the verticals' rhythm under a square-on beam.
                (px, pz), (hx, hz) = TENSION_CREASE
                ux, uz = hx - px, hz - pz
                ln = math.hypot(ux, uz)
                t = ((x - px) * ux + (z - pz) * uz) / (ln * ln)
                if -0.04 < t < 1.04:
                    dist = abs((x - px) * uz - (z - pz) * ux) / ln
                    tenv = max(0.0, min(1.0, (t + 0.02) / 0.16, (1.02 - t) / 0.16))
                    fd += (-0.0058 * math.exp(-((dist / 0.034) ** 2))
                           * tenv * front * guard)
                # Where tension crosses a drape valley the two would stack with
                # the chest rumple into the global clamp, and round 2 rendered
                # exactly that: two pit-dark marks on the stomach that read as
                # holes, not fabric. The front panel spends at most 9.5 mm.
                d += max(fd, -0.0095)

            # Armpit tension webs: rays converging on the armpit, front and back —
            # reach widened after round 2 so the drag folds actually cross the
            # chest panel instead of hiding under the sleeve head.
            for s in (1.0, -1.0):
                rr = math.hypot(x - s * 0.206, z - 1.338)
                if rr < 0.17:
                    phi = math.atan2(z - 1.338, x * s - 0.206)
                    web = (max(0.0, math.sin(2.7 * phi + 6.28 * _fold_hash(8.0)))
                           ** 1.5)
                    d += -0.0072 * web * math.exp(-((rr / 0.095) ** 2))

    # ── trousers: knee creases and boot-top bunching, subtler than the jacket ──
    # The column follows the harvested shank/knee line (knee centre x 0.142,
    # y ~+0.002), not the boot's LEG_X — the creases live on the knee, not the ankle.
    for s in (1.0, -1.0):
        dxl = x - s * 0.146
        r = math.hypot(dxl, y - 0.012)
        if x * s > 0.02 and r < 0.115 and 0.400 < z < 0.60:
            al = math.atan2(y - 0.012, dxl * s)
            env = min(1.0, (0.60 - z) / 0.04, (z - 0.400) / 0.03)
            # Knee: three creases, front-weighted — trousers bag over the knee cap.
            nf = max(0.0, -(y - 0.012) / max(r, 1e-6))
            w = 0.35 + 0.65 * nf
            for i, zc in enumerate((0.438, 0.480, 0.522)):
                zj = zc + 0.016 * (_fold_hash(20.0 + i + s) - 0.5)
                depth = (0.0050 + 0.0018 * _fold_hash(30.0 + i + s)) * w * env
                d += _crease(z, zj, 0.014, depth)
            # Bunching: two rings just above the boot cuff, wavering in z.
            for i, zc in enumerate((0.415, 0.450)):
                wav = 0.006 * math.sin(2.0 * al + 6.28 * _fold_hash(50.0 + i + s))
                d += _crease(z, zc + wav, 0.013, 0.0055 * env)

    return min(FOLD_CLAMP[1], max(FOLD_CLAMP[0], d))


def sculpt_folds(body: bpy.types.Object) -> None:
    """Applies ``clothing_displacement`` to every vertex, along its own normal.

    Runs between the smoothing and the decimation (see ``weld`` for why that slot and
    no other), on the full ~40k-vertex surface, so a 14 mm fold is drawn by dozens of
    vertices before the decimator chooses which ones earn their keep.
    """
    probe = Loft("_TrunkProbe", TRUNK_RINGS)
    mesh = body.data
    moved = 0
    peak = 0.0
    for v in mesh.vertices:
        f = clothing_displacement(v.co.x, v.co.y, v.co.z, probe._profile)
        if abs(f) > 1e-5:
            v.co += v.normal * f
            moved += 1
            peak = max(peak, abs(f))
    mesh.update()
    print(f"FOLD_PASS verts_moved={moved}/{len(mesh.vertices)} "
          f"peak={peak * 1000.0:.1f}mm clamp=({FOLD_CLAMP[0] * 1000.0:.0f},"
          f"{FOLD_CLAMP[1] * 1000.0:+.0f})mm seed={FOLD_SEED:.0f}")


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


@dataclass(frozen=True)
class Box:
    """An axis-aligned box: the headlamp housing.

    The one part that must NOT be round — a lamp housing reads as gear precisely
    because it has flats, and the light smoothing pass rounds its edges just enough
    to stop it reading as a debug cube."""

    name: str
    centre: tuple[float, float, float]
    half: tuple[float, float, float]

    def contains(self, p: Vector) -> bool:
        c, h = self.centre, self.half
        return all(abs(p[i] - c[i]) < h[i] for i in range(3))

    def depth(self, p: Vector) -> float:
        c, h = self.centre, self.half
        margins = [h[i] - abs(p[i] - c[i]) for i in range(3)]
        return max(0.0, min(margins))

    def aabb(self) -> tuple[Vector, Vector]:
        c, h = Vector(self.centre), Vector(self.half)
        return c - h, c + h

    def samples(self) -> list[Vector]:
        """Grid samples over all six faces."""
        c, h = self.centre, self.half
        pts: list[Vector] = []
        n = max(3, int(math.sqrt(PREFLIGHT_SAMPLES / 6)))
        for axis in range(3):
            u, v = (axis + 1) % 3, (axis + 2) % 3
            for sign in (-1.0, 1.0):
                for i in range(n):
                    for j in range(n):
                        p = [0.0, 0.0, 0.0]
                        p[axis] = c[axis] + sign * h[axis]
                        p[u] = c[u] + h[u] * (2.0 * (i + 0.5) / n - 1.0)
                        p[v] = c[v] + h[v] * (2.0 * (j + 0.5) / n - 1.0)
                        pts.append(Vector(p))
        return pts

    def build(self) -> list[bpy.types.Object]:
        obj = blendkit.add_box(self.name, tuple(2.0 * h for h in self.half),
                               location=self.centre)
        _bake_transform(obj)
        return [obj]


@dataclass(frozen=True)
class Loft:
    """One crease-free surface through elliptical rings: the jacket's trunk.

    Control rings are ``(z, rx, ry, y_centre)``; between them the profile is COSINE
    interpolated — tangent-flat at every control ring, so no slope discontinuity
    survives into the level set to read as a fold. That is the whole reason this class
    exists: a stack of ellipsoids keeps a concave crease at every mutual intersection
    (rounds one and two of this figure, Michelin then quilted), and a loft has no
    intersections to crease at. Emitted as dense rings every ~15 mm so the linear mesh
    between them is far below what a 9 mm voxel can record.
    """

    name: str
    rings: tuple[tuple[float, float, float, float], ...]

    def _profile(self, z: float) -> tuple[float, float, float]:
        """(rx, ry, yc) at height z, cosine-eased between control rings."""
        rings = self.rings
        if z <= rings[0][0]:
            return rings[0][1], rings[0][2], rings[0][3]
        for a, b in zip(rings, rings[1:]):
            if z <= b[0]:
                t = (z - a[0]) / (b[0] - a[0])
                e = 0.5 * (1.0 - math.cos(math.pi * t))
                return (a[1] + (b[1] - a[1]) * e,
                        a[2] + (b[2] - a[2]) * e,
                        a[3] + (b[3] - a[3]) * e)
        return rings[-1][1], rings[-1][2], rings[-1][3]

    def _frac(self, p: Vector) -> float:
        rx, ry, yc = self._profile(p.z)
        return (p.x / rx) ** 2 + ((p.y - yc) / ry) ** 2

    def contains(self, p: Vector) -> bool:
        return self.rings[0][0] < p.z < self.rings[-1][0] and self._frac(p) < 1.0

    def depth(self, p: Vector) -> float:
        """Conservative: radial margin scaled by the smaller radius, capped by the
        distance to either end. A lower bound is the safe direction here — the trunk's
        joins are all tens of millimetres deep, and under-reporting them cannot make
        the preflight pass a join that is really shallow."""
        if not self.contains(p):
            return 0.0
        rx, ry, _ = self._profile(p.z)
        radial = (1.0 - math.sqrt(self._frac(p))) * min(rx, ry)
        return min(radial, p.z - self.rings[0][0], self.rings[-1][0] - p.z)

    def aabb(self) -> tuple[Vector, Vector]:
        rx = max(r[1] for r in self.rings)
        lo_y = min(r[3] - r[2] for r in self.rings)
        hi_y = max(r[3] + r[2] for r in self.rings)
        return (Vector((-rx, lo_y, self.rings[0][0])),
                Vector((rx, hi_y, self.rings[-1][0])))

    def _dense(self) -> list[tuple[float, float, float, float]]:
        z0, z1 = self.rings[0][0], self.rings[-1][0]
        count = max(8, int((z1 - z0) / 0.015))
        out = []
        for i in range(count + 1):
            z = z0 + (z1 - z0) * i / count
            rx, ry, yc = self._profile(z)
            out.append((z, rx, ry, yc))
        return out

    def samples(self) -> list[Vector]:
        pts: list[Vector] = []
        dense = self._dense()
        per_ring = max(12, PREFLIGHT_SAMPLES // len(dense))
        for z, rx, ry, yc in dense:
            for j in range(per_ring):
                a = 2.0 * math.pi * j / per_ring
                pts.append(Vector((rx * math.cos(a), yc + ry * math.sin(a), z)))
        return pts

    def build(self) -> list[bpy.types.Object]:
        """The lofted mesh: dense rings bridged with quads, ngon caps at both ends."""
        segments = 96
        dense = self._dense()
        verts: list[tuple[float, float, float]] = []
        for z, rx, ry, yc in dense:
            for j in range(segments):
                a = 2.0 * math.pi * j / segments
                verts.append((rx * math.cos(a), yc + ry * math.sin(a), z))
        faces: list[tuple[int, ...]] = []
        for i in range(len(dense) - 1):
            base, nxt = i * segments, (i + 1) * segments
            for j in range(segments):
                k = (j + 1) % segments
                faces.append((base + j, base + k, nxt + k, nxt + j))
        faces.append(tuple(range(segments - 1, -1, -1)))                      # bottom
        faces.append(tuple((len(dense) - 1) * segments + j for j in range(segments)))
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(verts, [], faces)
        mesh.update()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        return [obj]


# ── The harvested body ──────────────────────────────────────────────────────
#
# CC0, Blender Foundation "Human Base Meshes" bundle v1.4.1, object
# GEO-body_male_realistic — vendored into the repo the same way the Mixamo clips are,
# so the generator rebuilds from a clean checkout with no downloads. The vendored
# file is the base CAGE alone (10,590 quads, multires data stripped, eyeball objects
# dropped — the hood void hides the face), canonicalized to exactly 1.700 m with feet
# on z = 0 and x centred; every landmark constant above is measured in that frame.

HUMAN_SOURCE = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "source", "human", "body_male_realistic.blend")
"""The vendored body. Absent → this generator fails loudly: unlike the mocap (which
has a procedural fallback), there is no second body to fall back to."""

HUMAN_OBJECT = "HumanBase"

HUMAN_SUBSURF = 2
"""Catmull-Clark levels applied to the suit copy before the weld. The cage's ~26 mm
edges would be digitised as faceting by a 9 mm voxel (SAMPLE_CHORD's argument); two
subdivisions put the surface below the grid the same way the primitives' tessellation
is. The HANDS stay at cage level — they keep their quads instead of being remeshed."""

LEG_INFLATE = 0.005
"""Metres of outward displacement on the trouser band (z 0.32 → hem) so the exposed
leg reads as cloth over a leg rather than the leg: the identity brief's failure case
is 'athletic nude human', and a skin-tight anatomical calf is half of that read.
Inward-facing inner-thigh verts are skipped so the slot the remesh needs stays open,
and the band stops above the boots so the measured boot containment is untouched."""

FOOT_SPLAY_DEG = 11.1
"""The vendored stance's toe-out angle, measured per foot. The feet are rotated
straight (about each ankle) because the toes are what tell a distant viewer which way
a racer faces, and because ``measure_sole``'s bands and the gait solver both assume a
foot that runs along −Y."""

HAND_CUT_FROM_TIP = 0.178
"""Where the harvested hand is severed, in metres from the middle fingertip along the
measured arm axis — through the wrist's narrowest ring (measured 0.170–0.185, and it
must stay INSIDE that window: a first cut at 0.170 landed on the palm side of it and
the cut ring picked up the thumb-base flare, which no wearable cuff can swallow).
The cut is capped so each hand is its own watertight shell, and the ring's world
positions are recorded (``_HAND_CAP_POINTS``) so ``verify_hand_shells`` can demand
that THE CUT — and exactly the cut, not the glove's visible flare — hides inside
the cuff."""

_HAND_CAP_POINTS: dict[str, list[Vector]] = {}
"""side ("left"/"right") → the cut ring's vertex positions, canonical frame, filled
by ``harvest_hands`` and consumed by ``verify_hand_shells`` through the fit."""

HAND_PALM_FROM_WRIST = 0.085
"""The palm anchor: this far distal of the wrist-ring centre, on the hand's axis. It
is the point ``harvest_hands`` lands on ``HAND_C`` — which is the gun-mount solve —
so the bone tail, the mount, and the visible palm coincide by construction."""

KNUCKLE_FROM_TIP = 0.093
"""Where the fingers start, in metres from the middle fingertip along the hand's axis:
the MCP line. MEASURED — slabbing the harvested arm and reading its width across the
palm plane, the section jumps 28.5 mm → 58.1 mm between 0.075 and 0.105 m from the tip,
which is the metacarpal heads spreading into four fingers. 0.093 is the middle of that
step and it is where ``harvest_hands`` starts curling."""

FINGER_CURL_DEG = 88.0
"""Total curl from the MCP line to the fingertip, degrees, applied as a circular BEND
(radius = length / angle) so the fingers arc instead of hinging as one flap.

**Why the hands are curled at all.** The vendored body is a T/A-posed reference mesh and
its hands are FLAT — every finger straight, fingers fanned apart, thumb out. On a body
they read as a reference pose; on a hooded worker sprinting down a corridor they read as
claws, which is the round-3 judgement of ``close_hand.png`` and a monster's silhouette on
the figure that is supposed to be the monster's negation. There are no finger bones and
there will not be any — a 9 mm voxel already turns fingers into a mitten and the shells
exist to escape that — so the curl is baked at harvest time and the glove stays rigid.

88° is a RELAXED curl, not a fist: the fingertips end roughly a knuckle's width off the
palm. A full fist would read as a threat, and this figure is not carrying one."""

FINGER_GATHER = 0.42
"""How much of their lateral spread the fingers keep at the tip — they are drawn toward
the hand's own axis, 1.0 at the knuckle line and this at the fingertip.

The curl alone was not enough and the round-1 close-up is why. The vendored body is a
reference mesh and its fingers are FANNED, four separate tapered rods with 6–10 mm of air
between them; bending them turns four rods into four hooks, and four hooks under a torch
at 3 m is still a claw. What a work glove looks like is one mass with the finger line
suggested on it, so the fan is closed and the four merge into that mass. They interpenetrate
after this and that is intended — the shell is welded to nothing, rendered as a solid, and
its interior is never seen; ``verify_hand_shells`` measures what matters about it, which is
the tuck, the clearance from the suit and the absence of a z-fight."""

THUMB_CURL_DEG = 34.0
"""Extra sweep on the thumb column, about the palm normal, bringing it in across the
fingers. The bend above runs about the knuckle axis and the thumb's own column lies close
to that axis, so the bend alone leaves it sticking out — which is exactly the spike that
photographs in ``close_hand.png``. Applied to the radial third of the hand only, ramped
in from the wrist so the cut ring — which ``verify_hand_shells`` measures — cannot move."""

_HUMAN_MESH: bpy.types.Mesh | None = None


def _human_pristine() -> bpy.types.Mesh:
    """Loads the vendored cage once per run; returns the untouched datablock."""
    global _HUMAN_MESH
    if _HUMAN_MESH is not None:
        return _HUMAN_MESH
    if not os.path.exists(HUMAN_SOURCE):
        blendkit.fail(
            f"the vendored body is missing: {HUMAN_SOURCE}. It is committed beside "
            "the Mixamo sources (docs/ASSETS.md, CC0 provenance); this generator "
            "cannot build a runner without it.")
    with bpy.data.libraries.load(HUMAN_SOURCE) as (src, dst):
        if HUMAN_OBJECT not in src.objects:
            blendkit.fail(f"{HUMAN_SOURCE} does not contain object '{HUMAN_OBJECT}' "
                          f"(has: {src.objects}). The vendored file is not the one "
                          "the vendor script writes.")
        dst.objects = [HUMAN_OBJECT]
    loaded = dst.objects[0]
    mesh = loaded.data
    mesh.use_fake_user = True   # survives with zero object users
    bpy.data.objects.remove(loaded, do_unlink=True)
    lo = Vector((math.inf,) * 3)
    hi = Vector((-math.inf,) * 3)
    for v in mesh.vertices:
        for i in range(3):
            lo[i] = min(lo[i], v.co[i])
            hi[i] = max(hi[i], v.co[i])
    print(f"HUMAN_SOURCE verts={len(mesh.vertices)} polys={len(mesh.polygons)} "
          f"height={hi.z - lo.z:.4f}m floor={lo.z:+.5f} span={hi.x - lo.x:.4f}m "
          f"file={os.path.basename(HUMAN_SOURCE)}")
    if abs((hi.z - lo.z) - 1.700) > 0.002 or abs(lo.z) > 0.002:
        blendkit.fail(
            f"the vendored body measures {hi.z - lo.z:.4f} m with its floor at "
            f"{lo.z:+.4f} — not the canonical 1.700 m on z = 0 every landmark "
            "constant in this file was measured against. Re-vendor it.")
    _HUMAN_MESH = mesh
    return mesh


def _bm_walk(seed: bmesh.types.BMVert, allowed) -> set:
    """Connected BMVerts reachable from ``seed`` while ``allowed`` holds."""
    seen = {seed}
    stack = [seed]
    while stack:
        v = stack.pop()
        for e in v.link_edges:
            w = e.other_vert(v)
            if w not in seen and allowed(w):
                seen.add(w)
                stack.append(w)
    return seen


def _severed_arms(bm: bmesh.types.BMesh) -> tuple[set, set]:
    """The two arm vert sets of the A-posed cage, one topology walk per side."""
    out = []
    for s in (1.0, -1.0):
        tip = max(bm.verts, key=lambda v: v.co.x * s)
        out.append(_bm_walk(tip, lambda w: w.co.x * s > X_CUT))
    return out[0], out[1]


def _prep_suit_body() -> bpy.types.Object:
    """The body copy that feeds the WELD: arms severed and capped, feet straightened,
    trouser band inflated, then subdivided below the voxel grid.

    The arms go because a hanging human arm sits closer to its own ribs than a 9 mm
    voxel can keep open — the sleeve is the arm here, as the placement table says.
    The feet stay: they weld into the boots, which is what connects leg to sole.
    """
    mesh = _human_pristine().copy()
    mesh.name = "HumanSuit"
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()

    arm_l, arm_r = _severed_arms(bm)
    doomed = arm_l | arm_r
    faces = [f for f in bm.faces if any(v in doomed for v in f.verts)]
    bmesh.ops.delete(bm, geom=faces, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bound = [e for e in bm.edges if len(e.link_faces) == 1]
    bmesh.ops.holes_fill(bm, edges=bound, sides=0)
    left_open = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    if left_open:
        bm.free()
        blendkit.fail(
            f"severing the arms left {left_open} boundary edges the cap fill could "
            "not close — an open shoulder leaks the voxel level set and the remesh "
            "returns garbage. X_CUT no longer lands on a clean ring of this mesh.")

    # Straighten the feet about each ankle (see FOOT_SPLAY_DEG), and SHRINK them 18 %
    # toward it in plan: a foot is box-cornered and a boot pad is an ellipsoid, so a
    # full-size foot pokes its toe corners through any pad that still reads as a
    # boot. The foot is invisible inside the boot — its only job is welding the leg
    # to the sole — and a smaller foot does that job with margin instead of luck.
    # Blended in over 30 mm so the shin never creases.
    for v in bm.verts:
        if v.co.z < 0.13:
            s = 1.0 if v.co.x > 0.0 else -1.0
            f = min(1.0, (0.13 - v.co.z) / 0.03)
            th = -s * math.radians(FOOT_SPLAY_DEG) * f
            px, py = s * LEG_X, ANKLE_Y
            dx, dy = v.co.x - px, v.co.y - py
            c, sn = math.cos(th), math.sin(th)
            shrink = 1.0 - 0.18 * f
            v.co.x = px + (dx * c - dy * sn) * shrink
            v.co.y = py + (dx * sn + dy * c) * shrink

    # Trouser standoff (see LEG_INFLATE). Horizontal component of the normal only —
    # inflating along z would move the crotch and the height.
    bm.normal_update()
    inflated = 0
    for v in bm.verts:
        z = v.co.z
        if 0.32 < z < HEM_BOTTOM_Z - 0.010:
            s = 1.0 if v.co.x >= 0.0 else -1.0
            if v.normal.x * s < -0.25:
                continue    # inner thigh: the slot stays open
            ramp = min(1.0, (z - 0.32) / 0.06, (HEM_BOTTOM_Z - 0.010 - z) / 0.05)
            n = Vector((v.normal.x, v.normal.y, 0.0))
            if n.length_squared > 1e-12:
                v.co += n.normalized() * (LEG_INFLATE * ramp)
                inflated += 1

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    obj = bpy.data.objects.new("HumanSuit", mesh)
    bpy.context.collection.objects.link(obj)
    sub = obj.modifiers.new("Refine", "SUBSURF")
    sub.levels = HUMAN_SUBSURF
    sub.render_levels = HUMAN_SUBSURF
    _apply_modifier(obj, sub)
    print(f"HUMAN_PREP suit verts={len(obj.data.vertices)} "
          f"polys={len(obj.data.polygons)} arms_cut={len(arm_l)}+{len(arm_r)}v "
          f"feet_straightened={FOOT_SPLAY_DEG:.1f}deg trouser_inflated={inflated}v "
          f"subsurf={HUMAN_SUBSURF}")
    return obj


class MeshPart:
    """The harvested body, speaking the placement table's Part protocol.

    ``contains``/``depth`` are answered by a BVH over the prepped mesh: a point is
    inside when the nearest surface's normal faces away from it — exact on a closed
    manifold, which ``_prep_suit_body`` guarantees. The preflight overlap graph and
    the coverage check both run through this, so the body is a first-class citizen
    of the same weld argument the primitives make, not a special case beside it.
    """

    def __init__(self, name: str, obj: bpy.types.Object):
        self.name = name
        self.obj = obj
        mesh = obj.data
        self._verts = [v.co.copy() for v in mesh.vertices]
        self._bvh = BVHTree.FromPolygons(
            [tuple(v.co) for v in mesh.vertices],
            [tuple(p.vertices) for p in mesh.polygons])
        lo = Vector((math.inf,) * 3)
        hi = Vector((-math.inf,) * 3)
        for co in self._verts:
            for i in range(3):
                lo[i] = min(lo[i], co[i])
                hi[i] = max(hi[i], co[i])
        self._lo, self._hi = lo, hi

    def contains(self, p: Vector) -> bool:
        hit = self._bvh.find_nearest(p)
        return hit[0] is not None and hit[1].dot(p - hit[0]) < 0.0

    def depth(self, p: Vector) -> float:
        co, normal, _idx, dist = self._bvh.find_nearest(p)
        if co is None or normal.dot(p - co) >= 0.0:
            return 0.0
        return dist

    def aabb(self) -> tuple[Vector, Vector]:
        return self._lo.copy(), self._hi.copy()

    def samples(self) -> list[Vector]:
        step = max(1, len(self._verts) // (PREFLIGHT_SAMPLES * 4))
        return [co.copy() for co in self._verts[::step]]

    def surface(self) -> list[Vector]:
        """Every vertex — the coverage check wants the whole skin, not a subsample."""
        return self._verts

    def build(self) -> list[bpy.types.Object]:
        return [self.obj]


def harvest_hands() -> list[bpy.types.Object]:
    """The two hands, cut from the pristine cage at the wrist, capped, and re-posed
    from the A-pose hang onto the cuffs — palm anchor on ``HAND_C`` exactly.

    They are DELIBERATELY not welded: a 9 mm voxel turns fingers back into the mitten
    this task exists to delete (the gaps between fingers are 2–6 mm). Each hand stays
    a separate watertight shell at the cage's own quad topology, joined into the body
    object after the weld, skinned rigidly to its arm bone, and tucked into the cuff
    so the seam reads as the glove's own cuff. Task #81's actual deliverable.
    """
    out: list[bpy.types.Object] = []
    src = _human_pristine()
    for s, side in ((1.0, "L"), (-1.0, "R")):
        mesh = src.copy()
        mesh.name = f"Hand_{side}"
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.verts.ensure_lookup_table()

        tip = max(bm.verts, key=lambda v: v.co.x * s)
        arm = _bm_walk(tip, lambda w: w.co.x * s > X_CUT)

        # the arm's own axis, measured (power iteration on the second moment)
        cos = [v.co.copy() for v in arm]
        cen = sum(cos, Vector()) / len(cos)
        axis = (tip.co - cen).normalized()
        for _ in range(30):
            acc = Vector()
            for co in cos:
                d = co - cen
                acc += d * d.dot(axis)
            axis = acc.normalized()
        if axis.x * s < 0.0:
            axis = -axis
        tipd = max((co - cen).dot(axis) for co in cos)

        def from_tip(v):
            return tipd - (v.co - cen).dot(axis)

        keep = {v for v in arm if from_tip(v) < HAND_CUT_FROM_TIP}
        doomed = [f for f in bm.faces if any(v not in keep for v in f.verts)]
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bound = [e for e in bm.edges if len(e.link_faces) == 1]
        cap_ring = {v for e in bound for v in e.verts}
        bmesh.ops.holes_fill(bm, edges=bound, sides=0)
        if sum(1 for e in bm.edges if len(e.link_faces) == 1):
            bm.free()
            blendkit.fail(f"the {side} hand's wrist cap failed to close — the cut at "
                          f"{HAND_CUT_FROM_TIP} m from the tip is not a clean ring.")

        # measured local frame: a = distal (fingers), n = palm normal, w = a × n.
        verts = [v.co.copy() for v in bm.verts]
        hc = sum(verts, Vector()) / len(verts)
        b = axis.cross(Vector((0.0, 0.0, 1.0))).normalized()
        c2 = axis.cross(b).normalized()
        fingers = [co - hc for co in verts
                   if tipd - (co - cen).dot(axis) < 0.10]
        best = None
        for t in range(90):
            th = math.pi * t / 90.0
            d = b * math.cos(th) + c2 * math.sin(th)
            spread = sum(abs(f.dot(d)) for f in fingers)
            if best is None or spread < best[0]:
                best = (spread, d)
        n = best[1]
        if n.x * s > 0.0:
            n = -n              # palm faces the body (−x on the left side)
        a_src = axis
        w_src = a_src.cross(n).normalized()
        n = w_src.cross(a_src).normalized()   # exact orthonormal triad

        # ── the relaxed curl (FINGER_CURL_DEG / THUMB_CURL_DEG) ──────────────
        # Both passes run in this measured triad, on the hand's own axis, BEFORE the
        # frame change below — so the palm anchor, the wrist ring and the cut the cuff
        # has to swallow are all measured on geometry the curl never touched.

        def _from_tip(co):
            return tipd - (co - cen).dot(axis)

        # 0. close the fan (FINGER_GATHER) — before anything else, because it is stated
        #    in the undeformed hand's own frame and it must not fight the curl.
        gathered = 0
        for v in bm.verts:
            t = _from_tip(v.co)
            if t >= KNUCKLE_FROM_TIP:
                continue
            d = v.co - cen
            axial = d.dot(axis)
            perp = d - axis * axial
            lat = perp.dot(w_src)
            k = 1.0 - (1.0 - FINGER_GATHER) * (1.0 - t / KNUCKLE_FROM_TIP)
            v.co -= w_src * (lat * (1.0 - k))
            gathered += 1

        # Which side is the thumb on? Answered by the mesh rather than assumed. In the
        # band from_tip 0.10-0.17 the four fingers are already behind us and the only
        # thing still sticking out sideways is the thumb column, so the extreme |w|
        # vertex in that band names the side.
        band = [v.co for v in bm.verts if 0.10 <= _from_tip(v.co) <= 0.17]
        thumb_side = 0.0
        if band:
            far = max(band, key=lambda co: abs((co - cen).dot(w_src)))
            thumb_side = math.copysign(1.0, (far - cen).dot(w_src))

        # 1. thumb in — a rotation ABOUT THE HAND'S AXIS, which leaves every vertex's
        #    axial coordinate alone, so pass 2 below still sees the same from_tip.
        moved = 0
        if thumb_side:
            for v in bm.verts:
                d = v.co - cen
                axial = d.dot(axis)
                perp = d - axis * axial
                if perp.dot(w_src) * thumb_side < 0.018:
                    continue
                t = _from_tip(v.co)
                ramp = min(1.0, max(0.0, (0.160 - t) / 0.040))
                if ramp <= 0.0:
                    continue
                # +psi takes w toward -n (w x a = n), so the sweep is toward the palm
                # whichever side the thumb turned out to be on.
                psi = -thumb_side * math.radians(THUMB_CURL_DEG) * ramp
                v.co = cen + axis * axial + (Matrix.Rotation(psi, 3, axis) @ perp)
                moved += 1

        # 2. the fingers — a circular bend about the MCP line. Radius = length / angle,
        #    so the neutral fibre keeps its arc length and a finger arcs instead of
        #    hinging as one flap. u = 0 at the knuckle plane maps to itself, so the
        #    palm is untouched and the surface cannot tear there.
        kn = [v.co for v in bm.verts if abs(_from_tip(v.co) - KNUCKLE_FROM_TIP) < 0.008]
        theta = math.radians(max(1e-3, FINGER_CURL_DEG))
        radius = KNUCKLE_FROM_TIP / theta
        curled = 0
        if kn and FINGER_CURL_DEG > 0.0:
            knuckle = sum(kn, Vector()) / len(kn)
            for v in bm.verts:
                d = v.co - knuckle
                u = d.dot(a_src)
                if u <= 0.0:
                    continue
                m, lat = d.dot(n), d.dot(w_src)
                phi = u / radius
                v.co = (knuckle + n * radius + w_src * lat
                        - (radius - m) * (n * math.cos(phi) - a_src * math.sin(phi)))
                curled += 1
        print(f"HAND_CURL {side} fingers={curled}v x{FINGER_CURL_DEG:.0f}deg "
              f"(bend radius {radius * 1000.0:.1f}mm) gathered={gathered}v "
              f"to {FINGER_GATHER:.2f} thumb={moved}v "
              f"x{THUMB_CURL_DEG:.0f}deg side={thumb_side:+.0f}w")

        # target frame: fingers nearly straight down with a touch of forward drift,
        # palm on the thigh, thumb forward — the relaxed hang of a gloved worker.
        # (drift trimmed 0.10 → 0.08: at 0.10 the right thumb tip grazed the cuff's
        # front within the z-fight tolerance)
        a_tgt = Vector((0.0, -0.08, -0.997)).normalized()
        n_tgt = Vector((-s, -0.18, 0.0)).normalized()
        w_tgt = a_tgt.cross(n_tgt).normalized()
        n_tgt = w_tgt.cross(a_tgt).normalized()

        src_m = Matrix((a_src, n, w_src)).transposed()      # columns = source triad
        tgt_m = Matrix((a_tgt, n_tgt, w_tgt)).transposed()  # columns = target triad
        rot = tgt_m @ src_m.inverted()

        # anchor: the wrist ring centre, then HAND_PALM_FROM_WRIST down the axis
        ring = [co for co in cos if 0.170 <= tipd - (co - cen).dot(axis) < 0.185]
        wrist = sum(ring, Vector()) / len(ring)
        palm_src = wrist + a_src * HAND_PALM_FROM_WRIST
        palm_tgt = Vector((s * HAND_X, HAND_Y, HAND_Z))

        for v in bm.verts:
            v.co = rot @ (v.co - palm_src) + palm_tgt
        _HAND_CAP_POINTS["left" if s > 0 else "right"] = [
            v.co.copy() for v in cap_ring if v.is_valid]

        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        obj = bpy.data.objects.new(f"Hand_{side}", mesh)
        bpy.context.collection.objects.link(obj)
        tris = sum(max(0, len(p.vertices) - 2) for p in mesh.polygons)
        print(f"HAND_HARVEST {side} verts={len(mesh.vertices)} tris={tris} "
              f"palm=({palm_tgt.x:+.3f},{palm_tgt.y:+.3f},{palm_tgt.z:+.3f}) "
              f"wrist_src=({wrist.x:+.3f},{wrist.y:+.3f},{wrist.z:+.3f})")
        out.append(obj)
    return out


Part = Ellipsoid | Shaft | Box | Loft | MeshPart


def build_parts() -> list[Part]:
    """The worker's placement table — the harvested body plus its GARMENT, 23 parts.

    The body part carries the anatomy (head, neck, torso, legs, feet); everything
    else is clothing and kit lofted or placed AROUND it. The blob head, blob neck,
    mitten hands and cone trouser legs of the primitive era are gone — the trunk
    loft is a jacket now, not a torso. Every part overlaps at least one other by
    design; ``verify_parts_interpenetrate`` proves that rather than trusting it, and
    ``verify_body_covered`` proves the garment actually dresses the body.
    """
    parts: list[Part] = [
        MeshPart("Body", _prep_suit_body()),
        Loft("Trunk", TRUNK_RINGS),
        Ellipsoid("Collar", COLLAR_C, COLLAR_R),
        Ellipsoid("Hood", HOOD_C, HOOD_R),
        Ellipsoid("HoodSkirt", HOODSKIRT_C, HOODSKIRT_R),
        Ellipsoid("Brim", BRIM_C, BRIM_R),
        Box("Lamp", LAMP_C, LAMP_HALF),
        # The upper back. An elliptical ring pinches exactly where the shoulder
        # blades and rear deltoid corners are, so the loft alone either goes
        # barrel-deep or lets the blades poke. This ellipsoid carries the corners
        # and bulges ~5 mm proud of the loft's back — which is where a work
        # jacket's own back yoke pleat sits, so the fix reads as tailoring.
        Ellipsoid("BackYoke", (0.0, 0.072, 1.300), (0.180, 0.062, 0.150)),
        # Left sleeve only — the armband is what breaks the mirror, on purpose: it is
        # the one asymmetry that lets a spectating runner tell front-left from
        # front-right on an otherwise symmetric stranger.
        Ellipsoid("Armband", ARMBAND_C, ARMBAND_R),
        # RIGHT sleeve only: a rolled cuff — a flattened ring ~15 mm proud of the
        # sleeve just above the glove. The second deliberate asymmetry (armband left,
        # roll right): a worker's clothes are not a uniform, and a mirror-perfect
        # figure is half the plush-toy read. Painted back to Runner_Jacket by an
        # explicit region in assign_materials, or the glove paint would swallow it.
        # Rides ABOVE the harvested hand's wrist stub (stub top ~0.825) so the ring
        # never intersects the hand shell — 0.878 after one build measured a single
        # roll face z-fighting the stub at 0.868.
        Ellipsoid("CuffRoll", (-0.283, -0.055, 0.878), (0.063, 0.073, 0.026)),
        # LEFT chest only: a patch-pocket flap, a flat box the remesh rounds into a
        # raised rectangle ~17 mm proud. The one crisp rectangle on the jacket front,
        # and the front's only feature that catches a head-on beam at 3 m.
        # y from the loft's own front at z 1.175 (−0.157): 17 mm proud, 26 mm buried
        # — the burial is the Trunk/Pocket preflight bottleneck, kept over 1.5 voxels.
        Box("Pocket", (0.092, -0.148, 1.175), (0.040, 0.026, 0.030)),
    ]

    # Mirrored, not modelled twice. s = -1 is the figure's right.
    for s in (-1.0, +1.0):
        tag = "L" if s > 0 else "R"
        parts += [
            # The shoulder ball welds sleeve to yoke, swallows the capped deltoid
            # stub, and sits AT the arm's pivot, so rotating about it barely
            # stretches the skin (the carry-era lesson). Deeper than wide (ry
            # 0.100): the stub's front/back corners are corners, not a sphere.
            Ellipsoid(f"Shoulder_{tag}", (s * SHOULDER_X, 0.004, SHOULDER_Z),
                      (SHOULDER_R, 0.100, SHOULDER_R)),
            # The sleeve: hangs from the shoulder, leaning 22 mm forward, ending
            # at the cuff the harvested hand tucks into.
            Shaft(f"Arm_{tag}", s * ARM_X, ARM_Y,
                  z_top=SHOULDER_Z, r_top=ARM_R_TOP,
                  z_bottom=ARM_Z_BOTTOM, r_bottom=ARM_R_BOTTOM),
            Ellipsoid(f"Cuff_{tag}", (s * CUFF_C[0], CUFF_C[1], CUFF_C[2]), CUFF_R),
            Ellipsoid(f"BootTop_{tag}",
                      (s * BOOT_TOP_C[0], BOOT_TOP_C[1], BOOT_TOP_C[2]), BOOT_TOP_R),
            Shaft(f"Boot_{tag}", s * LEG_X, 0.042,
                  z_top=BOOT_Z_TOP, r_top=BOOT_R_TOP,
                  z_bottom=BOOT_Z_BOTTOM, r_bottom=BOOT_R_BOTTOM),
            # Toe forward: the toes are what tell a viewer which way a distant racer
            # is facing. The straightened human foot is INSIDE this pad.
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

    # The folds go HERE and nowhere else. Before the remesh they are placement-table
    # noise the voxel grid can't record; before the smoothing they'd be eroded with
    # the staircase; after the decimation there are ~25 mm edges left and a 15 mm
    # crease drawn at that pitch is jagged. Between smooth and decimate the surface
    # is clean AND dense, and the collapse metric then preserves what this pass adds.
    sculpt_folds(body)
    _stage("folds", body)

    decimate = body.modifiers.new("Budget", "DECIMATE")
    decimate.decimate_type = "COLLAPSE"
    decimate.ratio = DECIMATE_RATIO
    decimate.use_collapse_triangulate = True
    _apply_modifier(body, decimate)
    _stage(f"decimate_{DECIMATE_RATIO}", body)

    # 42°, not the old 180: the figure HAS hard edges now — the fold ridges
    # sculpt_folds just authored — and the whole point of cutting them into a matte
    # 0.13-albedo jacket is the shading discontinuity, which smooth-everything normals
    # would average away. 42° is above the ~7–15° a 25 mm decimated edge subtends on
    # this body's smooth curvature (so no staircase faceting comes back) and below
    # what a 10–14 mm crease ridge folds through.
    blendkit.shade_smooth(body, angle_degrees=42.0)
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


def assign_materials(body: bpy.types.Object, fit: Fit) -> None:
    """Classifies every polygon of the welded body into one of the five slots.

    Painting by REGION rather than by part, because after the remesh no face knows
    which primitive it came from. The regions are re-derived from the same placement
    constants the parts were built from, carried through the fit, so the paint cannot
    drift from the geometry. Order matters: the first rule that claims a face keeps
    it (lamp before void before accent, else the strap band would recolour the lamp).

    Every slot must land on at least one face — a slot with zero faces means a
    threshold and the geometry have come apart, and in Unity it would be an invisible
    contract: the material imports, nothing wears it, and the first person to "clean
    it up" deletes the tint slot the whole 20-runner accent system targets.
    """
    for spec in MATERIAL_SPECS:
        body.data.materials.append(blendkit.make_material(spec))

    slot = {spec.name: i for i, spec in enumerate(MATERIAL_SPECS)}
    counts = {spec.name: 0 for spec in MATERIAL_SPECS}

    # Region thresholds, in final metres. Round one painted the void 1.50–1.635 and
    # ±75 mm wide, and the black spilled over the brow and cheek — a smashed face, not
    # a hollow. The void now stops at the brim's underside, inside its shadow, and only
    # claims FORWARD-FACING triangles (normal test below), so its ragged per-triangle
    # border cannot wrap around the head's side.
    #
    # THE OWL-EYE RULES (prodship_03m.png, the in-game truth this pass answers): the
    # strap band used to be a full amber ring at brow height, so a 3 m beam lit its
    # two front arcs either side of the dark lamp box and the runner had EYES. Now
    # (a) the lamp region tracks the housing to its off-centre perch above the brim,
    # (b) the band keeps Runner_Accent only where it faces clearly BACKWARD
    #     (normal.y > 0.3) and is near-black gear for its front arc — strap ends
    #     darkened, tint still readable from behind,
    # (c) nothing warm or specular is painted anywhere inside the hood at eye height:
    #     the void window rises to the brim's underside where the old lamp box sat.
    lamp_lo, lamp_hi = fit.z(LAMP_C[2] - LAMP_HALF[2] - 0.006), fit.z(1.694)
    lamp_x_lo, lamp_x_hi = -fit.d(0.004), fit.d(0.064)
    lamp_y = -fit.d(0.120)
    # The face window: the harvested nose/mouth/chin sliver that emerges through the
    # hood opening (HOOD_C note), plus the hood's own front rim around it — all of it
    # goes to black, which is what turns a real face into the no-face. Round 1's
    # render showed the window as a torn bib with a pale chin under it: the z floor
    # sat above the jaw and the facing gate (−0.40) let side-leaning nose/cheek
    # polys keep the jacket colour inside the hole. Floor dropped to the collar top,
    # gate relaxed to −0.18 — the hood's x-limit is what keeps the sides clean.
    void_lo, void_hi = fit.z(1.440), fit.z(1.622)
    void_x, void_y = fit.d(0.070), -fit.d(0.082)
    band_lo, band_hi = fit.z(1.606), fit.z(1.642)
    armband_x = fit.d(0.198)
    armband_lo, armband_hi = fit.z(ARMBAND_C[2] - 0.035), fit.z(ARMBAND_C[2] + 0.042)
    # The glove region swallows the whole harvested hand shell plus the cuff's mouth,
    # so the hand IS the glove — near-black gear with fingers, task #81's read.
    glove_c = (fit.d(0.272), -fit.d(0.045), fit.z(0.732))
    glove_r = (fit.d(0.075), fit.d(0.115), fit.d(0.115))
    roll_x, roll_lo, roll_hi = -fit.d(0.215), fit.z(0.850), fit.z(0.898)
    boot_top = fit.z(0.400)
    trouser_top = fit.z(HEM_BOTTOM_Z - 0.005)

    def in_glove(p: Vector) -> bool:
        return (((abs(p.x) - glove_c[0]) / glove_r[0]) ** 2
                + ((p.y - glove_c[1]) / glove_r[1]) ** 2
                + ((p.z - glove_c[2]) / glove_r[2]) ** 2) < 1.0

    for poly in body.data.polygons:
        p = body.matrix_world @ poly.center
        facing = poly.normal.y            # < 0 means the triangle faces forward
        if (lamp_lo < p.z < lamp_hi and lamp_x_lo < p.x < lamp_x_hi
                and p.y < lamp_y):
            name = MAT_GEAR.name          # headlamp housing, off-centre above the brim
        elif (void_lo < p.z < void_hi and abs(p.x) < void_x and p.y < void_y
              and facing < -0.18):
            name = MAT_VOID.name          # the face that is not there
        elif band_lo < p.z < band_hi:
            # Headlamp strap: tint accent ONLY on the clearly-rear arc; front and
            # sides are near-black gear so the beam finds no warm point at eye
            # height. 0.55, not 0.3 — at 0.3 a handful of side-wrapping triangles
            # still glinted amber at brow height in the 3 m three-quarter beam
            # (round 2), and one glint at that height is half an owl eye.
            name = MAT_ACCENT.name if facing > 0.55 else MAT_GEAR.name
        elif armband_lo < p.z < armband_hi and p.x > armband_x:
            name = MAT_ACCENT.name        # left-sleeve armband
        elif p.x < roll_x and roll_lo < p.z < roll_hi:
            name = MAT_JACKET.name        # right sleeve's rolled cuff stays fabric
        elif in_glove(p):
            name = MAT_GEAR.name          # glove + cuff
        elif p.z < boot_top:
            name = MAT_GEAR.name          # boots and feet
        elif p.z < trouser_top:
            name = MAT_TROUSERS.name      # trousers between boot cuff and hem
        else:
            name = MAT_JACKET.name
        poly.material_index = slot[name]
        counts[name] += 1

    for spec in MATERIAL_SPECS:
        print(f"MATERIAL {spec.name:15s} faces={counts[spec.name]:5d} "
              f"base=({spec.color[0]:.3f},{spec.color[1]:.3f},{spec.color[2]:.3f}) "
              f"roughness={spec.roughness:.2f} metallic=0.00")

    empty = [name for name, n in counts.items() if n == 0]
    if empty:
        blendkit.fail(
            "these material slots claimed no faces: " + ", ".join(empty) + ". A region "
            "threshold and the placement table have come apart — Unity would import an "
            "unused material, and Runner_Accent with no faces is the per-runner tint "
            "system silently gone.")


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


COVER_TOLERANCE = 0.006
"""How far (metres) a body vertex may poke past its covering garment before the
coverage check fails. Not slack — arithmetic: a poke under one 9 mm voxel cannot
survive the remesh as a separate surface (the level set unions it into the garment
and the smoothing rounds the bump), so a sub-voxel poke is a bulge in the cloth,
while anything bigger is skin showing through the jacket."""


def verify_body_covered(parts: list[Part]) -> None:
    """The garment must actually DRESS the harvested body — preflight, on the table.

    The primitive figure could not fail this way: its trunk loft WAS its torso. Now
    the loft is a jacket around a measured body, and every ring is torso-profile plus
    clearance — arithmetic this check refuses to take on faith, for the same reason
    the overlap graph exists. Every vertex of the body's covered bands must be inside
    the union of its covering parts (within COVER_TOLERANCE): the torso inside
    jacket/collar/yoke/balls, the nape inside collar/skirt/hood, the skull inside the
    hood, the feet inside the boots. The face window and the trouser band are the two
    deliberate exposures and are excluded by construction. A failure here is the
    'athletic nude human' identity failure, caught in one second instead of a render.
    """
    body = next((p for p in parts if isinstance(p, MeshPart)), None)
    if body is None:
        blendkit.fail("no MeshPart in the placement table — the harvested body is "
                      "missing from build_parts().")
    by_name = {p.name: p for p in parts}

    def group(*names: str) -> list[Part]:
        out = []
        for p in parts:
            if p.name in names or any(p.name.startswith(n + "_") for n in names):
                out.append(p)
        return out

    torso_cover = group("Trunk", "Collar", "HoodSkirt", "Hood", "BackYoke",
                        "Shoulder", "Arm")
    nape_cover = group("Collar", "HoodSkirt", "Hood", "Trunk")
    hood_cover = group("Hood", "Brim")
    feet_cover = group("Boot", "Foot", "BootTop")

    bands = (
        ("torso", lambda co: HEM_BOTTOM_Z + 0.015 <= co.z <= 1.40, torso_cover),
        ("nape", lambda co: 1.40 < co.z <= 1.52 and co.y > 0.005, nape_cover),
        ("skull", lambda co: 1.52 < co.z < 1.63 and co.y > -0.020, hood_cover),
        ("crown", lambda co: co.z >= 1.63, hood_cover),
        ("feet", lambda co: co.z < ANKLE_Z - 0.005, feet_cover),
    )

    for label, predicate, cover in bands:
        samples = [co for co in body.surface() if predicate(co)]
        if not samples:
            blendkit.fail(f"coverage band '{label}' selected no body vertices — the "
                          "band predicate and the body have come apart.")
        worst = 0.0
        worst_at = None
        escaped = 0
        for co in samples:
            if any(p.contains(co) for p in cover):
                continue
            poke = min(_poke_distance(p, co) for p in cover)
            escaped += 1
            if poke > worst:
                worst, worst_at = poke, co
        names = ",".join(p.name for p in cover)
        print(f"BODY_COVER {label:5s} samples={len(samples)} escaped={escaped} "
              f"worst_poke={worst * 1000.0:.1f}mm tol={COVER_TOLERANCE * 1000.0:.0f}mm "
              f"under [{names}]")
        if worst > COVER_TOLERANCE:
            blendkit.fail(
                f"the body escapes its garment in the '{label}' band: a vertex at "
                f"({worst_at.x:+.3f},{worst_at.y:+.3f},{worst_at.z:+.3f}) pokes "
                f"{worst * 1000.0:.1f} mm out of [{names}] against the "
                f"{COVER_TOLERANCE * 1000.0:.0f} mm voxel-absorption tolerance. That "
                "is skin through the jacket — the identity brief's failure case. "
                "Widen the ring/part it should be under.")
    # the one strict absolute: nothing of the body may hang below the boot soles,
    # because the boot is what stands on z = 0 after the drop.
    body_floor = min(co.z for co in body.surface())
    boot_floor = min(by_name["Foot_L"].centre[2] - by_name["Foot_L"].radii[2],
                     by_name["Foot_R"].centre[2] - by_name["Foot_R"].radii[2])
    print(f"BODY_COVER soles body_floor={body_floor * 1000.0:+.1f}mm "
          f"boot_floor={boot_floor * 1000.0:+.1f}mm")
    if body_floor < boot_floor + 0.004:
        blendkit.fail(
            f"the body's lowest skin ({body_floor * 1000.0:+.1f} mm) reaches within "
            f"4 mm of the boot sole ({boot_floor * 1000.0:+.1f} mm) — the figure "
            "would stand on its feet instead of its boots.")


def _poke_distance(part: Part, co: Vector) -> float:
    """How far ``co`` sits OUTSIDE ``part`` — 0 when inside; used only to grade
    coverage escapes, so a cheap conservative metric per part class is enough."""
    if part.contains(co):
        return 0.0
    if isinstance(part, Ellipsoid):
        c, r = Vector(part.centre), part.radii
        q = math.sqrt(part.quadric(co))
        reach = (co - c).length
        return reach * (1.0 - 1.0 / q) if q > 1.0 else 0.0
    if isinstance(part, Loft):
        rx, ry, yc = part._profile(co.z)
        frac = math.sqrt(max(part._frac(co), 1e-12))
        radial = (frac - 1.0) * min(rx, ry)
        below = part.rings[0][0] - co.z
        above = co.z - part.rings[-1][0]
        return max(radial if frac > 1.0 else 0.0, below, above, 0.0)
    if isinstance(part, Shaft):
        top, bottom = part._ends()
        if part.z_bottom <= co.z <= part.z_top:
            return max(0.0, math.hypot(co.x - part.x, co.y - part.y)
                       - part.radius_at(co.z))
        end = top if co.z > part.z_top else bottom
        return _poke_distance(end, co)
    if isinstance(part, Box):
        c, h = part.centre, part.half
        return max(max(abs(co[i] - c[i]) - h[i] for i in range(3)), 0.0)
    return 0.0


def verify_shells(obj: bpy.types.Object, expect: int = 3) -> list[set[int]]:
    """The figure must be EXACTLY ``expect`` closed shells: the welded suit, then one
    watertight hand per side.

    The old figure was one shell and the check demanded one; this figure is one WELD
    plus two harvested hands that are deliberately not welded (a 9 mm voxel would
    re-mitten the fingers — ``harvest_hands``), so the honest invariant is the exact
    census: 3 shells, every one of them closed, the suit overwhelmingly the biggest.
    Anything else is the old failure wearing a new count — a fourth shell is a piece
    of the union that never joined (a head floating over a body in Unity), and a
    boundary edge is a leak. Returns the shells' vertex-index sets, biggest first,
    because the limb and hand checks below reason about specific shells.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    seen: set[int] = set()
    shells: list[set[int]] = []
    for vert in bm.verts:
        if vert.index in seen:
            continue
        seen.add(vert.index)
        stack = [vert]
        members = {vert.index}
        while stack:
            v = stack.pop()
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in seen:
                    seen.add(w.index)
                    members.add(w.index)
                    stack.append(w)
        shells.append(members)
    shells.sort(key=len, reverse=True)

    boundary = sum(1 for e in bm.edges if len(e.link_faces) < 2)
    nonmanifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.free()

    print(f"SHELL_COUNT shells={len(shells)} "
          f"verts={','.join(str(len(s)) for s in shells[:6])}"
          f"{'...' if len(shells) > 6 else ''} boundary_edges={boundary} "
          f"nonmanifold_edges={nonmanifold} expected={expect} (suit + 2 hands)")

    if len(shells) != expect:
        blendkit.fail(
            f"the figure is {len(shells)} shells, not {expect} "
            f"(vertex counts {[len(s) for s in shells[:8]]}). Three are legitimate — "
            "the welded suit and the two harvested hands — and nothing else is: an "
            "extra shell is a part the union never joined (in Unity, a piece hanging "
            "in the air beside a running body), and a missing one is a hand that got "
            "swallowed. Deepen the overlap in build_parts(), or check harvest_hands.")

    if boundary:
        blendkit.fail(
            f"{boundary} boundary edges — the figure is not watertight. The remesh "
            "returns closed surfaces and both hand cuts are capped, so holes mean a "
            "cap failed or a primitive was open before the join.")

    if len(shells) >= 3 and len(shells[1]) > len(shells[0]) // 3:
        blendkit.fail(
            f"the second-biggest shell has {len(shells[1])} vertices against the "
            f"suit's {len(shells[0])} — that is not a hand, that is the weld split "
            "in two. A hand is a few hundred cage vertices.")
    return shells


def verify_hand_shells(obj: bpy.types.Object, shells: list[set[int]],
                       fit: Fit) -> None:
    """Each hand shell must be TUCKED, CLEAR, and never coplanar with the suit.

    Three measured invariants, replacing what "one shell" used to guarantee for the
    mittens (which, being welded, could not float, gape or z-fight — the hands can,
    so the checks move to where the risk moved):

    * **tucked** — the CUT RING (the recorded ``_HAND_CAP_POINTS``, carried through
      the fit) is buried ≥ 3 mm inside the cuff, so the severed wrist and its cap
      never surface. Exactly the ring: the glove's own flare beside it (the thumb
      base) is visible surface and belongs outside — an earlier cut of this check
      banded "the top 20 mm" and correctly refused a cuff that no wearable cuff
      could satisfy;
    * **clear** — the finger region sits ≥ 2.5 mm OUTSIDE the suit everywhere, so no
      fingertip is buried in a thigh or a cuff across any pose the rigid bind allows;
    * **no z-fight** — no hand face is both within 2.5 mm of the suit surface and
      near-parallel to it (|n·n| > 0.95): crossing surfaces are a visible seam by
      design, but parallel-and-touching surfaces shimmer.
    """
    mesh = obj.data
    suit = shells[0]
    verts = [v.co.copy() for v in mesh.vertices]
    suit_polys = [tuple(p.vertices) for p in mesh.polygons
                  if all(i in suit for i in p.vertices)]
    bvh = BVHTree.FromPolygons([tuple(co) for co in verts], suit_polys)

    def signed(co: Vector) -> float:
        near, normal, _i, dist = bvh.find_nearest(co)
        if near is None:
            return math.inf
        return -dist if normal.dot(co - near) < 0.0 else dist

    for shell in shells[1:]:
        cos = [verts[i] for i in shell]
        cen = sum(cos, Vector()) / len(cos)
        side = "left" if cen.x > 0.0 else "right"
        zlo = min(co.z for co in cos)
        zhi = max(co.z for co in cos)

        ring = _HAND_CAP_POINTS.get(side)
        if not ring:
            blendkit.fail(f"harvest_hands recorded no cut ring for the {side} hand — "
                          "the tuck check has nothing to measure.")
        cap_pts = [Vector((fit.d(p.x), fit.d(p.y), fit.z(p.z))) for p in ring]
        cap = [signed(co) for co in cap_pts]
        fingers = [signed(co) for co in cos if co.z < zlo + 0.40 * (zhi - zlo)]
        tuck = -max(cap)          # worst (shallowest) burial of the cut ring
        clear = min(fingers)      # worst (closest) finger approach to the suit
        worst_cap = cap_pts[cap.index(max(cap))]
        near_pt = bvh.find_nearest(worst_cap)[0]
        print(f"HAND_TUCK {side:5s} ring={len(cap_pts)}v worst_cap=({worst_cap.x:+.3f},"
              f"{worst_cap.y:+.3f},{worst_cap.z:.3f}) nearest_suit="
              f"({near_pt.x:+.3f},{near_pt.y:+.3f},{near_pt.z:.3f})")

        zfights = 0
        zfight_at = None
        for poly in mesh.polygons:
            if poly.vertices[0] not in shell:
                continue
            centre = Vector(poly.center)
            near, normal, _i, dist = bvh.find_nearest(centre)
            if near is not None and dist < 0.0025 \
                    and abs(normal.dot(poly.normal)) > 0.95:
                zfights += 1
                zfight_at = centre
        where = (f" at=({zfight_at.x:+.3f},{zfight_at.y:+.3f},{zfight_at.z:.3f})"
                 if zfight_at is not None else "")
        print(f"HAND_SHELL {side:5s} verts={len(shell)} z={zlo:.3f}..{zhi:.3f} "
              f"tuck_depth={tuck * 1000.0:+.1f}mm finger_clear={clear * 1000.0:+.1f}mm "
              f"zfight_faces={zfights}{where}")

        if tuck < 0.003:
            blendkit.fail(
                f"the {side} hand's cut ring is buried only {tuck * 1000.0:.1f} mm "
                "inside the cuff (3 mm minimum). The severed wrist and its cap would "
                "surface through the sleeve — deepen CUFF_R or move the cut back "
                "into the wrist's narrow.")
        if clear < 0.0025:
            blendkit.fail(
                f"the {side} hand's fingers come within {clear * 1000.0:.1f} mm of the "
                "suit (2.5 mm minimum). A finger inside the trouser or cuff surface "
                "is a finger the beam amputates — move HAND_C or shrink the cuff.")
        if zfights:
            blendkit.fail(
                f"{zfights} faces of the {side} hand lie within 2.5 mm of the suit "
                "surface while near-parallel to it. That is a z-fight at render "
                "distance — the glove and the cuff must cross cleanly, not kiss.")


def verify_limbs_hang_free(obj: bpy.types.Object, suit: set[int], ankle_z: float,
                           crotch_z: float, hip_z: float, leg_part_z: float,
                           armpit_z: float, ball_c: tuple[float, float, float],
                           ball_r: tuple[float, float, float]) -> None:
    """A limb may reach its opposite number only through the JOINT it hangs from.

    ``suit`` is the welded shell's vertex-index set (``verify_shells``): every walk
    below runs on the SUIT, because a hand shell is disconnected by construction and
    seeding a connectivity test on one would prove nothing about anything — the exact
    way this check would have gone silently vacuous after the body swap.

    A third check on the weld, and it is here because a walk cycle found the first half of
    it. ``verify_shells`` wants the suit to be one connected surface and it is; this
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
        pool = [v for v in bm.verts if v.co.z < ceiling and v.index in suit]
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
        band = [abs(v.co.x) for v in bm.verts
                if lo <= v.co.z < hi and v.index in suit]
        profile.append((0.5 * (lo + hi), 2.0 * min(band) if band else math.inf))
    print("LEG_SLOT " + " ".join(f"{z:.2f}m:{gap * 1000.0:.0f}mm" for z, gap in profile))

    welded = [z for z, gap in profile if gap < VOXEL_SIZE * 2.0]
    # Never welded is the BEST case, not a failure: on the worker the pelvis mass is
    # the jacket's skirt, which overhangs the thighs instead of joining them, so the
    # whole profile can honestly come back open. The mannequin's crotch always closed
    # just under the belly, which is why the old else-branch never fired.
    parted_to = min(welded) if welded else ankle_z
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
    # unable to reach the body's midline at all. The cut is the DESIGNED joint's own
    # ellipsoid (the ball is deeper than it is wide since the body swap), dilated 15 % —
    # a spherical cut on an ellipsoid ball leaves weld surface outside the cut and
    # flags the joint itself as a bridge.
    rx, ry, rz = (r * 1.15 for r in ball_r)
    for sign, side in ((1.0, "left"), (-1.0, "right")):
        bc = Vector((sign * ball_c[0], ball_c[1], ball_c[2]))

        def outside_ball(w) -> bool:
            d = w.co - bc
            return (d.x / rx) ** 2 + (d.y / ry) ** 2 + (d.z / rz) ** 2 > 1.0

        # Seed on the SUIT's own sleeve — the outermost suit vertex below the armpit
        # — never on a hand shell, which is disconnected and would pass vacuously.
        hand = max((v for v in bm.verts
                    if v.co.z < armpit_z and v.co.x * sign > 0.0
                    and v.index in suit),
                   key=lambda v: v.co.x * sign)
        reach = walk(hand, outside_ball)
        onto_body = [bm.verts[i].co.x * sign for i in reach if bm.verts[i].co.x * sign < 0.02]
        print(f"LIMB_FREE {side}_arm bridged={'YES' if onto_body else 'no'} "
              f"reachable_without_shoulder={len(reach)}v "
              f"shoulder_cut=({rx * 1000.0:.0f},{ry * 1000.0:.0f},{rz * 1000.0:.0f})mm")
        if onto_body:
            # name the crossing before failing: BFS again with parent tracking and
            # trace the first far-side vertex back to the seed — the printed route
            # IS the bridge, so the fix can aim at a place instead of a symptom.
            parent = {hand.index: None}
            queue = [hand]
            first_far = None
            while queue and first_far is None:
                nxt = []
                for v in queue:
                    for e in v.link_edges:
                        w = e.other_vert(v)
                        if w.index in parent or not outside_ball(w):
                            continue
                        parent[w.index] = v.index
                        if w.co.x * sign < 0.02:
                            first_far = w
                            break
                        nxt.append(w)
                    if first_far is not None:
                        break
                queue = nxt
            if first_far is not None:
                path = []
                i = first_far.index
                while i is not None:
                    path.append(bm.verts[i].co.copy())
                    i = parent[i]
                pts = " ".join(f"({c.x:+.2f},{c.y:+.2f},{c.z:.2f})"
                               for c in path[::max(1, len(path) // 14)])
                print(f"LIMB_BRIDGE {side}_arm shortest_route {len(path)}v: {pts}")
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
# SEVENTEEN BONES, AND WHY THIRTEEN WAS WRONG
# -------------------------------------------
# This comment used to argue for 13, and the argument was: *"a bone earns its place only
# if the surface it moves has somewhere to bend"* — a reading of the MESH, back when every
# pose in this file was authored by hand a few degrees at a time. Four of those bones are
# here because that premise stopped being true. The clips are professional mocap now
# (``MIXAMO_MAP``), and mocap is not a set of angles you can spread over whatever bones
# happen to exist: it is a recording of a skeleton, and the parts of it you have no bone
# for are not attenuated, they are DELETED.
#
# The old map said so in its own docstring — Neck, both ForeArms and every finger "are
# DROPPED … which is acceptable because this rig has no bone to carry it onto". It was not
# acceptable, and the round-3 renders are the receipt: a running arm in mocap is an upper
# arm swung BACK with the forearm folded ~90° across it, and if you keep only the shoulder
# the surviving shoulder→hand line projects straight out SIDEWAYS. Both arms held out level
# with the shoulders, zero elbow, over a torso folded at one hinge — a scarecrow. Measured
# on the committed rig: 84.6° of arm abduction at the peak of Run, 605 mm of the hand's
# travel side-to-side against 341 mm fore-and-aft.
#
#   Hips                       the pelvis, 20 mm long, so ``torso()`` has something to
#                              aim (see SPINE_JOIN_Z).
#   Spine, Chest               TWO now, split at ``CHEST_JOIN_Z``. Not because the jacket
#                              grew vertebrae — it did not — but because the source has
#                              three spine segments and telescoping their accumulated lean
#                              onto one bone bends the WHOLE torso by the top segment's
#                              angle. That is the 45° plank. Two bones let the fold
#                              distribute, and the split is placed where the source's own
#                              chest segment begins so each bone carries one thing.
#   Neck                       the head is no longer welded to the chest. Mixamo Neck
#                              carries 20° of its own in Run and it used to be discarded;
#                              with it the head stays level over a leaning torso, which is
#                              what a runner's head does and what a plank's does not.
#   Head                       from ``HEAD_BASE_Z`` (measured at the jaw) to the crown.
#   Left/RightUpperArm         }  shoulder→elbow and elbow→palm. **This is the fix.** The
#   Left/RightLowerArm         }  elbow is at ``ELBOW_Z``, measured off the harvested
#                              body's olecranon and sitting ON the shoulder→hand line, so
#                              the arm's total reach — and therefore ``GUN_MOUNT_RATIO``
#                              and ``verify_gun_pose`` — is exactly what it was. The
#                              LowerArm is also what the gun hangs off now: mounting on
#                              the UpperArm after the split would put it at the elbow.
#   Left/RightUpperLeg         }  two bones per leg is what the player's two-bone IK needs
#   Left/RightLowerLeg         }  (``gen_player_model._solve_leg``), and that solver is the
#                              difference between running and sliding: it places the
#                              planted foot by POSITION so the foot travels at a constant
#                              speed, which no amount of angle-authoring achieves. The knee
#                              sits at the measured knee centre (``KNEE_FRACTION``).
#   Left/RightFoot, …Toes      the foot pad is 324 mm long — a quarter of the figure's
#                              leg. Rigid, it is a plank, and toe-off pivots the whole
#                              body about the tip. The toe joint is also what makes
#                              ``FOOT_ROLL`` mean the same thing here as it does on the
#                              player (see ``retarget_gait_solver``).
#
# WHAT IS STILL NOT HERE: hands and fingers. The two glove shells are rigid, weighted 100 %
# to their LowerArm, and their relaxed curl is baked at harvest time
# (``FINGER_CURL_DEG``). That one really is a reading of the mesh — a 9 mm voxel turns
# fingers into a mitten, the shells exist to escape that, and nothing at §03's ten metres
# of dark corridor resolves a knuckle.
#
# Every name is one of Unity's humanoid spellings, so this rig is a strict SUBSET of
# Player.fbx's vocabulary and nothing downstream has to learn a second one. It is still
# imported **Generic**, not Humanoid, and the reason has narrowed rather than gone: the
# humanoid skeleton also requires a Hand on each side, and this figure's hand is a glove
# shell rather than a joint. ``Monster.fbx`` is Generic for the same class of reason.

RIG_NAME = "Runner_Rig"
"""The armature object, and therefore the FBX's root Model node. Matches
``Player_Rig`` / ``Monster_Rig``. **This renames the FBX root**, which is what Unity
derives a model prefab's root fileID from — see the report in ``main``."""

KNEE_FRACTION = 0.433
"""Where the knee sits between hip and ankle — measured off the harvested leg (knee
centre z 0.492 between hip 0.780 and ankle 0.115), not the old kneeless column's
midpoint. The mesh has a real knee now, and a knee bone anywhere else is a crease
that folds the calf instead of the joint. Thigh 0.288 / shank 0.377: unequal, which
``_solve_leg`` handles the same way it handles the player's own unequal pair."""

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
    """Joint positions in FINAL metres, derived from the placement table and the fit.

    The leg chain is no longer a plumb line: the harvested leg splays hip → ankle
    (x 0.092 → 0.172) and drops its ankle 56 mm behind the body line, so the chain
    carries per-joint x and y now. The retarget transfers mocap as rest-relative
    deltas, so a slanted rest leg swings exactly as well as a vertical one did.
    """

    hip_z: float
    knee_z: float
    ankle_z: float
    hip_x: float
    hip_y: float
    knee_x: float
    knee_y: float
    leg_x: float
    ankle_y: float
    thigh: float
    shank: float
    spine_z: float
    chest_z: float
    neck_z: float
    head_z: float
    crown_z: float
    shoulder_x: float
    shoulder_z: float
    elbow_x: float
    elbow_z: float
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

    Every leg landmark is now a MEASUREMENT of the harvested body rather than a
    property of a shaft primitive: the hip is the groin-crease/femoral-head level
    (LEG_Z_TOP's note — this base mesh is short-legged on purpose, crotch at 0.41 of
    height, so the leg stays inside the pendulum band the meta's frame ranges
    assume), the knee is the measured knee centre, and the ankle is the measured
    malleolus — 56 mm behind the body line, where a real ankle is. The sole contacts
    are still taken off the WELDED mesh (``measure_sole``), because what stands on
    the floor is the boot after the voxel grid, not the anatomy inside it.
    """
    hip_z = fit.z(LEG_Z_TOP)
    ankle_z = fit.z(ANKLE_Z)
    ankle_y = fit.d(ANKLE_Y)
    knee_z = hip_z - (hip_z - ankle_z) * KNEE_FRACTION

    ball_y, ball_sole = _sole(fit, BALL_T)
    tip_y, _tip_sole = _sole(fit, TIP_T)

    ball = (ball_y, ball_sole + BALL_LIFT)
    tip = (tip_y, ball[1] - TOE_DROOP)

    (heel_y, heel_z), (mid_y, mid_z), (toe_y, toe_z) = measure_sole(body, fit, ankle_z)
    print(f"SOLE_MEASURED heel=({heel_y:+.4f},{heel_z:+.4f}) "
          f"ball=({mid_y:+.4f},{mid_z:+.4f}) toe=({toe_y:+.4f},{toe_z:+.4f}) "
          f"(off the welded mesh, not off the ideal pad)")

    # Heel behind the ball joint belongs to Foot; everything ahead of it rolls with
    # Toes. Contact offsets are FROM the Foot bone's head, which now sits at the
    # anatomical ankle (leg_x, ankle_y, ankle_z) rather than on the y = 0 plane.
    contacts = (
        ("Foot", (0.0, heel_y - ankle_y, heel_z - ankle_z)),
        ("Toes", (0.0, mid_y - ball[0], mid_z - ball[1])),
        ("Toes", (0.0, toe_y - ball[0], toe_z - ball[1])),
    )

    return Skeleton(
        hip_z=hip_z, knee_z=knee_z, ankle_z=ankle_z,
        hip_x=fit.d(HIP_X), hip_y=fit.d(HIP_Y),
        knee_x=fit.d(KNEE_X), knee_y=fit.d(KNEE_Y),
        leg_x=fit.d(LEG_X), ankle_y=ankle_y,
        thigh=hip_z - knee_z, shank=knee_z - ankle_z,
        spine_z=fit.z(SPINE_JOIN_Z),
        chest_z=fit.z(CHEST_JOIN_Z),
        neck_z=fit.z(NECK_BASE_Z),
        head_z=fit.z(HEAD_BASE_Z),
        crown_z=fit.z(HOOD_C[2] + HOOD_R[2] * 0.8),
        shoulder_x=fit.d(SHOULDER_X), shoulder_z=fit.z(SHOULDER_Z),
        # ON the shoulder→hand line, at the measured ELBOW_FRACTION — so splitting the
        # arm cannot change its reach, and the gun mount solve is untouched.
        elbow_x=fit.d(SHOULDER_X + ELBOW_FRACTION * (HAND_C[0] - SHOULDER_X)),
        elbow_z=fit.z(ELBOW_Z),
        hand_x=fit.d(HAND_C[0]), hand_z=fit.z(HAND_C[2]),
        ball=ball, toe_tip=tip, contacts=contacts)


def bone_specs(sk: Skeleton) -> list[BoneSpec]:
    """The 17 bones, in final metres. See the essay above for the count.

    Every name that existed at 13 bones is byte-identical here and in the same place;
    the four new ones (Chest, Neck, Left/RightLowerArm) are inserted into chains rather
    than bolted onto them, so ``PlayerRigBones.Find`` keeps resolving every string the
    C# side holds. The arms now hang off ``Chest`` rather than ``Spine`` — a shoulder
    belongs on the ribcage, and hanging it on the lumbar would have swung both arms
    with a bone that is meant to describe a runner's crouch.

    The old KNEE_BIAS_Y −0.006 nudge is gone: the measured knee already sits ~22 mm
    forward of the hip–ankle line (the anatomical bend bias), so the IK's bend
    direction is set by the body instead of by an authored fudge."""
    specs = [
        BoneSpec("Hips", (0.0, 0.0, sk.hip_z), (0.0, 0.0, sk.spine_z)),
        BoneSpec("Spine", (0.0, 0.0, sk.spine_z), (0.0, 0.0, sk.chest_z), "Hips", True),
        BoneSpec("Chest", (0.0, 0.0, sk.chest_z), (0.0, 0.0, sk.neck_z), "Spine", True),
        BoneSpec("Neck", (0.0, 0.0, sk.neck_z), (0.0, 0.0, sk.head_z), "Chest", True),
        BoneSpec("Head", (0.0, 0.0, sk.head_z), (0.0, 0.0, sk.crown_z), "Neck", True),
    ]
    for side, s in ((1, "Left"), (-1, "Right")):
        x = float(side)
        specs += [
            BoneSpec(f"{s}UpperArm", (x * sk.shoulder_x, 0.0, sk.shoulder_z),
                     (x * sk.elbow_x, 0.0, sk.elbow_z), "Chest"),
            BoneSpec(f"{s}LowerArm", (x * sk.elbow_x, 0.0, sk.elbow_z),
                     (x * sk.hand_x, 0.0, sk.hand_z), f"{s}UpperArm", True),

            BoneSpec(f"{s}UpperLeg", (x * sk.hip_x, sk.hip_y, sk.hip_z),
                     (x * sk.knee_x, sk.knee_y, sk.knee_z), "Hips"),
            BoneSpec(f"{s}LowerLeg", (x * sk.knee_x, sk.knee_y, sk.knee_z),
                     (x * sk.leg_x, sk.ankle_y, sk.ankle_z), f"{s}UpperLeg", True),
            BoneSpec(f"{s}Foot", (x * sk.leg_x, sk.ankle_y, sk.ankle_z),
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
    gpm.FOOT_REST = Vector((0.0, sk.ball[0] - sk.ankle_y,
                            sk.ball[1] - sk.ankle_z)).normalized()
    gpm.TOES_REST = Vector((0.0, sk.toe_tip[0] - sk.ball[0],
                            sk.toe_tip[1] - sk.ball[1])).normalized()

    ball_ahead = sk.ankle_y - sk.ball[0]
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
    an arm bone, and the end of a leaf bone is a thing Unity does not know. Blender writes
    a bone as a node with a head transform; the LENGTH lives in the bone's tail, the tail
    of a leaf is not a node, and ``export_fbx`` sets ``add_leaf_bones=False`` precisely so
    that no tip node is invented. So the hand's position is authored here and nowhere
    else, and if it is not carried across it is guessed.

    **THE ARM SPLIT MOVED THIS NUMBER AND THE C# HAD TO MOVE WITH IT.** The mount used to
    be the whole arm's length hung off ``RightUpperArm``; that bone now ends at the ELBOW,
    so the same offset on the same bone would put the revolver in the crook of the runner's
    arm. ``RunnerGun.ArmBone`` is ``RightLowerArm`` now and the offset it needs is the
    FOREARM's length — the LowerArm's own head-to-tail, elbow to palm. Reach is unchanged
    (the elbow sits on the shoulder→hand line by construction) so ``verify_gun_pose`` still
    measures the same silhouette; what changed is which bone the number is measured along,
    and it is a bit over half of what it was.

    **It is carried across as a RATIO, not as metres, and that is deliberate.** This kit
    exports through ``FBX_SCALE_NONE``, which parks the unit conversion on the root node
    rather than in the data, so what "1.0" means inside a bone's local transform after
    Unity's importer has had it is a function of import settings this file cannot see.
    A ratio has no units to be wrong about: the runtime reads a length off the rig it
    actually loaded and multiplies. The denominator is the ``Spine`` → ``Head`` CHAIN —
    the C# walks Head's parents up to Spine and sums each one's ``localPosition``, which
    is Spine + Chest + Neck in whatever units arrived. A chain rather than the single
    ``Head.localPosition`` the 13-bone rig used, because Head's parent is ``Neck`` now and
    that one bone alone is not a unit of anything.

    The pose check is the other half. ``GUN_ARM_SWING`` claims the hand leaves the body's
    outline; this measures whether it does, on the body that was actually built rather
    than on the table that was meant to build it.
    """
    arm_len = math.hypot(sk.hand_x - sk.shoulder_x, sk.hand_z - sk.shoulder_z)
    upper_len = math.hypot(sk.elbow_x - sk.shoulder_x, sk.elbow_z - sk.shoulder_z)
    fore_len = math.hypot(sk.hand_x - sk.elbow_x, sk.hand_z - sk.elbow_z)
    torso_len = sk.neck_z - sk.spine_z
    chain_len = sk.head_z - sk.spine_z      # Spine + Chest + Neck, the C#'s own sum
    ratio = fore_len / chain_len
    print(f"RIG_ARM shoulder=({sk.shoulder_x:.4f},{sk.shoulder_z:.4f})m "
          f"elbow=({sk.elbow_x:.4f},{sk.elbow_z:.4f})m "
          f"hand=({sk.hand_x:.4f},{sk.hand_z:.4f})m arm={arm_len:.4f}m "
          f"(upper={upper_len:.4f} fore={fore_len:.4f}, split "
          f"{upper_len / arm_len:.4f}/{fore_len / arm_len:.4f}) "
          f"torso={torso_len:.4f}m spine_chain={chain_len:.4f}m "
          f"arm/torso={arm_len / torso_len:.4f} {GUN_MOUNT_CONSTANT}={ratio:.4f}")
    if abs((upper_len + fore_len) - arm_len) > 1e-6:
        blendkit.fail(
            f"the elbow is not on the shoulder→hand line: upper {upper_len:.4f} + fore "
            f"{fore_len:.4f} = {upper_len + fore_len:.4f} against an arm of {arm_len:.4f}. "
            "The split is only free of the gun contract while it preserves reach — off the "
            "line it silently shortens the arm, and GUN_MOUNT_RATIO, verify_gun_pose and "
            "the placed hand all key off that length.")
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


def build_rig(sk: Skeleton, body: bpy.types.Object,
              hand_shells: list[set[int]]) -> bpy.types.Object:
    """Builds the armature, binds the body to it and proves every bone reached the mesh.

    Automatic (bone-heat) weights for the SUIT, not hand-painted ones. On the welded
    shell that is the honest choice rather than the lazy one: its vertices come out of
    a voxel remesh and a heavy decimation, so there is no correspondence at all between
    a vertex and the part it used to belong to. Bone heat solves it off the geometry
    that actually exists, and ``verify_skin`` checks the result instead of trusting it.

    The two HAND shells are then overridden to 100 % of their **LowerArm** — the forearm,
    not the upper arm it used to be. That is not a convenience twice over. It is a glove
    hanging on a disconnected shell beside a thigh driven by a different bone, and bone
    heat happily samples both — a thumb two-thirds owned by LeftUpperLeg is a glove that
    tears itself open on the first stride. And it is the elbow: a hand still weighted to
    the upper arm would swing about the SHOULDER while the sleeve it sits in swings about
    the elbow, which is the wrist detaching once per stride.
    """
    specs = bone_specs(sk)
    rig = blendkit.build_armature(RIG_NAME, specs)
    gpm.cache_rig(rig)

    blendkit.bind_skin(body, rig, auto_weights=True)

    for shell in hand_shells:
        indices = sorted(shell)
        cen_x = sum(body.data.vertices[i].co.x for i in indices) / len(indices)
        side = "Left" if cen_x > 0.0 else "Right"
        for vg in body.vertex_groups:
            vg.remove(indices)
        body.vertex_groups[f"{side}LowerArm"].add(indices, 1.0, "REPLACE")
        print(f"HAND_SKIN {side.lower():5s} verts={len(indices)} -> "
              f"{side}LowerArm=1.0 (rigid glove on the FOREARM; heat smear removed)")

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


def arm(side: int, out: float = 4.0, swing: float = 0.0,
        elbow: float | None = None) -> dict:
    """Upper arm AND forearm, because the rig has an elbow now.

    ``elbow`` is the EXTRA forward swing the forearm carries over the upper arm — i.e. the
    joint's flexion, in the same absolute-aim vocabulary as everything else here. It has a
    default rather than being required, and the default is not zero: ``gpm.solve`` gives an
    unaimed bone its parent's absolute rotation, so a caller that named only the upper arm
    would get a forearm collinear with it, which is a straight rod from shoulder to
    fingertips. That is the exact defect the 17-bone rig exists to remove, so the fallback
    is not allowed to reintroduce it — an arm that hangs still carries ELBOW_REST.
    """
    s = "Left" if side > 0 else "Right"
    flex = ELBOW_REST if elbow is None else elbow
    return {f"{s}UpperArm": Aim(hang_dir(side, out, swing)),
            f"{s}LowerArm": Aim(hang_dir(side, out * 0.4, swing + flex))}


ELBOW_REST = 14.0
"""Degrees of elbow flexion a hanging arm carries in the PROCEDURAL clips. A relaxed arm
at the side is not straight — the carrying angle puts it at 5–15° — and more to the point
a rig whose fallback rested the elbow at exactly 0° would photograph the one pose the
retarget path was rebuilt to delete. Swings add to it: see WALK_ELBOW / RUN_ELBOW.

14 rather than the 9 this started at, because 9 leaves no margin: it puts the walk's most
extended frame at 170.7° of anatomical elbow against ``ELBOW_STRAIGHT_DEG``'s 172° ceiling,
and a fallback that passes its own straightness gate by 1.3° is one authored tweak away
from failing it."""

WALK_ELBOW = 26.0
RUN_ELBOW = 74.0
"""Elbow flexion at the extremes of the procedural walk and run. 74° is the mocap's own
number rounded off — ``Running.fbx`` measures 74°–105° of elbow through its cycle (the
anatomical angle, 180° = straight), so a procedural run that bends less than the shallowest
frame of the real thing is not a fallback, it is a different animation. The walk's 26° sits
against the source's 122°–164°, i.e. 16°–58° of flexion, near the top of that band because
a procedural walk has no forearm swing of its own to add to it."""

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

GUN_ELBOW = 55.0
"""Elbow flexion on the arm that holds §01's 총, procedural path. ``Pistol Idle.fbx``
measures 119°–135° of anatomical elbow — 45°–61° of flexion — and a low-ready pistol is
the middle of that. A straight arm holding a revolver is a duellist, not a worker who
found one in a corridor."""
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

EXPECTED_CYCLE_FRAMES = {"Idle": 92, "Walk": 16, "Run": 16, "Crouch": 80,
                         "CrouchWalk": 20, "GunIdle": 92, "GunWalk": 16}
"""``Runner.fbx.meta`` pins every clip to an explicit frame range, and a generator
never edits a .meta — so a cadence winner that drifts is not a different-but-fine clip,
it is a clip Unity TRUNCATES mid-stride on import (a walk looping with a pop, a
measured m/s that no longer matches the driver's reference). The pendulum search stays
free; this is the fence at the cliff. If a legitimate figure change moves a winner,
the meta's clipAnimations must move with it — by hand, in the same commit."""

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
                right_held: float | None = None, elbow_amp: float = WALK_ELBOW):
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
        # TWO phases, and they are a quarter cycle apart on purpose.
        #
        # `phase` is the LATERAL one — the weight shift that follows whichever foot is
        # planted — and the key orders put a foot down at i = 0, so a sine is right for it.
        #
        # `drive` is the arms and the torso's counter-rotation, and it is a NEGATIVE COSINE
        # because at i = 0 the left leg is at contact, i.e. forward, and an arm opposes the
        # leg on its own side. Both were keyed off `phase` until report_motion measured the
        # result: the arm ended a quarter cycle from where it belongs, correlating +0.29
        # with its own foot instead of about −1. Neither opposed nor in phase — the walk of
        # something that has been told about arms rather than issued with them. It never
        # showed up because nothing in this file compared one limb against another.
        phase = math.sin(2.0 * math.pi * i / period)
        drive = -math.cos(2.0 * math.pi * i / period)
        twist = -twist_amp * drive
        sway = sway_amp * phase
        # The elbow closes as the arm comes FORWARD and opens as it goes back, which is
        # what an elbow does in a gait — flexion in phase with the swing, never zero.
        spec = gpm.merge(
            torso(lean=lean, twist=twist, hips_yaw=-twist * 0.8, hips_tilt=sway * 80),
            head(lean=head_lean, yaw=-twist * 0.25),
            arm(1, arm_out, +swing_amp * drive,
                elbow=ELBOW_REST + elbow_amp * 0.5 * (1.0 + drive)),
            arm(-1, arm_out,
                -swing_amp * drive if right_held is None else right_held,
                elbow=(ELBOW_REST + elbow_amp * 0.5 * (1.0 - drive))
                if right_held is None else GUN_ELBOW),
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
                    swing_amp=RUN_ARM_SWING, head_lean=-4.0, period=8,
                    elbow_amp=RUN_ELBOW),
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
            # elbows well closed: a crouched worker draws their arms in
            arm(1, ARM_OUT + 2.0, 6.0, elbow=34.0),
            arm(-1, ARM_OUT + 2.0, 6.0, elbow=34.0),
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
                           arm(-1, ARM_OUT, GUN_ARM_SWING + b["sw"],
                               elbow=GUN_ELBOW),
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
    """§01 잡힘 — going down is a transition back to B1, not an exit.

    **The reason this clip used to give is deleted.** It read *"§08 makes the corpse a map
    marker teammates have to navigate back to for the dropped 전리품, and §09's 유령 is
    「자기 물건이 어디 있는지 보이는데 말할 수 없다」"*. There is nothing to drop, nobody is
    coming back for it, and the spectator 유령 went with §09. What is left is the rule that
    replaced them: 잡히면 B1 의 자기 칸으로 돌아가 계속 달린다 — so this is the pose the OTHER
    runners read, at the distance a flashlight reaches, in the second before the body is
    gone from the floor. It is not a corpse and it marks nothing.

    Does NOT loop, and settles flat rather than in a heap, and that is now a mechanical
    requirement rather than a design one: ``main`` runs ``settle_on_floor`` on every clip
    with ``loop=False``, so the lowest vertex of the last pose sits ON z = 0 — a heap
    would put an elbow inside the concrete and ``verify_floor`` would fail the run.

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
            # the elbows fold as the body goes down and stay folded under it
            arm(1, ARM_OUT + 14.0, swing[0], elbow=ELBOW_REST + abs(swing[0]) * 0.45),
            arm(-1, ARM_OUT + 6.0, swing[1], elbow=ELBOW_REST + abs(swing[1]) * 0.45),
            gpm.leg(1, thigh, shank, foot, 0.0, out=7.0),
            gpm.leg(-1, thigh - 8.0, shank + 6.0, foot - 6.0, 0.0, out=-3.0),
        )
        world = (hips[0], hips[1] * ratio, hips[2] * ratio)
        poses.append(gpm.make_pose(frame, spec, hips_world=world, prev=prev))
    return gpm.Clip(name="Death", poses=poses, loop=False, measure_frame=48,
                    note="§01 잡힘 → B1 복귀; 다른 주자가 읽는 자세, 표식이 아니다")


CLIP_BUILDERS = (clip_idle, clip_walk, clip_run, clip_crouch, clip_crouch_walk,
                 clip_gun_idle, clip_gun_walk, clip_death)
"""The PROCEDURAL clip authors, kept as the documented fallback. Everything above this
line — ``solve_cadence``, the gait tables, ``_cycle_body`` — is what built every shipped
clip before the mocap retarget, and it still runs when a Mixamo source is missing (see
``build_clips``). It is not dead code: it is what keeps ``gen_runner`` able to write a
Runner.fbx on a checkout that has never had the 8 source FBXs, and it is the honest
before/after this task's render protocol compares the mocap against. The DEFAULT path,
and the shipped Runner.fbx, are the retargeted mocap below."""


# ── Mocap retarget: eight Mixamo clips onto the 13-bone rig ──────────────────
#
# Task #84: "the run cycle does not read as running." The procedural authors above
# produce a stiff shuffle with hanging arms because a hand-built gait table has no flight,
# no opposite-arm swing and no weight — the things a keyframe artist spends a day on and a
# formula cannot invent. The fix is not a better formula. It is professional MOCAP,
# retargeted onto the rig this figure already has.
#
# THE SOURCES sit in source/mixamo/ — 8 clips on the 65-bone mixamorig, all audited. Their
# skeleton is a T-pose in centimetres, Y-up at the armature-local level (the FBX importer
# parks the Y-up→Z-up conversion on the object matrix, which the retarget never reads —
# see below). The runner's rest is arms-DOWN, metres, Z-up. The two rest poses disagree on
# every bone's orientation, and that disagreement is the whole problem a retarget solves.
#
# WHY LOCAL-DELTA, NOT WORLD-DELTA. The obvious retarget — give the target bone the same
# WORLD rotation the source bone underwent from its own rest — is correct only when the two
# rests share a bone's world orientation. The legs and spine do (both point down / up), but
# the ARMS do not: mixamo rests them straight out (T-pose), this rig rests them at the side.
# A world-delta then rotates a Mixamo arm that swings down-and-back into a runner arm that
# swings OUT sideways, because "down" in one rest is not "down" in the other. The retarget
# that survives a rest-orientation difference transfers the source bone's rotation measured
# in ITS OWN rest frame and re-expresses it in the TARGET's rest frame:
#
#     Bworld_target = Rest_target · (Rest_source⁻¹ · World_source)
#
# The parenthesised term is the source bone's rotation relative to its own rest — invariant
# to any global rotation or unit scale of the source armature (a global G left-multiplies
# both World_source and Rest_source and cancels), which is exactly why the Mixamo Y-up/cm
# object matrix never has to be undone. Left-multiplying the target rest lands that motion
# on the runner's own rest orientation, so an arm that rests at the side SWINGS from the
# side and a leg that rests down SWINGS from down. This is the axis-correct retarget; the
# world-delta the task sketches is its special case for the bones whose rests already agree.
#
# The per-bone WORLD orientations are then converted to Blender pose channels with the
# exact same basis formula gpm.make_pose uses (parent's POSED world, own rest), so the
# telescoping through the single Spine is consistent: Spine takes mixamo Spine2's fully
# accumulated torso lean, Head takes mixamo Head's, and Head-relative-to-Spine comes out as
# just the neck. The mixomarig Neck/forearm/hand/finger bones have no target here and are
# DROPPED — this rig's arm is one bone and has no elbow, so their motion is not represented.

MIXAMO_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "source", "mixamo")
"""Where the 8 audited source FBXs live. Absent → ``build_clips`` falls back to procedural."""

MIXAMO_PREFIX = "mixamorig:"

REREF_SUFFIXES = ("UpperArm", "LowerArm", "UpperLeg", "LowerLeg", "Foot", "Toes")
"""Which target bones get their source rest DECLARED rather than measured — every LIMB
bone. See the long note in ``sample_mixamo`` for what declaring it means and why.

**The legs are on this list because of a measurement, and it is the second defect this
task found.** They were left off the first draft on the standing argument that only the
arms rest differently from the source. Then the arms started transferring correctly and
the new ``arm_vs_leg`` opposition read **+0.98** — the runner swinging its left arm
forward together with its left leg, which no human gait does. Index-aligned against the
source (``sample_mixamo`` samples source frame i at i/N, so the two series compare
frame-for-frame) the rig's left ankle correlated **−1.00 with the source's left ankle and
+0.91 with its RIGHT**: the legs were running half a cycle out of phase, a left/right swap
in everything but name. Declaring their reference too puts both at +1.00 against their own
source leg and the opposition at −0.98.

It had been invisible, and the reason it was invisible is the whole shape of this task. A
symmetric gait shifted half a cycle is the same gait — nothing measures it, no render
shows it, the floor and the cadence and the sole error are all exactly as good. It only
becomes a defect once there is something else in the clip whose phase it has to agree
with, and until the arms worked there wasn't.

The torso is deliberately NOT on this list. Hips/Spine/Chest/Neck/Head keep the
rest-relative transfer: their leans measure correct in sign and size on every clip, a roll
mismatch on a vertical bone would have leaned them backwards, and Crouch's depth has
already been judged once at the angle that transfer produces."""

SOURCE_TO_RIG = Matrix.Rotation(math.pi / 2.0, 3, "X")
"""The frame change from a mixamo armature's world to this rig's world, and the ONLY
thing the retarget is allowed to do to a source bone's orientation.

Measured off the source's own rest, not assumed: mixamo's ``LeftArm`` sits at x = +15.16
so +X is the figure's left, as it is here; its ``Spine`` climbs in y so +Y is up, which is
this rig's +Z; and its spine leans back toward −z, so +Z is forward, which is this rig's
−Y. That is a quarter turn about X and nothing else. ``sample_mixamo`` re-measures it
against the legs every run."""

MIXAMO_MAP = {
    "Hips": "Hips",
    "Spine": "Spine",        # lumbar ← the source's own lumbar
    "Chest": "Spine2",       # thoracic ← the source's own chest. See CHEST_JOIN_Z: the
                             # two rig bones and these two source bones share midpoints.
    "Neck": "Neck",
    "Head": "Head",
    "LeftUpperArm": "LeftArm",
    "RightUpperArm": "RightArm",
    "LeftLowerArm": "LeftForeArm",
    "RightLowerArm": "RightForeArm",
    "LeftUpperLeg": "LeftUpLeg",
    "RightUpperLeg": "RightUpLeg",
    "LeftLowerLeg": "LeftLeg",
    "RightLowerLeg": "RightLeg",
    "LeftFoot": "LeftFoot",
    "RightFoot": "RightFoot",
    "LeftToes": "LeftToeBase",
    "RightToes": "RightToeBase",
}
"""target bone → mixamo source bone (short, no prefix). All 17 rig bones are mapped.

**What this map used to drop, and what dropping it looked like.** The 13-bone version
carried no ForeArm and no Neck and hung the one Spine on ``Spine2``, and said so: those
source bones "are DROPPED: their motion is not carried, which is acceptable because this
rig has no bone to carry it onto". It was not. Measured on the source, ``Running.fbx``
holds the elbow between 74° and 105° through the whole cycle — an arm swung back with the
forearm folded across it — and deleting the fold leaves the shoulder→hand line pointing
straight OUT SIDEWAYS, because that is where the upper arm alone points. Both arms level
with the shoulders, no bend anywhere: the scarecrow in the round-3 renders.

**The spine is a re-split, not a re-aim.** The single Spine used to take ``Spine2``'s
accumulated world lean on the argument that the top of the source chain is the whole
torso's angle. It is — for the TOP of the torso. Applied to a bone that spans hips to
neck it leans the entire trunk by the chest's angle: 27° of source ``Spine2`` delta became
a 26° fold of everything above the pelvis, against an actor whose own hips→neck chord in
that clip is 10.5°. Two bones on two source segments, matched by midpoint, put ~10° in the
lumbar and ~27° in the chest and land the chord where the mocap actually has it.

Still dropped, and now honestly: the two hands and every finger. This rig has no hand
joint — the gloves are rigid shells (``FINGER_CURL_DEG``) weighted 100 % to their
``LowerArm`` — so a mixamo ``LeftHand`` curve has nothing to drive. Nothing else is."""

CLIP_MAP_OVERRIDES: dict[str, dict[str, str]] = {
    # Crouch ONLY, and RE-AIMED by the spine split rather than retired by it.
    #
    # The old entry re-hung the single Spine from mixamo Spine2 onto mixamo Spine, because
    # "Crouching Idle" doubles the actor over at the waist — Spine2 pitches ~77° off its
    # rest, and telescoping that onto one runner bone read as a crawl on all fours. It was
    # the one clip of eight the motion judge refuted.
    #
    # The base map now already gives the runner's LUMBAR the source's lumbar, so that entry
    # would be a no-op. The same clip's problem has simply moved up one bone: the runner's
    # Chest would take Spine2's ~77°, and stacked on a lumbar already at ~49° the hips→neck
    # chord lands past 60° — deeper than the pose this generator has been shipping, on the
    # one clip whose depth was argued over. So the override moves to CHEST and points it at
    # the same source bone the lumbar uses: the torso folds by the lumbar's ~49°, which is
    # exactly the angle the shipped Crouch already stands at, and the fold is simply no
    # longer being asked to also carry the chest's. The head still comes off mixamo Head
    # through its own Neck, so it lands up and watching instead of nosing the floor, and
    # the source's deep knee bend, dropped hips and genuine idle breathing are untouched.
    "Crouch": {"Chest": "Spine"},
}
"""Per-clip source-bone remaps layered over ``MIXAMO_MAP``. Absent clip → the base map."""


def clip_mixamo_map(name: str) -> dict[str, str]:
    """The effective target→source bone map for one clip: the base map with this clip's
    overrides (if any) layered on top."""
    return {**MIXAMO_MAP, **CLIP_MAP_OVERRIDES.get(name, {})}

# clip name → (source filename, target frame count, loops). The counts are the ones
# Runner.fbx.meta pins (EXPECTED_CYCLE_FRAMES / Death lastFrame 47); a cycle authors N
# poses at frames 0..N-1 and make_action folds frame N onto frame 0, so the exported take
# is 0..N and frame N == frame 0 by construction. Death authors 0..47 and does not loop.
MIXAMO_SOURCES = {
    "Idle":       ("Breathing Idle.fbx", 92, True),
    "Walk":       ("Walking.fbx",        16, True),
    "Run":        ("Running.fbx",        16, True),
    "Crouch":     ("Crouching Idle.fbx", 80, True),
    "CrouchWalk": ("Crouch Walking.fbx", 20, True),
    "GunIdle":    ("Pistol Idle.fbx",    92, True),
    "GunWalk":    ("Pistol Walk.fbx",    16, True),
    "Death":      ("Death.fbx",          48, False),  # 48 poses → frames 0..47
}


def _rot3(mat: Matrix) -> Matrix:
    """The pure-rotation 3x3 of a 4x4, scale and shear stripped.

    Bone rest matrices are orthonormal, but a posed ``pose_bone.matrix`` can carry a
    non-unit scale if the source rig ever scales a bone; going through the quaternion
    guarantees an orthonormal rotation so the retarget's ``inverted()`` is a transpose and
    not a near-singular solve."""
    return mat.to_quaternion().to_matrix()


def sample_mixamo(path: str, target_frames: int, loop: bool, mapping=MIXAMO_MAP):
    """Imports a Mixamo FBX, samples the mapped source bones over ``target_frames``, and
    tears the import back down.

    ``mapping`` is the effective target→source bone map for THIS clip (``MIXAMO_MAP`` with
    any per-clip override from ``CLIP_MAP_OVERRIDES`` already layered on) — it decides which
    source bones are sampled, so a clip that re-hangs the Spine on a lower mixamo bone gets
    that bone's rest and posed matrices instead of Spine2's.

    Returns ``(srest, source_up_hip, frames)`` where ``srest`` maps each mapped source-bone
    short name to its rest 3x3 (armature-local), ``source_up_hip`` is the rest hip height
    along the source up axis (for scaling the vertical bob), and ``frames`` is a list of
    ``(world_rots, vert_bob)`` — ``world_rots`` a dict short→posed 3x3, ``vert_bob`` the
    hips' vertical displacement from rest in source units (centimetres).

    Sub-frame sampling (``frame_set`` with a fractional subframe) resamples the source
    cadence onto the pinned target count. For cycles the source's last frame is a duplicate
    of its first (measured — LOOP_DIFF 0.000 on all six looping sources), so the period is
    ``f1 - f0`` and the N phases at ``i/N`` never re-sample the duplicate; make_action's
    fold to frame 0 then closes the loop with no pop. Death samples its full arc inclusive.
    """
    before = {o.name for o in bpy.data.objects}
    before_acts = {a.name for a in bpy.data.actions}
    before_mats = {m.name for m in bpy.data.materials}
    before_imgs = {im.name for im in bpy.data.images}
    bpy.ops.import_scene.fbx(filepath=path)
    fresh = [o for o in bpy.data.objects if o.name not in before]
    arms = [o for o in fresh if o.type == "ARMATURE"]
    if not arms:
        for o in fresh:
            bpy.data.objects.remove(o, do_unlink=True)
        blendkit.fail(f"{os.path.basename(path)} imported no armature to retarget from.")
    src = arms[0]
    bones = src.data.bones
    pbones = src.pose.bones

    def full(short):
        return MIXAMO_PREFIX + short

    needed = set(mapping.values())
    missing = [s for s in needed if full(s) not in bones]
    if missing:
        for o in fresh:
            bpy.data.objects.remove(o, do_unlink=True)
        blendkit.fail(f"{os.path.basename(path)} is missing source bones: "
                      + ", ".join(missing))

    srest = {s: _rot3(bones[full(s)].matrix_local) for s in needed}
    # THE LIMB RE-REFERENCE, and why a rest-relative retarget needed one at all.
    #
    # ``bworld[t] = rest[t] @ srest[s]⁻¹ @ world[s]`` transfers the source bone's rotation
    # measured IN ITS OWN LOCAL FRAME onto the target's rest. That is only ever as good as
    # the correspondence between the two bones' local frames — and a bone's local frame is
    # its direction plus its ROLL. Direction is easy to see and easy to argue about. Roll is
    # invisible, nobody authors it (Blender computes it from the bone vector, the FBX
    # importer from the source's node axes), and it is what decides which PLANE a source
    # rotation lands in on the target. Get the roll wrong by 90° and a fore-aft swing
    # arrives as a sideways one; get it wrong by 180° and it arrives backwards.
    #
    # Both had happened here, and neither was visible until the other was fixed.
    #
    #   ARMS. The mixamo rest is a T-pose with the arms straight OUT; this rig rests them at
    #   the SIDE. The old fix pre-multiplied the T-pose arm rest by 90° "down" about the
    #   source's forward axis, which fixed the direction and left the roll to chance. On the
    #   committed rig a walking hand travelled 418 mm side-to-side against 177 mm fore-and-
    #   aft — the swing plane had tipped almost fully over, which is half of why the renders
    #   read as a scarecrow. (The other half was having no forearm at all.)
    #
    #   LEGS. Nothing looked wrong with them, and they were 180° out: the rig's left leg was
    #   reproducing the source's RIGHT one. A symmetric gait shifted half a cycle is the same
    #   gait, so no gate and no render could see it — until the arms came good and started
    #   swinging in phase with the leg on the same side.
    #
    # So the reference is DECLARED rather than nudged: for every bone in REREF_SUFFIXES the
    # source's zero IS this rig's own rest bone, expressed in the source's frame. The
    # correction then reduces to SOURCE_TO_RIG exactly — a pure frame change, no roll left
    # to be wrong about — and each limb bone simply takes the source bone's world
    # orientation converted into this rig's axes.
    if not gpm.REST:
        blendkit.fail("sample_mixamo ran before the rig was cached; gpm.REST is empty and "
                      "the arm re-reference has no target rest to declare against.")
    inv_frame = SOURCE_TO_RIG.inverted()
    for tbone, s in mapping.items():
        if tbone.endswith(REREF_SUFFIXES):
            srest[s] = inv_frame @ gpm.REST[tbone]
    # And SOURCE_TO_RIG is CHECKED against the source's own rest geometry rather than
    # taken on trust — three landmarks that cannot be argued with. Up is hips→spine, left
    # is right shoulder→left shoulder, and forward is their cross product; the frame change
    # has to put all three on this rig's +Z, +X and −Y.
    heads = {n: bones[full(n)].matrix_local.to_translation()
             for n in ("Hips", "Spine", "LeftArm", "RightArm")}
    up_s = (heads["Spine"] - heads["Hips"]).normalized()
    left_s = (heads["LeftArm"] - heads["RightArm"]).normalized()
    fwd_s = left_s.cross(up_s).normalized()
    got = [SOURCE_TO_RIG @ v for v in (up_s, left_s, fwd_s)]
    want = [Vector((0.0, 0.0, 1.0)), Vector((1.0, 0.0, 0.0)), Vector((0.0, -1.0, 0.0))]
    errs = [math.degrees(math.acos(max(-1.0, min(1.0, g.dot(w)))))
            for g, w in zip(got, want)]
    print(f"SOURCE_FRAME up={errs[0]:.1f}deg left={errs[1]:.1f}deg fwd={errs[2]:.1f}deg "
          f"off this rig's +Z/+X/−Y (SOURCE_TO_RIG checked against the source's rest)")
    if max(errs) > 5.0:
        blendkit.fail(
            f"SOURCE_TO_RIG does not convert {os.path.basename(path)}'s frame: its up, left "
            f"and forward land {errs[0]:.1f}/{errs[1]:.1f}/{errs[2]:.1f} degrees off this "
            "rig's. Every arm in every clip is re-referenced through that matrix, so a wrong "
            "one swings them in the wrong plane — which is the defect this rig was rebuilt "
            "to remove. A re-export from Mixamo with different axis settings would do it.")
    # up axis and rest hip height, both in the source armature-local frame (Y-up, cm).
    up = (bones[full("Spine")].matrix_local.to_translation()
          - bones[full("Hips")].matrix_local.to_translation()).normalized()
    hips_rest_t = bones[full("Hips")].matrix_local.to_translation()
    source_up_hip = hips_rest_t.dot(up)

    act = src.animation_data.action if src.animation_data else None
    if act is None:
        for o in fresh:
            bpy.data.objects.remove(o, do_unlink=True)
        blendkit.fail(f"{os.path.basename(path)} carries no action to retarget.")
    f0, f1 = float(act.frame_range[0]), float(act.frame_range[1])
    span = f1 - f0

    scene = bpy.context.scene
    frames = []
    for i in range(target_frames):
        if loop:
            src_f = f0 + (i / target_frames) * span
        else:
            src_f = f0 + (i / max(1, target_frames - 1)) * span
        whole = math.floor(src_f)
        scene.frame_set(whole, subframe=src_f - whole)
        world_rots = {s: _rot3(pbones[full(s)].matrix) for s in needed}
        hip_t = pbones[full("Hips")].matrix.to_translation()
        vert_bob = (hip_t - hips_rest_t).dot(up)
        frames.append((world_rots, vert_bob))

    for o in fresh:
        data = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        # purge the imported mesh/armature data so a shipped scene stays the runner's alone
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Armature):
                bpy.data.armatures.remove(data)
    for a in list(bpy.data.actions):
        if a.name not in before_acts:
            bpy.data.actions.remove(a)
    # Purge the imported materials and images too, or ASSET_REPORT counts them
    # (blendkit.describe reports len(bpy.data.materials)) and the five-slot material
    # contract reads as broken — 5 runner slots + 2 per mixamo mesh would print as 21.
    for m in list(bpy.data.materials):
        if m.name not in before_mats and m.users == 0:
            bpy.data.materials.remove(m)
    for im in list(bpy.data.images):
        if im.name not in before_imgs and im.users == 0:
            bpy.data.images.remove(im)
    return srest, source_up_hip, frames


def retarget_clip(rig: bpy.types.Object, body: bpy.types.Object, name: str) -> gpm.Clip:
    """One retargeted clip: mocap rotations on all 13 bones, a scaled vertical bob on the
    hips, and a single floor shift so the lowest planted moment sits on z = 0.

    The rotations are the axis-correct local-delta retarget (see the section header). The
    hips take ONLY the source's vertical bob, scaled by this figure's hip height — the XZ
    of the source root is discarded, which is what turns even a root-motion source (Running
    and Crouch Walking both stride the mixamo root forward metres) into a clean In-Place
    cycle: freezing the root while keeping the leg rotations sweeps the planted foot
    backward under a stationary hip, the treadmill the game's locomotion then drives.
    """
    filename, target_frames, loop = MIXAMO_SOURCES[name]
    mapping = clip_mixamo_map(name)
    srest, source_up_hip, frames = sample_mixamo(
        os.path.join(MIXAMO_DIR, filename), target_frames, loop, mapping)

    hip_scale = gpm.HIP_Z / source_up_hip  # source cm → runner metres, by hip height
    order = gpm.ORDER
    parent = gpm.PARENT
    rest = gpm.REST
    ident = Matrix.Identity(3)

    # Poses are authored at frames 1..N, not 0..N — the shipped convention every procedural
    # clip already follows. Frame 0 is reserved for the REST pose: main sets every NLA
    # strip to extrapolation NOTHING and asserts the rig sits at rest at frame 0 before
    # export (the bind pose Unity reads), which only holds when no strip's range reaches
    # frame 0. make_action folds a copy of frame 1 onto frame N+1 to close the loop.
    poses: list[blendkit.Pose] = []
    prev: dict[str, Euler] = {}
    world_offsets: list[float] = []
    for i, (world_rots, vert_bob) in enumerate(frames):
        # Desired world orientation of every target bone, top-down.
        bworld: dict[str, Matrix] = {}
        for tbone in order:
            s = mapping[tbone]
            bworld[tbone] = rest[tbone] @ srest[s].inverted() @ world_rots[s]
        # Convert to local pose-channel eulers (gpm.make_pose's own basis math).
        rotations: dict[str, tuple[float, float, float]] = {}
        for tbone in order:
            par = parent[tbone]
            pworld = bworld[par] if par else ident
            prest = rest[par] if par else ident
            basis = rest[tbone].inverted() @ prest @ pworld.inverted() @ bworld[tbone]
            if tbone in prev:
                euler = basis.to_euler("XYZ", prev[tbone])
            else:
                euler = basis.to_euler("XYZ")
            prev[tbone] = euler
            rotations[tbone] = (math.degrees(euler.x),
                                math.degrees(euler.y),
                                math.degrees(euler.z))
        z = vert_bob * hip_scale
        world_offsets.append(z)
        poses.append(blendkit.Pose(
            frame=i + 1, rotations=rotations,
            locations={"Hips": gpm.to_local_vec("Hips", (0.0, 0.0, z))}))

    lows = [lowest_vertex(rig, body, p) for p in poses]
    if loop:
        # One constant vertical shift so the deepest planted moment of a CYCLE rests on
        # z = 0. A per-frame drop would kill the run's flight; a per-frame lift would
        # stair-step the bob. The relative bob (and the flight) is the source's; only the
        # datum moves. A one-shot fall is different — its hip travels 0.7 m from standing to
        # prone, so no single datum keeps both the standing feet and the settled body on the
        # floor; Death keeps its raw bob and main's settle_on_floor lifts each penetrating
        # key of the collapse instead.
        shift = -min(lows)
        for i, p in enumerate(poses):
            p.locations["Hips"] = gpm.to_local_vec(
                "Hips", (0.0, 0.0, world_offsets[i] + shift))
    else:
        shift = 0.0

    measure = 1 if loop else max(1, target_frames - 1)
    seam = ""
    if loop:
        # loop-seam residual: how far frame N-1's mapped bone dirs sit from frame 0's, in
        # the same summed-direction metric the source LOOP_DIFF used. Small = no pop.
        seam = f" seam={_loop_seam(poses[-1], poses[0]):.3f}"
    override = CLIP_MAP_OVERRIDES.get(name)
    ov = (" override=" + ",".join(f"{t}<-{s}" for t, s in sorted(override.items()))
          if override else "")
    print(f"RETARGET_CLIP {name:11s} src={filename!r} frames={target_frames} "
          f"loop={int(loop)} hip_scale={hip_scale:.5f} bob={min(world_offsets):+.3f}.."
          f"{max(world_offsets):+.3f}m floor_shift={shift * 1000.0:+.1f}mm"
          f" lows={min(lows) * 1000.0:+.1f}..{max(lows) * 1000.0:+.1f}mm{seam}{ov}")
    return gpm.Clip(name=name, poses=poses, loop=loop,
                    cycle_frames=(target_frames if loop else 0),
                    measure_frame=measure,
                    note=f"mixamo {filename} retargeted, In-Place")


def _loop_seam(pose_a: blendkit.Pose, pose_b: blendkit.Pose) -> float:
    """A scalar seam metric: the summed absolute euler-degree change across all bones from
    ``pose_a`` to ``pose_b``, in turns. Frame N-1 → frame 0 should be one small step."""
    total = 0.0
    for bone, ra in pose_a.rotations.items():
        rb = pose_b.rotations[bone]
        total += sum(abs(((a - b + 180.0) % 360.0) - 180.0) for a, b in zip(ra, rb))
    return total / 360.0


def mocap_available() -> bool:
    """True when all 8 source FBXs are present, so the retarget can run."""
    return all(os.path.exists(os.path.join(MIXAMO_DIR, fn))
               for fn, _n, _l in MIXAMO_SOURCES.values())


def build_clips(rig: bpy.types.Object, body: bpy.types.Object,
                procedural: bool) -> list[gpm.Clip]:
    """The eight clips, retargeted from mocap by default, procedural as the fallback.

    The retarget needs the welded, skinned body (the floor shift is measured off the
    deformed mesh), so unlike the procedural authors — which only need the rig — the clips
    are built here in ``main`` after the skin, not from the rig-only ``CLIP_BUILDERS``.
    """
    if procedural or not mocap_available():
        why = "forced" if procedural else "sources missing"
        print(f"CLIP_SOURCE procedural ({why}); mixamo dir={MIXAMO_DIR}")
        return [build(rig) for build in CLIP_BUILDERS]
    print(f"CLIP_SOURCE mocap; retargeting {len(MIXAMO_SOURCES)} mixamo clips onto "
          f"{len(gpm.ORDER)} bones")
    return [retarget_clip(rig, body, name) for name in CLIP_NAMES]


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
    # hips → TOP OF THE TORSO. That landmark is ``Neck``'s head since the spine was
    # split; it is the same height (NECK_BASE_Z) the old ``Head``'s head sat at, so the
    # number stays comparable with every lean printed before the 17-bone rig.
    spine_dir = (gpm.bone_point(rig, "Neck", (0.0, 0.0, 0.0)) - hips).normalized()
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


ELBOW_STRAIGHT_DEG = 172.0
"""The anatomical elbow angle (180° = a straight rod) past which the arm has stopped
being an arm. The whole of task #85 is that the 13-bone rig held every frame of every
clip at exactly 180°; ``report_motion`` fails the build if any frame of a LOCOMOTION clip
reaches this. 172° rather than 179° because "not quite straight" is not a bend either —
at this arm length 8° is 85 mm of hand travel, which is the difference between a bent
elbow and a measurement artefact."""

SWING_PLANE_MAX = 0.90
"""How much of the hand's fore-aft travel it is allowed to also travel SIDEWAYS, over a
running cycle. Arms drive fore-and-aft; the failure this number fences is the one the
renders showed, where the hand's lateral excursion (583 mm) was 1.71× its fore-aft one
(341 mm) because the arm was pointing out of the side of the body. Not 0.0 — a real
running arm crosses slightly toward the chest and a mirror-perfect sagittal swing reads as
a toy soldier — but past this the plane has tipped and the swing is no longer a swing."""

SWING_PLANE_FLOOR = 0.100
"""Metres of fore-aft hand travel below which the plane ratio is printed but not judged.
A ratio needs a denominator: CrouchWalk holds its arms in against the body and moves a
hand 20 mm over a whole cycle, where 20 mm of unavoidable lateral wobble reads as a 1.03
"failure" and means nothing at all. 100 mm is a tenth of an arm — under it there is no
swing to have a plane, and what keeps those clips honest is the elbow check and the
render, not this."""


def report_motion(rig, clips: list[gpm.Clip]) -> None:
    """The acceptance instrument for task #85: elbow, swing plane, torso lean, per clip.

    **Why these three and why measured over the whole cycle rather than at one frame.**
    Each is a number a render can be argued with and this cannot. The elbow says the
    forearm exists and is being driven — a rig with no elbow reads exactly 180.0 every
    frame, which is what the 13-bone version did. The swing plane says the arm is driving
    the run rather than being held out of it: a hand's lateral excursion measured against
    its fore-aft one, in the shoulder's own frame. The lean says the torso is a runner's
    and not a plank's.

    Locomotion clips are ASSERTED, the rest are only printed. Idle and GunIdle have no
    swing to have a plane, and Death ends face down with an arm folded under a body — a
    clip whose whole content is the figure ceasing to be upright cannot be held to a
    runner's posture.
    """
    graded = {"Walk", "Run", "CrouchWalk", "GunWalk"}
    for clip in clips:
        rows = []
        for pose in clip.poses:
            gpm.apply_pose(rig, pose)
            row = {}
            for side, s in (("Left", 1.0), ("Right", -1.0)):
                sh = gpm.bone_point(rig, side + "UpperArm", (0.0, 0.0, 0.0))
                el = gpm.bone_point(rig, side + "LowerArm", (0.0, 0.0, 0.0))
                low = rig.data.bones[side + "LowerArm"]
                hand = gpm.bone_point(rig, side + "LowerArm",
                                      tuple(low.tail_local - low.head_local))
                u = (el - sh).normalized()
                f = (hand - el).normalized()
                d = hand - sh
                row[side] = (
                    180.0 - math.degrees(math.acos(max(-1.0, min(1.0, u.dot(f))))),
                    s * d.x,        # + = outboard of the shoulder
                    -d.y,           # + = in front of the shoulder
                )
            hips = gpm.bone_point(rig, "Hips", (0.0, 0.0, 0.0))
            chord = gpm.bone_point(rig, "Neck", (0.0, 0.0, 0.0)) - hips
            row["lean"] = math.degrees(math.atan2(-chord.y, chord.z))
            for side in ("Left", "Right"):
                hip = gpm.bone_point(rig, side + "UpperLeg", (0.0, 0.0, 0.0))
                low = rig.data.bones[side + "LowerLeg"]
                row[side + "foot"] = -(gpm.bone_point(
                    rig, side + "LowerLeg", tuple(low.tail_local - low.head_local)) - hip).y
            rows.append(row)

        worst_straight, worst_plane, worst_opp = 0.0, 0.0, -1.0
        for side in ("Left", "Right"):
            elbows = [r[side][0] for r in rows]
            lat = [r[side][1] for r in rows]
            fore = [r[side][2] for r in rows]
            lat_t, fore_t = max(lat) - min(lat), max(fore) - min(fore)
            plane = lat_t / fore_t if fore_t > 1e-4 else 0.0
            worst_straight = max(worst_straight, max(elbows))
            if fore_t >= SWING_PLANE_FLOOR:
                worst_plane = max(worst_plane, plane)
            # Opposition: a hand and the foot on the SAME side move in antiphase in every
            # human gait. Reported as the correlation of their fore-aft offsets, so it is
            # NEGATIVE when the figure is running and positive when an arm has been
            # transferred with its swing direction reversed — the one failure the plane
            # ratio cannot see, because a backwards swing is still a swing in the plane.
            ankles = [r[side + "foot"] for r in rows]
            mf, ma = (sum(fore) / len(fore)), (sum(ankles) / len(ankles))
            cov = sum((f - mf) * (a - ma) for f, a in zip(fore, ankles))
            sf = math.sqrt(sum((f - mf) ** 2 for f in fore))
            sa = math.sqrt(sum((a - ma) ** 2 for a in ankles))
            opp = cov / (sf * sa) if sf > 1e-9 and sa > 1e-9 else -1.0
            if fore_t >= SWING_PLANE_FLOOR:
                worst_opp = max(worst_opp, opp)
            print(f"MOTION {clip.name:11s} {side:5s} "
                  f"elbow={min(elbows):6.1f}..{max(elbows):6.1f}deg "
                  f"fore_travel={fore_t * 1000.0:6.1f}mm "
                  f"lat_travel={lat_t * 1000.0:6.1f}mm plane={plane:5.2f}"
                  f"{'' if fore_t >= SWING_PLANE_FLOOR else '*'} "
                  f"hand_out_max={max(lat) * 1000.0:+7.1f}mm arm_vs_leg={opp:+.2f}")
        leans = [r["lean"] for r in rows]
        print(f"MOTION {clip.name:11s} torso lean={min(leans):+6.1f}..{max(leans):+6.1f}deg "
              f"mean={sum(leans) / len(leans):+6.1f}deg (hips→Neck)")

        if clip.name not in graded:
            continue
        if worst_straight > ELBOW_STRAIGHT_DEG:
            blendkit.fail(
                f"{clip.name} straightens an elbow to {worst_straight:.1f}° against a "
                f"{ELBOW_STRAIGHT_DEG:.0f}° ceiling. A locomotion clip on this rig must "
                "bend the arm every frame — a straight one means the LowerArm is inheriting "
                "its parent's rotation and not being driven, which is the 13-bone scarecrow "
                "with two extra bones in the file. Check MIXAMO_MAP still names both "
                "ForeArms, or that the procedural arm() is being given an elbow.")
        if worst_plane > SWING_PLANE_MAX:
            blendkit.fail(
                f"{clip.name} swings a hand {worst_plane:.2f}× as far sideways as it does "
                f"fore-and-aft, past the {SWING_PLANE_MAX:.2f} a swing plane is allowed to "
                "tip. The arm is being held out of the side of the body rather than driving "
                "the stride — measured at 1.71 on the 13-bone rig, which is what the round-3 "
                "renders show. The usual cause is the arm re-reference in sample_mixamo "
                "having stopped matching the rig's rest arm.")
        if worst_opp > 0.0:
            blendkit.fail(
                f"{clip.name} swings a hand IN PHASE with the foot on the same side "
                f"(correlation {worst_opp:+.2f}; a gait is negative). No human walks or runs "
                "like that — arm and same-side leg oppose, which is what stops a body "
                "rotating about its own axis every step. This is the check that caught the "
                "legs being retargeted half a cycle out (REREF_SUFFIXES): it is invisible in "
                "a render because both sides of the figure are symmetric, and it will only "
                "ever be caught by measuring one limb against the other.")


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

    # Before any geometry: the table itself has to describe one solid, and the
    # garment has to actually dress the harvested body. Both are cheap, and both
    # fail by name instead of by symptom.
    verify_parts_interpenetrate(parts)
    verify_body_covered(parts)

    primitives: list[bpy.types.Object] = []
    for part in parts:
        primitives += part.build()
    print(f"RUNNER_PRIMITIVES count={len(primitives)} "
          f"(harvested body, loft and capped primitives; "
          f"{SAMPLE_CHORD * 1000.0:.0f}mm target chord)")

    body = weld(primitives)

    # The hands join AFTER the weld and the decimation, because both would destroy
    # them: the voxel grid re-mittens fingers and the collapse eats knuckles. They
    # are placed in the same canonical frame, so the fit below carries them with
    # everything else.
    hands = harvest_hands()
    body = blendkit.join([body] + hands, MESH_NAME)
    body.data.name = MESH_NAME
    blendkit.shade_smooth(body, angle_degrees=42.0)
    _stage("hands_joined", body)

    fit = fit_height_and_ground(body, TARGET_HEIGHT)
    print(f"RUNNER_FIT scale={fit.scale:.5f}x drop={fit.drop:.5f}m "
          f"(the smoothing shrinks the union, so the height is solved after it, "
          f"never authored into the table)")

    # After the fit, because the paint regions are stated in final metres.
    assign_materials(body, fit)

    shells = verify_shells(body, expect=3)
    verify_hand_shells(body, shells, fit)
    # The ceilings are the joints themselves: below the ankle the only way across is the
    # floor, and below the shoulder ball the only way across is the chest.
    # The crotch ceiling is the belly's own underside: above it the legs are allowed to
    # be one mass, because that mass is the pelvis. Below it they are two legs.
    hip_z, ankle_z = fit.z(LEG_Z_TOP), fit.z(ANKLE_Z)
    verify_limbs_hang_free(
        # The crotch ceiling stops UNDER the hem: the jacket's skirt legitimately
        # crosses the midline above 0.74, and a profile that included it would read
        # the hem as a thigh weld.
        body, shells[0], ankle_z=ankle_z, crotch_z=fit.z(HEM_BOTTOM_Z - 0.02),
        hip_z=hip_z,
        # The lower two thirds of a leg has to be a leg. The top third is inside the
        # pelvis, where being one mass is the point.
        leg_part_z=hip_z - LEG_PART_FRACTION * (hip_z - ankle_z),
        armpit_z=fit.z(SHOULDER_Z - SHOULDER_R),
        ball_c=(fit.d(SHOULDER_X), fit.d(0.004), fit.z(SHOULDER_Z)),
        ball_r=(fit.d(SHOULDER_R), fit.d(0.100), fit.d(SHOULDER_R)))
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

    rig = build_rig(skeleton, body, shells[1:])
    verify_skin(rig, body)

    # DEFAULT: the eight clips are professional mocap retargeted onto this rig (Task #84 —
    # the procedural gait did not read as running). `--procedural`, or a missing source
    # FBX, falls back to the hand-built CLIP_BUILDERS. build_clips needs `body` because the
    # retarget's floor shift is measured off the deformed mesh.
    procedural = "--procedural" in argv or os.environ.get("HORROR_RUNNER_PROCEDURAL") == "1"
    clips: list[gpm.Clip] = []
    for clip in build_clips(rig, body, procedural):
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

    # The frame-count gate stays load-bearing across the source change. Under the
    # procedural authors it fenced a cadence SEARCH that could drift; under the mocap
    # retarget the count is fixed BY CONSTRUCTION (sample_mixamo resamples each source onto
    # exactly this many frames), so a mismatch here is no longer a leg leaving the pendulum
    # band — it is MIXAMO_SOURCES and Runner.fbx.meta disagreeing, a wiring bug in this
    # file. Either way the consequence is identical: a take whose length is not the meta's
    # is TRUNCATED on import and loops with a pop, so the assert earns its place in both.
    for clip in clips:
        expected = EXPECTED_CYCLE_FRAMES.get(clip.name)
        if expected is not None and clip.cycle_frames != expected:
            blendkit.fail(
                f"{clip.name} built a {clip.cycle_frames}-frame cycle and "
                f"Runner.fbx.meta imports it as 0–{expected}. The importer would "
                "truncate the take mid-stride and the loop pops every cycle. Under the "
                "mocap path this is MIXAMO_SOURCES' target count disagreeing with the "
                "meta; under --procedural it is the leg leaving the cadence band ANKLE_Z "
                "describes. Fix the count in MIXAMO_SOURCES, or update the meta's "
                "clipAnimations by hand in the same commit.")
    verify_clip_speeds(clips)
    verify_floor(rig, body, clips)
    verify_skin_stretch(rig, body, clips)
    for clip in clips:
        pose_measure(rig, clip)
    report_motion(rig, clips)
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
    # 12.5k ceiling, up from the mannequin's 8k: a clothed body at 8–12k is the right
    # spend for the one asset twenty of which are on screen (task brief; URP Forward+
    # budget in the module docstring). Still a fifth of the monster.
    blendkit.assert_asset(report, min_vertices=200, max_triangles=12500,
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
