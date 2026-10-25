#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player profile UI (generated once, then hand-editable like every UI prefab):
///   ProfileScreen.prefab  ProfileScreenView (ProfileScreenController) — picture, usual combat strength, styles
/// and an idempotent PATCH of MainMenu.prefab: the profile icon placeholder becomes the button that opens it.
/// Called by UIScreenPrefabBuilder.
/// </summary>
public static class ProfileScreenPrefabs
{
    private static readonly Vector2 Center = new(0.5f, 0.5f), TopLeft = new(0f, 1f), MidLeft = new(0f, 0.5f);

    public static void Build(UIScreenPrefabContext ctx)
    {
        string dir = UIScreenPrefabContext.ScreensFolder;
        ctx.Library.profileScreen = ctx.Prefab<ProfileScreenView>($"{dir}/ProfileScreen.prefab", BuildProfileScreen);
        ctx.Patch($"{dir}/MainMenu.prefab", PatchMainMenu);
    }

    private static TextMeshProUGUI Text(string name, RectTransform parent, int size, Vector2 pos, Vector2 box, TextAlignmentOptions align,
                                        FontStyles style = FontStyles.Normal, UIColorToken color = UIColorToken.TextPrimary)
    {
        var t = UIFactory.CreateText(name, parent, "", size, Color.white, align, style);
        UIFactory.SetBox(t.rectTransform, TopLeft, TopLeft, TopLeft, pos, box);
        t.enableWordWrapping = true;
        t.gameObject.AddComponent<ThemeTextReceiver>().Initialize(color, UIFontToken.Body);
        return t;
    }

    private static GameObject BuildProfileScreen()
    {
        var go = UIScreenPrefabContext.Root("ProfileScreen", Vector2.zero);
        var rt = (RectTransform)go.transform;
        UIFactory.Stretch(rt);
        var view = go.AddComponent<ProfileScreenView>();

        var dim = UIFactory.CreatePanel("Root", rt, new Color(0f, 0f, 0f, 0.82f));
        UIFactory.Stretch(dim.rectTransform);
        view.root = dim.gameObject;
        var panel = UIFactory.CreatePanel("Panel", dim.rectTransform, new Color(0.08f, 0.08f, 0.1f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(1000f, 760f));
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        var p = panel.rectTransform;

        view.titleText = Text("Title", p, 34, new Vector2(40f, -28f), new Vector2(920f, 50f), TextAlignmentOptions.Center, FontStyles.Bold);
        view.titleText.GetComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        // Left: the picture + its look.
        view.portrait = UIFactory.CreatePanel("Portrait", p, new Color(0.5f, 0.5f, 0.55f));
        UIFactory.SetBox(view.portrait.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(50f, -110f), new Vector2(260f, 260f));
        view.portrait.raycastTarget = false;
        view.lookText = Text("Look", p, 16, new Vector2(50f, -385f), new Vector2(260f, 90f), TextAlignmentOptions.Top, FontStyles.Normal, UIColorToken.TextSecondary);

        // Right: usual strength (four bars) + summary.
        view.strengthHeader = Text("StrengthHeader", p, 22, new Vector2(350f, -110f), new Vector2(600f, 32f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        var stats = BonusHelpCatalog.BuildStats;
        view.stats = new ProfileScreenView.StatRow[stats.Length];
        for (int i = 0; i < stats.Length; i++)
        {
            var row = UIFactory.CreateRect($"Stat_{stats[i]}", p);
            UIFactory.SetBox(row, TopLeft, TopLeft, TopLeft, new Vector2(350f, -152f - i * 40f), new Vector2(600f, 32f));
            var icon = UIFactory.CreatePanel("Icon", row, Color.white);
            UIFactory.SetBox(icon.rectTransform, MidLeft, MidLeft, MidLeft, Vector2.zero, new Vector2(22f, 22f));
            icon.preserveAspect = true; icon.raycastTarget = false;
            var label = UIFactory.CreateText("Label", row, "", 17, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UIFactory.SetBox(label.rectTransform, MidLeft, MidLeft, MidLeft, new Vector2(32f, 0f), new Vector2(200f, 30f));
            var barBg = UIFactory.CreateFillBar("Bar", row, new Color(1f, 1f, 1f, 0.12f), Color.white, out var fill);
            UIFactory.SetBox(barBg.rectTransform, MidLeft, MidLeft, MidLeft, new Vector2(240f, 0f), new Vector2(280f, 12f));
            fill.fillAmount = 0f;
            var value = UIFactory.CreateText("Value", row, "–", 18, Color.white, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            UIFactory.SetBox(value.rectTransform, MidLeft, MidLeft, MidLeft, new Vector2(530f, 0f), new Vector2(70f, 30f));
            view.stats[i] = new ProfileScreenView.StatRow { stat = stats[i], icon = icon, label = label, fill = fill, value = value };
        }
        view.summaryText = Text("Summary", p, 17, new Vector2(350f, -320f), new Vector2(600f, 54f), TextAlignmentOptions.TopLeft);
        view.lastRunText = Text("LastRun", p, 15, new Vector2(350f, -376f), new Vector2(600f, 30f), TextAlignmentOptions.TopLeft, FontStyles.Normal, UIColorToken.TextSecondary);

        view.stylesHeader = Text("StylesHeader", p, 22, new Vector2(350f, -430f), new Vector2(600f, 32f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        view.stylesText = Text("Styles", p, 18, new Vector2(350f, -468f), new Vector2(600f, 80f), TextAlignmentOptions.TopLeft);
        view.noteText = Text("Note", p, 14, new Vector2(50f, -580f), new Vector2(900f, 60f), TextAlignmentOptions.Top, FontStyles.Italic, UIColorToken.TextSecondary);

        view.backButton = UIFactory.CreateButton("BackButton", p, "", out view.backLabel);
        UIFactory.SetBox(view.backButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(240f, 50f));
        view.backButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        view.backLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        go.SetActive(true);
        dim.gameObject.SetActive(false);
        return go;
    }

    // The top-right profile icon placeholder becomes a real button (opens the profile).
    public static void PatchMainMenu(GameObject root)
    {
        var view = root.GetComponent<MainMenuView>();
        if (view == null || view.profileButton != null) return;
        var icon = root.GetComponentsInChildren<Image>(true) is var images ? System.Array.Find(images, i => i.name == "ProfileIcon") : null;
        if (icon == null) return;
        icon.raycastTarget = true;
        var button = icon.gameObject.GetComponent<Button>() ?? icon.gameObject.AddComponent<Button>();
        button.targetGraphic = icon;
        view.profileButton = button;
    }
}
#endif
