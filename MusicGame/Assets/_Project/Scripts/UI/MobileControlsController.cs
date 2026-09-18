using TMPro;
using UnityEngine;

/// <summary>
/// Runner's mobile in-game controls — a virtual joystick (bottom-left, drag) and a jump button
/// (bottom-right, tap) feeding TouchInputState. Shown ONLY while PlatformService.IsMobile AND
/// GameFlowState is Gameplay — strictly platform-gated, including in the Editor (simulate Mobile
/// via AppConfigSO.editorPlatformSimulation to see/test these) — invisible/inert otherwise, and
/// gone the instant the run ends (Results/Fight/back to menu), matching "sap què mostrar o no en un
/// mode o altre" (Section on platform-aware UI).
///
/// The keyboard, unlike this UI, is DELIBERATELY not platform-gated in the Editor — see
/// PlatformService.DualInputInEditor's own doc: a developer must always be able to test with just a
/// keyboard, with no touchscreen, regardless of which platform is currently simulated. That's
/// PlayerController's concern, not this class's — it only ever gates its own visuals.
///
/// No art final: plain translucent panels via UIFactory — VirtualJoystick/TouchJumpButton own the
/// actual pointer-event logic, this class only builds the visuals and gates visibility.
///
/// Lives in the always-loaded UI Scene (added by UIFlowController) — reacts to
/// GameFlowStateChangedEvent directly, same pattern as the other screens.
/// </summary>
public class MobileControlsController : MonoBehaviour
{
    private RectTransform _root;
    private System.Action<GameFlowStateChangedEvent> _onFlowStateChanged;

    private void Awake() => Build();

    private void OnEnable()
    {
        _onFlowStateChanged = e => Refresh(e.Current);
        EventBus.Subscribe(_onFlowStateChanged);

        if (AppBootstrap.Context != null)
            Refresh(AppBootstrap.Context.AppFlow.CurrentState);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onFlowStateChanged);
    }

    private void Refresh(GameFlowState state)
    {
        bool show = PlatformService.IsMobile && state == GameFlowState.Gameplay;
        _root.gameObject.SetActive(show);
    }

    // ── Build (runtime-only, no prefab) ────────────────────────────────────────

    private void Build()
    {
        var canvas = UIFactory.RootCanvas();
        _root = UIFactory.CreateRect("MobileControls", canvas);
        UIFactory.Stretch(_root);

        BuildJoystick();
        BuildJumpButton();

        _root.gameObject.SetActive(false);
    }

    private void BuildJoystick()
    {
        const float size = 180f;

        var background = UIFactory.CreatePanel("JoystickBackground", _root, new Color(1f, 1f, 1f, 0.15f));
        UIFactory.SetBox(background.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(40f, 40f), new Vector2(size, size));

        var handle = UIFactory.CreatePanel("JoystickHandle", background.rectTransform, new Color(1f, 1f, 1f, 0.4f));
        handle.rectTransform.sizeDelta        = new Vector2(size * 0.45f, size * 0.45f);
        handle.rectTransform.anchoredPosition = Vector2.zero;

        var joystick = background.gameObject.AddComponent<VirtualJoystick>();
        joystick.Initialize(background.rectTransform, handle.rectTransform);
    }

    private void BuildJumpButton()
    {
        const float size = 120f;

        var button = UIFactory.CreatePanel("JumpButton", _root, new Color(1f, 1f, 1f, 0.25f));
        UIFactory.SetBox(button.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-40f, 40f), new Vector2(size, size));

        var label = UIFactory.CreateText("Label", button.rectTransform, Loc.Get("Mobile.Jump"), 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
        UIFactory.Stretch(label.rectTransform);

        button.gameObject.AddComponent<TouchJumpButton>();
    }
}
