using System.Collections;
using UnityEngine;

/// <summary>
/// Makes hits READABLE (2026-11-04): you see where and how hard a strike landed.
///   - a spark burst at the real contact point (FightHitResult.ContactPoint — FighterAttack's real-body
///     contact): bright for a landed hit, small and cold for a blocked one;
///   - HIT-STOP: a few hundredths of a second of near-frozen time on impact (longer for strong hits and
///     knockdowns) — the classic fighting-game "weight" that tells you it connected;
///   - (the SPECIAL's wind-up aura moved to FighterAuraFx: gold body aura + gathering fireball).
/// Presentation only: never changes damage or timing rules (hit-stop scales time uniformly for everyone,
/// and never fights the pause menu — it only acts while timeScale is 1 and only restores its own value).
/// One per Fight scene (FightSceneBootstrap).
/// </summary>
public class FightHitFeedback : MonoBehaviour
{
    private const float HitStopScale = 0.04f;
    private const float HitStopLight = 0.045f, HitStopHeavy = 0.09f, HitStopBlock = 0.025f;

    private Material _material;
    private Texture2D _softDot;
    private Coroutine _hitStop;
    private System.Action<HitLandedEvent> _onHit;
    private System.Action<HitBlockedEvent> _onBlock;
    private System.Action<FightMovePhaseChangedEvent> _onPhase;
    private readonly System.Collections.Generic.Dictionary<FighterMoveController, GameObject> _auras = new();

    private FightCombatBalanceConfig _balance;
    private bool SparkOn => _balance == null || _balance.hitSparkEnabled;
    private bool HitStopOn => _balance == null || _balance.hitStopEnabled;

    private void Awake()
    {
        var app = Resources.Load<AppConfigSO>("AppConfig");
        _balance = app != null ? app.combatBalance : null;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            _material = new Material(shader) { name = "FightHitSpark (runtime)" };
            if (_material.HasProperty("_Surface")) _material.SetFloat("_Surface", 1f);   // transparent
            if (_material.HasProperty("_Blend")) _material.SetFloat("_Blend", 2f);       // additive
            _material.SetOverrideTag("RenderType", "Transparent");
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _material.SetInt("_ZWrite", 0);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            // A soft round dot (no texture = a hard square flash, the look disliked in the 2026-11-04 playtest).
            _softDot = SoftDot(64);
            if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _softDot);
            if (_material.HasProperty("_MainTex")) _material.SetTexture("_MainTex", _softDot);
        }
    }

    private void OnEnable()
    {
        _onHit = e =>
        {
            bool heavy = e.Result.CausesKnockdown || e.Result.FinalHitStun >= 0.45f || !e.Result.Bonus.IsIdentity;
            if (SparkOn) Spark(e.Result.ContactPoint, heavy ? new Color(1f, 0.62f, 0.2f) : new Color(1f, 0.9f, 0.6f), heavy ? 30 : 18, heavy ? 1.4f : 1f);
            if (HitStopOn) HitStop(e.Result.CausesKnockdown ? HitStopHeavy * 1.3f : heavy ? HitStopHeavy : HitStopLight);
        };
        _onBlock = e =>
        {
            if (SparkOn)
            {
                Spark(e.Result.ContactPoint, new Color(0.5f, 0.78f, 1f), 10, 0.75f);
                Shield(e.Defender, e.Attacker, e.Result.ContactPoint);
            }
            if (HitStopOn) HitStop(HitStopBlock);
        };
        _onPhase = e => { }; // the Special's wind-up is FighterAuraFx's now
        EventBus.Subscribe(_onHit);
        EventBus.Subscribe(_onBlock);
        EventBus.Subscribe(_onPhase);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe(_onHit);
        EventBus.Unsubscribe(_onBlock);
        EventBus.Unsubscribe(_onPhase);
        if (_hitStop != null && Mathf.Approximately(Time.timeScale, HitStopScale)) Time.timeScale = 1f;
        _hitStop = null;
        foreach (var a in _auras.Values) if (a != null) Destroy(a);
        _auras.Clear();
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
        if (_softDot != null) Destroy(_softDot);
    }

    private static Texture2D SoftDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "FightSoftDot (runtime)", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);              // smooth falloff
                byte b = (byte)(a * 255f);
                px[y * size + x] = new Color32(255, 255, 255, b);
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    // ── Guard shield — a BLOCKED hit shows a blue disc in front of the defender's guard ──────────

    private void Shield(FighterActor defender, FighterActor attacker, Vector3 contact)
    {
        if (_material == null || defender == null) return;
        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "FightGuardShield";
        Destroy(disc.GetComponent<Collider>());
        disc.GetComponent<Renderer>().sharedMaterial = _material;
        Vector3 towardAttacker = attacker != null ? attacker.transform.position - defender.transform.position : defender.ForwardXZ;
        towardAttacker.y = 0f;
        if (towardAttacker.sqrMagnitude < 1e-4f) towardAttacker = defender.ForwardXZ;
        towardAttacker.Normalize();
        // Between the contact point and the defender's chest, facing the attacker.
        Vector3 basePos = defender.Body != null ? defender.Body.HipsPosition : defender.transform.position + Vector3.up;
        Vector3 pos = new Vector3(basePos.x, Mathf.Clamp(contact.y, basePos.y - 0.3f, basePos.y + 0.75f), basePos.z) + towardAttacker * 0.32f;
        disc.transform.position = pos;
        disc.transform.rotation = Quaternion.LookRotation(towardAttacker);
        StartCoroutine(ShieldRoutine(disc.transform, new Color(0.45f, 0.75f, 1f)));
    }

    private IEnumerator ShieldRoutine(Transform t, Color color)
    {
        var r = t.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        float elapsed = 0f, duration = 0.22f;
        while (elapsed < duration && t != null)
        {
            float k = elapsed / duration;
            t.localScale = Vector3.one * Mathf.Lerp(0.55f, 0.85f, Mathf.Sqrt(k));
            float a = (1f - k) * 0.85f;
            block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, a));
            block.SetColor("_Color", new Color(color.r, color.g, color.b, a));
            r.SetPropertyBlock(block);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    // ── Hit-stop ──────────────────────────────────────────────────────────────

    private void HitStop(float seconds)
    {
        if (seconds <= 0f || FightController.IsPaused) return;
        if (!Mathf.Approximately(Time.timeScale, 1f) && !Mathf.Approximately(Time.timeScale, HitStopScale)) return;
        if (_hitStop != null) StopCoroutine(_hitStop);
        _hitStop = StartCoroutine(HitStopRoutine(seconds));
    }

    private IEnumerator HitStopRoutine(float seconds)
    {
        Time.timeScale = HitStopScale;
        yield return new WaitForSecondsRealtime(seconds);
        if (Mathf.Approximately(Time.timeScale, HitStopScale)) Time.timeScale = 1f; // never undo a pause
        _hitStop = null;
    }

    // ── Spark ─────────────────────────────────────────────────────────────────

    private void Spark(Vector3 point, Color color, int count, float size)
    {
        if (_material == null) return;
        var go = new GameObject("FightHitSpark");
        go.transform.position = point;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.useUnscaledTime = true;          // the spark plays through the hit-stop
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * size, 6f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f * size, 0.1f * size);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.6f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.04f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.3f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = _material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1.5f;

        // A short flash ring at the point (reads even when particles are tiny on screen).
        var flash = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(flash.GetComponent<Collider>());
        flash.transform.SetParent(go.transform, false);
        flash.GetComponent<Renderer>().sharedMaterial = _material;
        StartCoroutine(Flash(flash.transform, color, 0.38f * size));
        ps.Play();
    }

    private IEnumerator Flash(Transform t, Color color, float maxSize)
    {
        var r = t.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        float elapsed = 0f, duration = 0.12f;
        while (elapsed < duration && t != null)
        {
            float k = elapsed / duration;
            var cam = FightCameraController.Instance != null ? FightCameraController.Instance.transform : null;
            if (cam != null) t.rotation = Quaternion.LookRotation(t.position - cam.position);
            t.localScale = Vector3.one * Mathf.Lerp(maxSize * 0.4f, maxSize, k);
            block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f - k));
            block.SetColor("_Color", new Color(color.r, color.g, color.b, 1f - k));
            r.SetPropertyBlock(block);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    // ── Special wind-up aura ──────────────────────────────────────────────────

    private void StartAura(FighterMoveController source, FightMoveDefinition move)
    {
        StopAura(source);
        if (_material == null || source.Actor == null) return;
        var aura = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        aura.name = "SpecialChargeAura";
        Destroy(aura.GetComponent<Collider>());
        aura.GetComponent<Renderer>().sharedMaterial = _material;
        aura.transform.SetParent(source.Actor.transform, false);
        _auras[source] = aura;
        StartCoroutine(Aura(source, aura.transform, Mathf.Max(0.1f, move.startupDuration / Mathf.Max(0.1f, source.AttackSpeed))));
    }

    private void StopAura(FighterMoveController source)
    {
        if (_auras.TryGetValue(source, out var aura) && aura != null) Destroy(aura);
        _auras.Remove(source);
    }

    private IEnumerator Aura(FighterMoveController source, Transform t, float duration)
    {
        var r = t.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        float elapsed = 0f;
        while (t != null)
        {
            float k = Mathf.Clamp01(elapsed / duration);
            float pulse = 0.85f + 0.15f * Mathf.Sin(elapsed * 30f);
            // In front of the chest, growing: the opponent sees the energy gathering.
            t.position = source.Actor.transform.position + source.Actor.ForwardXZ * 0.45f + Vector3.up * 0.35f;
            t.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.7f, k) * pulse;
            var c = Color.Lerp(new Color(0.4f, 0.7f, 1f), new Color(1f, 0.5f, 0.15f), k);
            block.SetColor("_BaseColor", new Color(c.r, c.g, c.b, Mathf.Lerp(0.35f, 0.8f, k)));
            block.SetColor("_Color", new Color(c.r, c.g, c.b, Mathf.Lerp(0.35f, 0.8f, k)));
            r.SetPropertyBlock(block);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           