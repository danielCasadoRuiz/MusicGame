using System;
using UnityEngine;

public class GameplayHUD : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;

    private CollectionStats _stats       = new();
    private int             _totalRings;
    private float           _songDuration;
    private bool            _gameEnded;
    private CollectionStats _finalStats;
    private Texture2D       _white;

    private Action<RingCollectedEvent>   _onRing;
    private Action<LevelGeneratedEvent>  _onLevel;
    private Action<GameEndedEvent>       _onEnd;
    private Action<SongProfileReadyEvent> _onProfile;

    private void Awake()
    {
        _white = new Texture2D(1, 1);
        _white.SetPixel(0, 0, Color.white);
        _white.Apply();
    }

    private void OnEnable()
    {
        _onRing    = e  => _stats.Register(e.Type);
        _onLevel   = e  => _totalRings = e.RingCount;
        _onEnd     = e  => { _gameEnded = true; _finalStats = e.Stats; };
        _onProfile = e  => _songDuration = e.Profile.duration;

        EventBus.Subscribe(_onRing);
        EventBus.Subscribe(_onLevel);
        EventBus.Subscribe(_onEnd);
        EventBus.Subscribe(_onProfile);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onRing);
        EventBus.Unsubscribe(_onLevel);
        EventBus.Unsubscribe(_onEnd);
        EventBus.Unsubscribe(_onProfile);
    }

    private void OnDestroy()
    {
        if (_white != null) Destroy(_white);
    }

    private void OnGUI()
    {
        if (_gameEnded) DrawEndScreen();
        else            DrawLiveHUD();
    }

    // ── Live HUD (top bar + progress) ────────────────────────────────────────

    private void DrawLiveHUD()
    {
        float sw = Screen.width;

        Rect(0, 0, sw, 44f, new Color(0.03f, 0.03f, 0.03f, 0.88f));
        Rect(0, 44f, sw, 1f, new Color(0.15f, 0.15f, 0.15f));

        float cellW = sw / 6f;
        Counter(cellW * 0f, "KICK",   _stats.Get(RingType.Kick),  new Color(0.90f, 0.22f, 0.10f));
        Counter(cellW * 1f, "SNARE",  _stats.Get(RingType.Snare), new Color(0.90f, 0.80f, 0.10f));
        Counter(cellW * 2f, "HI-HAT", _stats.Get(RingType.HiHat), new Color(0.15f, 0.62f, 1.00f));
        Counter(cellW * 3f, "BEAT",   _stats.Get(RingType.Beat),  new Color(0.62f, 0.20f, 0.90f));
        Counter(cellW * 4f, "ONSET",  _stats.Get(RingType.Onset), new Color(0.10f, 0.90f, 0.50f));

        int   total = _stats.Total;
        float pct   = _totalRings > 0 ? (float)total / _totalRings * 100f : 0f;
        GUI.color   = Color.white;
        GUI.Label(new Rect(cellW * 5f + 6f, 3f,  cellW, 20f), "TOTAL");
        GUI.Label(new Rect(cellW * 5f + 6f, 22f, cellW, 20f), $"{total}  {pct:F0}%");

        // Song progress bar
        if (audioSource != null && audioSource.isPlaying && _songDuration > 0f)
        {
            float progress = Mathf.Clamp01(audioSource.time / _songDuration);
            Rect(0, 45f, sw,            4f, new Color(0.10f, 0.10f, 0.10f));
            Rect(0, 45f, sw * progress, 4f, new Color(0.18f, 0.75f, 0.95f));
        }
    }

    private void Counter(float x, string label, int value, Color color)
    {
        GUI.color = color;
        GUI.Label(new Rect(x + 6f, 3f,  100f, 20f), label);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 6f, 22f, 100f, 20f), value.ToString());
    }

    // ── End screen ────────────────────────────────────────────────────────────

    private void DrawEndScreen()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float pw = 340f, ph = 300f;
        float px = (sw - pw) / 2f;
        float py = (sh - ph) / 2f;

        Rect(px - 3f, py - 3f, pw + 6f, ph + 6f, new Color(0.15f, 0.15f, 0.15f));
        Rect(px, py, pw, ph, new Color(0.04f, 0.04f, 0.04f, 0.97f));

        var s = _finalStats ?? _stats;

        CenteredLabel(px, py + 14f, pw, 24f, "─── RESULTS ───", 16);

        float row = py + 55f;
        StatLine(px, row,        pw, "KICK",   s.Get(RingType.Kick),  new Color(0.90f, 0.22f, 0.10f), s);
        StatLine(px, row + 46f,  pw, "SNARE",  s.Get(RingType.Snare), new Color(0.90f, 0.80f, 0.10f), s);
        StatLine(px, row + 92f,  pw, "HI-HAT", s.Get(RingType.HiHat), new Color(0.15f, 0.62f, 1.00f), s);
        StatLine(px, row + 138f, pw, "BEAT",   s.Get(RingType.Beat),  new Color(0.62f, 0.20f, 0.90f), s);
        StatLine(px, row + 184f, pw, "ONSET",  s.Get(RingType.Onset), new Color(0.10f, 0.90f, 0.50f), s);

        int   total = s.Total;
        float pct   = _totalRings > 0 ? (float)total / _totalRings * 100f : 0f;
        GUI.color   = Color.white;
        CenteredLabel(px, py + ph - 28f, pw, 20f, $"TOTAL  {total} / {_totalRings}  ({pct:F0}%)", 14);
    }

    private void StatLine(float x, float y, float w, string label, int value, Color color, CollectionStats s)
    {
        float maxPerType = Mathf.Max(_totalRings / 5f, 1f);
        float barMaxW    = w - 180f;
        float barFill    = Mathf.Clamp01(value / maxPerType) * barMaxW;

        GUI.color = color;
        GUI.Label(new Rect(x + 20f, y + 2f, 90f, 20f), label);

        Rect(x + 120f, y + 6f, barMaxW, 10f, new Color(0.12f, 0.12f, 0.12f));
        Rect(x + 120f, y + 6f, barFill, 10f, color * 0.75f);

        GUI.color = Color.white;
        GUI.Label(new Rect(x + w - 34f, y + 2f, 34f, 20f), value.ToString());
    }

    // ── Draw helpers ──────────────────────────────────────────────────────────

    private void Rect(float x, float y, float w, float h, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new UnityEngine.Rect(x, y, w, h), _white);
        GUI.color = Color.white;
    }

    private void CenteredLabel(float x, float y, float w, float h, string text, int size = 12)
    {
        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = size };
        GUI.color = Color.white;
        GUI.Label(new UnityEngine.Rect(x, y, w, h), text, style);
    }
}
