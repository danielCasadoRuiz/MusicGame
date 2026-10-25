#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play Mode probe of the fight charges in a REAL fight (Main Menu → Song Selection → Fight; the driver puts a COPY of
/// the real save in place and restores the original afterwards). Moves are requested through the SAME events the
/// player's input raises (FightComboDetectedEvent / FightPowerRequestedEvent from the player's FighterInputController),
/// the wallet is the player's real FighterCombatResources (= Run.Wallet).
/// Checks: build panel inside the top bar (player side, clear of the centred timer, no own background), conversion
/// destinations inside it · Special → enhanced Signature spends exactly 1 Special, only it; at 0 the enhanced version is
/// blocked (the free basic one runs) · x3 → triple punch / kick powered for exactly 1 x3, plain and free at 0 · x4 →
/// Power spends ALL x4 (4 s + 1 s per extra), at 0 / already active → rejected ·
/// rapid repeated requests never spend twice, never negative · rejected requests spend nothing · every free combo
/// runs at 0 charges · a multi-hit move pays once · a round change keeps the charges · the HUD shows the wallet's
/// values (dimmed at 0) · move help: only Special / Power carry a cost, never x3.
/// Screenshot Logs/FightChargesProbe/hud.png.  Batch: -executeMethod FightChargesProbe.RunFromCommandLine
/// </summary>
[InitializeOnLoad]
public static class FightChargesProbe
{
    private const string KeyActive = "FightChargesProbe.Active";
    private const string ShotDir = "Logs/FightChargesProbe";
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline;
    private static bool s_hooked;
    private static int s_enhanced, s_basic, s_round;
    private static (int s, int t, int q) s_before;

    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static FightFlowState FightState => FightFlowController.Instance != null ? FightFlowController.Instance.CurrentState : FightFlowState.OpponentSelection;
    private static FighterActor Player => FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.PlayerActor : null;
    private static FighterCombatResources W => Player != null ? Player.CombatResources : null;
    private static FighterMoveController MC => Player != null ? Player.GetComponentInChildren<FighterMoveController>() : null;
    private static FightMoveSetSO MoveSet => Resources.Load<AppConfigSO>("AppConfig")?.fightFlow?.defaultPlayerCombatProfile?.moveSet;
    private static FightCombatBalanceConfig Balance => Resources.Load<AppConfigSO>("AppConfig")?.combatBalance;
    private static FightComboDefinition Finisher(FightComboBonus kind) => Combos.combos.First(c => c != null && c.attackBonus == kind);
    private static FightComboSetSO Combos => Resources.Load<AppConfigSO>("AppConfig")?.fightFlow?.comboSet;

    static FightChargesProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static bool Idle => MC != null && MC.CurrentMove == null && MC.QueuedMove == null;
    private static bool Fighting => FightState == FightFlowState.Fighting && Player != null;

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (1f, "boot → song selection", () => Flow != GameFlowState.Boot && Flow != GameFlowState.Intro && AppFlowController.Instance != null,
         () => AppFlowController.Instance.RequestState(GameFlowState.SongSelection)),
        (3f, "→ fight", () => Flow == GameFlowState.SongSelection, () => AppFlowController.Instance.RequestState(GameFlowState.Fight)),
        (1f, "fighting", () => Fighting, () =>
        {
            foreach (var ai in FightSceneBootstrap.Instance.OpponentActor.GetComponentsInChildren<FighterAI>(true)) ai.enabled = false;
            EventBus.Subscribe<SignatureExecutedEvent>(e => { if (e.Fighter == Player) { if (e.Enhanced) s_enhanced++; else s_basic++; } });
            Check("the player's wallet IS the run's wallet (one source of truth)", ReferenceEquals(W, GameSession.Instance.Run.Wallet));
            s_round = FightMatchController.Instance != null ? FightMatchController.Instance.CurrentRound : 0;
            Set(1, 2, 2);
        }),
        (1.5f, "HUD integrated", null, () =>
        {
            Directory.CreateDirectory(ShotDir);
            ScreenCapture.CaptureScreenshot($"{ShotDir}/hud.png");
            var view = Find<FightHudView>();
            var panel = view.buildPanel;
            var bar = (RectTransform)panel.transform.parent;
            Rect barR = ScreenRect(bar), panelR = ScreenRect(panel.root), timerR = ScreenRect(view.timerText.rectTransform);
            Check("build panel lives inside the top bar (no separate floating panel)", bar.name == "TopBar" && Inside(panelR, barR) && !(panel.GetComponent<Image>()?.enabled ?? false),
                  $"parent {bar.name}, panel {panelR}, bar {barR}");
            Check("on the player's side, right of the health bar, clear of the timer", panelR.xMin >= ScreenRect((RectTransform)view.playerHealthFill.transform.parent).xMax && panelR.xMax <= timerR.xMin,
                  $"panel x {panelR.xMin:0}..{panelR.xMax:0}, timer x {timerR.xMin:0}..{timerR.xMax:0}");
            Check("timer still centred", Mathf.Abs(timerR.center.x - barR.center.x) < 2f, $"{timerR.center.x:0} vs {barR.center.x:0}");
            var statsMax = panel.stats.Max(r => ScreenRect(r.root).xMax);
            var chargesMin = panel.resources.Min(c => ScreenRect(c.root).xMin);
            Check("power bars and consumable charges in separate zones", statsMax <= chargesMin, $"stats end {statsMax:0}, charges start {chargesMin:0}");
            Check("conversion destinations are the integrated cells / rows",
                  panel.resources.All(c => BonusConversionController.Cell(panel, c.kind)?.icon != null && BonusConversionController.Cell(panel, c.kind).icon.transform.IsChildOf(bar))
                  && panel.stats.All(r => BonusConversionController.Row(panel, r.stat).root.IsChildOf(bar)));
            var conv = Find<BonusConversionView>();
            Check("after the conversion only the integrated HUD remains (no block / tray on screen)", conv == null || !conv.root.gameObject.activeInHierarchy || conv.blockGroup == null || conv.blockGroup.alpha < 0.01f);
            CheckHud("HUD shows the wallet (Special 1, x3 2, x4 2)");
        }),
        // ── Special ──
        (0f, "Special: enhanced spends 1 Special only", () => Fighting && Idle, () => { s_before = Snap(); Signature(); }),
        (0.1f, "…spent", null, () =>
        {
            var (s, t, q) = Snap();
            Check("enhanced Signature started and spent exactly 1 Special (x3 / x4 untouched)", MC.CurrentMove == MoveSet.signatureSpecial && s == s_before.s - 1 && t == s_before.t && q == s_before.q, $"{Snap()}");
            CheckHud("HUD updated at once (Special 0, dimmed)");
            var cell = BonusConversionController.Cell(Find<FightHudView>().buildPanel, BonusKind.Special);
            Check("Special icon dimmed at 0", cell.icon.color.a < 0.5f, $"alpha {cell.icon.color.a:0.00}");
        }),
        (0f, "multi-hit / whole move: paid once", () => Idle, () =>
        {
            Check("after the whole enhanced move (hits, projectile) still exactly 1 spent", Snap().s == s_before.s - 1, $"{Snap()}");
            s_before = Snap(); Signature();
        }),
        (0.1f, "Special at 0", null, () =>
            Check("at 0 Specials the enhanced version is blocked: the free basic Signature runs, nothing spent", MC.CurrentMove == MoveSet.signatureBasic && Snap() == s_before && W.Specials == 0, $"{MC.CurrentMove?.debugName} {Snap()}")),
        (0f, "two Specials, one each", () => Fighting && Idle, () => { Set(2, 2, 2); s_enhanced = 0; Signature(); }),
        (0.1f, "…first", null, () => Check("with 2: one execution → 1 left", W.Specials == 1 && s_enhanced == 1, $"{Snap()}")),
        (0f, "…second", () => Idle, () => Signature()),
        (0.1f, "…second spent", null, () => Check("second execution → 0 left (exactly one each)", W.Specials == 0 && s_enhanced == 2, $"{Snap()}")),
        (0f, "rapid presses", () => Fighting && Idle, () =>
        {
            Set(1, 2, 2); s_enhanced = 0; s_basic = 0;
            Signature(); Signature(); Signature();   // same frame: the first starts, the rest queue / replace
        }),
        (0f, "rapid presses resolved", () => Idle, () =>
            Check("rapid repeated Signature: 1 Special → exactly 1 enhanced, never negative", W.Specials == 0 && s_enhanced == 1, $"enhanced {s_enhanced}, basic {s_basic}, {Snap()}")),
        // ── Power (x4): spends ALL, 4 s + 1 s per extra ──
        (0.3f, "Power with 1 x4", () => Fighting && Idle, () => { Set(1, 2, 1); s_before = Snap(); Power(); }),
        (0.1f, "…active", null, () =>
        {
            float one = CombatResourceUse.PowerSeconds(1, Balance);
            Check("Power with 1 x4: spends it (Special / x3 untouched), lasts the base time", Player.Power.IsActive && Snap() == (s_before.s, s_before.t, 0)
                  && Mathf.Approximately(Player.Power.Duration, one) && Mathf.Approximately(one, 4f), $"{Snap()}, {Player.Power.Duration:0.#} s");
            Power(); Power();
            Check("repeated requests while active: rejected, nothing more spent", W.QuadCombos == 0 && Player.Power.IsActive);
            Player.Power.Clear(Player);
            Power();
            Check("x4 at 0: Power rejected, never negative", !Player.Power.IsActive && W.QuadCombos == 0, MC.LastPowerInfo);
            CheckHud("HUD follows (x4 0)");
            Set(1, 2, 3); Power();
            Check("Power with 3 x4: spends all three at once, lasts 4 + 2 = 6 s", Player.Power.IsActive && W.QuadCombos == 0 && Mathf.Approximately(Player.Power.Duration, 6f),
                  $"{Snap()}, {Player.Power.Duration:0.#} s — {MC.LastPowerInfo}");
            Player.Power.Clear(Player);
            Set(1, 2, 2); Power();
            Check("Power with 2 x4: 5 s, all spent", W.QuadCombos == 0 && Mathf.Approximately(Player.Power.Duration, 5f), $"{Player.Power.Duration:0.#} s");
            Player.Power.Clear(Player);
        }),
        // ── Finishers (x3) ──
        (0f, "Triple punch with 1 x3", () => Fighting && Idle, () => { Set(1, 1, 1); s_before = Snap(); Combo(Finisher(FightComboBonus.PunchFinisher)); }),
        (0.05f, "…powered", null, () =>
            Check("Triple punch with 1 x3: powered (finisher bonus) and spends exactly 1 x3 (Special / x4 untouched)",
                  MC.CurrentTag == FighterMoveController.AttackTag.Finisher && !MC.CurrentAttackBonus.IsIdentity && Snap() == (s_before.s, 0, s_before.q), $"{MC.CurrentTag} {MC.CurrentAttackBonus} {Snap()}")),
        (0f, "Triple kick with 0 x3", () => Idle, () =>
        {
            Check("whole finisher move paid once", W.TripleCombos == 0);
            s_before = Snap(); Combo(Finisher(FightComboBonus.KickFinisher));
        }),
        (0.05f, "…plain", null, () =>
            Check("Triple kick with 0 x3: still runs, unpowered (plain kick, no bonus), nothing spent",
                  MC.CurrentMove != null && MC.CurrentTag == FighterMoveController.AttackTag.Combo && MC.CurrentAttackBonus.IsIdentity && Snap() == s_before, $"{MC.CurrentMove?.debugName} {MC.CurrentTag} {Snap()}")),
        (0f, "two x3, one each", () => Idle, () => { Set(0, 2, 0); Combo(Finisher(FightComboBonus.KickFinisher)); }),
        (0.05f, "…first", null, () => Check("with 2 x3: one finisher → 1 left", W.TripleCombos == 1 && MC.CurrentTag == FighterMoveController.AttackTag.Finisher, $"{Snap()}")),
        (0f, "…second", () => Idle, () => Combo(Finisher(FightComboBonus.PunchFinisher))),
        (0.05f, "…second spent", null, () => Check("second finisher → 0 left (exactly one each)", W.TripleCombos == 0 && MC.CurrentTag == FighterMoveController.AttackTag.Finisher, $"{Snap()}")),
        (0f, "rapid finishers", () => Idle, () =>
        {
            Set(0, 1, 0);
            var f = Finisher(FightComboBonus.PunchFinisher);
            Combo(f); Combo(f); Combo(f);
        }),
        (0f, "rapid finishers resolved", () => Idle, () => Check("rapid repeated finishers with 1 x3: exactly 1 spent, never negative", W.TripleCombos == 0, $"{Snap()}")),
        // ── Rejections ──
        (0f, "rejected requests spend nothing", () => Fighting && Idle, () =>
        {
            Set(1, 2, 1); s_before = Snap();
            MC.DebugSetActive(false);   // the move system refuses (as outside Fighting)
            Signature(); Power();
            bool none = Snap() == s_before && MC.CurrentMove == null && !Player.Power.IsActive;
            MC.DebugSetActive(true);
            Check("rejected Signature / Power spend no charge", none, $"{Snap()}");
        }),
        // ── Free combos at 0 charges ──
        (0f, "free combos with 0 charges", () => Fighting && Idle, () => Set(0, 0, 0)),
    };

    private static readonly List<(float, string, Func<bool>, Action)> s_tail = new();

    private static IEnumerable<(float, string, Func<bool>, Action)> ComboSteps()
    {
        foreach (var c in Combos.combos.Where(c => c != null && c.moveId != MoveSet.signatureSpecial.id))   // finishers too: unpowered at 0
        {
            var combo = c;
            yield return (0.2f, $"free combo {combo.id}", () => Fighting && Idle, () => { s_before = Snap(); Combo(combo); });
            yield return (0.05f, $"…{combo.id}", null, () =>
            {
                bool ran = MC.CurrentMove != null || MC.QueuedMove != null || combo.steps.All(s => s.button == FightButton.None);
                Check($"free combo '{combo.id}' runs at 0 charges, spends nothing", ran && Snap() == (0, 0, 0) && string.IsNullOrEmpty(MC.LastRejection),
                      $"move {MC.CurrentMove?.debugName ?? MC.QueuedMove?.debugName ?? "-"} {MC.LastRejection}");
            });
        }
        // Round change: the same wallet carries on, nothing restored.
        yield return (0.5f, "round change", () => Fighting && Idle, () => { Set(1, 2, 0); s_before = Snap(); FightMatchController.Instance.DebugSetRoundTimeRemaining(0.05f); });
        yield return (0f, "next round", () => FightMatchController.Instance.CurrentRound > s_round && FightState == FightFlowState.Fighting, () =>
        {
            Check("round change: charges neither restored nor lost, same wallet", Snap() == s_before && ReferenceEquals(W, GameSession.Instance.Run.Wallet), $"{Snap()}");
            CheckHud("HUD after the round change");
        });
        // Move help / tip card data.
        yield return (0f, "move help", null, () =>
        {
            var entries = MoveHelpCatalog.Build(W);
            var costly = entries.Where(e => e.Cost != CombatResourceType.None).ToList();
            Check("move help: triple punch / kick spend x3, Special spends Special, Power spends x4 — nothing else costs",
                  costly.Count == 4 && costly.Count(e => e.Cost == CombatResourceType.TripleCombo) == 2
                  && costly.Any(e => e.Cost == CombatResourceType.Special) && costly.Any(e => e.Cost == CombatResourceType.QuadCombo)
                  && costly.All(e => e.Group == MoveHelpCatalog.Group.Special),
                  string.Join(", ", costly.Select(e => $"{e.Name}:{e.Cost}")));
            Check("same map as the fight (CombatResourceUse)", CombatResourceUse.SignatureCost(MoveSet) == CombatResourceType.Special && CombatResourceUse.PowerCost == CombatResourceType.QuadCombo
                  && CombatResourceUse.FinisherCharge == CombatResourceType.TripleCombo);
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // The frozen rival takes every probe hit: kept above half health so no round ends mid-check (a KO would move the
    // fight to RoundIntro, where the move system rightly rejects everything). Only the explicit round-change step ends one.
    private static void KeepRivalStanding()
    {
        var rival = FightSceneBootstrap.Instance != null ? FightSceneBootstrap.Instance.OpponentActor : null;
        if (rival == null || rival.Health == null || rival.Health.CurrentHealth > rival.Health.MaxHealth * 0.5f) return;
        typeof(FighterHealth).GetProperty("CurrentHealth").SetValue(rival.Health, rival.Health.MaxHealth);
    }

    private static (int s, int t, int q) Snap() => (W.Specials, W.TripleCombos, W.QuadCombos);

    private static void Set(int special, int triple, int quad)
    {
        W.TryConsume(CombatResourceType.Special, W.Specials);
        W.TryConsume(CombatResourceType.TripleCombo, W.TripleCombos);
        W.TryConsume(CombatResourceType.QuadCombo, W.QuadCombos);
        W.DebugGrant(triple, quad, special);
    }

    private static void Signature() => Combo(Combos.combos.First(c => c.moveId == MoveSet.signatureSpecial.id));
    private static int s_string = 1000;
    private static void Combo(FightComboDefinition c) =>
        EventBus.Publish(new FightComboDetectedEvent { Source = Player.InputController, Combo = c, StringId = ++s_string, ReplacesPrevious = false });
    private static void Power() => EventBus.Publish(new FightPowerRequestedEvent { Source = Player.InputController });

    private static void CheckHud(string name)
    {
        var panel = Find<FightHudView>().buildPanel;
        var wrong = panel.resources.Where(c => c.value.text != FightController.ChargeValue(W, c.kind).ToString())
                                   .Select(c => $"{c.kind} shows {c.value.text} (real {FightController.ChargeValue(W, c.kind)})").ToList();
        Check(name, wrong.Count == 0, string.Join(", ", wrong));
    }

    private static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
    }

    private static bool Inside(Rect a, Rect b) => a.xMin >= b.xMin - 0.5f && a.xMax <= b.xMax + 0.5f && a.yMin >= b.yMin - 0.5f && a.yMax <= b.yMax + 0.5f;

    private static T Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();

    private static void Tick()
    {
        if (!SessionState.GetBool(KeyActive, false)) { EditorApplication.update -= Tick; return; }
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (!s_hooked)
            {
                s_hooked = true;
                Application.logMessageReceived += (msg, _, type) => { if (type == LogType.Exception || type == LogType.Error) { s_errors++; s_log.AppendLine($"  [{type}] {msg}"); } };
            }
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 240; }
            KeepRivalStanding();
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} flow={Flow} fight={FightState}"); Finish(2); return; }
            if (s_step == Steps.Length && s_tail.Count == 0) s_tail.AddRange(ComboSteps());
            int total = Steps.Length + s_tail.Count;
            if (s_step >= total) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = s_step < Steps.Length ? Steps[s_step] : s_tail[s_step - Steps.Length];
            if (now - s_stepStart < step.Item1) return;
            if (step.Item3 != null && !step.Item3()) return;
            Log($"-- {step.Item2} (fight={FightState})");
            step.Item4?.Invoke();
            s_step++; s_stepStart = now;
        }
        catch (Exception e) { Log("EXCEPTION " + e); Finish(2); }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[ChargesProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText("Logs/FightChargesProbe.txt", s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
