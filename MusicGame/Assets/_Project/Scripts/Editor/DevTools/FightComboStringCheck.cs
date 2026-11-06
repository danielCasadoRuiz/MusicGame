using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Logic-only check of FightComboRecognizer (combo strings) against the real FightComboSetSO:
/// scripted abstract input sequences with timestamps → the one action each press resolves to.
/// Batch: -executeMethod FightComboStringCheck.RunFromCommandLine (Logs/FightComboStringCheck.txt).
/// </summary>
public static class FightComboStringCheck
{
    private static int _fails;

    public static void RunFromCommandLine()
    {
        var sb = new StringBuilder();
        _fails = 0;
        try { Run(sb); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); _fails++; }
        sb.Insert(0, _fails == 0 ? "[FightComboStringCheck] ALL PASSED\n" : $"[FightComboStringCheck] {_fails} FAILED\n");
        File.WriteAllText("Logs/FightComboStringCheck.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    private const FightButton P = FightButton.Punch, K = FightButton.Kick, T = FightButton.None;

    private struct In
    {
        public FightButton b; public FightHorizontalDirection h; public FightVerticalDirection v; public float dt;
        public In(FightButton b, float dt, FightHorizontalDirection h = FightHorizontalDirection.Neutral, FightVerticalDirection v = FightVerticalDirection.Neutral)
        { this.b = b; this.dt = dt; this.h = h; this.v = v; }
    }

    private static void Run(StringBuilder sb)
    {
        var flow = Resources.Load<AppConfigSO>("AppConfig").fightFlow;
        var F = FightHorizontalDirection.Forward;
        var D = FightVerticalDirection.Down;

        // 2026-11-04: the punch / kick FINISHERS (P P P / K K K) are no longer recognised as strings — the input
        // controller's double/triple press chain decides them (2nd press held chainThirdPressWait for a 3rd).
        // The recognizer gets the real set WITHOUT them, exactly like FighterInputController.RecognizerSet;
        // the 'bb' string is gone (two kicks are two kicks).
        sb.AppendLine($"  set: {flow.comboSet.combos.Length} combos, recognizer sees {RecognizerSet(flow.comboSet).combos.Length} (finishers removed); " +
                      $"'bb' present: {flow.comboSet.combos.Any(c => c != null && c.id == "bb")}");
        if (flow.comboSet.combos.Any(c => c != null && c.id == "bb")) { _fails++; sb.AppendLine("  FAIL  'bb' still in the combo set (run FightFeelSetup)"); }
        Scenario(sb, flow, "PPP → three presses reach the recognizer as normals (the chain makes the finisher)", "normal, normal, normal",
            new In(P, 0f), new In(P, 0.3f), new In(P, 0.3f));
        Scenario(sb, flow, "PPP then K in time → aaa_b", "normal, normal, normal, aaa_b",
            new In(P, 0f), new In(P, 0.3f), new In(P, 0.3f), new In(K, 0.3f));
        Scenario(sb, flow, "PPP (0.55 s gaps) then K → aaa_b timing (0.5) fails → plain kick", "normal, normal, normal, normal",
            new In(P, 0f), new In(P, 0.55f), new In(P, 0.55f), new In(K, 0.3f));
        Scenario(sb, flow, "PPP then K after the continuation window → plain kick", "normal, normal, normal, normal",
            new In(P, 0f), new In(P, 0.3f), new In(P, 0.3f), new In(K, 0.7f));
        Scenario(sb, flow, "KK → two kicks (no 'bb')", "normal, normal",
            new In(K, 0f), new In(K, 0.3f));
        Scenario(sb, flow, "KKPK → bbab", "normal, normal, normal, bbab",
            new In(K, 0f), new In(K, 0.3f), new In(P, 0.3f), new In(K, 0.3f));
        Scenario(sb, flow, "Signature twice → exactly one Signature per press", "special_fireball, special_fireball",
            new In(P, 0f, F, D), new In(P, 0.4f, F, D));
        Scenario(sb, flow, "PP then Signature → Signature (not a 3rd punch / finisher)", "normal, normal, special_fireball",
            new In(P, 0f), new In(P, 0.3f), new In(P, 0.3f, F, D));
        // forward_a (2026-10-25) = a Forward TAP + Punch within 0.3 s: the lunge punch.
        Scenario(sb, flow, "Forward, Punch, Forward → forward_a, no dash (one ordered stream: the punch sits between)", "-, forward_a, -",
            new In(T, 0f, F), new In(P, 0.1f), new In(T, 0.1f, F));
        Scenario(sb, flow, "Forward, Forward → dash", "-, dash_forward",
            new In(T, 0f, F), new In(T, 0.15f, F));
        Scenario(sb, flow, "walking back between punches keeps P P P (direction presses are transparent)", "normal, -, normal, -, normal",
            new In(P, 0f), new In(T, 0.1f, FightHorizontalDirection.Back), new In(P, 0.15f), new In(T, 0.1f, FightHorizontalDirection.Back), new In(P, 0.15f));
        Scenario(sb, flow, "a forward step right before a punch → that punch is forward_a", "normal, -, forward_a",
            new In(P, 0f), new In(T, 0.1f, F), new In(P, 0.15f));
        Scenario(sb, flow, "walk, pause, then PPP then K → aaa_b", "-, normal, normal, normal, aaa_b",
            new In(T, 0f, F), new In(P, 0.4f), new In(P, 0.3f), new In(P, 0.3f), new In(K, 0.3f));

        // Mixed Direction + attack sequences — TEST-ONLY definitions on top of the real set
        // (architecture check; nothing is added to the game's combo data).
        var mixed = ScriptableObject.CreateInstance<FightComboSetSO>();
        var list = new System.Collections.Generic.List<FightComboDefinition>(RecognizerSet(flow.comboSet).combos)
        {
            Def("t_dfp",  Dir(D, true), Dir(F, false), Btn(P)),
            Def("t_dfpk", Dir(D, true), Dir(F, false), Btn(P), Btn(K)),
            Def("t_dpk",  Dir(D, true), Btn(P), Btn(K)),
            Def("t_bfk",  Dir(FightHorizontalDirection.Back, false), Dir(F, false), Btn(K)),
            Def("t_fpp",  Dir(F, false), Btn(P), Btn(P)),
        };
        mixed.combos = list.ToArray();
        Scenario(sb, flow, mixed, "Down → Forward → Punch", "-, -, t_dfp",
            new In(T, 0f, v: D), new In(T, 0.1f, F), new In(P, 0.1f, F));
        Scenario(sb, flow, mixed, "Down → Forward → Punch → Kick replaces it", "-, -, t_dfp, t_dfpk*",
            new In(T, 0f, v: D), new In(T, 0.1f, F), new In(P, 0.1f, F), new In(K, 0.2f));
        Scenario(sb, flow, mixed, "sequential Down → Punch → Kick", "-, normal, t_dpk",
            new In(T, 0f, v: D), new In(P, 0.15f), new In(K, 0.2f));
        Scenario(sb, flow, mixed, "Back → Forward → Kick", "-, -, t_bfk",
            new In(T, 0f, FightHorizontalDirection.Back), new In(T, 0.15f, F), new In(K, 0.15f));
        Scenario(sb, flow, mixed, "Forward → Punch → Punch (a direction-started string; forward_a first)", "-, forward_a, t_fpp*",
            new In(T, 0f, F), new In(P, 0.15f), new In(P, 0.3f));
        Scenario(sb, flow, mixed, "Down → Forward too slow (0.8 s) → no t_dfp, the forward tap + punch is forward_a", "-, -, forward_a",
            new In(T, 0f, v: D), new In(T, 0.8f, F), new In(P, 0.1f));
        Scenario(sb, flow, "Back+P P K → back_aab", "normal, normal, back_aab",
            new In(P, 0f, FightHorizontalDirection.Back), new In(P, 0.3f), new In(K, 0.3f));
    }

    private static FightComboDefinition.Step Dir(FightHorizontalDirection h, bool down) =>
        new() { button = FightButton.None, horizontal = down ? FightHorizontalDirection.Neutral : h, vertical = down ? FightVerticalDirection.Down : FightVerticalDirection.Neutral };
    private static FightComboDefinition.Step Dir(FightVerticalDirection v, bool _) =>
        new() { button = FightButton.None, horizontal = FightHorizontalDirection.Neutral, vertical = v };
    private static FightComboDefinition.Step Btn(FightButton b) => new() { button = b, anyDirection = true };
    private static FightComboDefinition Def(string id, params FightComboDefinition.Step[] steps) =>
        new() { id = id, debugName = id, steps = steps, maxTimeBetweenInputs = 0.5f, moveId = "move_test" };

    private static void Scenario(StringBuilder sb, FightFlowConfig flow, string name, string expected, params In[] inputs) =>
        Scenario(sb, flow, RecognizerSet(flow.comboSet), name, expected, inputs);

    // Same rule as FighterInputController.RecognizerSet: the finishers belong to the press chain.
    private static FightComboSetSO RecognizerSet(FightComboSetSO set)
    {
        var filtered = ScriptableObject.CreateInstance<FightComboSetSO>();
        filtered.combos = set.combos.Where(c => c != null && c.attackBonus != FightComboBonus.PunchFinisher && c.attackBonus != FightComboBonus.KickFinisher).ToArray();
        return filtered;
    }

    private static void Scenario(StringBuilder sb, FightFlowConfig flow, FightComboSetSO set, string name, string expected, params In[] inputs)
    {
        var buffer = new FightInputBuffer(flow.inputBufferWindowSeconds, flow.inputBufferMaxEntries);
        var rec = new FightComboRecognizer(set, buffer, null, flow.comboContinuationWindow);
        float t = Time.time; // the buffer prunes against Time.time — keep scripted events "recent"
        var got = new System.Collections.Generic.List<string>();
        int seq = 0;
        foreach (var i in inputs)
        {
            t += i.dt;
            var e = new FightInputEvent(i.b, i.h, i.v, t, ++seq);
            buffer.Add(e);
            var r = rec.OnNewInput(e);
            got.Add(r.Combo != null ? r.Combo.id + (r.ReplacesPrevious ? "*" : "") : (i.b == T ? "-" : "normal"));
        }
        string actual = string.Join(", ", got);
        bool ok = actual == expected;
        if (!ok) _fails++;
        sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {actual}{(ok ? "" : $"   (expected {expected})")}");
    }
}
