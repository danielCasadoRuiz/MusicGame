using UnityEngine;

/// <summary>
/// Player and Opponent share this EXACT component — no separate health system per side. Self-
/// sufficient (loads its own MaxHealth from AppConfig.combatBalance in Awake, same convention as
/// every other Fight controller) so FighterActor.Initialize only needs to add+own it, never hand it
/// numbers. FightHud/FightDebugHUD only ever READ CurrentHealth/MaxHealth/IsKO — nothing about
/// combat/damage decisions lives in the HUD (see FightController's own doc).
///
/// Publishes DamageTakenEvent on every non-zero hit and FighterKOEvent exactly once, the instant
/// CurrentHealth first reaches 0 — see FightCombatEvents' own doc on why these stay significant-
/// events-only, never a per-frame tick.
///
/// Deliberately stops at "IsKO = true" — no round/match resolution exists yet (see this phase's own
/// scope note); a KO'd fighter simply can no longer be damaged again (ApplyDamage no-ops) and
/// FighterAttack/FighterHitReaction both check IsKO before doing anything further to it.
/// </summary>
public class FighterHealth : MonoBehaviour
{
    public float MaxHealth { get; private set; } = 100f;
    public float CurrentHealth { get; private set; }
    public bool IsKO { get; private set; }

    private FighterActor _owner;

    private void Awake()
    {
        var config = Resources.Load<AppConfigSO>("AppConfig")?.combatBalance;
        MaxHealth = config != null ? config.defaultMaxHealth : 100f;
        CurrentHealth = MaxHealth;
    }

    public void SetOwner(FighterActor owner) => _owner = owner;

    public void ApplyDamage(float amount)
    {
        if (IsKO || amount <= 0f) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        EventBus.Publish(new DamageTakenEvent { Fighter = _owner, Amount = amount, RemainingHealth = CurrentHealth });

        if (CurrentHealth <= 0f)
        {
            IsKO = true;
            Debug.Log($"[FighterHealth] {(_owner != null ? _owner.Side.ToString() : "Fighter")} is KO'd.");

            // Reuses FighterHitReaction's own machinery (InterruptMove + IsHitStunned + movement
            // lock) as the enforcement for "KO -> cannot move or attack" — an infinite hit stun IS
            // exactly that, so no separate IsKO flag/plumbing was added to FighterMoveController/
            // FighterMovement (see this phase's own scope note on movement/control-lock authority).
            // Zero knockback: any real knockback from the killing blow itself is applied separately,
            // right after this, by FighterAttack's own ApplyHit call.
            _owner?.HitReaction?.ApplyHit(float.PositiveInfinity, 0f);

            EventBus.Publish(new FighterKOEvent { Fighter = _owner });
        }
    }

    /// <summary>Explicit API for FightMatchController's between-rounds reset — never touched from
    /// anywhere else. IsKO deliberately does NOT need a HitReaction counterpart call here:
    /// HitReaction.ResetForRound() (called separately by FighterActor.ResetForRound) already clears
    /// the infinite hit stun ApplyDamage applied above.</summary>
    public void ResetForRound()
    {
        CurrentHealth = MaxHealth;
        IsKO = false;
    }
}
