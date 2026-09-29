"""
SYNTHETIC pipeline fixture (NOT Rokoko data, never part of the combat library):

  blender -b --factory-startup --python make_mixamo_fixture.py -- <cmu.bvh> <out.fbx> <first> <last>

Takes a CMU BVH take (already in the project, CMU licence), rebuilds its armature with the BVH
frame-0 T-pose as rest (same as AnimationTests/Source/cmu_bvh_to_fbx.py), RENAMES every bone to the
Mixamo convention ("mixamorig:Hips", "mixamorig:Spine2", ...) and replaces the CMU finger stubs with
a full Mixamo 5x4 finger chain per hand whose curl is animated (a slow open/close sine) — i.e. the
same shape as a Rokoko "Mixamo skeleton" export with Smartgloves finger capture.

It exists only so the Rokoko import pipeline (CombatAnimationImporter) can be exercised end to end
before the real Rokoko packs are downloaded: bone auto-detection, Humanoid source Avatar, finger
preservation, processed + in-place clips, library entries, tests.
"""
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
bvh_path, fbx_path, first, last = argv[0], argv[1], int(argv[2]), int(argv[3])

RENAME = {
    "Hips": "Hips", "LowerBack": "Spine", "Spine": "Spine1", "Spine1": "Spine2",
    "Neck": "Neck", "Neck1": "Neck1", "Head": "Head",
    "LHipJoint": "LHipJoint", "RHipJoint": "RHipJoint",
    "LeftUpLeg": "LeftUpLeg", "LeftLeg": "LeftLeg", "LeftFoot": "LeftFoot", "LeftToeBase": "LeftToeBase",
    "RightUpLeg": "RightUpLeg", "RightLeg": "RightLeg", "RightFoot": "RightFoot", "RightToeBase": "RightToeBase",
    "LeftShoulder": "LeftShoulder", "LeftArm": "LeftArm", "LeftForeArm": "LeftForeArm", "LeftHand": "LeftHand",
    "RightShoulder": "RightShoulder", "RightArm": "RightArm", "RightForeArm": "RightForeArm", "RightHand": "RightHand",
}
STUBS = ["LeftFingerBase", "LeftHandIndex1", "LThumb", "RightFingerBase", "RightHandIndex1", "RThumb"]
FINGERS = ["Thumb", "Index", "Middle", "Ring", "Pinky"]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_anim.bvh(filepath=bvh_path, global_scale=0.056444, frame_start=0,
                        use_fps_scale=False, update_scene_fps=True, update_scene_duration=True,
                        rotate_mode='NATIVE', axis_forward='-Z', axis_up='Y')
source = bpy.context.view_layer.objects.active
scene = bpy.context.scene

target = source.copy()
target.data = source.data.copy()
target.animation_data_clear()
target.name = "Armature"
scene.collection.objects.link(target)

scene.frame_set(0)
for pb in target.pose.bones:
    pb.matrix_basis = source.pose.bones[pb.name].matrix_basis.copy()

bpy.ops.object.select_all(action='DESELECT')
target.select_set(True)
bpy.context.view_layer.objects.active = target
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.armature_apply(selected=False)

# Rename + replace finger stubs (edit mode).
bpy.ops.object.mode_set(mode='EDIT')
eb = target.data.edit_bones
for stub in STUBS:
    if stub in eb:
        eb.remove(eb[stub])
original_of = {}
for bone in list(eb):
    new = "mixamorig:" + RENAME.get(bone.name, bone.name)
    original_of[new] = bone.name
    bone.name = new

up = Vector((0, 0, 1))
for side in ("Left", "Right"):
    hand = eb[f"mixamorig:{side}Hand"]
    direction = (hand.tail - hand.head).normalized()
    across = direction.cross(up).normalized()          # palm-plane axis the fingers are spread along
    base = hand.head + direction * 0.08
    for f, finger in enumerate(FINGERS):
        if finger == "Thumb":
            head = hand.head + direction * 0.025 + across * 0.03
            d = (direction + across).normalized()
        else:
            head = base + across * (0.03 - 0.02 * (f - 1))
            d = direction
        parent = hand
        for j, length in enumerate((0.035, 0.025, 0.02, 0.015), start=1):
            bone = eb.new(f"mixamorig:{side}Hand{finger}{j}")
            bone.head = head
            bone.tail = head + d * length
            bone.roll = 0.0
            bone.parent = parent
            bone.use_connect = j > 1
            parent = bone
            head = bone.tail
bpy.ops.object.mode_set(mode='POSE')

for pb in target.pose.bones:
    if pb.name in original_of:
        c = pb.constraints.new('COPY_TRANSFORMS')
        c.target = source
        c.subtarget = original_of[pb.name]
        c.target_space = 'WORLD'
        c.owner_space = 'WORLD'

bpy.ops.pose.select_all(action='SELECT')
bpy.ops.nla.bake(frame_start=first, frame_end=last, only_selected=False, visual_keying=True,
                 clear_constraints=True, use_current_action=False, bake_types={'POSE'})

# Animated finger curl: phalanges 1-3 open/close over a 1.5 s period (4th is the end bone).
fps = scene.render.fps
for frame in range(first, last + 1):
    curl = 0.5 + 0.5 * math.sin(2.0 * math.pi * (frame - first) / (1.5 * fps))
    for side in ("Left", "Right"):
        for finger in FINGERS:
            for j in (1, 2, 3):
                pb = target.pose.bones[f"mixamorig:{side}Hand{finger}{j}"]
                pb.rotation_mode = 'XYZ'
                pb.rotation_euler = (curl * (0.9 if finger != "Thumb" else 0.4), 0.0, 0.0)
                pb.keyframe_insert("rotation_euler", frame=frame)
bpy.ops.object.mode_set(mode='OBJECT')
target.animation_data.action.name = "Fixture_MixamoWalk"

bpy.data.objects.remove(source, do_unlink=True)
scene.frame_start, scene.frame_end = first, last

bpy.ops.export_scene.fbx(filepath=fbx_path, object_types={'ARMATURE'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS')
print(f"[make_mixamo_fixture] {bvh_path} frames {first}..{last} -> {fbx_path}")
