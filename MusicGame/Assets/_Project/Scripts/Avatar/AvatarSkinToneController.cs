using UnityEngine;

/// <summary>Per-avatar UI controls; preserves garment property blocks and shared materials.</summary>
[ExecuteAlways]
public sealed class AvatarSkinToneController : MonoBehaviour
{
    [Range(0,1)] public float tone=1f/3f;
    [Range(-1,1)] public float undertone;
    public AvatarNailSettings nails = new AvatarNailSettings();
    public void SetNailColor(int index,Color color) { nails ??= new AvatarNailSettings(); nails.Set(index,color); SyncNails();Apply(); }
    public void UseNaturalNails(){SetNailMode(AvatarNailMode.Natural);}
    public void SetNailMode(AvatarNailMode mode){nails??=new AvatarNailSettings();nails.mode=mode;SyncNails();Apply();}
    public void PaintGroup(AvatarNailGroup group,Color color)=>PaintGroup((int)group,color);
    public void PaintGroup(int group,Color color) { nails??=new AvatarNailSettings();nails.SetGroup(group,color);SyncNails();Apply(); }
    public void PaintAll(Color color)
    {
        if(color.a<=0){UseNaturalNails();return;}
        nails??=new AvatarNailSettings();for(int i=0;i<4;i++)nails.SetGroup(i,color);SyncNails();Apply();
    }
    void SyncNails() { if(Instance?.Recipe!=null)Instance.Recipe.Nails=nails.Copy(); }
    private MaterialPropertyBlock _block;
    public AvatarRecipeSO SourceRecipe { get; internal set; }
    public AvatarInstance Instance { get; internal set; }
    public void SetTone(float value){tone=Mathf.Clamp01(value);Apply();}
    public void SetUndertone(float value){undertone=Mathf.Clamp(value,-1,1);Apply();}
    private void OnEnable()=>Apply();
    private void OnValidate(){SyncNails();Apply();}
    public void Apply()
    {
        _block??=new MaterialPropertyBlock();
        foreach(var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var material=renderer.sharedMaterial;
            if(material==null||!material.HasProperty("_SkinToneEnabled"))continue;
            renderer.GetPropertyBlock(_block);
            _block.SetFloat("_SkinToneEnabled",1);_block.SetFloat("_SkinTone",tone);_block.SetFloat("_SkinUndertone",undertone);
            _block.SetFloat("_NailMode",(float)(nails?.mode ?? AvatarNailMode.Natural));
            for(int i=0;i<4;i++)
            {
                var group=nails?.groups!=null && i<nails.groups.Length?nails.groups[i]:null;
                _block.SetFloat("_NailGroup"+i+"Enabled",group!=null && group.painted?1:0);
                _block.SetColor("_NailGroupColor"+i,group!=null?group.color:Color.clear);
            }
            for(int i=0;i<20;i++)_block.SetColor("_NailColor"+i,nails?.colors!=null && i<nails.colors.Length?nails.colors[i]:Color.clear);
            renderer.SetPropertyBlock(_block);
        }
    }
}
