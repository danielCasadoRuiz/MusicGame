# Validació — versió 1.1, 2026-10-02

Entorn d’execució: Linux, Python 3, Blender 4.5.3 LTS, CPU, vuit fils.

La recepta per defecte s’ha ajustat al procés base de la sabata/pantalons: QuadriFlow seed 42, Angle Based, marge UV 32/4096, bake local 4K, sense activar les correccions específiques del tors de l’armilla. Les reparacions estan condicionades als controls, i no es promet la mateixa geometria exacta dels originals lliurats.

## Comprovacions executades

- Sintaxi del llançador i dels auxiliars; ajuda del CLI.
- Prevalidació dels cinc GLB originals.
- Imports reals d’un lot amb la sabata i els pantalons, sense indicar perfils ni carpeta de sortida: carpeta `resultats/_work` creada automàticament.
- GLB invàlid afegit al mateix lot: registre d’error per peça, la resta es processen, sortida de procés amb codi 1.
- Represa del lot sense repetir les inspeccions completades.
- Camins de carpeta amb espais i accents.
- Verificació i publicació a la carpeta principal de resultats d’un GLB de sabata prèviament processat, amb els controls reals de reimportació i geometria final.
- Represa d’una peça acabada sense repetir passos i reconstrucció de la còpia final si s’ha eliminat, a partir del checkpoint verificat.

La prova de publicació reutilitza el checkpoint i GLB reals de la sabata. No s’ha presentat aquesta prova com una nova execució completa de retopologia, UV i bake.

Les comprovacions de la versió 1.0 també van executar importació, bloqueig per configuració incompatible, protecció de sortides i detecció de diagnòstics modificats. La validació geomètrica de l’armilla corregida va passar amb 50.872 triangles; no certifica la qualitat visual del coll i vores.

## Límits de la validació

No s’ha tornat a executar tot el flux consolidat de zero sobre les cinc peces ni s’ha provat el BAT en Windows. L’autodetecció de Blender a Windows i l’arrossegament de carpetes s’han implementat, però no s’han comprovat en una sessió Windows.

Els forats visuals reportats als pantalons no s’han corregit i verificat. Els controls topològics d’aquell model passen, però això no equival a una validació visual dels plecs inferiors.
