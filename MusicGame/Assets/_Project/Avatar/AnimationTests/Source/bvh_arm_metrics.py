"""
Source-side ground truth for rig diagnosis, computed straight from a CMU BVH (no Unity involved):

  blender -b --factory-startup --python bvh_arm_metrics.py -- <in.bvh> <first> <last>

Prints per-side upper-arm FLEXION (forward +, back -), ABDUCTION (away from the body) and ELBOW bend
in degrees (min / mean / max over the segment), measured in the subject's own body frame
(right = right hip - left hip, up = world up). MakeHumanRigDiagnostics prints the same numbers for the
retargeted MakeHuman avatar, so the two can be compared directly.
"""
import sys, math
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
path, first, last = argv[0], int(argv[1]), int(argv[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_anim.bvh(filepath=path, global_scale=0.056444, frame_start=0, use_fps_scale=False,
                        update_scene_fps=True, update_scene_duration=True)
arm = bpy.context.view_layer.objects.active
scene = bpy.context.scene

def P(bone):
    return arm.matrix_world @ arm.pose.bones[bone].head

stats = {}
def add(key, value):
    stats.setdefault(key, []).append(value)

for frame in range(first, last + 1, 2):
    scene.frame_set(frame)
    up = Vector((0, 0, 1))
    right = P("RightUpLeg") - P("LeftUpLeg")
    right.z = 0
    right.normalize()
    forward = up.cross(right)  # right-handed, Z up: character facing -Y has right = -X -> forward = -Y
    for side, sign in (("Left", -1.0), ("Right", 1.0)):
        shoulder, elbow, wrist = P(f"{side}Arm"), P(f"{side}ForeArm"), P(f"{side}Hand")
        d = (elbow - shoulder).normalized()
        flex = math.degrees(math.atan2(d.dot(forward), -d.dot(up)))
        abd = math.degrees(math.atan2(sign * d.dot(right), -d.dot(up)))
        e = (wrist - elbow).normalized()
        elbow_bend = math.degrees(d.angle(e))
        add(f"{side} flex", flex); add(f"{side} abd", abd); add(f"{side} elbow", elbow_bend)

for key in sorted(stats):
    v = stats[key]
    print(f"SRCARM {path.split('/')[-1]} {key:12s} min {min(v):+6.1f} mean {sum(v)/len(v):+6.1f} max {max(v):+6.1f}")
