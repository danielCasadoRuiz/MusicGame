using System;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private AudioSource      audioSource;
    [SerializeField] private GameplayConfig   config;

    [Header("Position")]
    [SerializeField] private float behindDistance = 8f;   // units behind canonical Z
    [SerializeField] private float height         = 4f;   // world-space Y
    [SerializeField] private float xSmoothTime    = 0.18f;

    [Header("Energy Response")]
    [SerializeField] private float energyZPull  = 2.5f;
    [SerializeField] private float energySmooth = 2f;

    private SongProfile _profile;
    private float       _startZ;
    private float       _startTime;
    private bool        _running;
    private float       _velX;
    private float       _energyOffset;

    private Action<SongProfileReadyEvent> _onProfile;
    private Action<GameStartedEvent>      _onGameStart;

    private void OnEnable()
    {
        _onProfile   = e => _profile = e.Profile;
        _onGameStart = _ =>
        {
            if (playerController != null)
                _startZ = playerController.transform.position.z;
            _startTime = Time.time;
            _running   = true;
        };
        EventBus.Subscribe(_onProfile);
        EventBus.Subscribe(_onGameStart);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onProfile);
        EventBus.Unsubscribe(_onGameStart);
    }

    private void LateUpdate()
    {
        if (!_running || config == null) return;

        // ── Song-driven canonical Z: set directly, zero jitter ────────────────
        bool  songPlaying = audioSource != null && audioSource.isPlaying;
        float songT       = songPlaying
            ? config.warmupTime + audioSource.time
            : Time.time - _startTime;
        float canonicalZ  = _startZ + songT * config.playerSpeed;

        // Energy modulation: camera pulls closer during intense moments
        if (_profile != null && songPlaying)
        {
            float e      = _profile.GetEnergyAt(audioSource.time);
            float norm   = _profile.maxEnergy > 0f ? e / _profile.maxEnergy : 0f;
            _energyOffset = Mathf.Lerp(_energyOffset, norm * energyZPull, energySmooth * Time.deltaTime);
        }

        float cameraZ = canonicalZ - behindDistance + _energyOffset;

        // ── X: smoothly follows player lateral position ───────────────────────
        float playerX  = playerController != null ? playerController.transform.position.x : 0f;
        float smoothedX = Mathf.SmoothDamp(transform.position.x, playerX, ref _velX, xSmoothTime);

        transform.position = new Vector3(smoothedX, height, cameraZ);

        // ── Look at the player's actual position (shows surge movement) ───────
        // Z only drifts by surge amount, so the look change is subtle and not jittery
        if (playerController != null)
        {
            Vector3 lookAt = playerController.transform.position + new Vector3(0f, 0.75f, 2f);
            transform.LookAt(lookAt);
        }
    }
}
