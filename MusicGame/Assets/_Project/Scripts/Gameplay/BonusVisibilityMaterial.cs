using UnityEngine;

/// <summary>One shared unlit glow material per authored bonus/type, built when pools are created.</summary>
public static class BonusVisibilityMaterial
{
    public static Material Create(Material source, MusicRunnerCollectiblesConfig config, Color fallbackColor)
    {
        var shader=config.ringGlowShader!=null?config.ringGlowShader:Shader.Find("MusicGame/BonusGlowUnlit");
        if(shader==null) return source!=null?new Material(source):new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        var material=new Material(shader){name="BonusGlow_"+(source!=null?source.name:"Fallback")};
        if(source!=null)
        {
            string map=source.HasProperty("_BaseMap")?"_BaseMap":"_MainTex";
            if(source.HasProperty(map)){material.SetTexture("_BaseMap",source.GetTexture(map));material.SetTextureScale("_BaseMap",source.GetTextureScale(map));material.SetTextureOffset("_BaseMap",source.GetTextureOffset(map));}
            material.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white);
        }
        else material.SetColor("_BaseColor",fallbackColor);
        material.SetFloat("_GlowStrength",Mathf.Max(0,config.ringGlowIntensity));
        material.SetFloat("_FlowSpeed",config.ringGlowFlowSpeed);
        material.SetFloat("_PulseAmount",Mathf.Clamp01(config.ringGlowPulseAmount));
        return material;
    }
}
