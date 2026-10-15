using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Progressive reveal for ONE TextMeshPro text: the full string is assigned once and only
/// maxVisibleCharacters advances, counted over the characters TMP actually parsed (rich-text tags
/// are never shown half-typed and no substring is rebuilt per frame). Line breaks are therefore
/// final from the first frame — the text never re-wraps while it types.
///
/// Unscaled time, so a timeScale 0 elsewhere never freezes a subtitle; IsPaused lets the owner tie
/// it to a real pause instead (e.g. FightController.IsPaused).
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TypewriterText : MonoBehaviour
{
    private const int ShowAll = 99999; // TMP's own "no limit" default for maxVisibleCharacters

    private TMP_Text _text;
    private int      _total;
    private int      _visible;
    private float    _charsPerSecond;
    private float    _punctuationPause;
    private float    _budget;

    public bool IsRevealing { get; private set; }
    public Func<bool> IsPaused;
    public event Action Completed;

    private TMP_Text Text => _text != null ? _text : _text = GetComponent<TMP_Text>();

    /// <summary>Assigns `fullText` and reveals it at `charsPerSecond`. A zero/negative/NaN speed shows
    /// it complete at once (never a stuck, invisible line).</summary>
    public void Play(string fullText, float charsPerSecond, float punctuationPause = 0f)
    {
        _charsPerSecond   = charsPerSecond;
        _punctuationPause = float.IsNaN(punctuationPause) || punctuationPause < 0f ? 0f : punctuationPause;
        _budget  = 0f;
        _visible = 0;

        Text.text = fullText ?? "";
        Text.maxVisibleCharacters = 0;
        Text.ForceMeshUpdate();
        _total = Text.textInfo.characterCount;

        bool validSpeed = !float.IsNaN(charsPerSecond) && !float.IsInfinity(charsPerSecond) && charsPerSecond > 0f;
        if (_total == 0 || !validSpeed) { Complete(); return; }
        IsRevealing = true;
    }

    /// <summary>Same position, different string (the language changed): keeps the revealed FRACTION,
    /// or stays fully shown if it already was.</summary>
    public void ReplaceText(string fullText)
    {
        bool wasRevealing = IsRevealing;
        float fraction = _total > 0 ? (float)_visible / _total : 1f;

        Text.text = fullText ?? "";
        Text.maxVisibleCharacters = ShowAll;
        Text.ForceMeshUpdate();
        _total = Text.textInfo.characterCount;

        if (!wasRevealing) return;
        _visible = Mathf.Clamp(Mathf.FloorToInt(fraction * _total), 0, _total);
        if (_visible >= _total) { Complete(); return; }
        Text.maxVisibleCharacters = _visible;
    }

    /// <summary>Shows the whole text now. Raises Completed once per Play.</summary>
    public void Complete()
    {
        bool wasRevealing = IsRevealing || _visible < _total;
        IsRevealing = false;
        _visible = _total;
        Text.maxVisibleCharacters = ShowAll;
        if (wasRevealing || _total == 0) Completed?.Invoke();
    }

    /// <summary>Stops without raising Completed and leaves the text empty.</summary>
    public void Clear()
    {
        IsRevealing = false;
        _total = _visible = 0;
        Text.text = "";
        Text.maxVisibleCharacters = ShowAll;
    }

    private void Update()
    {
        if (!IsRevealing) return;
        if (IsPaused != null && IsPaused()) return;

        // Clamp a hitch (scene load, breakpoint) so it never dumps the whole line in one frame.
        _budget += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float perChar = 1f / _charsPerSecond;
        var chars = Text.textInfo.characterInfo;

        while (_budget >= perChar && _visible < _total)
        {
            _budget -= perChar;
            _visible++;
            char c = _visible - 1 < chars.Length ? chars[_visible - 1].character : '\0';
            if (_punctuationPause > 0f && _visible < _total && IsPausePunctuation(c))
                _budget -= _punctuationPause; // the next character waits a little longer
        }

        if (_visible >= _total) Complete();
        else Text.maxVisibleCharacters = _visible;
    }

    private static bool IsPausePunctuation(char c) => c == '.' || c == '!' || c == '?' || c == ',' || c == ';' || c == ':' || c == '…';
}
