using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// COORDINATION of one composer line on one screen (VersusScreenController, MatchResultController):
/// load → pick → show in a ComposerDialogueBox → wait for the player → report back ONCE.
///
///   Begin(opponent, category, onFinished)   onFinished(true)  the player continued past the line
///                                           onFinished(false) nothing could be shown (disabled, no
///                                                             content, load error or timeout) — the
///                                                             caller carries on without dialogue
///   Cancel()                                screen hidden / Fight left: nothing is reported, the box
///                                           is emptied, pending loads are ignored when they land
///
/// Every async step captures a presentation token and is dropped if the token changed meanwhile, so
/// a slow load from a previous rival / screen can never replace the current line or move the flow.
/// A language change re-resolves the SAME key (never a new draw); one that happens while the line is
/// still loading is applied before it is shown.
/// </summary>
public sealed class ComposerDialoguePresentation
{
    private readonly ComposerDialogueBox _box;
    private readonly FightFlowConfig     _config;

    private int    _token;
    private CancellationTokenSource _cts;
    private Action<bool> _onFinished;
    private string _composerId, _speaker, _key;
    private ComposerDialogueCategory _category;
    private bool   _localeChangedWhileLoading;
    private bool   _subscribedToLocale;

    public bool   IsActive   => _onFinished != null;
    public string CurrentKey => _key;
    public ComposerDialogueBox Box => _box;

    public ComposerDialoguePresentation(ComposerDialogueBox box, FightFlowConfig config)
    {
        _box = box;
        _config = config;
        _box.ContinueRequested += OnContinueRequested;
        _box.IsPaused = () => FightController.IsPaused;
    }

    public void Begin(OpponentDefinition opponent, ComposerDialogueCategory category, Action<bool> onFinished,
                      float autoContinueSeconds = 0f)
    {
        Cancel();
        if (onFinished == null) return;
        if ((_config != null && !_config.composerDialoguesEnabled) || opponent == null || string.IsNullOrEmpty(opponent.id))
        {
            onFinished(false);
            return;
        }

        int token = ++_token;
        _cts = new CancellationTokenSource();
        _onFinished = onFinished;
        _composerId = opponent.id;
        _speaker    = opponent.displayName;
        _category   = category;
        _key        = null;
        _localeChangedWhileLoading = false;
        if (!_subscribedToLocale) { LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged; _subscribedToLocale = true; }

        _ = RunAsync(token, _cts.Token, ComposerDialogueTiming.From(_config, autoContinueSeconds));
    }

    public void Cancel()
    {
        _token++;
        if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
        _onFinished = null;
        _key = null;
        if (_subscribedToLocale) { LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged; _subscribedToLocale = false; }
        if (_box != null) _box.Hide();
    }

    private async Task RunAsync(int token, CancellationToken ct, ComposerDialogueTiming timing)
    {
        ComposerDialogueLine line = default;
        try
        {
            var pick = ComposerDialogueService.PickLineAsync(_composerId, _category, ct);
            float timeout = _config != null ? _config.dialogueLoadTimeoutSeconds : 3f;
            if (timeout > 0f && !pick.IsCompleted)
            {
                var finished = await Task.WhenAny(pick, Task.Delay(TimeSpan.FromSeconds(timeout)));
                if (token != _token) return;
                if (finished != pick)
                {
                    Debug.LogWarning($"[ComposerDialogue] {_category} line for '{_composerId}' not ready after {timeout:0.#} s — continuing without it.");
                    Finish(token, false);
                    return;
                }
            }
            line = await pick;
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

        if (!line.IsValid) { Finish(token, false); return; }

        _key = line.Key;
        _box.Show(_speaker, line.Text, timing);
    }

    private void OnContinueRequested()
    {
        if (!IsActive || _key == null) return;
        Finish(_token, true);
    }

    private void Finish(int token, bool shown)
    {
        if (token != _token || _onFinished == null) return;
        var callback = _onFinished;
        Cancel(); // clean state BEFORE the callback (it usually changes the flow state)
        callback(shown);
    }

    private void OnLocaleChanged(Locale locale)
    {
        if (!IsActive) return;
        if (_key == null) { _localeChangedWhileLoading = true; return; }
        _ = ReResolveAsync(_token, _key, _cts != null ? _cts.Token : default);
    }

    private async Task ReResolveAsync(int token, string key, CancellationToken ct)
    {
        try
        {
            var line = await ComposerDialogueService.ResolveTextAsync(_composerId, _category, key, ct);
            if (token != _token || key != _key) return;
            if (line.IsValid) _box.ReplaceText(line.Text);
            else Debug.LogWarning($"[ComposerDialogue] '{key}' has no text in the new language — keeping the current text.");
        }
        catch (Exception e)
        {
            if (token == _token) Debug.LogWarning($"[ComposerDialogue] Re-reading '{key}' after a language change failed: {e.Message}");
        }
    }
}
