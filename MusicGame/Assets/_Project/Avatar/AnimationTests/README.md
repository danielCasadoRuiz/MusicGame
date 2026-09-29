# Rig-validation animations (Idle / Walk / Run)

Plain, realistic human locomotion used only to validate Humanoid retargeting onto the MakeHuman avatar
in `Scenes/Debug/AvatarDebug.unity`. This is not gameplay locomotion.

| State | Clip | CMU take | Segment (BVH frames @120 fps) | Loop | Why this take |
|---|---|---|---|---|---|
| Idle | `Clips/CMU_Idle_111_28.fbx` | 111_28 "Standing still" | 35–339 | 2.53 s | Most stable standing take: feet drift 2 mm, hips bob 1 mm, sway 6 mm, arms hanging relaxed |
| Walk | `Clips/CMU_Walk_07_09.fbx` | 07_09 "walk" | 104–231 | 1.06 s | Symmetric natural arm swing (±20–25°, arms clear of the torso), clean foot contact |
| Run  | `Clips/CMU_Run_09_05.fbx`  | 09_05 "run" | 16–106 | 0.75 s | Symmetric arm swing, elbows about 100°, arms away from the body |

## Why the first set was replaced

The rig diagnosis (Tools > MusicGame > Avatars > Rig Diagnostics, plus `Source/bvh_arm_metrics.py`) showed
that the retargeted arms reproduce the source mocap within a few degrees. The problems were in the
source takes themselves:

- **77_02 (idle):** the subject sways 77 mm side to side, which looked like "floating".
- **16_15 (walk):** only ±8° of arm swing, and the right arm is held against the torso (the "dead arm").
- **35_17 (run):** the right arm has 0° abduction, so the hands cross in front of the belly.

The replacements were chosen by scanning about 80 CMU walk/run takes and 7 idle takes with
the same metrics (arm swing and symmetry, abduction, elbow bend, foot drift, hip sway).

## Source and license

Carnegie Mellon University Graphics Lab Motion Capture Database (mocap.cs.cmu.edu), via the cgspeed
"MotionBuilder-friendly" BVH conversion (mirror: github.com/una-dinosauria/cmu-mocap). License text
from the release: *"This data is free for use in research and commercial projects worldwide."*
Suggested acknowledgment: "The data used in this project was obtained from mocap.cs.cmu.edu. The
database was created with funding from NSF EIA-0196217." This is not Mixamo, and no third-party
framework is involved.

## Pipeline (all reproducible)

1. `Source/cmu_bvh_to_fbx.py` (Blender 4.2, headless) imports the BVH, rebuilds the armature with the
   BVH frame-0 T-pose as its rest pose, re-bakes the segment in world space and exports an FBX:
   ```
   blender -b --factory-startup --python Source/cmu_bvh_to_fbx.py -- Source/07_09.bvh Clips/CMU_Walk_07_09.fbx 104 231
   ```
2. **Tools > MusicGame > Avatars > Setup Animation Test Clips** (`MakeHumanAnimationTestSetup`) does the rest:
   - Humanoid import with an explicit, finger-free bone map.
   - The source T-pose is enforced on the mapped bones only (spine and neck up, legs down, arms
     horizontal). The cgspeed T-pose is synthetic and off by up to about 40°.
   - Root settings keep the character in place.
   - Derived clips (`Clips/Derived/*.anim`) are generated, with finger muscles pinned to the MakeHuman
     relaxed rest hand, the ground offset calibrated and the facing calibrated.
   - `AvatarDebugAnimator.controller` is built with Idle / Walk / Run states and Foot IK on.

The MakeHuman avatar (`MakeHuman_Canonical.fbx`) is not touched by any of this.
