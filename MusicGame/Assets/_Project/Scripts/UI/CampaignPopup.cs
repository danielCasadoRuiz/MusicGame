using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Provisional modal for campaign moments (lap completed + final-rival reveal, unlock, finale, seal):
/// dim backdrop, optional image (silhouette / card), title, body and up to two buttons. Built once on
/// the root canvas, always on top while open.
/// </summary>
public class CampaignPopup : MonoBehaviour
{
    private RectTransform _root, _panel;
    private Image _image;
    private TextMeshProUGUI _title, _body;
    private Button _primary, _secondary;
    private TextMeshProUGUI _primaryLabel, _secondaryLabel;
    private Action _onPrimary, _onSecondary;

    public bool IsOpen => _root != null && _root.gameObject.activeSelf;

    public static CampaignPopup Create(string name)
    {
        var canvas = UIFactory.RootCanvas();
        var root = UIFactory.CreateRect(name, canvas);
        UIFactory.Stretch(root);
        var dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.8f);
        var popup = root.gameObject.AddComponent<CampaignPopup>();
        popup._root = root;

        var panel = UIFactory.CreatePanel("Panel", root, new Color(0.07f, 0.07f, 0.1f, 0.98f));
        popup._panel = panel.rectTransform;
        UIFactory.SetBox(popup._panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 620f));
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.82f, 0.35f, 0.9f); outline.effectDistance = new Vector2(3f, -3f);

        var imgRt = UIFactory.CreateRect("Image", popup._panel);
        UIFactory.SetBox(imgRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(200f, 200f));
        popup._image = imgRt.gameObject.AddComponent<Image>();
        popup._image.preserveAspect = true;

        popup._title = UIFactory.CreateText("Title", popup._panel, "", 34, new Color(1f, 0.82f, 0.35f), TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(popup._title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -236f), new Vector2(760f, 50f));
        popup._title.enableWordWrapping = true;
        popup._body = UIFactory.CreateText("Body", popup._panel, "", 20, Color.white, TextAlignmentOptions.Top);
        UIFactory.SetBox(popup._body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -296f), new Vector2(740f, 220f));
        popup._body.enableWordWrapping = true;

        popup._primary = UIFactory.CreateButton("Primary", popup._panel, "", out popup._primaryLabel);
        UIFactory.SetBox(popup._primary.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-150f, 28f), new Vector2(280f, 56f));
        popup._primary.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        popup._primaryLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        popup._primary.onClick.AddListener(() => popup.Close(popup._onPrimary));

        popup._secondary = UIFactory.CreateButton("Secondary", popup._panel, "", out popup._secondaryLabel);
        UIFactory.SetBox(popup._secondary.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(150f, 28f), new Vector2(280f, 56f));
        popup._secondary.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        popup._secondaryLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        popup._secondary.onClick.AddListener(() => popup.Close(popup._onSecondary));

        root.gameObject.SetActive(false);
        return popup;
    }

    /// <summary>Opens the popup. `secondaryLabel` null = single (primary) button, centred.</summary>
    public void Show(string title, string body, Sprite image, Color imageTint, string primaryLabel, Action onPrimary,
                     string secondaryLabel = null, Action onSecondary = null)
    {
        _title.text = title ?? "";
        _body.text = body ?? "";
        _image.gameObject.SetActive(image != null);
        _image.sprite = image;
        _image.color = imageTint;
        _primaryLabel.text = primaryLabel ?? "OK";
        _onPrimary = onPrimary;
        bool two = !string.IsNullOrEmpty(secondaryLabel);
        _secondary.gameObject.SetActive(two);
        _secondaryLabel.text = secondaryLabel ?? "";
        _onSecondary = onSecondary;
        ((RectTransform)_primary.transform).anchoredPosition = new Vector2(two ? -150f : 0f, 28f);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    private void Close(Action then)
    {
        _root.gameObject.SetActive(false);
        then?.Invoke();
    }
}
