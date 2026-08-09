#!/usr/bin/env python3
"""Generates the set-dressing kit — the props that turn a level into a place.

Run headless::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_dressing.py

Optional filter while iterating (everything after ``--`` is a name substring)::

    ... --python tools/blender/gen_dressing.py -- Puddle Sign

Outputs one FBX per piece into ``Assets/Models/Dressing/`` plus
``Dressing.manifest.json``, which is the contract the Unity scatter tool
(``Assets/Scripts/Editor/Dressing/``) reads. Nothing about a piece's size or
placement rule is restated in C#: it is measured here, written to the manifest,
and consumed there. A re-export that changes a footprint therefore changes where
the scatterer will and will not put it, instead of leaving a stale number in a
second file.


WHY THIS KIT EXISTS
===================

**Empty corridors are not neutral, they are a mechanical hole.** §12 derives the
whole escape structure from line of sight: 어그로 해제 needs 3 s of broken sight
line, 시야 차단 지점 간격 is fixed at 15~25 m, and the 주자 테스트 grades the map on
whether that actually works from ten arbitrary points. A corridor made of bare
walls has cover only where the architecture bends. Dressing is where the rest of
those blockers come from — which is why the tall pieces here are sized against
standing eye height and against `GameConstants.LineOfSightBreakSpacingMin`, not
against a mood board.

**§03 makes light the mechanic, so this kit is really about albedo.** The
building is lit by a 12 m flashlight cone in near-black ambient. A surface only
exists if it returns light, so the kit deliberately carries the brightest
materials in the project — galvanised pipe, dust sheets, loose paper, enamel
signage and standing water — and the flashlight is what finds them. A corridor
dressed only in browns and rusts stays exactly as invisible as an empty one.

**Water is atmosphere now, not information.** It used to be §03's worked example
of a clue — *"그것은 물이 있는 층에 있다"* — so puddles, drips, stains and a floor
drain were the thing a clue could point at. 단서 is deleted and the destination
is announced at the start, so they are back to being what they look like: the
kit's only mirror, and the reason a torch beam has somewhere to bounce. The
scatter tool still concentrates them in one zone, which now buys a storey a
memorable look rather than a name.

**§12 makes floor material a gameplay channel**, and the Listener has to be able
to learn it. The floor tint is somebody else's file, but which *props* a zone
gets is this one's, and four distinct dressing palettes (storage timber /
institutional steel / wet gravel plant / utility concrete) give a player a second,
visual way to know which zone they are standing in.


THE SIGNAGE SET USED TO BE §03's CONFUSION PAIRS
================================================

Four pieces — Dress_PipeLabel, Dress_HangingSign, Dress_WallSign,
Dress_DoorPlate — each carried a single flat ``Clue_Face`` quad mapped exactly
0..1, the seam §13's host-rendered glyph landed on, and each was *built* for one
misread condition: a pipe label symmetric under a half-turn about its own face
normal so 6 is 9 from the far end of the corridor; a double-sided hanging sign
with the back face's UVs mirrored so 좌 is 우 depending which way you came; a
glossy enamel field that blows out under a close beam for ㅁ↔ㅇ; a 0.18 m plate
read at a glancing angle for 1↔7. Two of the four were asserted to a millimetre.

All of it is deleted. 경주는 목적지를 처음부터 알려 준다 — there is no chain to
narrow, no glyph to stamp, and therefore nothing a runner can misread. What is
left is the *plate*: painted steel, an enamel field, bolts, chains. A basement
with signage in it looks like a basement, which is §12's job for the kit, and
the scatter tool no longer stamps 526 readable surfaces into a race.


CONVENTIONS THIS FILE GUARANTEES
================================

* **1 Blender unit = 1 metre = 1 Unity unit**, checked per piece against a
  declared size, exactly as `gen_props.py` does.
* **Pivot**, by mount, so the scatter tool never carries an offset:

  ======== =========================================================
  FLOOR    origin on the floor under the footprint centre (min z = 0)
  WALL     origin on the wall plane (max y = 0), centred in x
  CEILING  origin on the ceiling plane (max z = 0), centred in x and y
  CORNER   origin at the wall/wall/ceiling corner (max x, y, z = 0)
  ======== =========================================================

* **Facing −Y**, matching `gen_props.py` and the monster generator, which
  `export_fbx`'s ``axis_forward='-Z'`` turns into +Z forward in Unity. A floor
  piece meant to stand against a wall has its **back at +Y**, so the scatter tool
  faces it away from the wall with a plain yaw.
* **One mesh, one object, identity transform** per FBX. Material names are the
  seam Unity binds against; the manifest lists them so the scatter tool can build
  real URP materials rather than inheriting whatever the FBX importer guesses.
"""

from __future__ import annotations

import json
import math
import os
import struct
import sys
import traceback
import zlib
from dataclasses import dataclass, field
from typing import Callable

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402  (bundled with Blender's Python)
from mathutils import Euler, Matrix, Vector  # noqa: E402

import blendkit  # noqa: E402
import gen_props  # noqa: E402
from blendkit import MaterialSpec  # noqa: E402
from gen_props import Frame, PropBuild, bbox_size, world_bbox  # noqa: E402


# ── Assumptions NOT taken from the design document ──────────────────────────
# Same treatment as gen_props.py's block: printed with the report so a real
# number can replace them. §12 fixes corridor *length* and zone diagonals but
# never states a corridor width or a body size, and §05 never states eye height.

EYE_HEIGHT_ASSUMED = 1.63
"""Standing eye height, metres. Matches the camera rig in `SceneShot`. NOT a design number."""

CEILING_CLEAR_ASSUMED = 3.0
"""Corridor clear height from the MapKit manifest (``corridor_clear.height``). A
kit measurement, so ceiling-mounted pieces must hang shorter than this minus
head clearance or a player walks through them."""

HEAD_CLEARANCE_ASSUMED = 0.35
"""Gap left between the lowest point of a ceiling piece and a standing player's
crown. NOT a design number — it is what stops hanging dressing reading as a bug."""

MEASURE_ONLY = os.environ.get("DRESSING_MEASURE_ONLY") == "1"
"""Survey mode: report size mismatches instead of failing on the first one.

Only for retuning proportions, where failing on piece 1 of 37 hides the other 36.
Downgrades the declared-size guard and nothing else."""


# ── Materials ───────────────────────────────────────────────────────────────
# Registered into gen_props' shared table so `PropBuild` resolves them. Names are
# prefixed `Dress_` so they cannot collide with `Prop_*` (§08's temptation and
# efficiency contrast is encoded in those roughness values) or with the
# contractual `Floor_*` namespace blendkit reserves for §12's footstep surfaces.
#
# The value range is the point. §03 lights this building with a 12 m cone in
# near-black ambient, so the kit needs surfaces at the top of the range to be
# visible at all: galvanised pipe at 0.52 albedo, dust sheets at 0.62, paper at
# 0.78, enamel at 0.70. Everything dark in here is dark *against* those.

STEEL_PAINTED = "Dress_SteelPainted"
STEEL_BARE = "Dress_SteelBare"
GALVANISED = "Dress_Galvanised"
COPPER = "Dress_CopperPipe"
RUST_HEAVY = "Dress_RustHeavy"
CONCRETE = "Dress_Concrete"
CONCRETE_BROKEN = "Dress_ConcreteBroken"
BRICK = "Dress_Brick"
TIMBER_PALE = "Dress_TimberPale"
TIMBER_DARK = "Dress_TimberDark"
PLY = "Dress_Ply"
CARD = "Dress_Cardboard"
PAPER = "Dress_Paper"
CLOTH_DUST = "Dress_ClothDust"
CLOTH_STAINED = "Dress_ClothStained"
COBWEB = "Dress_Cobweb"
WATER = "Dress_Water"
WET_STAIN = "Dress_WetStain"
ENAMEL = "Dress_Enamel"
ENAMEL_RED = "Dress_EnamelRed"
GAUGE_FACE = "Dress_GaugeFace"
GLASS_DIRTY = "Dress_GlassDirty"
BULB_DEAD = "Dress_BulbDead"
BULB_LIT = "Dress_BulbLit"
RUBBER = "Dress_Rubber"
GRIME = "Dress_Grime"
BRASS_FITTING = "Dress_BrassFitting"

# Materials carried by the CC0 PolyHaven replacements (see REAL PROPS below).
# Each has real 1024² maps shipped beside the kit and bound by DressingMaterials;
# the flat numbers here are the FALLBACK the binder uses when a map is missing,
# so they approximate each texture's average rather than invent a new look.
BARREL03 = "Dress_Barrel03"
PIPE_GALV01 = "Dress_PipeGalv01"
PIPE_VALVE02 = "Dress_PipeValve02"
CRATE_MIL = "Dress_CrateMilitary"
CAGED_LAMP = "Dress_CagedLamp"
RACK_WORN = "Dress_RackWorn"
GENERATOR_BODY = "Dress_GeneratorBody"

# DELETED: CLUE_FACE. It was `gen_props.CLUE_FACE` — §13's seam, the surface the
# host rendered one clue's glyph onto and stamped here. §03 단서 is deleted:
# 목적지가 이미 알려져 있으니 좁혀 갈 것이 없다. Four Sign pieces carried one each
# and the scatter tool stamped 526 of them into the shipped scene; the enamel
# field they sat on is still there, so a sign is still a sign, with nothing
# written on it that a runner has to read.

REFUSED_MATERIALS: tuple[str, ...] = ("Clue_Face",)
"""Material names a piece may not carry, kept as data because they are a GUARD.

The name is the whole point of this tuple and it is the reason it exists at all.
`DressingManifest.RefuseDeletedSystems` drops any piece that declares a face here or
whose FBX carries the slot, and it did so against a manifest key literally called
`clue_faces` — so the guard that removes §03 was the last thing in the project still
saying 단서 out loud, and `PivotAssetTombstoneTests` read the shipped
`Dressing.manifest.json` and reported it. The manifest key this file writes is now
`refused_faces`, which says what the number is FOR instead of what it once counted.

**One half of that guard is INERT until the C# side is renamed to match, and saying
so here is the point.** `DressingPiece` still declares `public int clue_faces;` and
`RefuseDeletedSystems` still tests `piece.clue_faces > 0`. Unity's `JsonUtility`
leaves a field absent from the JSON at its default, so that test now reads 0 for
every piece, forever, without erroring — the exact shape of a guard that goes quiet
instead of going red. It is not currently hiding anything: the loader's *other* test
walks `piece.materials` for `Clue_Face`, that list is still written in full, and no
piece carries the slot today. But the belt is gone and only the braces are left, so
until `DressingPiece.clue_faces` is renamed `refused_faces` (and line ~129 with it),
this file is writing a number that nothing on the Unity side reads.

**Measured, not asserted.** `emit` counts the polygons whose material slot is one of
these — it does not write a hard 0. A generator that wrote a constant 0 and a loader
that believed it would be a green number nobody verified, which is worse than a red
one. The count is 0 today because the quads are gone from the builders; the tuple is
here so that a re-export which pulls one back in from `gen_props` is caught by the
same number rather than by nobody."""

MATERIALS: dict[str, MaterialSpec] = {
    # Painted steel is metallic=0. Paint is a dielectric coat over the metal, and
    # calling it metallic is not a stylistic choice — with no reflection probe in
    # the basement, URP falls back to the skybox, so every "metal" surface
    # mirrored a dusk sky and the kit's lockers and cabinets came out with white
    # tops in a pitch-dark corridor. The physically correct value fixes the look
    # and the reason at the same time.
    STEEL_PAINTED: MaterialSpec(STEEL_PAINTED, (0.238, 0.252, 0.262), roughness=0.52, metallic=0.0),
    # Bare steel is the kit's specular anchor: a beam sliding along it is the
    # cheapest possible depth cue in a corridor with no textures. It stays a real
    # metal, and it is the only one that is both bright and smooth.
    STEEL_BARE: MaterialSpec(STEEL_BARE, (0.452, 0.462, 0.478), roughness=0.38, metallic=1.0),
    GALVANISED: MaterialSpec(GALVANISED, (0.524, 0.541, 0.556), roughness=0.52, metallic=0.85),
    COPPER: MaterialSpec(COPPER, (0.552, 0.312, 0.182), roughness=0.34, metallic=1.0),
    # Rust is iron oxide — a dielectric. Metallic rust is the single most common
    # material mistake and it reads as wet plastic.
    RUST_HEAVY: MaterialSpec(RUST_HEAVY, (0.262, 0.132, 0.072), roughness=0.95, metallic=0.0),
    CONCRETE: MaterialSpec(CONCRETE, (0.302, 0.300, 0.292), roughness=0.92),
    # Freshly broken concrete is paler than the wall it fell off. That contrast is
    # what makes a rubble pile findable at the edge of a beam.
    CONCRETE_BROKEN: MaterialSpec(CONCRETE_BROKEN, (0.412, 0.404, 0.382), roughness=0.95),
    BRICK: MaterialSpec(BRICK, (0.322, 0.182, 0.142), roughness=0.90),
    TIMBER_PALE: MaterialSpec(TIMBER_PALE, (0.442, 0.342, 0.222), roughness=0.80),
    TIMBER_DARK: MaterialSpec(TIMBER_DARK, (0.162, 0.112, 0.072), roughness=0.82),
    PLY: MaterialSpec(PLY, (0.502, 0.402, 0.262), roughness=0.85),
    CARD: MaterialSpec(CARD, (0.422, 0.322, 0.212), roughness=0.95),
    PAPER: MaterialSpec(PAPER, (0.782, 0.752, 0.682), roughness=0.90),
    # The brightest large surface in the building. A dust sheet over a stack is a
    # silhouette a player reads before the flashlight resolves anything on it.
    CLOTH_DUST: MaterialSpec(CLOTH_DUST, (0.622, 0.602, 0.552), roughness=0.95),
    CLOTH_STAINED: MaterialSpec(CLOTH_STAINED, (0.342, 0.312, 0.262), roughness=0.95),
    COBWEB: MaterialSpec(COBWEB, (0.722, 0.722, 0.702), roughness=0.85),
    # Dark base colour, mirror roughness. A puddle is nearly black until a beam
    # crosses it and then it is the brightest thing in frame — which is exactly
    # what §03 wants of "그것은 물이 있는 층에 있다": you find water by lighting it.
    # 0.16, not 0.05. A perfect mirror is right in principle and wrong on screen:
    # URP has no reflection probe in this building yet, so a puddle falls back to the
    # skybox, and at the grazing angle a standing player sees a floor from, every
    # distant pool turned into a flat white blob of reflected dusk sky. At 0.16 the
    # near pools still mirror the beam — which is the whole read — and the far ones
    # blur into wet floor. It is still the smoothest surface in the kit by a factor
    # of two over the next one.
    WATER: MaterialSpec(WATER, (0.042, 0.052, 0.058), roughness=0.22, metallic=0.0),
    WET_STAIN: MaterialSpec(WET_STAIN, (0.102, 0.112, 0.108), roughness=0.35),
    ENAMEL: MaterialSpec(ENAMEL, (0.702, 0.712, 0.702), roughness=0.30),
    ENAMEL_RED: MaterialSpec(ENAMEL_RED, (0.422, 0.092, 0.072), roughness=0.35),
    GAUGE_FACE: MaterialSpec(GAUGE_FACE, (0.862, 0.842, 0.782), roughness=0.40),
    GLASS_DIRTY: MaterialSpec(GLASS_DIRTY, (0.402, 0.432, 0.442), roughness=0.22, metallic=0.30),
    # A dead bulb still has to catch a beam — most of them are dead, and a corridor
    # of invisible fittings is a corridor with no ceiling.
    BULB_DEAD: MaterialSpec(BULB_DEAD, (0.722, 0.702, 0.642), roughness=0.12),
    # §03: 어둠 = 목표의 잠금장치. A working bulb is emissive but weak, and the
    # scatter tool gives only a minority of them a real light, ranged so the pool
    # never substitutes for the flashlight.
    BULB_LIT: MaterialSpec(BULB_LIT, (1.0, 0.862, 0.622), roughness=0.10, emission=2.2),
    RUBBER: MaterialSpec(RUBBER, (0.062, 0.062, 0.068), roughness=0.85),
    GRIME: MaterialSpec(GRIME, (0.092, 0.086, 0.078), roughness=0.98),
    BRASS_FITTING: MaterialSpec(BRASS_FITTING, (0.482, 0.382, 0.162), roughness=0.34, metallic=1.0),
    # ── Real-prop materials (CC0 PolyHaven, textured — flat values are fallbacks).
    # Painted drums and crates are dielectric paint over the metal/wood (§7.12's
    # rule, same argument as STEEL_PAINTED above); the per-pixel metal that
    # survives scratches lives in the shipped mask map, not in this number.
    BARREL03: MaterialSpec(BARREL03, (0.212, 0.286, 0.352), roughness=0.56, metallic=0.12),
    PIPE_GALV01: MaterialSpec(PIPE_GALV01, (0.402, 0.442, 0.468), roughness=0.55, metallic=0.72),
    PIPE_VALVE02: MaterialSpec(PIPE_VALVE02, (0.382, 0.418, 0.448), roughness=0.58, metallic=0.70),
    CRATE_MIL: MaterialSpec(CRATE_MIL, (0.302, 0.292, 0.202), roughness=0.80, metallic=0.0),
    CAGED_LAMP: MaterialSpec(CAGED_LAMP, (0.202, 0.262, 0.182), roughness=0.55, metallic=0.30),
    RACK_WORN: MaterialSpec(RACK_WORN, (0.478, 0.488, 0.468), roughness=0.50, metallic=0.60),
    GENERATOR_BODY: MaterialSpec(GENERATOR_BODY, (0.522, 0.402, 0.122), roughness=0.60, metallic=0.10),
}

gen_props.register_materials(MATERIALS)


def rads(degrees):
    return gen_props.rads(degrees)


# ══════════════════════════════════════════════════════════════════════════
#  REAL PROPS — six pieces' visuals now come from CC0 PolyHaven scans.
#
#  The procedural kit's whole §03 argument is that a surface only exists when
#  it returns light, and a flat-colour cylinder returns light as one unbroken
#  gradient. The 2 k scans return it the way forty years of basement do:
#  roughness that varies per pixel, paint that thins over rust, stencils.
#  Geometry, pivot, budget and the manifest contract are unchanged — a piece
#  still goes out through the same emit()/export_fbx path as its procedural
#  siblings, and the scatter tool cannot tell which kind it placed.
#
#  Sources are VENDORED under tools/blender/source/props/<id>/ (.blend plus
#  textures, exactly as PolyHaven serves them — see PROVENANCE.json there) so
#  a rebuild needs no network and no scratch dir. CC0 1.0: no attribution
#  required, commercial use fine; recorded in docs/ASSETS.md all the same.
# ══════════════════════════════════════════════════════════════════════════

SOURCE_PROPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "source", "props")


def _source_blend(prop_id: str) -> str:
    path = os.path.join(SOURCE_PROPS, prop_id, prop_id + "_2k.blend")
    if not os.path.exists(path):
        blendkit.fail(f"vendored CC0 source missing: {path} — restore tools/blender/source/props/"
                      f" (PolyHaven '{prop_id}', see PROVENANCE.json)")
    return path


def _source_texture(prop_id: str, filename: str) -> str:
    path = os.path.join(SOURCE_PROPS, prop_id, "textures", filename)
    if not os.path.exists(path):
        blendkit.fail(f"vendored CC0 texture missing: {path}")
    return path


def _append_object(prop_id: str, name: str) -> bpy.types.Object:
    """Appends one object from a vendored .blend and returns it.

    Appending the same name twice is legal (Blender suffixes the copy), so the
    new object is found by set difference rather than by name.
    """
    blend = _source_blend(prop_id)
    before = set(bpy.data.objects)
    bpy.ops.wm.append(filepath=blend + "/Object/" + name,
                      directory=blend + "/Object/", filename=name)
    fresh = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if len(fresh) != 1:
        blendkit.fail(f"append of {prop_id}/{name} produced {len(fresh)} mesh objects, expected 1")
    obj = fresh[0]
    # Bake the source file's own transform in immediately, so every later
    # measurement and placement works in plain world coordinates.
    blendkit.apply_transforms(obj, location=True, rotation=True, scale=True)
    return obj


def _adopt(b: PropBuild, obj: bpy.types.Object, mat_name: str) -> bpy.types.Object:
    """Registers an imported object as a PropBuild part: kit material, no bevel.

    The source's own materials are dropped here on purpose. The FBX carries only
    slot NAMES; Unity rebuilds URP materials from the manifest, and the manifest
    row for `mat_name` is what points at the shipped texture maps.
    """
    blendkit.assign_material(obj, gen_props.mat(mat_name))
    b.parts.append(obj)
    b.nobevel.add(obj.name)
    return obj


def _orient(obj: bpy.types.Object, translate=(0.0, 0.0, 0.0), rot_deg=(0.0, 0.0, 0.0),
            scale=(1.0, 1.0, 1.0), pivot=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    """Applies scale→rotation→translation about `pivot`, baked into the mesh.

    Baking immediately (instead of leaving transforms on the object) means
    world_bbox measurements between composition steps are always honest.
    """
    if isinstance(scale, (int, float)):
        scale = (scale, scale, scale)
    p = Vector(pivot)
    m = (Matrix.Translation(Vector(translate) + p)
         @ Euler(rads(tuple(rot_deg)), "XYZ").to_matrix().to_4x4()
         @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
         @ Matrix.Translation(-p))
    obj.matrix_world = m @ obj.matrix_world
    blendkit.apply_transforms(obj, location=True, rotation=True, scale=True)
    return obj


def _decimate(obj: bpy.types.Object, ratio: float) -> None:
    """Collapse-decimates in place. Deterministic for a given mesh and ratio.

    The scans arrive at 1.5–26 k triangles and the kit budgets 700–2600 per
    piece (§05: dark, first person). The 2 k normal maps are what carry the
    detail the collapse throws away — that trade is the whole point of
    shipping textures with these pieces.
    """
    if ratio >= 1.0:
        return
    mod = obj.modifiers.new("dec", "DECIMATE")
    mod.ratio = ratio
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def _dup(b: PropBuild, obj: bpy.types.Object, mat_name: str) -> bpy.types.Object:
    """A registered copy of an already-adopted part (own mesh, so edits stay local)."""
    twin = obj.copy()
    twin.data = obj.data.copy()
    bpy.context.scene.collection.objects.link(twin)
    return _adopt(b, twin, mat_name)


def _filter_islands(obj: bpy.types.Object, keep) -> int:
    """Deletes every connected island `keep(lo, hi)` rejects; returns kept count.

    Used to harvest a sub-assembly out of a one-mesh scan (the caged lamp's
    body without its authored chains) without hand-editing the CC0 source.
    """
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    parent = list(range(len(bm.verts)))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for edge in bm.edges:
        a, c = find(edge.verts[0].index), find(edge.verts[1].index)
        if a != c:
            parent[a] = c

    groups: dict[int, list] = {}
    for v in bm.verts:
        g = groups.setdefault(find(v.index), [Vector((math.inf,) * 3),
                                              Vector((-math.inf,) * 3), []])
        for k in range(3):
            g[0][k] = min(g[0][k], v.co[k])
            g[1][k] = max(g[1][k], v.co[k])
        g[2].append(v)

    doomed: list = []
    kept = 0
    for lo, hi, verts in groups.values():
        if keep(lo, hi):
            kept += 1
        else:
            doomed.extend(verts)
    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return kept


def _paint_faces_by_image(obj: bpy.types.Object, image_path: str, mat_name: str,
                          threshold: float = 0.20) -> int:
    """Assigns `mat_name` to every face whose UV centre samples bright in `image_path`.

    This is how the caged lamp keeps the bulb-swap contract: the scan is one
    mesh with one material, but `ScatterSession.LightBulb` swaps a slot named
    Dress_BulbDead for Dress_BulbLit, so the glass faces — found through the
    scan's own emissive map — get that slot while the housing keeps its scan.
    """
    img = bpy.data.images.load(image_path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    px = buf.reshape(h, w, 4)
    bpy.data.images.remove(img)

    me = obj.data
    if not me.uv_layers.active:
        blendkit.fail(f"{obj.name}: no UV layer to sample {os.path.basename(image_path)} through")
    uv = me.uv_layers.active.data
    me.materials.append(gen_props.mat(mat_name))
    slot = len(me.materials) - 1
    painted = 0
    for poly in me.polygons:
        cu = cv = 0.0
        for li in poly.loop_indices:
            cu += uv[li].uv[0]
            cv += uv[li].uv[1]
        cu = (cu / poly.loop_total) % 1.0
        cv = (cv / poly.loop_total) % 1.0
        x = min(w - 1, int(cu * w))
        y = min(h - 1, int(cv * h))
        r, g, bl = px[y, x, 0], px[y, x, 1], px[y, x, 2]
        if 0.2126 * r + 0.7152 * g + 0.0722 * bl > threshold:
            poly.material_index = slot
            painted += 1
    return painted


# ── Texture shipping ────────────────────────────────────────────────────────
# The FBX deliberately carries no textures (path_mode is a blendkit decision
# this file does not own, and the binder rebuilds URP materials anyway). What
# ships is one 1024² PNG set per scan under Assets/Models/Dressing/Textures/,
# named in the manifest per MATERIAL — DressingMaterials loads them by path.
#   albedo.png  sRGB base colour     → _BaseMap
#   normal.png  tangent-space GL +Y  → _BumpMap (+_NORMALMAP)
#   mask.png    R=metallic, A=1−rough (smoothness) → _MetallicGlossMap
# Written by hand (fixed zlib level, no Blender colour management) so a rebuild
# is byte-identical, which is the same determinism rule the FBXs live under.

TEXTURE_RECIPES: dict[str, dict] = {
    # `albedo_gain` and `rough_scale` are §03 art direction, not correction:
    # the building is lit by one 12 m torch in near-black ambient, and the kit
    # doctrine (see the MATERIALS block) is that a surface only exists if it
    # returns light. The pipe scans' worn blue-grey paint sits BELOW the
    # darkest corridor wall's 0.21 luminance, which round 1's beam render
    # showed as two invisible lines where the manifest promises "the
    # corridor's depth cue". Gain lifts the albedo toward the procedural
    # galvanised it replaces; the roughness scale restores the along-pipe
    # beam streak that flat 0.52-roughness metal used to give. Both are
    # applied at export so the SHIPPED maps are the judged artefact.
    BARREL03: {
        "dir": "Barrel03",
        "albedo": ("barrel_03", "barrel_03_diff_2k.jpg"),
        "normal": ("barrel_03", "barrel_03_nor_gl_2k.exr"),
        "rough": ("barrel_03", "barrel_03_rough_2k.jpg"),
        "metal": ("barrel_03", "barrel_03_metal_2k.exr"),
    },
    PIPE_GALV01: {
        "dir": "Pipes01",
        "albedo_gain": 1.5,
        "rough_scale": 0.7,
        "albedo": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group01_diff_2k.png"),
        "normal": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group01_nor_gl_2k.png"),
        "rough": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group01_rough_2k.png"),
        "metal": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group01_metal_2k.png"),
    },
    PIPE_VALVE02: {
        "dir": "Pipes02",
        "albedo_gain": 1.5,
        "rough_scale": 0.7,
        "albedo": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group02_diff_2k.png"),
        "normal": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group02_nor_gl_2k.png"),
        "rough": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group02_rough_2k.png"),
        "metal": ("modular_industrial_pipes_01", "modular_industrial_pipes_01_group02_metal_2k.png"),
    },
    CRATE_MIL: {
        "dir": "MilitaryCrate",
        "albedo_gain": 1.18,
        "albedo": ("old_military_crate", "old_military_crate_diff_2k.jpg"),
        "normal": ("old_military_crate", "old_military_crate_nor_gl_2k.exr"),
        "rough": ("old_military_crate", "old_military_crate_rough_2k.exr"),
        "metal": ("old_military_crate", "old_military_crate_metal_2k.exr"),
    },
    CAGED_LAMP: {
        "dir": "CagedLamp",
        "albedo_gain": 1.3,
        "rough_scale": 0.85,
        "albedo": ("caged_hanging_light", "caged_hanging_light_diff_2k.jpg"),
        "normal": ("caged_hanging_light", "caged_hanging_light_nor_gl_2k.exr"),
        "rough": ("caged_hanging_light", "caged_hanging_light_rough_2k.exr"),
        "metal": ("caged_hanging_light", "caged_hanging_light_metal_2k.exr"),
    },
    RACK_WORN: {
        "dir": "MetalRack",
        "albedo": ("worn_metal_rack", "worn_metal_rack_diff_2k.jpg"),
        "normal": ("worn_metal_rack", "worn_metal_rack_nor_gl_2k.exr"),
        "rough": ("worn_metal_rack", "worn_metal_rack_rough_2k.exr"),
        "metal": ("worn_metal_rack", "worn_metal_rack_metal_2k.exr"),
    },
    GENERATOR_BODY: {
        "dir": "Generator",
        "albedo": ("portable_generator", "portable_generator_diff_2k.jpg"),
        "normal": ("portable_generator", "portable_generator_nor_gl_2k.exr"),
        "rough": ("portable_generator", "portable_generator_rough_2k.exr"),
        "metal": ("portable_generator", "portable_generator_metal_2k.exr"),
    },
}

MATERIAL_MAPS: dict[str, dict[str, str]] = {}
"""Per-material map paths (relative to the kit root), filled by export_real_textures
and merged into the manifest's material rows by write_manifest. Procedural
materials never appear here, so their manifest rows are byte-identical to before."""

TEXTURE_SIZE = 1024
"""Shipped texel side. The scans are 2048; §05 (dark, first person) and the 12 m
beam mean the extra octave is invisible, and halving quarters the repo cost."""


def _load_pixels(path: str) -> np.ndarray:
    """An image file as HxWx4 float32, raw values (no colour management).

    Non-Color stops Blender linearising 8-bit files on read, so a JPEG's bytes
    and an EXR's floats both arrive exactly as stored — which is what the PNG
    writer below re-quantises. Albedo therefore stays sRGB-encoded end to end,
    and data maps stay data.
    """
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return buf.reshape(h, w, 4)


def _downscale(px: np.ndarray, side: int) -> np.ndarray:
    """Box-filters to side×side. Source sides are powers of two (PolyHaven 2k)."""
    h, w = px.shape[:2]
    fy, fx = h // side, w // side
    if fy < 1 or fx < 1 or h % side or w % side:
        blendkit.fail(f"texture is {w}x{h}, cannot box-filter to {side}")
    return px.reshape(side, fy, side, fx, 4).mean(axis=(1, 3))


def _write_png(path: str, rgb: np.ndarray, alpha: np.ndarray | None = None) -> int:
    """Writes an 8-bit PNG (RGB, or RGBA when `alpha` given). Deterministic bytes."""
    h, w = rgb.shape[:2]
    rgb8 = np.clip(np.rint(rgb * 255.0), 0, 255).astype(np.uint8)
    if alpha is not None:
        a8 = np.clip(np.rint(alpha * 255.0), 0, 255).astype(np.uint8).reshape(h, w, 1)
        data = np.concatenate([rgb8, a8], axis=2)
        color_type = 6
    else:
        data = rgb8
        color_type = 2
    data = data[::-1]  # Blender buffers are bottom-up; PNG rows are top-down.
    raw = b"".join(b"\x00" + row.tobytes() for row in data)

    def chunk(tag: bytes, body: bytes) -> bytes:
        return (struct.pack(">I", len(body)) + tag + body
                + struct.pack(">I", zlib.crc32(tag + body) & 0xFFFFFFFF))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, color_type, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    with open(path, "wb") as fh:
        fh.write(png)
    return len(png)


def export_real_textures() -> None:
    """Writes every scan's Unity texture set and records the manifest paths."""
    for mat_name, recipe in sorted(TEXTURE_RECIPES.items()):
        folder = recipe["dir"]
        rel = "Textures/" + folder

        albedo = _downscale(_load_pixels(_source_texture(*recipe["albedo"])), TEXTURE_SIZE)
        gain = recipe.get("albedo_gain", 1.0)
        if gain != 1.0:
            albedo = np.clip(albedo * gain, 0.0, 1.0)
        albedo_path = blendkit.out_path("Dressing", "Textures", folder, "albedo.png")
        albedo_bytes = _write_png(albedo_path, albedo[:, :, :3])

        normal = _downscale(_load_pixels(_source_texture(*recipe["normal"])), TEXTURE_SIZE)
        vec = normal[:, :, :3] * 2.0 - 1.0  # renormalise after the box filter
        length = np.maximum(1e-6, np.sqrt((vec * vec).sum(axis=2, keepdims=True)))
        normal_path = blendkit.out_path("Dressing", "Textures", folder, "normal.png")
        normal_bytes = _write_png(normal_path, vec / length * 0.5 + 0.5)

        rough = _downscale(_load_pixels(_source_texture(*recipe["rough"])), TEXTURE_SIZE)
        rough = np.clip(rough * recipe.get("rough_scale", 1.0), 0.0, 1.0)
        metal = _downscale(_load_pixels(_source_texture(*recipe["metal"])), TEXTURE_SIZE)
        mask = np.zeros((TEXTURE_SIZE, TEXTURE_SIZE, 3), dtype=np.float32)
        mask[:, :, 0] = metal[:, :, 0]
        mask_path = blendkit.out_path("Dressing", "Textures", folder, "mask.png")
        mask_bytes = _write_png(mask_path, mask, alpha=1.0 - rough[:, :, 0])

        MATERIAL_MAPS[mat_name] = {
            "albedo_map": rel + "/albedo.png",
            "normal_map": rel + "/normal.png",
            "mask_map": rel + "/mask.png",
        }
        print(f"DRESS_TEXTURES {mat_name} dir={rel} albedo={albedo_bytes}b "
              f"normal={normal_bytes}b mask={mask_bytes}b")


# ── Measurement helpers ─────────────────────────────────────────────────────


# DELETED: `rotational_symmetry_error_axis` and `mirror_symmetry_error`. The
# first measured a half-turn about an arbitrary axis (gen_props' version
# hard-codes Z, and a wall label's face normal is Y); the second measured a
# plate against its own mirror plane. Both existed only to assert §03's 6↔9 and
# 좌↔우 misread conditions to a millimetre. gen_props.rotational_symmetry_error
# is still imported for the Z-axis case and is untouched.
# DELETED: `map_clue_faces`. It grouped a piece's Clue_Face polygons by facing,
# gave each group its own 0..1 UV mapping and flipped U on the group facing +Y —
# the entire 좌↔우 mechanism: one glyph, two sides, one of them reversed. With no
# glyph the mapping has nothing to map and the flip nothing to reverse.
def _case(f: Frame, side: float, height: float, wood: str = TIMBER_PALE,
          batten: str = TIMBER_DARK, lid_pop: bool = False) -> None:
    """One packing case: a body plus corner battens and a lid.

    **Named `_case`, not `_crate`.** 「crate」 is §01's 궤짝 — the two-person 전리품
    piece — and `PivotAssetTombstoneTests` reads asset PATHS, so the word shipped in
    three filenames under `Assets/Models/Dressing/` and the guard was right to say so.
    The GEOMETRY is not the 궤짝 and never was: a stack of boxes against a basement
    wall is §12's 시야 차단 지점, nothing picks it up and nothing is inside it. Only
    the name was wrong, so only the name changed.

    Kept to a handful of primitives because these are stacked three deep and the
    whole stack has to stay inside the per-piece triangle budget §05 justifies
    (dark, first person — detail buys nothing, silhouette buys everything).

    `lid_pop` rotates the lid off its seat with packing straw in the gap — the
    round-1 renders showed a stack of intact cases fusing into one extruded
    monolith, and a popped lid is the cheapest break in that top line.
    """
    t = 0.028
    f.box((side - 0.03, side - 0.03, height - 0.03), (0.0, 0.0, height / 2), mat=wood)
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            f.box((0.052, 0.052, height), (sx * (side / 2 - 0.026), sy * (side / 2 - 0.026),
                                           height / 2), mat=batten, nobevel=True)
    if lid_pop:
        f.box((side, side, t), (0.05, -0.03, height + 0.016), rot=(3.0, 7.0, -6.0),
              mat=wood, nobevel=True)
        # Packing straw pushed out of the open corner.
        for (dx, dy, yaw) in ((0.16, 0.20, 24.0), (0.24, 0.10, -18.0), (0.08, 0.26, 58.0)):
            f.box((0.150, 0.060, 0.010), (dx, dy, height - 0.006), rot=(4.0, 0.0, yaw),
                  mat=PAPER, nobevel=True)
    else:
        f.box((side, side, t), (0.0, 0.0, height - t / 2), mat=wood, nobevel=True)


BARREL_SCALE = 0.9578
"""barrel_03's scanned diameter is 0.639 m; the procedural drum the scatterer has
been packing corridors around was 0.612 m. §12's clear-band arithmetic is done
against the manifest footprint, so the scan is shrunk to the footprint it is
replacing rather than the footprint growing to meet the scan."""


def _real_barrel(b: PropBuild, ratio: float, translate=(0.0, 0.0, 0.0),
                 rot_deg=(0.0, 0.0, 0.0), scale: float = BARREL_SCALE) -> bpy.types.Object:
    """One PolyHaven barrel_03 (CC0), decimated and placed.

    The procedural drum needed hoop/streak/skirt geometry because flat colour
    has no history; the scan's 2 k maps carry the paint, the rust bleed and the
    grime gradient per pixel, so the geometry is just the drum. `rot_deg`
    composes X-tilt→Y-roll→Z-yaw, so (0, 90, yaw) is a drum on its side."""
    obj = _adopt(b, _append_object("barrel_03", "barrel_03"), BARREL03)
    _decimate(obj, ratio)
    _orient(obj, translate=translate, rot_deg=rot_deg, scale=scale)
    return obj


def _real_rack(b: PropBuild, ratio: float) -> bpy.types.Object:
    """PolyHaven worn_metal_rack (CC0), decimated and squashed 0.60 m → 0.42 m deep.

    The squash is a §12/§08 contract, not taste: the scatterer fits this bay
    against a corridor wall by its manifest `mount_depth`, and the procedural
    bay it replaces shipped at 0.42 m. Angle-iron uprights and flat decks
    survive a 30 % depth squash without reading as squashed; growing the
    corridor-side depth by 18 cm would eat the two-runner clear band instead."""
    obj = _adopt(b, _append_object("worn_metal_rack", "worn_metal_rack"), RACK_WORN)
    _decimate(obj, ratio)
    _orient(obj, scale=(1.0, 0.70, 1.0))
    return obj


def _tin(f: Frame, radius: float, height: float, loc, mat_name: str = STEEL_BARE) -> None:
    f.cyl(radius, height, (loc[0], loc[1], loc[2] + height / 2), verts=10, mat=mat_name,
          nobevel=True)
    f.cyl(radius + 0.004, 0.010, (loc[0], loc[1], loc[2] + height - 0.005), verts=10,
          mat=STEEL_BARE, nobevel=True)


PIPE_SOURCE = "modular_industrial_pipes_01"
PIPE_GROUP: dict[str, str] = {
    # Which of the scan's two texture groups each modular segment belongs to.
    # Assigning across groups would put group01's UV island under group02's
    # map — the pipe would render wearing another pipe's rust.
    "modular_industrial_pipes_01_pipe01": PIPE_GALV01,
    "modular_industrial_pipes_01_pipe02": PIPE_GALV01,
    "modular_industrial_pipes_01_pipe03": PIPE_GALV01,
    "modular_industrial_pipes_01_pipe04": PIPE_GALV01,
    "modular_industrial_pipes_01_pipe05": PIPE_VALVE02,
    "modular_industrial_pipes_01_pipe06": PIPE_VALVE02,
    "modular_industrial_pipes_01_pipe07": PIPE_VALVE02,
    "modular_industrial_pipes_01_pipe08": PIPE_VALVE02,
}


def _pipe_line(b: PropBuild, segments: tuple, ratio: float,
               target_len: float) -> tuple[list, float]:
    """Chains scan pipe segments into one straight line along X.

    The modular segments stand vertically in the scan (length along Z, wall
    behind +Y). Each is normalised onto its own axis, laid along +X, butted
    flange-to-opening in order, and the whole line is uniform-scaled to exactly
    `target_len` — the MapKit tiling contract the procedural run also obeyed;
    a run that does not tile leaves a gap at every 2.5 m cell boundary.
    Returns (objects, pipe radius); the axis lies on y=0 / z=0, x centred on 0.
    """
    objs = []
    cursor = 0.0
    for name in segments:
        obj = _adopt(b, _append_object(PIPE_SOURCE, name), PIPE_GROUP[name])
        _decimate(obj, ratio)
        lo, hi = world_bbox([obj])
        # Pipe axis: x-centre of the segment (side outlets are symmetric), and
        # y=0.025 in every scan segment (measured; the wall side is +Y).
        _orient(obj, translate=(-(lo.x + hi.x) / 2.0, -0.025, 0.0))
        _orient(obj, rot_deg=(0.0, 90.0, 0.0))
        lo, hi = world_bbox([obj])
        _orient(obj, translate=(cursor - lo.x, 0.0, 0.0))
        cursor += hi.x - lo.x
        objs.append(obj)
    s = target_len / cursor
    for obj in objs:
        _orient(obj, scale=s)
        _orient(obj, translate=(-target_len / 2.0, 0.0, 0.0))
    return objs, 0.1035 * s


def _plank_scatter(b: PropBuild, planks: tuple, mat_name: str = TIMBER_PALE) -> None:
    for (sx, sy, sz, x, y, z, rot) in planks:
        b.box((sx, sy, sz), (x, y, z), rot=rot, mat=mat_name, nobevel=True)


def _rubble(b: PropBuild, chunks: tuple, mat_name: str = CONCRETE_BROKEN) -> None:
    """Chunks as (sx, sy, sz, x, y, z, pitch, roll, yaw).

    Every chunk used to share one hard-coded (7°, −5°) tilt, and the round-1
    render said so out loud: eight parallel top faces read as boxes from one
    mould, not as a collapsed ceiling. The tilts are data now, and no two
    chunks in a pile repeat one."""
    for (sx, sy, sz, x, y, z, pitch, roll, yaw) in chunks:
        b.box((sx, sy, sz), (x, y, z), rot=(pitch, roll, yaw), mat=mat_name, nobevel=True)


def _chain(b: PropBuild, x: float, y: float, z_top: float, z_bottom: float,
           links: int = 0, radius: float = 0.024) -> None:
    """A hanging chain built from alternating elongated links that OVERLAP.

    The round-1 renders are why this is spelled out: links spaced by span/count
    with a diameter smaller than the step rendered as a dotted line of floating
    rings — a chain where no link touches the next is the single fastest way to
    make a piece read as programmer art. So the link count is now derived from
    the span (`links` is only a floor), each torus is stretched 1.6× along the
    hang so its long axis threads through its neighbours, and the step is ~60 %
    of a link's length. Eight-by-three segment tori: at 2.5 m in a torch beam a
    chain is alternating bright bars, and anything smoother is triangles nobody
    sees.
    """
    span = z_top - z_bottom
    n = max(links, max(2, int(round(span / (radius * 2.6)))))
    step = span / n
    for i in range(n):
        z = z_top - step * (i + 0.5)
        obj = b.torus(radius, 0.006, (x, y, z), rot=(0.0, 0.0, 0.0),
                      mseg=8, nseg=3, mat=STEEL_BARE)
        obj.scale = Vector((1.0, 1.6, 1.0))
        blendkit.apply_transforms(obj, scale=True)
        obj.rotation_euler = Euler(
            (math.radians(90.0), 0.0, math.radians(0.0 if i % 2 else 90.0)), "XYZ")


# ── Stencilled Hangul ───────────────────────────────────────────────────────
# §12 wants a corridor to read as a PLACE, and a Korean basement's walls say
# 위험 and 출입금지 in stencilled paint. No font is licensed for baking, so the
# glyphs are drawn as geometry: each stroke is one flat quad a millimetre proud
# of the enamel, each ㅇ/ㅎ ring one 8-segment torus. Two rules keep this inside
# the deleted-단서 line: the text is IDENTICAL on every instance (it is paint,
# not information), and nothing directional or numeric — no arrows, no 좌/우, no
# storey numbers a racer could misread as wayfinding.
#
# Strokes are ("q", cx, cz, w, h[, tilt°]) quads or ("o", cx, cz, r) rings, in a
# 0..1 glyph square (x right, z up), drawn stencil-fat so they survive a moving
# beam at 8 m.

GLYPH_지 = (("q", 0.34, 0.82, 0.56, 0.10), ("q", 0.22, 0.52, 0.11, 0.46, 20.0),
            ("q", 0.46, 0.52, 0.11, 0.46, -20.0), ("q", 0.82, 0.50, 0.12, 0.88))
GLYPH_하 = (("q", 0.24, 0.93, 0.20, 0.08), ("q", 0.24, 0.80, 0.38, 0.08),
            ("o", 0.24, 0.55, 0.16), ("q", 0.74, 0.50, 0.12, 0.92),
            ("q", 0.88, 0.52, 0.16, 0.09))
GLYPH_위 = (("o", 0.30, 0.76, 0.17), ("q", 0.30, 0.44, 0.56, 0.09),
            ("q", 0.30, 0.29, 0.11, 0.24), ("q", 0.84, 0.50, 0.12, 0.94))
GLYPH_험 = (("q", 0.24, 0.95, 0.16, 0.06), ("q", 0.24, 0.86, 0.34, 0.06),
            ("o", 0.24, 0.68, 0.13), ("q", 0.57, 0.74, 0.14, 0.07),
            ("q", 0.72, 0.70, 0.11, 0.54), ("q", 0.34, 0.45, 0.46, 0.08),
            ("q", 0.34, 0.20, 0.46, 0.08), ("q", 0.14, 0.32, 0.09, 0.32),
            ("q", 0.54, 0.32, 0.09, 0.32))
GLYPH_출 = (("q", 0.40, 0.96, 0.10, 0.06), ("q", 0.40, 0.88, 0.46, 0.06),
            ("q", 0.30, 0.78, 0.10, 0.16, 20.0), ("q", 0.50, 0.78, 0.10, 0.16, -20.0),
            ("q", 0.40, 0.64, 0.58, 0.07), ("q", 0.40, 0.54, 0.10, 0.14),
            ("q", 0.40, 0.44, 0.50, 0.07), ("q", 0.61, 0.37, 0.08, 0.09),
            ("q", 0.40, 0.30, 0.50, 0.07), ("q", 0.19, 0.23, 0.08, 0.09),
            ("q", 0.40, 0.16, 0.50, 0.07))
GLYPH_입 = (("o", 0.30, 0.78, 0.15), ("q", 0.76, 0.76, 0.11, 0.40),
            ("q", 0.22, 0.30, 0.09, 0.36), ("q", 0.58, 0.30, 0.09, 0.36),
            ("q", 0.40, 0.34, 0.30, 0.07), ("q", 0.40, 0.15, 0.45, 0.07))
GLYPH_금 = (("q", 0.50, 0.88, 0.54, 0.08), ("q", 0.71, 0.74, 0.10, 0.22),
            ("q", 0.50, 0.56, 0.62, 0.08), ("q", 0.50, 0.38, 0.46, 0.07),
            ("q", 0.50, 0.13, 0.46, 0.07), ("q", 0.30, 0.25, 0.09, 0.26),
            ("q", 0.70, 0.25, 0.09, 0.26))

TEXT_위험 = (GLYPH_위, GLYPH_험)
TEXT_출입금지 = (GLYPH_출, GLYPH_입, GLYPH_금, GLYPH_지)
TEXT_지하 = (GLYPH_지, GLYPH_하)


def _stencil(b: PropBuild, glyphs: tuple, x0: float, z0: float, s: float,
             y: float, gap: float = 0.18, back: bool = False,
             mat_name: str = GRIME) -> None:
    """Paints a glyph run onto a vertical face.

    `x0`/`z0` is the bottom-left of the first glyph square, `s` the square side,
    `y` the plane the quads sit on. `back=True` renders the run mirrored for the
    +Y face of a double-sided plate, so the text reads correctly from that side
    (a quad is single-sided; the back face needs its own strokes anyway).
    """
    total = len(glyphs) * s + (len(glyphs) - 1) * gap * s
    for gi, glyph in enumerate(glyphs):
        gx = x0 + (gi * (1.0 + gap)) * s
        if back:
            gx = x0 + total - s - (gi * (1.0 + gap)) * s
        for stroke in glyph:
            if stroke[0] == "q":
                _k, cx, cz, w, h = stroke[:5]
                tilt = stroke[5] if len(stroke) > 5 else 0.0
                x = gx + (1.0 - cx if back else cx) * s
                b.quad(w * s, h * s, (x, y, z0 + cz * s),
                       rot=(-90.0 if back else 90.0, -tilt if back else tilt, 0.0),
                       mat=mat_name)
            else:
                _k, cx, cz, r = stroke
                x = gx + (1.0 - cx if back else cx) * s
                b.torus(r * s, max(0.0045, 0.05 * s), (x, y, z0 + cz * s),
                        rot=(90.0, 0.0, 0.0), mseg=8, nseg=3, mat=mat_name)


# ══════════════════════════════════════════════════════════════════════════
#  BULK — storage and furniture. These are the pieces §12's escape maths cares
#  about: anything at or above eye height is a 시야 차단 지점.
# ══════════════════════════════════════════════════════════════════════════


def _crate_closed(b: PropBuild) -> bpy.types.Object:
    """One CLOSED old_military_crate (CC0): body, lid, latch, hasp loop, joined
    and centred on its own footprint. The caller stacks copies via `_dup`.

    Parts are decimated SEPARATELY, at ~570 triangles a unit. Round 1 decimated
    the joined unit to one ratio and the render showed a stack of shredded
    fins: collapse spends its budget by error metric, so the curvy latch
    hardware hoarded triangles while the crate WALLS collapsed first. The
    open-crate builder already worked this way, and its render was intact at
    half the density — per-part ratios are the lesson, not gentler ones."""
    ratios = {"old_military_crate_a": 0.066, "old_military_crate_lid_a": 0.044,
              "old_military_crate_latch_a": 0.055, "old_military_crate_loop_a": 0.055}
    parts = []
    for name, ratio in ratios.items():
        obj = _append_object("old_military_crate", name)
        _decimate(obj, ratio)
        parts.append(obj)
    unit = blendkit.join(parts, "crate_closed_unit")
    _orient(unit, translate=(0.5, 0.0, 0.0))  # the a-set is authored around x=-0.5
    return _adopt(b, unit, CRATE_MIL)


def _crate_open(b: PropBuild) -> bpy.types.Object:
    """The scan's OPEN crate: body, packing cloth, lid leaning behind (+Y), open
    latch hardware. Parts decimated separately — one collapse ratio across a
    box, a cloth and thin hardware wrecks whichever it was not tuned for."""
    ratios = {"old_military_crate_b": 0.075, "old_military_crate_cloth_b": 0.09,
              "old_military_crate_lid_b": 0.045, "old_military_crate_latch_b": 0.06,
              "old_military_crate_loop_b": 0.06}
    parts = []
    for name, ratio in ratios.items():
        obj = _append_object("old_military_crate", name)
        _decimate(obj, ratio)
        if name == "old_military_crate_cloth_b":
            # The scan's cloth flops 9 cm over the crate's front lip, and with
            # the lid leaning off the back that drape sets the whole piece's
            # depth — which is a §12 clearance number. Tucking the drape 20 %
            # toward its hinge line keeps the cloth and loses the centimetres.
            _orient(obj, scale=(1.0, 0.80, 1.0), pivot=(0.0, 0.188, 0.0))
        parts.append(obj)
    unit = blendkit.join(parts, "crate_open_unit")
    _orient(unit, translate=(-0.5, 0.0, 0.0))  # the b-set is authored around x=+0.5
    return _adopt(b, unit, CRATE_MIL)


def build_case_stack_tall() -> PropBuild:
    """Three flat crates with a fourth stood on END on top — a 1.6 m sight break.

    §12 wants 시야 차단 지점 every 15~25 m and §06 needs 3 s of broken line of
    sight for an aggro release; this tower breaks the line just under standing
    eye height without hiding the route. Standing the top crate on end is what
    makes the height honest at four crates' triangle budget — round 1 tried
    six flat-stacked crates and had to shred each one to afford the stack,
    and the render said so. An end-stand only rolls about Y, so every latch
    face still points down the corridor."""
    b = PropBuild("Dress_CaseStack_Tall")
    base = _crate_closed(b)
    lo, hi = world_bbox([base])
    pitch = hi.z * 0.94
    flat2 = _dup(b, base, CRATE_MIL)
    flat3 = _dup(b, base, CRATE_MIL)
    stander = _dup(b, base, CRATE_MIL)
    _orient(base, rot_deg=(0.0, 0.0, 1.6), translate=(0.0, 0.0, 0.0), scale=0.94)
    _orient(flat2, rot_deg=(0.0, 0.0, -2.0), translate=(0.004, -0.008, pitch), scale=0.94)
    _orient(flat3, rot_deg=(0.0, 0.0, 1.2), translate=(-0.004, 0.006, 2 * pitch), scale=0.94)
    # The fourth crate stood on END on the pile — somebody wanted the one
    # under it. Ry(−90) turns the crate's 0.76 m length into height, keeps the
    # latch face on −Y, and its centre lands on the pile's own z-axis; the
    # pivot shift below zeroes the whole tower onto the floor.
    _orient(stander, rot_deg=(0.0, -90.0, -2.4), translate=(0.02, 0.01, 3 * pitch + 0.378),
            scale=0.94)
    b.pivot_part = base
    # Packing cloth flopped out under the third lid, pinned by the stander —
    # the scan authored it for exactly this seam, the tower's one soft line.
    cloth = _adopt(b, _append_object("old_military_crate", "old_military_crate_cloth_a"),
                   CRATE_MIL)
    _decimate(cloth, 0.085)
    _orient(cloth, translate=(0.5, 0.0, 0.0))
    _orient(cloth, rot_deg=(0.0, 0.0, 1.2), translate=(-0.004, 0.006, 2 * pitch), scale=0.94)
    return b


def build_case_stack_low() -> PropBuild:
    """A crate stood on end on a flat one, beside an OPENED crate — §06 cover.

    **Why this is still worth building now that §12's 막힌 길 pay nothing.** What
    survives of the old reason is the shape: around chest height is the one height a
    player can BREAK a creature's line of sight behind without also losing their own
    view of the route out, which is §06's 3 s aggro release bought at no cost to
    §01's race. The opened crate — lid leaning against the column, packing cloth
    thrown back — is the human evidence the dust sheet used to provide, and its
    pale cloth is still the brightest patch in the pile."""
    b = PropBuild("Dress_CaseStack_Low")
    base = _crate_closed(b)
    lo, hi = world_bbox([base])
    pitch = hi.z * 0.92
    # Every copy is taken BEFORE any placement bakes — _orient writes into the
    # mesh, so a late _dup would clone a crate already standing in the column.
    stander = _dup(b, base, CRATE_MIL)
    _orient(base, translate=(-0.36, 0.0, 0.0), rot_deg=(0.0, 0.0, -1.8), scale=0.92)
    # A crate stood on END on the flat one: chest height (§06's 3 s break)
    # from two crates' worth of triangles, exactly the tall stack's trick.
    _orient(stander, rot_deg=(0.0, -90.0, 1.4), translate=(-0.35, 0.008, pitch + 0.370),
            scale=0.92)
    b.pivot_part = base
    # The opened crate beside it, lid leaning back toward +Y (authored that way
    # in the scan) — the human evidence, and its pale cloth is the §03 bright.
    opened = _crate_open(b)
    _orient(opened, translate=(0.36, -0.012, 0.0), rot_deg=(0.0, 0.0, -1.6), scale=0.92)
    return b


def build_case_broken() -> PropBuild:
    """A case that lost an argument with a forklift. Spilled straw and boards.

    Below knee height everywhere: §05 makes backward movement 65 % speed and §06
    gives the monster 4.8 m/s, so dressing a player can trip on is a death written
    by an artist rather than by design."""
    b = PropBuild("Dress_CaseBroken")
    b.box((0.620, 0.580, 0.300), (0.0, 0.0, 0.150), rot=(0.0, 4.0, 0.0), mat=TIMBER_PALE)
    b.pivot_part = b.parts[0]
    for sx in (-1.0, 1.0):
        b.box((0.048, 0.048, 0.320), (sx * 0.290, -0.270, 0.160), mat=TIMBER_DARK, nobevel=True)
    _plank_scatter(b, (
        (0.620, 0.140, 0.026, 0.10, 0.380, 0.014, (0.0, 0.0, 14.0)),
        (0.560, 0.130, 0.024, -0.34, 0.330, 0.040, (0.0, -22.0, -34.0)),
        (0.480, 0.120, 0.024, 0.42, -0.300, 0.012, (0.0, 0.0, 62.0)),
    ))
    # Packing straw and paper — bright, low, catches a floor sweep.
    for (x, y, yaw) in ((0.10, 0.20, 12.0), (-0.16, 0.28, -48.0), (0.30, 0.06, 71.0)):
        b.box((0.300, 0.220, 0.014), (x, y, 0.008), rot=(0.0, 0.0, yaw), mat=PAPER, nobevel=True)
    return b


def build_barrel_upright() -> PropBuild:
    """One scanned steel drum, 0.89 m. The kit's most reusable single silhouette.

    The procedural version earned its keep with a dented lid, relief streaks
    and a grime skirt, because a flat-colour cylinder has no history. The scan
    carries all of that in its maps — blue paint thinning over rust, a grimy
    skirt, a stencilled bung — so the piece is now one drum and nothing else,
    and instance three still does not read as instance one because the wear is
    azimuth-asymmetric and the scatterer yaws every placement."""
    b = PropBuild("Dress_BarrelUpright")
    # The z-stretch is 1.1 % and it is load-bearing: at the scan's own
    # proportions the drum lands at 0.891 m and `breaks_sightline` (0.897 m,
    # 55 % of eye height) silently flips off the kit's most-repeated Bulk
    # piece. Stretching height back to the procedural drum's 0.901 m keeps
    # BOTH manifest contracts — footprint ≤ 0.612 m AND the §12 flag.
    b.pivot_part = _real_barrel(b, ratio=0.46,
                                scale=(BARREL_SCALE, BARREL_SCALE, 0.9686))
    return b


def build_barrel_cluster() -> PropBuild:
    """Three scanned drums, one on its side — a 1.4 m wide block of cover.

    Wide on purpose: §12 asks for cover at 15~25 m spacing and a single drum is
    too narrow to break a corridor's line of sight. The three share one scan but
    never read as copies: each shows the texture from a different yaw, and the
    lying one shows the beam its lid. Decimated harder than the solo drum —
    mid-cluster silhouettes overlap, so the extra edges bought nothing."""
    b = PropBuild("Dress_BarrelCluster")
    b.pivot_part = _real_barrel(b, ratio=0.33, translate=(-0.36, 0.10, 0.0),
                                rot_deg=(0.0, 0.0, 17.0))
    _real_barrel(b, ratio=0.33, translate=(0.28, -0.14, 0.0), rot_deg=(0.0, 0.0, -53.0))
    # The toppled one, lying along X with a slight yaw, its open end at -X.
    _real_barrel(b, ratio=0.33, translate=(-0.22, 0.43, 0.3045), rot_deg=(0.0, 90.0, 8.0))
    # What it spilled, dried dark under the open end.
    b.cyl(0.240, 0.005, (-0.320, 0.38, 0.0025), verts=14, mat=WET_STAIN, nobevel=True)
    b.cyl(0.150, 0.006, (-0.430, 0.52, 0.0030), verts=12, mat=WET_STAIN, nobevel=True)
    return b


def build_barrel_toppled() -> PropBuild:
    """A scanned drum on its side with a wedge under it — walk-over-able cover.

    The chock is the one procedural part left: a drum that stays where it fell
    on a route players sprint through needs a visible reason not to roll."""
    b = PropBuild("Dress_BarrelToppled")
    b.pivot_part = _real_barrel(b, ratio=0.44, translate=(-0.4455, 0.0, 0.3040),
                                rot_deg=(0.0, 90.0, 0.0))
    b.box((0.180, 0.220, 0.070), (0.30, 0.0, 0.035), rot=(0.0, 12.0, 0.0), mat=TIMBER_DARK,
          nobevel=True)
    return b


def build_shelf_stocked() -> PropBuild:
    """The scanned rack, stocked. 0.92 m wide, 1.90 m tall — a §12 sightline blocker.

    The bay itself is PolyHaven's worn_metal_rack; the STOCK stays procedural,
    because the scan ships empty and a bare rack in a storage wing reads as a
    showroom. The clutter follows the old builder's §03 logic: tins and glass
    put small bright specular hits at beam height, cardboard and ledgers break
    the deck lines, and nothing overhangs the squashed 0.42 m depth the
    scatterer fits against the wall."""
    b = PropBuild("Dress_ShelfStocked")
    rack = _real_rack(b, 0.30)
    b.pivot_part = rack
    f = b.frame((0.0, 0.0, 0.0))
    # Deck 1 (top at 0.43) — paint tins and a coil of hose.
    for x in (-0.30, -0.15):
        _tin(f, 0.062, 0.160, (x, 0.02, 0.430))
    f.cyl(0.115, 0.080, (0.24, 0.0, 0.470), verts=12, mat=RUBBER, nobevel=True)
    # Deck 2 (top at 0.92) — cardboard boxes, one crooked.
    f.box((0.280, 0.250, 0.220), (-0.22, 0.01, 1.032), rot=(0.0, 0.0, 5.0), mat=CARD)
    f.box((0.230, 0.230, 0.180), (0.16, -0.02, 1.012), rot=(0.0, 0.0, -8.0), mat=CARD)
    # Deck 3 (top at 1.41) — bottles, a paper bundle, one bottle over and rolled
    # to the lip: identical soldiers was the copy-paste tell the first time.
    for i, x in enumerate((-0.32, -0.20, 0.02)):
        lean = 4.0 if i == 1 else 0.0
        f.cyl(0.040, 0.200, (x, 0.0, 1.512), rot=(lean, 0.0, 0.0), verts=10,
              mat=GLASS_DIRTY, nobevel=True)
        f.cyl(0.019, 0.060, (x, 0.0, 1.642), rot=(lean, 0.0, 0.0), verts=8,
              mat=GLASS_DIRTY, nobevel=True)
    f.cyl(0.040, 0.230, (0.24, -0.10, 1.452), rot=(90.0, 0.0, 74.0), verts=10,
          mat=GLASS_DIRTY, nobevel=True)
    f.box((0.160, 0.260, 0.110), (0.33, 0.0, 1.467), mat=PAPER, nobevel=True)
    # Top deck (1.90) — ledgers and a folded dust sheet, kept low so the piece
    # stays inside the height the manifest already promised §12.
    for i, x in enumerate((-0.34, -0.28, -0.22)):
        f.box((0.044, 0.240, 0.280), (x, 0.0, 1.762), rot=(0.0, 3.0 * i - 3.0, 0.0),
              mat=TIMBER_DARK, nobevel=True)
    f.box((0.360, 0.300, 0.060), (0.20, 0.0, 1.932), mat=CLOTH_DUST, nobevel=True)
    return b


def build_shelf_toppled() -> PropBuild:
    """The same scanned bay on its face, contents spilled. Knee height.

    A toppled unit is the cheapest way to say "something happened here" without
    animating anything — §06 designs the building to be silent, so the evidence
    has to be in the geometry. Decimated harder than the standing bay: a rack
    on the floor is read from above at a walk, not searched at arm's length."""
    b = PropBuild("Dress_ShelfToppled")
    rack = _real_rack(b, 0.16)
    # Fallen forward: the 1.90 m height lies along -Y, decks facing the floor,
    # then recentred so the piece straddles its own footprint centre. The spill
    # hugs the fallen top edge — the manifest footprint this piece already
    # ships (1.21 × 2.17 m) is a §12 clearance input, so nothing may roll past it.
    _orient(rack, rot_deg=(90.0, 0.0, 0.0), translate=(0.0, 0.95, 0.212))
    b.pivot_part = rack
    # What shook out when it went over, thrown past the top edge.
    for (x, y, yaw) in ((-0.30, -1.06, 24.0), (0.14, -1.08, -33.0), (0.38, -1.02, 61.0)):
        b.cyl(0.062, 0.160, (x, y, 0.062), rot=(90.0, 0.0, yaw), verts=10, mat=STEEL_BARE,
              nobevel=True)
    for (x, y, yaw) in ((-0.06, -1.02, 18.0), (0.28, -1.05, -37.0)):
        b.box((0.260, 0.220, 0.024), (x, y, 0.012), rot=(0.0, 0.0, yaw), mat=PAPER,
              nobevel=True)
    b.box((0.280, 0.260, 0.200), (-0.38, -0.99, 0.100), rot=(0.0, 8.0, 27.0), mat=CARD)
    return b


def build_filing_cabinet() -> PropBuild:
    """Four-drawer cabinet, one drawer out. 1.32 m — waist-to-chest cover.

    The pulled drawer is the whole point: it turns a box into a thing somebody
    was using, and it gives the silhouette an asymmetry a beam can find."""
    b = PropBuild("Dress_FilingCabinet")
    W, D, H = 0.460, 0.620, 1.320
    body = b.box((W, D, H), (0.0, 0.0, H / 2), mat=STEEL_PAINTED)
    b.pivot_part = body
    # The top took a blow once: the cap sits with a 2° list and carries a box's
    # worth of ring stains. The cabinet's one asymmetric feature after the
    # pulled drawer.
    b.box((W + 0.02, D + 0.02, 0.026), (0.004, 0.0, H - 0.003), rot=(0.0, 2.0, 0.0),
          mat=STEEL_PAINTED, nobevel=True)
    b.box((0.140, 0.120, 0.004), (0.09, -0.12, H + 0.013), rot=(0.0, 2.0, 31.0),
          mat=GRIME, nobevel=True)
    for i, z in enumerate((0.190, 0.500, 0.810, 1.120)):
        out = 0.320 if i == 2 else 0.0
        tilt = 2.5 if i == 0 else 0.0  # bottom drawer never quite shuts square
        b.box((W - 0.04, 0.026, 0.280), (0.0, -D / 2 - 0.013 - out - (0.012 if i == 0 else 0.0), z),
              rot=(0.0, tilt, 0.0), mat=STEEL_PAINTED, nobevel=True)
        b.box((0.140, 0.030, 0.030), (0.0, -D / 2 - 0.030 - out - (0.012 if i == 0 else 0.0),
                                      z + 0.070), rot=(0.0, tilt, 0.0),
              mat=STEEL_BARE, nobevel=True)
        b.box((0.110, 0.024, 0.044), (0.0, -D / 2 - 0.027 - out - (0.012 if i == 0 else 0.0),
                                      z - 0.080), rot=(0.0, tilt, 0.0),
              mat=PAPER, nobevel=True)
    # The open drawer's sides and its paper, which is the bright bit.
    b.box((W - 0.06, 0.300, 0.230), (0.0, -D / 2 - 0.170, 0.810), mat=STEEL_PAINTED,
          nobevel=True)
    for i in range(4):
        b.box((W - 0.10, 0.260, 0.030), (0.0, -D / 2 - 0.170, 0.760 + i * 0.036),
              rot=(0.0, 2.0 * i - 3.0, 0.0), mat=PAPER, nobevel=True)
    # Boot grime up the plinth line, rust weeping from under the listing cap.
    b.box((W + 0.006, 0.005, 0.070), (0.0, -D / 2 - 0.0035, 0.035), mat=GRIME,
          nobevel=True)
    b.quad(0.045, 0.260, (W / 2 + 0.002, -0.05, H - 0.180), rot=(90.0, 2.0, 90.0),
           mat=RUST_HEAVY)
    return b


def build_locker_bank() -> PropBuild:
    """Three locker bays in three states: shut, hanging at 4°, swung open on its
    hinges. 1.82 m — a full sightline blocker.

    Round 1's "ajar" door was a box yawed 62° in mid-air, and the render showed
    exactly that: a plank floating beside the cabinet. The open leaf now lives
    on two visible hinge knuckles at its own jamb and the cavity behind it is
    furnished, because an open door is only convincing if there is somewhere it
    opened FROM.

    Distinct from §12's 은폐 지점 (`gen_props`' HidingSpot_Locker) on purpose: this
    one is shallow, has no usable cavity and reads as furniture, so a player does
    not waste a §07 새벽 escape trying to climb into set dressing."""
    b = PropBuild("Dress_LockerBank")
    W, D, H = 1.050, 0.420, 1.820
    plinth = 0.090
    door_w, door_h = 0.330, H - plinth - 0.060
    zc = (H + plinth) / 2
    base = b.box((W, D, plinth), (0.0, 0.0, plinth / 2), mat=STEEL_PAINTED)
    b.pivot_part = base
    b.box((W, D, H - plinth), (0.0, 0.02, zc), mat=STEEL_PAINTED)
    b.box((W + 0.04, D + 0.03, 0.040), (0.0, 0.0, H + 0.020), mat=STEEL_PAINTED, nobevel=True)

    # Bay 1 — shut and squared. The control the other two read against.
    x = -0.348
    b.box((door_w, 0.028, door_h), (x, -D / 2 - 0.006, zc), mat=STEEL_PAINTED, nobevel=True)
    for j in range(3):
        b.box((0.180, 0.016, 0.020), (x, -D / 2 - 0.020, H - 0.230 + j * 0.058),
              mat=GRIME, nobevel=True)
    b.box((0.030, 0.030, 0.130), (x + 0.132, -D / 2 - 0.026, zc - 0.10),
          mat=STEEL_BARE, nobevel=True)
    b.box((0.110, 0.016, 0.070), (x, -D / 2 - 0.016, H - 0.420), mat=PAPER, nobevel=True)
    # Rust bleeding out of the vent slots — two narrow tapering strips starting
    # AT the slots, not the round-2 brown rectangle that read as a poster.
    b.quad(0.030, 0.190, (x - 0.055, -D / 2 - 0.021, H - 0.335), rot=(90.0, 2.0, 0.0),
           mat=RUST_HEAVY)
    b.quad(0.016, 0.300, (x + 0.030, -D / 2 - 0.021, H - 0.390), rot=(90.0, -1.5, 0.0),
           mat=RUST_HEAVY)
    b.box((0.014, 0.006, 0.220), (x + 0.158, -D / 2 - 0.021, zc + 0.30),
          mat=STEEL_BARE, nobevel=True)

    # Bay 2 — the HANGING door: still latched at the bottom, torn off its top
    # hinge, so the whole leaf rolls 4° about its own face normal and opens a
    # dark wedge at the top corner. This is the asymmetry a corridor of
    # repeated banks is bought with — a 4° line in a wall of verticals is
    # visible three cells away in a moving beam.
    x = 0.0
    b.quad(door_w - 0.04, 0.30, (x + 0.02, -D / 2 - 0.004, H - 0.24),
           rot=(90.0, 0.0, 0.0), mat=GRIME)
    b.box((door_w, 0.028, door_h), (x + 0.012, -D / 2 - 0.006, zc - 0.014),
          rot=(0.0, 4.0, 0.0), mat=STEEL_PAINTED, nobevel=True)
    for j in range(3):
        dz = H - 0.244 + j * 0.058 - zc
        b.box((0.180, 0.016, 0.020), (x + 0.012 + dz * 0.0698, -D / 2 - 0.020,
                                      H - 0.244 + j * 0.058),
              rot=(0.0, 4.0, 0.0), mat=GRIME, nobevel=True)
    b.box((0.030, 0.030, 0.130), (x + 0.140, -D / 2 - 0.026, zc - 0.125),
          rot=(0.0, 4.0, 0.0), mat=STEEL_BARE, nobevel=True)
    b.box((0.110, 0.016, 0.070), (x + 0.020, -D / 2 - 0.016, H - 0.434),
          rot=(0.0, 4.0, 0.0), mat=PAPER, nobevel=True)

    # Bay 3 — properly open: a leaf on visible hinge knuckles at its jamb,
    # swung 38° into the corridor, and the cavity behind it furnished (shelf,
    # coat, floor grime) so the beam finds a used locker rather than a hole.
    x = 0.348
    hinge_x = x - door_w / 2
    a = math.radians(38.0)
    b.box((0.024, D - 0.02, door_h), (x + door_w / 2 - 0.012, 0.01, zc),
          mat=GRIME, nobevel=True)  # cavity shadow wall
    b.box((door_w - 0.03, 0.020, 0.020), (x, -0.02, H - 0.360), mat=STEEL_PAINTED,
          nobevel=True)  # hat shelf
    b.box((0.220, 0.100, 0.760), (x + 0.02, -0.03, 1.080), rot=(0.0, 2.0, -3.0),
          mat=CLOTH_STAINED, nobevel=True)  # the coat
    b.box((0.200, 0.150, 0.060), (x - 0.03, -0.06, plinth + 0.030), rot=(0.0, 0.0, 24.0),
          mat=CARD, nobevel=True)  # kicked-in box on the locker floor
    for hz in (zc + door_h * 0.38, zc - door_h * 0.38):
        b.cyl(0.011, 0.070, (hinge_x, -D / 2 - 0.010, hz), verts=8, mat=STEEL_BARE,
              nobevel=True)
    leaf_cx = hinge_x + (door_w / 2) * math.cos(a)
    leaf_cy = -D / 2 - 0.014 - (door_w / 2) * math.sin(a)
    b.box((door_w, 0.028, door_h), (leaf_cx, leaf_cy, zc), rot=(0.0, 0.0, -38.0),
          mat=STEEL_PAINTED, nobevel=True)
    b.box((0.030, 0.030, 0.130),
          (hinge_x + (door_w - 0.03) * math.cos(a), -D / 2 - 0.014 - (door_w - 0.03) * math.sin(a),
           zc - 0.10), rot=(0.0, 0.0, -38.0), mat=STEEL_BARE, nobevel=True)

    # §03's grime gradient, floor up: a dark skirt along the plinth and one
    # long rust weep from under the cap — the beam rakes these at boot height.
    b.box((W + 0.006, 0.005, 0.085), (0.0, -D / 2 - 0.0035, 0.0475), mat=GRIME,
          nobevel=True)
    for sx in (-1.0, 1.0):
        b.box((0.005, D - 0.02, 0.085), (sx * (W / 2 + 0.0035), 0.0, 0.0475), mat=GRIME,
              nobevel=True)
    b.quad(0.060, 0.500, (-0.46, -D / 2 - 0.0075, H - 0.30), rot=(90.0, 0.0, 0.0),
           mat=RUST_HEAVY)
    return b


def build_workbench() -> PropBuild:
    """A 1.80 m bench with a vice, a tool board and clutter. Waist height.

    §04's 정비공 is the role that edits the map mid-match; a bench is where that
    reads as belonging to the building rather than to a menu. The tool board
    behind it is the piece's real job — it puts bright, small, specular objects at
    eye height, which is where a flashlight beam actually points."""
    b = PropBuild("Dress_Workbench")
    W, D, H = 1.800, 0.680, 0.900
    top = b.box((W, D, 0.060), (0.0, 0.0, H - 0.030), mat=TIMBER_PALE)
    b.pivot_part = top
    for sx in (-1.0, 1.0):
        for sy in (-1.0, 1.0):
            b.box((0.080, 0.080, H - 0.060), (sx * (W / 2 - 0.070), sy * (D / 2 - 0.070),
                                              (H - 0.060) / 2), mat=TIMBER_DARK, nobevel=True)
    b.box((W - 0.20, 0.040, 0.140), (0.0, -D / 2 + 0.040, H - 0.140), mat=TIMBER_DARK,
          nobevel=True)
    # The lower shelf SAGS: two halves pitched toward a dropped midpoint, the
    # load (a full paint tin) sitting exactly over the dip. A dead-flat shelf
    # was half of why round 1 read as a furniture showroom.
    hw = (W - 0.24) / 2
    b.box((hw, D - 0.20, 0.028), (-hw / 2 - 0.01, 0.0, 0.238), rot=(0.0, 2.8, 0.0),
          mat=PLY, nobevel=True)
    b.box((hw, D - 0.20, 0.028), (hw / 2 + 0.01, 0.0, 0.238), rot=(0.0, -2.8, 0.0),
          mat=PLY, nobevel=True)
    f = b.frame((0.0, 0.0, 0.0))
    _tin(f, 0.085, 0.200, (0.06, 0.03, 0.220), mat_name=STEEL_PAINTED)
    # One diagonal brace, one side only — the other one is long gone.
    b.box((0.060, 0.024, H - 0.18), (W / 2 - 0.070, 0.0, (H - 0.10) / 2),
          rot=(38.0, 0.0, 0.0), mat=TIMBER_DARK, nobevel=True)
    # Vice — dark cast body, BRIGHT worked faces. The jaw plate and the handle
    # are what a beam actually catches at bench height.
    b.box((0.180, 0.160, 0.130), (-0.640, -0.150, H + 0.065), mat=STEEL_PAINTED)
    b.box((0.150, 0.060, 0.110), (-0.640, -0.245, H + 0.055), mat=STEEL_PAINTED,
          nobevel=True)
    b.box((0.160, 0.014, 0.040), (-0.640, -0.212, H + 0.110), mat=STEEL_BARE, nobevel=True)
    b.cyl(0.018, 0.300, (-0.640, -0.320, H + 0.065), rot=(90.0, 0.0, 0.0), verts=8,
          mat=STEEL_BARE, nobevel=True)
    b.cyl(0.014, 0.220, (-0.655, -0.460, H + 0.058), rot=(8.0, 90.0, 0.0), verts=8,
          mat=STEEL_BARE, nobevel=True)
    b.sph(0.022, (-0.545, -0.460, H + 0.043), segs=8, rings=5, mat=STEEL_BARE,
          nobevel=True)
    # Tool board — pale ply so the dark tools silhouette against it. Two hooks
    # hang empty, one tool hangs crooked, and the missing one lies on the top:
    # seven identical verticals were the round-1 giveaway. The dark patch is
    # the grime shadow of a board that was unscrewed and carried off — round 2
    # tried it bright-on-dark and it read as a pinned-up sheet of paper.
    b.box((W, 0.024, 0.620), (0.0, D / 2 - 0.012, H + 0.310), mat=PLY, nobevel=True)
    b.quad(0.30, 0.34, (-0.42, D / 2 - 0.0255, H + 0.330), rot=(90.0, 0.0, 0.0),
           mat=TIMBER_DARK)  # grime shadow where a board used to hang
    tools = ((-0.62, 0.200, 0.0, True), (-0.42, 0.0, 0.0, False), (-0.22, 0.260, 0.0, True),
             (0.06, 0.180, 12.0, True), (0.26, 0.0, 0.0, False), (0.48, 0.290, -4.0, True),
             (0.68, 0.150, 0.0, True))
    for (x, h, tilt, present) in tools:
        b.box((0.036, 0.020, 0.070), (x, D / 2 - 0.030, H + 0.540), mat=STEEL_BARE,
              nobevel=True)
        if present:
            b.box((0.040, 0.018, h), (x + 0.012 * (tilt != 0.0), D / 2 - 0.034,
                                      H + 0.500 - h / 2), rot=(0.0, tilt, 0.0),
                  mat=STEEL_BARE, nobevel=True)
            b.box((0.070, 0.022, 0.036), (x, D / 2 - 0.034, H + 0.500 - h),
                  rot=(0.0, tilt, 0.0), mat=TIMBER_DARK, nobevel=True)
    # The dropped tool, where it landed.
    b.box((0.340, 0.042, 0.020), (-0.30, 0.16, H + 0.010), rot=(0.0, 0.0, -28.0),
          mat=STEEL_BARE, nobevel=True)
    # Clutter on the deck.
    _tin(f, 0.060, 0.140, (0.360, 0.060, H + 0.030))
    _tin(f, 0.052, 0.110, (0.500, -0.070, H + 0.030), mat_name=BRASS_FITTING)
    f.box((0.360, 0.260, 0.070), (0.030, 0.080, H + 0.065), rot=(0.0, 0.0, 8.0),
          mat=STEEL_PAINTED, nobevel=True)
    for i, yaw in enumerate((6.0, -14.0, 21.0)):
        f.box((0.300, 0.220, 0.006), (0.760, -0.060 + i * 0.02, H + 0.033 + i * 0.007),
              rot=(0.0, 0.0, yaw), mat=PAPER, nobevel=True)
    f.box((0.220, 0.160, 0.180), (-0.220, 0.070, H + 0.120), rot=(0.0, 0.0, -6.0), mat=CARD)
    # Twenty years of use, floor up: oil soak around the vice, a scorch by the
    # tins, a rag over the front edge, grime socks on the legs.
    b.box((0.300, 0.240, 0.004), (-0.500, -0.020, H + 0.0025), rot=(0.0, 0.0, 14.0),
          mat=GRIME, nobevel=True)
    b.box((0.200, 0.170, 0.005), (-0.360, 0.120, H + 0.0030), rot=(0.0, 0.0, -31.0),
          mat=GRIME, nobevel=True)
    b.box((0.160, 0.130, 0.004), (0.300, -0.180, H + 0.0025), rot=(0.0, 0.0, 40.0),
          mat=GRIME, nobevel=True)
    b.box((0.190, 0.070, 0.026), (0.620, -D / 2 + 0.012, H - 0.010), rot=(0.0, 0.0, 6.0),
          mat=CLOTH_STAINED, nobevel=True)
    b.box((0.180, 0.048, 0.150), (0.620, -D / 2 - 0.017, H - 0.105), rot=(4.0, 0.0, 0.0),
          mat=CLOTH_STAINED, nobevel=True)
    for sx in (-1.0, 1.0):
        b.box((0.086, 0.086, 0.070), (sx * (W / 2 - 0.070), -(D / 2 - 0.070), 0.035),
              mat=GRIME, nobevel=True)
    return b


def build_table_broken() -> PropBuild:
    """A trestle table with a collapsed leg. 0.78 m at the high end.

    The tilt is the information: a level table is furniture, a tilted one is a
    building nobody has maintained since §07's 저녁 stage started."""
    b = PropBuild("Dress_TableBroken")
    top = b.box((1.400, 0.760, 0.046), (0.0, 0.0, 0.700), rot=(0.0, -9.0, 0.0),
                mat=TIMBER_PALE)
    b.pivot_part = top
    for (sx, sy, h) in ((-1.0, -1.0, 0.700), (-1.0, 1.0, 0.700), (1.0, -1.0, 0.560)):
        b.box((0.070, 0.070, h), (sx * 0.620, sy * 0.320, h / 2), mat=TIMBER_DARK, nobevel=True)
    # The failed leg, lying under the corner it used to hold up.
    b.box((0.070, 0.070, 0.640), (0.500, 0.360, 0.035), rot=(0.0, 90.0, 24.0),
          mat=TIMBER_DARK, nobevel=True)
    b.box((1.300, 0.060, 0.036), (0.0, 0.0, 0.600), rot=(0.0, -9.0, 0.0), mat=TIMBER_DARK,
          nobevel=True)
    for i, yaw in enumerate((11.0, -26.0, 44.0)):
        b.box((0.280, 0.210, 0.006), (-0.30 + i * 0.28, 0.10 - i * 0.09,
                                      0.760 - i * 0.04), rot=(0.0, -9.0, yaw), mat=PAPER,
              nobevel=True)
    return b


def build_chair() -> PropBuild:
    """A plain wooden chair, upright. 0.92 m.

    Small, cheap, and the single most human object in the kit — one chair pulled
    out from a table is a person who left."""
    b = PropBuild("Dress_Chair")
    seat = b.box((0.420, 0.400, 0.040), (0.0, 0.0, 0.450), mat=TIMBER_PALE)
    b.pivot_part = seat
    for (sx, sy) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        h = 0.920 if sy > 0 else 0.450
        b.box((0.040, 0.040, h), (sx * 0.180, sy * 0.170, h / 2), mat=TIMBER_DARK,
              nobevel=True)
    for z in (0.700, 0.830):
        b.box((0.360, 0.030, 0.070), (0.0, 0.170, z), mat=TIMBER_PALE, nobevel=True)
    b.box((0.380, 0.030, 0.030), (0.0, -0.170, 0.230), mat=TIMBER_DARK, nobevel=True)
    # A work jacket left over the backrest — the person who did not come back.
    b.box((0.380, 0.070, 0.240), (0.01, 0.170, 0.780), rot=(0.0, -2.0, 3.0),
          mat=CLOTH_STAINED, nobevel=True)
    b.box((0.120, 0.060, 0.330), (-0.170, 0.150, 0.560), rot=(0.0, -7.0, 8.0),
          mat=CLOTH_STAINED, nobevel=True)  # one sleeve hanging
    return b


def build_generator() -> PropBuild:
    """A portable generator, dead. 0.82 × 0.56 × 0.58 m. BRAND-NEW piece.

    The manifest is the whole integration: `BulkPass`/`PickBulk` choose by
    group, palette and weight, so a new Bulk row ships with zero scatterer
    changes — which is exactly the seam this kit exists to prove. §12 gives
    zone D (utility) its plant character; a generator that clearly does not run
    — no light, no cable, dust on the tank — is §06's silence made visible, and
    its worn yellow shell is the largest bright object the utility palette
    owns. PolyHaven portable_generator (CC0), 26 k triangles decimated to ~2.5 k;
    the vents and fins the collapse eats come back in the normal map. The
    scan's control panel already faces −Y, which is the kit's front."""
    b = PropBuild("Dress_Generator")
    body = _adopt(b, _append_object("portable_generator", "portable_generator"),
                  GENERATOR_BODY)
    _decimate(body, 0.092)
    b.pivot_part = body
    for name in ("portable_generator_dial", "portable_generator_switch",
                 "portable_generator_toggle"):
        _adopt(b, _append_object("portable_generator", name), GENERATOR_BODY)
    return b


def build_chair_broken() -> PropBuild:
    """A chair on its side with a leg gone. 0.44 m tall, entirely walk-around."""
    b = PropBuild("Dress_ChairBroken")
    seat = b.box((0.420, 0.400, 0.040), (0.0, 0.0, 0.220), rot=(74.0, 0.0, 18.0),
                 mat=TIMBER_PALE)
    b.pivot_part = seat
    b.box((0.360, 0.040, 0.400), (0.10, -0.16, 0.230), rot=(74.0, 0.0, 18.0), mat=TIMBER_PALE,
          nobevel=True)
    for (x, y, yaw, pitch) in ((-0.20, 0.14, 26.0, 80.0), (0.16, 0.22, -48.0, 84.0)):
        b.box((0.040, 0.040, 0.440), (x, y, 0.100), rot=(pitch, 0.0, yaw), mat=TIMBER_DARK,
              nobevel=True)
    b.box((0.040, 0.040, 0.420), (0.42, -0.24, 0.020), rot=(0.0, 90.0, 62.0), mat=TIMBER_DARK,
          nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  DEBRIS — low enough that §05's 65 % backward speed is never the reason a
#  player dies. Nothing in this group passes knee height.
# ══════════════════════════════════════════════════════════════════════════


def build_rubble_pile() -> PropBuild:
    """Fallen ceiling: concrete chunks, brick and dust. 1.15 x 0.95 x 0.34 m.

    Deterministic layout, no RNG, so a rebuild is byte-for-byte identical.
    Pale broken concrete over a dark floor is one of the few places this kit gets
    a genuine value step without a texture."""
    b = PropBuild("Dress_RubblePile")
    # Two offset, rotated dust beds instead of one axis-aligned mat: round 1
    # showed a crisp rectangle under the chunks, which no ceiling has ever left.
    bed = b.box((1.000, 0.780, 0.022), (0.02, 0.0, 0.011), rot=(0.0, 0.0, 9.0),
                mat=CONCRETE, nobevel=True)
    b.pivot_part = bed
    b.box((0.680, 0.560, 0.026), (-0.18, -0.10, 0.013), rot=(0.0, 0.0, -24.0),
          mat=CONCRETE, nobevel=True)
    _rubble(b, (
        (0.230, 0.200, 0.170, -0.300, -0.090, 0.090, 16.0, -9.0, 18.0),
        (0.190, 0.170, 0.140, 0.060, 0.070, 0.082, -11.0, 14.0, -34.0),
        (0.160, 0.150, 0.120, 0.330, -0.140, 0.066, 22.0, 5.0, 52.0),
        (0.140, 0.130, 0.100, -0.120, 0.290, 0.058, -7.0, -17.0, -12.0),
        (0.120, 0.110, 0.090, 0.420, 0.230, 0.052, 12.0, 8.0, 41.0),
        (0.200, 0.150, 0.130, -0.430, 0.220, 0.072, 4.0, 19.0, -58.0),
        (0.130, 0.120, 0.095, 0.170, -0.320, 0.054, -19.0, 3.0, 24.0),
        (0.105, 0.100, 0.080, -0.020, -0.180, 0.125, 31.0, -12.0, 66.0),
    ))
    _rubble(b, (
        (0.215, 0.100, 0.062, 0.230, 0.360, 0.032, 6.0, -8.0, -21.0),
        (0.215, 0.100, 0.062, -0.360, -0.320, 0.032, -13.0, 4.0, 39.0),
        (0.215, 0.100, 0.062, 0.480, -0.020, 0.090, 9.0, 15.0, 8.0),
    ), mat_name=BRICK)
    # One big slab leaning against the tallest chunk — the pile's silhouette
    # feature, and the shadow pocket the beam digs into.
    b.box((0.360, 0.050, 0.300), (-0.190, -0.020, 0.150), rot=(-56.0, 6.0, 30.0),
          mat=CONCRETE_BROKEN, nobevel=True)
    # Grime pockets where the dust settled between chunks.
    for (x, y, yaw) in ((0.20, 0.02, 33.0), (-0.30, 0.16, -12.0), (0.05, -0.26, 61.0)):
        b.box((0.190, 0.150, 0.005), (x, y, 0.026), rot=(0.0, 0.0, yaw), mat=GRIME,
              nobevel=True)
    # Rebar — two straight ends and one bent double, which says "ceiling", not
    # "gravel".
    for (x, y, pitch, yaw) in ((-0.10, 0.06, 62.0, 24.0), (0.24, -0.04, 71.0, -46.0)):
        b.cyl(0.011, 0.520, (x, y, 0.180), rot=(pitch, 0.0, yaw), verts=6, mat=RUST_HEAVY,
              nobevel=True)
    b.cyl(0.010, 0.300, (-0.380, -0.230, 0.120), rot=(48.0, 0.0, -70.0), verts=6,
          mat=RUST_HEAVY, nobevel=True)
    b.cyl(0.010, 0.220, (-0.430, -0.330, 0.200), rot=(112.0, 0.0, -70.0), verts=6,
          mat=RUST_HEAVY, nobevel=True)
    return b


def build_rubble_small() -> PropBuild:
    """A scatter of chunks, 0.18 m tall. Filler for corners and wall feet."""
    b = PropBuild("Dress_RubbleSmall")
    bed = b.box((0.520, 0.420, 0.015), (0.0, 0.0, 0.0075), rot=(0.0, 0.0, 13.0),
                mat=CONCRETE, nobevel=True)
    b.pivot_part = bed
    _rubble(b, (
        (0.150, 0.130, 0.110, -0.120, -0.040, 0.058, 14.0, -11.0, 22.0),
        (0.120, 0.110, 0.090, 0.110, 0.070, 0.048, -18.0, 6.0, -41.0),
        (0.100, 0.095, 0.075, 0.190, -0.120, 0.042, 25.0, 9.0, 63.0),
        (0.085, 0.080, 0.065, -0.190, 0.130, 0.038, -6.0, 16.0, -17.0),
        (0.070, 0.070, 0.055, 0.020, 0.160, 0.034, 9.0, -21.0, 48.0),
    ))
    # One brick and a dust pocket, so five grey chunks are not the whole story.
    b.box((0.180, 0.085, 0.055, ), (0.060, -0.030, 0.030), rot=(7.0, 12.0, -33.0),
          mat=BRICK, nobevel=True)
    b.box((0.150, 0.120, 0.004), (-0.05, 0.06, 0.018), rot=(0.0, 0.0, 27.0), mat=GRIME,
          nobevel=True)
    return b


def build_planks_fallen() -> PropBuild:
    """Timber that came off a case or a shelf. 1.65 x 0.80 x 0.17 m."""
    b = PropBuild("Dress_PlanksFallen")
    first = b.box((1.560, 0.160, 0.032), (0.0, -0.170, 0.016), rot=(0.0, 0.0, 4.0),
                  mat=TIMBER_PALE, nobevel=True)
    b.pivot_part = first
    _plank_scatter(b, (
        (1.420, 0.150, 0.030, -0.04, 0.030, 0.048, (0.0, 2.0, -7.0)),
        (1.180, 0.140, 0.030, 0.10, 0.220, 0.020, (0.0, 0.0, 12.0)),
        (0.940, 0.130, 0.028, -0.28, 0.320, 0.084, (0.0, -14.0, 27.0)),
        (0.760, 0.120, 0.026, 0.44, -0.290, 0.016, (0.0, 0.0, -31.0)),
    ))
    b.box((0.140, 0.130, 0.110), (-0.62, 0.170, 0.055), rot=(0.0, 6.0, 22.0),
          mat=CONCRETE_BROKEN, nobevel=True)
    for (x, y, yaw) in ((0.30, -0.06, 0.0), (-0.10, 0.14, 40.0)):
        b.cyl(0.006, 0.090, (x, y, 0.062), rot=(84.0, 0.0, yaw), verts=6, mat=RUST_HEAVY,
              nobevel=True)
    return b


def build_papers_scatter() -> PropBuild:
    """Loose paper across the floor. 1.25 x 1.00 x 0.03 m, walkable.

    Albedo 0.78 on a floor that renders near black: this is the cheapest readable
    object in the kit, and §03's whole loop is about reading things in a narrow
    beam. Kept under 3 cm so it is scenery, not an obstacle."""
    b = PropBuild("Dress_PapersScatter")
    sheets = (
        (0.300, 0.220, -0.36, -0.26, 0.003, 12.0),
        (0.300, 0.220, -0.10, -0.34, 0.005, -37.0),
        (0.290, 0.210, 0.24, -0.22, 0.003, 61.0),
        (0.300, 0.220, 0.44, 0.06, 0.007, -14.0),
        (0.280, 0.210, 0.10, 0.14, 0.003, 28.0),
        (0.300, 0.220, -0.22, 0.10, 0.009, -52.0),
        (0.290, 0.215, -0.44, 0.34, 0.003, 7.0),
        (0.300, 0.220, 0.06, 0.40, 0.005, 44.0),
        (0.280, 0.200, 0.40, 0.36, 0.003, -23.0),
    )
    first = None
    for (sx, sy, x, y, z, yaw) in sheets:
        obj = b.box((sx, sy, 0.004), (x, y, z), rot=(0.0, 0.0, yaw), mat=PAPER, nobevel=True)
        first = first or obj
    b.pivot_part = first
    # A folded bundle, so the scatter has one thing with a shadow under it.
    b.box((0.260, 0.190, 0.026), (-0.02, -0.10, 0.013), rot=(0.0, 3.0, -8.0), mat=PAPER,
          nobevel=True)
    b.box((0.240, 0.180, 0.020), (0.30, -0.44, 0.010), rot=(0.0, -4.0, 33.0), mat=CARD,
          nobevel=True)
    return b


def build_tool_scatter() -> PropBuild:
    """Dropped tools, 0.60 x 0.44 x 0.10 m. Small bright specular hits on a floor."""
    b = PropBuild("Dress_ToolScatter")
    first = b.box((0.320, 0.044, 0.018), (-0.10, 0.06, 0.009), rot=(0.0, 0.0, 16.0),
                  mat=STEEL_BARE, nobevel=True)
    b.pivot_part = first
    b.box((0.070, 0.070, 0.020), (0.06, 0.03, 0.010), rot=(0.0, 0.0, 16.0), mat=STEEL_BARE,
          nobevel=True)
    b.box((0.240, 0.036, 0.016), (0.14, -0.12, 0.008), rot=(0.0, 0.0, -41.0), mat=STEEL_BARE,
          nobevel=True)
    b.cyl(0.020, 0.170, (-0.16, -0.14, 0.020), rot=(90.0, 0.0, 62.0), verts=8,
          mat=TIMBER_DARK, nobevel=True)
    b.box((0.090, 0.040, 0.048), (-0.24, -0.10, 0.024), rot=(0.0, 0.0, 62.0), mat=STEEL_BARE,
          nobevel=True)
    # The abrasive wheel: dark rubber-bound grit with a bright arbour, not the
    # round-1 "pink cake" of raw rust material.
    b.cyl(0.090, 0.060, (0.20, 0.14, 0.030), verts=12, mat=RUBBER, nobevel=True)
    b.cyl(0.024, 0.066, (0.20, 0.14, 0.030), verts=8, mat=STEEL_BARE, nobevel=True)
    b.box((0.130, 0.100, 0.004), (0.16, 0.10, 0.0025), rot=(0.0, 0.0, 18.0), mat=RUST_HEAVY,
          nobevel=True)  # rust shadow where it has sat for years
    for (x, y) in ((0.04, 0.16), (-0.02, 0.19), (0.10, 0.20)):
        b.cyl(0.010, 0.014, (x, y, 0.007), verts=6, mat=STEEL_BARE, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  WET — water is atmosphere, and only atmosphere. It used to be §03's worked
#  example of a 단서 ("그것은 물이 있는 층에 있다") and therefore diegetic
#  information; §03 is deleted and the destination is announced at the start.
#  What it is now is the kit's only mirror — see the module header.
# ══════════════════════════════════════════════════════════════════════════


def _puddle(b: PropBuild, lobes: tuple, thickness: float) -> None:
    """A pool built from overlapping flattened cylinders.

    Two reasons it is geometry rather than a decal: this project has no decal
    system yet, and a puddle needs its own *material* — roughness 0.05 — because
    the read is specular. A rough plane tinted blue is not water in a torch beam;
    a mirror is."""
    for (x, y, r, verts) in lobes:
        b.cyl(r, thickness, (x, y, thickness / 2), verts=verts, mat=WATER, nobevel=True)


def build_puddle_large() -> PropBuild:
    """Standing water, 1.85 x 1.35 m. §12 zone C's largest piece.

    A dark mirror on the floor. It costs nothing until a flashlight crosses it and
    then it is the brightest thing in the corridor — which is how a player learns
    "I am on the floor with the water" without a single line of UI. That sentence is
    the whole reason it survived §03: it tells a runner WHICH STOREY they are on, and
    §01 stacks eight of them behind one-way 투하구."""
    b = PropBuild("Dress_PuddleLarge")
    _puddle(b, (
        (0.00, 0.00, 0.560, 20),
        (0.44, 0.16, 0.400, 16),
        (-0.46, -0.14, 0.360, 16),
        (0.18, -0.38, 0.300, 14),
        (-0.20, 0.40, 0.250, 12),
    ), 0.012)
    b.pivot_part = b.parts[0]
    # A damp rim, so the pool has an edge instead of floating on the floor.
    for (x, y, r) in ((0.00, 0.00, 0.620), (0.46, 0.17, 0.450), (-0.48, -0.15, 0.410)):
        b.cyl(r, 0.005, (x, y, 0.0025), verts=16, mat=WET_STAIN, nobevel=True)
    return b


def build_puddle_small() -> PropBuild:
    """A 0.85 m pool for under a drip or against a wall foot."""
    b = PropBuild("Dress_PuddleSmall")
    _puddle(b, (
        (0.00, 0.00, 0.290, 16),
        (0.20, 0.08, 0.180, 12),
        (-0.18, -0.09, 0.150, 12),
    ), 0.008)
    b.pivot_part = b.parts[0]
    # Rim tucked nearly under the water: round 1's wide pale ring made the pool
    # read as a grey dinner plate instead of a dark mirror with a damp edge.
    b.cyl(0.305, 0.004, (0.01, 0.0, 0.002), verts=16, mat=WET_STAIN, nobevel=True)
    return b


def build_drain_grate() -> PropBuild:
    """A floor drain with water in it, 0.64 m square, 0.05 m proud.

    §12 asks for zone boundaries a player can name; a drain is a landmark that
    survives being seen for a quarter of a second at the edge of a beam. It also
    explains where §03's water is going, which is the difference between a wet
    basement and a puddle somebody placed."""
    b = PropBuild("Dress_DrainGrate")
    frame_obj = b.box((0.640, 0.640, 0.036), (0.0, 0.0, 0.018), mat=CONCRETE, nobevel=True)
    b.pivot_part = frame_obj
    b.box((0.520, 0.520, 0.010), (0.0, 0.0, 0.010), mat=WATER, nobevel=True)
    b.box((0.560, 0.040, 0.030), (0.0, -0.260, 0.032), mat=STEEL_BARE, nobevel=True)
    b.box((0.560, 0.040, 0.030), (0.0, 0.260, 0.032), mat=STEEL_BARE, nobevel=True)
    for i in range(5):
        y = -0.200 + i * 0.100
        b.box((0.540, 0.036, 0.024), (0.0, y, 0.030), mat=RUST_HEAVY, nobevel=True)
    b.box((0.036, 0.520, 0.024), (0.0, 0.0, 0.030), mat=RUST_HEAVY, nobevel=True)
    # Wet spread around the frame.
    b.cyl(0.480, 0.005, (0.0, 0.0, 0.0025), verts=16, mat=WET_STAIN, nobevel=True)
    return b


def build_drip_stain_wall() -> PropBuild:
    """A wet streak down a wall with the leaking joint at the top. WALL mount.

    Reaches from 2.40 m to the floor so it connects a ceiling pipe to a puddle:
    the three wet pieces are meant to be read as one story, which is what stops
    water looking like a texture somebody remembered to add."""
    b = PropBuild("Dress_DripStain_Wall")
    back = b.box((0.520, 0.014, 2.380), (0.0, -0.007, 1.190), mat=WET_STAIN, nobevel=True)
    b.pivot_part = back
    for (x, w, top, bot) in ((-0.150, 0.052, 2.320, 0.10), (0.020, 0.070, 2.360, 0.05),
                             (0.140, 0.044, 2.180, 0.22), (0.210, 0.030, 1.900, 0.40)):
        b.box((w, 0.008, top - bot), (x, -0.018, (top + bot) / 2), mat=WATER, nobevel=True)
    # Mineral crust where the water has been running longest.
    for (x, z, w, h) in ((0.02, 2.30, 0.260, 0.130), (-0.09, 1.62, 0.170, 0.090),
                         (0.13, 0.92, 0.140, 0.080)):
        b.box((w, 0.012, h), (x, -0.016, z), mat=PAPER, nobevel=True)
    # The joint that is leaking.
    b.cyl(0.046, 0.180, (0.020, -0.052, 2.420), rot=(90.0, 0.0, 0.0), verts=10,
          mat=RUST_HEAVY, nobevel=True)
    b.cyl(0.058, 0.040, (0.020, -0.052, 2.420), rot=(90.0, 0.0, 0.0), verts=10,
          mat=STEEL_BARE, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  WALL SERVICES — the pipes, valves and meters that make a basement a
#  basement. All authored 2.5 m so they tile on the MapKit grid.
# ══════════════════════════════════════════════════════════════════════════

RUN_LENGTH = 2.500
"""Length of a tiling run, metres. Equal to the MapKit's ``grid_metres`` so a run
docks with the next cell's run instead of leaving a gap every 2.5 m."""


def build_pipe_run_wall() -> PropBuild:
    """Two scanned flanged lines at ~2.0–2.3 m, tied by a real tee. WALL mount.

    A horizontal bright line at head height is the single most effective depth
    cue in a dark corridor — the beam slides along it and the corridor's length
    becomes visible. The scan's flange joints give the run the rhythm the
    procedural version faked with rings, and the tee's up-stub almost touching
    the top line plus a copper drop leg off its down-stub keep it reading as
    plumbing rather than as two stripes. Brackets, the one bracket hanging 14°
    off plumb, and the wall's rust/damp memory stay procedural — they are the
    kit's own storytelling and they sit on the mount plane, not on the pipes."""
    b = PropBuild("Dress_PipeRun_Wall")
    line_a, r_a = _pipe_line(b, ("modular_industrial_pipes_01_pipe02",
                                 "modular_industrial_pipes_01_pipe01"),
                             ratio=0.42, target_len=RUN_LENGTH)
    for obj in line_a:
        _orient(obj, translate=(0.0, -0.112, 2.240))
    line_b, r_b = _pipe_line(b, ("modular_industrial_pipes_01_pipe01",
                                 "modular_industrial_pipes_01_pipe07",
                                 "modular_industrial_pipes_01_pipe02"),
                             ratio=0.26, target_len=RUN_LENGTH)
    for obj in line_b:
        _orient(obj, translate=(0.0, -0.112, 2.008))
    # The tee sits 1.0655/3.225 along line b; its down-stub takes the drop leg.
    tee_x = -RUN_LENGTH / 2.0 + 1.0655 * (RUN_LENGTH / 3.225)
    b.cyl(0.026, 0.340, (tee_x, -0.112, 1.700), verts=8, mat=COPPER, nobevel=True)
    b.cyl(0.034, 0.050, (tee_x, -0.112, 1.810), verts=8, mat=BRASS_FITTING, nobevel=True)
    # Brackets at the tiling thirds; the middle one has torn its lower fixing
    # and hangs 14° off plumb — the run's procedural asymmetric feature.
    for x in (-0.960, 0.960):
        b.box((0.056, 0.026, 0.420), (x, -0.046, 2.115), mat=STEEL_PAINTED, nobevel=True)
        for (z, r) in ((2.240, r_a), (2.008, r_b)):
            b.box((0.056, 0.135, 0.026), (x, -0.0675, z + r + 0.013), mat=STEEL_PAINTED,
                  nobevel=True)
    b.box((0.056, 0.026, 0.400), (0.020, -0.078, 2.105), rot=(14.0, 0.0, 6.0),
          mat=STEEL_PAINTED, nobevel=True)
    # What the wall remembers: rust weeping under the flange joints, and a damp
    # streak under the drop leg. All of it on the mount plane.
    b.quad(0.070, 0.560, (0.486, -0.002, 1.920), rot=(90.0, 0.0, 0.0), mat=RUST_HEAVY)
    b.quad(0.048, 0.320, (-0.583, -0.002, 1.840), rot=(90.0, 0.0, 0.0), mat=RUST_HEAVY)
    b.quad(0.110, 0.450, (tee_x + 0.04, -0.001, 1.750), rot=(90.0, 0.0, 4.0), mat=WET_STAIN)
    return b


def build_pipe_run_ceiling() -> PropBuild:
    """Two scanned lines hung under the ceiling on drop rods. CEILING mount.

    The scan composes cleanly here because a ceiling run IS the wall run lying
    down: the same straight segments butt to the same 2.5 m tiling length, and
    the cross fitting's stubs point at the ceiling and the floor, which is what
    a take-off on a hung main really does. Hangs 0.44 m at most, leaving a
    standing player head clearance under the kit's 3.0 m clear height. Rods,
    anchor plates and the bright cable tray stay procedural — they are rigging,
    not pipe, and the tray is the flat overhead plane that reads at distance."""
    b = PropBuild("Dress_PipeRun_Ceiling")
    line_a, r_a = _pipe_line(b, ("modular_industrial_pipes_01_pipe02",
                                 "modular_industrial_pipes_01_pipe01"),
                             ratio=0.42, target_len=RUN_LENGTH)
    for obj in line_a:
        _orient(obj, translate=(0.0, -0.150, -0.320))
    line_b, r_b = _pipe_line(b, ("modular_industrial_pipes_01_pipe01",
                                 "modular_industrial_pipes_01_pipe06",
                                 "modular_industrial_pipes_01_pipe02"),
                             ratio=0.28, target_len=RUN_LENGTH)
    for obj in line_b:
        _orient(obj, translate=(0.0, 0.130, -0.298))
    # Anchor plates and drop rods at the tiling thirds. One rod on the thin
    # line has torn its anchor and hangs kinked — the asymmetric feature.
    for x in (-0.960, 0.0, 0.960):
        b.box((0.060, 0.420, 0.024), (x, -0.010, -0.012), mat=STEEL_PAINTED, nobevel=True)
        for (y, z, r) in ((-0.150, -0.320, r_a), (0.130, -0.298, r_b)):
            if x == 0.0 and y > 0.0:
                b.cyl(0.011, -z - r - 0.024, (x + 0.030, y, (z + r - 0.024) / 2),
                      rot=(0.0, 16.0, 0.0), verts=6, mat=STEEL_BARE, nobevel=True)
                continue
            b.cyl(0.011, -z - r - 0.024, (x, y, (z + r - 0.024) / 2), verts=6,
                  mat=STEEL_BARE, nobevel=True)
    # A cable tray beside the pipes: a flat bright plane overhead reads at distance.
    b.box((RUN_LENGTH, 0.200, 0.020), (0.0, 0.310, -0.170), mat=GALVANISED, nobevel=True)
    for x in (-0.960, 0.0, 0.960):
        b.box((0.026, 0.200, 0.060), (x, 0.310, -0.150), mat=GALVANISED, nobevel=True)
        b.cyl(0.009, 0.100, (x, 0.310, -0.050), verts=6, mat=STEEL_BARE, nobevel=True)
    for y in (0.250, 0.310, 0.370):
        b.cyl(0.014, RUN_LENGTH - 0.10, (0.0, y, -0.146), rot=(0.0, 90.0, 0.0), verts=6,
              mat=RUBBER, nobevel=True)
    return b


def build_pipe_valve_cluster() -> PropBuild:
    """The scan's globe valve and a tee on a wall manifold, plus a gauge. WALL.

    §04's 정비공 turns things on; this is what "on" looks like when it belongs
    to the building. The valve — red handwheel facing the corridor — is the
    scan's 5.8 k-triangle showpiece decimated to ~800, and it carries the red
    the procedural version had to paint on with enamel tori. The gauge stays
    procedural: its face is albedo 0.86, the brightest disc in the kit, so the
    cluster resolves before its outline does."""
    b = PropBuild("Dress_PipeValve_Cluster")
    valve = _adopt(b, _append_object(PIPE_SOURCE, "modular_industrial_pipes_01_pipe08"),
                   PIPE_VALVE02)
    _decimate(valve, 0.14)
    # Axis to origin, base to z=0. The axis is MEASURED (top-flange centroid at
    # x −0.3003, y 0.0253), not the bbox centre — the handwheel bulge skews the
    # bbox 3 cm, which round 2's render exposed as a valve parked off its riser.
    _orient(valve, translate=(0.3003, -0.0253, 0.914))
    # The scan's wheel sits 24° shy of the kit's −Y front; square it up so the
    # red disc faces the corridor the way the procedural handwheels did.
    _orient(valve, rot_deg=(0.0, 0.0, -24.4))
    _orient(valve, translate=(-0.200, -0.117, 1.660))
    b.pivot_part = valve
    tee = _adopt(b, _append_object(PIPE_SOURCE, "modular_industrial_pipes_01_pipe07"),
                 PIPE_VALVE02)
    _decimate(tee, 0.24)
    _orient(tee, translate=(0.0, -0.025, 0.131))
    _orient(tee, translate=(0.200, -0.117, 1.660))
    # The manifold both risers stand on, and the wall memory under the valve.
    b.cyl(0.052, 0.700, (0.0, -0.117, 1.712), rot=(0.0, 90.0, 0.0), verts=12,
          mat=GALVANISED, nobevel=True)
    b.quad(0.050, 0.140, (-0.200, -0.002, 1.600), rot=(90.0, 2.0, 0.0), mat=RUST_HEAVY)
    # Gauge on a brass stub between the risers.
    b.cyl(0.016, 0.150, (0.0, -0.117, 1.815), verts=6, mat=BRASS_FITTING, nobevel=True)
    b.cyl(0.075, 0.045, (0.0, -0.117, 1.905), rot=(90.0, 0.0, 0.0), verts=16,
          mat=BRASS_FITTING, nobevel=True)
    b.cyl(0.064, 0.012, (0.0, -0.146, 1.905), rot=(90.0, 0.0, 0.0), verts=16,
          mat=GAUGE_FACE, nobevel=True)
    b.box((0.048, 0.008, 0.009), (0.016, -0.154, 1.911), rot=(0.0, 24.0, 0.0), mat=GRIME,
          nobevel=True)
    return b


def build_gauge_board() -> PropBuild:
    """Three meters on a backboard at 1.45–1.95 m. WALL mount.

    Deliberately at reading height. The reason used to be §03's loop — "look at a
    thing in a beam and remember it", practised on something harmless before a 단서
    asked for it in earnest — and there is nothing to read any more. It stays at that
    height for what is left: a corridor whose only detail is at floor level reads as a
    tunnel, and §12 wants a place."""
    b = PropBuild("Dress_GaugeBoard")
    back = b.box((0.640, 0.030, 0.500), (0.0, -0.015, 1.700), mat=TIMBER_DARK, nobevel=True)
    b.pivot_part = back
    b.box((0.680, 0.020, 0.040), (0.0, -0.030, 1.940), mat=STEEL_PAINTED, nobevel=True)
    b.box((0.680, 0.020, 0.040), (0.0, -0.030, 1.460), mat=STEEL_PAINTED, nobevel=True)
    for (x, r) in ((-0.200, 0.090), (0.020, 0.072), (0.220, 0.062)):
        # The smallest meter has lost its lower mount and hangs 11° off level.
        droop = 11.0 if r == 0.062 else 0.0
        b.cyl(r + 0.014, 0.044, (x, -0.052, 1.760 - (0.008 if droop else 0.0)),
              rot=(90.0, droop, 0.0), verts=16, mat=BRASS_FITTING, nobevel=True)
        b.cyl(r, 0.012, (x, -0.078, 1.760 - (0.008 if droop else 0.0)),
              rot=(90.0, droop, 0.0), verts=16, mat=GAUGE_FACE, nobevel=True)
        b.box((r * 1.5, 0.006, 0.008), (x + r * 0.3, -0.086, 1.760 - (0.008 if droop else 0.0)),
              rot=(0.0, -34.0 - droop, 0.0), mat=GRIME, nobevel=True)
    # The board itself has streaked below the leakiest meter.
    b.quad(0.055, 0.180, (-0.245, -0.032, 1.560), rot=(90.0, -2.0, 0.0), mat=RUST_HEAVY)
    # A meter with a glass window and a paper chart behind it.
    b.box((0.260, 0.070, 0.170), (-0.060, -0.050, 1.560), mat=STEEL_PAINTED, nobevel=True)
    b.box((0.210, 0.014, 0.120), (-0.060, -0.090, 1.560), mat=GLASS_DIRTY, nobevel=True)
    b.box((0.190, 0.006, 0.100), (-0.060, -0.082, 1.560), mat=PAPER, nobevel=True)
    b.box((0.070, 0.040, 0.070), (0.190, -0.048, 1.550), mat=ENAMEL_RED, nobevel=True)
    b.cyl(0.026, 0.056, (0.190, -0.080, 1.550), rot=(90.0, 0.0, 0.0), verts=10,
          mat=STEEL_BARE, nobevel=True)
    return b


def build_conduit_wall() -> PropBuild:
    """A 2.5 m electrical conduit run with a junction box. WALL mount.

    §12 puts an 전기 패널 in every zone for the Engineer; conduit is what connects
    it to the rest of the building. Without it the panel is a box nailed to a wall
    and the zone-light mechanic looks like a UI element in a costume."""
    b = PropBuild("Dress_Conduit_Wall")
    main = b.cyl(0.026, RUN_LENGTH, (0.0, -0.052, 2.480), rot=(0.0, 90.0, 0.0), verts=8,
                 mat=GALVANISED, nobevel=True)
    b.pivot_part = main
    b.cyl(0.020, RUN_LENGTH, (0.0, -0.048, 2.380), rot=(0.0, 90.0, 0.0), verts=8,
          mat=GALVANISED, nobevel=True)
    for x in (-1.050, -0.350, 0.350, 1.050):
        b.box((0.036, 0.070, 0.036), (x, -0.038, 2.480), mat=STEEL_BARE, nobevel=True)
        b.box((0.030, 0.062, 0.030), (x, -0.036, 2.380), mat=STEEL_BARE, nobevel=True)
    # Junction box and a drop to switch height. The box has rust-bled down the
    # wall, and one strap on the drop has sheared so the conduit bows off plumb.
    b.box((0.180, 0.090, 0.180), (0.680, -0.045, 2.430), mat=GALVANISED)
    b.cyl(0.012, 0.010, (0.680, -0.092, 2.430), verts=8, mat=STEEL_BARE, nobevel=True)
    b.cyl(0.020, 1.020, (0.687, -0.052, 1.920), rot=(0.0, 1.2, 0.0), verts=8,
          mat=GALVANISED, nobevel=True)
    b.box((0.110, 0.070, 0.160), (0.680, -0.050, 1.400), mat=GALVANISED, nobevel=True)
    b.box((0.040, 0.030, 0.070), (0.680, -0.094, 1.400), mat=ENAMEL, nobevel=True)
    b.quad(0.070, 0.240, (0.660, -0.001, 2.220), rot=(90.0, -3.0, 0.0), mat=RUST_HEAVY)
    b.box((0.052, 0.020, 0.016), (0.700, -0.060, 1.760), rot=(0.0, 0.0, 32.0),
          mat=STEEL_BARE, nobevel=True)  # the sheared strap, still bolted one side
    # A cable that came loose and hangs in a loop.
    for i, (x, z) in enumerate(((-0.60, 2.300), (-0.44, 2.180), (-0.28, 2.240))):
        b.cyl(0.010, 0.230, (x, -0.040, z), rot=(0.0, 62.0 - i * 44.0, 0.0), verts=6,
              mat=RUBBER, nobevel=True)
    return b


def build_vent_grille() -> PropBuild:
    """A wall vent at 2.05–2.45 m. WALL mount. Small, cheap, everywhere."""
    b = PropBuild("Dress_VentGrille")
    frame_obj = b.box((0.620, 0.040, 0.420), (0.0, -0.020, 2.250), mat=STEEL_PAINTED,
                      nobevel=True)
    b.pivot_part = frame_obj
    b.box((0.560, 0.030, 0.360), (0.0, -0.030, 2.250), mat=GRIME, nobevel=True)
    for i in range(6):
        if i == 1:
            # One louvre pried and left bent — every grille in a building this
            # old has met a crowbar. The piece's asymmetric feature.
            b.box((0.270, 0.026, 0.036), (-0.135, -0.052, 2.166), rot=(52.0, 0.0, -4.0),
                  mat=GALVANISED, nobevel=True)
            b.box((0.250, 0.026, 0.036), (0.140, -0.044, 2.160), rot=(28.0, 0.0, 0.0),
                  mat=GALVANISED, nobevel=True)
            continue
        b.box((0.540, 0.026, 0.036), (0.0, -0.044, 2.100 + i * 0.060), rot=(28.0, 0.0, 0.0),
              mat=GALVANISED, nobevel=True)
    for (sx, sz) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.cyl(0.012, 0.014, (sx * 0.280, -0.046, 2.250 + sz * 0.190), rot=(90.0, 0.0, 0.0),
              verts=6, mat=STEEL_BARE, nobevel=True)
    # Exhaust grime blown through the slats onto the frame's lower lip.
    b.quad(0.480, 0.052, (0.02, -0.0405, 2.068), rot=(90.0, -2.0, 0.0), mat=GRIME)
    b.quad(0.030, 0.110, (-0.270, -0.0405, 2.130), rot=(90.0, 3.0, 0.0), mat=RUST_HEAVY)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  HANGING — the ceiling is half of every frame in a first-person game and it
#  is the half nobody dresses. §05: dark and first person, so what hangs into
#  the beam matters more than what sits on the floor beside it.
# ══════════════════════════════════════════════════════════════════════════


def build_bulb_cord() -> PropBuild:
    """A bare bulb on a cord with a tin reflector. CEILING mount, 1.02 m drop.

    §03 makes darkness the lock on the objective, so a fitting that lights a
    corridor for free would be a design bug. Two things stop that. The material is
    emissive but weak, and the scatter tool gives a *minority* of these a real
    light whose range is one MapKit cell — so a working bulb marks a cell and
    never replaces the flashlight's 12 m reach.

    The reflector is the other half: even dead, its underside is albedo 0.72 and
    catches the player's own beam, so the ceiling stops being a void."""
    b = PropBuild("Dress_BulbCord")
    rose = b.cyl(0.062, 0.030, (0.0, 0.0, -0.015), verts=12, mat=GRIME, nobevel=True)
    b.pivot_part = rose
    # The flex has a kink where somebody once yanked it — two segments, not one.
    b.cyl(0.007, 0.400, (0.0, 0.0, -0.230), verts=6, mat=RUBBER, nobevel=True)
    b.cyl(0.007, 0.260, (-0.011, 0.007, -0.552), rot=(2.5, -4.0, 0.0), verts=6,
          mat=RUBBER, nobevel=True)
    b.cyl(0.026, 0.090, (-0.018, 0.011, -0.700), verts=10, mat=STEEL_PAINTED, nobevel=True)
    # Conical shade: painted steel OUTSIDE, white enamel INSIDE. Round 1 proved
    # the docstring wrong the way only a render can — a metallic=1 cone over a
    # black world is a black hole, so the "albedo 0.72 underside" claim now has
    # an actual dielectric enamel surface to be true OF. The inner cone is what
    # a player's beam lights from below.
    b.cone(0.170, 0.045, 0.180, (-0.018, 0.011, -0.800), rot=(180.0, 0.0, 0.0), verts=14,
           mat=STEEL_PAINTED, nobevel=True)
    b.cone(0.160, 0.040, 0.170, (-0.018, 0.011, -0.806), rot=(180.0, 0.0, 0.0), verts=14,
           mat=ENAMEL, nobevel=True)
    b.cyl(0.022, 0.055, (-0.018, 0.011, -0.885), verts=10, mat=BRASS_FITTING, nobevel=True)
    b.sph(0.048, (-0.018, 0.011, -0.955), scale=(1.0, 1.0, 1.25), segs=12, rings=6,
          mat=BULB_DEAD, nobevel=True)
    # Fly dirt on the shade rim — one dark bite out of a bright edge.
    b.box((0.070, 0.020, 0.014), (0.120, 0.052, -0.884), rot=(0.0, 0.0, 24.0), mat=GRIME,
          nobevel=True)
    return b


def build_bulb_caged() -> PropBuild:
    """The scanned twin-chain caged fitting, dead. CEILING mount, 0.68 m drop.

    PolyHaven's caged_hanging_light: a 1.05 m military strip fitting whose glass
    windows sit behind a real wire cage. The scan's own chains, cable and
    ceiling pads are harvested OFF (their islands all reach above z −0.50) and
    the body is re-hung on the kit's procedural chain — same links as every
    other hang in the building, and a third of the triangles.

    THE BULB CONTRACT HOLDS BY CONSTRUCTION: the glass faces are found through
    the scan's own emissive map and given the `Dress_BulbDead` slot, so
    `ScatterSession.LightBulb` still swaps them to the emissive `Dress_BulbLit`
    kit material by name, and `LightStratifiedBulbs` still finds the piece by
    its `Dress_Bulb` prefix. The housing keeps the scan's maps."""
    b = PropBuild("Dress_BulbCaged")
    lamp = _adopt(b, _append_object("caged_hanging_light", "caged_hanging_light"),
                  CAGED_LAMP)
    kept = _filter_islands(lamp, lambda lo, hi: hi.z <= -0.50)
    if kept < 8:
        blendkit.fail(f"Dress_BulbCaged: island harvest kept only {kept} islands — "
                      "the vendored scan changed shape")
    _decimate(lamp, 0.086)
    painted = _paint_faces_by_image(
        lamp, _source_texture("caged_hanging_light", "caged_hanging_light_emissive_2k.png"),
        BULB_DEAD, threshold=0.20)
    if painted < 8:
        blendkit.fail(f"Dress_BulbCaged: only {painted} glass faces found via the emissive "
                      "map — the Dress_BulbDead slot would be empty and "
                      "ScatterSession.LightBulb would have nothing to swap")
    _orient(lamp, scale=0.90)
    b.pivot_part = lamp
    # Ceiling roses over the scan's own chain lugs, and the kit chain between.
    for sx in (-1.0, 1.0):
        b.cyl(0.045, 0.024, (sx * 0.114, 0.0, -0.012), verts=10, mat=STEEL_PAINTED,
              nobevel=True)
        _chain(b, sx * 0.114, 0.0, -0.028, -0.462, radius=0.020)
    return b


def build_chain_hang() -> PropBuild:
    """A chain hanging from a ceiling anchor, 1.00 m. CEILING mount.

    Vertical bright dashes in a frame otherwise made of horizontals. It also
    hangs *into* a beam rather than sitting under one, which is the difference
    between the ceiling being lit and the ceiling existing.

    The drop is capped by head clearance, not by taste: 3.0 m of kit clear height
    minus a 1.63 m player and 0.35 m of crown gap leaves 1.02 m, and `emit()`
    fails the build if a ceiling piece exceeds it."""
    b = PropBuild("Dress_ChainHang")
    plate = b.box((0.120, 0.120, 0.024), (0.0, 0.0, -0.012), mat=STEEL_PAINTED, nobevel=True)
    b.pivot_part = plate
    b.torus(0.038, 0.010, (0.0, 0.0, -0.056), rot=(90.0, 0.0, 0.0), mseg=10, nseg=4,
            mat=STEEL_BARE)
    _chain(b, 0.0, 0.0, -0.086, -0.760, radius=0.028)
    # It ends in HARDWARE, not a plug: a rusted shackle ring with an S-hook
    # dropped through it, hanging a few degrees off the chain's axis.
    b.torus(0.048, 0.011, (0.006, 0.0, -0.800), rot=(90.0, 0.0, 20.0), mseg=10, nseg=4,
            mat=RUST_HEAVY)
    b.torus(0.034, 0.009, (0.018, 0.010, -0.878), rot=(84.0, 0.0, 110.0), mseg=10,
            nseg=4, mat=RUST_HEAVY)
    b.torus(0.030, 0.009, (0.026, 0.016, -0.940), rot=(96.0, 0.0, 110.0), mseg=10,
            nseg=4, mat=STEEL_BARE)
    return b


def build_sheet_hanging() -> PropBuild:
    """A dust sheet on a rail, 1.45 m wide, hanging to 1.05 m above the floor.

    The brightest large surface the kit can put in a corridor, and a real
    line-of-sight break: §06 releases aggro on 3 s without sight, and §12 wants
    those breaks at 15~25 m. A sheet does it without closing the route, so the
    scatter tool keeps its collider — a hider behind one is hidden, which a
    renderer alone would not deliver."""
    b = PropBuild("Dress_SheetHanging")
    rail = b.cyl(0.024, 1.500, (0.0, 0.0, -0.070), rot=(0.0, 90.0, 0.0), verts=8,
                 mat=STEEL_BARE, nobevel=True)
    b.pivot_part = rail
    for sx in (-1.0, 1.0):
        b.box((0.050, 0.100, 0.090), (sx * 0.740, 0.0, -0.045), mat=STEEL_PAINTED,
              nobevel=True)
    # Cloth as folds with real DEPTH: panels alternate toward and away from the
    # rail plane and stop at staggered heights. Round 1's coplanar panels with
    # ±3° tilts rendered as one flat poster — a fold that does not displace in Y
    # cannot catch a shadow, and cloth with a ruled bottom edge is paper.
    panels = ((-0.560, 0.300, -0.016, 1.800, -4.0), (-0.290, 0.310, 0.018, 1.900, 2.5),
              (-0.010, 0.300, -0.020, 1.860, -1.5), (0.270, 0.305, 0.014, 1.930, 3.5),
              (0.545, 0.300, -0.014, 1.760, -3.0))
    for (x, w, y, drop, tilt) in panels:
        b.box((w, 0.026, drop), (x, y, -0.090 - drop / 2), rot=(0.0, tilt, 0.0),
              mat=CLOTH_DUST, nobevel=True)
    # The bottom third kicks out where feet and carts have brushed it.
    b.box((0.560, 0.024, 0.420), (0.28, -0.052, -1.930), rot=(7.0, 2.0, 0.0),
          mat=CLOTH_DUST, nobevel=True)
    # A torn corner hanging by a thread.
    b.box((0.300, 0.022, 0.380), (0.585, 0.012, -1.700), rot=(0.0, -16.0, 3.0),
          mat=CLOTH_DUST, nobevel=True)
    b.box((1.420, 0.036, 0.060), (0.0, 0.0, -0.130), mat=CLOTH_DUST, nobevel=True)
    # Water-mark tide lines, irregular and overlapping, not one neat patch.
    b.box((0.700, 0.014, 0.300), (-0.24, -0.032, -1.700), rot=(0.0, 1.5, -2.0),
          mat=CLOTH_STAINED, nobevel=True)
    b.box((0.460, 0.012, 0.200), (0.10, -0.036, -1.850), rot=(0.0, -2.0, 3.0),
          mat=CLOTH_STAINED, nobevel=True)
    b.box((0.330, 0.012, 0.130), (-0.48, -0.034, -1.560), rot=(0.0, 0.0, -5.0),
          mat=CLOTH_STAINED, nobevel=True)
    # Grime hem where it has dragged on the floor.
    b.box((0.520, 0.014, 0.070), (0.30, -0.056, -2.100), rot=(7.0, 0.0, 0.0), mat=GRIME,
          nobevel=True)
    return b


def build_cobweb_corner() -> PropBuild:
    """A web filling a wall/wall/ceiling corner, 0.95 m across. CORNER mount.

    Authored with the corner itself at the origin and all geometry in the −X/−Y/−Z
    octant, so the scatter tool drops it on a corner point with no offset. Albedo
    0.72 and paper-thin: in a beam it is a bright triangle where the room's
    geometry stops, which is exactly the cue that tells a player a corner is a
    corner and not a doorway."""
    b = PropBuild("Dress_CobwebCorner")
    # DUST FUNNEL, third attempt, and the reasoning is worth keeping: round 1
    # built big regular fan sheets (read: stacked paper); round 2 built straight
    # bright threads (read: umbrella skeleton). What a basement corner actually
    # holds is a broken FUNNEL of dusty membrane — small ragged patches dense at
    # the corner, thinning outward, with only a few short trailing threads. So:
    # membrane scraps as thin boxes (two-sided, unlike a quad) in a rough cone
    # around the corner diagonal, gaps between them, every one differently
    # tilted; threads short and few; the heavy hank hanging below.
    first = None
    # The corner pocket: two membranes snug against the walls' meeting line,
    # angled like a funnel throat.
    first = b.box((0.30, 0.003, 0.26), (-0.10, -0.10, -0.115), rot=(38.0, -34.0, 45.0),
                  mat=COBWEB, nobevel=True)
    b.box((0.22, 0.003, 0.18), (-0.065, -0.065, -0.24), rot=(-26.0, -48.0, 45.0),
          mat=COBWEB, nobevel=True)
    b.pivot_part = first
    # Torn curtains hanging off both wall-ceiling edges: slim strips of varied
    # drop, each with its own lean, dense near the corner and ragged further
    # out. This is the read that finally says "web" in one glance — drooping
    # fringe — where flat scraps said "paper" and threads said "umbrella".
    # Each strip is a flattened 4-vert cone — wide where it roots at the
    # ceiling line, tapering to a point as it hangs. Straight box strips read
    # as venetian blinds; the taper is what makes them tatters. A dust band
    # runs along both wall-ceiling edges so the fringe grows out of something.
    b.box((0.005, 0.640, 0.055), (-0.012, -0.330, -0.026), mat=COBWEB, nobevel=True)
    b.box((0.640, 0.005, 0.055), (-0.330, -0.012, -0.026), mat=COBWEB, nobevel=True)

    def _tatter(loc, drop, w, rot):
        obj = b.cone(w * 0.16, w * 0.62, drop, loc, rot=rot, verts=4, mat=COBWEB,
                     nobevel=True)
        obj.scale = Vector((1.0, 0.10, 1.0))
        blendkit.apply_transforms(obj, scale=True)

    for (yy, w, drop, lean) in ((-0.11, 0.11, 0.36, -7.0), (-0.20, 0.08, 0.21, 5.0),
                                (-0.30, 0.12, 0.48, -4.0), (-0.41, 0.07, 0.17, 10.0),
                                (-0.54, 0.09, 0.30, -12.0)):
        _tatter((-0.013, yy, -drop / 2 - 0.010), drop, w, (lean, 3.0, 90.0))
    for (xx, w, drop, lean) in ((-0.15, 0.10, 0.32, 8.0), (-0.25, 0.07, 0.44, -6.0),
                                (-0.36, 0.11, 0.24, 5.0), (-0.49, 0.08, 0.31, -9.0)):
        _tatter((xx, -0.013, -drop / 2 - 0.010), drop, w, (3.0, lean, 0.0))
    # Threads tying the outermost curtains back toward the pocket.
    threads = ((218.0, -26.0, 0.10, 0.44), (236.0, -40.0, 0.12, 0.40),
               (207.0, -48.0, 0.10, 0.38))
    for (yaw, pitch, start, length) in threads:
        ya, pa = math.radians(yaw), math.radians(pitch)
        d = Vector((math.cos(pa) * math.cos(ya), math.cos(pa) * math.sin(ya),
                    math.sin(pa)))
        mid = d * (start + length / 2)
        b.box((0.005, 0.005, length), (mid.x - 0.01, mid.y - 0.01, mid.z - 0.01),
              rot=(math.degrees(math.acos(max(-1.0, min(1.0, d.z)))), 0.0, yaw + 90.0),
              mat=COBWEB, nobevel=True)
    # The heavy hank, hanging out of the corner with a bend in it.
    b.box((0.045, 0.045, 0.240), (-0.185, -0.185, -0.290), rot=(6.0, -8.0, 45.0),
          mat=COBWEB, nobevel=True)
    b.box((0.028, 0.028, 0.150), (-0.205, -0.240, -0.470), rot=(14.0, -16.0, 45.0),
          mat=COBWEB, nobevel=True)
    return b


# ══════════════════════════════════════════════════════════════════════════
#  SIGNAGE — blank plates. Each of these carried one `Clue_Face` island mapped
#  0..1 for §13's host-rendered glyph; §03 단서 is deleted and so is the glyph.
#  They are kept as what is left when the writing goes: painted steel, an enamel
#  field, bolts and chains. §12 wants a corridor to read as a place.
# ══════════════════════════════════════════════════════════════════════════


def build_wall_sign() -> PropBuild:
    """An enamel sign at 1.42–1.78 m. WALL mount.

    The face is a **glossy** enamel field (roughness 0.30) in a bright bezel. §03
    files ㅁ↔ㅇ under blur, and gloss is how a flashlight produces blur: held close
    and square on, the beam's own hotspot blows the plate out and the difference
    between a closed square and a closed circle stops existing. Read it from an
    angle and it is legible — which is the version the player who is not in a
    hurry gets."""
    b = PropBuild("Dress_WallSign")
    W, H = 0.520, 0.360
    back = b.box((W, 0.026, H), (0.0, -0.013, 1.600), mat=STEEL_PAINTED, nobevel=True)
    b.pivot_part = back
    b.box((W, 0.016, 0.034), (0.0, -0.032, 1.600 + H / 2 - 0.017), mat=STEEL_BARE,
          nobevel=True)
    b.box((W, 0.016, 0.034), (0.0, -0.032, 1.600 - H / 2 + 0.017), mat=STEEL_BARE,
          nobevel=True)
    for sx in (-1.0, 1.0):
        b.box((0.034, 0.016, H), (sx * (W / 2 - 0.017), -0.032, 1.600), mat=STEEL_BARE,
              nobevel=True)
    b.box((W - 0.060, 0.012, H - 0.060), (0.0, -0.032, 1.600), mat=ENAMEL, nobevel=True)
    for (sx, sz) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.cyl(0.010, 0.014, (sx * (W / 2 - 0.026), -0.040, 1.600 + sz * (H / 2 - 0.026)),
              rot=(90.0, 0.0, 0.0), verts=6, mat=STEEL_BARE, nobevel=True)
    # 위험 in stencilled red on a red-bordered field — the standard Korean plant
    # danger plate, drawn as geometry (no font licence to survey). Identical on
    # every instance: paint, not information.
    for (cx, cz, w, h) in ((0.0, 1.752, W - 0.10, 0.016), (0.0, 1.448, W - 0.10, 0.016),
                           (-0.212, 1.600, 0.016, H - 0.10), (0.212, 1.600, 0.016, H - 0.10)):
        b.quad(w, h, (cx, -0.0395, cz), rot=(90.0, 0.0, 0.0), mat=ENAMEL_RED)
    _stencil(b, TEXT_위험, -0.2015, 1.505, 0.185, -0.040, mat_name=ENAMEL_RED)
    # One rust drip from the top-left bolt, and grime settling on the lower rail.
    b.quad(0.026, 0.150, (-0.234, -0.041, 1.500), rot=(90.0, 0.0, 3.0), mat=RUST_HEAVY)
    b.box((W - 0.20, 0.006, 0.012), (0.03, -0.0405, 1.600 - H / 2 + 0.036), mat=GRIME,
          nobevel=True)
    return b


def build_hanging_sign() -> PropBuild:
    """A double-sided sign on two chains. CEILING mount. §03's 좌↔우.

    Both faces used to carry a ``Clue_Face`` quad, one of them UV-mirrored, so a
    single host-stamped glyph read correctly from one side and reversed from the
    other — §03's 좌↔우 without a mirror. The glyph is deleted; the plate is
    symmetric in Y about its own mid-plane and stays that way, because neither
    face being "the front" is what lets the scatterer hang it in any corridor.

    The plate is asserted mirror-symmetric about its own plane, because a single
    bracket on one side would tell a player which face is the front and the pair
    would collapse."""
    b = PropBuild("Dress_HangingSign")
    W, H = 0.680, 0.320
    top = -0.560
    bar = b.cyl(0.014, W + 0.120, (0.0, 0.0, top), rot=(0.0, 90.0, 0.0), verts=8,
                mat=STEEL_BARE, nobevel=True)
    b.pivot_part = bar
    for sx in (-1.0, 1.0):
        _chain(b, sx * 0.290, 0.0, -0.030, top, radius=0.026)
        b.torus(0.026, 0.007, (sx * 0.290, 0.0, top - 0.030), rot=(0.0, 0.0, 0.0),
                mseg=8, nseg=4, mat=STEEL_BARE)
    b.box((0.090, 0.090, 0.020), (0.0, 0.0, -0.010), mat=STEEL_PAINTED, nobevel=True)
    zc = top - 0.210
    # The plate: symmetric in Y about its own mid-plane, so neither face is "the front".
    b.box((W, 0.024, H), (0.0, 0.0, zc), mat=STEEL_PAINTED, nobevel=True)
    for sy in (-1.0, 1.0):
        b.box((W - 0.050, 0.010, H - 0.050), (0.0, sy * 0.017, zc), mat=ENAMEL, nobevel=True)
        b.box((W, 0.008, 0.026), (0.0, sy * 0.016, zc + H / 2 - 0.013), mat=STEEL_BARE,
              nobevel=True)
        b.box((W, 0.008, 0.026), (0.0, sy * 0.016, zc - H / 2 + 0.013), mat=STEEL_BARE,
              nobevel=True)
    for sx in (-1.0, 1.0):
        b.cyl(0.012, 0.048, (sx * (W / 2 - 0.030), 0.0, zc + H / 2 + 0.008), verts=6,
              mat=STEEL_BARE, nobevel=True)
    # 출입금지, stencilled on BOTH faces — geometry, not a font, and identical
    # on every instance, so it is paint the way §12 wants a place painted, not
    # information the deleted 단서 system would have owned. Both faces carry it,
    # which keeps the plate's no-front symmetry.
    _stencil(b, TEXT_출입금지, -0.295, zc - 0.065, 0.13, -0.027)
    _stencil(b, TEXT_출입금지, -0.295, zc - 0.065, 0.13, 0.027, back=True)
    # Rust weep from each chain eye, mirrored — weather is symmetric too.
    for sy in (-1.0, 1.0):
        b.quad(0.040, 0.110, (-0.260, sy * 0.023, zc + H / 2 - 0.070),
               rot=(90.0 if sy < 0 else -90.0, 0.0, 0.0), mat=RUST_HEAVY)
    return b


def build_pipe_label() -> PropBuild:
    """A colour-banded pipe section at 2.05 m. WALL mount.

    This carried §03's 6↔9 pair — a plate with no up-cue, asserted symmetric
    under a half-turn to a millimetre. The pair died with 단서, and the symmetry
    died with this production pass, on purpose: the banded section now rusts
    harder at one clamp than the other and weeps down the wall on one side,
    because an anonymous rectangle repeated down a corridor was exactly the
    copy-paste read this pass exists to remove. The plate itself still says
    nothing — red hazard chevrons, no text."""
    b = PropBuild("Dress_PipeLabel")
    zc = 2.050
    y = -0.130
    pipe = b.cyl(0.070, 1.100, (0.0, y, zc), rot=(0.0, 90.0, 0.0), verts=12, mat=GALVANISED,
                 nobevel=True)
    b.pivot_part = pipe
    for sx in (-1.0, 1.0):
        b.cyl(0.082, 0.070, (sx * 0.470, y, zc), rot=(0.0, 90.0, 0.0), verts=12,
              mat=STEEL_BARE, nobevel=True)
        b.box((0.056, 0.026, 0.190), (sx * 0.330, y * 0.35, zc), mat=STEEL_PAINTED,
              nobevel=True)
        for sz in (-1.0, 1.0):
            b.box((0.056, abs(y) * 1.05, 0.026), (sx * 0.330, y * 0.5, zc + sz * 0.083),
                  mat=STEEL_PAINTED, nobevel=True)
    # Band and plate. Everything from here on is symmetric about the plate centre.
    for sx in (-1.0, 1.0):
        b.cyl(0.078, 0.024, (sx * 0.130, y, zc), rot=(0.0, 90.0, 0.0), verts=12,
              mat=STEEL_BARE, nobevel=True)
    b.box((0.320, 0.014, 0.170), (0.0, y - 0.076, zc), mat=ENAMEL, nobevel=True)
    for (sx, sz) in ((-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)):
        b.cyl(0.009, 0.014, (sx * 0.140, y - 0.086, zc + sz * 0.070), rot=(90.0, 0.0, 0.0),
              verts=6, mat=STEEL_BARE, nobevel=True)
    # Red hazard chevrons across the enamel — colour-coded pipe banding, which
    # is what a Korean plant room really labels a line with. No text: the plate
    # keeps its half-turn anonymity.
    for i, sx in enumerate((-0.105, -0.035, 0.035, 0.105)):
        b.quad(0.034, 0.150, (sx, y - 0.0845, zc), rot=(90.0, 38.0, 0.0), mat=ENAMEL_RED)
    # The banded section has rusted where the clamps bite, and wept onto the wall.
    b.cyl(0.0705, 0.050, (0.205, y, zc), rot=(0.0, 90.0, 0.0), verts=12, mat=RUST_HEAVY,
          nobevel=True)
    b.cyl(0.0705, 0.032, (-0.240, y, zc), rot=(0.0, 90.0, 0.0), verts=12, mat=RUST_HEAVY,
          nobevel=True)
    b.quad(0.060, 0.300, (0.330, -0.001, zc - 0.230), rot=(90.0, 2.0, 0.0),
           mat=RUST_HEAVY)
    return b


def build_door_plate() -> PropBuild:
    """A stencilled number plate at 1.42 m. WALL mount. §03's 1↔7, 손글씨체.

    0.20 m across on a brass back with an enamel field. It was §03's 1↔7 — the
    cheapest readable surface in the kit and therefore the one that could be
    everywhere, read at a glancing angle in a moving beam where an ascender and a
    serif are the same smudge. Nothing is written on it now; it is the smallest
    piece in the kit and the one that makes a doorway look like a doorway."""
    b = PropBuild("Dress_DoorPlate")
    W, H = 0.200, 0.140
    back = b.box((W, 0.010, H), (0.0, -0.005, 1.420), mat=BRASS_FITTING, nobevel=True)
    b.pivot_part = back
    b.box((W - 0.024, 0.008, H - 0.024), (0.0, -0.012, 1.420), mat=ENAMEL, nobevel=True)
    for sx in (-1.0, 1.0):
        b.cyl(0.007, 0.012, (sx * (W / 2 - 0.016), -0.018, 1.420), rot=(90.0, 0.0, 0.0),
              verts=6, mat=STEEL_BARE, nobevel=True)
    # 지하 — the one word every door in this building can honestly carry. Drawn
    # as geometry, identical on every instance, deliberately NOT a number: a
    # storey digit is exactly the wayfinding §03's deletion removed.
    _stencil(b, TEXT_지하, -0.076, 1.382, 0.072, -0.0185)
    # The plate has been polished by hands at one corner and grimed at the other.
    b.quad(0.052, 0.030, (-0.056, -0.0165, 1.362), rot=(90.0, -8.0, 0.0), mat=GRIME)
    return b


# ── Piece table ─────────────────────────────────────────────────────────────


@dataclass
class Piece:
    """One dressing piece and everything the scatter tool needs to place it."""

    name: str
    build: Callable[[], PropBuild]
    group: str
    """Placement family: Bulk · Debris · Decal · Wet · Wall · Ceiling · Corner · Sign."""

    expect: tuple[float, float, float]
    """Intended metre dimensions. Checked, so a unit-scale slip fails the build."""

    mount: str = "FLOOR"
    palettes: tuple[str, ...] = ("*",)
    """Which zone palettes may use it. `*` means any. See PALETTES."""

    weight: float = 1.0
    """Relative pick probability inside its group."""

    bevel: float = 0.005
    max_tris: int = 1600
    solid: bool = True
    """Whether the piece should keep a collider and be baked into the NavMesh."""

    keep_uvs: bool = False
    """True for pieces built from CC0 scans: their authored UVs are the seam the
    shipped texture maps land on, so smart-project must not touch them. These
    pieces also get the §7.12 dark-metal-panel check a textured surface can
    still fail — see emit()."""

    note: str = ""
    checks: tuple[str, ...] = ()


PALETTES: dict[str, str] = {
    "storage": "§12 zone A 나무 — an old storage wing: timber, cases, sheets, cobwebs.",
    "institutional": "§12 zone B 타일 — the tiled hall: lockers, cabinets, signage, meters.",
    "wet": "§12 zone C 자갈 — the unfinished, flooded end. §03's 물이 있는 층 lives here.",
    "utility": "§12 zone D 콘크리트 — plant and services: pipes, valves, benches, tools.",
}

PIECES: list[Piece] = [
    # ── Bulk — cover, and §12's 시야 차단 지점 ───────────────────────────────
    # Budget bumps on the scan-built pieces, each inside the kit's 2600 cap:
    # CaseStack_Tall 1200→2500 (four real crates at ~570 tris replace three
    # 140-tri boxes); CaseStack_Low 1300→2000 (two closed + one opened crate);
    # BulbCaged 900→2400 (a real wire cage cannot survive 900); PipeRun_Wall
    # 900→1400 (two flanged lines and a tee replace three bare cylinders).
    Piece("Dress_CaseStack_Tall", build_case_stack_tall, "Bulk", (0.776, 0.528, 1.585),
          palettes=("storage", "utility"), weight=1.2, max_tris=2500, keep_uvs=True,
          note="sightline break at eye height"),
    Piece("Dress_CaseStack_Low", build_case_stack_low, "Bulk", (1.465, 0.841, 1.008),
          palettes=("storage", "wet"), weight=1.0, max_tris=2000, keep_uvs=True,
          note="crouch cover"),
    Piece("Dress_CaseBroken", build_case_broken, "Debris", (1.181, 1.072, 0.397),
          palettes=("storage", "wet"), weight=0.8, max_tris=900),
    Piece("Dress_BarrelUpright", build_barrel_upright, "Bulk", (0.607, 0.612, 0.901),
          weight=1.3, max_tris=700, keep_uvs=True),
    Piece("Dress_BarrelCluster", build_barrel_cluster, "Bulk", (1.368, 1.299, 0.891),
          palettes=("wet", "utility"), weight=0.9, max_tris=1600, keep_uvs=True),
    Piece("Dress_BarrelToppled", build_barrel_toppled, "Bulk", (0.892, 0.612, 0.625),
          palettes=("wet", "utility", "storage"), weight=0.7, max_tris=700, keep_uvs=True),
    Piece("Dress_ShelfStocked", build_shelf_stocked, "Bulk", (0.915, 0.420, 1.962),
          palettes=("storage", "institutional", "utility"), weight=1.2, max_tris=2600,
          keep_uvs=True,
          note="§12 sightline blocker; gaps make it cover rather than wall"),
    Piece("Dress_ShelfToppled", build_shelf_toppled, "Bulk", (1.051, 2.166, 0.440),
          palettes=("storage", "institutional"), weight=0.6, max_tris=1200, keep_uvs=True),
    Piece("Dress_FilingCabinet", build_filing_cabinet, "Bulk", (0.480, 0.995, 1.333),
          palettes=("institutional", "utility"), weight=1.0, max_tris=1100),
    Piece("Dress_LockerBank", build_locker_bank, "Bulk", (1.090, 0.668, 1.860),
          palettes=("institutional",), weight=0.9, max_tris=1600),
    Piece("Dress_Workbench", build_workbench, "Bulk", (1.839, 0.829, 1.520),
          palettes=("utility", "storage"), weight=0.8, max_tris=2600),
    Piece("Dress_TableBroken", build_table_broken, "Bulk", (1.501, 0.902, 0.831),
          palettes=("storage", "institutional"), weight=0.7, max_tris=1100),
    Piece("Dress_Chair", build_chair, "Bulk", (0.463, 0.415, 0.920),
          palettes=("storage", "institutional"), weight=0.7, max_tris=800),
    Piece("Dress_ChairBroken", build_chair_broken, "Debris", (0.851, 0.817, 0.416),
          palettes=("storage", "institutional"), weight=0.6, max_tris=800),
    # The kit's one brand-new piece: entering through the manifest means
    # BulkPass/PickBulk place it with zero ScatterSession changes.
    Piece("Dress_Generator", build_generator, "Bulk", (0.814, 0.566, 0.578),
          palettes=("utility", "storage"), weight=0.25, max_tris=2600, keep_uvs=True,
          note="dead plant for zone D; §06's silence made visible. Weight cut 0.9→0.25 "
               "on 2026-08-09: at 0.9 the building scattered twelve identical EN2500s "
               "and a hero landmark repeated twelve times stops being one; at 0.25 the "
               "same seed measures six, which an 8-storey building can carry."),
    # ── Debris — never above knee height (§05's 65 % backward speed) ────────
    Piece("Dress_RubblePile", build_rubble_pile, "Debris", (1.201, 0.957, 0.348),
          palettes=("wet", "utility", "storage"), weight=1.2, max_tris=1200),
    Piece("Dress_RubbleSmall", build_rubble_small, "Debris", (0.601, 0.526, 0.164),
          weight=1.4, max_tris=700),
    Piece("Dress_PlanksFallen", build_planks_fallen, "Debris", (1.580, 1.124, 0.255),
          palettes=("storage", "wet"), weight=1.0, max_tris=800),
    Piece("Dress_PapersScatter", build_papers_scatter, "Decal", (1.209, 1.164, 0.041),
          weight=1.3, max_tris=600, solid=False, note="albedo 0.78 — the kit's brightest floor"),
    Piece("Dress_ToolScatter", build_tool_scatter, "Decal", (0.569, 0.442, 0.060),
          palettes=("utility", "storage"), weight=0.9, max_tris=700, solid=False),
    # ── Wet — §03: "그것은 물이 있는 층에 있다" ─────────────────────────────
    Piece("Dress_PuddleLarge", build_puddle_large, "Decal", (1.860, 1.340, 0.012),
          palettes=("wet", "institutional"), weight=1.4, max_tris=700, solid=False,
          note="§12 zone C 물, and the kit's only mirror"),
    Piece("Dress_PuddleSmall", build_puddle_small, "Decal", (0.710, 0.660, 0.008),
          weight=1.2, max_tris=500, solid=False),
    Piece("Dress_DrainGrate", build_drain_grate, "Decal", (0.960, 0.960, 0.050),
          palettes=("wet", "institutional", "utility"), weight=0.6, max_tris=800,
          solid=False),
    Piece("Dress_DripStain_Wall", build_drip_stain_wall, "Wall", (0.520, 0.180, 2.478),
          mount="WALL", palettes=("wet", "institutional"), weight=0.9, max_tris=900,
          solid=False),
    # ── Wall services ──────────────────────────────────────────────────────
    Piece("Dress_PipeRun_Wall", build_pipe_run_wall, "Wall", (2.500, 0.206, 0.833),
          mount="WALL", weight=1.6, max_tris=1400, solid=False, keep_uvs=True,
          note="tiles on the 2.5 m grid; the flanged lines are the corridor's depth cue"),
    Piece("Dress_PipeValve_Cluster", build_pipe_valve_cluster, "Wall",
          (0.747, 0.340, 0.542), mount="WALL", palettes=("utility", "wet"), weight=0.8,
          max_tris=1400, solid=False, keep_uvs=True),
    Piece("Dress_GaugeBoard", build_gauge_board, "Wall", (0.680, 0.100, 0.520),
          mount="WALL", palettes=("utility", "institutional"), weight=0.8, max_tris=1400,
          solid=False),
    Piece("Dress_Conduit_Wall", build_conduit_wall, "Wall", (2.500, 0.104, 1.240),
          mount="WALL", weight=1.2, max_tris=1200, solid=False),
    Piece("Dress_VentGrille", build_vent_grille, "Wall", (0.620, 0.084, 0.426),
          mount="WALL", weight=1.0, max_tris=900, solid=False),
    # ── Hanging ────────────────────────────────────────────────────────────
    Piece("Dress_PipeRun_Ceiling", build_pipe_run_ceiling, "Ceiling",
          (2.500, 0.652, 0.421), mount="CEILING", weight=1.5, max_tris=1600, solid=False,
          keep_uvs=True),
    Piece("Dress_BulbCord", build_bulb_cord, "Ceiling", (0.340, 0.340, 1.020),
          mount="CEILING", weight=1.4, max_tris=700, solid=False,
          note="§03: a minority get a real light, ranged to one cell"),
    Piece("Dress_BulbCaged", build_bulb_caged, "Ceiling", (1.047, 0.285, 0.674),
          mount="CEILING", weight=1.0, max_tris=2400, solid=False, keep_uvs=True,
          note="glass carries Dress_BulbDead; LightBulb swaps it lit by name"),
    Piece("Dress_ChainHang", build_chain_hang, "Ceiling", (0.120, 0.120, 0.977),
          mount="CEILING", palettes=("storage", "wet", "utility"), weight=0.8,
          max_tris=1400, solid=False),
    # The one ceiling piece exempt from head clearance. It is a cloth sheet: it is
    # *meant* to hang into the room and break a line of sight (§06 releases aggro
    # on 3 s without sight), and the manifest flags it `hangs_low` so the scatter
    # tool keeps it flat against a wall and out of dead-end mouths rather than
    # slung across a route a fleeing player has to take at 5.6 m/s.
    Piece("Dress_SheetHanging", build_sheet_hanging, "Ceiling", (1.554, 0.140, 2.150),
          mount="CEILING", palettes=("storage", "institutional"), weight=0.5,
          max_tris=900, checks=("hangs_low",),
          note="a §06 line-of-sight break that is not a wall; hangs into the room"),
    Piece("Dress_CobwebCorner", build_cobweb_corner, "Corner", (0.708, 0.736, 0.602),
          mount="CORNER", palettes=("storage", "wet"), weight=1.0, max_tris=900,
          solid=False),
    # ── Signage ────────────────────────────────────────────────────────────
    # These four were §03's 혼동쌍 fittings and each carried a Clue_Face the host
    # stamped a glyph onto: ㅁ↔ㅇ on a glossy field, 좌↔우 on a mirrored back
    # face, 6↔9 on a plate with no up-cue, 1↔7 on a matte plate read in passing.
    # 단서 is deleted, so the glyph faces and the four misread checks that made
    # them misreadable went with it. The enamel plate under each one is kept:
    # a basement with signage in it looks like a basement, and §12 wants a
    # corridor to read as a place. Nothing on them is information.
    Piece("Dress_WallSign", build_wall_sign, "Sign", (0.520, 0.045, 0.360), mount="WALL",
          weight=1.0, max_tris=800, solid=False, note="wall plate, blank"),
    Piece("Dress_HangingSign", build_hanging_sign, "Sign", (0.800, 0.086, 0.930),
          mount="CEILING", weight=0.7, max_tris=1600, solid=False,
          note="double-sided plate on two chains, blank"),
    Piece("Dress_PipeLabel", build_pipe_label, "Sign", (1.100, 0.226, 0.477), mount="WALL",
          weight=0.9, max_tris=1200, solid=False, note="labelled pipe band, blank"),
    Piece("Dress_DoorPlate", build_door_plate, "Sign", (0.200, 0.026, 0.140), mount="WALL",
          weight=1.1, max_tris=400, solid=False, note="door number plate, blank"),
]


# ── Emit ────────────────────────────────────────────────────────────────────

ROWS: list[dict] = []


def pivot_shift(b: PropBuild, mount: str) -> Vector:
    """Offset that puts the origin where the mount convention says.

    Extends `gen_props.pivot_shift` with the two mounts a dressing kit needs and
    a floor-standing prop kit does not: CEILING, whose origin is the ceiling plane so a piece
    hangs from wherever it is dropped, and CORNER, whose origin is the
    wall/wall/ceiling corner itself.
    """
    if mount in ("FLOOR", "WALL"):
        return gen_props.pivot_shift(b, mount)

    ref = [b.pivot_part] if b.pivot_part is not None else b.parts
    rlo, rhi = world_bbox(ref)
    alo, ahi = world_bbox(b.parts)
    if mount == "CEILING":
        return Vector((-(rlo.x + rhi.x) / 2, -(rlo.y + rhi.y) / 2, -ahi.z))
    if mount == "CORNER":
        return Vector((-ahi.x, -ahi.y, -ahi.z))
    blendkit.fail(f"unknown mount '{mount}'")
    raise AssertionError


def emit(piece: Piece) -> None:
    blendkit.reset_scene()
    b = piece.build()
    if not b.parts:
        blendkit.fail(f"{piece.name}: builder produced no geometry")

    shift = pivot_shift(b, piece.mount)
    for obj in b.parts:
        obj.location = obj.location + shift
    bpy.context.view_layer.update()

    bevel_skipped = 0
    for obj in b.parts:
        blendkit.apply_transforms(obj, location=True, rotation=True, scale=True)
        if piece.bevel <= 0.0 or obj.name in b.nobevel:
            bevel_skipped += 1
            continue
        if min(obj.dimensions) < piece.bevel * 4.0:
            bevel_skipped += 1
            continue
        blendkit.bevel(obj, width=piece.bevel, segments=1)

    obj = blendkit.join(b.parts, piece.name)
    blendkit.triangulate(obj)
    sharp = gen_props.apply_smooth(obj, 30.0)
    if piece.keep_uvs:
        # A scan-built piece's authored UVs ARE the seam its shipped maps land
        # on; smart-project would scramble every texel. Its procedural garnish
        # (brackets, chains, stock) keeps primitive UVs, which is all the flat
        # kit materials ever needed. And §7.12's check runs here because a
        # textured piece can still ship a dark-metal slab — the rule is about
        # what a flat face gives a torch back, not about how it was authored.
        for material, (area, span) in gen_props.largest_visible_panel(obj).items():
            spec = MATERIALS.get(material)
            if spec is None or spec.metallic <= 0.5:
                continue
            if gen_props.albedo_luminance(material) >= gen_props.DARK_METAL_LUMINANCE:
                continue
            if (area > gen_props.DARK_METAL_PANEL_AREA
                    and span > gen_props.DARK_METAL_PANEL_SPAN):
                blendkit.fail(
                    f"{piece.name}: a {area:.2f} m² face of '{material}', {span:.2f} m "
                    "across its narrow side — dark metal renders what it reflects, and "
                    "under a 12 m torch in a black corridor that is a hole (ART.md §7.12)")
    else:
        blendkit.uv_smart_project(obj)

    path = blendkit.out_path("Dressing", piece.name + ".fbx")
    blendkit.export_fbx(path, objects=[obj], with_animation=False)

    report = blendkit.describe(path)
    blendkit.assert_asset(report, min_vertices=8, max_triangles=piece.max_tris,
                          max_dimension=4.0)

    used = [m.name for m in obj.data.materials if m is not None]
    lost = gen_props.fbx_missing_materials(path, used)
    if lost:
        blendkit.fail(f"{piece.name}: materials missing from the exported FBX: {', '.join(lost)}")

    # Counted off the joined mesh's real polygons, per REFUSED_MATERIALS' docstring:
    # the manifest states the fact and the loader acts on it, so the fact has to be a
    # measurement. Read after the join and the triangulate, which is the geometry that
    # actually went into the FBX — a count taken before either would be counting a mesh
    # this file did not export.
    slots = [m.name if m is not None else "" for m in obj.data.materials]
    refused_faces = sum(1 for p in obj.data.polygons
                        if 0 <= p.material_index < len(slots)
                        and slots[p.material_index] in REFUSED_MATERIALS)
    if refused_faces:
        blendkit.fail(
            f"{piece.name}: {refused_faces} polygon(s) carry a deleted system's material "
            f"({', '.join(REFUSED_MATERIALS)}). The scatter tool's loader would refuse the "
            "whole piece, so shipping it is shipping a piece that never appears — fail here "
            "instead, where the builder that added it is one frame up the stack.")

    size = report.size
    for i, axis in enumerate("XYZ"):
        want = piece.expect[i]
        tol = max(0.008, want * 0.10)
        if abs(size[i] - want) > tol:
            msg = (f"{piece.name}: {axis} is {size[i]:.3f} m, intended {want:.3f} m "
                   f"(tolerance {tol:.3f}). 1 unit must be 1 metre.")
            if MEASURE_ONLY:
                print("SIZE_MISMATCH " + msg)
            else:
                blendkit.fail(msg)

    lo, hi = report.bounds_min, report.bounds_max
    if piece.mount == "WALL":
        if abs(hi[1]) > 0.004:
            blendkit.fail(f"{piece.name}: wall piece's mounting face is at y={hi[1]:.4f}, must be 0")
        if lo[2] < -0.004:
            blendkit.fail(f"{piece.name}: wall piece dips below the floor (min z={lo[2]:.4f})")
    elif piece.mount == "CEILING":
        if abs(hi[2]) > 0.004:
            blendkit.fail(f"{piece.name}: ceiling piece's mounting face is at z={hi[2]:.4f}, must be 0")
        drop = -lo[2]
        headroom = CEILING_CLEAR_ASSUMED - drop
        if headroom < EYE_HEIGHT_ASSUMED + HEAD_CLEARANCE_ASSUMED and "hangs_low" not in piece.checks:
            blendkit.fail(f"{piece.name}: hangs {drop:.2f} m, leaving {headroom:.2f} m under a "
                          f"{CEILING_CLEAR_ASSUMED} m ceiling — a "
                          f"{EYE_HEIGHT_ASSUMED} m player walks into it")
    elif piece.mount == "CORNER":
        for i, axis in enumerate("XYZ"):
            if abs(hi[i]) > 0.004:
                blendkit.fail(f"{piece.name}: corner piece's {axis} face is at {hi[i]:.4f}, must be 0")
    else:
        if abs(lo[2]) > 0.004:
            blendkit.fail(f"{piece.name}: floor piece's base is at z={lo[2]:.4f}, must be 0")

    ROWS.append({
        "name": piece.name, "group": piece.group, "mount": piece.mount, "size": size,
        "bounds_min": lo, "bounds_max": hi, "tris": report.triangles,
        "verts": report.vertices, "bytes": report.bytes, "path": path,
        "materials": used,
        "sharp": sharp, "bevel_skipped": bevel_skipped, "piece": piece,
        "emissive": gen_props.emissive_material_count(obj),
        "refused_faces": refused_faces,
    })

    blendkit.print_report(report)
    extra = [f"piece={piece.name}", f"group={piece.group}", f"mount={piece.mount}",
             f"size={size[0]:.3f}x{size[1]:.3f}x{size[2]:.3f}m",
             f"tris={report.triangles}", f"emissive={ROWS[-1]['emissive']}"]
    print("DRESS_DETAIL " + " ".join(extra))


# ── Manifest ────────────────────────────────────────────────────────────────


def write_manifest(rows: list[dict]) -> str:
    """Writes the contract the Unity scatter tool reads.

    Every number the scatterer needs about a piece is *measured* here and written
    out: footprint, height, how deep it sits when it is pushed against a wall,
    whether it breaks a sight line, whether it keeps a collider. Restating any of
    those in C# would let a re-export silently invalidate them — the piece would
    get 8 cm wider and the scatterer would keep believing the old figure and
    keep parking it 8 cm inside §08's carry channel.
    """
    pieces = []
    for r in rows:
        piece: Piece = r["piece"]
        sx, sy, sz = r["size"]
        pieces.append({
            "name": piece.name,
            "file": piece.name + ".fbx",
            "group": piece.group,
            "mount": piece.mount,
            "palettes": list(piece.palettes),
            "weight": piece.weight,
            "size_x": round(sx, 4),
            "size_y": round(sy, 4),
            "size_z": round(sz, 4),
            # How far the piece reaches out from the surface it is mounted on.
            # FLOOR pieces are authored back-to-+Y, so it is the +Y half-extent
            # doubled; WALL and CEILING pieces measure straight off the mount plane.
            "mount_depth": round(sy if piece.mount in ("FLOOR", "WALL") else sz, 4),
            "breaks_sightline": bool(sz >= EYE_HEIGHT_ASSUMED * 0.55
                                     and piece.mount == "FLOOR"),
            "solid": piece.solid,
            "hangs_low": "hangs_low" in piece.checks,
            "triangles": r["tris"],
            "emissive_materials": r["emissive"],
            "materials": r["materials"],
            # The guard half of `RefuseDeletedSystems`, restated per REFUSED_MATERIALS.
            # It sits beside `materials` because the loader's other half reads that list,
            # and the two are meant to be read together: a re-export can break either.
            "refused_faces": r["refused_faces"],
            "note": piece.note,
        })

    manifest = {
        "generated_by": "tools/blender/gen_dressing.py",
        "grid_metres": 2.5,
        "assumptions": {
            "eye_height_metres": EYE_HEIGHT_ASSUMED,
            "ceiling_clear_metres": CEILING_CLEAR_ASSUMED,
            "head_clearance_metres": HEAD_CLEARANCE_ASSUMED,
            "source": "NOT_IN_DESIGN_DOC",
        },
        "palettes": [{"name": k, "note": v} for k, v in sorted(PALETTES.items())],
        # A material row optionally carries `albedo_map` / `normal_map` /
        # `mask_map` — kit-root-relative paths written by export_real_textures.
        # Absent for every procedural material, so their rows (and Unity's
        # JsonUtility view of them) are unchanged: an absent field deserialises
        # to its default and the flat-value path keeps doing what it did.
        "materials": [
            {
                "name": spec.name,
                "r": round(spec.color[0], 4),
                "g": round(spec.color[1], 4),
                "b": round(spec.color[2], 4),
                "roughness": spec.roughness,
                "metallic": spec.metallic,
                "emission": spec.emission,
                **MATERIAL_MAPS.get(spec.name, {}),
            }
            for spec in sorted(MATERIALS.values(), key=lambda s: s.name)
        ],
        "pieces": pieces,
    }

    path = blendkit.out_path("Dressing", "Dressing.manifest.json")
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    return path


# ── Report ──────────────────────────────────────────────────────────────────


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    todo = [p for p in PIECES if not argv or any(a.lower() in p.name.lower() for a in argv)]
    if not todo:
        blendkit.fail(f"no dressing piece matches {argv}")

    print("DRESS_ASSUMPTIONS " + " ".join([
        f"eye_height={EYE_HEIGHT_ASSUMED}m",
        f"ceiling_clear={CEILING_CLEAR_ASSUMED}m",
        f"head_clearance={HEAD_CLEARANCE_ASSUMED}m",
        "source=NOT_IN_DESIGN_DOC",
    ]))

    for piece in todo:
        emit(piece)

    lines: list[str] = []

    # DELETED with §03 단서: the four 혼동쌍 assertions and the coverage check.
    # They proved the signage set could make a glyph *misread* — Dress_PipeLabel
    # half-turn symmetric to a millimetre so 6 is 9 from the far end,
    # Dress_HangingSign mirror symmetric so the back face reverses, every
    # Clue_Face flat and UV 0..1 so a stamped glyph would not distort. The
    # geometry those checks guarded is gone with the material, and there is
    # nothing to misread in a race that announces its finish at the start.

    # Nothing on the floor may be tall enough to trip a fleeing player (§05, §06).
    for r in ROWS:
        piece = r["piece"]
        if piece.group == "Debris" and r["size"][2] > 0.50:
            blendkit.fail(f"{piece.name} is {r['size'][2]:.2f} m tall — debris above knee "
                          "height is a §05/§06 death written by set dressing")
    lines.append("no Debris piece passes 0.50 m — §05's 65 % backward speed stays survivable  OK")

    # The kit has to contain surfaces bright enough to see. §03 lights this
    # building with a 12 m cone in near-black ambient.
    bright = [m for m in MATERIALS.values() if max(m.color) >= 0.50 and m.emission == 0.0]
    if len(bright) < 6:
        blendkit.fail(f"only {len(bright)} materials sit at albedo ≥ 0.50 — the kit would "
                      "disappear in a flashlight beam exactly like the empty corridors it "
                      "is replacing")
    lines.append(f"{len(bright)} of {len(MATERIALS)} materials at albedo ≥ 0.50 "
                 f"(brightest {max(max(m.color) for m in MATERIALS.values()):.2f}), "
                 f"{sum(1 for m in MATERIALS.values() if m.roughness <= 0.35)} at roughness "
                 "≤ 0.35 for specular response  OK")

    # Textures ship only on a full run, exactly like the manifest that names
    # them — a filtered iteration run regenerates neither, so it can never
    # leave the two disagreeing.
    manifest_path = None
    if len(todo) == len(PIECES):
        export_real_textures()
        manifest_path = write_manifest(ROWS)

    print()
    print("=" * 112)
    print("DRESSING KIT — measured, not intended.")
    print("=" * 112)
    print(f"{'piece':<28}{'group':<10}{'mount':<9}{'dimensions (m)':<24}{'tris':>6}"
          f"{'solid':>7}  palettes")
    print("-" * 112)
    for r in ROWS:
        piece = r["piece"]
        sx, sy, sz = r["size"]
        print(f"{piece.name:<28}{piece.group:<10}{piece.mount:<9}"
              f"{sx:.2f} x {sy:.2f} x {sz:.2f}      {r['tris']:>6}"
              f"{'yes' if piece.solid else 'no':>7}  {','.join(piece.palettes)}")
    print("-" * 112)
    print(f"{len(ROWS)} pieces   total tris {sum(r['tris'] for r in ROWS)}   "
          f"max {max(r['tris'] for r in ROWS)} "
          f"({max(ROWS, key=lambda r: r['tris'])['name']})   "
          f"total bytes {sum(r['bytes'] for r in ROWS)}")
    print()
    for line in lines:
        print("CHECK " + line)
    print()
    if manifest_path:
        print(f"FILE {manifest_path}")
    for r in ROWS:
        print(f"FILE {r['path']} ({r['bytes']} bytes)")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        blendkit.fail("gen_dressing.py raised:\n" + traceback.format_exc())
