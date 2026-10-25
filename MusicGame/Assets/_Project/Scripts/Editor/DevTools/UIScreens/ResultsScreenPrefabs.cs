#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Results group: the post-Fight Match Result screen (Prefabs/UI/MatchResult.prefab → MatchResultView,
/// UIPrefabLibrarySO.matchResult): ONE panel — the title (match outcome) on top, the nested EvaluationPanel
/// component prefab as the single results list below it (FIGHT / RACE / MATCH sections, filled by
/// MatchResultController), Continue/Select Song (win), Fight Again/Replay Song (lose) and Main Menu stacked at
/// the bottom, and the Rewarded-Ad confirmation modal (hidden).
/// Called by UIScreenPrefabBuilder.
/// </summary>
public static class ResultsScreenPrefabs
{
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    public static void Build(UIScreenPrefabContext ctx)
    {
        var evaluationPanel = ctx.Library.evaluationPanel;
        if (evaluationPanel == null) ctx.Log.AppendLine("  FAIL  Results: library evaluationPanel missing — MatchResult built without it");
        ctx.Library.matchResult = ctx.Prefab<MatchResultView>($"{UIScreenPrefabContext.ScreensFolder}/MatchResult.prefab",
            () => BuildMatchResult(evaluationPanel));
    }

    /// <summary>"PERFECT!" stamp beside the title (hidden until a perfect win; MatchResultController animates it).
    /// Also used to add it to an existing hand-edited prefab (MatchResultPerfectSetup).</summary>
    public static TextMeshProUGUI BuildPerfectBanner(RectTransform root)
    {
        var top = new Vector2(0.5f, 1f);
        var t = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("PerfectBanner", root, "PERFECT!", 64, new Color(1f, 0.83f, 0.22f), TextAlignmentOptions.Center, FontStyles.Bold | FontStyles.Italic), "Eval.Perfect");
        UIFactory.SetBox(t.rectTransform, top, top, top, new Vector2(330f, -30f), new Vector2(420f, 96f));
        t.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 8f);
        t.raycastTarget = false;
        t.gameObject.SetActive(false);
        return t;
    }

    private static GameObject BuildMatchResult(EvaluationPanel evaluationPanelPrefab)
    {
        var go = UIScreenPrefabContext.Root("MatchResultScreen", Vector2.zero);
        var root = (RectTransform)go.transform;
        UIFactory.Stretch(root);
        var view = go.AddComponent<MatchResultView>();
        view.root = go;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 0.9f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var top = new Vector2(0.5f, 1f);
        view.titleText = UIFactory.CreateText("Title", root, "", 60, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.titleText.rectTransform, top, top, top, new Vector2(0f, -40f), new Vector2(900f, 84f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        view.perfectBanner = BuildPerfectBanner(root);

        // The single results list under the title (height follows its rows — ContentSizeFitter in the prefab).
        if (evaluationPanelPrefab != null)
        {
            view.evaluationPanel = UIScreenPrefabContext.Nested(evaluationPanelPrefab, root);
            view.evaluationPanel.name = "Evaluation";
            UIFactory.SetBox(view.evaluationPanel.Rect, top, top, top, new Vector2(0f, -136f), new Vector2(700f, 0f));
        }

        // Buttons stacked on the bottom edge: primary (Continue / Fight Again), secondary (Select Song /
        // Replay Song), Main Menu.
        view.continueButton = ResultButton(root, "ContinueButton", "MatchResult.Continue", new Vector2(0f, 200f), true, out view.continueButtonLabel);
        view.fightAgainButton = ResultButton(root, "FightAgainButton", null, new Vector2(0f, 200f), true, out view.fightAgainButtonLabel);
        view.replaySongButton = ResultButton(root, "ReplaySongButton", "MatchResult.ReplaySong", new Vector2(0f, 140f), false, out view.replaySongButtonLabel);
        view.selectSongButton = ResultButton(root, "SelectSongButton", "MatchResult.SelectSong", new Vector2(0f, 140f), false, out view.selectSongButtonLabel);
        view.mainMenuButton = ResultButton(root, "MainMenuButton", "Fight.MainMenu", new Vector2(0f, 80f), false, out view.mainMenuButtonLabel);

        BuildRewardAdModal(root, view);

        go.SetActive(false);
        return go;
    }

    private static void BuildRewardAdModal(RectTransform root, MatchResultView view)
    {
        var modal = UIFactory.CreateRect("RewardAdModal", root);
        UIFactory.Stretch(modal);

        var dim = UIFactory.CreatePanel("Dim", modal, new Color(0f, 0f, 0f, 0.75f));
        UIFactory.Stretch(dim.rectTransform);

        var panel = UIFactory.CreatePanel("Panel", modal, new Color(0.05f, 0.05f, 0.05f, 0.98f));
        UIFactory.SetBox(panel.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(420f, 220f));
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);

        var top = new Vector2(0.5f, 1f);
        view.rewardAdTitleText = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Title", panel.rectTransform, "", 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "MatchResult.WatchAdTitle");
        UIFactory.SetBox(view.rewardAdTitleText.rectTransform, top, top, top, new Vector2(0f, -30f), new Vector2(380f, 60f));
        view.rewardAdTitleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        view.watchAdButton = ModalButton(panel.rectTransform, "WatchAdButton", "MatchResult.WatchAd", new Vector2(0f, 70f), true, out view.watchAdButtonLabel);
        view.cancelButton = ModalButton(panel.rectTransform, "CancelButton", "MatchResult.Cancel", new Vector2(0f, 16f), false, out view.cancelButtonLabel);

        view.rewardAdModalRoot = modal.gameObject;
        view.rewardAdModalRoot.SetActive(false);
    }

    /// <summary>A bottom-anchored 260×52 result button; primary = ButtonPrimary/Accent, else ButtonSecondary/TextPrimary.
    /// `key` null = data-dependent label (no LocalizedText).</summary>
    private static Button ResultButton(RectTransform parent, string name, string key, Vector2 pos, bool primary, out TextMeshProUGUI label)
    {
        var btn = MakeButton(parent, name, key, primary, out label);
        var bottom = new Vector2(0.5f, 0f);
        UIFactory.SetBox(btn.GetComponent<RectTransform>(), bottom, bottom, bottom, pos, new Vector2(260f, 52f));
        return btn;
    }

    /// <summary>A bottom-anchored 320×46 modal button.</summary>
    private static Button ModalButton(RectTransform parent, string name, string key, Vector2 pos, bool primary, out TextMeshProUGUI label)
    {
        var btn = MakeButton(parent, name, key, primary, out label);
        var bottom = new Vector2(0.5f, 0f);
        UIFactory.SetBox(btn.GetComponent<RectTransform>(), bottom, bottom, bottom, pos, new Vector2(320f, 46f));
        return btn;
    }

    private static Button MakeButton(RectTransform parent, string name, string key, bool primary, out TextMeshProUGUI label)
    {
        var btn = UIFactory.CreateButton(name, parent, "", out label);
        if (key != null) UIScreenPrefabContext.Localized(label, key);
        btn.gameObject.AddComponent<ThemeColorReceiver>().Initialize(primary ? UIColorToken.ButtonPrimary : UIColorToken.ButtonSecondary);
        label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(primary ? UIColorToken.Accent : UIColorToken.TextPrimary, UIFontToken.Body);
        return btn;
    }
}
#endif
