using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// COL·LECCIÓ — the rival collection, opened from the Main Menu. One card per roster rival; a tap opens that
/// rival's versions. NON-LINEAR collection: every rival VERSION (OpponentLevelConfig.level) is discovered only
/// by actually defeating it (PlayerProgressService.DefeatedLevels) — beating Lv4 says nothing about Lv1–3.
///   Card:   never defeated → black silhouette + "???"; otherwise the portrait of the HIGHEST version
///           actually defeated, the name and "defeated / defined" versions.
///   Detail: every defined version, each lit (defeated) or a silhouette (undefeated), independently.
///   Final rival: its card appears only once revealed (first campaign completed); never part of the NORMAL
///           collection required to unlock it.
/// Structure and look: prefabs RivalCollectionScreen + RivalCard (UIPrefabLibrarySO); this controller fills data.
/// </summary>
public class RivalCollectionController : MonoBehaviour
{
    public static RivalCollectionController Instance { get; private set; }

    private OpponentRosterSO _roster;
    private RivalCollectionView _view;
    private readonly List<GameObject> _spawned = new();
    private readonly List<GameObject> _detailSpawned = new();
    private System.Action<GameFlowStateChangedEvent> _onFlow;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        var app = Resources.Load<AppConfigSO>("AppConfig");
        _roster = app != null ? app.opponentRoster : null;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.collectionScreen : null, UIFactory.RootCanvas(), "RivalCollectionScreen");
        if (_view == null) return;
        UIFactory.Stretch((RectTransform)_view.transform);
        if (_view.backButton != null) _view.backButton.onClick.AddListener(Close);
        if (_view.detailClose != null) _view.detailClose.onClick.AddListener(() => _view.detailRoot.gameObject.SetActive(false));
        if (_view.grid == null || _view.detailRoot == null || _view.detailRow == null)
            Debug.LogError("[RivalCollectionController] RivalCollectionScreen.prefab references missing (grid / detailRoot / detailRow).");
        _view.gameObject.SetActive(false);
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
        if (_view == null) return;
        Refresh();
        if (_view.detailRoot != null) _view.detailRoot.gameObject.SetActive(false);
        _view.gameObject.SetActive(true);
        _view.transform.SetAsLastSibling();
    }

    public void Close() { if (_view != null) _view.gameObject.SetActive(false); }

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
        Clear(_spawned);
        var rivals = _roster != null ? _roster.opponents.Where(o => o != null).ToArray() : System.Array.Empty<OpponentDefinition>();
        int found = 0, total = 0;
        foreach (var rival in rivals)
        {
            var defined = DefinedLevels(rival);
            var defeated = DefeatedDefinedLevels(rival);
            total += defined.Length;
            found += defeated.Length;
            bool discovered = defeated.Length > 0;
            int shownLevel = discovered ? defeated.Max() : (defined.Length > 0 ? defined.Max() : 0);
            var card = SpawnCard(_view.grid, _spawned);
            if (card == null) break;
            card.Set(PortraitFor(rival, shownLevel), discovered, discovered ? rival.displayName : "???",
                     discovered ? $"{Loc.Get("Rivals.Level", shownLevel.ToString())}  ·  {defeated.Length} / {defined.Length}" : $"{defeated.Length} / {defined.Length}", true);
            var r = rival;
            card.button?.onClick.AddListener(() => OpenDetail(r));
        }

        var boss = CampaignService.Boss;
        bool bossShown = CampaignService.Data != null && CampaignService.Data.bossRevealed && boss != null && boss.opponent != null;
        if (bossShown)
        {
            var o = boss.opponent;
            var defined = DefinedLevels(o);
            var defeated = DefeatedDefinedLevels(o);
            bool discovered = defeated.Length > 0;
            int shownLevel = discovered ? defeated.Max() : (defined.Length > 0 ? defined.Min() : 0);
            var card = SpawnCard(_view.grid, _spawned);
            if (card != null)
            {
                card.Set(boss.silhouette != null && !discovered ? boss.silhouette : PortraitFor(o, shownLevel), discovered,
                         discovered ? Loc.Get(boss.nameKey) : "???", $"{defeated.Length} / {defined.Length}", true);
                card.button?.onClick.AddListener(() => OpenDetail(o));
            }
            if (_view.summary != null) Loc.Set(_view.summary, "Rivals.SummaryBoss", found.ToString(), total.ToString(), defeated.Length.ToString(), defined.Length.ToString());
        }
        else if (_view.summary != null) Loc.Set(_view.summary, "Rivals.Summary", found.ToString(), total.ToString());
    }

    private void OpenDetail(OpponentDefinition rival)
    {
        Clear(_detailSpawned);
        var defined = DefinedLevels(rival);
        var defeated = DefeatedDefinedLevels(rival);
        bool isBoss = CampaignService.Boss != null && CampaignService.Boss.opponent == rival;
        if (_view.detailTitle != null) _view.detailTitle.text = defeated.Length > 0 ? (isBoss ? Loc.Get(CampaignService.Boss.nameKey) : rival.displayName) : "???";
        string bio = Loc.Get(rival.BiographyKey);
        // Revealed once ANY version was defeated (same text whatever the version); undiscovered keeps the mystery.
        if (_view.detailBio != null)
            _view.detailBio.text = defeated.Length == 0 ? Loc.Get("Rivals.BioLocked") : bio == rival.BiographyKey ? "" : bio; // missing entry → no text
        foreach (int level in defined)
        {
            bool won = defeated.Contains(level);
            var card = SpawnCard(_view.detailRow, _detailSpawned);
            if (card == null) break;
            card.Set(PortraitFor(rival, level), won, Loc.Get("Rivals.Level", level.ToString()), Loc.Get(won ? "Rivals.Defeated" : "Rivals.Undefeated"), false);
        }
        _view.detailRoot.gameObject.SetActive(true);
        _view.detailRoot.SetAsLastSibling();
    }

    private static RivalCardView SpawnCard(RectTransform parent, List<GameObject> track)
    {
        var lib = UIPrefabLibrarySO.Instance;
        var card = UIPrefabLibrarySO.Spawn(lib != null ? lib.rivalCard : null, parent, "RivalCard");
        if (card != null) track.Add(card.gameObject);
        return card;
    }

    private static void Clear(List<GameObject> list)
    {
        foreach (var go in list) if (go != null) { go.SetActive(false); Destroy(go); }
        list.Clear();
    }
}
