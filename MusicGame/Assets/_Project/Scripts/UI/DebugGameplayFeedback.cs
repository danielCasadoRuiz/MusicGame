using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMPORARY playtest feedback (Editor + Development builds only — compiled out of release builds):
/// a small on-screen feed of what the combo / resource systems are doing, plus PLAYER / RIVAL labels
/// over the fighters, so they can be verified in normal play without debug shortcuts.
///   Runner: pickup-combo sequence start / count / broken, Triple / Quad granted, LIFE / SPECIAL collected.
///   Fight:  combo string progress / reset, combo detected, bonus hits, tackle, Signature (SPECIAL), Power.
/// Pure listener (EventBus + read-only polling of the player's combo recognizer); replace with real
/// VFX/UI later by deleting this one component (added by UIFlowController).
/// </summary>
public class DebugGameplayFeedback : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private struct Toast { public string Text; public Color Color; public float Time; }

    private const float ToastSeconds = 3f;
    private const int MaxToasts = 7;
    private static readonly Color RunnerColor = new(1f, 0.9f, 0.4f);
    private static readonly Color FightColor  = new(0.6f, 0.9f, 1f);
    private static readonly Color ResourceColor = new(1f, 0.5f, 1f);

    private readonly List<Toast> _toasts = new();
    private FighterActor _player, _opponent;
    private float _nextFindTime;
    private string _lastComboString = "";
    private RunnerResourceCounts _lastCounts;
    private int _runnerSequence;
    private GUIStyle _style, _labelStyle;

    private System.Action<FightComboDetectedEvent> _onCombo;
    private System.Action<HitLandedEvent> _onHit;
    private System.Action<FightTackleEvent> _onTackle;
    private System.Action<SignatureExecutedEvent> _onSignature;
    private System.Action<PowerStateChangedEvent> _onPower;
    private System.Action<PickupComboEvent> _onPickupCombo;
    private System.Action<PickupComboProgressEvent> _onPickupProgress;
    private System.Action<RunnerResourcesChangedEvent> _onResources;

    private void OnEnable()
    {
        _onCombo = e =>
        {
            if (e.Combo == null) return;
            string who = Who(e.Source);
            string name = string.IsNullOrEmpty(e.Combo.debugName) ? e.Combo.id : e.Combo.debugName;
            Add($"{who} COMBO: {name}{(e.ReplacesPrevious ? "  (extended)" : "")}", FightColor);
        };
        _onHit = e =>
        {
            if (e.Result.Bonus.damage <= 1.01f) return; // only combo / bonus effects
            Add($"{WhoActor(e.Attacker)} bonus hit x{e.Result.Bonus.damage:0.0#} → {e.Result.FinalDamage:0.#} dmg", FightColor);
        };
        _onTackle = e => Add($"{WhoActor(e.Attacker)} TACKLE {e.Speed:0.0} m/s → " +
                             (e.Result.IsEvaded ? "evaded" : e.Result.IsBlocked ? "blocked" :
                              $"{e.Result.FinalDamage:0.#} dmg{(e.Result.CausesKnockdown ? ", KNOCKDOWN" : "")}"), FightColor);
        _onSignature = e => Add($"{WhoActor(e.Fighter)} SIGNATURE {(e.Enhanced ? "ENHANCED (1 SPECIAL used)" : "basic")} — specials left {e.SpecialsLeft}", ResourceColor);
        _onPower = e => Add($"{WhoActor(e.Fighter)} POWER {(e.Active ? $"ON ({e.Duration:0.#}s)" : "OFF")}", FightColor);
        _onPickupCombo = e => Add($"PICKUP COMBO: {e.Tier.ToString().ToUpperInvariant()}  (x3 {e.Counts.TripleCombos} / x4 {e.Counts.QuadCombos})", RunnerColor);
        _onPickupProgress = e =>
        {
            if (e.Broken) Add($"pickup combo broken (had {_runnerSequence})", RunnerColor);
            else if (e.Count == 1) Add("pickup combo started (1)", RunnerColor);
            else if (e.Count > 1) Add($"pickup combo {e.Count}", RunnerColor);
            _runnerSequence = e.Count;
        };
        _onResources = e =>
        {
            if (e.Counts.Lives > _lastCounts.Lives) Add($"LIFE collected +{e.Counts.Lives - _lastCounts.Lives}  (this run {e.Counts.Lives})", ResourceColor);
            if (e.Counts.Specials > _lastCounts.Specials) Add($"SPECIAL collected +{e.Counts.Specials - _lastCounts.Specials}  (this run {e.Counts.Specials})", ResourceColor);
            _lastCounts = e.Counts;
        };
        EventBus.Subscribe(_onCombo);
        EventBus.Subscribe(_onHit);
        EventBus.Subscribe(_onTackle);
        EventBus.Subscribe(_onSignature);
        EventBus.Subscribe(_onPower);
        EventBus.Subscribe(_onPickupCombo);
        EventBus.Subscribe(_onPickupProgress);
        EventBus.Subscribe(_onResources);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onCombo);
        EventBus.Unsubscribe(_onHit);
        EventBus.Unsubscribe(_onTackle);
        EventBus.Unsubscribe(_onSignature);
        EventBus.Unsubscribe(_onPower);
        EventBus.Unsubscribe(_onPickupCombo);
        EventBus.Unsubscribe(_onPickupProgress);
        EventBus.Unsubscribe(_onResources);
    }

    private void Update()
    {
        // Fighters exist only while Fight.unity is loaded — look them up at most twice a second.
        if ((_player == null || _opponent == null) && Time.unscaledTime >= _nextFindTime)
        {
            _nextFindTime = Time.unscaledTime + 0.5f;
            foreach (var a in FindObjectsByType<FighterActor>(FindObjectsSortMode.None))
                if (a.Side == FighterSide.Player) _player = a; else _opponent = a;
        }

        // Player's combo string: progress while it grows, "reset" when it clears.
        var rec = _player != null && _player.InputController != null ? _player.InputController.Recognizer : null;
        string current = rec != null ? rec.CurrentStringText ?? "" : "";
        if (current != _lastComboString)
        {
            if (current.Length > 0) Add($"combo string: {current}", FightColor);
            else if (_lastComboString.Length > 0) Add("combo string reset", FightColor);
            _lastComboString = current;
        }
        _toasts.RemoveAll(t => Time.unscaledTime - t.Time > ToastSeconds);
    }

    private void Add(string text, Color color)
    {
        _toasts.Add(new Toast { Text = text, Color = color, Time = Time.unscaledTime });
        if (_toasts.Count > MaxToasts) _toasts.RemoveAt(0);
    }

    private string Who(FighterInputController source) =>
        _player != null && source == _player.InputController ? "PLAYER" : "RIVAL";
    private string WhoActor(FighterActor actor) => actor != null && actor.Side == FighterSide.Player ? "PLAYER" : "RIVAL";

    private void OnGUI()
    {
        _style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _labelStyle ??= new GUIStyle(_style) { fontSize = 13 };

        float y = 70f;
        foreach (var t in _toasts)
        {
            float a = Mathf.Clamp01((ToastSeconds - (Time.unscaledTime - t.Time)) / 0.6f);
            Shadowed(new Rect(0f, y, Screen.width, 22f), t.Text, new Color(t.Color.r, t.Color.g, t.Color.b, a), _style);
            y += 22f;
        }

        // PLAYER / RIVAL over the fighters (which avatar is which after a cross-up).
        var cam = FightCameraController.Instance != null ? FightCameraController.Instance.GetComponent<Camera>() : null;
        if (cam == null) return;
        Label(cam, _player, "PLAYER", new Color(0.35f, 0.75f, 1f));
        Label(cam, _opponent, "RIVAL", new Color(1f, 0.4f, 0.35f));
    }

    private void Label(Camera cam, FighterActor actor, string text, Color color)
    {
        if (actor == null) return;
        Vector3 sp = cam.WorldToScreenPoint(actor.transform.position + Vector3.up * 2.2f);
        if (sp.z <= 0f) return;
        Shadowed(new Rect(sp.x - 60f, Screen.height - sp.y - 10f, 120f, 20f), text, color, _labelStyle);
    }

    private static void Shadowed(Rect r, string text, Color color, GUIStyle style)
    {
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, color.a * 0.8f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        GUI.color = color;
        GUI.Label(r, text, style);
        GUI.color = prev;
    }
#endif
}
