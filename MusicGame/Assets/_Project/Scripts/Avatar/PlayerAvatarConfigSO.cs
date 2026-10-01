using UnityEngine;

/// <summary>
/// The ONE player visual identity, shared by every presentation context: the Runner
/// (PlayerController builds it under its VisualAnchor) and Fight (FightSceneBootstrap builds it
/// under the player FighterActor's VisualRoot). Each context keeps its own AnimatorController —
/// Runner uses runnerAnimatorController, Fight the shared FighterCombat controller — only the
/// recipe/model is shared.
///
/// PROVISIONAL: a single fixed recipe until the player customization/style system exists.
/// Referenced from AppConfigSO.playerAvatar.
/// </summary>
[CreateAssetMenu(fileName = "PlayerAvatarConfig", menuName = "MusicGame/Avatar/Player Avatar Config")]
public class PlayerAvatarConfigSO : ScriptableObject
{
    [Tooltip("PROVISIONAL player avatar recipe, built via AvatarFactory in both Runner and Fight.")]
    public AvatarRecipeSO avatarRecipe;

    [Tooltip("Runner presentation controller (Humanoid Idle/Run). Never the combat controller.")]
    public RuntimeAnimatorController runnerAnimatorController;

    public static AvatarRecipeSO ResolveRecipe(AppConfigSO appConfig) =>
        appConfig != null && appConfig.playerAvatar != null ? appConfig.playerAvatar.avatarRecipe : null;
}
