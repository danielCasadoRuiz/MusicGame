# Arenes amb escenes i qualitats

Implementat el 17 d'octubre de 2026. No s'ha fet cap bake ni publicació de contingut remot.

## Edició i generació

La composició mestra continua a `Assets/_Project/Art/Arenas/Monteverdi/Monteverdi_Arena_01.prefab`. Els prefabs de cada objecte conserven els cinc nivells de qualitat per facilitar l'edició.

Després de modificar la composició, executar **Tools → MusicGame → Content → Arenas → Generate Monteverdi Quality Scenes**. El generador crea aquestes escenes a `Assets/_Project/Art/Arenas/Monteverdi/Scenes/`:

- `Monteverdi_Arena_01_Low.unity`
- `Monteverdi_Arena_01_Mid.unity`
- `Monteverdi_Arena_01_HighMid.unity`
- `Monteverdi_Arena_01_High.unity`
- `Monteverdi_Arena_01_Ultra.unity`

Cada escena té la mateixa composició però només els prefabs de la seva qualitat. No conté el selector amb cinc fills ni referencia el prefab mestre. El generador compara els transforms de les col·locacions i audita les dependències per evitar incloure models d'altres qualitats. El terra polit, la coberta parcial i la galeria posterior es mantenen.

Les escenes es regeneren des de la composició mestra: no cal repetir el muntatge manualment cinc vegades. Cal generar abans de fer el bake. La generació reemplaça el contingut de les escenes; quan s'introdueixi il·luminació precalculada, caldrà incorporar al flux la conservació de configuracions d'il·luminació i la regeneració dels seus bakes.

## Selecció i Addressables

`Resources/ArenaSceneCatalog.asset` relaciona el GUID de l'arena original amb cinc referències d'escena, per nom de qualitat de Unity. Les referències són metadades, no referències directes als objectes: carregar el catàleg no carrega les cinc escenes ni el prefab mestre.

Es conserva la selecció d'arena per compositor, versió i etapa de boss que utilitzen OpponentDefinition, ArenaSelector, GameSession i FightPreloader. El carregador resol aquesta identitat a una escena quan existeix al catàleg. Les arenes encara no migrades continuen funcionant com a prefabs.

Les cinc escenes es registren al grup existent `Remote_Arenas`, amb empaquetat separat i els perfils de distribució ja configurats. Adreces: `Arena/monteverdi/01/<qualitat>`. Etiquetes: `Category_Arenas`, `Arena_monteverdi` i `Q_<qualitat>`. L'entrada Addressable del prefab mestre s'elimina; la seva identitat es conserva als selectors existents. ArenaContentSetup respecta les arenes migrades i no torna a incloure aquest prefab als bundles.

La resolució fa servir QualitySettings i la regla existent: qualitat exacta; si falta, la millor inferior disponible; si no n'hi ha cap, la menor superior disponible. La qualitat entra en la clau de precàrrega.

## Cicle del combat

1. Durant els resultats del runner, FightPreloader escull l'arena i descarrega les dependències de la seva escena de qualitat. No activa cap escena abans d'entrar al combat. Els avatars continuen amb la precàrrega anterior.
2. Fight es carrega amb la càmera i la lògica compartides. No genera lluitadors durant la selecció del rival.
3. En confirmar el rival, ArenaEnvironmentLoader carrega l'escena additivament amb Addressables i l'activa. L'escena conté el seu propi sol; el de Fight queda desactivat mentre l'arena està carregada. El cel, l'ambient i les dades futures de lightmaps pertanyen a l'arena.
4. Quan l'arena està preparada, es generen els dos lluitadors dins d'aquesta escena i es construeixen els seus avatars.
5. StageReady només és cert quan existeixen els lluitadors, els avatars estan preparats i l'arena està carregada. Versus conserva el vel de càrrega fins aleshores, sense avançar pel simple venciment del temps límit anterior.
6. Fight Again reutilitza l'arena i els actors preparats. L'arena es conserva durant la pantalla de resultat del combat per permetre aquesta repetició.
7. En sortir del mode Fight, SceneFlowController espera ShutdownStageAsync: es cancel·len lògicament les preparacions pendents, s'alliberen els avatars i la precàrrega i es descarrega l'escena amb Addressables. Després es descarrega Fight. No s'esborra la memòria cau de descàrregues al disc.

El carregador serialitza substitucions i descàrregues. Si una càrrega acaba després d'haver estat substituïda o cancel·lada, descarrega el resultat en comptes d'adoptar-lo. Una fallada de càrrega conserva el fallback de terra provisional. En tornar a l'escena propietària es restauren l'ambient i el sol; en sortir cap a un mode nou no se sobreescriu la seva il·luminació.

## Comprovacions

- `ArenaSceneSetup.RunFromCommandLine`: genera, registra i valida les cinc escenes, les dependències i la resolució de qualitat. També comprova que ArenaContentSetup continua funcionant després de la migració.
- `ArenaSceneLifecycleProbe.RunFromCommandLine`: proves aïllades en mode de joc amb el proveïdor Asset Database d'Addressables. El selector de Play Mode d'Addressables es restaura al final. No es carrega Core ni el servei de partides desades.
- Proves: cinc qualitats; fallback de qualitat; reutilització de la mateixa arena; substitució d'una càrrega en curs; cancel·lació abans d'acabar; compatibilitat amb arenes prefab; FightSceneBootstrap real amb les receptes de producció; lluitadors generats després de carregar l'arena i assignats a la seva escena; Fight Again; alliberament de recursos en sortir.

Informes: `Logs/ArenaSceneSetup.txt` i `Logs/ArenaSceneLifecycleProbe.txt`. El build dels bundles per a dispositiu i el seu desplegament al servidor segueixen el flux de publicació d'Addressables existent; aquestes proves de Play Mode no substitueixen una prova posterior amb bundles construïts.

L'arquitectura és genèrica; en aquesta migració s'han generat les cinc escenes de Monteverdi Arena 01. Per convertir altres arenes cal generar-ne les escenes i afegir l'entrada corresponent al catàleg.
