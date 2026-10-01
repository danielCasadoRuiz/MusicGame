using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives each of the 12 production composers its OWN lightweight body configuration, in the existing
/// Recipe → Identity → BodyMorphProfile chain (no meshes duplicated — every recipe shares the one
/// BaseAvatar_MakeHuman / canonical FBX / skeleton / materials):
///   Avatar/MakeHuman/Content/Composers/Body/BodyMorph_<Composer>.asset   (Gender Male, Weight, Muscle)
///   Avatar/MakeHuman/Content/Composers/Identities/Identity_<Composer>.asset (that profile + shared face/hair)
///   Avatar/MakeHuman/Content/Composers/Recipes/Avatar_<Composer>.asset      (that identity + shared base avatar)
/// Every tier (and defaultConfig) of a composer that still points at the shared generic recipe is
/// re-pointed to that composer's recipe; a tier with its own custom recipe is preserved. Tier-specific
/// recipes remain possible later (OpponentLevelConfig.avatarRecipe is per tier).
///
/// PROVISIONAL values live in the table below and are written ONLY when a profile is first created —
/// after that, edit the BodyMorph_<Composer> asset directly; re-running never overwrites edits.
/// BodyMorphValues convention: Weight 0 = slim, 0.5 = base, 1 = heavy; Muscle 0 = base, 1 = strong.
///
/// Also a light validation: independence of every composer's values, one edit-mode lineup render
/// (Logs/ComposerBodies.png) and a check that a renderer registered after the body was applied (a
/// "late wearable" carrying the canonical shape keys) receives the same weights.
/// Menu: Tools > MusicGame > Avatar > Setup Composer Bodies. Batch: ComposerBodySetup.SetupFromCommandLine.
/// </summary>
public static class ComposerBodySetup
{
    private const string Root = "Assets/_Project/Avatar/MakeHuman/Content/Composers";
    private const string GenericRecipePath   = "Assets/_Project/Avatar/MakeHuman/Content/Recipes/Avatar_MakeHuman_TestMale.asset";
    private const string GenericIdentityPath = "Assets/_Project/Avatar/MakeHuman/Content/Identities/Identity_MakeHumanMale.asset";

    // name, weight (0 slim .. 0.5 base .. 1 heavy), muscle (0 .. 1). All male (Gender 0) for now.
    // The canonical MakeHuman targets are mild even at 100% (waist ≈ 0.286 m slim .. 0.323 m heavy,
    // see the silhouette report), so the composers use most of each axis to read as different bodies.
    private static readonly (string name, float weight, float muscle)[] Bodies =
    {
        ("Vivaldi",     0.02f, 0.00f),
        ("Chopin",      0.08f, 0.02f),
        ("Mozart",      0.16f, 0.05f),
        ("Monteverdi",  0.25f, 0.08f),
        ("Tchaikovsky", 0.33f, 0.25f),
        ("Haydn",       0.40f, 0.25f),
        ("Pachelbel",   0.50f, 0.30f),
        ("Beethoven",   0.55f, 0.85f),
        ("Handel",      0.72f, 0.55f),
        ("Bach",        0.80f, 0.40f),
        ("Wagner",      0.90f, 0.75f),
        ("Brahms",      0.97f, 0.22f),
    };

    [MenuItem("Tools/MusicGame/Avatar/Setup Composer Bodies")]
    public static void SetupMenu() => Debug.Log(Setup(render: false));

    public static void SetupFromCommandLine()
    {
        // -overwriteBodies rewrites existing profiles from the table (otherwise manual edits win).
        string report = Setup(render: true, overwrite: System.Environment.GetCommandLineArgs().Contains("-overwriteBodies"));
        File.WriteAllText("Logs/ComposerBodySetup.txt", report);
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Setup(bool render, bool overwrite = false)
    {
        var sb = new StringBuilder("[ComposerBodySetup]\n");
        foreach (var sub in new[] { "Body", "Identities", "Recipes" }) EnsureFolder($"{Root}/{sub}");

        var genericRecipe   = AssetDatabase.LoadAssetAtPath<AvatarRecipeSO>(GenericRecipePath);
        var genericIdentity = AssetDatabase.LoadAssetAtPath<AvatarIdentitySO>(GenericIdentityPath);
        var roster = Resources.Load<AppConfigSO>("AppConfig")?.opponentRoster;
        if (genericRecipe == null || genericIdentity == null || roster == null)
            return sb.AppendLine("ERROR: generic recipe/identity or roster missing").ToString();

        var recipes = new System.Collections.Generic.Dictionary<string, AvatarRecipeSO>();
        foreach (var (name, weight, muscle) in Bodies)
        {
            var profile = LoadOrCreate<BodyMorphProfileSO>($"{Root}/Body/BodyMorph_{name}.asset", out bool newProfile);
            if (newProfile || overwrite)
            {
                profile.baseType = BodyBaseType.Male;
                profile.overrideGender = false; // Gender 0 (Male) — the continuous axis is untouched
                profile.weight = weight;
                profile.muscle = muscle;
                EditorUtility.SetDirty(profile);
            }

            var identity = LoadOrCreate<AvatarIdentitySO>($"{Root}/Identities/Identity_{name}.asset", out bool newIdentity);
            if (newIdentity)
            {
                identity.id = $"composer_{name.ToLowerInvariant()}";
                identity.face = genericIdentity.face;
                identity.hair = genericIdentity.hair;
            }
            identity.bodyMorphProfile = profile;
            EditorUtility.SetDirty(identity);

            var recipe = LoadOrCreate<AvatarRecipeSO>($"{Root}/Recipes/Avatar_{name}.asset", out bool newRecipe);
            if (newRecipe)
            {
                recipe.baseAvatar = genericRecipe.baseAvatar;
                recipe.items = genericRecipe.items?.ToArray() ?? System.Array.Empty<WearableItemSO>();
                recipe.bodyOverride = new BodyMorphOverride(); // no overrides: the profile is the source of truth
                recipe.colorOverrides = genericRecipe.colorOverrides?.ToArray() ?? System.Array.Empty<AvatarColorOverride>();
            }
            recipe.identity = identity;
            EditorUtility.SetDirty(recipe);
            recipes[name] = recipe;
        }

        // Point every tier still on the shared generic recipe at the composer's own one.
        int repointed = 0;
        foreach (var opponent in roster.opponents)
        {
            if (opponent == null || !recipes.TryGetValue(opponent.displayName, out var recipe))
            {
                sb.AppendLine($"  ERROR  roster opponent '{opponent?.displayName}' has no body entry");
                continue;
            }
            foreach (var level in opponent.levels.Append(opponent.defaultConfig))
            {
                if (level == null) continue;
                if (level.avatarRecipe == null || level.avatarRecipe == genericRecipe) { level.avatarRecipe = recipe; repointed++; }
            }
            EditorUtility.SetDirty(opponent);
        }
        AssetDatabase.SaveAssets();
        sb.AppendLine($"  {repointed} tier config(s) re-pointed from the generic recipe to their composer's own recipe");

        // Validation: every tier resolves its composer recipe; values male, distinct and independent.
        foreach (var opponent in roster.opponents)
        {
            var recipe = recipes[opponent.displayName];
            var body = recipe.ToRuntime().Identity.Body;
            bool tiersOk = opponent.levels.All(l => l != null && l.avatarRecipe == recipe);
            bool ownProfile = recipes.Values.Count(r => r.identity.bodyMorphProfile == recipe.identity.bodyMorphProfile) == 1;
            Check(sb, $"{opponent.displayName,-11} Gender {body.Gender:0.00}  Weight {body.Weight:0.00}  Muscle {body.Muscle:0.00}  " +
                      $"({opponent.levels.Length} tiers → {recipe.name}, own profile {ownProfile})",
                  tiersOk && ownProfile && body.Gender == 0f);
        }

        CheckLateRegistration(sb, recipes["Pachelbel"]);
        MeasureSilhouettes(sb, recipes);
        if (render) RenderLineup(sb, recipes);
        return sb.ToString();
    }

    /// <summary>Baked torso widths (waist / chest, metres) per composer + the axis extremes, so the
    /// silhouette differences are measured, not only eyeballed.</summary>
    private static void MeasureSilhouettes(StringBuilder sb, System.Collections.Generic.Dictionary<string, AvatarRecipeSO> recipes)
    {
        var instance = Assemble(recipes["Pachelbel"], Vector3.zero, out var go);
        try
        {
            string Measure(BodyMorphValues v)
            {
                instance.ApplyBody(v);
                float waist = 0f, chest = 0f, depth = 0f;
                foreach (var smr in instance.BodyRenderers)
                {
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    var m = smr.transform.localToWorldMatrix;
                    foreach (var local in baked.vertices)
                    {
                        var p = m.MultiplyPoint3x4(local);
                        if (Mathf.Abs(p.x) > 0.25f) continue; // torso only (arms out in rest pose)
                        if (p.y > 1.00f && p.y < 1.06f) waist = Mathf.Max(waist, Mathf.Abs(p.x));
                        if (p.y > 1.25f && p.y < 1.30f) { chest = Mathf.Max(chest, Mathf.Abs(p.x)); depth = Mathf.Max(depth, Mathf.Abs(p.z)); }
                    }
                    Object.DestroyImmediate(baked);
                }
                return $"waist {waist * 2f:0.000}  chest {chest * 2f:0.000}  chestDepth {depth:0.000}";
            }
            sb.AppendLine("  silhouette (m):");
            sb.AppendLine($"    [ref] Weight 0   : {Measure(new BodyMorphValues { Weight = 0f })}");
            sb.AppendLine($"    [ref] base       : {Measure(new BodyMorphValues { Weight = 0.5f })}");
            sb.AppendLine($"    [ref] Weight 1   : {Measure(new BodyMorphValues { Weight = 1f })}");
            sb.AppendLine($"    [ref] Muscle 1   : {Measure(new BodyMorphValues { Weight = 0.5f, Muscle = 1f })}");
            foreach (var (name, _, _) in Bodies)
                sb.AppendLine($"    {name,-11}: {Measure(recipes[name].ToRuntime().Identity.Body)}");
        }
        finally { instance.Dispose(); if (go != null) Object.DestroyImmediate(go); }
    }

    // A clone of the body mesh plays the role of a garment exported with the canonical shape keys:
    // registered AFTER the body values were applied, it must immediately carry the same weights.
    private static void CheckLateRegistration(StringBuilder sb, AvatarRecipeSO recipeSO)
    {
        var instance = Assemble(recipeSO, Vector3.zero, out var go);
        try
        {
            instance.ApplyBody(new BodyMorphValues { Gender = 0f, Weight = 0.8f, Muscle = 0.6f });
            var body = instance.BodyRenderers[0];
            var garmentGO = new GameObject("LateGarment");
            garmentGO.transform.SetParent(go.transform, false);
            var garment = garmentGO.AddComponent<SkinnedMeshRenderer>();
            garment.sharedMesh = body.sharedMesh;
            garment.bones = body.bones;
            var noShapesGO = new GameObject("RigidPart");
            noShapesGO.transform.SetParent(go.transform, false);
            var noShapes = noShapesGO.AddComponent<SkinnedMeshRenderer>();
            noShapes.sharedMesh = new Mesh { name = "NoShapes" };

            int registered = instance.RegisterMorphTargets(garmentGO, "LateGarment") + instance.RegisterMorphTargets(noShapesGO, "RigidPart");
            bool same = true;
            for (int i = 0; i < body.sharedMesh.blendShapeCount; i++)
            {
                string shape = body.sharedMesh.GetBlendShapeName(i);
                int gi = garment.sharedMesh.GetBlendShapeIndex(shape);
                same &= gi >= 0 && Mathf.Abs(garment.GetBlendShapeWeight(gi) - body.GetBlendShapeWeight(i)) < 0.01f;
            }
            Check(sb, "late-registered garment gets the current body weights; a renderer without shape keys is ignored",
                  registered == 1 && same && garment.GetBlendShapeWeight(garment.sharedMesh.GetBlendShapeIndex("MaleHeavy")) > 50f);

            instance.ApplyBody(new BodyMorphValues { Gender = 0f, Weight = 0.2f, Muscle = 0f });
            int slim = garment.sharedMesh.GetBlendShapeIndex("MaleSlim");
            Check(sb, "later ApplyBody updates the garment too (Slim follows)", Mathf.Abs(garment.GetBlendShapeWeight(slim) - 60f) < 0.5f);
            Object.DestroyImmediate(noShapes.sharedMesh);
        }
        finally { instance.Dispose(); if (go != null) Object.DestroyImmediate(go); }
    }

    private static void RenderLineup(StringBuilder sb, System.Collections.Generic.Dictionary<string, AvatarRecipeSO> recipes)
    {
        var built = new System.Collections.Generic.List<(AvatarInstance, GameObject)>();
        try
        {
            float spacing = 1.15f;
            int i = 0;
            foreach (var (name, _, _) in Bodies)
            {
                var inst = Assemble(recipes[name], new Vector3((i - (Bodies.Length - 1) * 0.5f) * spacing, 0f, 0f), out var go);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the camera
                built.Add((inst, go));
                i++;
            }
            var camGO = new GameObject("LineupCam");
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.0f;
            cam.transform.position = new Vector3(0f, 1.1f, -8f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.24f, 0.28f);
            var lightGO = new GameObject("LineupLight");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGO.transform.rotation = Quaternion.Euler(30f, 20f, 0f);
            const int w = 3300, h = 520;
            var rt = new RenderTexture(w, h, 24);
            cam.aspect = (float)w / h;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes("Logs/ComposerBodies.png", tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(camGO); Object.DestroyImmediate(lightGO);
            sb.AppendLine("  lineup: Logs/ComposerBodies.png (left→right = table order, slim → heavy)");
        }
        finally
        {
            foreach (var (inst, go) in built) { inst.Dispose(); if (go != null) Object.DestroyImmediate(go); }
        }
    }

    private static AvatarInstance Assemble(AvatarRecipeSO recipeSO, Vector3 position, out GameObject go)
    {
        var recipe = recipeSO.ToRuntime();
        var prefab = recipe.BaseAvatar.baseAvatarPrefab.editorAsset;
        go = Object.Instantiate(prefab, position, Quaternion.identity);
        go.name = $"Check_{recipeSO.name}";
        var instance = new AvatarInstance { Recipe = recipe, FinalIdentity = recipe.Identity, BodyMorphValues = recipe.Identity.Body };
        AvatarFactory.AssembleBody(instance, go, recipe);
        return instance;
    }

    private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        created = asset == null;
        if (created)
        {
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    private static void Check(StringBuilder sb, string name, bool ok) => sb.AppendLine($"  {(ok ? "PASS" : "ERROR")}  {name}");
}
