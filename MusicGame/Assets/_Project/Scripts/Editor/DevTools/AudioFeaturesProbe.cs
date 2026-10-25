#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>
/// Focused Play Mode probe of the game audio in the REAL flow (boots Core.unity), rival forced to a composer whose
/// voice sample exists (GameSession.DebugForce, never saved), voice language English:
///   sounds really start (GameAudio.CuePlayed) for: countdown ticks + GO, trapped (once for two simultaneous
///   requests), chaser wake, hits + the hit composer's vocalisation, SPECIAL + the player's vocalisation, victory,
///   the KO'd fighter's vocalisation, PERFECT! per ROUND (real KOs: player full bar -> under the player's bar + the
///   announcer's voice; rival full bar -> under the rival's; winner hurt -> none; never at the end of the match);
///   the Versus line is VOICED from VoicePack/{rival}/en and only that language's pack (+ fx) is held;
///   after leaving Fight a Fight sound is refused (never late).
/// Writes the save (a real won match): back it up. Batch: -executeMethod AudioFeaturesProbe.RunFromCommandLine [-voiceRival beethoven]
/// </summary>
[InitializeOnLoad]
public static class AudioFeaturesProbe
{
    private const string KeyActive = "AudioFeaturesProbe.Active", KeyRival = "AudioFeaturesProbe.Rival";
    private static readonly StringBuilder s_log = new();
    private static int s_pass, s_fail, s_errors, s_step = -1;
    private static double s_stepStart, s_deadline;
    private static bool s_hooked, s_continued;
    private static string s_rival, s_originalLocale;
    private static readonly List<string> s_cues = new();
    private static readonly List<AudioClip> s_voices = new();
    private static readonly List<(AudioClip clip, double time)> s_announced = new();
    private static string s_announcer;
    private static int s_cuesAtCountdown, s_cuesAtCountdownEnd, s_fightWordsFrom;
    private static string Words(int from) => string.Join(",", s_announced.Skip(from).Select(a => Word(a.clip)));
    private static string Word(AudioClip c)
    {
        var a = GameAudio.Instance.Announcer;
        if (a.numbers != null)
            foreach (var n in a.numbers)
                if (n != null && (c == n.three || c == n.two || c == n.one)) return (c == n.three ? "three" : c == n.two ? "two" : "one") + "." + n.language;
        return c == a.three ? "three" : c == a.two ? "two" : c == a.one ? "one" : c == a.tempo ? "tempo" : c == a.fury ? "fury" : c == a.perfect ? "perfect" : "FOREIGN:" + c.name;
    }

    private static MusicClock Clock => MusicClock.Instance;
    private static GameplayManager Manager => UnityEngine.Object.FindFirstObjectByType<GameplayManager>();
    private static GameFlowState Flow => AppBootstrap.Context != null ? AppBootstrap.Context.AppFlow.CurrentState : GameFlowState.Boot;
    private static GameSfxLibrary L => GameAudio.Instance != null ? GameAudio.Instance.Library : null;
    private static int Count(SfxCue c) => c == null ? 0 : s_cues.Count(n => n == c.name);

    static AudioFeaturesProbe() { if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick; }

    public static void RunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-voiceRival");
        SessionState.SetString(KeyRival, i >= 0 && i < args.Length - 1 ? args[i + 1] : "beethoven");
        SessionState.SetBool(KeyActive, true);
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Core.unity");
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static readonly (float wait, string name, Func<bool> until, Action act)[] Steps =
    {
        (12f, "boot: force rival, English voice", null, () =>
        {
            s_rival = SessionState.GetString(KeyRival, "beethoven");
            GameAudio.CuePlayed += (c, _) => s_cues.Add(c.name);
            GameAudio.VoicePlayed += c => s_voices.Add(c);
            GameAudio.AnnouncerPlayed += c => s_announced.Add((c, EditorApplication.timeSinceStartup));
            s_announcer = GameAudio.Instance.Announcer?.voiceId;
            Check("an announcer set picked for the session (complete)", GameAudio.Instance.Announcer != null && GameAudio.Instance.Announcer.IsComplete, s_announcer);
            GameSession.Instance.DebugForce(s_rival, 0);
            s_originalLocale = LocalizationSettings.SelectedLocale != null ? LocalizationSettings.SelectedLocale.Identifier.Code : null;
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            Check("library + mixer loaded, clips preloaded", L != null && L.mixer != null && GameAudio.Instance.IsReady);
            Check("every cue has clips", L.AllCues().All(c => c.HasClips), string.Join(",", L.AllCues().Where(c => !c.HasClips).Select(c => c.name)));
            AppFlowController.Instance.RequestState(GameFlowState.SongSelection);
        }),
        (1f, "select + play", () => { DiagnoseRows(); return PlayableRows().Any(); }, () => PlayableRows().First().button.onClick.Invoke()),
        (1f, "play", null, () => { Find<SongSelectionView>().playButton.onClick.Invoke(); s_cuesAtCountdown = s_cues.Count; }),
        (0f, "countdown done", () => Clock != null && Manager != null && Manager.IsRunning && Clock.HasPlaybackStarted, () =>
        {
            RunnerHazardSystem.Instance?.ClearGeneratedForTest();
            s_cuesAtCountdownEnd = s_cues.Count;
        }),
        (1f, "trapped twice in the same frame", null, () =>
        {
            RunnerHazardSystem.Instance.Penalize(RunnerPenaltyCause.Trapped);
            RunnerHazardSystem.Instance.Penalize(RunnerPenaltyCause.Trapped);
        }),
        (0.5f, "trapped accent", null, () =>
        {
            Check("race countdown: only the announcer - Three, Two, One, Tempo! (once each)", Words(0) == "three,two,one,tempo", Words(0));
            Check("no instrumental tick / GO during the countdown", s_cuesAtCountdownEnd == s_cuesAtCountdown,
                  string.Join(",", s_cues.Skip(s_cuesAtCountdown).Take(s_cuesAtCountdownEnd - s_cuesAtCountdown)));
            Check("trapped: ONE penalty accent", Count(L.trapped) == 1, $"{Count(L.trapped)}");
        }),
        (3f, "chaser ahead", null, () => RunnerHazardSystem.Instance.SpawnTestChaser(12f)),
        (0f, "chaser wakes", () => RunnerHazardSystem.Instance.Chaser != null && RunnerHazardSystem.Instance.Chaser.Current == RunnerChaser.State.WaitingPass, () =>
            Check("chaser: wake warning sound", Count(L.chaserWake) == 1)),
        (0f, "chaser activates", () => RunnerHazardSystem.Instance.Chaser.Current == RunnerChaser.State.Chasing, () =>
        {
            Check("chaser: activation sound", Count(L.chaserActivate) == 1);
            RunnerHazardSystem.Instance.ClearGeneratedForTest();
            Clock.StartPlayback(Manager.SongPlayEnd - 1.5f, MusicTimeDiscontinuityReason.Seek);
            Manager.ResyncCursors(Clock.SongTime);
            s_deadline = EditorApplication.timeSinceStartup + 300;
        }),
        (0f, "end → Fight", () => Flow == GameFlowState.Fight || ClickContinue(), null),
        (0f, "fight countdown", () => FightFlowController.Instance != null && FightFlowController.Instance.CurrentState == FightFlowState.RoundIntro, () => s_fightWordsFrom = s_announced.Count),
        (0f, "versus voice", () => s_voices.Count > 0 || (FightFlowController.Instance != null && FightFlowController.Instance.CurrentState >= FightFlowState.RoundIntro), () =>
        {
            var held = ComposerVoiceService.HeldAddresses.ToList();
            Check($"Versus line VOICED from {s_rival}'s English pack", s_voices.Count > 0, $"voice clips {s_voices.Count}, held: {string.Join(", ", held)}");
            Check("only the active language (+ fx) is loaded", held.Contains($"VoicePack/{s_rival}/en") && held.All(a => a.EndsWith("/en") || a.EndsWith("/fx")), string.Join(", ", held));
        }),
        // Real KOs once each round is really running (Fighting): round 1 player perfect, round 2 rival perfect, round 3 no perfect.
        (0f, "fight", () => FightFlowController.Instance.CurrentState == FightFlowState.Fighting, () =>
        {
            Check("fight countdown: only the announcer - Three, Two, One, Fury! (once each)", Words(s_fightWordsFrom) == "three,two,one,fury", Words(s_fightWordsFrom));
            var ann = GameAudio.Instance.Announcer;
            Check("Catalan game: 3-2-1 in Catalan (Tres/Dos/U), English otherwise; Tempo!/Fury!/Perfect! reserved (English)",
                  ann != null && Word(ann.Number(3, "ca")) == "three.ca" && Word(ann.Number(1, "ca")) == "one.ca" && ann.Number(2, "en") == ann.two
                  && ann.Number(2, "fr") == ann.two, $"{Word(ann?.Number(3, "ca"))}, {Word(ann?.Number(1, "ca"))}");
            Check("same announcer as the race (one set per session)", GameAudio.Instance.Announcer?.voiceId == s_announcer && s_announced.All(a => !Word(a.clip).StartsWith("FOREIGN")), GameAudio.Instance.Announcer?.voiceId);
            var actors = UnityEngine.Object.FindObjectsByType<FighterActor>(FindObjectsSortMode.None);
            var opp = actors.FirstOrDefault(a => a.Side == FighterSide.Opponent);
            var me = actors.FirstOrDefault(a => a.Side == FighterSide.Player);
            var heavy = new FightHitResult { CausesKnockdown = true, FinalDamage = 30f };
            EventBus.Publish(new HitLandedEvent { Attacker = me, Defender = opp, Result = heavy });
            EventBus.Publish(new FightResourceSpentEvent { Fighter = me, Type = CombatResourceType.Special, Amount = 1 });
        }),
        (0.3f, "hit + special sounds", null, () =>
        {
            var fx = ComposerVoiceService.GetEfforts(s_rival);
            Check("heavy hit + the composer's hurt vocalisation (recorded)", Count(L.hitHeavy) == 1 && fx?.hurt != null && Count(fx.hurt) == 1, $"hit {Count(L.hitHeavy)}, hurt {(fx?.hurt != null ? Count(fx.hurt) : -1)}");
            Check("SPECIAL effect + the player's effort (recorded)", Count(L.specialLaunch) == 1 && Count(L.playerEfforts?.special) == 1,
                  $"special {Count(L.specialLaunch)}, player effort {Count(L.playerEfforts?.special)}");
            Actor(FighterSide.Opponent).Health.ApplyDamage(Actor(FighterSide.Opponent).Health.CurrentHealth); // REAL KO, player untouched
        }),
        (0.6f, "round 1: player KO win with a full bar", null, () =>
        {
            var fx = ComposerVoiceService.GetEfforts(s_rival);
            Check("KO: impact + the defeated composer's final vocalisation", Count(L.knockOut) == 1 && fx?.defeat != null && Count(fx.defeat) == 1);
            var hud = Find<FightHudView>();
            Check("PERFECT! under the PLAYER's bar + the announcer says it (KO round, full bar)", hud.playerPerfectText.gameObject.activeInHierarchy
                  && !hud.opponentPerfectText.gameObject.activeInHierarchy && PerfectWords() == 1, $"perfect words {PerfectWords()}");
        }),
        (0f, "round 2 intro", () => FightFlowController.Instance.CurrentState == FightFlowState.RoundIntro, () =>
            Check("next round: PERFECT! cleared", !Find<FightHudView>().playerPerfectText.gameObject.activeInHierarchy)),
        (0f, "round 2", () => FightFlowController.Instance.CurrentState == FightFlowState.Fighting,
            () => Actor(FighterSide.Player).Health.ApplyDamage(Actor(FighterSide.Player).Health.CurrentHealth)), // the rival KOs the player, rival untouched
        (0.6f, "round 2: rival KO win with a full bar", null, () =>
        {
            var hud = Find<FightHudView>();
            Check("PERFECT! under the RIVAL's bar too (either fighter)", hud.opponentPerfectText.gameObject.activeInHierarchy
                  && !hud.playerPerfectText.gameObject.activeInHierarchy && PerfectWords() == 2, $"perfect words {PerfectWords()}");
            Check("the KO'd player's final vocalisation", Count(L.playerEfforts?.defeat) == 1);
        }),
        (0f, "round 3", () => FightFlowController.Instance.CurrentState == FightFlowState.Fighting, () =>
        {
            Actor(FighterSide.Player).Health.ApplyDamage(10f);                          // the winner is NOT at full health
            Actor(FighterSide.Opponent).Health.ApplyDamage(Actor(FighterSide.Opponent).Health.CurrentHealth);
        }),
        (0.6f, "round 3: KO win without a full bar", null, () =>
        {
            var hud = Find<FightHudView>();
            Check("no PERFECT! when the winner's bar is not full", !hud.playerPerfectText.gameObject.activeInHierarchy && !hud.opponentPerfectText.gameObject.activeInHierarchy
                  && PerfectWords() == 2, $"perfect words {PerfectWords()}");
        }),
        (0f, "match result", () => FightFlowController.Instance.CurrentState == FightFlowState.MatchWon, null),
        (1f, "end of the match", null, () =>
        {
            var fx = ComposerVoiceService.GetEfforts(s_rival);
            var view = Find<MatchResultView>();
            Check("match end: no PERFECT! (neither banner nor voice), no jingle, KO cries only at their KOs",
                  (view.perfectBanner == null || !view.perfectBanner.gameObject.activeInHierarchy) && PerfectWords() == 2
                  && Count(fx?.defeat) == 2 && !s_cues.Any(n => n.Contains("Victory") || n.Contains("Defeat") || n.Contains("Perfect")),
                  $"perfect words {PerfectWords()}, composer KO cries {Count(fx?.defeat)}, cues: {string.Join(",", s_cues.Skip(Math.Max(0, s_cues.Count - 6)))}");
            AppFlowController.Instance.RequestState(GameFlowState.MainMenu);
        }),
        (2f, "after leaving Fight", null, () =>
        {
            bool played = GameAudio.Instance.Play(L.hitHeavy, GameAudioScope.Fight);
            Check("a Fight sound is refused once Fight was left (never late)", !played && ComposerVoiceService.HeldAddresses.Count() == 0);
        }),
    };

    private static bool ClickContinue()
    {
        if (s_continued || Manager == null || Manager.IsRunning) return false;
        var end = Find<EndScreenView>();
        if (end == null || !end.continueButton.gameObject.activeInHierarchy) return false;
        s_continued = true;
        end.continueButton.onClick.Invoke();
        return false;
    }

    private static double s_lastDiag;
    private static void DiagnoseRows()
    {
        if (EditorApplication.timeSinceStartup - s_lastDiag < 10) return;
        s_lastDiag = EditorApplication.timeSinceStartup;
        var rows = UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Log($"   rows {rows.Length}: active {rows.Count(r => r.gameObject.activeInHierarchy)}, interactable {rows.Count(r => r.button != null && r.button.interactable)}, " +
            $"played {rows.Count(r => r.Played)}");
    }

    private static FighterActor Actor(FighterSide side) =>
        UnityEngine.Object.FindObjectsByType<FighterActor>(FindObjectsSortMode.None).FirstOrDefault(a => a.Side == side);
    private static int PerfectWords() => s_announced.Count(a => Word(a.clip) == "perfect");

    // Rows appear once the catalog listing has loaded (can take a few seconds): the step waits for them.
    private static IEnumerable<SongRowView> PlayableRows() =>
        UnityEngine.Object.FindObjectsByType<SongRowView>(FindObjectsSortMode.None).OrderBy(r => r.transform.GetSiblingIndex())
            .Where(r => r.button != null && r.button.interactable && !r.Played);
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
            if (s_step < 0) { s_step = 0; s_stepStart = now; s_deadline = now + 300; }
            if (now > s_deadline) { Log($"TIMEOUT at step {s_step} ({(s_step < Steps.Length ? Steps[s_step].name : "")}) flow={Flow} fight={FightFlowController.Instance?.CurrentState}"); Finish(2); return; }
            if (s_step >= Steps.Length) { Finish(s_fail == 0 && s_errors == 0 ? 0 : 1); return; }
            var step = Steps[s_step];
            if (now - s_stepStart < step.wait) return;
            if (step.until != null && !step.until()) return;
            Log($"-- {step.name} (flow={Flow})");
            step.act?.Invoke();
            s_step++; s_stepStart = now;
        }
        catch (Exception e) { Log("EXCEPTION " + e); Finish(2); }
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) s_pass++; else s_fail++;
        Log($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    }

    private static void Log(string m) { s_log.AppendLine(m); Debug.Log("[AudioProbe] " + m); }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(KeyActive, false);
        try
        {
            GameSession.Instance?.DebugForce(null, 0);
            if (s_originalLocale != null) LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale(s_originalLocale);
        }
        catch { }
        Log($"RESULT: {s_pass} passed, {s_fail} failed, {s_errors} errors (exit {code})");
        File.WriteAllText("Logs/AudioFeaturesProbe.txt", s_log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
