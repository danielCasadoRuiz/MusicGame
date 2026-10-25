#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The initial fictional voice of every character (created only when its VoiceDefinition asset is missing —
/// edit the assets afterwards; this never overwrites them). Each persona follows the character's in-game
/// personality (Composer.{id}.Bio); none describes or imitates a real person's recorded voice.
/// </summary>
public static class VoiceDefinitionDefaults
{
    // id, persona (voice-design instruct), reference text (in character, ~10 s). Combat vocalisations are recorded CC0
    // clips (GameAudioSetup), never generated.
    private static readonly (string id, string persona, string reference)[] VOICES =
    {
        ("bach", "Male, late fifties, deep warm bass-baritone, calm authority of a church organist and teacher, measured unhurried pace, very precise articulation, dry understated wit.",
         "Every voice has its place, and every place has its voice. Listen carefully: four lines, one purpose. Now, shall we begin the lesson?"),
        ("beethoven", "Male, forties, gravelly intense baritone, gruff and impatient, sudden explosive emphasis followed by a low growl, stubborn and fiery, strong breath.",
         "Quiet! Quiet, I said! You call that music? Listen. It starts softly, very softly... and then it hits you right in the face!"),
        ("brahms", "Male, around fifty, low gentle bass-baritone, reserved and thoughtful, slightly melancholic, warm and unhurried, soft-spoken.",
         "I have waited a long time for this. Patience, my friend. A good symphony takes twenty years. A good fight, perhaps a little less."),
        ("chopin", "Male, thirties, soft light refined tenor, elegant and delicate, a little fragile and poetic, intimate, expressive, slightly breathy.",
         "Shh... not so loud. Music is a whisper, not a shout. Let the melody breathe, bend the time a little, and then... it dances away."),
        ("handel", "Male, fifties, big booming jovial bass, theatrical and grand, proud showman, rich resonant chest voice, loves an audience.",
         "Ladies and gentlemen, the curtain rises! Bigger, louder, more trumpets! When I enter a hall, the whole of London stands up!"),
        ("haydn", "Male, sixties, kind cheerful elderly voice, good-humoured and mischievous, gentle teasing, lively but warm, chuckles easily.",
         "Ah, good evening! Sit down, relax, enjoy the music. Nice and calm, nice and calm... and then, surprise! Did I wake you up?"),
        ("monteverdi", "Male, fifties, dramatic expressive baritone, theatrical and passionate, declamatory like an opera narrator, sweeping emotion, rolling cadence.",
         "Behold the stage! Every word must sing, every silence must weep. Tonight, my friend, we invent the drama all over again!"),
        ("mozart", "Male, twenties, bright quick playful tenor, cheeky and energetic, fast-talking, giggly, sparkling confidence, a hint of mischief.",
         "Ha! Too slow, too slow! I wrote three symphonies while you were tying your shoes. Come on, keep up, it's only getting faster!"),
        ("pachelbel", "Male, forties, calm patient very even voice, soft-spoken, methodical and serene, slow steady rhythm, quietly confident.",
         "The same eight notes. Again. And again. You may tire of them. I never will. Patience is a canon, and the canon always returns."),
        ("tchaikovsky", "Male, forties, sensitive emotional baritone, passionate and romantic, swelling feeling, slight nervous intensity, grand gestures.",
         "Can you feel it? The swans, the snow, the cannons! Everything at once! Oh, music is too beautiful to be gentle!"),
        ("vivaldi", "Male, thirties, fast animated fiery tenor, energetic and flamboyant, quick bursts like a storm, enthusiastic and restless.",
         "Spring, summer, autumn, winter! Faster, faster! The violins are flying, the storm is coming, and I have five hundred concertos left!"),
        ("wagner", "Male, fifties, commanding imperious bass-baritone, slow heavy and grandiose, self-important, every word weighty, dramatic pauses.",
         "Silence. You stand before an epic. Four evenings, fifteen hours, gods and fire. Do not expect this to be short."),
        ("music_being", "Ageless androgynous otherworldly voice, calm resonant and mysterious, slow and deliberate, as if heard from everywhere at once, never human-sounding.",
         "I am every note you have ever heard, and every silence between them. You have played well. Now listen to what music truly is."),
        // Tempo Fury announcers: fictional, game-owned, ALWAYS English (shared content, not per artist / language).
        ("announcer_m", "Male, thirties, basketball arena hype man and PA announcer, deep powerful baritone with thick chest resonance, loud and commanding, street swagger, punchy rhythmic flow, elongated vowels, electrifying crowd energy, never nasal, never soft.",
         "Ladies and gentlemen, get on your feet and make some noise! It's showtime! Three! Two! One! Let's goooo! Are you ready? I can't hear you! Louder! Here we go, baby!"),
        ("announcer_f", "Female, late thirties, powerful arena MC, deep resonant low voice, a little gravel, shouting with full power from the chest, bold rhythmic delivery, hype and swagger, commanding like a coach, never high, never sweet.",
         "Ladies and gentlemen, get on your feet and make some noise! It's showtime! Three! Two! One! Let's goooo! Are you ready? I can't hear you! Louder! Here we go, baby!"),
    };

    // The countdown is said in ONE breath and cut into words (flow + no reference leaking into a lone word); Tempo! /
    // Fury! / Perfect! are reserved words (English in every language). Catalan numbers: the engine has no Catalan, so
    // the Spanish model says the same sounds. Seeds are picked afterwards by listening tests (announcer_phrases.py).
    private const string EnTempo = "Three! Two! One! Tempo!", EnFury = "Three! Two! One! Fyury!", CaCount = "¡Tres, dos, u!";
    private static System.Collections.Generic.List<VoiceDefinition.ExtraLine> AnnouncerLines() => new()
    {
        new() { key = "Announcer.Three", text = "Three!", phrase = EnTempo, segment = 0 },
        new() { key = "Announcer.Two", text = "Two!", phrase = EnTempo, segment = 1 },
        new() { key = "Announcer.One", text = "One!", phrase = EnTempo, segment = 2 },
        new() { key = "Announcer.Tempo", text = "Tempo!", phrase = EnTempo, segment = 3 },
        new() { key = "Announcer.Fury", text = "Fury!", phrase = EnFury, segment = 3 },
        new() { key = "Announcer.Perfect", text = "Perfect!" },
        new() { key = "Announcer.Three", text = "Tres!", language = "ca", engineLanguage = "es", phrase = CaCount, segment = 0 },
        new() { key = "Announcer.Two", text = "Dos!", language = "ca", engineLanguage = "es", phrase = CaCount, segment = 1 },
        new() { key = "Announcer.One", text = "U!", language = "ca", engineLanguage = "es", phrase = CaCount, segment = 2 },
    };

    [MenuItem("Tools/MusicGame/Audio/Create Missing Voice Definitions")]
    public static void Run() => Debug.Log(EnsureAll());

    public static string EnsureAll()
    {
        Directory.CreateDirectory(VoiceProduction.DefinitionsFolder);
        var sb = new StringBuilder();
        int created = 0;
        for (int i = 0; i < VOICES.Length; i++)
        {
            var v = VOICES[i];
            string path = $"{VoiceProduction.DefinitionsFolder}/Voice_{v.id}.asset";
            if (AssetDatabase.LoadAssetAtPath<VoiceDefinition>(path) != null) continue;
            var d = ScriptableObject.CreateInstance<VoiceDefinition>();
            d.voiceId = v.id; d.description = v.persona; d.referenceText = v.reference; d.referenceLanguage = "en";
            d.seed = 1000 + i * 37;
            if (v.id.StartsWith("announcer_")) d.extraLines = AnnouncerLines();
            AssetDatabase.CreateAsset(d, path);
            created++;
        }
        AssetDatabase.SaveAssets();
        sb.Append($"voice definitions: {created} created, {VOICES.Length - created} kept");
        return sb.ToString();
    }
}
#endif
