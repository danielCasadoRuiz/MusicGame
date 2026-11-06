# Vegetació dels escenaris

Revisades les 12 imatges base. Monteverdi, Pachelbel, Mozart i Haydn tenen vegetació important a primer pla: s’hi han integrat plantes sobre plans amb transparència en les tres versions i les cinc qualitats (60 escenes). Els altres vuit escenaris es conserven perquè les referències no hi mostren vegetació de primer pla comparable. No s’han substituït paisatges del fons, escultures ni ornaments florals tallats en pedra.

Monteverdi: arbustos de flors vermelles i crema; eliminada la geometria activa dels dos antics tipus de gerro amb flors, substituint el recipient amb un gerro de bronze existent. Conservades les seves posicions. Els rams inicials han estat substituïts.
Pachelbel: eliminats els arbustos GardenTopiary modelats dels renderitzadors actius. Conservats els recipients de marbre i les seves posicions. Cada planta s’ancora al gerro o al terra. Es mantenen les enfiladisses de l’anterior feina.
Mozart: rosers rosats i blancs en gerros de bronze ja disponibles al projecte, prop de les finestres i del piano.
Haydn: arbustos amb flors petites blanques i crema, en recipients existents al projecte.

Quatre construccions completes per compositor, cadascuna amb cinc densitats de geometria. Cada arbust és una sola malla, sense un objecte del joc per fulla. Cost: 1208–3444 triangles per arbust segons qualitat i construcció. Les branques, fulles i flors tenen profunditat i orientacions diferents; no són cartells encarats sempre a càmera.

Material estàndard URP/Lit amb retall alfa, sense emissió ni brillantor artificial. Atles compartit 2048×1536 amb mipmaps que preserven el contorn i compressió ASTC per Android. Dues cares amb normals correctes. UV2 disponibles. S’ha afegit una superfície de terra fosca de 20 triangles dins dels recipients, amb material compartit. La tècnica és compatible amb probes i ombres al bake; no s’ha fet un bake nou de les 60 escenes.

Una nova generació amb l’eina d’imatges integrada ha proporcionat quatre elements de fullatge i quatre flors. Reutilitzades les quatre flors anteriors. PNG individuals, atles i prompt inclosos. Fonts de les flors reutilitzades: la carpeta anterior Flora_Monteverdi_20261105.

Comprovacions: 60 escenes sense geometria vegetal antiga activa, material i UV2 correctes, cap arbust per sobre de la qualitat de la seva escena. Prova de regeneració dels quatre mestres en Low i Ultra. Dependències agrupades per compositor i qualitat. Els fitxers originals es conserven per recuperar-los; no s’han esborrat els models font. Les configuracions globals de gràfics no s’han canviat.

Assets: Assets/_Project/Art/Shared/ReferenceFlora. Eina d’edició: ScenarioBotanicalUpgrade. Fonts reproduïbles: tools/scenario-quality/scenario-botanical-20261105. Còpies prèvies: carpeta de treball flora_all/backup.

Les captures mostren la il·luminació actual de cada escena. Aquesta passada tracta la vegetació; no equival a una reconstrucció completa de l’arquitectura o del look de les referències. El rendiment en mòbil no s’ha mesurat. Cal reconstruir els paquets Addressables abans de distribuir una nova build.
