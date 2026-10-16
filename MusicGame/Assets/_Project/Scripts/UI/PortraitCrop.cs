using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Face-centred crop of a portrait sprite inside a small square slot (Fight HUD portraits). The composer
/// portraits (OpponentLevelConfig.portrait) are half-body cut-outs with the face in the upper part, so a
/// plain preserveAspect fit shows a tiny face. This slot carries a RectMask2D; its child `image` is scaled
/// so `visibleWidth` of the sprite's width fills the slot, and offset so `faceCenter` sits in the middle —
/// clamped so the sprite always covers the whole slot (no empty edges). Both crop parameters are plain
/// serialized fields on the prefab (editable per slot), never hardcoded in a controller.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PortraitCrop : MonoBehaviour
{
    [Tooltip("The masked child Image that shows the sprite.")]
    public Image image;
    [Tooltip("Face centre inside the sprite, normalized (0,0 = bottom-left, 1,1 = top-right). The composer " +
             "portraits share the same framing: face ≈ (0.48, 0.75).")]
    public Vector2 faceCenter = new(0.48f, 0.75f);
    [Tooltip("Fraction of the sprite's WIDTH that fills the slot (smaller = closer on the face).")]
    [Range(0.1f, 1f)] public float visibleWidth = 0.5f;

    /// <summary>Shows `sprite` cropped on the face, or a full-slot placeholder tint when null.</summary>
    public void Show(Sprite sprite, Color placeholderColor)
    {
        if (image == null) return;
        var slot = (RectTransform)transform;
        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        image.preserveAspect = false;
        image.sprite = sprite;

        Vector2 slotSize = slot.rect.size;
        if (sprite == null || slotSize.x <= 0f || slotSize.y <= 0f)
        {
            image.color = sprite != null ? Color.white : placeholderColor;
            rt.sizeDelta = slotSize;
            rt.anchoredPosition = Vector2.zero;
            return;
        }

        image.color = Color.white;
        Vector2 spriteSize = sprite.rect.size;
        float cover = Mathf.Max(slotSize.x / spriteSize.x, slotSize.y / spriteSize.y);
        float scale = Mathf.Max(cover, slotSize.x / (spriteSize.x * Mathf.Clamp(visibleWidth, 0.1f, 1f)));
        Vector2 size = spriteSize * scale;
        rt.sizeDelta = size;

        // Put the face point at the slot centre, then keep the sprite's edges outside the slot.
        Vector2 faceOffset = Vector2.Scale(faceCenter - new Vector2(0.5f, 0.5f), size);
        Vector2 maxShift = (size - slotSize) * 0.5f;
        rt.anchoredPosition = new Vector2(Mathf.Clamp(-faceOffset.x, -maxShift.x, maxShift.x),
                                          Mathf.Clamp(-faceOffset.y, -maxShift.y, maxShift.y));
    }
}
