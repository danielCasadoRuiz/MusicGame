#if UNITY_EDITOR
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlay screens: the full-screen, mostly text-only layers drawn above everything else —
///   AnalyzingScreen, CountdownScreen, FinishBanner, MobileControls (Runner)
///   NextSongTransition, RoundIntro, RoundEnd (Fight)
/// Each is a 1:1 port of its controller's former procedural Build(). Every root is stretched and saved
/// INACTIVE (each controller started hidden); the view sits on that root. Called by UIScreenPrefabBuilder.
/// </summary>
public static class OverlayScreenPrefabs
{
    private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    public static void Build(UIScreenPrefabContext ctx)
    {
        string dir = UIScreenPrefabContext.ScreensFolder;
        ctx.Library.analyzingScreen    = ctx.Prefab<AnalyzingScreenView>($"{dir}/AnalyzingScreen.prefab", BuildAnalyzing);
        ctx.Library.countdownScreen    = ctx.Prefab<CountdownScreenView>($"{dir}/CountdownScreen.prefab", BuildCountdown);
        ctx.Library.finishBanner       = ctx.Prefab<FinishBannerView>($"{dir}/FinishBanner.prefab", BuildFinishBanner);
        ctx.Library.mobileControls     = ctx.Prefab<MobileControlsView>($"{dir}/MobileControls.prefab", BuildMobileControls);
        ctx.Library.nextSongTransition = ctx.Prefab<NextSongTransitionView>($"{dir}/NextSongTransition.prefab", BuildNextSongTransition);
        ctx.Library.roundIntro         = ctx.Prefab<RoundIntroView>($"{dir}/RoundIntro.prefab", BuildRoundIntro);
        ctx.Library.roundEnd           = ctx.Prefab<RoundEndView>($"{dir}/RoundEnd.prefab", BuildRoundEnd);
    }

    private static RectTransform StretchedRoot(string name)
    {
        var rt = (RectTransform)UIScreenPrefabContext.Root(name, Vector2.zero).transform;
        UIFactory.Stretch(rt);
        return rt;
    }

    private static void CenterBox(RectTransform rt, Vector2 pos, Vector2 size) => UIFactory.SetBox(rt, Center, Center, Center, pos, size);

    // ── Analyzing ────────────────────────────────────────────────────────────

    private static GameObject BuildAnalyzing()
    {
        var root = StretchedRoot("AnalyzingScreen");
        var view = root.gameObject.AddComponent<AnalyzingScreenView>();
        view.root = root.gameObject;

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 0.96f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        view.titleText = UIFactory.CreateText("Title", root, "", 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        CenterBox(view.titleText.rectTransform, new Vector2(0f, 40f), new Vector2(900f, 40f));
        view.titleText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        view.tipText = UIFactory.CreateText("Tip", root, "", 16, new Color(0.75f, 0.75f, 0.8f), TextAlignmentOptions.Center);
        CenterBox(view.tipText.rectTransform, new Vector2(0f, -10f), new Vector2(900f, 30f));
        view.tipText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Secondary, UIFontToken.Body);

        var barBg = UIFactory.CreateFillBar("Progress", root, new Color(1f, 1f, 1f, 0.12f), new Color(0.18f, 0.75f, 0.95f), out var fill);
        CenterBox(barBg.rectTransform, new Vector2(0f, -60f), new Vector2(500f, 6f));
        fill.fillAmount = 0f;
        fill.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Accent);
        view.progressFill = fill;

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    // ── Countdown / Finish (Runner) ──────────────────────────────────────────

    private static GameObject BuildCountdown()
    {
        var root = StretchedRoot("CountdownScreen");
        var view = root.gameObject.AddComponent<CountdownScreenView>();
        view.root = root.gameObject;

        view.numberText = UIFactory.CreateText("Number", root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        CenterBox(view.numberText.rectTransform, Vector2.zero, new Vector2(700f, 200f));   // wide enough for "TEMPO!"
        view.numberText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    private static GameObject BuildFinishBanner()
    {
        var root = StretchedRoot("FinishBanner");
        var view = root.gameObject.AddComponent<FinishBannerView>();
        view.root = root.gameObject;

        view.text = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Text", root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Countdown.Finish");
        CenterBox(view.text.rectTransform, Vector2.zero, new Vector2(700f, 200f));
        view.text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    // ── Mobile controls (Runner) ─────────────────────────────────────────────

    private static GameObject BuildMobileControls()
    {
        var root = StretchedRoot("MobileControls");
        var view = root.gameObject.AddComponent<MobileControlsView>();

        // Joystick — bottom-left, drag.
        const float joySize = 180f;
        var background = UIFactory.CreatePanel("JoystickBackground", root, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.SetBox(background.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 40f), new Vector2(joySize, joySize));
        var handle = UIFactory.CreatePanel("JoystickHandle", background.rectTransform, new Color(1f, 1f, 1f, 0.4f));
        handle.rectTransform.sizeDelta        = new Vector2(joySize * 0.45f, joySize * 0.45f);
        handle.rectTransform.anchoredPosition = Vector2.zero;
        view.joystick           = background.gameObject.AddComponent<VirtualJoystick>();
        view.joystickBackground = background.rectTransform;
        view.joystickHandle     = handle.rectTransform;

        // Jump (action A) — bottom-right, tap.
        var jump = UIFactory.CreatePanel("JumpButton", root, new Color(1f, 1f, 1f, 0.25f));
        UIFactory.SetBox(jump.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(120f, 120f));
        var jumpLabel = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Label", jump.rectTransform, "", 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Mobile.Jump");
        UIFactory.Stretch(jumpLabel.rectTransform);
        view.jumpButton = jump.gameObject.AddComponent<TouchJumpButton>();

        // Crouch (action B, hold) — just left of Jump.
        var crouch = UIFactory.CreatePanel("CrouchButton", root, new Color(1f, 1f, 1f, 0.2f));
        UIFactory.SetBox(crouch.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-180f, 30f), new Vector2(100f, 100f));
        var crouchLabel = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Label", crouch.rectTransform, "", 15, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "Mobile.Crouch");
        UIFactory.Stretch(crouchLabel.rectTransform);
        view.crouchButton = crouch.gameObject.AddComponent<TouchCrouchButton>();

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    // ── Next song cartela (Fight → Runner) ───────────────────────────────────

    private static GameObject BuildNextSongTransition()
    {
        var root = StretchedRoot("NextSongTransitionScreen");
        var view = root.gameObject.AddComponent<NextSongTransitionView>();
        view.root = root.gameObject;
        view.canvasGroup = root.gameObject.AddComponent<CanvasGroup>();

        var dim = UIFactory.CreatePanel("Dim", root, new Color(0.02f, 0.02f, 0.02f, 1f));
        UIFactory.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<ThemeColorReceiver>().Initialize(UIColorToken.Background);

        view.levelText = UIFactory.CreateText("Level", root, "", 26, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        CenterBox(view.levelText.rectTransform, new Vector2(0f, 70f), new Vector2(700f, 40f));
        view.levelText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Secondary, UIFontToken.Body);

        view.headerText = UIScreenPrefabContext.Localized(
            UIFactory.CreateText("Header", root, "", 20, Color.white, TextAlignmentOptions.Center, FontStyles.Bold), "NextSong.Header");
        CenterBox(view.headerText.rectTransform, new Vector2(0f, 20f), new Vector2(700f, 34f));
        view.headerText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Body);

        view.songNameText = UIFactory.CreateText("SongName", root, "", 40, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        CenterBox(view.songNameText.rectTransform, new Vector2(0f, -40f), new Vector2(900f, 60f));
        view.songNameText.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Primary, UIFontToken.Display);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    // ── Round intro / end (Fight) ────────────────────────────────────────────

    private static GameObject BuildRoundIntro()
    {
        var root = StretchedRoot("RoundIntroScreen");
        var view = root.gameObject.AddComponent<RoundIntroView>();
        view.root = root.gameObject;

        view.overlay = UIFactory.CreatePanel("Overlay", root, new Color(0f, 0f, 0f, 0f));
        UIFactory.Stretch(view.overlay.rectTransform);

        view.text = UIFactory.CreateText("Text", root, "", 96, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        CenterBox(view.text.rectTransform, Vector2.zero, new Vector2(700f, 200f));
        view.text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    private static GameObject BuildRoundEnd()
    {
        var root = StretchedRoot("RoundEndScreen");
        var view = root.gameObject.AddComponent<RoundEndView>();
        view.root = root.gameObject;

        view.text = UIFactory.CreateText("Text", root, "", 80, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        CenterBox(view.text.rectTransform, Vector2.zero, new Vector2(800f, 220f));
        view.text.gameObject.AddComponent<ThemeTextReceiver>().Initialize(UIColorToken.Accent, UIFontToken.Display);

        root.gameObject.SetActive(false);
        return root.gameObject;
    }
}
#endif
