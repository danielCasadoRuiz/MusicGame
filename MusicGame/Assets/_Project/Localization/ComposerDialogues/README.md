# Diàlegs dels compositors

## On són els textos

- Una String Table Collection per compositor: `ComposerDialogues_{id}`, a `Assets/_Project/Localization/ComposerDialogues/`. Cada una té català i anglès.
- Claus: `Composer.{id}.Dialogue.{Intro|Defeat|Victory}.{índex}`. L'índex té 3 xifres o més (`001`, `012`, `1000`) i pot tenir forats.
  - `Intro`: abans del Versus.
  - `Defeat`: perd el compositor (el jugador guanya).
  - `Victory`: guanya el compositor (el jugador perd).
- `UIText` només conté la interfície general (inclou les biografies `Composer.{id}.Bio`).
- Les frases s'editen a **Window > Asset Management > Localization Tables**, triant `ComposerDialogues_{id}`.

El joc descobreix les variants que hi ha a la taula carregada. No hi ha cap recompte al codi, així que afegir o treure files no requereix tocar codi.

## Com corregir, afegir o retirar frases

| Vull… | Què faig a la taula |
|---|---|
| Corregir una frase | Canvio el text de la cel·la (ca o en). La clau no es toca. |
| Afegir una frase | Afegeixo una fila nova amb una clau del mateix patró i un índex no usat (p. ex. `Composer.bach.Dialogue.Intro.011`), amb text en **els dos** idiomes. |
| Retirar una frase | Esborro la fila. Per retirar-la només en un idioma, deixo buida aquella cel·la: el joc la ignora en aquell idioma. |
| Retirar-les totes d'una categoria | No es recomana. Si passa, el joc fa servir l'altre idioma i, si tampoc n'hi ha, continua sense diàleg. |

Política d'errors (en temps d'execució):
- Claus mal formades dins la taula: s'ignoren i surt un avís al log.
- Text buit: aquella variant no existeix en aquell idioma.
- Cap variant en l'idioma actiu: es fa servir l'idioma de reserva (anglès) i surt un avís.
- Cap variant enlloc, error de càrrega o temps d'espera esgotat: es registra un error i el flux continua sense diàleg. El Versus torna al temporitzador `versusDuration` i la pantalla de resultat habilita els botons.

Després d'editar, comprova-ho amb **Tools > MusicGame > Content > Composer Dialogues > Validate**. Si has afegit o tret frases, avisarà que ja no són 10 per categoria; això és informatiu, no un error.

## Publicar-ho sense actualitzar l'app

Requisit: l'app publicada s'ha generat amb **Tools > MusicGame > Content > Release > Build Release Content (archive state)** i amb un perfil d'Addressables que apunta al servidor real. Aquest pas guarda `ContentState/<plataforma>/<versió>/addressables_content_state.bin`. Cal fer commit d'aquest fitxer i conservar-lo mentre aquella versió de l'app tingui usuaris.

1. Edita les taules i valida-les.
2. Selecciona el mateix perfil d'Addressables que es va fer servir per a la release.
3. Executa **Tools > MusicGame > Content > Release > Build Content Update (for current version)**. `PlayerSettings.bundleVersion` ha de ser la versió publicada. L'eina:
   - compara el projecte amb l'estat guardat;
   - mou només les taules canviades a un grup remot `Content Update <versió>` (un paquet per taula);
   - genera el catàleg i els paquets nous a `ServerData/<plataforma>`.
4. Puja tot el contingut de `ServerData/<plataforma>` al servidor.
5. L'app instal·lada ho rep en la propera comprovació de catàleg: a l'arrencada, o en tornar al menú principal si han passat `catalogRecheckMinutes`. Mai durant un combat ni un diàleg. Descarrega només les taules canviades i les guarda per jugar sense connexió.

Una correcció d'una frase només baixa la taula d'aquell compositor i idioma. Afegir o esborrar files també baixa les dades compartides i les dues taules d'aquell compositor.

### Abans de la següent versió de l'app (release nova)
Executa **Composer Dialogues > Configure Addressables**. Torna a moure les taules dels grups `Content Update` al grup local (perquè la nova instal·lació les porti de sèrie) i esborra els grups buits. Després fes una release nova.

## Què és compatible i què requereix una versió nova de l'app

**Actualització de contingut (sense nova versió):**
- Corregir, afegir o retirar frases de compositors existents.
- Canviar textos de `UIText`. Va pel mateix mecanisme, però aquells textos els llegeix `Loc.Get` de manera síncrona; per tant, s'ha de comprovar amb la release que els tingui en un paquet ja descarregat.

**Requereix una versió nova de l'app:**
- Un compositor nou (necessita la seva `OpponentDefinition`, el roster, models…), o canviar-ne l'`id`.
- Un idioma nou (Locale).
- Categories noves (són un `enum` al codi) o canvis de format de clau.
- Canvis de codi o de configuració (`FightFlowConfig`, temps del typewriter, etc.).
- Canvis a la configuració d'Addressables (perfils, adreça del servidor, grups).

## Configuració
- `FightFlowConfig` (`AppConfig > fightFlow`), secció *Composer dialogues*: `composerDialoguesEnabled`, `dialogueCharactersPerSecond`, `dialoguePunctuationPauseSeconds`, `dialogueMinContinueDelaySeconds`, `versusDialogueAutoContinueSeconds`, `dialogueLoadTimeoutSeconds`. `versusDuration` ara només s'aplica quan no hi ha diàleg.
- `ContentDeliveryConfig`: `checkCatalogUpdatesOnStart`, `catalogRecheckMinutes`.
- Addressables:
  - grup `Localization-ComposerDialogues` (local, *Cannot Change Post Release*, un paquet per taula, etiqueta `ComposerDialogues`);
  - regles de grup de Localization a `Assets/_Project/Localization/LocalizationAddressableGroupRules.asset`;
  - perfils `DevelopmentLocal` (tot local), `LocalContentServer` (proves, `http://localhost:8765`) i `ProductionCCD` (pendent de dades del servidor).

## Veus (àudio de les frases)

- **D'on surten.** Cada frase pot tenir àudio generat. Està a `Assets/_Project/Content/Voices/{id}/{idioma}/` i es genera amb **Tools > MusicGame > Audio > Voice Production** (instal·lació: `Tools/VoiceGen/README.md`).
- **Paquets.** Hi ha un paquet d'Addressables per compositor i idioma: grup `Voice_{id}_{idioma}`, adreça `VoicePack/{id}/{idioma}`. Les vocalitzacions (cops, especial, derrota) van al paquet `fx`.
- **Text i àudio.** Cada clip guarda el hash del text del qual s'ha generat.
  - Si corregeixes una frase i publiques només el text, el joc mostra el subtítol nou i no reprodueix l'àudio antic.
  - L'eina marca aquella frase com a pendent. Quan la regeneres, només canvia el paquet `Voice_{id}_{idioma}`; publica'l amb el mateix *Build Content Update*.
- **Català.** Encara no hi ha motor de veu: les frases en català es mostren només com a subtítol.
- **Si falta un àudio** (paquet no descarregat, frase nova sense veu, error), la frase es mostra igualment i el joc continua.
