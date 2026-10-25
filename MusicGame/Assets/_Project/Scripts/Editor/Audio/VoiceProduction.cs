#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Localization;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Voice production (editor + batch): inventory of every line to voice, change detection, generation through the
/// offline generator (Tools/VoiceGen/voicegen.py), import, packs and Addressables. The translation is NOT done
/// here: every line is read, already translated, from the existing dialogue tables (ComposerDialogues_{id}).
///
/// Identity of a line: (voice, language, key). Its SIGNATURE = text hash + voice identity hash + engine + model +
/// post-processing settings + seed. A line is pending when it has never been generated, its signature changed
/// (text edited, voice redefined, settings changed) or a file is missing — so editing one phrase regenerates only
/// that phrase, and its pack (one per voice × language) is the only Addressables bundle that changes.
///
/// Files:
///   masters   {repo}/AssetsMaterial/VoiceMasters/{voice}/{lang}/{key}.wav   24-bit production masters (+ _reference/)
///   game      Assets/_Project/Content/Voices/{voice}/{lang}/{key}.wav       16-bit, imported as Vorbis (in memory)
///   packs     Assets/_Project/Content/Voices/{voice}/{lang}/VoicePack_{voice}_{lang}.asset  (fx = vocalisations)
///   manifest  {repo}/AssetsMaterial/VoiceMasters/manifest.json               what was generated from what
/// Batch: -executeMethod VoiceProduction.RunFromCommandLine [-voices a,b] [-languages en] [-limit N] [-force] [-jobOnly | -ingest JOB]
/// </summary>
public static class VoiceProduction
{
    public const string DefinitionsFolder = "Assets/_Project/Audio/Voices/Definitions";
    public const string ReferencesFolder = "Assets/_Project/Audio/Voices/References"; // editor previews only, never referenced by the game
    public const string GameRoot = "Assets/_Project/Content/Voices";
    public const string PlayerVoice = "player";
    public const string EffortsLang = ComposerVoicePack.EffortsLanguage;

    public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    public static string ToolRoot => Path.Combine(ProjectRoot, "Tools", "VoiceGen");
    public static string PythonExe => Path.Combine(ToolRoot, ".venv", "Scripts", "python.exe");
    public static string MastersRoot => Path.GetFullPath(Path.Combine(ProjectRoot, "..", "AssetsMaterial", "VoiceMasters"));
    public static string ManifestPath => Path.Combine(MastersRoot, "manifest.json");

    // ── Engines (C# mirror of voicegen.py ENGINES) ───────────────────────────────

    public sealed class EngineInfo
    {
        public string Id, Model;
        public string[] Languages;
        public Func<string, object> Config; // tool root → JSON config for voicegen
    }

    public static readonly Dictionary<string, EngineInfo> Engines = new()
    {
        ["qwen3-tts"] = new EngineInfo
        {
            Id = "qwen3-tts",
            Model = "Qwen3-TTS-12Hz-1.7B-VoiceDesign+1.7B-Base",
            Languages = new[] { "en", "es", "fr", "de", "it", "pt", "ru", "ko", "zh", "ja" },
        },
    };

    public static bool Supports(string engine, string language) =>
        Engines.TryGetValue(engine ?? "", out var e) && e.Languages.Contains(language);

    // Post-processing (part of every signature: changing it marks everything pending).
    [Serializable] public class PostSettings { public float target_lufs = -18f, peak_db = -1f, max_lead_silence = 0.3f, keep_silence = 0.04f, tail_silence = 0.12f, min_duration = 0.15f, max_duration = 20f; }
    public static readonly PostSettings Post = new();
    public static string SettingsHash => VoiceTextHash.Of(JsonUtility.ToJson(Post));

    // ── Manifest ─────────────────────────────────────────────────────────────────

    [Serializable] public class ManifestItem
    {
        public string id, voice, language, key, text, textHash, signature, master, game, warnings, error, generatedUtc;
        public bool ok;
        public float duration, leadSilence;
    }
    [Serializable] public class ManifestVoice { public string voice, identityHash, refWav, refText; }
    [Serializable] public class Manifest
    {
        public List<ManifestItem> items = new();
        public List<ManifestVoice> voices = new();
        public ManifestItem Find(string id) => items.FirstOrDefault(i => i.id == id);
        public ManifestVoice FindVoice(string v) => voices.FirstOrDefault(x => x.voice == v);
    }

    public static Manifest LoadManifest()
    {
        try { if (File.Exists(ManifestPath)) return JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath, Encoding.UTF8)) ?? new Manifest(); }
        catch (Exception e) { Debug.LogError("[VoiceProduction] manifest unreadable: " + e.Message); }
        return new Manifest();
    }

    public static void SaveManifest(Manifest m)
    {
        Directory.CreateDirectory(MastersRoot);
        string tmp = ManifestPath + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(m, true), new UTF8Encoding(false)); // no BOM: read by verify.py too
        if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
        File.Move(tmp, ManifestPath);
    }

    // ── Inventory ────────────────────────────────────────────────────────────────

    public enum LineState { UpToDate, New, TextChanged, VoiceChanged, SettingsChanged, MissingFile, Failed, NoEngine }

    public sealed class VoiceLine
    {
        public string Id, Voice, Language, Key, Text, EngineLanguage, Engine;
        public int Seed;
        public string Signature;
        public LineState State;
        public ManifestItem Done;
        public bool IsAnnouncer;
        public string Phrase;          // said within this phrase, cut out as word `Segment` of `Segments` (announcers)
        public int Segment, Segments;
        public float MaxSeconds;       // dry cut (0 = as spoken)
        public string GamePath => $"{GameRoot}/{Voice}/{Language}/{SafeName(Key)}.wav";
        public string MasterPath => Path.Combine(MastersRoot, Voice, Language, SafeName(Key) + ".wav");
    }

    public static string SafeName(string key) => new string(key.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' ? c : '_').ToArray());

    public static List<VoiceDefinition> Definitions() =>
        AssetDatabase.FindAssets("t:VoiceDefinition", new[] { DefinitionsFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<VoiceDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null && !string.IsNullOrEmpty(d.voiceId)).OrderBy(d => d.voiceId).ToList();

    /// <summary>Every line to voice (dialogue lines of every table language + the definitions' fixed extra lines, e.g. the
    /// announcers), with its state. Combat vocalisations are never generated here (recorded CC0 libraries instead).</summary>
    public static List<VoiceLine> Inventory(Manifest manifest)
    {
        var list = new List<VoiceLine>();
        foreach (var def in Definitions())
        {
            // Dialogue lines, already translated, straight from the tables.
            var collection = LocalizationEditorSettings.GetStringTableCollection(ComposerDialogueKeys.TableName(def.voiceId));
            if (collection != null)
                foreach (var table in collection.StringTables)
                {
                    string lang = table.LocaleIdentifier.Code;
                    foreach (var shared in collection.SharedData.Entries)
                    {
                        if (!ComposerDialogueKeys.TryParse(shared.Key, out var id, out _, out _, out _) || id != def.voiceId) continue;
                        var entry = table.GetEntry(shared.Id);
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Value)) continue;
                        list.Add(MakeLine(def, lang, shared.Key, entry.Value, lang));
                    }
                }
            // Fixed extra lines (announcers: shared game content, in the reference language or the line's own language).
            if (def.extraLines != null)
                foreach (var x in def.extraLines)
                    if (!string.IsNullOrWhiteSpace(x.key) && !string.IsNullOrWhiteSpace(x.text))
                    {
                        string lang = string.IsNullOrWhiteSpace(x.language) ? def.referenceLanguage : x.language.Trim();
                        string engineLang = string.IsNullOrWhiteSpace(x.engineLanguage) ? lang : x.engineLanguage.Trim();
                        bool inPhrase = !string.IsNullOrWhiteSpace(x.phrase);
                        string phrase = inPhrase ? x.phrase.Trim() : "";
                        int segments = inPhrase ? PhraseWords(phrase) : 0;
                        var l = MakeLine(def, lang, x.key, x.text, engineLang, x.seed,
                                         AnnouncerFxVersion + (inPhrase ? $"|phrase:{phrase}#{x.segment}/{segments}" : "")
                                         + (x.maxSeconds > 0f ? $"|max:{x.maxSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" : ""));
                        l.Phrase = phrase; l.Segment = x.segment; l.Segments = segments; l.MaxSeconds = x.maxSeconds;
                        l.IsAnnouncer = true;
                        list.Add(l);
                    }
        }
        foreach (var l in list) Classify(l, manifest);
        return list;
    }

    /// <summary>Words of a phrase said in one breath: "Three! Two! One! Tempo!" / "¡Tres, dos, u!" → 4 / 3 (split at ! . ? ,).</summary>
    public static int PhraseWords(string phrase) =>
        phrase.Split(new[] { '!', '.', '?', ',' }, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Trim().Trim('¡', '¿').Length > 0);

    // Announcer words go through voicegen.announcer_fx (arena PA sound); its version is part of their signature.
    public const string AnnouncerFxVersion = "hype1";

    private static VoiceLine MakeLine(VoiceDefinition def, string lang, string key, string text, string engineLang, int seedOverride = 0, string fx = "")
    {
        var l = new VoiceLine
        {
            Id = $"{def.voiceId}|{lang}|{key}", Voice = def.voiceId, Language = lang, Key = key, Text = text.Trim(),
            EngineLanguage = engineLang, Engine = def.EngineFor(engineLang),
        };
        l.Seed = seedOverride != 0 ? seedOverride : (int)(Convert.ToUInt64(VoiceTextHash.Of(l.Id), 16) & 0x7FFFFFFF);
        string model = Engines.TryGetValue(l.Engine, out var e) ? e.Model : "?";
        l.Signature = VoiceTextHash.Of($"{VoiceTextHash.Of(l.Text)}|{def.IdentityHash}|{l.Engine}|{model}|{SettingsHash}|{l.Seed}"
                                       + (fx.Length > 0 ? "|fx:" + fx : ""));
        return l;
    }

    private static void Classify(VoiceLine l, Manifest m)
    {
        l.Done = m.Find(l.Id);
        if (!Supports(l.Engine, l.EngineLanguage)) { l.State = LineState.NoEngine; return; }
        var d = l.Done;
        if (d == null) { l.State = LineState.New; return; }
        if (!d.ok) { l.State = LineState.Failed; return; }
        if (d.signature == l.Signature)
            l.State = File.Exists(Path.Combine(ProjectRoot, l.GamePath)) && File.Exists(l.MasterPath) ? LineState.UpToDate : LineState.MissingFile;
        else if (d.textHash != VoiceTextHash.Of(l.Text)) l.State = LineState.TextChanged;
        else
        {
            var def = Definitions().FirstOrDefault(x => x.voiceId == l.Voice);
            var mv = m.FindVoice(l.Voice);
            l.State = def != null && mv != null && mv.identityHash != def.IdentityHash ? LineState.VoiceChanged : LineState.SettingsChanged;
        }
    }

    public static bool IsPending(VoiceLine l) => l.State != LineState.UpToDate && l.State != LineState.NoEngine;

    // ── Job ──────────────────────────────────────────────────────────────────────

    [Serializable] class JobVoice { public string id, engine, description, ref_text, ref_language, ref_wav, ref_preview; public int seed; public bool redesign; }
    // signature / text_hash / key / pack_language travel with the job, so results ingested later are recorded against the
    // exact text they were generated from (never a text edited meanwhile).
    [Serializable] class JobItem { public string id, voice, text, language, master, game, signature, text_hash, key, pack_language, phrase; public int seed, segment, segments; public float max_seconds; public bool announcer; }
    [Serializable] class QwenModels { public string design, clone; }
    [Serializable] class QwenConfig { public QwenModels models; public string device = "cuda:0", dtype = "bfloat16", attn = "sdpa"; }
    [Serializable] class Job { public List<JobVoice> voices = new(); public List<JobItem> items = new(); public PostSettings post; }

    public static string ReferenceWav(VoiceDefinition d) => Path.Combine(MastersRoot, d.voiceId, "_reference", $"reference_{d.IdentityHash.Substring(0, 8)}.wav");
    public static string ReferencePreview(VoiceDefinition d) => $"{ReferencesFolder}/{d.voiceId}_reference.wav";

    /// <summary>Writes the job for `lines` (and the voice identities they need) and returns its path.</summary>
    public static string WriteJob(IEnumerable<VoiceLine> lines)
    {
        var job = new Job { post = Post };
        var defs = Definitions().ToDictionary(d => d.voiceId);
        foreach (var l in lines)
        {
            if (!defs.TryGetValue(l.Voice, out var def)) continue;
            if (job.voices.All(v => v.id != def.voiceId))
                job.voices.Add(new JobVoice
                {
                    id = def.voiceId, engine = def.engine, description = def.description, ref_text = def.referenceText,
                    ref_language = def.referenceLanguage, seed = def.seed, ref_wav = ReferenceWav(def),
                    ref_preview = Path.Combine(ProjectRoot, ReferencePreview(def)),
                });
            job.items.Add(new JobItem { id = l.Id, voice = l.Voice, text = l.Text, language = l.EngineLanguage, seed = l.Seed,
                                        master = l.MasterPath, game = Path.Combine(ProjectRoot, l.GamePath),
                                        signature = l.Signature, text_hash = VoiceTextHash.Of(l.Text), key = l.Key, pack_language = l.Language, announcer = l.IsAnnouncer,
                                        phrase = l.Phrase ?? "", segment = l.Segment, segments = l.Segments, max_seconds = l.MaxSeconds });
        }
        // Engine configuration as a raw JSON object (JsonUtility cannot serialise dictionaries).
        var qwen = new QwenConfig { models = new QwenModels { design = Path.Combine(ToolRoot, "models", "Qwen3-TTS-12Hz-1.7B-VoiceDesign"),
                                                              clone = Path.Combine(ToolRoot, "models", "Qwen3-TTS-12Hz-1.7B-Base") } };
        string json = JsonUtility.ToJson(job, true);
        json = json.Insert(json.LastIndexOf('}'), $",\n\"engines\": {{\"qwen3-tts\": {JsonUtility.ToJson(qwen)}}}\n");
        string dir = Path.Combine(ToolRoot, "work");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"job_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    public static string ResultsPathFor(string jobPath) => Path.ChangeExtension(jobPath, ".results.jsonl");

    /// <summary>The lines of a job file, as written (text, signature) — for ingesting its results later.</summary>
    public static List<VoiceLine> LinesOfJob(string jobPath)
    {
        var job = JsonUtility.FromJson<Job>(File.ReadAllText(jobPath, Encoding.UTF8));
        return job.items.Select(i => new VoiceLine
        {
            Id = i.id, Voice = i.voice, Language = i.pack_language, Key = i.key, Text = i.text, EngineLanguage = i.language,
            Seed = i.seed, Signature = i.signature, IsAnnouncer = i.announcer, Phrase = i.phrase, Segment = i.segment, Segments = i.segments, MaxSeconds = i.max_seconds,
        }).ToList();
    }

    /// <summary>Applies a results log (voicegen --results) to the manifest and imports: a run made outside Unity, or one
    /// interrupted, is recovered with everything it finished.</summary>
    public static string Ingest(string jobPath)
    {
        var manifest = LoadManifest();
        var run = new Run(manifest, LinesOfJob(jobPath));
        string results = ResultsPathFor(jobPath);
        if (!File.Exists(results)) return "no results file " + results;
        foreach (var line in File.ReadAllLines(results, Encoding.UTF8)) run.Enqueue(line);
        run.Pump();
        return string.Join("\n", run.Log.Where(l => l.StartsWith("FAIL"))) + "\n" + Import(manifest, run.Touched);
    }

    // ── Running the generator ───────────────────────────────────────────────────

    /// <summary>One generator run. Lines are reported as they finish (OnResult), so a cancel keeps them all.</summary>
    public sealed class Run
    {
        public Process Process;
        public int Done, Total;
        public string Current = "";
        public readonly List<string> Log = new();
        public readonly HashSet<(string voice, string lang)> Touched = new();
        public bool Finished, Cancelled;
        public string Fatal;
        private readonly Queue<string> _lines = new();
        private readonly Manifest _manifest;
        private readonly Dictionary<string, VoiceLine> _byId;

        public Run(Manifest manifest, IEnumerable<VoiceLine> lines) { _manifest = manifest; _byId = lines.ToDictionary(l => l.Id); Total = _byId.Count; }

        public void Enqueue(string line) { lock (_lines) _lines.Enqueue(line); }

        /// <summary>Main thread: applies the generator's messages (manifest is saved after every finished line).</summary>
        public void Pump()
        {
            while (true)
            {
                string s;
                lock (_lines) { if (_lines.Count == 0) break; s = _lines.Dequeue(); }
                Handle(s);
            }
        }

        [Serializable] class Msg { public string @event, id, status, error, ref_wav, warnings_text; public bool ok; public int done, total; public float duration, lead_silence, seconds; public string[] warnings; }

        private void Handle(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || !s.TrimStart().StartsWith("{")) return;
            Msg m;
            try { m = JsonUtility.FromJson<Msg>(s); } catch { return; } // the C# field @event is named "event"
            switch (m.@event)
            {
                case "progress": Done = m.done; Total = m.total; Current = m.id; break;
                case "voice":
                    Log.Add($"voice {m.id}: {m.status}");
                    if (m.status == "ready")
                    {
                        var def = Definitions().FirstOrDefault(d => d.voiceId == m.id);
                        var mv = _manifest.FindVoice(m.id);
                        if (mv == null) _manifest.voices.Add(mv = new ManifestVoice { voice = m.id });
                        mv.refWav = m.ref_wav; mv.identityHash = def != null ? def.IdentityHash : ""; mv.refText = def != null ? def.referenceText : "";
                        SaveManifest(_manifest);
                    }
                    break;
                case "result":
                    if (!_byId.TryGetValue(m.id ?? "", out var l)) break;
                    var it = _manifest.Find(l.Id);
                    if (it == null) _manifest.items.Add(it = new ManifestItem { id = l.Id });
                    it.voice = l.Voice; it.language = l.Language; it.key = l.Key; it.text = l.Text; it.textHash = VoiceTextHash.Of(l.Text);
                    it.ok = m.ok; it.error = m.error; it.duration = m.duration; it.leadSilence = m.lead_silence;
                    it.warnings = m.warnings != null ? string.Join("; ", m.warnings) : "";
                    it.signature = m.ok ? l.Signature : "";
                    it.master = l.MasterPath; it.game = l.GamePath; it.generatedUtc = DateTime.UtcNow.ToString("o");
                    SaveManifest(_manifest);
                    Touched.Add((l.Voice, l.Language));
                    Log.Add(m.ok ? $"ok   {l.Id} {m.duration:0.00}s ({m.seconds:0.0}s){(string.IsNullOrEmpty(it.warnings) ? "" : "  ⚠ " + it.warnings)}" : $"FAIL {l.Id}: {m.error}");
                    break;
                case "fatal": Fatal = m.error; Log.Add("FATAL " + m.error); break;
                case "finished": Finished = true; break;
            }
        }

        public void Cancel()
        {
            Cancelled = true;
            try { if (Process != null && !Process.HasExited) Process.Kill(); } catch { }
        }

        public bool Exited => Process == null || Process.HasExited;
    }

    public static Run Start(Manifest manifest, List<VoiceLine> lines)
    {
        var run = new Run(manifest, lines);
        if (!File.Exists(PythonExe)) { run.Fatal = $"generator not installed ({PythonExe}) — see Tools/VoiceGen/README.md"; run.Finished = true; return run; }
        string job = WriteJob(lines);
        var psi = new ProcessStartInfo(PythonExe, $"-u voicegen.py --job \"{job}\" --results \"{ResultsPathFor(job)}\"")
        {
            WorkingDirectory = ToolRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) run.Enqueue(e.Data); };
        p.ErrorDataReceived += (_, e) => { };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        run.Process = p;
        return run;
    }

    // ── Import: clips, packs, Addressables ──────────────────────────────────────

    /// <summary>After a run (finished or cancelled): import the new clips and rebuild the touched packs.</summary>
    public static string Import(Manifest manifest, IEnumerable<(string voice, string lang)> touched)
    {
        var sb = new StringBuilder();
        AssetDatabase.Refresh();
        // Only audio generated from the text CURRENTLY in the tables goes into a pack (an edited line is left out until it
        // is regenerated: never shipped as if it were current).
        var current = Inventory(manifest).ToDictionary(l => l.Id);
        foreach (var (voice, lang) in touched.Distinct())
        {
            var items = manifest.items.Where(i => i.voice == voice && i.language == lang && i.ok && File.Exists(Path.Combine(ProjectRoot, i.game))
                                                  && current.TryGetValue(i.id, out var cur) && cur.State == LineState.UpToDate).ToList();
            bool announcer = Definitions().FirstOrDefault(d => d.voiceId == voice)?.extraLines?.Count > 0;
            foreach (var i in items) ConfigureVoiceImport(i.game, announcer);
            sb.AppendLine(announcer ? BuildAnnouncer(voice, lang, items) : BuildPack(voice, lang, items));
        }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    /// <summary>Dialogue lines: compressed in memory (Vorbis), loaded in the background on demand. Short announcer clips:
    /// decompressed and preloaded (they must sound on the exact frame of the countdown).</summary>
    public static void ConfigureVoiceImport(string path, bool effort)
    {
        var imp = AssetImporter.GetAtPath(path) as AudioImporter;
        if (imp == null) return;
        var s = imp.defaultSampleSettings;
        s.loadType = effort ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = effort ? 0.6f : 0.5f;
        s.preloadAudioData = effort;
        imp.defaultSampleSettings = s;
        imp.forceToMono = true;
        imp.loadInBackground = !effort;
        imp.SaveAndReimport();
    }

    private static string BuildPack(string voice, string lang, List<ManifestItem> items)
    {
        string folder = $"{GameRoot}/{voice}/{lang}";
        Directory.CreateDirectory(Path.Combine(ProjectRoot, folder));
        string path = $"{folder}/VoicePack_{voice}_{lang}.asset";
        var pack = AssetDatabase.LoadAssetAtPath<ComposerVoicePack>(path);
        if (pack == null) { pack = ScriptableObject.CreateInstance<ComposerVoicePack>(); AssetDatabase.CreateAsset(pack, path); }
        var def = Definitions().FirstOrDefault(d => d.voiceId == voice);
        pack.composerId = voice; pack.language = lang;
        pack.engine = def != null ? def.EngineFor(lang == EffortsLang ? def.referenceLanguage : lang) : "";
        pack.voiceDefinitionHash = def != null ? def.IdentityHash : "";
        pack.efforts = null; // vocalisations are shared CC0 recordings in the GameSfxLibrary, never per pack
        pack.lines = items.OrderBy(i => i.key).Select(i => new ComposerVoicePack.Line
        {
            key = i.key, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(i.game), textHash = i.textHash, duration = i.duration,
        }).Where(l => l.clip != null).ToList();
        EditorUtility.SetDirty(pack);
        AssetDatabase.SaveAssets();
        EnsureAddressable(path, voice, lang);
        return $"pack {voice}/{lang}: {pack.lines.Count} lines → {ComposerVoicePack.GroupName(voice, lang)}";
    }

    // The announcers are shared game content (local): their clips go straight into the GameSfxLibrary. The reference
    // language fills the set itself; another language only its countdown numbers (Tempo! / Fury! / Perfect! stay English).
    private static string BuildAnnouncer(string voice, string lang, List<ManifestItem> items)
    {
        var def = Definitions().FirstOrDefault(d => d.voiceId == voice);
        if (def != null && lang != def.referenceLanguage) return BuildAnnouncerNumbers(voice, lang, items);
        var lib = AssetDatabase.LoadAssetAtPath<GameSfxLibrary>(GameAudioSetup.LibraryPath);
        if (lib == null) return "announcer: no GameSfxLibrary (run Build Game Sfx first)";
        var old = (lib.announcers ?? new AnnouncerSet[0]).FirstOrDefault(x => x != null && x.voiceId == voice);
        var list = (lib.announcers ?? new AnnouncerSet[0]).Where(x => x != null && x.voiceId != voice).ToList();
        AudioClip Clip(string key) { var i = items.FirstOrDefault(x => x.key == key); return i != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(i.game) : null; }
        var set = new AnnouncerSet
        {
            voiceId = voice, inRotation = old == null || old.inRotation, three = Clip("Announcer.Three"), two = Clip("Announcer.Two"), one = Clip("Announcer.One"),
            tempo = Clip("Announcer.Tempo"), fury = Clip("Announcer.Fury"), perfect = Clip("Announcer.Perfect"),
            numbers = old?.numbers ?? new List<AnnouncerNumbers>(),
        };
        list.Add(set);
        lib.announcers = list.OrderBy(x => x.voiceId).ToArray();
        EditorUtility.SetDirty(lib);
        return $"announcer {voice}: {items.Count} clips → GameSfxLibrary.announcers ({(set.IsComplete ? "complete" : "INCOMPLETE")})";
    }

    private static string BuildAnnouncerNumbers(string voice, string lang, List<ManifestItem> items)
    {
        var lib = AssetDatabase.LoadAssetAtPath<GameSfxLibrary>(GameAudioSetup.LibraryPath);
        var set = lib != null ? (lib.announcers ?? new AnnouncerSet[0]).FirstOrDefault(x => x != null && x.voiceId == voice) : null;
        if (set == null) return $"announcer {voice}/{lang}: no {voice} set yet (generate its reference language first)";
        AudioClip Clip(string key) { var i = items.FirstOrDefault(x => x.key == key); return i != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(i.game) : null; }
        set.numbers ??= new List<AnnouncerNumbers>();
        set.numbers.RemoveAll(n => n == null || n.language == lang);
        var nums = new AnnouncerNumbers { language = lang, three = Clip("Announcer.Three"), two = Clip("Announcer.Two"), one = Clip("Announcer.One") };
        if (nums.IsComplete) set.numbers.Add(nums);
        set.numbers.Sort((a, b) => string.CompareOrdinal(a.language, b.language));
        EditorUtility.SetDirty(lib);
        return $"announcer {voice}/{lang}: countdown numbers {(nums.IsComplete ? "complete" : "INCOMPLETE (English kept)")}";
    }

    /// <summary>One remote group per voice × language (one bundle), address VoicePack/{voice}/{lang}, labels
    /// VoicePack + VoiceLang_{lang} (so a language can be downloaded / updated on its own).</summary>
    private static void EnsureAddressable(string assetPath, string voice, string lang)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;
        string name = ComposerVoicePack.GroupName(voice, lang);
        var group = settings.FindGroup(name)
                    ?? settings.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        schema.IncludeInBuild = true;
        group.GetSchema<ContentUpdateGroupSchema>().StaticContent = false; // a regenerated line updates this pack only
        EditorUtility.SetDirty(schema);
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.FindAssetEntry(guid);
        if (entry == null || entry.parentGroup != group) entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = ComposerVoicePack.Address(voice, lang);
        entry.SetLabel("VoicePack", true, true);
        entry.SetLabel("VoiceLang_" + lang, true, true);
        EditorUtility.SetDirty(settings);
    }

    // ── Retired content (2026-10-23 feedback) ───────────────────────────────────

    /// <summary>Removes what is no longer used by any event: the TTS combat vocalisations (fx packs + groups, the
    /// player's TTS clips and its voice definition, their masters / manifest entries) and the instrumental countdown /
    /// victory / defeat sounds (cues + files). Shared sounds still in use are never touched. Idempotent.</summary>
    public static string RemoveRetiredContent()
    {
        var sb = new StringBuilder();
        void Del(string assetPath) { if (AssetDatabase.IsValidFolder(assetPath) || File.Exists(Path.Combine(ProjectRoot, assetPath))) { AssetDatabase.DeleteAsset(assetPath); sb.AppendLine("  deleted " + assetPath); } }
        foreach (var dir in Directory.Exists(Path.Combine(ProjectRoot, GameRoot)) ? Directory.GetDirectories(Path.Combine(ProjectRoot, GameRoot)) : new string[0])
            Del($"{GameRoot}/{Path.GetFileName(dir)}/{EffortsLang}");
        Del($"{GameRoot}/{PlayerVoice}");
        Del($"{DefinitionsFolder}/Voice_{PlayerVoice}.asset");
        Del($"{ReferencesFolder}/{PlayerVoice}_reference.wav");
        foreach (var cue in new[] { "CountdownTick", "CountdownGo", "DefeatSting", "VictorySting" }) Del($"{GameAudioSetup.CueFolder}/{cue}.asset");
        Del(GameAudioSetup.Root + "/Sfx/Generated");
        Del(GameAudioSetup.Root + "/Sfx/Kenney/Jingles");

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
            foreach (var g in settings.groups.Where(g => g != null && g.Name.StartsWith("Voice_") && g.Name.EndsWith("_" + EffortsLang)).ToList())
            { settings.RemoveGroup(g); sb.AppendLine("  removed group " + g.Name); }

        var m = LoadManifest();
        int before = m.items.Count;
        m.items.RemoveAll(i => i.language == EffortsLang || i.voice == PlayerVoice);
        m.voices.RemoveAll(v => v.voice == PlayerVoice);
        SaveManifest(m);
        foreach (var dir in Directory.Exists(MastersRoot) ? Directory.GetDirectories(MastersRoot) : new string[0])
        {
            string fx = Path.Combine(dir, EffortsLang);
            if (Directory.Exists(fx)) Directory.Delete(fx, true);
        }
        if (Directory.Exists(Path.Combine(MastersRoot, PlayerVoice))) Directory.Delete(Path.Combine(MastersRoot, PlayerVoice), true);
        sb.AppendLine($"  manifest: {before - m.items.Count} retired entries removed");
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ── Batch (headless) ────────────────────────────────────────────────────────

    public static void RunFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        string Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i < args.Length - 1 ? args[i + 1] : null; }
        var voices = (Arg("-voices") ?? "").Split(',').Where(s => s.Length > 0).ToHashSet();
        var langs = (Arg("-languages") ?? "").Split(',').Where(s => s.Length > 0).ToHashSet();
        int limit = int.TryParse(Arg("-limit"), out int n) ? n : int.MaxValue;
        bool force = args.Contains("-force");
        bool jobOnly = args.Contains("-jobOnly");
        string ingest = Arg("-ingest");
        var log = new StringBuilder();
        int code = 0;
        try
        {
            if (ingest != null)
            {
                log.AppendLine(Ingest(ingest));
                File.WriteAllText(Path.Combine(ProjectRoot, "Logs", "VoiceProduction.txt"), log.ToString());
                EditorApplication.Exit(0);
                return;
            }
            if (args.Contains("-removeRetired")) log.AppendLine(RemoveRetiredContent());
            log.AppendLine(VoiceDefinitionDefaults.EnsureAll());
            var manifest = LoadManifest();
            var lines = Inventory(manifest).Where(l => (voices.Count == 0 || voices.Contains(l.Voice)) && (langs.Count == 0 || langs.Contains(l.Language))
                                                       && (force ? l.State != LineState.NoEngine : IsPending(l))).Take(limit).ToList();
            log.AppendLine($"{lines.Count} line(s) to generate");
            if (jobOnly)
            {
                // Run the generator outside Unity (voicegen.py --job J --results R), then -ingest J.
                string job = lines.Count > 0 ? WriteJob(lines) : "";
                log.AppendLine("JOB " + job);
                log.AppendLine("RESULTS " + (job.Length > 0 ? ResultsPathFor(job) : ""));
                File.WriteAllText(Path.Combine(ProjectRoot, "Logs", "VoiceProduction.txt"), log.ToString());
                EditorApplication.Exit(0);
                return;
            }
            if (lines.Count > 0)
            {
                var run = Start(manifest, lines);
                var t0 = DateTime.Now;
                while (!run.Exited) { run.Pump(); System.Threading.Thread.Sleep(100); }
                run.Process?.WaitForExit();
                run.Pump();
                log.AppendLine(string.Join("\n", run.Log));
                if (run.Fatal != null) { log.AppendLine("FATAL " + run.Fatal); code = 1; }
                log.AppendLine($"generation {(DateTime.Now - t0).TotalSeconds:0}s");
                log.AppendLine(Import(manifest, run.Touched));
            }
            var after = Inventory(LoadManifest()).Where(l => (voices.Count == 0 || voices.Contains(l.Voice)) && (langs.Count == 0 || langs.Contains(l.Language))).ToList();
            log.AppendLine($"state: {string.Join(", ", after.GroupBy(l => l.State).Select(g => $"{g.Key}={g.Count()}"))}");
        }
        catch (Exception e) { log.AppendLine(e.ToString()); code = 2; }
        File.WriteAllText(Path.Combine(ProjectRoot, "Logs", "VoiceProduction.txt"), log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
