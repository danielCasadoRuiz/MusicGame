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
    [Tooltip("Gender endpoint: Male = Gender 0, Female = Gender 1 (see BodyMorphValues.Gender).")]
    public BodyBaseType baseType;
    [Range(0f, 1f)] public float weight = 0.5f;
    [Range(0f, 1f)] public float muscle;

    [Tooltip("Optional — use a continuous Gender value instead of the baseType endpoint.")]
    public bool overrideGender;
    [Range(0f, 1f)] public float gender;

    public BodyMorphValues ToValues()
    {
        var values = BodyMorphValues.FromBaseType(baseType, weight, muscle);
        if (overrideGender) values.Gender = gender;
        return values;
    }
}
