using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RIVALS — the rival collection, opened from the Main Menu. One tile per roster rival; a tap opens
/// that rival's versions. NON-LINEAR collection: every rival VERSION (OpponentLevelConfig.level) is
/// discovered only by actually defeating it (PlayerProgressService.DefeatedLevels) — beating Lv4 says
/// nothing about Lv1–3.
///   Tile:   never defeated → black silhouette + "???"; otherwise the portrait of the HIGHEST version
///           actually defeated, the name and "defeated / defined" versions.
///   Detail: every defined version, each lit (defeated) or a silhouette (undefeated), independently.
/// Silhouettes = the existing portrait sprite drawn with a black tint (its alpha keeps the shape) — no
/// texture is modified or duplicated. Read-only view of PlayerProgressService + the roster; built
/// procedurally (UIFactory), lives in the UI scene (UIFlowController).
/// </summary>
public class RivalCollectionController : MonoBehaviour
{
    public static RivalCollectionController Instance { get; private set; }

    private static readonly Color SilhouetteColor = new(0f, 0f, 0f, 0.92f);
    private static readonly Color CellColor = new(1f, 1f, 1f, 0.10f);
    // Fixed layout: two balanced rows of six (12 rivals today); more rivals simply add rows.
    private const int Columns = 6;
    private static readonly Vector2 CardSize = new(172f, 252f);
    private static readonly Vector2 CardSpacing = new(18f, 22f);

    private OpponentRosterSO _roster;
    private RectTransform _root, _grid, _detail, _detailRow;
    private TextMeshProUGUI _summaryText, _detailTitle, _detailBio;
    private readonly List<GameObject> _spawned = new();
    private readonly List<GameObject> _detailSpawned = new();
    private System.Action<GameFlowStateChangedEvent> _onFlow;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        _roster = app != null ? app.opponentRoster : null;
        Build();
        _root.gameObject.SetActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        _onFlow = e => { if (e.Previous == GameFlowState.MainMenu) Close(); };
        EventBus.Subscribe(_onFlow);
    }

    private void OnDisable() => EventBus.Unsubscribe(_onFlow);

    public void Open()
    {
        Refresh();
        _detail.gameObject.SetActive(false);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    public void Close()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    // ── Build ────────────────────────────────────────────────────────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("RivalCollectionScreen", canvas);
        UIFactory.Stretch(_root);

        var dim = UIFactory.CreatePanel("Dim", _root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var title = UIFactory.CreateText("Title", _root, Loc.Get("Rivals.Title"), 34, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(900f, 50f));
        title.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        _summaryText = UIFactory.CreateText("Summary", _root, "", 18, Color.white);
        UIFactory.SetBox(_summaryText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(900f, 28f));
        _summaryText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        _grid = UIFactory.CreateRect("Grid", _root);
        UIFactory.SetBox(_grid, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f),
            new Vector2(Columns * CardSize.x + (Columns - 1) * CardSpacing.x, 2 * CardSize.y + CardSpacing.y));
        var layout = _grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = CardSize;
        layout.spacing = CardSpacing;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = Columns;

        var back = UIFactory.CreateButton("BackButton", _root, Loc.Get("Rivals.Back"), out var backLabel);
        UIFactory.SetBox(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(240f, 52f));
        back.onClick.AddListener(Close);
        back.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        backLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        // Detail overlay (one rival's versions).
        _detail = UIFactory.CreateRect("Detail", _root);
        UIFactory.Stretch(_detail);
        var detailDim = UIFactory.CreatePanel("DetailDim", _detail, new Color(0f, 0f, 0f, 0.8f));
        UIFactory.Stretch(detailDim.rectTransform);
        var panel = UIFactory.CreatePanel("Panel", _detail, new Color(0.1f, 0.1f, 0.12f, 1f));
        UIFactory.SetBox(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 660f));
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        _detailTitle = UIFactory.CreateText("DetailTitle", panel.rectTransform, "", 28, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(_detailTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1000f, 40f));
        _detailTitle.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        _detailRow = UIFactory.CreateRect("Versions", panel.rectTransform);
        UIFactory.SetBox(_detailRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 75f), new Vector2(1040f, 300f));
        // Composer biography — one per composer (OpponentDefinition.BiographyKey), never per version.
        _detailBio = UIFactory.CreateText("Biography", panel.rectTransform, "", 17, Color.white, TextAlignmentOptions.TopLeft);
        UIFactory.SetBox(_detailBio.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(980f, 140f));
        _detailBio.textWrappingMode = TextWrappingModes.Normal;
        _detailBio.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        var row = _detailRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 18f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        var closeDetail = UIFactory.CreateButton("CloseDetail", panel.rectTransform, Loc.Get("Rivals.Back"), out var closeLabel);
        UIFactory.SetBox(closeDetail.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(220f, 48f));
        closeDetail.onClick.AddListener(() => _detail.gameObject.SetActive(false));
        closeDetail.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        closeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        _detail.gameObject.SetActive(false);
    }

    // ── Content ──────────────────────────────────────────────────────────────

    private static int[] DefinedLevels(OpponentDefinition o) =>
        o.levels == null ? System.Array.Empty<int>() : o.levels.Where(l => l != null && l.level > 0).Select(l => l.level).Distinct().OrderBy(l => l).ToArray();

    private static int[] DefeatedDefinedLevels(OpponentDefinition o)
    {
        var progress = PlayerProgressService.Instance;
        if (progress == null) return System.Array.Empty<int>();
        var defeated = progress.DefeatedLevels(o.id);
        return DefinedLevels(o).Where(defeated.Contains).ToArray(); // stored ids no longer defined are ignored
    }

    private static Sprite PortraitFor(OpponentDefinition o, int level)
    {
        var cfg = o.levels?.FirstOrDefault(l => l != null && l.level == level);
        if (cfg != null && cfg.portrait != null) return cfg.portrait;
        return o.levels?.FirstOrDefault(l => l != null && l.portrait != null)?.portrait ?? o.defaultConfig?.portrait;
    }

    private void Refresh()
    {
        foreach (var go in _spawned) Destroy(go);
        _spawned.Clear();
        var rivals = _roster != null ? _roster.opponents.Where(o => o != null).ToArray() : System.Array.Empty<OpponentDefinition>();

        var layout = _grid.GetComponent<GridLayoutGroup>();

        int found = 0, total = 0;
        foreach (var rival in rivals)
        {
            var defined = DefinedLevels(rival);
            var defeated = DefeatedDefinedLevels(rival);
            total += defined.Length;
            found += defeated.Length;
            bool discovered = defeated.Length > 0;
            int shownLevel = discovered ? defeated.Max() : (defined.Length > 0 ? defined.Max() : 0);
            var cell = BuildCard(_grid, layout.cellSize, PortraitFor(rival, shownLevel), discovered,
                                 discovered ? rival.displayName : "???",
                                 discovered ? $"{Loc.Get("Rivals.Level", shownLevel.ToString())}  ·  {defeated.Length} / {defined.Length}"
                                            : $"{defeated.Length} / {defined.Length}");
            var r = rival;
            cell.onClick.AddListener(() => OpenDetail(r));
            _spawned.Add(cell.gameObject);
        }
        _summaryText.text = Loc.Get("Rivals.Summary", found.ToString(), total.ToString());
    }

    private void OpenDetail(OpponentDefinition rival)
    {
        foreach (var go in _detailSpawned) Destroy(go);
        _detailSpawned.Clear();
        var defined = DefinedLevels(rival);
        var defeated = DefeatedDefinedLevels(rival);
        _detailTitle.text = defeated.Length > 0 ? rival.displayName : "???";
        string bio = Loc.Get(rival.BiographyKey);
        // Revealed once ANY version was defeated (same text whatever the version); undiscovered keeps the mystery.
        _detailBio.text = defeated.Length == 0 ? Loc.Get("Rivals.BioLocked")
                        : bio == rival.BiographyKey ? "" : bio; // missing entry → no text, never the raw key
        var size = new Vector2(Mathf.Min(220f, (_detailRow.sizeDelta.x - 18f * (defined.Length - 1)) / Mathf.Max(1, defined.Length)), 290f);
        foreach (int level in defined)
        {
            bool won = defeated.Contains(level);
            var card = BuildCard(_detailRow, size, PortraitFor(rival, level), won, Loc.Get("Rivals.Level", level.ToString()),
                                 Loc.Get(won ? "Rivals.Defeated" : "Rivals.Undefeated"));
            card.interactable = false;
            card.GetComponent<RectTransform>().sizeDelta = size;
            _detailSpawned.Add(card.gameObject);
        }
        _detail.gameObject.SetActive(true);
        _detail.SetAsLastSibling();
    }

    // One portrait card: lit portrait when `revealed`, else the same sprite as a black silhouette.
    private static Button BuildCard(RectTransform parent, Vector2 size, Sprite portrait, bool revealed, string name, string footer)
    {
        var btn = UIFactory.CreateButton("Card", parent, name, out var nameLabel);
        btn.GetComponent<Image>().color = CellColor;
        UIFactory.SetBox(nameLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(-8f, 24f));
        nameLabel.fontSize = 14;

        var footerText = UIFactory.CreateText("Footer", btn.GetComponent<RectTransform>(), footer, 13, new Color(1f, 1f, 1f, 0.75f));
        UIFactory.SetBox(footerText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(-8f, 22f));

        var portraitRt = UIFactory.CreateRect("Portrait", btn.GetComponent<RectTransform>());
        UIFactory.SetBox(portraitRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-10f, size.y - 60f));
        var img = portraitRt.gameObject.AddComponent<Image>();
        img.sprite = portrait;
        img.preserveAspect = true;
        img.color = portrait == null ? new Color(1f, 1f, 1f, 0.15f) : revealed ? Color.white : SilhouetteColor;
        return btn;
    }
}
