using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Every game sound event and the mixer channels (Resources/Audio/GameSfxLibrary — loaded and its clips
/// preloaded by GameAudio at boot, so a countdown or a fight never waits for, or plays late, a sound).
/// Built / refreshed by Tools > MusicGame > Audio > Build Game Sfx.
/// </summary>
public class GameSfxLibrary : ScriptableObject
{
    public const string ResourcePath = "Audio/GameSfxLibrary";

    [Header("Mixer (independent volume per channel: exposed MusicVolume / SfxVolume / VoiceVolume)")]
    public AudioMixer mixer;
    public AudioMixerGroup musicGroup, sfxGroup, voiceGroup;

    [Header("Announcer (Tempo Fury) — two fictional voices, one picked per app session; always English")]
    public AnnouncerSet[] announcers;

    [Header("Fight")]
    public SfxCue hitLight;
    public SfxCue hitHeavy;
    public SfxCue hitBlocked;
    public SfxCue specialLaunch;
    public SfxCue knockOut;

    [Header("Character vocalisations (recorded CC0 libraries — see Audio/Sfx/CREDITS.md), shared by every language")]
    public VoiceEffortSet playerEfforts;
    [Tooltip("Per composer id; a composer without its own entry uses defaultComposerEfforts.")]
    public CharacterEfforts[] composerEfforts;
    public VoiceEffortSet defaultComposerEfforts;

    [Header("Runner hazards")]
    public SfxCue chaserWake;
    public SfxCue chaserActivate;
    public SfxCue chaserContact;
    public SfxCue trapped;

    public System.Collections.Generic.IEnumerable<SfxCue> AllCues()
    {
        foreach (var c in new[] { hitLight, hitHeavy, hitBlocked, specialLaunch, knockOut, chaserWake, chaserActivate, chaserContact, trapped })
            if (c != null) yield return c;
        if (playerEfforts != null) foreach (var c in playerEfforts.All()) yield return c;
        if (defaultComposerEfforts != null) foreach (var c in defaultComposerEfforts.All()) yield return c;
        if (composerEfforts != null) foreach (var e in composerEfforts) if (e?.efforts != null) foreach (var c in e.efforts.All()) yield return c;
    }

    /// <summary>Every announcer clip (preloaded with the library).</summary>
    public System.Collections.Generic.IEnumerable<AudioClip> AllAnnouncerClips()
    {
        if (announcers == null) yield break;
        foreach (var a in announcers) if (a != null) foreach (var c in a.All()) if (c != null) yield return c;
    }

    public VoiceEffortSet EffortsFor(string composerId)
    {
        if (composerEfforts != null) foreach (var e in composerEfforts) if (e != null && e.id == composerId && e.efforts != null) return e.efforts;
        return defaultComposerEfforts;
    }
}

[System.Serializable]
public class CharacterEfforts
{
    public string id;
    public VoiceEffortSet efforts;
}

/// <summary>One announcer voice: the countdown words and "Perfect!" (generated once, local, shared game content).</summary>
[System.Serializable]
public class AnnouncerSet
{
    public string voiceId;
    [Tooltip("Part of the random pick at app start. Off = kept in the project but never chosen (e.g. a rejected voice).")]
    public bool inRotation = true;
    public AudioClip three, two, one, tempo, fury, perfect;
    [Tooltip("The countdown numbers in other game languages (Tempo! / Fury! / Perfect! are reserved words: always English).")]
    public System.Collections.Generic.List<AnnouncerNumbers> numbers = new();
    public bool IsComplete => three && two && one && tempo && fury && perfect;

    /// <summary>The countdown number n (3, 2, 1) in the game language when this voice has it, otherwise in English.</summary>
    public AudioClip Number(int n, string language)
    {
        var loc = numbers?.Find(x => x != null && x.language == language && x.IsComplete);
        return n == 3 ? (loc?.three ?? three) : n == 2 ? (loc?.two ?? two) : n == 1 ? (loc?.one ?? one) : null;
    }
    public System.Collections.Generic.IEnumerable<AudioClip> All()
    {
        yield return three; yield return two; yield return one; yield return tempo; yield return fury; yield return perfect;
        if (numbers != null) foreach (var n in numbers) if (n != null) { yield return n.three; yield return n.two; yield return n.one; }
    }
}

/// <summary>An announcer's countdown numbers in one game language.</summary>
[System.Serializable]
public class AnnouncerNumbers
{
    public string language;
    public AudioClip three, two, one;
    public bool IsComplete => three && two && one;
}

/// <summary>A character's short vocalisations without words (hurt / special effort / final defeat).</summary>
[System.Serializable]
public class VoiceEffortSet
{
    public SfxCue hurt;
    public SfxCue special;
    public SfxCue defeat;

    public System.Collections.Generic.IEnumerable<SfxCue> All()
    {
        if (hurt != null) yield return hurt;
        if (special != null) yield return special;
        if (defeat != null) yield return defeat;
    }
}
