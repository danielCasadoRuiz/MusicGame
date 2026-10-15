using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Reveal/continue timings (FightFlowConfig → Composer dialogues).</summary>
public struct ComposerDialogueTiming
{
    public float CharactersPerSecond;
    public float PunctuationPauseSeconds;
    public float MinContinueDelaySeconds;
    /// <summary>&gt; 0: continue on its own this long after the line is complete (0 = wait for a press).</summary>
    public float AutoContinueSeconds;

    public static ComposerDialogueTiming From(FightFlowConfig config, float autoContinueSeconds = 0f) => config == null
        ? new ComposerDialogueTiming { CharactersPerSecond = 40f, PunctuationPauseSeconds = 0.12f, MinContinueDelaySeconds = 0.6f, AutoContinueSeconds = autoContinueSeconds }
        : new ComposerDialogueTiming
        {
            CharactersPerSecond     = config.dialogueCharactersPerSecond,
            PunctuationPauseSeconds = config.dialoguePunctuationPauseSeconds,
            MinContinueDelaySeconds = config.dialogueMinContinueDelaySeconds,
            AutoContinueSeconds     = autoContinueSeconds,
        };
}

/// <summary>
/// PRESENTATION of one composer subtitle — the same look on the Versus and the Match Result screens:
/// composer name, a readable box at the bottom of the screen and a blinking "»" once the player may
/// continue. Built procedurally on top of the owning screen (so regenerating the screen prefabs never
/// removes it), with a full-screen transparent tap catcher in front of everything else on that screen.
///
/// Input (tap / click / Space / Enter / gamepad South): while typing → show the whole line; once
/// complete AND MinContinueDelaySeconds after it appeared → ContinueRequested. One press never does
/// both (one action per frame + a short guard after completing), and because the catcher owns the
/// press, the release can never click a result button that becomes interactable right after.
/// </summary>
public class ComposerDialogueBox : MonoBehaviour, IPointerClickHandler
{
    private const float CompleteToContinueGuard = 0.15f;

    private RectTransform   _root;
    private TextMeshProUGUI _nameText;
    private TextMeshProUGUI _bodyText;
    private TextMeshProUGUI _continueHint;
    private TypewriterText  _typewriter;

    private float _shownAt, _completedAt, _minContinueDelay, _autoContinue;
    private bool  _autoContinued;
    private int   _lastInputFrame = -1;

    public event Action ContinueRequested;
    public bool IsVisible => _root != null && _root.gameObject.activeSelf;
    public bool IsRevealing => _typewriter != null && _typewriter.IsRevealing;
    public string CurrentText => _bodyText != null ? _bodyText.text : null;
    public TextMeshProUGUI BodyText => _bodyText;

    /// <summary>Pauses the reveal (e.g. () => FightController.IsPaused).</summary>
    public Func<bool> IsPaused { set { if (_typewriter != null) _typewriter.IsPaused = value; } }

    public bool CanContinue =>
        IsVisible && !IsRevealing &&
        Time.unscaledTime - _shownAt >= _minContinueDelay &&
        Time.unscaledTime - _completedAt >= CompleteToContinueGuard;

    // ── Build ────────────────────────────────────────────────────────────────────

    public static ComposerDialogueBox Create(RectTransform screenRoot)
    {
        var root = UIFactory.CreateRect("ComposerDialogue", screenRoot);
        UIFactory.Stretch(root);
        var catcher = root.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f); // invisible, but it owns every press on this screen
        catcher.raycastTarget = true;

        var box = root.gameObject.AddComponent<ComposerDialogueBox>();
        box._root = root;

        var panel = UIFactory.CreatePanel("Panel", root, new Color(0.06f, 0.06f, 0.08f, 0.94f));
        var prt = panel.rectTransform;
        prt.anchorMin = new Vector2(0.08f, 0.05f);
        prt.anchorMax = new Vector2(0.92f, 0.05f);
        prt.pivot     = new Vector2(0.5f, 0f);
        prt.anchoredPosition = Vector2.zero;
        panel.raycastTarget = false;
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
        outline.effectDistance = new Vector2(3f, -3f);

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 22, 30);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        box._nameText = UIFactory.CreateText("Speaker", prt, "", 28, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.TopLeft, FontStyles.Bold);
        box._nameText.raycastTarget = false;
        box._nameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        box._bodyText = UIFactory.CreateText("Line", prt, "", 34, Color.white, TextAlignmentOptions.TopLeft);
        box._bodyText.enableWordWrapping = true;                       // wrap, never truncate
        box._bodyText.overflowMode = TextOverflowModes.Overflow;
        box._bodyText.raycastTarget = false;
        box._bodyText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        box._bodyText.gameObject.AddComponent<LayoutElement>().minHeight = 90f; // two lines: short lines don't make a tiny box
        box._typewriter = box._bodyText.gameObject.AddComponent<TypewriterText>();
        box._typewriter.Completed += () => box._completedAt = Time.unscaledTime;

        box._continueHint = UIFactory.CreateText("ContinueHint", prt, "»", 34, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.BottomRight, FontStyles.Bold);
        box._continueHint.raycastTarget = false;
        var hintLayout = box._continueHint.gameObject.AddComponent<LayoutElement>();
        hintLayout.ignoreLayout = true;
        UIFactory.SetBox(box._continueHint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                         new Vector2(-18f, 8f), new Vector2(40f, 40f));
        box._continueHint.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        root.gameObject.SetActive(false);
        return box;
    }

    // ── Show / hide ──────────────────────────────────────────────────────────────

    public void Show(string speaker, string text, ComposerDialogueTiming timing)
    {
        _nameText.text = speaker ?? "";
        _minContinueDelay = Mathf.Max(0f, float.IsNaN(timing.MinContinueDelaySeconds) ? 0f : timing.MinContinueDelaySeconds);
        _autoContinue = float.IsNaN(timing.AutoContinueSeconds) ? 0f : Mathf.Max(0f, timing.AutoContinueSeconds);
        _autoContinued = false;
        _shownAt = Time.unscaledTime;
        _completedAt = float.NegativeInfinity;
        _lastInputFrame = Time.frameCount; // the press that opened this screen never counts
        _continueHint.alpha = 0f;

        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _typewriter.Play(text, timing.CharactersPerSecond, timing.PunctuationPauseSeconds);
        if (!_typewriter.IsRevealing) _completedAt = Time.unscaledTime;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_bodyText.transform.parent);
    }

    /// <summary>Same line, new language — keeps the reveal position.</summary>
    public void ReplaceText(string text)
    {
        if (!IsVisible) return;
        _typewriter.ReplaceText(text);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_bodyText.transform.parent);
    }

    /// <summary>Hides and empties the box (old text never flashes when it opens again).</summary>
    public void Hide()
    {
        if (_typewriter != null) _typewriter.Clear();
        if (_nameText != null) _nameText.text = "";
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── Input ────────────────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData) => Press();

    /// <summary>One player press (also used by tests). Returns what it did.</summary>
    public PressResult Press()
    {
        if (!IsVisible) return PressResult.Ignored;
        if (Time.frameCount == _lastInputFrame) return PressResult.Ignored;
        _lastInputFrame = Time.frameCount;

        if (_typewriter.IsRevealing)
        {
            _typewriter.Complete();
            return PressResult.Completed;
        }
        if (!CanContinue) return PressResult.Ignored;
        ContinueRequested?.Invoke();
        return PressResult.Continued;
    }

    public enum PressResult { Ignored, Completed, Continued }

    private void Update()
    {
        if (!IsVisible) return;

        if (_continueHint != null)
            _continueHint.alpha = CanContinue ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;

        var kb = Keyboard.current;
        var pad = Gamepad.current;
        bool pressed = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                    || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        if (pressed) { Press(); return; }

        if (_autoContinue > 0f && !_autoContinued && CanContinue && Time.unscaledTime - _completedAt >= _autoContinue)
        {
            _autoContinued = true; // once per line, even if nobody hides the box
            _lastInputFrame = Time.frameCount;
            ContinueRequested?.Invoke();
        }
    }
}
