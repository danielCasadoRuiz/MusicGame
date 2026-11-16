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
    private readonly Dictionary<Image,Material> _transitions = new();

    private TMPro.TextMeshProUGUI _styleLabel;

    private void Awake()
    {
        var bar = transform.Find("HudBar");
        if (bar == null) return;
        foreach (string name in new[] { "StyleBar", "ClassicBar", "PauseWell" })
        {
            var graphic = bar.Find(name)?.GetComponent<Image>();
            if (graphic != null) graphic.color = Color.clear;
        }
        void Place(string name, float x, float y, float w, float h)
        {
            var rt = bar.Find(name) as RectTransform;
            if (rt == null) return;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.up;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
        Place("Logo", 16, 8, 1920, 1080);
        if (logo != null) logo.transform.localScale = Vector3.one * .21f;
        string[] cells = { "Kick", "Snare", "HiHat", "Beat", "Onset", "Impact", "Life", "Special" };
        for (int i = 0; i < cells.Length; i++)
            Place("Cell_" + cells[i], i < 6 ? 440 + i * 148 : 1384 + (i - 6) * 148, 66, 148, 112);
        int separator = 0;
        foreach (Transform child in bar)
            if (child.name == "Separator")
            {
                var rt = (RectTransform)child;
                rt.anchoredPosition = new Vector2(separator++ == 0 ? 1350 : 1708, -66);
            }
        Place("ArtistSong", 1742, 40, 810, 58);
        Place("ProgressBorder", 1742, 104, 1080, 28);
        Place("ProgressTrack", 1745, 107, 1074, 22);
        Place("ProgressFill", 1745, 107, 1074, 22);
        var title = bar.Find("ArtistSong")?.GetComponent<TMPro.TextMeshProUGUI>();
        if (title != null)
        {
            _styleLabel = Instantiate(title, bar);
            _styleLabel.name = "PlayingStyle";
            if(library!=null && library.titleFont!=null)
            {
                _styleLabel.font=library.titleFont;_styleLabel.fontSharedMaterial=library.titleFont.material;
                _styleLabel.color=library.titleColor;
            }
            DetectedStyleLabel.RegisterHud(_styleLabel);
            _styleLabel.fontSizeMin = _styleLabel.fontSizeMax = _styleLabel.fontSize = 30;
            _styleLabel.alignment = TMPro.TextAlignmentOptions.MidlineRight;
            Place("PlayingStyle", 2568, 40, 254, 58);
        }
        _iconX = null;
    }

    public void Apply(GameMusicStyle style, float styleWeight)
    {
        if (library == null) return;
        styleWeight = Mathf.Clamp01(styleWeight);
        if (style != _style)
        {
            _style = style;
            if (_styleLabel != null) _styleLabel.text = DetectedStyleLabel.Caption(style);
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
        var reference = classicBar != null ? classicBar.rectTransform : (RectTransform)transform;
        if(logo!=null)logo.ApplyClassicCurtain(classicShare,reference);
        for (int i = 0; i < icons.Count; i++)
        {
            var classic = icons[i].classic;
            if (classic == null || icons[i].normal == null) continue;
            var normal=icons[i].normal;
            if(Transition(normal,reference,classicShare,classic.sprite,Color.white,Color.white))classic.enabled=false;
        }
    }

    private void Tint(Image image, Color style, Color classic, float classicShare)
    {
        if (image == null || classicBar == null) return;
        Transition(image,classicBar.rectTransform,classicShare,null,style,classic);
    }

    private bool Transition(Image image,RectTransform reference,float share,Sprite classic,Color styleColor,Color classicColor)
    {
        if(!_transitions.TryGetValue(image,out var material))
        {
            material=UIStyleTransition.Create(library,"HUD spatial style transition");
            if(material==null)return false;
            _transitions.Add(image,material);
        }
        UIStyleTransition.Apply(material,image,reference,library,share,classic,styleColor,classicColor);
        return true;
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
        foreach(var material in _transitions.Values)UIStyleTransition.Release(material);
        if (_curtain == null) return;
        if (Application.isPlaying) Destroy(_curtain); else DestroyImmediate(_curtain);
    }
}
