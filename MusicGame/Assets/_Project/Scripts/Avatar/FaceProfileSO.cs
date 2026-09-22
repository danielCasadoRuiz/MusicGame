using UnityEngine;

/// <summary>
/// Face + skin authoring data — deliberately minimal this phase (task's own explicit "no facis
/// Character Creator UI / photo->face" scope note): just enough for AvatarFactory to tint the body's
/// skin renderers and optionally drop in a face texture. `faceTexture` is optional (null is tolerated
/// — the base avatar prefab's own authored material stands as-is). Additional face data (shape
/// sliders, makeup, etc.) has an obvious home here later without touching AvatarIdentity/AvatarFactory's
/// own shape — this SO is the one place that would grow.
///
/// SkinTone here is the SOLE authority for the avatar's skin color (task's own explicit "el SkinTone
/// del FaceProfile és l'autoritat del color de pell") — AvatarFactory applies it via
/// MaterialPropertyBlock on AvatarVisualPart.skinToneRenderers, never by editing a shared Material
/// (see AvatarFactory's own doc on why: two avatars must never recolor each other).
/// </summary>
[CreateAssetMenu(fileName = "FaceProfile", menuName = "MusicGame/Avatar/Face Profile")]
public class FaceProfileSO : ScriptableObject
{
    public string id;

    [Tooltip("Optional — applied to AvatarVisualPart.faceRenderer via MaterialPropertyBlock (see " +
             "BaseAvatarDefinitionSO.faceTextureShaderProperty). Null is tolerated: the base avatar's " +
             "own authored material/texture stands as-is.")]
    public Texture2D faceTexture;

    public Color skinTone = Color.white;

    // Placeholder for future face data (shape/makeup/etc.) — task's own explicit "future face data
    // placeholder" scope note: intentionally left empty rather than speculatively designed now.
}
