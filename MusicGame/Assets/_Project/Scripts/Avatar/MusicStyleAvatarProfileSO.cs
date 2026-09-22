using UnityEngine;

/// <summary>
/// What the Player wears for ONE detected music style — the outfit half of PlayerAvatarRecipeBuilder's
/// combination (task's own "L'estil musical canvia l'outfit, però no la identitat base" requirement).
/// Never touches Body/Face/Hair — those always come from the Player's own AvatarIdentitySO,
/// unaffected by style.
/// </summary>
[CreateAssetMenu(fileName = "MusicStyleAvatarProfile", menuName = "MusicGame/Avatar/Music Style Avatar Profile")]
public class MusicStyleAvatarProfileSO : ScriptableObject
{
    public MusicStyleId style;

    [Tooltip("Wearables only — Hair is never style-driven (see AvatarIdentity's own doc), so this is " +
             "typed WearableItemSO rather than the generic AvatarItemSO base.")]
    public WearableItemSO[] items = System.Array.Empty<WearableItemSO>();

    public AvatarColorOverride[] colorOverrides = System.Array.Empty<AvatarColorOverride>();
}
