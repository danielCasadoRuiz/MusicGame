using UnityEngine;

/// <summary>
/// Sits on a menu root (the Runner pause overlay, the Fight pause panel) and pushes one style + weight to
/// every StyleBlendButton under it. PauseController / FightController call Apply when the menu opens:
/// the Runner from the played fraction of the song, the Fight from who is winning (StyleSkinLibrarySO).
/// </summary>
public class StyleBlendGroup : MonoBehaviour
{
    public StyleSkinLibrarySO library;

    public void Apply(GameMusicStyle style, float styleWeight)
    {
        foreach (var button in GetComponentsInChildren<StyleBlendButton>(true)) button.SetBlend(style, styleWeight);
    }
}
