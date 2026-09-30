using UnityEngine;

/// <summary>Which combo a finished pickup sequence resolved to.</summary>
public enum PickupComboTier
{
    None,
    Triple,
    Quad,
}

/// <summary>
/// Temporal pickup-combo recognizer (pure C#, no Unity lifecycle): MUSICAL pickups collected within
/// MusicRunnerScoringConfig.comboWindowSeconds of a sequence's first pickup form one sequence. Each
/// sequence is resolved exactly ONCE, to its highest tier — four pickups inside one window is one
/// Quad, never "Triple + Quad". A pending Triple is therefore only granted when its window closes
/// without a fourth pickup (Tick / the next out-of-window pickup / Flush at run end).
///
/// Combos are a collectible resource for the Fight: this class never touches TotalScore.
/// The resolution rules live in Resolve() and the Quad rule (PickupComboQuadRule), so they can be
/// changed in one place.
/// </summary>
public class PickupComboTracker
{
    private readonly MusicRunnerScoringConfig _config;

    private bool  _inSequence;
    private float _sequenceStart;
    private int   _sequenceCount;

    public int TripleCount { get; private set; }
    public int QuadCount   { get; private set; }

    /// <summary>Pickups in the sequence currently open (0 when none) — for HUD feedback only.</summary>
    public int PendingCount => _inSequence ? _sequenceCount : 0;

    public PickupComboTracker(MusicRunnerScoringConfig config) => _config = config;

    private float Window      => _config != null ? _config.comboWindowSeconds : 1.15f;
    private int   TripleNeeds => _config != null ? _config.tripleComboPickups : 3;
    private int   QuadNeeds   => _config != null ? Mathf.Max(_config.quadComboPickups, TripleNeeds + 1) : 4;
    private bool  CloseOnQuad => _config == null || _config.quadRule == PickupComboQuadRule.CloseSequence;

    /// <summary>Registers one musical pickup collected at `songTime`. Returns the tier of any
    /// sequence that got resolved by this call (a previous sequence closing, or this pickup
    /// completing a Quad), else None.</summary>
    public PickupComboTier Register(float songTime)
    {
        var resolved = PickupComboTier.None;
        if (_inSequence && songTime - _sequenceStart > Window)
            resolved = Close();

        if (!_inSequence)
        {
            _inSequence    = true;
            _sequenceStart = songTime;
            _sequenceCount = 0;
        }
        _sequenceCount++;

        if (CloseOnQuad && _sequenceCount >= QuadNeeds)
        {
            var quad = Close();
            if (quad != PickupComboTier.None) resolved = quad;
        }
        return resolved;
    }

    /// <summary>Closes the open sequence once its window has passed (call every frame with the
    /// current song time) — this is what grants a Triple promptly instead of at the next pickup.</summary>
    public PickupComboTier Tick(float songTime)
    {
        if (_inSequence && songTime - _sequenceStart > Window) return Close();
        return PickupComboTier.None;
    }

    /// <summary>Resolves whatever sequence is still open (end of run).</summary>
    public PickupComboTier Flush() => _inSequence ? Close() : PickupComboTier.None;

    /// <summary>Drops the open sequence without granting anything (a fall).</summary>
    public void Cancel()
    {
        _inSequence    = false;
        _sequenceCount = 0;
    }

    public void Reset()
    {
        Cancel();
        TripleCount = 0;
        QuadCount   = 0;
    }

    private PickupComboTier Close()
    {
        var tier = Resolve(_sequenceCount);
        if (tier == PickupComboTier.Quad) QuadCount++;
        else if (tier == PickupComboTier.Triple) TripleCount++;
        Cancel();
        return tier;
    }

    /// <summary>The single tier rule: highest tier the sequence reached, granted once.</summary>
    private PickupComboTier Resolve(int count)
    {
        if (count >= QuadNeeds) return PickupComboTier.Quad;
        if (count >= TripleNeeds) return PickupComboTier.Triple;
        return PickupComboTier.None;
    }
}
