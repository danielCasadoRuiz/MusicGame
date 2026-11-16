using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One confirmation sound per successful Button activation, including keyboard submit.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class UIButtonClickSound : MonoBehaviour
{
    private static AudioClip clip;
    private Button button;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        clip = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        clip = Resources.Load<AudioClip>("Audio/UIClick");
        SceneManager.sceneLoaded += OnSceneLoaded;
        foreach (var candidate in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Attach(candidate.gameObject);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects()) Attach(root);
    }

    public static void Attach(GameObject root)
    {
        if (!Application.isPlaying || root == null) return;
        foreach (var candidate in root.GetComponentsInChildren<Button>(true))
            if (!candidate.TryGetComponent<UIButtonClickSound>(out _)) candidate.gameObject.AddComponent<UIButtonClickSound>();
    }

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(Play);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(Play);
    }

    private void Play()
    {
        if (clip != null) GameAudio.Instance?.PlayClip(clip, .48f, GameAudioScope.UI);
    }
}
