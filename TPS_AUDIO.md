# TempoFurySound — protecció local del catàleg musical

La música del catàleg jugable i les seves còpies per al reproductor natiu s’entreguen xifrades. No s’afegeix cap consulta de llicència, servidor de claus ni connexió necessària per desxifrar. Els originals d’edició es conserven.

## Format i claus

TPS1 utilitza **AES-256-CBC i HMAC-SHA256 en esquema encrypt-then-MAC**. CBC s’utilitza amb IV aleatori nou i padding PKCS#7; la integritat es valida abans del desxifrat. S’utilitzen claus diferents per a AES i HMAC. La capçalera autenticada identifica versió, tipus de contingut, identificador de clau, IV i longitud original; el contingut acaba amb una etiqueta HMAC de 32 bytes. Es rebutgen claus incorrectes, canvis de tipus, fitxers truncats i dades modificades.

`Assets/_Project/Resources/TpsKeyring.bytes` conserva l’arrel local necessària per al funcionament offline. Les claus de contingut es deriven amb HMAC i etiquetes separades a partir de l’arrel i de la identitat de cada payload. La derivació permet afegir contingut compatible sense obtenir claus d’un servidor.

**Conservar aquesta arrel entre versions i còpies del projecte.** No generar-la de nou per canviar una cançó. Si falta i ja existeix contingut TPS, el bake s’atura i demana restaurar-la.

És protecció contra extreure/copiar directament els fitxers d’àudio, no DRM invulnerable. L’aplicació offline porta el necessari per desxifrar; una persona amb prou coneixements pot recuperar claus o mostres en memòria. La clau local no s’ha de considerar un secret de servidor. La simulació de compres i els permisos premium són independents d’aquest xifrat.

## Àudio de Unity

El provider `TpsBundleProvider` obre els bundles musicals protegits en memòria. L’AudioClip intern conserva el format, les mostres, els canals i la freqüència de la build habitual: no es recodifica el runner ni es reanalitza la música per afegir el xifrat.

El provider s’aplica als grups `Remote_Songs` i `Remote_JukeboxAudio`, i als grups d’actualització que continguin les seves entrades. Els fitxers de sortida protegits es diuen `.tps`. El catàleg manté les identitats de càrrega i el provider resol la ruta corresponent. La memòria cau pròpia conserva només bytes TPS, evita reutilitzar la memòria cau antiga de bundles oberts i permet treballar offline. El gestor de descàrregues té en compte aquesta memòria cau en calcular mides i en eliminar contingut.

La protecció del catàleg no converteix automàticament altres àudios que utilitzin circuits diferents, com clips directes dels rivals o efectes de so. La validació de build impedeix que les cançons del catàleg jugable es filtrin com a dependències directes de Resources o de les escenes del player.

## Reproductor natiu

Les 22 còpies generades `song_NNN.m4a.bytes` s’han convertit a `song_NNN.tps.bytes`, conservant-ne els GUID i les identitats Addressables. El payload és el mateix AAC/M4A que ja existia, ara xifrat. `.bytes` és l’extensió de transport per a Unity; al dispositiu es desa com `.tps`.

- Android valida i desxifra en memòria i proporciona dades al MediaPlayer amb `MediaDataSource`, amb suport de lectures i posicionament.
- iOS valida i desxifra en memòria i entrega les dades a AVAudioPlayer amb `initWithData`.
- Els buffers de lectura que controlem es netegen en tancar-los. No es reconstrueix un M4A desprotegit al disc.
- La preparació valida els àudios en un worker i només transmet les claus derivades necessàries a la cua nativa, sense desar-les al fitxer de cua.
- Els imports de l’usuari mantenen el seu circuit local existent; aquests fitxers no són cançons originals distribuïdes pel joc.

## Construcció i distribució

1. **Tools → MusicGame → Content → Songs → Setup TPS Encryption** configura els providers i comprova les claus.
2. **Bake Jukebox Background Audio** genera els nous àudios natius xifrats quan canvia la música. Els temporals d’edició s’eliminen després de xifrar.
3. Construir el contingut amb les eines normals de Release/Content Update. Un hook transforma les sortides musicals en `.tps` i elimina les còpies obertes d’aquella build. Les sortides musicals antigues en els mateixos directoris es retiren a `Library/TpsLegacyBuildOutputs`, que no s’ha de distribuir.
4. Distribuir el player i el catàleg de contingut compatibles amb TPS, juntament amb els `.tps` resultants. No reutilitzar un catàleg antic que indiqui el provider habitual, ni publicar antics `.bundle` musicals sense xifrar.

La prova de build s’ha executat amb un bundle real de validació, no amb una reconstrucció completa de tots els assets i plataformes. Cal regenerar el contingut de producció abans de distribuir aquesta versió. En Editor/Fast Mode és normal que s’utilitzin els originals de treball; això no prova la protecció de la build.

## Comprovacions

**Tools → MusicGame → Content → Songs → Verify TPS Encryption** comprova:

- Les còpies natives xifrades del catàleg i l’absència dels antics M4A generats.
- El backend criptogràfic real de Unity, inclòs el rebuig de manipulacions.
- Una build d’AudioClip real, el hook de xifrat i la càrrega del `.tps` amb el provider real.
- Igualtat exacta de les mostres PCM abans i després del xifrat.
- Lectura offline amb un URL remot inaccessible i memòria cau xifrada ja disponible.
- Rebuig i eliminació d’una memòria cau manipulada.

Resultat: `MusicGame/Logs/TpsValidation.txt`. També s’han executat comprovacions independents de padding, integritat i claus, i interoperabilitat C# → lector Java real d’Android. La compilació nativa d’Android s’ha comprovat amb el SDK; iOS i el comportament físic de segon pla requereixen proves en els seus dispositius abans de publicar.
