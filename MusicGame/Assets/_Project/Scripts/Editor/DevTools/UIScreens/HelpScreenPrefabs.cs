#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Help UI (generated once, then hand-editable like every UI prefab):
///   Components/TipCard.prefab + Items/TipRow.prefab           compact preRun / preFight side card (HelpTipsController)
///   HelpScreen.prefab + Items/HelpRow.prefab, HelpHeader.prefab the full help, own canvas above the pauses
///   BonusConversion.prefab + Items/ConversionChip/Flyer        runner → fight conversion overlay
/// and idempotent PATCHES of existing prefabs (only what is missing is added):
///   FightHud.prefab   build panel (5 stat bars + Special / x3 / x4 / life cells) INSIDE the top bar (player side,
///                     between health bar and timer) + pause "Moves" button
///   PauseMenu.prefab  pause "Bonuses" button (runner)
///   LiveHud.prefab    icon slots in the bonus counters
///   BonusConversion.prefab  title + budget + tray grouped in ONE "Block" (docked / carried / converted), own canvas
///   EndScreen.prefab  BonusSlot: where that block sits in the runner results
///   VersusScreen.prefab "Loading…" moved above the block's place
/// Sprites are never baked here: controllers take them from HelpUIConfigSO (placeholders until the final art).
/// Called by UIScreenPrefabBuilder.
/// </summary>
public static class HelpScreenPrefabs
{
    private const string ComponentsFolder = UIScreenPrefabContext.ScreensFolder + "/Components";
    private static readonly Vector2 Center = new(0.5f, 0.5f), TopLeft = new(0f, 1f);

    public static void Build(UIScreenPrefabContext ctx)
    {
        string items = UIScreenPrefabContext.ItemsFolder, dir = UIScreenPrefabContext.ScreensFolder;
        var tipRow = ctx.Prefab<TipRowView>($"{items}/TipRow.prefab", BuildTipRow);
        var helpRow = ctx.Prefab<HelpRowView>($"{items}/HelpRow.prefab", BuildHelpRow);
        var helpHeader = ctx.Prefab<TextMeshProUGUI>($"{items}/HelpHeader.prefab", BuildHelpHeader);
        var chip = ctx.Prefab<ConversionChipView>($"{items}/ConversionChip.prefab", BuildChip);
        var flyer = ctx.Prefab<Image>($"{items}/ConversionFlyer.prefab", BuildFlyer);

        ctx.Library.tipCard = ctx.Prefab<TipCardView>($"{ComponentsFolder}/TipCard.prefab", () => BuildTipCard(tipRow));
        ctx.Library.helpScreen = ctx.Prefab<HelpScreenView>($"{dir}/HelpScreen.prefab", () => BuildHelpScreen(helpRow, helpHeader));
        ctx.Library.bonusConversion = ctx.Prefab<BonusConversionView>($"{dir}/BonusConversion.prefab", () => BuildConversion(chip, flyer));

        ctx.Patch($"{dir}/FightHud.prefab", PatchFightHud);
        ctx.Patch($"{dir}/HelpScreen.prefab", PatchHelpScreen);
        ctx.Patch($"{items}/HelpRow.prefab", PatchHelpRow);
        ctx.Patch($"{dir}/PauseMenu.prefab", PatchPauseMenu);
        ctx.Patch($"{dir}/LiveHud.prefab", PatchLiveHud);
        ctx.Patch($"{dir}/BonusConversion.prefab", PatchConversion);
        ctx.Patch($"{dir}/EndScreen.prefab", PatchEndScreen);
        ctx.Patch($"{dir}/VersusScreen.prefab", PatchVersusScreen);
    }

    // ── Tip card ─────────────────────────────────────────────────────────────

    private static GameObject BuildTipRow()
    {
        var root = UIScreenPrefabContext.Root("TipRow", new Vector2(380f, 24f));
        var rt = (RectTransform)root.transform;
        var h = root.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f; h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        var view = root.AddComponent<TipRowView>();

        view.icon = UIFactory.CreatePanel("Icon", rt, Color.white);
        Fixed(view.icon.gameObject, 18f, 18f);
        view.icon.preserveAspect = true;

        view.keysText = UIFactory.CreateText("Keys", rt, "", 14, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Fixed(view.keysText.gameObject, 136f, 22f);
        view.keysText.richText = true;

        view.labelText = UIFactory.CreateText("Label", rt, "", 13, Color.white, TextAlignmentOptions.MidlineLeft);
        view.labelText.overflowMode = TextOverflowModes.Ellipsis;
        var le = view.labelText.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f; le.preferredHeight = 22f;
        view.labelText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        return root;
    }

    private static GameObject BuildTipCard(TipRowView rowPrefab)
    {
        var root = UIScreenPrefabContext.Root("TipCard", new Vector2(400f, 0f));
        var rt = (RectTransform)root.transform;
        UIFactory.SetBox(rt, TopLeft, TopLeft, TopLeft, new Vector2(20f, -118f), new Vector2(400f, 0f));
        var view = root.AddComponent<TipCardView>();
        view.root = rt;
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.09f, 0.82f);
        root.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        view.group = root.AddComponent<CanvasGroup>();
        var v = root.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(12, 12, 10, 10); v.spacing = 6f;
        v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        root.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = UIFactory.CreateRect("Header", rt);
        var hl = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        view.titleText = UIFactory.CreateText("Title", header, "", 15, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        var tle = view.titleText.gameObject.AddComponent<LayoutElement>(); tle.flexibleWidth = 1f; tle.preferredHeight = 22f;
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.pageText = UIFactory.CreateText("Page", header, "", 12, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.MidlineRight);
        Fixed(view.pageText.gameObject, 40f, 22f);

        var page = UIFactory.CreateRect("Rows", rt);
        view.rows = page;
        view.pageGroup = page.gameObject.AddComponent<CanvasGroup>();
        var grid = page.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(376f, 24f); grid.spacing = new Vector2(10f, 4f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 1;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        view.rowPrefab = rowPrefab;

        var footer = UIFactory.CreateRect("Footer", rt);
        var fl = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        fl.spacing = 10f; fl.childAlignment = TextAnchor.MiddleLeft;
        fl.childControlWidth = true; fl.childControlHeight = true; fl.childForceExpandWidth = false; fl.childForceExpandHeight = false;
        view.moreInfoButton = UIFactory.CreateButton("MoreInfoButton", footer, "", out view.moreInfoLabel);
        UIScreenPrefabContext.Localized(view.moreInfoLabel, "Help.Tip.More");
        view.moreInfoLabel.fontSize = 13;
        Fixed(view.moreInfoButton.gameObject, 150f, 26f);
        view.moreInfoButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        view.moreInfoLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.pauseHintText = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("PauseHint", footer, "", 11, new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.MidlineLeft, FontStyles.Italic), "Help.Tip.PauseHint");
        var hle = view.pauseHintText.gameObject.AddComponent<LayoutElement>(); hle.flexibleWidth = 1f; hle.preferredHeight = 26f;

        root.SetActive(false);
        return root;
    }

    // ── Full help screen ─────────────────────────────────────────────────────

    private static GameObject BuildHelpRow()
    {
        var root = UIScreenPrefabContext.Root("HelpRow", new Vector2(900f, 40f));
        var rt = (RectTransform)root.transform;
        var h = root.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 6, 6); h.spacing = 14f; h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
        var view = root.AddComponent<HelpRowView>();

        view.icon = UIFactory.CreatePanel("Icon", rt, Color.white);
        Fixed(view.icon.gameObject, 34f, 34f);
        view.icon.preserveAspect = true;
        view.keysText = UIFactory.CreateText("Keys", rt, "", 17, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        KeysColumn(view.keysText);

        var col = UIFactory.CreateRect("Text", rt);
        var cv = col.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = 2f; cv.childControlWidth = true; cv.childControlHeight = true; cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        view.titleText = UIFactory.CreateText("Title", col, "", 17, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);
        view.bodyText = UIFactory.CreateText("Body", col, "", 14, new Color(1f, 1f, 1f, 0.82f), TextAlignmentOptions.TopLeft);
        view.bodyText.enableWordWrapping = true;
        view.bodyText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);
        return root;
    }

    private static GameObject BuildHelpHeader()
    {
        var t = UIFactory.CreateText("HelpHeader", null, "", 19, Color.white, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
        var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 36f;
        t.margin = new Vector4(8f, 0f, 0f, 2f);
        t.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);
        return t.gameObject;
    }

    private static GameObject BuildHelpScreen(HelpRowView rowPrefab, TextMeshProUGUI headerPrefab)
    {
        var root = UIScreenPrefabContext.Root("HelpScreen", Vector2.zero);
        var rt = (RectTransform)root.transform;
        UIFactory.Stretch(rt);
        var view = root.AddComponent<HelpScreenView>();
        view.root = rt;
        // Own canvas: above both pause menus (the Runner's scene canvas and the UI scene's, both at order 100).
        var canvas = root.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = 300;
        root.AddComponent<GraphicRaycaster>();

        var dim = UIFactory.CreatePanel("Dim", rt, new Color(0f, 0f, 0f, 0.78f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        var panel = UIFactory.CreatePanel("Panel", rt, new Color(0.08f, 0.08f, 0.11f, 0.97f));
        UIFactory.SetBox(panel.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(1000f, 860f));
        panel.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        var p = panel.rectTransform;

        view.titleText = UIFactory.CreateText("Title", p, "", 30, Color.white, TextAlignmentOptions.Top, FontStyles.Bold);
        UIFactory.SetBox(view.titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(-60f, 40f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        view.introText = UIFactory.CreateText("Intro", p, "", 15, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.TopLeft);
        view.introText.enableWordWrapping = true;
        UIFactory.SetBox(view.introText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(-80f, 60f));
        view.introText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextSecondary, UIFontToken.Body);

        view.scroll = UIFactory.CreateScrollRect("List", p, out view.content, 4f);
        var list = (RectTransform)view.scroll.transform;
        UIFactory.SetBox(list, new Vector2(0f, 0f), new Vector2(1f, 1f), Center, new Vector2(0f, -4f), new Vector2(-60f, -248f));
        list.offsetMax = new Vector2(-30f, -140f); list.offsetMin = new Vector2(30f, 100f);
        var layout = view.content.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;   // rows size to their wrapped text
        layout.padding = new RectOffset(0, 12, 0, 12);
        view.rowPrefab = rowPrefab;
        view.headerPrefab = headerPrefab;

        view.closeButton = UIFactory.CreateButton("CloseButton", p, "", out view.closeLabel);   // label set per origin
        UIFactory.SetBox(view.closeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 38f), new Vector2(240f, 46f));
        view.closeButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonPrimary);
        view.closeLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);
        view.hintText = UIFactory.CreateText("Hint", p, "", 12, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Bottom, FontStyles.Italic);
        UIFactory.SetBox(view.hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(700f, 20f));

        root.SetActive(false);
        return root;
    }

    // ── Conversion overlay ───────────────────────────────────────────────────

    private static GameObject BuildChip()
    {
        var root = UIScreenPrefabContext.Root("ConversionChip", new Vector2(84f, 96f));
        var rt = (RectTransform)root.transform;
        var view = root.AddComponent<ConversionChipView>();
        view.group = root.AddComponent<CanvasGroup>();
        var le = root.AddComponent<LayoutElement>(); le.preferredWidth = 84f; le.preferredHeight = 96f;
        view.icon = UIFactory.CreatePanel("Icon", rt, Color.white);
        UIFactory.SetBox(view.icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(56f, 56f));
        view.icon.preserveAspect = true;
        view.countText = UIFactory.CreateText("Count", rt, "×0", 20, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.countText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(84f, 28f));
        view.countText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Display);
        return root;
    }

    private static GameObject BuildFlyer()
    {
        var img = UIFactory.CreatePanel("ConversionFlyer", null, Color.white);
        img.raycastTarget = false;
        img.preserveAspect = true;
        img.rectTransform.sizeDelta = new Vector2(56f, 56f);
        return img.gameObject;
    }

    private static GameObject BuildConversion(ConversionChipView chipPrefab, Image flyerPrefab)
    {
        var root = UIScreenPrefabContext.Root("BonusConversion", Vector2.zero);
        var rt = (RectTransform)root.transform;
        UIFactory.Stretch(rt);
        var view = root.AddComponent<BonusConversionView>();
        view.root = rt;
        view.group = root.AddComponent<CanvasGroup>();

        var dim = UIFactory.CreatePanel("Dim", rt, new Color(0f, 0f, 0f, 0.35f));
        UIFactory.Stretch(dim.rectTransform);
        dim.raycastTarget = false;

        view.titleText = UIFactory.CreateText("Title", rt, "", 30, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.SetBox(view.titleText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 300f), new Vector2(900f, 44f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);
        view.budgetText = UIFactory.CreateText("Budget", rt, "", 16, new Color(1f, 1f, 1f, 0.8f), TextAlignmentOptions.Center);
        UIFactory.SetBox(view.budgetText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 270f), new Vector2(900f, 26f));

        var tray = UIFactory.CreatePanel("Tray", rt, new Color(0.06f, 0.06f, 0.09f, 0.8f));
        UIFactory.SetBox(tray.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(0f, 116f));
        tray.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
        var hl = tray.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(18, 18, 10, 10); hl.spacing = 14f; hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
        tray.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        view.tray = tray.rectTransform;
        view.chipPrefab = chipPrefab;

        view.flightLayer = UIFactory.CreateRect("Flights", rt);
        UIFactory.Stretch(view.flightLayer);
        view.flyerPrefab = flyerPrefab;

        view.skipButton = UIFactory.CreateButton("SkipButton", rt, "", out view.skipLabel);
        UIScreenPrefabContext.Localized(view.skipLabel, "Fight.Skip");
        UIFactory.SetBox(view.skipButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(150f, 44f));
        view.skipButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        view.skipLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);

        root.SetActive(false);
        return root;
    }

    // ── Patches ──────────────────────────────────────────────────────────────

    private static readonly BonusKind[] PanelResources = { BonusKind.Special, BonusKind.TripleCombo, BonusKind.QuadCombo, BonusKind.Life };

    // The build panel sits under the player's side of the top bar (90 px), left-aligned with its portrait.
    private static void PatchFightHud(GameObject root)
    {
        var view = root.GetComponent<FightHudView>();
        if (view == null) return;
        var hud = view.root != null ? (RectTransform)view.root.transform : (RectTransform)root.transform;

        if (view.buildPanel == null)
        {
            var bg = UIFactory.CreatePanel("BuildPanel", hud, new Color(0.06f, 0.06f, 0.09f, 0.72f));
            UIFactory.SetBox(bg.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(20f, -98f), new Vector2(372f, 150f));
            bg.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Surface);
            bg.raycastTarget = false;
            var panel = bg.gameObject.AddComponent<FightBuildPanelView>();
            panel.root = bg.rectTransform;
            var stats = BonusHelpCatalog.BuildStats;
            panel.stats = new FightBuildPanelView.StatRow[stats.Length];
            for (int i = 0; i < stats.Length; i++)
            {
                var row = UIFactory.CreateRect($"Stat_{stats[i]}", bg.rectTransform);
                UIFactory.SetBox(row, TopLeft, TopLeft, TopLeft, new Vector2(8f, -8f - i * 22f), new Vector2(356f, 20f));
                var icon = UIFactory.CreatePanel("Icon", row, Color.white);
                UIFactory.SetBox(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
                icon.preserveAspect = true; icon.raycastTarget = false;
                var label = UIFactory.CreateText("Label", row, "", 12, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UIFactory.SetBox(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(96f, 18f));
                label.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
                var barBg = UIFactory.CreateFillBar("Bar", row, new Color(1f, 1f, 1f, 0.12f), Color.white, out var fill);
                UIFactory.SetBox(barBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(122f, 0f), new Vector2(188f, 8f));
                fill.fillAmount = 0f;
                var value = UIFactory.CreateText("Value", row, "0", 13, Color.white, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
                UIFactory.SetBox(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(314f, 0f), new Vector2(40f, 18f));
                panel.stats[i] = new FightBuildPanelView.StatRow { stat = stats[i], root = row, icon = icon, label = label, fill = fill, value = value };
            }
            panel.resources = new FightBuildPanelView.ResourceCell[PanelResources.Length];
            for (int i = 0; i < PanelResources.Length; i++)
            {
                var cell = UIFactory.CreateRect($"Res_{PanelResources[i]}", bg.rectTransform);
                UIFactory.SetBox(cell, TopLeft, TopLeft, TopLeft, new Vector2(8f + i * 90f, -120f), new Vector2(84f, 24f));
                var icon = UIFactory.CreatePanel("Icon", cell, Color.white);
                UIFactory.SetBox(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
                icon.preserveAspect = true; icon.raycastTarget = false;
                var tag = UIFactory.CreateText("Tag", cell, PanelResources[i] == BonusKind.TripleCombo ? "x3" : PanelResources[i] == BonusKind.QuadCombo ? "x4" : "",
                                               10, Color.black, TextAlignmentOptions.Center, FontStyles.Bold);
                UIFactory.SetBox(tag.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
                var value = UIFactory.CreateText("Value", cell, "0", 15, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                UIFactory.SetBox(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(56f, 22f));
                panel.resources[i] = new FightBuildPanelView.ResourceCell { kind = PanelResources[i], root = cell, icon = icon, value = value };
            }
            view.buildPanel = panel;
        }
        IntegrateBuildPanel(view, hud);

        if (view.movesHelpButton == null && view.pausePanel != null)
        {
            var pause = (RectTransform)view.pausePanel.transform;
            view.movesHelpButton = UIFactory.CreateButton("MovesHelpButton", pause, "", out view.movesHelpButtonLabel);
            UIScreenPrefabContext.Localized(view.movesHelpButtonLabel, "Help.Pause.Moves");
            UIFactory.SetBox(view.movesHelpButton.GetComponent<RectTransform>(), Center, Center, Center, new Vector2(0f, -130f), new Vector2(220f, 48f));
            view.movesHelpButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
            view.movesHelpButtonLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
        }
    }

    // The build panel INSIDE the top bar, on the player's side: right of the health bar (portrait 70 + gap 12 + bar
    // 320 from x 20 → 422), ending before the centred timer. Two compact zones split by thin rules: the five build
    // stats (power attributes: icon, short name, bar, points) and the 2×2 consumable charges (Special, x3, x4, lives).
    // No background of its own: it is part of the bar. Re-applied on every run (layout only; references kept).
    private const float PanelLeft = 436f, PanelWidth = 440f, BarHeight = 90f;

    private static void IntegrateBuildPanel(FightHudView view, RectTransform hud)
    {
        var panel = view.buildPanel;
        var topBar = hud.Find("TopBar") as RectTransform;
        if (panel == null || topBar == null) return;
        var rt = (RectTransform)panel.transform;
        if (rt.parent != topBar) rt.SetParent(topBar, false);
        rt.SetAsLastSibling();
        UIFactory.SetBox(rt, TopLeft, TopLeft, TopLeft, new Vector2(PanelLeft, 0f), new Vector2(PanelWidth, BarHeight));
        rt.localScale = Vector3.one;
        var bg = rt.GetComponent<Image>();
        if (bg != null) bg.enabled = false;

        Rule(rt, "RuleStats", 0f);
        Rule(rt, "RuleCharges", 206f);

        const float rowH = 14.5f;
        for (int i = 0; i < panel.stats.Length; i++)
        {
            var row = panel.stats[i];
            if (row?.root == null) continue;
            UIFactory.SetBox(row.root, TopLeft, TopLeft, TopLeft, new Vector2(10f, -8f - i * rowH), new Vector2(190f, rowH));
            if (row.icon != null) UIFactory.SetBox(row.icon.rectTransform, MidLeft, MidLeft, MidLeft, Vector2.zero, new Vector2(10f, 10f));
            if (row.label != null)
            {
                UIFactory.SetBox(row.label.rectTransform, MidLeft, MidLeft, MidLeft, new Vector2(14f, 0f), new Vector2(54f, rowH));
                row.label.fontSize = 10;
            }
            if (row.fill != null)
                UIFactory.SetBox((RectTransform)row.fill.transform.parent, MidLeft, MidLeft, MidLeft, new Vector2(70f, 0f), new Vector2(90f, 6f));
            if (row.value != null)
            {
                UIFactory.SetBox(row.value.rectTransform, MidLeft, MidLeft, MidLeft, new Vector2(162f, 0f), new Vector2(28f, rowH));
                row.value.fontSize = 11;
            }
        }
        for (int i = 0; i < panel.resources.Length; i++)
        {
            var cell = panel.resources[i];
            if (cell?.root == null) continue;
            UIFactory.SetBox(cell.root, TopLeft, TopLeft, TopLeft, new Vector2(216f + (i % 2) * 110f, -10f - (i / 2) * 38f), new Vector2(104f, 32f));
            cell.root.pivot = new Vector2(0.25f, 0.5f);      // pulses around the icon, not the cell's empty right side
            cell.root.anchoredPosition += new Vector2(26f, -16f);
            if (cell.icon != null) UIFactory.SetBox(cell.icon.rectTransform, MidLeft, MidLeft, MidLeft, Vector2.zero, new Vector2(24f, 24f));
            var tag = cell.root.Find("Tag") as RectTransform;
            if (tag != null) UIFactory.SetBox(tag, MidLeft, MidLeft, MidLeft, Vector2.zero, new Vector2(24f, 24f));
            if (cell.value != null)
            {
                UIFactory.SetBox(cell.value.rectTransform, MidLeft, MidLeft, MidLeft, new Vector2(30f, 0f), new Vector2(70f, 28f));
                cell.value.fontSize = 18;
            }
        }
    }

    private static readonly Vector2 MidLeft = new(0f, 0.5f);

    private static void Rule(RectTransform panel, string name, float x)
    {
        var rule = panel.Find(name) as RectTransform;
        if (rule == null)
        {
            var img = UIFactory.CreatePanel(name, panel, new Color(1f, 1f, 1f, 0.14f));
            img.raycastTarget = false;
            rule = img.rectTransform;
            rule.SetAsFirstSibling();
        }
        UIFactory.SetBox(rule, TopLeft, TopLeft, TopLeft, new Vector2(x, -12f), new Vector2(1f, BarHeight - 24f));
    }

    // Runner pause: a 4th button under MAIN MENU, same size and spacing; the panel grows to hold it.
    private static void PatchPauseMenu(GameObject root)
    {
        var view = root.GetComponent<PauseView>();
        if (view == null || view.bonusHelpButton != null || view.mainMenuButton == null) return;
        var main = (RectTransform)view.mainMenuButton.transform;
        var panel = (RectTransform)main.parent;
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, panel.sizeDelta.y + main.sizeDelta.y + 12f);
        view.bonusHelpButton = UIFactory.CreateButton("BonusHelpButton", panel, "", out view.bonusHelpButtonLabel);
        UIScreenPrefabContext.Localized(view.bonusHelpButtonLabel, "Help.Pause.Bonuses");
        var rt = (RectTransform)view.bonusHelpButton.transform;
        UIFactory.SetBox(rt, main.anchorMin, main.anchorMax, main.pivot, main.anchoredPosition - new Vector2(0f, main.sizeDelta.y + 12f), main.sizeDelta);
        view.bonusHelpButton.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.ButtonSecondary);
        view.bonusHelpButtonLabel.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.TextPrimary, UIFontToken.Body);
    }

    // Runner HUD: a small icon slot at the left of each bonus counter's label (the label / value move right).
    private static void PatchLiveHud(GameObject root)
    {
        var view = root.GetComponent<LiveHudView>();
        if (view == null) return;
        view.kickIcon = Slot(view.kickLabel, view.kickValue, view.kickIcon);
        view.snareIcon = Slot(view.snareLabel, view.snareValue, view.snareIcon);
        view.hiHatIcon = Slot(view.hiHatLabel, view.hiHatValue, view.hiHatIcon);
        view.beatIcon = Slot(view.beatLabel, view.beatValue, view.beatIcon);
        view.onsetIcon = Slot(view.onsetLabel, view.onsetValue, view.onsetIcon);
        view.impactIcon = Slot(view.impactLabel, view.impactValue, view.impactIcon);
        view.lifeIcon = Slot(view.lifeLabel, view.lifeValue, view.lifeIcon);
        view.specialIcon = Slot(view.specialLabel, view.specialValue, view.specialIcon);
    }

    private static Image Slot(TextMeshProUGUI label, TextMeshProUGUI value, Image existing)
    {
        if (existing != null || label == null) return existing;
        var cell = (RectTransform)label.rectTransform.parent;
        var icon = UIFactory.CreatePanel("Icon", cell, Color.white);
        UIFactory.SetBox(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -4f), new Vector2(14f, 14f));
        icon.preserveAspect = true; icon.raycastTarget = false;
        foreach (var t in new[] { label, value })
            if (t != null) { t.rectTransform.offsetMin += new Vector2(18f, 0f); }
        return icon;
    }

    // ── The ONE bonus block: results → fight presentation ────────────────────

    private static readonly Vector2 BottomCentre = new(0.5f, 0f);
    /// <summary>The block's place during the fight presentation: bottom-centre, under the roulette grid and the
    /// Versus names (the block's top stays below ~220 px).</summary>
    public static readonly Vector2 BlockBottomPosition = new(0f, 24f);

    // Title + budget + tray become one "Block" (vertical layout, sized to its content, bottom-centre pivot) that the
    // controller docks in the runner results and carries to the bottom of the fight presentation. Own canvas (its
    // sorting is applied at runtime by BonusConversionController — a prefab root canvas never keeps overrideSorting):
    // above the Runner end screen (Runner canvas, order 100), the roulette and the Versus card. The old full-screen dim (it darkened the arena under the rival's line)
    // is removed: the tray has its own background.
    public static void PatchConversion(GameObject root)
    {
        var view = root.GetComponent<BonusConversionView>();
        if (view == null) return;
        var rt = (RectTransform)root.transform;
        if (root.GetComponent<Canvas>() == null)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 140;
            root.AddComponent<GraphicRaycaster>();
        }
        var dim = rt.Find("Dim");
        if (dim != null) Object.DestroyImmediate(dim.gameObject);

        if (view.block == null)
        {
            var block = UIFactory.CreateRect("Block", rt);
            UIFactory.SetBox(block, BottomCentre, BottomCentre, BottomCentre, BlockBottomPosition, new Vector2(900f, 190f));
            var v = block.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4f; v.childAlignment = TextAnchor.LowerCenter;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var fit = block.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.blockGroup = block.gameObject.AddComponent<CanvasGroup>();
            view.blockGroup.blocksRaycasts = false;   // nothing to click in it; never eats the results / presentation clicks
            block.SetSiblingIndex(0);
            view.block = block;
        }
        var b = view.block;
        Child(view.titleText != null ? view.titleText.rectTransform : null, b, 0, 40f);
        Child(view.budgetText != null ? view.budgetText.rectTransform : null, b, 1, 24f);
        Child(view.tray, b, 2, 116f);
        if (view.tray != null)
        {
            var fit = view.tray.GetComponent<ContentSizeFitter>();   // the block's layout sizes it now
            if (fit != null) Object.DestroyImmediate(fit);
        }
        if (view.flightLayer != null) view.flightLayer.SetSiblingIndex(b.GetSiblingIndex() + 1);   // icons fly over the block
    }

    private static void Child(RectTransform child, RectTransform block, int index, float height)
    {
        if (child == null) return;
        if (child.parent != block) child.SetParent(block, false);
        child.SetSiblingIndex(index);
        var le = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
    }

    // Runner results: the block's slot at the bottom of the results panel, above Restart / Continue (the block is
    // scaled to fit it). Replaces the old per-type rows + "for the fight" line (GameplayHUD).
    public static void PatchEndScreen(GameObject root)
    {
        var view = root.GetComponent<EndScreenView>();
        if (view == null) return;
        var panel = root.transform.Find("Panel") as RectTransform;
        if (panel == null) return;
        var slot = panel.Find("BonusSlot") as RectTransform;
        if (slot == null)
        {
            slot = UIFactory.CreateRect("BonusSlot", panel);
            UIFactory.SetBox(slot, new Vector2(0f, 0f), new Vector2(1f, 0f), BottomCentre, new Vector2(0f, 74f), new Vector2(-40f, 150f));
        }
        view.bonusSlot = slot;
    }

    // Versus: "Loading…" above the bonus block (it sat where the block now waits).
    public static void PatchVersusScreen(GameObject root)
    {
        var view = root.GetComponent<VersusScreenView>();
        if (view == null || view.loadingText == null) return;
        view.loadingText.rectTransform.anchoredPosition = new Vector2(0f, 250f);
    }

    // Patch for prefabs generated before: the close label is set per origin, the key column wraps.
    public static void PatchHelpScreen(GameObject root)
    {
        var view = root.GetComponent<HelpScreenView>();
        if (view == null) return;
        var loc = view.closeLabel != null ? view.closeLabel.GetComponent<LocalizedText>() : null;
        if (loc != null) Object.DestroyImmediate(loc, true);
    }

    public static void PatchHelpRow(GameObject root)
    {
        var view = root.GetComponent<HelpRowView>();
        if (view != null && view.keysText != null) KeysColumn(view.keysText);
    }

    // Key sequences can be long (e.g. PUNCH PUNCH PUNCH KICK on touch): a wide column that wraps, never overlapping the name.
    private static void KeysColumn(TextMeshProUGUI keys)
    {
        var le = keys.GetComponent<LayoutElement>() ?? keys.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 300f; le.flexibleWidth = 0f;
        le.minHeight = 26f; le.preferredHeight = -1f;
        keys.richText = true;
        keys.enableWordWrapping = true;
        keys.lineSpacing = 18f;
    }

    private static void Fixed(GameObject go, float w, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w; le.minHeight = le.preferredHeight = h; le.flexibleWidth = 0f;
    }
}
#endif
