# Jukebox i Custom Audio

Accés des del menú inicial, amb la mateixa botonera del joc. La biblioteca combina les cançons originals i les importacions locals.

## Comportament

- Una victòria després del runner desbloqueja la cançó. Les estrelles i la lap no canvien aquesta regla.
- La primera victòria determina la posició a la biblioteca. Tornar-la a guanyar no la duplica ni la mou.
- Els desbloquejos existents es migren automàticament conservant l’ordre.
- Només apareixen les cançons guanyades. Tocar una fila inicia directament el preview gratuït, amb el mateix rang que el selector. En entrar a la pantalla no es reprodueix res automàticament.
- Anterior/següent permeten passar manualment entre previews gratuïts, també després que s’acabi un preview. Els previews no avancen automàticament.
- Jukebox Premium habilita cançons completes, reproducció consecutiva automàtica, segon pla i les lletres disponibles de les nostres cançons. La pausa funciona també als previews. No hi ha playlists, favorits ni aleatori.
- La importació es fa exclusivament al selector de cançons existent. Els imports només apareixen al Jukebox després de guanyar el combat; no hi ha accions per importar ni començar partides dins del Jukebox.
- Els reintents des dels resultats del runner o del combat conserven l’àudio i l’anàlisi de la sessió. Tornar al menú principal descarta els imports no guanyats i neteja la selecció. Cal tornar a triar el fitxer al selector; el mateix àudio del tast es pot tornar a importar sense una compra ni un tast nou. Els imports guanyats es conserven.
- La primera importació és gratuïta. Cancel·lar o fallar la descodificació no consumeix el tast. Eliminar-la no concedeix un altre tast.
- Les importacions es copien a l’espai local del joc i sobreviuen als reinicis. No s’envien a cap servidor. Formats: WAV, MP3, OGG i AIFF, sempre que el descodificador de la plataforma els admeti.
- Es poden eliminar imports, amb confirmació. S’elimina la còpia del joc, el seu desbloqueig i la còpia derivada per al reproductor natiu. Mai s’elimina el document original.
- Les cançons originals no tenen opció d’eliminació. Reiniciar el progrés esborra els desbloquejos, però conserva els fitxers importats i el registre del tast consumit.
- Títol, artista, estil i origen a cada fila. Les caràtules i les lletres es mostren quan s’han proporcionat; les lletres només s’admeten per a cançons originals.

## Compres: placeholder

No s’ha integrat cap botiga real. Fora de l’editor, la interfície mostra la compra com a pendent de connexió i no concedeix cap dret. A l’editor, el simulador permet confirmar una compra de prova i comprovar-ne l’efecte immediat. Els preus es mostren explícitament com a previstos, sense càrrecs, només quan es demana una funció de pagament. No hi ha botons generals d’ampliacions ni de restauració al Jukebox. `IMusicStore` és el punt d’entrada per al futur adaptador de compra i restauració amb propietat verificada. Els drets estan separats de la progressió.

Per provar compres a Unity: **Tools → MusicGame → Jukebox → Test Purchases**. La finestra permet activar/desactivar la simulació, marcar les dues compres per separat (Jukebox Premium i Custom Audio), aplicar presets i veure els tres permisos efectius. Els canvis s’apliquen durant Play, també en revocar una compra. L’estat és de la sessió de l’editor, sobreviu a entrar/sortir de Play i no s’inclou a les builds. L’antic menú Preview Premium Features obre ara aquesta finestra.

L’opció de compra apareix en demanar **Cançó completa · Premium**, lletres o **Segon pla**. Segon pla està inclòs a Jukebox Premium i, si falta premium, ofereix aquesta mateixa compra. No hi ha una compra separada de segon pla ni packs. Els preus nous queden pendents de definir. Obrir l’oferta no activa res: cal confirmar la compra simulada o marcar-la a la finestra. No s’afegeixen botons generals de botiga al Jukebox.

## Metadades

`Assets/_Project/Resources/JukeboxMetadata.asset` conté una entrada per songId amb títol, artista, caràtula opcional i lletra opcional. No s’han inventat lletres ni caràtules. L’estil prové de l’índex musical publicat; els imports guarden l’estil detectat en guanyar.

## Segon pla a PC i mòbil

- El permís comercial és el mateix a PC i mòbil: requereix Jukebox Premium, que ja inclou segon pla. Sense el permís i el mode activat, perdre el focus o suspendre l’aplicació pausa el reproductor; recuperar el focus no engega la música per sorpresa.
- PC i editor: el reproductor continua en perdre el focus quan s’activa Segon pla. Es conserva i restaura el valor anterior de `Application.runInBackground` en desactivar-lo, sortir o acabar la cua. Tancar el joc acaba la reproducció: no s’instal·la un reproductor extern al procés.
- Android: servei privat de reproducció amb MediaPlayer, MediaSession, notificació, controls remots i gestió de focus/desconnexió d’auriculars.
- iOS: AVAudioPlayer, sessió Playback, controls remots, Now Playing i configuració de background audio al projecte exportat.
- La cua nativa pot seguir sense frames de Unity. En tancar el Jukebox o començar una partida, la reproducció s’atura.
- El botó Segon pla prepara una còpia local de totes les cançons desbloquejades de la cua. La primera preparació pot descarregar contingut; després la cua preparada funciona sense connexió. Es pot cancel·lar tancant el Jukebox.
- Els originals tenen una representació AAC independent, ara xifrada com a TPS, per al reproductor natiu: no altera els àudios ni els perfils del runner. Els imports es descodifiquen localment a WAV quan cal preparar-los per al reproductor natiu.

Quan s’afegeixin o canviïn cançons: executar **Tools → MusicGame → Content → Songs → Bake Jukebox Background Audio**, i publicar els Addressables incloent el grup `Remote_JukeboxAudio` i l’índex musical. El bake requereix FFmpeg disponible i conserva els GUID dels assets. La publicació musical utilitza ara el provider TPS i genera paquets xifrats; vegeu TPS_AUDIO.md per a les claus, el procés de build i els límits de protecció. No es publica res automàticament amb aquesta implementació.

La compilació de runtime i editor i les comprovacions de concessió/permisos es poden executar sense tancar l’editor. Les interaccions de focus i les confirmacions visuals també s’han de provar en Play. La compilació Java es pot comprovar des de Windows. L’àudio en segon pla, les interrupcions, els auriculars i els selectors de documents s’han de validar també en dispositius Android/iOS abans de publicar. iOS necessita una build amb Xcode en un Mac.

## Validació

`JukeboxValidation.InstallAndVerify` genera els prefabs del Jukebox, comprova migració, ordre, permisos de les dues compres, imports, persistència, eliminació i tast únic en fitxers temporals, genera captures i prepara els àudios natius. No utilitza ni modifica el progrés real del jugador.

Resultat: `MusicGame/Logs/JukeboxValidation.txt`. Captures: `VisualReviews/Jukebox/`.
