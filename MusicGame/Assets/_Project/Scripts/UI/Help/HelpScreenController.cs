using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum HelpTopic { Bonuses, Moves }

/// <summary>Where the help was opened from — decides where closing it goes.</summary>
public enum HelpOrigin
{
    /// <summary>A tip card's "More info": closing resumes the countdown directly (no pause menu).</summary>
    TipCard,
    /// <summary>The pause menu: closing returns to the pause menu, the game stays paused.</summary>
    PauseMenu,
}

/// <summary>
/// The ONE full help screen (bonuses / moves) for both ways in. The owner (runner PauseController / fight
/// FightController) pauses the game, then calls Open with the origin and a callback; closing — the close button,
/// Escape, or the gamepad's East / Start button — always goes through the same Close, which hands the origin back
/// so the owner resumes (tip card) or shows its pause menu again (pause menu). The game is paused
/// (Time.timeScale = 0) all the time it is open; this screen only uses unscaled UI input, so it keeps responding.
/// ClosedThisFrame lets the pause controllers ignore the same Escape press that closed it (it never also toggles the
/// pause, and the owners resume one frame later so the closing key/click never reaches gameplay).
/// Content is built from BonusHelpCatalog / MoveHelpCatalog — the same data the tip cards show.
/// Lives in the UI scene (UIFlowController); HelpScreen.prefab (UIPrefabLibrarySO.helpScreen).
/// </summary>
public class HelpScreenController : MonoBehaviour
{
    public static HelpScreenController Instance { get; private set; }

    public static bool IsOpen => Instance != null && Instance._open;
    /// <summary>True on the frame the help closed (its Escape must not also reach the pause toggle).</summary>
    public static bool ClosedThisFrame => Instance != null && Instance._closedFrame == Time.frameCount;

    public HelpTopic Topic { get; private set; }
    public HelpOrigin Origin { get; private set; }

    private HelpScreenView _view;
    private bool _open;
    private int _openedFrame = -1, _closedFrame = -1;
    private Action<HelpOrigin> _onClosed;
    private readonly List<GameObject> _spawned = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.helpScreen : null, UIFactory.RootCanvas(), "HelpScreen");
        if (_view == null) { enabled = false; return; }
        UIFactory.Stretch((RectTransform)_view.transform);
        _view.closeButton.onClick.AddListener(Close);
        _view.root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_view != null) Destroy(_view.gameObject);
    }

    public void Open(HelpTopic topic, HelpOrigin origin, Action<HelpOrigin> onClosed)
    {
        if (_view == null) { onClosed?.Invoke(origin); return; }
        Topic = topic; Origin = origin; _onClosed = onClosed;
        _open = true;
        _openedFrame = Time.frameCount;
        Populate();
        _view.root.gameObject.SetActive(true);
        _view.root.SetAsLastSibling();
        // Above both pause menus and the bonus block — applied on every show: a prefab's root Canvas never keeps
        // overrideSorting and a nested canvas loses it when activated.
        var canvas = _view.GetComponent<Canvas>();
        if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = 300; }
        if (_view.scroll != null) _view.scroll.verticalNormalizedPosition = 1f;
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        _closedFrame = Time.frameCount;
        _view.root.gameObject.SetActive(false);
        var cb = _onClosed; _onClosed = null;
        cb?.Invoke(Origin);
    }

    private void Update()
    {
        if (!_open || Time.frameCount == _openedFrame) return;   // the press that opened it never closes it
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        if ((kb != null && kb.escapeKey.wasPressedThisFrame) ||
            (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
            Close();
    }

    // ── Content ──────────────────────────────────────────────────────────────

    private void Populate()
    {
        foreach (var go in _spawned) if (go != null) Destroy(go);
        _spawned.Clear();

        Loc.Set(_view.titleText, Topic == HelpTopic.Bonuses ? "Help.Bonus.HelpTitle" : "Help.Moves.Title");
        Loc.Set(_view.closeLabel, Origin == HelpOrigin.PauseMenu ? "Help.Back" : "Help.Continue");
        Loc.Set(_view.hintText, Origin == HelpOrigin.PauseMenu ? "Help.HintBack" : "Help.HintContinue");

        if (Topic == HelpTopic.Bonuses) PopulateBonuses();
        else PopulateMoves();
    }

    private void PopulateBonuses()
    {
        var cfg = HelpUIConfigSO.Instance;
        Loc.Bind(_view.introText, BonusHelpCatalog.Intro);
        foreach (var e in BonusHelpCatalog.Build())
            Row(cfg != null ? cfg.Icon(e.Kind) : null, cfg != null ? cfg.Tint(e.Kind) : Color.white, null, e.Name, e.FullText);
        Header(Loc.Get("Help.Bonus.StatsHeader"));
        foreach (var stat in BonusHelpCatalog.BuildStats)
            Row(cfg != null ? cfg.StatSprite(stat) : null, cfg != null ? cfg.StatColor(stat) : Color.white, null,
                BonusHelpCatalog.StatName(stat), BonusHelpCatalog.StatDescription(stat));
    }

    private void PopulateMoves()
    {
        Loc.Bind(_view.introText, () => Loc.Get(FightInputGlyphs.Keyboard ? "Help.Moves.Intro" : "Help.Moves.IntroMobile"));
        var cfg = HelpUIConfigSO.Instance;
        var wallet = GameSession.Instance != null ? GameSession.Instance.Run.Wallet : null;
        var entries = MoveHelpCatalog.Build(wallet);
        foreach (MoveHelpCatalog.Group g in Enum.GetValues(typeof(MoveHelpCatalog.Group)))
        {
            Header(MoveHelpCatalog.GroupName(g));
            foreach (var e in entries)
            {
                if (e.Group != g) continue;
                // The charge a move spends shows as its icon next to the keys (free moves: empty slot).
                var kind = CombatResourceUse.ToBonusKind(e.Cost);
                bool costs = e.Cost != CombatResourceType.None;
                Row(costs && cfg != null ? cfg.Icon(kind) : null, costs && cfg != null ? cfg.Tint(kind) : Color.white, e.Keys, e.Name, e.Detail, iconSlot: true);
            }
        }
    }

    private void Row(Sprite icon, Color tint, string keys, string title, string body, bool iconSlot = false)
    {
        var row = Instantiate(_view.rowPrefab, _view.content, false);
        row.gameObject.SetActive(true);
        _spawned.Add(row.gameObject);
        bool showIcon = keys == null || iconSlot;
        row.icon.gameObject.SetActive(showIcon);
        if (showIcon) { row.icon.sprite = icon; row.icon.color = tint; row.icon.enabled = icon != null; }
        row.keysText.gameObject.SetActive(keys != null);
        if (keys != null) row.keysText.text = keys;
        row.titleText.text = title;
        row.bodyText.text = body ?? "";
        row.bodyText.gameObject.SetActive(!string.IsNullOrEmpty(body));
    }

    private void Header(string text)
    {
        var h = Instantiate(_view.headerPrefab, _view.content, false);
        h.gameObject.SetActive(true);
        h.text = text;
        _spawned.Add(h.gameObject);
    }
}
