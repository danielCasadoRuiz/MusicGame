using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JukeboxRowView : MonoBehaviour
{
    public Button button;
    public Image background, cover, plate;
    public JukeboxNeon rowLight;
    public TextMeshProUGUI title, detail, state;
    public Sprite normalArt, selectedArt;

    public static void ApplyButtonFeedback(Button button)
    {
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, .94f, .84f);
        colors.pressedColor = new Color(.8f, .7f, .58f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = Color.white;
        button.colors = colors;
    }

    private void Awake() => ApplyButtonFeedback(button);

    public void SetSelected(bool selected, bool unlocked = true)
    {
        if (normalArt != null)
        {
            var art = plate != null ? plate : background;
            art.sprite = selected ? selectedArt : normalArt;
            art.color = unlocked ? Color.white : new Color(.63f, .61f, .57f, 1);
            if (rowLight != null) rowLight.gameObject.SetActive(selected && unlocked);
            title.color = selected ? new Color(1, .95f, .83f) : new Color(.11f, .064f, .037f);
        }
        else
        {
            background.color = selected ? new Color(.12f, .5f, .62f, .8f) : new Color(.12f, .15f, .2f, .9f);
        }
    }
}
