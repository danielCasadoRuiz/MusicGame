using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual test bench for dressed enemy avatars (scene Scenes/Debug/AvatarsClothesChecker) — completely
/// outside the game flow. Pick any opponent (and tier), it is built through the SAME runtime path the
/// Fight uses (AvatarRecipeSO → AvatarFactory.CreateAsync, composer height scale), then any combat or
/// runner clip can be played / paused / scrubbed to look for clipping. Switching the Unity quality
/// level rebuilds the avatar so every wearable quality variant can be inspected.
///
/// In the Editor the opponent / animation lists are refreshed from the project on Play, so new
/// opponents, items and clips show up without touching the scene.
/// Mouse: left/right drag = orbit, middle drag = move focus height, wheel = zoom.
/// Keys: Space = pause, ←/→ = step one frame, F = frame the whole body.
/// </summary>
public class AvatarsClothesChecker : MonoBehaviour
{
    [Header("Content (refreshed from the project on Play in the Editor)")]
    public OpponentDefinition[] opponents = System.Array.Empty<OpponentDefinition>();
    public CombatAnimationLibrarySO combatLibrary;
    public RunnerAnimationStyleSO[] runnerStyles = System.Array.Empty<RunnerAnimationStyleSO>();

    [Header("Scene")]
    public Transform avatarAnchor;
    public Camera viewCamera;

    [Header("View")]
    public float turntableSpeed = 30f;
    public float panelWidth = 360f;

    private struct ClipItem { public string label; public AnimationClip clip; public bool loop; }

    private readonly List<string> _clipGroups = new();
    private readonly List<List<ClipItem>> _clipsByGroup = new();

    private AvatarInstance _instance;
    private AvatarClipPlayer _player;
    private ClipItem? _currentClip;
    private int _buildToken;
    private bool _building;
    private string _status = "Tria un enemic.";

    private int _opponent = -1, _level;
    private bool _applyHeight = true;
    private int _originalQuality;
    private BodyMorphValues _recipeBody;
    private bool _bodyVisible = true;
    private readonly Dictionary<EquippedAvatarItem, bool> _itemVisible = new();

    private int _clipGroup;
    private string _clipFilter = "";
    private float _speed = 1f;
    private Vector2 _panelScroll, _clipScroll;

    private float _yaw = 180f, _pitch = 8f, _distance = 3.2f, _focusHeight = 0.95f;
    private bool _turntable, _dragging;
    private Rect _panelRect;

    private void Awake()
    {
        _originalQuality = QualitySettings.GetQualityLevel();
#if UNITY_EDITOR
        RefreshFromProject();
#endif
        BuildClipCatalog();
        if (avatarAnchor == null) avatarAnchor = transform;
        EnsureStage();
    }

    /// <summary>The scene only holds this component: camera, lights and floor are created here when
    /// absent, so the stage stays identical whoever opens it.</summary>
    private void EnsureStage()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null)
        {
            var camGo = new GameObject("Camera") { tag = "MainCamera" };
            viewCamera = camGo.AddComponent<Camera>();
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = new Color(0.22f, 0.24f, 0.27f);
            viewCamera.fieldOfView = 35f;
            viewCamera.nearClipPlane = 0.05f;
            if (FindAnyObjectByType<AudioListener>() == null) camGo.AddComponent<AudioListener>();
        }
        if (FindAnyObjectByType<Light>() == null)
        {
            AddLight("Key Light", 1.2f, LightShadows.Soft, new Vector3(45f, 150f, 0f));
            AddLight("Fill Light", 0.45f, LightShadows.None, new Vector3(20f, -40f, 0f));
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.38f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.2f);
        }
        if (GameObject.Find("Floor") == null)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
            Destroy(floor.GetComponent<Collider>());
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit) { color = new Color(0.45f, 0.45f, 0.47f) };
                mat.SetFloat("_Smoothness", 0.1f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }
    }

    private static void AddLight(string name, float intensity, LightShadows shadows, Vector3 euler)
    {
        var go = new GameObject(name);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.shadows = shadows;
        go.transform.rotation = Quaternion.Euler(euler);
    }

#if UNITY_EDITOR
    private void RefreshFromProject()
    {
        var found = new List<OpponentDefinition>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:OpponentDefinition", new[] { "Assets/_Project" }))
        {
            var o = UnityEditor.AssetDatabase.LoadAssetAtPath<OpponentDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (o != null) found.Add(o);
        }
        if (found.Count > 0)
        {
            found.Sort((a, b) => string.CompareOrdinal(a.displayName ?? a.name, b.displayName ?? b.name));
            opponents = found.ToArray();
        }

        var styles = new List<RunnerAnimationStyleSO>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:RunnerAnimationStyleSO", new[] { "Assets/_Project" }))
        {
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<RunnerAnimationStyleSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (s != null) styles.Add(s);
        }
        if (styles.Count > 0) runnerStyles = styles.ToArray();

        if (combatLibrary == null)
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:CombatAnimationLibrarySO", new[] { "Assets/_Project/Animations/Processed" }))
            {
                combatLibrary = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatAnimationLibrarySO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (combatLibrary != null) break;
            }
    }
#endif

    private void BuildClipCatalog()
    {
        _clipGroups.Clear();
        _clipsByGroup.Clear();

        if (combatLibrary != null)
        {
            var list = new List<ClipItem>();
            foreach (var e in combatLibrary.entries)
                if (e != null && e.DebugClip != null)
                    list.Add(new ClipItem { label = $"[{e.category}] {e.name}", clip = e.DebugClip, loop = e.loop });
            list.Sort((a, b) => string.CompareOrdinal(a.label, b.label));
            AddGroup("Combat", list);
        }

        foreach (var style in runnerStyles)
        {
            if (style == null) continue;
            var list = new List<ClipItem>();
            AddRunner(list, "Idle", style.idle, true);
            AddRunner(list, "Run", style.locomotion, true);
            AddRunner(list, "FastRun", style.fastLocomotion, true);
            AddRunner(list, "Jump", style.jump, false);
            AddRunner(list, "Land", style.land, false);
            AddRunner(list, "Fall", style.fall, true);
            AddRunner(list, "Crouch", style.crouch, true);
            AddRunner(list, "AutoReturn", style.autoReturn, false);
            AddRunner(list, "Flourish", style.flourish, false);
            string name = string.IsNullOrEmpty(style.displayName) ? style.name.Replace("RunnerStyle_", "") : style.displayName;
            AddGroup("Runner " + name, list);
        }
    }

    private static void AddRunner(List<ClipItem> list, string role, RunnerAnimationEntry[] entries, bool loop)
    {
        if (entries == null) return;
        foreach (var e in entries)
            if (e != null && e.clip != null)
                list.Add(new ClipItem { label = $"{role}: {e.clip.name}", clip = e.clip, loop = loop });
    }

    private void AddGroup(string name, List<ClipItem> clips)
    {
        if (clips.Count == 0) return;
        _clipGroups.Add(name);
        _clipsByGroup.Add(clips);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private AvatarRecipeSO RecipeFor(OpponentDefinition o, int level)
    {
        if (o == null) return null;
        if (o.levels != null && level >= 0 && level < o.levels.Length && o.levels[level]?.avatarRecipe != null)
            return o.levels[level].avatarRecipe;
        return o.defaultConfig?.avatarRecipe;
    }

    private void Select(int opponent, int level)
    {
        _opponent = opponent;
        _level = level;
        _ = BuildAsync();
    }

    private async System.Threading.Tasks.Task BuildAsync()
    {
        int token = ++_buildToken;
        ReleaseAvatar();
        var o = _opponent >= 0 && _opponent < opponents.Length ? opponents[_opponent] : null;
        var recipe = RecipeFor(o, _level);
        if (recipe == null) { _status = $"{(o != null ? o.displayName : "?")}: sense AvatarRecipe."; return; }

        _building = true;
        _status = $"Muntant {recipe.name} ({QualitySettings.names[QualitySettings.GetQualityLevel()]})...";
        var instance = await AvatarFactory.CreateAsync(recipe.ToRuntime(), avatarAnchor);
        if (this == null || token != _buildToken) { instance?.Dispose(); return; }
        _building = false;

        if (instance == null || instance.Root == null)
        {
            instance?.Dispose();
            _status = $"ERROR muntant {recipe.name} — mira la consola.";
            return;
        }

        _instance = instance;
        _instance.Root.localPosition = Vector3.zero;
        _instance.Root.localRotation = Quaternion.identity;
        ApplyHeight();
        _recipeBody = _instance.BodyMorphValues;
        _bodyVisible = true;
        _itemVisible.Clear();
        foreach (var item in _instance.EquippedItems) _itemVisible[item] = true;
        if (_instance.Animator != null) _instance.Animator.applyRootMotion = false;
        _status = $"{o.displayName} — {recipe.name}: {_instance.EquippedItems.Count} peça(es).";

        if (_currentClip.HasValue) PlayClip(_currentClip.Value, keepTime: false);
    }

    private void ApplyHeight()
    {
        if (_instance?.Root == null) return;
        var o = _opponent >= 0 && _opponent < opponents.Length ? opponents[_opponent] : null;
        float height = _applyHeight && o != null ? o.heightMeters : FightSceneBootstrap.CanonicalAvatarHeight;
        _instance.Root.localScale = Vector3.one * Mathf.Clamp(height / FightSceneBootstrap.CanonicalAvatarHeight, 0.8f, 1.2f);
    }

    private void ReleaseAvatar()
    {
        _player?.Dispose();
        _player = null;
        _instance?.Dispose();
        _instance = null;
        _itemVisible.Clear();
    }

    private void OnDestroy()
    {
        _buildToken++;
        ReleaseAvatar();
        if (QualitySettings.GetQualityLevel() != _originalQuality) QualitySettings.SetQualityLevel(_originalQuality, true);
    }

    // ── Animation ────────────────────────────────────────────────────────────

    private void PlayClip(ClipItem item, bool keepTime)
    {
        _currentClip = item;
        var animator = _instance?.Animator;
        if (animator == null) return;
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        if (_player == null || !_player.IsValid) _player = new AvatarClipPlayer(animator);
        float t = keepTime ? _player.Time : 0f;
        _player.Speed = _speed;
        _player.Paused = false;
        _player.Play(item.clip, item.loop, keepTime ? 0f : 0.15f);
        if (keepTime) _player.Seek(t);
    }

    private void StopClip()
    {
        _currentClip = null;
        _player?.Dispose();
        _player = null;
        _instance?.ResetToRestPose();
    }

    private void Step(int frames)
    {
        if (_player == null || _player.Clip == null) return;
        _player.Paused = true;
        float fps = _player.Clip.frameRate > 0f ? _player.Clip.frameRate : 30f;
        _player.Seek(Mathf.Clamp(_player.Time + frames / fps, 0f, _player.Length));
    }

    private void Update()
    {
        if (_player != null && _player.IsValid)
        {
            _player.Speed = _speed;
            _player.Tick(Time.deltaTime);
        }
        if (_turntable) _yaw += turntableSpeed * Time.deltaTime;
    }

    // ── Camera ───────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (viewCamera == null) return;
        float scale = _instance?.Root != null ? _instance.Root.localScale.y : 1f;
        var focus = avatarAnchor.position + Vector3.up * _focusHeight * scale;
        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * _distance, rotation);
    }

    private void Frame(float height, float distance, float pitch = 8f)
    {
        _focusHeight = height;
        _distance = distance;
        _pitch = pitch;
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        float uiScale = Mathf.Max(1f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        float screenH = Screen.height / uiScale;
        _panelRect = new Rect(8f, 8f, panelWidth, screenH - 16f);

        HandleInput();

        GUILayout.BeginArea(_panelRect, GUI.skin.box);
        _panelScroll = GUILayout.BeginScrollView(_panelScroll);
        GUILayout.Label("<b>AVATARS CLOTHES CHECKER</b>", Rich());
        GUILayout.Label(_status, Wrap());

        DrawOpponents();
        DrawQuality();
        DrawItems();
        DrawBody();
        DrawAnimation();
        DrawCamera();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawOpponents()
    {
        Header("Enemic");
        if (opponents.Length == 0) { GUILayout.Label("Cap OpponentDefinition trobat."); return; }
        var names = new string[opponents.Length];
        for (int i = 0; i < opponents.Length; i++)
            names[i] = opponents[i] != null ? (string.IsNullOrEmpty(opponents[i].displayName) ? opponents[i].name : opponents[i].displayName) : "?";
        int picked = GUILayout.SelectionGrid(_opponent, names, 3);
        if (picked != _opponent) Select(picked, 0);

        var o = _opponent >= 0 && _opponent < opponents.Length ? opponents[_opponent] : null;
        if (o != null && o.levels != null && o.levels.Length > 1)
        {
            var tiers = new string[o.levels.Length];
            for (int i = 0; i < tiers.Length; i++) tiers[i] = $"T{o.levels[i]?.level ?? i}";
            int tier = GUILayout.Toolbar(_level, tiers);
            if (tier != _level) Select(_opponent, tier);
            var recipe = RecipeFor(o, _level);
            GUILayout.Label($"Recepta: {(recipe != null ? recipe.name : "cap")}");
        }
        GUI.enabled = _opponent >= 0 && !_building;
        if (GUILayout.Button("Tornar a muntar")) _ = BuildAsync();
        GUI.enabled = true;
    }

    private void DrawQuality()
    {
        Header("Qualitat (Unity Quality)");
        int current = QualitySettings.GetQualityLevel();
        int picked = GUILayout.Toolbar(current, QualitySettings.names);
        if (picked != current)
        {
            QualitySettings.SetQualityLevel(picked, true);
            if (_opponent >= 0) _ = BuildAsync();
        }
    }

    private void DrawItems()
    {
        Header("Roba");
        if (_instance == null) { GUILayout.Label(_building ? "Muntant..." : "—"); return; }

        bool body = GUILayout.Toggle(_bodyVisible, " Cos");
        if (body != _bodyVisible)
        {
            _bodyVisible = body;
            foreach (var r in _instance.BodyRenderers) if (r != null) r.enabled = body;
        }
        if (_instance.EquippedItems.Count == 0) GUILayout.Label("(sense peces equipades)");
        foreach (var item in _instance.EquippedItems)
        {
            bool visible = !_itemVisible.TryGetValue(item, out var v) || v;
            string label = $" {item.Definition?.displayName ?? "?"}  <color=#9cf>{VariantName(item)}</color>  {Triangles(item)} tris";
            bool now = GUILayout.Toggle(visible, label, Toggle());
            if (now != visible)
            {
                _itemVisible[item] = now;
                if (item.Instance != null) item.Instance.SetActive(now);
            }
        }
    }

    private static string VariantName(EquippedAvatarItem item) =>
        item.Instance != null ? item.Instance.name.Replace("(Clone)", "") : "?";

    private static int Triangles(EquippedAvatarItem item)
    {
        int tris = 0;
        if (item.VisualPart == null) return 0;
        foreach (var smr in item.VisualPart.skinnedRenderers)
        {
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null) continue;
            for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
        }
        return tris;
    }

    private void DrawBody()
    {
        Header("Cos (per provar clipping amb altres físics)");
        bool height = GUILayout.Toggle(_applyHeight, " Alçada del compositor (com a la lluita)");
        if (height != _applyHeight) { _applyHeight = height; ApplyHeight(); }
        if (_instance == null) return;

        var b = _instance.BodyMorphValues;
        float gender = Slider("Gender", b.Gender);
        float weight = Slider("Weight", b.Weight);
        float muscle = Slider("Muscle", b.Muscle);
        if (gender != b.Gender || weight != b.Weight || muscle != b.Muscle)
        {
            b.Gender = gender; b.Weight = weight; b.Muscle = muscle;
            _instance.ApplyBody(b);
        }
        if (GUILayout.Button("Cos de la recepta")) _instance.ApplyBody(_recipeBody);
    }

    private void DrawAnimation()
    {
        Header("Animació");
        if (_clipGroups.Count == 0) { GUILayout.Label("Cap clip trobat."); return; }

        _clipGroup = GUILayout.SelectionGrid(Mathf.Clamp(_clipGroup, 0, _clipGroups.Count - 1), _clipGroups.ToArray(), 2);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Filtre", GUILayout.Width(40f));
        _clipFilter = GUILayout.TextField(_clipFilter);
        GUILayout.EndHorizontal();

        _clipScroll = GUILayout.BeginScrollView(_clipScroll, GUILayout.Height(200f));
        foreach (var item in _clipsByGroup[_clipGroup])
        {
            if (_clipFilter.Length > 0 && item.label.IndexOf(_clipFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            bool current = _currentClip.HasValue && _currentClip.Value.clip == item.clip;
            if (GUILayout.Button((current ? "▶ " : "") + item.label, Left())) PlayClip(item, keepTime: false);
        }
        GUILayout.EndScrollView();

        if (GUILayout.Button("Pose de repòs (aturar)")) StopClip();
        if (_player == null || _player.Clip == null) return;

        GUILayout.Label($"{_player.Clip.name}  {_player.Time:0.00}s / {_player.Length:0.00}s");
        float t = GUILayout.HorizontalSlider(_player.Time, 0f, Mathf.Max(0.01f, _player.Length));
        if (Mathf.Abs(t - _player.Time) > 0.0001f) { _player.Paused = true; _player.Seek(t); }
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ frame")) Step(-1);
        if (GUILayout.Button(_player.Paused ? "Play" : "Pausa")) _player.Paused = !_player.Paused;
        if (GUILayout.Button("frame ▶")) Step(1);
        GUILayout.EndHorizontal();
        _player.Loop = GUILayout.Toggle(_player.Loop, " Loop");
        _speed = Slider("Velocitat", _speed, 0f, 2f);
    }

    private void DrawCamera()
    {
        Header("Càmera");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cos")) Frame(0.95f, 3.2f);
        if (GUILayout.Button("Tors")) Frame(1.35f, 1.5f);
        if (GUILayout.Button("Cames")) Frame(0.55f, 1.7f);
        if (GUILayout.Button("Peus")) Frame(0.12f, 1.0f, 20f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Davant")) _yaw = 180f;
        if (GUILayout.Button("Esquerra")) _yaw = 90f;
        if (GUILayout.Button("Darrere")) _yaw = 0f;
        if (GUILayout.Button("Dreta")) _yaw = 270f;
        GUILayout.EndHorizontal();
        _turntable = GUILayout.Toggle(_turntable, " Girar automàticament");
        GUILayout.Label("Arrossegar: orbitar · Botó del mig: alçada · Roda: zoom\nEspai: pausa · ←/→: frame · F: cos sencer", Wrap());
    }

    private void HandleInput()
    {
        var e = Event.current;
        bool overPanel = _panelRect.Contains(e.mousePosition);
        switch (e.type)
        {
            case UnityEngine.EventType.MouseDown:
                _dragging = !overPanel; // drags that start on the panel belong to its controls
                break;
            case UnityEngine.EventType.MouseUp:
                _dragging = false;
                break;
            case UnityEngine.EventType.MouseDrag when _dragging:
                if (e.button == 2) _focusHeight = Mathf.Clamp(_focusHeight + e.delta.y * 0.005f * _distance, 0f, 2.2f);
                else { _yaw += e.delta.x * 0.3f; _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.3f, -60f, 80f); }
                e.Use();
                break;
            case UnityEngine.EventType.ScrollWheel when !overPanel:
                _distance = Mathf.Clamp(_distance * (1f + e.delta.y * 0.05f), 0.3f, 10f);
                e.Use();
                break;
            case UnityEngine.EventType.KeyDown when GUIUtility.keyboardControl == 0:
                if (e.keyCode == KeyCode.Space && _player != null) { _player.Paused = !_player.Paused; e.Use(); }
                else if (e.keyCode == KeyCode.LeftArrow) { Step(-1); e.Use(); }
                else if (e.keyCode == KeyCode.RightArrow) { Step(1); e.Use(); }
                else if (e.keyCode == KeyCode.F) { Frame(0.95f, 3.2f); e.Use(); }
                break;
        }
    }

    // ── IMGUI helpers ────────────────────────────────────────────────────────

    private static void Header(string text)
    {
        GUILayout.Space(8f);
        GUILayout.Label($"<b>{text}</b>", Rich());
    }

    private static float Slider(string label, float value, float min = 0f, float max = 1f)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} {value:0.00}", GUILayout.Width(110f));
        value = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.EndHorizontal();
        return value;
    }

    private static GUIStyle _rich, _wrap, _left, _toggle;
    private static GUIStyle Rich() => _rich ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 };
    private static GUIStyle Wrap() => _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
    private static GUIStyle Left() => _left ??= new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
    private static GUIStyle Toggle() => _toggle ??= new GUIStyle(GUI.skin.toggle) { richText = true };
}
