# Canonical aprovat (revisió v12)

`canonical_shapes.json` conté les vuit formes finals aprovades en T-pose: MaleBase, FemaleBase i els extrems Slim/Heavy/Muscle de cada sexe. MakeHumanApprovedShapes les aplica durant MakeHumanBodyBaker.Bake, abans de generar els presets i l'FBX. Per regenerar cal utilitzar el builder habitual de Unity; no exportar directament les dades antigues de Library.

Es conserven la base masculina, topologia, UV, pesos, esquelet de 53 ossos i els set noms de blendshape. Els canals femenins estan referits a la nova FemaleBase (Gender). Les màscares graduals de coll, mans i peus ja estan incorporades a les formes.

L'FBX generat ha de continuar dient-se MakeHuman_Canonical.fbx. La còpia del fitting és a AssetsMaterial/3D_pipeline/_System/FittingAndRigging/Avatar/MakeHuman_Canonical.fbx i ha de coincidir amb Generated/MakeHuman_Canonical.fbx. Si es modifiquen les formes cal regenerar també els morphs de les peces que els utilitzen. No canviar empremtes de fit_record.json sense verificar neutral, rig i fitting.

Validació d'aquesta instal·lació: 84 comprovacions d'edició i 37 en joc superades; GUID, identificadors interns i configuració d'importació conservats.
SHA256 FBX: 955d5e83294f03b73868f2c4003afe5f3f5ba1b7d10a33522baa30276ec53153.
