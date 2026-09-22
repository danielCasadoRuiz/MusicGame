#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot Editor sweep over every Avatar-module asset in the project — BaseAvatarDefinitionSO,
/// WearableItemSO, HairItemSO, AvatarIdentitySO, AvatarRecipeSO — logging clear warnings/errors for
/// anything AvatarFactory would otherwise only discover at runtime (missing AvatarVisualPart, a
/// declared morph channel with no matching blendshape, a GenderSpecific variant declaring the wrong
/// gender's channel, a recipe with conflicting item slots or a Wearable that doesn't exist for its own
/// Identity's gender, a color override targeting an item the recipe doesn't even equip). Never throws/
/// blocks the build — this is diagnostic tooling for iterating on still-in-progress content (task's
/// own explicit "encara no tinc els assets finals" context), not a build-time gate.
///
/// Uses AssetReferenceGameObject.editorAsset (Editor-only, synchronous) to peek at referenced prefabs
/// without going through Addressables' real runtime load path — safe to run any time, including
/// outside Play Mode.
/// </summary>
public static class AvatarValidator
{
    [MenuItem("Tools/MusicGame/Validate Avatars")]
    public static void Validate()
    {
        int warnings = 0;

        warnings += ValidateBaseAvatars();
        warnings += ValidateItems();
        warnings += ValidateHair();
        warnings += ValidateRecipes();

        Debug.Log(warnings == 0
            ? "[AvatarValidator] Clean — no issues found."
            : $"[AvatarValidator] Finished — {warnings} issue(s) logged above.");
    }

    private static readonly MorphChannel[] AllBodyChannels =
    {
        MorphChannel.MaleSlim,   MorphChannel.MaleHeavy,   MorphChannel.MaleMuscle,
        MorphChannel.FemaleSlim, MorphChannel.FemaleHeavy, MorphChannel.FemaleMuscle,
    };

    private static int ValidateBaseAvatars()
    {
        int warnings = 0;
        foreach (var baseAvatar in FindAssets<BaseAvatarDefinitionSO>())
        {
            string label = $"BaseAvatarDefinitionSO '{baseAvatar.name}'";

            var prefab = ResolveEditorPrefab(baseAvatar.baseAvatarPrefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label} has no baseAvatarPrefab assigned.");
                warnings++;
                continue;
            }

            var visualPart = prefab.GetComponentInChildren<AvatarVisualPart>();
            if (visualPart == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label} prefab '{prefab.name}' has no AvatarVisualPart — AvatarFactory cannot resolve its skeleton/regions/morphs.");
                warnings++;
                continue;
            }

            if (visualPart.rootBone == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label} prefab '{prefab.name}' has no rootBone — AvatarSkeletonMapper will fall back to the prefab root itself.");
                warnings++;
            }
            if (visualPart.skinnedRenderers == null || visualPart.skinnedRenderers.Length == 0)
            {
                Debug.LogWarning($"[AvatarValidator] {label} prefab '{prefab.name}' has no skinnedRenderers — Weight/Muscle morphs will have nothing to apply to.");
                warnings++;
            }
            if (visualPart.skinToneRenderers == null || visualPart.skinToneRenderers.Length == 0)
            {
                Debug.LogWarning($"[AvatarValidator] {label} prefab '{prefab.name}' has no skinToneRenderers — FaceProfileSO.skinTone will never be applied.");
                warnings++;
            }
            if (visualPart.regionMap == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label} prefab '{prefab.name}' has no AvatarRegionMap — equipped items' hiddenBodyRegions will be ignored (warning-not-crash at runtime, but nothing will actually get hidden).");
                warnings++;
            }

            if (baseAvatar.maleBase == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label} has no MaleBase preset assigned.");
                warnings++;
            }
            if (baseAvatar.femaleBase == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label} has no FemaleBase preset assigned.");
                warnings++;
            }

            warnings += ValidateBasePresetVertexCounts(label, "MaleBase", baseAvatar.maleBase, visualPart);
            warnings += ValidateBasePresetVertexCounts(label, "FemaleBase", baseAvatar.femaleBase, visualPart);
            warnings += ValidateBasePresetTopologyMatch(label, baseAvatar.maleBase, baseAvatar.femaleBase);

            // ONE shared prefab now drives BOTH genders (see BaseAvatarDefinitionSO's own doc) — its
            // mesh must carry ALL SIX blendshapes, not just one gender's three, since the same mesh
            // is reused for both with only its base vertex positions swapped.
            warnings += WarnMissingBlendShapes(label, prefab.name, visualPart.skinnedRenderers, AllBodyChannels, isError: false);
        }
        return warnings;
    }

    /// <summary>Every renderer the base prefab actually has must exist in `preset` with EXACTLY the
    /// same vertex count as the real mesh — task's own explicit "el nom del canal/topologia és el
    /// contracte, no busquis mappings alternatius" philosophy applied to base presets too.</summary>
    private static int ValidateBasePresetVertexCounts(string label, string presetLabel, AvatarMeshBasePresetSO preset, AvatarVisualPart visualPart)
    {
        if (preset == null || visualPart.skinnedRenderers == null) return 0;

        int warnings = 0;
        foreach (var smr in visualPart.skinnedRenderers)
        {
            if (smr == null || smr.sharedMesh == null) continue;

            var data = preset.FindRenderer(smr.name);
            if (data == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label}: {presetLabel} '{preset.name}' has no vertex data for renderer '{smr.name}'.");
                warnings++;
                continue;
            }
            if (data.vertices.Length != smr.sharedMesh.vertexCount)
            {
                Debug.LogError($"[AvatarValidator] {label}: {presetLabel} '{preset.name}' renderer '{smr.name}' " +
                                $"has {data.vertices.Length} vertices but the mesh has {smr.sharedMesh.vertexCount} — topology mismatch.");
                warnings++;
            }
        }
        return warnings;
    }

    /// <summary>MaleBase and FemaleBase must describe the SAME topology (task's own explicit "shared
    /// vertex count/topology/skeleton/bone weights/UVs" requirement) — only their vertex POSITIONS may
    /// differ, never which renderers exist or how many vertices each one has.</summary>
    private static int ValidateBasePresetTopologyMatch(string label, AvatarMeshBasePresetSO male, AvatarMeshBasePresetSO female)
    {
        if (male == null || female == null) return 0;

        int warnings = 0;
        foreach (var maleEntry in male.renderers)
        {
            var femaleEntry = female.FindRenderer(maleEntry.rendererName);
            if (femaleEntry == null)
            {
                Debug.LogWarning($"[AvatarValidator] {label}: MaleBase has renderer '{maleEntry.rendererName}' but FemaleBase doesn't — they must share the exact same topology.");
                warnings++;
                continue;
            }
            if (maleEntry.vertices.Length != femaleEntry.vertices.Length)
            {
                Debug.LogError($"[AvatarValidator] {label}: MaleBase/FemaleBase renderer '{maleEntry.rendererName}' vertex counts differ " +
                                $"({maleEntry.vertices.Length} vs {femaleEntry.vertices.Length}) — they must share the exact same topology.");
                warnings++;
            }
        }
        return warnings;
    }

    private static int ValidateItems()
    {
        int warnings = 0;
        foreach (var item in FindAssets<WearableItemSO>())
        {
            string label = $"WearableItemSO '{(string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName)}'";

            if (item.occupiedSlots == AvatarSlot.None)
            {
                Debug.LogWarning($"[AvatarValidator] {label} has occupiedSlots = None — it will never conflict-check against anything, which usually means a slot assignment was forgotten.");
                warnings++;
            }

            warnings += ValidateItemVariants(item, label);
        }
        return warnings;
    }

    private static int ValidateHair()
    {
        int warnings = 0;
        foreach (var hair in FindAssets<HairItemSO>())
        {
            string label = $"HairItemSO '{(string.IsNullOrEmpty(hair.displayName) ? hair.name : hair.displayName)}'";

            warnings += ValidateItemVariants(hair, label);

            // Rigid-attachment check only matters for whichever variant(s) actually apply.
            if (hair.variantMode == AvatarItemVariantMode.Shared)
            {
                warnings += ValidateHairAttachment(label, "SharedVariant", hair.sharedVariant, hair.attachmentBoneName);
            }
            else
            {
                warnings += ValidateHairAttachment(label, "MaleVariant", hair.maleVariant, hair.attachmentBoneName);
                warnings += ValidateHairAttachment(label, "FemaleVariant", hair.femaleVariant, hair.attachmentBoneName);
            }
        }
        return warnings;
    }

    private static int ValidateHairAttachment(string label, string variantLabel, AvatarItemVariant variant, string attachmentBoneName)
    {
        var prefab = ResolveEditorPrefab(variant?.prefab);
        if (prefab == null) return 0; // already reported by ValidateItemVariants

        var visualPart = prefab.GetComponentInChildren<AvatarVisualPart>();
        bool isSkinned = visualPart != null && visualPart.skinnedRenderers != null && visualPart.skinnedRenderers.Length > 0;
        if (!isSkinned && string.IsNullOrEmpty(attachmentBoneName))
        {
            Debug.LogWarning($"[AvatarValidator] {label}: {variantLabel} is a rigid prefab (no skinned AvatarVisualPart) but attachmentBoneName is empty — it will end up unattached at VisualRoot.");
            return 1;
        }
        return 0;
    }

    /// <summary>Shared validation for BOTH WearableItemSO and HairItemSO — see AvatarItemSO's own doc
    /// on why variant resolution is common to every item type. Shared just needs a prefab + valid
    /// declared morph names; GenderSpecific additionally needs at least one variant assigned AND each
    /// assigned variant's declared channels to actually match ITS OWN gender (task's own explicit
    /// "MaleVariant declara FemaleHeavy -> error" requirement).</summary>
    private static int ValidateItemVariants(AvatarItemSO item, string label)
    {
        int warnings = 0;

        if (item.variantMode == AvatarItemVariantMode.Shared)
        {
            warnings += ValidateVariant(label, "SharedVariant", item.sharedVariant, null);
            return warnings;
        }

        bool maleAssigned   = item.maleVariant != null && item.maleVariant.IsAssigned;
        bool femaleAssigned = item.femaleVariant != null && item.femaleVariant.IsAssigned;

        if (!maleAssigned && !femaleAssigned)
        {
            Debug.LogWarning($"[AvatarValidator] {label} is GenderSpecific but has neither MaleVariant nor FemaleVariant assigned — this item can never be equipped by anyone.");
            warnings++;
        }

        if (maleAssigned)   warnings += ValidateVariant(label, "MaleVariant",   item.maleVariant,   BodyBaseType.Male);
        if (femaleAssigned) warnings += ValidateVariant(label, "FemaleVariant", item.femaleVariant, BodyBaseType.Female);

        return warnings;
    }

    /// <summary>`expectedGender` is null for a Shared variant (no gender restriction on its declared
    /// channels) and Male/Female for a GenderSpecific one (its declared channels must all belong to
    /// that SAME gender — a Male variant declaring a Female channel, or vice versa, is always an
    /// authoring error, never something AvatarFactory could sensibly apply anyway).</summary>
    private static int ValidateVariant(string label, string variantLabel, AvatarItemVariant variant, BodyBaseType? expectedGender)
    {
        var prefab = ResolveEditorPrefab(variant?.prefab);
        if (prefab == null)
        {
            Debug.LogWarning($"[AvatarValidator] {label}: {variantLabel} has no prefab assigned.");
            return 1;
        }

        if (variant.supportedMorphChannels == null || variant.supportedMorphChannels.Length == 0)
            return 0; // zero morphs is perfectly normal (hair/glasses/hats/most shoes/gloves/accessories) — no warning

        int warnings = 0;
        var visualPart = prefab.GetComponentInChildren<AvatarVisualPart>();
        if (visualPart == null)
        {
            Debug.LogError($"[AvatarValidator] {label}: {variantLabel} declares supportedMorphChannels but its prefab '{prefab.name}' has no AvatarVisualPart — those channels can never be applied.");
            return 1;
        }

        foreach (var channel in variant.supportedMorphChannels)
        {
            if (expectedGender == BodyBaseType.Male && !IsMaleChannel(channel))
            {
                Debug.LogError($"[AvatarValidator] {label}: {variantLabel} declares '{channel}', which isn't a Male morph channel — authoring error.");
                warnings++;
            }
            else if (expectedGender == BodyBaseType.Female && !IsFemaleChannel(channel))
            {
                Debug.LogError($"[AvatarValidator] {label}: {variantLabel} declares '{channel}', which isn't a Female morph channel — authoring error.");
                warnings++;
            }
        }

        // Error, not a warning: supportedMorphChannels is an explicit authoring declaration (task's
        // own "MorphChannel.ToString() == blendshape name, cap mapping alternatiu" strict-contract
        // requirement) — a mismatch here is a definite asset bug, not just something the system expects.
        warnings += WarnMissingBlendShapes($"{label} ({variantLabel})", prefab.name, visualPart.skinnedRenderers, variant.supportedMorphChannels, isError: true);

        return warnings;
    }

    private static bool IsMaleChannel(MorphChannel c) =>
        c == MorphChannel.MaleSlim || c == MorphChannel.MaleHeavy || c == MorphChannel.MaleMuscle;

    private static bool IsFemaleChannel(MorphChannel c) =>
        c == MorphChannel.FemaleSlim || c == MorphChannel.FemaleHeavy || c == MorphChannel.FemaleMuscle;

    private static int ValidateRecipes()
    {
        int warnings = 0;
        foreach (var recipe in FindAssets<AvatarRecipeSO>())
        {
            string label = $"AvatarRecipeSO '{recipe.name}'";

            if (recipe.identity == null) { Debug.LogWarning($"[AvatarValidator] {label} has no identity assigned."); warnings++; }
            if (recipe.baseAvatar == null) { Debug.LogWarning($"[AvatarValidator] {label} has no baseAvatar assigned."); warnings++; }

            BodyBaseType? resolvedBodyType = recipe.identity != null && recipe.identity.bodyMorphProfile != null
                ? recipe.identity.bodyMorphProfile.baseType
                : null;

            AvatarSlot occupied = AvatarSlot.None;
            var equipped = new List<WearableItemSO>();
            foreach (var item in recipe.items)
            {
                if (item == null) continue;
                if ((item.occupiedSlots & occupied) != AvatarSlot.None)
                {
                    Debug.LogWarning($"[AvatarValidator] {label}: '{item.displayName}' conflicts with an already-listed item over slot(s) {(item.occupiedSlots & occupied)}.");
                    warnings++;
                }
                occupied |= item.occupiedSlots;
                equipped.Add(item);

                // task's own explicit example: Identity Female + a Male-only Wearable must be caught
                // here, before Play, not silently skipped at runtime.
                if (resolvedBodyType.HasValue && item.GetVariant(resolvedBodyType.Value) == null)
                {
                    Debug.LogError($"[AvatarValidator] {label}: '{item.displayName}' has no variant for " +
                                    $"{resolvedBodyType.Value} (this recipe's own Identity) — it will silently " +
                                    "never appear. Fix the item's variants or remove it from this recipe.");
                    warnings++;
                }
            }

            foreach (var colorOverride in recipe.colorOverrides)
            {
                if (colorOverride.item == null) continue;

                bool foundInRecipe = false;
                foreach (var e in equipped) { if (e == colorOverride.item) { foundInRecipe = true; break; } }

                if (!foundInRecipe)
                {
                    Debug.LogWarning($"[AvatarValidator] {label}: a color override targets '{colorOverride.item.displayName}', which isn't in this recipe's items[] — it will never apply.");
                    warnings++;
                }
            }
        }
        return warnings;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────────

    /// <summary>Exact-name check ONLY — `channel.ToString()` IS the blendshape name (task's own
    /// explicit "cap mapper de noms" requirement), never an alternative/fuzzy lookup.</summary>
    private static int WarnMissingBlendShapes(string label, string prefabName, SkinnedMeshRenderer[] renderers, IReadOnlyList<MorphChannel> expectedChannels, bool isError)
    {
        int warnings = 0;
        foreach (var channel in expectedChannels)
        {
            bool found = false;
            if (renderers != null)
            {
                foreach (var smr in renderers)
                {
                    if (smr == null || smr.sharedMesh == null) continue;
                    if (smr.sharedMesh.GetBlendShapeIndex(channel.ToString()) >= 0) { found = true; break; }
                }
            }
            if (!found)
            {
                string message = $"[AvatarValidator] {label} prefab '{prefabName}' declares/expects morph channel " +
                                  $"'{channel}' but no skinned renderer has a blendshape named exactly '{channel}'.";
                if (isError) Debug.LogError(message); else Debug.LogWarning(message);
                warnings++;
            }
        }
        return warnings;
    }

    private static GameObject ResolveEditorPrefab(UnityEngine.AddressableAssets.AssetReferenceGameObject reference)
    {
        if (reference == null) return null;
        return reference.editorAsset as GameObject;
    }

    private static IEnumerable<T> FindAssets<T>() where T : UnityEngine.Object
    {
        var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        foreach (var guid in guids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) yield return asset;
        }
    }
}
#endif
