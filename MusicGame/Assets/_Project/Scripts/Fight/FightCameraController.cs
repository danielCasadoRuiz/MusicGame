using UnityEngine;

/// <summary>
/// First functional Fight camera — frames BOTH fighters (never just the Player), unlike Runner's
/// own CameraFollow. Added at runtime, directly onto Fight.unity's existing arenaCamera GameObject,
/// by FightSceneBootstrap (see that class's own doc) — this is not a prefab component.
///
/// Keeps running regardless of FightFlowState (see this phase's own scope note: "càmera pot
/// continuar funcionant" even before Fighting) — it only ever reads the two FighterActors'
/// Transforms, never gameplay state, so there's nothing to gate.
///
/// Config-driven (FightCameraConfig via AppConfig.fightCamera) — falls back to hardcoded defaults
/// if unconfigured, same convention as every other Fight controller.
/// </summary>
public class FightCameraController : MonoBehaviour
{
    private FightCameraConfig _config;
    private Transform _player;
    private Transform _opponent;

    private Vector3 _positionVelocity;

    public void Initialize(FightCameraConfig config, Transform player, Transform opponent)
    {
        _config   = config;
        _player   = player;
        _opponent = opponent;
    }

    private void LateUpdate()
    {
        if (_player == null || _opponent == null) return;

        Vector3 offset            = _config != null ? _config.offset             : new Vector3(0f, 2.2f, -6f);
        float   minDistance       = _config != null ? _config.minDistance        : 4f;
        float   maxDistance       = _config != null ? _config.maxDistance        : 9f;
        float   separationPadding = _config != null ? _config.separationPadding  : 0.6f;
        float   posSmoothTime     = _config != null ? _config.positionSmoothTime : 0.15f;
        float   rotSmoothSpeed    = _config != null ? _config.rotationSmoothSpeed : 6f;

        Vector3 midpoint     = (_player.position + _opponent.position) * 0.5f;
        float   separation   = Mathf.Abs(_player.position.x - _opponent.position.x);
        float   backDistance = Mathf.Clamp(Mathf.Abs(offset.z) + separation * separationPadding, minDistance, maxDistance);

        Vector3    desiredPosition = midpoint + new Vector3(offset.x, offset.y, 0f) + Vector3.back * backDistance;
        Quaternion desiredRotation = Quaternion.LookRotation((midpoint - desiredPosition).normalized, Vector3.up);

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _positionVelocity, posSmoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotSmoothSpeed * Time.deltaTime);
    }
}
