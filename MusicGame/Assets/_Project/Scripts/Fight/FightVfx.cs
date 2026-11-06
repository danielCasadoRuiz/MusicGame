using UnityEngine;

/// <summary>
/// Shared runtime building blocks for the fight's code-built effects (2026-11-04): a soft radial
/// texture, one additive particle material using it, a configurable particle emitter, a camera-facing
/// glow quad and a flickering point light. Everything is created in code (no prefab / asset to
/// maintain) and is presentation only — nothing here touches gameplay.
/// </summary>
public static class FightVfx
{
    private static Texture2D s_soft;
    private static Material s_additive;

    /// <summary>White radial falloff (bright core, smooth edge) — particles and glow quads.</summary>
    public static Texture2D SoftTexture
    {
        get
        {
            if (s_soft != null) return s_soft;
            const int n = 64;
            s_soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "FightVfx_Soft", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);           // smoothstep
                    a = Mathf.Pow(a, 1.6f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            s_soft.SetPixels32(px);
            s_soft.Apply(false, true);
            return s_soft;
        }
    }

    /// <summary>Additive, soft-textured, unlit (URP Particles/Unlit; vertex colour × _BaseColor × texture).</summary>
    public static Material Additive
    {
        get
        {
            if (s_additive != null) return s_additive;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            s_additive = new Material(shader) { name = "FightVfx_Additive (runtime)", hideFlags = HideFlags.DontSave };
            if (s_additive.HasProperty("_BaseMap")) s_additive.SetTexture("_BaseMap", SoftTexture);
            if (s_additive.HasProperty("_MainTex")) s_additive.SetTexture("_MainTex", SoftTexture);
            if (s_additive.HasProperty("_Surface")) s_additive.SetFloat("_Surface", 1f);
            if (s_additive.HasProperty("_Blend")) s_additive.SetFloat("_Blend", 2f);
            s_additive.SetOverrideTag("RenderType", "Transparent");
            s_additive.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            s_additive.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            s_additive.SetInt("_ZWrite", 0);
            s_additive.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_additive.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_additive;
        }
    }

    public struct EmitterSpec
    {
        public float rate, radius, lifeMin, lifeMax, sizeMin, sizeMax, speedMin, speedMax, upward, orbitalY, noise, gravity;
        public Gradient color;
        public bool world;
        public int maxParticles;
    }

    /// <summary>A looping emitter parented to `parent` (sphere shape), playing immediately.</summary>
    public static ParticleSystem Emitter(Transform parent, string name, EmitterSpec s, Vector3 localPosition = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(s.lifeMin, s.lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(s.speedMin, s.speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(s.sizeMin, s.sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = s.world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.gravityModifier = s.gravity;
        main.maxParticles = s.maxParticles > 0 ? s.maxParticles : 300;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission;
        emission.rateOverTime = s.rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.001f, s.radius);
        if (s.upward != 0f || s.orbitalY != 0f)
        {
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            // x / y / z must share one curve mode (TwoConstants).
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(s.upward * 0.6f, s.upward);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            if (s.orbitalY != 0f) vel.orbitalY = new ParticleSystem.MinMaxCurve(s.orbitalY);
        }
        if (s.noise > 0f)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = s.noise;
            noise.frequency = 1.5f;
            noise.scrollSpeed = 1f;
        }
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = s.color;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Additive;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    /// <summary>Gradient: `a` → `b` over the life, alpha fading in quickly and out at the end.</summary>
    public static Gradient Fade(Color a, Color b, float peakAlpha = 1f)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.15f), new GradientAlphaKey(peakAlpha * 0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    /// <summary>Camera-facing soft glow quad (FightBillboard keeps it facing the fight camera).</summary>
    public static Renderer Glow(Transform parent, string name, Color color, float size)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localScale = Vector3.one * size;
        var r = quad.GetComponent<Renderer>();
        r.sharedMaterial = Additive;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        SetColor(r, color);
        quad.AddComponent<FightBillboard>();
        return r;
    }

    private static MaterialPropertyBlock s_block;
    public static void SetColor(Renderer r, Color c)
    {
        if (r == null) return;
        s_block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(s_block);
        s_block.SetColor("_BaseColor", c);
        s_block.SetColor("_Color", c);
        r.SetPropertyBlock(s_block);
    }

    public static Light PointLight(Transform parent, string name, Color color, float intensity, float range, Vector3 localPosition = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }
}

/// <summary>Keeps a quad facing the fight camera (or the main camera).</summary>
public class FightBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        var cam = FightCameraController.Instance != null ? FightCameraController.Instance.transform
                : Camera.main != null ? Camera.main.transform : null;
        if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position, cam.up);
    }
}
