using System.Collections.Generic;
using UnityEngine;

public enum CombatAnimationCategory
{
    IdleCombat,
    AttackPunch,
    AttackKick,
    AttackOther,
    Block,
    Dodge,
    HitReaction,
    Knockdown,
    GetUp,
    Taunt,
    Victory,
    Movement,
    Special,
    Unknown,
}

/// <summary>Gameplay roles the shortlist recommends a clip for (one clip per role, visually verified).</summary>
public enum CombatAnimationRole
{
    CombatIdle,
    Punch,
    HeavyPunch,
    Kick,
    Block,
    Dodge,
    HitReaction,
    Knockdown,
    GetUp,
    Taunt,
    Victory,
}

public enum CombatRootMotion
{
    /// <summary>The clip barely travels: play it in place.</summary>
    InPlace,
    /// <summary>The clip travels/turns noticeably: a gameplay controller should consume its root motion.</summary>
    RootMotion,
}

/// <summary>How a clip's finger curves are used.</summary>
public enum CombatFingerMode
{
    /// <summary>Keep the source finger curves (Rokoko Smartgloves capture, or a static source hand).</summary>
    Preserve,
    /// <summary>Replace them with the avatar's relaxed rest hand — only for a clip whose fingers look bad.</summary>
    RelaxedRestHand,
}

public enum CombatReviewStatus
{
    /// <summary>Imported, only auto-labelled from its file name — not visually checked yet.</summary>
    Unreviewed,
    /// <summary>Visually checked on the MakeHuman avatar and usable.</summary>
    Approved,
    /// <summary>Visually checked and not usable (see notes).</summary>
    Rejected,
}

[System.Serializable]
public class CombatAnimationEntry
{
    public string name;

    [Tooltip("Processed clip with the source root motion intact (root XZ travel, root yaw and height all kept as root motion).")]
    public AnimationClip clip;

    [Tooltip("Derived debug clip: same motion, played in place (yaw and height baked into the pose, XZ travel discarded), " +
             "grounded and facing +Z on the MakeHuman avatar.")]
    public AnimationClip inPlaceClip;

    public CombatAnimationCategory category = CombatAnimationCategory.Unknown;
    [Tooltip("True when the category was set by a person after watching the clip (otherwise guessed from the file name).")]
    public bool categoryReviewed;
    public bool loop;
    public CombatRootMotion rootMotion;
    public string sourcePack;
    public string sourceFbx;
    public string sourceTake;

    [Header("Measured at import (on the MakeHuman avatar)")]
    public float duration;
    public float frameRate;
    [Tooltip("Net root travel over the clip in the character's starting frame (x right, z forward), metres.")]
    public Vector3 rootTravel;
    [Tooltip("Net root yaw over the clip, degrees.")]
    public float rootYaw;
    [Tooltip("Frames trimmed from the start (e.g. a T-pose calibration frame at the head of the take).")]
    public int trimmedStartFrames;
    [Tooltip("The source has animated finger curves (Smartgloves capture).")]
    public bool fingerCapture;
    public CombatFingerMode fingerMode;
    [Tooltip("Automatic warnings found at import (pops, extreme muscles, very short clip...).")]
    public string importWarnings;

    [Header("Review")]
    public CombatReviewStatus review;
    [TextArea] public string notes;

    public AnimationClip DebugClip => inPlaceClip != null ? inPlaceClip : clip;
}

[System.Serializable]
public class CombatRoleAssignment
{
    public CombatAnimationRole role;
    public string entryName;
}

[System.Serializable]
public class CombatDemoStep
{
    [Tooltip("Label shown in the debug UI (e.g. Guard, Punch).")]
    public string label;
    public string entryName;
    [Tooltip("Optional: what the facing dummy plays in response (e.g. a hit reaction).")]
    public string partnerEntryName;
    [Range(0f, 1f), Tooltip("Normalized time of `entryName` at which the partner reacts.")]
    public float partnerDelay = 0.4f;
}

/// <summary>
/// Catalogue of the imported combat/action mocap (Rokoko packs, Mixamo skeleton), built by
/// Tools > MusicGame > Animations > Import Rokoko Combat Packs. Debug/browsing data only — nothing in
/// production gameplay reads it yet. Review fields (review, notes, category if reviewed, fingerMode,
/// loop) survive a re-import.
/// </summary>
[CreateAssetMenu(menuName = "MusicGame/Animation/Combat Animation Library", fileName = "CombatAnimationLibrary")]
public class CombatAnimationLibrarySO : ScriptableObject
{
    public List<CombatAnimationEntry> entries = new();

    [Tooltip("Recommended clip per gameplay role — filled only after visual verification.")]
    public List<CombatRoleAssignment> roles = new();

    [Tooltip("The [ Combat Demo ] sequence in the avatar debug scene.")]
    public List<CombatDemoStep> demoSequence = new();

    [Tooltip("SHA-256 of MakeHuman_Canonical.fbx at import time (proves the import never touched it).")]
    public string canonicalFbxHash;

    [TextArea(3, 20)] public string lastImportReport;

    public CombatAnimationEntry Find(string entryName)
    {
        if (string.IsNullOrEmpty(entryName)) return null;
        foreach (var entry in entries) if (entry != null && entry.name == entryName) return entry;
        return null;
    }

    public CombatAnimationEntry ForRole(CombatAnimationRole role)
    {
        foreach (var assignment in roles) if (assignment.role == role) return Find(assignment.entryName);
        return null;
    }

    public List<CombatAnimationEntry> Filter(CombatAnimationCategory? category, bool includeRejected = true)
    {
        var result = new List<CombatAnimationEntry>();
        foreach (var entry in entries)
        {
            if (entry == null || entry.DebugClip == null) continue;
            if (category.HasValue && entry.category != category.Value) continue;
            if (!includeRejected && entry.review == CombatReviewStatus.Rejected) continue;
            result.Add(entry);
        }
        return result;
    }
}
