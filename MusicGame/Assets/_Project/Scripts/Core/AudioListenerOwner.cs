using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The game's ONE AudioListener, created by AppBootstrap on its own DontDestroyOnLoad object, so a
/// listener exists from the first frame of Play in every state (boot, menus, analysis, Runner,
/// Fight) — previously the only listener lived on Runner.unity's Main Camera, so menus and the whole
/// Fight had none (no audio) and it died with that scene.
///
/// It follows whichever camera is currently rendering the game (Camera.main if enabled, else the
/// enabled screen camera with the highest depth), so 3D one-shots (pickup sounds) still pan
/// correctly. Any other AudioListener a loaded scene brings is disabled once, with a single log —
/// there is never more than one active listener.
/// </summary>
public class AudioListenerOwner : MonoBehaviour
{
    public static AudioListenerOwner Instance { get; private set; }

    private AudioListener _listener;
    private Camera _target;
    private float _nextScanTime;
    private Camera[] _cameraBuffer = new Camera[8];

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _listener = gameObject.AddComponent<AudioListener>();
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        DisableForeignListeners();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DisableForeignListeners();
        _target = null; // re-pick: the new mode scene usually brings its own camera
    }

    private void OnSceneUnloaded(Scene scene) => _target = null;

    private void DisableForeignListeners()
    {
        foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l == _listener || !l.enabled) continue;
            l.enabled = false;
            Debug.Log($"[AudioListenerOwner] Disabled extra AudioListener on '{l.name}' (scene '{l.gameObject.scene.name}') " +
                      "— the persistent [Audio Listener] is the only active one.");
        }
    }

    private void LateUpdate()
    {
        if ((_target == null || !_target.isActiveAndEnabled) && Time.unscaledTime >= _nextScanTime)
        {
            _nextScanTime = Time.unscaledTime + 0.25f;
            _target = PickCamera();
        }
        if (_target != null) transform.SetPositionAndRotation(_target.transform.position, _target.transform.rotation);
    }

    private Camera PickCamera()
    {
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled) return main;

        if (_cameraBuffer.Length < Camera.allCamerasCount) _cameraBuffer = new Camera[Camera.allCamerasCount];
        int count = Camera.GetAllCameras(_cameraBuffer);
        Camera best = null;
        for (int i = 0; i < count; i++)
        {
            var c = _cameraBuffer[i];
            if (c == null || c.targetTexture != null) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        System.Array.Clear(_cameraBuffer, 0, count);
        return best;
    }
}
