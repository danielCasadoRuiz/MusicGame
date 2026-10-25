

<pasted_content id="09d1">
Implementa una millora completa de l’anàlisi musical automàtica de MusicGame, de la seva connexió amb els bonus i de la distribució de perfils preanalitzats amb Addressables.

Projecte:
C:\Users\VISYON\Documents\MusicGame\MusicGame

Llegeix primer les instruccions del repositori i verifica el codi actual abans de modificar-lo. Implementa els canvis, valida’ls i documenta els resultats. No et quedis només en una proposta.

## Objectiu i abast

L’usuari no té coneixements musicals i no vol marcar manualment les cançons. El sistema ha d’interpretar-les automàticament tan bé com sigui possible.

Actualment les cançons són pròpies i formen un catàleg controlat. Totes han de publicar-se ja analitzades, amb el perfil distribuït juntament amb l’àudio mitjançant Addressables remots. El jugador no ha d’analitzar-les la primera vegada que les selecciona.

Conserva també la capacitat d’analitzar cançons noves o importades en el futur. Utilitza el mateix pipeline per preparar el catàleg i per analitzar importacions; evita ajustos particulars codificats per cançó.

La referència perceptiva és Minim BeatDetect de Processing: reaccions clares als atacs musicals, amb adaptació al context recent. No cal copiar la llibreria Java ni prometre identificació perfecta d’instruments en una barreja.

Entorn observat: Unity 6000.3.2f1 i com.unity.ai.inference 2.6.1. Verifica les versions instal·lades. Destinacions: mòbil i ordinador; WebGL no és requisit.

No implementis ara:

- Editora de marcadors o flux d’anotació manual.
- Mapes jugables escrits a mà.
- Integracions amb Spotify.
- Serveis de pagament o processament remot obligatori.
- Generació incremental del nivell durant la reproducció.
- Un nou sistema de precàrrega de futures cançons durant la partida.

## 1. Audita i preserva la sincronització existent

Revisa especialment:

- Gameplay/MusicClock.cs
- Gameplay/GameplayTimeline.cs
- Gameplay/GameplayManager.cs
- Gameplay/RingController.cs
- Audio/Core/AudioAnalyzer.cs i AudioSampler.cs
- Audio/Detectors/
- Core/AudioSystemBootstrapper.cs
- UI/SongAnalysisController.cs
- Audio/PreAnalysis/
- Audio/SemanticTagging/SentisMusicTagger.cs
- Song/AddressableSongSource.cs
- Song/PlayableSongCatalogSO.cs
- Editor/DevTools/SongAddressablesSetup.cs
- Editor/DevTools/SongSimilarityBake.cs

Localitza els fitxers si alguna ruta ha canviat.

Tot el joc està governat per la cançó. MusicClock és l’autoritat temporal per a jugador, càmera, món, checkpoints i bonus.

GameplayTimeline no és un error pel fet de programar esdeveniments anticipadament. L’objectiu és millorar les dades musicals que l’alimenten, preservant aquest disseny.

Documenta i comprova les conversions entre:

- Temps del clip i de SongProfile, sense warmup.
- Temps de partida de MusicClock, amb warmup.
- Temps de captura de l’anàlisi en directe.
- Aparició anticipada, revelació visual, pulsació musical i recollida.

Comprova pausa, seek, respawn, reinici, reproducció des d’un punt intermedi i final de cançó. No introdueixis rellotges independents ni substitueixis MusicClock sense demostrar una necessitat.

Problema concret observat: AudioAnalyzer assigna Time.time a data.timestamp, però diversos detectors utilitzen aquest valor per consultar SongProfile. Verifica i corregeix aquesta barreja de dominis.

No substitueixis mecànicament Time.time per MusicClock.SongTime: les consultes al perfil necessiten temps del clip, sense warmup. Defineix explícitament el significat dels timestamps i adapta els consumidors.

Reinicia historials, cooldowns i estat davant discontinuïtats. Comprova també que l’estat anterior dels detectors s’actualitza correctament durant els cooldowns.

Els detectors en directe actualment serveixen principalment al visualitzador de depuració. Verifica els consumidors i no els atribueixis el comportament dels bonus de producció sense evidència.

## 2. Millora la detecció musical automàtica

Actualment AudioPreAnalyzer detecta onsets amb flux espectral global i GameplayTimeline.ClassifyOnset assigna una única categoria utilitzant centroid, flatness i energia de bandes.

Millora aquest enfocament perquè detecti atacs locals, encara que coincideixin amb altres instruments.

Implementa una solució proporcionada que inclogui:

- Detecció d’atacs per bandes o grups de bandes.
- Canvi positiu d’energia o espectre, evitant confondre energia sostinguda amb cops repetits.
- Normalització i llindars adaptats al context recent.
- Terra de soroll, selecció de pics i supressió de duplicats.
- Interval mínim configurable per família, conservant patrons ràpids de hi-hat.
- Possibilitat de detectar activitat greu i aguda simultània.
- Rangs de freqüència configurables en Hz.

Separa tres decisions:

1. Hi ha evidència d’un atac?
2. A quina família o instrument podria correspondre?
3. Convé seleccionar-lo per al gameplay?

No classifiquis qualsevol greu com a kick ni qualsevol atac brillant com a hi-hat. Quan l’evidència sigui ambigua, conserva un onset genèric.

Cada esdeveniment ha de conservar temps, força, tipus o família, origen i confiança heurística. No presentis aquesta confiança com una probabilitat calibrada.

No imposis una única etiqueta excloent per instant. Tampoc afirmis que detectar activitat simultània equival a separar amb certesa els instruments.

Revisa la resolució temporal i freqüencial de la configuració actual. Justifica mida de finestra i hop segons resultats, especialment per a kick i hi-hat.

Comprova les convencions de mida, freqüències dels bins i normalització: GetSpectrumData i la FFT offline no tenen necessàriament la mateixa representació.

Mesura l’alineament temporal amb atacs de temps conegut. Revisa l’efecte de la finestra FFT, la selecció del pic i la suavització abans d’introduir compensacions.

Prioritza precisió dels atacs i sincronització abans d’afegir descriptors generals o models nous. Si proposes una dependència gratuïta, comprova llicència, manteniment, compatibilitat amb Unity/IL2CPP i cost a mòbil. Justifica que millora la solució existent.

No ampliïs aquesta tasca amb reconeixement específic de piano, trompeta o altres instruments. Prioritza kick, snare, hi-hat i onsets genèrics fiables.

## 3. Connecta les deteccions amb els bonus

Mantén el flux:

anàlisi offline o perfil publicat → SongProfile → GameplayTimeline → MusicClock.

Preserva els cops detectats encara que el gameplay només en seleccioni alguns per dificultat o accessibilitat. No eliminis informació musical només perquè no podem generar un objecte per cada cop.

Revisa com es redueixen, prioritzen i espacien els candidats. L’objectiu és que els bonus seleccionats coincideixin amb atacs audibles, evitant saturar el nivell o perdre patrons importants.

Revisa tots els consumidors de BeatPulseEvent abans de canviar-ne el contracte. Evita duplicar efectes de càmera, puntuació o pulsacions quan:

- Coincideixen diverses deteccions.
- Un cop produeix un bonus i una reacció ambiental.
- Conviuen detectors en directe i esdeveniments offline.

Preserva la diferència entre aparició anticipada, revelació espacial i pulsació musical. Revisa pulseLeadTime abans d’afegir altres offsets.

Afegeix opcions de diagnòstic, sense construir una editora manual:

- Desactivar el farciment de graella BPM.
- Comparar deteccions, bonus seleccionats i pulsacions executades.
- Distingir deteccions reals i esdeveniments sintètics.
- Activar clics o indicadors temporals de prova, desactivats en ús normal.

La graella actual genera intervals a partir d’un BPM estimat. No assumeixis que aquests instants coincideixen amb cops reals ni amb la fase musical. Mantén el farciment configurable i avalua el resultat sense ell. Si millores el beat tracking, valida també la fase.

## 4. Mesura i redueix el cost d’anàlisi i càrrega

L’usuari observa aproximadament deu segons d’espera. Mesura abans d’atribuir-ne el cost.

Instrumenta per separat:

- Descàrrega i càrrega del contingut.
- Descodificació i lectura PCM.
- Conversió a mono.
- Preprocessament del model.
- Inferència semàntica.
- FFT rítmica i features.
- Detecció i classificació.
- Lectura, deserialització i escriptura de perfils.
- Reconstrucció de dades derivades.
- Generació del món.
- Esperes i transicions visuals.

Compara:

- Anàlisi nova d’una cançó importada.
- Càrrega de la seva cache local.
- Càrrega d’una cançó del catàleg amb perfil publicat.
- Contingut remot pendent de descarregar i contingut ja descarregat.

Mesura temps total, bloquejos del fil principal, assignacions i memòria màxima.

Punts observats a verificar:

- El tagging semàntic s’executa abans de la FFT rítmica.
- OfflineFFT recalcula finestres i crea buffers a cada crida.
- SentisMusicTagger utilitza CPU i DownloadToArray després de Schedule.
- AudioPreAnalyzer imposa dos segons artificials en cache hit.

Precalcula finestres i reutilitza buffers de manera segura. Elimina esperes artificials obligatòries del camí crític, preservant una transició visual coherent.

No presentis una càrrega de perfil com si s’estigués analitzant àudio. Mostra l’estat real i diferencia temps de càlcul, descàrrega i presentació.

No redueixis qualitat ni eliminis funcions existents silenciosament per obtenir una xifra millor.

## 5. Implementa segon pla i paral·lelisme segur

Les coroutines actuals no impliquen execució en paral·lel.

Treu del fil principal els càlculs purs sobre mostres i arrays. Tria entre tasques de fons o Jobs/Burst segons cost, compatibilitat i mesures; evita una reescriptura innecessària.

Captura al fil principal les dades necessàries de Unity i una còpia immutable de la configuració. Els workers no han d’accedir a AudioSource, ScriptableObjects, GameObjects ni publicar esdeveniments de gameplay.

Les FFT de finestres independents poden paral·lelitzar-se. Les operacions amb dependències temporals, com flux entre finestres i historials adaptatius, s’han d’executar en ordre o amb dependències explícites.

Evita buffers mutables compartits i tasques excessivament petites. Limita concurrència i memòria per no perjudicar els mòbils.

Solapa la branca rítmica i la semàntica quan sigui segur i les mesures ho justifiquin. Consulta les API de la versió instal·lada d’Inference Engine: no moguis Worker i tensors indiscriminadament a Task.Run. Evita lectures bloquejants quan existeixi una alternativa asíncrona compatible i preserva la vida útil dels recursos.

Implementa:

- Progrés real.
- Cancel·lació en canviar de cançó o sortir de l’estat.
- Identificador de petició per descartar resultats antics.
- Prevenció d’anàlisis i càrregues duplicades.
- Alliberament segur de recursos en èxit, cancel·lació o error.
- Publicació dels resultats al fil principal.

Les branques no han de modificar simultàniament el mateix SongProfile. Publica el perfil complet només quan sigui coherent.

Preserva la resolució d’estil i la transició de tema, inclosa la publicació única de l’estil final.

La partida ha de continuar esperant el perfil necessari per construir el món. No iniciïs gameplay amb un perfil incomplet.

## 6. Preanalitza tot el catàleg a l’Editor

Totes les cançons jugables del catàleg propi han de tenir un perfil complet generat abans de publicar el contingut.

Implementa una eina d’Editor per:

- Analitzar totes les cançons jugables.
- Regenerar només perfils absents o obsolets.
- Forçar una regeneració completa.
- Mostrar progrés i permetre cancel·lació.
- Informar de cançons processades, reutilitzades i fallides.
- Validar que tot el catàleg publicable té perfils compatibles.

Utilitza el mateix pipeline millorat que serveix per a cançons importades.

Aprofita les eines existents quan tingui sentit. SongSimilarityBake genera vectors de similitud; no assumeixis que aquests contenen totes les dades necessàries per jugar.

Genera assets de contingut en una ubicació adequada i versionable del projecte. La cache personal de l’Editor i persistentDataPath/SongCache no han de ser la font de veritat del contingut publicat.

Evita anàlisis duplicades si el mateix processament pot proporcionar perfil complet i dades de similitud.

## 7. Defineix un perfil publicat complet i eficient

Audita els consumidors de SongProfile abans de decidir què serialitzar.

Inclou les dades necessàries per al funcionament actual:

- Deteccions rítmiques, temps, forces i classificacions.
- Envelopes i bandes utilitzades pel món i els visuals.
- Estructura i altres features consumides pel joc.
- Tags semàntics.
- Dades derivades necessàries per evitar repetir càlcul costós.
- Metadades d’identitat i compatibilitat.

No publiquis només una llista de kicks si el món necessita altres arrays.

Prioritza un perfil llest per consumir. Si alguna reconstrucció lleugera continua fent-se en carregar-lo, justifica-la i mesura’n el cost. Amb un perfil publicat vàlid no s’han d’executar FFT ni inferència semàntica.

Preserva la distinció entre dades crues i derivades. Evita aplicar dues vegades suavitzacions o transformacions in-place en desar i carregar. No permetis que una partida modifiqui accidentalment l’asset compartit i afecti la següent.

Mesura la mida real. Hi ha dades per fotograma, bandes visuals i chroma; no assumeixis que són només uns quants valors.

Tria un format adequat per a Unity. Compara alternatives compactes si el volum ho justifica. Evita JSON amb formatació innecessària, duplicació de dades i compressió redundant.

No introdueixis quantització o pèrdua de precisió sense validar l’impacte en visuals i sincronització.

## 8. Distribueix àudio i perfil amb Addressables remots

Les cançons i els seus perfils han de distribuir-se com a contingut Addressable remot, separat del player build.

Verifica els grups i les rutes existents: marcar un asset com Addressable no garanteix que sigui remot.

Associa cada perfil amb la cançó mitjançant songId estable i referències o claus explícites. No depenguis del nom visible ni d’una convenció de noms fràgil.

Àudio i perfil han de formar una unitat coherent de contingut publicat. Tria i justifica si comparteixen bundle o si són assets vinculats en bundles separats.

Evita un bundle monolític que obligui a descarregar tot el catàleg per jugar una cançó. No carreguis totes les cançons en RAM a l’inici: “totes preanalitzades” significa preparades abans de publicar.

Evita referències directes des de scenes o Resources que incorporin accidentalment els àudios o perfils complets al player build. Un índex lleuger pot formar part del player si és compatible amb les actualitzacions remotes.

Revisa que descobrir noves cançons i resoldre els seus songId i perfils continuï funcionant amb actualitzacions de contingut. No assumeixis que un índex estàtic dins del player coneixerà cançons publicades posteriorment.

El procés ha de quedar així:

afegir o actualitzar cançó → preparar Addressables → generar perfil → validar → construir contingut remot → publicar.

Integra una validació abans de construir contingut publicable que detecti perfils absents, obsolets o associats a un altre àudio. No publiquis silenciosament un catàleg incomplet.

No pugis contingut a serveis externs ni modifiquis credencials de hosting sense autorització explícita. Deixa els assets i la configuració preparats i explica el pas de publicació pendent si no es pot executar.

## 9. Adapta la càrrega al joc

Per a cançons del catàleg:

- Carrega àudio i perfil asíncronament, solapant operacions quan sigui possible.
- Valida correspondència i compatibilitat.
- Entrega el perfil al flux existent de GameSession, estil, tema i gameplay.
- Evita executar l’analitzador o simular una anàlisi nova.
- Mantén els esdeveniments necessaris sense duplicar publicacions ni transicions.
- Gestiona seleccions consecutives, cancel·lació i alliberament dels handles Addressables.

Revisa especialment la vida útil dels handles actuals d’AddressableSongSource. No alliberis l’àudio mentre l’utilitzin el preview o la partida, ni mantinguis indefinidament recursos de seleccions anteriors.

La descàrrega, descodificació i generació del món encara poden requerir temps. Mostra’l correctament i no prometis càrrega instantània.

Si un perfil falta o és incompatible:

- Diferencia error de descàrrega i contingut invàlid.
- Ofereix una recuperació explícita adequada.
- No encobreixis un error de publicació amb una anàlisi costosa silenciosa.
- Evita bloquejos indefinits o iniciar una partida amb dades incorrectes.

Per a cançons importades per l’usuari, conserva anàlisi dinàmica i cache local com a via independent.

Els bundles remots descarregats poden estar en la cache d’Addressables. Si s’esborra, pot caldre descarregar-los de nou, però no tornar a analitzar la cançó: el perfil continua existint com a contingut remot publicat.

No implementis un sistema de persistència alternatiu només per intentar fer inesborrable aquesta cache.

## 10. Identitat, versionat i actualitzacions

Inclou al perfil i al procés de validació:

- songId estable.
- Identitat o hash de la revisió d’àudio.
- Imports rellevants i relació amb les variants de plataforma.
- Versió del format.
- Versió del pipeline.
- Paràmetres que afecten els resultats.
- Versió del model semàntic quan correspongui.

No depenguis només del nom i nombre de mostres, perquè poden col·lidir.

Evita combinar àudio nou amb un perfil vell durant actualitzacions remotes. Els perfils han de ser compatibles amb la versió del joc que els consumeix.

Una modificació de l’àudio, del detector o d’una configuració rellevant ha de marcar els perfils afectats com a obsolets i permetre regenerar-los abans de publicar.

Preserva els tags semàntics vàlids quan només canviï la detecció rítmica, sempre que el versionat permeti reutilitzar-los amb seguretat.

No desis perfils parcials o resultats de peticions cancel·lades. Publica els assets generats només quan l’anàlisi hagi acabat correctament.

## 11. Validació musical, temporal i de concurrència

Afegeix proves significatives amb senyals sintètics de temps conegut:

- Atacs greus, soroll percussiu mitjà i ràfegues agudes.
- Greu i agut simultanis.
- Baix sostingut.
- Silenci i canvis de volum.
- Patrons ràpids.
- Diferents freqüències de mostreig.
- Àudio curt i casos límit de finestres.

Les proves sintètiques verifiquen transitoris i temps; no demostren identificació perfecta d’instruments reals.

Compara fragments de diverses cançons disponibles al projecte. Facilita una prova A/B amb indicadors o clics opcionals perquè l’usuari pugui valorar la correspondència sense saber música ni anotar cançons.

Mesura omissions, falsos positius i error temporal quan hi hagi una referència coneguda. No inventis mètriques de precisió per fragments sense anotacions.

Valida:

- Pausa i represa.
- Seek i respawn.
- Reinici.
- Reproducció des d’un punt intermedi.
- Final de cançó.
- Seleccions ràpides i cancel·lació.
- Absència de dobles efectes.
- Equivalència entre execució seqüencial i paral·lela dins d’una tolerància definida.
- Alliberament de recursos després d’errors o cancel·lacions.

## 12. Validació de perfils publicats i Addressables

Comprova:

- Instal·lació neta sense SongCache local: una cançó del catàleg utilitza el perfil publicat sense FFT ni inferència.
- Esborrar la cache local d’anàlisi no afecta els perfils publicats.
- Esborrar bundles descarregats pot requerir una nova descàrrega, però no recalcular l’anàlisi.
- Perfil absent, corrupte, incompatible o corresponent a una altra revisió d’àudio.
- Actualització remota coherent de cançó i perfil.
- Incorporació d’una nova cançó al catàleg remot.
- Selecció ràpida de cançons i alliberament de handles.
- Cançó importada: continua funcionant amb anàlisi dinàmica.
- Funcionament amb bundles construïts i contingut remot de prova, no només amb Asset Database de l’Editor.
- Absència d’incorporació accidental dels àudios i perfils remots al player build.

Executa les comprovacions de compilació i proves adequades. Compara rendiment abans/després amb les mateixes cançons i configuració.

## Entrega

Explica:

- Què has implementat i per què.
- Quins problemes temporals has confirmat realment.
- Com alimenten les deteccions el gameplay.
- Com es preparen totes les cançons abans de publicar.
- Com s’afegeix o s’actualitza una cançó.
- Com es vinculen àudio, perfil i catàleg remot.
- Rendiment abans/després i condicions de mesura.
- Mida dels perfils per cançó i total del catàleg, inclosa mida distribuïda.
- Temps de càrrega, deserialització i memòria màxima.
- Valors predeterminats recomanats.
- Passos senzills per provar la correspondència musical sense coneixements de música.
- Limitacions, publicació pendent i proves pendents en dispositius reals.

Si no pots executar Unity, construir bundles o provar un dispositiu, indica exactament què has verificat i què queda pendent.

No presentis com a mesurades millores de velocitat, qualitat, mida o sincronització que no hagis comprovat.
</pasted_content id="09d1">
