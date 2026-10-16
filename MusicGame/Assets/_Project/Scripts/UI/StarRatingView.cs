using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable 0–5 star display in HALF steps (full / half / empty), driven by half-stars (0..10 — see
/// StarMath). Built procedurally (UIFactory style); the star shape is a generated sprite, so it never
/// depends on a font glyph. SetHalfStars(-1) shows "pending" (all stars hollow and dimmed).
/// </summary>
public class StarRatingView : MonoBehaviour
{
    private static Sprite s_star;
    private Image[] _fills;
    private Image[] _backs;
    private Color _fillColor = new(1f, 0.82f, 0.25f);
    private Color _emptyColor = new(1f, 1f, 1f, 0.18f);

    public int HalfStars { get; private set; }

    public static StarRatingView Create(RectTransform parent, float starSize = 28f, float spacing = 4f)
    {
        var root = UIFactory.CreateRect("Stars", parent);
        root.sizeDelta = new Vector2(starSize * 5f + spacing * 4f, starSize);
        var view = root.gameObject.AddComponent<StarRatingView>();
        view._fills = new Image[5];
        view._backs = new Image[5];
        for (int i = 0; i < 5; i++)
        {
            var slot = UIFactory.CreateRect("Star" + i, root);
            slot.anchorMin = slot.anchorMax = new Vector2(0f, 0.5f);
            slot.pivot = new Vector2(0f, 0.5f);
            slot.sizeDelta = new Vector2(starSize, starSize);
            slot.anchoredPosition = new Vector2(i * (starSize + spacing), 0f);
            var back = slot.gameObject.AddComponent<Image>();
            back.sprite = StarSprite; back.color = view._emptyColor; back.raycastTarget = false;
            var fillRt = UIFactory.CreateRect("Fill", slot);
            UIFactory.Stretch(fillRt);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = StarSprite; fill.color = view._fillColor; fill.raycastTarget = false;
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            view._backs[i] = back; view._fills[i] = fill;
        }
        view.SetHalfStars(0);
        return view;
    }

    /// <summary>0..10 half stars; negative = pending / not rated.</summary>
    public void SetHalfStars(int halfStars)
    {
        HalfStars = halfStars;
        bool pending = halfStars < 0;
        int h = Mathf.Clamp(halfStars, 0, StarMath.Max);
        for (int i = 0; i < 5; i++)
        {
            int inThis = Mathf.Clamp(h - i * 2, 0, 2);
            _fills[i].fillAmount = inThis * 0.5f;
            _backs[i].color = pending ? new Color(1f, 1f, 1f, 0.08f) : _emptyColor;
        }
    }

    public static Sprite StarSprite => s_star != null ? s_star : s_star = BuildStar(64);

    /// <summary>Five-pointed star rasterised into an alpha mask (anti-aliased by 4x supersampling).</summary>
    private static Sprite BuildStar(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float r = i % 2 == 0 ? 0.5f : 0.2f;
            float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
            pts[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r - 0.02f);
        }
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (Inside(new Vector2((x + (sx + 0.5f) / 4f) / size, (y + (sy + 0.5f) / 4f) / size), pts)) hits++;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }
}
