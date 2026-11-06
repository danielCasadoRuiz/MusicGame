#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// FIGHT PRACTICE (dev tool, 2026-11-05 — "una escena per provar cada combo amb el teclat"). Lives in
/// Scenes/Debug/FightPractice.unity: on Play it sets up a fight exactly like the fight probes do (a GameSession, a
/// rival, Fight.unity loaded additively, the Fighting state) and then:
///   • infinite resources for the player (x3, x4, Specials) and health for both — no bonus run needed;
///   • the rival is a dummy (idle / guarding / attacking every couple of seconds) or the real AI;
///   • an on-screen list of EVERY technique with its keys for the current view (first / third person), a ▶ button
///     that makes the player perform it, and live feedback of what the last press became (technique, axis, height,
///     landed / blocked / whiff, feints, the rival's guard).
/// Developer overlay (IMGUI, like FightDebugHUD) — never part of the shipped game UI. F1 hides / shows it.
/// </summary>
public class FightPracticeMode : MonoBehaviour
{
    [SerializeField] private string opponentId = "mozart";
    [SerializeField] private int opponentTier = 3;

    private enum RivalMode { Idle, Guard, Attack, AI }
    private static readonly string[] RivalModeNames = { "Ninot quiet", "Ninot en guàrdia", "Ninot que ataca", "IA real" };

    private FighterActor _player, _rival;
    private RivalMode _mode = RivalMode.Idle;
    private bool _visible = true;
    private bool _ready;
    private string _status = "Preparant el combat…";
    private Vector2 _scroll;
    private float _nextRivalAttack;
    private readonly List<string> _feed = new();
    private string _lastMove = "", _lastResult = "";
    private FightMoveDefinition _watchedMove;
    private bool _watchedHit;
    private System.Action<HitLandedEvent> _onLanded;
    private System.Action<HitBlockedEvent> _onBlocked;
    private System.Action<FightMovePhaseChangedEvent> _onPhase;

    // Practice never touches the real save (the fight setup publishes a run result like a real game would).
    private void Awake() => PlayerProgressStore.SuspendWrites = true;

    private IEnumerator Start()
    {
        foreach (var p in FindObjectsByType<AvatarDebugPreview>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
        var app = Resources.Load<AppConfigSO>("AppConfig");
        if (app == null) { _status = "ERROR: no AppConfig"; yield break; }
        var session = GameSession.Instance != null ? GameSession.Instance : new GameObject("PracticeGameSession").AddComponent<GameSession>();
        session.Configure(app.fightStats);
        session.Configure(app.progression);
        // A neutral runner result (the fight build comes from it); resources are topped up below anyway.
        var perf = new GamePerformance();
        foreach (var (t, a, c) in new[] { (RingType.Kick, 40, 34), (RingType.Snare, 30, 18), (RingType.Beat, 60, 50), (RingType.Onset, 25, 20), (RingType.Impact, 8, 7) })
            perf.ByType[t] = new TypePerformance { Type = t, Available = a, Collected = c, CollectionRate = (float)c / a, TimingAccuracy = 0.9f };
        var stats = new CollectionStats(); stats.AddScore(3000);
        EventBus.Publish(new GameEndedEvent { Stats = stats, Performance = perf, Resources = new RunnerResourceCounts { TripleCombos = 3, QuadCombos = 3, Lives = 2, Specials = 3 } });
        var rival = app.opponentRoster != null ? app.opponentRoster.opponents.FirstOrDefault(o => o != null && o.id == opponentId) : null;
        if (rival != null)
        {
            var tier = rival.GetConfigForTier(opponentTier, out int resolved);
            session.SelectedOpponent = rival; session.SelectedOpponentLevelConfig = tier; session.SelectedOpponentTier = resolved;
        }
        var load = SceneManager.LoadSceneAsync("Fight", LoadSceneMode.Additive);
        while (load != null && !load.isDone) yield return null;
        float deadline = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < deadline)
        {
            _player = GameObject.Find("FighterPlayer")?.GetComponent<FighterActor>();
            _rival = GameObject.Find("FighterOpponent")?.GetComponent<FighterActor>();
            if (_player != null && _rival != null && _player.Body != null && _rival.Body != null) break;
            yield return null;
        }
        if (_player == null || _rival == null) { _status = "ERROR: no s'han creat els lluitadors"; yield break; }
        EventBus.Publish(new FightFlowStateChangedEvent { Previous = FightFlowState.Countdown, Current = FightFlowState.Fighting });
        SetMode(RivalMode.Idle);
        Place(1.2f);
        _onLanded = e => { if (e.Attacker == _player) { _watchedHit = true; _lastResult = $"IMPACTE ({e.Result.FinalDamage:0} de mal)"; } else if (e.Attacker == _rival) Log($"Rival t'ha tocat: {e.Move?.debugName}"); };
        _onBlocked = e =>
        {
            if (e.Attacker == _player) { _watchedHit = true; _lastResult = "BLOQUEJAT pel rival"; }
            else if (e.Attacker == _rival) Log($"Has bloquejat: {e.Move?.debugName} ({_player.Guard?.LastSideInfo})");
        };
        _onPhase = e =>
        {
            if (e.Source != _player.MoveController || e.Move == null) return;
            if (e.Current == FighterMoveState.Startup)
            {
                _watchedMove = e.Move; _watchedHit = false; _lastResult = "…";
                var mc = _player.MoveController;
                string tech = string.IsNullOrEmpty(mc.LastTechniqueInfo) ? "" : $" [{mc.LastTechniqueInfo}]";
                _lastMove = $"{e.Move.debugName}{tech} — eix {Axis(mc.CurrentStrikeSide)}, alçada {Height(e.Move)}";
                Log(_lastMove);
            }
            else if (e.Current == FighterMoveState.Recovery && e.Move == _watchedMove && !_watchedHit && e.Move.hits != null && e.Move.hits.Length > 0)
                _lastResult = "A L'AIRE (no ha tocat el cos)";
        };
        EventBus.Subscribe(_onLanded); EventBus.Subscribe(_onBlocked); EventBus.Subscribe(_onPhase);
        _ready = true;
        _status = "";
    }

    /// <summary>Tests (FightPracticeSetup smoke): ready to play, and what the last player move became.</summary>
    public bool IsReady => _ready;
    public string LastMoveInfo => _lastMove;
    public string LastResultInfo => _lastResult;
    public FighterActor Player => _player;
    public void DemoForTest(FightMoveDefinition move) => Demo(move);

    private void OnDestroy()
    {
        PlayerProgressStore.SuspendWrites = false;
        if (_onLanded != null) EventBus.Unsubscribe(_onLanded);
        if (_onBlocked != null) EventBus.Unsubscribe(_onBlocked);
        if (_onPhase != null) EventBus.Unsubscribe(_onPhase);
    }

    private static string Axis(int s) => s > 0 ? "dret" : s < 0 ? "esquerre" : "centre";
    private static string Height(FightMoveDefinition m) =>
        m.hits == null || m.hits.Length == 0 || m.hits[0] == null ? "—" :
        m.hits[0].attackHeight == AttackHeight.High ? "alta" : m.hits[0].attackHeight == AttackHeight.Low ? "baixa" : "mitja";

    private void Log(string line)
    {
        _feed.Insert(0, $"{Time.time:0.0}s  {line}");
        if (_feed.Count > 8) _feed.RemoveAt(_feed.Count - 1);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame) _visible = !_visible;
        if (!_ready) return;
        // Infinite resources and health (practice: no bonus run).
        var w = _player.CombatResources;
        if (w != null)
            foreach (var t in new[] { CombatResourceType.TripleCombo, CombatResourceType.QuadCombo, CombatResourceType.Special })
                if (w.Get(t) < 3) w.Add(t, 3 - w.Get(t));
        foreach (var a in new[] { _player, _rival })
        {
            if (a.Health != null && (a.Health.IsKO || a.Health.CurrentHealth < a.Health.MaxHealth * 0.35f)) a.Health.ResetForRound();
            if (a.HitReaction != null && a.HitReaction.WaitingForGetUp) a.HitReaction.RequestGetUp();
        }
        if (_mode == RivalMode.Attack && Time.time >= _nextRivalAttack && _rival.MoveController.CurrentPhase == FighterMoveState.Idle)
        {
            var s = _rival.MoveController.MoveSet;
            var pool = new[] { s.normalPunch, s.highNormalPunch, s.lowNormalPunch, s.punchMidSide, s.punchHighSide, s.normalKick, s.highNormalKick, s.lowNormalKick, s.kickMidCenter }
                       .Where(m => m != null).ToArray();
            if (pool.Length > 0)
            {
                var m = pool[Random.Range(0, pool.Length)];
                _rival.MoveController.TryExecuteMove(m);
                Log($"Rival llança: {m.debugName} — eix {Axis(_rival.MoveController.CurrentStrikeSide)}, alçada {Height(m)}");
            }
            _nextRivalAttack = Time.time + Random.Range(1.4f, 2.4f);
        }
    }

    private void SetMode(RivalMode mode)
    {
        _mode = mode;
        if (_rival == null) return;
        if (_rival.AI != null)
        {
            _rival.AI.enabled = mode == RivalMode.AI;
            if (mode == RivalMode.AI) EventBus.Publish(new FightFlowStateChangedEvent { Previous = FightFlowState.Countdown, Current = FightFlowState.Fighting });
        }
        if (_rival.Guard != null) { _rival.Guard.DebugForcedState = mode == RivalMode.Guard ? FighterGuardState.StandingGuard : FighterGuardState.None; _rival.Guard.DebugForcedSide = 0; }
        _nextRivalAttack = Time.time + 1f;
    }

    private void Place(float distance)
    {
        if (_player == null || _rival == null) return;
        _player.transform.position = new Vector3(-distance * 0.5f, _player.transform.position.y, 0f);
        _rival.transform.position = new Vector3(distance * 0.5f, _rival.transform.position.y, 0f);
    }

    // ── Demos: the player performs the technique (as authored — the keys are in the list) ───────────
    private void Demo(FightMoveDefinition move, bool fromJump = false)
    {
        if (move == null || _player == null) return;
        if (fromJump) StartCoroutine(JumpThen(move));
        else _player.MoveController.TryExecuteMove(move);
    }

    private IEnumerator JumpThen(FightMoveDefinition move)
    {
        var jump = typeof(FighterMovement).GetMethod("StartJump", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        jump?.Invoke(_player.Movement, null);
        yield return new WaitForSeconds(0.12f);
        _player.MoveController.TryExecuteMove(move);
    }

    // ── Overlay ──────────────────────────────────────────────────────────────────────────────────────
    private GUIStyle _box, _label, _title, _small, _btn;

    private void Styles()
    {
        if (_label != null) return;
        _box = new GUIStyle(GUI.skin.box) { normal = { background = Tex(new Color(0f, 0f, 0f, 0.78f)) } };
        _label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = true, normal = { textColor = Color.white } };
        _small = new GUIStyle(_label) { fontSize = 12 };
        _title = new GUIStyle(_label) { fontSize = 17, fontStyle = FontStyle.Bold };
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
    }

    private static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    private bool FirstPerson => FightCameraController.Instance != null && FightCameraController.Instance.ViewMode == CameraViewMode.FirstPerson;

    private void OnGUI()
    {
        Styles();
        if (!_visible) { GUI.Label(new Rect(10, 10, 300, 24), "F1: mostra la pràctica", _label); return; }
        float w = Mathf.Min(560f, Screen.width * 0.46f);
        var area = new Rect(Screen.width - w - 10f, 10f, w, Screen.height - 20f);
        GUI.Box(area, GUIContent.none, _box);
        GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
        GUILayout.Label("PRÀCTICA DE LLUITA", _title);
        if (!_ready) { GUILayout.Label(_status, _label); GUILayout.EndArea(); return; }

        GUILayout.Label("<b>J</b> = puny · <b>K</b> = puntada · <b>Espai</b> = salt · fletxes o WASD · F1 amaga. Recursos (x3, x4, especials) i vida infinits.", _small);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(FirstPerson ? "Vista: 1a persona (canvia)" : "Vista: 3a persona (canvia)", _btn)) FightCameraController.Instance?.ToggleView();
        if (GUILayout.Button("Rival: " + RivalModeNames[(int)_mode] + " (canvia)", _btn)) SetMode((RivalMode)(((int)_mode + 1) % 4));
        if (GUILayout.Button("Recol·loca (1,2 m)", _btn)) Place(1.2f);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        GUILayout.Label($"<b>Última tècnica:</b> {(_lastMove == "" ? "—" : _lastMove)}", _label);
        GUILayout.Label($"<b>Resultat:</b> {(_lastResult == "" ? "—" : _lastResult)}", _label);
        var mc = _player.MoveController;
        if (!string.IsNullOrEmpty(mc.LastFeintInfo)) GUILayout.Label($"<b>Finta / canvi:</b> {mc.LastFeintInfo}", _small);
        GUILayout.Label($"<b>Distància:</b> {_player.DistanceToOpponent:0.00} m (de prop ≤ {CloseRange:0.00} m)   <b>Guàrdia rival:</b> {_rival.Guard?.State} {(_rival.Guard != null && _rival.Guard.CoverSide != 0 ? Axis(-_rival.Guard.CoverSide) : "")}", _small);
        foreach (var l in _feed.Take(4)) GUILayout.Label(l, _small);

        GUILayout.Space(4);
        _scroll = GUILayout.BeginScrollView(_scroll);
        var s = mc.MoveSet;
        foreach (var (name, keys, move, jump) in FirstPerson ? FirstPersonList(s) : ThirdPersonList(s))
        {
            if (move == null && keys == null) { GUILayout.Space(6); GUILayout.Label($"<b>{name}</b>", _label); continue; }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{name}</b>\n<color=#ffd27f>{keys}</color>", _small, GUILayout.Width(w - 110f));
            if (move != null) { if (GUILayout.Button("▶ Veure", _btn, GUILayout.Width(70f))) Demo(move, jump); }
            else GUILayout.Label("(teclat)", _small, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private static float CloseRange => Resources.Load<AppConfigSO>("AppConfig")?.combatBalance?.closeStrikeRange ?? 0.72f;

    private static (string, string, FightMoveDefinition, bool) H(string title) => (title, null, null, false);
    private static (string, string, FightMoveDefinition, bool) R(string name, string keys, FightMoveDefinition m = null, bool jump = false) => (name, keys, m, jump);

    private static IEnumerable<(string, string, FightMoveDefinition, bool)> ThirdPersonList(FightMoveSetSO s) => new[]
    {
        H("3a PERSONA — cops (l'eix és aleatori: ⅓ esquerra, ⅓ centre, ⅓ dreta)"),
        R("Puny mig (directe / ganxo)", "J", s.normalPunch),
        R("Puny alt", "↑ + J", s.highNormalPunch),
        R("Puny baix (ventre baix)", "↓ + J", s.lowNormalPunch),
        R("Ganxo (costat)", "J — surt quan l'eix és un costat", s.punchMidSide),
        R("Puntada mitja", "K", s.normalKick),
        R("Puntada alta", "↑ + K", s.highNormalKick),
        R("Puntada baixa (turmell)", "↓ + K", s.lowNormalKick),
        R("Puntada frontal (centre)", "K — surt quan l'eix és el centre", s.kickMidCenter),
        H("3a PERSONA — tècniques"),
        R("Puny amb impuls", "→ i després J", null),
        R("Puntada d'empenta", "→ + K   (amb ↑ alta, amb ↓ baixa)", s.pushKickMid),
        R("Giratòria mitja", "← + K", s.spinKickMid),
        R("Giratòria alta", "← + ↑ + K", s.backNormalKick),
        R("Escombrada giratòria (tomba)", "← + ↓ + K", s.spinSweep),
        R("De prop: uppercut / ganxo curt", "J a menys de 0,72 m", s.closeUppercut),
        R("De prop: genoll", "K a menys de 0,72 m", s.closeKnee),
        R("Puny en salt", "Espai, J a l'aire", s.airNormalPunch, true),
        R("Puntada en salt", "Espai, K a l'aire", s.airNormalKick, true),
        R("Taló giratori en salt", "← + Espai (salt enrere), K a l'aire", s.airSpinHeel, true),
        R("Taló caient (també a terra)", "Espai, ↓ + K a l'aire", s.axeKick, true),
        R("Puntada voladora", "→→ (córrer), Espai, K", s.flyingKick, true),
        R("Contraatac (imparable)", "bloqueja bé i prem J/K dins de 0,2 s (posa el rival a 'Ninot que ataca')"),
        R("Esquivar i contraatacar", "↑↑ o ↓↓ (pas lateral) i J/K dins de 0,3 s"),
        H("3a PERSONA — combos i recursos"),
        R("Cop fort", "J J J ràpid   ·   K K K ràpid"),
        R("Combos", "J J J K   ·   K K J K   ·   ← J J K"),
        R("Especial (bola de foc)", "↓ + → + J"),
        R("Power", "↓ + J + K mantinguts 1 s"),
        R("Agafar", "J + K alhora"),
        H("3a PERSONA — defensa i moviment"),
        R("Guàrdia mitja / alta / baixa", "← (enrere)  ·  ← + ↑  ·  ↓   (es gira sola cap al costat del cop)"),
        R("Pas lateral", "↑↑ (lluny de la càmera)  ·  ↓↓ (cap a la càmera)"),
        R("Córrer / pas enrere ràpid", "→→  ·  ←←"),
        R("Finta", "just després de començar un cop: guàrdia, pas lateral, salt o esprint"),
    };

    private static IEnumerable<(string, string, FightMoveDefinition, bool)> FirstPersonList(FightMoveSetSO s) => new[]
    {
        H("1a PERSONA — cops (tu tries l'eix: ← / → costat, res = centre)"),
        R("Puny mig al centre (directe)", "J", s.normalPunch),
        R("Puny mig de costat (ganxo)", "← o → + J", s.punchMidSide),
        R("Puny alt", "↑ + J   (amb ← / → de costat)", s.highNormalPunch),
        R("Puny baix", "↓ + J   (amb ← / → de costat)", s.lowNormalPunch),
        R("Puntada mitja al centre (frontal)", "K", s.kickMidCenter),
        R("Puntada mitja de costat (circular)", "← o → + K", s.normalKick),
        R("Puntada d'empenta alta", "↑ + K (sense costat)", s.pushKickHigh),
        R("Puntada alta de costat", "← o → + ↑ + K", s.highNormalKick),
        R("Puntada baixa", "↓ + K   (amb ← / → de costat)", s.kickLowCenter),
        H("1a PERSONA — tècniques"),
        R("De prop: uppercut / ganxo curt / genoll", "J o K a menys de 0,72 m", s.closeUppercut),
        R("Cop en salt", "Espai, J/K a l'aire (← / → tria el costat)", s.airNormalKick, true),
        R("Taló caient", "Espai, ↓ + K a l'aire", s.axeKick, true),
        R("Giratòria fent un pas enrere", "↓↓ i K dins de 0,3 s (← / → costat)", s.stepBackSpin),
        R("Esquivar i contraatacar", "←← o →→ i J/K dins de 0,3 s"),
        R("Contraatac (imparable)", "bloqueja bé i prem J/K dins de 0,2 s"),
        R("Canviar de costat a mig cop", "durant la preparació prem l'altra fletxa · ↓ el cancel·la"),
        H("1a PERSONA — combos i recursos"),
        R("Cop fort", "J J J ràpid   ·   K K K ràpid"),
        R("Especial (bola de foc)", "↓, després ↑ + J"),
        R("Power", "↓ + J + K mantinguts 1 s"),
        R("Agafar", "J + K alhora"),
        H("1a PERSONA — defensa i moviment"),
        R("Guàrdia d'un costat", "mantén ← o →  (+ ↑ alta, + ↓ baixa ajupida)"),
        R("Avançar / recular", "↑ / ↓   ·   córrer ↑↑   ·   pas enrere ↓↓"),
        R("Pas lateral", "←← / →→"),
    };
}
#endif
