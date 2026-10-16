using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings language row — prefab Prefabs/UI/Components/LanguageSelector.prefab: a "Language" label
/// (LocalizedText) + an options container (HorizontalLayoutGroup) filled with LanguageOption prefabs, one
/// per mode (Automatic + every available Locale). Placement inside the settings panel, sizes and the
/// "selected" look are in the prefabs; MainMenuController only provides the modes and reacts to clicks.
/// </summary>
public class LanguageSelectorView : MonoBehaviour
{
    [SerializeField] private RectTransform _options;
    [SerializeField] private LanguageOptionView _optionPrefab;
    [Tooltip("Extra height the settings panel grows by to make room for this row.")]
    [SerializeField] private float _panelExtraHeight = 110f;

    private readonly List<LanguageOptionView> _spawned = new();

    public float PanelExtraHeight => _panelExtraHeight;
    public int OptionCount => _spawned.Count;

    public void Build(IList<string> modes, Func<string, string> displayName, Action<string> onPick)
    {
        foreach (var o in _spawned) if (o != null) Destroy(o.gameObject);
        _spawned.Clear();
        if (_options == null || _optionPrefab == null) { Debug.LogError("[LanguageSelectorView] options container / option prefab not assigned."); return; }
        foreach (var mode in modes)
        {
            var option = Instantiate(_optionPrefab, _options, false);
            option.Set(mode, displayName(mode), mode == LanguagePreference.Auto, onPick);
            _spawned.Add(option);
        }
    }

    public void SetSelected(string mode)
    {
        foreach (var o in _spawned) if (o != null) o.SetSelected(o.Mode == mode);
    }

    public void Bind(RectTransform options, LanguageOptionView optionPrefab) { _options = options; _optionPrefab = optionPrefab; }
}
