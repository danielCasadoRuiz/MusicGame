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
    private TMPro.TextMeshProUGUI _songLabel;

    private void Awake() => ConfigureLayout();
    public void ConfigureLayout()
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
        Place("Logo", -28, 8, 1920, 1080);
        if (logo != null) logo.transform.localScale = Vector3.one * .21f;
        string[] cells = { "Kick", "Snare", "HiHat", "Beat", "Onset", "Impact", "Life", "Special", "TripleCombo", "QuadCombo" };
        for (int i = 0; i < cells.Length; i++)
        {
            Place("Cell_" + cells[i], i < 6 ? 390 + i * 130 : 1220 + (i - 6) * 132, 66, 132, 112);
            var cell = bar.Find("Cell_" + cells[i]);
            if (cell == null) continue;
            foreach (string name in new[] { "Icon", "IconClassic" })
            {
                var icon = cell.Find(name) as RectTransform;
                if (icon != null) { icon.anchoredPosition = new Vector2(0, -12); icon.sizeDelta = Vector2.one * 76; }
            }
            var value = cell.Find("Value")?.GetComponent<TMPro.TextMeshProUGUI>();
            if (value != null) { value.rectTransform.anchoredPosition = new Vector2(76, -17); value.rectTransform.sizeDelta = new Vector2(54, 72); value.enableAutoSizing = true; value.fontSizeMin = 20; value.fontSizeMax = 42; }
        }
        int separator = 0;
        foreach (Transform child in bar)
            if (child.name == "Separator")
            {
                var rt = (RectTransform)child;
                rt.anchoredPosition = new Vector2(separator++ == 0 ? 1198 : 1780, -94);
                rt.sizeDelta = new Vector2(2, 52);
            }
        Place("ArtistSong", 1810, 40, 704, 58);
        Place("ProgressBorder", 1810, 104, 1035, 22);
        Place("ProgressTrack", 1813, 107, 1029, 16);
        Place("ProgressFill", 1813, 107, 1029, 16);
        var title = bar.Find("ArtistSong")?.GetComponent<TMPro.TextMeshProUGUI>();
        if (title != null)
        {
            _songLabel = title;
            title.enableAutoSizing = true;
            title.fontSizeMin = 8; title.fontSizeMax = 30;
            title.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            title.overflowMode = TMPro.TextOverflowModes.Overflow;
            _styleLabel = bar.Find("PlayingStyle")?.GetComponent<TMPro.TextMeshProUGUI>();
            if (_styleLabel == null) _styleLabel = Instantiate(title, bar);
            _styleLabel.name = "PlayingStyle";
            if(library!=null && library.titleFont!=null)
            {
                _styleLabel.font=library.titleFont;_styleLabel.fontSharedMaterial=library.titleFont.material;
                _styleLabel.color=library.titleColor;
            }
            DetectedStyleLabel.RegisterHud(_styleLabel);
            _styleLabel.enableAutoSizing = true;
            _styleLabel.fontSizeMin = 8; _styleLabel.fontSizeMax = _styleLabel.fontSize = 30;
            _styleLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            _styleLabel.overflowMode = TMPro.TextOverflowModes.Overflow;
            _styleLabel.wordSpacing = AnalysisPresentation.BrushWordSpacing;
            _styleLabel.alignment = TMPro.TextAlignmentOptions.MidlineRight;
            Place("PlayingStyle", 2530, 40, 315, 58);
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
            if (_styleLabel != null)
            {
                _styleLabel.text = DetectedStyleLabel.Caption(style);
                _styleLabel.enableVertexGradient = false;
                _styleLabel.color = library.titleColor;
            }
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
        float classicShare = style == GameMusicStyle.Classical || style == GameMusicStyle.Unknown ? 1f : 1f - styleWeight;
        SetCurtain(classicShare);
        Color accent = library.HudAccent(style);
        Color gold = library.HudAccent(GameMusicStyle.Classical);
        foreach (var separator in separators) Tint(separator, accent, gold, classicShare);
        Tint(progressFill, accent, gold, classicShare);
        Tint(progressBorder, accent, gold, classicShare);
        var reference = classicBar != null ? classicBar.rectTransform : (RectTransform)transform;
        TintText(_styleLabel, reference, library.titleColor, gold, classicShare);
        TintText(_songLabel, reference, library.titleColor, gold, classicShare);
        if(logo!=null)logo.ApplyClassicCurtain(classicShare,reference);
        for (int i = 0; i < icons.Count; i++)
        {
            var classic = icons[i].classic;
            if (classic == null || icons[i].normal == null) continue;
            var normal=icons[i].normal;
            bool combo = icons[i].kind == BonusKind.TripleCombo || icons[i].kind == BonusKind.QuadCombo;
            if(Transition(normal,reference,classicShare,classic.sprite,Color.white,Color.white,combo))classic.enabled=false;
        }
    }

    private void Tint(Image image, Color style, Color classic, float classicShare)
    {
        if (image == null || classicBar == null) return;
        if (!Transition(image,classicBar.rectTransform,classicShare,null,style,classic)) image.color = Color.Lerp(style, classic, classicShare);
    }

    private bool Transition(Image image,RectTransform reference,float share,Sprite classic,Color styleColor,Color classicColor,bool gold=false)
    {
        if(!_transitions.TryGetValue(image,out var material))
        {
            material=UIStyleTransition.Create(library,"HUD spatial style transition");
            if(material==null)return false;
            _transitions.Add(image,material);
        }
        UIStyleTransition.Apply(material,image,reference,library,share,classic,styleColor,classicColor,gold);
        return true;
    }

    private void TintText(TMPro.TMP_Text text, RectTransform reference, Color style, Color gold, float share)
    {
        if (text == null || reference == null || reference.rect.width <= 0) return;
        text.color = Color.white; text.ForceMeshUpdate();
        var info = text.textInfo;
        for (int i = 0; i < info.characterCount; i++)
        {
            var ch = info.characterInfo[i]; if (!ch.isVisible) continue;
            var mesh = info.meshInfo[ch.materialReferenceIndex];
            for (int v = 0; v < 4; v++)
            {
                int index = ch.vertexIndex + v;
                var local = reference.InverseTransformPoint(text.transform.TransformPoint(mesh.vertices[index]));
                float x = (local.x - reference.rect.xMin) / reference.rect.width;
                mesh.colors32[index] = Color.Lerp(style, gold, library.CurtainClassic(share, x));
            }
        }
        text.UpdateVertexData(TMPro.TMP_VertexDataUpdateFlags.Colors32);
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
