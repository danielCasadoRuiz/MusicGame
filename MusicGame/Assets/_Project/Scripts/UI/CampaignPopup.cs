using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal for campaign moments (lap completed + final-rival reveal, unlock, finale, seal) — prefab
/// Prefabs/UI/Components/CampaignPopup.prefab: dim backdrop, image (silhouette / card), title, body, primary and
/// secondary buttons; all layout and look in the prefab. Show fills it; a single-button popup centres the
/// primary button.
/// </summary>
public class CampaignPopup : MonoBehaviour
{
    [SerializeField] private Image _image;
    [SerializeField] private TextMeshProUGUI _title, _body;
    [SerializeField] private Button _primary, _secondary;
    [SerializeField] private TextMeshProUGUI _primaryLabel, _secondaryLabel;
    [Tooltip("Primary button X when the secondary button is shown (single button = centred).")]
    [SerializeField] private float _twoButtonsPrimaryX = -150f;
    private Action _onPrimary, _onSecondary;
    private bool _wired;

    public bool IsOpen => gameObject.activeSelf;

    public static CampaignPopup Spawn(string name)
    {
        var popup = UIPrefabLibrarySO.Spawn(UIPrefabLibrarySO.Instance != null ? UIPrefabLibrarySO.Instance.campaignPopup : null, UIFactory.RootCanvas(), "CampaignPopup");
        if (popup == null) return null;
        popup.name = name;
        UIFactory.Stretch((RectTransform)popup.transform);
        popup.Wire();
        popup.gameObject.SetActive(false);
        return popup;
    }

    private void Wire()
    {
        if (_wired) return;
        _wired = true;
        if (_primary != null) _primary.onClick.AddListener(() => Close(_onPrimary));
        if (_secondary != null) _secondary.onClick.AddListener(() => Close(_onSecondary));
        if (_primary == null || _title == null || _body == null) Debug.LogError($"[CampaignPopup] '{name}': prefab references missing.");
    }

    /// <summary>Opens the popup. `secondaryLabel` null = single (primary) button, centred.</summary>
    public void Show(string title, string body, Sprite image, Color imageTint, string primaryLabel, Action onPrimary,
                     string secondaryLabel = null, Action onSecondary = null)
    {
        if (_title != null) _title.text = title ?? "";
        if (_body != null) _body.text = body ?? "";
        if (_image != null)
        {
            _image.gameObject.SetActive(image != null);
            _image.sprite = image;
            _image.color = imageTint;
        }
        if (_primaryLabel != null) _primaryLabel.text = primaryLabel ?? "OK";
        _onPrimary = onPrimary;
        bool two = !string.IsNullOrEmpty(secondaryLabel);
        if (_secondary != null) _secondary.gameObject.SetActive(two);
        if (_secondaryLabel != null) _secondaryLabel.text = secondaryLabel ?? "";
        _onSecondary = onSecondary;
        if (_primary != null)
        {
            var rt = (RectTransform)_primary.transform;
            rt.anchoredPosition = new Vector2(two ? _twoButtonsPrimaryX : 0f, rt.anchoredPosition.y);
        }
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Bind(Image image, TextMeshProUGUI title, TextMeshProUGUI body, Button primary, TextMeshProUGUI primaryLabel,
                     Button secondary, TextMeshProUGUI secondaryLabel)
    {
        _image = image; _title = title; _body = body; _primary = primary; _primaryLabel = primaryLabel;
        _secondary = secondary; _secondaryLabel = secondaryLabel;
    }

    private void Close(Action then)
    {
        gameObject.SetActive(false);
        then?.Invoke();
    }
}
