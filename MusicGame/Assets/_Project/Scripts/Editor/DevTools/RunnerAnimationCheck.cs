using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One lightweight play-mode check of the data-driven Runner animation: three Runner players
/// (slim male / heavy-muscular male / female — same style data) cycle every style through
/// Locomotion → Fast → Locomotion → Jump → Land → Locomotion → AutoReturn → Locomotion → Flourish
/// → Locomotion, checking states, root motion off, root untouched and the hips staying over the
/// root. The real gameplay AutoReturn signal path (thresholds/grounded gate) is exercised too.
/// Two capture strips: Logs/RunnerAnim_Default.png, Logs/RunnerAnim_Electronic.png.
/// Batch: -executeMethod RunnerAnimationCheck.RunFromCommandLine (no -quit; exits itself).
/// </summary>
[InitializeOnLoad]
public static class RunnerAnimationCheck
{
    private const string PendingKey = "RunnerAnimationCheck.Pending";
    private static readonly StringBuilder Log = new();
    private static int _fails;
    private static float _maxHipsDrift;

    static RunnerAnimationCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            _ = Run();
        };
    }

    public static void RunFromCommandLine()
    {
        EditorSceneManager.OpenScene(MakeHumanBodyBuilder.DebugScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PendingKey, true);
        EditorApplication.isPlaying = true;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        Log.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        if (!ok) _fails++;
    }

    private static async Task Run()
    {
        Log.Clear(); _fails = 0;
        try { await Body(); }
        catch (System.Exception e) { Check("Exception", false, e.ToString()); }
        string report = (_fails == 0 ? "[RunnerAnimationCheck] ALL PASSED\n" : $"[RunnerAnimationCheck] {_fails} FAILED\n") + Log;
        File.WriteAllText("Logs/RunnerAnimationCheck.txt", report);
        Debug.Log(report);
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    private static async Task Wait(float seconds, System.Action perFrame = null)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) { perFrame?.Invoke(); await Task.Yield(); }
    }

    private static async Task<bool> WaitUntil(System.Func<bool> cond, float timeout, System.Action perFrame = null)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (Time.realtimeSinceStartup < end) { perFrame?.Invoke(); if (cond()) return true; await Task.Yield(); }
        return cond();
    }

    private class Runner { public PlayerController pc; public RunnerAvatarAnimator anim; public Animator animator; public Vector3 start; public string label; }

    private static async Task Body()
    {
        foreach (var p in Object.FindObjectsByType<AvatarDebugPreview>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = new Vector3(3f, 1f, 3f);

        var bodies = new[]
        {
            ("slim male",            new BodyMorphValues { Gender = 0f, Weight = 0.05f, Muscle = 0f }),
            ("heavy muscular male",  new BodyMorphValues { Gender = 0f, Weight = 0.95f, Muscle = 0.8f }),
            ("female",               new BodyMorphValues { Gender = 1f, Weight = 0.5f,  Muscle = 0.2f }),
        };
        var runners = new Runner[bodies.Length];
        var avatarField = typeof(PlayerController).GetField("_avatarInstance", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < bodies.Length; i++)
        {
            var go = new GameObject($"Runner_{i}");
            go.transform.position = new Vector3(0f, 0.02f, (i - 1) * 1.4f);
            var pc = go.AddComponent<PlayerController>();
            runners[i] = new Runner { pc = pc, label = bodies[i].Item1 };
        }
        bool built = await WaitUntil(() => runners.All(r => r.pc.GetComponent<RunnerAvatarAnimator>() != null), 30f);
        Check("3 Runner avatars built + RunnerAvatarAnimator bound", built);
        for (int i = 0; i < runners.Length; i++)
        {
            var r = runners[i];
            r.anim = r.pc.GetComponent<RunnerAvatarAnimator>();
            r.animator = r.pc.GetComponentInChildren<Animator>();
            (avatarField.GetValue(r.pc) as AvatarInstance)?.ApplyBody(bodies[i].Item2);
            r.pc.GetComponent<CharacterController>().Move(Vector3.down * 0.1f); // grounded on the floor
            r.start = r.pc.transform.position;
        }
        Check("root motion disabled on every avatar", runners.All(r => !r.animator.applyRootMotion));
        Check("each avatar has its OWN override of the shared RunnerHumanoid controller",
              runners.Select(r => r.animator.runtimeAnimatorController).Distinct().Count() == runners.Length &&
              runners.All(r => r.animator.runtimeAnimatorController is AnimatorOverrideController aoc && aoc.runtimeAnimatorController.name == "RunnerHumanoid"));
        Check("Idle before running", runners.All(r => r.anim.CurrentRole == RunnerAnimationRole.Idle));

        _maxHipsDrift = 0f;
        System.Action track = () =>
        {
            foreach (var r in runners)
            {
                var hips = r.animator.GetBoneTransform(HumanBodyBones.Hips).position;
                var d = new Vector2(hips.x - r.pc.transform.position.x, hips.z - r.pc.transform.position.z).magnitude;
                _maxHipsDrift = Mathf.Max(_maxHipsDrift, d);
            }
        };

        foreach (var r in runners) r.pc.StartRunning();
        string[] styleNames = { "Default", "Salsa", "Electronic", "Classical" };
        for (int s = 0; s < styleNames.Length; s++)
        {
            foreach (var r in runners) r.anim.DebugForceStyle(s);
            string style = runners[0].anim.Style.name;
            Task cap = s == 0 ? Capture("RunnerAnim_Default", 7.5f, 30) : s == 2 ? Capture("RunnerAnim_Electronic", 7.5f, 30) : null;

            bool loco = await WaitUntil(() => runners.All(r => r.anim.CurrentRole == RunnerAnimationRole.Locomotion && IsState(r, "Locomotion_0")), 2f, track);
            Check($"{style}: Locomotion", loco, runners[0].anim.DebugSummary.Split('\n')[0]);

            foreach (var r in runners) r.anim.DebugTrigger(RunnerAnimationRole.FastLocomotion);
            bool fast = await WaitUntil(() => runners.All(r => IsState(r, "FastLocomotion_0")), 2f, track);
            foreach (var r in runners) r.anim.DebugTrigger(RunnerAnimationRole.FastLocomotion);
            bool back = await WaitUntil(() => runners.All(r => IsState(r, "Locomotion_0")), 2f, track);
            Check($"{style}: Locomotion → FastLocomotion → Locomotion", fast && back);

            foreach (var r in runners) r.anim.DebugTrigger(RunnerAnimationRole.Jump);
            bool jump = await WaitUntil(() => runners.All(r => IsState(r, "Jump_0")), 1f, track);
            bool land = await WaitUntil(() => runners.All(r => IsState(r, "Land_0")), 2f, track);
            bool afterLand = await WaitUntil(() => runners.All(r => IsState(r, "Locomotion_0")), 2f, track);
            Check($"{style}: Jump → Land → Locomotion", jump && land && afterLand);

            // Real gameplay signal path (gates included): a big surge return animates, a tiny one doesn't.
            var onReturn = typeof(RunnerAvatarAnimator).GetMethod("OnAutoReturn", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var r in runners) onReturn.Invoke(r.anim, new object[] { RunnerAutoReturnKind.SurgeReturn, 0.1f, 0.3f });
            bool smallIgnored = runners.All(r => r.anim.CurrentRole == RunnerAnimationRole.Locomotion);
            foreach (var r in runners) onReturn.Invoke(r.anim, new object[] { RunnerAutoReturnKind.SurgeReturn, 1f, 0.83f });
            bool ret = await WaitUntil(() => runners.All(r => IsState(r, "AutoReturn_0")), 1f, track);
            bool afterRet = await WaitUntil(() => runners.All(r => IsState(r, "Locomotion_0")), 2.5f, track);
            Check($"{style}: AutoReturn (gameplay signal) → Locomotion; small return ignored", smallIgnored && ret && afterRet);

            foreach (var r in runners) r.anim.DebugTrigger(RunnerAnimationRole.Flourish);
            bool flourishing = await WaitUntil(() => runners.All(r => r.anim.UpperBodyFlourishActive || r.anim.CurrentRole == RunnerAnimationRole.Flourish), 1f, track);
            bool legsRun = runners.All(r => !r.anim.UpperBodyFlourishActive || IsState(r, "Locomotion_0"));
            bool flourishDone = await WaitUntil(() => runners.All(r => !r.anim.UpperBodyFlourishActive && IsState(r, "Locomotion_0")), 7f, track);
            Check($"{style}: Flourish → Locomotion (upper-body flourish keeps the legs in Locomotion)", flourishing && legsRun && flourishDone);

            if (cap != null) await cap;
        }

        Check("roots never moved by the animation", runners.All(r => (r.pc.transform.position - r.start).sqrMagnitude < 1e-6f));
        Check("hips stay over the root (horizontal drift < 0.35 m) on slim/heavy/female", _maxHipsDrift < 0.35f, $"max drift {_maxHipsDrift:0.00} m");
        float feet = runners.Min(r => Mathf.Min(r.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y, r.animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y));
        Check("feet not below the floor", feet > -0.1f, $"lowest foot {feet:0.00}");

        static bool IsState(Runner r, string state) => r.animator.GetCurrentAnimatorStateInfo(0).IsName(state) && !r.animator.IsInTransition(0);
    }

    private static async Task Capture(string file, float seconds, int frames)
    {
        var camGO = new GameObject("CheckCam");
        var cam = camGO.AddComponent<Camera>();
        cam.transform.position = new Vector3(6.5f, 1.1f, 0f);
        cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));
        cam.fieldOfView = 40f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
        cam.enabled = false;
        const int w = 300, h = 360;
        var strip = new Texture2D(w * frames, h, TextureFormat.RGB24, false);
        var rt = new RenderTexture(w, h, 24);
        float start = Time.realtimeSinceStartup;
        for (int i = 0; i < frames; i++)
        {
            while (Time.realtimeSinceStartup < start + seconds * i / frames) await Task.Yield();
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            strip.ReadPixels(new Rect(0, 0, w, h), i * w, 0);
            RenderTexture.active = null;
        }
        strip.Apply();
        File.WriteAllBytes($"Logs/{file}.png", strip.EncodeToPNG());
        cam.targetTexture = null;
        Object.Destroy(rt); Object.Destroy(strip); Object.Destroy(camGO);
    }
}
