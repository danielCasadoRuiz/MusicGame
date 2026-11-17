using UnityEngine;
using UnityEngine.UI;

/// <summary>Only the appearance of the existing touch controls; input and hit areas stay authored.</summary>
[DisallowMultipleComponent]
public sealed class MobileControlSkin : MonoBehaviour
{
    public StyleSkinLibrarySO library;
    public Texture2D atlas;
    private Sprite[] sprites;
    private Image[] normal, classic;
    private GameMusicStyle shown = (GameMusicStyle)(-1);
    private float priorShare = -1;

    private void Awake() => Build();
    public void Build()
    {
        if (sprites != null || atlas == null) return;
        var view = GetComponent<MobileControlsView>();
        if (view == null) return;
        sprites = new Sprite[6];
        float w = atlas.width / 3f, h = atlas.height / 2f;
        for (int n = 0; n < 6; n++)
            sprites[n] = Sprite.Create(atlas, new Rect(n % 3 * w + 2, (1 - n / 3) * h + 2, w - 4, h - 4), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        normal = new[] { view.joystickBackground?.GetComponent<Image>(), view.joystickHandle?.GetComponent<Image>(), view.jumpButton?.GetComponent<Image>(), view.crouchButton?.GetComponent<Image>() };
        classic = new Image[normal.Length];
        for (int i = 0; i < normal.Length; i++)
        {
            if (normal[i] == null) continue;
            int slot = Mathf.Min(i, 2);
            normal[i].sprite = sprites[slot]; normal[i].preserveAspect = true;
            var rt = UIFactory.CreateRect("ClassicalTouchArt", normal[i].rectTransform);
            UIFactory.Stretch(rt); rt.SetAsFirstSibling();
            classic[i] = rt.gameObject.AddComponent<Image>(); classic[i].sprite = sprites[slot + 3];
            classic[i].preserveAspect = true; classic[i].raycastTarget = false;
        }
    }
    private void LateUpdate()
    {
        if (library == null) return;
        var style = StyleSkinLibrarySO.CurrentStyle();
        float share = style == GameMusicStyle.Classical ? 1 : 1 - library.RunnerWeight(GameplayHUD.SongProgress);
        Apply(style, share);
    }
    public void Apply(GameMusicStyle style, float classicShare)
    {
        Build(); if (normal == null || library == null) return;
        classicShare = style == GameMusicStyle.Classical ? 1 : Mathf.Clamp01(classicShare);
        if (style == shown && Mathf.Abs(classicShare - priorShare) < .002f) return;
        shown = style; priorShare = classicShare;
        Color accent = Color.Lerp(Color.white, library.HudAccent(style), .6f);
        for (int i = 0; i < normal.Length; i++)
        {
            if (normal[i] == null) continue;
            normal[i].color = new Color(accent.r, accent.g, accent.b, .85f * (1 - classicShare));
            classic[i].color = new Color(1, 1, 1, .85f * classicShare);
        }
    }
    private void OnDestroy()
    {
        if (sprites == null) return;
        foreach (var sprite in sprites)
            if (sprite != null) { if (Application.isPlaying) Destroy(sprite); else DestroyImmediate(sprite); }
    }
}
