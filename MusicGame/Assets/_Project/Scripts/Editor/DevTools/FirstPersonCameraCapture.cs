#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Play Mode verification of the first-person cameras (Runner CameraFollow + Fight FightCameraController)
/// and of the perspective toggle labels. Boots Core.unity like the game (same flow as UIPlaymodeSmokeTests),
/// plays the first playable song, switches the Runner to First then Third Person, then enters the Fight and
/// does the same there. For every view it saves the 3D frame (the real camera stack — never Camera.main) and
/// a UI-only frame (HUD: toggle label, timer, rival portrait crop), and logs camera height vs the avatar's
/// Head bone / eye point, whether the own head is collapsed and the body still visible, and the toggle label.
/// Batch (with graphics, NOT -nographics):
///   -executeMethod FirstPersonCameraCapture.RunFromCommandLine -fpCaptureOut "C:/path/dir"
/// Writes &lt;dir&gt;/report.txt (RESULT PASS/FAIL, exit code 0/1) and &lt;dir&gt;/NN_*.png. Boots the real
/// AppBootstrap, so it reads/writes the real save file: back it up before running. The camera-view
/// PlayerPrefs it toggles are restored at the end.
/// </summary>
[InitializeOnLoad]
public static class FirstPersonCameraCapture
{
    private const string KeyActive = "FPCapture.Active";
    private const string KeyOut = "FPCapture.Out";
    private const string KeyStep = "FPCapture.Step";
    private const string KeyNext = "FPCapture.Next";
    private const string KeyLog = "FPCapture.Log";
    private const string KeyFails = "FPCapture.Fails";
    private const string KeyWaitStart = "FPCapture.WaitStart";
    private const string KeyRunnerPref = "FPCapture.RunnerPref";
    private const string KeyFightPref = "FPCapture.FightPref";

    private const string RunnerPrefsKey = "MusicGame.CameraViewMode";   // CameraFollow
    private const string FightPrefsKey = "MusicGame.FightCameraView";   // FightCameraController

    // Camera must sit at the eye point (smoothing lag tolerated) and never above the top of the head.

    private static bool s_hooked;

    private static readonly (float wait, string name, Action act)[] Steps =
    {
        (12f, null, null),
        (0f, null, () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (3f, null, () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (3f, null, () => FirstPlayableRow().button.onClick.Invoke()),
        (1f, null, () => Find<SongSelectionView>().playButton.onClick.Invoke()),
        (30f, null, () => CameraFollow.Instance.SetView(CameraViewMode.FirstPerson)),
        // Runner: advancing, then the 360 AutoReturn spin (the view must turn WITH the head).
        (2.5f, "01_runner_fp_advance", () => { CheckRunner(CameraViewMode.FirstPerson); BodyInView("Runner advance", RunnerCam(), CameraFollow.Instance.HeadRig, false); }),
        // Crouch: the eyes (and the view) go down with the crouched run.
        (0f, null, () => { s_standCamY = RunnerCamHeight(); TouchInputState.CrouchHeld = true; }),
        (0.9f, "01b_runner_fp_crouch", () => { float d = s_standCamY - RunnerCamHeight(); Expect(d > 0.2f, $"Runner crouch: the first-person view goes down with the eyes ({d:0.00} m)"); TouchInputState.CrouchHeld = false; }),
        // Jump: the side-on jump clip must not tilt/turn the view beyond the limits.
        (1f, null, () => { s_maxRoll = 0f; s_maxYaw = 0f; TouchInputState.JumpRequested = true; }),
        (0.12f, null, TrackJump), (0.12f, null, TrackJump), (0.12f, "01c_runner_fp_jump", TrackJump), (0.12f, null, TrackJump), (0.12f, null, TrackJump),
        (0.12f, null, () => { TrackJump(); Expect(s_maxRoll <= 8.5f && s_maxYaw <= 25.5f, $"Runner jump: view roll ≤ 8° and yaw ≤ 25° from the heading (max roll {s_maxRoll:0.0}°, yaw {s_maxYaw:0.0}°)"); }),
        (1f, null, () => { s_spinMaxYaw = 0f; s_spinAccum = 0f; s_spinPrev = 0f; s_horizonYaw0 = HorizonYaw(); s_horizonMax = 0f; Find<RunnerAvatarAnimator>().DebugTrigger(RunnerAnimationRole.AutoReturn); }),
        (0.2f, "02_runner_fp_spin_a", TrackSpin),
        (0.2f, "02_runner_fp_spin_b", TrackSpin),
        (0.2f, "02_runner_fp_spin_c", TrackSpin),
        (0.25f, null, () => { TrackSpin(); Expect(s_spinMaxYaw > 200f, $"Runner: the view turns with the 360 spin (accumulated turn {s_spinMaxYaw:0} deg)");
                              Expect(s_horizonMax > 80f, $"Runner: the background (Horizon camera) turns with the spin too (max {s_horizonMax:0} deg)"); }),
        (0.5f, null, () => CameraFollow.Instance.SetView(CameraViewMode.ThirdPerson)),
        (2f, "03_runner_third_person", () => CheckRunner(CameraViewMode.ThirdPerson)),
        (0f, null, () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (4f, null, () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (3f, null, () => AppFlowController.Instance.RequestState(GameFlowState.Fight)),
        (1f, WaitFighting, null),
        (0f, null, () => { FreezeRival(); SetFightView(CameraViewMode.FirstPerson); }),
        (2f, "04_fight_fp_idle", () => { CheckFight(CameraViewMode.FirstPerson); BodyInView("Fight idle", FightCameraController.Instance.Camera, FightCameraController.Instance.PlayerHeadRig, false); }),
        // Advance (W): the lens must stay ON the eyes while moving — no lag, never the own head from behind.
        (0f, null, () => FightTouchInputState.Vertical = 1f),
        (0.35f, null, CheckFightGlued),
        (0.3f, "05_fight_fp_advance", () => { CheckFightGlued(); FightTouchInputState.Vertical = 0f; }),
        (0.4f, null, () => FightTouchInputState.Horizontal = 1f),
        (0.6f, "06_fight_fp_sidestep", () => { CheckFightGlued(); FightTouchInputState.Horizontal = 0f; }),
        (0.4f, null, () => FightTouchInputState.PunchRequested = true),
        (0.18f, "07_fight_fp_punch", () => { CheckFightGlued(); BodyInView("Fight punch", FightCameraController.Instance.Camera, FightCameraController.Instance.PlayerHeadRig, false); }),
        (0.6f, null, () => FightTouchInputState.Vertical = -1f),
        (0.5f, "08_fight_fp_block", () => { CheckFightGlued(); LogGuard(); BodyInView("Fight block", FightCameraController.Instance.Camera, FightCameraController.Instance.PlayerHeadRig, false); }),
        (0f, null, () => FightTouchInputState.Vertical = 0f),
        // Fall: the view goes down WITH the head (from the floor), the lens still at the eyes.
        (0.6f, null, () => { s_minFallEyeY = float.MaxValue; KnockPlayerDown(); }),
        (0.4f, "09_fight_fp_fall_a", TrackFall),
        (0.5f, "09_fight_fp_fall_b", TrackFall),
        (0.5f, "09_fight_fp_fall_c", () => { TrackFall(); Expect(s_minFallEyeY < 0.9f, $"Fight fall: the view goes down with the head (lowest camera height {s_minFallEyeY:0.00} m)"); }),
        (2.5f, null, () => SetFightView(CameraViewMode.ThirdPerson)),
        (2f, "10_fight_third_person", () => CheckFight(CameraViewMode.ThirdPerson)),
        (0f, null, () => AppFlowController.Instance.RequestState(GameFlowState.MainMenu)),
        (3f, null, null),
    };

    private static float s_spinAccum, s_spinPrev;
    private static float s_spinMaxYaw, s_minFallEyeY, s_standCamY, s_maxRoll, s_maxYaw, s_horizonYaw0, s_horizonMax;

    private static float RunnerCamHeight()
    {
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        return CameraFollow.Instance.transform.position.y - (player != null ? player.transform.position.y : 0f);
    }

    private static float HorizonYaw()
    {
        var h = HorizonCameraController.Instance;
        return h != null && h.HorizonCamera != null ? h.HorizonCamera.transform.eulerAngles.y : 0f;
    }

    private static void TrackJump()
    {
        var follow = CameraFollow.Instance;
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (follow == null || player == null) return;
        Vector3 e = (Quaternion.Inverse(player.transform.rotation) * follow.transform.rotation).eulerAngles;
        float roll = Mathf.Abs(Mathf.DeltaAngle(0f, e.z)), yaw = Mathf.Abs(Mathf.DeltaAngle(0f, e.y));
        s_maxRoll = Mathf.Max(s_maxRoll, roll); s_maxYaw = Mathf.Max(s_maxYaw, yaw);
        Log($"     jump: roll {roll:0.0}°, yaw {yaw:0.0}°, airborne {!player.GetComponent<CharacterController>().isGrounded}");
    }

    private static Camera RunnerCam() => CameraFollow.Instance.GetComponent<Camera>();

    private static void TrackSpin()
    {
        var follow = CameraFollow.Instance;
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (follow == null || player == null) return;
        Vector3 cam = follow.transform.forward; cam.y = 0f;
        Vector3 heading = player.transform.forward; heading.y = 0f;
        float yaw = Vector3.Angle(cam, heading);
        float signedYaw = Vector3.SignedAngle(heading, cam, Vector3.up);
        s_spinAccum += Mathf.DeltaAngle(s_spinPrev, signedYaw); // unwrapped: a full turn adds up to ~360
        s_spinPrev = signedYaw;
        s_spinMaxYaw = Mathf.Max(s_spinMaxYaw, Mathf.Abs(s_spinAccum));
        s_horizonMax = Mathf.Max(s_horizonMax, Mathf.Abs(Mathf.DeltaAngle(s_horizonYaw0, HorizonYaw())));
        Log($"     spin: camera yaw from the heading {yaw:0} deg, lens on the pivot {Vector3.Distance(follow.transform.position, follow.HeadRig.EyePosition):0.000} m");
    }

    private static void KnockPlayerDown()
    {
        var boot = FightSceneBootstrap.Instance;
        if (boot != null && boot.PlayerActor != null && boot.PlayerActor.HitReaction != null) boot.PlayerActor.HitReaction.ApplyKnockdown(1.2f);
        else Expect(false, "Fight: player hit reaction available for the fall test");
    }

    private static void TrackFall()
    {
        var cam = FightCameraController.Instance;
        if (cam == null) return;
        float y = cam.transform.position.y - cam.PlayerHeadRig.FeetY;
        s_minFallEyeY = Mathf.Min(s_minFallEyeY, y);
        CheckFightGlued();
        Log($"     fall: camera height {y:0.00} m, forward {Fmt(cam.transform.forward)}");
    }

    /// <summary>First person in the fight: the lens is ON the eye pivot (no positional lag at all).</summary>
    private static void CheckFightGlued()
    {
        var cam = FightCameraController.Instance;
        if (cam == null || !cam.PlayerHeadRig.IsValid) { Expect(false, "Fight: first-person rig resolved"); return; }
        float d = Vector3.Distance(cam.transform.position, cam.PlayerHeadRig.EyePosition);
        Expect(d < 0.01f, $"Fight: lens glued to the eye pivot while moving ({d:0.000} m)");
    }

    /// <summary>Step name meaning "repeat this step every `wait` s until the fight is in Fighting (max 90 s)".</summary>
    private const string WaitFighting = "__wait_fighting";

    /// <summary>The rival stands still so the shots are taken in a neutral fighting pose (no knockdown).</summary>
    private static void FreezeRival()
    {
        var boot = FightSceneBootstrap.Instance;
        if (boot == null || boot.OpponentActor == null) return;
        foreach (var ai in boot.OpponentActor.GetComponentsInChildren<FighterAI>(true)) ai.enabled = false;
    }

    private static void LogGuard()
    {
        var boot = FightSceneBootstrap.Instance;
        var guard = boot != null && boot.PlayerActor != null ? boot.PlayerActor.Guard : null;
        Expect(guard != null && guard.State != FighterGuardState.None, $"Fight: player guard raised while holding back (state {(guard != null ? guard.State.ToString() : "?")})");
    }

    /// <summary>Own body renderers enabled (nothing hidden) and which arm/body bones are inside the first-person
    /// view. `required`: at least one must be in view (a punch reaches forward); otherwise logged only.</summary>
    private static void BodyInView(string who, Camera camera, FirstPersonHeadRig rig, bool required)
    {
        if (camera == null || rig == null || !rig.IsValid) { Expect(false, $"{who}: camera + player rig resolved"); return; }
        var anim = rig.Animator;
        var renderers = anim.GetComponentsInChildren<SkinnedMeshRenderer>();
        Expect(renderers.Length > 0 && renderers.All(r => r.enabled), $"{who}: whole player body rendered, head included ({renderers.Count(r => r.enabled)}/{renderers.Length})");
        Expect(rig.Head.lossyScale.sqrMagnitude > 0.01f, $"{who}: own head not collapsed (scale {rig.Head.lossyScale.x:0.00})");
        var seen = new System.Collections.Generic.List<string>();
        foreach (var b in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.Chest })
        {
            var t = anim.GetBoneTransform(b);
            if (t == null) continue;
            var v = camera.WorldToViewportPoint(t.position);
            if (v.z > camera.nearClipPlane && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f) seen.Add(b.ToString());
        }
        Log($"     {who}: fov {camera.fieldOfView:0}, near {camera.nearClipPlane:0.000}, own body in view: {(seen.Count > 0 ? string.Join(", ", seen) : "none")}");
        if (required) Expect(seen.Count > 0, $"{who}: own arm in view");
    }

    static FirstPersonCameraCapture()
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        Hook();
        EditorApplication.update += Tick;
    }

    public static void RunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpCaptureOut");
        string outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Logs/FirstPersonCameraCapture");
        Directory.CreateDirectory(outDir);
        foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
        File.WriteAllText(Path.Combine(outDir, "log.txt"), "");
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outDir);
        SessionState.SetInt(KeyStep, -1);
        SessionState.SetString(KeyLog, "");
        SessionState.SetInt(KeyFails, 0);
        SessionState.SetInt(KeyRunnerPref, PlayerPrefs.GetInt(RunnerPrefsKey, 0));
        SessionState.SetInt(KeyFightPref, PlayerPrefs.GetInt(FightPrefsKey, 0));
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        if (s_hooked) return;
        s_hooked = true;
        Application.logMessageReceived += (msg, stack, type) =>
            File.AppendAllText(Path.Combine(SessionState.GetString(KeyOut, "."), "log.txt"), $"[{type}] {msg}\n");
    }

    private static void Log(string line) => SessionState.SetString(KeyLog, SessionState.GetString(KeyLog, "") + line + "\n");

    private static void Expect(bool ok, string what)
    {
        Log($"  [{(ok ? "ok" : "FAIL")}] {what}");
        if (!ok) SessionState.SetInt(KeyFails, SessionState.GetInt(KeyFails, 0) + 1);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        Hook();
        double now = EditorApplication.timeSinceStartup;
        int step = SessionState.GetInt(KeyStep, -1);
        if (step == -1) { Schedule(0, now); return; }
        if (now < double.Parse(SessionState.GetString(KeyNext, "0"), CultureInfo.InvariantCulture)) return;

        if (step >= Steps.Length) { Finish(); return; }
        try
        {
            var s = Steps[step];
            if (s.name == WaitFighting)
            {
                var flow = FightFlowController.Instance;
                bool fighting = flow != null && flow.CurrentState == FightFlowState.Fighting;
                double started = double.Parse(SessionState.GetString(KeyWaitStart, "0"), CultureInfo.InvariantCulture);
                if (started <= 0) SessionState.SetString(KeyWaitStart, now.ToString(CultureInfo.InvariantCulture));
                if (!fighting && (started <= 0 || now - started < 90))
                {
                    if (flow != null && flow.CurrentState == FightFlowState.RoundIntro) FightTouchInputState.Vertical = 0f;
                    SessionState.SetString(KeyNext, (now + 0.5).ToString(CultureInfo.InvariantCulture));
                    return;
                }
                SessionState.SetString(KeyWaitStart, "0");
                Log($"── fight state reached: {(flow != null ? flow.CurrentState.ToString() : "?")} after {(started > 0 ? now - started : 0):F1} s");
                Schedule(step + 1, now);
                return;
            }
            if (s.name != null) Log($"── {s.name}");
            s.act?.Invoke();
            if (s.name != null) { Capture3D(s.name); CaptureUI(s.name); }
        }
        catch (Exception e) { Expect(false, $"step {step} threw: {e}"); }
        Schedule(step + 1, now);
    }

    private static void Schedule(int step, double now)
    {
        float wait = step < Steps.Length ? Steps[step].wait : 1f;
        SessionState.SetInt(KeyStep, step);
        SessionState.SetString(KeyNext, (now + wait).ToString(CultureInfo.InvariantCulture));
    }

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    private static SongRowView FirstPlayableRow() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None)
            .OrderBy(r => r.transform.GetSiblingIndex())
            .First(r => r.button.interactable && !r.Played);

    // ── Checks ────────────────────────────────────────────────────────────────

    private static void CheckRunner(CameraViewMode mode)
    {
        var follow = CameraFollow.Instance;
        Expect(follow != null && follow.ViewMode == mode, $"Runner view mode = {mode}");
        if (follow == null) return;
        var rig = follow.HeadRig;
        float camY = follow.transform.position.y;
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        if (mode == CameraViewMode.FirstPerson)
        {
            CheckEyes("Runner", follow.transform.position, rig, rig.IsValid ? rig.EyePosition : Vector3.zero);
            if (rig.IsValid)
                Expect(Quaternion.Angle(follow.transform.rotation, rig.Pivot.rotation) < 30f,
                       $"Runner: view follows the head pivot's rotation (roll/yaw limited: {Quaternion.Angle(follow.transform.rotation, rig.Pivot.rotation):0.0} deg off)");
        }

        if (player != null)
        {
            int visible = player.VisualRenderers.Count(r => r != null && r.enabled);
            Expect(visible > 0, $"Runner {mode}: player body renderers enabled {visible}/{player.VisualRenderers.Length} (camY {camY:F3}, player feet Y {player.transform.position.y:F3})");
        }
        var pause = Find<PauseView>();
        string label = pause != null && pause.cameraToggleLabel != null ? pause.cameraToggleLabel.text : "(no label)";
        string expected = Loc.Get(mode == CameraViewMode.ThirdPerson ? "Pause.FirstPerson" : "Pause.ThirdPerson");
        Expect(label == expected, $"Runner toggle label in {mode} = '{label}' (expected '{expected}', the perspective it switches TO)");
    }

    private static void SetFightView(CameraViewMode mode)
    {
        var cam = FightCameraController.Instance;
        if (cam != null && cam.ViewMode != mode) cam.ToggleView();
    }

    private static void CheckFight(CameraViewMode mode)
    {
        var cam = FightCameraController.Instance;
        Expect(cam != null && cam.ViewMode == mode, $"Fight view mode = {mode} (flow state {(FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState.ToString() : "?")})");
        if (cam == null) return;
        var rig = cam.PlayerHeadRig;
        if (mode == CameraViewMode.FirstPerson)
        {
            CheckEyes("Fight", cam.transform.position, rig, rig.IsValid ? rig.EyePosition : Vector3.zero);
            Log($"     eye target {Fmt(cam.FirstPersonEyeTarget)}  camera fwd {Fmt(cam.transform.forward)}  near clip {cam.Camera.nearClipPlane:F3}");
        }


        var hud = Find<FightHudView>();
        string label = hud != null && hud.cameraToggleLabel != null ? hud.cameraToggleLabel.text : "(no label)";
        string expected = Loc.Get(mode == CameraViewMode.ThirdPerson ? "Pause.FirstPerson" : "Pause.ThirdPerson");
        Expect(label == expected, $"Fight toggle label in {mode} = '{label}' (expected '{expected}')");
        string timer = hud != null && hud.timerText != null ? hud.timerText.text : "(no timer)";
        Expect(timer.TrimEnd().EndsWith(" s") && !timer.Contains(":"), $"Fight timer text = '{timer}' (seconds, e.g. '45 s')");
        if (hud != null && hud.opponentPortraitCrop != null && hud.opponentPortrait != null)
        {
            var rt = hud.opponentPortrait.rectTransform;
            Log($"     rival portrait: sprite '{(hud.opponentPortrait.sprite != null ? hud.opponentPortrait.sprite.name : "none")}' inner size {rt.sizeDelta} pos {rt.anchoredPosition} slot {((RectTransform)hud.opponentPortraitCrop.transform).rect.size}");
        }
        else Expect(false, "FightHud has PortraitCrop slots (regenerate the UI prefabs)");
    }

    private static void CheckEyes(string who, Vector3 camPos, FirstPersonHeadRig rig, Vector3 eye)
    {
        Expect(rig.IsValid, $"{who}: player avatar Humanoid Head bone + eye pivot resolved");
        if (!rig.IsValid) return;
        float headY = rig.Head.position.y, feetY = rig.FeetY;
        Vector3 face = rig.Pivot.forward;
        float lensAhead = Vector3.Dot(camPos - rig.Head.position, face);
        Log($"     camera {Fmt(camPos)}  pivot {Fmt(eye)}  head bone Y {headY:F3}  feet Y {feetY:F3}  " +
            $"cam height above feet {camPos.y - feetY:F3}  lens ahead of the head pivot {lensAhead:F3} m  eye bones {rig.UsesEyeBones}");
        Expect(Vector3.Distance(camPos, eye) < 0.01f, $"{who}: lens exactly on the eye pivot ({Vector3.Distance(camPos, eye):F3} m)");
        // The eyes are ~6 cm above the head pivot with the head level; a pitched head (boxing stance, run lean)
        // brings them level with it — anything within ±0.12 m of the head pivot is "at the eyes".
        Expect(Mathf.Abs(camPos.y - headY) < 0.12f, $"{who}: lens at eye height (head pivot {camPos.y - headY:+0.000;-0.000} m)");
        Expect(lensAhead > 0.11f, $"{who}: lens in front of the face, never inside the head ({lensAhead:F3} m ahead of the head pivot)");
    }

    private static string Fmt(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    // ── Capture ───────────────────────────────────────────────────────────────

    /// <summary>The live gameplay camera, rendered through its URP stack: an Overlay camera (Runner's
    /// CameraFollow under "[Horizon Camera]") is rendered via the Base camera that stacks it.</summary>
    private static void Capture3D(string name)
    {
        Camera cam = CameraFollow.Instance != null && CameraFollow.Instance.isActiveAndEnabled ? CameraFollow.Instance.GetComponent<Camera>()
                   : FightCameraController.Instance != null ? FightCameraController.Instance.Camera : null;
        if (cam == null) { Expect(false, $"{name}: no gameplay camera to capture"); return; }
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null && data.renderType == CameraRenderType.Overlay)
        {
            var baseCam = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.GetUniversalAdditionalCameraData() is { renderType: CameraRenderType.Base } d && d.cameraStack.Contains(cam));
            if (baseCam != null) cam = baseCam;
        }
        RenderToPng(cam, name + "_3d");
    }

    /// <summary>UI only (screen-space overlay canvases re-targeted to a temporary camera), like UIPlaymodeSmokeTests.</summary>
    private static void CaptureUI(string name)
    {
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var camGo = new GameObject("[FPCaptureUICamera]");
        camGo.transform.position = new Vector3(0f, -5000f, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.2f, 0.24f);
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 10f; }
        Canvas.ForceUpdateCanvases();
        RenderToPng(cam, name + "_ui");
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
        UnityEngine.Object.DestroyImmediate(camGo);
    }

    private static void RenderToPng(Camera cam, string file)
    {
        const int w = 1920, h = 1080;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
        File.WriteAllBytes(Path.Combine(SessionState.GetString(KeyOut, "."), file + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
    }

    private static void Finish()
    {
        SessionState.SetBool(KeyActive, false);
        EditorApplication.update -= Tick;
        PlayerPrefs.SetInt(RunnerPrefsKey, SessionState.GetInt(KeyRunnerPref, 0));
        PlayerPrefs.SetInt(FightPrefsKey, SessionState.GetInt(KeyFightPref, 0));
        PlayerPrefs.Save();
        int fails = SessionState.GetInt(KeyFails, 0);
        string report = $"[FirstPersonCameraCapture] failures: {fails}\n{SessionState.GetString(KeyLog, "")}\nRESULT {(fails == 0 ? "PASS" : "FAIL")}\n";
        File.WriteAllText(Path.Combine(SessionState.GetString(KeyOut, "."), "report.txt"), report, Encoding.UTF8);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
#endif
