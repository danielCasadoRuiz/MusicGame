using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PRESENTATION of a composer line as a speech bubble over the rival's head — shared by the Versus
/// intro and the post-match farewell (ComposerDialoguePresentation drives it).
///
/// Screen-space, not world-space text: the bubble lives on the UI root canvas (its own nested canvas,
/// sorted above the countdown curtain so it is never dimmed) and every LateUpdate — after the fight
/// camera has moved — projects the anchor (ComposerAnchor: the avatar's Head bone, else the top of its
/// renderers) through the live fight camera. That keeps a constant, readable size during the camera
/// pass, at any resolution/aspect, and geometry can never cover it.
///
/// Placement rules (all in canvas units, so they scale with CanvasScaler):
///   • bubble bottom-centre sits `gap` above the projected head, tail pointing down at it;
///   • clamped inside the screen minus `margin`, below the HUD band (`topReserved`) and, while
///     AvoidCenterBand is on (round intro / countdown), never over the central countdown text;
///   • never over the KeepOut areas (the bonus block / build panel while the conversion plays);
///   • anchor BEHIND the camera or no camera/anchor: the bubble parks top-centre without a tail —
///     the line stays readable, it just stops pointing; anchor off-screen: clamped to the edge, no tail.
/// </summary>
[DefaultExecutionOrder(1000)] // after FightCameraController.LateUpdate
public class ComposerSpeechBubble : MonoBehaviour
{
    public struct Layout
    {
        public float MaxWidth, Margin, TopReserved, Gap;
        public Vector2 CenterBand;

        public static Layout From(FightFlowConfig c) => c == null
            ? new Layout { MaxWidth = 640f, Margin = 32f, TopReserved = 150f, Gap = 26f, CenterBand = new Vector2(760f, 240f) }
            : new Layout { MaxWidth = c.bubbleMaxWidth, Margin = c.bubbleScreenMargin, TopReserved = c.bubbleTopReserved,
                           Gap = c.bubbleGapAboveHead, CenterBand = c.bubbleCountdownBand };
    }

    [Header("Prefab parts (Prefabs/UI/Components/ComposerSpeechBubble.prefab)")]
    [SerializeField] private RectTransform _bubble;
    [SerializeField] private RectTransform _tail;
    [SerializeField] private CanvasGroup   _group;
    [SerializeField] private TextMeshProUGUI _name;
    [SerializeField] private TextMeshProUGUI _body;
    [SerializeField] private TypewriterText  _typewriter;
    [Header("Inner layout (canvas units)")]
    [SerializeField] private float PadX = 26f;
    [SerializeField] private float PadTop = 16f;
    [SerializeField] private float PadBottom = 20f;
    [SerializeField] private float Spacing = 4f;
    [SerializeField] private float MinWidth = 240f;
    [SerializeField] private float TailSize = 22f;

    private RectTransform   _canvasRect;
    private Layout          _layout;
    private Vector2         _size;
    private Vector2         _smoothed;
    private bool            _hasPlacement;
    private float           _fadeFrom, _fadeDuration, _fadeElapsed;
    private Action          _fadeDone;

    /// <summary>World point the tail points at (null = none). Re-evaluated every frame.</summary>
    public Func<Vector3?> AnchorProvider;
    /// <summary>Optional SCREEN-space anchor (pixels) that wins over AnchorProvider while it returns a value — e.g.
    /// the rival's portrait on the opaque Versus loading card. Cleared (null) → the bubble glides to the world
    /// anchor (the head) with its usual smoothing.</summary>
    public Func<Vector2?> ScreenAnchorProvider;
    public bool AvoidCenterBand;
    /// <summary>Optional areas (UI root canvas local units) the bubble must never cover — e.g. the bonus block and
    /// the build panel while the runner → fight conversion plays under the rival's line. The bubble moves the
    /// shortest way out of each one that stays on screen.</summary>
    public Func<System.Collections.Generic.IReadOnlyList<Rect>> KeepOut;

    public bool IsVisible => _bubble != null && gameObject.activeSelf;
    public bool IsRevealing => _typewriter != null && _typewriter.IsRevealing;
    public string CurrentText => _body != null ? _body.text : null;
    public TextMeshProUGUI BodyText => _body;
    public RectTransform BubbleRect => _bubble;
    public bool TailVisible => _tail != null && _tail.gameObject.activeSelf;
    public Func<bool> IsPaused { set { if (_typewriter != null) _typewriter.IsPaused = value; } }

    // ── Spawn ────────────────────────────────────────────────────────────────────

    /// <summary>Instantiates the library's ComposerSpeechBubble prefab on the root canvas (hidden). Placement
    /// limits come from FightFlowConfig (gameplay tuning); the bubble's look from the prefab.</summary>
    public static ComposerSpeechBubble Spawn(RectTransform rootCanvas, FightFlowConfig config)
    {
        var bubble = UIPrefabLibrarySO.Spawn(UIPrefabLibrarySO.Instance != null ? UIPrefabLibrarySO.Instance.speechBubble : null, rootCanvas, "ComposerSpeechBubble");
        if (bubble == null) return null;
        var rt = (RectTransform)bubble.transform;
        UIFactory.Stretch(rt);
        bubble._canvasRect = rootCanvas;
        bubble._layout = Layout.From(config);
        bubble.gameObject.SetActive(false);
        return bubble;
    }

    /// <summary>Editor builder hook.</summary>
    public void Bind(RectTransform bubble, RectTransform tail, CanvasGroup group, TextMeshProUGUI speaker, TextMeshProUGUI body, TypewriterText typewriter)
    {
        _bubble = bubble; _tail = tail; _group = group; _name = speaker; _body = body; _typewriter = typewriter;
    }

    // ── Content ──────────────────────────────────────────────────────────────────

    /// <summary>Shows the bubble (instantly opaque) and starts revealing `text`.</summary>
    public void Show(string speaker, string text, float charsPerSecond, float punctuationPause)
    {
        // Spawn sets these; a bubble placed by hand in a scene/prefab falls back to its parent canvas + defaults.
        if (_canvasRect == null) _canvasRect = transform.parent as RectTransform;
        if (_layout.MaxWidth <= 0f) _layout = Layout.From(null);
        CancelFade();
        _group.alpha = 1f;
        _name.text = speaker ?? "";
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        Resize(text);
        _typewriter.Play(text, charsPerSecond, punctuationPause);
        _hasPlacement = false;
        Place(snap: true);
    }

    /// <summary>New text for the SAME line (language change). `restartReveal` types it again from the
    /// start; otherwise it is shown complete at once.</summary>
    public void SetText(string text, bool restartReveal, float charsPerSecond, float punctuationPause)
    {
        if (!IsVisible) return;
        Resize(text);
        if (restartReveal) _typewriter.Play(text, charsPerSecond, punctuationPause);
        else { _typewriter.Play(text, 0f); }
        Place(snap: true);
    }

    public void CompleteReveal() { if (_typewriter != null && _typewriter.IsRevealing) _typewriter.Complete(); }

    /// <summary>Fades out over `seconds` (0 = at once), then hides; `done` runs once at the end.</summary>
    public void FadeOut(float seconds, Action done = null)
    {
        if (!IsVisible) { done?.Invoke(); return; }
        _fadeDone = done;
        _fadeFrom = _group.alpha;
        _fadeDuration = Mathf.Max(0f, float.IsNaN(seconds) ? 0f : seconds);
        _fadeElapsed = 0f;
        if (_fadeDuration <= 0f) FinishFade();
    }

    /// <summary>Hides immediately and empties the text (no stale line when it opens again).</summary>
    public void Hide()
    {
        CancelFade();
        if (_typewriter != null) _typewriter.Clear();
        if (_name != null) _name.text = "";
        gameObject.SetActive(false);
    }

    private void CancelFade() { _fadeDone = null; _fadeDuration = -1f; }

    private void FinishFade()
    {
        var done = _fadeDone;
        Hide();
        done?.Invoke();
    }

    private void Resize(string text)
    {
        if (_body.font == null) _body.font = TMP_Settings.defaultFontAsset; // a TMP text that never ran Awake has no font yet and cannot be measured
        if (_name.font == null) _name.font = TMP_Settings.defaultFontAsset;
        float maxInner = Mathf.Max(MinWidth, _layout.MaxWidth) - PadX * 2f;
        Vector2 bodyPref = _body.GetPreferredValues(text ?? "", maxInner, 0f);
        Vector2 namePref = _name.GetPreferredValues(_name.text, maxInner, 0f);
        float inner = Mathf.Clamp(Mathf.Max(bodyPref.x, namePref.x) + 2f, MinWidth - PadX * 2f, maxInner);
        bodyPref = _body.GetPreferredValues(text ?? "", inner, 0f);

        _name.rectTransform.sizeDelta = new Vector2(inner, namePref.y);
        _name.rectTransform.anchoredPosition = new Vector2(PadX, -PadTop);
        _body.rectTransform.sizeDelta = new Vector2(inner, bodyPref.y);
        _body.rectTransform.anchoredPosition = new Vector2(PadX, -PadTop - namePref.y - Spacing);
        _size = new Vector2(inner + PadX * 2f, PadTop + namePref.y + Spacing + bodyPref.y + PadBottom);
        _bubble.sizeDelta = _size;
    }

    // ── Placement ────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!IsVisible) return;
        if (_fadeDuration > 0f)
        {
            _fadeElapsed += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(_fadeFrom, 0f, Mathf.Clamp01(_fadeElapsed / _fadeDuration));
            if (_fadeElapsed >= _fadeDuration) { FinishFade(); return; }
        }
        Place(snap: false);
    }

    private void Place(bool snap)
    {
        Rect area = _canvasRect.rect; // centre-origin canvas units
        float halfW = _size.x * 0.5f;
        float minX = area.xMin + _layout.Margin + halfW, maxX = area.xMax - _layout.Margin - halfW;
        float minY = area.yMin + _layout.Margin + TailSize, maxY = area.yMax - _layout.TopReserved - _size.y;

        bool pointing = TryProjectAnchor(out Vector2 anchor, out bool onScreen);
        Vector2 target;
        if (!pointing) target = new Vector2(0f, maxY); // parked top-centre (see class doc)
        else target = anchor + new Vector2(0f, _layout.Gap + TailSize * 0.5f);

        target.x = minX <= maxX ? Mathf.Clamp(target.x, minX, maxX) : 0f;
        target.y = minY <= maxY ? Mathf.Clamp(target.y, minY, maxY) : minY;

        var keepOut = KeepOut?.Invoke();
        if (keepOut != null)
            foreach (var r in keepOut) target = MoveOutOf(target, r, minX, maxX, minY, maxY);

        if (AvoidCenterBand && OverlapsCenterBand(target))
        {
            float above = _layout.CenterBand.y * 0.5f + 8f;
            target.y = Mathf.Min(Mathf.Max(target.y, above), Mathf.Max(minY, maxY));
        }

        if (snap || !_hasPlacement) _smoothed = target;
        else _smoothed = Vector2.Lerp(_smoothed, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        _hasPlacement = true;
        _bubble.anchoredPosition = _smoothed;

        // Tail only when it can honestly point at the head just below the bubble.
        bool tail = pointing && onScreen && anchor.y < _smoothed.y &&
                    _smoothed.y - anchor.y < _layout.Gap + TailSize * 3f &&
                    anchor.x > _smoothed.x - halfW + 14f && anchor.x < _smoothed.x + halfW - 14f;
        _tail.gameObject.SetActive(tail);
        if (tail) _tail.anchoredPosition = new Vector2(anchor.x, _smoothed.y);
    }

    // Shortest on-screen move (up / down / right / left) that takes the bubble (bottom-centre pivot, tail below it)
    // clear of `area`; unchanged if it does not overlap or no move fits.
    private Vector2 MoveOutOf(Vector2 bottomCentre, Rect area, float minX, float maxX, float minY, float maxY)
    {
        float halfW = _size.x * 0.5f;
        var me = Rect.MinMaxRect(bottomCentre.x - halfW, bottomCentre.y - TailSize, bottomCentre.x + halfW, bottomCentre.y + _size.y);
        if (!me.Overlaps(area)) return bottomCentre;
        Vector2 best = bottomCentre;
        float bestCost = float.MaxValue;
        void Try(Vector2 p)
        {
            if (p.x < minX - 0.5f || p.x > maxX + 0.5f || p.y < minY - 0.5f || p.y > maxY + 0.5f) return;
            float cost = (p - bottomCentre).sqrMagnitude;
            if (cost < bestCost) { bestCost = cost; best = p; }
        }
        Try(new Vector2(bottomCentre.x, area.yMax + TailSize));
        Try(new Vector2(bottomCentre.x, area.yMin - _size.y));
        Try(new Vector2(area.xMax + halfW, bottomCentre.y));
        Try(new Vector2(area.xMin - halfW, bottomCentre.y));
        return best;
    }

    private bool OverlapsCenterBand(Vector2 bottomCentre)
    {
        Vector2 half = _layout.CenterBand * 0.5f;
        float l = bottomCentre.x - _size.x * 0.5f, r = bottomCentre.x + _size.x * 0.5f;
        float b = bottomCentre.y, t = bottomCentre.y + _size.y;
        return r > -half.x && l < half.x && t > -half.y && b < half.y;
    }

    private bool TryProjectAnchor(out Vector2 local, out bool onScreen)
    {
        local = default; onScreen = false;
        var screenAnchor = ScreenAnchorProvider?.Invoke();
        if (screenAnchor.HasValue)
        {
            onScreen = true;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenAnchor.Value, null, out local);
        }
        var world = AnchorProvider?.Invoke();
        var cam = FightSceneBootstrap.Instance != null && FightSceneBootstrap.Instance.CameraController != null
            ? FightSceneBootstrap.Instance.CameraController.Camera : null;
        if (world == null || cam == null || !cam.isActiveAndEnabled) return false;
        Vector3 screen = cam.WorldToScreenPoint(world.Value);
        if (screen.z <= 0.05f) return false; // behind the camera
        onScreen = screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out local);
    }
}

/// <summary>Where a composer's speech bubble points: just above the avatar's Head bone (any rig that
/// names it "Head"), else the top of its renderers, else a fixed height over the actor — so it follows
/// different heights, avatar variants and animations (a KO'd rival's low head included).</summary>
public static class ComposerAnchor
{
    public static Vector3? Resolve(FighterActor actor, float aboveHead)
    {
        if (actor == null) return null;
        var head = FindHead(actor);
        if (head != null) return head.position + Vector3.up * aboveHead;

        var root = actor.VisualRoot != null ? actor.VisualRoot : actor.transform;
        bool any = false;
        Bounds bounds = default;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        if (any) return new Vector3(bounds.center.x, bounds.max.y + aboveHead * 0.5f, bounds.center.z);
        return actor.transform.position + Vector3.up * 1.9f;
    }

    // One rival at a time: cache the head until the actor's visual is replaced (avatar swap).
    private static FighterActor s_actor;
    private static Transform s_head;
    private static int s_visualSignature;

    private static Transform FindHead(FighterActor actor)
    {
        var root = actor.VisualRoot;
        if (root == null) return null;
        int signature = root.childCount > 0 ? root.GetChild(0).GetInstanceID() : 0;
        if (actor == s_actor && signature == s_visualSignature && s_head != null) return s_head;
        s_actor = actor;
        s_visualSignature = signature;
        s_head = FindHeadUncached(root);
        return s_head;
    }

    private static Transform FindHeadUncached(Transform root)
    {
        var animator = root.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            var bone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (bone != null) return bone;
        }
        return FindByName(root, "head");
    }

    private static Transform FindByName(Transform t, string lowerName)
    {
        if (t.name.ToLowerInvariant() == lowerName) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var found = FindByName(t.GetChild(i), lowerName);
            if (found != null) return found;
        }
        return null;
    }
}
