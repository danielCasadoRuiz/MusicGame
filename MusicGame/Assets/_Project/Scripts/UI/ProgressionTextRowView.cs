using TMPro;
using UnityEngine;

/// <summary>Simple list row (BossHeaderRow / BossStageRow prefabs): a text and optional stars.</summary>
public class ProgressionTextRowView : MonoBehaviour
{
    public TextMeshProUGUI label;
    public StarRatingView stars;

    public void Set(string text, int halfStars = -2)
    {
        if (label != null) label.text = text;
        if (stars != null)
        {
            stars.gameObject.SetActive(halfStars >= -1);
            if (halfStars >= -1) stars.SetHalfStars(halfStars);
        }
    }
}
