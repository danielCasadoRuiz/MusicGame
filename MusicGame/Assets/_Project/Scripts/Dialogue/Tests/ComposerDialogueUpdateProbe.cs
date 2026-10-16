using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// TEST-ONLY player probe (scene built by ComposerDialogueTestBuild, never in the game's scene list).
/// Runs in a real player — real Addressables catalog, real cache, real remote server — exercises the
/// SAME runtime code the game uses (ContentDownloadManager, ComposerDialogueService/Selector/
/// Presentation, ComposerSpeechBubble, TypewriterText, the Versus/farewell controllers) and writes a PASS/FAIL report:
///
///   -probeOut &lt;dir&gt;   report + screenshots folder (required)
///   -probePhase &lt;name&gt;  label of this run (initial, updated, offline…), used in file names
///   -probeFull          also run the synthetic selection / bubble / presentation / language checks
///   -probeArena         also run the REAL fight presentation (bootstrap, camera, Versus, countdown, farewell) + screenshots
/// </summary>
public class ComposerDialogueUpdateProbe : MonoBehaviour
{
    private readonly StringBuilder _report = new();
    private int _fails;
    private string _outDir, _phase;
    private bool _full;
    private OpponentRosterSO _roster;
    private RectTransform _canvas;

    private IEnumerator Start()
    {
        _outDir = Arg("-probeOut") ?? Path.Combine(Application.persistentDataPath, "probe");
        _phase  = Arg("-probePhase") ?? "run";
        _full   = Environment.GetCommandLineArgs().Contains("-probeFull");
        bool arena = Environment.GetCommandLineArgs().Contains("-probeArena");
        Directory.CreateDirectory(_outDir);
        Log($"phase={_phase} version={Application.version} persistent={Application.persistentDataPath}");

        // The real content manager (startup catalog check + prefetch of updated dialogues).
        var content = new GameObject("[Content]").AddComponent<ContentDownloadManager>();
        ((IAppModule)content).Initialize(null);
        yield return Wait(content.Initialization, 30f);
        yield return Wait(content.UpdatedTextPrefetch, 30f);
        Log($"content initialized={content.IsInitialized}");

        if (Environment.GetCommandLineArgs().Contains("-probeCampaign"))
        {
            yield return gameObject.AddComponent<CampaignFlowProbe>().Run(_outDir, _phase); // quits the player itself
            yield break;
        }

        var app = Resources.Load<AppConfigSO>("AppConfig");
        _roster = app != null ? app.opponentRoster : null;
        Check(_roster != null && _roster.opponents.Length == 12, "roster has 12 composers");

        yield return ContentChecks();
        if (_full)
        {
            SyntheticSelectionChecks();
            _canvas = BuildCanvas();
            yield return BubbleChecks();
            yield return PresentationChecks();
            yield return LanguageChecks();
        }
        if (arena)
        {
            _canvas ??= BuildCanvas();
            yield return ArenaChecks();
        }

        ComposerDialogueService.ReleaseAll();
        Check(ComposerDialogueService.HeldTableCount == 0, "all dialogue tables released");
        Log(_fails == 0 ? "RESULT PASS" : $"RESULT FAIL ({_fails})");
        File.WriteAllText(Path.Combine(_outDir, $"probe_{_phase}.txt"), _report.ToString(), new UTF8Encoding(false));
        Application.Quit(_fails == 0 ? 0 : 1);
    }

    // ── Real content ─────────────────────────────────────────────────────────────

    private IEnumerator ContentChecks()
    {
        foreach (string code in new[] { "ca", "en" })
        {
            yield return SetLocale(code);
            int total = 0;
            foreach (var o in _roster.opponents)
            {
                var tableTask = LoadTable(o.id);
                yield return Wait(tableTask, 20f);
                var table = tableTask.Result;
                if (table == null) { Fail($"[{code}] table for {o.id} did not load"); continue; }
                var counts = Enum.GetValues(typeof(ComposerDialogueCategory)).Cast<ComposerDialogueCategory>()
                                 .Select(c => ComposerDialogueService.GetVariantKeys(table, o.id, c).Count).ToArray();
                total += counts.Sum();
                Log($"[{code}] {o.id}: Intro {counts[0]} / Defeat {counts[1]} / Victory {counts[2]}");
                if (counts.Any(n => n == 0)) Fail($"[{code}] {o.id} has an empty category");
            }
            Log($"[{code}] total variants {total}");

            var bachTask = LoadTable("bach");
            yield return Wait(bachTask, 20f);
            var bach = bachTask.Result;
            var intro = ComposerDialogueService.GetVariantKeys(bach, "bach", ComposerDialogueCategory.Intro);
            Log($"[{code}] bach Intro keys: {string.Join(",", intro.Select(k => k.Substring(k.LastIndexOf('.') + 1)))}");
            Log($"[{code}] TEXT bach.Intro.001 = {bach?.GetEntry("Composer.bach.Dialogue.Intro.001")?.Value}");
            Log($"[{code}] TEXT bach.Intro.011 = {bach?.GetEntry("Composer.bach.Dialogue.Intro.011")?.Value ?? "(absent)"}");

            // Selection over the real list: never the same key twice in a row, every variant reachable.
            ComposerDialogueSelector.ResetHistory();
            string last = null; bool repeat = false; var seen = new HashSet<string>();
            for (int i = 0; i < 400; i++)
            {
                var k = ComposerDialogueSelector.Pick(intro, "bach", ComposerDialogueCategory.Intro);
                if (k == last) repeat = true;
                last = k; seen.Add(k);
            }
            Check(!repeat, $"[{code}] 400 picks without a consecutive repeat");
            Check(seen.Count == intro.Count, $"[{code}] every one of the {intro.Count} Intro variants was picked");
            Log($"[{code}] picked Intro.011: {seen.Contains("Composer.bach.Dialogue.Intro.011")}");

            var line = PickLine("bach", ComposerDialogueCategory.Defeat);
            yield return Wait(line, 20f);
            Check(line.Result.IsValid && line.Result.LocaleCode == code, $"[{code}] PickLineAsync(bach, Defeat) → {line.Result.Key}");
        }
    }

    // ── Synthetic content: gaps, >10, single, malformed, empty ──────────────────

    private void SyntheticSelectionChecks()
    {
        var table = ScriptableObject.CreateInstance<StringTable>();
        table.SharedData = ScriptableObject.CreateInstance<SharedTableData>();
        table.SharedData.TableCollectionName = "ComposerDialogues_test";
        foreach (int i in new[] { 1, 2, 5, 10, 11, 12, 13, 20, 37, 99, 100, 250, 999, 1000, 4321 })
            table.AddEntry(ComposerDialogueKeys.Key("test", ComposerDialogueCategory.Intro, i), $"line {i}");
        table.AddEntry("Composer.test.Dialogue.Intro.7", "malformed (2 digits)");
        table.AddEntry("Composer.test.Dialogue.Intro.003", "");                     // retired in this language
        table.AddEntry("Composer.other.Dialogue.Intro.001", "other composer");
        table.AddEntry(ComposerDialogueKeys.Key("test", ComposerDialogueCategory.Victory, 42), "only one");

        var intro = ComposerDialogueService.GetVariantKeys(table, "test", ComposerDialogueCategory.Intro);
        Check(intro.Count == 15 && intro[0].EndsWith(".001") && intro[^1].EndsWith(".4321"),
              $"gaps / >10 / 4-digit indices discovered and sorted ({intro.Count})");
        Check(!intro.Any(k => k.EndsWith(".7") || k.EndsWith(".003") || k.Contains(".other.")),
              "malformed, empty and foreign keys ignored");

        var single = ComposerDialogueService.GetVariantKeys(table, "test", ComposerDialogueCategory.Victory);
        string a = ComposerDialogueSelector.Pick(single, "test", ComposerDialogueCategory.Victory);
        string b = ComposerDialogueSelector.Pick(single, "test", ComposerDialogueCategory.Victory);
        Check(single.Count == 1 && a == b && a != null, "single variant is reused without error");
        Check(ComposerDialogueService.GetVariantKeys(table, "test", ComposerDialogueCategory.Defeat).Count == 0 &&
              ComposerDialogueSelector.Pick(new List<string>(), "test", ComposerDialogueCategory.Defeat) == null,
              "empty category → no line (null), no exception");

        // Uniformity (excluding the previous): 15 variants, 15000 picks → each ≈ 1000.
        var hist = new Dictionary<string, int>();
        string last = null; bool repeat = false;
        for (int i = 0; i < 15000; i++)
        {
            var k = ComposerDialogueSelector.Pick(intro, "test", ComposerDialogueCategory.Intro);
            if (k == last) repeat = true;
            last = k;
            hist[k] = (hist.TryGetValue(k, out int n) ? n : 0) + 1;
        }
        Check(!repeat && hist.Count == 15 && hist.Values.Min() > 850 && hist.Values.Max() < 1150,
              $"uniform without consecutive repeats (min {hist.Values.Min()}, max {hist.Values.Max()})");

        // Independent histories per category.
        var two = new List<string> { "x1", "x2" };
        string i1 = ComposerDialogueSelector.Pick(two, "h", ComposerDialogueCategory.Intro);
        string v1 = ComposerDialogueSelector.Pick(two, "h", ComposerDialogueCategory.Victory);
        string i2 = ComposerDialogueSelector.Pick(two, "h", ComposerDialogueCategory.Intro);
        Check(i1 != i2 && v1 != null, "category histories are independent (Intro alternates regardless of Victory)");
    }

    // ── Bubble + typewriter ──────────────────────────────────────────────────────

    private IEnumerator BubbleChecks()
    {
        var bubble = ComposerSpeechBubble.Create(_canvas, null);
        const string text = "Una melodia tota sola? <b>Això</b> és contrapunt en vaga! Amb accents: àèéíïòóúüç·l";
        bubble.Show("Bach", text, 30f, 0.1f);
        yield return null; yield return null;
        int total = bubble.BodyText.textInfo.characterCount;
        Check(bubble.IsRevealing && bubble.BodyText.maxVisibleCharacters < total, $"typewriter reveals progressively ({bubble.BodyText.maxVisibleCharacters}/{total})");
        Check(total < text.Length, "rich text tags are not counted as characters");
        bubble.CompleteReveal();
        Check(!bubble.IsRevealing, "complete shows the whole line");
        bubble.Show("Bach", text, 0f, 0f);
        Check(!bubble.IsRevealing, "speed 0 shows the whole line at once");
        bubble.Show("Bach", text, float.NaN, 0f);
        Check(!bubble.IsRevealing, "NaN speed shows the whole line at once");
        bool faded = false;
        bubble.FadeOut(0.3f, () => faded = true);
        yield return new WaitForSecondsRealtime(0.6f);
        Check(faded && !bubble.IsVisible && string.IsNullOrEmpty(bubble.CurrentText), "fade-out ends hidden, empty, callback once");
        Destroy(bubble.gameObject);
    }

    // ── Presentation (reveal → read hold → done; skip; freeze; language) ───────

    private IEnumerator PresentationChecks()
    {
        var config = ScriptableObject.CreateInstance<FightFlowConfig>();
        config.dialogueCharactersPerSecond = 40f;
        config.dialogueReadHoldSeconds = 1f;
        config.dialogueLoadTimeoutSeconds = 3f;
        var bubble = ComposerSpeechBubble.Create(_canvas, config);
        var p = new ComposerDialoguePresentation(bubble, config);
        var bach = Opponent("bach"); var mozart = Opponent("mozart");
        Func<Vector3?> noAnchor = () => null;

        // 1. Natural route: revealed, then held dialogueReadHoldSeconds, then complete — on its own.
        yield return SetLocale("ca");
        p.Begin(bach, ComposerDialogueCategory.Intro, noAnchor);
        float t0 = Time.realtimeSinceStartup, revealedAt = -1f;
        while (!p.IsReadingComplete && Time.realtimeSinceStartup - t0 < 10f)
        {
            p.Tick(Time.unscaledDeltaTime);
            if (revealedAt < 0f && p.CurrentPhase == ComposerDialoguePresentation.Phase.Holding) revealedAt = Time.realtimeSinceStartup;
            yield return null;
        }
        float hold = Time.realtimeSinceStartup - revealedAt;
        Check(p.CurrentPhase == ComposerDialoguePresentation.Phase.Done && revealedAt > 0f && hold >= 0.95f && hold < 1.3f,
              $"reveal then {hold:0.00}s reading hold (configured 1s) then done, no press needed");
        p.Cancel();
        Check(!bubble.IsVisible, "cancel hides the bubble");

        // 2. Skip while revealing: complete at once, no hold.
        p.Begin(bach, ComposerDialogueCategory.Victory, noAnchor);
        yield return Until(() => { p.Tick(Time.unscaledDeltaTime); return p.CurrentPhase == ComposerDialoguePresentation.Phase.Revealing; }, 5f);
        p.SkipToEnd();
        Check(p.IsReadingComplete && !bubble.IsRevealing && bubble.IsVisible, "skip while revealing: full line, no hold");
        p.Cancel();

        // 3. Skip while loading: the late line must never appear.
        ComposerDialogueService.ReleaseAll();
        p.Begin(bach, ComposerDialogueCategory.Defeat, noAnchor);
        p.SkipToEnd();
        Check(p.CurrentPhase == ComposerDialoguePresentation.Phase.Unavailable, "skip while loading: unavailable at once");
        float until = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < until) { p.Tick(Time.unscaledDeltaTime); yield return null; }
        Check(!bubble.IsVisible, "a line that finishes loading after the skip never shows");
        p.Cancel();

        // 4. Language change mid-reveal: same key, new text typed again.
        yield return SetLocale("ca");
        p.Begin(bach, ComposerDialogueCategory.Intro, noAnchor);
        yield return Until(() => { p.Tick(Time.unscaledDeltaTime); return p.CurrentPhase == ComposerDialoguePresentation.Phase.Revealing; }, 5f);
        string key = p.CurrentKey, caText = bubble.CurrentText;
        yield return SetLocale("en");
        yield return Until(() => { p.Tick(Time.unscaledDeltaTime); return bubble.CurrentText != caText; }, 5f);
        var en = Resolve("bach", key);
        yield return Wait(en, 10f);
        Check(p.CurrentKey == key && bubble.CurrentText == en.Result.Text && bubble.IsRevealing,
              $"language change mid-reveal: same key ({key}), English text typed again");

        // 5. Language change during the hold: complete text, hold restarts.
        yield return Until(() => { p.Tick(Time.unscaledDeltaTime); return p.CurrentPhase == ComposerDialoguePresentation.Phase.Holding; }, 8f);
        yield return new WaitForSecondsRealtime(0.6f);
        p.Tick(0.6f);
        yield return SetLocale("ca");
        yield return Until(() => { p.Tick(0f); return bubble.CurrentText != en.Result.Text; }, 5f);
        float restart = Time.realtimeSinceStartup;
        yield return Until(() => { p.Tick(Time.unscaledDeltaTime); return p.IsReadingComplete; }, 5f);
        float heldAfter = Time.realtimeSinceStartup - restart;
        Check(!bubble.IsRevealing && heldAfter >= 0.9f, $"language change during the hold: full text, hold restarted ({heldAfter:0.00}s)");

        // 6. Frozen (countdown): text swaps, nothing else changes.
        p.Freeze();
        var phase = p.CurrentPhase;
        string before = bubble.CurrentText;
        yield return SetLocale("en");
        yield return Until(() => bubble.CurrentText != before, 5f);
        Check(p.CurrentPhase == phase && !bubble.IsRevealing && bubble.CurrentText != before, "frozen: language swaps the complete text only");
        p.Cancel();

        // 7. Rival changes before the first load lands: only the new rival's line.
        ComposerDialogueService.ReleaseAll();
        p.Begin(bach, ComposerDialogueCategory.Intro, noAnchor);
        p.Begin(mozart, ComposerDialogueCategory.Intro, noAnchor);
        yield return Until(() => { p.Tick(Time.unscaledDeltaTime); return bubble.IsVisible; }, 5f);
        yield return new WaitForSecondsRealtime(0.5f);
        Check(p.CurrentKey != null && p.CurrentKey.StartsWith("Composer.mozart."), $"stale rival load ignored ({p.CurrentKey})");
        p.Cancel();

        // 8. Missing content / disabled: unavailable, never blocking.
        var nobody = ScriptableObject.CreateInstance<OpponentDefinition>();
        nobody.id = "nobody"; nobody.displayName = "Nobody";
        p.Begin(nobody, ComposerDialogueCategory.Intro, noAnchor);
        t0 = Time.realtimeSinceStartup;
        yield return Until(() => { p.Tick(Time.unscaledDeltaTime); return p.IsReadingComplete; }, 6f);
        Check(p.CurrentPhase == ComposerDialoguePresentation.Phase.Unavailable && Time.realtimeSinceStartup - t0 < 4f,
              $"missing table → unavailable in {Time.realtimeSinceStartup - t0:0.00}s");
        config.composerDialoguesEnabled = false;
        p.Begin(bach, ComposerDialogueCategory.Intro, noAnchor);
        Check(p.CurrentPhase == ComposerDialoguePresentation.Phase.Unavailable, "dialogues disabled → unavailable at once");
        p.Cancel();
        Destroy(bubble.gameObject);
        yield return SetLocale("ca");
    }

    // ── Language policy + live refresh ───────────────────────────────────────────

    private IEnumerator LanguageChecks()
    {
        var avail = new List<string> { "ca", "en" };
        void Case(string mode, string device, string expected) =>
            Check(LanguagePreference.ResolveCode(avail, mode, new[] { device }) == expected,
                  $"policy mode={mode} device={device} → {expected}");
        Case("auto", "ca-ES", "ca"); Case("auto", "ca", "ca"); Case("auto", "ca-AD", "ca");
        Case("auto", "en-GB", "en"); Case("auto", "en-US", "en"); Case("auto", "fr-FR", "en");
        Case("auto", "es-ES", "en"); Case("auto", "", "en");
        Case("ca", "en-US", "ca"); Case("en", "ca-ES", "en"); Case("xx", "ca-ES", "ca"); Case("garbage", "fr", "en");

        string saved = PlayerPrefs.GetString(LanguagePreference.PrefKey, null);
        try
        {
            PlayerPrefs.SetString(LanguagePreference.PrefKey, "klingon");
            Check(LanguagePreference.Mode == LanguagePreference.Auto, "invalid stored preference reads as Automatic");
            LanguagePreference.SetMode("en");
            Check(PlayerPrefs.GetString(LanguagePreference.PrefKey) == "en" && LocalizationSettings.SelectedLocale.Identifier.Code == "en",
                  "manual English: stored as the code and applied");
            LanguagePreference.SetMode("ca");
            Check(PlayerPrefs.GetString(LanguagePreference.PrefKey) == "ca" && LocalizationSettings.SelectedLocale.Identifier.Code == "ca",
                  "manual Català: stored and applied");
            LanguagePreference.SetMode(LanguagePreference.Auto);
            string device = LanguagePreference.ResolveCode(avail, "auto", LanguagePreference.DeviceLanguageCodes());
            Check(PlayerPrefs.GetString(LanguagePreference.PrefKey) == "auto" && LocalizationSettings.SelectedLocale.Identifier.Code == device,
                  $"back to Automatic: stored as 'auto' (not the resolved '{device}') and applied");
            Log($"device languages: {string.Join(", ", LanguagePreference.DeviceLanguageCodes())} → {device}");

            // Live refresh of tracked texts.
            yield return SetLocale("ca");
            var go = new GameObject("LocProbe", typeof(RectTransform));
            go.transform.SetParent(_canvas, false);
            var tracked = go.AddComponent<TextMeshProUGUI>();
            var dynamicText = new GameObject("LocProbe2", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            var overwritten = new GameObject("LocProbe3", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            Loc.Set(tracked, "Settings.Volume");
            Loc.Set(dynamicText, "Fight.RoundLabel", "2");
            Loc.Set(overwritten, "Settings.Close");
            overwritten.text = "Mozart"; // a proper name assigned later by other code
            string caVolume = tracked.text, caRound = dynamicText.text;
            int refreshed = 0;
            Action onRefresh = () => refreshed++;
            Loc.Refreshed += onRefresh;
            // Rapid changes: only the last one wins.
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("ca");
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            yield return Until(() => refreshed > 0, 5f);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(tracked.text == Loc.Get("Settings.Volume") && tracked.text != caVolume, $"tracked label follows the language ({caVolume} → {tracked.text})");
            Check(dynamicText.text == Loc.Get("Fight.RoundLabel", "2") && dynamicText.text != caRound, $"dynamic text keeps its argument ({caRound} → {dynamicText.text})");
            Check(overwritten.text == "Mozart", "a text overwritten by other code is left alone");
            Check(refreshed >= 1 && LocalizationSettings.SelectedLocale.Identifier.Code == "en" && tracked.text == Loc.Get("Settings.Volume"),
                  $"three quick changes → the last language wins ({refreshed} refresh(es), stale async ones dropped)");
            Loc.Refreshed -= onRefresh;
            Destroy(go); Destroy(dynamicText.gameObject); Destroy(overwritten.gameObject);
        }
        finally
        {
            if (saved == null) PlayerPrefs.DeleteKey(LanguagePreference.PrefKey); else PlayerPrefs.SetString(LanguagePreference.PrefKey, saved);
            PlayerPrefs.Save();
        }
        yield return SetLocale("ca");
    }

    // ── Arena: the REAL fight presentation components, end to end ───────────────

    private IEnumerator ArenaChecks()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        var flowCfg = app.fightFlow;
        var camCfg = app.fightCamera;
        float travel = camCfg != null ? camCfg.introTravellingSeconds : 3f;
        Log($"config: travel {travel}s, read hold {flowCfg.dialogueReadHoldSeconds}s, cps {flowCfg.dialogueCharactersPerSecond}, fade {flowCfg.dialogueBubbleFadeSeconds}s");

        var keyboard = InputSystem.AddDevice<Keyboard>();
        var rival = _roster.opponents.FirstOrDefault(o => o.id == "monteverdi") ?? _roster.opponents[0];
        // The app's own bootstrap may already own a GameSession (its Awake destroys a second one AND its
        // GameObject) — reuse it, and keep the fight controllers on their own host.
        var host = new GameObject("[ArenaHost]");
        var session = GameSession.Instance != null ? GameSession.Instance : new GameObject("[Session]").AddComponent<GameSession>();
        session.SelectedOpponent = rival;
        session.SelectedOpponentLevelConfig = rival.GetConfigForTier(1, out int tier);
        session.SelectedOpponentTier = tier;
        Log($"rival {rival.id}, tier {tier}, level config {(session.SelectedOpponentLevelConfig != null ? "level " + session.SelectedOpponentLevelConfig.level : "(null)")}");
        var flow = host.AddComponent<FightFlowController>();
        host.AddComponent<FightMatchController>();
        host.AddComponent<FightController>();
        var versus = host.AddComponent<VersusScreenController>();
        host.AddComponent<RoundIntroController>();
        var farewell = host.AddComponent<MatchFarewellController>();

        var states = new List<(FightFlowState state, float time)>();
        Action<FightFlowStateChangedEvent> onState = e => states.Add((e.Current, Time.realtimeSinceStartup));
        EventBus.Subscribe(onState);
        EventBus.Publish(new GameFlowStateChangedEvent { Previous = GameFlowState.MainMenu, Current = GameFlowState.Fight });
        yield return null;

        var camGo = new GameObject("ArenaCamera", typeof(Camera));
        camGo.SetActive(false);
        var bootstrap = camGo.AddComponent<FightSceneBootstrap>();
        typeof(FightSceneBootstrap).GetField("arenaCamera", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(bootstrap, camGo.GetComponent<Camera>());
        camGo.SetActive(true);
        yield return null; yield return null;
        var cam = bootstrap.CameraController;
        Check(cam != null && bootstrap.OpponentActor != null, "real FightSceneBootstrap: arena, fighters and fight camera up");

        // ── Match 1: natural route ──
        float tIntro = Time.realtimeSinceStartup;
        flow.RequestState(FightFlowState.VersusIntro);
        yield return Until(() => bootstrap.OpponentVisualReady, 15f);
        Log($"rival on stage after {Time.realtimeSinceStartup - tIntro:0.00}s (avatar ready={bootstrap.OpponentVisualReady})");
        yield return Until(() => versus.Dialogue.Bubble.IsVisible, 10f);
        Check(versus.Dialogue.Bubble.IsVisible && cam.IsIntroTravelling, "bubble appears over the rival while the camera is still travelling");
        Check(versus.Dialogue.CurrentKey != null && versus.Dialogue.CurrentKey.StartsWith($"Composer.{rival.id}.Dialogue.Intro."), $"Intro line of the definitive rival ({versus.Dialogue.CurrentKey})");
        yield return new WaitForSecondsRealtime(0.5f);
        Check(versus.Dialogue.Bubble.TailVisible, "bubble tail points at the rival's head");
        Check(!states.Any(s => s.state == FightFlowState.RoundIntro), "no countdown while travelling/reading");
        yield return ScreensAt("intro");
        yield return Until(() => flow.CurrentState == FightFlowState.RoundIntro, 20f);
        float tRound = Time.realtimeSinceStartup - tIntro;
        Check(!cam.IsIntroTravelling && flow.CurrentState == FightFlowState.RoundIntro,
              $"RoundIntro only after the pass AND the read hold ({tRound:0.0}s ≥ travel {travel}s)");
        Vector3 naturalPos = cam.transform.position; Quaternion naturalRot = cam.transform.rotation;
        Check(states.Count(s => s.state == FightFlowState.RoundIntro) == 1, "exactly one RoundIntro request");
        Check(versus.Dialogue.Bubble.IsVisible, "bubble stays during the countdown");
        yield return ScreensAt("countdown");
        // ESC during the countdown pauses (normal behaviour) — not a skip.
        PressEsc(keyboard);
        yield return null; yield return null;
        ReleaseEsc(keyboard); yield return null;
        Check(FightController.IsPaused, "ESC during the countdown is the normal pause");
        PressEsc(keyboard);
        yield return null; yield return null;
        ReleaseEsc(keyboard); yield return null;
        Check(!FightController.IsPaused, "ESC again resumes");
        yield return Until(() => flow.CurrentState == FightFlowState.Fighting, 10f);
        yield return new WaitForSecondsRealtime((flowCfg.dialogueBubbleFadeSeconds) + 0.3f);
        Check(flow.CurrentState == FightFlowState.Fighting && !versus.Dialogue.Bubble.IsVisible, "bubble fades out once Fighting starts");

        // ── Match 1 end: player loses → composer Victory farewell, then the result screen ──
        int wonEvents = 0;
        Action<MatchEndedEvent> onEnd = e => wonEvents++;
        EventBus.Subscribe(onEnd);
        FightMatchController.Instance.DebugForceMatchResult(false);
        yield return null;
        Check(flow.CurrentState == FightFlowState.MatchFarewell && wonEvents == 1, "decided match → MatchFarewell (result published once)");
        PressEsc(keyboard); // farewell skip before the bubble even shows
        yield return null; yield return null;
        ReleaseEsc(keyboard); yield return null;
        Check(flow.CurrentState == FightFlowState.MatchLost && !FightController.IsPaused, "ESC in the farewell: straight to MatchLost, no pause");
        Check(!farewell.Dialogue.Bubble.IsVisible, "no farewell bubble left after skipping");
        Check(states.Count(s => s.state == FightFlowState.MatchLost) == 1 && wonEvents == 1, "one MatchLost, no duplicated MatchEndedEvent");

        // ── Match 2 (Fight Again): skip the intro mid-travel → same camera end state ──
        states.Clear();
        flow.RequestState(FightFlowState.VersusIntro);
        yield return Until(() => cam.IsIntroTravelling, 5f);
        yield return new WaitForSecondsRealtime(0.4f);
        Check(cam.IsIntroTravelling, "Fight Again restarts the camera pass from the start");
        PressEsc(keyboard);
        yield return null; yield return null;
        ReleaseEsc(keyboard); yield return null;
        Check(flow.CurrentState == FightFlowState.RoundIntro && !cam.IsIntroTravelling && !FightController.IsPaused,
              "ESC skip: one RoundIntro, pass ended, pause not opened");
        float posErr = Vector3.Distance(cam.transform.position, naturalPos), rotErr = Quaternion.Angle(cam.transform.rotation, naturalRot);
        Check(posErr < 0.05f && rotErr < 1f, $"skip lands on the same framing as the natural route (Δ {posErr:0.000} m, {rotErr:0.00}°)");
        Check(versus.Dialogue.Bubble.IsVisible || versus.Dialogue.CurrentPhase == ComposerDialoguePresentation.Phase.Idle ||
              versus.Dialogue.CurrentPhase == ComposerDialoguePresentation.Phase.Unavailable, "skipped line is complete (or dropped if it was still loading)");
        Check(!versus.Dialogue.Bubble.IsRevealing, "no typing left after the skip");
        yield return Until(() => flow.CurrentState == FightFlowState.Fighting, 10f);
        yield return new WaitForSecondsRealtime(flowCfg.dialogueBubbleFadeSeconds + 0.3f);
        Check(!versus.Dialogue.Bubble.IsVisible, "skipped intro bubble also fades at Fighting");

        // ── Match 2 end: player WINS → composer Defeat farewell, natural route ──
        FightMatchController.Instance.DebugForceMatchResult(true);
        yield return Until(() => farewell.Dialogue.Bubble.IsVisible, 8f);
        Check(farewell.Dialogue.CurrentKey != null && farewell.Dialogue.CurrentKey.Contains(".Dialogue.Defeat."), $"player won → Defeat line ({farewell.Dialogue.CurrentKey})");
        Check(flow.CurrentState == FightFlowState.MatchFarewell, "result screen waits for the farewell");
        yield return ScreensAt("farewell");
        yield return Until(() => flow.CurrentState == FightFlowState.MatchWon, 20f);
        Check(flow.CurrentState == FightFlowState.MatchWon && !farewell.Dialogue.Bubble.IsVisible, "after reading + hold the bubble is gone and MatchWon opens");
        Check(states.Count(s => s.state == FightFlowState.MatchWon) == 1, "one MatchWon");

        // ── Bubble placement rules with the real camera ──
        var bubble = versus.Dialogue.Bubble;
        bubble.Show("Test", "Fora de l'enquadrament", 0f, 0f);
        bubble.AnchorProvider = () => cam.transform.position - cam.transform.forward * 3f; // behind the camera
        yield return null; yield return null;
        var r = bubble.BubbleRect;
        Check(!bubble.TailVisible && InsideCanvas(r), "anchor behind the camera: parked on screen, no tail");
        bubble.AnchorProvider = () => cam.transform.position + cam.transform.forward * 4f + cam.transform.right * 40f; // far off to the side
        yield return null; yield return null;
        Check(!bubble.TailVisible && InsideCanvas(r), "anchor off-screen: clamped inside the margins, no tail");
        bubble.Hide();

        EventBus.Unsubscribe(onState);
        EventBus.Unsubscribe(onEnd);
        EventBus.Publish(new GameFlowStateChangedEvent { Previous = GameFlowState.Fight, Current = GameFlowState.MainMenu });
        yield return null;
        Check(!versus.Dialogue.Bubble.IsVisible && !farewell.Dialogue.Bubble.IsVisible, "leaving Fight clears every bubble");
        Destroy(camGo);
        Destroy(host);
        InputSystem.RemoveDevice(keyboard);
    }

    private bool InsideCanvas(RectTransform r)
    {
        var corners = new Vector3[4];
        r.GetWorldCorners(corners);
        foreach (var c in corners)
            if (c.x < -1f || c.y < -1f || c.x > Screen.width + 1f || c.y > Screen.height + 1f) return false;
        return true;
    }

    /// <summary>Queues an ESC press: the next frame's input update sees it as pressed this frame.
    /// The caller yields, then ReleaseEsc.</summary>
    private static void PressEsc(Keyboard kb) => InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Escape));
    private static void ReleaseEsc(Keyboard kb) => InputSystem.QueueStateEvent(kb, new KeyboardState());

    private IEnumerator ScreensAt(string label)
    {
        if (Application.isBatchMode) yield break;
        ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, $"arena_{label}_{Screen.width}x{Screen.height}.png"));
        yield return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private RectTransform BuildCanvas()
    {
        var canvas = UIFactory.RootCanvas();
        return canvas;
    }

    private OpponentDefinition Opponent(string id) => _roster.opponents.First(o => o.id == id);

    private static async Task<StringTable> LoadTable(string id) =>
        await ComposerDialogueService.LoadTableAsync(id, await ComposerDialogueService.ActiveLocaleAsync());

    private static Task<ComposerDialogueLine> PickLine(string id, ComposerDialogueCategory c) => ComposerDialogueService.PickLineAsync(id, c);
    private static Task<ComposerDialogueLine> Resolve(string id, string key) =>
        ComposerDialogueService.ResolveTextAsync(id, ComposerDialogueCategory.Intro, key);

    private static IEnumerator SetLocale(string code)
    {
        var init = LocalizationSettings.InitializationOperation;
        while (!init.IsDone) yield return null;
        var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
        if (LocalizationSettings.SelectedLocale != locale) LocalizationSettings.SelectedLocale = locale;
        yield return null;
    }

    private IEnumerator Wait(Task task, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
        if (!task.IsCompleted) Fail($"task timed out after {timeout}s");
        else if (task.IsFaulted) Fail($"task faulted: {task.Exception?.GetBaseException().Message}");
    }

    private static IEnumerator Until(Func<bool> condition, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < end) yield return null;
    }

    private void Check(bool ok, string what) { if (ok) Log("PASS  " + what); else Fail(what); }
    private void Fail(string what) { _fails++; Log("FAIL  " + what); }
    private void Log(string line) { _report.AppendLine(line); Debug.Log("[Probe] " + line); }

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }
}
