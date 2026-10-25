# Precisions de l'usuari per a la FASE 3 (2026-10-17) — substitueixen l'orientació anterior per a aquesta fase

Continua amb la fase 3 de MusicGame incorporant aquestes precisions. Aquest missatge substitueix l’orientació anterior per a aquesta fase.

OBJECTIU

Totes les cançons del catàleg han de publicar-se amb l’anàlisi ja preparada i carregar-se mitjançant Addressables remots, sense executar FFT ni inferència al dispositiu.

La partida i la reproducció musical només poden començar quan totes les dependències necessàries estiguin preparades. La rapidesa de càrrega no justifica avançar l’inici ni introduir race conditions.

1. PREANÀLISI DEL CATÀLEG

Implementa el bake a l’Editor amb el mateix pipeline d’anàlisi validat:

- Processar totes les cançons jugables.
- Reutilitzar perfils compatibles i regenerar els absents o obsolets.
- Permetre reconstrucció completa, progrés i cancel·lació.
- Informar d’errors i impedir publicar contingut incomplet o incompatible.
- Generar assets versionables del projecte, sense dependre de la cache personal del desenvolupador.

El perfil musical és independent del nivell i la dificultat. Conserva les deteccions, forces, famílies, beats, estructura, estil i arrays necessaris per al món i els visuals.

No fixis una distribució única de bonus dins d’aquest perfil.

2. ÀUDIO D’ANÀLISI I ÀUDIO DE REPRODUCCIÓ

Conserva el màster WAV sense pèrdues per preparar l’anàlisi i distribueix una representació comprimida adequada per jugar.

No converteixis automàticament els WAV a MP3. Unity ja comprimeix els clips segons els imports: compara mida real dels bundles, qualitat, CPU, memòria i seek abans de triar la configuració.

Evita doble compressió amb pèrdues i no sobreescriguis els màsters.

Valida l’alineament entre màster i representació de reproducció:

- Principi, mig i final.
- Retard inicial, padding, retalls i resampling.
- Reproducció des d’un punt intermedi, pausa, respawn i reinici.
- Final natural de cançó.

La mateixa durada no garanteix alineament. Si cal una conversió temporal, ha de ser explícita, mesurada i vinculada a la revisió corresponent. No introdueixis offsets manuals arbitraris ni compensis dues vegades el detector i la representació comprimida.

Vincula el perfil amb la identitat del màster, la representació de reproducció, les versions d’anàlisi i les configuracions d’importació/plataforma rellevants.

No descodifiquis tota la cançó al dispositiu només per recalcular-ne un hash.

3. ADDRESSABLES REMOTS

Distribueix àudio i perfil com a contingut remot coherent, identificat per songId estable i revisió.

- Migra el grup Songs actualment local.
- Evita un bundle monolític de tot el catàleg.
- Tria i justifica àudio i perfil en un bundle per cançó o en bundles vinculats.
- Comprova el layout construït, no només els ajustos dels grups.
- Evita incorporar accidentalment àudios, màsters o perfils complets al player build.
- No carreguis tot el catàleg en RAM.

El catàleg remot ha de permetre incorporar cançons noves amb identitat, progressió i dades de similitud. No depenguis exclusivament de la taula estàtica de cançons inclosa al player.

Valida abans de construir contingut que cada cançó disposa d’un perfil compatible amb l’àudio, el pipeline, el format i la configuració.

No publiquis a serveis externs ni modifiquis credencials sense autorització explícita.

4. BARRERA DE PREPARACIÓ ABANS DE COMENÇAR

Aquest és un requisit explícit: abans d’iniciar el compte enrere jugable o la música, cal haver completat la preparació necessària.

Audita el flux real i espera, segons correspongui:

- Inicialització del catàleg i resolució de la selecció.
- Descàrrega dels bundles necessaris.
- Càrrega i validació de l’àudio i del perfil.
- Preparació del clip per a reproducció.
- Reconstrucció lleugera de dades derivades, si existeix.
- Càrrega de l’escena i dels assets imprescindibles.
- Generació del món i de la timeline que necessita el flux actual.
- Preparació dels objectes, pools i consumidors musicals.
- Transició de tema necessària per mostrar correctament la partida.

Adapta aquesta barrera a l’arquitectura existent; no creïs un segon sistema de preparació paral·lel.

No pressuposis que:
- Acabar una descàrrega equival a tenir l’àudio preparat.
- Rebre el perfil significa que el món ja està construït.
- Disparar un callback significa que tots els consumidors han acabat.
- Esperar un nombre fix de segons substitueix comprovar l’estat real.

Utilitza estats i finalitzacions explícits. Les operacions independents poden solapar-se, però l’inici depèn que totes les obligatòries hagin acabat correctament.

La música no pot començar mentre encara s’estan preparant visuals o dades que necessiten reaccionar des del primer instant.

No permetis que MusicClock avanci la partida durant una espera tècnica prèvia. Preserva el warmup com a fase de joc coherent, no com un marge suposat per acabar càrregues.

Amb Streaming, no cal carregar tot el PCM, però sí garantir les condicions d’inici requerides per la política de reproducció i validar possibles interrupcions.

Mostra l’estat real: descarregant, carregant o preparant. No afegeixis esperes artificials ni una barra de progrés inventada.

5. INICIALITZACIÓ I PAUSA DE PRIMER ÚS

Investiga el cost inicial d’uns 350 ms sense ocultar-lo ni traslladar-lo a l’inici de la música.

Per al catàleg publicat, no inicialitzis el model semàntic si el perfil ja proporciona l’estil i no és necessari.

Qualsevol recurs que sí sigui necessari per a la partida s’ha de preparar abans de donar-la per llesta. Si es fa preescalfament a l’arrencada, ha de tenir una finalització explícita i el flux dependent ha d’esperar-la.

L’objectiu és evitar bloquejos durant el compte enrere, el primer cop musical o l’inici del moviment perquè s’ha inicialitzat tard un recurs obligatori.

6. CANCEL·LACIÓ, ERRORS I RECURSOS

Mantén una identitat de selecció/petició en tot el flux.

Una operació antiga no pot:
- Publicar el perfil sobre una selecció nova.
- Canviar-ne el tema.
- Marcar-la com a preparada.
- Iniciar música o gameplay.
- Alliberar recursos utilitzats per la selecció vigent.

Gestiona cancel·lació, errors i handles amb propietat explícita. No iniciïs una partida parcialment preparada ni deixis esperes indefinides.

Diferencia errors de descàrrega, càrrega i contingut incompatible, amb recuperació adequada. No executis una anàlisi costosa silenciosa per encobrir un perfil publicat invàlid.

7. INCIDÈNCIA DELS 128 MS

Mantén oberta la incidència de la pulsació executada 128 ms tard.

Registra el temps musical previst, el real, la durada del frame i les operacions concurrents. Comprova si hi ha inicialitzacions o càrregues pendents després de l’inici.

No assumeixis que està relacionada amb els 350 ms sense evidència. No introdueixis un offset general ni augmentis el límit de la prova per fer-la passar.

8. CONTRACTE PER A LA FASE 4

Deixa registrat, sense implementar ara tota la fase 4:

- El perfil musical es prepara una vegada per cançó i revisió.
- La distribució dels bonus depèn de la cançó, el nivell i la partida concreta dins d’aquell nivell.
- La dificultat ha de poder incrementar-se entre partides del mateix nivell; no són només quatre variants.
- La distribució es genera el primer cop que es juga aquella combinació i es desa.
- En repetir la mateixa combinació, es carrega la distribució guardada; no es torna a sortejar ni generar.
- No precalculis ara totes les combinacions possibles.

Una llavor determinista ajuda a reproduir el resultat, però no substitueix guardar la distribució si volem evitar recalcular-la.

La clau de reutilització ha d’incloure les revisions i paràmetres que afecten el resultat: cançó/perfil, posició de campanya, dificultat, generador i geometria/moviment rellevants.

No incloguis el número d’intent. Inclou la volta de campanya només si modifica realment la dificultat.

A la fase 3 prepara un contracte compatible amb això, sense barrejar el perfil musical amb el guardat dels bonus.

9. IMPORTACIÓ DE FITXERS

Probablement retirarem la importació de cançons pròpies. No l’eliminis ara ni hi afegeixis funcionalitat: conserva el que ja funciona i prioritza el catàleg remot preanalitzat.

10. VALIDACIÓ I ENTREGA

Comprova especialment:

- Instal·lació neta sense cache d’anàlisi.
- Contingut pendent de descarregar i ja descarregat.
- Perfil compatible, absent i incompatible.
- Seleccions consecutives i cancel·lació en diferents etapes.
- Descàrrega o càrrega lenta.
- Preparació correcta abans del primer instant musical.
- Inici des d’un punt intermedi, pausa, respawn i final natural.
- Actualització coherent d’àudio, perfil i catàleg.
- Alliberament dels handles.
- Funcionament amb bundles construïts.
- Absència de FFT i inferència al carregar una cançó del catàleg.

Informa del temps de descàrrega, càrrega, preparació i inici per separat, així com de mida i memòria.

Explica quina condició autoritza exactament començar el compte enrere i la reproducció. Indica què has verificat i què queda pendent, especialment en dispositius mòbils.

Torna a comprovar l’espai lliure abans de construir contingut. No esborris dades personals ni altres fitxers sense autorització.
