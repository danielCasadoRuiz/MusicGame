using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional per-character gradient; all body/face/eye slots reuse one cloned surface material.
/// Shares the atlas and source iris images; the calibrated gradient runs in the shader.</summary>
public sealed class AvatarIrisAppearance : MonoBehaviour
{
    private readonly List<Material> _owned = new();
    private readonly List<Texture2D> _textures = new();

    public static void Apply(GameObject body, FaceProfileSO profile, AvatarIrisSettings settings = null)
    {
        if (body == null) return;
        settings = Resolve(settings);

        var owner=body.GetComponent<AvatarIrisAppearance>();
        if(owner==null)owner=body.AddComponent<AvatarIrisAppearance>();
        var copies=new Dictionary<Material,Material>();
        foreach(var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            var materials=renderer.sharedMaterials;bool changed=false;
            for(int i=0;i<materials.Length;i++)
            {
                var original=materials[i];
                if(original==null||!original.HasProperty("_IrisEnabled"))continue;
                if(!copies.TryGetValue(original,out var material))
                {
                    material=owner._owned.Contains(original)?original:new Material(original){name=original.name+" (avatar iris)"};
                    if(!owner._owned.Contains(material))owner._owned.Add(material);
                    Configure(material,settings);
                    copies.Add(original,material);
                }
                materials[i]=material;changed=true;
            }
            if(changed)renderer.sharedMaterials=materials;
        }
    }

    public static void Configure(Material material,AvatarIrisSettings settings)
    {
        if(material==null||settings==null)return;
        material.SetFloat("_IrisBaked",0);material.SetFloat("_IrisEnabled",1);
        material.SetFloat("_IrisUseGradient",settings.useGradient?1:0);
        material.SetColor("_IrisDefaultColor",settings.defaultColor);
        material.SetColor("_IrisInnerColor",settings.inner);material.SetColor("_IrisMainColor",settings.middle);material.SetColor("_IrisOuterColor",settings.middle);
        material.SetFloat("_IrisInnerRadius",settings.innerExtent);material.SetFloat("_IrisOuterRadius",settings.outerStart);
        material.SetVector("_IrisVariation",new Vector4(settings.offset.x,settings.offset.y,settings.seed,0));material.SetFloat("_IrisDistortion",settings.distortion);
        Texture2D detail=null;
        if(settings.baseTextures!=null&&settings.baseTextures.Length>0)detail=settings.baseTextures[Mathf.Clamp(settings.textureIndex,0,settings.baseTextures.Length-1)];
        AvatarIrisTextureLibrary.Get(detail,out var outer,out var pupil);
        material.SetTexture("_IrisDetailMap",detail);material.SetFloat("_IrisDetailEnabled",detail!=null?1:0);
        material.SetVector("_IrisDetailOuter",outer);material.SetVector("_IrisDetailPupil",pupil);
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
        result.outer = result.middle;
        result.seed = random.Next(10000);
        result.offset = new Vector2((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f)*.22f;
        result.distortion = .12f + (float)random.NextDouble()*.12f;
        return result;
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
