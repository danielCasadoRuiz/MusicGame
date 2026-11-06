# Vegetació de Monteverdi i Pachelbel

Implementades sis construccions completes d’enfiladissa per compositor, amb branques contínues i acumulacions irregulars. Sis fulles i quatre flors en PNG amb alfa. Flors vermelles i crema integrades als gerros de Monteverdi, seguint la referència original. Conservades les posicions dels elements existents.

Aplicació: les 30 escenes dels dos compositors (tres versions i cinc qualitats). Pachelbel conserva la substitució d’enfiladisses; els nous rams són de Monteverdi.

Atlas compartit 2048×1536, material estàndard URP/Lit amb retall alfa, sense emissió. Geometria amb normals correctes per les dues cares. Les plantes reben llum de probes i projecten ombres al bake. La prova aïllada confirma que les ombres segueixen la silueta de les fulles i flors.

Proves de Monteverdi High i Mid en mode joc: correctes, 255 probes, dos lightmaps, MSAA 4 i una llum en temps real. La prova d’il·luminació es manté a les escenes Debug/MonteverdiLighting_High_Recommended i Mid_Recommended; aquesta feina no ha fet bake de totes les escenes de producció.

Les enfiladisses passen de 4.000–27.200 triangles segons qualitat a aproximadament 460–1.640 segons construcció i qualitat. Això és una mesura de geometria, no una prova de FPS en mòbil. L’arquitectura i la llum general encara poden diferir de la imatge de referència.

Assets del joc: Assets/_Project/Art/Shared/ReferenceFlora. Fonts de generació i atlas: tools/scenario-quality/reference-flora-20261105. Les dependències es registren separades per compositor i qualitat. Cal reconstruir els paquets de contingut abans de distribuir una nova build.

Les captures són renders reals de Unity. La comparativa abans/ara usa la mateixa càmera. La referència original és una vista diferent.

PNG generats amb el motor d’imatge. Prompts complets: [fulles](prompt_fulles.txt) i [flors](prompt_flors.txt). L’arxiu original de l’usuari queda intacte. Còpies prèvies guardades a les carpetes de treball ivy_replacement/backup i reference_flora/backup.

Verificació final passada: 30 escenes, dependències de qualitat correctes i un únic material compartit. Informes inclosos a packaging_verification.txt i als dos Play_check.txt.
