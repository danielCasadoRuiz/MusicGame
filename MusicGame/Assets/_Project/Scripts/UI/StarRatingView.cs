using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 0–5 star display in HALF steps (full / half / empty) — prefab Prefabs/UI/Components/StarRating.prefab,
/// five nested Star.prefab instances. Each star = a "back" Image (empty look) + a "fill" Image (Filled,
/// horizontal) whose fillAmount is 0 / 0.5 / 1. The look (sprite Art/UI/Star.png, sizes, colours, spacing)
/// is edited in the prefabs; this view only sets the values. SetHalfStars(-1) = pending (dimmed backs).
/// </summary>
public class StarRatingView : MonoBehaviour
{
    [SerializeField] private Image[] fills = new Image[5];
    [SerializeField] private Image[] backs = new Image[5];
    [SerializeField] private Color emptyColor = new(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color pendingColor = new(1f, 1f, 1f, 0.08f);

    public int HalfStars { get; private set; }

    /// <summary>Instantiates the library's StarRating prefab under `parent`.</summary>
    public static StarRatingView Spawn(Transform parent) =>
        UIPrefabLibrarySO.Spawn(UIPrefabLibrarySO.Instance != null ? UIPrefabLibrarySO.Instance.starRating : null, parent, "StarRating");

    /// <summary>0..10 half stars; negative = pending / not rated.</summary>
    public void SetHalfStars(int halfStars)
    {
        HalfStars = halfStars;
        bool pending = halfStars < 0;
        int h = Mathf.Clamp(halfStars, 0, StarMath.Max);
        for (int i = 0; i < fills.Length; i++)
        {
            if (fills[i] != null) fills[i].fillAmount = Mathf.Clamp(h - i * 2, 0, 2) * 0.5f;
            if (i < backs.Length && backs[i] != null) backs[i].color = pending ? pendingColor : emptyColor;
        }
    }

    /// <summary>Editor builder hook: wires the five stars.</summary>
    public void Bind(Image[] starFills, Image[] starBacks) { fills = starFills; backs = starBacks; }
}
