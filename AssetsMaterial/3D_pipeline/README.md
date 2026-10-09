# FittingAndRigging v3.4

Programa independent del retopo. Processa els LODs d'una peça ja presents a `Output/<asset>/`.

## Avatar canònic

Ha d'existir exactament:

`_System/FittingAndRigging/Avatar/MakeHuman_Canonical.fbx`

## LODs esperats

El programa reconeix i processa, en aquest ordre:

1. `*_ULTRA_50k.glb` — únic LOD que s'obre per al fitting manual.
2. `*_HIGH_30k.glb`
3. `*_MEDIUM_20k.glb`
4. `*_MEDIUM_10k.glb`
5. `*_LOW_5k.glb`

Si algun LOD no existeix, simplement processa els que trobi mantenint aquest ordre.

## Flow

1. Blender obre `MakeHuman_Canonical.fbx` + `ULTRA_50k`.
2. L'usuari ajusta una sola vegada:
   - escala / posició / rotació de la peça;
   - pose temporal del rig/body perquè encaixi amb la peça.
3. `Ctrl+S`, tornar a consola i ENTER.
4. La consola pregunta una sola vegada si la peça necessita blendshapes.
5. ULTRA: pesos -> normalització a REST/T -> blendshapes opcional -> GLB.
6. La mateixa transformació de fitting i la mateixa pose temporal del rig s'apliquen automàticament a HIGH/MEDIUM/LOW.
7. Cada LOD rep els seus propis pesos i blendshapes segons la seva topologia i acaba en la REST/T-pose canònica.

## Output

`Output/<asset>/Rigged/` conté només:

`*_ULTRA_50k_rigged.glb`
`*_HIGH_30k_rigged.glb`
`*_MEDIUM_20k_rigged.glb`
`*_MEDIUM_10k_rigged.glb`
`*_LOW_5k_rigged.glb`

No es generen `.blend` finals ni reports JSON dins de `Output`.

Els checkpoints de treball, inclòs el `.blend` manual, es mantenen només a:

`_System/FittingAndRigging/_work/<asset>/`


## v3.5 — preprocess mestre ULTRA replicat a tots els LODs

El fitting manual es fa **només sobre `ULTRA_50k`**. El programa captura:

- `matrix_world` de la peça: posició, rotació i escala.
- `matrix_basis` de tots els ossos del rig: la pose temporal ajustada.

Per cada altre LOD (`HIGH_30k`, `MEDIUM_20k`, `MEDIUM_10k`, `LOW_5k`) s'importa el GLB original i se li aplica exactament el mateix snapshot de l'ULTRA abans de qualsevol càlcul de rigging.

Abans de transferir pesos o normalitzar a REST/T-pose es guarda un checkpoint propi a:

`_System/FittingAndRigging/_work/<asset>/preprocessed/<LOD>_preprocessed.blend`

Això permet refinar manualment un LOD concret més endavant sense repetir el fitting general. Els resultats públics continuen nets: `Output/<asset>/Rigged/` només conté els `.glb` finals.


## v3.6 — world-space fitting preservat

El `.blend` manual de l'ULTRA és la font de veritat.

- Posició, rotació i escala del garment es bakegen directament als vèrtexs en world-space.
- Després del bake, el garment queda amb transformació identitat sense moure's visualment.
- Si el bake altera el bounding box world-space més de `1e-5`, el pipeline s'atura.
- El parentatge al rig conserva exactament `matrix_world`.
- Els altres LODs reben la mateixa transformació i la mateixa pose temporal capturades de l'ULTRA.
- El pipeline no recentra ni reinterpreta el fitting després de l'ajust manual.


## v3.7 — output Unity-ready per LOD

Cada LOD genera una carpeta pròpia dins `Output/<asset>/Rigged/`:

```text
Rigged/
├─ ULTRA/
│  ├─ *_ULTRA_50k_rigged.fbx
│  ├─ *_ULTRA_50k_rigged.blend
│  └─ Textures/
├─ HIGH/
├─ MID-HIGH/
├─ MEDIUM/
└─ LOW/
```

L'FBX s'exporta amb Apply Transform (`bake_space_transform=True`), `-Z Forward / Y Up`, sense leaf bones ni animacions, i només amb `ARMATURE + MESH`. Les textures es copien físicament a `Textures/` i també es demana a l'exportador FBX que les embegui (`embed_textures=True`).


## v4.0 — projecte net, pesos de roba i fitting reutilitzable

### Què queda després de cada .bat (la resta s'esborra automàticament quan el pas acaba bé)

```text
01_GENERAR_RAW3D.bat  ->  Raw3D/<asset>/
                           ├─ <asset>.glb
                           ├─ input_original.<ext>
                           └─ upscaled_and_cut/<vistes enviades a Meshy>.png
                          (s'esborren Upscaled/<asset>, Upscaled_and_Cut/<asset>, Manual_Leonardo/<asset>,
                           Debug/<asset> i .playwright-mcp; _System/config.json behavior.keep_debug=true els conserva)

02_RETOPO.bat         ->  Output/<asset>/
                           ├─ <asset>_ULTRA_50k.glb · _HIGH_30k · _MIDHIGH_20k · _MEDIUM_10k · _LOW_5k
                           └─ preProcess/  (el paquet de Raw3D: glb original, input_original, upscaled_and_cut/)
                          (_W només és scratch; s'esborra quan queda buit)

03_FITTING_&_RIGGING  ->  Output/<asset>/Rigged/
                           ├─ ULTRA/ HIGH/ MID-HIGH/ MEDIUM/ LOW/   (*_rigged.fbx, *_rigged.blend, Textures/)
                           └─ fit_record.json   (fitting desat, vegeu sota)
                          (_System/FittingAndRigging/_work/<asset> s'esborra en acabar;
                           config.json keep_work_files=true el conserva)
```

Si un pas falla, també s'esborren els seus fitxers intermedis (Debug, `_W`, `_work/<asset>`, Blender es
tanca): el detall de l'error només surt a la consola, no es guarden logs a disc. Es conserva només el que
ja s'havia completat (Input, Raw3D, Upscaled_and_Cut, Output) i, a l'etapa 1, dos casos per no perdre feina:
la imatge de Leonardo (`Upscaled/<asset>`) si encara no hi ha vistes tallades (si no, es tornaria a
encarregar l'upscale), i la carpeta `Manual_Leonardo/<asset>` mentre l'upscale manual no s'ha completat.

Les textures de `Rigged/<LOD>/Textures` es reescriuen a cada exportació (no s'acumulen `*_2.png`) i el
`.blend` final se substitueix sense deixar `.blend1`.

### Pesos de roba (garment_weights.py)

Després de la transferència de pesos del cos, el refinament analitza la peça (camals, panells, obertures,
gruix) i només actua si troba problemes: sense problemes conserva els pesos; peça ajustada amb problemes
-> correcció local; peça ampla -> redistribució completa. L'ULTRA fa de referència per a tots els LODs.
Tipus de peça pel nom (o `Output/<asset>/garment.json`). Paràmetres: `config.json` -> `garment_weights`.

### Fitting reutilitzable

`Rigged/fit_record.json` guarda el fitting (transformació de la peça + postura temporal del rig) i
l'empremta (sha256) de l'ULTRA i de l'avatar ajustats. En tornar a executar el .bat d'un asset ja ajustat,
es pot reutilitzar (pregunta [Y/n]) i es recalculen pesos i LODs sense fitting manual. Si l'ULTRA o
l'avatar han canviat, no s'ofereix i cal fer el fitting de nou. Si no hi ha cap asset pendent, el .bat
llista els assets amb fitting desat per regenerar-ne un.
