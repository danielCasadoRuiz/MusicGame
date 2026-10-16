using TMPro;
using UnityEngine;

/// <summary>One row of an EvaluationPanel (prefabs EvaluationRow_*): a label and, for star rows, a nested
/// StarRating. Look and size are the prefab's; Set only writes the text and the stars.</summary>
public class EvaluationRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private StarRatingView stars;

    public void Set(string text, int halfStars)
    {
        if (label != null) label.text = text ?? "";
        if (stars != null) stars.SetHalfStars(halfStars);
    }

    public void Bind(TMP_Text rowLabel, StarRatingView rowStars) { label = rowLabel; stars = rowStars; }
}
