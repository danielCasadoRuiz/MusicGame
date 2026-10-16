using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Optional GLOBAL ambience of an arena prefab: skybox, ambient light, fog, the scene's MAIN (sun) light and
/// the fight camera's background. Put it on the arena root (or its Lighting node) and tick what the arena
/// overrides. ArenaEnvironmentLoader applies it when the arena loads and restores every previous value when
/// the arena is unloaded — the Fight scene's own settings are never permanently changed. Without this
/// component an arena leaves them untouched. Realtime reflection probes of the arena set to "Via scripting"
/// are rendered once after applying (a polished floor then reflects the arena's sky and decor).
/// </summary>
public class ArenaEnvironmentSettings : MonoBehaviour
{
    [Header("Skybox")]
    public bool overrideSkybox;
    public Material skybox;

    [Header("Ambient light")]
    public bool overrideAmbient;
    public AmbientMode ambientMode = AmbientMode.Flat;
    [Tooltip("Flat mode colour; Trilight mode SKY colour.")]
    public Color ambientColor = new(0.35f, 0.35f, 0.4f);
    [Tooltip("Trilight mode only.")]
    public Color ambientEquatorColor = new(0.3f, 0.3f, 0.3f);
    [Tooltip("Trilight mode only.")]
    public Color ambientGroundColor = new(0.2f, 0.2f, 0.2f);
    [Tooltip("Skybox mode only.")]
    public float ambientIntensity = 1f;

    [Header("Fog")]
    public bool overrideFog;
    public bool fog;
    public Color fogColor = new(0.5f, 0.5f, 0.55f);
    public float fogDensity = 0.01f;

    [Header("Main light (the Fight scene's sun)")]
    public bool overrideMainLight;
    public Color mainLightColor = Color.white;
    public float mainLightIntensity = 1f;
    [Tooltip("World rotation of the sun (pitch, yaw).")]
    public Vector3 mainLightEuler = new(50f, -30f, 0f);
    public LightShadows mainLightShadows = LightShadows.Soft;
    [Range(0f, 1f)] public float mainLightShadowStrength = 0.6f;

    [Header("Fight camera background")]
    [Tooltip("The fight camera clears to the skybox instead of its solid colour while this arena is loaded.")]
    public bool cameraClearsToSkybox;

    public struct Snapshot
    {
        public Material Skybox; public AmbientMode AmbientMode; public Color AmbientColor, AmbientEquator, AmbientGround; public float AmbientIntensity;
        public bool Fog; public Color FogColor; public float FogDensity;
        public Light Sun; public Color SunColor; public float SunIntensity; public Quaternion SunRotation; public LightShadows SunShadows; public float SunShadowStrength;
        public Camera Camera; public CameraClearFlags ClearFlags;
    }

    /// <param name="sun">The scene's main directional light (never one of the arena's own lights).</param>
    /// <param name="camera">The fight camera (never Camera.main).</param>
    public Snapshot Apply(Light sun = null, Camera camera = null)
    {
        var before = new Snapshot
        {
            Skybox = RenderSettings.skybox, AmbientMode = RenderSettings.ambientMode, AmbientColor = RenderSettings.ambientSkyColor,
            AmbientEquator = RenderSettings.ambientEquatorColor, AmbientGround = RenderSettings.ambientGroundColor, AmbientIntensity = RenderSettings.ambientIntensity,
            Fog = RenderSettings.fog, FogColor = RenderSettings.fogColor, FogDensity = RenderSettings.fogDensity,
        };
        if (overrideSkybox) RenderSettings.skybox = skybox;
        if (overrideAmbient)
        {
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientColor; // == ambientLight in Flat mode
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;
            RenderSettings.ambientIntensity = ambientIntensity;
        }
        if (overrideFog) { RenderSettings.fog = fog; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity; }
        if (overrideMainLight && sun != null)
        {
            before.Sun = sun; before.SunColor = sun.color; before.SunIntensity = sun.intensity; before.SunRotation = sun.transform.rotation;
            before.SunShadows = sun.shadows; before.SunShadowStrength = sun.shadowStrength;
            sun.color = mainLightColor;
            sun.intensity = mainLightIntensity;
            sun.transform.rotation = Quaternion.Euler(mainLightEuler);
            sun.shadows = mainLightShadows;
            sun.shadowStrength = mainLightShadowStrength;
        }
        if (cameraClearsToSkybox && camera != null)
        {
            before.Camera = camera; before.ClearFlags = camera.clearFlags;
            camera.clearFlags = CameraClearFlags.Skybox;
        }
        if (overrideSkybox || overrideAmbient) DynamicGI.UpdateEnvironment();
        foreach (var probe in GetComponentsInChildren<ReflectionProbe>(true))
            if (probe.mode == ReflectionProbeMode.Realtime && probe.refreshMode == ReflectionProbeRefreshMode.ViaScripting) probe.RenderProbe();
        return before;
    }

    public static void Restore(Snapshot s)
    {
        RenderSettings.skybox = s.Skybox;
        RenderSettings.ambientMode = s.AmbientMode;
        RenderSettings.ambientSkyColor = s.AmbientColor;
        RenderSettings.ambientEquatorColor = s.AmbientEquator;
        RenderSettings.ambientGroundColor = s.AmbientGround;
        RenderSettings.ambientIntensity = s.AmbientIntensity;
        RenderSettings.fog = s.Fog;
        RenderSettings.fogColor = s.FogColor;
        RenderSettings.fogDensity = s.FogDensity;
        if (s.Sun != null)
        {
            s.Sun.color = s.SunColor; s.Sun.intensity = s.SunIntensity; s.Sun.transform.rotation = s.SunRotation;
            s.Sun.shadows = s.SunShadows; s.Sun.shadowStrength = s.SunShadowStrength;
        }
        if (s.Camera != null) s.Camera.clearFlags = s.ClearFlags;
        DynamicGI.UpdateEnvironment();
    }
}
