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

/// <summary>
/// TEST-ONLY player probe (scene built by ComposerDialogueTestBuild, never in the game's scene list).
/// Runs in a real player — real Addressables catalog, real cache, real remote server — exercises the
/// SAME runtime code the game uses (ContentDownloadManager, ComposerDialogueService/Selector/
/// Presentation, ComposerDialogueBox, TypewriterText) and writes a PASS/FAIL report:
///
///   -probeOut &lt;dir&gt;   report + screenshots folder (required)
///   -probePhase &lt;name&gt;  label of this run (initial, updated, offline…), used in file names
///   -probeFull          also run the synthetic selection / typewriter / presentation checks + screenshots
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
        Directory.CreateDirectory(_outDir);
        Log($"phase={_phase} version={Application.version} persistent={Application.persistentDataPath}");

        // The real content manager (startup catalog check + prefetch of updated dialogues).
        var content = new GameObject("[Content]").AddComponent<ContentDownloadManager>();
        ((IAppModule)content).Initialize(null);
        yield return Wait(content.Initialization, 30f);
        yield return Wait(content.UpdatedTextPrefetch, 30f);
        Log($"content initialized={content.IsInitialized}");

        var app = Resources.Load<AppConfigSO>("AppConfig");
        _roster = app != null ? app.opponentRoster : null;
        Check(_roster != null && _roster.opponents.Length == 12, "roster has 12 composers");

        yield return ContentChecks();
        if (_full)
        {
            SyntheticSelectionChecks();
            _canvas = BuildCanvas();
            yield return BoxChecks();
            yield return PresentationChecks();
            if (!Application.isBatchMode) yield return Screenshots();
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

    // ── Box + typewriter ─────────────────────────────────────────────────────────

    private IEnumerator BoxChecks()
    {
        var box = ComposerDialogueBox.Create(_canvas);
        int continued = 0;
        box.ContinueRequested += () => continued++;
        var timing = new ComposerDialogueTiming { CharactersPerSecond = 30f, PunctuationPauseSeconds = 0.1f, MinContinueDelaySeconds = 0.6f };
        const string text = "Una melodia tota sola? <b>Això</b> és contrapunt en vaga! Amb accents: àèéíïòóúüç·l";

        box.Show("Bach", text, timing);
        yield return null; yield return null;
        int total = box.BodyText.textInfo.characterCount;
        Check(box.IsRevealing && box.BodyText.maxVisibleCharacters < total, $"typewriter reveals progressively ({box.BodyText.maxVisibleCharacters}/{total})");
        Check(!box.BodyText.text.Contains("<b>") || total < text.Length, "rich text tags are not counted as characters");
        Check(box.Press() == ComposerDialogueBox.PressResult.Completed && !box.IsRevealing, "first press completes the line");
        Check(box.Press() == ComposerDialogueBox.PressResult.Ignored, "same-frame second press does nothing");
        yield return null;
        Check(box.Press() == ComposerDialogueBox.PressResult.Ignored && continued == 0, "press before the minimum delay does not continue");
        yield return new WaitForSecondsRealtime(0.7f);
        Check(box.Press() == ComposerDialogueBox.PressResult.Continued && continued == 1, "press after the delay continues exactly once");

        box.Show("Bach", text, new ComposerDialogueTiming { CharactersPerSecond = 0f });
        Check(!box.IsRevealing, "speed 0 shows the whole line at once");
        box.Show("Bach", text, new ComposerDialogueTiming { CharactersPerSecond = float.NaN });
        Check(!box.IsRevealing, "NaN speed shows the whole line at once");

        // Natural completion + auto-continue.
        continued = 0;
        box.Show("Bach", "Curt.", new ComposerDialogueTiming { CharactersPerSecond = 200f, AutoContinueSeconds = 0.3f });
        yield return new WaitForSecondsRealtime(1.2f);
        Check(continued == 1, "auto-continue fires once after the line completes");

        box.Hide();
        Check(!box.IsVisible && string.IsNullOrEmpty(box.CurrentText), "hide empties the box");
        Destroy(box.gameObject);
    }

    // ── Presentation (flow coordination) ─────────────────────────────────────────

    private IEnumerator PresentationChecks()
    {
        var config = ScriptableObject.CreateInstance<FightFlowConfig>();
        config.dialogueCharactersPerSecond = 25f;
        config.dialogueMinContinueDelaySeconds = 0.2f;
        config.dialogueLoadTimeoutSeconds = 3f;
        var box = ComposerDialogueBox.Create(_canvas);
        var p = new ComposerDialoguePresentation(box, config);
        var bach = Opponent("bach"); var mozart = Opponent("mozart");

        // 1. Language change DURING the reveal: same key, new language.
        yield return SetLocale("ca");
        int results = 0; bool lastShown = false;
        p.Begin(bach, ComposerDialogueCategory.Intro, s => { results++; lastShown = s; });
        yield return Until(() => box.IsVisible, 5f);
        string key = p.CurrentKey;
        string caText = box.CurrentText;
        yield return SetLocale("en");
        yield return Until(() => box.CurrentText != caText, 5f);
        var expected = Resolve("bach", key);
        yield return Wait(expected, 10f);
        Check(p.CurrentKey == key && box.CurrentText == expected.Result.Text && expected.Result.LocaleCode == "en",
              $"language change mid-reveal keeps the key ({key}) and shows its English text");
        while (box.IsRevealing) { box.Press(); yield return null; }
        yield return new WaitForSecondsRealtime(0.3f);
        box.Press();
        yield return null;
        Check(results == 1 && lastShown, "continue reports once (shown=true)");
        box.Press(); yield return null;
        Check(results == 1, "no second report after continuing");

        // 2. Language change while the table is still loading.
        ComposerDialogueService.ReleaseAll();
        yield return SetLocale("ca");
        ComposerDialogueService.ReleaseAll();
        p.Begin(bach, ComposerDialogueCategory.Victory, _ => { });
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
        yield return Until(() => box.IsVisible, 5f);
        var exp2 = Resolve("bach", p.CurrentKey);
        yield return Wait(exp2, 10f);
        Check(box.CurrentText == exp2.Result.Text && exp2.Result.LocaleCode == "en", "language change while loading shows the new language");
        p.Cancel();

        // 3. Leaving while loading: nothing reported, nothing shown later.
        ComposerDialogueService.ReleaseAll();
        bool reported = false;
        p.Begin(bach, ComposerDialogueCategory.Intro, _ => reported = true);
        p.Cancel();
        yield return new WaitForSecondsRealtime(1.5f);
        Check(!reported && !box.IsVisible, "cancel during load: no callback, no text");

        // 4. Rival changes before the first load lands: only the new rival's line appears.
        ComposerDialogueService.ReleaseAll();
        p.Begin(bach, ComposerDialogueCategory.Intro, _ => { });
        p.Begin(mozart, ComposerDialogueCategory.Intro, _ => { });
        yield return Until(() => box.IsVisible, 5f);
        yield return new WaitForSecondsRealtime(0.5f);
        Check(p.CurrentKey != null && p.CurrentKey.StartsWith("Composer.mozart."), $"stale rival callback ignored ({p.CurrentKey})");
        p.Cancel();

        // 5. Missing content: carry on without blocking.
        var nobody = ScriptableObject.CreateInstance<OpponentDefinition>();
        nobody.id = "nobody"; nobody.displayName = "Nobody";
        bool? shown = null;
        float t0 = Time.realtimeSinceStartup;
        p.Begin(nobody, ComposerDialogueCategory.Intro, s => shown = s);
        yield return Until(() => shown.HasValue, 6f);
        Check(shown == false && Time.realtimeSinceStartup - t0 < 4f, $"missing table → onFinished(false) in {Time.realtimeSinceStartup - t0:0.00}s");

        // 6. Disabled switch.
        config.composerDialoguesEnabled = false;
        shown = null;
        p.Begin(bach, ComposerDialogueCategory.Intro, s => shown = s);
        Check(shown == false, "dialogues disabled → immediate onFinished(false)");
        config.composerDialoguesEnabled = true;
        Destroy(box.gameObject);
    }

    // ── Screenshots (longest lines, several resolutions) ─────────────────────────

    private IEnumerator Screenshots()
    {
        var box = ComposerDialogueBox.Create(_canvas);
        foreach (string code in new[] { "ca", "en" })
        {
            yield return SetLocale(code);
            string longest = null, speaker = null;
            foreach (var o in _roster.opponents)
            {
                var t = LoadTable(o.id);
                yield return Wait(t, 20f);
                if (t.Result == null) continue;
                foreach (ComposerDialogueCategory c in Enum.GetValues(typeof(ComposerDialogueCategory)))
                    foreach (var k in ComposerDialogueService.GetVariantKeys(t.Result, o.id, c))
                    {
                        var v = t.Result.GetEntry(k).Value;
                        if (longest == null || v.Length > longest.Length) { longest = v; speaker = o.displayName; }
                    }
            }
            Log($"[{code}] longest line ({longest?.Length} chars): {longest}");
            foreach (var (w, h) in new[] { (1920, 1080), (1280, 720), (2560, 1080), (1024, 768) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(0.4f);
                box.Show(speaker, longest, new ComposerDialogueTiming { CharactersPerSecond = 0f });
                yield return new WaitForEndOfFrame();
                yield return null;
                yield return new WaitForEndOfFrame();
                var body = box.BodyText;
                body.ForceMeshUpdate();
                bool fits = body.textInfo.characterCount > 0 && !body.isTextOverflowing && body.textInfo.lineCount <= 4;
                Check(fits, $"[{code}] {Screen.width}x{Screen.height}: longest line fits ({body.textInfo.lineCount} lines)");
                ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, $"subtitle_{code}_{w}x{h}.png"));
                yield return null;
            }
        }
        Destroy(box.gameObject);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private RectTransform BuildCanvas()
    {
        var cam = new GameObject("Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.18f, 0.22f, 0.3f);
        var canvas = UIFactory.RootCanvas();
        var label = UIFactory.CreateText("Backdrop", canvas, "VS", 160, new Color(1f, 1f, 1f, 0.15f), TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);
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
