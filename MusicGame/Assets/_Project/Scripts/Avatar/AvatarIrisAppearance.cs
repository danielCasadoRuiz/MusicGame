using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional per-character gradient; all body/face/eye slots reuse one cloned surface material.
/// Bakes the iris into an owned atlas once, with no per-frame painting.</summary>
public sealed class AvatarIrisAppearance : MonoBehaviour
{
    private readonly List<Material> _owned = new();
    private readonly List<Texture2D> _textures = new();

    public static void Apply(GameObject body, FaceProfileSO profile, AvatarIrisSettings settings = null)
    {
        if (body == null) return;
        settings = Resolve(settings);

        var owner = body.AddComponent<AvatarIrisAppearance>();
        var copies = new Dictionary<Material, Material>();
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var original = materials[i];
                if (original == null || !original.HasProperty("_IrisEnabled")) continue;
                if (!copies.TryGetValue(original, out var material))
                {
                    material = new Material(original) { name = original.name + " (avatar iris)" };
                    if (settings != null)
                    {
                        var baked = Bake(original, settings);
                        material.SetTexture("_BaseMap", baked);
                        material.SetFloat("_IrisBaked", 1);
                        owner._textures.Add(baked);
                    }
                    else
                    {
                    material.SetColor("_IrisInnerColor", profile.irisCenter);
                    material.SetColor("_IrisMainColor", profile.irisBody);
                    material.SetColor("_IrisOuterColor", profile.irisRim);
                    material.SetFloat("_IrisInnerRadius", profile.irisCenterExtent);
                    material.SetFloat("_IrisOuterRadius", profile.irisRimStart);
                    }
                    material.SetFloat("_IrisEnabled", 1f);
                    copies.Add(original, material);
                    owner._owned.Add(material);
                }
                materials[i] = material;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }

    static AvatarIrisSettings Resolve(AvatarIrisSettings source)
    {
        source ??= new AvatarIrisSettings();
        var result = JsonUtility.FromJson<AvatarIrisSettings>(JsonUtility.ToJson(source));
        result.baseTextures = source.baseTextures;
        if (result.baseTextures == null || result.baseTextures.Length == 0)
            result.baseTextures = Resources.LoadAll<Texture2D>("Avatar/Iris");
        if (!source.randomizeOnSpawn) return result;
        // Local RNG leaves gameplay's UnityEngine.Random sequence untouched.
        var random = new System.Random(System.Guid.NewGuid().GetHashCode());
        Color[] centers = { new Color(.82f,.59f,.22f), new Color(.58f,.35f,.14f),
            new Color(.67f,.46f,.19f), new Color(.34f,.49f,.25f), new Color(.27f,.47f,.65f), new Color(.22f,.12f,.055f) };
        Color[] bodies = { new Color(.41f,.57f,.29f), new Color(.68f,.48f,.23f),
            new Color(.43f,.29f,.16f), new Color(.32f,.56f,.75f), new Color(.20f,.105f,.045f) };
        result.textureIndex = random.Next(System.Math.Max(1,result.baseTextures.Length));
        result.useGradient = true;
        int bodyIndex = random.Next(bodies.Length);
        result.middle = bodies[bodyIndex];
        // Blue and green centers are reserved for green irises.
        int[] allowedCenters = bodyIndex == 0 ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 0, 1, 2, 5 };
        result.inner = centers[allowedCenters[random.Next(allowedCenters.Length)]];
        result.outer = Color.Lerp(result.middle, Color.black, .38f);
        result.seed = random.Next(10000);
        result.offset = new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*.22f;
        result.distortion = .12f + (float)random.NextDouble()*.12f;
        return result;
    }

    static Texture2D Read(Texture source)
    {
        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        try {
            Graphics.Blit(source, rt); RenderTexture.active = rt;
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0,0,source.width,source.height),0,0); result.Apply(); return result;
        } finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
    }
    static Texture2D Bake(Material material, AvatarIrisSettings settings)
    {
        var atlas = Read(material.GetTexture("_BaseMap") ?? Texture2D.whiteTexture);
        Texture2D detail = null;
        if (settings.baseTextures != null && settings.baseTextures.Length > 0) {
            var selected = settings.baseTextures[Mathf.Clamp(settings.textureIndex,0,settings.baseTextures.Length-1)];
            if (selected != null) detail = Read(selected);
        }
        foreach (var property in new[]{"_IrisRightUV","_IrisLeftUV"}) {
            var region = material.GetVector(property);
            for (int y=Mathf.Max(0,Mathf.FloorToInt((region.y-region.z*1.025f)*atlas.height)); y<Mathf.Min(atlas.height,Mathf.CeilToInt((region.y+region.z*1.025f)*atlas.height)); y++)
            for (int x=Mathf.Max(0,Mathf.FloorToInt((region.x-region.z*1.025f)*atlas.width)); x<Mathf.Min(atlas.width,Mathf.CeilToInt((region.x+region.z*1.025f)*atlas.width)); x++) {
                var uv = new Vector2(((x+.5f)/atlas.width-region.x)/region.z,((y+.5f)/atlas.height-region.y)/region.z);
                float r=uv.magnitude;
                if(r<.34f || r>1.025f) continue;
                float radius=(uv-settings.offset).magnitude + (Mathf.PerlinNoise(uv.x*4+settings.seed,uv.y*4+settings.seed)-.5f)*settings.distortion;
                var tint=settings.useGradient ? Color.Lerp(Color.Lerp(settings.inner,settings.middle,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.36f,settings.innerExtent,radius))),settings.outer,Mathf.SmoothStep(0,1,Mathf.InverseLerp(settings.outerStart,1.025f,radius))) : settings.defaultColor;
                var old=atlas.GetPixel(x,y);
                var basis=detail != null ? detail.GetPixelBilinear(uv.x*.44f+.5f,uv.y*.44f+.5f) : new Color(old.grayscale,old.grayscale,old.grayscale,old.a)*material.GetFloat("_IrisDetailGain");
                var color=basis*tint; color.a=old.a;
                float mask=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.34f,.38f,r))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.97f,1.025f,r)));
                atlas.SetPixel(x,y,Color.Lerp(old,color,mask));
            }
        }
        if(detail != null) { if(Application.isPlaying) Destroy(detail); else DestroyImmediate(detail); }
        atlas.name="Baked avatar iris"; atlas.wrapMode=TextureWrapMode.Clamp; atlas.Apply(false,true); return atlas;
    }

    private void OnDestroy()
    {
        foreach (var material in _owned)
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        _owned.Clear();
        foreach(var texture in _textures) { if(Application.isPlaying) Destroy(texture); else DestroyImmediate(texture); }
        _textures.Clear();
    }
}
