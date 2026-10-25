using UnityEngine;

/// <summary>
/// Game events → sounds (persistent, next to GameAudio). One place for the mapping:
///   Fight: HitLanded (light / heavy by damage share) + the hit character's hurt vocalisation (not every hit),
///          HitBlocked, SPECIAL spent (effect + the attacker's effort vocalisation), KO = the fall: impact + the KO'd
///          character's final vocalisation. The match end has NO jingle: the accent is the defeated character; its final
///          vocalisation sounds at the match end only when no KO already voiced it (time-out decision) — never twice.
///          The winner keeps its animation and stays silent.
///   Runner: chaser contact / trapped penalties (one RunnerPenaltyEvent per penalty — never per frame). The chaser's
///          wake / activation and the countdown are played where they happen (RunnerChaser, CountdownController).
/// Vocalisations are recorded CC0 clips (GameSfxLibrary.playerEfforts / EffortsFor(composer id)), shared by all languages.
/// </summary>
public class GameSfxEvents : MonoBehaviour
{
    [Tooltip("A hit removing at least this share of the defender's max health sounds heavy.")]
    [SerializeField] private float heavyDamageShare = 0.08f;
    [Tooltip("Chance a landed hit makes the hit character vocalise (its cue's cooldown also limits it).")]
    [SerializeField, Range(0f, 1f)] private float hurtVoiceChance = 0.55f;
    [Tooltip("A KO vocalisation this recent already voiced the defeat: the match end adds nothing.")]
    [SerializeField] private float defeatVoiceWindow = 4f;
    private float _lastDefeatVoice = -99f;

    private System.Action<HitLandedEvent> _onHit;
    private System.Action<HitBlockedEvent> _onBlocked;
    private System.Action<FightResourceSpentEvent> _onSpent;
    private System.Action<FighterKOEvent> _onKO;
    private System.Action<MatchEndedEvent> _onMatchEnded;
    private System.Action<RunnerPenaltyEvent> _onPenalty;

    private static GameAudio A => GameAudio.Instance;
    private static GameSfxLibrary L => A != null ? A.Library : null;

    private void OnEnable()
    {
        _onHit = e =>
        {
            if (L == null) return;
            float max = e.Defender != null && e.Defender.Health != null ? Mathf.Max(1f, e.Defender.Health.MaxHealth) : 100f;
            bool heavy = e.Result.CausesKnockdown || e.Result.FinalDamage / max >= heavyDamageShare;
            A.Play(heavy ? L.hitHeavy : L.hitLight, GameAudioScope.Fight);
            if (Random.value < (heavy ? 1f : hurtVoiceChance)) A.Play(EffortsOf(e.Defender)?.hurt, GameAudioScope.Fight);
        };
        _onBlocked = _ => { if (L != null) A.Play(L.hitBlocked, GameAudioScope.Fight); };
        _onSpent = e =>
        {
            if (L == null || e.Type != CombatResourceType.Special) return;
            A.Play(L.specialLaunch, GameAudioScope.Fight);
            A.Play(EffortsOf(e.Fighter)?.special, GameAudioScope.Fight);
        };
        _onKO = e =>
        {
            if (L == null) return;
            A.Play(L.knockOut, GameAudioScope.Fight);                                   // the body hitting the floor
            if (A.Play(EffortsOf(e.Fighter)?.defeat, GameAudioScope.Fight)) _lastDefeatVoice = Time.unscaledTime;
        };
        _onMatchEnded = e =>
        {
            if (L == null || Time.unscaledTime - _lastDefeatVoice < defeatVoiceWindow) return; // already voiced by the KO
            var loser = e.Winner == FighterSide.Player ? ComposerEfforts() : L.playerEfforts;
            if (A.Play(loser?.defeat, GameAudioScope.Fight)) _lastDefeatVoice = Time.unscaledTime;
        };
        _onPenalty = e =>
        {
            if (L == null) return;
            if (e.Cause == RunnerPenaltyCause.ChaserContact) A.Play(L.chaserContact, GameAudioScope.Runner);
            else if (e.Cause == RunnerPenaltyCause.Trapped) A.Play(L.trapped, GameAudioScope.Runner);
        };
        EventBus.Subscribe(_onHit); EventBus.Subscribe(_onBlocked); EventBus.Subscribe(_onSpent);
        EventBus.Subscribe(_onKO); EventBus.Subscribe(_onMatchEnded); EventBus.Subscribe(_onPenalty);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onHit); EventBus.Unsubscribe(_onBlocked); EventBus.Unsubscribe(_onSpent);
        EventBus.Unsubscribe(_onKO); EventBus.Unsubscribe(_onMatchEnded); EventBus.Unsubscribe(_onPenalty);
    }

    private static VoiceEffortSet EffortsOf(FighterActor fighter)
    {
        if (fighter == null) return null;
        return fighter.Side == FighterSide.Player ? L?.playerEfforts : ComposerEfforts();
    }

    private static VoiceEffortSet ComposerEfforts()
    {
        var opponent = GameSession.Instance != null ? GameSession.Instance.SelectedOpponent : null;
        return opponent != null && L != null ? L.EffortsFor(opponent.id) : null;
    }
}
