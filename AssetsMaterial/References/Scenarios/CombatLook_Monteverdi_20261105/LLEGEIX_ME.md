# Monteverdi: proposta visual de combat

Obrir Assets/_Project/Scenes/Debug/Monteverdi_CombatLook_Proposal.unity a Unity i prémer Play. Es munten el jugador Classical i Monteverdi amb les receptes reals del joc, en postura de guàrdia. No s’inicia cap partida ni es modifica el progrés del jugador.

El tractament i les flames també s’han aplicat a les quinze escenes de producció de Monteverdi (tres versions i cinc qualitats), amb còpies prèvies a combat_look/backup. Aquesta passada no aplica el look a altres compositors.

Postprocessament: tonemapping Neutral, contrast +5, saturació −7, temperatura +3, vinyeta 0,16 amb transició ampla. Bloom amb llindar 1,12, intensitat 0,12 en Mid i 0,18 en qualitats superiors; desactivat en Low. Filtratge de qualitat alta desactivat, resolució inicial a un quart i tres iteracions. No s’ha afegit AO en temps real, profunditat de camp, motion blur ni aberració cromàtica.

Dues llums de personatges sense ombres, separades del decorat amb rendering layers. S’ha definit una capa de renderització CombatFighters. No es canvien les capes de física. Els shaders propis del terra i el vellut s’han adaptat perquè respectin aquesta separació. Les màscares dels personatges i els ajustos de càmera es restauren quan es descarrega l’arena.

Espelmes: un ParticleSystem per canelobre amb cinc punts d’emissió calculats sobre la seva malla, material i textura de flama compartits amb el sistema existent, màxim 32 partícules per sistema. Sense afegir una llum en temps real per espelma. Retirada la geometria de les petites flames estàtiques originals; es conserva el cos del canelobre i les seves textures. Les malles modificades s’agrupen per qualitat per evitar carregar les cinc versions juntes.

Les captures són de Unity en mode joc amb els avatars reals. L’especial usa FightFireballVisual; l’aura usa FighterAuraFx. Les imatges d’efectes mostren els efectes reals en una prova visual, sense simular un combat complet. La comparativa principal conserva la càmera, la postura i l’estat de les partícules; les dues imatges ja utilitzen les espelmes animades. Compara només el tractament visual i la llum dels personatges. La prova de llum activada/desactivada confirma que les llums de personatges no modifiquen els píxels del decorat fora de les regions dels dos lluitadors (vegeu lighting_isolation.json).

La prova reutilitza el bake i les probes del pilot de Monteverdi. No s’ha fet un bake nou de les quinze escenes ni es presenta aquesta feina com una mesura de rendiment de mòbil. Els assets globals de qualitat, els ajustos de gràfics i la capa de física es conserven.

Assets: Assets/_Project/Art/Arenas/Monteverdi/LightingPilot/CombatLook. Les dependències estan registrades a Addressables; cal reconstruir els paquets abans de distribuir una build.

Referència tècnica: [postprocessament mòbil a URP](https://docs.unity.com/en-us/engine/6000.3/manual/post-processing-and-full-screen-effects/urp/integration-with-post-processing) i [Bloom](https://docs.unity.com/en-us/engine/6000.0/manual/post-processing-and-full-screen-effects/urp/effect-list/bloom).
