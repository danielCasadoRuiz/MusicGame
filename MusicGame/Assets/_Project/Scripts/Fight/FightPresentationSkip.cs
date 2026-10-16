using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The Skip button + ESC for the in-arena presentations (Versus intro, post-match farewell) — prefab
/// Prefabs/UI/Components/PresentationSkipButton.prefab (look, size and corner placement live there; its label
/// has a LocalizedText "Fight.Skip"). One instance per owner, shown only while that presentation can still
/// be skipped. A skip fires at most once per Show(), click or ESC, and ESC is only read while shown —
/// FightController leaves ESC alone in VersusIntro/MatchFarewell, so it never also opens the pause.
/// </summary>
public class FightPresentationSkip : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private TextMeshProUGUI _label;
    private Action _onSkip;
    private bool _armed, _wired;

    public bool IsShown => gameObject.activeSelf;

    public static FightPresentationSkip Spawn(string name)
    {
        var skip = UIPrefabLibrarySO.Spawn(UIPrefabLibrarySO.Instance != null ? UIPrefabLibrarySO.Instance.skipButton : null, UIFactory.RootCanvas(), "PresentationSkipButton");
        if (skip == null) return null;
        skip.name = name;
        skip.Wire();
        skip.gameObject.SetActive(false);
        return skip;
    }

    private void Wire()
    {
        if (_wired) return;
        _wired = true;
        if (_button != null) _button.onClick.AddListener(Fire);
        else Debug.LogError($"[FightPresentationSkip] '{name}': Button not assigned in the prefab.");
    }

    public void Show(Action onSkip)
    {
        _onSkip = onSkip;
        _armed = true;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        _armed = false;
        _onSkip = null;
        if (this != null) gameObject.SetActive(false);
    }

    public void RefreshLabel() { if (_label != null) Loc.Set(_label, "Fight.Skip"); }

    public void Bind(Button button, TextMeshProUGUI label) { _button = button; _label = label; }

    private void Update()
    {
        if (!_armed) return;
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Fire();
    }

    private void Fire()
    {
        if (!_armed) return;
        var callback = _onSkip;
        Hide();             // disarmed BEFORE the callback: one skip, never two
        callback?.Invoke();
    }
}
