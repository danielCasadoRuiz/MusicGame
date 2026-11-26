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

    public Button previous,next;
    public TMP_Text currentLabel;
    readonly List<string> modes=new();
    Func<string,string> nameForMode;
    Action<string> onChoice;
    string selectedMode;
    public bool UsesArrows=>currentLabel!=null;
    void Awake()
    {
        if(previous!=null)previous.onClick.AddListener(()=>Step(-1));
        if(next!=null)next.onClick.AddListener(()=>Step(1));
    }
    public void BindArrows(Button back,Button forward,TMP_Text label){previous=back;next=forward;currentLabel=label;}
    public void Step(int direction)
    {
        if(modes.Count==0)return;
        int index=Mathf.Max(0,modes.IndexOf(selectedMode??LanguagePreference.Mode));
        SetSelected(modes[(index+direction%modes.Count+modes.Count)%modes.Count]);
        onChoice?.Invoke(selectedMode);
    }

    private readonly List<LanguageOptionView> _spawned = new();

    public float PanelExtraHeight => _panelExtraHeight;
    public int OptionCount => UsesArrows ? modes.Count : _spawned.Count;

    public void Build(IList<string> modes, Func<string, string> displayName, Action<string> onPick)
    {
        this.modes.Clear();this.modes.AddRange(modes);nameForMode=displayName;onChoice=onPick;
        if(UsesArrows){if(_options!=null)_options.gameObject.SetActive(false);SetSelected(LanguagePreference.Mode);return;}
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
        selectedMode=mode;
        if(currentLabel!=null)currentLabel.text=nameForMode!=null?nameForMode(mode):LanguagePreference.DisplayName(mode);
        foreach (var o in _spawned) if (o != null) o.SetSelected(o.Mode == mode);
    }

    public void Bind(RectTransform options, LanguageOptionView optionPrefab) { _options = options; _optionPrefab = optionPrefab; }
}
