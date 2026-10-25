# Revisió de les primeres escenes · 23 d’octubre de 2026

**Aplicada al projecte Unity i comprovada.** Primera passada amb els models disponibles; els objectes encara no exportats queden pendents de la segona passada.

[Veure totes les comparatives](comparatives_revisades/comparatives.html)

## Resultat

- 60 escenes: cinc qualitats per cadascun dels dotze compositors.
- 24 captures de Unity: High i Ultra de cada compositor.
- 173 comprovacions de càrrega, descàrrega i combat passades; zero fallades.
- 84 tipus de model exportat i 413 variants comprovades. Zero fonts inexistents o d’altres compositors.
- 528 col·locacions del manifest, inclosos els cinc complements de Monteverdi. Aquest recompte exclou el muntatge original de Monteverdi, que es conserva.
- Mapes specular de terra ajornats, tal com s’ha demanat. Es mantenen albedo i normal.

## Canvis visuals

Bach: orgue al fons i desplaçat a l’esquerra, vitralls alts, canelobres i consoles laterals; escala revisada amb marcadors d’1,80 m. Interior sense cel, llum càlida a l’orgue i al centre amb aportacions fredes de les finestres.

Beethoven: escala de graons, estàtues, columnes, cortines i balconades revisada; sostre rectificat conservant la proporció rectangular dels seus mòduls. Materials de marbre corregits com a pedra.

Brahms: auditori amb sis files de seients, balconada, aranya central i piano a la dreta. Piano separat de la zona de combat; llum central més càlida.

Chopin: referència confirmada per l’usuari i vinculada al manifest. Finestres, pilastres, cornises, sostre i mobiliari propi col·locats; sense afegir l’aranya ni la taula de saló que encara no estan exportades.

Handel: disposició de retrats, finestres, clavicèmbal i mobiliari ajustada a la referència. Escultures reduïdes a mida de sobretaula damunt les consoles; partitures damunt els faristols. Els vidres roses de l’albedo exportat s’han corregit mitjançant el material, conservant els marcs i la textura originals.

Haydn: finestres, columnes, cortinatges i piano revisats; busts reduïts i col·locats sobre els pedestals ja exportats. El seu jardí torna a ser visible a través de les finestres. Tancaments clars i obertures ajustades al marc.

Pachelbel: font al fons, estàtues i columnes al perímetre, cadires i canelobres propis. La vegetació i les arcades que encara no existeixen no s’han substituït amb objectes d’altres compositors.

Mozart, Tchaikovsky, Vivaldi i Wagner: terra i fons disponibles, sense models d’escenari exportats encara. La projecció dels paisatges s’ha ajustat perquè els edificis deixin de veure’s gegants i tallats.

Monteverdi: arquitectura, terra, llum i skybox del muntatge existent conservats. S’ha definit una càmera de comparació explícita; això no modifica la càmera del combat.

## Correccions del muntatge

L’ajust de dimensions dels sostres girats intercanviava alçada i profunditat. Ara les dimensions s’apliquen als eixos de l’arena mitjançant un pare sense gir. Les cinc qualitats conserven la mateixa composició i només carreguen els models de la qualitat seleccionada, amb la substitució existent quan falta una variant.

Els fons proporcionats són vistes de paisatge amb un camp visual limitat. S’ha afegit un material de fons amb cobertura angular ajustable, en lloc d’estirar-los verticalment com si fossin fotografies esfèriques completes. Les imatges originals no s’han retocat. Monteverdi conserva el seu material anterior.

Les captures esperen la generació dels reflexos i utilitzen la configuració High o Ultra corresponent. Els marcadors temporals d’escala no es desen a les escenes.

## Recursos i límits pendents

Sense exportació actual: paret de marbre de Bach; aranya i taula de saló de Chopin; aranya i jardinera de Haydn. Mozart, Tchaikovsky, Vivaldi i Wagner encara no tenen models exportats. Les zones sense decoració continuen incompletes respecte a les referències.

Algunes estàtues i figures dels retrats dels FBX tenen els braços en creu. És una forma del model exportat que aquesta passada de col·locació i materials no resol. Està assenyalada a les comparatives; no s’ha dissimulat ni substituït amb models aliens.

Un vitrall de Bach mostrava el revers amb aspecte de relleu. En les posicions visibles s’ha utilitzat l’altre vitrall disponible del mateix compositor.

L’última auditoria manté quatre substitucions de qualitat a Chopin i tres a Haydn perquè aquestes variants encara no estan exportades. S’utilitza el mateix criteri del sistema existent: millor inferior disponible, o menor superior quan no n’hi ha cap d’inferior.

## Inventari del manifest

| Compositor | Tipus de model | Col·locacions |
|---|---:|---:|
| Bach | 8 | 27 |
| Beethoven | 11 | 67 |
| Brahms | 13 | 190 |
| Chopin | 16 | 73 |
| Handel | 16 | 91 |
| Haydn | 9 | 60 |
| Monteverdi | 5 | 5 |
| Mozart | 0 | 0 |
| Pachelbel | 6 | 15 |
| Tchaikovsky | 0 | 0 |
| Vivaldi | 0 | 0 |
| Wagner | 0 | 0 |

Les xifres de Monteverdi només compten els cinc complements. L’escenografia anterior queda fora d’aquest manifest i es conserva.

## Comprovació

La prova del carregador de producció s’ha executat en una escena de depuració aïllada: cinc qualitats de Monteverdi, Low i Ultra dels altres compositors, materials, entorns interiors/exteriors, neteja de càrregues, cancel·lació, compatibilitat amb les arenes antigues i preparació, repetició i sortida d’un combat real. S’ha restaurat el proveïdor d’Addressables i s’ha sortit del mode de joc sense tancar Unity.

L’informe és `MusicGame/Logs/ComposerArenaLifecycleProbe.txt`: 173 PASS i cap FAIL. Les escenes generades han passat també la comprovació de dependències pròpies, selecció de qualitat i espai lliure central.

No s’han construït ni publicat bundles remots. Aquesta comprovació funcional no significa que els models pendents o les formes incorrectes ja s’assemblin a les referències.
