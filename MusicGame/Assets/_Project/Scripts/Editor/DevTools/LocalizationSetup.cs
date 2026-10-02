#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

/// <summary>
/// One-shot Editor tool: creates (or updates) the "UIText" String Table Collection — every
/// user-facing string in the game (AnalyzingScreenController's tips, GameplayHUD's live/end-screen
/// text, PauseController's menu) reads from this ONE table via Loc.Get, so this is the single
/// place new text gets added. Populated with English + Catalan entries.
///
/// Also creates the English + Catalan Locale assets if the project doesn't have them yet — same
/// CreateInstance&lt;Locale&gt;/AssetDatabase.CreateAsset/LocalizationEditorSettings.AddLocale
/// sequence Unity's own Locale Generator window uses internally, just for these two specific
/// languages instead of an arbitrary picked set. Any OTHER locale already in the project (or
/// added later via the Localization window) is left alone and simply gets English text.
///
/// Safe to re-run: existing entries for the same key are overwritten with the text below (so hand
/// edits made directly in the table asset afterward are NOT preserved by re-running this — same
/// "run once, then hand-tune the asset" contract as UIPrefabBuilder). Existing Locales are never
/// duplicated or touched.
/// </summary>
public static class LocalizationSetup
{
    private const string TableName    = "UIText";
    private const string AssetFolder  = "Assets/_Project/Localization";
    private const string LocaleFolder = "Assets/_Project/Localization/Locales";

    // key → (English, Catalan). Any OTHER project locale (if added later) falls back to the
    // English text below rather than being left blank.
    private static readonly (string key, string en, string ca)[] Entries =
    {
        ("Analyzing.Title", "Analyzing song...",                 "Analitzant la cançó..."),
        ("Analyzing.Tip01", "Extracting musical style...",        "Extraient l'estil musical..."),
        ("Analyzing.Tip02", "Measuring the flow...",              "Mesurant el flow..."),
        ("Analyzing.Tip03", "Counting beats per minute...",       "Comptant els beats per minut..."),
        ("Analyzing.Tip04", "Untangling the rhythm...",           "Desxifrant el ritme..."),
        ("Analyzing.Tip05", "Listening for the kick drum...",     "Escoltant el bombo..."),
        ("Analyzing.Tip06", "Mapping the hi-hats...",             "Mapejant els hi-hats..."),
        ("Analyzing.Tip07", "Detecting the drops...",             "Detectant els drops..."),
        ("Analyzing.Tip08", "Reading the song's mood...",         "Llegint l'ànim de la cançó..."),
        ("Analyzing.Tip09", "Calibrating the jump physics...",    "Calibrant la física dels salts..."),
        ("Analyzing.Tip10", "Tuning into the frequencies...",     "Sintonitzant les freqüències..."),
        ("Analyzing.Tip11", "Teaching the track to run...",       "Ensenyant el circuit a córrer..."),
        ("Analyzing.Tip12", "Syncing the beat to the world...",   "Sincronitzant el ritme amb el món..."),

        // Analyzing.CacheHit01-03 are currently unused (AnalyzingScreenController's progress-phase
        // redesign replaced the old fixed cache-hit caption sequence with the more informative
        // Analyzing.StyleDetected flash below) — left here rather than deleted in case a future
        // pass wants them back for a dedicated "found in cache" moment.
        ("Analyzing.CacheHit01", "Song found in cache...", "Cançó trobada a la memòria cau..."),
        ("Analyzing.CacheHit02", "Finalizing...",          "Finalitzant..."),
        ("Analyzing.CacheHit03", "Changing theme...",      "Canviant l'estil..."),
        ("Analyzing.StyleDetected", "Style detected: {0}!", "Estil detectat: {0}!"),

        // ── Pause menu ──────────────────────────────────────────────────────────
        ("Pause.PauseButton", "Pause",           "Pausa"),
        ("Pause.Title",       "PAUSED",          "PAUSA"),
        ("Pause.Resume",      "RESUME",          "REPRÈN"),
        ("Pause.RestartSong", "RESTART SONG",    "REINICIA LA CANÇÓ"),
        ("Pause.MainMenu",    "MAIN MENU",       "MENÚ PRINCIPAL"),
        ("Pause.ThirdPerson", "Third Person",    "Tercera Persona"),
        ("Pause.FirstPerson", "First Person",    "Primera Persona"),

        // ── Live HUD — top-bar column headers, reused by EndScreen's performance rows ──────────
        ("HUD.Kick",     "KICK",   "KICK"),
        ("HUD.Snare",    "SNARE",  "SNARE"),
        ("HUD.HiHat",    "HI-HAT", "HI-HAT"),
        ("HUD.Beat",     "BEAT",   "BEAT"),
        ("HUD.Onset",    "ONSET",  "ONSET"),
        ("HUD.Peak",     "PEAK",   "PEAK"),
        ("HUD.Impact",   "IMPACT", "IMPACT"),
        ("HUD.Score",    "SCORE",  "PUNTS"),
        ("HUD.Total",    "TOTAL",  "TOTAL"),
        ("HUD.TagStyle", "STYLE",  "ESTIL"),
        ("HUD.TagVibe",  "VIBE",   "VIBE"),
        ("HUD.TagOther", "OTHER",  "ALTRES"),

        // ── End screen ───────────────────────────────────────────────────────────
        ("EndScreen.Title",          "─── RESULTS ───",   "─── RESULTATS ───"),
        ("EndScreen.Restart",        "RESTART",           "REINICIA"),
        ("EndScreen.Continue",       "CONTINUE",          "CONTINUA"),
        ("EndScreen.RatingPoor",     "POOR",              "POBRE"),
        ("EndScreen.RatingWeak",     "WEAK",              "FLUIX"),
        ("EndScreen.RatingDecent",   "DECENT",            "DECENT"),
        ("EndScreen.RatingGreat",    "GREAT",             "MOLT BO"),
        ("EndScreen.RatingInsane",   "INSANE",            "BRUTAL"),
        ("EndScreen.ScoreSummary",   "score {0} / {1}  (raw {2}%)",       "puntuació {0} / {1}  (en brut {2}%)"),
        ("EndScreen.Falls",          "Falls: {0}",                        "Caigudes: {0}"),
        ("EndScreen.NoFallBonus",    "NO FALL BONUS +{0}%",               "BONUS SENSE CAURE +{0}%"),
        ("EndScreen.SessionSingular","Session: {0} run · {1}% avg",       "Sessió: {0} partida · {1}% mitjana"),
        ("EndScreen.SessionPlural",  "Session: {0} runs · {1}% avg",      "Sessió: {0} partides · {1}% mitjana"),

        // ── Intro screen ─────────────────────────────────────────────────────────
        ("Intro.Title", "MUSIC RUNNER", "MUSIC RUNNER"),

        // ── Main menu ────────────────────────────────────────────────────────────
        ("MainMenu.Title",    "MUSIC RUNNER", "MUSIC RUNNER"),
        ("MainMenu.Play",     "PLAY",         "JUGA"),
        ("MainMenu.Settings", "SETTINGS",     "AJUSTOS"),
        ("MainMenu.Quit",     "QUIT",         "SORTIR"),

        // ── Settings panel (opened from Main Menu) ──────────────────────────────
        ("Settings.Title",  "SETTINGS", "AJUSTOS"),
        ("Settings.Volume", "Volume",   "Volum"),
        ("Settings.Close",  "CLOSE",    "TANCA"),

        // ── Song selection ───────────────────────────────────────────────────────
        ("SongSelection.Title",               "CHOOSE YOUR SONG",                 "TRIA LA TEVA CANÇÓ"),
        ("SongSelection.PlayYourSong",         "▶ PLAY YOUR SONG",                 "▶ PUNXA LA TEVA CANÇÓ"),
        ("SongSelection.Spotify",              "Spotify",                          "Spotify"),
        ("SongSelection.YouTubeMusic",         "YouTube Music",                    "YouTube Music"),
        ("SongSelection.AmazonMusic",          "Amazon Music",                     "Amazon Music"),
        ("SongSelection.ComingSoon",           "Coming Soon",                      "Properament"),
        ("SongSelection.Back",                 "BACK",                             "ENRERE"),
        ("SongSelection.Play",                 "PLAY",                             "JUGA"),
        ("SongSelection.Loading",              "Loading song...",                  "Carregant la cançó..."),
        ("SongSelection.LoadFailed",           "Couldn't load that song — try again.", "No s'ha pogut carregar la cançó — torna-ho a provar."),
        ("SongSelection.LocalFileCancelled",   "File selection cancelled.",        "Selecció de fitxer cancel·lada."),
        ("SongSelection.LocalFileSelected",    "Selected: {0}",                    "Seleccionat: {0}"),
        ("SongSelection.LocalFileDefaultName", "Local File",                       "Fitxer local"),

        // ── Countdown ────────────────────────────────────────────────────────────
        ("Countdown.Go",     "GO!",     "JA!"),
        ("Countdown.Finish", "FINISH!", "FINAL!"),

        // ── Mobile controls ──────────────────────────────────────────────────────
        ("Mobile.Jump",  "JUMP",  "SALTA"),
        ("Mobile.Punch", "PUNCH", "COP"),
        ("Mobile.Kick",  "KICK",  "PUNTADA"),

        // ── Fight HUD ────────────────────────────────────────────────────────────
        ("Fight.PlayerName",   "PLAYER",     "JUGADOR"),
        ("Fight.RivalUnknown", "RIVAL",      "RIVAL"),
        ("Fight.Paused",       "PAUSED",     "PAUSA"),
        ("Fight.Pause",        "Pause",      "Pausa"),
        ("Fight.Resume",       "RESUME",     "REPRÈN"),
        ("Fight.MainMenu",     "MAIN MENU",  "MENÚ PRINCIPAL"),

        // ── Fight flow (Opponent Selection -> Versus -> Round Intro -> Countdown) ──
        ("OpponentSelection.Title", "CHOOSE YOUR OPPONENT", "TRIA EL TEU RIVAL"),
        ("Fight.Versus",            "VS",                   "VS"),
        ("Fight.RoundLabel",        "ROUND {0}",             "RONDA {0}"),
        ("Fight.Banner",            "FIGHT!",                "LLUITA!"),

        // ── Fight flow (Round End / Match Result) ──────────────────────────────────
        ("Fight.KO",          "KO!",           "KO!"),
        ("Fight.TimeUp",      "TIME UP!",      "TEMPS ESGOTAT!"),
        ("Fight.RoundWinner",    "{0} WINS THE ROUND", "{0} GUANYA LA RONDA"),
        ("Fight.RoundDraw",      "DRAW",          "EMPAT"),
        ("Fight.WinsOnPoints",   "{0} WINS ON POINTS", "{0} GUANYA ALS PUNTS"),
        ("Fight.PointDifference","POINT DIFFERENCE: {0}", "DIFERÈNCIA DE PUNTS: {0}"),
        ("Fight.YouWin",         "YOU WIN",       "HAS GUANYAT"),
        ("Fight.YouLose",        "YOU LOSE",      "HAS PERDUT"),

        // ── Match Result (post-Fight flow: Continue / Fight Again / Replay Song / Rewarded Ad) ────
        ("MatchResult.LevelUp",        "LEVEL {0} → LEVEL {1}",        "NIVELL {0} → NIVELL {1}"),
        ("MatchResult.FinalHealth",    "Health — You: {0}%  Rival: {1}%", "Vida — Tu: {0}%  Rival: {1}%"),
        ("MatchResult.Continue",       "CONTINUE",                          "CONTINUA"),
        ("MatchResult.FightAgain",     "FIGHT AGAIN",                       "TORNA A LLUITAR"),
        ("MatchResult.FightAgainCount","FIGHT AGAIN (x{0})",                "TORNA A LLUITAR (x{0})"),
        ("MatchResult.ReplaySong",     "REPLAY SONG",                       "REPETEIX LA CANÇÓ"),
        ("MatchResult.WatchAdTitle",   "GET ONE MORE LIFE?",                "VOLS UNA VIDA MÉS?"),
        ("MatchResult.WatchAd",        "WATCH AD",                          "MIRA UN ANUNCI"),
        ("MatchResult.Cancel",         "CANCEL",                            "CANCEL·LA"),

        // ── Next Song transition cartela (Continue -> Song Analysis hand-off) ─────────────────────
        ("NextSong.Header", "NEXT SONG",  "SEGÜENT CANÇÓ"),
        ("NextSong.Level",  "LEVEL {0}",  "NIVELL {0}"),

        // ── Progression / rival collection / previews / runner resources ─────────────────────────
        ("MainMenu.Continue", "CONTINUE", "CONTINUA"),
        ("MainMenu.Rivals", "COLLECTION", "COL·LECCIÓ"),
        ("MainMenu.Profile", "LEVEL {0}  ·  XP {1}/{2}  ·  LIVES {3}", "NIVELL {0}  ·  XP {1}/{2}  ·  VIDES {3}"),
        ("Rivals.Title", "COLLECTION", "COL·LECCIÓ"),
        ("Rivals.Back", "BACK", "ENRERE"),
        ("Rivals.Summary", "{0} / {1} rival versions defeated", "{0} / {1} versions de rivals derrotades"),
        ("Rivals.Level", "LV {0}", "NV {0}"),
        ("Rivals.Defeated", "DEFEATED", "DERROTAT"),
        ("Rivals.Undefeated", "UNDISCOVERED", "PER DESCOBRIR"),
        ("MatchResult.NewRival", "NEW RIVAL UNLOCKED", "NOU RIVAL DESBLOQUEJAT"),
        ("MatchResult.NewRivalLevel", "{0} — Level {1}", "{0} — Nivell {1}"),
        ("HUD.Resources", "LIFE {0}    SPECIAL {1}", "VIDA {0}    ESPECIAL {1}"),
        ("EndScreen.RunScore", "Score: {0}", "Puntuació: {0}"),
        ("EndScreen.LivesCollected", "Lives collected: +{0}", "Vides recollides: +{0}"),
        ("EndScreen.TotalLives", "Total lives: {0}", "Vides totals: {0}"),
        ("EndScreen.SpecialsCollected", "Specials collected: {0}", "Especials recollits: {0}"),
        ("EndScreen.SpecialsAvailable", "Specials for the fight: {0}", "Especials per al combat: {0}"),
        ("EndScreen.Xp", "XP: +{0}  (Level {1})", "XP: +{0}  (Nivell {1})"),
        ("SongSelection.Preview", "PREVIEW", "ESCOLTA"),
        ("SongSelection.StopPreview", "STOP", "ATURA"),
        ("OpponentSelection.Skip", "SKIP", "SALTA"),
        ("SongSelection.PreviewRange", "PREVIEW {0} – {1}", "ESCOLTA {0} – {1}"),
        ("HUD.Life", "LIFE", "VIDA"),
        ("HUD.Special", "SPECIAL", "ESPECIAL"),
        ("SongSelection.CardPlay", "PREVIEW", "ESCOLTA"),
        ("SongSelection.CardPause", "PAUSE", "PAUSA"),
        ("Mastery.Arrhythmic", "ARRHYTHMIC", "ARRÍTMIC"),
        ("Mastery.Apprentice", "APPRENTICE", "APRENENT"),
        ("Mastery.RhythmKeeper", "RHYTHM KEEPER", "RÍTMIC"),
        ("Mastery.Performer", "PERFORMER", "INTÈRPRET"),
        ("Mastery.Harmonist", "HARMONIST", "HARMONISTA"),
        ("Mastery.Virtuoso", "VIRTUOSO", "VIRTUÓS"),
        ("Mastery.Maestro", "MAESTRO", "MESTRE"),
        ("Mastery.Composer", "COMPOSER", "COMPOSITOR"),
        ("MainMenu.Victories", "{0} victories", "{0} victòries"),
        ("MatchResult.Mastery", "{0} · {1} victories", "{0} · {1} victòries"),
        ("MatchResult.MasteryUp", "MASTERY UP: {0} → {1}", "MESTRIA: {0} → {1}"),
        ("Mastery.ToneDeaf", "TONE-DEAF", "DUR D'ORELLA"),
        ("Mastery.OnBeat", "ON BEAT (SOMETIMES)", "A COMPÀS (DE VEGADES)"),
        ("Mastery.Musician", "LEGIT MUSICIAN", "MÚSIC DE VERITAT"),
        ("Mastery.SharpEar", "SHARP EAR", "ORELLA FINA"),
        ("Composer.bach.Bio", "Johann Sebastian Bach once walked some 400 km just to hear an organist play, and was briefly jailed by a duke for trying to quit his job. He fathered twenty children and still found time for more than a thousand works. Counterpoint is his combo system: every voice attacks at once.", "Johann Sebastian Bach va caminar uns 400 km només per sentir tocar un organista, i un duc el va tancar a la presó per voler plegar de la feina. Va tenir vint fills i encara va trobar temps per a més de mil obres. El contrapunt és el seu sistema de combos: totes les veus ataquen alhora."),
        ("Composer.beethoven.Bio", "Ludwig van Beethoven kept writing masterpieces after going almost completely deaf, which says a lot about stubbornness. He moved house in Vienna dozens of times and was famous for his temper. Expect sudden dynamics: quiet, then a sforzando to the face.", "Ludwig van Beethoven va continuar escrivint obres mestres després de quedar-se gairebé sord del tot, cosa que diu molt de la seva tossuderia. A Viena es va canviar de casa desenes de vegades i tenia un geni famós. Prepara't per a dinàmiques sobtades: silenci, i de cop un sforzando a la cara."),
        ("Composer.brahms.Bio", "Johannes Brahms played piano in Hamburg's harbour taverns as a teenager before Robert Schumann hailed him as the next big thing. He then spent about twenty years on his First Symphony, terrified of Beethoven's shadow. He also wrote the world's most famous lullaby, so don't fall asleep mid-round.", "Johannes Brahms tocava el piano a les tavernes del port d'Hamburg d'adolescent, abans que Robert Schumann el proclamés la gran promesa. Després va trigar uns vint anys a acabar la Primera Simfonia, aterrit per l'ombra de Beethoven. També va escriure la cançó de bressol més famosa del món: no t'adormis a mig assalt."),
        ("Composer.chopin.Bio", "Frédéric Chopin wrote almost only for the piano and preferred intimate salons to big concert halls. Born in Poland and settled in Paris, he asked for his heart to go back home, and it rests in a Warsaw church. His rubato bends time; so do his dodges.", "Frédéric Chopin va escriure gairebé només per a piano i preferia els salons íntims a les grans sales de concert. Nascut a Polònia i establert a París, va demanar que el seu cor tornés a casa, i reposa en una església de Varsòvia. El seu rubato doblega el temps; les seves esquives, també."),
        ("Composer.handel.Bio", "George Frideric Handel was born in Germany and conquered London with operas and oratorios. He wrote Messiah in roughly three and a half weeks, and as a young man survived a sword duel thanks, the story goes, to a coat button. Here, Hallelujah is not a celebration: it is a warning.", "Georg Friedrich Händel va néixer a Alemanya i va conquerir Londres amb òperes i oratoris. Va escriure el Messies en unes tres setmanes i mitja i, de jove, va sobreviure a un duel d'espases gràcies, segons diuen, a un botó de la jaqueta. Aquí l'Al·leluia no és una celebració: és un avís."),
        ("Composer.haydn.Bio", "Joseph Haydn wrote over a hundred symphonies and earned the nickname 'Father of the Symphony'. In his Farewell Symphony the musicians leave the stage one by one, a polite hint to his prince that everyone wanted a holiday. His Surprise Symphony proves he also enjoys a sudden hit.", "Joseph Haydn va escriure més de cent simfonies i el van batejar com el 'pare de la simfonia'. A la Simfonia dels Adéus els músics abandonen l'escenari un per un, una indirecta educada al seu príncep perquè tothom volia vacances. La Simfonia de la Sorpresa demostra que també li agrada un cop inesperat."),
        ("Composer.monteverdi.Bio", "Claudio Monteverdi helped invent opera: his L'Orfeo, from 1607, is still staged today. He later ran the music at St Mark's in Venice for three decades, bridging the Renaissance and the Baroque. He has been making dramatic entrances for over four hundred years.", "Claudio Monteverdi va ajudar a inventar l'òpera: el seu L'Orfeo, de 1607, encara es representa avui. Després va dirigir la música de Sant Marc de Venècia durant tres dècades, fent de pont entre el Renaixement i el Barroc. Fa més de quatre-cents anys que fa entrades dramàtiques."),
        ("Composer.mozart.Bio", "Wolfgang Amadeus Mozart toured Europe's courts as a child prodigy and wrote more than 600 works before dying at just 35. His letters are full of very rude jokes, so the elegance is partly a costume. Fast, light and precise: he finishes a combo before you notice it started.", "Wolfgang Amadeus Mozart va recórrer les corts d'Europa com a nen prodigi i va escriure més de 600 obres abans de morir amb només 35 anys. Les seves cartes estan plenes d'acudits molt grollers, així que l'elegància és, en part, una disfressa. Ràpid, lleuger i precís: acaba un combo abans que t'adonis que l'ha començat."),
        ("Composer.pachelbel.Bio", "Johann Pachelbel was a respected German organist who taught Johann Christoph Bach, J. S. Bach's older brother. His Canon in D slept for centuries before becoming a 20th-century wedding obsession. The same eight bass notes, over and over: patience is his weapon.", "Johann Pachelbel va ser un organista alemany molt respectat que va tenir com a alumne Johann Christoph Bach, el germà gran de J. S. Bach. El seu Cànon en Re va dormir durant segles abans de convertir-se en l'obsessió de les bodes del segle XX. Les mateixes vuit notes de baix, una vegada i una altra: la paciència és la seva arma."),
        ("Composer.tchaikovsky.Bio", "Pyotr Ilyich Tchaikovsky worked as a clerk at Russia's Ministry of Justice before committing fully to music. He went on to write Swan Lake, The Nutcracker and an overture that calls for real cannons. He conducted at the opening of Carnegie Hall in 1891, so big crowds do not scare him.", "Piotr Ílitx Txaikovski va treballar d'oficinista al Ministeri de Justícia rus abans de dedicar-se del tot a la música. Després va escriure El llac dels cignes, El trencanous i una obertura que demana canons de veritat. El 1891 va dirigir a la inauguració del Carnegie Hall: les grans multituds no li fan por."),
        ("Composer.vivaldi.Bio", "Antonio Vivaldi, nicknamed 'the Red Priest' for his hair, taught music to the girls of Venice's Ospedale della Pietà. He wrote around five hundred concertos and attached sonnets to The Four Seasons describing storms, birds and frozen roads. He died poor in Vienna, then became one of the most played composers on Earth.", "Antonio Vivaldi, conegut com 'el Capellà Roig' pel color dels cabells, ensenyava música a les noies de l'Ospedale della Pietà de Venècia. Va escriure uns cinc-cents concerts i va acompanyar Les Quatre Estacions amb sonets sobre tempestes, ocells i camins glaçats. Va morir pobre a Viena i després es va convertir en un dels compositors més escoltats del planeta."),
        ("Composer.wagner.Bio", "Richard Wagner wrote The Ring of the Nibelung, an opera cycle of about fifteen hours spread over four evenings. He built his own theatre in Bayreuth to stage it, after years of dodging creditors and a warrant from the 1849 Dresden uprising. Nothing he does is short, this fight included.", "Richard Wagner va escriure L'anell del nibelung, un cicle d'òperes d'unes quinze hores repartides en quatre vetllades. Va construir el seu propi teatre a Bayreuth per representar-lo, després d'anys esquivant creditors i una ordre de detenció per l'aixecament de Dresden de 1849. Res del que fa és curt, i aquest combat tampoc."),
        ("Rivals.BioLocked", "Defeat any version of this composer to unlock their story.", "Derrota qualsevol versió d'aquest compositor per descobrir-ne la història."),
        ("GameStyle.Unknown", "MYSTERY", "MISTERI"),
        ("GameStyle.Classical", "CLASSICAL", "CLÀSSICA"),
        ("GameStyle.Cinematic", "EPIC / CINEMATIC", "ÈPICA / CINEMATOGRÀFICA"),
        ("GameStyle.Acoustic", "ACOUSTIC", "ACÚSTICA"),
        ("GameStyle.Rock", "ROCK", "ROCK"),
        ("GameStyle.Heavy", "HEAVY", "CONTUNDENT"),
        ("GameStyle.Electronic", "ELECTRONIC", "ELECTRÒNICA"),
        ("GameStyle.Dance", "DANCE", "BALL"),
        ("GameStyle.Groove", "GROOVE", "GROOVE"),
        ("GameStyle.Funky", "FUNKY", "FUNKY"),
        ("GameStyle.Soulful", "SOULFUL", "AMB ÀNIMA"),
        ("GameStyle.Jazzy", "JAZZY", "JAZZ"),
        ("GameStyle.Catchy", "CATCHY POP", "POP ENGANXÓS"),
        ("GameStyle.Energetic", "ENERGETIC", "ENÈRGICA"),
        ("GameStyle.Chill", "CHILL", "RELAX"),
        ("GameStyle.Dreamy", "DREAMY", "SOMIADORA"),
        ("GameStyle.Dark", "DARK", "FOSCA"),
        ("GameStyle.Experimental", "EXPERIMENTAL", "EXPERIMENTAL"),
    };

    [MenuItem("Tools/MusicGame/Setup Localization")]
    public static void Setup()
    {
        // Standard, stable Addressables bootstrap — safe to call even if Addressables settings
        // already exist (no-op then). Avoids a "no Addressable settings" error the first time
        // CreateStringTableCollection touches the Addressables system below.
        AddressableAssetSettingsDefaultObject.GetSettings(true);

        EnsureFolder(AssetFolder);
        EnsureFolder(LocaleFolder);

        Locale english = EnsureLocale(SystemLanguage.English, "en");
        Locale catalan  = EnsureLocale(SystemLanguage.Catalan, "ca");

        var locales = new List<Locale>(LocalizationEditorSettings.GetLocales());

        var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
        if (collection == null)
            collection = LocalizationEditorSettings.CreateStringTableCollection(TableName, AssetFolder, locales);

        foreach (var locale in locales)
        {
            // Self-healing: a previous broken/partial run (or a collection made before every
            // Locale existed) can leave the collection missing a table for some locale — add it
            // instead of silently skipping that locale forever.
            var table = collection.GetTable(locale.Identifier) as StringTable;
            if (table == null)
                table = collection.AddNewTable(locale.Identifier) as StringTable;
            if (table == null)
            {
                Debug.LogError($"[LocalizationSetup] Could not create/find a StringTable for locale '{locale.Identifier}' — skipping it.");
                continue;
            }

            bool useCatalan = catalan != null && locale.Identifier.Equals(catalan.Identifier);
            foreach (var e in Entries)
                table.AddEntry(e.key, useCatalan ? e.ca : e.en);

            EditorUtility.SetDirty(table);
        }

        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(collection);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LocalizationSetup] '{TableName}' populated with {Entries.Length} entries across {locales.Count} locale(s).");
    }

    // Returns the existing project Locale matching `code` if there is one; otherwise creates it
    // (CreateInstance<Locale> + AssetDatabase.CreateAsset + LocalizationEditorSettings.AddLocale —
    // the exact same sequence Unity's own Locale Generator window uses) and returns the new one.
    private static Locale EnsureLocale(SystemLanguage language, string code)
    {
        foreach (var l in LocalizationEditorSettings.GetLocales())
            if (l.Identifier.Code == code) return l;

        var locale = Locale.CreateLocale(language);

        // A plain concatenated path, not AssetDatabase.GenerateUniqueAssetPath — that call can
        // return "" (and CreateAsset then silently writes a malformed, extension-less file) if
        // the folder isn't in the AssetDatabase's index yet, which is exactly what happened right
        // after EnsureFolder created it in this same script run without a Refresh in between.
        AssetDatabase.Refresh();
        string assetPath = $"{LocaleFolder}/{code}.asset";
        if (AssetDatabase.LoadAssetAtPath<Locale>(assetPath) != null)
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError($"[LocalizationSetup] Could not compute a valid asset path under '{LocaleFolder}' for locale '{code}' " +
                           "— the folder may not exist. This locale will still work for THIS run, but won't be saved as an asset.");
            return locale;
        }

        AssetDatabase.CreateAsset(locale, assetPath);
        LocalizationEditorSettings.AddLocale(locale);
        Debug.Log($"[LocalizationSetup] Created Locale '{locale.name}' ({code}) at {assetPath}.");
        return locale;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf   = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
        AssetDatabase.Refresh(); // make the new folder immediately visible to AssetDatabase APIs
    }
}
#endif
