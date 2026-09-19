using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F1 toggle overlay for the Fight input/combo layer — same IMGUI style as Runner's own
/// GameplayDebugHUD, but that class is scene-local to Runner (destroyed the instant Runner
/// unloads) and knows nothing about Fight, so this is a separate, UI-Scene-resident class rather
/// than an extension of it. Only actually toggles/draws while GameFlowState is Fight, so pressing
/// F1 during a Runner session never shows a confusing, empty-looking overlay from this class
/// layered on top of GameplayDebugHUD's own.
///
/// Discrete actions only get logged as they happen (Normal Punch/Kick, Combo Detected) — never
/// every frame for the joystick's continuous axis, per FighterInputController's own doc on "no
/// noisy logging".
/// </summary>
public class FightDebugHUD : MonoBehaviour
{
    private const int MaxLogLines = 6;

    private bool _visible;
    private FighterInputController _input;
    private FighterMoveController _moves;
    private FighterActor _player;
    private FighterActor _opponent;

    private readonly List<string> _log = new();
    private string _lastNormal = "(none)";
    private string _lastCombo  = "(none)";

    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _headerStyle;

    private System.Action<FightNormalPunchEvent>   _onPunch;
    private System.Action<FightNormalKickEvent>    _onKick;
    private System.Action<FightComboDetectedEvent> _onCombo;

    private void Start()
    {
        _input = FindFirstObjectByType<FighterInputController>();
        _moves = FindFirstObjectByType<FighterMoveController>();
        FindActors();
    }

    // FighterActors live in Fight.unity (spawned by FightSceneBootstrap), loaded/unloaded well
    // after this UI-Scene-resident class's own Start() — re-tried lazily from OnGUI (see below)
    // until they actually exist, same tolerant pattern as _input/_moves above.
    private void FindActors()
    {
        if (_player != null && _opponent != null) return;
        foreach (var actor in FindObjectsByType<FighterActor>(FindObjectsSortMode.None))
        {
            if (actor.Side == FighterSide.Player) _player = actor;
            else _opponent = actor;
        }
    }

    private void OnEnable()
    {
        _onPunch = _ => { _lastNormal = "Normal Punch"; AddLog("Normal Punch"); };
        _onKick  = _ => { _lastNormal = "Normal Kick";  AddLog("Normal Kick"); };
        _onCombo = e =>
        {
            _lastCombo = e.Combo != null && !string.IsNullOrEmpty(e.Combo.debugName) ? e.Combo.debugName : "(unnamed)";
            AddLog($"Combo: {_lastCombo}");
        };
        EventBus.Subscribe(_onPunch);
        EventBus.Subscribe(_onKick);
        EventBus.Subscribe(_onCombo);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPunch);
        EventBus.Unsubscribe(_onKick);
        EventBus.Unsubscribe(_onCombo);
    }

    private void Update()
    {
        if (!InFight) return;
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            _visible = !_visible;
    }

    private static bool InFight =>
        AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.Fight;

    private void OnGUI()
    {
        if (!_visible || !InFight) return;
        EnsureStyles();
        if (_input == null) _input = FindFirstObjectByType<FighterInputController>();
        if (_moves == null) _moves = FindFirstObjectByType<FighterMoveController>();
        FindActors();

        const float x = 8f, w = 340f;
        float y = 8f;
        float h = 26f + 18f * 3f + 18f + 16f * 7f + 18f + 16f * 10f + 18f + 16f * (MaxLogLines + 1);

        GUI.Box(new Rect(x, y, w, h), "", _boxStyle);
        GUI.Label(new Rect(x + 6f, y + 2f, w - 12f, 16f), "FIGHT INPUT/COMBO DEBUG (F1)", _headerStyle);
        y += 22f;

        string dir = _input != null ? $"{_input.CurrentHorizontal} / {_input.CurrentVertical}" : "(no FighterInputController)";
        Row(x, ref y, w, $"Direction: {dir}");
        Row(x, ref y, w, $"Last normal: {_lastNormal}");
        Row(x, ref y, w, $"Last combo: {_lastCombo}");
        y += 4f;

        Row(x, ref y, w, "Move System:");
        if (_moves == null)
        {
            Row(x, ref y, w, "  (no FighterMoveController)");
        }
        else
        {
            string moveName = _moves.CurrentMove != null ? _moves.CurrentMove.debugName : "(none)";
            string queuedName = _moves.QueuedMove != null ? _moves.QueuedMove.debugName : "(none)";
            Row(x, ref y, w, $"  Move: {moveName}");
            Row(x, ref y, w, $"  Phase: {_moves.CurrentPhase} ({_moves.PhaseElapsed:F2}s / {_moves.CurrentPhaseDuration:F2}s, {_moves.PhaseProgress01 * 100f:F0}%)");
            Row(x, ref y, w, $"  Queued: {queuedName}");
            Row(x, ref y, w, $"  CanAttack: {_moves.CanAttack}");
            Row(x, ref y, w, $"  Anim state: {_moves.CurrentAnimationState}");
        }
        y += 4f;

        var flowState = FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState.ToString() : "(no FightFlowController)";
        Row(x, ref y, w, "Fighter Actors:");
        Row(x, ref y, w, $"  FightFlowState: {flowState}");
        if (_player == null || _opponent == null)
        {
            Row(x, ref y, w, "  (no FighterActors found yet)");
        }
        else
        {
            string playerVisual   = _player.UsedFallbackCapsule   ? "capsule" : "prefab";
            string opponentVisual = _opponent.UsedFallbackCapsule ? "capsule" : "prefab";
            Row(x, ref y, w, $"  Player pos: {_player.transform.position:F2}  ({playerVisual})");
            Row(x, ref y, w, $"  Opponent pos: {_opponent.transform.position:F2}  ({opponentVisual})");
            Row(x, ref y, w, $"  DistanceToOpponent: {_player.DistanceToOpponent:F2}");
            Row(x, ref y, w, $"  Player facing: {(_player.FacingRight ? "Right" : "Left")}   Opponent facing: {(_opponent.FacingRight ? "Right" : "Left")}");
            if (_player.Movement != null)
            {
                Row(x, ref y, w, $"  Player movement locked: {_player.Movement.IsLocked}   multiplier: {_player.Movement.Multiplier:F2}");
                Row(x, ref y, w, $"  Last lunge: {_player.Movement.LastLungeDistance:F2}");
            }
            else
            {
                Row(x, ref y, w, "  (no FighterMovement on Player)");
            }
        }
        y += 4f;

        Row(x, ref y, w, "Recent actions:");
        if (_log.Count == 0) Row(x, ref y, w, "  (none yet)");
        foreach (var line in _log) Row(x, ref y, w, "  " + line);
    }

    private void AddLog(string line)
    {
        _log.Insert(0, $"{Time.time:F2}s  {line}");
        if (_log.Count > MaxLogLines) _log.RemoveAt(_log.Count - 1);
    }

    private void Row(float x, ref float y, float w, string text)
    {
        GUI.Label(new Rect(x + 6f, y, w - 12f, 16f), text, _labelStyle);
        y += 16f;
    }

    private void EnsureStyles()
    {
        if (_boxStyle != null) return;

        var bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
        bgTex.Apply();

        _boxStyle = new GUIStyle(GUI.skin.box) { border = new RectOffset(4, 4, 4, 4) };
        _boxStyle.normal.background = bgTex;

        _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
        _labelStyle.normal.textColor = new Color(0.9f, 0.95f, 1f);

        _headerStyle = new GUIStyle(_labelStyle) { fontStyle = FontStyle.Bold, fontSize = 11 };
        _headerStyle.normal.textColor = new Color(0.7f, 1f, 0.8f);
    }
}
