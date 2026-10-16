using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One language button (prefab LanguageOption.prefab). "Automatic" is localized (follows language
/// changes); "Català" / "English" are proper names. `selectedMark` (any graphic: outline, underline…) shows
/// the current preference.</summary>
public class LanguageOptionView : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private TextMeshProUGUI _label;
    [SerializeField] private Behaviour _selectedMark;

    public string Mode { get; private set; }

    public void Set(string mode, string label, bool localizedAuto, Action<string> onPick)
    {
        Mode = mode;
        if (_label != null)
        {
            if (localizedAuto) Loc.Set(_label, "Settings.LanguageAuto");
            else _label.text = label;
        }
        if (_button != null) _button.onClick.AddListener(() => onPick?.Invoke(mode));
    }

    public void SetSelected(bool selected) { if (_selectedMark != null) _selectedMark.enabled = selected; }

    public void Bind(Button button, TextMeshProUGUI label, Behaviour selectedMark) { _button = button; _label = label; _selectedMark = selectedMark; }
}
