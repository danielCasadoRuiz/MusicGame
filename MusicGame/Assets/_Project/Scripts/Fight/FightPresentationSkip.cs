using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The Skip button + ESC for the in-arena presentations (Versus intro, post-match farewell). One
/// instance per owner, shown only while that presentation can still be skipped. A skip fires at most
/// once per Show(), whichever arrives first (click or ESC), and ESC is only read while it is shown —
/// FightController leaves ESC alone in VersusIntro/MatchFarewell, so it never also opens the pause.
/// </summary>
public class FightPresentationSkip : MonoBehaviour
{
    private RectTransform _root;
    private Button _button;
    private TextMeshProUGUI _label;
    private Action _onSkip;
    private bool _armed;

    public bool IsShown => _root != null && _root.gameObject.activeSelf;

    public static FightPresentationSkip Create(string name)
    {
        var canvas = UIFactory.RootCanvas();
        var button = UIFactory.CreateButton(name, canvas, Loc.Get("Fight.Skip"), out var label); Loc.Track(label, "Fight.Skip");
        var rt = button.GetComponent<RectTransform>();
        UIFactory.SetBox(rt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 36f), new Vector2(190f, 56f));
        button.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        var skip = button.gameObject.AddComponent<FightPresentationSkip>();
        skip._root = rt;
        skip._button = button;
        skip._label = label;
        button.onClick.AddListener(skip.Fire);
        rt.gameObject.SetActive(false);
        return skip;
    }

    public void Show(Action onSkip)
    {
        _onSkip = onSkip;
        _armed = true;
        Loc.Set(_label, "Fight.Skip");
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    public void Hide()
    {
        _armed = false;
        _onSkip = null;
        if (_root != null) _root.gameObject.SetActive(false);
    }

    public void RefreshLabel() { if (_label != null) Loc.Set(_label, "Fight.Skip"); }

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
