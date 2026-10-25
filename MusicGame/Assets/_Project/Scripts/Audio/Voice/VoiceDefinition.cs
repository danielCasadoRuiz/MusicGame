using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PRODUCTION data (never referenced by the game): the fixed identity of one fictional character voice — a
/// composer (voiceId = its OpponentDefinition.id) or the player ("player"). The engine designs ONE reference
/// recording from description + referenceText (+ seed) and every line, in every language, is spoken by cloning
/// that same reference, so the voice stays recognisable across lines and languages. Changing any identity field
/// changes IdentityHash → the reference and every line of that voice are regenerated (the Voice Production tool
/// shows it as pending). These voices are invented to fit each character's personality; they are not, and must
/// not be, imitations of any real person's recordings.
/// </summary>
[CreateAssetMenu(fileName = "VoiceDefinition", menuName = "MusicGame/Audio/Voice Definition")]
public class VoiceDefinition : ScriptableObject
{
    public string voiceId;
    [Tooltip("Generation engine (Tools/VoiceGen/voicegen.py ENGINES).")]
    public string engine = "qwen3-tts";
    [Tooltip("Natural-language persona for the voice-design model: age, timbre, attitude, pace. Invented, never a real person.")]
    [TextArea(3, 6)] public string description;
    [Tooltip("What the reference recording says (in referenceLanguage) — in character, ~10 s, varied intonation.")]
    [TextArea(3, 6)] public string referenceText;
    public string referenceLanguage = "en";
    public int seed = 1;

    [Tooltip("Optional per-language engine override (e.g. a future Catalan engine). Empty = the default engine.")]
    public List<LanguageOverride> languageOverrides = new();

    [Tooltip("Fixed lines of this voice that are not in a dialogue table (the announcers' Three / Two / One / Tempo! / Fury! / " +
             "Perfect!), spoken in referenceLanguage unless the line names its own language (the countdown numbers per game " +
             "language; Tempo! / Fury! / Perfect! are reserved words, always English). Combat vocalisations are NOT generated: " +
             "they come from recorded CC0 libraries.")]
    public List<ExtraLine> extraLines = new();

    [System.Serializable]
    public struct ExtraLine
    {
        public string key;
        public string text;
        [Tooltip("0 = seed derived from the line id; any other value fixes the take (e.g. a take that sounded right).")]
        public int seed;
        [Tooltip("Game language this line is for (e.g. ca). Empty = referenceLanguage.")]
        public string language;
        [Tooltip("Language the engine speaks it in, when the engine lacks the game language (Catalan numbers through the " +
                 "Spanish model: same sounds). Empty = language.")]
        public string engineLanguage;
        [Tooltip("Said inside a whole phrase in one breath (e.g. \"Three! Two! One! Tempo!\": real flow, and a lone word never " +
                 "drifts into the reference text); this line is the phrase's word number `segment` (0-based), cut at its pauses. " +
                 "Empty = the text alone.")]
        public string phrase;
        public int segment;
        [Tooltip("Dry cut: the word is cut this many seconds after it starts, with a quick fade (a drawn-out vowel such as " +
                 "Catalan \"U!\" becomes as short and dry as the other numbers). 0 = as spoken.")]
        public float maxSeconds;
    }

    [System.Serializable]
    public struct LanguageOverride
    {
        public string language;
        public string engine;
    }

    public string EngineFor(string language)
    {
        foreach (var o in languageOverrides) if (o.language == language && !string.IsNullOrEmpty(o.engine)) return o.engine;
        return engine;
    }

    /// <summary>Everything that defines the voice's identity (not the efforts texts — those are per-item text).</summary>
    public string IdentityHash => VoiceTextHash.Of($"{engine}\n{description}\n{referenceText}\n{referenceLanguage}\n{seed}");
}
