/// <summary>
/// WHO the character is — Body morphology + Face/Skin + Hair, nothing about what it's currently
/// wearing (see AvatarRecipe's own doc for that side of the split). Plain runtime class, never a
/// ScriptableObject: this is the thing that gets copied/tweaked per-recipe (AvatarRecipeSO's optional
/// Body/Hair/Face overrides), so it must never alias the authored data it came from — see
/// AvatarIdentitySO.ToRuntime's own doc.
///
/// Face/Hair are kept as direct SO references (not deep-cloned) — they're immutable authored data
/// (texture/color/prefab reference), exactly the same "reference shared authored data, only the
/// combination is new" pattern OpponentLevelConfig already uses for its own SO fields. Only Body
/// (a plain value struct) is ever actually mutated per-recipe.
/// </summary>
public class AvatarIdentity
{
    public BodyMorphValues Body;
    public FaceProfileSO Face;
    public HairItemSO Hair;
}
