using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Visual-only score sheets: orbit -> hands -> the existing projectile. Never touches combat timing or damage.</summary>
public sealed class FightScorePages : MonoBehaviour
{
    private static readonly Dictionary<FighterActor,FightScorePages> charging=new();
    private static Mesh sheetMesh;
    private static Material sheetMaterial;
    private readonly List<Transform> pages=new();
    private readonly List<Renderer> renderers=new();
    private MaterialPropertyBlock properties;
    private FighterActor owner;
    private bool flying,waiting;
    private float deadline,flightTime,diameter;
    private Vector3 direction;
    private Vector3[] releasePositions;
    private System.Action<FightFlowStateChangedEvent> onFlow;
    public int PageCount=>pages.Count;
    public bool IsFlying=>flying;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState(){charging.Clear();Remove(sheetMesh);sheetMesh=null;sheetMaterial=null;}

    public static FightScorePages Begin(FighterActor actor)
    {
        if(actor==null || actor.Side!=FighterSide.Opponent)return null;
        if(charging.TryGetValue(actor,out var previous)&&previous!=null)Remove(previous.gameObject);
        var effect=Create(actor.transform);
        if(effect==null)return null;
        effect.owner=actor;charging[actor]=effect;return effect;
    }

    public static FightScorePages Launch(FighterActor actor,Transform projectile,float size,Vector3 forward)
    {
        if(actor==null || actor.Side!=FighterSide.Opponent)return null;
        charging.TryGetValue(actor,out var effect);charging.Remove(actor);
        if(effect==null)effect=Create(projectile);
        if(effect==null)return null;
        effect.owner=actor;effect.releasePositions=new Vector3[effect.pages.Count];
        for(int i=0;i<effect.pages.Count;i++)effect.releasePositions[i]=effect.pages[i].position;
        effect.transform.SetParent(projectile,true);effect.transform.localPosition=Vector3.zero;
        effect.transform.localRotation=Quaternion.identity;effect.transform.localScale=Vector3.one;
        for(int i=0;i<effect.pages.Count;i++)effect.pages[i].position=effect.releasePositions[i];
        effect.flying=true;effect.waiting=false;effect.flightTime=0;effect.diameter=Mathf.Max(.3f,size);
        effect.direction=forward.sqrMagnitude>.001f?forward.normalized:Vector3.forward;
        return effect;
    }

    // Public for the editor's isolated visual review as well as the real charge.
    public static FightScorePages Create(Transform parent)
    {
        if(sheetMaterial==null)sheetMaterial=Resources.Load<Material>("VFX/ScorePages");
        if(sheetMaterial==null)return null;
        if(sheetMesh==null)sheetMesh=MakeSheetMesh();
        var go=new GameObject("Burning score sheets");go.transform.SetParent(parent,false);
        var effect=go.AddComponent<FightScorePages>();
        for(int i=0;i<12;i++)
        {
            var page=new GameObject("Score sheet "+(i+1),typeof(MeshFilter),typeof(MeshRenderer));
            page.transform.SetParent(go.transform,false);page.GetComponent<MeshFilter>().sharedMesh=sheetMesh;
            var renderer=page.GetComponent<MeshRenderer>();renderer.sharedMaterial=sheetMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            effect.pages.Add(page.transform);effect.renderers.Add(renderer);
        }
        return effect;
    }

    public void PoseCharge(Vector3 body,Vector3 hands,float progress,float time)
    {
        if(flying)return;
        float gather=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.30f,.96f,progress));
        for(int i=0;i<pages.Count;i++)
        {
            float seed=i*2.399963f,angle=seed+time*(3.4f+i*.08f);
            Vector3 orbit=body+new Vector3(Mathf.Cos(angle)*(.65f+i%3*.12f),.25f+Mathf.Sin(seed*1.7f)*.60f,Mathf.Sin(angle)*.65f);
            Vector3 cluster=hands+new Vector3(Mathf.Cos(angle)*.15f,Mathf.Sin(seed+time*5)*.12f,Mathf.Sin(angle)*.15f);
            var page=pages[i];page.position=Vector3.Lerp(orbit,cluster,gather);
            page.rotation=Quaternion.Euler(18+Mathf.Sin(angle)*35,angle*Mathf.Rad2Deg+25,Mathf.Sin(seed+time*3)*35);
            page.localScale=Vector3.one*Mathf.Lerp(.25f+(i%3)*.025f,.16f,gather);
            SetLook(i,Mathf.Lerp(.05f,.42f,gather),Mathf.Clamp01(progress*8-i*.025f),time);
        }
    }

    public void EndCharge(bool releasing)
    {
        // SpawnProjectile and the phase-change listeners can run in either order.
        if(flying)return;
        if(releasing){waiting=true;deadline=Time.time+.15f;}
        else {ForgetCharge();Remove(gameObject);}
    }

    private void Update(){if(waiting && Time.time>=deadline)Remove(gameObject);}
    private void OnEnable()
    {
        onFlow=e=>{if(e.Current!=FightFlowState.Fighting){ForgetCharge();Remove(gameObject);}};
        EventBus.Subscribe(onFlow);
    }
    private void OnDisable()=>EventBus.Unsubscribe(onFlow);
    private void LateUpdate()
    {
        if(!flying)return;
        flightTime+=Time.deltaTime;PoseFlight(transform.position,direction,diameter,flightTime);
    }

    public void PoseFlight(Vector3 centre,Vector3 forward,float size,float time)
    {
        Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;
        if(side.sqrMagnitude<.001f)side=Vector3.right;
        for(int i=0;i<pages.Count;i++)
        {
            float seed=i*2.399963f,angle=seed+time*9;
            float radius=size*(.55f+(i%3)*.14f);
            Vector3 position=centre+side*(Mathf.Cos(angle)*radius)+Vector3.up*(Mathf.Sin(angle)*radius)
                -forward*(.08f+(i%4)*.16f+Mathf.Sin(seed+time*5)*.05f);
            if(releasePositions!=null)position=Vector3.Lerp(releasePositions[i],position,Mathf.Clamp01(time/.07f));
            pages[i].position=position;pages[i].rotation=Quaternion.LookRotation(forward)*Quaternion.Euler(angle*55,seed*35+time*130,seed*20+time*240);
            pages[i].localScale=Vector3.one*(.18f+(i%3)*.025f);
            SetLook(i,.42f+.43f*(1-Mathf.Exp(-time*2.8f)),1,time);
        }
    }

    private void SetLook(int i,float heat,float opacity,float time)
    {
        properties ??= new MaterialPropertyBlock();
        properties.Clear();properties.SetFloat("_Seed",i*.731f);properties.SetFloat("_Heat",heat);
        properties.SetFloat("_Opacity",opacity);properties.SetFloat("_AnimationTime",time);
        renderers[i].SetPropertyBlock(properties);
    }

    private void OnDestroy()
    {
        ForgetCharge();
    }

    private void ForgetCharge()
    {
        if(!ReferenceEquals(owner,null) && charging.TryGetValue(owner,out var current) && ReferenceEquals(current,this))charging.Remove(owner);
    }

    private static void Remove(Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}

    private static Mesh MakeSheetMesh()
    {
        const int cols=5,rows=8;var mesh=new Mesh{name="Curved score page",hideFlags=HideFlags.DontSave};
        var vertices=new Vector3[cols*rows];var uv=new Vector2[vertices.Length];var indices=new int[(cols-1)*(rows-1)*6];int n=0;
        for(int y=0;y<rows;y++)for(int x=0;x<cols;x++)
        {
            int i=y*cols+x;float u=(float)x/(cols-1),v=(float)y/(rows-1);
            vertices[i]=new Vector3(u-.5f,(v-.5f)*1.4f,Mathf.Sin(u*Mathf.PI)*.055f);uv[i]=new Vector2(u,v);
            if(x<cols-1&&y<rows-1){indices[n++]=i;indices[n++]=i+cols;indices[n++]=i+1;indices[n++]=i+1;indices[n++]=i+cols;indices[n++]=i+cols+1;}
        }
        mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateNormals();mesh.bounds=new Bounds(Vector3.zero,new Vector3(1.4f,1.7f,.5f));return mesh;
    }
}
