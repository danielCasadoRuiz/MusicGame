using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The compact side tip cards shown while the countdowns run, the same from the very first game (no tutorial, no
/// confirmation, nothing to read before playing):
///   preRun   — the bonuses and their effect, from GameStartedEvent until the race starts (warmup time).
///   preFight — the player's essential moves, from the round intro (after the bonus conversion) until Fighting.
/// The card sits beside the action (left edge, under the HUD), never over the countdown; content that does not fit
/// is paged in the same space with a short fade (never shrunk). "More info" pauses the game and opens the full
/// help straight on its section (the owner's OpenHelpFromTip); closing it resumes the countdown exactly where it
/// stopped. Timings: HelpUIConfigSO. Content: BonusHelpCatalog / MoveHelpCatalog. Never shown during the action.
/// Lives in the UI scene (UIFlowController); TipCard.prefab (UIPrefabLibrarySO.tipCard).
/// </summary>
public class HelpTipsController : MonoBehaviour
{
    public static HelpTipsController Instance { get; private set; }

    public enum Card { None, Bonuses, Moves }

    public Card Showing { get; private set; }
    public int Page { get; private set; }
    public int PageCount => _pages.Count;
    /// <summary>The card on screen (null when none) — e.g. for the rival's bubble to keep clear of it.</summary>
    public RectTransform CardRect => Showing != Card.None && _view != null && _view.root.gameObject.activeInHierarchy ? _view.root : null;

    private TipCardView _view;
    private Coroutine _routine;
    private readonly List<GameObject> _rows = new();
    private readonly List<List<System.Action<TipRowView>>> _pages = new();

    // Rows per page: the bonuses fit one page (the race countdown is short); moves page by group.
    private const int BonusRowsPerPage = 11;

    private System.Action<GameStartedEvent> _onGameStarted;
    private System.Action<GameEndedEvent> _onGameEnded;
    private System.Action<FightFlowStateChangedEvent> _onFightFlow;
    private System.Action<GameFlowStateChangedEvent> _onGameFlow;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.tipCard : null, UIFactory.RootCanvas(), "TipCard");
        if (_view == null) { enabled = false; return; }
        _view.moreInfoButton.onClick.AddListener(OnMoreInfo);
        _view.root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_view != null) Destroy(_view.gameObject);
    }

    private void OnEnable()
    {
        // preRun: the race countdown is GameStartedEvent's warmup (the first start and every restart).
        _onGameStarted = e => ShowFor(Card.Bonuses, e.WarmupTime);
        _onGameEnded = _ => Hide();
        _onFightFlow = e => { if (e.Current != FightFlowState.RoundIntro && e.Current != FightFlowState.Countdown) Hide(Card.Moves); };
        _onGameFlow = e => { if (e.Previous != e.Current) Hide(); };
        EventBus.Subscribe(_onGameStarted);
        EventBus.Subscribe(_onGameEnded);
        EventBus.Subscribe(_onFightFlow);
        EventBus.Subscribe(_onGameFlow);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onGameStarted);
        EventBus.Unsubscribe(_onGameEnded);
        EventBus.Unsubscribe(_onFightFlow);
        EventBus.Unsubscribe(_onGameFlow);
        Hide();
    }

    /// <summary>preFight: called by RoundIntroController once the conversion is over (the round intro starts);
    /// stays until Fighting. Later rounds start on the next page, so the player sees every page over a match.</summary>
    public void ShowMoves(int round) => ShowFor(Card.Moves, -1f, Mathf.Max(0, round - 1));

    public void Hide(Card only = Card.None)
    {
        if (only != Card.None && Showing != only) return;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        Showing = Card.None;
        if (_view != null) _view.root.gameObject.SetActive(false);
    }

    private void ShowFor(Card card, float seconds, int firstPage = 0)
    {
        if (_view == null) return;
        Hide();
        BuildPages(card);
        if (_pages.Count == 0) return;
        Showing = card;
        Loc.Set(_view.titleText, card == Card.Bonuses ? "Help.Tip.BonusTitle" : "Help.Tip.MovesTitle");
        Loc.Set(_view.moreInfoLabel, "Help.Tip.More");
        Loc.Set(_view.pauseHintText, "Help.Tip.PauseHint");
        var cfg = HelpUIConfigSO.Instance;
        if (cfg != null)
        {
            _view.root.anchoredPosition = card == Card.Bonuses ? cfg.runnerTipPosition : cfg.fightTipPosition;
            _view.root.sizeDelta = new Vector2(cfg.tipWidth, _view.root.sizeDelta.y);
            var grid = _view.rows.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            if (grid != null)
            {
                int columns = card == Card.Bonuses ? Mathf.Max(1, cfg.bonusTipColumns) : 1;
                var pad = grid.padding;
                grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
                float inner = cfg.tipWidth - 24f - pad.left - pad.right - grid.spacing.x * (columns - 1);
                grid.cellSize = new Vector2(inner / columns, grid.cellSize.y);
            }
        }
        _view.root.gameObject.SetActive(true);
        _view.root.SetAsLastSibling();
        _routine = StartCoroutine(Run(seconds, firstPage % _pages.Count));
    }

    // Scaled time: the pause (help open included) freezes the paging and the card's lifetime with the countdown.
    private IEnumerator Run(float seconds, int page)
    {
        var cfg = HelpUIConfigSO.Instance;
        float pageSeconds = cfg != null ? cfg.tipPageSeconds : 2.2f, fade = cfg != null ? cfg.tipFadeSeconds : 0.25f;
        float alive = 0f, onPage = 0f;
        _view.group.alpha = 0f;
        ShowPage(page);
        for (float t = 0f; t < fade; t += Time.deltaTime) { _view.group.alpha = t / fade; yield return null; }
        _view.group.alpha = 1f;
        while (seconds < 0f || alive < seconds)
        {
            alive += Time.deltaTime; onPage += Time.deltaTime;
            if (_pages.Count > 1 && onPage >= pageSeconds)
            {
                for (float t = 0f; t < fade; t += Time.deltaTime) { _view.pageGroup.alpha = 1f - t / fade; yield return null; }
                page = (page + 1) % _pages.Count;
                ShowPage(page);
                for (float t = 0f; t < fade; t += Time.deltaTime) { _view.pageGroup.alpha = t / fade; yield return null; }
                _view.pageGroup.alpha = 1f;
                onPage = 0f;
            }
            yield return null;
        }
        _routine = null;
        Hide();
    }

    private void ShowPage(int page)
    {
        Page = page;
        foreach (var r in _rows) if (r != null) Destroy(r);
        _rows.Clear();
        foreach (var fill in _pages[page])
        {
            var row = Instantiate(_view.rowPrefab, _view.rows, false);
            row.gameObject.SetActive(true);
            fill(row);
            _rows.Add(row.gameObject);
        }
        _view.pageText.gameObject.SetActive(_pages.Count > 1);
        _view.pageText.text = _pages.Count > 1 ? $"{page + 1}/{_pages.Count}" : "";
        _view.pageGroup.alpha = 1f;
    }

    private void BuildPages(Card card)
    {
        _pages.Clear();
        var cfg = HelpUIConfigSO.Instance;
        if (card == Card.Bonuses)
        {
            var page = new List<System.Action<TipRowView>>();
            foreach (var e in BonusHelpCatalog.Build())
            {
                var entry = e;
                if (page.Count == BonusRowsPerPage) { _pages.Add(page); page = new List<System.Action<TipRowView>>(); }
                page.Add(row =>
                {
                    row.keysText.gameObject.SetActive(false);
                    row.icon.gameObject.SetActive(true);
                    row.icon.sprite = cfg != null ? cfg.Icon(entry.Kind) : null;
                    row.icon.color = cfg != null ? cfg.Tint(entry.Kind) : Color.white;
                    row.labelText.text = $"<b>{entry.Name}</b>  {entry.ShortEffect}";
                });
            }
            if (page.Count > 0) _pages.Add(page);
        }
        else
        {
            var wallet = GameSession.Instance != null ? GameSession.Instance.Run.Wallet : null;
            var entries = MoveHelpCatalog.Build(wallet);
            foreach (MoveHelpCatalog.Group g in System.Enum.GetValues(typeof(MoveHelpCatalog.Group)))
            {
                var page = new List<System.Action<TipRowView>>();
                foreach (var e in entries)
                {
                    if (e.Group != g || !e.Essential) continue;
                    var entry = e;
                    page.Add(row =>
                    {
                        // The charge it spends as its icon; free moves keep the slot empty (columns stay aligned).
                        bool costs = entry.Cost != CombatResourceType.None;
                        var kind = CombatResourceUse.ToBonusKind(entry.Cost);
                        row.icon.gameObject.SetActive(true);
                        row.icon.enabled = costs;
                        if (costs) { row.icon.sprite = cfg != null ? cfg.Icon(kind) : null; row.icon.color = cfg != null ? cfg.Tint(kind) : Color.white; }
                        row.keysText.gameObject.SetActive(true);
                        row.keysText.text = entry.Keys;
                        row.labelText.text = string.IsNullOrEmpty(entry.Short) ? entry.Name : $"{entry.Name}  <alpha=#AA><size=85%>{entry.Short}</size>";
                    });
                }
                if (page.Count > 0) _pages.Add(page);
            }
        }
    }

    private void OnMoreInfo()
    {
        if (Showing == Card.Bonuses) PauseController.Instance?.OpenHelpFromTip();
        else if (Showing == Card.Moves) FightController.Instance?.OpenHelpFromTip();
    }
}
