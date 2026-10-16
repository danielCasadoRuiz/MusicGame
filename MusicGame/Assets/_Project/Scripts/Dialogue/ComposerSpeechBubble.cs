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

    private const float PadX = 26f, PadTop = 16f, PadBottom = 20f, Spacing = 4f, MinWidth = 240f, TailSize = 22f;

    private RectTransform   _canvasRect, _bubble, _tail;
    private CanvasGroup     _group;
    private TextMeshProUGUI _name, _body;
    private TypewriterText  _typewriter;
    private Layout          _layout;
    private Vector2         _size;
    private Vector2         _smoothed;
    private bool            _hasPlacement;
    private float           _fadeFrom, _fadeDuration, _fadeElapsed;
    private Action          _fadeDone;

    /// <summary>World point the tail points at (null = none). Re-evaluated every frame.</summary>
    public Func<Vector3?> AnchorProvider;
    public bool AvoidCenterBand;

    public bool IsVisible => _bubble != null && gameObject.activeSelf;
    public bool IsRevealing => _typewriter != null && _typewriter.IsRevealing;
    public string CurrentText => _body != null ? _body.text : null;
    public TextMeshProUGUI BodyText => _body;
    public RectTransform BubbleRect => _bubble;
    public bool TailVisible => _tail != null && _tail.gameObject.activeSelf;
    public Func<bool> IsPaused { set { if (_typewriter != null) _typewriter.IsPaused = value; } }

    // ── Build ────────────────────────────────────────────────────────────────────

    public static ComposerSpeechBubble Create(RectTransform rootCanvas, FightFlowConfig config)
    {
        var root = UIFactory.CreateRect("ComposerSpeechBubble", rootCanvas);
        UIFactory.Stretch(root);
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 150; // above the RoundIntro curtain + HUD; no raycaster: never takes input
        var bubble = root.gameObject.AddComponent<ComposerSpeechBubble>();
        bubble._canvasRect = rootCanvas;
        bubble._layout = Layout.From(config);
        bubble._group = root.gameObject.AddComponent<CanvasGroup>();
        bubble._group.interactable = false;
        bubble._group.blocksRaycasts = false;

        var body = UIFactory.CreatePanel("Bubble", root, new Color(0.07f, 0.07f, 0.09f, 0.94f));
        body.raycastTarget = false;
        bubble._bubble = body.rectTransform;
        bubble._bubble.anchorMin = bubble._bubble.anchorMax = new Vector2(0.5f, 0.5f);
        bubble._bubble.pivot = new Vector2(0.5f, 0f);
        var outline = body.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.82f, 0.35f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        // Tail: a rotated square half-hidden under the bubble's bottom edge.
        var tail = UIFactory.CreatePanel("Tail", root, new Color(0.07f, 0.07f, 0.09f, 0.94f));
        tail.raycastTarget = false;
        bubble._tail = tail.rectTransform;
        bubble._tail.anchorMin = bubble._tail.anchorMax = new Vector2(0.5f, 0.5f);
        bubble._tail.pivot = new Vector2(0.5f, 0.5f);
        bubble._tail.sizeDelta = new Vector2(TailSize, TailSize);
        bubble._tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
        bubble._tail.SetAsFirstSibling();

        bubble._name = UIFactory.CreateText("Speaker", bubble._bubble, "", 24, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.TopLeft, FontStyles.Bold);
        bubble._name.raycastTarget = false;
        bubble._body = UIFactory.CreateText("Line", bubble._bubble, "", 30, Color.white, TextAlignmentOptions.TopLeft);
        bubble._body.enableWordWrapping = true;
        bubble._body.overflowMode = TextOverflowModes.Overflow;
        bubble._body.raycastTarget = false;
        foreach (var t in new[] { bubble._name.rectTransform, bubble._body.rectTransform })
        {
            t.anchorMin = t.anchorMax = new Vector2(0f, 1f);
            t.pivot = new Vector2(0f, 1f);
        }
        bubble._typewriter = bubble._body.gameObject.AddComponent<TypewriterText>();

        root.gameObject.SetActive(false);
        return bubble;
    }

    // ── Content ──────────────────────────────────────────────────────────────────

    /// <summary>Shows the bubble (instantly opaque) and starts revealing `text`.</summary>
    public void Show(string speaker, string text, float charsPerSecond, float punctuationPause)
    {
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
