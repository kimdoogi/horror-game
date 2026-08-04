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
"""

from __future__ import annotations

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

from blendkit import (  # noqa: E402
    MaterialSpec,
    add_box,
    add_cylinder,
    apply_transforms,
    assign_material,
    bevel,
    export_fbx,
    join,
    make_material,
    out_path,
    reset_scene,
    shade_smooth,
    triangulate,
    uv_smart_project,
)

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

MATERIALS = {
    "Gun_Steel": MaterialSpec("Gun_Steel", (0.055, 0.058, 0.062), roughness=0.34, metallic=1.0),
    "Gun_Grip": MaterialSpec("Gun_Grip", (0.075, 0.052, 0.038), roughness=0.72, metallic=0.0),
}


def build_revolver(name: str) -> bpy.types.Object:
    """One revolver, muzzle down −Y, grip down −Z, origin at the grip's web.

    The origin is where the hand closes rather than the centre of mass, because
    both consumers put it somewhere by that point: the held one parents to a hand
    bone, and the pickup one is dropped so the grip rests on the floor.
    """
    steel = make_material(MATERIALS["Gun_Steel"])
    grip_mat = make_material(MATERIALS["Gun_Grip"])

    parts: list[bpy.types.Object] = []

    # Barrel — a cylinder along −Y with a flat top strap over it, which is what
    # actually carries the silhouette. A bare tube reads as a pipe.
    barrel = add_cylinder(
        "Barrel",
        radius=BARREL_RADIUS,
        depth=BARREL_LENGTH,
        location=(0.0, -(CYLINDER_LENGTH * 0.5 + BARREL_LENGTH * 0.5), 0.0),
        rotation=(math.radians(90.0), 0.0, 0.0),
        vertices=12,
    )
    assign_material(barrel, steel)
    parts.append(barrel)

    strap = add_box(
        "TopStrap",
        size=(0.016, BARREL_LENGTH + CYLINDER_LENGTH, 0.010),
        location=(0.0, -(BARREL_LENGTH * 0.5), BARREL_RADIUS * 0.75),
    )
    assign_material(strap, steel)
    parts.append(strap)

    cylinder = add_cylinder(
        "Cylinder",
        radius=CYLINDER_RADIUS,
        depth=CYLINDER_LENGTH,
        location=(0.0, 0.0, 0.0),
        rotation=(math.radians(90.0), 0.0, 0.0),
        vertices=12,
    )
    assign_material(cylinder, steel)
    parts.append(cylinder)

    frame = add_box(
        "Frame",
        size=(0.018, 0.062, FRAME_HEIGHT),
        location=(0.0, CYLINDER_LENGTH * 0.5 + 0.024, -FRAME_HEIGHT * 0.25),
    )
    assign_material(frame, steel)
    parts.append(frame)

    guard = add_box(
        "TriggerGuard",
        size=(0.014, 0.034, 0.008),
        location=(0.0, CYLINDER_LENGTH * 0.5 + 0.014, -FRAME_HEIGHT * 0.62),
    )
    assign_material(guard, steel)
    parts.append(guard)

    # Grip — raked back so a hand closing on it points the barrel forward. A
    # vertical grip makes the held pose look like somebody carrying a drill.
    grip = add_box(
        "Grip",
        size=(GRIP_THICKNESS, 0.036, GRIP_LENGTH),
        location=(0.0, CYLINDER_LENGTH * 0.5 + 0.040, -GRIP_LENGTH * 0.52),
        rotation=(math.radians(-GRIP_RAKE_DEGREES), 0.0, 0.0),
    )
    assign_material(grip, grip_mat)
    parts.append(grip)

    hammer = add_box(
        "Hammer",
        size=(0.010, HAMMER_DEPTH, 0.016),
        location=(0.0, CYLINDER_LENGTH * 0.5 + HAMMER_SETBACK, FRAME_HEIGHT * 0.30),
    )
    assign_material(hammer, steel)
    parts.append(hammer)

    gun = join(parts, name)
    bevel(gun, width=0.0018, segments=1)
    shade_smooth(gun, angle_degrees=32.0)
    uv_smart_project(gun)
    triangulate(gun)
    apply_transforms(gun, location=False, rotation=True, scale=True)
    return gun


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
    pickup = build_revolver("Gun_Pickup")

    # The pickup lies on its side with the barrel across the cell, which is the
    # reading angle: somebody walking past a 막힌 길 sees the long axis, not the
    # muzzle. Rotated in the asset rather than in the scene so the generator that
    # places it has nothing to remember.
    pickup.rotation_euler = (math.radians(90.0), 0.0, math.radians(20.0))
    apply_transforms(pickup, location=False, rotation=True, scale=True)

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
    # because a deforming surface with a gap in it tears. A hard-surface prop is seven
    # overlapping boxes in ONE object, which is what a revolver is, and welding them
    # would cost triangles to fix a problem that does not exist.
    #
    # What actually protects the thing the old message was worried about is that it is
    # one object: one transform, so no part can be left behind by a move. That is what
    # is asserted, and the island count is printed as information.
    if len([o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Gun_Held")]) != 1:
        raise SystemExit(
            "Gun_Held is not a single object. Seven boxes under seven transforms is "
            "seven things that can be moved apart, and a runner would end up holding a "
            "barrel with the grip left behind on the floor."
        )


if __name__ == "__main__":
    main()
