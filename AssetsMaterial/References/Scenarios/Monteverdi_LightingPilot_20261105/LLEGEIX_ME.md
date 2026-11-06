# Monteverdi 01 — pilot de llum i antialiàsing

La millor proposta visual d'aquestes proves és un bake selectiu: terra i arquitectura simple amb lightmaps; ornaments amb light probes; sol principal mixt; llum de suport baked; reflexió precalculada. MSAA 4× dona els contorns més nets de les variants comparades. MSAA 2× és l'alternativa a provar en dispositius limitats.

## Què hi trobaràs

- `abans_despres_Mid.jpg`: mateixa càmera, resolució i geometria Mid. Es compara la configuració inicial (escala 0,85, sense MSAA) amb la proposta (escala 1, MSAA 4×), amb els canvis de llum i materials.
- `abans_despres_High.jpg`: mateixa geometria High i resolució; MSAA 4× a les dues bandes. Mostra sobretot la diferència de llum i resposta dels materials.
- `comparacio_moviment.mp4`: recorregut idèntic, muntat amb captures reals de Unity. No és una gravació de rendiment en temps real.
- `Mid_vista_0..3.png` i `High_vista_0..3.png`: diferents angles. Les càpsules grises de la vista 3 només demostren la llum rebuda per objectes dinàmics; no s'han afegit a l'escena final.

## Prova dins de Unity

Obre una d'aquestes escenes i prem Play; mira la finestra Game:

- `Assets/_Project/Scenes/Debug/MonteverdiLighting_Mid_Recommended.unity`
- `Assets/_Project/Scenes/Debug/MonteverdiLighting_High_Recommended.unity`

Són còpies de prova independents de Monteverdi 01. La càmera de previsualització aplica el seu perfil privat en Play i el restaura en sortir. Les escenes originals i les altres variants es conserven. No cal tornar a fer bake per obrir el pilot. Els recursos propis són a `Assets/_Project/Art/Arenas/Monteverdi/LightingPilot/`.

## Configuració escollida

| Element | Inicial | Pilot |
|---|---|---|
| Llums en temps real | 2 direccionals | 1 sol mixt; suport baked |
| Lightmaps | 0 | 2, no direccionals |
| Memòria de textures de lightmap mesurada | 0 | 744.640 bytes, aproximadament 727 KiB |
| Light probes | 0 | 255, distribuïdes pel recinte i la zona del ring |
| Reflexió | Probe realtime actualitzada a la càrrega | Cubemap precalculat de 128 px, mode Custom |
| AA Mid | Sense MSAA | MSAA 4× |
| Escala de render Mid | 0,85 | 1 |
| Filtratge | Principalment bilineal | Trilineal i anisotròpic 8 al pilot |
| Ombres | Perfil original | Distància 25, 2 cascades, mapa principal 1024 |

La suma de lightmaps és només la memòria de les textures de color mesurada per Unity en aquest ordinador. No inclou probes, cubemap, materials ni tota la memòria de l'escena, i pot canviar en compilar per mòbil.

He reduït la brillantor molt concentrada i la intensitat de les normals dels materials del pilot; el shader del terra incorpora lectura de lightmaps i un filtre de brillantor segons la variació de la normal. Això ajuda a estabilitzar el detall del terra. Les textures originals es comparteixen i els materials modificats són còpies.

## UV i duplicació

No he tornat a desplegar ni duplicat les malles ornamentals. Reben la il·luminació indirecta de les probes i continuen compartint les malles originals.

El terra és una sola malla compartida: he reutilitzat les seves coordenades UV de textura al canal de lightmap, després de comprovar que no se solapen en aquesta superfície. L'asset nou ocupa 19.391 bytes al disc i l'increment mesurat de memòria de malles és 17.512 bytes. L'arquitectura feta amb cubs aprofita el canal UV2 que ja existia. Les probes eviten haver de generar un segon desplegat en tots els ornaments.

Reutilitzar UV de textura només és adequat quan les illes no se solapen i tenen marge suficient. No ho he aplicat indiscriminadament a totes les peces.

## Antialiàsing: què ha guanyat

He comparat sense AA, MSAA 2×, MSAA 4×, SMAA i MSAA 4× + SMAA. Per aïllar l'AA, aquestes mesures comparteixen geometria, materials, escala 1 i càmera, i es comparen amb una referència de 3840×2160 reduïda a 1280×720. Error menor és millor:

| Variant | Error RMSE respecte de la referència |
|---|---:|
| Sense AA | 6.22 |
| MSAA 2× | 5.29 |
| MSAA 4× | 4.60 |
| SMAA | 5.78 |
| MSAA 4× + SMAA | 5.07 |

MSAA 4× és el millor resultat segons aquesta comparació de píxels. Afegir SMAA no millora la referència i afegeix una passada. És una mesura d'una vista, no una garantia universal de qualitat temporal. La vegetació i els reflexos també poden necessitar ajustos propis: MSAA no elimina tot l'aliasing d'un shader.

## Rendiment: què podem afirmar

El pilot redueix les llums en temps real i evita haver de capturar sis cares de reflexió en entrar a l'escena. A canvi, renderitzar Mid a escala 1 i MSAA 4× augmenta el cost de píxels respecte d'escala 0,85 sense AA. El resultat visual millora, però no es pot concloure que el conjunt sigui més ràpid sense una mesura al dispositiu.

La geometria segueix sent considerable: Mid suma 704.404 triangles i High 2.041.199 als MeshFilters auditats. No és el recompte de triangles visibles d'un fotograma ni inclou personatges. Per mòbil, partiria de Mid amb les reduccions per distància que ja té el joc, i provaria MSAA 2×/4× al dispositiu objectiu.

Les proves s'han fet amb una RTX 3070 Laptop i Direct3D11. Els temps desats als JSON inclouen renderització síncrona, resolució i lectura de la imatge cap a CPU; NO són temps de GPU ni FPS del joc. Els comptadors UnityStats no eren vàlids en aquest mode i no s'han utilitzat. No hi ha una validació de FPS, temperatura o consum en mòbil.

## Decisió proposada

Mantindria aquest bake híbrid i les reflexions precalculades. Per qualitat visual, MSAA 4×; per un primer perfil mòbil més ajustat, provaria 2×. Abans d'estendre-ho als altres escenaris, cal mesurar el joc complet en un mòbil de referència amb personatges, interfície i efectes actius. Aquest lliurament és el pilot de Monteverdi 01 que permet valorar el canvi.

## Documentació de suport

- [Unity: UV de lightmap i requisits de marge](https://docs.unity3d.com/6000.0/Documentation/Manual/LightingGiUvs.html)
- [Unity URP: antialiàsing](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/anti-aliasing.html)

## Verificació final

Les dues escenes han passat la prova en Play: dues lightmaps, terra vinculat al mapa 0, 255 probes, cubemap present, MSAA 4× i una sola llum en temps real. Els fitxers de comprovació s’adjunten. Les quinze escenes originals de Monteverdi i els dos fitxers d’ajustos generals auditats coincideixen byte a byte amb les còpies inicials; els ajustos temporals d’entrada a Play també s’han restaurat. La sessió batch de prova ha finalitzat.
