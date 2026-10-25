using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MusicGame.Audio.Analysis;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// The rule of P4 (see BonusLayoutContract): the layout of a combination is generated the FIRST time it is
/// played, saved complete, and loaded — never regenerated — on every later play. GameplayManager calls it
/// in the readiness barrier (PreparingConsumers), so a load or a generation always completes before the
/// countdown. Main thread; ~1 ms to load, a generation costs what GameplayTimeline.Generate costs.
/// </summary>
public sealed class BonusLayoutProvider : IBonusLayoutProvider
{
    /// <summary>The app's provider (file store under persistentDataPath/BonusLayouts).</summary>
    public static readonly BonusLayoutProvider Default = new(new FileBonusLayoutStore(null));

    /// <summary>Diagnostics: outcome of the last GetOrCreate in this process.</summary>
    public static BonusLayoutOutcome? LastOutcome { get; private set; }
    public static string LastKey { get; private set; }

    private readonly IBonusLayoutStore _store;
    public BonusLayoutProvider(IBonusLayoutStore store) { _store = store; }

    /// <summary>The key of the run being prepared: profile revision, the campaign position + difficulty
    /// (RunnerDifficultyOverride, or free play), generator version and the layout-relevant config.</summary>
    public static BonusLayoutKey KeyFor(SongProfile profile, MusicRunnerGameplayConfig config, float difficulty,
                                        string campaignPosition, int lapIfItChangesDifficulty)
        => BonusLayoutKey.Create(ProfileRevision.Of(profile), campaignPosition, difficulty,
                                 RunnerPickupLayout.GeneratorVersion, LayoutConfigHash(config), lapIfItChangesDifficulty);

    /// <summary>Current run: config.collectibles.CurrentDifficulty + RunnerDifficultyOverride position / lap.</summary>
    public BonusLayoutResult GetOrCreateForCurrentRun(SongProfile profile, MusicRunnerGameplayConfig config, MusicPath path)
        => GetOrCreate(profile, config, path, config.collectibles.CurrentDifficulty, RunnerDifficultyOverride.Position, RunnerDifficultyOverride.KeyLap);

    public BonusLayoutResult GetOrCreate(SongProfile profile, MusicRunnerGameplayConfig config, MusicPath path, float difficulty,
                                         string campaignPosition, int lapIfItChangesDifficulty)
    {
        var sw = Stopwatch.StartNew();
        var key = KeyFor(profile, config, difficulty, campaignPosition, lapIfItChangesDifficulty);
        LastKey = key.ToString();
        string problem = null;
        if (_store != null && _store.TryLoad(key, out var stored, out problem))
        {
            LastOutcome = BonusLayoutOutcome.Loaded;
            return new BonusLayoutResult(GameplayTimeline.FromStored(profile, config, stored.events, stored.stats), stored,
                                         BonusLayoutOutcome.Loaded, sw.Elapsed.TotalMilliseconds, null);
        }

        // First play of this combination (or an unreadable file): generate with the key's own seed.
        var timeline = GameplayTimeline.Generate(profile, config, path, key.Seed, key.Difficulty);
        var record = new BonusLayoutRecord
        {
            key = key, seed = key.Seed, generatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            stats = timeline.Stats, events = timeline.Events,
        };
        var outcome = problem != null ? BonusLayoutOutcome.Rebuilt : BonusLayoutOutcome.Generated;
        if (problem != null) Debug.LogWarning($"[BonusLayout] Stored layout unusable ({problem}) — rebuilt from the same seed {key.Seed}.");
        if (_store != null)
        {
            try
            {
                _store.Save(record);
                int pruned = _store.PruneStale(key.profile.songId, key.profile);
                if (pruned > 0) Debug.Log($"[BonusLayout] Removed {pruned} layout(s) of an older profile revision of '{key.profile.songId}'.");
            }
            catch (Exception e) { Debug.LogWarning($"[BonusLayout] Could not save the layout ({e.Message}) — it will be regenerated identically next time."); }
        }
        LastOutcome = outcome;
        return new BonusLayoutResult(timeline, record, outcome, sw.Elapsed.TotalMilliseconds, problem);
    }

    /// <summary>Hash of everything in the gameplay config that decides WHICH moments are selected and WHERE
    /// they are placed: core (movement, jump, warmup, play range), collectibles (selection, heights —
    /// diagnostics excluded; the dev layout overrides only while they are ACTIVE) and level generation (path
    /// shape). Object references and presentation-only fields (PresentationOnly) are ignored.</summary>
    public static ulong LayoutConfigHash(MusicRunnerGameplayConfig config)
    {
        var h = StableHash.Create();
        h.Add("MusicGame.BonusLayoutConfig.v1");
        h.Add(Canonical(config.core != null ? JsonUtility.ToJson(config.core) : ""));
        h.Add(Canonical(config.collectibles != null ? JsonUtility.ToJson(config.collectibles) : ""));
        h.Add(Canonical(config.levelGeneration != null ? JsonUtility.ToJson(config.levelGeneration) : ""));
        var dev = config.collectibles != null ? config.collectibles.DevOverrides : null;
        if (dev != null) h.Add("dev:" + JsonUtility.ToJson(dev)); // inactive overrides never fork a normal key
        return h.Value;
    }

    private static readonly Regex InstanceIds = new("\\{\"instanceID\":-?\\d+\\}", RegexOptions.Compiled);
    private static readonly Regex Diagnostics = new("\"diagnostics\":\\{[^}]*\\},?", RegexOptions.Compiled);

    private static readonly Regex DevOverridesJson = new("\"devLayoutOverrides\":\\{[^}]*\\},?", RegexOptions.Compiled);

    // Presentation / timing-feel fields the generator never reads (ground playhead line, the player's trail
    // behind the musical front, pulse lead, reveal distances): tuning them must not fork or regenerate layouts.
    private static readonly Regex PresentationOnly = new(
        @"""(playhead\w*|playerTrailSeconds|pulseLeadTime|bonusVisualActivationDistance\w*)"":(\{[^}]*\}|[^,}]*),?", RegexOptions.Compiled);

    private static string Canonical(string json) =>
        PresentationOnly.Replace(DevOverridesJson.Replace(Diagnostics.Replace(InstanceIds.Replace(json, "null"), ""), ""), "");
}

/// <summary>
/// One binary file per layout: persistentDataPath/BonusLayouts/&lt;songId&gt;_&lt;revision tag&gt;_&lt;key hash&gt;.mgbl.
/// "MGBL" v1, the full key string (verified on load — a hash collision or a foreign file is never used), seed,
/// generation time, selection counts, every placed event, CRC-32 over the content. Written to a temp file
/// then moved. Separate from the save: deleting it only means the layout is rebuilt (identically).
/// </summary>
public sealed class FileBonusLayoutStore : IBonusLayoutStore
{
    private const uint Magic = 0x4C42474D; // "MGBL"
    private const int  Version = 1;
    public const string Extension = ".mgbl";

    private readonly string _root;
    public FileBonusLayoutStore(string root) { _root = root; }
    public string Root => _root ?? Path.Combine(Application.persistentDataPath, "BonusLayouts");

    public string PathFor(in BonusLayoutKey key)
    {
        var h = StableHash.Create();
        h.Add(key.ToString());
        return Path.Combine(Root, $"{Sanitize(key.profile.songId)}_{key.profile.Tag}_{h.Value:x16}{Extension}");
    }

    public bool TryLoad(in BonusLayoutKey key, out BonusLayoutRecord record, out string problem)
    {
        record = null; problem = null;
        string path = PathFor(key);
        if (!File.Exists(path)) return false;
        try
        {
            record = Read(File.ReadAllBytes(path));
            if (record.key.ToString() != key.ToString()) { problem = "the file holds another key"; record = null; return false; }
            return true;
        }
        catch (Exception e) { problem = e.Message; record = null; return false; }
    }

    public void Save(BonusLayoutRecord record)
    {
        string path = PathFor(record.key);
        Directory.CreateDirectory(Root);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, Write(record));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public int PruneStale(string songId, ProfileRevision current)
    {
        if (!Directory.Exists(Root)) return 0;
        string prefix = Sanitize(songId) + "_", keep = prefix + current.Tag + "_";
        int removed = 0;
        foreach (var f in Directory.GetFiles(Root, prefix + "*" + Extension))
        {
            string name = Path.GetFileName(f);
            if (name.StartsWith(keep, StringComparison.Ordinal)) continue;
            // Only "<songId>_<8 hex>_<16 hex>.mgbl" of THIS song (another id may share the prefix: song_1 vs song_10).
            string rest = name.Substring(prefix.Length);
            if (rest.Length != 8 + 1 + 16 + Extension.Length) continue;
            try { File.Delete(f); removed++; } catch { /* in use: next time */ }
        }
        return removed;
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "song";
        var chars = s.ToCharArray();
        var bad = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++) if (Array.IndexOf(bad, chars[i]) >= 0 || chars[i] == '_') chars[i] = '-';
        return new string(chars);
    }

    // ── Binary format ────────────────────────────────────────────────────────────

    public static byte[] Write(BonusLayoutRecord r)
    {
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.UTF8, true))
        {
            w.Write(r.key.ToString());
            WriteKey(w, r.key);
            w.Write(r.seed);
            w.Write(r.generatedAtUnixSeconds);
            var s = r.stats;
            foreach (int v in new[] { s.detections, s.belowStrength, s.belowEnergy, s.mergedIntoImpact, s.spacingRejected, s.quotaRejected, s.selected, s.synthetic,
                                      s.kick, s.snare, s.hihat, s.generic, s.multiFamily }) w.Write(v);
            w.Write(s.difficulty); w.Write(s.minInterval); w.Write(s.strengthFloor);
            w.Write(r.events.Length);
            foreach (var e in r.events)
            {
                w.Write(e.eventTime); w.Write(e.eventDistance); w.Write((int)e.eventType); w.Write((int)e.ringType);
                w.Write(e.lateralOffset); w.Write(e.verticalOffset); w.Write(e.floorClearance); w.Write(e.isOffTrack);
                w.Write(e.scoreMultiplier); w.Write(e.strength); w.Write(e.confidence);
                w.Write((byte)e.origin); w.Write(e.onsetIndex);
                w.Write(e.sourceFeature ?? ""); w.Write(e.contributors ?? "");
            }
        }
        byte[] content = body.ToArray();
        using var file = new MemoryStream();
        using (var w = new BinaryWriter(file))
        {
            w.Write(Magic); w.Write(Version); w.Write(content.Length); w.Write(Crc32.Compute(content, 0, content.Length));
            w.Write(content);
        }
        return file.ToArray();
    }

    public static BonusLayoutRecord Read(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a bonus layout file");
        int version = r.ReadInt32();
        if (version != Version) throw new InvalidDataException($"layout format {version} (reads {Version})");
        int length = r.ReadInt32();
        uint crc = r.ReadUInt32();
        int offset = (int)r.BaseStream.Position;
        if (length < 0 || offset + length > bytes.Length) throw new InvalidDataException("truncated layout file");
        if (Crc32.Compute(bytes, offset, length) != crc) throw new InvalidDataException("layout CRC mismatch (corrupt)");

        var rec = new BonusLayoutRecord();
        string keyText = r.ReadString();
        rec.key = ReadKey(r);
        if (rec.key.ToString() != keyText) throw new InvalidDataException("inconsistent key");
        rec.seed = r.ReadInt32();
        rec.generatedAtUnixSeconds = r.ReadInt64();
        var s = new GameplayTimeline.SelectionStats
        {
            detections = r.ReadInt32(), belowStrength = r.ReadInt32(), belowEnergy = r.ReadInt32(), mergedIntoImpact = r.ReadInt32(),
            spacingRejected = r.ReadInt32(), quotaRejected = r.ReadInt32(), selected = r.ReadInt32(), synthetic = r.ReadInt32(),
            kick = r.ReadInt32(), snare = r.ReadInt32(), hihat = r.ReadInt32(), generic = r.ReadInt32(), multiFamily = r.ReadInt32(),
        };
        s.difficulty = r.ReadSingle(); s.minInterval = r.ReadSingle(); s.strengthFloor = r.ReadSingle();
        rec.stats = s;
        int n = r.ReadInt32();
        if (n < 0 || n > 100000) throw new InvalidDataException("implausible event count " + n);
        rec.events = new TimelineEvent[n];
        for (int i = 0; i < n; i++)
        {
            rec.events[i] = new TimelineEvent
            {
                eventTime = r.ReadSingle(), eventDistance = r.ReadSingle(), eventType = (EventType)r.ReadInt32(), ringType = (RingType)r.ReadInt32(),
                lateralOffset = r.ReadSingle(), verticalOffset = r.ReadSingle(), floorClearance = r.ReadSingle(), isOffTrack = r.ReadBoolean(),
                scoreMultiplier = r.ReadSingle(), strength = r.ReadSingle(), confidence = r.ReadSingle(),
                origin = (BonusOrigin)r.ReadByte(), onsetIndex = r.ReadInt32(),
                sourceFeature = r.ReadString(), contributors = r.ReadString(),
            };
        }
        return rec;
    }

    private static void WriteKey(BinaryWriter w, in BonusLayoutKey k)
    {
        w.Write(k.profile.songId ?? ""); w.Write(k.profile.masterIdentity); w.Write(k.profile.playbackRevision); w.Write(k.profile.profileCrc);
        w.Write(k.profile.pipelineVersion); w.Write(k.profile.paramsHash);
        w.Write(k.campaignPosition ?? ""); w.Write(k.difficultyMilli); w.Write(k.generatorVersion); w.Write(k.geometryHash); w.Write(k.lapIfItChangesDifficulty);
    }

    private static BonusLayoutKey ReadKey(BinaryReader r) => new()
    {
        profile = new ProfileRevision
        {
            songId = r.ReadString(), masterIdentity = r.ReadUInt64(), playbackRevision = r.ReadUInt64(), profileCrc = r.ReadUInt32(),
            pipelineVersion = r.ReadInt32(), paramsHash = r.ReadUInt64(),
        },
        campaignPosition = r.ReadString(), difficultyMilli = r.ReadInt32(), generatorVersion = r.ReadInt32(),
        geometryHash = r.ReadUInt64(), lapIfItChangesDifficulty = r.ReadInt32(),
    };
}
