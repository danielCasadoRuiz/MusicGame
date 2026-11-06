using UnityEngine;

/// <summary>
/// The Special's projectile look (2026-11-04), built in code when the move's FightProjectileData has no
/// visualPrefab: a white-hot core inside an orange glow, flames streaming behind it (world-space
/// particles, so the fireball leaves a burning trail), embers, and an orange light that lights the
/// fighters and the floor as it passes. Pulses while it flies. Presentation only (FightProjectile owns
/// movement, hits and lifetime); when the projectile is destroyed the trail is detached and left to burn
/// out instead of vanishing in one frame.
/// </summary>
public class FightFireballVisual : MonoBehaviour
{
    private Renderer _core, _glow;
    private Light _light;
    private ParticleSystem _flames, _embers;
    private float _size, _t;

    public static FightFireballVisual Build(Transform parent, float diameter)
    {
        var go = new GameObject("FireballVisual");
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<FightFireballVisual>();
        v._size = Mathf.Max(0.3f, diameter);
        float d = v._size;
        v._glow = FightVfx.Glow(go.transform, "Glow", new Color(1f, 0.45f, 0.08f, 0.9f), d * 2.6f);
        v._core = FightVfx.Glow(go.transform, "Core", new Color(1f, 0.92f, 0.6f, 1f), d * 1.1f);

        var fire = new Gradient();
        fire.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.3f),
                             new GradientColorKey(new Color(0.9f, 0.18f, 0.03f), 0.7f), new GradientColorKey(new Color(0.25f, 0.05f, 0.02f), 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        v._flames = FightVfx.Emitter(go.transform, "Flames", new FightVfx.EmitterSpec
        {
            rate = 160f, radius = d * 0.3f, lifeMin = 0.22f, lifeMax = 0.45f, sizeMin = d * 0.45f, sizeMax = d * 0.95f,
            speedMin = 0.1f, speedMax = 0.6f, upward = 0.7f, noise = 0.4f, color = fire, world = true, maxParticles = 400,
        });
        v._embers = FightVfx.Emitter(go.transform, "Embers", new FightVfx.EmitterSpec
        {
            rate = 45f, radius = d * 0.4f, lifeMin = 0.3f, lifeMax = 0.7f, sizeMin = 0.025f, sizeMax = 0.06f,
            speedMin = 0.8f, speedMax = 2.2f, gravity = 0.4f, noise = 0.8f,
            color = FightVfx.Fade(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.3f, 0.05f)), world = true, maxParticles = 200,
        });
        v._light = FightVfx.PointLight(go.transform, "Light", new Color(1f, 0.55f, 0.18f), 4f, 4f);
        // The trail emitters live OUTSIDE the projectile (they follow it in LateUpdate) so they can keep
        // burning out after it is destroyed (a child can't be detached while its parent is destroyed).
        v._flames.transform.SetParent(null, true);
        v._embers.transform.SetParent(null, true);
        return v;
    }

    private void Update()
    {
        _t += Time.deltaTime;
        float pulse = 1f + 0.12f * Mathf.Sin(_t * 31f) + 0.06f * Mathf.Sin(_t * 13f);
        if (_core != null) _core.transform.localScale = Vector3.one * _size * 1.1f * pulse;
        if (_glow != null) _glow.transform.localScale = Vector3.one * _size * 2.6f * (2f - pulse);
        if (_light != null) _light.intensity = 4f * pulse;
    }

    private void LateUpdate()
    {
        if (_flames != null) _flames.transform.position = transform.position;
        if (_embers != null) _embers.transform.position = transform.position;
    }

    // The trail keeps burning where it was: detach the world-space emitters, stop them, let them die out.
    private void OnDestroy()
    {
        foreach (var ps in new[] { _flames, _embers })
        {
            if (ps == null) continue;
            var main = ps.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Destroy; // set before stopping: it fires once all particles die
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
                                                