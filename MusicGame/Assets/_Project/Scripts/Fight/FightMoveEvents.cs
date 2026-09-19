/// <summary>
/// Fired by FighterMoveController EVERY TIME its own CurrentPhase actually changes — the initial
/// Idle-&gt;Startup at move start, every natural Startup-&gt;Active-&gt;Recovery-&gt;Idle step, AND the
/// abnormal ...-&gt;Idle transition from InterruptMove(). The single hook anything downstream
/// (hitbox activation — see FighterAttack — VFX/SFX, AI) needs; nobody else re-derives Startup/
/// Active/Recovery timing themselves — FighterMoveController remains the sole authority (see its
/// own doc).
/// </summary>
public struct FightMovePhaseChangedEvent
{
    public FighterMoveController Source;
    public FighterMoveState Previous;
    public FighterMoveState Current;
    public FightMoveDefinition Move;
}
