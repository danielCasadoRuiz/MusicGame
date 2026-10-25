using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The generated voice of ONE composer in ONE language (or its language-independent vocalisations, language "fx"):
/// one Addressables bundle each (group Voice_{composer}_{language}, address VoicePack/{composer}/{language}), so a
/// player downloads only the voice language they use and the composers they meet. Every line keeps its own
/// identity (the dialogue key) and the hash of the exact text it was generated from: a line whose text has
/// changed since (a text-only content update) is NOT played — the subtitle shows alone until its audio is
/// regenerated. Written by the Voice Production tool, never by hand.
/// </summary>
public class ComposerVoicePack : ScriptableObject
{
    public const string EffortsLanguage = "fx";

    public string composerId;
    public string language;
    [Tooltip("Engine / model / voice definition the clips were generated with (informative; the tool tracks the details).")]
    public string engine;
    public string voiceDefinitionHash;
    public List<Line> lines = new();
    [Tooltip("fx pack only: the composer's vocalisations.")]
    public VoiceEffortSet efforts;

    [System.Serializable]
    public struct Line
    {
        public string key;
        public AudioClip clip;
        public string textHash;
        public float duration;
    }

    public static string Address(string composerId, string language) => $"VoicePack/{composerId}/{language}";
    public static string GroupName(string composerId, string language) => $"Voice_{composerId}_{language}";

    public bool TryGet(string key, out Line line)
    {
        foreach (var l in lines) if (l.key == key) { line = l; return true; }
        line = default;
        return false;
    }
}

/// <summary>Stable hash of the text a line was generated from (whitespace-normalised, case kept). The same
/// function is used by the production tool and at runtime, so "audio of an older text" is always detected.</summary>
public static class VoiceTextHash
{
    public static string Of(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text.Trim())
        {
            if (char.IsWhiteSpace(c)) { if (!space) sb.Append(' '); space = true; }
            else { sb.Append(c); space = false; }
        }
        ulong h = 1469598103934665603UL;                   // FNV-1a 64 over UTF-16 code units
        foreach (char c in sb.ToString()) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("x16");
    }
}
