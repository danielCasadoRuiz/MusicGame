# Revisió independent del treball de Claude — 17/10/2026

## Abast

Revisats el disseny i les tres auditories de Claude, contrastant els punts de sincronització amb el projecte actual. Revisió parcial del nou nucli C# en desenvolupament. Cap modificació al projecte Unity ni al scratchpad de Claude. No s'ha obert ni tancat Unity, compilat el projecte o executat una build de jugador.

S'han copiat les auditories i el disseny a aquesta carpeta per conservar la versió revisada. S'ha executat el harness sintètic des d'una còpia independent del binari: no s'ha recompilat ni interferit amb la feina activa de Claude.

## Valoració del disseny

L'enfocament és adequat: preservar MusicClock, separar temps de clip i de partida, detecció per bandes amb diverses famílies, càlcul independent de Unity, concurrència limitada, perfils binaris publicats i càrrega del catàleg sense FFT/inferència. Els errors de caigudes i ambientació que Claude ha afegit són reals en la lògica llegida.

Confirmacions sobre el codi actual:

- `FallRespawnSystem.cs:151-153` pausa l'àudio sense pausar MusicClock. `MusicClock.cs:142-152` passa llavors a Time.time-wallStart. GameplayManager segueix consumint el rellotge. La represa fa ForceUpdate, però no desfà tots els esdeveniments ja disparats durant el salt.
- `MusicEnvironmentController.cs:138-139,153` consulta SongProfile amb SongTime en lloc de temps del clip. Amb warmup=3 consulta dades de 3 s més endavant. És anticipació de features; la suavització pot alterar l'efecte visual percebut.
- `MusicRunnerLevelConfig.asset:48` té beatGridSubdivision=0.7 i la graella no estima fase.
- La cache personal té 20 fitxers i 734.377.583 bytes en el recompte fet en aquesta revisió: aproximadament 700,36 MiB / 734,38 MB decimals. Això és compatible amb l'ordre de magnitud del resum de Claude.
- El catàleg d'identitats actual és un asset referenciat per AppConfig dins Resources; nous àudios remots poden descobrir-se com a localitzacions, però el flux complet d'identitat, progressió i similitud no queda resolt amb aquella descoberta. El nou índex remot és necessari.

Matís: les xifres de classificació dels perfils antics descriuen aquesta cache; no demostren precisió sobre la versió futura ni garanteixen que totes les caches coincideixin amb el pipeline actual. El percentatge de graella de l'auditoria és de candidats abans del thinning, no necessàriament de bonus finals jugats.

## Resultats del primer binari del nucli, encara en desenvolupament

Snapshot del binari compilat al voltant de les 00:30:46, hora de Madrid, el 17/10/2026.

SHA256: `B2734BE21EC3FA802261F921A5B6029475BF18718A8CDF31816DAD84D97CEAF2`

Ordre executada: `harness.dll synth`, .NET 8, proves de Claude a 48 kHz i 44,1 kHz. Sortida íntegra: `synth_snapshot_results.txt`.

**17 PASS i 23 FAIL sobre 40 casos impresos. El procés retorna 0 tot i les fallades.** Aquest és un estat intermedi, no una avaluació de la implementació final.

Exemples a 48 kHz:

- Kicks: 12 cops de referència, 12 localitzats, però 12 esdeveniments extra; els 12 cops aparellats tenen etiqueta Kick+HiHat.
- Snares: 12 cops localitzats, 0/12 amb la família esperada. Els classifica majoritàriament Kick+HiHat.
- Kick+hat simultanis: els 12 moments aparellats contenen les famílies esperades, però hi ha 12 esdeveniments addicionals.
- Loop amb bateria, baix i plucks: els 64 moments es troben, però hi ha 31 esdeveniments extra i només 48/64 compleixen les famílies esperades.
- Hi-hats de semicorxera a 180 BPM: 48/48, sense esdeveniments extra; error absolut mitjà ~0,89 ms.
- Hi-hats de fusa a 120 BPM: PASS amb 64/64 moments, tot i que un inclou una etiqueta Snare sobrera.
- Inici del clip i un clip de 500 mostres amb click: encara fallen.

El temps dels cops aparellats és prometedor en diversos estímuls. Això no compensa falsos positius o famílies incorrectes i no demostra el comportament sobre música real.

## Dos defectes del harness que convé corregir abans de confiar en un PASS

### R1 — El veredicte no penalitza famílies sobrants

`Harness/SynthTests.cs:103-105` considera encertada una família si conté totes les esperades i compta les sobrants a famExtraWrong. Però la condició `r.pass` a :118 no consulta famExtraWrong.

Per tant un detector que digués Kick+Snare+HiHat a cada moment podria passar la comprovació de família dels casos que esperen només una d'elles, si no falla pels temps o recompte.

Corregir amb mètriques per família: TP, FP i FN, i criteris explícits per cas. En estímuls sintètics controlats, les famílies no presents han de penalitzar. No aplicar aquesta exclusivitat als tests de música real sense anotacions fiables.

### R2 — El programa retorna èxit encara que les proves fallin

`Harness/Program.cs:21` executa RunAll i retorna 0 sense agregar `CaseResult.pass`. `fftcheck` també imprimeix errors sense un llindar d'acceptació al codi llegit.

Retornar un codi no zero si falla qualsevol prova obligatòria. La compilació correcta i l'exit code actual no són evidència d'una suite passada. Afegir un llindar a les comprovacions numèriques de FFT i reportar determinisme/cancel·lació per separat.

## Precisions que recomano incorporar al pla

1. **Final natural:** `!isPlaying` per si sol no significa final; pot ser pausa, stop, fallada o estat de transició. El nou rellotge necessita estats explícits de warmup, playback, hold/respawn, ended i farewell. Comprovar finals naturals després de caigudes i reproducció parcial, sense reproduir en ràfega esdeveniments saltats.
2. **Desduplicació de càmera:** basar-la en temps i identitat dels esdeveniments musicals, no només en temps d'arribada al frame. Un frame lent pot contenir diversos cops legítims diferents. No desplaçar cada pulsació 30 ms per poder agrupar-la ni canviar l'horari dels bonus.
3. **Correspondència àudio/perfil:** samples, frequency i songId no distingeixen dues revisions amb la mateixa longitud. L'índex remot ha d'identificar revisió/hash i versions compatibles. Runtime ha de comparar els headers amb les metadades publicades de la mateixa revisió. No recalcular un hash de tot el PCM al mòbil només per validar el perfil.
4. **Empaquetament:** dos assets marcats PackSeparately no formen automàticament un bundle per parella. El bundle per cançó exigeix un grup per parella o PackTogetherByLabel amb exactament el mateix conjunt de labels per a àudio i perfil. Verificar el layout construït; no només la configuració.
5. **Descàrregues per actualització:** mateix bundle per cançó és una decisió defensable, però un canvi només del perfil pot redescargar àudio. No tractar aquest cost com inevitable només perquè canviï el pipeline. Mesurar i decidir respecte a bundles separats, mantenint vinculació de revisions.
6. **Dades derivades:** el disseny i l'auditoria alternen perfil cru + recomputació i perfil complet. Fixar un contracte únic: perfil funcional complet amb recomputació només si és lleugera i mesurada. No reintroduir espera important ni dependència de cache local.
7. **Memòria:** ~270 MB d'assignacions temporals acumulades no és el mateix que 270 MB de memòria màxima simultània. Separar bytes assignats, memòria viva màxima, GC i resident/PCM. Solapar semàntica i ritme pot empitjorar el pic de memòria; cal pressupost global de concurrència.
8. **Calibratge:** els offsets per família s'han d'avaluar amb diverses formes d'atac, fases dins del hop i sample rates, i amb senyals no utilitzats per ajustar-los. No inferir qualitat general només dels estímuls de calibratge.
9. **Progrés concurrent:** FeatureExtractor i OnsetDetector fan Interlocked.Increment i després criden callbacks des de diversos workers. Els callbacks poden arribar fora d'ordre; el wrapper ha de publicar progrés monòton al fil principal.
10. **Cancel·lació:** descartar una petició abans de publicar qualsevol esdeveniment global, no només abans del callback final. Validar propietat del perfil a GameSession i alliberament de recursos de peticions cancel·lades.

## Feedback prioritari per a Claude

El disseny general és coherent i els bugs de caiguda/ambientació estan ben identificats. Abans de donar el nucli per validat, corregir el criteri de famílies sobrants i l'exit code del harness; estabilitzar snares, falsos positius i vores del clip; mantenir una bateria de validació independent del calibratge. La integració ha de preservar MusicClock i validar perfils amb la revisió remota correcta, no només la durada.

No s'ha contactat Claude directament ni s'ha modificat el seu treball actiu. Aquest informe és feedback per compartir-li.
