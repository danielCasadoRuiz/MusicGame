#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

/// <summary>
/// Tools > MusicGame > Campaign > Setup Final Rival Dialogues — the "Ésser de la música" lines:
/// 10 per category (Intro / Defeat = the being loses / Victory = the being wins) for EACH of its three
/// versions, as version-specific keys Composer.music_being.Dialogue.{Category}.L{v}.{index:000}, in the
/// collection ComposerDialogues_music_being (ca + en), then filed into the composer-dialogue
/// Addressables group like every other composer table. Only missing entries are added (hand edits kept).
/// Batch: -executeMethod BossDialogueSetup.SetupFromCommandLine
/// </summary>
public static class BossDialogueSetup
{
    public const string BossId = "music_being";

    // (version, category, ca, en) — 3 × 3 × 10.
    private static readonly (int v, ComposerDialogueCategory cat, string ca, string en)[] Lines =
    {
        // ── Version 1 · Intro ──
        (1, ComposerDialogueCategory.Intro, "Així que tu ets qui ha vençut els meus alumnes? Jo era aquí abans que la primera nota.", "So you're the one who beat my students? I was here before the first note."),
        (1, ComposerDialogueCategory.Intro, "Bach, Mozart, Wagner... tots em demanaven permís. Tu ni has trucat.", "Bach, Mozart, Wagner... they all asked my permission. You didn't even knock."),
        (1, ComposerDialogueCategory.Intro, "Aquesta cançó teva... la vaig descartar fa deu mil anys. Massa previsible.", "That song of yours... I discarded it ten thousand years ago. Too predictable."),
        (1, ComposerDialogueCategory.Intro, "Soc el do, el re i el mi alhora. Tu, com a molt, un soroll de fons.", "I am do, re and mi at once. You are, at best, background noise."),
        (1, ComposerDialogueCategory.Intro, "Abans que existís el ritme, jo ja picava de peus. Comencem?", "Before rhythm existed, I was already tapping my feet. Shall we?"),
        (1, ComposerDialogueCategory.Intro, "M'han invocat amb un loop de quatre compassos? Quina falta de respecte.", "Summoned with a four-bar loop? How disrespectful."),
        (1, ComposerDialogueCategory.Intro, "Tranquil, només faré servir una octava. De moment.", "Relax, I'll only use one octave. For now."),
        (1, ComposerDialogueCategory.Intro, "El silenci també és música. Ara en sentiràs molt, de silenci.", "Silence is music too. You're about to hear plenty of it."),
        (1, ComposerDialogueCategory.Intro, "Has afinat abans de venir? Ho sento des d'aquí.", "Did you tune up before coming? I can hear it from here."),
        (1, ComposerDialogueCategory.Intro, "Benvingut a l'escenari original. L'acústica és perfecta: la vaig fer jo.", "Welcome to the original stage. The acoustics are perfect: I made them."),
        // ── Version 1 · Defeat (the being loses) ──
        (1, ComposerDialogueCategory.Defeat, "Hm. Un acord inesperat. M'ha agradat... una mica.", "Hm. An unexpected chord. I liked it... a little."),
        (1, ComposerDialogueCategory.Defeat, "Has tocat a temps. Tres vegades seguides. Ho apunto.", "You played in time. Three times in a row. I'll make a note."),
        (1, ComposerDialogueCategory.Defeat, "Aquesta síncope no la tenia prevista. I jo ho preveig tot.", "I didn't see that syncopation coming. And I see everything coming."),
        (1, ComposerDialogueCategory.Defeat, "D'acord, d'acord: tens més ritme del que sembla.", "Fine, fine: you've got more rhythm than you look."),
        (1, ComposerDialogueCategory.Defeat, "Primer compàs per a tu. La partitura encara és llarga.", "First bar to you. The score is still long."),
        (1, ComposerDialogueCategory.Defeat, "M'has desafinat. Ningú m'havia desafinat mai. Interessant.", "You put me out of tune. Nobody's ever done that. Interesting."),
        (1, ComposerDialogueCategory.Defeat, "Bé. Has superat l'escala de do major. Ara ve la complicada.", "Good. You've mastered C major. Now comes the tricky one."),
        (1, ComposerDialogueCategory.Defeat, "Reconec la melodia. Era meva, però l'has dit amb gràcia.", "I recognise that melody. It was mine, but you said it with style."),
        (1, ComposerDialogueCategory.Defeat, "Una cadència perfecta... contra mi. Quina barra.", "A perfect cadence... against me. The nerve."),
        (1, ComposerDialogueCategory.Defeat, "Aplaudiré. Només amb una mà, que l'altra porta el compàs.", "I'll applaud. With one hand only: the other keeps time."),
        // ── Version 1 · Victory (the being wins) ──
        (1, ComposerDialogueCategory.Victory, "Tornes a l'assaig. Jo ja era a l'estrena.", "Back to rehearsal with you. I was already at the premiere."),
        (1, ComposerDialogueCategory.Victory, "T'has quedat en un silenci de negra. Molt llarg.", "You froze on a crotchet rest. A very long one."),
        (1, ComposerDialogueCategory.Victory, "Una altra vegada? És clar. La música sempre torna al tema.", "Again? Of course. Music always returns to the theme."),
        (1, ComposerDialogueCategory.Victory, "Ho sento, aquest final l'he escrit jo.", "Sorry, I wrote this ending."),
        (1, ComposerDialogueCategory.Victory, "Tens potencial. Com un triangle a l'última fila.", "You have potential. Like a triangle in the back row."),
        (1, ComposerDialogueCategory.Victory, "Has perdut el compàs. Me'l quedo: tots els compassos són meus.", "You lost the beat. I'll keep it: every beat is mine."),
        (1, ComposerDialogueCategory.Victory, "Ni un do de pit. Ni un do de res.", "Not a high C. Not a C of any kind."),
        (1, ComposerDialogueCategory.Victory, "Bravo! Ah no, era l'eco del meu propi bravo.", "Bravo! Oh no, that was the echo of my own bravo."),
        (1, ComposerDialogueCategory.Victory, "Estudia solfeig i torna. Jo t'espero: tinc tota l'eternitat.", "Study your music theory and come back. I'll wait: I have all eternity."),
        (1, ComposerDialogueCategory.Victory, "El públic demana un bis. De mi, no de tu.", "The audience wants an encore. Of me, not you."),

        // ── Version 2 · Intro ──
        (2, ComposerDialogueCategory.Intro, "Has tornat. Ara tocaré amb tota l'orquestra.", "You're back. Now I play with the full orchestra."),
        (2, ComposerDialogueCategory.Intro, "Abans només afinava. Ara comença la simfonia.", "Before, I was only tuning. Now the symphony begins."),
        (2, ComposerDialogueCategory.Intro, "Sento el teu cor: va a 140 bpm. Massa ràpid per a un adagio.", "I can hear your heart: 140 bpm. Too fast for an adagio."),
        (2, ComposerDialogueCategory.Intro, "Cada nota que toques, jo ja l'he tocada abans. I millor.", "Every note you play, I've played before. And better."),
        (2, ComposerDialogueCategory.Intro, "Avui modularé a un to que encara no té nom.", "Today I'll modulate to a key that doesn't have a name yet."),
        (2, ComposerDialogueCategory.Intro, "El metrònom és meu. I ara mateix s'està accelerant.", "The metronome is mine. And right now it's speeding up."),
        (2, ComposerDialogueCategory.Intro, "Porto vuitanta violins a dins. Tots desafinen contra tu.", "I've got eighty violins inside me. All of them out of tune against you."),
        (2, ComposerDialogueCategory.Intro, "Els grans mestres eren assajos. Tu ets la prova de so.", "The great masters were rehearsals. You're the soundcheck."),
        (2, ComposerDialogueCategory.Intro, "Prepara't per a un crescendo que no s'acaba mai.", "Get ready for a crescendo that never ends."),
        (2, ComposerDialogueCategory.Intro, "Aquest cop res de silencis. Tot serà fortissimo.", "No rests this time. Everything fortissimo."),
        // ── Version 2 · Defeat ──
        (2, ComposerDialogueCategory.Defeat, "Has sobreviscut al meu tutti. Pocs ho poden explicar.", "You survived my tutti. Few live to tell."),
        (2, ComposerDialogueCategory.Defeat, "Aquest contrapunt l'havia de fer jo. Me l'has robat.", "That counterpoint was supposed to be mine. You stole it."),
        (2, ComposerDialogueCategory.Defeat, "Bé. Et mereixes un acord de setena. No més.", "Fine. You deserve a seventh chord. No more."),
        (2, ComposerDialogueCategory.Defeat, "M'has fet perdre l'entrada. A mi, que soc totes les entrades.", "You made me miss my cue. Me. I AM every cue."),
        (2, ComposerDialogueCategory.Defeat, "Accepto la derrota com un calderó: breu i elegant.", "I accept defeat like a fermata: brief and elegant."),
        (2, ComposerDialogueCategory.Defeat, "La teva melodia tenia alguna cosa... no sé què. Ja ho descobriré.", "Your melody had something... I don't know what. I'll find out."),
        (2, ComposerDialogueCategory.Defeat, "Segon moviment per a tu. El tercer és el meu preferit.", "Second movement to you. The third is my favourite."),
        (2, ComposerDialogueCategory.Defeat, "Has trobat la nota blava. I jo que l'havia amagat tan bé.", "You found the blue note. And I'd hidden it so well."),
        (2, ComposerDialogueCategory.Defeat, "D'acord: et deixo tocar a la meva banda. De moment, la pandereta.", "Alright: you can join my band. Tambourine, for now."),
        (2, ComposerDialogueCategory.Defeat, "Has fet ressonar l'harmonia. Em fa pessigolles.", "You made the harmony resonate. It tickles."),
        // ── Version 2 · Victory ──
        (2, ComposerDialogueCategory.Victory, "La simfonia s'acaba quan jo ho dic. I ho dic ara.", "The symphony ends when I say so. And I say so now."),
        (2, ComposerDialogueCategory.Victory, "Massa notes i massa poques idees.", "Too many notes, too few ideas."),
        (2, ComposerDialogueCategory.Victory, "Has ensopegat en un compàs de 7/8. Un clàssic.", "You tripped on a 7/8 bar. A classic."),
        (2, ComposerDialogueCategory.Victory, "M'ha agradat la teva derrota. Tenia bon tempo.", "I enjoyed your defeat. It had a good tempo."),
        (2, ComposerDialogueCategory.Victory, "Tot l'auditori en silenci. Per respecte. A mi.", "The whole hall is silent. Out of respect. For me."),
        (2, ComposerDialogueCategory.Victory, "Has tocat amb sentiment. El sentiment equivocat.", "You played with feeling. The wrong feeling."),
        (2, ComposerDialogueCategory.Victory, "Et regalo una partitura en blanc. Comença per aquí.", "Here's a blank score, on the house. Start there."),
        (2, ComposerDialogueCategory.Victory, "No pateixis: la història et recordarà com una nota a peu de pàgina.", "Don't worry: history will remember you as a footnote."),
        (2, ComposerDialogueCategory.Victory, "El teu solo ha durat menys que un glissando.", "Your solo lasted less than a glissando."),
        (2, ComposerDialogueCategory.Victory, "Tornaràs. Tothom acaba tornant a la tònica.", "You'll be back. Everyone returns to the tonic in the end."),

        // ── Version 3 · Intro ──
        (3, ComposerDialogueCategory.Intro, "Ja no soc una orquestra. Soc el so de l'univers.", "I am no longer an orchestra. I am the sound of the universe."),
        (3, ComposerDialogueCategory.Intro, "Les estrelles vibren a la meva freqüència. Tu també vibraràs.", "The stars vibrate at my frequency. You'll vibrate too."),
        (3, ComposerDialogueCategory.Intro, "Abans del Big Bang hi va haver un compte enrere. El vaig marcar jo.", "Before the Big Bang there was a count-in. I called it."),
        (3, ComposerDialogueCategory.Intro, "Aquesta és la meva forma final: totes les cançons alhora.", "This is my final form: every song at once."),
        (3, ComposerDialogueCategory.Intro, "Cada vegada que algú taral·lejava, era jo. Ara taral·lejaré contra tu.", "Every time anyone hummed, it was me. Now I hum against you."),
        (3, ComposerDialogueCategory.Intro, "Has derrotat els mestres i les meves orquestres. Només queda el silenci absolut.", "You beat the masters and my orchestras. Only absolute silence remains."),
        (3, ComposerDialogueCategory.Intro, "Ho sento, aquí no hi ha botó de pausa. No n'hi ha hagut mai.", "Sorry, there's no pause button here. There never was."),
        (3, ComposerDialogueCategory.Intro, "Si guanyes, la música serà teva. Si perds, seràs un acord de fons.", "Win, and music is yours. Lose, and you become a background chord."),
        (3, ComposerDialogueCategory.Intro, "Tota la història de la música cap en aquest compàs. Endavant.", "The whole history of music fits in this bar. Go ahead."),
        (3, ComposerDialogueCategory.Intro, "Escolta bé: aquest és l'últim la que sentiràs per afinar.", "Listen carefully: this is the last A you'll ever tune to."),
        // ── Version 3 · Defeat ──
        (3, ComposerDialogueCategory.Defeat, "Increïble. M'has superat... i encara sonava bé.", "Incredible. You beat me... and it still sounded good."),
        (3, ComposerDialogueCategory.Defeat, "D'acord. A partir d'ara, quan algú canti a la dutxa, pensaré en tu.", "Alright. From now on, whenever someone sings in the shower, I'll think of you."),
        (3, ComposerDialogueCategory.Defeat, "M'has tocat de memòria. Ni jo em sabia tan bé.", "You played me by heart. Even I didn't know myself that well."),
        (3, ComposerDialogueCategory.Defeat, "Accepto. La propera simfonia te la dedico. A contracor.", "I accept. My next symphony is dedicated to you. Reluctantly."),
        (3, ComposerDialogueCategory.Defeat, "Has trobat l'acord perdut. Jo el buscava des de sempre.", "You found the lost chord. I'd been looking for it forever."),
        (3, ComposerDialogueCategory.Defeat, "Molt bé, mestre. Però que consti que el ritme el vaig inventar jo.", "Very well, maestro. But for the record, I invented rhythm."),
        (3, ComposerDialogueCategory.Defeat, "Has fet callar la mateixa música. Qui sap què sonarà ara.", "You silenced music itself. Who knows what will play now."),
        (3, ComposerDialogueCategory.Defeat, "Una ovació de les galàxies. Fan molt de soroll, les galàxies.", "A standing ovation from the galaxies. They're very loud, galaxies."),
        (3, ComposerDialogueCategory.Defeat, "Has guanyat l'última nota. Gaudeix-la, que dura poc.", "You won the last note. Enjoy it, it doesn't last long."),
        (3, ComposerDialogueCategory.Defeat, "Abaixa el teló. I no toquis res: el piano és meu.", "Bring down the curtain. And don't touch anything: the piano is mine."),
        // ── Version 3 · Victory ──
        (3, ComposerDialogueCategory.Victory, "L'univers continua afinat. Gràcies per participar.", "The universe remains in tune. Thanks for taking part."),
        (3, ComposerDialogueCategory.Victory, "Has estat molt a prop. Mig to, com a molt.", "You came close. A semitone, at most."),
        (3, ComposerDialogueCategory.Victory, "La música sempre guanya. Ho porta al nom.", "Music always wins. It's in the name."),
        (3, ComposerDialogueCategory.Victory, "Torna a començar des del primer compàs. T'espero al final.", "Start again from the first bar. I'll wait for you at the end."),
        (3, ComposerDialogueCategory.Victory, "Bon intent. El posaré en un recopilatori de rareses.", "Nice try. I'll put it on a compilation of oddities."),
        (3, ComposerDialogueCategory.Victory, "Has perdut contra un concepte. El concepte està encantat.", "You lost to a concept. The concept is delighted."),
        (3, ComposerDialogueCategory.Victory, "Les estrelles riuen en do major.", "The stars are laughing in C major."),
        (3, ComposerDialogueCategory.Victory, "Has estat la meva nota preferida d'avui. L'única, de fet.", "You were my favourite note today. The only one, actually."),
        (3, ComposerDialogueCategory.Victory, "Tranquil: fins i tot Beethoven va necessitar nou simfonies.", "Don't worry: even Beethoven needed nine symphonies."),
        (3, ComposerDialogueCategory.Victory, "Final, aplaudiments, teló. I la protagonista, com sempre: jo.", "Finale, applause, curtain. And the star, as always: me."),
    };

    [MenuItem("Tools/MusicGame/Campaign/Setup Final Rival Dialogues")]
    public static void SetupMenu() => Debug.Log(Setup(out _));

    public static void SetupFromCommandLine()
    {
        string report = Setup(out bool ok);
        Debug.Log(report);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/BossDialogueSetup.txt", report, Encoding.UTF8);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static string Setup(out bool ok)
    {
        var sb = new StringBuilder("[BossDialogueSetup]\n");
        ok = true;
        var locales = LocalizationEditorSettings.GetLocales().ToList();
        string name = ComposerDialogueKeys.TableName(BossId);
        var collection = LocalizationEditorSettings.GetStringTableCollection(name);
        if (collection == null)
        {
            collection = LocalizationEditorSettings.CreateStringTableCollection(name, ComposerDialogueContentSetup.CollectionFolder, locales);
            sb.AppendLine($"  new   {name}");
        }
        foreach (var locale in locales)
            if (collection.GetTable(locale.Identifier) == null) collection.AddNewTable(locale.Identifier);

        int added = 0;
        var counters = new System.Collections.Generic.Dictionary<(int, ComposerDialogueCategory), int>();
        foreach (var (v, cat, ca, en) in Lines)
        {
            counters.TryGetValue((v, cat), out int n);
            counters[(v, cat)] = ++n;
            string key = ComposerDialogueKeys.Key(BossId, cat, v, n);
            foreach (var table in collection.StringTables)
            {
                var entry = table.GetEntry(key);
                if (entry != null && !string.IsNullOrEmpty(entry.Value)) continue;
                table.AddEntry(key, table.LocaleIdentifier.Code == "ca" ? ca : en);
                EditorUtility.SetDirty(table);
                added++;
            }
        }
        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();
        bool shape = counters.Count == 9 && counters.Values.All(c => c == 10);
        if (!shape) ok = false;
        sb.AppendLine($"  {(shape ? "ok  " : "FAIL")}  {Lines.Length} lines = 3 versions × 3 categories × 10; {added} texts added");

        // Validation: every key parses as a version-specific line of this rival, both languages filled.
        int bad = 0;
        foreach (var shared in collection.SharedData.Entries)
        {
            if (!ComposerDialogueKeys.TryParse(shared.Key, out var id, out _, out _, out int version) || id != BossId || version < 1 || version > 3) bad++;
            foreach (var t in collection.StringTables) if (string.IsNullOrWhiteSpace(t.GetEntry(shared.Id)?.Value)) bad++;
        }
        if (bad > 0) ok = false;
        sb.AppendLine(bad == 0 ? "  ok    every key parses (version 1–3) and has ca + en text" : $"  FAIL  {bad} invalid key(s)/empty text(s)");

        sb.Append(ComposerDialogueContentSetup.ConfigureAddressables());
        sb.AppendLine(ok ? "  RESULT PASS" : "  RESULT FAIL");
        return sb.ToString();
    }
}
#endif
