using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// The ONE place an AvatarRecipe turns into a real, wearable, on-screen avatar — Runner/Fight never
/// touch morphs/clothes/Face/Addressables themselves (task's own explicit requirement); they call
/// CreateAsync and get back an AvatarInstance handle, full stop.
///
/// A plain static class — deliberately no persistent state, no .Instance (task's own "no creïs
/// managers globals innecessaris" scope note): every call is self-contained, and every resource it
/// opens (Addressables handles, cloned meshes) is tracked on the AvatarInstance it returns, released
/// entirely by that instance's own Dispose().
///
/// Runs the pipeline from this phase's own spec, in this order: load+instantiate the SINGLE shared
/// base body prefab -> find its skeleton/AvatarVisualPart -> clone its meshes -> apply MaleBase/
/// FemaleBase vertex positions -> apply Weight/Muscle blendshapes -> apply Face/Skin -> hair ->
/// resolve slot conflicts + load equipped items -> remap each item's skeleton -> apply morphs to
/// compatible items -> body masking -> garment-vs-garment occlusion -> color overrides -> return.
/// Never fitting/deforming geometry procedurally beyond that (task's own explicit "no facis fitting
/// procedural automàtic" scope note) — masking/occlusion are pure renderer.enabled toggles, morphs are
/// pure blendshape weights layered on top of whatever base vertex preset was applied.
///
/// LIFECYCLE SAFETY (task's own explicit correction): `parent` is checked for validity (Unity's own
/// destroyed-object equality) right after every `await` boundary — if the caller's build target
/// (typically a FighterActor.VisualRoot) has been destroyed in the meantime (e.g. Fight was exited to
/// Main Menu mid-load), this aborts immediately, disposes whatever was tracked so far (Addressables
/// handles + cloned meshes — see AvatarInstance.Dispose's own doc), and returns an instance with
/// Root == null, exactly like any other failed build — callers already have to check that. No
/// CancellationToken/generation-id infrastructure needed: `parent` going Unity-null IS the signal.
/// </summary>
public static class AvatarFactory
{
    private static readonly MorphChannel[] MaleChannels   = { MorphChannel.MaleSlim,   MorphChannel.MaleHeavy,   MorphChannel.MaleMuscle };
    private static readonly MorphChannel[] FemaleChannels = { MorphChannel.FemaleSlim, MorphChannel.FemaleHeavy, MorphChannel.FemaleMuscle };

    public static async Task<AvatarInstance> CreateAsync(AvatarRecipe recipe, Transform parent)
    {
        var instance = new AvatarInstance
        {
            Recipe = recipe,
            FinalIdentity = recipe?.Identity,
            BodyMorphValues = recipe?.Identity != null ? recipe.Identity.Body : BodyMorphValues.Default(BodyBaseType.Male),
        };

        if (recipe == null || recipe.Identity == null || recipe.BaseAvatar == null)
        {
            Debug.LogError("[AvatarFactory] Cannot build an avatar with no recipe/Identity/BaseAvatar — returning an empty instance (Root stays null).");
            return instance;
        }

        // 1-2. Load + instantiate the ONE shared base body prefab — Male/Female never means a
        // different prefab, only a different vertex preset applied below (see class doc).
        var bodyGO = await InstantiateAsync(recipe.BaseAvatar.baseAvatarPrefab, parent, instance, "BaseAvatar");
        if (AbortIfInvalid(parent, instance, "base avatar instantiate")) return instance;
        if (bodyGO == null)
        {
            Debug.LogError("[AvatarFactory] Failed to instantiate the base avatar — returning an unfinished instance.");
            return instance;
        }

        instance.Root = bodyGO.transform;
        instance.VisualRoot = bodyGO.transform;
        instance.Animator = bodyGO.GetComponentInChildren<Animator>();

        // 3. Find skeleton — via the base body's own AvatarVisualPart contract (see its own doc).
        var bodyPart = bodyGO.GetComponentInChildren<AvatarVisualPart>();
        if (bodyPart == null)
        {
            Debug.LogError($"[AvatarFactory] Base avatar prefab '{bodyGO.name}' has no AvatarVisualPart — cannot resolve skeleton/regions/morphs. Returning an unfinished instance.");
            return instance;
        }

        instance.SkeletonRoot = bodyPart.rootBone != null ? bodyPart.rootBone : bodyGO.transform;
        var mapper = new AvatarSkeletonMapper(instance.SkeletonRoot);
        var morphController = new AvatarBodyMorphController();
        instance.MorphController = morphController;

        // 4-6. Clone runtime meshes (never mutate sharedMesh) -> apply MaleBase/FemaleBase vertex
        // positions -> register + apply Weight/Muscle blendshapes, using ONLY this base's own
        // gender-matched channels (see class doc). Base preset MUST be applied before any blendshape
        // weight is set — blendshapes are deltas evaluated on top of whatever Mesh.vertices currently
        // holds (see AvatarMeshBasePresetSO's own doc).
        var basePreset = recipe.BaseAvatar.GetBasePreset(recipe.Identity.Body.BaseType);
        if (basePreset == null)
            Debug.LogWarning($"[AvatarFactory] BaseAvatarDefinitionSO '{recipe.BaseAvatar.name}' has no " +
                              $"{recipe.Identity.Body.BaseType}Base preset assigned — the base mesh keeps whatever shape it was imported with.");

        var bodyMorphChannels = recipe.Identity.Body.BaseType == BodyBaseType.Male ? MaleChannels : FemaleChannels;
        foreach (var smr in bodyPart.skinnedRenderers)
        {
            CloneMeshForMutation(smr, instance);
            ApplyBasePreset(smr, basePreset, "BaseAvatar");
            morphController.RegisterRenderer(smr, bodyMorphChannels, "BaseAvatar");
        }
        morphController.Apply(recipe.Identity.Body);

        // 7. Face/Skin.
        ApplySkinTone(bodyPart, recipe.BaseAvatar, recipe.Identity.Face);
        ApplyFaceTexture(bodyPart, recipe.BaseAvatar, recipe.Identity.Face);

        // 8-11. Resolve slot conflicts BEFORE loading anything (never wastes an Addressables load on
        // a rejected item — see ResolveSlotConflicts' own doc), then load Hair + every accepted item,
        // remapping skeletons and registering item-declared morph channels as each one comes in —
        // checking `parent` liveness after each Addressables round-trip (see class doc).
        var accepted = ResolveSlotConflicts(recipe.Items);
        bool hairHiddenByHeadwear = HairHiddenByEquippedHeadwear(accepted);
        BodyBaseType bodyType = recipe.Identity.Body.BaseType;

        await LoadHair(recipe.Identity.Hair, hairHiddenByHeadwear, bodyType, mapper, instance);
        if (AbortIfInvalid(parent, instance, "hair load")) return instance;

        foreach (var itemDef in accepted)
        {
            await LoadItem(itemDef, bodyType, mapper, morphController, instance);
            if (AbortIfInvalid(parent, instance, $"item load ('{itemDef.displayName}')")) return instance;
        }

        morphController.Apply(recipe.Identity.Body); // re-apply now that garment renderers are registered too

        // 12. Body masking.
        ApplyBodyMasking(bodyPart, accepted);

        // 13. Garment-vs-garment occlusion.
        ApplyGarmentOcclusion(instance.EquippedItems);

        // 14. Colors/materials.
        ApplyColorOverrides(recipe, instance.EquippedItems);

        return instance;
    }

    /// <summary>True (and disposes `instance`) the instant `parent` has become a destroyed Unity
    /// object — see class doc's own "LIFECYCLE SAFETY" section. `context` is purely for the log line.</summary>
    private static bool AbortIfInvalid(Transform parent, AvatarInstance instance, string context)
    {
        if (parent != null) return false;

        Debug.LogWarning($"[AvatarFactory] Build target was destroyed during '{context}' — aborting, " +
                          "releasing everything loaded so far, and never applying this avatar.");
        instance.Dispose();
        return true;
    }

    // ── Slot conflicts ───────────────────────────────────────────────────────────

    /// <summary>First-come-first-served over recipe.Items' own array order — an item whose
    /// occupiedSlots overlaps anything already accepted is rejected with a clear warning (task's own
    /// explicit "La Factory ha de detectar conflictes" requirement), never silently double-equipped.
    /// Runs BEFORE any variant is even resolved — a rejected item's Addressables load never
    /// happens.</summary>
    private static List<WearableItemSO> ResolveSlotConflicts(List<WearableItemSO> requested)
    {
        var accepted = new List<WearableItemSO>();
        if (requested == null) return accepted;

        AvatarSlot occupied = AvatarSlot.None;
        foreach (var item in requested)
        {
            if (item == null) continue;

            if ((item.occupiedSlots & occupied) != AvatarSlot.None)
            {
                Debug.LogWarning($"[AvatarFactory] '{item.displayName}' conflicts with an already-equipped item " +
                                  $"over slot(s) {(item.occupiedSlots & occupied)} — skipped.");
                continue;
            }

            occupied |= item.occupiedSlots;
            accepted.Add(item);
        }
        return accepted;
    }

    private static bool HairHiddenByEquippedHeadwear(List<WearableItemSO> accepted)
    {
        foreach (var item in accepted)
            if ((item.occupiedSlots & AvatarSlot.Headwear) != AvatarSlot.None && item.hidesHair)
                return true;
        return false;
    }

    // ── Hair ──────────────────────────────────────────────────────────────────────

    /// <summary>Resolves Hair's variant for `bodyType` FIRST (task's own explicit "no carreguis una
    /// variant incompatible" requirement) — if none exists for this gender, the avatar simply has no
    /// hair (one clear warning, never a crash, never a fallback to the other gender's mesh).</summary>
    private static async Task LoadHair(HairItemSO hair, bool hidden, BodyBaseType bodyType, AvatarSkeletonMapper mapper, AvatarInstance instance)
    {
        if (hair == null || hidden) return;

        var variant = hair.GetVariant(bodyType);
        if (variant == null)
        {
            Debug.LogWarning($"[AvatarFactory] Hair '{hair.stableId}' has no variant for {bodyType} — this avatar will have no hair.");
            return;
        }

        var hairGO = await InstantiateAsync(variant.prefab, instance.VisualRoot, instance, $"Hair({hair.stableId})");
        if (hairGO == null) return;

        var visualPart = hairGO.GetComponentInChildren<AvatarVisualPart>();
        if (visualPart != null && visualPart.skinnedRenderers.Length > 0)
        {
            foreach (var smr in visualPart.skinnedRenderers)
            {
                mapper.Remap(smr, $"Hair({hair.stableId})");
                if (variant.supportedMorphChannels.Length > 0)
                {
                    CloneMeshForMutation(smr, instance);
                    instance.MorphController.RegisterRenderer(smr, variant.supportedMorphChannels, $"Hair({hair.stableId})");
                }
            }
        }
        else
        {
            var bone = mapper.FindBone(hair.attachmentBoneName);
            if (bone != null)
            {
                hairGO.transform.SetParent(bone, false);
            }
            else
            {
                Debug.LogWarning($"[AvatarFactory] Hair '{hair.stableId}' wants to attach to bone " +
                                  $"'{hair.attachmentBoneName}' which the skeleton doesn't have — left at VisualRoot.");
            }
        }
    }

    // ── Equipped items ────────────────────────────────────────────────────────────

    /// <summary>Resolves `itemDef`'s variant for `bodyType` FIRST — never loads the wrong gender's
    /// mesh only to discover it doesn't apply (task's own explicit requirement). No variant for this
    /// gender is a normal, silent-except-for-one-warning skip (e.g. a Male-only item in a Female
    /// recipe) — see AvatarItemSO.GetVariant's own doc.</summary>
    private static async Task LoadItem(WearableItemSO itemDef, BodyBaseType bodyType, AvatarSkeletonMapper mapper, AvatarBodyMorphController morphController, AvatarInstance instance)
    {
        var variant = itemDef.GetVariant(bodyType);
        if (variant == null)
        {
            Debug.LogWarning($"[AvatarFactory] '{itemDef.displayName}' has no variant for {bodyType} — skipped (this item doesn't exist for this Identity).");
            return;
        }

        var itemGO = await InstantiateAsync(variant.prefab, instance.VisualRoot, instance, itemDef.displayName);
        if (itemGO == null) return;

        var visualPart = itemGO.GetComponentInChildren<AvatarVisualPart>();
        if (visualPart == null)
        {
            Debug.LogWarning($"[AvatarFactory] '{itemDef.displayName}' has no AvatarVisualPart — treated as a " +
                              "fully rigid prop with no skeleton/morph/region support.");
            instance.EquippedItems.Add(new EquippedAvatarItem { Definition = itemDef, Instance = itemGO, VisualPart = null });
            return;
        }

        if (visualPart.skinnedRenderers.Length > 0)
        {
            foreach (var smr in visualPart.skinnedRenderers)
            {
                mapper.Remap(smr, itemDef.displayName);
                if (variant.supportedMorphChannels.Length > 0)
                {
                    CloneMeshForMutation(smr, instance);
                    morphController.RegisterRenderer(smr, variant.supportedMorphChannels, itemDef.displayName);
                }
            }
        }
        else if (visualPart.rootBone != null)
        {
            var bone = mapper.FindBone(visualPart.rootBone.name);
            if (bone != null)
                itemGO.transform.SetParent(bone, false);
            else
                Debug.LogWarning($"[AvatarFactory] '{itemDef.displayName}' wants to attach to bone " +
                                  $"'{visualPart.rootBone.name}' which the skeleton doesn't have — left at VisualRoot.");
        }
        // else: no skinned renderers and no declared rootBone — a rigid prop already correctly
        // parented at VisualRoot by InstantiateAsync, nothing more to do.

        instance.EquippedItems.Add(new EquippedAvatarItem { Definition = itemDef, Instance = itemGO, VisualPart = visualPart });
    }

    // ── Masking / occlusion ───────────────────────────────────────────────────────

    private static void ApplyBodyMasking(AvatarVisualPart bodyPart, List<WearableItemSO> accepted)
    {
        AvatarBodyRegion hidden = AvatarBodyRegion.None;
        foreach (var item in accepted) hidden |= item.hiddenBodyRegions;
        if (hidden == AvatarBodyRegion.None) return;

        if (bodyPart.regionMap == null)
        {
            Debug.LogWarning($"[AvatarFactory] Equipped item(s) want to hide body region(s) {hidden} but the " +
                              "base avatar has no AvatarRegionMap — body masking not supported for this asset.");
            return;
        }
        bodyPart.regionMap.ApplyHiddenRegions(hidden);
    }

    /// <summary>An outer item (strictly higher GarmentLayer) occludes a lower item's OWN exposed
    /// regions — never a raw region flag applied blind: only the intersection with what the lower
    /// item's own AvatarRegionMap actually exposes (see AvatarRegionMap.ExposedRegions' own doc), so
    /// a Jacket's Torso occlusion harmlessly does nothing to a pair of Boots. A lower item with no
    /// AvatarRegionMap at all that something genuinely tried to occlude gets one clear warning instead
    /// of silently rendering through — never a crash (task's own explicit requirement).</summary>
    private static void ApplyGarmentOcclusion(List<EquippedAvatarItem> equipped)
    {
        foreach (var lower in equipped)
        {
            AvatarBodyRegion accumulated = AvatarBodyRegion.None;
            bool anyOuterWantsToOccludeThis = false;

            foreach (var outer in equipped)
            {
                if (ReferenceEquals(outer, lower)) continue;
                if (outer.Definition.layer <= lower.Definition.layer) continue;
                if (outer.Definition.occludedLowerGarmentRegions == AvatarBodyRegion.None) continue;

                anyOuterWantsToOccludeThis = true;
                if (lower.VisualPart?.regionMap != null)
                    accumulated |= outer.Definition.occludedLowerGarmentRegions & lower.VisualPart.regionMap.ExposedRegions;
            }

            if (lower.VisualPart?.regionMap != null)
            {
                lower.VisualPart.regionMap.ApplyHiddenRegions(accumulated);
            }
            else if (anyOuterWantsToOccludeThis)
            {
                Debug.LogWarning($"[AvatarFactory] '{lower.Definition.displayName}' is occluded by an outer " +
                                  "garment but has no AvatarRegionMap — partial masking not supported for this " +
                                  "asset; it will render fully underneath.");
            }
        }
    }

    // ── Colors ────────────────────────────────────────────────────────────────────

    private static void ApplyColorOverrides(AvatarRecipe recipe, List<EquippedAvatarItem> equipped)
    {
        var block = new MaterialPropertyBlock();

        foreach (var equippedItem in equipped)
        {
            if (equippedItem.VisualPart == null) continue;

            AvatarColorOverride? effective = null;
            foreach (var recipeOverride in recipe.ColorOverrides)
            {
                if (recipeOverride.item == equippedItem.Definition) { effective = recipeOverride; break; }
            }
            effective ??= equippedItem.Definition.defaultColorOverride;

            if (effective == null || string.IsNullOrEmpty(effective.Value.shaderProperty)) continue;

            foreach (var renderer in equippedItem.VisualPart.AllRenderers())
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor(effective.Value.shaderProperty, effective.Value.color);
                renderer.SetPropertyBlock(block);
            }
        }
    }

    // ── Face / skin ───────────────────────────────────────────────────────────────

    private static void ApplySkinTone(AvatarVisualPart bodyPart, BaseAvatarDefinitionSO baseAvatar, FaceProfileSO face)
    {
        if (face == null || bodyPart.skinToneRenderers == null || bodyPart.skinToneRenderers.Length == 0) return;

        var block = new MaterialPropertyBlock();
        foreach (var renderer in bodyPart.skinToneRenderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(block);
            block.SetColor(baseAvatar.skinColorShaderProperty, face.skinTone);
            renderer.SetPropertyBlock(block);
        }
    }

    private static void ApplyFaceTexture(AvatarVisualPart bodyPart, BaseAvatarDefinitionSO baseAvatar, FaceProfileSO face)
    {
        if (face == null || face.faceTexture == null || bodyPart.faceRenderer == null) return;

        var block = new MaterialPropertyBlock();
        bodyPart.faceRenderer.GetPropertyBlock(block);
        block.SetTexture(baseAvatar.faceTextureShaderProperty, face.faceTexture);
        bodyPart.faceRenderer.SetPropertyBlock(block);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────────

    /// <summary>Clones sharedMesh onto its own Mesh instance before any blendshape weight is ever set
    /// on it — NEVER mutate sharedMesh directly (task's own explicit requirement: two renderers using
    /// the same sharedMesh would otherwise fight over blendshape weights). Tracked on the
    /// AvatarInstance so Dispose() destroys it.</summary>
    private static void CloneMeshForMutation(SkinnedMeshRenderer renderer, AvatarInstance instance)
    {
        if (renderer == null || renderer.sharedMesh == null) return;
        var clone = Object.Instantiate(renderer.sharedMesh);
        renderer.sharedMesh = clone;
        instance.TrackClonedMesh(clone);
    }

    /// <summary>Overwrites `renderer`'s (already-cloned, never shared) mesh vertices with `preset`'s
    /// own data for the matching renderer name — see AvatarMeshBasePresetSO's own doc on why this
    /// MUST run before any blendshape weight is set. A renderer the preset has no entry for keeps its
    /// original imported vertex positions (warning, not a crash); a vertex-count mismatch is a clear
    /// topology-authoring error (error, skipped) rather than a corrupted mesh. normals/tangents are
    /// applied from the preset when provided, otherwise recalculated — see AvatarMeshBaseVertexData's
    /// own doc on why both are optional.</summary>
    private static void ApplyBasePreset(SkinnedMeshRenderer renderer, AvatarMeshBasePresetSO preset, string label)
    {
        if (renderer == null || renderer.sharedMesh == null || preset == null) return;

        var data = preset.FindRenderer(renderer.name);
        if (data == null)
        {
            Debug.LogWarning($"[AvatarFactory] {label}: base preset '{preset.name}' has no vertex data for " +
                              $"renderer '{renderer.name}' — keeping its original (imported) vertex positions.");
            return;
        }

        var mesh = renderer.sharedMesh; // already this renderer's own cloned instance by this point
        if (data.vertices.Length != mesh.vertexCount)
        {
            Debug.LogError($"[AvatarFactory] {label}: base preset '{preset.name}' renderer '{renderer.name}' " +
                            $"has {data.vertices.Length} vertices but the mesh has {mesh.vertexCount} — " +
                            "topology mismatch, skipped (mesh keeps its original vertex positions).");
            return;
        }

        mesh.vertices = data.vertices;

        if (data.normals.Length == mesh.vertexCount) mesh.normals = data.normals;
        else mesh.RecalculateNormals();

        if (data.tangents.Length == mesh.vertexCount) mesh.tangents = data.tangents;
        else mesh.RecalculateTangents();

        mesh.RecalculateBounds();
    }

    private static async Task<GameObject> InstantiateAsync(AssetReferenceGameObject reference, Transform parent, AvatarInstance instance, string label)
    {
        if (reference == null || !reference.RuntimeKeyIsValid())
        {
            Debug.LogWarning($"[AvatarFactory] '{label}' has no valid Addressable reference — skipped.");
            return null;
        }

        var handle = Addressables.InstantiateAsync(reference, parent);
        instance.TrackHandle(handle);
        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[AvatarFactory] Failed to instantiate Addressable '{label}'.");
            return null;
        }
        return handle.Result;
    }
}
