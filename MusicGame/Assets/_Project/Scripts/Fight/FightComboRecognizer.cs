using System.Collections.Generic;
using UnityEngine;

/// <summary>What one input resolved to — exactly ONE action per physical press.</summary>
public struct FightComboResolution
{
    /// <summary>The registered combo this input completed, or null → the press's own normal
    /// (a direction press that completes nothing does nothing — movement handles it).</summary>
    public FightComboDefinition Combo;
    /// <summary>The combo-string this input belongs to (moves started from the same string share it).</summary>
    public int StringId;
    /// <summary>True when this input EXTENDS an already-completed combo of the same string
    /// (PPP → PPPK): the shorter combo's move must be cancelled and replaced, never run alongside.</summary>
    public bool ReplacesPrevious;
    /// <summary>The completed combo being replaced (debug).</summary>
    public FightComboDefinition Replaced;
}

/// <summary>
/// EXTENDABLE COMBO STRINGS over ONE ordered stream of abstract inputs (FightInputBuffer): direction
/// presses (Up/Down/Forward/Back, button None) and Punch/Kick presses (with the direction held at that
/// moment) in their real chronological order, each with a timestamp and a unique press number. A
/// combo may mix them freely (Down → Forward → Punch, Forward → Kick, P P P K …). Keyboard and touch
/// feed the same abstract inputs.
///
/// Every input resolves IMMEDIATELY to exactly one action — never waiting for a longer combo:
///   EXTENSION — the current string S grows by input I when S+I is a prefix of (or equals) some
///     registered combo with its timing satisfied (each gap ≤ that combo's maxTimeBetweenInputs, the
///     whole sequence ≤ its maxTotalDuration, and — if S is itself a completed combo — I within the
///     comboContinuationWindow).
///   FRESH — the longest contiguous suffix of the recent, UNCONSUMED stream ending at I that is a
///     valid combo prefix (this is how Down → Forward → Punch starts while an older string exists).
///   Choice: a COMPLETED combo beats a mere prefix; between two completed ones the longer wins (tie →
///     the extension). Completing a combo whose string was already a completed combo is flagged
///     ReplacesPrevious, so the move system cancels the shorter combo's move and starts the longer.
///   A direction press that extends nothing and completes nothing is TRANSPARENT: it stays in the
///     stream (a later fresh match may start from it) but does not end the current string — walking
///     between punches never breaks P P P. A Punch/Kick that fits nothing ends the string and resolves
///     to its plain normal.
/// CONSUMPTION: the input that completes a combo consumes it and everything before it, so no press is
/// ever reused to complete a second, independent combo; only extending the SAME string builds on it.
/// Works to any depth (PPP → PPPK → PPPKP …).
/// </summary>
public class FightComboRecognizer
{
    private readonly FightComboSetSO _comboSet;
    private readonly FightInputBuffer _buffer;
    private readonly FighterInputController _owner;
    private readonly float _continuationWindow;

    // The current string.
    private readonly List<FightInputEvent> _steps = new();
    private FightComboDefinition _completed;          // the combo the current string node IS (null = prefix only)
    private bool _alive;
    private int _stringId;
    private int _consumedSequence = int.MinValue;     // inputs up to here belong to a completed combo
    private int _nextStringId = 1;

    private readonly List<FightInputEvent> _scratch = new();
    private readonly List<FightInputEvent> _recent = new();

    public FightComboDefinition LastDetected { get; private set; }
    public float LastDetectedTime { get; private set; } = -1f;
    /// <summary>Debug: last "short → long" extension, e.g. "Punch Finisher → Combo A-A-A-B".</summary>
    public string LastExtension { get; private set; } = "";
    public float LastExtensionTime { get; private set; } = -1f;

    public FightComboRecognizer(FightComboSetSO comboSet, FightInputBuffer buffer, FighterInputController owner, float continuationWindow = 0.5f)
    {
        _comboSet = comboSet;
        _buffer = buffer;
        _owner = owner;
        _continuationWindow = Mathf.Max(0.01f, continuationWindow);
    }

    /// <summary>Resolve the input just appended to the buffer (see class doc).</summary>
    public FightComboResolution OnNewInput(FightInputEvent input)
    {
        bool isDirection = input.Button == FightButton.None;
        if (_comboSet == null || _comboSet.combos == null || _comboSet.combos.Length == 0)
            return isDirection ? new FightComboResolution { StringId = _stringId } : new FightComboResolution { StringId = BeginString() };

        // EXTENSION candidate.
        bool extValid = false;
        FightComboDefinition extCombo = null;
        bool live = IsLiveAt(input.Time);
        if (live)
        {
            _scratch.Clear();
            _scratch.AddRange(_steps);
            _scratch.Add(input);
            extValid = IsNode(_scratch, out extCombo);
        }

        // FRESH candidate: longest contiguous unconsumed suffix ending at this input.
        CollectRecent(input);
        int freshStart = -1, freshCompleteStart = -1;
        FightComboDefinition freshCombo = null;
        for (int start = 0; start < _recent.Count; start++)
        {
            _scratch.Clear();
            for (int i = start; i < _recent.Count; i++) _scratch.Add(_recent[i]);
            if (!IsNode(_scratch, out var completed)) continue;
            if (freshStart < 0) freshStart = start;
            if (completed != null && freshCompleteStart < 0) { freshCompleteStart = start; freshCombo = completed; }
        }

        // Choose (see class doc).
        if (extCombo != null || freshCombo != null)
        {
            bool useExt = extCombo != null && (freshCombo == null || extCombo.steps.Length >= freshCombo.steps.Length);
            return useExt ? Extend(input, extCombo) : StartFrom(freshCompleteStart, freshCombo, input);
        }
        if (extValid) return Extend(input, null);
        if (freshStart >= 0 && (!isDirection || !live)) return StartFrom(freshStart, null, input);
        if (isDirection) return new FightComboResolution { StringId = _stringId }; // transparent

        // A Punch/Kick that fits nothing: the string is over, plain normal.
        return new FightComboResolution { StringId = BeginString() };
    }

    /// <summary>Ends the string (stun, knockdown, match end, round reset) — nothing typed so far may
    /// continue or seed a new combo.</summary>
    public void Reset()
    {
        int last = _buffer.Events.Count > 0 ? _buffer.Events[_buffer.Events.Count - 1].Sequence : int.MinValue;
        if (!_alive && _steps.Count == 0 && _consumedSequence >= last) return; // already reset
        _alive = false;
        _completed = null;
        _steps.Clear();
        _consumedSequence = Mathf.Max(_consumedSequence, last);
    }

    // ── Debug ───────────────────────────────────────────────────────────────────
    public string CurrentStringText => _alive && _steps.Count > 0 ? string.Join(" → ", _steps.ConvertAll(s => s.ToString())) : "-";
    public FightComboDefinition CurrentCombo => _alive ? _completed : null;
    public bool HasContinuation => IsLiveAt(Time.time) && NextSteps().Count > 0;
    /// <summary>"Kick | Punch" — the inputs that would continue the current string right now.</summary>
    public string ContinuationText => IsLiveAt(Time.time) ? string.Join(" | ", NextSteps()) : "";
    /// <summary>Seconds left to continue the current string (0 = no continuation possible).</summary>
    public float ContinuationRemaining => HasContinuation ? Mathf.Max(0f, LastTime() + AllowedGap() - Time.time) : 0f;

    // ── Internals ───────────────────────────────────────────────────────────────

    private FightComboResolution Extend(FightInputEvent input, FightComboDefinition completed)
    {
        var previous = _completed;
        _steps.Add(input);
        _completed = completed;
        if (completed != null) Consume(input, completed);
        bool replaces = completed != null && previous != null;
        if (replaces)
        {
            LastExtension = $"{Name(previous)} → {Name(completed)}";
            LastExtensionTime = Time.time;
        }
        return new FightComboResolution { Combo = completed, StringId = _stringId, ReplacesPrevious = replaces, Replaced = replaces ? previous : null };
    }

    private FightComboResolution StartFrom(int start, FightComboDefinition completed, FightInputEvent input)
    {
        BeginString();
        for (int i = start; i < _recent.Count; i++) _steps.Add(_recent[i]);
        _completed = completed;
        _alive = true;
        if (completed != null) Consume(input, completed);
        return new FightComboResolution { Combo = completed, StringId = _stringId };
    }

    private int BeginString()
    {
        _steps.Clear();
        _completed = null;
        _alive = false;
        _stringId = _nextStringId++;
        return _stringId;
    }

    private void Consume(FightInputEvent input, FightComboDefinition completed)
    {
        _consumedSequence = input.Sequence;
        LastDetected = completed;
        LastDetectedTime = Time.time;
    }

    // Recent unconsumed inputs in chronological (press) order, ending with `input`.
    private void CollectRecent(FightInputEvent input)
    {
        _recent.Clear();
        var events = _buffer.Events;
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Sequence > _consumedSequence && e.Sequence < input.Sequence) _recent.Add(e);
        }
        _recent.Add(input);
    }

    // The string can still be continued by an input at `time`.
    private bool IsLiveAt(float time) => _alive && time - LastTime() <= AllowedGap();

    private float LastTime() => _steps.Count > 0 ? _steps[_steps.Count - 1].Time : float.NegativeInfinity;

    // Longest gap any still-possible continuation allows; a completed node is also capped by the
    // continuation window. No continuation at all → -1 (the string can't grow).
    private float AllowedGap()
    {
        float gap = -1f;
        foreach (var combo in _comboSet.combos)
            if (combo != null && combo.steps.Length > _steps.Count && PrefixMatches(combo, _steps) && TimingHolds(combo, _steps))
                gap = Mathf.Max(gap, combo.maxTimeBetweenInputs);
        if (_completed != null) gap = Mathf.Min(gap, _continuationWindow);
        return gap;
    }

    private List<string> NextSteps()
    {
        var next = new List<string>();
        foreach (var combo in _comboSet.combos)
        {
            if (combo == null || combo.steps.Length <= _steps.Count || !PrefixMatches(combo, _steps) || !TimingHolds(combo, _steps)) continue;
            string s = combo.steps[_steps.Count].ToString();
            if (!next.Contains(s)) next.Add(s);
        }
        return next;
    }

    // `seq` is a valid node: a prefix of at least one registered combo with its timing satisfied.
    // `completed` = the combo it equals exactly (higher priority wins ties), else null.
    private bool IsNode(List<FightInputEvent> seq, out FightComboDefinition completed)
    {
        completed = null;
        bool node = false;
        foreach (var combo in _comboSet.combos)
        {
            if (combo == null || combo.steps.Length < seq.Count || !PrefixMatches(combo, seq) || !TimingHolds(combo, seq)) continue;
            node = true;
            if (combo.steps.Length == seq.Count && (completed == null || combo.priority > completed.priority))
                completed = combo;
        }
        return node;
    }

    private static bool PrefixMatches(FightComboDefinition combo, List<FightInputEvent> seq)
    {
        if (combo.steps == null || combo.steps.Length < seq.Count) return false;
        for (int i = 0; i < seq.Count; i++)
            if (!combo.steps[i].Matches(seq[i])) return false;
        return true;
    }

    private static bool TimingHolds(FightComboDefinition combo, List<FightInputEvent> seq)
    {
        for (int i = 1; i < seq.Count; i++)
            if (seq[i].Time - seq[i - 1].Time > combo.maxTimeBetweenInputs) return false;
        if (combo.maxTotalDuration > 0f && seq.Count > 1 && seq[seq.Count - 1].Time - seq[0].Time > combo.maxTotalDuration) return false;
        return true;
    }

    private static string Name(FightComboDefinition c) => c == null ? "-" : (!string.IsNullOrEmpty(c.debugName) ? c.debugName : c.id);
}
