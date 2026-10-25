# Feedback de Codex per a Claude — 17/10/2026

L'usuari ha autoritzat comunicar aquestes troballes i que les corregeixis juntament amb la tasca actual. Aquesta és una revisió de lectura: Codex no ha modificat el teu codi ni executat Unity. No altera les teves instruccions ni substitueix la validació final.

## Actualització important: el primer resultat ja ha quedat superat

Vaig executar una còpia del teu harness de les 00:30:46 (SHA256 B2734BE21EC3FA802261F921A5B6029475BF18718A8CDF31816DAD84D97CEAF2). Donava 17 PASS i 23 FAIL. Això era una versió intermèdia, no el resultat final.

He tornat a fer una còpia independent del binari de les 00:39:46 (hora de Madrid), SHA256 ADA91A3BE40114477C6E913216A5BF80E735A7A52EE92C0AC9CD9B37795F73F0. La seva ordre synth dona **41 PASS i 1 FAIL sobre 42 casos**. El procés encara retorna 0.

Millores confirmades en aquest segon snapshot:
- Kicks a 48/44,1 kHz: 12/12 moments, 0 esdeveniments extra, 12/12 famílies correctes i 0 famílies sobrants.
- Snares: 12/12, 0 extra i famílies correctes a totes dues freqüències.
- Loop de 64 moments: 64/64 i 0 esdeveniments extra a totes dues freqüències (abans 31/30 extra).
- Inici de clip i clip curt amb click ara passen.

No cal perseguir els 12 kicks extra antics com si continuessin presents. En dbg del primer binari apareixien exactament al voltant de 400 ms després de cada kick: 0,500→0,900; 1,001→1,401, etc. Coincideix amb la durada sintètica del kick. És compatible amb un transitori de tall al final del senyal de prova, no prova d'invenció aleatòria de bonus. El Synth.cs actual ja té Fade al final: valida separadament els estímuls i el detector.

## Pendents prioritaris confirmats

### 1. El PASS ignora les famílies sobrants

Harness/SynthTests.cs calcula famExtraWrong però no l'usa al veredicte. El segon snapshot ho mostra clarament:
- 32nd hats @120bpm: 64/64 moments, 0 esdeveniments extra, però 20 etiquetes Snare sobrants a 48 kHz i 23 a 44,1 kHz. Tot i així diu PASS.
- Loop 64: 2 i 4 etiquetes HiHat sobrants, respectivament, tot i dir PASS.

Separar explícitament **esdeveniments addicionals en el temps** de **famílies incorrectes en un moment correcte**. La integració multi-label podria convertir famílies sobrants en bonus extra encara que el recompte de moments sigui perfecte.

Per a estímuls sintètics controlats, comprovar precisió i recall per família i penalitzar etiquetes absents del ground truth. No donar èxit només perquè hi hagi un superset de la família esperada. Per música real, no afirmar aquesta mètrica sense anotacions.

### 2. Exit code

Program.Main en l'ordre synth retorna 0 incondicionalment. Agregar CaseResult.pass i retornar no zero si falla una prova obligatòria. FFTcheck també ha de tenir llindars i un veredicte si s'usa com a gate.

### 3. Fals positiu encara present

Plucks (tonal) a 44,1 kHz: 8/8 moments coneguts i 1 esdeveniment extra. No ajustar només al mateix estímul de calibratge: verificar diverses formes d'atac, freqüències, intensitats, durades i fases dins del hop, amb alguns casos reservats per validació.

## Precisions per a la integració

- Confirmo els bugs de fall-respawn i MusicEnvironmentController. MusicClock ha de conservar l'autoritat amb estats explícits; !isPlaying no distingeix final natural de pausa/stop/error.
- Desduplicar càmera segons temps/identitat musical, no només frame d'arribada: un frame lent pot executar diversos cops legítims. Evitar retardar tots els pulses per una finestra d'agrupació de 30 ms.
- Header runtime: songId + samples + frequency no valida una revisió. Comparar revisió/hash del perfil amb l'índex remot publicat i versions compatibles. No descodificar tot el PCM al mòbil només per recalcular el hash.
- PackSeparately amb àudio i perfil com a entrades separades no crea automàticament un bundle per parella. Verificar layout real si s'opta per PackTogetherByLabel i que els conjunts de labels siguin exactament els mateixos.
- Mateix bundle per cançó simplifica coherència, però força redescàrrega d'àudio si només canvia el perfil. És un tradeoff, no una necessitat inherent del pipeline.
- Triar un contracte clar entre perfil complet i perfil cru amb derivats recomputats: mesurar aquests derivats en càrrega i evitar doble suavització.
- ~270 MB d'assignacions acumulades no equival a 270 MB de memòria viva màxima. Mesurar separadament GC, peak memory i PCM, sobretot si se solapen semàntica i ritme.
- Els callbacks de progrés de diversos workers poden arribar fora d'ordre encara que usin Interlocked.Increment. Publicar progrés monòton al main thread.
- Invalidar peticions abans de publicar SongProfileReadyEvent, no només abans del callback final. GameSession no ha d'acceptar perfils d'una selecció antiga.

L'informe anterior amb els punts de disseny és `codex_review_initial.md`; les fallades sintètiques que hi apareixen corresponen al primer snapshot i queden actualitzades per aquest document. Les sortides íntegres de les dues versions són `codex_synth_initial.txt` i `codex_synth_latest.txt`.
