

<pasted_content id="09d1">
Continua amb la implementació de MusicGame incorporant aquest feedback i el nou requisit d’àudio. Aquest missatge consolida les quatre fases pendents i la revisió independent de Codex.

No reiniciïs la feina ni descartis el nucli nou. Comprova què ja està resolt a la versió actual i completa la integració.

OBJECTIU

Volem que els bonus tinguin una correspondència clara amb els cops audibles, sense exigir coneixements musicals ni anotacions manuals a l’usuari.

Les cançons del catàleg propi han de publicar-se preanalitzades, amb àudio comprimit i perfil distribuïts per Addressables remots. El jugador ha de carregar-los sense executar FFT ni inferència.

Conserva l’anàlisi dinàmica per a futures cançons importades.

No implementis marcadors manuals, una editora de cançons, integracions amb Spotify ni serveis de pagament. Preserva les restriccions ja acordades sobre Unity, builds, dades personals i publicació externa.

ESTAT QUE CAL TENIR EN COMPTE

La Phase 2A ja ha produït un nucli C# independent de Unity, preprocessament semàntic optimitzat, beat tracker i format binari.

Segons el teu últim informe:

- 41/42 proves sintètiques passen.
- La fallada restant és una etiqueta HiHat sobrera en un dels dotze cops simultanis Kick+Snare a 44,1 kHz.
- Els errors antics dels 12 kicks extra i dels 31 esdeveniments addicionals del loop ja no s’han de considerar presents sense reproduir-los.
- Els benchmarks són externs a Unity i sobre WAV originals normalitzats, no sobre els clips comprimits finals.
- La qualitat de les etiquetes en música real encara necessita validació auditiva.
- La integració al projecte i les fases 1, 3 i 4 continuen pendents segons aquell informe.

Els temps externs de 0,4–0,7 s corresponen al nucli mesurat, no al procés complet de càrrega i preparació de partida.

FASE 1 — SINCRONITZACIÓ

Preserva MusicClock com a autoritat de temps i distància. GameplayTimeline és coherent amb el runner: programar anticipadament els objectes no és un error.

Corregeix i valida:

- Caigudes: FallRespawnSystem pausa l’àudio però MusicClock passa al temps de l’aplicació. Evita avançar esdeveniments mentre l’àudio està aturat.
- Ambientació: MusicEnvironmentController consulta SongProfile amb els segons de warmup inclosos i llegeix dades futures.
- Detectors live: utilitzen Time.time per consultar dades indexades pel temps del clip.
- Reinici d’historials, cooldowns i estat davant seek, respawn, restart i canvi de cançó.
- Inici de reproducció des d’un punt intermedi.
- Final natural de cançó i transició a farewell.
- Espera de càrrega després del warmup.
- Subscripcions dels detectors que no es donen de baixa.

Defineix conversions explícites entre temps del clip, temps de partida i temps dels esdeveniments.

No interpretis !isPlaying per si sol com a final natural: també pot correspondre a pausa, stop, error o transició.

En començar des d’un punt intermedi o fer seek, sincronitza els cursors sense executar en ràfega els esdeveniments anteriors.

Preserva la diferència entre spawn anticipat, revelació espacial, pulsació musical i recollida.

Desduplica els efectes de càmera segons temps i identitat musical. Un frame lent pot processar diversos cops legítims; no els fusionis només perquè arriben al mateix frame ni introdueixis un retard general per agrupar-los.

FASE 2B — INTEGRACIÓ DE L’ANÀLISI

Integra el nucli validat i utilitza el mateix pipeline per al bake del catàleg i per a importacions.

Mantén:

- Configuració immutable capturada al fil principal.
- Càlculs purs en segon pla.
- Concurrència limitada i buffers independents.
- Dependències temporals correctes entre finestres.
- Inferència i API de Unity en els fils compatibles.
- Lectura no bloquejant dels resultats del model quan sigui possible.
- Progrés real, monòton i publicat al fil principal.
- Cancel·lació, identificació de peticions i prevenció de duplicats.
- Publicació única d’un perfil complet i coherent.
- Publicació única de l’estil final.

Invalida una petició antiga abans de publicar qualsevol esdeveniment global, no només abans del callback final. GameSession ha de comprovar que el perfil correspon a la selecció vigent.

Gestiona la vida útil de tensors, models, workers, clips i handles. No alliberis recursos encara utilitzats ni retinguis indefinidament seleccions anteriors.

Substitueix la cache JSON pel format binari versionat i elimina l’espera artificial de dos segons.

Verifica també les correccions dels errors antics:

- Identitat basada només en nom i nombre de mostres.
- Camí de retag que desa probabilitat de veu ja suavitzada com si fos crua.
- Resultat de clip.GetData ignorat.
- Còmput de finestres que omet la primera finestra completa.
- BPM arbitrari quan no hi ha evidència.
- Candidats desordenats abans de compondre patrons.

Separa clarament dades crues i derivades. Evita doble suavització i qualsevol mutació dels assets compartits.

Mesura dins Unity el temps total, cada etapa, els bloquejos del fil principal i la memòria. Les assignacions acumulades, la memòria viva màxima i el PCM resident són magnituds diferents.

Solapar ritme i semàntica pot accelerar el procés però augmentar el pic de memòria. Aplica un pressupost global de concurrència adequat per a mòbil.

FASE 3 — CATÀLEG PREANALITZAT I ADDRESSABLES REMOTS

Implementa una eina d’Editor que analitzi totes les cançons, regeneri només perfils absents o obsolets, permeti reconstrucció completa i cancel·lació, i informi dels errors.

Els perfils publicats han de ser assets versionables del projecte. No depenguis de la cache personal del desenvolupador com a font de veritat.

Inclou totes les dades necessàries per a món, visuals, estil, deteccions i gameplay. Qualsevol reconstrucció de derivats en carregar ha de ser lleugera i mesurada.

Distribueix àudio i perfil com a contingut remot vinculat per songId i revisió. No carreguis tot el catàleg en RAM.

Migra el grup Songs, actualment local i PackTogether, i verifica el layout real dels bundles. Dos assets amb PackSeparately no formen automàticament un bundle per parella.

Tria i justifica entre:

- Un bundle per cançó amb àudio i perfil.
- Bundles separats, però amb vinculació explícita i coherent de revisions.

Considera que un canvi només del perfil pot obligar a descarregar de nou l’àudio si comparteixen bundle.

El catàleg remot ha de permetre incorporar cançons noves amb identitat, progressió i dades de similitud. No n’hi ha prou amb descobrir una nova localització AudioClip si la resolució de songId continua depenent d’un asset estàtic del player.

Valida abans de construir contingut:

- Perfils presents i compatibles.
- Revisió correcta de l’àudio.
- Versions de format, pipeline, model i configuració.
- Grups i dependències remots.
- Absència d’inclusió accidental dels àudios o perfils al player.

Runtime ha de carregar el perfil publicat sense FFT ni inferència. Diferencia descàrrega, càrrega, preparació del món i errors de contingut.

Si falta un perfil o és incompatible, no encobreixis l’error amb una anàlisi costosa silenciosa.

FASE 4 — BONUS I VALIDACIÓ AUDITIVA

GameplayTimeline ha de consumir les noves deteccions preservant tots els moments musicals al perfil i seleccionant-ne una quantitat jugable segons dificultat i accessibilitat.

Diferencia:

- Atac detectat.
- Famílies possibles.
- Objectes seleccionats.
- Efectes visuals executats.

Una família sobrera no ha de convertir-se automàticament en un bonus addicional sense una política de selecció.

Ordena els candidats abans de qualsevol operació que assumeixi ordre temporal. Gestiona explícitament els simultanis.

Utilitza beatTimes quan correspongui en lloc d’una graella rígida basada només en 60/BPM. Mantén la graella sintètica identificada, configurable i desactivable. No la presentis com a cops reals detectats.

Afegeix diagnòstics i clics opcionals per comparar, escoltant:

- Deteccions.
- Bonus seleccionats.
- Pulsacions executades.
- Esdeveniments reals i sintètics.

Aquestes eines han de permetre jutjar el resultat sense saber música i han d’estar desactivades en l’ús normal.

NOU REQUISIT — WAV PER ANALITZAR I ÀUDIO COMPRIMIT PER JUGAR

Volem conservar un màster WAV sense pèrdues per preparar l’anàlisi i distribuir una representació comprimida per reproduir-la al joc.

L’objectiu és reduir descàrrega, emmagatzematge i, quan sigui possible, memòria de reproducció, preservant qualitat i sincronització.

No assumeixis que cal crear un MP3 intermedi:

- Els WAV actuals ja es comprimeixen a Vorbis segons la configuració inspeccionada.
- L’extensió del fitxer font no determina per si sola la mida del contingut construït.
- Convertir WAV → MP3 → format d’importació de Unity pot introduir una segona compressió amb pèrdues sense benefici.

Avalua primer aquest flux:

màster WAV → anàlisi i perfil → importació comprimida de Unity → Addressables remots.

Compara formats i qualitat efectivament compatibles amb les plataformes objectiu. Tria segons qualitat audible, mida real dels bundles, CPU, memòria, seek i sincronització; no només segons la mida del fitxer font.

No sobreescriguis ni degradis els màsters originals. Evita publicar-los accidentalment com a còpies addicionals si només són necessaris per al bake.

Alineament temporal:

- Tenir la mateixa durada no garanteix que els atacs coincideixin.
- Comprova retard inicial, padding final, canvis de freqüència de mostreig, retalls i possibles diferències de descodificació.
- Mesura correspondència al principi, al mig i al final entre màster d’anàlisi i àudio de reproducció.
- Un offset constant només és suficient si l’error és constant.
- Si hi ha deriva o una altra transformació temporal, resol-ne la causa o defineix una conversió explícita i validada.
- No introdueixis compensacions manuals arbitràries per cançó.
- Separa compensació del detector, alineament màster/reproducció i latència de sortida del dispositiu. No els compensis dues vegades.

Si el pipeline necessita un ajust, desa’n les metadades amb una convenció de signe clara i aplica’l coherentment als consumidors.

El perfil ha de vincular explícitament:

- Identitat i revisió del màster analitzat.
- Paràmetres i versió d’anàlisi.
- Identitat i revisió de la representació de reproducció.
- Configuració d’importació/plataforma rellevant.
- Conversió temporal, si existeix.

No exigeixis hashes de PCM idèntics entre plataformes ni descodifiquis tota la cançó al mòbil només per verificar-ne la identitat.

Com que el catàleg ja disposarà de perfils, avalua CompressedInMemory o Streaming per a reproducció. No confonguis reducció de mida del paquet amb reducció de RAM: DecompressOnLoad pot conservar l’àudio descomprimit en memòria.

Valida preview, seek, pausa, respawn, reinici, reproducció parcial i final natural amb la política triada.

Si analitzar el màster altera la normalització respecte al pipeline anterior, defineix una política reproduïble. Evita que els visuals o llindars quedin a una escala inesperada.

FEEDBACK SOBRE LES PROVES

Codex va trobar aquests defectes en snapshots anteriors. Segons el teu informe final, ja has començat a corregir-los; verifica que quedin resolts:

1. El veredicte ignorava famílies sobrants.
   Una predicció Kick+Snare+HiHat no pot passar una prova de kick aïllat només perquè contingui Kick.

2. El programa retornava exit code 0 encara que hi hagués proves fallides.
   Agrega els resultats i retorna error quan falla una prova obligatòria.

3. Els sons sintètics podien generar transitoris al tall final.
   Valida els estímuls i diferencia aquests artefactes dels errors del detector.

Informa separadament de:

- Omissions.
- Esdeveniments temporals addicionals.
- Famílies incorrectes o sobrants.
- Error temporal.
- Casos opcionals o informatius.

Si un instrument està emmascarat i s’accepta com a opcional, justifica aquesta decisió amb el senyal i documenta-la. No retiris requisits només per fer passar les proves.

La fallada restant de Kick+Snare a 44,1 kHz s’ha de corregir si és possible o mantenir explícitament documentada. No cal fingir una classificació perfecta.

VALIDACIÓ I ENTREGA

Completa les quatre fases i informa de què està implementat, verificat o pendent.

Inclou:

- Suite sintètica amb criteris de classificació corregits.
- Equivalència seqüencial/paral·lela.
- Cancel·lació i progrés.
- Sincronització dins Unity.
- Alineament entre màster i àudio comprimit.
- Instal·lació neta sense cache d’anàlisi.
- Càrrega de catàleg sense FFT ni inferència.
- Actualitzacions remotes coherents.
- Layout real dels bundles.
- Mides, temps, CPU i memòria amb condicions de mesura.
- Resultat de la comparació auditiva i limitacions.

Els benchmarks previs sobre WAV i .NET extern són referències, no resultats finals de Unity o mòbil.

No tornis a presentar errors d’un snapshot antic com si continuessin presents. No presentis distribució de famílies, alineament amb les pròpies deteccions o proves sintètiques com a demostració de precisió musical en cançons reals.
</pasted_content id="09d1">
