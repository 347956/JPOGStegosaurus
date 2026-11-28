"""Run with Blender 4.1: blender --background --python export_fbx.py -- <output directory>."""

from pathlib import Path
import sys

import bpy
from mathutils import Matrix
from io_scene_fbx import export_fbx_bin


# Blender records the full .blend path in the header even with metadata disabled.
fbx_compound = export_fbx_bin.elem_props_compound


def use_relative_source_name(element, name, *args, **kwargs):
    setter = fbx_compound(element, name, *args, **kwargs)

    def set_property(kind, property_name, value, *property_args, **property_kwargs):
        if name == b"Original" and property_name == b"ApplicationNativeFile":
            value = Path(value).name
        setter(kind, property_name, value, *property_args, **property_kwargs)

    return set_property


export_fbx_bin.elem_props_compound = use_relative_source_name


def open_rig(source):
    bpy.ops.wm.open_mainfile(filepath=str(Path(__file__).parent / source))
    rig = bpy.data.objects["Steg_hi"]
    rig.animation_data_clear()
    rig.matrix_world = Matrix.Identity(4)
    bpy.context.scene.render.fps = 30
    bpy.context.scene.render.fps_base = 1.0
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = rig
    rig.hide_set(False)
    rig.select_set(True)
    return rig


def export(path, object_types, **animation):
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types=object_types,
        axis_forward="-Z", axis_up="Y", apply_unit_scale=True, add_leaf_bones=False,
        use_metadata=False, **animation,
    )


def export_take(rig, action, path):
    start, end = map(round, action.frame_range)
    rig.animation_data_clear()
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    track = rig.animation_data_create().nla_tracks.new()
    strip = track.strips.new(action.name, start, action)
    strip.action_frame_start = strip.frame_start = start
    strip.action_frame_end = strip.frame_end = end
    strip.extrapolation = "NOTHING"
    bpy.context.scene.frame_set(start - 2)
    export(path, {"ARMATURE"}, bake_anim=True, bake_anim_use_nla_strips=True,
           bake_anim_use_all_actions=False, bake_anim_simplify_factor=0.0)


output = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
takes = output / "FBX"
takes.mkdir(parents=True, exist_ok=True)
rig = open_rig("StegoFIXblend.blend")
rig.data.pose_position = "REST"
for mesh in rig.children:
    if mesh.type == "MESH":
        mesh.animation_data_clear()
        mesh.hide_set(False)
        mesh.select_set(True)
bpy.context.view_layer.update()
export(output / "JPOGStegosaurus.fbx", {"ARMATURE", "MESH"}, bake_anim=False)

rig.data.pose_position = "POSE"
for child in rig.children:
    child.select_set(False)
for action in bpy.data.actions:
    if any(curve.keyframe_points for curve in action.fcurves):
        export_take(rig, action, takes / f"{action.name}.fbx")

# The older authoring file also has the separate reverse swipe used to construct the edited take.
rig = open_rig("JPOGStegosaurus.blend")
rig.data.pose_position = "POSE"
reverse = bpy.data.actions["Lcarn_statkReverse"]
export_take(rig, reverse, takes / f"{reverse.name}.fbx")
