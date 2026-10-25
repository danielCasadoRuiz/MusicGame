using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PERFIL (Main Menu → profile icon): the player's long-term profile from PlayerProgressService.Profile —
///   · usual combat strength: the running average of every run's CONVERTED build (the four attributes the fight
///     received), with the best total and the last run — how good this player usually arrives at a fight, the seed of
///     a future matchmaking against other players;
///   · the music styles the player picks themselves (manual Song Selection choices), the most picked one deciding how
///     their avatar should look (PlayerAvatarConfigSO.styleRecipes — no outfits authored yet: the picture is a
///     placeholder tinted by the style).
/// Read-only. Lives in the UI scene (UIFlowController); ProfileScreen.prefab (UIPrefabLibrarySO.profileScreen).
/// </summary>
public class ProfileScreenController : MonoBehaviour
{
    public static ProfileScreenController Instance { get; private set; }

    private ProfileScreenView _view;
    private System.Action<GameFlowStateChangedEvent> _onFlow;
    private System.Action _onLanguage;

    public bool IsOpen => _view != null && _view.root.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        var lib = UIPrefabLibrarySO.Instance;
        _view = UIPrefabLibrarySO.Spawn(lib != null ? lib.profileScreen : null, UIFactory.RootCanvas(), "ProfileScreen");
        if (_view == null) return;
        UIFactory.Stretch((RectTransform)_view.transform);
        if (_view.backButton != null) _view.backButton.onClick.AddListener(Close);
        _view.root.SetActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnEnable()
    {
        _onFlow = e => { if (e.Previous == GameFlowState.MainMenu) Close(); };
        _onLanguage = () => { if (IsOpen) Refresh(); };
        EventBus.Subscribe(_onFlow);
        Loc.Refreshed += _onLanguage;
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlow);
        Loc.Refreshed -= _onLanguage;
    }

    public void Open()
    {
        if (_view == null) return;
        Refresh();
        _view.root.SetActive(true);
        _view.transform.SetAsLastSibling();
    }

    public void Close() { if (_view != null) _view.root.SetActive(false); }

    private void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) Close();
    }

    private void Refresh()
    {
        var progress = PlayerProgressService.Instance;
        var p = progress != null ? progress.Profile : null;
        var cfg = HelpUIConfigSO.Instance;
        var statsCfg = Resources.Load<AppConfigSO>("AppConfig")?.fightStats;
        float full = statsCfg != null ? statsCfg.maxStatPoints : 40f;

        Loc.Set(_view.titleText, "Profile.Title");
        Loc.Set(_view.strengthHeader, "Profile.Strength");
        Loc.Set(_view.stylesHeader, "Profile.Styles");
        Loc.Set(_view.backLabel, "Profile.Back");

        foreach (var row in _view.stats)
        {
            if (row == null) continue;
            float v = p == null ? 0f : row.stat switch
            {
                FightStatId.PunchPower => p.avgPunchPower, FightStatId.KickPower => p.avgKickPower,
                FightStatId.Resistance => p.avgResistance, FightStatId.Agility => p.avgAgility, _ => 0f,
            };
            if (row.label != null) row.label.text = BonusHelpCatalog.StatName(row.stat);
            if (row.icon != null && cfg != null) { row.icon.sprite = cfg.StatSprite(row.stat); row.icon.color = cfg.StatColor(row.stat); }
            if (row.fill != null) { UIFactory.EnsureFillSprite(row.fill); if (cfg != null) row.fill.color = cfg.StatColor(row.stat); row.fill.fillAmount = Mathf.Clamp01(v / full); }
            if (row.value != null) row.value.text = p != null && p.runs > 0 ? v.ToString("0.#") : "–";
        }

        bool any = p != null && p.runs > 0;
        _view.summaryText.text = any
            ? Loc.Get("Profile.Summary", p.AverageTotal.ToString("0.#"), p.bestTotal.ToString("0.#"), p.runs.ToString())
            : Loc.Get("Profile.NoRuns");
        _view.lastRunText.text = any
            ? Loc.Get("Profile.LastRun", p.lastPunchPower.ToString("0.#"), p.lastKickPower.ToString("0.#"), p.lastResistance.ToString("0.#"), p.lastAgility.ToString("0.#"))
            : "";

        var picks = p != null ? p.stylePicks.Where(x => x.count > 0).OrderByDescending(x => x.count).ToList() : new System.Collections.Generic.List<StylePickData>();
        _view.stylesText.text = picks.Count == 0
            ? Loc.Get("Profile.NoStyles")
            : string.Join("   ", picks.Take(5).Select(x => $"{Loc.Get($"GameStyle.{(GameMusicStyle)x.style}")} ×{x.count}"));

        var preferred = progress != null ? progress.PreferredStyle : GameMusicStyle.Unknown;
        _view.lookText.text = preferred == GameMusicStyle.Unknown
            ? Loc.Get("Profile.LookNone")
            : Loc.Get("Profile.Look", Loc.Get($"GameStyle.{preferred}"));
        if (_view.portrait != null) _view.portrait.color = StyleTint(preferred);
        Loc.Set(_view.noteText, "Profile.Note");
    }

    // Placeholder picture tint per style family until there is a real portrait / outfit.
    private static Color StyleTint(GameMusicStyle s) => s switch
    {
        GameMusicStyle.Unknown => new Color(0.5f, 0.5f, 0.55f),
        GameMusicStyle.Classical or GameMusicStyle.Cinematic => new Color(0.75f, 0.62f, 0.35f),
        GameMusicStyle.Rock or GameMusicStyle.Heavy => new Color(0.75f, 0.25f, 0.25f),
        GameMusicStyle.Electronic or GameMusicStyle.Dance => new Color(0.3f, 0.6f, 0.95f),
        GameMusicStyle.Groove or GameMusicStyle.Funky => new Color(0.85f, 0.5f, 0.2f),
        GameMusicStyle.Soulful or GameMusicStyle.Jazzy => new Color(0.55f, 0.35f, 0.75f),
        _ => new Color(0.4f, 0.75f, 0.5f),
    };
}
