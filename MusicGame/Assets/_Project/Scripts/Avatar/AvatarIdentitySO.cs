using UnityEngine;

/// <summary>
/// An AUTHORED character identity — "Identity_Rex": one BodyMorphProfileSO + one FaceProfileSO + one
/// HairItemSO, reusable across many AvatarRecipeSO assets (task's own example: Identity_Rex feeds
/// Avatar_Rex_Level1/5/10, each dressing/evolving it differently — see AvatarRecipeSO's own doc).
///
/// ToRuntime() always returns a FRESH AvatarIdentity instance — this SO's own fields are never
/// mutated by anything downstream (AvatarRecipeSO's optional overrides apply to the COPY, not here),
/// so the same AvatarIdentitySO can safely back any number of simultaneously-alive AvatarInstances
/// (task's own explicit "Player i Opponent poden tenir bases diferents simultàniament" note extends
/// naturally to "many recipes can share one Identity SO at once").
/// </summary>
[CreateAssetMenu(fileName = "AvatarIdentity", menuName = "MusicGame/Avatar/Avatar Identity")]
public class AvatarIdentitySO : ScriptableObject
{
    public string id;

    public BodyMorphProfileSO bodyMorphProfile;
    public FaceProfileSO face;
    public HairItemSO hair;

    public AvatarIdentity ToRuntime()
    {
        if (bodyMorphProfile == null)
            Debug.LogWarning($"[AvatarIdentitySO] '{(string.IsNullOrEmpty(id) ? name : id)}' has no bodyMorphProfile — falling back to a default Male body.");

        return new AvatarIdentity
        {
            Body = bodyMorphProfile != null ? bodyMorphProfile.ToValues() : BodyMorphValues.Default(BodyBaseType.Male),
            Face = face,
            Hair = hair,
        };
    }
}
