using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// COORDINATION of one composer line in a ComposerSpeechBubble, for an owner that sequences the
/// screen (VersusScreenController: intro, MatchFarewellController: post-match). Driven by the owner's
/// Update through Tick(), so the camera pass, the reveal and the reading hold are ONE coordinated
/// timeline with ONE cancellation point — no free-running coroutines asking for transitions.
///
///   Loading → (display allowed) Revealing → Holding (FightFlowConfig.dialogueReadHoldSeconds) → Done
///                                    ↘ Unavailable (disabled, no content, load error, load timeout, skipped while loading)
///   IsReadingComplete  Done or Unavailable — the owner may advance
///   SkipToEnd()        line complete at once, no hold; still loading → dropped (never appears late)
///   Freeze()           the owner moved on (countdown): the line stays as it is, timers stop for good
///   FadeOut()/Cancel() leave; a cancelled presentation never shows or reports anything again
///
/// Language change keeps composer, category and KEY (never a new draw): revealing → types the new
/// translation from the start; complete → shows it complete and restarts the reading hold; frozen →
/// just swaps the text. A per-line English fallback never changes the global locale.
///
/// VOICE: the line's generated audio (ComposerVoiceService, in the language the text is shown in, only if its text
/// hash matches) is awaited at most VoiceWaitSeconds together with the text; it starts WITH the bubble (the reveal
/// is sped up if needed to keep pace with it) and the reading hold also waits for it to end. Missing / late / stale
/// audio = subtitle only, and a late clip is never played afterwards. Skip, Freeze, FadeOut, Cancel and a language
/// change stop it.
/// </summary>
public sealed class ComposerDialoguePresentation
{
    public enum Phase { Idle, Loading, Revealing, Holding, Done, Unavailable }

    private readonly ComposerSpeechBubble _bubble;
    private readonly FightFlowConfig      _config;

    private int    _token;
    private CancellationTokenSource _cts;
    private string _composerId, _speaker, _key, _pendingText;
    private ComposerDialogueCategory _category;
    private int _version;
    private float  _loadElapsed, _holdLeft;
    private bool   _displayAllowed, _frozen, _localeChangedWhileLoading, _subscribed;
    private AudioClip _pendingVoice;
    private int    _voiceToken;
    private bool   _voicePaused;
    private const float VoiceWaitSeconds = 1.5f;

    public Phase  CurrentPhase { get; private set; } = Phase.Idle;
    public string CurrentKey => _key;
    public ComposerSpeechBubble Bubble => _bubble;
    public bool   IsActive => CurrentPhase != Phase.Idle;
    public bool   IsReadingComplete => CurrentPhase == Phase.Done || CurrentPhase == Phase.Unavailable;

    public ComposerDialoguePresentation(ComposerSpeechBubble bubble, FightFlowConfig config)
    {
        _bubble = bubble;
        _config = config;
        if (_bubble != null) _bubble.IsPaused = () => FightController.IsPaused;
    }

    private float CharsPerSecond => _config != null ? _config.dialogueCharactersPerSecond : 40f;
    private float PunctuationPause => _config != null ? _config.dialoguePunctuationPauseSeconds : 0.12f;
    private float ReadHold => _config != null ? Mathf.Max(0f, _config.dialogueReadHoldSeconds) : 2f;
    private float LoadTimeout => _config != null ? _config.dialogueLoadTimeoutSeconds : 3f;

    /// <summary>Starts loading a line for (opponent, category); the bubble points at `anchor`.
    /// Nothing is displayed until AllowDisplay() (the owner may still be showing something else).</summary>
    /// <param name="version">Rival version (OpponentLevelConfig.level): lines written for that version win.</param>
    public void Begin(OpponentDefinition opponent, ComposerDialogueCategory category, Func<Vector3?> anchor, bool displayAllowed = true, int version = 0)
    {
        Cancel();
        if (_bubble == null) { CurrentPhase = Phase.Unavailable; return; } // no bubble prefab: logged by Spawn; never blocks the flow
        _bubble.AnchorProvider = anchor;
        _bubble.AvoidCenterBand = false;
        if ((_config != null && !_config.composerDialoguesEnabled) || opponent == null || string.IsNullOrEmpty(opponent.id))
        {
            CurrentPhase = Phase.Unavailable;
            return;
        }

        int token = ++_token;
        _cts = new CancellationTokenSource();
        _composerId = opponent.id;
        _speaker = opponent.displayName;
        _category = category;
        _version = version;
        _key = null; _pendingText = null;
        _loadElapsed = 0f;
        _displayAllowed = displayAllowed;
        _frozen = _localeChangedWhileLoading = false;
        CurrentPhase = Phase.Loading;
        if (!_subscribed) { LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged; _subscribed = true; }
        _ = LoadAsync(token, _cts.Token);
    }

    public void AllowDisplay()
    {
        _displayAllowed = true;
        TryDisplayPending();
    }

    /// <summary>Owner's per-frame tick (unscaled seconds).</summary>
    public void Tick(float dt)
    {
        if (_frozen) return;
        switch (CurrentPhase)
        {
            case Phase.Loading:
                _loadElapsed += dt;
                if (LoadTimeout > 0f && _loadElapsed >= LoadTimeout && _pendingText == null)
                {
                    Debug.LogWarning($"[ComposerDialogue] {_category} line for '{_composerId}' not ready after {LoadTimeout:0.#} s — continuing without it.");
                    DropLoad();
                }
                break;
            case Phase.Revealing:
                if (!_bubble.IsRevealing) { CurrentPhase = Phase.Holding; _holdLeft = ReadHold; }
                break;
            case Phase.Holding:
                if (FightController.IsPaused) break;
                _holdLeft -= dt;
                if (_holdLeft <= 0f && VoiceRemaining <= 0f) CurrentPhase = Phase.Done; // never cut the voice
                break;
        }
        // The voice pauses with the fight.
        bool paused = FightController.IsPaused;
        if (paused != _voicePaused && _voiceToken != 0) { _voicePaused = paused; GameAudio.Instance?.PauseVoice(paused, _voiceToken); }
    }

    private float VoiceRemaining => _voiceToken != 0 && GameAudio.Instance != null ? GameAudio.Instance.VoiceRemaining(_voiceToken) : 0f;

    private void StopVoice(float fade)
    {
        if (_voiceToken != 0) GameAudio.Instance?.StopVoice(fade, _voiceToken);
        _voiceToken = 0;
        _voicePaused = false;
    }

    /// <summary>Skip: the line (if any) complete at once, no reading hold; a line still loading is
    /// dropped and can never appear later.</summary>
    public void SkipToEnd()
    {
        switch (CurrentPhase)
        {
            case Phase.Loading:
                if (_pendingText != null && _displayAllowed) { TryDisplayPending(); goto case Phase.Revealing; }
                DropLoad();
                break;
            case Phase.Revealing:
            case Phase.Holding:
                _bubble.CompleteReveal();
                StopVoice(0.12f);
                CurrentPhase = Phase.Done;
                break;
        }
    }

    /// <summary>The owner has moved on (countdown started): keep the line on screen as it is, but no
    /// timer or late load may change anything any more (language changes still swap its text).</summary>
    public void Freeze()
    {
        if (CurrentPhase == Phase.Loading) DropLoad();
        StopVoice(0.25f); // the countdown takes over: no voice under it
        _frozen = true;
    }

    public void FadeOut(float seconds, Action done = null)
    {
        int token = _token;
        StopLoading();
        StopVoice(seconds);
        if (_bubble == null) { Reset(); done?.Invoke(); return; }
        _bubble.FadeOut(seconds, () =>
        {
            if (token == _token) Reset();
            done?.Invoke();
        });
    }

    /// <summary>Gone at once; nothing pending can show or report afterwards.</summary>
    public void Cancel()
    {
        StopLoading();
        StopVoice(0f);
        if (_bubble != null) _bubble.Hide();
        Reset();
    }

    // ── internals ────────────────────────────────────────────────────────────────

    private void Reset()
    {
        _token++;
        CurrentPhase = Phase.Idle;
        _key = null; _pendingText = null; _pendingVoice = null;
        _frozen = false;
        if (_subscribed) { LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged; _subscribed = false; }
    }

    private void StopLoading()
    {
        if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
    }

    private void DropLoad()
    {
        _token++;              // a late result is ignored
        StopLoading();
        _pendingText = null;
        _pendingVoice = null;
        CurrentPhase = Phase.Unavailable;
    }

    private async Task LoadAsync(int token, CancellationToken ct)
    {
        ComposerDialogueLine line;
        try
        {
            line = await ComposerDialogueService.PickLineAsync(_composerId, _category, ct, _version);
            if (token != _token) return;
            if (line.IsValid && _localeChangedWhileLoading)
            {
                var again = await ComposerDialogueService.ResolveTextAsync(_composerId, _category, line.Key, ct);
                if (token != _token) return;
                if (again.IsValid) line = again;
            }
        }
        catch (Exception e)
        {
            if (token != _token) return;
            Debug.LogError($"[ComposerDialogue] {_category} line for '{_composerId}' failed: {e}");
            line = default;
        }

        if (!line.IsValid) { CurrentPhase = Phase.Unavailable; return; }

        // Its voice, bounded: whatever is not ready within VoiceWaitSeconds is dropped (subtitle only).
        AudioClip voice;
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            wait.CancelAfter(TimeSpan.FromSeconds(VoiceWaitSeconds));
            voice = await ComposerVoiceService.GetLineAsync(_composerId, line.Key, line.Text, line.LocaleCode, wait.Token);
        }
        if (token != _token) return;

        _key = line.Key;
        _pendingText = line.Text;
        _pendingVoice = voice;
        TryDisplayPending();
    }

    private void TryDisplayPending()
    {
        if (CurrentPhase != Phase.Loading || _pendingText == null || !_displayAllowed || _frozen) return;
        float cps = CharsPerSecond;
        if (_pendingVoice != null)
        {
            _voiceToken = GameAudio.Instance != null ? GameAudio.Instance.PlayVoice(_pendingVoice, GameAudioScope.Fight) : 0;
            // The text never lags behind the voice: done typing by ~90 % of the spoken line.
            if (_voiceToken != 0) cps = Mathf.Max(cps, _pendingText.Length / Mathf.Max(0.4f, _pendingVoice.length * 0.9f));
        }
        _pendingVoice = null;
        _bubble.Show(_speaker, _pendingText, cps, PunctuationPause);
        _pendingText = null;
        CurrentPhase = _bubble.IsRevealing ? Phase.Revealing : Phase.Holding;
        _holdLeft = ReadHold;
    }

    private void OnLocaleChanged(Locale locale)
    {
        StopVoice(0.15f); // the voice was in the previous language
        if (CurrentPhase == Phase.Loading && _key == null) { _localeChangedWhileLoading = true; return; }
        if (_key == null) return;
        _ = ReResolveAsync(_token, _key, _cts != null ? _cts.Token : default);
    }

    private async Task ReResolveAsync(int token, string key, CancellationToken ct)
    {
        ComposerDialogueLine line;
        try { line = await ComposerDialogueService.ResolveTextAsync(_composerId, _category, key, ct); }
        catch (Exception e)
        {
            if (token == _token) Debug.LogWarning($"[ComposerDialogue] Re-reading '{key}' after a language change failed: {e.Message}");
            return;
        }
        if (token != _token || key != _key) return; // a newer change / another line won
        if (!line.IsValid) { Debug.LogWarning($"[ComposerDialogue] '{key}' has no text in the new language — keeping the current text."); return; }

        if (CurrentPhase == Phase.Loading) { _pendingText = line.Text; return; } // loaded, waiting for AllowDisplay
        if (_frozen) { _bubble.SetText(line.Text, false, CharsPerSecond, PunctuationPause); return; }
        bool wasRevealing = CurrentPhase == Phase.Revealing;
        _bubble.SetText(line.Text, wasRevealing, CharsPerSecond, PunctuationPause);
        if (wasRevealing && _bubble.IsRevealing) CurrentPhase = Phase.Revealing;
        else { CurrentPhase = Phase.Holding; _holdLeft = ReadHold; } // time to read the new text
    }
}
