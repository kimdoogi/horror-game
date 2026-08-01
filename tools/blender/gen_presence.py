#!/usr/bin/env python3
"""Builds the 그늘 — the second thing in the building — and the sound it makes.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_presence.py

Writes five files and nothing else::

    Assets/Models/Presence/Presence_Figure.fbx      the 형상 that stands at 임박
    Assets/Models/Presence/Presence_Mote.fbx        one flake, instanced by PresenceView
    Assets/Models/Presence/Presence.manifest.json   the two materials, for PresenceSkin.cs
    Assets/Audio/Presence/*.wav                     four clips, none of them positional

WHAT IS BEING BUILT, AND WHY IT IS NOT A CREATURE
--------------------------------------------------
``Core/Presence`` implements a condition, not a pursuer: the 그늘 pools on a player
who is standing in less light than §03 needs to read by, and when it is full it takes
their voice and their certainty. §01 keeps its horror with **one** unkillable pursuer
— 「이길 수 없는 적 → 공포가 유지된다」 — and a second one would not double the fear,
it would halve the first. So everything here is authored against one rule:

    **it must never be mistaken for the monster, at any distance, for one frame.**

That rules out most of what a monster asset does. No rig, no bones, no clips, no
locomotion, no eyes to make contact with, no tell to read. ``Monster.fbx`` is 2.336 m,
hunched, digitigrade, clawed, with a bladed head, a three-segment crest and two lit
lenses. The 형상 is 2.05 m, dead upright, arms at its sides, no head detail at all,
and it has no animation of any kind. If you see it, it is standing still, and the next
time you look it is somewhere else — which is a cut, not a movement.

THE ONE ART PROBLEM, AND HOW IT IS SOLVED
-----------------------------------------
The monster's art problem was that it disappeared: a dark creature in a dark corridor
is nothing, and ART.md records two passes spent making it legible at 15 m. The 그늘 has
the *opposite* problem and the same cause. It is supposed to be made of absence, so the
obvious authoring — a black figure — is invisible by construction rather than by
accident, and no amount of grading fixes a shape whose whole idea is "darker than the
room".

So it is built in two materials that do opposite things:

* ``Presence_Void`` — the core, albedo ``0.013`` linear. That is **an order of magnitude
  under the 0.21 of the darkest §12 wall** and under the monster hide's 0.17. It is not
  meant to be seen; it is meant to *remove* what is behind it, so the figure reads as a
  hole punched in an already dim frame. ART.md puts the unlit room's median luminance at
  3–16, which is exactly enough for a 0.013 shape to sit below it and separate.
* ``Presence_Grain`` — several hundred small flakes standing 1–5 cm off that core, faintly
  emissive and cold. They are what the eye actually catches, and they are why the figure
  reads at range in a frame where nothing else does. They are also the honest answer to
  what the thing is: the figure is not a body with grain on it, it is the grain, briefly
  agreeing on a shape.

The flake density is deliberately top-weighted (``GRAIN_TOP_BIAS``): dense at the head
and shoulders, thinning to nothing below the knee. A figure that dissolves downward has
no feet, and a thing with no feet did not walk there.

THE AUDIO IS IN THIS FILE ON PURPOSE
------------------------------------
Every other family lives in ``tools/audio/``. These four clips do not, for a reason that
is a design decision rather than a convenience: **the 그늘 has no position, so its sound
must have no position either.**

§04 gives the 청음사 exactly one ability — reading the monster's 위치 · 거리 · 이동 방향
by ear — and ASSETS.md §1 records that the whole audio policy exists to protect it. A
second world-space emitter with its own direction and distance would compete for that
channel and the role would get quieter without anything reporting a fault. So these are
authored as non-diegetic: mono, 2D, at the player's own ears, with nothing to localise.
``AssetImportPolicy.ResolveRole`` reaches the same verdict from the other end — an
unrecognised folder under ``Assets/Audio`` resolves to ``InterfaceCue``, which is
non-positional — so the policy and the intent agree without a policy entry being needed.

The loops are made periodic **in the frequency domain** rather than crossfaded, so the
wrap is sample-exact rather than nearly right. ``verify_audio.py`` reports a −9.7 dB hole
at every wrap of ``flare_burn_loop.wav``; that failure mode is not reachable here,
because a spectrum built only from integer multiples of ``1/duration`` has no wrap.
"""

from __future__ import annotations

import json
import math
import os
import struct
import sys
import traceback
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import blendkit  # noqa: E402


# ── The figure ──────────────────────────────────────────────────────────────

FIGURE_HEIGHT = 2.05
"""Metres. Between the player's 1.750 (AssetImportPolicy.PlayerHeightMetres) and the
monster's 2.336, and closer to the player. It has to read as *a person, wrong* — too
tall to be one of the four and too short to be the thing that is chasing them."""

SHOULDER_WIDTH = 0.34
"""Metres across the shoulders. A player's rig is 0.60 m across the capsule, so this is
a little over half a person wide. Thin is what makes the silhouette wrong before
anything else about it registers."""

TRI_BUDGET = 3200
"""docs/ART.md §12 — darkness does the work, not triangles. The monster gets 6000 and it
has a face, a crest and seven clips; this has none of those."""

GRAIN_COUNT = 900
"""Flakes on the figure. Enough that the silhouette is legible from the grain alone at
the distance §04's 관측자 works at, few enough that the core still reads as solid up
close.

Was 520 at 3 cm, which the first preview render settled: at that size and count the
flakes read as *confetti stuck to a mannequin* rather than as the substance the figure is
made of. Small and many is grain; large and few is decoration, and decoration on a
2 m silhouette is the one thing that would make it look like an asset rather than like
something wrong with the room."""

GRAIN_TOP_BIAS = 1.5
"""Exponent on the vertical density ramp. Flakes cluster at the head and shoulders and
thin toward the floor, so the figure has no feet — see the module docstring.

Reduced from 2.4 for a reason the first preview made obvious and the design intent had
hidden: in the game the void core is *invisible*, because it is 16× darker than the wall
behind it and the room medians 3–16 luminance. So the grain is not decoration on the
silhouette, the grain **is** the silhouette, and a bias steep enough to strip the legs
leaves a torso floating in the dark. 1.5 still halves the density between the shoulders
and the shins; 2.4 removed them."""

GRAIN_SIZE = 0.017
"""Metres across one flake on the figure."""

GRAIN_STANDOFF = (0.005, 0.026)
"""Metres the flakes float off the core, min and max. The gap is what makes them read as
a separate substance rather than as speckle on a surface — but at the first preview's
1–5 cm they detached from the form entirely and read as a swarm around a statue."""

MOTE_SIZE = 0.030
"""Nominal metres across one free mote — the shard is a triangle rather than a disc, so
it spans about 0.022 m on its widest axis.

Two render rounds set this and they pushed in opposite directions, which is worth
recording because the midpoint is not a compromise. At 3 cm nothing read: 88 motes were on
screen and the frame looked empty, because a two-pixel speck is indistinguishable from
sensor grain. At 5.6 cm they read as **hard white paper triangles floating in a corridor**
— an emissive polygon at 1.5 m is a solid object with visible edges, and no amount of
placement makes origami frightening.

The answer was not a size at all. It is ``PresenceView`` scaling each mote in proportion
to its distance so every one of them subtends about the same five pixels wherever it is,
plus an additive, translucent material so what those pixels contain is a glow rather than
a polygon. This number is only the reference size at 3 m."""

SEED = 20260801
"""Fixed, so two runs of this generator produce byte-identical geometry. Every other
asset in this project is reproducible and an irreproducible one cannot be reviewed."""


VOID_MATERIAL = "Presence_Void"
"""``PresenceSkin.VoidMaterialName``. The hole. A contract, not a label."""

GRAIN_MATERIAL = "Presence_Grain"
"""``PresenceSkin.GrainMaterialName``. The emissive flakes, and the only part of the
figure a player at range actually sees."""

DUST_MATERIAL = "Presence_Dust"
"""``PresenceSkin.DustMaterialName``. The same substance at a third the emission, for the
free motes.

Two materials rather than one because the two things are seen at completely different
distances and one exposure cannot serve both. The figure's flakes have to carry a
silhouette at 12 m, where a flake is under two pixels and only brightness survives
downsampling. The free motes are at 1–4 m, and at the emission that makes the figure work
they blow to hard white and read as scraps of paper stuck to the brickwork — which is
exactly what the second render round photographed."""


MATERIALS = (
    {
        "name": VOID_MATERIAL,
        # 0.013 linear. gen_monster_ai.py holds the monster hide at 0.17 against a
        # darkest-corridor 0.21 so the creature does not announce itself; this sits a
        # further order of magnitude down, because the 형상 is not supposed to be lit at
        # all — it is supposed to subtract.
        "color": [0.013, 0.014, 0.016],
        "roughness": 1.0,
        "metallic": 0.0,
        "emission": 0.0,
        "additive": False,
        "alpha": 1.0,
        "note": "the hole. Darker than the darkest §12 wall by 16x, so it reads as absence.",
    },
    {
        "name": GRAIN_MATERIAL,
        # Ash, barely cold. Deliberately NOT the zone-fitting blue (ART.md §3.6) and not
        # the apron's amber: the 그늘 must not be mistaken for a light somebody left on.
        "color": [0.470, 0.520, 0.505],
        "roughness": 0.90,
        "metallic": 0.0,
        # Tuned by render against ART.md's bands — see PresenceShot. The unlit room medians
        # 3–16, so the flakes have to clear that at 12 m without going near the 250/255
        # blown ceiling at 4 m. 2.2 carried 12 m and blew the 4 m frame to white; 1.6 is
        # the value that holds both.
        "emission": 1.6,
        "additive": False,
        "alpha": 1.0,
        "note": "the flakes. Faintly emissive so the silhouette survives a frame with no light in it.",
    },
    {
        "name": DUST_MATERIAL,
        "color": [0.470, 0.520, 0.505],
        "roughness": 0.90,
        "metallic": 0.0,
        # Additive rather than opaque — see "additive" below — so this is read as the
        # amount each mote adds to the frame rather than as a surface brightness.
        "emission": 1.30,
        # PresenceSkin builds this one as a transparent, additively-blended material with
        # depth writes off. An opaque emissive polygon 1.5 m from the camera has visible
        # straight edges and reads as a scrap of paper however small it is; the same
        # polygon added to what is behind it has no edge at all, which is the whole
        # difference between grain and litter. Nothing else in the project needs this, so
        # it is a flag on the material rather than a second shader.
        "additive": True,
        "alpha": 0.55,
        "note": "the free motes. Additive and translucent, so they are a glow and not a polygon.",
    },
)


# ── Geometry helpers ────────────────────────────────────────────────────────


def loft(name: str, rings: list[tuple[float, float, float]], segments: int = 12) -> bpy.types.Object:
    """Builds a closed mesh by lofting elliptical rings up the Z axis.

    ``rings`` is ``(z, half_x, half_y)`` bottom to top. Primitives were tried first and
    produced exactly what ``gen_monster_model.py``'s docstring warns about: an assembly
    of bulges with no concave anything. A loft costs forty lines and gives the one thing
    the silhouette depends on — a waist that is narrower than both the ribs above it and
    the hips below it, which is what makes a shape read as a body rather than as a post.
    """
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    bm = bmesh.new()
    loops: list[list[bmesh.types.BMVert]] = []

    for z, hx, hy in rings:
        if hx <= 1e-5 or hy <= 1e-5:
            loops.append([bm.verts.new((0.0, 0.0, z))])
            continue

        loop = []
        for s in range(segments):
            theta = 2.0 * math.pi * s / segments
            loop.append(bm.verts.new((hx * math.cos(theta), hy * math.sin(theta), z)))
        loops.append(loop)

    bm.verts.ensure_lookup_table()

    for lower, upper in zip(loops, loops[1:]):
        if len(lower) == 1 and len(upper) == 1:
            continue
        if len(lower) == 1:
            for s in range(len(upper)):
                bm.faces.new((lower[0], upper[s], upper[(s + 1) % len(upper)]))
            continue
        if len(upper) == 1:
            for s in range(len(lower)):
                bm.faces.new((lower[(s + 1) % len(lower)], lower[s], upper[0]))
            continue
        for s in range(len(lower)):
            t = (s + 1) % len(lower)
            bm.faces.new((lower[s], lower[t], upper[t], upper[s]))

    # Cap a flat bottom ring if the lowest loop is not a point, so the mesh is closed and
    # Unity's importer does not report an open manifold.
    if len(loops[0]) > 1:
        bm.faces.new(tuple(reversed(loops[0])))
    if len(loops[-1]) > 1:
        bm.faces.new(tuple(loops[-1]))

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    return obj


def figure_profile() -> list[tuple[float, float, float]]:
    """The 형상's cross-sections, bottom to top, in metres.

    Read it as a silhouette rather than as anatomy. Three things are doing the work:

    * **It has no feet.** The lowest rings taper to almost nothing at the floor, so the
      figure meets the ground the way smoke does. A thing with no feet did not walk here.
    * **It is flat.** ``half_y`` is roughly half ``half_x`` all the way up, so from the
      front it is a person and from the side it is a sheet. §05 is first person with a
      22° cone, so the player almost always sees it front-on — and the one time they
      circle it, it thins to nothing, which is worse than either view alone.
    * **The head is a stub.** No jaw, no crown, no lenses. ``MonsterSkin`` lights
      ``Monster_Eyes`` because §04's 관측자 reads a *facing* from two separated points at
      15 m; giving this a face would hand the player the one cue the monster's design
      spends its whole budget on, from a thing that has nothing to tell them.
    """
    h = FIGURE_HEIGHT
    sx = SHOULDER_WIDTH * 0.5
    return [
        (0.000 * h, 0.026, 0.018),   # where it meets the floor — almost nothing
        (0.040 * h, 0.050, 0.032),
        (0.120 * h, 0.058, 0.038),   # calves, such as they are
        (0.260 * h, 0.052, 0.034),   # knee
        (0.410 * h, 0.072, 0.045),   # thigh
        (0.490 * h, 0.098, 0.056),   # hip
        (0.560 * h, 0.082, 0.047),   # waist — the concavity a primitive stack cannot make
        (0.645 * h, 0.112, 0.057),
        (0.725 * h, 0.136, 0.064),   # ribs
        (0.800 * h, sx, 0.068),      # shoulders
        (0.838 * h, 0.100, 0.055),   # the drop to the neck
        (0.866 * h, 0.045, 0.038),   # neck
        (0.912 * h, 0.086, 0.074),   # head, a stub
        (0.962 * h, 0.072, 0.064),
        (0.985 * h, 0.038, 0.034),   # a rounded crown, not a point — the first preview
        (1.000 * h, 0.000, 0.000),   # tapered to a flame, which read as a candle
    ]


def arm_profile(side: float) -> list[tuple[float, float, float]]:
    """One arm, hanging dead straight. ``side`` is -1 or +1.

    Straight down and slightly away from the body. The monster's arms are elongated and
    its clips pump them; these do nothing at all, and stillness is the whole read.
    """
    h = FIGURE_HEIGHT
    x = side * (SHOULDER_WIDTH * 0.5 + 0.020)
    return [
        (0.400 * h, 0.020, 0.017),   # the hand, unresolved
        (0.470 * h, 0.028, 0.023),
        (0.590 * h, 0.031, 0.026),   # elbow
        (0.720 * h, 0.038, 0.032),
        (0.790 * h, 0.046, 0.040),   # shoulder joint
    ], x


def build_core() -> bpy.types.Object:
    """The 형상's solid part — torso, head and two arms, joined into one mesh."""
    body = loft("PresenceBody", figure_profile(), segments=14)

    limbs = []
    for side in (-1.0, 1.0):
        rings, x = arm_profile(side)
        arm = loft(f"PresenceArm{int(side)}", rings, segments=8)
        arm.location = Vector((x, 0.0, 0.0))
        blendkit.apply_transforms(arm, location=True, scale=False)
        limbs.append(arm)

    core = blendkit.join([body] + limbs, "Presence_Core")
    blendkit.shade_smooth(core, angle_degrees=42.0)
    blendkit.uv_smart_project(core, angle_limit=66.0)
    return core


def scatter_grain(core: bpy.types.Object, rng: np.random.Generator) -> bpy.types.Object:
    """Places the flakes off the core's own surface and returns them as one mesh.

    Sampling the core rather than authoring positions is the same lesson
    ``gen_monster_ai.py`` records about the monster's eyes: the previous attempt used
    hand-written coordinates and put a lens 15 cm inside the head, and the only symptom
    was that a brightness sweep moved the measured frame by 0.0003. A flake buried inside
    the void material is invisible and silent in exactly the same way.
    """
    mesh = core.data
    mesh.calc_loop_triangles()
    tris = mesh.loop_triangles
    if not tris:
        blendkit.fail("Presence_Core has no triangles to scatter grain over")

    verts = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float64)
    idx = np.array([t.vertices for t in tris], dtype=np.int64)
    normals = np.array([tuple(t.normal) for t in tris], dtype=np.float64)

    a, b, c = verts[idx[:, 0]], verts[idx[:, 1]], verts[idx[:, 2]]
    areas = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)

    # The vertical bias: a triangle's chance of carrying a flake rises with its height up
    # the figure. GRAIN_TOP_BIAS = 2.4 puts roughly four times as many flakes on the top
    # third as on the bottom third, which is what makes the thing dissolve downward.
    centroid_z = (a[:, 2] + b[:, 2] + c[:, 2]) / 3.0
    height01 = np.clip(centroid_z / FIGURE_HEIGHT, 0.0, 1.0)
    weight = areas * np.power(0.08 + 0.92 * height01, GRAIN_TOP_BIAS)
    weight = weight / weight.sum()

    picks = rng.choice(len(tris), size=GRAIN_COUNT, p=weight)

    bm = bmesh.new()
    half = GRAIN_SIZE * 0.5

    for tri_index in picks:
        # Uniform barycentric point on the chosen triangle.
        r1, r2 = rng.random(), rng.random()
        s = math.sqrt(r1)
        p = (1.0 - s) * a[tri_index] + s * (1.0 - r2) * b[tri_index] + s * r2 * c[tri_index]

        n = normals[tri_index]
        length = float(np.linalg.norm(n))
        n = n / length if length > 1e-9 else np.array([0.0, -1.0, 0.0])

        standoff = rng.uniform(*GRAIN_STANDOFF)
        centre = Vector(tuple(p + n * standoff))

        # Two arbitrary axes perpendicular to the normal, then a random twist about it,
        # so the flakes do not all face the same way and the crowd never reads as a
        # regular pattern.
        nv = Vector(tuple(n))
        helper = Vector((0.0, 0.0, 1.0)) if abs(nv.z) < 0.9 else Vector((1.0, 0.0, 0.0))
        u = nv.cross(helper).normalized()
        v = nv.cross(u).normalized()
        twist = rng.uniform(0.0, math.pi)
        u2 = (u * math.cos(twist) + v * math.sin(twist)).normalized()
        v2 = nv.cross(u2).normalized()

        scale = rng.uniform(0.55, 1.35)
        du = u2 * (half * scale)
        dv = v2 * (half * scale * rng.uniform(0.35, 1.0))

        quad = [
            bm.verts.new(tuple(centre - du - dv)),
            bm.verts.new(tuple(centre + du - dv)),
            bm.verts.new(tuple(centre + du + dv)),
            bm.verts.new(tuple(centre - du + dv)),
        ]
        bm.faces.new(quad)

    bm.normal_update()
    grain_mesh = bpy.data.meshes.new("Presence_GrainMesh")
    bm.to_mesh(grain_mesh)
    bm.free()

    obj = bpy.data.objects.new("Presence_Grain", grain_mesh)
    bpy.context.collection.objects.link(obj)
    blendkit.uv_smart_project(obj, angle_limit=89.0)
    return obj


def build_figure() -> bpy.types.Object:
    """Core plus grain, in one object with two material slots, origin at the floor."""
    rng = np.random.default_rng(SEED)

    core = build_core()
    grain = scatter_grain(core, rng)

    void_mat = blendkit.make_material(blendkit.MaterialSpec(
        name=VOID_MATERIAL,
        color=tuple(MATERIALS[0]["color"]),
        roughness=MATERIALS[0]["roughness"],
        metallic=MATERIALS[0]["metallic"],
    ))
    grain_mat = blendkit.make_material(blendkit.MaterialSpec(
        name=GRAIN_MATERIAL,
        color=tuple(MATERIALS[1]["color"]),
        roughness=MATERIALS[1]["roughness"],
        metallic=MATERIALS[1]["metallic"],
        emission=MATERIALS[1]["emission"],
    ))

    blendkit.assign_material(core, void_mat)
    blendkit.assign_material(grain, grain_mat)

    # Joined rather than parented: one mesh, two slots. PresenceView instantiates this
    # hundreds of times over a match and a two-object prefab doubles the draw calls for
    # nothing — and the two halves must never be able to drift apart in a scene.
    figure = blendkit.join([core, grain], "Presence_Figure")
    blendkit.triangulate(figure)
    return figure


def build_mote() -> bpy.types.Object:
    """One free flake, for the gathering stage.

    Six triangles rather than two. A flat quad vanishes edge-on, and the motes are placed
    all round the player at the fringe of the beam — a third of them would be invisible
    for a third of every turn, which reads as flicker rather than as grain.
    """
    r = MOTE_SIZE * 0.5
    bm = bmesh.new()

    top = bm.verts.new((0.0, 0.0, r * 0.55))
    bottom = bm.verts.new((0.0, 0.0, -r * 0.35))
    ring = [
        bm.verts.new((r * math.cos(a), r * 0.62 * math.sin(a), 0.0))
        for a in (0.0, 2.0 * math.pi / 3.0, 4.0 * math.pi / 3.0)
    ]

    for i in range(3):
        j = (i + 1) % 3
        bm.faces.new((top, ring[i], ring[j]))
        bm.faces.new((bottom, ring[j], ring[i]))

    bm.normal_update()
    mesh = bpy.data.meshes.new("Presence_MoteMesh")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new("Presence_Mote", mesh)
    bpy.context.collection.objects.link(obj)
    blendkit.uv_smart_project(obj, angle_limit=89.0)

    dust_mat = blendkit.make_material(blendkit.MaterialSpec(
        name=DUST_MATERIAL,
        color=tuple(MATERIALS[2]["color"]),
        roughness=MATERIALS[2]["roughness"],
        metallic=MATERIALS[2]["metallic"],
        emission=MATERIALS[2]["emission"],
    ))
    blendkit.assign_material(obj, dust_mat)

    return obj


# ── Audio ───────────────────────────────────────────────────────────────────

RATE = 48000
"""48 kHz PCM WAVE. AssetImportPolicy reads the RIFF header for length and refuses to
guess; anything else here would be an import written on a fabricated number."""

AUDIO_DIR = os.path.join(blendkit.UNITY_ASSETS, "Audio", "Presence")


def periodic_noise(seconds: float, rng: np.random.Generator,
                   low_hz: float, high_hz: float, tilt: float = 0.0) -> np.ndarray:
    """Band-limited noise that is *exactly* periodic over ``seconds``.

    Built in the frequency domain from integer multiples of ``1/seconds`` with random
    phase, so the last sample joins the first with no discontinuity at all. This is the
    reason none of these loops can develop the wrap hole ``verify_audio.py`` reports on
    ``flare_burn_loop.wav`` — there is no wrap to have a hole in.
    """
    n = int(round(seconds * RATE))
    bins = n // 2 + 1
    freqs = np.arange(bins) * (RATE / n)

    mag = np.zeros(bins)
    band = (freqs >= low_hz) & (freqs <= high_hz)
    mag[band] = 1.0

    # Soft shoulders, so the band does not ring.
    edge = max(1, int(bins * 0.01))
    kernel = np.hanning(edge * 2 + 1)
    kernel /= kernel.sum()
    mag = np.convolve(mag, kernel, mode="same")

    with np.errstate(divide="ignore"):
        slope = np.where(freqs > 0.0, np.power(np.maximum(freqs, 1.0), tilt), 0.0)
    mag = mag * slope

    phase = rng.uniform(0.0, 2.0 * math.pi, bins)
    phase[0] = 0.0
    if n % 2 == 0:
        phase[-1] = 0.0

    spectrum = mag * np.exp(1j * phase)
    out = np.fft.irfft(spectrum, n=n)
    peak = np.max(np.abs(out))
    return out / peak if peak > 0.0 else out


def periodic_tone(seconds: float, hz: float, phase: float = 0.0) -> np.ndarray:
    """A sine snapped to the nearest frequency with a whole number of cycles in the loop."""
    n = int(round(seconds * RATE))
    cycles = max(1, int(round(hz * seconds)))
    t = np.arange(n) / RATE
    return np.sin(2.0 * math.pi * (cycles / seconds) * t + phase)


def normalise(x: np.ndarray, peak_dbfs: float) -> np.ndarray:
    """Scales to a peak, in dBFS. Everything here is quiet on purpose — see write_wav."""
    top = np.max(np.abs(x))
    if top <= 0.0:
        return x
    return x * (10.0 ** (peak_dbfs / 20.0)) / top


def write_wav(path: str, samples: np.ndarray) -> str:
    """Writes 48 kHz 16-bit mono PCM.

    Mono, always. ASSETS.md §1 records the one setting that silently deletes §04's
    청음사 — Unity does not spatialise a stereo clip and reports nothing — and although
    these four are deliberately non-positional, a family that ships mono cannot be broken
    by a later decision to make one of them positional.
    """
    os.makedirs(os.path.dirname(path), exist_ok=True)
    clipped = np.clip(samples, -1.0, 1.0)
    pcm = (clipped * 32767.0).astype(np.int16)

    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())

    return path


def clip_gathering(rng: np.random.Generator) -> np.ndarray:
    """고임 — 8 s loop. The bed that was not there a minute ago.

    Almost entirely below 200 Hz. It has to be something a player notices having been
    there rather than something they notice arriving, and it has to sit under §12's zone
    beds without masking a footstep, because a footstep is §04's whole channel.
    """
    seconds = 8.0
    n = int(seconds * RATE)
    t = np.arange(n) / RATE

    sub = periodic_tone(seconds, 38.0) * 0.55
    sub += periodic_tone(seconds, 57.0, phase=1.1) * 0.22

    # Two beats an eight-second loop: slow enough to feel like breathing rather than
    # like a tremolo effect.
    breath = 0.62 + 0.38 * (0.5 - 0.5 * np.cos(2.0 * math.pi * (2.0 / seconds) * t))
    body = periodic_noise(seconds, rng, 20.0, 190.0, tilt=-0.6) * 0.85

    out = (sub * 0.5 + body) * breath
    return normalise(out, -19.0)


def clip_close(rng: np.random.Generator) -> np.ndarray:
    """임박 — 6 s loop. The warning, and the only thing the player is ever told.

    The sub comes up and a narrow band of hiss crawls in on top of it. The hiss is where
    the information is: it is the first sound in the mix that is not a place, a footstep
    or a machine, so it cannot be confused with the building.
    """
    seconds = 6.0
    n = int(seconds * RATE)
    t = np.arange(n) / RATE

    sub = periodic_tone(seconds, 41.0) * 0.7 + periodic_tone(seconds, 82.0, phase=0.4) * 0.18
    body = periodic_noise(seconds, rng, 24.0, 240.0, tilt=-0.5) * 0.7

    crawl = periodic_noise(seconds, rng, 1900.0, 4400.0, tilt=-0.2)
    # 7.5 Hz — 45 whole cycles in six seconds, so the tremolo wraps exactly too.
    tremolo = 0.35 + 0.65 * (0.5 - 0.5 * np.cos(2.0 * math.pi * (45.0 / seconds) * t))
    crawl = crawl * tremolo * 0.30

    swell = 0.55 + 0.45 * (0.5 - 0.5 * np.cos(2.0 * math.pi * (1.0 / seconds) * t))

    out = (sub * 0.5 + body) * swell + crawl
    return normalise(out, -13.0)


def clip_taken(rng: np.random.Generator) -> np.ndarray:
    """빼앗김 — 3 s one-shot. The moment the voice goes.

    A swallow, then near-silence with a tone in it. The silence is the point: §06 already
    argues that "침묵이 가장 무서운 소리다" for the monster's 정지, and this is that
    argument turned on the player — the room does not go quiet, *they* do.
    """
    seconds = 3.0
    n = int(seconds * RATE)
    t = np.arange(n) / RATE

    # 0.35 s downward glide, 300 Hz → 40 Hz, with a noise burst riding it.
    glide_len = int(0.35 * RATE)
    gt = t[:glide_len]
    sweep_hz = 300.0 * np.power(40.0 / 300.0, gt / gt[-1])
    phase = 2.0 * math.pi * np.cumsum(sweep_hz) / RATE
    swallow = np.zeros(n)
    swallow[:glide_len] = np.sin(phase) * np.linspace(1.0, 0.15, glide_len)

    burst = np.zeros(n)
    burst_len = int(0.22 * RATE)
    burst[:burst_len] = (rng.standard_normal(burst_len)
                         * np.power(np.linspace(1.0, 0.0, burst_len), 2.2) * 0.5)

    # The tinnitus that is left. 3.1 kHz, quiet, decaying over the rest of the clip —
    # it is the sound of having been shut off rather than of anything in the room.
    ring = np.sin(2.0 * math.pi * 3100.0 * t) * 0.11 * np.exp(-t * 1.15)
    ring[:glide_len] *= np.linspace(0.0, 1.0, glide_len)

    out = swallow * 0.9 + burst + ring
    return normalise(out, -9.0)


def clip_return(rng: np.random.Generator) -> np.ndarray:
    """돌아옴 — 1.6 s one-shot. The voice coming back, and the certainty not.

    Deliberately unresolved. It rises and then simply stops rather than landing on a
    note, because §03's smear outlasts the silence by design and the sound should not
    say "that is over".
    """
    seconds = 1.6
    n = int(seconds * RATE)
    t = np.arange(n) / RATE

    lift = np.sin(2.0 * math.pi * 3100.0 * t) * 0.10 * np.exp(-t * 3.4)
    breath = rng.standard_normal(n) * 0.20
    # One-pole low-pass that opens over the clip: a room coming back, not a whoosh.
    cutoff = np.linspace(0.02, 0.20, n)
    filtered = np.zeros(n)
    acc = 0.0
    for i in range(n):
        acc += cutoff[i] * (breath[i] - acc)
        filtered[i] = acc

    envelope = np.power(np.linspace(0.0, 1.0, n), 0.7) * np.linspace(1.0, 0.25, n)
    out = filtered * envelope * 3.0 + lift
    return normalise(out, -15.0)


def build_audio() -> list[tuple[str, float, float]]:
    """Writes the four clips. Returns (path, seconds, peak dBFS) for the report."""
    rng = np.random.default_rng(SEED)

    written = []
    for name, samples in (
        ("pre_gathering_loop.wav", clip_gathering(rng)),
        ("pre_close_loop.wav", clip_close(rng)),
        ("pre_taken.wav", clip_taken(rng)),
        ("pre_return.wav", clip_return(rng)),
    ):
        path = write_wav(os.path.join(AUDIO_DIR, name), samples)
        peak = 20.0 * math.log10(max(1e-9, float(np.max(np.abs(samples)))))
        written.append((path, len(samples) / RATE, peak))

        if name.endswith("_loop.wav"):
            verify_loop(name, samples)

    return written


def verify_loop(name: str, samples: np.ndarray) -> None:
    """Asserts the wrap is a seam and not a click.

    <b>Measured as a discontinuity, not as an RMS ratio.</b> The obvious check — compare
    the RMS of a window straddling the wrap against a window from the middle — was tried
    first and reported +3.2 dB on a clip that is periodic by construction. It was right
    about the number and wrong about what the number means: a 40 ms window of 20–190 Hz
    noise holds about seven independent samples, so its local RMS wanders by several dB
    for entirely innocent reasons. An amplitude *envelope* mismatch is what
    ``verify_audio.py`` catches on ``flare_burn_loop.wav`` (−9.7 dB, a real hole); a
    constructed-periodic bed cannot have one, and pretending otherwise would have meant
    tuning the loop until a meaningless statistic agreed.

    What a listener actually hears at a bad wrap is the step between the last sample and
    the first. So that is what is measured, against the largest step the clip already
    contains — if the join is no worse than the loudest transient inside the loop, there
    is nothing there to hear.
    """
    interior = np.abs(np.diff(samples))
    wrap_step = abs(float(samples[0]) - float(samples[-1]))
    worst_interior = float(np.max(interior))

    if worst_interior <= 0.0:
        blendkit.fail(f"{name}: the clip is silent")

    ratio = wrap_step / worst_interior
    if ratio > 1.0:
        blendkit.fail(f"{name}: the wrap step is {ratio:.2f}x the largest step inside the "
                      "loop, so the join is the loudest transient in the clip — that is a "
                      "click. periodic_noise and periodic_tone are supposed to make it "
                      "impossible, so something aperiodic was mixed in.")

    print(f"  loop wrap {name:<26} step {wrap_step:.5f} vs worst interior "
          f"{worst_interior:.5f}  ({ratio:.2f}x, limit 1.00)")


# ── Manifest and reporting ──────────────────────────────────────────────────


def write_manifest(figure_tris: int, mote_tris: int, clips: list[tuple[str, float, float]]) -> str:
    """The single description of the 그늘's surfaces, read by ``PresenceSkin.cs``.

    Same contract as ``Props.manifest.json``: FBX loses metallic and emission entirely, so
    the values authored on the Principled BSDF have to travel beside the mesh or the Unity
    binder is guessing. An emission of 0 on ``Presence_Grain`` is the failure that matters
    — the figure would still be there and nobody would ever see it.
    """
    path = os.path.join(blendkit.MODELS_DIR, "Presence", "Presence.manifest.json")
    os.makedirs(os.path.dirname(path), exist_ok=True)

    payload = {
        "generator": "tools/blender/gen_presence.py",
        "note": ("The 그늘 (§10). A condition, not a pursuer — see Core/Presence. Material "
                 "values as authored on the Principled BSDF; FBX carries neither metallic "
                 "nor emission, so PresenceSkin rebuilds URP Lit from these."),
        "materials": list(MATERIALS),
        "models": [
            {
                "name": "Presence_Figure",
                "file": "Presence_Figure.fbx",
                "height_metres": FIGURE_HEIGHT,
                "triangles": figure_tris,
                "materials": [VOID_MATERIAL, GRAIN_MATERIAL],
                "note": ("the 형상 that stands at PresenceStage.Close. No rig, no clips, "
                         "no collider in play — it is looked at, never touched."),
            },
            {
                "name": "Presence_Mote",
                "file": "Presence_Mote.fbx",
                "height_metres": MOTE_SIZE,
                "triangles": mote_tris,
                "materials": [DUST_MATERIAL],
                "note": "one flake, instanced around the player at PresenceStage.Gathering.",
            },
        ],
        "audio": [
            {
                "file": "Assets/Audio/Presence/" + os.path.basename(p),
                "seconds": round(s, 3),
                "peak_dbfs": round(db, 1),
                "positional": False,
            }
            for p, s, db in clips
        ],
        "audio_note": ("Non-positional on purpose. §04 gives the 청음사 one channel — the "
                       "monster's 위치 · 거리 · 이동 방향 by ear — and a second world-space "
                       "emitter would compete for it and take the role apart with nothing "
                       "reporting a fault. The 그늘 has no position in Core either; see "
                       "PresenceTests.The그늘HasNoPositionAnywhereInItsApi."),
    }

    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=2)
        f.write("\n")

    return path


def main() -> None:
    blendkit.reset_scene()

    figure = build_figure()
    figure_report_path = blendkit.out_path("Presence", "Presence_Figure.fbx")
    blendkit.export_fbx(figure_report_path, [figure])
    figure_report = blendkit.describe(figure_report_path)
    blendkit.assert_asset(figure_report, min_vertices=200, max_triangles=TRI_BUDGET,
                          max_dimension=3.0)
    blendkit.print_report(figure_report)

    verify_figure(figure_report)

    # blendkit.describe measures the whole scene rather than the exported selection, so
    # the figure has to leave before the mote is measured or the mote reports 1,582
    # triangles and a two-metre bounding box.
    bpy.data.objects.remove(figure, do_unlink=True)

    mote = build_mote()
    mote_path = blendkit.out_path("Presence", "Presence_Mote.fbx")
    blendkit.export_fbx(mote_path, [mote])
    mote_report = blendkit.describe(mote_path)
    blendkit.assert_asset(mote_report, min_vertices=4, max_triangles=32, max_dimension=0.2)

    print("\n[presence] audio — four clips, none of them positional")
    clips = build_audio()
    for path, seconds, peak in clips:
        print(f"  {os.path.basename(path):<26} {seconds:5.2f} s  peak {peak:6.1f} dBFS")

    manifest = write_manifest(figure_report.triangles, mote_report.triangles, clips)

    print("\n[presence] 그늘 built")
    print(f"  figure     {figure_report.triangles} tris, "
          f"{figure_report.size[2]:.3f} m tall, {figure_report.materials} materials")
    print(f"  mote       {mote_report.triangles} tris, {max(mote_report.size):.3f} m across")
    print(f"  manifest   {os.path.relpath(manifest, blendkit.REPO_ROOT)}")
    print(f"  audio      {len(clips)} clips in Assets/Audio/Presence/")


def verify_figure(report: blendkit.AssetReport) -> None:
    """Asserts the three things about the figure that a render cannot re-establish cheaply."""
    # blendkit.describe measures the Blender scene, which is Z-up. The -Z/+Y swap
    # happens inside the FBX writer and is not visible here.
    height = report.size[2]
    if abs(height - FIGURE_HEIGHT) > 0.02:
        blendkit.fail(f"Presence_Figure is {height:.3f} m, not {FIGURE_HEIGHT} m. It has to sit "
                      "between the player's 1.750 and the monster's 2.336 — a figure that reads "
                      "as either one of those is the failure this asset is authored against.")

    if report.materials != 2:
        blendkit.fail(f"Presence_Figure carries {report.materials} material(s), not 2. "
                      "PresenceSkin lights Presence_Grain and only Presence_Grain; on a "
                      "one-material figure that write either does nothing or turns the whole "
                      "shape into a lantern — the same failure gen_monster_ai.py records for "
                      "Monster_Maw.")

    if report.bones or report.actions:
        blendkit.fail("Presence_Figure has a rig or clips. It must not: §01 keeps its horror "
                      "with one pursuer, and the first thing that would make this read as a "
                      "second one is it moving.")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_presence.py raised:\n" + traceback.format_exc())
