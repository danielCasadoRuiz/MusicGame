Meshy_pipeline CLEAN V5

VISIBLE PIPELINE
================
Input
  -> Upscaled
  -> Upscaled_and_Cut
  -> Raw3D
  -> Output

01_GENERAR_RAW3D.bat
--------------------
- Input: ChatGPT 4-view images + optional view_orders.json
- Hidden temporary global crop
- Claude Code local + Playwright MCP operates Leonardo WEB
- Leonardo direct URL comes from _System/config.json
- Pro / Precise / x4 / Detailed
- Browser result is persisted in Upscaled/<asset>/upscaled_full.*
- Then Python cuts semantic views into Upscaled_and_Cut/<asset>/
- Then Meshy API produces Raw3D/<asset>.glb
- File timestamps are the primary stage state.
- If interrupted during Leonardo, V5 first attempts recovery WITHOUT generating.
- It asks before submitting a fresh Leonardo job after an uncertain interruption.

02_RETOPO.bat
-------------
- Watches Raw3D independently
- Uses recovered retopo engine v2.1.2
- Dense QuadriFlow 1,000,000 -> validated 200,000 -> UV/PBR bake/verify
- Publishes verified final GLB to Output

CLAUDE / PLAYWRIGHT SETUP
=========================
No manual Claude MCP command is required in V6.
On first run the producer:
  1. checks `claude mcp list`
  2. if Playwright is missing, automatically runs:
       claude mcp add playwright npx @playwright/mcp@latest
  3. verifies it was registered

Requirement: Node.js/npx must already exist on Windows because Playwright MCP is
distributed through npm. The Playwright browser is downloaded automatically on
first use.

Playwright uses a persistent browser profile by default, so Leonardo login/cookies
can persist. If Leonardo asks for login, complete it in the visible browser window.

view_orders.json
================
Lives in Input.
If absent or an input has no entry:
  front, back, right, left

Duplicate semantic side labels are allowed:
  front, back, right, right
will only send one right view to Meshy.

SECRETS
=======
Only MESHY_API_KEY is required.
_System/secrets.env is reused if it already exists.
Leonardo API is NOT used in V5.


V7 CHANGES
==========
1) Faster Claude repetition
- First Leonardo asset uses the full Playwright discovery prompt.
- Claude JSON session_id is persisted in _System/State/ClaudeBrowser/session.json.
- Following assets use `claude -p --resume <session_id>` with a short "repeat the
  same workflow" prompt and a lower max-turn budget.
- This is session reuse, not ML training. If Leonardo changes its UI Claude can
  still inspect/recover within the resumed session.

2) New visual preprocessing
- The source 1x4 strip is NOT sent directly to Leonardo anymore.
- The four physical views are detected and isolated first.
- Neighbour-safe source territories prevent overlap/leakage.
- Each view gets transparent cell padding.
- Views are packed as:
      0   1
      2   3
- A 96 px transparent central gap separates cells before x4.
- Canvas is padded toward max aspect ratio 1.50 without changing relative scale.
- After Leonardo, splitting is deterministic from known cell coordinates.
  No second "four columns" detection is performed.


V8 CHANGES
==========
- Leonardo input is now RGB on a solid neutral background (238,238,238).
  Transparency is NOT sent to Leonardo.
- The original alpha mask is preserved separately and restored after x4.
- Final Meshy cuts use alpha_dilate_px=0 (previously 6) to avoid exposing
  coloured Leonardo fringe pixels around the silhouette.
- If Claude clicks Download but hits max_turns before saveAs/final response,
  Python scans recent Downloads/Descargas images, validates expected x4 geometry,
  adopts the newest matching Leonardo/UniversalUpscaler result into Upscaled,
  and continues.
- Repeat max turns increased from 32 to 48 as a fallback.


V9 DEBUG ARCHIVE
================
A visible Debug/<asset>/ folder is now preserved for every asset.

Debug/<asset>/
  00_manifest.json
  01_preprocess/
    00_source_snapshot.png
    01_source_foreground_mask.png
    02_detection_overlay.jpg
    03_isolated_view_0..3.png
    04_isolated_mask_0..3.png
    05_leonardo_input_exact.png
    06_leonardo_input_alpha.png
    07_layout.json
  02_claude_leonardo/
    generate/00_prompt.txt
    generate/01_result.txt
    generate/02_run_meta.json
    recovery/... when used
  03_leonardo_output/
    exact browser/download artifacts before normalization
    exact normalized Upscaled copy
  04_cut/
    00_upscaled_as_read.png
    01_scaled_original_alpha.png
    raw Leonardo cell crops
    masks
    cells after alpha
    final padded semantic PNGs
  05_meshy/
    00_request.json
    01_create_response.json
    02_latest_status.json
    03_succeeded_response.json
    task_id.txt
    04_raw3d_exact.glb
  06_retopo/
    command
    retopo log
    state
    exact final GLB

Nothing in Debug is used as the primary pipeline input; it is an audit/debug
archive so deleting Debug never changes the canonical outputs in the five main
pipeline folders.


V10 - FULLY NON-INTERACTIVE PIPELINE
====================================
There are NO yes/no questions during asset processing.

Canonical file-state rules:
1. Input/<asset> exists
2. If valid Upscaled/<asset>/upscaled_full.* exists -> SKIP Leonardo
3. Otherwise:
   - build isolated balanced 2x2 grey-background input
   - run ONE fresh Leonardo upscale attempt in this BAT
   - accept exact requested download OR a recent matching browser download
   - if no valid image exists, discard the Claude session and skip the asset
     until the BAT is restarted; never tight-loop paid generations
4. If cuts already exist and are current -> skip split
5. If Raw3D/<asset>.glb exists and is current -> SKIP Meshy
6. If a known Meshy task_id exists -> resume it automatically
7. If no Meshy task_id exists -> submit a new Meshy job automatically
8. Download Raw3D and continue with the next asset

Claude session reuse:
- A session is saved ONLY after Claude returns SUCCESS AND a real Upscaled file
  has been validated.
- Failed, max-turn, uncertain or rescued sessions are discarded automatically.
- An old v7/v8/v9 session.json is ignored because it lacks validated_success=true.

No recovery dialogue, no confirmation dialogue, no manual per-asset intervention.
Debug/<asset>/ continues preserving every useful intermediate/output.


V14 FINAL
=========
Producer control is filesystem-only: Raw3D > Cuts > Upscaled > Input.
Claude is always fresh (reuse_session=False).
Meshy task ids are diagnostics only; never resumed after restart.
Auto chroma is the normal preprocessing/cutting path.
Retopo control is filesystem-only: Output HIGH GLB.


V16 CONTROLLED FLOW
===================
Root pipeline_steps.json defines ordered authorization.
First false is a hard stop; later stage values are ignored.
Default stops before Meshy.
Physical checkpoints remain the only recovery authority.


V17 MANUAL LEONARDO HANDOFF
===========================
settings.leonardo_mode supports manual|claude.
Default manual. Manual_Leonardo/<asset>/UPLOAD_THIS_TO_LEONARDO.png
is the visible handoff input. Any valid x4 image copied back into that
folder is validated and canonicalized into Upscaled.
No Claude initialization in manual mode.


V18 SIMPLE CHROMA
=================
Post-upscale alpha/mask/chroma-key pipeline removed from normal flow.
Known 2x2 cell rectangles are split directly and chroma is preserved.
Meshy receives RGB PNG views with chroma background intact.


V19 COMPACT 2x2
================
cell_padding 25, gap 50, outer_margin 20.
Known tile boxes are still scaled to actual output dimensions.


V20 RETOPO INDEPENDENT
======================
Removed retopo_high from producer pipeline_steps.
retopo_worker.py no longer reads or references producer control state.
It independently watches Raw3D and publishes missing HIGH outputs.


V21 RETOPO FALLBACK
====================
Primary dense 1M -> 200k.
Fallback fresh original -> geometry auto 200k.
Prints inner retopo.log tail automatically.


V23 RETOPO CONFIG
=================
Root retopo_config.json is independent of producer controls.
Dense and final triangle budgets are loaded per asset.
Defaults: 1,000,000 dense -> 200,000 final.
