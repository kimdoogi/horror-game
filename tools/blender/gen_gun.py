"""§02's gun — the one-shot revolver a runner finds on the floor.

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
      --python tools/blender/gen_gun.py

Two objects, because the game needs the same object in two situations and they are
not the same mesh:

  Gun_Pickup   lying on the floor of a 막힌 길, seen from above by somebody deciding
               whether the detour is worth it. It has to read as a gun from three
               metres away in a dark corridor, so it lies flat with its silhouette
               across the cell.
  Gun_Held     in a runner's fist, seen by OTHER runners at up to
               Gunplay.RangeMetres. What matters here is only the silhouette: an arm
               with a straight line off the end of it is the whole message, and at
               twelve metres under a torch nobody reads a cylinder flute.

Both are the same 0.26 m revolver. The pickup is not a smaller model or a sprite —
a player who picks up one shape and finds themselves holding another has been lied
to, and this project has paid for that kind of thing before.

Scale is the argument this file has to win. §12's corridor is 2.20 m of clear width
and a runner is 1.75 m tall; a gun authored at "looks right in the viewport" comes
out at 0.4 m and reads as a rifle in the hand. Everything below is measured off the
runner: barrel length is a fraction of forearm, grip is a fraction of hand.

WHAT THE PRODUCTION PASS ADDED, AND WHY EACH PIECE
==================================================

The first version was seven boxes and it photographed as one: a pipe with a lumber
offcut on the end. Every addition below was made against a render of the shipped
FBX, not against taste:

* **The pickup now actually lies down.** The old pose was ``(90° X, 20° Z)``,
  which pitches the muzzle INTO the floor — rotate (0,-1,0) 90° about X and you
  get (0,0,-1), straight down. ``AlignFloorBottom`` then stood every alcove gun
  vertically on its muzzle like a tombstone, and at 3 m under a beam it rendered
  as an unreadable 30-pixel sliver. The pose is now built as roll-then-pitch-then-
  yaw about the gun's own axes (composed as matrices, because Euler XYZ applies X
  first and a roll after a pitch eats the pitch): on its side, muzzle 8° proud,
  20° across the cell — the long axis is what a passing torch finds.
* **A rag under the pickup.** The floor is albedo ~0.16 and worn bluing is 0.07;
  dark-on-dark is the §7.12 hole. A pale dropped cloth (luminance ~0.26) under
  the revolver is the cheapest legal way to buy figure-ground contrast, it reads
  as story (somebody wrapped this), and it enlarges the pickup's bounds a little,
  which only makes ``MapSceneBuilder``'s crosshair box easier to hold.
* **Fluted cylinder + forcing cone + recoil shield.** The three shapes that say
  "revolver" instead of "pistol-shaped object" at arm's length.
* **A trigger-guard LOOP.** The old guard was a solid box, so the one negative
  space every human uses to recognise a handgun was missing from the silhouette.
* **Wrapped grip.** Three leather straps over the wood — checkering at 0.1 mm
  pitch is invisible at this game's distances, a 10 mm wrap is not.
* **Worn-blued steel with edge wear.** Every hard steel edge is chamfered (1–2
  segments) and the chamfer faces are assigned a brighter ``Gun_SteelWorn`` by a
  deterministic hash biased toward upward/outward faces. Under a coaxial beam the
  flats stay correctly dark and the worn chamfers return the highlight — that
  broken roughness IS the read. FBX carries no PBR, so the wear lives in
  geometry + material slots, the same seam every prop in this project binds
  through (``Props.manifest.json`` → ``PropMaterials``).
* **A brand-free stamp band** on the frame flat, 0.25 mm proud, in the worn
  steel — under a raking beam it reads as a stamped line without spelling
  anything.

The gun's materials live in ``gen_props.MATERIALS`` so they travel in
``Props.manifest.json`` — before this pass Gun_Steel/Gun_Grip were in NO manifest,
``PropMaterials.Bind`` logged them as UNBOUND, and the gun shipped on the
importer's guess (metallic 0, no emission keyword, so no crosshair highlight).
"""

from __future__ import annotations

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from blendkit import (  # noqa: E402
    add_box,
    add_cylinder,
    apply_transforms,
    assign_material,
    export_fbx,
    join,
    make_material,
    out_path,
    reset_scene,
    shade_smooth,
    triangulate,
    uv_smart_project,
)

# One source of truth for the gun's materials: gen_props owns the manifest that
# PropMaterials.cs rebuilds URP materials from, so the specs are registered there
# and only *used* here. Defining them in this file too would be the two-numbers-
# one-length drift the barrel comment below refuses.
from gen_props import MATERIALS as PROP_MATERIALS  # noqa: E402

# ── The measurements, and where each comes from ─────────────────────────────
#
# The runner is 1.75 m (gen_runner.py asserts it to the millimetre) with a hand
# whose grip is about 0.11 m across. A service revolver is a hair over one hand
# length in the grip and roughly two in overall length, which is where these come
# from — not from a reference photo scaled by eye.
OVERALL_LENGTH = 0.260          # m, muzzle to the back of the hammer
BARREL_RADIUS = 0.011           # m, a .38 bore plus wall
CYLINDER_LENGTH = 0.042         # m
CYLINDER_RADIUS = 0.021         # m
HAMMER_SETBACK = 0.062          # m, back of the frame behind the cylinder face
HAMMER_DEPTH = 0.020            # m

# The barrel is what is LEFT, not a number of its own. Authored separately, the
# parts summed to 0.2298 m against a declared 0.260 and the shape gate caught it —
# two numbers describing one length will always drift, and the one nobody measures
# is the one that lies. Everything behind the cylinder face is fixed by the frame,
# so the barrel takes the remainder and OVERALL_LENGTH becomes the single truth.
BARREL_LENGTH = OVERALL_LENGTH - (CYLINDER_LENGTH * 0.5 + HAMMER_SETBACK + HAMMER_DEPTH * 0.5) \
    - CYLINDER_LENGTH * 0.5
FRAME_HEIGHT = 0.048            # m, top strap down to the trigger guard
GRIP_LENGTH = 0.105             # m, matched to the runner's hand
GRIP_THICKNESS = 0.030          # m
GRIP_RAKE_DEGREES = 18.0        # backward lean, so it sits in a fist rather than a pipe grip

# The silhouette is the whole point at range, so it is measured and asserted.
MIN_SILHOUETTE_LENGTH = 0.24    # m — under this it reads as a tool, not a gun
MAX_SILHOUETTE_LENGTH = 0.30    # m — over this it reads as a rifle in one hand

# Budgets from the production brief. Asserted, not hoped.
MAX_TRIS_HELD = 1500
MAX_TRIS_PICKUP = 1200

# Chamfers. 1.5 mm on steel is a real armourer's break; the grip is wood and
# rounder. Anything whose smallest dimension is under 4× the width is left sharp
# rather than folded inside out (the same guard gen_props.emit uses).
STEEL_BEVEL = 0.0015
GRIP_BEVEL = 0.0040

MATERIALS = {name: PROP_MATERIALS[name] for name in (
    "Gun_Steel", "Gun_SteelWorn", "Gun_Bore", "Gun_Grip", "Gun_GripWrap", "Gun_Cloth",
)}


# ── Worn-edge machinery ─────────────────────────────────────────────────────


def _bevel_with_wear(obj: bpy.types.Object, width: float, segments: int,
                     worn_slot: int) -> None:
    """Chamfers every edge and assigns a deterministic subset of the new chamfer
    faces to the worn-steel slot.

    The rule is hash-plus-orientation: upward and outward faces wear first,
    because that is where a holster, a floor and a hand actually polish bluing
    off. Deterministic (a sine hash of the face centre), so a rebuild is a
    byte-for-byte rebuild — the same reason gen_props' debris refuses RNG.
    """
    dims = list(obj.dimensions)
    if min(dims) < width * 4.0:
        return
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    result = bmesh.ops.bevel(
        bm,
        geom=list(bm.edges) + list(bm.verts),
        offset=width,
        segments=segments,
        profile=0.7,
        affect="EDGES",
    )
    for face in result["faces"]:
        c = face.calc_center_median()
        h = math.sin(c.x * 913.7 + c.y * 471.3 + c.z * 733.1) * 0.5 + 0.5
        up = max(face.normal.z, 0.0)
        out = abs(face.normal.x)
        if h < 0.25 + 0.35 * up + 0.20 * out:
            face.material_index = worn_slot
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def _steel(obj: bpy.types.Object, steel, worn, bevel: float = STEEL_BEVEL,
           segments: int = 1) -> bpy.types.Object:
    """Assigns worn-blued steel and chamfers with edge wear."""
    assign_material(obj, steel)
    obj.data.materials.append(worn)
    if bevel > 0.0:
        _bevel_with_wear(obj, bevel, segments, worn_slot=1)
    return obj


def _add_fluted_cylinder(name: str, radius: float, depth: float, flutes: int = 6,
                         flute_depth: float = 0.0035, flute_width: float = 0.52,
                         seg_per_flute: int = 7,
                         location=(0.0, 0.0, 0.0), rotation=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    """A revolver cylinder: a cylinder whose wall carries `flutes` cosine scallops.

    Built as a profile rather than booleans — a boolean on a 12-gon is the kind of
    operation that quietly leaves non-manifold slivers, and this mesh has to
    survive a bevel, a triangulation and an FBX round trip.
    """
    n = flutes * seg_per_flute
    lo_s = (1.0 - flute_width) / 2.0
    hi_s = 1.0 - lo_s
    ring: list[tuple[float, float]] = []
    for i in range(n):
        theta = (i / n) * 2.0 * math.pi
        s = (i % seg_per_flute) / seg_per_flute
        d = 0.0
        if lo_s < s < hi_s:
            u = (s - lo_s) / (hi_s - lo_s)
            d = flute_depth * math.sin(u * math.pi)
        r = radius - d
        ring.append((r * math.cos(theta), r * math.sin(theta)))

    mesh = bpy.data.meshes.new(name)
    verts = [(x, y, -depth / 2.0) for (x, y) in ring] + \
            [(x, y, +depth / 2.0) for (x, y) in ring]
    faces = []
    for i in range(n):
        j = (i + 1) % n
        faces.append((i, j, n + j, n + i))
    faces.append(tuple(range(n - 1, -1, -1)))          # bottom cap
    faces.append(tuple(range(n, 2 * n)))               # top cap
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.location = Vector(location)
    obj.rotation_euler = rotation
    bpy.context.view_layer.objects.active = obj
    return obj


def _add_torus(name: str, major: float, minor: float, location, rotation,
               mseg: int = 14, nseg: int = 6) -> bpy.types.Object:
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor,
                                     major_segments=mseg, minor_segments=nseg,
                                     location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = rotation
    return obj


def _add_cone(name: str, r1: float, r2: float, depth: float, location, rotation,
              vertices: int = 12) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cone_add(radius1=r1, radius2=r2, depth=depth,
                                    location=location, vertices=vertices)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = rotation
    return obj


# ── The revolver ────────────────────────────────────────────────────────────


def build_revolver(name: str, held: bool = True) -> bpy.types.Object:
    """One revolver, muzzle down −Y, grip down −Z, origin at the grip's web.

    The origin is where the hand closes rather than the centre of mass, because
    both consumers put it somewhere by that point: the held one parents to a hand
    bone (RunnerGun.MountOffset — do NOT move this origin or the forward axis),
    and the pickup one is dropped so bounds alignment rests it on the floor.

    ``held=False`` drops only interior details smaller than 5 mm — chamber
    mouths, sight nubs, the stamp band, the trigger inside the guard — and takes
    one chamfer segment on the frame instead of two. At the pickup's 3 m reading
    distance a 5 mm feature subtends about 2 pixels of a 1920-wide frame, so the
    two meshes are indistinguishable there BY ARITHMETIC, not by hope: same
    parts, same dimensions, same silhouette. This is not the smaller-model lie
    the header refuses — it is the same revolver with the sub-pixel machining
    left off, spent instead on the rag it lies on.
    """
    steel = make_material(MATERIALS["Gun_Steel"])
    worn = make_material(MATERIALS["Gun_SteelWorn"])
    bore = make_material(MATERIALS["Gun_Bore"])
    grip_mat = make_material(MATERIALS["Gun_Grip"])
    wrap_mat = make_material(MATERIALS["Gun_GripWrap"])

    parts: list[bpy.types.Object] = []
    rx90 = (math.radians(90.0), 0.0, 0.0)

    # ── Barrel group ──
    barrel_mid_y = -(CYLINDER_LENGTH * 0.5 + BARREL_LENGTH * 0.5)
    muzzle_y = -(CYLINDER_LENGTH * 0.5 + BARREL_LENGTH)

    barrel = add_cylinder("Barrel", radius=BARREL_RADIUS, depth=BARREL_LENGTH,
                          location=(0.0, barrel_mid_y, 0.0), rotation=rx90, vertices=14)
    assign_material(barrel, steel)   # round already; ends get the band
    barrel.data.materials.append(worn)
    for poly in barrel.data.polygons:
        # Holster-drag facets down the barrel: a deterministic fifth of the side
        # faces go worn, which is what breaks the tube's monotone under a beam.
        if abs(poly.normal.z) < 0.7 and math.sin(poly.index * 12.9898) * 0.5 + 0.5 < 0.20:
            poly.material_index = 1
    parts.append(barrel)

    # Muzzle band — the reinforced ring a service revolver carries at the crown.
    # Worn steel: the crown is the edge a holster polishes first.
    band = add_cylinder("MuzzleBand", radius=BARREL_RADIUS + 0.0016, depth=0.009,
                        location=(0.0, muzzle_y + 0.0045, 0.0), rotation=rx90, vertices=14)
    parts.append(_steel(band, worn, worn, bevel=0.0))

    # The bore: a near-black disc a hair proud of the crown. From front-on the
    # muzzle must read as a hole, not a flat cap.
    bore_disc = add_cylinder("Bore", radius=BARREL_RADIUS * 0.55, depth=0.0016,
                             location=(0.0, muzzle_y - 0.0004, 0.0), rotation=rx90, vertices=10)
    assign_material(bore_disc, bore)
    parts.append(bore_disc)

    # Ejector-rod housing under the barrel — the second horizontal the profile needs.
    housing = add_cylinder("EjectorHousing", radius=0.0058, depth=BARREL_LENGTH * 0.62,
                           location=(0.0, barrel_mid_y + BARREL_LENGTH * 0.10, -0.0138),
                           rotation=rx90, vertices=10)
    parts.append(_steel(housing, steel, worn, bevel=0.0))
    if held:
        rod = add_cylinder("EjectorRod", radius=0.0028, depth=0.014,
                           location=(0.0, barrel_mid_y - BARREL_LENGTH * 0.24, -0.0138),
                           rotation=rx90, vertices=8)
        parts.append(_steel(rod, worn, worn, bevel=0.0))  # the rod is polished by use

    # Top strap with a sight groove: strap, then a front blade and two rear nubs.
    strap = add_box("TopStrap", size=(0.013, BARREL_LENGTH + CYLINDER_LENGTH + 0.010, 0.0085),
                    location=(0.0, -(BARREL_LENGTH * 0.5) + 0.002, BARREL_RADIUS * 0.82))
    parts.append(_steel(strap, steel, worn))
    blade = add_box("FrontSight", size=(0.0032, 0.012, 0.0095),
                    location=(0.0, muzzle_y + 0.008, BARREL_RADIUS + 0.0068))
    parts.append(_steel(blade, steel, worn, bevel=0.0))
    if held:
        for sx in (-1.0, 1.0):
            nub = add_box("RearSight", size=(0.0035, 0.009, 0.0045),
                          location=(sx * 0.00425, CYLINDER_LENGTH * 0.5 + 0.020,
                                    BARREL_RADIUS * 0.82 + 0.0060))
            parts.append(_steel(nub, steel, worn, bevel=0.0))

    # ── Cylinder group ──
    cylinder = _add_fluted_cylinder("Cylinder", radius=CYLINDER_RADIUS,
                                    depth=CYLINDER_LENGTH, location=(0.0, 0.0, 0.0),
                                    rotation=rx90)
    # No chamfer on a 42-gon — the flutes already break the light. Wear goes on
    # the flute LANDS instead: a deterministic scatter of the raised strips
    # between scallops, which is exactly where a cylinder drags in a holster.
    assign_material(cylinder, steel)
    cylinder.data.materials.append(worn)
    for poly in cylinder.data.polygons:
        if abs(poly.normal.z) > 0.6:      # caps (mesh-local: axis is Z pre-rotation)
            continue
        h = math.sin(poly.index * 12.9898) * 0.5 + 0.5
        if h < 0.22:
            poly.material_index = 1
    parts.append(cylinder)

    if held:
        # Six chamber mouths on the cylinder face. Bore-dark, slightly proud,
        # offset half a sector so they sit between the flutes like real chambers.
        for k in range(6):
            a = (k + 0.5) / 6.0 * 2.0 * math.pi
            cx = 0.0128 * math.cos(a)
            cz = 0.0128 * math.sin(a)
            mouth = add_cylinder(f"Chamber{k}", radius=0.0046, depth=0.0018,
                                 location=(cx, -CYLINDER_LENGTH * 0.5 - 0.0004, cz),
                                 rotation=rx90, vertices=8)
            assign_material(mouth, bore)
            parts.append(mouth)

    # Forcing cone — the flared throat where barrel meets cylinder. Small, and it
    # is exactly the join whose absence makes barrel+cylinder read as two parts.
    cone = _add_cone("ForcingCone", r1=0.0132, r2=BARREL_RADIUS, depth=0.011,
                     location=(0.0, -(CYLINDER_LENGTH * 0.5 + 0.0055), 0.0),
                     rotation=(math.radians(-90.0), 0.0, 0.0), vertices=12)
    parts.append(_steel(cone, steel, worn, bevel=0.0))

    # Recoil shield — the disc behind the cylinder. Seen side-on it closes the
    # frame the way a revolver's standing breech does.
    shield = add_cylinder("RecoilShield", radius=CYLINDER_RADIUS * 0.98, depth=0.0075,
                          location=(0.0, CYLINDER_LENGTH * 0.5 + 0.0038, 0.0),
                          rotation=rx90, vertices=18)
    parts.append(_steel(shield, steel, worn, bevel=0.0))

    # ── Frame group ──
    frame = add_box("Frame", size=(0.017, 0.058, FRAME_HEIGHT),
                    location=(0.0, CYLINDER_LENGTH * 0.5 + 0.028, -FRAME_HEIGHT * 0.25))
    parts.append(_steel(frame, steel, worn, segments=2 if held else 1))

    if held:
        # The stamp band: 0.25 mm proud of both frame flats, worn steel, no glyphs.
        stamp = add_box("StampBand", size=(0.0175, 0.026, 0.0032),
                        location=(0.0, CYLINDER_LENGTH * 0.5 + 0.030, -0.0035))
        parts.append(_steel(stamp, worn, worn, bevel=0.0))

    # Trigger guard — a LOOP. The old solid box deleted the one negative space a
    # silhouette is recognised by.
    guard = _add_torus("TriggerGuard", major=0.0165, minor=0.0034,
                       location=(0.0, CYLINDER_LENGTH * 0.5 + 0.0145, -FRAME_HEIGHT * 0.560),
                       rotation=(0.0, math.radians(90.0), 0.0),
                       mseg=12 if held else 10, nseg=5)
    guard.scale = Vector((1.0, 1.30, 1.0))   # oval, longer fore-aft
    apply_transforms(guard, scale=True)
    parts.append(_steel(guard, steel, worn, bevel=0.0))
    if held:
        trigger = add_box("Trigger", size=(0.0042, 0.0050, 0.0130),
                          location=(0.0, CYLINDER_LENGTH * 0.5 + 0.0155, -FRAME_HEIGHT * 0.505),
                          rotation=(math.radians(-14.0), 0.0, 0.0))
        parts.append(_steel(trigger, worn, worn, bevel=0.0))

    # Hammer: a body rising OUT of the frame (the old one floated 4 mm clear and
    # the silhouette showed the gap) and a spur raked back 38°.
    hammer = add_box("Hammer", size=(0.0085, 0.0125, 0.0210),
                     location=(0.0, CYLINDER_LENGTH * 0.5 + HAMMER_SETBACK - 0.004,
                               FRAME_HEIGHT * 0.22))
    parts.append(_steel(hammer, steel, worn))
    spur = add_box("HammerSpur", size=(0.0085, 0.0170, 0.0050),
                   location=(0.0, CYLINDER_LENGTH * 0.5 + HAMMER_SETBACK + 0.0035,
                             FRAME_HEIGHT * 0.30),
                   rotation=(math.radians(38.0), 0.0, 0.0))
    parts.append(_steel(spur, steel, worn, bevel=0.0))

    # ── Grip group ──
    rake = math.radians(-GRIP_RAKE_DEGREES)
    grip_centre = Vector((0.0, CYLINDER_LENGTH * 0.5 + 0.040, -GRIP_LENGTH * 0.52))
    grip = add_box("Grip", size=(GRIP_THICKNESS, 0.036, GRIP_LENGTH),
                   location=tuple(grip_centre), rotation=(rake, 0.0, 0.0))
    assign_material(grip, grip_mat)
    _bevel_with_wear(grip, GRIP_BEVEL, segments=2, worn_slot=0)  # slot 0: just rounds it
    parts.append(grip)

    # The wrap: three straps following the rake. Geometry, because a 10 mm strap
    # survives this game's viewing distances and a normal-mapped checker does not.
    down = Vector((0.0, math.sin(rake), -math.cos(rake)))
    for i, t in enumerate((-0.028, 0.002, 0.032)):
        loc = grip_centre + down * t
        strapg = add_box(f"GripWrap{i}", size=(GRIP_THICKNESS + 0.0022, 0.0382, 0.0075),
                         location=tuple(loc), rotation=(rake, 0.0, 0.0))
        assign_material(strapg, wrap_mat)
        _bevel_with_wear(strapg, 0.0012, segments=1, worn_slot=0)
        parts.append(strapg)

    # Butt cap: blued steel whose chamfer wears — the part a floor actually
    # touches. All-worn read as a pale plate in round 2 and topped the grip with
    # the brightest thing on the gun, exactly upside down.
    butt = grip_centre + down * (GRIP_LENGTH * 0.5)
    cap = add_box("ButtCap", size=(GRIP_THICKNESS + 0.001, 0.037, 0.0045),
                  location=tuple(butt), rotation=(rake, 0.0, 0.0))
    parts.append(_steel(cap, steel, worn, bevel=0.0012))

    gun = join(parts, name)
    shade_smooth(gun, angle_degrees=40.0)
    uv_smart_project(gun)
    triangulate(gun)
    apply_transforms(gun, location=False, rotation=True, scale=True)
    return gun


def build_cloth(name: str) -> bpy.types.Object:
    """The rag the pickup lies on. A crumpled quad grid, deterministic (no RNG).

    Its job is figure-ground: worn bluing at luminance 0.07 on a 0.16 floor is a
    hole; on a 0.26 cloth it is a gun. It also softens AlignFloorBottom — the
    lowest vertex of the asset is now fabric, so the revolver never clips the slab.
    """
    cloth = make_material(MATERIALS["Gun_Cloth"])
    nx, ny = 9, 7
    w, d = 0.36, 0.27
    mesh = bpy.data.meshes.new(name)
    verts = []
    for j in range(ny + 1):
        for i in range(nx + 1):
            x = (i / nx - 0.5) * w
            y = (j / ny - 0.5) * d
            # Crumple: two crossing sine folds plus a radial settle toward the
            # edges, all deterministic.
            z = (0.0035 * abs(math.sin(x * 21.0 + 1.3) * math.sin(y * 26.0 - 0.7))
                 + 0.0022 * math.sin(x * 9.0 - y * 12.0))
            edge = min(i, nx - i, j, ny - j)
            if edge == 0:
                z *= 0.25
            verts.append((x, y, z + 0.0015))
    faces = []
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1))
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    assign_material(obj, cloth)
    bpy.context.view_layer.objects.active = obj
    shade_smooth(obj, angle_degrees=60.0)
    return obj


def measure(obj: bpy.types.Object) -> dict[str, float]:
    """World-space extents and triangle count, off the object rather than the plan."""
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    xs = [c.x for c in corners]
    ys = [c.y for c in corners]
    zs = [c.z for c in corners]
    return {
        "length": max(ys) - min(ys),
        "width": max(xs) - min(xs),
        "height": max(zs) - min(zs),
        "tris": len(obj.data.polygons),
        "verts": len(obj.data.vertices),
    }


def main() -> None:
    reset_scene()

    held = build_revolver("Gun_Held")
    m = measure(held)

    print(
        "GUN_SHAPE length={length:.4f}m width={width:.4f}m height={height:.4f}m "
        "tris={tris} verts={verts}".format(**m)
    )

    # The silhouette is what another runner sees at twelve metres, so it is a gate
    # and not a note. Both bounds are stated in the header with their reasoning.
    if not (MIN_SILHOUETTE_LENGTH <= m["length"] <= MAX_SILHOUETTE_LENGTH):
        raise SystemExit(
            "GUN_SHAPE length {:.4f} m is outside {:.2f}~{:.2f} — under the floor it reads "
            "as a tool in the hand, over the ceiling as a rifle. Neither is a revolver "
            "somebody found on a basement floor.".format(
                m["length"], MIN_SILHOUETTE_LENGTH, MAX_SILHOUETTE_LENGTH
            )
        )

    # One shell: a gun that exports as seven loose boxes lights seven draw calls and,
    # worse, can lose a part to a stray transform without anything noticing.
    bpy.ops.object.select_all(action="DESELECT")
    held.select_set(True)
    bpy.context.view_layer.objects.active = held
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    shells = len([o for o in bpy.context.selected_objects if o.type == "MESH"])
    print("GUN_ISLANDS islands={} (overlapping boxes in one object — a prop, not a skin)".format(shells))

    # Rebuild after the destructive shell probe.
    reset_scene()
    held = build_revolver("Gun_Held")
    pickup = build_revolver("Gun_Pickup", held=False)

    # The pickup lies on its LEFT SIDE with the barrel across the cell, muzzle 8°
    # proud, yawed 20° — the reading pose for somebody walking past a 막힌 길.
    # Composed as roll-then-pitch-then-yaw MATRICES, not an Euler triple: Blender's
    # XYZ Euler applies X first, so writing (pitch, roll, yaw) as a tuple rotates
    # the pitch into the roll and the previous version of this file shipped a gun
    # standing vertically on its muzzle because of exactly that.
    pose = (Matrix.Rotation(math.radians(20.0), 4, "Z")
            @ Matrix.Rotation(math.radians(-8.0), 4, "X")
            @ Matrix.Rotation(math.radians(90.0), 4, "Y"))
    pickup.matrix_world = pose @ pickup.matrix_world
    apply_transforms(pickup, location=False, rotation=True, scale=True)

    # Presentation pass for the 3 m read, applied AFTER the pose so "up" means
    # up: the torch is coaxial with the eye, so a lying gun's beam-facing faces
    # are the only ones that can answer it, and in blued steel they answered
    # with nothing — the round-2 render shows a gun-shaped hole on a legible
    # rag. Half of the upward steel faces go to the same Gun_SteelWorn the held
    # gun already wears. It is the identical material set, scattered where this
    # variant's one job — being findable on a floor — needs it.
    slot_names = [m.name if m is not None else "" for m in pickup.data.materials]
    if "Gun_Steel" in slot_names and "Gun_SteelWorn" in slot_names:
        steel_i = slot_names.index("Gun_Steel")
        worn_i = slot_names.index("Gun_SteelWorn")
        for poly in pickup.data.polygons:
            if poly.material_index != steel_i or poly.normal.z < 0.45:
                continue
            if math.sin(poly.index * 7.1319) * 0.5 + 0.5 < 0.55:
                poly.material_index = worn_i

    # Rest it on the rag: cloth top crumple is ~5 mm, so the gun settles to 3 mm
    # above the floor plane — pressing into the fabric, not floating on it.
    lo_z = min((pickup.matrix_world @ Vector(c)).z for c in pickup.bound_box)
    pickup.location.z -= lo_z - 0.003
    cloth = build_cloth("PickupCloth")
    apply_transforms(pickup, location=True, rotation=True, scale=True)
    pickup = join([pickup, cloth], "Gun_Pickup")
    triangulate(pickup)

    pm = measure(pickup)
    if pm["height"] > 0.10:
        raise SystemExit(
            "Gun_Pickup stands {:.3f} m tall — a lying revolver is under 0.08 m. The "
            "pose matrix regressed and the gun is on its muzzle again.".format(pm["height"])
        )
    if m["tris"] > MAX_TRIS_HELD or pm["tris"] > MAX_TRIS_PICKUP:
        raise SystemExit(
            "Over budget: held {} tris (max {}), pickup {} tris (max {}).".format(
                m["tris"], MAX_TRIS_HELD, pm["tris"], MAX_TRIS_PICKUP))

    held_path = export_fbx(out_path("Props", "Gun_Held.fbx"), [held])
    pickup_path = export_fbx(out_path("Props", "Gun_Pickup.fbx"), [pickup])

    for path, obj in ((held_path, held), (pickup_path, pickup)):
        stat = measure(obj)
        print(
            "ASSET_REPORT path={} bytes={} tris={} verts={} materials={} "
            "size={:.3f}x{:.3f}x{:.3f}m".format(
                os.path.relpath(path, os.path.join(os.path.dirname(__file__), "..", "..")),
                os.path.getsize(path),
                stat["tris"],
                stat["verts"],
                len(obj.data.materials),
                stat["width"],
                stat["length"],
                stat["height"],
            )
        )

    # Loose islands are NOT a defect here and the first version of this gate said they
    # were. A skinned organic mesh has to be one shell — gen_runner.py asserts that,
    # because a deforming surface with a gap in it tears. A hard-surface prop is many
    # overlapping solids in ONE object, which is what a revolver is, and welding them
    # would cost triangles to fix a problem that does not exist.
    #
    # What actually protects the thing the old message was worried about is that it is
    # one object: one transform, so no part can be left behind by a move. That is what
    # is asserted, and the island count is printed as information.
    if len([o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Gun_Held")]) != 1:
        raise SystemExit(
            "Gun_Held is not a single object. Many parts under many transforms is "
            "many things that can be moved apart, and a runner would end up holding a "
            "barrel with the grip left behind on the floor."
        )


if __name__ == "__main__":
    main()
