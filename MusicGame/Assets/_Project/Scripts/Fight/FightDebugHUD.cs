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
    private FighterActor _player;
    private FighterActor _opponent;
    private FightCombatBalanceConfig _balanceConfig;

    // Resolved lazily from _player/_opponent once they exist (see FindActors) — no longer
    // FindFirstObjectByType singletons now that each fighter has its OWN FighterInputController/
    // FighterMoveController instance (see those classes' own doc).
    private FighterInputController _input => _player != null ? _player.InputController : null;
    private FighterMoveController _moves => _player != null ? _player.MoveController : null;

    private readonly List<string> _log = new();
    private string _lastNormal = "(none)";
    private string _lastCombo  = "(none)";
    private FightHitResult? _lastHit;

    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _headerStyle;

    private System.Action<FightNormalPunchEvent>   _onPunch;
    private System.Action<FightNormalKickEvent>    _onKick;
    private System.Action<FightComboDetectedEvent> _onCombo;
    private System.Action<HitLandedEvent>          _onHitLanded;
    private System.Action<HitBlockedEvent>         _onHitBlocked;

    private void Start()
    {
        _balanceConfig = Resources.Load<AppConfigSO>("AppConfig")?.combatBalance;
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
        // Filtered to the Player's own FighterInputController — both fighters publish these events
        // now (see FightNormalPunchEvent's own Source doc), and this "Last normal/combo" readout is
        // specifically about what the PLAYER just did (the Opponent's own actions surface in the AI
        // section below instead).
        _onPunch = e => { if (e.Source != _input) return; _lastNormal = "Normal Punch"; AddLog("Normal Punch"); };
        _onKick  = e => { if (e.Source != _input) return; _lastNormal = "Normal Kick";  AddLog("Normal Kick"); };
        _onCombo = e =>
        {
            if (e.Source != _input) return;
            _lastCombo = e.Combo != null && !string.IsNullOrEmpty(e.Combo.debugName) ? e.Combo.debugName : "(unnamed)";
            AddLog($"Combo: {_lastCombo}");
        };
        _onHitLanded = e =>
        {
            _lastHit = e.Result;
            AddLog($"Hit: {(e.Move != null ? e.Move.debugName : "?")} -> {e.Result.FinalDamage:F1} dmg");
        };
        _onHitBlocked = e =>
        {
            _lastHit = e.Result;
            AddLog($"Blocked: {(e.Move != null ? e.Move.debugName : "?")} -> {e.Result.FinalChipDamage:F1} chip");
        };
        EventBus.Subscribe(_onPunch);
        EventBus.Subscribe(_onKick);
        EventBus.Subscribe(_onCombo);
        EventBus.Subscribe(_onHitLanded);
        EventBus.Subscribe(_onHitBlocked);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onPunch);
        EventBus.Unsubscribe(_onKick);
        EventBus.Unsubscribe(_onCombo);
        EventBus.Unsubscribe(_onHitLanded);
        EventBus.Unsubscribe(_onHitBlocked);
    }

    // ── Debug commands (F2-F8) — only live while the F1 overlay is visible, so they can never be
    // hit by accident during normal play. Every command goes through a REAL public API (FighterHealth.
    // ApplyDamage, FightMatchController.DebugSetRoundTimeRemaining/DebugForceDraw/
    // DebugSetAccumulatedDifferential) — none of them poke a private field, so the match flow they
    // trigger is indistinguishable from a real one.
    //   F1  Toggle this overlay
    //   F2  Force Player KO
    //   F3  Force Opponent KO
    //   F4  Set round timer to 3s
    //   F5  Force a round-level Draw (equalize health, timer -> 0) — NEVER awards a round win to
    //       either side. Only matters as a MATCH tie-break trigger on the FINAL round (see F6-F8).
    //   F6  Set accumulated point differential to +0.50 (tied match after final round -> Player wins on points)
    //   F7  Set accumulated point differential to -0.50 (tied match after final round -> Opponent wins on points)
    //   F8  Set accumulated point differential to 0 (tied match after final round -> exact-tie fallback)
    //   F9  Force Match WIN  — jumps straight to Match Result (Win/Level Up/Continue) for testing
    //   F10 Force Match LOSE — jumps straight to Match Result (Lose/Fight Again/Replay Song) for testing
    //   F11 +1 Extra Life (PlayerProgressService.ExtraLives, persistent) — so "Lose with a life" is testable
    //       on demand; the real economy for how lives are earned isn't decided yet (see FightResources'
    //       own doc), this is purely a test aid
    //   F12 EDITOR ONLY — toggle "simulate a successful Rewarded Ad" (see NotImplementedRewardedAdService's
    //       own doc); off by default, never available in a real build
    private void Update()
    {
        if (!InFight) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f1Key.wasPressedThisFrame) _visible = !_visible;
        if (!_visible) return;

        if (kb.f2Key.wasPressedThisFrame) DebugForceKO(_player, "Player");
        if (kb.f3Key.wasPressedThisFrame) DebugForceKO(_opponent, "Opponent");
        if (kb.f4Key.wasPressedThisFrame)
        {
            FightMatchController.Instance?.DebugSetRoundTimeRemaining(3f);
            AddLog("Debug: timer -> 3s");
        }
        if (kb.f5Key.wasPressedThisFrame)
        {
            FightMatchController.Instance?.DebugForceDraw();
            AddLog("Debug: force round-level Draw");
        }
        if (kb.f6Key.wasPressedThisFrame) DebugSetDifferential(0.5f);
        if (kb.f7Key.wasPressedThisFrame) DebugSetDifferential(-0.5f);
        if (kb.f8Key.wasPressedThisFrame) DebugSetDifferential(0f);
        if (kb.f9Key.wasPressedThisFrame)
        {
            FightMatchController.Instance?.DebugForceMatchResult(true);
            AddLog("Debug: force Match WIN");
        }
        if (kb.f10Key.wasPressedThisFrame)
        {
            FightMatchController.Instance?.DebugForceMatchResult(false);
            AddLog("Debug: force Match LOSE");
        }
        if (kb.f11Key.wasPressedThisFrame)
        {
            if (GameSession.Instance?.FightResources != null)
            {
                PlayerProgressService.Instance?.AddLife(1, "debug F11");
                AddLog($"Debug: +1 Extra Life (now {PlayerProgressService.Instance?.ExtraLives})");
            }
        }
#if UNITY_EDITOR
        if (kb.f12Key.wasPressedThisFrame)
        {
            NotImplementedRewardedAdService.DebugSimulateSuccess = !NotImplementedRewardedAdService.DebugSimulateSuccess;
            AddLog($"Debug: Simulate Rewarded Ad Success -> {NotImplementedRewardedAdService.DebugSimulateSuccess}");
        }
#endif
    }

    // Read-only view of GameSession progression / opponent selection (forcing: Tools > MusicGame >
    // Progression menu, or GameSession.DebugForce).
    private void DrawProgression(float x, ref float y, float w)
    {
        var s = GameSession.Instance;
        Row(x, ref y, w, "Progression / Opponent selection:");
        if (s == null || s.Progression == null) { Row(x, ref y, w, "  (no GameSession)"); return; }
        var p = s.ProgressionInfo;
        Row(x, ref y, w, $"  Completed songs {p.CompletedSongs}   Tier {p.CurrentTier}   {p.CompletedSongsInCurrentTier}/{p.SongsRequiredForNextTier} ({p.SongsUntilNextTier} to next)");
        string opp = s.SelectedOpponent != null ? s.SelectedOpponent.displayName : "(none)";
        Row(x, ref y, w, $"  Opponent {opp}   config tier {s.SelectedOpponentTier}   (effective tier {s.EffectiveOpponentTier})");
        Row(x, ref y, w, $"  Bag left: {string.Join(", ", s.OpponentBag.Remaining)}");
        Row(x, ref y, w, $"  Recent: {string.Join(", ", s.OpponentBag.Recent)}");
        if (!string.IsNullOrEmpty(s.DebugForcedOpponentId) || s.DebugForcedTier > 0)
            Row(x, ref y, w, $"  DEBUG FORCE: {s.DebugForcedOpponentId ?? "(bag)"} tier {(s.DebugForcedTier > 0 ? s.DebugForcedTier.ToString() : "(real)")}");
    }

    private void DebugSetDifferential(float value)
    {
        FightMatchController.Instance?.DebugSetAccumulatedDifferential(value);
        AddLog($"Debug: differential -> {value:+0.00;-0.00}");
    }

    private void DebugForceKO(FighterActor actor, string label)
    {
        if (actor?.Health == null) return;
        actor.Health.ApplyDamage(actor.Health.CurrentHealth);
        AddLog($"Debug: force {label} KO");
    }

    private static bool InFight =>
        AppBootstrap.Context != null && AppBootstrap.Context.AppFlow.CurrentState == GameFlowState.Fight;

    private void OnGUI()
    {
        if (!_visible || !InFight) return;
        EnsureStyles();
        if (_balanceConfig == null) _balanceConfig = Resources.Load<AppConfigSO>("AppConfig")?.combatBalance;
        FindActors();

        const float x = 8f, w = 420f;
        float y = 8f;
        float h = 26f + 18f + 16f * 6f + 16f * 4f + 18f * 3f + 18f + 16f * 8f + 18f + 16f * 12f + 18f + 16f * 8f + 18f + 16f * 10f + 18f + 16f * 6f + 18f + 16f * 4f + 18f + 16f * 26f + 18f + 16f * (MaxLogLines + 1);

        GUI.Box(new Rect(x, y, w, h), "", _boxStyle);
        GUI.Label(new Rect(x + 6f, y + 2f, w - 12f, 16f), "FIGHT DEBUG (F1 | F2/3 KO | F4 timer | F5 draw | F6/7/8 diff | F9/10 win/lose | F11 +life | F12 ad)", _headerStyle);
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
            Row(x, ref y, w, $"  CanAttack: {_moves.CanAttack}   IsHitStunned: {_moves.IsHitStunned}");
            Row(x, ref y, w, $"  Anim state: {_moves.CurrentAnimationState}");
        }
        y += 4f;

        var flowState = FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState.ToString() : "(no FightFlowController)";
        DrawProgression(x, ref y, w);
        y += 4f;

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
            Row(x, ref y, w, $"  Player pos: {_player.transform.position:F2}  X={_player.transform.position.x:F2} Z(depth)={_player.transform.position.z:F2}  ({playerVisual})");
            Row(x, ref y, w, $"  Opponent pos: {_opponent.transform.position:F2}  X={_opponent.transform.position.x:F2} Z(depth)={_opponent.transform.position.z:F2}  ({opponentVisual})");
            Row(x, ref y, w, $"  Planar distance (XZ): {_player.DistanceToOpponent:F2}");
            Row(x, ref y, w, $"  Player ForwardXZ: {_player.ForwardXZ:F2}   SideXZ: {_player.SideXZ:F2}");
            Row(x, ref y, w, $"  Opponent ForwardXZ: {_opponent.ForwardXZ:F2}   SideXZ: {_opponent.SideXZ:F2}");
            DrawMovementDebugRow("Player", _player, x, ref y, w);
            DrawMovementDebugRow("Opponent", _opponent, x, ref y, w);
        }
        y += 4f;

        Row(x, ref y, w, "Match:");
        var match = FightMatchController.Instance;
        if (match == null)
        {
            Row(x, ref y, w, "  (no FightMatchController)");
        }
        else
        {
            Row(x, ref y, w, $"  Round {match.CurrentRound}/{match.MaxRounds}   RoundActive: {match.RoundActive}   Timer: {match.RoundTimeRemaining:F1}s");
            Row(x, ref y, w, $"  Rounds won — Player: {match.PlayerRoundsWon}   Opponent: {match.OpponentRoundsWon}");

            string leader = match.MatchPointDifferential > 0f ? "Player" : match.MatchPointDifferential < 0f ? "Opponent" : "(even)";
            Row(x, ref y, w, $"  Point differential — last round: {match.LastRoundDifferential:+0.00;-0.00}   accumulated: {match.MatchPointDifferential:+0.00;-0.00} ({leader})");
            Row(x, ref y, w, $"  Last round resolution: {match.LastRoundResolution}");
            Row(x, ref y, w, $"  Match resolution: {(match.LastMatchResolution.HasValue ? match.LastMatchResolution.Value.ToString() : "(match not decided yet)")}");
        }
        y += 4f;

        Row(x, ref y, w, "Combatants:");
        DrawFighterSummary("Player", _player, x, ref y, w);
        DrawFighterSummary("Opponent", _opponent, x, ref y, w);
        y += 4f;

        Row(x, ref y, w, "Last Attack:");
        if (_lastHit == null)
        {
            Row(x, ref y, w, "  (none yet)");
        }
        else
        {
            var hit = _lastHit.Value;
            string moveName     = hit.Move != null ? hit.Move.debugName : "(unknown move)";
            string attackerSide = hit.Attacker != null ? hit.Attacker.Side.ToString() : "?";
            string defenderSide = hit.Defender != null ? hit.Defender.Side.ToString() : "?";
            string delivery     = hit.Move != null ? hit.Move.attackDelivery.ToString() : "?";
            string height        = hit.HitDef != null ? hit.HitDef.attackHeight.ToString() : "?";
            string guardType    = hit.HitDef != null ? hit.HitDef.guardType.ToString() : "?";

            Row(x, ref y, w, $"  Move: {moveName}  ({attackerSide} -> {defenderSide})  [{delivery}]");
            Row(x, ref y, w, $"  Height: {height}   GuardType: {guardType}   Result: {(hit.IsBlocked ? "BLOCKED" : "HIT")}");
            if (hit.IsBlocked)
            {
                Row(x, ref y, w, $"  ChipDamage: {hit.FinalChipDamage:F1}");
                Row(x, ref y, w, $"  BlockStun: {hit.FinalBlockStun:F2}s   Knockback: {hit.FinalKnockback:F2}");
            }
            else
            {
                Row(x, ref y, w, $"  Damage: {hit.BaseDamage:F1} x{hit.DamageModifier:F2}(str) x{hit.DefenseModifier:F2}(def) = {hit.FinalDamage:F1}");
                Row(x, ref y, w, $"  HitStun: {hit.BaseHitStun:F2}s x{hit.HitStunResistanceModifier:F2}(bal) = {hit.FinalHitStun:F2}s");
                Row(x, ref y, w, $"  Knockback: {hit.BaseKnockback:F2} x{hit.KnockbackModifier:F2}(knb) = {hit.FinalKnockback:F2}");
            }
        }
        y += 4f;

        Row(x, ref y, w, "Projectiles:");
        var projectiles = FindObjectsByType<FightProjectile>(FindObjectsSortMode.None);
        if (projectiles.Length == 0)
        {
            Row(x, ref y, w, "  (none active)");
        }
        else
        {
            foreach (var proj in projectiles)
                Row(x, ref y, w, $"  {(proj.Owner != null ? proj.Owner.Side.ToString() : "?")} projectile — lifetime left: {proj.LifetimeRemaining:F2}s");
        }
        y += 4f;

        Row(x, ref y, w, "Opponent AI:");
        var ai = _opponent != null ? _opponent.AI : null;
        if (ai == null)
        {
            Row(x, ref y, w, "  (no FighterAI)");
        }
        else
        {
            var profile = ai.Profile;
            Row(x, ref y, w, $"  Active: {ai.IsActive}   Profile: {(profile != null ? profile.name : "(none — flat defaults)")}");
            Row(x, ref y, w, $"  Intention: {ai.CurrentIntention}   Reaction left: {ai.DecisionTimeRemaining:F2}s");

            string topScores = "  Scores:";
            int shown = 0;
            foreach (var kv in SortedByScoreDesc(ai.LastScores))
            {
                if (shown >= 4) break;
                topScores += $" {kv.Key}={kv.Value:F2}";
                shown++;
            }
            Row(x, ref y, w, topScores);

            Row(x, ref y, w, $"  Target spacing: {ai.TargetIdealDistance:F2}   DistanceToOpponent: {_opponent.DistanceToOpponent:F2}");

            string selectedMove = _opponent.MoveController != null && _opponent.MoveController.CurrentMove != null
                ? _opponent.MoveController.CurrentMove.debugName : "(none)";
            Row(x, ref y, w, $"  Selected move: {selectedMove}");

            string comboText = ai.LastSelectedCombo != null
                ? $"{ai.LastSelectedCombo.debugName} (step {ai.LastComboStepIndex + 1}/{ai.LastSelectedCombo.steps.Length})"
                : "(none)";
            Row(x, ref y, w, $"  Selected combo: {comboText}");

            bool projectileThreat = false;
            foreach (var proj in FindObjectsByType<FightProjectile>(FindObjectsSortMode.None))
                if (proj.Owner == _player) { projectileThreat = true; break; }
            Row(x, ref y, w, $"  Defense response: {DescribeDefenseResponse(ai.CurrentIntention)}   Projectile threat: {projectileThreat}");

            Row(x, ref y, w, $"  Last error: {ai.LastErrorNote ?? "(none)"}");

            if (profile != null)
            {
                Row(x, ref y, w, $"  aggr {profile.aggression:F2}  def {profile.defenseProbability:F2}  punish {profile.punishSkill:F2}  combo {profile.comboSkill:F2}");
                Row(x, ref y, w, $"  spacing {profile.spacingAccuracy:F2}  error {profile.errorRate:F2}  special {profile.specialUsage:F2}  reaction {profile.reactionTime:F2}s");
            }
        }
        y += 4f;

        Row(x, ref y, w, "Recent actions:");
        if (_log.Count == 0) Row(x, ref y, w, "  (none yet)");
        foreach (var line in _log) Row(x, ref y, w, "  " + line);
    }

    private void DrawMovementDebugRow(string label, FighterActor actor, float x, ref float y, float w)
    {
        var movement = actor.Movement;
        if (movement == null) { Row(x, ref y, w, $"  ({label}: no FighterMovement)"); return; }

        string sidestep = movement.SidestepDirection == 0 ? "-" : (movement.SidestepDirection > 0 ? "+Side" : "-Side");
        Row(x, ref y, w, $"  {label} move: locked={movement.IsLocked} mult={movement.Multiplier:F2} lunge={movement.LastLungeDistance:F2} " +
                          $"sidestep={sidestep} sidewalk={movement.IsSideWalking}");
    }

    private void DrawFighterSummary(string label, FighterActor actor, float x, ref float y, float w)
    {
        if (actor == null) { Row(x, ref y, w, $"  {label}: (not found)"); return; }

        string hpText = actor.Health != null
            ? $"{actor.Health.CurrentHealth:F0}/{actor.Health.MaxHealth:F0}{(actor.Health.IsKO ? " [KO]" : "")}"
            : "(no FighterHealth)";
        Row(x, ref y, w, $"  {label} HP: {hpText}");
        Row(x, ref y, w, $"    Posture: {actor.Posture}   Movement: {actor.MovementState}");

        string guardText = "Not Guarding";
        if (actor.HitReaction != null && actor.HitReaction.IsInBlockStun) guardText = "BlockStun";
        else if (actor.Guard != null && actor.Guard.State != FighterGuardState.None) guardText = actor.Guard.State.ToString();
        Row(x, ref y, w, $"    Defense: {guardText}");

        // Combat architecture: profile / reaction / move / phase / build / resources.
        var mc = actor.MoveController;
        string profile = actor.CombatProfile != null ? actor.CombatProfile.name : "(no profile)";
        string reaction = actor.HitReaction != null ? actor.HitReaction.State.ToString() : "-";
        Row(x, ref y, w, $"    Profile {profile}   Reaction {reaction}{(actor.IsInvulnerable ? " INVULN" : "")}{(mc != null && mc.IsMatchLocked ? " MATCH-LOCK" : "")}");
        Row(x, ref y, w, $"    Move {(mc?.CurrentMove != null ? mc.CurrentMove.debugName : "-")} ({mc?.CurrentMove?.role})  Phase {mc?.CurrentPhase}  Anim {actor.AnimationDriver?.CurrentState}");
        var b = actor.BuildStats;
        Row(x, ref y, w, $"    Punch {b.punchPower:F1}  Kick {b.kickPower:F1}  Agi {b.agility:F1}  Res {b.resistance:F1}  Imp {b.impactPower:F1}");
        Row(x, ref y, w, $"    Resources {actor.CombatResources}   last reject: {mc?.LastRejection}");
        if (mc != null)
        {
            // Repeat chains / Signature / Power (see FighterMoveController, FighterPowerState).
            var input = actor.InputController;
            string power = actor.Power != null && actor.Power.IsActive
                ? $"POWER ON {actor.Power.Remaining:0.0}s"
                : input != null && input.IsChargingPower ? $"charging {input.PowerChargeProgress * 100f:0}%" : "power off";
            Row(x, ref y, w, $"    Power: {power}   x4 {actor.CombatResources.QuadCombos}   last attempt {(string.IsNullOrEmpty(mc.LastPowerInfo) ? "-" : mc.LastPowerInfo)}");
            Row(x, ref y, w, $"    attack {mc.CurrentTag} {mc.CurrentAttackBonus}   last combo {(string.IsNullOrEmpty(mc.LastComboInfo) ? "-" : mc.LastComboInfo)}");
            var rec = input != null ? input.Recognizer : null;
            if (rec != null)
            {
                var recent = input.RecentInputs;
                var sb = new System.Text.StringBuilder();
                for (int i = recent != null ? System.Math.Max(0, recent.Count - 8) : 0; recent != null && i < recent.Count; i++)
                    sb.Append(sb.Length > 0 ? " → " : "").Append(recent[i]);
                Row(x, ref y, w, $"    Inputs: {(sb.Length > 0 ? sb.ToString() : "-")}");
                var cur = rec.CurrentCombo;
                Row(x, ref y, w, $"    String: {rec.CurrentStringText}   combo {(cur != null ? (string.IsNullOrEmpty(cur.debugName) ? cur.id : cur.debugName) : "-")}" +
                                  $"   next {(rec.HasContinuation ? $"{rec.ContinuationText} ({rec.ContinuationRemaining:0.00}s)" : "-")}");
                bool recentExt = rec.LastExtensionTime >= 0f && Time.time - rec.LastExtensionTime < 3f;
                Row(x, ref y, w, $"    Extended: {(recentExt ? rec.LastExtension : "-")}   cancelled/replaced: {(string.IsNullOrEmpty(mc.LastReplaceInfo) ? "-" : mc.LastReplaceInfo)}");
            }
            Row(x, ref y, w, $"    Signature: {(string.IsNullOrEmpty(mc.LastSignatureInfo) ? "-" : mc.LastSignatureInfo)}   Specials {actor.CombatResources.Specials}");
        }

        if (_balanceConfig != null)
        {
            var mods = _balanceConfig.ComputeModifiers(actor.BuildStats, mc?.CurrentMove != null ? mc.CurrentMove.scalingStat : FighterBuildStat.None);
            Row(x, ref y, w, $"    dmgDealt x{mods.DamageDealtMultiplier:F2}  dmgTaken x{mods.DamageTakenMultiplier:F2}  " +
                              $"impact x{mods.KnockbackDealtMultiplier:F2}  res x{mods.ResistanceMultiplier:F2}  timing x{mods.TimingScale:F2}");
        }
    }

    private static List<KeyValuePair<FightAIIntention, float>> SortedByScoreDesc(IReadOnlyDictionary<FightAIIntention, float> scores)
    {
        var list = new List<KeyValuePair<FightAIIntention, float>>(scores);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        return list;
    }

    private static string DescribeDefenseResponse(FightAIIntention intention) => intention switch
    {
        FightAIIntention.Guard         => "Standing Guard",
        FightAIIntention.CrouchGuard   => "Crouch Guard",
        FightAIIntention.Crouch        => "Crouch (evade High)",
        FightAIIntention.Jump          => "Jump (escape Unblockable)",
        FightAIIntention.Retreat       => "Retreat",
        FightAIIntention.BackDash      => "BackDash",
        FightAIIntention.SideStepLeft  => "SideStep (dodge)",
        FightAIIntention.SideStepRight => "SideStep (dodge)",
        FightAIIntention.SideWalk      => "SideWalk (reposition)",
        _ => "(none)",
    };

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
