"""
CMU mocap BVH -> Unity-ready FBX (armature + one baked action), run headless:

  blender -b --factory-startup --python cmu_bvh_to_fbx.py -- <in.bvh> <out.fbx> <first_frame> <last_frame>

first/last are 0-based BVH frame indices of the loop segment to keep.

Why this exists: the CMU BVH conversion (cgspeed 2010 release) puts a proper T-pose in BVH
frame 0, but its REST pose (all-zero rotations) is NOT a T-pose (legs ~21 deg apart, arms ~8 deg
low). Unity's Humanoid importer takes the model's rest pose as the T-pose reference, so we rebuild
the armature with frame 0 as its rest pose and re-bake the motion onto it (world-space copy, so
the motion itself is unchanged). Everything else (Humanoid mapping, loop/root settings) is done by
Unity's importer — see MakeHumanAnimationTestSetup.cs.
"""
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
bvh_path, fbx_path, first, last = argv[0], argv[1], int(argv[2]), int(argv[3])

bpy.ops.wm.read_factory_settings(use_empty=True)
# CMU BVH units are ~inches-scale; 0.056444 is the standard CMU -> metres factor.
bpy.ops.import_anim.bvh(filepath=bvh_path, global_scale=0.056444, frame_start=0,
                        use_fps_scale=False, update_scene_fps=True, update_scene_duration=True,
                        rotate_mode='NATIVE', axis_forward='-Z', axis_up='Y')
source = bpy.context.view_layer.objects.active
scene = bpy.context.scene

# Target armature: same bones, rest = the BVH frame-0 T-pose.
target = source.copy()
target.data = source.data.copy()
target.animation_data_clear()
target.name = "CMU"
scene.collection.objects.link(target)

scene.frame_set(0)
for pb in target.pose.bones:
    pb.matrix_basis = source.pose.bones[pb.name].matrix_basis.copy()

bpy.ops.object.select_all(action='DESELECT')
target.select_set(True)
bpy.context.view_layer.objects.active = target
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.armature_apply(selected=False)

for pb in target.pose.bones:
    c = pb.constraints.new('COPY_TRANSFORMS')
    c.target = source
    c.subtarget = pb.name
    c.target_space = 'WORLD'
    c.owner_space = 'WORLD'

bpy.ops.pose.select_all(action='SELECT')
bpy.ops.nla.bake(frame_start=first, frame_end=last, only_selected=False, visual_keying=True,
                 clear_constraints=True, use_current_action=False, bake_types={'POSE'})
bpy.ops.object.mode_set(mode='OBJECT')
target.animation_data.action.name = "Take"

bpy.data.objects.remove(source, do_unlink=True)
scene.frame_start, scene.frame_end = first, last

bpy.ops.export_scene.fbx(filepath=fbx_path, object_types={'ARMATURE'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_UNITS')
print(f"[cmu_bvh_to_fbx] {bvh_path} frames {first}..{last} -> {fbx_path}")
