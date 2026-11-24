using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>Prefabs/UI/Collection/RivalCard.prefab — one card: portrait (lit when revealed, black silhouette
/// otherwise), a separate blank nameplate + TMP name, and four version stars.</summary>
public class RivalCardView : MonoBehaviour
{
    public Button button;
    public Image portrait;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI footer;
    public Image namePlate;
    public Image[] versionStars = new Image[4];
    public Color starColor = new(1f, .75f, .22f, 1f);
    public Color revealedColor = Color.white;
    public Color silhouetteColor = new(0f, 0f, 0f, 1f);
    public Color emptyColor = new(1f, 1f, 1f, 0.15f);

    public void Set(Sprite sprite, bool revealed, string name, string footerText, bool interactable)
    {
        if (portrait != null)
        {
            portrait.sprite = sprite;
            portrait.color = sprite == null ? emptyColor : revealed ? revealedColor : silhouetteColor;
            portrait.enabled = sprite != null;
            if (sprite != null && portrait.TryGetComponent<AspectRatioFitter>(out var fitter))
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
        }
        if (nameText != null) nameText.text = revealed ? name : "";
        if (namePlate != null) namePlate.gameObject.SetActive(revealed);
        if (footer != null) { footer.text = footerText; footer.gameObject.SetActive(false); }
        if (button != null) button.interactable = interactable && revealed;
    }

    public void SetCollection(Sprite sprite, string composerName, IReadOnlyList<int> defeatedLevels, bool interactable = true)
    {
        bool revealed = defeatedLevels != null && defeatedLevels.Count > 0;
        Set(sprite, revealed, composerName, "", interactable);
        for (int i = 0; i < versionStars.Length; i++)
        {
            bool won = false;
            if (defeatedLevels != null)
                for (int j = 0; j < defeatedLevels.Count; j++)
                    if (defeatedLevels[j] == i + 1) { won = true; break; }
            if (versionStars[i] != null)
            {
                versionStars[i].enabled = won;
                versionStars[i].color = starColor;
            }
        }
    }
    // Popup versions use cumulative stars to identify their level, not the gallery's victory marks.
    public void SetVersion(Sprite sprite, bool discovered, int level, bool current)
    {
        Set(sprite, discovered, "", "", false);
        if (namePlate != null) namePlate.gameObject.SetActive(false);
        for (int i = 0; i < versionStars.Length; i++)
        {
            if (versionStars[i] == null) continue;
            versionStars[i].enabled = i < level;
            versionStars[i].color = discovered ? starColor : new Color(.5f, .44f, .32f, 1f);
        }
        if (footer != null)
        {
            footer.gameObject.SetActive(current);
            footer.text = Loc.Get("Rivals.CurrentVersion");
            footer.color = new Color(.23f, .20f, .15f, 1f);
        }
    }

}
