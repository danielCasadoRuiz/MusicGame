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
    internal static readonly (string key, string en, string ca)[] Entries =
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
        ("EndScreen.Title", "RACE RESULTS", "RESULTATS DE LA CURSA"),
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
        ("Settings.MusicVolume", "Music",   "Música"),
        ("Settings.SfxVolume",   "Effects", "Efectes"),
        ("Settings.VoiceVolume", "Voices",  "Veus"),
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
        ("SongSelection.DownloadFailed",       "Couldn't download that song — check your connection and try again.", "No s'ha pogut descarregar la cançó — comprova la connexió i torna-ho a provar."),
        ("SongSelection.ContentIncompatible",  "This song's content needs an update — try another song.", "El contingut d'aquesta cançó s'ha d'actualitzar — prova'n una altra."),
        ("Preparing.Downloading",              "Downloading song ({0} MB)...", "Descarregant la cançó ({0} MB)..."),
        ("Preparing.Loading",                  "Loading song...", "Carregant la cançó..."),
        ("Preparing.Preparing",                "Preparing the level...", "Preparant el nivell..."),
        ("SongSelection.LocalFileCancelled",   "File selection cancelled.",        "Selecció de fitxer cancel·lada."),
        ("SongSelection.LocalFileSelected",    "Selected: {0}",                    "Seleccionat: {0}"),
        ("SongSelection.LocalFileDefaultName", "Local File",                       "Fitxer local"),

        // ── Countdown ────────────────────────────────────────────────────────────
        ("Countdown.Go",     "TEMPO!",  "TEMPO!"),  // reserved word (= the announcer), same in every language
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
        ("Fight.Banner",            "FURY!",                 "FURY!"),   // reserved word (= the announcer)

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
        ("MainMenu.SelectSong", "SELECT SONG", "TRIA CANÇÓ"),
        ("MainMenu.CatalogComplete", "CATALOG COMPLETE — every song completed", "CATÀLEG COMPLETAT — totes les cançons superades"),
        ("SongSelection.Completed", "COMPLETED", "COMPLETADA"),
        ("MatchResult.SelectSong", "SELECT SONG", "TRIA CANÇÓ"),
        ("Mobile.Crouch", "CROUCH", "AJUP"),
        // ── In-arena presentation + language selector (see LanguageSettingsSetup) ──
        ("Fight.Skip",            "SKIP",      "SALTA"),
        ("Fight.TimerSeconds",    "{0} s",     "{0} s"),
        ("Fight.Loading",         "Loading…",  "Carregant…"),
        ("Settings.Language",     "Language",  "Idioma"),
        ("Settings.LanguageAuto", "Automatic", "Automàtic"),
        ("Results.RaceStars", "Race rating", "Valoració de la cursa"),
        ("Results.Score", "Score {0} / {1} ({2}%)", "Puntuació {0} / {1} ({2}%)"),
        ("Results.NoFallBonus", "No falls — no-fall bonus +{0}%", "Cap caiguda — bonus sense caure +{0}%"),
        ("Results.FallsBonusLost", "Falls {0} — no-fall bonus (+{1}%) lost", "Caigudes {0} — bonus sense caure (+{1}%) perdut"),
        ("Results.Falls", "Falls {0}", "Caigudes {0}"),
        ("Results.ForTheFight", "FOR THE FIGHT:", "PER AL COMBAT:"),
        ("Results.Specials", "{0} specials", "{0} especials"),
        ("Results.SpecialsSplit", "{0} specials collected, {1} available", "{0} especials recollits, {1} disponibles"),
        ("Results.Combos", "x3 combos {0} · x4 combos {1}", "combos x3 {0} · combos x4 {1}"),
        ("Results.Lives", "Lives +{0}  ·  {1} extra lives for Fight Again", "Vides +{0}  ·  {1} vides extra per tornar a lluitar"),
        ("Results.LivesCapped", "Lives +{0} ({1} collected; the rest were already earned in an earlier attempt of this match)  ·  {2} extra lives for Fight Again", "Vides +{0} ({1} recollides; la resta ja es van guanyar en un intent anterior d'aquesta partida)  ·  {2} vides extra per tornar a lluitar"),
        ("Results.BestRace", "Best race of this match", "Millor cursa d'aquesta partida"),
        ("Results.MatchPassed", "MATCH PASSED", "PARTIDA SUPERADA"),
        ("Results.MatchNotPassed", "MATCH NOT PASSED", "PARTIDA NO SUPERADA"),
        ("Results.FightWon", "FIGHT · won vs {0} (version {1})", "COMBAT · guanyat contra {0} (versió {1})"),
        ("Results.FightLost", "FIGHT · lost vs {0} (version {1})", "COMBAT · perdut contra {0} (versió {1})"),
        ("Results.FightStars", "Fight rating", "Valoració del combat"),
        ("Results.Rounds", "Rounds {0}–{1}", "Rondes {0}–{1}"),
        ("Results.RaceHeader", "RACE · {0}", "CURSA · {0}"),
        ("Results.MatchHeader", "MATCH · {0}", "PARTIDA · {0}"),
        ("Results.MatchHeaderFree", "MATCH · free run (outside the campaign)", "PARTIDA · cursa lliure (fora de la campanya)"),
        ("Results.NotPassed", "Not passed: the campaign does not advance. Fight Again (1 extra life) or replay the song.", "No superada: la campanya no avança. Torna a lluitar (1 vida extra) o repeteix la cançó."),
        ("Results.LivesLeft", "Extra lives: {0}", "Vides extra: {0}"),
        ("Results.MatchStars", "Match rating (race + fight)", "Valoració de la partida (cursa + combat)"),
        ("Results.Advanced", "Match completed! Next: {0}", "Partida completada! Següent: {0}"),
        ("Results.LevelCompletedNext", "Level completed! Next: {0}", "Nivell completat! Següent: {0}"),
        ("Results.ReplayedPosition", "Match already completed: only its records can improve.", "Partida ja completada: només en poden millorar els rècords."),
        ("Results.RecordImproved", "Match record improved", "Rècord de la partida millorat"),
        ("Results.RecordKept", "Match record", "Rècord de la partida"),
        ("Campaign.PositionOf", "{0} · match {1} of {2}", "{0} · partida {1} de {2}"),
        ("MainMenu.ExtraLives", "Extra lives {0}", "Vides extra {0}"),
        ("MainMenu.LapDone", "LAP {0} COMPLETED", "VOLTA {0} COMPLETADA"),
        ("MainMenu.CampaignStatus", "Lap {0}  ·  Mastered matches {1}/{2}", "Volta {0}  ·  Partides dominades {1}/{2}"),
        // Help: tip cards, full help, bonus conversion (HelpScreenPrefabs / HelpUISetup).
        ("Help.Stat.PunchPower", "Punch power", "Força de cop de puny"),
        ("Help.Stat.KickPower", "Kick power", "Força de puntada"),
        ("Help.Stat.Agility", "Agility", "Agilitat"),
        ("Help.Stat.Resistance", "Resistance", "Resistència"),
        ("Help.Stat.Short.PunchPower", "PUNCH", "PUNY"),
        ("Help.Stat.Short.KickPower", "KICK", "PUNTADA"),
        ("Help.Stat.Short.Agility", "AGILITY", "AGILITAT"),
        ("Help.Stat.Short.Resistance", "RESIST.", "RESIST."),
        ("Help.Stat.PunchPower.Full", "More damage with your punches.", "Més dany amb els cops de puny."),
        ("Help.Stat.KickPower.Full", "More damage with your kicks.", "Més dany amb les puntades."),
        ("Help.Stat.Agility.Full", "Faster moves: shorter start-up and recovery.", "Moviments més ràpids: menys preparació i menys recuperació."),
        ("Help.Stat.Resistance.Full", "You take less damage and get staggered less.", "Reps menys dany i et desequilibren menys."),
        ("Help.Bonus.TripleCombo", "COMBO x3", "COMBO x3"),
        ("Help.Bonus.QuadCombo", "COMBO x4", "COMBO x4"),
        ("Help.Effect.ScoreOnly", "Score only", "Només punts"),
        ("Help.Effect.Life", "Extra life", "Vida extra"),
        ("Help.Effect.Special", "+1 Special", "+1 Especial"),
        ("Help.Effect.Triple", "{0} in a row", "{0} seguits"),
        ("Help.Effect.Quad", "{0} in a row: Power", "{0} seguits: Poder"),
        ("Help.Bonus.Feeds", "Builds: {0}.", "Prepara: {0}."),
        ("Help.Bonus.ScoreOnlyFull", "Adds score. It does not change any fight attribute.", "Suma punts. No modifica cap atribut del combat."),
        ("Help.Bonus.LifeFull", "+1 extra life, saved with your progress (at most {0} per song). After losing a fight, a life lets you fight again.", "+1 vida extra, que es desa amb el progrés (com a màxim {0} per cançó). Si perds una lluita, una vida et permet tornar a lluitar."),
        ("Help.Bonus.SpecialFull", "+1 Special for the next fight. The Special move spends it on its powered version (×{0} damage); without one it is a plain heavy punch.", "+1 Especial per a la lluita següent. El moviment Especial el gasta en la versió potenciada (dany ×{0}); sense, és un cop fort normal."),
        ("Help.Bonus.TripleFull", "{0} musical bonuses in a row (within {1} s) give an x3 charge for the fight. Its use in the fight is still pending.", "{0} bonus musicals seguits (en {1} s) donen una càrrega x3 per a la lluita. El seu ús al combat encara està pendent."),
        ("Help.Bonus.QuadFull", "{0} musical bonuses in a row (within {1} s) give an x4 charge. In the fight, hold ↓ + Punch + Kick for {2} s: Power state for {3} s (×{4} damage).", "{0} bonus musicals seguits (en {1} s) donen una càrrega x4. Al combat, mantén ↓ + Puny + Puntada {2} s: estat de Poder durant {3} s (dany ×{4})."),
        ("Help.Bonus.Intro", "Musical bonuses prepare your fighter. How well you collect them (out of those the song has) sets your total power, up to {0} points; which ones you collect decides how those points are shared between the attributes. If a song has none of an attribute's bonuses, that attribute takes your average performance.", "Els bonus musicals preparen el teu lluitador. Com de bé els reculls (dels que té la cançó) fixa la potència total, fins a {0} punts; quins reculls decideix com es reparteixen aquests punts entre els atributs. Si la cançó no té cap bonus d'un atribut, aquest atribut fa servir el teu rendiment mitjà."),
        ("Help.Bonus.HelpTitle", "BONUSES", "BONUS"),
        ("Help.Bonus.StatsHeader", "What each attribute does", "Què fa cada atribut"),
        ("Help.Moves.Title", "MOVES", "MOVIMENTS"),
        ("Help.Moves.Intro", "Keyboard: J = Punch · K = Kick · WASD or arrows = move. → means towards the rival, ← away from them.", "Teclat: J = Puny · K = Puntada · WASD o fletxes = moure't. → vol dir cap al rival, ← enrere."),
        ("Help.Moves.IntroMobile", "Joystick to move, PUNCH and KICK buttons to attack. → means towards the rival, ← away from them.", "Joystick per moure't, botons COP i PUNTADA per atacar. → vol dir cap al rival, ← enrere."),
        ("Help.Moves.Basics", "Basics", "Bàsics"),
        ("Help.Moves.Combos", "Combos", "Combos"),
        ("Help.Moves.Special", "Special and Power", "Especial i Poder"),
        ("Help.Move.Punch", "Punch", "Cop de puny"),
        ("Help.Move.Kick", "Kick", "Puntada"),
        ("Help.Move.HighLow", "High / low attack", "Atac alt / baix"),
        ("Help.Move.HighLowDetail", "Hold ↑ or ↓ while pressing the button.", "Mantén ↑ o ↓ mentre prems el botó."),
        ("Help.Move.Guard", "Guard (hold)", "Guàrdia (mantén)"),
        ("Help.Move.GuardDetail", "Hold away from the rival to block. With ↓ you block low attacks, with ↑ high ones. Not in the air or mid-move.", "Mantén enrere per bloquejar. Amb ↓ bloqueges els atacs baixos, amb ↑ els alts. No funciona a l'aire ni enmig d'un moviment."),
        ("Help.Move.Grab", "Grab", "Agafada"),
        ("Help.Move.GrabDetail", "Press both buttons together (within {0} s). The same press escapes a grab.", "Prem els dos botons alhora (en {0} s). La mateixa pulsació et deslliura d'una agafada."),
        ("Help.Move.ComboTiming", "At most {0} s between presses.", "Com a màxim {0} s entre pulsacions."),
        ("Help.Move.FinisherDetail", "The third hit deals ×{0} damage.", "El tercer cop fa dany ×{0}."),
        ("Help.Move.FinisherShort", "3rd hit ×{0}", "3r cop ×{0}"),
        ("Help.Move.SpecialReq", "Spends 1 Special (you have {0}) on the powered version (×{1} damage). Without a Special: a plain heavy punch.", "Gasta 1 Especial (en tens {0}) en la versió potenciada (dany ×{1}). Sense Especial: un cop fort normal."),
        ("Help.Move.SpecialShort", "needs 1 Special ({0})", "cal 1 Especial ({0})"),
        ("Help.Move.Power", "Power state", "Estat de Poder"),
        ("Help.Move.PowerReq", "Hold for {0} s. Spends 1 x4 charge (you have {1}). Lasts {2} s: ×{3} damage.", "Mantén {0} s. Gasta 1 càrrega x4 (en tens {1}). Dura {2} s: dany ×{3}."),
        ("Help.Move.PowerShort", "hold {0} s · needs 1 x4 ({1})", "mantén {0} s · cal 1 x4 ({1})"),
        ("Help.Combo.aaa_b", "Punches + kick", "Cops + puntada"),
        ("Help.Combo.bb", "Double kick", "Doble puntada"),
        ("Help.Combo.bbab", "Kick chain", "Cadena de puntades"),
        ("Help.Combo.forward_a", "Lunge punch", "Cop en estocada"),
        ("Help.Combo.down_b", "Low kick", "Puntada baixa"),
        ("Help.Combo.back_aab", "Back chain", "Cadena enrere"),
        ("Help.Combo.dash_forward", "Dash", "Esprint"),
        ("Help.Combo.backdash", "Backdash", "Salt enrere"),
        ("Help.Combo.punch_finisher", "Triple punch", "Triple cop de puny"),
        ("Help.Combo.kick_finisher", "Triple kick", "Triple puntada"),
        ("Help.Combo.special_fireball", "Special", "Especial"),
        // Charges (CombatResourceUse): which moves are free, which spend a charge — new keys (tables are never overwritten).
        ("Help.Moves.FreeCombos", "Combos (free)", "Combos (sense cost)"),
        ("Help.Moves.Charged", "Spend a charge", "Gasten una càrrega"),
        ("Help.Move.NoCharge", "Spends no charge.", "No gasta cap càrrega."),
        ("Help.Move.SpecialSpend", "spends 1 Special · you have {0}", "gasta 1 Especial · en tens {0}"),
        // 2026-10-25: x3 → powered finishers, x4 → Power spends all charges (CombatResourceUse).
        ("Help.Move.FinisherPowered", "With an x3 charge the third hit deals ×{0} damage and spends it; without one it is a normal hit.", "Amb una càrrega x3, el tercer cop fa dany ×{0} i la gasta; sense, és un cop normal."),
        ("Help.Move.FinisherSpend", "spends 1 x3 · 3rd hit ×{0} · you have {1}", "gasta 1 x3 · 3r cop ×{0} · en tens {1}"),
        ("Help.Move.PowerAll", "hold {0} s · spends all x4 ({1}) → {2} s", "mantén {0} s · gasta tots els x4 ({1}) → {2} s"),
        ("Help.Move.PowerAllReq", "Hold for {0} s. Spends ALL your x4 charges (you have {1}): {2} s with one, +{3} s for each extra one (now {4} s). ×{5} damage.", "Mantén {0} s. Gasta TOTES les càrregues x4 (en tens {1}): {2} s amb una, +{3} s per cada una de més (ara {4} s). Dany ×{5}."),
        ("Help.Bonus.TripleUse", "{0} musical bonuses in a row (within {1} s) give an x3 charge. In the fight, the Triple punch or the Triple kick spends one: its third hit deals ×{2} damage.", "{0} bonus musicals seguits (en {1} s) donen una càrrega x3. Al combat, el Triple cop de puny o la Triple puntada en gasta una: el tercer cop fa dany ×{2}."),
        ("Help.Bonus.QuadUse", "{0} musical bonuses in a row (within {1} s) give an x4 charge. In the fight, hold ↓ + Punch + Kick for {2} s: Power state (×{3} damage), spending every x4 you have — {4} s with one, +{5} s for each extra one.", "{0} bonus musicals seguits (en {1} s) donen una càrrega x4. Al combat, mantén ↓ + Puny + Puntada {2} s: estat de Poder (dany ×{3}), que gasta tots els x4 que tinguis — {4} s amb un, +{5} s per cada un de més."),
        // 2026-10-25: four attributes, direct conversion (new keys — tables are never overwritten).
        ("Help.Effect.Bonus.Kick", "Stronger kicks", "Patades més fortes"),
        ("Help.Effect.Bonus.Snare", "Stronger punches", "Punys més forts"),
        ("Help.Effect.Bonus.HiHat", "Move faster", "Mou-te més ràpid"),
        ("Help.Effect.Bonus.Beat", "Take less damage", "Rep menys dany"),
        ("Help.Effect.Bonus.Onset", "Improves every attribute", "Millora tots els atributs"),
        ("Help.Effect.Bonus.Impact", "More powerful punches and kicks", "Punys i patades més potents"),
        ("Help.Bonus.Adds", "Each one you collect adds: {0}.", "Cada un que reculls suma: {0}."),
        ("Help.Bonus.IntroDirect", "Musical bonuses prepare your fighter: every one you collect adds points to the attributes it feeds — nothing is shared out or taken away. With no bonuses an attribute stays at its low base ({0}); it can reach {1}.", "Els bonus musicals preparen el teu lluitador: cada un que reculls suma punts als atributs que alimenta, sense repartir ni treure res. Sense bonus, un atribut es queda a la base baixa ({0}); pot arribar a {1}."),
        ("Help.Stat.Agility.MoveAndHit", "You move faster and every move comes out sooner: you get there first, and can guard or dodge before they hit you.", "Et mous més de pressa i tots els moviments surten abans: hi arribes primer i et pots protegir o esquivar abans que et toquin."),
        ("Help.Stat.PunchPower.Hit", "Your punches deal more damage and hit harder.", "Els teus cops de puny fan més dany i són més contundents."),
        ("Help.Stat.KickPower.Hit", "Your kicks deal more damage and hit harder.", "Les teves puntades fan més dany i són més contundents."),
        ("Help.Conv.Total", "Power {0}", "Potència {0}"),
        // The Special: a fixed 30 % of the rival's health, unblockable, dodgeable (FightMoveDefinition.damageAsMaxHealthFraction).
        ("Help.Move.SpecialFixed", "Spends 1 Special (you have {0}): a projectile that always takes {1}% of the rival's health. Guard can't stop it — only a dodge. Without a Special: a plain heavy punch.", "Gasta 1 Especial (en tens {0}): un projectil que sempre treu el {1}% de la vida del rival. La guàrdia no l'atura; només esquivar-lo. Sense Especial: un cop fort normal."),
        ("Help.Move.SpecialSpendFixed", "spends 1 Special · {1}% of the rival's health · you have {0}", "gasta 1 Especial · {1}% de la vida del rival · en tens {0}"),
        ("Help.Bonus.SpecialFixed", "+1 Special for the next fight. The Special move spends it: a projectile that always takes {0}% of the rival's health — guard can't stop it, a dodge can. The rival's Special does the same to you. Without one it is a plain heavy punch.", "+1 Especial per a la lluita següent. El moviment Especial el gasta: un projectil que sempre treu el {0}% de la vida del rival; la guàrdia no l'atura, esquivar-lo sí. L'Especial del rival et fa el mateix. Sense, és un cop fort normal."),
        // Player profile (Main Menu → profile icon).
        ("Profile.Title", "PROFILE", "PERFIL"),
        ("Profile.Strength", "Your usual fight strength", "La teva força habitual al combat"),
        ("Profile.Summary", "Average total {0} · best run {1} · {2} runs", "Total mitjà {0} · millor cursa {1} · {2} curses"),
        ("Profile.NoRuns", "Finish a run to start your profile.", "Acaba una cursa per començar el teu perfil."),
        ("Profile.LastRun", "Last run: punch {0} · kick {1} · resistance {2} · agility {3}", "Última cursa: puny {0} · patada {1} · resistència {2} · agilitat {3}"),
        ("Profile.Styles", "Styles you choose", "Estils que tries"),
        ("Profile.NoStyles", "Choose songs yourself in Song Selection to define your style.", "Tria cançons tu mateix a la selecció per definir el teu estil."),
        ("Profile.Look", "Your style: {0}", "El teu estil: {0}"),
        ("Profile.LookNone", "Your style: still to be discovered", "El teu estil: encara per descobrir"),
        ("Profile.Note", "Your strength is the average of what you turn into fight power in every run. It will measure you against other players.", "La teva força és la mitjana del que converteixes en poder de combat a cada cursa. Servirà per mesurar-te amb altres jugadors."),
        ("Profile.Back", "BACK", "TORNA"),
        ("Help.Tip.BonusTitle", "Bonuses prepare your next fight", "Els bonus preparen la lluita"),
        ("Help.Tip.MovesTitle", "Your moves", "Els teus moviments"),
        ("Help.Tip.More", "More info", "Més informació"),
        ("Help.Tip.PauseHint", "Full help also in Pause (Esc)", "Ajuda completa també a Pausa (Esc)"),
        ("Help.Pause.Bonuses", "BONUSES", "BONUS"),
        ("Help.Pause.Moves", "MOVES", "MOVIMENTS"),
        ("Help.Back", "BACK", "TORNA"),
        ("Help.Continue", "CONTINUE", "CONTINUA"),
        ("Help.HintBack", "Esc: back to the pause menu", "Esc: torna al menú de pausa"),
        ("Help.HintContinue", "Esc: close and continue", "Esc: tanca i continua"),
        ("Help.Conv.Title", "Your bonuses → your fight", "Els teus bonus → la lluita"),
        ("Help.Conv.Budget", "Power {0} / {1}", "Potència {0} / {1}"),
    };

    /// <summary>Adds ONLY the given keys that are missing from the UIText table (every locale) — never
    /// overwrites existing text (hand edits survive). Used by tools that introduce new strings (P3 song
    /// preparation). Returns how many entries were added.</summary>
    public static int AddMissingEntries(IEnumerable<string> keys)
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
        if (collection == null) { Debug.LogError("[LocalizationSetup] No UIText collection — run Tools > MusicGame > Setup Localization."); return 0; }
        var wanted = new HashSet<string>(keys);
        int added = 0;
        foreach (var table in collection.StringTables)
        {
            if (table == null) continue;
            bool useCatalan = table.LocaleIdentifier.Code == "ca";
            foreach (var e in Entries)
            {
                if (!wanted.Contains(e.key) || table.GetEntry(e.key) != null) continue;
                table.AddEntry(e.key, useCatalan ? e.ca : e.en);
                added++;
            }
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();
        return added;
    }

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
