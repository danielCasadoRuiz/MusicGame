using UnityEngine;

/// <summary>
/// The editable UI PREFABS that controllers instantiate (AppConfigSO.uiPrefabs): component prefabs (stars,
/// evaluation panel, speech bubble, popup, skip button, language selector), list rows and the screens that
/// are not part of the UI scene's UIRegistry. Structure and look live in these prefabs
/// (Assets/_Project/Prefabs/UI/Components, Progression, Collection…); controllers only fill data.
/// Screen prefabs and their list items are declared in the partial files UIPrefabLibrarySO.<Group>.cs.
/// Generated once by Tools > MusicGame > UI > Build All UI Prefabs (existing prefabs are never
/// overwritten — "Rebuild" is a separate, explicit menu). No runtime code builds UI: a missing prefab is
/// logged as an error, never replaced by a procedural fallback.
/// </summary>
[CreateAssetMenu(fileName = "UIPrefabLibrary", menuName = "MusicGame/UI/UI Prefab Library")]
public partial class UIPrefabLibrarySO : ScriptableObject
{
    [Header("Root")]
    [Tooltip("The single Screen Space Overlay canvas every UI prefab is instantiated under (UIFactory.RootCanvas).")]
    public Canvas rootCanvas;

    [Header("Components")]
    public StarRatingView starRating;
    public EvaluationPanel evaluationPanel;
    public ComposerSpeechBubble speechBubble;
    public FightPresentationSkip skipButton;
    public CampaignPopup campaignPopup;
    public LanguageSelectorView languageSelector;

    [Header("Progression")]
    public ProgressionScreenView progressionScreen;
    public ProgressionLevelHeaderView progressionLevelHeader;
    public GameObject progressionColumnHeader;
    public ProgressionMatchRowView progressionMatchRow;
    public ProgressionTextRowView progressionBossHeader;
    public ProgressionTextRowView progressionBossStage;

    [Header("Collection")]
    public RivalCollectionView collectionScreen;
    public RivalCardView rivalCard;

    private static UIPrefabLibrarySO s_instance;

    public static UIPrefabLibrarySO Instance
    {
        get
        {
            if (s_instance != null) return s_instance;
            var app = Resources.Load<AppConfigSO>("AppConfig");
            s_instance = app != null ? app.uiPrefabs : null;
            if (s_instance == null) Debug.LogError("[UI] AppConfig.uiPrefabs (UIPrefabLibrarySO) is not assigned — run Tools > MusicGame > UI > Build Component Prefabs.");
            return s_instance;
        }
    }

    /// <summary>Instantiates a library prefab under `parent`, or logs a precise error (never a silent fallback).</summary>
    public static T Spawn<T>(T prefab, Transform parent, string what) where T : Component
    {
        if (prefab == null)
        {
            Debug.LogError($"[UI] Prefab '{what}' is not assigned in UIPrefabLibrary (AppConfig.uiPrefabs) — run Tools > MusicGame > UI > Build Component Prefabs.");
            return null;
        }
        var instance = Instantiate(prefab, parent, false);
        instance.name = prefab.name;
        return instance;
    }
}
