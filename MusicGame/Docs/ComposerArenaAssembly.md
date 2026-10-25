> Revisió vigent del 23/10/2026: vegeu `ComposerArenaReview_2026-10-23/Revisio_escenes_2026-10-23.md` i les comparatives del mateix directori. Aquest document descriu la integració inicial.

# Primers escenaris dels compositors

Muntatge del 22 d'octubre de 2026, a partir de les imatges d'`AssetsMaterial/References/Scenarios`, les exportacions disponibles d'`AssetsMaterial/3D_pipeline/Output` i els terres i cels d'`AssetsMaterial/Scenarios`.

## EdiciÃ³ i generaciÃ³

Cada compositor conserva el seu prefab mestre `Assets/_Project/Art/Arenas/<Compositor>/<Compositor>_Arena_01.prefab`. Monteverdi conserva l'arquitectura, els materials de terra, la ilÂ·luminaciÃ³ i el skybox existents; s'hi afegeixen cinc complements als laterals. La composiciÃ³ dels altres muntatges Ã©s a `ComposerScenery`, amb agrupacions North, South, East i West, i els tancaments arquitectÃ²nics necessaris.

El manifest `Assets/_Project/Art/Arenas/composer_scenery_manifest.json` contÃ© les fonts, les dimensions, les colÂ·locacions i les variants exportades. L'eina **Tools â†’ MusicGame â†’ Content â†’ Arenas â†’ Build All First Composer Arenas** importa i normalitza els objectes, construeix els prefabs de qualitat, aplica la composiciÃ³ i genera les escenes. El manifest Ã©s l'autoritat d'aquest generador: cal traslladar-hi les modificacions manuals abans de regenerar, perquÃ¨ el muntatge generat es reemplaÃ§a.

Sortida: `Assets/_Project/Art/Arenas/<Compositor>/Scenes/<Compositor>_Arena_01_<Qualitat>.unity`, per Low, Mid, HighMid, High i Ultra. Cada escena nomÃ©s referencia els models seleccionats per a la seva qualitat. Si una qualitat d'un objecte encara no s'ha exportat, s'utilitza la millor inferior disponible o, si no n'hi ha, la menor superior, i es registra al log.

Les escenes es relacionen amb el GUID original del prefab a `Resources/ArenaSceneCatalog.asset`. Estan registrades a `Remote_Arenas` amb adreces `Arena/<compositor>/01/<qualitat>` i les etiquetes existents. El prefab mestre es conserva per editar, perÃ² s'exclou com a entrada descarregable independent. Les arenes 02 i 03 i les referÃ¨ncies originals dels compositors es conserven.

## Materials i recursos pendents

Els terres utilitzen albedo i relleu convertit a normal map, amb URP Lit. No es creen ni s'assignen mapes specular, per indicaciÃ³ de l'usuari. Els cels disponibles utilitzen Skybox/Panoramic. Bach no tÃ© una font de skybox identificada i utilitza provisionalment un cel procedural fosc.

Mozart, Tchaikovsky, Vivaldi i Wagner encara no tenen objectes d'escenari exportats: les seves primeres arenes tenen el terra i el cel preparats. Els directoris sense FBX i textures utilitzables no s'importen.

## Comprovacions

`ComposerSceneryAssembly.RunFromCommandLine` genera i comprova les cinc qualitats, la composiciÃ³, els materials, l'espai central de combat, les dependÃ¨ncies seleccionades i la resoluciÃ³ del catÃ leg. L'opciÃ³ `-sceneryComposer Bach` permet regenerar nomÃ©s un compositor, mantenint l'auditoria final del catÃ leg complet.

`ComposerArenaLifecycleProbe.RunFromCommandLine` prova el carregador de producciÃ³ en una escena aÃ¯llada, amb el proveÃ¯dor Asset Database d'Addressables. Comprova les cinc qualitats de Monteverdi, Low i Ultra dels altres onze compositors, el terra i el cel actius, l'absÃ¨ncia de selectors de cinc qualitats al runtime, la descÃ rrega neta, la substituciÃ³ i cancelÂ·laciÃ³ d'una cÃ rrega, la compatibilitat amb els prefabs antics i la preparaciÃ³ i repeticiÃ³ del combat real. Restaura el proveÃ¯dor d'Addressables i no arrenca Core ni el servei de partides desades.

Informes: `Logs/ComposerSceneryAssembly.txt` i `Logs/ComposerArenaLifecycleProbe.txt`. Captures de composiciÃ³: `Logs/ComposerSceneryPreviews/`. Captures en mode de joc: `Logs/ComposerArenaRuntimePreviews/`.

La integraciÃ³ no executa bakes, builds de bundles ni publicaciÃ³ remota. Les comprovacions amb Asset Database no substitueixen una prova posterior al dispositiu amb els bundles construÃ¯ts.

Resultat final: 60 escenes generades i auditades; 173 comprovacions del carregador i combat passades. Els materials dels vitralls i cortinatges sâ€™han corregit com a no metÃ lÂ·lics per evitar que el mapa generat desaturi els colors.
