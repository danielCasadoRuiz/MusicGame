using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Minimal IMGUI body editor for the avatars an AvatarDebugPreview built — Gender / Weight / Muscle,
/// live. It edits the REAL runtime avatar through AvatarInstance.ApplyBody (the same path
/// AvatarFactory uses at build time) and AvatarBodyTransition (the same path gameplay will use for
/// the transformation), never a debug-only deformation.
///
/// Gender is a continuous 0 (Male) .. 1 (Female) slider; the Male/Female buttons animate it over
/// `genderTransitionDuration` (0 = snap). Weight is shown as Thin (-1) .. Base (0) .. Fat (+1) and
/// mapped onto BodyMorphValues' own 0..1 convention (0.5 = base). The morph weights shown are read
/// back from the actual SkinnedMeshRenderer, so what's displayed is what's rendered.
///
/// POSE TESTS drive the avatars through Unity Humanoid muscles only (AvatarPoseTests) — the Avatar
/// calibration on its own, with no animation clip involved.
///
/// ANIMATION plays the rig-validation clips (Idle / Walk / Run, AvatarDebugPreview.SetDebugAnimation)
/// on the SAME avatars — never rebuilt. "Ping-pong Gender" keeps morphing Male <-> Female while any of
/// them plays. Camera presets frame the selected avatar (full body / upper body / hands / feet).
///
/// COMBAT ANIMATION LIBRARY (second window) browses AvatarDebugPreview.combatLibrary, the imported
/// Rokoko combat mocap: category filter, Previous / Next, Play / Stop, Loop, Slow 0.5x / Normal 1x,
/// scrubbing, in-place vs source-root-motion clip, and the [ Combat Demo ] sequence (optionally with the
/// second avatar as a facing dummy that reacts). Playback goes through AvatarDebugPreview.PlayClip, so
/// the body controls above keep working on the playing avatars.
///
/// "Stress test" hammers random Gender/Weight/Muscle changes, returns to the current settings and
/// checks the rendered body is bit-identical to before (the determinism guarantee).
/// </summary>
public class AvatarBodyDebugPanel : MonoBehaviour
{
    public AvatarDebugPreview preview;
    public Rect area = new Rect(10, 10, 360, 780);
    [Tooltip("Male/Female buttons animate Gender over this many seconds (0 = snap).")]
    public float genderTransitionDuration = AvatarBodyTransition.DefaultDuration;
    public Rect libraryArea = new Rect(380, 10, 420, 700);

    private int _selected;
    private bool _pingPongGender;
    private string _lastStressResult = "";
    private readonly StringBuilder _sb = new();

    // Combat animation library browser state.
    private int _categoryIndex;          // 0 = All, else index into _presentCategories + 1
    private int _entryIndex;
    private bool _libraryLoop = true;
    private float _librarySpeed = 1f;
    private bool _useInPlaceClip = true;
    private bool _playOnSelectedOnly;
    private bool _hideRejected;
    private bool _demoFacing = true;
    private Coroutine _demo;
    private string _demoStatus = "";
    private Vector2 _libraryScroll;
    private readonly List<CombatAnimationCategory> _presentCategories = new();

    private static readonly MorphChannel[] ShownChannels =
    {
        MorphChannel.Gender,
        MorphChannel.MaleSlim, MorphChannel.MaleHeavy, MorphChannel.MaleMuscle,
        MorphChannel.FemaleSlim, MorphChannel.FemaleHeavy, MorphChannel.FemaleMuscle,
    };

    private AvatarInstance Selected
    {
        get
        {
            if (preview == null || preview.Instances.Count == 0) return null;
            _selected = Mathf.Clamp(_selected, 0, preview.Instances.Count - 1);
            var instance = preview.Instances[_selected];
            return instance != null && instance.Root != null ? instance : null;
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(area, "Avatar Body Debug", GUI.skin.window);

        if (preview == null) { GUILayout.Label("No AvatarDebugPreview assigned."); GUILayout.EndArea(); return; }
        if (preview.IsBuilding) GUILayout.Label("Building...");

        if (preview.Instances.Count > 1)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < preview.Instances.Count; i++)
                if (GUILayout.Toggle(_selected == i, $"Avatar {i}", GUI.skin.button)) _selected = i;
            GUILayout.EndHorizontal();
        }

        var instance = Selected;
        if (instance == null) { GUILayout.Label("No built avatar (press Play / Build)."); GUILayout.EndArea(); return; }

        var body = instance.BodyMorphValues;
        var edited = body;
        var transition = instance.Root.GetComponent<AvatarBodyTransition>();
        bool transitioning = transition != null && transition.IsRunning;

        GUILayout.Label($"Gender  (Male 0 .. Female 1): {body.Gender:0.00}{(transitioning ? "  (animating)" : "")}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Male"))   AvatarBodyTransition.StartGender(instance, 0f, genderTransitionDuration);
        if (GUILayout.Button("Female")) AvatarBodyTransition.StartGender(instance, 1f, genderTransitionDuration);
        GUILayout.EndHorizontal();
        float gender = GUILayout.HorizontalSlider(body.Gender, 0f, 1f);
        if (!Mathf.Approximately(gender, body.Gender))
        {
            if (transitioning) transition.Stop(); // the slider takes over
            edited.Gender = gender;
        }
        GUILayout.Label("Transition duration (s)");
        genderTransitionDuration = GUILayout.HorizontalSlider(genderTransitionDuration, 0f, 3f);

        float signedWeight = body.Weight * 2f - 1f;
        GUILayout.Label($"Weight  (Thin -1 .. Base 0 .. Fat +1): {signedWeight:+0.00;-0.00;0.00}");
        signedWeight = GUILayout.HorizontalSlider(signedWeight, -1f, 1f);
        edited.Weight = (signedWeight + 1f) * 0.5f;

        GUILayout.Label($"Muscle  (Base 0 .. Strong 1): {body.Muscle:0.00}");
        edited.Muscle = GUILayout.HorizontalSlider(body.Muscle, 0f, 1f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Neutral")) { edited.Weight = 0.5f; edited.Muscle = 0f; }
        if (GUILayout.Button("Thin"))    { edited.Weight = 0f;   edited.Muscle = 0f; }
        if (GUILayout.Button("Fat"))     { edited.Weight = 1f;   edited.Muscle = 0f; }
        if (GUILayout.Button("Strong"))  { edited.Weight = 0.5f; edited.Muscle = 1f; }
        GUILayout.EndHorizontal();

        if (!edited.Equals(body)) instance.ApplyBody(edited);

        GUILayout.Label("ANIMATION");
        GUILayout.BeginHorizontal();
        foreach (DebugAnimation animation in System.Enum.GetValues(typeof(DebugAnimation)))
            if (GUILayout.Toggle(preview.CurrentAnimation == animation, animation.ToString(), GUI.skin.button) && preview.CurrentAnimation != animation)
                preview.SetDebugAnimation(animation);
        GUILayout.EndHorizontal();
        _pingPongGender = GUILayout.Toggle(_pingPongGender, "Ping-pong Gender (all avatars, continuous)");

        GUILayout.Label("POSE TESTS (Humanoid muscles, no clip)");
        GUILayout.BeginHorizontal();
        int column = 0;
        foreach (AvatarTestPose pose in System.Enum.GetValues(typeof(AvatarTestPose)))
        {
            if (GUILayout.Button(pose.ToString()))
            {
                preview.SetDebugAnimation(DebugAnimation.None); // a playing controller would overwrite the pose
                foreach (var avatar in preview.Instances) AvatarPoseTests.Apply(avatar, pose);
            }
            if (++column % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("CAMERA");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Full Body"))  Frame(instance, new Vector3(0f, 0.95f, 0f), 3.4f);
        if (GUILayout.Button("Upper Body")) Frame(instance, new Vector3(0f, 1.35f, 0f), 1.6f);
        if (GUILayout.Button("Hands"))      Frame(instance, new Vector3(0f, 0.95f, 0f), 1.3f);
        if (GUILayout.Button("Feet"))       Frame(instance, new Vector3(0f, 0.15f, 0f), 1.2f);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Stress test (500 random changes)")) _lastStressResult = RunStressTest(instance);
        if (!string.IsNullOrEmpty(_lastStressResult)) GUILayout.Label(_lastStressResult);

        GUILayout.Label(DescribeWeights(instance));
        GUILayout.EndArea();

        DrawLibrary(instance);
    }

    // ── Combat animation library browser ─────────────────────────────────────────

    private void DrawLibrary(AvatarInstance selected)
    {
        GUILayout.BeginArea(libraryArea, "Combat Animation Library", GUI.skin.window);
        var library = preview.combatLibrary;
        if (library == null)
        {
            GUILayout.Label("No CombatAnimationLibrarySO on the AvatarDebugPreview.\nRun Tools > MusicGame > Animations > Import Rokoko Combat Packs.");
            GUILayout.EndArea();
            return;
        }
        if (library.entries.Count == 0)
        {
            GUILayout.Label("Library is empty. Drop the official Rokoko FBX packs into\nAssets/_Project/Animations/Source/Rokoko/<Pack>/ and run\nTools > MusicGame > Animations > Import Rokoko Combat Packs.");
            GUILayout.EndArea();
            return;
        }

        // Category filter (only categories that have clips).
        _presentCategories.Clear();
        foreach (CombatAnimationCategory c in System.Enum.GetValues(typeof(CombatAnimationCategory)))
            if (library.Filter(c, !_hideRejected).Count > 0) _presentCategories.Add(c);
        var labels = new string[_presentCategories.Count + 1];
        labels[0] = $"All ({library.Filter(null, !_hideRejected).Count})";
        for (int i = 0; i < _presentCategories.Count; i++)
            labels[i + 1] = $"{_presentCategories[i]} ({library.Filter(_presentCategories[i], !_hideRejected).Count})";
        _categoryIndex = Mathf.Clamp(_categoryIndex, 0, labels.Length - 1);
        int category = GUILayout.SelectionGrid(_categoryIndex, labels, 3);
        if (category != _categoryIndex) { _categoryIndex = category; _entryIndex = 0; }
        _hideRejected = GUILayout.Toggle(_hideRejected, "Hide rejected clips");

        var list = library.Filter(_categoryIndex == 0 ? null : _presentCategories[_categoryIndex - 1], !_hideRejected);
        if (list.Count == 0) { GUILayout.Label("No clips in this category."); GUILayout.EndArea(); return; }
        _entryIndex = Mathf.Clamp(_entryIndex, 0, list.Count - 1);

        GUILayout.Label($"Index {_entryIndex + 1}/{list.Count}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("< Prev", GUILayout.Width(70))) { _entryIndex = (_entryIndex + list.Count - 1) % list.Count; PlayEntry(list[_entryIndex]); }
        GUILayout.Label(list[_entryIndex].name, GUI.skin.box, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Next >", GUILayout.Width(70))) { _entryIndex = (_entryIndex + 1) % list.Count; PlayEntry(list[_entryIndex]); }
        GUILayout.EndHorizontal();
        var entry = list[_entryIndex];

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Play")) PlayEntry(entry);
        if (GUILayout.Button("Stop")) { StopDemo(); preview.StopClips(); }
        bool loop = GUILayout.Toggle(_libraryLoop, "Loop", GUI.skin.button);
        if (loop != _libraryLoop) { _libraryLoop = loop; ForEachPlayer(p => p.Loop = loop); }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(Mathf.Approximately(_librarySpeed, 0.5f), "Slow 0.5x", GUI.skin.button)) SetSpeed(0.5f);
        if (GUILayout.Toggle(Mathf.Approximately(_librarySpeed, 1f), "Normal 1x", GUI.skin.button)) SetSpeed(1f);
        GUILayout.EndHorizontal();

        var player = preview.PlayerOf(selected) ?? preview.PlayerOf(preview.Instances[0]);
        if (player != null && player.Clip != null)
        {
            GUILayout.Label($"{player.Clip.name}  {player.Time:0.00} / {player.Length:0.00} s{(player.Paused ? "  (paused)" : "")}");
            float scrub = GUILayout.HorizontalSlider(player.Time, 0f, player.Length);
            if (Mathf.Abs(scrub - player.Time) > 1e-3f) ForEachPlayer(p => { p.Paused = true; p.Seek(scrub); });
            if (player.Paused && GUILayout.Button("Resume")) ForEachPlayer(p => p.Paused = false);
        }

        bool inPlace = GUILayout.Toggle(_useInPlaceClip, _useInPlaceClip
            ? "In-place debug clip (grounded, faces +Z)"
            : "Source root-motion clip (root motion NOT applied)");
        if (inPlace != _useInPlaceClip) { _useInPlaceClip = inPlace; PlayEntry(entry); }
        if (preview.Instances.Count > 1) _playOnSelectedOnly = GUILayout.Toggle(_playOnSelectedOnly, "Play on the selected avatar only");

        _libraryScroll = GUILayout.BeginScrollView(_libraryScroll, GUILayout.Height(170));
        GUILayout.Label(Describe(entry));
        GUILayout.EndScrollView();

        GUILayout.Label("COMBAT DEMO");
        _demoFacing = GUILayout.Toggle(_demoFacing, "Facing dummy (avatar 0 attacks, avatar 1 reacts)");
        GUILayout.BeginHorizontal();
        if (_demo == null && GUILayout.Button("[ Combat Demo ]")) _demo = StartCoroutine(RunDemo(library));
        if (_demo != null && GUILayout.Button("Stop demo")) StopDemo();
        if (GUILayout.Button(preview.FacingMode ? "Line up" : "Face pair")) { preview.SetFacingMode(!preview.FacingMode); FramePair(); }
        if (GUILayout.Button("Camera: pair")) FramePair();
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(_demoStatus)) GUILayout.Label(_demoStatus);
        GUILayout.EndArea();
    }

    private static string Describe(CombatAnimationEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Category: {e.category}{(e.categoryReviewed ? "" : "  (auto, from file name)")}   Review: {e.review}");
        sb.AppendLine($"Pack: {e.sourcePack}   Take: {e.sourceTake}");
        sb.AppendLine($"Length {e.duration:0.00} s @ {e.frameRate:0} fps   Loop: {e.loop}");
        sb.AppendLine($"Root motion: {e.rootMotion}  (travel x {e.rootTravel.x:+0.00;-0.00} z {e.rootTravel.z:+0.00;-0.00} m, yaw {e.rootYaw:+0;-0} deg)");
        sb.AppendLine($"Fingers: {(e.fingerCapture ? "captured" : "static")} -> {e.fingerMode}");
        if (e.trimmedStartFrames > 0) sb.AppendLine($"Trimmed {e.trimmedStartFrames} leading frame(s)");
        if (!string.IsNullOrEmpty(e.importWarnings)) sb.AppendLine($"Warnings: {e.importWarnings}");
        if (!string.IsNullOrEmpty(e.notes)) sb.AppendLine($"Notes: {e.notes}");
        sb.Append($"Source: {e.sourceFbx}");
        return sb.ToString();
    }

    private AnimationClip ClipOf(CombatAnimationEntry entry) => _useInPlaceClip || entry.clip == null ? entry.DebugClip : entry.clip;

    private void PlayEntry(CombatAnimationEntry entry)
    {
        StopDemo();
        var targets = _playOnSelectedOnly && Selected != null ? new[] { Selected } : null;
        preview.PlayClip(ClipOf(entry), _libraryLoop, _librarySpeed, targets);
    }

    private void SetSpeed(float speed)
    {
        _librarySpeed = speed;
        ForEachPlayer(p => p.Speed = speed);
    }

    private void ForEachPlayer(System.Action<AvatarClipPlayer> action)
    {
        foreach (var instance in preview.Instances)
        {
            var player = preview.PlayerOf(instance);
            if (player != null) action(player);
        }
    }

    private void FramePair()
    {
        var camera = Camera.main;
        if (camera == null || preview == null) return;
        var target = preview.transform.TransformPoint(new Vector3(0f, 0.95f, 0f));
        camera.transform.position = target + preview.transform.forward * 3.4f + Vector3.up * 0.15f;
        camera.transform.LookAt(target);
    }

    private void StopDemo()
    {
        if (_demo != null) StopCoroutine(_demo);
        _demo = null;
    }

    /// <summary>Plays library.demoSequence on avatar 0 (on every avatar without the facing dummy). With
    /// the dummy, avatar 1 faces avatar 0 and plays each step's partner clip at `partnerDelay`.</summary>
    private IEnumerator RunDemo(CombatAnimationLibrarySO library)
    {
        var steps = library.demoSequence;
        if (steps == null || steps.Count == 0) { _demoStatus = "No demo sequence in the library."; _demo = null; yield break; }

        bool facing = _demoFacing && preview.Instances.Count >= 2;
        preview.SetFacingMode(facing);
        if (facing) FramePair();
        var attacker = preview.Instances[0];
        var dummy = facing ? preview.Instances[1] : null;
        IEnumerable<AvatarInstance> attackers = facing ? new[] { attacker } : null;
        var guard = library.Find(steps[0].entryName);
        if (dummy != null && guard != null) preview.PlayClip(guard.DebugClip, true, _librarySpeed, new[] { dummy });

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var entry = library.Find(step.entryName);
            if (entry == null || entry.DebugClip == null) { _demoStatus = $"{i + 1}/{steps.Count} {step.label}: (no clip)"; continue; }
            _demoStatus = $"{i + 1}/{steps.Count} {step.label}: {entry.name}";

            bool hold = entry.loop;
            preview.PlayClip(entry.DebugClip, hold, _librarySpeed, attackers);
            var partner = dummy != null ? library.Find(step.partnerEntryName) : null;
            bool partnerStarted = false;
            float start = Time.time;
            while (true)
            {
                var player = preview.PlayerOf(attacker);
                if (player == null) break;
                if (partner != null && !partnerStarted && player.NormalizedTime >= step.partnerDelay)
                {
                    preview.PlayClip(partner.DebugClip, false, _librarySpeed, new[] { dummy });
                    partnerStarted = true;
                }
                if (hold ? Time.time - start >= 1.6f / Mathf.Max(_librarySpeed, 0.1f) : player.IsFinished) break;
                yield return null;
            }

            // The dummy returns to guard once its reaction has finished.
            if (dummy != null && guard != null && partnerStarted)
            {
                var dummyPlayer = preview.PlayerOf(dummy);
                while (dummyPlayer != null && !dummyPlayer.IsFinished) yield return null;
                preview.PlayClip(guard.DebugClip, true, _librarySpeed, new[] { dummy });
            }
        }

        if (guard != null) preview.PlayClip(guard.DebugClip, true, _librarySpeed, attackers);
        _demoStatus += "  (demo finished)";
        _demo = null;
    }

    private void Update()
    {
        if (!_pingPongGender || preview == null) return;
        foreach (var instance in preview.Instances)
        {
            if (instance?.Root == null) continue;
            var transition = instance.Root.GetComponent<AvatarBodyTransition>();
            if (transition != null && transition.IsRunning) continue;
            float target = instance.BodyMorphValues.Gender < 0.5f ? 1f : 0f;
            AvatarBodyTransition.StartGender(instance, target, Mathf.Max(0.5f, genderTransitionDuration * 2f));
        }
    }

    /// <summary>Front-on framing of the selected avatar around `localTarget` at `distance`.</summary>
    private static void Frame(AvatarInstance instance, Vector3 localTarget, float distance)
    {
        var camera = Camera.main;
        if (camera == null) return;
        var target = instance.Root.TransformPoint(localTarget);
        camera.transform.position = target + instance.Root.forward * distance;
        camera.transform.LookAt(target);
    }

    private string DescribeWeights(AvatarInstance instance)
    {
        var body = instance.BodyMorphValues;
        _sb.Clear();
        _sb.AppendLine($"Gender {body.Gender:0.00}   Weight {body.Weight * 2f - 1f:+0.00;-0.00;0.00}   Muscle {body.Muscle:0.00}");

        var smr = instance.BodyRenderers.Count > 0 ? instance.BodyRenderers[0] : null;
        if (smr == null || smr.sharedMesh == null) return _sb.ToString();

        foreach (var channel in ShownChannels)
        {
            int index = smr.sharedMesh.GetBlendShapeIndex(channel.ToString());
            string value = index < 0 ? "missing" : $"{smr.GetBlendShapeWeight(index):0.0}";
            _sb.AppendLine($"  {channel,-13} {value}");
        }
        return _sb.ToString();
    }

    private static string RunStressTest(AvatarInstance instance)
    {
        var original = instance.BodyMorphValues;
        var reference = AvatarBodyChecks.BakeBody(instance);

        var random = new System.Random(12345);
        for (int i = 0; i < 500; i++)
        {
            instance.ApplyBody(new BodyMorphValues
            {
                Gender = random.Next(3) == 0 ? random.Next(2) : (float)random.NextDouble(),
                Weight = (float)random.NextDouble(),
                Muscle = (float)random.NextDouble(),
            });
        }
        instance.ApplyBody(original);

        int mismatched = AvatarBodyChecks.CountDifferent(reference, AvatarBodyChecks.BakeBody(instance));
        return mismatched == 0
            ? "Stress test PASSED — body is bit-identical to before."
            : $"Stress test FAILED — {mismatched} vertices differ.";
    }
}

/// <summary>Shared exact-comparison helpers for the debug panel and the editor body tests.</summary>
public static class AvatarBodyChecks
{
    /// <summary>The body renderer's skinned + blendshaped vertices exactly as rendered, in the avatar
    /// root's own space.</summary>
    public static Vector3[] BakeBody(AvatarInstance instance, int renderer = 0)
    {
        var smr = instance.BodyRenderers[renderer];
        var mesh = new Mesh();
        smr.BakeMesh(mesh, true);
        var vertices = mesh.vertices;
        if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);

        var toRoot = instance.Root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = toRoot.MultiplyPoint3x4(vertices[i]);
        return vertices;
    }

    /// <summary>Exact (bitwise-float) difference count — not Vector3's approximate ==.</summary>
    public static int CountDifferent(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return int.MaxValue;
        int count = 0;
        for (int i = 0; i < a.Length; i++)
            if (a[i].x != b[i].x || a[i].y != b[i].y || a[i].z != b[i].z) count++;
        return count;
    }
}
