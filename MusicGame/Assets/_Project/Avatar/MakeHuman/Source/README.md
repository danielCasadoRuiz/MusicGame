# MakeHuman source data (CC0)

Input for **Tools > MusicGame > Avatars > MakeHuman Body Builder**. Nothing here ships in a build:
the builder bakes it into `../Generated/` (meshes, presets, Humanoid Avatars, prefab).

| Path | What | Origin |
|---|---|---|
| `Mesh/base_mesh.txt` | MakeHuman **hm08** `base.obj` (19,158 vertices), renamed `.txt` | Copied from the AvatarLab project (`Assets/AvatarLab/Resources/MakeHuman/Mesh`) |
| `Targets/macrodetails/*.txt` | 20 macrodetails targets: `universal-{male,female}-young-*` + `caucasian-{male,female}-young` | Copied from AvatarLab (same folder); decompressed `.target.gz` |
| `Rig/rig.game_engine.json` | MakeHuman "game_engine" rig (53 bones, joints = `joint-*` cube means) | `makehumancommunity/mpfb2` @ `3edf9df0`, `src/mpfb/data/rigs/standard/` |
| `Rig/weights.game_engine.json` | Skin weights for that rig, keyed by raw hm08 vertex index | same |

**License:** CC0 1.0 Universal (the header of every mesh/target file, `"license": "CC0"` in the rig files,
and `LICENSE.ASSETS.md` in mpfb2). Only MakeHuman's *data* is used. No MakeHuman program code
(AGPL) is included or linked: the target evaluation and rig resolution are MusicGame's own C#, ported
from AvatarLab. See AvatarLab's `THIRD_PARTY_LICENSES.md` for the full licensing notes.

## Why only 20 targets

MusicGame bakes at a fixed young/caucasian/average-height/average-proportions state and only varies
Gender, Weight and Muscle. Every other macrodetails corner (baby/child/old, asian/african, the
min/max height and proportions targets) has a weight of exactly zero there, so leaving them out
changes nothing. To expose Age or Race later, add the relevant `.target` files; the loader and evaluator
need no code change.

## Re-downloading

```
git clone --depth 1 --filter=blob:none --sparse https://github.com/makehumancommunity/mpfb2.git
cd mpfb2
git sparse-checkout set --no-cone src/mpfb/data/3dobjs src/mpfb/data/targets src/mpfb/data/rigs
# targets are .target.gz: gunzip them and rename *.target -> *.txt
```
