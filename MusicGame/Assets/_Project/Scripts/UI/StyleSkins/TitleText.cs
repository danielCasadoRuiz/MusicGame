using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>Title size tiers — one size each, set in StyleSkinLibrarySO (the shared title look).</summary>
public enum TitleTier
{
    Display,      // countdown 3-2-1 TEMPO! / FURY!, ROUND, KO!, FINISH!, PERFECT!, VS
    ScreenTitle,  // the title of a menu / screen (PAUSE, YOU WIN, RIVALS, PROFILE…)
    Section,      // a section header inside a screen (profile blocks, progression levels, result sections…)
}

/// <summary>
/// Marks a TMP text as a title: TempoFuryBrush font, ivory colour and the size of its tier, all read from
/// StyleSkinLibrarySO so every title of a tier matches (change the size there, not here). Auto-size keeps a
/// long line (e.g. "MOZART WINS THE ROUND") inside its rect, never above the tier size.
/// Added by TempoFuryUISetup; it replaces the ThemeTextReceiver those texts had.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class TitleText : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public TitleTier tier = TitleTier.ScreenTitle;

    private void OnEnable() => Apply();
#if UNITY_EDITOR
    private void OnValidate() => Apply();
#endif

    public void Apply()
    {
        var text = GetComponent<TMP_Text>();
        if (library == null || text == null || library.titleFont == null) return;
        if (text.font != library.titleFont)
        {
            text.font = library.titleFont;
            text.fontSharedMaterial = library.titleFont.material;
        }
        text.fontStyle = FontStyles.Normal;   // the brush lettering has no bold cut
        if (text.fontFeatures == null || !text.fontFeatures.Contains(OTL_FeatureTag.kern))
            text.fontFeatures = new List<OTL_FeatureTag> { OTL_FeatureTag.kern };
        text.color = library.titleColor;
        float size = library.TitleSize(tier);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMax = size;
        text.fontSizeMin = size * 0.5f;
        text.fontSize = size;
    }
}
