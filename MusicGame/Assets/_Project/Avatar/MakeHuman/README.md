# Canonical avatar consumer

The generator and all MakeHuman/authoring source are owned by AvatarLab at `Assets/AvatarLab/Canonical`.

This pipeline consumes AvatarLab's exported `MakeHuman_Canonical.fbx`. MusicGame consumes the same FBX and owns gameplay assets, skin-visibility masks, clothing assembly and blendshape sliders. It contains no human generator or raw MakeHuman source.

To update: generate in AvatarLab, review the model, then publish the approved FBX to this existing fitting-avatar path and MusicGame's existing canonical FBX path, preserving Unity metadata. Review fitting and garment morphs after geometry changes. Historical fitting records must retain their actual source hash until migration is performed.

Head and hand/wrist anchors use the approved shared midpoint and gradual attachment transitions. Male and female body channels remain independent elsewhere.
