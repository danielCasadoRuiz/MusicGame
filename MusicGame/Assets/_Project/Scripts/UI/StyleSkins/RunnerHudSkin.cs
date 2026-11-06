using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Tempo Fury look of the Runner's live HUD (LiveHud.prefab): the song style's top bar with the classic bar
/// drawn over it as a CURTAIN coming in from the right (MusicGame/UI/CurtainFade, soft edge
/// StyleSkinLibrarySO.runnerCurtainSoftness), each bonus icon turning into its gold classic version as the curtain
/// passes over it, and the TEMPO FURY logo of the song's style on the left.
/// Apply(style, weight) — weight = StyleSkinLibrarySO.RunnerWeight(progress), called every frame by GameplayHUD: at
/// the start of the song the curtain is off the right edge (all style), at the end it covers everything (all classic,
/// the rival's territory). The pause button in the bar's round slot is PauseMenu.prefab's; PauseController blends it
/// with the same curtain (CurtainClassic at the slot's x).
/// Built by TempoFuryUISetup.
/// </summary>
public class RunnerHudSkin : MonoBehaviour
{
    [System.Serializable]
    public class BonusIcon
    {
        public BonusKind kind;
        public Image normal;
        public Image classic;
    }

    private static readonly int EdgeId = Shader.PropertyToID("_Edge");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");

    public StyleSkinLibrarySO library;
    [Tooltip("Under: the song style's bar.")]
    public Image styleBar;
    [Tooltip("Over: the classic bar, drawn with the curtain material.")]
    public Image classicBar;
    public List<BonusIcon> icons = new();
    public StyledTitleView logo;
    public List<Image> separators = new();
    public Image progressFill;
    public Image progressBorder;

    private GameMusicStyle _style = (GameMusicStyle)(-1);
    private float _weight = -1f;
    private Material _curtain;
    private float[] _iconX;   // each icon's centre across the bar (0..1), as the curtain sees it

    public void Apply(GameMusicStyle style, float styleWeight)
    {
        if (library == null) return;
        styleWeight = Mathf.Clamp01(styleWeight);
        if (style != _style)
        {
            _style = style;
            var skin = style == GameMusicStyle.Classical ? null : library.Hud(style);
            if (styleBar != null)
            {
                styleBar.sprite = skin != null ? skin.bar : null;
                styleBar.enabled = styleBar.sprite != null;
            }
            if (logo != null) logo.Show(style);
            _weight = -1f;
        }
        if (Mathf.Abs(styleWeight - _weight) < 0.002f) return;
        _weight = styleWeight;

        // No style bar (Classical / unknown song): the classic bar alone, uncovered.
        float classicShare = styleBar != null && styleBar.enabled ? 1f - styleWeight : 1f;
        SetCurtain(classicShare);
        Color accent = library.HudAccent(style);
        Color gold = library.HudAccent(GameMusicStyle.Classical);
        foreach (var separator in separators) Tint(separator, accent, gold, classicShare);
        Tint(progressFill, accent, gold, classicShare);
        Tint(progressBorder, accent, gold, classicShare);
        EnsureIconPositions();
        for (int i = 0; i < icons.Count; i++)
        {
            var classic = icons[i].classic;
            if (classic == null) continue;
            float a = library.CurtainClassic(classicShare, _iconX[i]);
            classic.enabled = classic.sprite != null && a > 0.001f;
            var c = classic.color; c.a = a; classic.color = c;
        }
    }

    private void Tint(Image image, Color style, Color classic, float classicShare)
    {
        if (image == null || classicBar == null) return;
        var bar = classicBar.rectTransform;
        var center = bar.InverseTransformPoint(image.rectTransform.TransformPoint(image.rectTransform.rect.center));
        float x = Mathf.InverseLerp(bar.rect.xMin, bar.rect.xMax, center.x);
        image.color = Color.Lerp(style, classic, library.CurtainClassic(classicShare, x));
    }

    private void SetCurtain(float classicShare)
    {
        if (classicBar == null) return;
        if (_curtain == null && library.curtainMaterial != null)
        {
            _curtain = new Material(library.curtainMaterial) { name = "CurtainFade (HUD)" };
            classicBar.material = _curtain;
        }
        if (_curtain == null) { classicBar.enabled = classicShare > 0.5f; return; }   // no material: plain swap
        classicBar.enabled = true;
        _curtain.SetFloat(SoftnessId, library.runnerCurtainSoftness);
        _curtain.SetFloat(EdgeId, library.CurtainEdge(classicShare));
        classicBar.SetMaterialDirty();
    }

    private void EnsureIconPositions()
    {
        if (_iconX != null && _iconX.Length == icons.Count) return;
        _iconX = new float[icons.Count];
        var bar = classicBar != null ? classicBar.rectTransform : null;
        for (int i = 0; i < icons.Count; i++)
        {
            var img = icons[i].normal != null ? icons[i].normal : icons[i].classic;
            if (bar == null || img == null) { _iconX[i] = 0.5f; continue; }
            var local = bar.InverseTransformPoint(img.rectTransform.TransformPoint(img.rectTransform.rect.center));
            var r = bar.rect;
            _iconX[i] = Mathf.Clamp01((local.x - r.xMin) / r.width);
        }
    }

    private void OnDestroy()
    {
        if (_curtain == null) return;
        if (Application.isPlaying) Destroy(_curtain); else DestroyImmediate(_curtain);
    }
}
