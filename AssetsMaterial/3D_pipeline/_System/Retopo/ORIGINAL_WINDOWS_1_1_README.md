# Processar una carpeta de models Meshy — versió 1.1

Aquest paquet executa localment el procés base utilitzat amb la sabata i els pantalons: QuadriFlow, conformació a la superfície original, UV noves, bake PBR 4K i GLB verificat. L’objectiu és aproximadament **50.000 triangles**, no 50.000 vèrtexs. No utilitza Decimate.

## La manera senzilla, a Windows

1. Descomprimeix **tot** el ZIP.
2. Posa tots els GLB originals a la carpeta `models`.
3. Fes doble clic a **PROCESSAR_MODELS.bat**.

El programa cerca Blender 4.5.3 LTS, processa les peces una per una i crea `models/resultats`. També pots **arrossegar la teva carpeta de GLB damunt del BAT**: els resultats quedaran dins d’aquella carpeta, a `resultats`.

Cal Python 3.10 o posterior i **Blender 4.5.3 LTS**. No cal instal·lar dependències de Python amb pip. Tot el procés és local; no necessita ChatGPT ni connexió a Internet.

## Amb una ordre

Des de la carpeta descomprimida:

```powershell
py meshy_to_blender.py "C:\ElsMeusModels"
```

Si no troba Blender automàticament:

```powershell
py meshy_to_blender.py "C:\ElsMeusModels" --blender "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"
```

A Linux/macOS, utilitza `python3` i el camí de l’executable de Blender. Si no indiques cap carpeta, el programa utilitza `models`, al costat del script.

## On deixa els resultats

Si l’entrada és `C:\ElsMeusModels` i hi ha `sabata.glb` i `pantalons.glb`, els fitxers finals verificats queden a:

- `C:\ElsMeusModels\resultats\sabata_HIGH.glb`
- `C:\ElsMeusModels\resultats\pantalons_HIGH.glb`

Els originals es conserven. Les còpies de treball, textures PNG, checkpoints `.blend`, comparacions visuals i registres es guarden a `resultats/_work/<nom de la peça>/`.

Un GLB es copia a la carpeta principal de resultats **només després de passar la verificació**. Si fallen les comparacions visuals posteriors, el GLB ja verificat es conserva i es registra aquest error. `resultats/_work/<peça>/job.json` indica els passos completats i l’estat.

Només es llegeixen els GLB directament dins la carpeta d’entrada, sense subcarpetes. Així els GLB generats dins `resultats` no es tornen a processar.

## Tornar-lo a executar

Executa la mateixa ordre o el mateix BAT:

- Reprèn peces interrompudes des dels passos completats.
- Omet els passos de les peces ja acabades.
- Processa els GLB nous que hagis afegit.
- Si un fitxer és invàlid o falla un control, registra l’error i continua amb els altres.

La represa és automàtica. Compara el SHA-256 de l’input, la configuració, els scripts i els checkpoints. Un canvi de recepta o d’input amb el mateix nom demana una altra carpeta de sortida; no barreja resultats ni sobreescriu un GLB final modificat manualment.

Per començar en una carpeta de resultats nova:

```powershell
py meshy_to_blender.py "C:\ElsMeusModels" --output "C:\ElsMeusModels\resultats_2"
```

Les sortides de la versió 1.0 no es poden reprendre amb la 1.1; utilitza una carpeta de sortida nova.

## Procés per defecte

Totes les peces del lot utilitzen la mateixa recepta base:

- Blender 4.5.3 LTS, CPU, vuit fils, llavor QuadriFlow 42.
- QuadriFlow amb objectiu de 50.000 triangles, sense preservar arestes sharp i amb suavitzat inicial de normals. Escala temporal ×1000 abans de QuadriFlow, restaurada abans de conformar.
- UV amb Angle Based, fins a vuit intents i reparacions locals; marge d’empaquetat de 32 píxels sobre 4096.
- Validació de la triangulació definitiva i de les UV abans del bake.
- Bake BaseColor, Normal, Roughness i Metallic de 4096×4096, marge de 12 píxels, selecció del cage local segons cobertura.
- Verificació del GLB reimportat, geometria final i comparacions de davant/darrere.

El nom només aporta una pista per als seams: `shoe`, `sabata`, `zapato`, `boot`, `bota` → sabata; `trouse`, `pants`, `pantal` → pantalons; `wig`, `hood`, `perruca`, `peluca` → perruca. La resta utilitza els seams del perfil superior. **No és una classificació de la geometria**. Si els noms són genèrics, el lot continua igualment; pots indicar seams de sabata/pantalons amb un perfil explícit o un JSON.

Per defecte no s’aplica la projecció exterior del tors ni les correccions especials de l’armilla. Els ajustos base segueixen el procés de la sabata/pantalons; les reparacions necessàries poden canviar segons cada geometria. No es promet un resultat idèntic byte a byte als models ja lliurats.

## Opcions addicionals

- `--dry-run`: valida cada GLB i mostra el pla sense generar sortides.
- `--until inspect`: només inspecciona. Després torna a executar sense aquesta opció per continuar.
- `--no-preview`: omet les comparacions visuals addicionals; la verificació continua activa. Mantén aquesta opció en reprendre.
- `--profile shoe`, `trousers`, `wig` o `standard`: fixa els seams per a tots els GLB d’una carpeta, mantenint la recepta base.
- `--profiles perfils.json`: tria un perfil per nom exacte de fitxer; hi ha un exemple al paquet.
- `--profile vest` o `coat`: selecciona explícitament les receptes avançades de l’armilla/abric (Minimum Stretch, densitat UV normalitzada, bake escalat, multi-cage i correccions de projecció). L’armilla afegeix projecció exterior del tors; `--local-projection` la desactiva. Aquests perfils no s’activen automàticament.

## Inputs admesos i límits

L’entrada prevista són GLB Meshy estàtics semblants als originals: una malla/primitiva, un material PBR opac amb factors iguals a 1, UV0 i BaseColor, Normal i Metallic/Roughness incrustades. Sense rigs, animacions, morphs, recursos externs ni extensions obligatòries. Un input fora d’aquest perfil es registra com a error i es passa al següent.

Els seams assumeixen orientació com als originals: Z amunt després de la importació a Blender, davant cap a −Y, i llargada de la sabata sobre X. La retopologia i els seams són heurístics. Una carpeta amb objectes arbitraris de formes molt diferents pot necessitar nous perfils.

Els controls exigeixen 45.000–55.000 triangles, topologia sense errors detectats, UV finites dins 0–1 i sense solapament d’àrea positiva segons la tolerància de l’auditor, desviació P99 mostrejada en tots dos sentits ≤0,5% de la dimensió màxima i cobertura de bake dins el llindar. Això no certifica que cada plec i cada píxel quedin perfectes. Les textures 4K distribueixen el detall de les fonts 2K, però no inventen detall.

**Els forats visuals reportats als pantalons encara no s’han corregit i verificat.** La seva malla final passa el control de geometria tancada; això no demostra que l’aspecte a la zona baixa dels plecs sigui correcte. El programa inclou els controls i reparacions topològiques, però no anuncia que aquell problema visual estigui resolt.

El lot s’ha comprovat amb imports reals, continuació després d’un GLB invàlid, represa i sortida automàtica. El procés consolidat complet no s’ha tornat a executar de zero sobre tots els models ni s’ha provat el BAT en Windows. Consulta `VALIDATION.md`.
