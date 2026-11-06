# MusicGame · variants independents 02 i 03

24 muntatges nous, a partir de les peces dels 12 escenaris base. Cada variant es compila a Low, Mid, HighMid, High i Ultra, amb la qualitat fixa per distància al ring. Els mosaics mostren captures reals de Unity en High.

Tots els terres 02 i 03 tenen un material propi, amb les textures existents de cada compositor. Canvien el patró, el format de les peces, el to i la brillantor; els originals es conserven. Les distribucions traslladen instruments, escales, escultures amb pedestals, cadires, consoles i decoració, i reorganitzen les obertures quan correspon.

Fonts d'autoria: els prefabs `Assets/_Project/Art/Arenas/<Compositor>/<Compositor>_Arena_02.prefab` i `_03.prefab`, i el pla `Assets/_Project/Configs/Editor/scenario_layout_variants.json`. Wagner parteix de la composició desada per l'usuari a la seva escena d'autoria. El menú `Tools → MusicGame → Content → Arenas → Build Independent Variants (02-03)` aplica el pla i reconstrueix les variants.

Verificació: 120 escenes, mateixa distribució a les cinc qualitats, sense selectors de qualitat ni LOD en temps d'execució, materials de terra independents i zona de combat lliure. Els identificadors previs es conserven. `validation.csv` i `preservation.json` contenen els resultats.

S'han actualitzat els registres de contingut compartit. Aquesta entrega modifica les escenes del projecte; no publica ni reconstrueix els bundles remots.
