using UnityEngine;

/// <summary>
/// Watches a FightInputBuffer against a FightComboSetSO and publishes FightComboDetectedEvent when
/// a sequence matches — entirely separate from normals (FightNormalPunchEvent/Kick fire immediately
/// elsewhere, the instant a button is pressed, regardless of anything below).
///
/// PREFIX POLICY (e.g. "B-B" vs "B-B-A-B", where the former is a strict prefix of the latter):
///   1. On every new input, find the LONGEST combo whose steps match the buffer's tail exactly
///      (timing included) — ties broken by `priority`.
///   2. If that match is itself a strict prefix of some STRICTLY LONGER combo in the set (i.e. the
///      player could still be mid-way through typing the longer one), DON'T fire yet — hold it as
///      "pending" for exactly that combo's own maxTimeBetweenInputs.
///        - If a later input completes the longer combo before the pending deadline, the longer
///          combo becomes the new longest match, the pending short one is superseded (dropped, one
///          pending slot only) and fires as usual — via case 3 below, since by definition nothing
///          in this V1 set is longer than "B-B-A-B" itself.
///        - If the deadline passes with no extension, the short combo fires as originally matched.
///   3. If the match ISN'T a prefix of anything longer, fire it immediately — no delay at all.
/// This means only genuinely AMBIGUOUS combos (short ones that are prefixes of a longer one) ever
/// wait, and only for that combo's own configured window — everything else, including every
/// single-step combo like "Forward + A", resolves the instant it's typed.
/// </summary>
public class FightComboRecognizer
{
    private readonly FightComboSetSO _comboSet;
    private readonly FightInputBuffer _buffer;

    private FightComboDefinition _pendingCombo;
    private float _pendingFireTime;

    public FightComboDefinition LastDetected { get; private set; }
    public float LastDetectedTime { get; private set; } = -1f;

    public FightComboRecognizer(FightComboSetSO comboSet, FightInputBuffer buffer)
    {
        _comboSet = comboSet;
        _buffer = buffer;
    }

    /// <summary>Call right after appending a new FightInputEvent to the buffer.</summary>
    public void OnNewInput()
    {
        var combos = _comboSet != null ? _comboSet.combos : null;
        if (combos == null || combos.Length == 0) return;

        FightComboDefinition best = null;
        for (int i = 0; i < combos.Length; i++)
        {
            var candidate = combos[i];
            if (!MatchesTail(candidate, _buffer.Events)) continue;
            if (best == null || candidate.steps.Length > best.steps.Length ||
                (candidate.steps.Length == best.steps.Length && candidate.priority > best.priority))
                best = candidate;
        }

        if (best == null) return; // nothing complete yet — leave any existing pending timer alone

        // Fresher information supersedes whatever was pending — there's only ever one pending slot.
        _pendingCombo = null;

        bool extendable = false;
        for (int i = 0; i < combos.Length; i++)
        {
            var candidate = combos[i];
            if (candidate == best || candidate.steps.Length <= best.steps.Length) continue;
            if (IsPrefixOf(best, candidate)) { extendable = true; break; }
        }

        if (extendable)
        {
            _pendingCombo = best;
            _pendingFireTime = Time.time + Mathf.Max(0.01f, best.maxTimeBetweenInputs);
        }
        else
        {
            Fire(best);
        }
    }

    /// <summary>Call once per frame regardless of new input — fires a pending short combo once its
    /// grace window expires unextended.</summary>
    public void Tick()
    {
        if (_pendingCombo == null || Time.time < _pendingFireTime) return;
        var combo = _pendingCombo;
        _pendingCombo = null;
        Fire(combo);
    }

    private void Fire(FightComboDefinition combo)
    {
        LastDetected = combo;
        LastDetectedTime = Time.time;
        EventBus.Publish(new FightComboDetectedEvent { Combo = combo });
    }

    private static bool MatchesTail(FightComboDefinition combo, System.Collections.Generic.IReadOnlyList<FightInputEvent> events)
    {
        int n = combo.steps.Length;
        if (n == 0 || events.Count < n) return false;

        int offset = events.Count - n;
        for (int i = 0; i < n; i++)
        {
            var step = combo.steps[i];
            var evt  = events[offset + i];
            if (step.button != evt.Button || step.horizontal != evt.Horizontal || step.vertical != evt.Vertical)
                return false;
            if (i > 0 && evt.Time - events[offset + i - 1].Time > combo.maxTimeBetweenInputs)
                return false;
        }
        return true;
    }

    private static bool IsPrefixOf(FightComboDefinition prefix, FightComboDefinition full)
    {
        for (int i = 0; i < prefix.steps.Length; i++)
        {
            var a = prefix.steps[i];
            var b = full.steps[i];
            if (a.button != b.button || a.horizontal != b.horizontal || a.vertical != b.vertical) return false;
        }
        return true;
    }
}
