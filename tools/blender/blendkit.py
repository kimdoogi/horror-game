"""Blender helpers for generating the game's 3D assets headlessly.

§16-1 names the character assets "프로젝트의 숨은 병목" and says the monster and
player models cannot be worked around. This directory answers that: every model,
rig, animation and map piece is produced by a script, run through Blender's
`--background` CLI, and exported straight into the Unity project.

Why scripts rather than driving Blender interactively:

* **Reproducible.** A rebuild produces the same asset. Hand-modelling produces a
  .blend nobody can regenerate or review as a diff.
* **Parallel-safe.** Each `--background` run is its own process with its own
  scene. Several agents editing one live Blender session would overwrite each
  other's work.
* **CI-able.** The same command runs on a build machine.
* **Cheap to retune.** §12 fixes corridor lengths and zone diagonals by rule, and
  those rules will move during balancing. Regenerating the kit from changed
  numbers takes seconds.

The models are deliberately low-poly and stylised. §05 notes the game is dark and
first-person, so detail requirements are low — and low-poly geometry generated
from primitives is exactly what a script does well.

Run a generator like this::

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \\
        --python tools/blender/gen_monster.py

`--factory-startup` matters: it ignores user preferences and add-on state so the
result does not depend on whose machine ran it.
"""

from __future__ import annotations

import math
import os
import sys
from dataclasses import dataclass, field
from typing import Iterable, Sequence

import bpy
import bmesh
from mathutils import Euler, Matrix, Vector

# ── Paths ───────────────────────────────────────────────────────────────────

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
"""Repository root, resolved from this file's location."""

UNITY_ASSETS = os.path.join(REPO_ROOT, "unity", "HorrorGame", "Assets")
"""The Unity Assets folder. Exports land in subfolders of this."""

MODELS_DIR = os.path.join(UNITY_ASSETS, "Models")
"""Where exported FBX files go."""


def out_path(*parts: str) -> str:
    """Builds a path under Assets/Models, creating directories as needed."""
    p = os.path.join(MODELS_DIR, *parts)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    return p


# ── Scene setup ─────────────────────────────────────────────────────────────


def reset_scene() -> None:
    """Empties the scene and purges orphaned data.

    `--factory-startup` still opens with the default cube, camera and light. A
    generator that forgets to clear them exports them, which is how a stray
    "Cube" ends up in the Unity project.
    """
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False, confirm=False)

    for collection in (
        bpy.data.meshes, bpy.data.materials, bpy.data.armatures,
        bpy.data.actions, bpy.data.objects, bpy.data.images,
    ):
        for item in list(collection):
            if item.users == 0:
                collection.remove(item)

    # 1 Blender unit = 1 metre = 1 Unity unit. §12's distances are in metres, so
    # anything else silently breaks the map rules.
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.scene.render.fps = 30


def set_frame_range(start: int, end: int) -> None:
    """Sets the scene's frame range. Exported animations use it as their extent."""
    bpy.context.scene.frame_start = start
    bpy.context.scene.frame_end = end


# ── Materials ───────────────────────────────────────────────────────────────


@dataclass(frozen=True)
class MaterialSpec:
    """A material to create.

    Names are contractual: the Unity import step and the floor-material lookup
    both match on them. `FloorMaterial` in the core enum maps to
    ``Floor_Wood``, ``Floor_Tile``, ``Floor_Gravel``, ``Floor_Concrete`` and
    ``Floor_Metal``, so those five spellings must not drift.
    """

    name: str
    color: tuple[float, float, float]
    roughness: float = 0.7
    metallic: float = 0.0
    emission: float = 0.0


def make_material(spec: MaterialSpec) -> bpy.types.Material:
    """Creates (or reuses) a Principled BSDF material."""
    existing = bpy.data.materials.get(spec.name)
    if existing is not None:
        return existing

    mat = bpy.data.materials.new(spec.name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        r, g, b = spec.color
        bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)
        bsdf.inputs["Roughness"].default_value = spec.roughness
        bsdf.inputs["Metallic"].default_value = spec.metallic
        if spec.emission > 0.0 and "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = spec.emission
            if "Emission Color" in bsdf.inputs:
                bsdf.inputs["Emission Color"].default_value = (r, g, b, 1.0)
    return mat


def assign_material(obj: bpy.types.Object, mat: bpy.types.Material) -> None:
    """Replaces an object's material slots with a single material."""
    obj.data.materials.clear()
    obj.data.materials.append(mat)


# ── Primitives ──────────────────────────────────────────────────────────────


def add_box(
    name: str,
    size: tuple[float, float, float],
    location: tuple[float, float, float] = (0.0, 0.0, 0.0),
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    """Adds a box of the given metre dimensions, centred on `location`.

    Sizes are applied to the mesh rather than left on the object scale, because
    non-unit object scale exports as a scaled transform and then fights Unity's
    physics and NavMesh baking.
    """
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = Euler(rotation, "XYZ")
    obj.scale = Vector(size)
    apply_transforms(obj, scale=True, rotation=False)
    return obj


def add_cylinder(
    name: str,
    radius: float,
    depth: float,
    location: tuple[float, float, float] = (0.0, 0.0, 0.0),
    vertices: int = 16,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    """Adds a cylinder. 16 sides is plenty for a dark, first-person game."""
    bpy.ops.mesh.primitive_cylinder_add(
        radius=radius, depth=depth, location=location, vertices=vertices
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = Euler(rotation, "XYZ")
    return obj


def add_sphere(
    name: str,
    radius: float,
    location: tuple[float, float, float] = (0.0, 0.0, 0.0),
    segments: int = 16,
    rings: int = 8,
) -> bpy.types.Object:
    """Adds a UV sphere."""
    bpy.ops.mesh.primitive_uv_sphere_add(
        radius=radius, location=location, segments=segments, ring_count=rings
    )
    obj = bpy.context.active_object
    obj.name = name
    return obj


def add_plane(
    name: str,
    size_x: float,
    size_y: float,
    location: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    """Adds a rectangular plane. Used for floors, whose material carries §12 meaning."""
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = Vector((size_x, size_y, 1.0))
    apply_transforms(obj, scale=True)
    return obj


def apply_transforms(obj: bpy.types.Object, location: bool = False,
                     rotation: bool = False, scale: bool = True) -> None:
    """Bakes the object's transform into its mesh data."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=location, rotation=rotation, scale=scale)


# ── Mesh operations ─────────────────────────────────────────────────────────


def join(objects: Sequence[bpy.types.Object], name: str) -> bpy.types.Object:
    """Joins objects into one mesh.

    Worth doing before export: one mesh means one draw call and one collider,
    and a basement full of separate corridor pieces is a lot of both.
    """
    if not objects:
        raise ValueError("join() needs at least one object")

    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1:
        bpy.ops.object.join()

    result = bpy.context.active_object
    result.name = name
    return result


def bevel(obj: bpy.types.Object, width: float = 0.01, segments: int = 1) -> None:
    """Bevels every edge slightly.

    A hard 90° edge catches light as a single flat line and reads as untextured
    geometry. A 1 cm bevel gives it a highlight, which is most of what makes
    primitive-built geometry stop looking like primitives.
    """
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.bevel(
        bm,
        geom=list(bm.edges) + list(bm.verts),
        offset=width,
        segments=segments,
        profile=0.5,
        affect="EDGES",
    )
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def shade_smooth(obj: bpy.types.Object, angle_degrees: float = 30.0) -> None:
    """Smooth shading with an auto-smooth angle, so hard edges stay hard."""
    mesh = obj.data
    for poly in mesh.polygons:
        poly.use_smooth = True

    # Blender 4.1 removed mesh.use_auto_smooth in favour of a modifier.
    if hasattr(mesh, "use_auto_smooth"):
        mesh.use_auto_smooth = True
        mesh.auto_smooth_angle = math.radians(angle_degrees)
    else:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        try:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle_degrees))
        except (AttributeError, RuntimeError):
            pass  # Smooth shading is set above regardless; the angle is a refinement.


def triangulate(obj: bpy.types.Object) -> None:
    """Triangulates the mesh. Unity does this on import anyway; doing it here makes
    the exported triangle count the real one."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def uv_smart_project(obj: bpy.types.Object, angle_limit: float = 66.0) -> None:
    """Generates UVs. Needed for any material with a texture."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    try:
        bpy.ops.uv.smart_project(angle_limit=math.radians(angle_limit))
    except (RuntimeError, TypeError):
        bpy.ops.uv.smart_project()
    bpy.ops.object.mode_set(mode="OBJECT")


# ── Armatures ───────────────────────────────────────────────────────────────


@dataclass
class BoneSpec:
    """One bone of a rig."""

    name: str
    head: tuple[float, float, float]
    tail: tuple[float, float, float]
    parent: str | None = None
    connected: bool = False
    """Whether the head snaps to the parent's tail."""


def build_armature(name: str, bones: Sequence[BoneSpec]) -> bpy.types.Object:
    """Creates an armature from bone specs, parents included.

    Bone names are contractual for Unity's Humanoid mapping. Use Unity's expected
    names (Hips, Spine, Chest, Neck, Head, LeftUpperArm, …) on anything meant to
    retarget; anything else has to be mapped by hand in the importer.
    """
    bpy.ops.object.armature_add(location=(0.0, 0.0, 0.0))
    arm_obj = bpy.context.active_object
    arm_obj.name = name
    arm_obj.data.name = name + "_Data"

    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = arm_obj.data.edit_bones

    for b in list(edit_bones):
        edit_bones.remove(b)

    created: dict[str, bpy.types.EditBone] = {}
    for spec in bones:
        bone = edit_bones.new(spec.name)
        bone.head = Vector(spec.head)
        bone.tail = Vector(spec.tail)
        created[spec.name] = bone

    for spec in bones:
        if spec.parent:
            parent = created.get(spec.parent)
            if parent is None:
                raise ValueError(f"bone '{spec.name}' names a missing parent '{spec.parent}'")
            created[spec.name].parent = parent
            created[spec.name].use_connect = spec.connected

    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


def bind_skin(mesh_obj: bpy.types.Object, arm_obj: bpy.types.Object,
              auto_weights: bool = True) -> None:
    """Parents a mesh to an armature with automatic weights."""
    bpy.ops.object.select_all(action="DESELECT")
    mesh_obj.select_set(True)
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.parent_set(type="ARMATURE_AUTO" if auto_weights else "ARMATURE")


# ── Animation ───────────────────────────────────────────────────────────────


@dataclass
class Pose:
    """A pose at one frame: per-bone rotation in degrees and optional location."""

    frame: int
    rotations: dict[str, tuple[float, float, float]] = field(default_factory=dict)
    locations: dict[str, tuple[float, float, float]] = field(default_factory=dict)


def make_action(arm_obj: bpy.types.Object, action_name: str, poses: Sequence[Pose],
                loop: bool = True) -> bpy.types.Action:
    """Keyframes an armature action from a list of poses.

    When `loop` is set the first pose is copied onto a frame one past the last, so
    the cycle closes. Without it a walk animation visibly snaps on repeat — which
    matters here because §05 lists walking, running and carrying as the animations
    other players will see constantly.
    """
    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="POSE")

    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    action = bpy.data.actions.new(action_name)
    arm_obj.animation_data.action = action

    ordered = sorted(poses, key=lambda p: p.frame)
    sequence = list(ordered)
    if loop and len(ordered) > 1:
        last = ordered[-1].frame
        first = ordered[0]
        step = last - ordered[-2].frame if len(ordered) > 1 else 1
        sequence.append(Pose(frame=last + max(1, step),
                             rotations=dict(first.rotations),
                             locations=dict(first.locations)))

    for pose in sequence:
        bpy.context.scene.frame_set(pose.frame)
        for bone_name, rot in pose.rotations.items():
            pbone = arm_obj.pose.bones.get(bone_name)
            if pbone is None:
                raise ValueError(f"action '{action_name}' poses a missing bone '{bone_name}'")
            pbone.rotation_mode = "XYZ"
            pbone.rotation_euler = Euler([math.radians(a) for a in rot], "XYZ")
            pbone.keyframe_insert(data_path="rotation_euler", frame=pose.frame)
        for bone_name, loc in pose.locations.items():
            pbone = arm_obj.pose.bones.get(bone_name)
            if pbone is None:
                raise ValueError(f"action '{action_name}' poses a missing bone '{bone_name}'")
            pbone.location = Vector(loc)
            pbone.keyframe_insert(data_path="location", frame=pose.frame)

    for fcurve in iter_fcurves(action):
        for kp in fcurve.keyframe_points:
            kp.interpolation = "BEZIER"

    bpy.ops.object.mode_set(mode="OBJECT")
    return action


def iter_fcurves(action: bpy.types.Action) -> Iterable[bpy.types.FCurve]:
    """Yields every F-curve of an action, across Blender's two action models.

    Blender 4.4 replaced the flat ``Action.fcurves`` list with slotted actions,
    where curves live at ``action.layers[].strips[].channelbags[].fcurves``, and
    5.x removed the old attribute entirely. Reaching for ``action.fcurves``
    therefore raises on any current Blender, so all curve access goes through here.
    """
    flat = getattr(action, "fcurves", None)
    if flat is not None:
        for fcurve in flat:
            yield fcurve
        return

    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            bags = getattr(strip, "channelbags", None)
            if bags is None:
                # Some builds expose a per-slot accessor instead of a collection.
                getter = getattr(strip, "channelbag", None)
                bags = [getter(slot) for slot in getattr(action, "slots", [])] if getter else []
            for bag in bags:
                if bag is None:
                    continue
                for fcurve in getattr(bag, "fcurves", []):
                    yield fcurve


def stash_action(arm_obj: bpy.types.Object, action: bpy.types.Action) -> None:
    """Pushes an action into an NLA track so the FBX exporter emits it as its own clip.

    Without this, only the single active action exports and every other animation
    is silently dropped.
    """
    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    track = arm_obj.animation_data.nla_tracks.new()
    track.name = action.name
    track.strips.new(action.name, int(action.frame_range[0]), action)
    arm_obj.animation_data.action = None


# ── Export ──────────────────────────────────────────────────────────────────


def export_fbx(path: str, objects: Sequence[bpy.types.Object] | None = None,
               with_animation: bool = False) -> str:
    """Exports to FBX with Unity-correct settings.

    The axis and bone settings are the ones that matter, and getting them wrong is
    subtle rather than obvious:

    * ``axis_forward='-Z'``, ``axis_up='Y'`` — Blender is Z-up, Unity is Y-up.
      Skip this and every asset arrives lying on its back.
    * ``add_leaf_bones=False`` — Blender otherwise appends an extra tip bone per
      chain, which breaks Unity's Humanoid auto-mapping.
    * ``primary_bone_axis='Y'``, ``secondary_bone_axis='X'`` — Unity's convention.
    * ``apply_unit_scale=True`` with ``FBX_SCALE_NONE`` — keeps 1 unit = 1 metre
      instead of arriving at 0.01 scale.
    """
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)

    bpy.ops.object.select_all(action="DESELECT")
    if objects is None:
        bpy.ops.object.select_all(action="SELECT")
    else:
        for o in objects:
            o.select_set(True)
        if objects:
            bpy.context.view_layer.objects.active = objects[0]

    kwargs = dict(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        object_types={"ARMATURE", "MESH"} if with_animation else {"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=False,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        bake_anim=with_animation,
        bake_anim_use_all_bones=with_animation,
        # NLA strips only. `stash_action` puts every clip in its own NLA track, so
        # enabling `use_all_actions` as well exports each one TWICE — once from the
        # track and once as a loose action — and the duplicates arrive in Unity
        # double-prefixed ("Rig|Walk" alongside "Rig|Rig|Walk"). Both flags on looks
        # like belt-and-braces and is actually a duplication bug.
        bake_anim_use_nla_strips=with_animation,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=with_animation,
        path_mode="COPY",
        embed_textures=False,
    )

    try:
        bpy.ops.export_scene.fbx(**kwargs)
    except TypeError as exc:
        # Blender occasionally retires an export flag between versions. Drop the
        # rejected one and retry rather than failing the whole asset build.
        bad = str(exc)
        for key in list(kwargs):
            if key in bad and key != "filepath":
                kwargs.pop(key)
                break
        bpy.ops.export_scene.fbx(**kwargs)

    return path


def export_gltf(path: str, with_animation: bool = False) -> str:
    """Exports glTF. Useful for previewing an asset without opening Unity."""
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        export_yup=True,
        export_animations=with_animation,
        export_apply=True,
    )
    return path


# ── Verification ────────────────────────────────────────────────────────────


@dataclass(frozen=True)
class AssetReport:
    """What was actually produced. Printed by every generator."""

    path: str
    bytes: int
    objects: int
    vertices: int
    triangles: int
    materials: int
    bones: int
    actions: int
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]

    @property
    def size(self) -> tuple[float, float, float]:
        """Bounding-box dimensions in metres."""
        return tuple(self.bounds_max[i] - self.bounds_min[i] for i in range(3))


def describe(path: str) -> AssetReport:
    """Measures the current scene and the file just written.

    A generator can run without error and still produce nothing useful — an empty
    export, a model 100× too large, a rig whose actions were dropped. Reporting
    real numbers turns those into visible failures.
    """
    verts = tris = mats = bones = 0
    objs = 0
    lo = [math.inf] * 3
    hi = [-math.inf] * 3

    for obj in bpy.context.scene.objects:
        if obj.type == "MESH":
            objs += 1
            mesh = obj.data
            verts += len(mesh.vertices)
            tris += sum(max(0, len(p.vertices) - 2) for p in mesh.polygons)
            mats += len(mesh.materials)
            for corner in obj.bound_box:
                world = obj.matrix_world @ Vector(corner)
                for i in range(3):
                    lo[i] = min(lo[i], world[i])
                    hi[i] = max(hi[i], world[i])
        elif obj.type == "ARMATURE":
            bones += len(obj.data.bones)

    if not math.isfinite(lo[0]):
        lo = [0.0] * 3
        hi = [0.0] * 3

    return AssetReport(
        path=path,
        bytes=os.path.getsize(path) if os.path.exists(path) else 0,
        objects=objs,
        vertices=verts,
        triangles=tris,
        materials=len(bpy.data.materials),
        bones=bones,
        actions=len(bpy.data.actions),
        bounds_min=tuple(lo),
        bounds_max=tuple(hi),
    )


def assert_asset(
    report: AssetReport,
    min_vertices: int = 8,
    max_triangles: int = 40000,
    expect_bones: int = 0,
    expect_actions: int = 0,
    max_dimension: float = 200.0,
    exact_actions: int | None = None,
) -> AssetReport:
    """Raises if the produced asset is unusable. Returns the report.

    `max_dimension` defaults generously because §12 sizes a whole map at 100 m,
    but a *character* passing 3 m almost always means a unit-scale mistake, so
    character generators should tighten it.
    """
    if report.bytes < 512:
        raise AssertionError(f"{report.path}: file is {report.bytes} bytes — the export produced nothing")
    if report.vertices < min_vertices:
        raise AssertionError(f"{report.path}: only {report.vertices} vertices — the mesh is empty or was cleared")
    if report.triangles > max_triangles:
        raise AssertionError(
            f"{report.path}: {report.triangles} triangles exceeds {max_triangles}. "
            "The game is dark and first-person (§05) — detail here is wasted budget."
        )
    if report.bones < expect_bones:
        raise AssertionError(f"{report.path}: {report.bones} bones, expected at least {expect_bones}")
    if report.actions < expect_actions:
        raise AssertionError(
            f"{report.path}: {report.actions} actions, expected at least {expect_actions}. "
            "Actions must be stashed into NLA tracks or the FBX exporter drops all but the active one."
        )
    if exact_actions is not None and report.actions != exact_actions:
        raise AssertionError(
            f"{report.path}: {report.actions} actions, expected exactly {exact_actions}. "
            "More than expected usually means each clip was exported twice — see the "
            "note on bake_anim_use_all_actions in export_fbx."
        )

    duplicated = _duplicated_action_names()
    if duplicated:
        raise AssertionError(
            f"{report.path}: these action names have a repeated rig prefix: {duplicated}. "
            "The FBX exporter emitted each clip twice, once from its NLA track and once "
            "as a loose action, and Unity will import both. Export with NLA strips or "
            "all-actions, never both."
        )

    biggest = max(report.size)
    if biggest > max_dimension:
        raise AssertionError(
            f"{report.path}: largest dimension {biggest:.2f} m exceeds {max_dimension} m — "
            "check unit scale; 1 Blender unit must be 1 metre."
        )
    return report


def _duplicated_action_names() -> list[str]:
    """Finds action names whose rig prefix repeats, e.g. ``Rig|Rig|Walk``.

    That shape is the signature of a double export rather than a naming choice, and
    it is invisible until the clips show up twice in Unity's animator.
    """
    offenders = []
    for action in bpy.data.actions:
        parts = action.name.split("|")
        if len(parts) >= 3 and parts[0] == parts[1]:
            offenders.append(action.name)
    return offenders


def print_report(report: AssetReport) -> None:
    """Prints a report in a format the calling script's log can be grepped for."""
    sx, sy, sz = report.size
    print("ASSET_REPORT " + " ".join([
        f"path={os.path.relpath(report.path, REPO_ROOT)}",
        f"bytes={report.bytes}",
        f"objects={report.objects}",
        f"verts={report.vertices}",
        f"tris={report.triangles}",
        f"materials={report.materials}",
        f"bones={report.bones}",
        f"actions={report.actions}",
        f"size={sx:.2f}x{sy:.2f}x{sz:.2f}m",
    ]))


def fail(message: str) -> None:
    """Prints a failure and exits non-zero so a headless run reports as failed.

    Blender's `--background` exits 0 even after a Python exception, so a generator
    that dies quietly looks like it succeeded. Every generator must route failures
    through here.
    """
    print(f"ASSET_FAILED {message}", file=sys.stderr)
    sys.exit(1)
