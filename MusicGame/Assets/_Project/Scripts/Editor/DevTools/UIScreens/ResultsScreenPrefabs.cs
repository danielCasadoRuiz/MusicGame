#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Results group: the post-Fight Match Result screen (Prefabs/UI/MatchResult.prefab → MatchResultView,
/// UIPrefabLibrarySO.matchResult), ported 1:1 from MatchResultController's former procedural Build() —
/// title/rival/rounds/summary/level texts, Continue/Select Song (win), Fight Again/Replay Song (lose), Main
/// Menu, the nested EvaluationPanel component prefab and the Rewarded-Ad confirmation modal (hidden).
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

        view.titleText = UIFactory.CreateText("Title", root, "", 64, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.titleText.rectTransform, Center, Center, Center, new Vector2(0f, 220f), new Vector2(800f, 90f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        view.rivalText = UIFactory.CreateText("Rival", root, "", 20, Color.white);
        UIFactory.SetBox(view.rivalText.rectTransform, Center, Center, Center, new Vector2(0f, 160f), new Vector2(700f, 30f));
        view.rivalText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        view.roundsText = UIFactory.CreateText("Rounds", root, "", 40, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.roundsText.rectTransform, Center, Center, Center, new Vector2(0f, 110f), new Vector2(400f, 50f));
        view.roundsText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);

        view.summaryText = UIFactory.CreateText("Summary", root, "", 16, Color.white);
        UIFactory.SetBox(view.summaryText.rectTransform, Center, Center, Center, new Vector2(0f, 55f), new Vector2(700f, 50f));
        view.summaryText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        view.levelText = UIFactory.CreateText("Level", root, "", 22, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.levelText.rectTransform, Center, Center, Center, new Vector2(0f, 0f), new Vector2(500f, 34f));
        view.levelText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Positive, UIFontToken.Body);

        view.continueButton = ResultButton(root, "ContinueButton", "MatchResult.Continue", new Vector2(0f, -60f), true, out view.continueButtonLabel);
        view.fightAgainButton = ResultButton(root, "FightAgainButton", null, new Vector2(0f, -60f), true, out view.fightAgainButtonLabel);
        view.replaySongButton = ResultButton(root, "ReplaySongButton", "MatchResult.ReplaySong", new Vector2(0f, -120f), false, out view.replaySongButtonLabel);
        view.selectSongButton = ResultButton(root, "SelectSongButton", "MatchResult.SelectSong", new Vector2(0f, -120f), false, out view.selectSongButtonLabel);
        view.mainMenuButton = ResultButton(root, "MainMenuButton", "Fight.MainMenu", new Vector2(0f, -180f), false, out view.mainMenuButtonLabel);

        // Evaluation panel BEFORE the reward-ad modal, so the modal (and its dim) draws on top of it.
        if (evaluationPanelPrefab != null)
        {
            view.evaluationPanel = UIScreenPrefabContext.Nested(evaluationPanelPrefab, root);
            view.evaluationPanel.name = "Evaluation"; // anchoring/size from the component prefab
        }

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

    /// <summary>A centred 260×52 result button; primary = ButtonPrimary/Accent, else ButtonSecondary/TextPrimary.
    /// `key` null = data-dependent label (no LocalizedText).</summary>
    private static Button ResultButton(RectTransform parent, string name, string key, Vector2 pos, bool primary, out TextMeshProUGUI label)
    {
        var btn = MakeButton(parent, name, key, primary, out label);
        UIFactory.SetBox(btn.GetComponent<RectTransform>(), Center, Center, Center, pos, new Vector2(260f, 52f));
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
