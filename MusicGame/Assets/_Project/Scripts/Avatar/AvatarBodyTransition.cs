using UnityEngine;

/// <summary>
/// Animates an avatar's body toward a target Gender over time — the prototype of the in-game
/// Male ↔ Female transformation. Every frame it calls AvatarInstance.ApplyBody (the one real body
/// path), changing ONLY Gender: whatever Weight/Muscle the avatar has at that moment are held, so
/// e.g. a heavy male turns into a heavy female, never through a neutral body.
///
/// Safe mid-animation: ApplyBody only sets blendshape weights (no skeleton/Animator/Rebind change).
/// Lives on the avatar's own root and is destroyed with it. No VFX here — the body morph only.
/// </summary>
public class AvatarBodyTransition : MonoBehaviour
{
    public const float DefaultDuration = 0.75f;

    private AvatarInstance _instance;
    private float _fromGender;
    private float _toGender;
    private float _duration;
    private float _elapsed;

    public bool IsRunning => _instance != null && _elapsed < _duration;
    public float TargetGender => _toGender;

    /// <summary>Starts (or retargets, from the CURRENT gender) a transition. Returns the component so
    /// gameplay can poll IsRunning. `duration` &lt;= 0 applies the target immediately.</summary>
    public static AvatarBodyTransition StartGender(AvatarInstance instance, float targetGender, float duration = DefaultDuration)
    {
        if (instance?.Root == null) return null;

        var transition = instance.Root.GetComponent<AvatarBodyTransition>();
        if (transition == null) transition = instance.Root.gameObject.AddComponent<AvatarBodyTransition>();

        transition._instance = instance;
        transition._fromGender = instance.BodyMorphValues.Gender;
        transition._toGender = Mathf.Clamp01(targetGender);
        transition._duration = Mathf.Max(0f, duration);
        transition._elapsed = 0f;
        if (transition._duration <= 0f) transition.Apply(1f);
        return transition;
    }

    /// <summary>Stops wherever the body currently is.</summary>
    public void Stop() => _elapsed = _duration;

    private void Update()
    {
        if (!IsRunning) return;
        _elapsed += Time.deltaTime;
        Apply(Mathf.Clamp01(_elapsed / _duration));
    }

    private void Apply(float t)
    {
        float eased = t * t * (3f - 2f * t); // smoothstep
        var body = _instance.BodyMorphValues;
        body.Gender = Mathf.Lerp(_fromGender, _toGender, eased);
        if (t >= 1f) body.Gender = _toGender; // land exactly on the endpoint
        _instance.ApplyBody(body);
    }
}
