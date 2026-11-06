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

    [Tooltip("Shared Runner presentation controller (RunnerHumanoid: one state family per " +
             "RunnerAnimationRole). Never the combat controller.")]
    public RuntimeAnimatorController runnerAnimatorController;

    [Tooltip("Music style → RunnerAnimationStyleSO (clips per role) used by RunnerAvatarAnimator.")]
    public RunnerAnimationStyleLibrarySO runnerAnimationStyles;

    [System.Serializable]
    public class StyleRecipe
    {
        public GameMusicStyle style;
        public AvatarRecipeSO recipe;
    }

    [Tooltip("How the player looks for the style of the song being played (GameSession.DetectedGameStyle, the same " +
             "style the UI skins follow) in the Runner and Fight; styles without an outfit use the fixed avatarRecipe above. " +
             "Written by SegmentedOutfitSetup (Avatar_Player_<Style>: the player recipe + that style's garments).")]
    public StyleRecipe[] styleRecipes = System.Array.Empty<StyleRecipe>();

    /// <summary>The player's look: the current song's style recipe when one is authored, else the fixed recipe.</summary>
    public static AvatarRecipeSO ResolveRecipe(AppConfigSO appConfig)
    {
        var cfg = appConfig != null ? appConfig.playerAvatar : null;
        if (cfg == null) return null;
        var style = StyleSkinLibrarySO.CurrentStyle();
        if (style != GameMusicStyle.Unknown && cfg.styleRecipes != null)
            foreach (var r in cfg.styleRecipes) if (r != null && r.style == style && r.recipe != null) return r.recipe;
        return cfg.avatarRecipe;
    }
}
