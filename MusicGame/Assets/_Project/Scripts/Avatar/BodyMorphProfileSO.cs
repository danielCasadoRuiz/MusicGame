using UnityEngine;

/// <summary>
/// A reusable, nameable Weight/Muscle preset — "Male Normal", "Male Strong", "Female Athletic",
/// "Female Heavy" (task's own examples). Purely an AUTHORING convenience: AvatarIdentitySO references
/// one of these so several identities can share "the same body build" without retyping Weight/Muscle
/// on each — it does NOT limit what the runtime can do (BodyMorphValues stays fully continuous; see
/// its own doc) and nothing downstream of ToValues() ever knows a preset was involved.
/// </summary>
[CreateAssetMenu(fileName = "BodyMorphProfile", menuName = "MusicGame/Avatar/Body Morph Profile")]
public class BodyMorphProfileSO : ScriptableObject
{
    public BodyBaseType baseType;
    [Range(0f, 1f)] public float weight = 0.5f;
    [Range(0f, 1f)] public float muscle;

    public BodyMorphValues ToValues() => new BodyMorphValues
    {
        BaseType = baseType,
        Weight   = weight,
        Muscle   = muscle,
    };
}
