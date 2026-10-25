#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Builds / refreshes the game audio: the mixer (Master → Music / SFX / Voice, exposed MusicVolume / SfxVolume /
/// VoiceVolume), one SfxCue per game sound event with its clips (CUES below — the single place that says which
/// file plays where), the clips' import settings (short sfx: decompress on load + preload) and
/// Resources/Audio/GameSfxLibrary. The player's vocalisations are added by the Voice Production tool.
/// Menu: Tools > MusicGame > Audio > Build Game Sfx. Batch: -executeMethod GameAudioSetup.RunFromCommandLine
/// </summary>
public static class GameAudioSetup
{
    public const string Root = "Assets/_Project/Audio";
    public const string CueFolder = Root + "/Sfx/Cues";
    public const string MixerPath = Root + "/GameAudioMixer.mixer";
    public const string LibraryPath = "Assets/_Project/Resources/Audio/GameSfxLibrary.asset";
    private const string K = Root + "/Sfx/Kenney";

    // name, field, clips, volume, maxInstances, cooldown, pitch spread, credits
    private static readonly (string name, string field, string[] clips, float vol, int max, float cd, float pitch, string credits)[] CUES =
    {
        ("HitLight",      "hitLight",      Files(K + "/Impact", "impactPunch_medium_000", "impactPunch_medium_001", "impactPunch_medium_002", "impactPunch_medium_003", "impactPunch_medium_004"), 0.55f, 3, 0.04f, 0.06f, Kenney("Impact Sounds")),
        ("HitHeavy",      "hitHeavy",      Files(K + "/Impact", "impactPunch_heavy_000", "impactPunch_heavy_001", "impactPunch_heavy_002", "impactPunch_heavy_003", "impactPunch_heavy_004"), 0.7f, 2, 0.06f, 0.05f, Kenney("Impact Sounds")),
        ("HitBlocked",    "hitBlocked",    Files(K + "/Impact", "impactPlate_light_000", "impactPlate_light_001", "impactPlate_light_002", "impactPlate_light_003"), 0.45f, 2, 0.05f, 0.05f, Kenney("Impact Sounds")),
        ("SpecialLaunch", "specialLaunch", Files(K + "/Digital", "phaserUp4", "phaserUp6", "powerUp7"), 0.6f, 1, 0.3f, 0.02f, Kenney("Digital Audio")),
        ("KnockOut",      "knockOut",      Files(K + "/SciFi", "lowFrequency_explosion_000", "lowFrequency_explosion_001"), 0.6f, 1, 0.5f, 0.03f, Kenney("Sci-fi Sounds")),
        ("ChaserWake",    "chaserWake",    Files(K + "/SciFi", "forceField_000", "forceField_001", "forceField_002"), 0.5f, 1, 0.5f, 0.03f, Kenney("Sci-fi Sounds")),
        ("ChaserActivate","chaserActivate",Files(K + "/SciFi", "laserLarge_000", "laserLarge_001", "laserLarge_002"), 0.5f, 1, 0.5f, 0.03f, Kenney("Sci-fi Sounds")),
        ("ChaserContact", "chaserContact", Files(K + "/SciFi", "impactMetal_000", "impactMetal_001", "impactMetal_002"), 0.65f, 1, 0.6f, 0.03f, Kenney("Sci-fi Sounds")),
        ("Trapped",       "trapped",       Files(K + "/Interface", "error_004", "error_006", "error_008"), 0.6f, 1, 0.6f, 0.02f, Kenney("Interface Sounds")),
    };


    private static string Kenney(string pack) => $"Kenney \"{pack}\" (kenney.nl) — CC0 1.0. Licence: {K}/License.txt";
    private static string[] Files(string folder, params string[] names) => names.Select(n => $"{folder}/{n}").ToArray();

    [MenuItem("Tools/MusicGame/Audio/Build Game Sfx")]
    public static void Run() => Debug.Log(Build());

    public static void RunFromCommandLine()
    {
        int code = 0;
        try { File.WriteAllText("Logs/GameAudioSetup.txt", Build()); }
        catch (Exception e) { File.WriteAllText("Logs/GameAudioSetup.txt", e.ToString()); Debug.LogException(e); code = 1; }
        EditorApplication.Exit(code);
    }

    public static string Build()
    {
        var sb = new StringBuilder();
        Directory.CreateDirectory(CueFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
        AssetDatabase.Refresh();

        var lib = AssetDatabase.LoadAssetAtPath<GameSfxLibrary>(LibraryPath);
        if (lib == null) { lib = ScriptableObject.CreateInstance<GameSfxLibrary>(); AssetDatabase.CreateAsset(lib, LibraryPath); }

        // Mixer
        var mixer = EnsureMixer(sb);
        if (mixer != null)
        {
            lib.mixer = mixer;
            lib.musicGroup = mixer.FindMatchingGroups("Music").FirstOrDefault();
            lib.sfxGroup = mixer.FindMatchingGroups("SFX").FirstOrDefault();
            lib.voiceGroup = mixer.FindMatchingGroups("Voice").FirstOrDefault();
            sb.AppendLine($"mixer: groups music={lib.musicGroup != null} sfx={lib.sfxGroup != null} voice={lib.voiceGroup != null}");
        }

        // Cues
        foreach (var c in CUES)
        {
            string[] paths = c.clips;
            var clips = new List<AudioClip>();
            foreach (var p in paths)
            {
                string file = FindAudio(p);
                if (file == null) { sb.AppendLine($"  MISSING clip {p}"); continue; }
                ConfigureSfxImport(file);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
                if (clip != null) clips.Add(clip);
            }
            var cue = Cue(c.name);
            cue.clips = clips.ToArray();
            cue.channel = GameAudioChannel.Sfx;
            cue.volume = c.vol; cue.maxInstances = c.max; cue.cooldown = c.cd;
            cue.pitch = new Vector2(1f - c.pitch, 1f + c.pitch);
            cue.credits = c.credits;
            EditorUtility.SetDirty(cue);
            typeof(GameSfxLibrary).GetField(c.field).SetValue(lib, cue);
            sb.AppendLine($"{c.name,-15} {clips.Count} clip(s)");
        }
        BuildEfforts(lib, sb);
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ── Character vocalisations: recorded CC0 takes (HaelDB, Audio/Sfx/Vocal/HaelDB/LICENSE.txt) ──────────────────
    // One vocalist per character group (a character always sounds like the same person), takes chosen by measured
    // length: hurt 0.12–0.45 s of voice, effort 0.4–0.8 s (no long scream), defeat 0.8–1.4 s; none contains words.
    private const string V = Root + "/Sfx/Vocal/HaelDB/haeldb_";
    private static readonly Dictionary<string, (string[] hurt, string[] effort, string[] defeat)> VOCALISTS = new()
    {
        ["deep"]   = (new[] { "3grunt4", "3grunt2", "3yell3", "3grunt1", "3grunt5", "3yell4" }, new[] { "3yell8", "3yell12", "3yell7" }, new[] { "3yell15", "3yell14", "3yell10", "3yell2" }),
        ["mid"]    = (new[] { "yell13", "yell6", "yell7" }, new[] { "yell3", "yell8", "yell9" }, new[] { "yell4", "yell5", "yell2", "yell10" }),
        ["bright"] = (new[] { "1yell15", "1yell9", "1yell7", "1yell6", "1yell10", "1yell14" }, new[] { "1yell13", "1yell8", "1yell4" }, new[] { "1yell3", "1yell5", "1yell11", "1yell12" }),
        ["player"] = (new[] { "2yell1", "2yell10", "2yell7" }, new[] { "2yell9", "2yell11" }, new[] { "2yell3", "2yell5", "2yell8" }),
    };
    // Composer → vocalist group + its own pitch (so two composers of a group never sound identical).
    private static readonly (string id, string group, float pitch)[] COMPOSERS =
    {
        ("wagner", "deep", 0.92f), ("brahms", "deep", 0.95f), ("bach", "deep", 0.97f), ("handel", "deep", 0.99f), ("pachelbel", "deep", 1.02f),
        ("beethoven", "mid", 0.97f), ("monteverdi", "mid", 1.0f), ("tchaikovsky", "mid", 1.02f), ("haydn", "mid", 1.05f),
        ("chopin", "bright", 1.03f), ("vivaldi", "bright", 1.05f), ("mozart", "bright", 1.08f), ("music_being", "bright", 1.12f),
    };

    private static void BuildEfforts(GameSfxLibrary lib, StringBuilder sb)
    {
        string folder = CueFolder + "/Efforts";
        Directory.CreateDirectory(folder);
        VoiceEffortSet Set(string id, string group, float pitch)
        {
            var g = VOCALISTS[group];
            return new VoiceEffortSet
            {
                hurt    = EffortCue(folder, $"{id}_hurt", g.hurt, 0.55f, 0.55f, pitch),
                special = EffortCue(folder, $"{id}_effort", g.effort, 0.6f, 1.2f, pitch),
                defeat  = EffortCue(folder, $"{id}_defeat", g.defeat, 0.75f, 2f, pitch),
            };
        }
        lib.playerEfforts = Set("player", "player", 1f);
        lib.composerEfforts = COMPOSERS.Select(c => new CharacterEfforts { id = c.id, efforts = Set(c.id, c.group, c.pitch) }).ToArray();
        lib.defaultComposerEfforts = Set("composer_default", "mid", 1f);
        sb.AppendLine($"vocalisations: player + {COMPOSERS.Length} composers + default (recorded CC0, {VOCALISTS.Sum(v => v.Value.hurt.Length + v.Value.effort.Length + v.Value.defeat.Length)} takes)");
    }

    private static SfxCue EffortCue(string folder, string name, string[] takes, float volume, float cooldown, float pitch)
    {
        var clips = new List<AudioClip>();
        foreach (var t in takes)
        {
            string file = FindAudio(V + t);
            if (file == null) continue;
            ConfigureSfxImport(file);
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
            if (c != null) clips.Add(c);
        }
        string path = $"{folder}/{name}.asset";
        var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
        if (cue == null) { cue = ScriptableObject.CreateInstance<SfxCue>(); AssetDatabase.CreateAsset(cue, path); }
        cue.clips = clips.ToArray();
        cue.channel = GameAudioChannel.Voice;
        cue.volume = volume; cue.volumeJitter = 0.06f; cue.maxInstances = 1; cue.cooldown = cooldown;
        cue.pitch = new Vector2(pitch - 0.015f, pitch + 0.015f);
        cue.credits = "HaelDB, \"Male Grunt/Yelling sounds\" (OpenGameArt) — CC0 1.0. Audio/Sfx/Vocal/HaelDB/LICENSE.txt";
        EditorUtility.SetDirty(cue);
        return cue;
    }

    public static SfxCue Cue(string name)
    {
        string path = $"{CueFolder}/{name}.asset";
        var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
        if (cue == null) { cue = ScriptableObject.CreateInstance<SfxCue>(); AssetDatabase.CreateAsset(cue, path); }
        return cue;
    }

    private static string FindAudio(string pathWithoutExt)
    {
        foreach (var ext in new[] { ".ogg", ".wav" })
            if (File.Exists(pathWithoutExt + ext)) return pathWithoutExt + ext;
        return null;
    }

    /// <summary>Short sfx: decompressed at load, data preloaded with the library (never a late first play).</summary>
    public static void ConfigureSfxImport(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as AudioImporter;
        if (imp == null) return;
        var s = imp.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.7f;
        s.preloadAudioData = true;
        imp.defaultSampleSettings = s;
        imp.loadInBackground = false;
        imp.forceToMono = true;
        imp.SaveAndReimport();
    }

    // ── Mixer (no public creation API: the editor's own AudioMixerController, by reflection) ──────────

    private static AudioMixer EnsureMixer(StringBuilder sb)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (existing != null && existing.FindMatchingGroups("Voice").Length > 0) return existing;
        try
        {
            var asm = typeof(UnityEditor.Editor).Assembly;
            var ctrlType = asm.GetType("UnityEditor.Audio.AudioMixerController");
            var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
            var create = ctrlType.GetMethod("CreateMixerControllerAtPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var ctrl = existing != null ? (object)existing : create.Invoke(null, new object[] { MixerPath });
            var master = ctrlType.GetProperty("masterGroup").GetValue(ctrl);
            var createGroup = ctrlType.GetMethod("CreateNewGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var addChild = ctrlType.GetMethod("AddChildToParent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            var addExposed = ctrlType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "AddExposedParameter");
            var guidForVolume = groupType.GetMethod("GetGUIDForVolume", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var exposedProp = ctrlType.GetProperty("exposedParameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var (group, param) in new[] { ("Music", "MusicVolume"), ("SFX", "SfxVolume"), ("Voice", "VoiceVolume") })
            {
                var g = createGroup.Invoke(ctrl, new object[] { group, false });
                addChild.Invoke(ctrl, new[] { g, master });
                var guid = guidForVolume.Invoke(g, null);
                var path = Activator.CreateInstance(pathType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { g, guid }, null);
                addExposed.Invoke(ctrl, new[] { path });
                // Rename the new exposed parameter (it is the one bound to this GUID).
                var arr = (Array)exposedProp.GetValue(ctrl);
                for (int i = 0; i < arr.Length; i++)
                {
                    var p = arr.GetValue(i);
                    var gf = p.GetType().GetField("guid");
                    if (gf.GetValue(p).Equals(guid)) { p.GetType().GetField("name").SetValue(p, param); arr.SetValue(p, i); }
                }
                exposedProp.SetValue(ctrl, arr);
            }
            EditorUtility.SetDirty((UnityEngine.Object)ctrl);
            AssetDatabase.SaveAssets();
            sb.AppendLine("mixer created: " + MixerPath);
            return AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        }
        catch (Exception e)
        {
            sb.AppendLine("mixer NOT created (" + e.GetType().Name + ": " + e.Message + ") — GameAudio falls back to per-source channel volume.");
            return existing;
        }
    }
}
#endif
