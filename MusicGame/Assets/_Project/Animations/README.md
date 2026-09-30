# Animations: combat / action mocap library (debug only)

```
Source/Rokoko/Fight/        <- official Rokoko FBX files, untouched (only their import settings change)
Source/Rokoko/MartialArts/
Source/Rokoko/EricJacobus/
Source/Rokoko/Superhero/
Processed/Combat/<Pack>/<Name>.anim                  editable copy, source root motion intact
Processed/Combat/<Pack>/InPlace/<Name>_InPlace.anim  derived debug clip (in place, grounded, faces +Z)
Processed/Combat/CombatAnimationLibrary.asset        CombatAnimationLibrarySO: entries, roles, demo
Debug/PipelineFixture/                               SYNTHETIC Mixamo-named test rig (CMU walk + fake finger curl), tests only
```

## 1. Download (manual: every pack is behind a Rokoko registration form)

Only the official Rokoko pages. Do not use mirrors.

| Pack | Official page | Unzip the FBX files into |
|---|---|---|
| Rokoko Mocap: 13 Free Fight Animations | https://www.rokoko.com/resources/rokoko-mocap-13-free-fight-animations | `Source/Rokoko/Fight/` |
| Rokoko Mocap: 6 Free Martial Arts Animations | https://www.rokoko.com/resources/rokoko-mocap-6-free-martial-arts-animations | `Source/Rokoko/MartialArts/` |
| Motion Library: 10 free assets from Eric Jacobus (God of War) | https://www.rokoko.com/resources/motion-library-10-free-motion-assets-from-eric-jacobus-god-of-war | `Source/Rokoko/EricJacobus/` |
| Rokoko Mocap: 15 Free Superhero Animations | https://www.rokoko.com/resources/rokoko-mocap-15-free-superhero-animations | `Source/Rokoko/Superhero/` |

- Put in the `.fbx` files only. Subfolders are fine. Do not add the ZIPs, and do not add the `.blend` or `.bvh` variants if the pack includes them.
- Licence (as stated on the Rokoko pages): free to use in any project, including commercial ones. The Jacobus page shows no licence text, so check the text inside that ZIP.

## 2. Import

Run **Tools > MusicGame > Animations > Import Rokoko Combat Packs**. With the editor closed you can also run it in batch:
`Unity.exe -batchmode -projectPath . -executeMethod CombatAnimationImporter.ImportFromCommandLine -quit`

What the importer does for each FBX:

- **Rig.** Sets the FBX to Humanoid with its own source Avatar, created from the FBX's own skeleton. Mixamo bone names are mapped explicitly, including all 10 fingers. The source T-pose is enforced on the arms and legs, and on the spine only if it tilts more than 10°.
- **Takes.** Trims a pop in the first or last 5 frames, such as a T-pose calibration frame.
- **Processed clips.** Writes two `.anim` files per take, as shown in the folder layout above.
- **Fingers.** Keeps the Rokoko finger curves as captured. The CMU relaxed-hand fix is applied only to entries whose `fingerMode` you set to `RelaxedRestHand`.
- **Library entries.** Fills the category (a guess from the file name, marked "auto"), loop flag, root-motion recommendation (more than 0.3 m of travel or 45° of turn), measured travel and yaw, finger capture, and warnings.
- **Re-imports.** `review` and `notes` are always kept. For reviewed entries, `category`, `loop` and `fingerMode` are kept too.
- **Safety.** `MakeHuman_Canonical.fbx` is never touched. The importer hashes it before and after every run.

### Segments (single moves cut from long takes)

Every Rokoko file is one long take, 10 to 85 s long. The single moves (one punch, one kick…) are defined in `CombatAnimationLibrarySO.segments` as `sourceEntry`, `name`, `start` and `end` in seconds, plus `category`, `loop`, `keepFacing` and `notes`.

- **Output.** The importer turns each segment into an extra importer clip, and from it into a library entry named `<take>__<name>`, with its own clips under `Processed/Combat/<Pack>/Segments/`.
- **`keepFacing`.** On by default, the root yaw is extracted, so the character keeps facing forward. Turn it off only when the turn is part of the move (the spinning roundhouse).

To find the moves:
- **Motion analysis.** `Logs/CombatPreview/_index.txt` lists the hand and foot speed peaks and the spans with the pelvis on the floor.
- **Window renders.** `-executeMethod MakeHumanBodyTests.RenderCombatPreviewsFromCommandLine -combatWindows "Entry@8.0:9.6;Entry2@3:4" -quit` renders those time windows frame by frame.

**Ground.** Each in-place clip's `level` is set so the robust floor sits at y = 0. The robust floor is the 25th percentile of the per-frame lowest point, after discarding samples that jump away from their neighbours, so a single capture glitch cannot lift the whole clip.

## 3. Review and browse

- **Contact sheets.** Run **Tools > MusicGame > Animations > Render Combat Library Previews**. It writes `Logs/CombatPreview/<name>.png`: 8 phases on the Male and Female avatars, plus the source model when the FBX has a mesh. For clips with finger capture it also writes `<name>_hands_*.png`.
- **Browser.** Open `Scenes/Debug/AvatarDebug.unity` and press Play. The "Combat Animation Library" window has:
  - a category filter;
  - Prev / Next with the clip name and "Index n/N";
  - Play / Stop, Loop, Slow 0.5x / Normal 1x;
  - a scrub bar;
  - a switch between the in-place clip and the root-motion clip;
  - `[ Combat Demo ]`, with an optional facing dummy.

  The Gender, Weight and Muscle controls and the camera presets keep working while a clip plays.
- **Review fields.** In the library asset, set `review`, `notes`, `category` (with `categoryReviewed`) and `roles`. Then re-run the importer, which also rebuilds the demo sequence. In the demo, `*` marks an unreviewed auto candidate.

## 4. Tests

Run **Tools > MusicGame > Animations > Run Combat Animation Library Tests** for the edit-mode checks. For edit and play mode in batch:
`-executeMethod MakeHumanBodyTests.RunCombatTestsFromCommandLine`, which writes `Logs/CombatAnimationTests.txt`.

The tests are structural only. Whether a clip looks good is judged on the renders and in the browser.
